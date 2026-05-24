using System;
using System.Threading;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Top-level state machine for the multiplayer feature. One instance per
/// launcher run. Coordinates the subsystems that the UI shouldn't have
/// to glue together itself:
///
///   * <see cref="LobbyApiClient"/> — REST to the backend (meta layer:
///     auth, lobbies, chat, ELO, match history).
///   * <see cref="LobbyWebSocket"/> — per-room realtime channel.
///
/// The actual game-traffic transport is OUT of scope for the launcher.
/// The community uses Radmin VPN to put every player on the same virtual
/// LAN, and AoE3's stock LAN multiplayer code finds peers over that
/// network — no hooks, no virtual NICs, no per-room signaling here.
///
/// The UI binds to <see cref="StateChanged"/> and queries the public
/// properties; it never reaches into the underlying services directly.
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
        /// <summary>Joining a lobby (REST in flight).</summary>
        Joining,
        /// <summary>In a lobby; WS open.</summary>
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
    public LobbyWebSocket? RoomSocket { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>
    /// True once the user has both signed in AND entered a lobby. The
    /// launcher's "Start Game" button is gated by this so we never
    /// spawn AoE3 from outside a lobby context (the match-report POST
    /// at the end needs the lobby id). The actual game-network
    /// connectivity is the user's responsibility (Radmin VPN) — the
    /// launcher just orchestrates the meta layer. (Pre-Radmin: this
    /// used to be called IsP2pBridgeReady when an in-process n2n
    /// bridge had to come up before launch; renamed because the gate
    /// is now purely "are we in a lobby?".)
    /// </summary>
    public bool IsInLobby => Lobby != LobbyStatus.Idle;

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
    /// Full join flow: REST /join with the backend, then open the room
    /// WebSocket. Returns once the WS hello has been sent — incoming
    /// room_state, member_*, chat events arrive via the
    /// <see cref="RoomSocket"/> event after.
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
            var join = await Api.JoinLobbyAsync(lobbyId, new JoinLobbyRequest
            {
                ModCombinedHash = modCombinedHash,
                Password = password,
            }, ct);

            await OpenRoomSocketAsync(lobbyId, join.JoinToken, LobbyWebSocket.HelloMode.JoinToken);
            CurrentLobbyId = lobbyId;
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
    /// row in the backend; we just need to open the room WS with our
    /// JWT (no join_token for the host path).
    /// </summary>
    public async Task EnterHostedLobbyAsync(
        CreateLobbyResponse created,
        CancellationToken ct = default)
    {
        if (Status != SessionStatus.SignedIn) throw new InvalidOperationException("Sign in first.");

        DiagnosticLog.Write($"EnterHostedLobbyAsync: starting for lobby {created.Id}");
        Lobby = LobbyStatus.Joining;
        Raise();

        try
        {
            var token = _config.Multiplayer.SessionToken;
            if (string.IsNullOrEmpty(token))
                throw new InvalidOperationException("Session token missing — sign in again.");

            DiagnosticLog.Write($"EnterHostedLobbyAsync: opening WS for {created.Id}");
            await OpenRoomSocketAsync(created.Id, token, LobbyWebSocket.HelloMode.SessionToken);
            CurrentLobbyId = created.Id;
            Lobby = LobbyStatus.InLobby;
            MultiplayerTelemetry.Bump(MultiplayerTelemetry.LobbyCreated);
            Raise();
            DiagnosticLog.Write($"EnterHostedLobbyAsync: InLobby state for {created.Id}");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"EnterHostedLobbyAsync FAILED for {created.Id}: {ex.GetType().Name}: {ex.Message}");
            LastError = ex.Message;
            Lobby = LobbyStatus.Idle;
            Raise();
            throw;
        }
    }

    public async Task LeaveCurrentLobbyAsync(CancellationToken ct = default)
    {
        if (Lobby == LobbyStatus.Idle || CurrentLobbyId == null) return;

        var lobbyId = CurrentLobbyId;
        var socket = RoomSocket;

        // Optimistic UI transition first — the user sees the room
        // collapse and the lobby list reappear within a single frame.
        CurrentLobbyId = null;
        CurrentLobbyTitle = null;
        RoomSocket = null;
        Lobby = LobbyStatus.Idle;
        Raise();

        // The REST /leave call is the only thing that matters for
        // server-side cleanup — it marks the lobby `closed` and
        // notifies the other members via the room WS. We await it so
        // MainWindow.OnClosing can guarantee the backend received the
        // message before the launcher process exits.
        try
        {
            await Api.LeaveLobbyAsync(lobbyId, ct);
        }
        catch (LobbyApiException ex)
        {
            DiagnosticLog.Write($"MultiplayerSession.Leave: REST returned {ex.Code}: {ex.Message}");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"MultiplayerSession.Leave: REST failed: {ex.Message}");
        }

        // Local WS close is fire-and-forget — runtime closes the
        // socket on process exit anyway.
        if (socket != null) _ = socket.DisposeAsync().AsTask();
    }

    private async Task OpenRoomSocketAsync(string lobbyId, string credential, LobbyWebSocket.HelloMode mode)
    {
        if (RoomSocket != null)
        {
            await RoomSocket.DisposeAsync();
            RoomSocket = null;
        }

        var wsUri = LobbyWebSocket.BuildWsUri(Api.BaseUri, $"lobbies/{lobbyId}/ws");
        DiagnosticLog.Write($"OpenRoomSocketAsync: WS URI {wsUri}");
        var sock = new LobbyWebSocket(wsUri, mode, credential);
        sock.FrameReceived += OnFrame;
        sock.Disconnected += (_, reason) => DiagnosticLog.Write($"Room WS disconnected: {reason}");
        RoomSocket = sock;
        sock.Start();
        DiagnosticLog.Write($"OpenRoomSocketAsync: WS started for {lobbyId}");
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
