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

    // TODO(elo): not wired yet. The backend's POST /matches endpoint
    // is live and the DTOs (ReportMatchRequest / ReportMatchResponse /
    // MatchParticipantReport / RatingChange) are kept here for the day
    // we hook it up. The expected call site is
    // MultiplayerTab.OnGameExitedAsync — when AoE3 closes, fill the
    // request from the room state we still have in memory (lobby_id,
    // mod_id, mod_combined_hash, started_at, ended_at, durations) and
    // post it so the backend can update ELO. Per-player win/loss has
    // to come from replay parsing because AoE3 doesn't expose results
    // on the command line, so v1 reports may just carry participants
    // with result=0.5 (no rating change) until the replay parser
    // lands. Endpoint behaviour is identical to the old Worker —
    // safe to call from the launcher whenever we're ready.
    public Task<ReportMatchResponse> ReportMatchAsync(ReportMatchRequest req, CancellationToken ct = default)
        => PostAsync<ReportMatchResponse>("matches", req, requireAuth: true, ct);

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
