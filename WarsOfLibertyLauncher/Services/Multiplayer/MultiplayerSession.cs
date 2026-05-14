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
///   * <see cref="VirtualLanService"/> — WinDivert bridge that makes
///     AoE3 think it's playing on a LAN.
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
    /// WinDivert-backed virtual LAN bridge. Captures AoE3's local
    /// broadcasts and forwards them via <see cref="Mesh"/> to peers;
    /// receives peers' game packets and re-injects them locally so
    /// AoE3 sees a LAN. Null when WinDivert isn't installed or the
    /// launcher isn't running elevated.
    /// </summary>
    public VirtualLanService? VirtualLan { get; private set; }

    /// <summary>
    /// True when both <see cref="Mesh"/> is connected to at least one
    /// peer AND <see cref="VirtualLan"/> is capturing. Drives the
    /// "P2P ready" indicator in the lobby header — if this is false
    /// the host should fall back to the ZeroTier path for game traffic.
    /// </summary>
    public bool IsVirtualLanActive => VirtualLan != null && Mesh != null;

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
        if (VirtualLan != null)
        {
            try { VirtualLan.Dispose(); } catch { /* shutdown path */ }
            VirtualLan = null;
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

            // Open the room WS — that triggers PeerMesh + VirtualLan
            // bootstrap inside OpenRoomSocketAsync. Hole-punching to
            // the other room members starts as soon as their
            // peer_announce frames arrive.
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
        var vlan = VirtualLan;

        // Optimistic UI transition first — the user sees the room
        // collapse and the lobby list reappear within a single frame.
        CurrentLobbyId = null;
        CurrentLobbyTitle = null;
        RoomSocket = null;
        Mesh = null;
        VirtualLan = null;
        Lobby = LobbyStatus.Idle;
        Raise();

        // Tear down P2P fabric in the background — sockets close,
        // hole-punch loops bail, WinDivert handles release.
        if (mesh != null) _ = mesh.DisposeAsync().AsTask();
        if (vlan != null)
        {
            _ = Task.Run(() =>
            {
                try { vlan.Dispose(); }
                catch (Exception ex) { DiagnosticLog.Write($"VLan dispose: {ex.Message}"); }
            });
        }

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
                _ = sock.SendAsync(new
                {
                    type = "peer_announce",
                    endpoints = mesh.LocalEndpoints,
                });

                // Spin up the WinDivert virtual-LAN bridge if the
                // driver is available + the launcher has the privileges
                // to open a capture handle. Failures are non-fatal:
                // ZeroTier keeps working as the game-traffic transport
                // until the new stack is mature.
                StartVirtualLan(mesh);
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"MultiplayerSession: PeerMesh start failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Start the WinDivert virtual-LAN bridge for this room. Idempotent
    /// — if WinDivert isn't installed or we lack admin rights, this
    /// logs the reason and leaves <see cref="VirtualLan"/> null so the
    /// caller can fall back to the legacy transport.
    /// </summary>
    private void StartVirtualLan(PeerMesh mesh)
    {
        // Voobly-style virtual NIC: if the user opted into it, ensure
        // a Microsoft Loopback adapter exists and carries a derived
        // 10.147.x.y IP so AoE3's lobby UI shows that instead of the
        // real LAN address. Fire-and-forget — failures are logged and
        // do not block the rest of the multiplayer setup.
        if (_config.Multiplayer.VirtualAdapterEnabled && CurrentUser != null)
        {
            var ownIp = VirtualAdapterService.DeriveIpFor(CurrentUser.Id);
            _ = Task.Run(async () =>
            {
                try
                {
                    if (!await VirtualAdapterService.IsInstalledAsync())
                    {
                        var ok = await VirtualAdapterService.InstallAsync();
                        if (!ok)
                        {
                            DiagnosticLog.Write("VirtualAdapter: install failed; the lobby will show the real LAN IP this session.");
                            return;
                        }
                    }
                    await VirtualAdapterService.ConfigureAsync(ownIp);
                    DiagnosticLog.Write($"VirtualAdapter: configured with {ownIp} for {CurrentUser.GithubLogin}");
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Write($"VirtualAdapter setup: {ex.Message}");
                }
            });
        }

        if (!WinDivertNative.IsAvailable())
        {
            DiagnosticLog.Write(
                "MultiplayerSession: WinDivert not available — virtual LAN disabled. " +
                "Game traffic will not flow until the driver is installed and the " +
                "launcher is restarted elevated.");
            return;
        }

        try
        {
            var vlan = new VirtualLanService();

            // Outgoing path: AoE3 broadcasts → captured → fan-out.
            // Connected peers get a direct UDP send; peers whose
            // hole-punching failed are TURN-relayed through the
            // lobby WS so they stay in the session.
            vlan.GamePacketCaptured += async (_, packet) =>
            {
                try
                {
                    var relayTargets = new System.Collections.Generic.List<string>();
                    await mesh.BroadcastGamePacketAsync(packet, relayTargets);
                    // Tally outgoing traffic per peer. mesh.Peers
                    // gives us the live set of recipients; each one
                    // received a copy of `packet.Payload`, so we
                    // record the same byte count per peer. Counters
                    // live in-process and feed the InGame status
                    // panel — no Worker calls, no KV writes.
                    var payloadLen = packet.Payload?.Length ?? 0;
                    foreach (var peer in mesh.Peers)
                    {
                        vlan.RecordBytesOut(peer.UserId, payloadLen);
                    }
                    if (relayTargets.Count > 0 && RoomSocket != null)
                    {
                        foreach (var uid in relayTargets)
                        {
                            try
                            {
                                await RoomSocket.SendGameRelayAsync(
                                    uid, packet.SrcPort, packet.DstPort, packet.Payload);
                            }
                            catch (Exception ex)
                            {
                                DiagnosticLog.Write($"MultiplayerSession.GameRelay → {uid}: {ex.Message}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Write($"MultiplayerSession.GamePacketCaptured: {ex.Message}");
                }
            };

            // Incoming path: peer's packet → inject locally with the
            // peer's allocated 10.147.x.y virtual address so AoE3
            // treats it like a real LAN host.
            mesh.GamePacketReceived += (_, payload) =>
            {
                try
                {
                    vlan.Inject(payload.FromUserId, payload.Packet);
                    vlan.RecordBytesIn(payload.FromUserId, payload.Packet.Payload?.Length ?? 0);
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Write($"MultiplayerSession.GamePacketReceived: {ex.Message}");
                }
            };

            vlan.Start();
            VirtualLan = vlan;
            Raise();
            DiagnosticLog.Write("MultiplayerSession: VirtualLanService active for this room.");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"MultiplayerSession.StartVirtualLan: {ex.Message}");
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
                    // Worker-as-TURN inbound: another peer couldn't
                    // hole-punch us, so they tunneled their game
                    // packet through the lobby WS. Decode and inject
                    // locally just like a direct mesh frame.
                    var vlan = VirtualLan;
                    if (vlan == null) break;
                    var fromUser = e.Json.TryGetProperty("from_user", out var fu) ? fu.GetString() : null;
                    var srcPort = e.Json.TryGetProperty("src_port", out var sp) ? sp.GetUInt16() : (ushort)0;
                    var dstPort = e.Json.TryGetProperty("dst_port", out var dp) ? dp.GetUInt16() : (ushort)0;
                    var payloadB64 = e.Json.TryGetProperty("payload_b64", out var pb) ? pb.GetString() : null;
                    if (string.IsNullOrEmpty(fromUser) || string.IsNullOrEmpty(payloadB64)) break;
                    try
                    {
                        var payload = Convert.FromBase64String(payloadB64);
                        vlan.Inject(fromUser, new GamePacket(srcPort, dstPort, payload));
                        vlan.RecordBytesIn(fromUser, payload.Length);
                    }
                    catch (Exception ex)
                    {
                        DiagnosticLog.Write($"MultiplayerSession.game_relay inject: {ex.Message}");
                    }
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
