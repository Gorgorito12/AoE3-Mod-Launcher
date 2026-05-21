using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer.P2P;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Top-level state machine for the multiplayer feature. One instance per
/// launcher run. Coordinates the subsystems that the UI shouldn't have
/// to glue together itself:
///
///   * <see cref="LobbyApiClient"/> — REST to the Worker (meta layer:
///     auth, lobbies, chat, ELO).
///   * <see cref="LobbyWebSocket"/> — per-room realtime channel.
///   * <see cref="PeerMesh"/> — direct UDP P2P fabric (hole-punched).
///   * <see cref="NativeHook.AoeP2pHookInjector"/> — DLL injected into
///     age3y.exe that captures DirectPlay traffic in-process and ships
///     it to the mesh. Replaces the old WinDivert+Wintun bridge.
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
    public LobbyWebSocket? RoomSocket { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>
    /// Direct P2P fabric for the current room. Recreated on every
    /// room enter; null when the user isn't in a room. UI may query
    /// <c>Peers</c> for connection state per member.
    /// </summary>
    public PeerMesh? Mesh { get; private set; }

    /// <summary>
    /// True when both halves of the P2P plumbing are in place: the
    /// <see cref="Mesh"/> is up for this room AND the DLL-injector
    /// helper (<see cref="NativeHook.AoeP2pHookInjector.IsAvailable"/>)
    /// is shipped next to the launcher .exe, so when the user hits
    /// Start Game the launcher can inject <c>AoeP2pHook.dll</c> into
    /// age3y.exe and own LAN traffic forwarding from inside the game
    /// process.
    ///
    /// "Ready" means we will be able to spin up the bridge — we do
    /// NOT block here on the hook's actual IPC handshake, because the
    /// hook can only connect AFTER the game spawns. From the lobby's
    /// perspective, "injector present" is the right "we can handle
    /// LAN" promise. Drives the lobby header banner and the Start
    /// Game button's enable state.
    /// </summary>
    public bool IsP2pBridgeReady =>
        Mesh != null && NativeHook.AoeP2pHookInjector.IsAvailable();

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
        if (Mesh != null)
        {
            await Mesh.DisposeAsync();
            Mesh = null;
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
            // Ask the Worker for permission to join the lobby. The
            // mod fingerprint check happens server-side; on success we
            // get a short-lived join_token that authenticates our WS.
            var join = await Api.JoinLobbyAsync(lobbyId, new JoinLobbyRequest
            {
                ModCombinedHash = modCombinedHash,
                Password = password,
            }, ct);

            // Open the room WS — that triggers PeerMesh bootstrap
            // inside OpenRoomSocketAsync. Hole-punching to the other
            // room members starts as soon as their peer_announce
            // frames arrive.
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
    /// row in D1; we just need to open the room WS with our JWT
    /// (no join_token for the host path) and let OpenRoomSocketAsync
    /// bring up the P2P stack.
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
        var mesh = Mesh;

        // Optimistic UI transition first — the user sees the room
        // collapse and the lobby list reappear within a single frame.
        CurrentLobbyId = null;
        CurrentLobbyTitle = null;
        RoomSocket = null;
        Mesh = null;
        Lobby = LobbyStatus.Idle;
        Raise();

        // Tear down P2P fabric in the background — sockets close,
        // hole-punch loops bail.
        if (mesh != null) _ = mesh.DisposeAsync().AsTask();

        // The REST /leave call is the only thing that matters for
        // server-side cleanup — it marks the lobby `closed` in D1 and
        // notifies the other members via the room WS. We await it so
        // MainWindow.OnClosing can guarantee the Worker received the
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

        // Build a fresh P2P mesh for this room. Doing it here (after
        // the WS is up but before we start it) means the mesh's STUN
        // probe runs in parallel with the room_state arrival — by the
        // time peers announce themselves we already know our public
        // endpoint and can answer with our own peer_announce.
        var ownUserId = CurrentUser?.Id;
        if (!string.IsNullOrEmpty(ownUserId))
        {
            try
            {
                var mesh = new PeerMesh();
                mesh.PeerStateChanged += (_, peer) =>
                {
                    Raise();
                    // Verbose per-state-change log so a future P2P
                    // investigation can read off the timeline of
                    // "peer X went Discovering → Punching → Connected"
                    // without instrumenting the launcher.
                    try
                    {
                        DiagnosticLog.Write(
                            $"PeerMesh state change: user={peer.UserId} login={peer.Login} " +
                            $"state={peer.State} rtt={peer.RttMs:F0}ms endpoint={peer.ConfirmedEndpoint}");
                    }
                    catch { /* logging should never throw */ }
                };
                await mesh.StartAsync(ownUserId, _config.Multiplayer.RelayOnly);
                Mesh = mesh;
                var epSummary = mesh.LocalEndpoints != null && mesh.LocalEndpoints.Count > 0
                    ? string.Join(", ", mesh.LocalEndpoints.Select(ep => $"{ep.Ip}:{ep.Port}({ep.Kind})"))
                    : "(none)";
                DiagnosticLog.Write(
                    $"PeerMesh started for user '{ownUserId}'. " +
                    $"Local endpoints announced: {mesh.LocalEndpoints?.Count ?? 0} [{epSummary}]");

                // Publish our endpoints to everyone else in the room.
                // Fire-and-forget — the WS is already up. Peers will
                // start hole-punching us as soon as this lands.
                //
                // We pass user_id explicitly: the Worker echoes
                // peer_announce back unchanged, and PeerMesh.OnPeerAnnounceFromWs
                // drops any frame without a user_id as defensive
                // hygiene against malformed broadcasts. Without this
                // field, a freshly-deployed Worker that no longer
                // injects user_id server-side silently breaks all P2P.
                _ = sock.SendAsync(new
                {
                    type = "peer_announce",
                    user_id = ownUserId,
                    endpoints = mesh.LocalEndpoints,
                });

                // No further bridge bring-up is needed here: the LAN
                // traffic forwarding lives entirely inside the hook
                // DLL that gets injected into age3y.exe when the user
                // hits Start Game. IsP2pBridgeReady will flip to true
                // as soon as this mesh is up, and the launch path
                // takes care of the rest.
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"MultiplayerSession: PeerMesh start failed: {ex.Message}");
            }
        }
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

        // Route P2P signaling + membership events into the mesh so
        // hole-punching kicks in / cleans up as peers come and go.
        // The mesh is set up to ignore frames for our own user id, so
        // looping a peer_announce echo back to ourselves is harmless.
        var mesh = Mesh;
        if (mesh == null) return;

        try
        {
            switch (e.Type)
            {
                case "peer_announce":
                {
                    var a = JsonSerializer.Deserialize<WsPeerAnnounce>(e.Json.GetRawText());
                    if (a != null) mesh.OnPeerAnnounceFromWs(a);
                    break;
                }
                case "peer_relay":
                {
                    var r = JsonSerializer.Deserialize<WsPeerRelay>(e.Json.GetRawText());
                    if (r != null) mesh.OnPeerRelayFromWs(r);
                    break;
                }
                case "member_joined":
                {
                    var uid = e.Json.TryGetProperty("user_id", out var u) ? u.GetString() : null;
                    var login = e.Json.TryGetProperty("github_login", out var l) ? l.GetString() : null;
                    if (!string.IsNullOrEmpty(uid))
                        mesh.OnMemberJoined(uid, login ?? uid);
                    break;
                }
                case "member_left":
                {
                    var uid = e.Json.TryGetProperty("user_id", out var u) ? u.GetString() : null;
                    if (!string.IsNullOrEmpty(uid))
                        mesh.OnMemberLeft(uid);
                    break;
                }
                case "room_state":
                {
                    // Seed the mesh with every member from the initial
                    // snapshot. Their endpoints will follow via
                    // peer_announce frames each one sends.
                    if (e.Json.TryGetProperty("members", out var members) &&
                        members.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var m in members.EnumerateObject())
                        {
                            var uid = m.Name;
                            var login = m.Value.TryGetProperty("login", out var l)
                                ? (l.GetString() ?? uid)
                                : uid;
                            mesh.OnMemberJoined(uid, login);
                        }
                    }
                    break;
                }
                case "game_relay":
                {
                    // Worker-as-TURN inbound (Phase 2.c): a peer whose
                    // hole-punch with us failed tunnelled their game
                    // packet through the lobby WS. We hand it to the
                    // hook DLL via the bridge so AoE3 sees it as a
                    // normal recvfrom (with the sender's virtual IP).
                    //
                    // Frame shape mirrors LobbyWebSocket.SendGameRelayAsync:
                    //   { type: "game_relay", from_user, src_port,
                    //     dst_port, payload_b64 }
                    // (The Worker rewrites the outbound `to_user` into
                    // `from_user` for the recipient — same pattern as
                    // peer_relay → from_user/from_login.)
                    var bridge = NativeHook.AoeP2pBridgeService.Current;
                    if (bridge == null) break;  // no game running, nothing to inject into

                    var fromUser = e.Json.TryGetProperty("from_user", out var fu)
                        ? (fu.GetString() ?? "") : "";
                    var srcPort = e.Json.TryGetProperty("src_port", out var sp) && sp.ValueKind == JsonValueKind.Number
                        ? sp.GetInt32() : 0;
                    var dstPort = e.Json.TryGetProperty("dst_port", out var dp) && dp.ValueKind == JsonValueKind.Number
                        ? dp.GetInt32() : 0;
                    byte[] payload = Array.Empty<byte>();
                    if (e.Json.TryGetProperty("payload_b64", out var pb) && pb.ValueKind == JsonValueKind.String)
                    {
                        var b64 = pb.GetString();
                        if (!string.IsNullOrEmpty(b64))
                        {
                            try { payload = Convert.FromBase64String(b64); }
                            catch (FormatException) { /* malformed; leave empty so we bail below */ }
                        }
                    }
                    if (string.IsNullOrEmpty(fromUser) || payload.Length == 0)
                        break;

                    var srcVip = WarsOfLibertyLauncher.Services.Multiplayer.NativeHook
                        .VirtualIpAllocator.DeriveFor(fromUser);

                    // The hook's PACKET_IN handler delivers to every g_lanSockets
                    // entry bound to dstPort, so dstIp is informational only.
                    // Use broadcast as a sane placeholder.
                    bridge.InjectIntoGame(
                        srcVirtualIp: srcVip,
                        srcPort: (ushort)srcPort,
                        dstIp: System.Net.IPAddress.Broadcast,
                        dstPort: (ushort)dstPort,
                        payload: payload);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"MultiplayerSession.OnFrame P2P route ({e.Type}): {ex.Message}");
        }
    }

    private void Raise() => StateChanged?.Invoke(this, EventArgs.Empty);
}

file static class StringExt
{
    public static string? NullIfEmpty(this string s) =>
        string.IsNullOrEmpty(s) ? null : s;
}
