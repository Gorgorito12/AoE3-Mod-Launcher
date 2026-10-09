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
    /// <remarks>
    /// Can fire on the room socket's PUMP thread (a <c>game_started</c> frame), not only on the
    /// caller's — subscribers marshal, as <c>MultiplayerTab.OnSessionStateChanged</c> does.
    /// </remarks>
    public event EventHandler? StateChanged;

    public SessionStatus Status { get; private set; } = SessionStatus.SignedOut;
    public LobbyStatus Lobby { get; private set; } = LobbyStatus.Idle;
    public LobbyUserSummary? CurrentUser { get; private set; }
    public string? CurrentLobbyId { get; private set; }
    public string? CurrentLobbyTitle { get; private set; }
    public LobbyWebSocket? RoomSocket { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>
    /// Adopt a room name pushed by the server (the <c>room_renamed</c> frame,
    /// after a host rename). The server owns the name — this is the ONLY way it
    /// changes mid-room, so clients can never drift apart. Normalises blank to
    /// null, matching how create/join seed it.
    /// </summary>
    public void SetCurrentLobbyTitle(string? title)
    {
        CurrentLobbyTitle = string.IsNullOrWhiteSpace(title) ? null : title;
        Raise();
    }

    /// <summary>
    /// The cached session JWT, or null when signed out. Exposed so the
    /// global-chat WebSocket (lifecycle owned by <c>MultiplayerTab</c>,
    /// since it's gated on tab visibility) can authenticate its <c>hello</c>
    /// frame with the same token the room socket uses.
    /// </summary>
    public string? SessionToken => _config.Multiplayer.SessionToken.NullIfEmpty();

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

        // Signing out ends any room membership too. This used to clear the token and
        // the user and leave Lobby/CurrentLobbyId exactly as they were, so signing out
        // from inside a room and back in left the session still claiming that room —
        // and JoinLobbyAsync's Idle guard then refused every join for the rest of the
        // process. Nothing server-side survives a sign-out either, so the local state
        // must not.
        var orphan = RoomSocket;
        CurrentLobbyId = null;
        CurrentLobbyTitle = null;
        RoomSocket = null;
        Lobby = LobbyStatus.Idle;
        Raise();
        if (orphan != null) _ = orphan.DisposeAsync().AsTask();
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
        string? title = null,
        bool asSpectator = false,
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
                // The server REFUSES this when no watching seat is free rather than seating
                // the asker as a player, so a race with another watcher surfaces as an error
                // instead of quietly putting somebody into the match.
                AsSpectator = asSpectator,
            }, ct);

            await OpenRoomSocketAsync(lobbyId, join.JoinToken, LobbyWebSocket.HelloMode.JoinToken);
            CurrentLobbyId = lobbyId;
            CurrentLobbyTitle = string.IsNullOrWhiteSpace(title) ? null : title;
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
        string? title = null,
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
            CurrentLobbyTitle = string.IsNullOrWhiteSpace(title) ? null : title;
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

    /// <summary>
    /// Clears a room the SERVER already closed — locally, with no REST call.
    ///
    /// <para>For a terminal close on the room socket (<c>4404</c>/<c>4006</c>: the room is gone;
    /// <c>4002</c>/<c>4004</c>: our membership is). There is nothing to tell the server — it is the
    /// one that closed it — and calling <c>/leave</c> for a host would re-stamp the room's
    /// <c>closed_at</c> and re-finalise its Discord post. The same local clear the drift branch of
    /// <see cref="LeaveCurrentLobbyAsync"/> and <see cref="SignOut"/> perform; leaving the session
    /// as it was is the zombie room this exists to end.</para>
    /// </summary>
    public void DropClosedLobby(string why)
    {
        if (Lobby == LobbyStatus.Idle) return;
        DiagnosticLog.Write(
            $"MultiplayerSession: dropping room {CurrentLobbyId ?? "(no id)"} locally - {why}.");
        var orphan = RoomSocket;
        CurrentLobbyId = null;
        CurrentLobbyTitle = null;
        RoomSocket = null;
        Lobby = LobbyStatus.Idle;
        Raise();
        if (orphan != null) _ = orphan.DisposeAsync().AsTask();
    }

    /// <param name="why">Who asked — logged, because a bundle could not tell the player's own
    /// leave (or window close) from a server close: both ended in the same 4006.</param>
    public async Task LeaveCurrentLobbyAsync(CancellationToken ct = default, string why = "unspecified")
    {
        if (Lobby == LobbyStatus.Idle) return;

        // THE RECOVERY PATH MAY NEVER REFUSE TO RECOVER. This used to read
        // `Lobby == Idle || CurrentLobbyId == null`, phrased as "nothing to do" —
        // but the second half really said "if the two fields have drifted apart,
        // return silently and leave them drifted". Since this is the ONLY thing in
        // the codebase that puts Lobby back to Idle outside a failed join, a single
        // drift (see the detached-socket note in OnFrame, or a SignOut) wedged the
        // session for the rest of the process: every join threw before reaching the
        // catch that would have reset it. Claiming a room with no id is exactly the
        // state that needs clearing, so clear it — there is simply no id to tell the
        // server about, and the server already believes we left.
        if (CurrentLobbyId == null)
        {
            DiagnosticLog.Write(
                $"MultiplayerSession.Leave: state drifted (Lobby={Lobby}, no lobby id) — clearing locally.");
            var orphan = RoomSocket;
            CurrentLobbyTitle = null;
            RoomSocket = null;
            Lobby = LobbyStatus.Idle;
            Raise();
            if (orphan != null) _ = orphan.DisposeAsync().AsTask();
            return;
        }

        var lobbyId = CurrentLobbyId;
        var socket = RoomSocket;
        DiagnosticLog.Write($"Leaving room {lobbyId} ({why}) — Lobby={Lobby}.");

        // Optimistic UI transition first — the user sees the room
        // collapse and the lobby list reappear within a single frame.
        CurrentLobbyId = null;
        CurrentLobbyTitle = null;
        RoomSocket = null;
        Lobby = LobbyStatus.Idle;
        Raise();

        // The socket stays UP until the server has our /leave — it decides host migration and
        // whether a walkout is recorded from the order the two arrive in — but it must not
        // reconnect afterwards: for a host alone in the room this /leave closes the room, the
        // socket gets 4006, and the retry loop used to chase 4404s until the dispose below.
        socket?.EndAfterThisConnection();

        // The REST /leave call is the only thing that matters for
        // server-side cleanup — it marks the lobby `closed` and
        // notifies the other members via the room WS. We await it so
        // MainWindow.OnClosing can guarantee the backend received the
        // message before the launcher process exits.
        try
        {
            var started = Environment.TickCount64;
            await Api.LeaveLobbyAsync(lobbyId, ct);
            DiagnosticLog.Write($"REST /leave ok in {Environment.TickCount64 - started} ms.");
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
            // Creating a room from inside another one: the old socket goes without a /leave (the
            // server closes that room itself when the new one is created). Said here, because the
            // old socket's last 4006 otherwise looks exactly like a server closing a live room.
            DiagnosticLog.Write(
                $"OpenRoomSocketAsync: replacing the socket of {CurrentLobbyId ?? "(no id)"} without a /leave.");
            await RoomSocket.DisposeAsync();
            RoomSocket = null;
        }

        var wsUri = LobbyWebSocket.BuildWsUri(Api.BaseUri, $"lobbies/{lobbyId}/ws");
        DiagnosticLog.Write($"OpenRoomSocketAsync: WS URI {wsUri}");
        var sock = new LobbyWebSocket(wsUri, mode, credential);
        sock.FrameReceived += OnFrame;
        // Which room, and whether it is still ours: the tab logs the current socket's events, so
        // this is the only trace of a socket the session has already left behind.
        sock.Disconnected += (s, reason) => DiagnosticLog.Write(
            $"Room WS disconnected ({lobbyId}{(ReferenceEquals(s, RoomSocket) ? "" : ", no longer the current room")}): {reason}");
        sock.Reconnecting += (s, next) =>
        {
            if (!ReferenceEquals(s, RoomSocket))
                DiagnosticLog.Write($"Room WS reconnecting ({lobbyId}, no longer the current room) {next}");
        };
        RoomSocket = sock;
        sock.Start();
        DiagnosticLog.Write($"OpenRoomSocketAsync: WS started for {lobbyId}");
    }

    private void OnFrame(object? sender, LobbyWebSocket.FrameReceivedEventArgs e)
    {
        // A DETACHED socket may not write state. This handler is subscribed in
        // OpenRoomSocketAsync and never unsubscribed, it runs on the WS pump thread
        // (unlike MultiplayerTab.OnRoomFrame, which marshals), and DisposeAsync only
        // aborts the socket — so a frame decoded moments before a leave can still be
        // delivered afterwards. Without this guard, pressing Leave DURING a countdown
        // let the pump write Lobby = InGame right after LeaveCurrentLobbyAsync had set
        // Idle and nulled CurrentLobbyId, landing on (InGame, null): a pair that the
        // recovery path itself then refused to repair, so every later join threw
        // "Leave the current lobby first." until the launcher was restarted.
        if (!ReferenceEquals(sender, RoomSocket)) return;

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
