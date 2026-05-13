using System;
using System.Threading;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Top-level state machine for the multiplayer feature. One instance per
/// launcher run. Coordinates the three subsystems that the UI shouldn't
/// have to glue together itself:
///
///   * <see cref="LobbyApiClient"/> — REST to the Worker.
///   * <see cref="ZeroTierService"/> — local daemon detection / install.
///   * <see cref="LobbyWebSocket"/> — per-room realtime channel.
///
/// The UI binds to <see cref="StateChanged"/> and queries the public
/// properties; it never reaches into the underlying services directly.
/// That way the auth/refresh + lobby join + WS lifecycle stay in one
/// place and the UI's job stays "render the current state".
///
/// State persists across launcher runs via <see cref="LauncherConfig"/>:
/// the session token, expiry and cached user are written back through
/// <see cref="LauncherConfig.Save"/> whenever they change.
/// </summary>
public sealed class MultiplayerSession : IAsyncDisposable
{
    public enum SessionStatus
    {
        /// <summary>No saved session, or saved one is expired.</summary>
        SignedOut,
        /// <summary>Device flow in progress (waiting for the user to approve in browser).</summary>
        SigningIn,
        /// <summary>We have a valid token and the cached user.</summary>
        SignedIn,
        /// <summary>The last call returned 401 — caller should re-sign-in.</summary>
        TokenRejected,
    }

    public enum LobbyStatus
    {
        /// <summary>Not in a lobby.</summary>
        Idle,
        /// <summary>Joining a lobby (REST in flight + ZT auth wait).</summary>
        Joining,
        /// <summary>In a lobby; WS open, ZT membership OK.</summary>
        InLobby,
        /// <summary>In a lobby that has transitioned to in_game.</summary>
        InGame,
        /// <summary>Leaving — REST POST /leave in flight.</summary>
        Leaving,
    }

    private readonly LauncherConfig _config;
    public LobbyApiClient Api { get; }

    /// <summary>Raised whenever any public property changes.</summary>
    public event EventHandler? StateChanged;

    public SessionStatus Status { get; private set; } = SessionStatus.SignedOut;
    public LobbyStatus Lobby { get; private set; } = LobbyStatus.Idle;
    public LobbyUserSummary? CurrentUser { get; private set; }
    public string? CurrentLobbyId { get; private set; }
    public string? CurrentLobbyTitle { get; private set; }
    public string? CurrentZtNetworkId { get; private set; }
    public LobbyWebSocket? RoomSocket { get; private set; }
    public string? LastError { get; private set; }

    public MultiplayerSession(LauncherConfig config)
    {
        _config = config;
        Api = new LobbyApiClient(_config.Multiplayer.LobbyBaseUrl, _config.Multiplayer.SessionToken.NullIfEmpty());

        // Resume a saved session if the token still has time on it.
        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (!string.IsNullOrEmpty(_config.Multiplayer.SessionToken)
            && _config.Multiplayer.SessionExpiresAt > nowUnix + 60)
        {
            CurrentUser = _config.Multiplayer.CachedUser;
            Status = SessionStatus.SignedIn;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (RoomSocket != null)
        {
            await RoomSocket.DisposeAsync();
            RoomSocket = null;
        }
        Api.Dispose();
    }

    // -------- Auth ------------------------------------------------------

    public Task<DeviceFlowStart> StartSignInAsync(CancellationToken ct = default)
    {
        Status = SessionStatus.SigningIn;
        LastError = null;
        Raise();
        return Api.StartDeviceFlowAsync(ct);
    }

    public async Task<DeviceFlowComplete> CompleteSignInAsync(
        DeviceFlowStart start,
        CancellationToken ct = default)
    {
        try
        {
            var done = await Api.PollDeviceFlowAsync(
                start.PollHandle,
                start.IntervalSeconds,
                TimeSpan.FromSeconds(Math.Max(60, start.ExpiresInSeconds)),
                ct);

            _config.Multiplayer.SessionToken = done.Token;
            _config.Multiplayer.SessionExpiresAt = done.ExpiresAt;
            _config.Multiplayer.CachedUser = done.User;
            _config.Save();

            CurrentUser = done.User;
            Status = SessionStatus.SignedIn;
            MultiplayerTelemetry.Bump(MultiplayerTelemetry.SignInSucceeded);
            Raise();
            return done;
        }
        catch
        {
            Status = SessionStatus.SignedOut;
            Raise();
            throw;
        }
    }

    public void SignOut()
    {
        _config.Multiplayer.SessionToken = "";
        _config.Multiplayer.SessionExpiresAt = 0;
        _config.Multiplayer.CachedUser = null;
        _config.Save();

        Api.SetSessionToken(null);
        CurrentUser = null;
        Status = SessionStatus.SignedOut;
        Raise();
    }

    // -------- Lobby flow ------------------------------------------------

    /// <summary>
    /// Full join flow: validates ZT, joins the network locally, calls
    /// REST /join on the Worker, then opens the room WebSocket. Returns
    /// once the WS hello has been sent — incoming room_state, member_*,
    /// chat events arrive via the <see cref="RoomSocket"/> event after.
    /// </summary>
    public async Task<JoinLobbyResponse> JoinLobbyAsync(
        string lobbyId,
        string modCombinedHash,
        string? password,
        CancellationToken ct = default)
    {
        if (Status != SessionStatus.SignedIn) throw new InvalidOperationException("Sign in first.");
        if (Lobby != LobbyStatus.Idle) throw new InvalidOperationException("Leave the current lobby first.");

        Lobby = LobbyStatus.Joining;
        LastError = null;
        Raise();

        try
        {
            // 1. Make sure ZeroTier is up and we know our node id.
            var ztClient = ZeroTierService.CreateClient()
                ?? throw new InvalidOperationException("ZeroTier authtoken unavailable.");
            var status = await ztClient.GetStatusAsync(ct)
                ?? throw new InvalidOperationException("ZeroTier daemon not responding.");
            if (string.IsNullOrEmpty(status.Address))
                throw new InvalidOperationException("ZeroTier returned no node id.");

            // 2. Ask the Worker to authorise us on the lobby's ZT network.
            var join = await Api.JoinLobbyAsync(lobbyId, new JoinLobbyRequest
            {
                ZtNodeId = status.Address,
                ModCombinedHash = modCombinedHash,
                Password = password,
            }, ct);

            // 3. Join the ZT network locally and wait for OK.
            await ztClient.JoinAsync(join.ZtNetworkId, ct);
            await ztClient.WaitForOkAsync(join.ZtNetworkId, TimeSpan.FromSeconds(20), ct);

            // 4. Open the room WS.
            await OpenRoomSocketAsync(lobbyId, join.JoinToken, LobbyWebSocket.HelloMode.JoinToken);
            CurrentLobbyId = lobbyId;
            CurrentZtNetworkId = join.ZtNetworkId;
            Lobby = LobbyStatus.InLobby;
            MultiplayerTelemetry.Bump(MultiplayerTelemetry.LobbyJoined);
            Raise();
            return join;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Lobby = LobbyStatus.Idle;
            Raise();
            throw;
        }
    }

    /// <summary>
    /// Variant for the host: REST /lobbies has already created the room
    /// + ZT network; we just need to join the network locally and open
    /// the room WS with our JWT (no join_token for the host path).
    /// </summary>
    public async Task EnterHostedLobbyAsync(
        CreateLobbyResponse created,
        CancellationToken ct = default)
    {
        if (Status != SessionStatus.SignedIn) throw new InvalidOperationException("Sign in first.");

        Lobby = LobbyStatus.Joining;
        Raise();

        try
        {
            var ztClient = ZeroTierService.CreateClient()
                ?? throw new InvalidOperationException("ZeroTier authtoken unavailable.");
            await ztClient.JoinAsync(created.ZtNetworkId, ct);
            await ztClient.WaitForOkAsync(created.ZtNetworkId, TimeSpan.FromSeconds(20), ct);

            var token = _config.Multiplayer.SessionToken;
            if (string.IsNullOrEmpty(token))
                throw new InvalidOperationException("Session token missing — sign in again.");

            await OpenRoomSocketAsync(created.Id, token, LobbyWebSocket.HelloMode.SessionToken);
            CurrentLobbyId = created.Id;
            CurrentZtNetworkId = created.ZtNetworkId;
            Lobby = LobbyStatus.InLobby;
            MultiplayerTelemetry.Bump(MultiplayerTelemetry.LobbyCreated);
            Raise();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Lobby = LobbyStatus.Idle;
            Raise();
            throw;
        }
    }

    public async Task LeaveCurrentLobbyAsync(CancellationToken ct = default)
    {
        if (Lobby == LobbyStatus.Idle || CurrentLobbyId == null) return;
        Lobby = LobbyStatus.Leaving;
        Raise();

        try
        {
            if (RoomSocket != null)
            {
                await RoomSocket.DisposeAsync();
                RoomSocket = null;
            }

            if (CurrentZtNetworkId != null)
            {
                var ztClient = ZeroTierService.CreateClient();
                if (ztClient != null)
                {
                    await ztClient.LeaveAsync(CurrentZtNetworkId, ct);
                    ztClient.Dispose();
                }
            }

            try { await Api.LeaveLobbyAsync(CurrentLobbyId, ct); }
            catch (LobbyApiException ex)
            {
                DiagnosticLog.Write($"MultiplayerSession.Leave: REST returned {ex.Code}: {ex.Message}");
            }
        }
        finally
        {
            CurrentLobbyId = null;
            CurrentLobbyTitle = null;
            CurrentZtNetworkId = null;
            Lobby = LobbyStatus.Idle;
            Raise();
        }
    }

    private async Task OpenRoomSocketAsync(string lobbyId, string credential, LobbyWebSocket.HelloMode mode)
    {
        if (RoomSocket != null)
        {
            await RoomSocket.DisposeAsync();
            RoomSocket = null;
        }

        var wsUri = LobbyWebSocket.BuildWsUri(Api.BaseUri, $"lobbies/{lobbyId}/ws");
        var sock = new LobbyWebSocket(wsUri, mode, credential);
        sock.FrameReceived += OnFrame;
        sock.Disconnected += (_, reason) => DiagnosticLog.Write($"Room WS disconnected: {reason}");
        RoomSocket = sock;
        sock.Start();
    }

    private void OnFrame(object? sender, LobbyWebSocket.FrameReceivedEventArgs e)
    {
        // Reflect a few high-level transitions in our public state so
        // the UI doesn't need to introspect raw frames for navigation
        // decisions. Chat lines, member ready toggles etc. are left to
        // the UI to interpret directly via the FrameReceived event.
        if (e.Type == "game_started")
        {
            Lobby = LobbyStatus.InGame;
            Raise();
        }
    }

    private void Raise() => StateChanged?.Invoke(this, EventArgs.Empty);
}

file static class StringExt
{
    public static string? NullIfEmpty(this string s) =>
        string.IsNullOrEmpty(s) ? null : s;
}
