using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Exception carrying the backend's <c>{code, message, details}</c> envelope.
/// The UI layer branches on <see cref="Code"/> to surface the right
/// message (rate_limited → "slow down", mod_mismatch → diff dialog, …).
/// </summary>
public class LobbyApiException : Exception
{
    public int Status { get; }
    public string Code { get; }
    public Dictionary<string, object?>? Details { get; }

    public LobbyApiException(int status, string code, string message, Dictionary<string, object?>? details)
        : base(message)
    {
        Status = status;
        Code = code;
        Details = details;
    }
}

/// <summary>
/// HTTP client for the multiplayer lobby backend (self-hosted Node +
/// Fastify; previously a Cloudflare Worker). One instance per launcher
/// lifetime; thread-safe by virtue of <see cref="HttpClient"/>'s own
/// guarantees.
///
/// Session lifecycle:
///   * The launcher creates the client at startup with whatever token it
///     has on disk (may be null/expired).
///   * On a 401 the client raises <see cref="LobbyApiException"/> with
///     code "unauthorized" or "invalid_token"; the UI layer re-runs the
///     Discord sign-in flow and calls <see cref="SetSessionToken"/> with
///     the fresh token.
/// </summary>
public class LobbyApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        // SQLITE HAS NO BOOLEANS, and that is the whole reason this converter exists. Any column
        // this API hands over raw arrives as a NUMBER — 0 or 1 — and System.Text.Json will not
        // bind a number to a bool: it throws, and the throw aborts the WHOLE response, so one
        // field takes an entire page down. Launcher 1.0.13l shipped exactly that: the history
        // endpoint started selecting `rated`, the DTO declared it `bool?`, and the Profile's
        // History section sat on "Loading..." for ever.
        //
        // The backend coerces that field now, which is the fix people already on 1.0.13l get.
        // This is the OTHER half: it belongs on the shared options rather than on one property
        // so that the next column somebody exposes cannot repeat it.
        Converters = { new TolerantBoolConverter(), new TolerantNullableBoolConverter() },
    };

    private string? _sessionToken;

    public LobbyApiClient(string baseUrl, string? sessionToken = null)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new ArgumentException("Lobby base URL is required.", nameof(baseUrl));

        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(30),
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Aoe3ModLauncher/1.0 (Multiplayer)");
        // The REAL build, so the server can refuse a launcher too old for the protocol — the
        // User-Agent above is a fixed literal and has never carried the version. Same value the
        // self-updater compares itself with, letter suffix and all (v1.0.12e), so the two sides
        // order releases identically.
        _http.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Launcher-Version", LauncherUpdateService.CurrentInformationalTag);
        _sessionToken = sessionToken;
    }

    public void Dispose() => _http.Dispose();

    /// <summary>Update the in-memory session token after a successful login or refresh.</summary>
    public void SetSessionToken(string? token) => _sessionToken = token;

    public string? SessionToken => _sessionToken;

    /// <summary>The base URL the client was configured with. Needed to
    /// build the WS URI in <see cref="LobbyWebSocket"/>.</summary>
    public Uri BaseUri => _http.BaseAddress!;

    // ---------------------------------------------------------------
    // Auth — Discord (state-based flow, shaped like a device flow so the
    // launcher code path is unchanged from the old GitHub implementation).
    // ---------------------------------------------------------------

    public Task<DeviceFlowStart> StartDeviceFlowAsync(CancellationToken ct = default)
        => PostAsync<DeviceFlowStart>("auth/login/device", body: null, requireAuth: false, ct);

    /// <summary>
    /// Poll the backend until the sign-in flow completes or times out.
    /// Returns the completed payload (includes a JWT). Throws
    /// <see cref="LobbyApiException"/> for terminal errors (expired
    /// state, access denied).
    /// </summary>
    public async Task<DeviceFlowComplete> PollDeviceFlowAsync(
        string pollHandle,
        int intervalSeconds,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        // Floor at 10 s (was 5 s). The backend's per-IP poll rate limit
        // tolerates this comfortably and it halves the number of HTTP
        // requests generated during the sign-in window (typical user
        // takes 30-60 s to approve; that's 6-12 polls instead of 12-24).
        // When the server explicitly returns `slow_down`, the loop
        // below still backs off another 5 s per occurrence.
        var currentInterval = Math.Max(10, intervalSeconds);

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(TimeSpan.FromSeconds(currentInterval), ct);

            using var req = new HttpRequestMessage(HttpMethod.Post, "auth/login/poll")
            {
                Content = JsonContent.Create(new { poll_handle = pollHandle }, options: _jsonOptions),
            };
            using var resp = await _http.SendAsync(req, ct);

            if (resp.StatusCode == HttpStatusCode.Accepted)
            {
                // status: authorization_pending or slow_down — back off.
                if (await TryReadStatusAsync(resp, ct) == "slow_down")
                    currentInterval = Math.Min(60, currentInterval + 5);
                continue;
            }

            if (!resp.IsSuccessStatusCode)
            {
                throw await BuildExceptionAsync(resp, ct);
            }

            var stream = await resp.Content.ReadAsStreamAsync(ct);
            var complete = await JsonSerializer.DeserializeAsync<DeviceFlowComplete>(stream, _jsonOptions, ct)
                ?? throw new LobbyApiException(500, "internal", "Empty poll response.", null);
            SetSessionToken(complete.Token);
            return complete;
        }

        throw new LobbyApiException(408, "device_flow_timeout", "Discord authorisation timed out.", null);
    }

    public Task<LobbyUserSummary> GetMeAsync(CancellationToken ct = default)
        => GetAsync<LobbyUserSummary>("me", requireAuth: true, ct);

    // ---------------------------------------------------------------
    // Lobbies
    // ---------------------------------------------------------------

    public Task<LobbyListResponse> ListLobbiesAsync(CancellationToken ct = default)
        => GetAsync<LobbyListResponse>("lobbies", requireAuth: false, ct);

    public Task<CreateLobbyResponse> CreateLobbyAsync(CreateLobbyRequest req, CancellationToken ct = default)
        => PostAsync<CreateLobbyResponse>("lobbies", req, requireAuth: true, ct);

    public Task<JoinLobbyResponse> JoinLobbyAsync(string lobbyId, JoinLobbyRequest req, CancellationToken ct = default)
        => PostAsync<JoinLobbyResponse>($"lobbies/{lobbyId}/join", req, requireAuth: true, ct);

    public async Task LeaveLobbyAsync(string lobbyId, CancellationToken ct = default)
    {
        await PostAsync<object>($"lobbies/{lobbyId}/leave", body: null, requireAuth: true, ct);
    }

    /// <summary>Fetch one lobby's full roster (members + avatars + ready) WITHOUT
    /// joining its WS. Public endpoint — powers the "see who's in a room" peek.</summary>
    public Task<LobbyDetail> GetLobbyByIdAsync(string lobbyId, CancellationToken ct = default)
        => GetAsync<LobbyDetail>($"lobbies/{Uri.EscapeDataString(lobbyId)}", requireAuth: false, ct);

    // ---------------------------------------------------------------
    // Status
    // ---------------------------------------------------------------

    public Task<QuotaSnapshot> GetQuotaAsync(CancellationToken ct = default)
        => GetAsync<QuotaSnapshot>("quota", requireAuth: false, ct);

    // ---------------------------------------------------------------
    // Matches + replays
    // ---------------------------------------------------------------

    public Task<MatchHistoryResponse> GetHistoryAsync(string userId, CancellationToken ct = default)
        => GetAsync<MatchHistoryResponse>($"matches/history/{userId}", requireAuth: false, ct);

    /// <summary>
    /// A player's rating and decided-game tally. Unauthenticated like the history call —
    /// standings are public, and the endpoint takes any user id.
    /// </summary>
    public Task<EloSnapshot> GetEloAsync(string userId, CancellationToken ct = default)
        => GetAsync<EloSnapshot>($"matches/elo/{userId}", requireAuth: false, ct);

    /// <summary>
    /// BOTH ladders and the activity histogram, for the community strip and the Ranking
    /// subtab. Unauthenticated like the other standings calls, and re-asked at most once a
    /// minute — matching the server's own cache, so a refresh inside that window costs it
    /// nothing. A backend without the route answers 404, which the caller treats as "hide
    /// the cards", not as an error.
    ///
    /// <para>The default limit is the server's maximum: one payload feeds a three-row
    /// summary and a full table, and asking twice for two sizes would double a budget that
    /// is per IP and shared behind a Radmin NAT.</para>
    /// </summary>
    public Task<CommunityStats> GetCommunityStatsAsync(
        int limit = 50, string? modId = null, string? mode = null,
        CancellationToken ct = default)
        => GetAsync<CommunityStats>(
            ScopedPath($"stats/community?limit={limit}", modId, mode), requireAuth: false, ct);

    /// <summary>
    /// A statistics path with the mod scope attached, or without it.
    ///
    /// <para>Omitted rather than sent empty when no mod is chosen: an older backend ignores an
    /// unknown parameter, but a newer one would read <c>mod=</c> as a mod whose id is the empty
    /// string and answer with nothing. One helper for all four reads, so they cannot disagree
    /// about what "this mod" means — a page whose halves are scoped differently is worse than
    /// one that is not scoped at all.</para>
    /// </summary>
    /// <summary>
    /// Which mods the server has matches for.
    ///
    /// <para>The statistics picker unions this with the mods installed locally, so a mod added to
    /// the catalogue shows up as soon as anybody plays it: no install here and no code. A backend
    /// without the route answers 404, which the caller reads as "then just the installed
    /// ones".</para>
    /// </summary>
    public Task<StatsModsResponse> GetStatsModsAsync(CancellationToken ct = default)
        => GetAsync<StatsModsResponse>("stats/mods", requireAuth: false, ct);

    private static string ScopedPath(string path, string? modId, string? mode = null)
    {
        string result = path;
        if (!string.IsNullOrWhiteSpace(modId))
        {
            result += $"{(result.Contains('?') ? '&' : '?')}mod={Uri.EscapeDataString(modId!)}";
        }

        // Only ever sent when it is NOT the default. An older backend ignores a parameter it does
        // not know and a newer one defaults to the 1v1 figures, so the two agree on silence.
        if (string.Equals(mode, "team", StringComparison.Ordinal))
        {
            result += $"{(result.Contains('?') ? '&' : '?')}mode=team";
        }

        return result;
    }

    /// <summary>
    /// How each civilization is doing across the community, per mod and version.
    ///
    /// <para>A route of its own rather than a field on the community payload, and with its own
    /// rate-limit scope server-side: that payload is fetched once a minute for as long as the
    /// Rooms tab is focused, and this table is opened by almost nobody. Folding it in would make
    /// everybody pay its bytes and let it compete for the same per-IP budget — which is shared
    /// behind a Radmin NAT.</para>
    /// </summary>
    public Task<CivStatsResponse> GetCivStatsAsync(
        string? modId = null, string? mode = null, CancellationToken ct = default)
        => GetAsync<CivStatsResponse>(
            ScopedPath("stats/civs", modId, mode), requireAuth: false, ct);

    /// <summary>
    /// Civilization against civilization. A backend without the route answers 404, which the
    /// caller has to treat as "not deployed yet" and hide, exactly like an absent field.
    /// </summary>
    public Task<MatchupsResponse> GetMatchupsAsync(
        string? modId = null, string? mode = null, CancellationToken ct = default)
        => GetAsync<MatchupsResponse>(
            ScopedPath("stats/matchups", modId, mode), requireAuth: false, ct);

    /// <summary>Which cards the community brings. 404 on a backend without the route.</summary>
    public Task<DeckStatsResponse> GetDeckStatsAsync(
        string? modId = null, CancellationToken ct = default)
        => GetAsync<DeckStatsResponse>(ScopedPath("stats/decks", modId), requireAuth: false, ct);

    /// <summary>
    /// Contribute this machine's decks. Requires auth — the server keys them to the account,
    /// which is what lets a later upload REPLACE them instead of stacking.
    /// </summary>
    public Task<object> UploadDecksAsync(DeckUploadRequest req, CancellationToken ct = default)
        => PostAsync<object>("stats/decks", req, requireAuth: true, ct);

    /// <summary>
    /// Reports a finished match, host-only. Called from
    /// <c>MultiplayerTab.OnGameExitedAsync</c> once the recording has been read, so the
    /// participants carry a real per-player result for a clean 1v1 and 0.5 — meaning "not
    /// known", not "drawn" — for everything else. The backend feeds those to Glicko.
    /// </summary>
    public Task<ReportMatchResponse> ReportMatchAsync(ReportMatchRequest req, CancellationToken ct = default)
        => PostAsync<ReportMatchResponse>("matches", req, requireAuth: true, ct);

    /// <summary>
    /// Our own reading of a match the HOST reports, sent by everyone who is not the host.
    ///
    /// <para>Evidence, not a vote: the server records it next to the host's claim and
    /// nothing about whether the match scores depends on it. See
    /// <see cref="ConfirmMatchRequest"/>.</para>
    /// </summary>
    public Task<ConfirmMatchResponse> ConfirmMatchAsync(ConfirmMatchRequest req, CancellationToken ct = default)
        => PostAsync<ConfirmMatchResponse>("matches/confirm", req, requireAuth: true, ct);

    public Task<ReplayUploadHandle> RequestReplayUploadAsync(string matchId, CancellationToken ct = default)
        => PostAsync<ReplayUploadHandle>(
            "replays/upload-url",
            new { match_id = matchId },
            requireAuth: true,
            ct);

    /// <summary>
    /// Stream a replay file body to the backend. The endpoint returned by
    /// <see cref="RequestReplayUploadAsync"/> is a single-use handle: the
    /// backend validates size + auth and writes the bytes to the replays
    /// directory.
    /// </summary>
    public async Task UploadReplayAsync(
        string uploadUrlPath,
        System.IO.Stream body,
        long contentLength,
        CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, uploadUrlPath.TrimStart('/'));
        ApplyAuth(req, requireAuth: true);
        var content = new StreamContent(body);
        content.Headers.ContentLength = contentLength;
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        req.Content = content;

        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            throw await BuildExceptionAsync(resp, ct);
    }

    // ---------------------------------------------------------------
    // Internals
    // ---------------------------------------------------------------

    // ---------------------------------------------------------------
    // Tournaments and teams
    // ---------------------------------------------------------------
    //
    // The two reads are PUBLIC, like history and the community stats: a bracket is
    // something you can look at without signing in. Everything that changes something is
    // authenticated.
    //
    // A 404 here means the backend predates tournaments. Callers must render that as
    // "not available on this server" rather than as a failure — see the header of
    // TournamentSummary for why every field is nullable.

    public Task<TournamentListResponse> ListTournamentsAsync(CancellationToken ct = default)
        => GetAsync<TournamentListResponse>("tournaments", requireAuth: false, ct);

    public Task<TournamentDetail> GetTournamentAsync(string id, CancellationToken ct = default)
        => GetAsync<TournamentDetail>(
            $"tournaments/{Uri.EscapeDataString(id)}", requireAuth: false, ct);

    public Task<TournamentSummary> CreateTournamentAsync(object req, CancellationToken ct = default)
        => PostAsync<TournamentSummary>("tournaments", req, requireAuth: true, ct);

    public Task<TournamentEntryResponse> EnterTournamentAsync(
        string id, object? req = null, CancellationToken ct = default)
        => PostAsync<TournamentEntryResponse>(
            $"tournaments/{Uri.EscapeDataString(id)}/entrants", req, requireAuth: true, ct);

    public Task<object> WithdrawFromTournamentAsync(
        string id, string entrantId, CancellationToken ct = default)
        => PostAsync<object>(
            $"tournaments/{Uri.EscapeDataString(id)}/entrants/{Uri.EscapeDataString(entrantId)}/withdraw",
            body: null, requireAuth: true, ct);

    /// <summary>Open — or re-enter — the room for one bracket match.
    ///
    /// <para>Answers 200 with <c>Existing = true</c> when somebody on the match already
    /// opened it, which is how both sides pressing the same button end up in the same
    /// room. The title is composed by the SERVER; display it, never invent one.</para></summary>
    public Task<TournamentLobbyResponse> OpenTournamentMatchLobbyAsync(
        string id, string matchId, TournamentLobbyRequest req, CancellationToken ct = default)
        => PostAsync<TournamentLobbyResponse>(
            $"tournaments/{Uri.EscapeDataString(id)}/matches/{Uri.EscapeDataString(matchId)}/lobby",
            req, requireAuth: true, ct);

    // Owner-only. The server re-checks ownership on every one of these, so the launcher's
    // hiding of the buttons is a courtesy and never the enforcement.

    public Task<object> OpenTournamentRegistrationAsync(string id, CancellationToken ct = default)
        => PostAsync<object>($"tournaments/{Uri.EscapeDataString(id)}/open", null, true, ct);

    public Task<object> CloseTournamentRegistrationAsync(string id, CancellationToken ct = default)
        => PostAsync<object>($"tournaments/{Uri.EscapeDataString(id)}/close", null, true, ct);

    public Task<object> SeedTournamentAsync(string id, object? req = null, CancellationToken ct = default)
        => PostAsync<object>($"tournaments/{Uri.EscapeDataString(id)}/seed", req, true, ct);

    public Task<object> StartTournamentAsync(string id, CancellationToken ct = default)
        => PostAsync<object>($"tournaments/{Uri.EscapeDataString(id)}/start", null, true, ct);

    public Task<object> CancelTournamentAsync(string id, CancellationToken ct = default)
        => PostAsync<object>($"tournaments/{Uri.EscapeDataString(id)}/cancel", null, true, ct);

    public Task<object> AcceptEntrantAsync(string id, string entrantId, CancellationToken ct = default)
        => PostAsync<object>(
            $"tournaments/{Uri.EscapeDataString(id)}/entrants/{Uri.EscapeDataString(entrantId)}/accept",
            null, true, ct);

    public Task<object> RejectEntrantAsync(string id, string entrantId, CancellationToken ct = default)
        => PostAsync<object>(
            $"tournaments/{Uri.EscapeDataString(id)}/entrants/{Uri.EscapeDataString(entrantId)}/reject",
            null, true, ct);

    public Task<object> DisqualifyEntrantAsync(string id, string entrantId, CancellationToken ct = default)
        => PostAsync<object>(
            $"tournaments/{Uri.EscapeDataString(id)}/entrants/{Uri.EscapeDataString(entrantId)}/disqualify",
            null, true, ct);

    public Task<object> AwardWalkoverAsync(
        string id, string matchId, string winnerEntrantId, CancellationToken ct = default)
        => PostAsync<object>(
            $"tournaments/{Uri.EscapeDataString(id)}/matches/{Uri.EscapeDataString(matchId)}/walkover",
            new { winner_entrant_id = winnerEntrantId }, true, ct);

    // Teams.

    public Task<MyTeamsResponse> GetMyTeamsAsync(CancellationToken ct = default)
        => GetAsync<MyTeamsResponse>("teams/mine", requireAuth: true, ct);

    public Task<TeamSummary> GetTeamAsync(string id, CancellationToken ct = default)
        => GetAsync<TeamSummary>($"teams/{Uri.EscapeDataString(id)}", requireAuth: false, ct);

    public Task<TeamSummary> CreateTeamAsync(object req, CancellationToken ct = default)
        => PostAsync<TeamSummary>("teams", req, requireAuth: true, ct);

    public Task<object> InviteToTeamAsync(string teamId, string userId, CancellationToken ct = default)
        => PostAsync<object>(
            $"teams/{Uri.EscapeDataString(teamId)}/invites", new { user_id = userId }, true, ct);

    public Task<object> AcceptTeamInviteAsync(string inviteId, CancellationToken ct = default)
        => PostAsync<object>($"teams/invites/{Uri.EscapeDataString(inviteId)}/accept", null, true, ct);

    public Task<object> DeclineTeamInviteAsync(string inviteId, CancellationToken ct = default)
        => PostAsync<object>($"teams/invites/{Uri.EscapeDataString(inviteId)}/decline", null, true, ct);

    public Task<object> RemoveTeamMemberAsync(string teamId, string userId, CancellationToken ct = default)
        => PostAsync<object>(
            $"teams/{Uri.EscapeDataString(teamId)}/members/{Uri.EscapeDataString(userId)}/remove",
            null, true, ct);

    public Task<object> DisbandTeamAsync(string teamId, CancellationToken ct = default)
        => PostAsync<object>($"teams/{Uri.EscapeDataString(teamId)}/disband", null, true, ct);

    private void ApplyAuth(HttpRequestMessage req, bool requireAuth)
    {
        if (!string.IsNullOrEmpty(_sessionToken))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _sessionToken);
        }
        else if (requireAuth)
        {
            // Throw early instead of bouncing off the backend — saves a
            // request and gives the UI a clearer signal.
            throw new LobbyApiException(401, "unauthorized", "Sign in with Discord first.", null);
        }
    }

    private async Task<T> GetAsync<T>(string path, bool requireAuth, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        ApplyAuth(req, requireAuth);
        using var resp = await _http.SendAsync(req, ct);
        return await ParseResponseAsync<T>(resp, ct);
    }

    private async Task<T> PostAsync<T>(string path, object? body, bool requireAuth, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, path);
        ApplyAuth(req, requireAuth);
        if (body != null)
            req.Content = JsonContent.Create(body, options: _jsonOptions);

        using var resp = await _http.SendAsync(req, ct);
        return await ParseResponseAsync<T>(resp, ct);
    }

    private async Task<T> ParseResponseAsync<T>(HttpResponseMessage resp, CancellationToken ct)
    {
        if (!resp.IsSuccessStatusCode)
        {
            throw await BuildExceptionAsync(resp, ct);
        }

        // 204 / empty body — return default. Object type is fine for the
        // POST /leave style endpoint where the caller ignores the return.
        if (resp.Content.Headers.ContentLength == 0)
            return default!;

        var stream = await resp.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, ct)
            ?? throw new LobbyApiException(500, "internal", "Empty success response.", null);
    }

    private async Task<LobbyApiException> BuildExceptionAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        int status = (int)resp.StatusCode;
        ApiErrorBody? err = null;
        try
        {
            var stream = await resp.Content.ReadAsStreamAsync(ct);
            err = await JsonSerializer.DeserializeAsync<ApiErrorBody>(stream, _jsonOptions, ct);
        }
        catch
        {
            // Body wasn't JSON — synthesise a generic error so the UI
            // still gets something predictable.
        }

        var code = err?.Code ?? "http_error";
        var message = err?.Message ?? $"HTTP {status}";

        // Bump well-known counters so we get a feel for how often each
        // failure mode fires across a session without needing per-call
        // logging in every caller.
        if (code == "rate_limited") MultiplayerTelemetry.Bump(MultiplayerTelemetry.RateLimited);
        else if (code == "mod_mismatch") MultiplayerTelemetry.Bump(MultiplayerTelemetry.ModMismatch);
        else if (code == "quota_degraded") MultiplayerTelemetry.Bump(MultiplayerTelemetry.QuotaDegraded);
        else if (code == "quota_exhausted") MultiplayerTelemetry.Bump(MultiplayerTelemetry.QuotaExhausted);

        return new LobbyApiException(status, code, message, err?.Details);
    }

    private async Task<string?> TryReadStatusAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try
        {
            var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            return doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() : null;
        }
        catch
        {
            return null;
        }
    }
}
