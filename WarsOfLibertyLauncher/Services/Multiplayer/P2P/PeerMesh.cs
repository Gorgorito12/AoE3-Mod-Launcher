using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer.P2P;

/// <summary>
/// Top-level P2P fabric for one lobby room.
///
/// Owns:
///   * One shared <see cref="UdpClient"/> bound to a single local UDP
///     port. The port was already STUN-probed by
///     <see cref="NatTypeDetector"/> so its public mapping is known.
///   * One <see cref="PeerChannel"/> per other room member, each
///     tracking its own hole-punching state machine.
///   * A 250-ms tick timer that feeds <c>Tick(now)</c> to every
///     channel and writes the returned datagrams to the socket.
///   * A background receive loop that demuxes inbound datagrams to
///     the channel whose <c>Matches</c> says "yes".
///
/// External lifecycle (called by <see cref="MultiplayerSession"/>):
///   <c>StartAsync(ownUserId)</c>      → bind socket, STUN-probe, return public endpoint
///   <c>OnPeerAnnounceFromWs(...)</c>  → fed by the lobby WS handler
///   <c>OnPeerRelayFromWs(...)</c>     → fed by the lobby WS handler
///   <c>OnMemberLeft(userId)</c>       → remove channel + drop pending probes
///   <c>StopAsync()</c>                → close socket, kill loops
///
/// What the lobby WS layer needs to do:
///   1. Tell us the user ids of other room members so we can spin up
///      channels in Discovering state.
///   2. Send a <c>peer_announce</c> frame with the endpoints
///      <see cref="LocalEndpoints"/> returns.
///   3. Forward every incoming <c>peer_announce</c> + <c>peer_relay</c>
///      frame into our handler methods.
/// </summary>
public sealed class PeerMesh : IAsyncDisposable
{
    public event EventHandler<PeerChannel>? PeerStateChanged;

    /// <summary>
    /// Fired when a peer's launcher pushed an AoE3 game frame to us
    /// over the mesh. Args: (peer user id, decoded packet). The
    /// hook-IPC bridge (Phase 2.b) consumes these and forwards them
    /// to the in-game DLL hook so AoE3 sees a LAN datagram.
    /// </summary>
    public event EventHandler<(string FromUserId, GamePacket Packet)>? GamePacketReceived;

    /// <summary>
    /// Fired whenever the set returned by <see cref="RoutableUserIds"/>
    /// changes — i.e. a peer transitions into or out of
    /// <see cref="PeerLinkState.Connected"/> OR <see cref="PeerLinkState.Failed"/>,
    /// or a member is removed outright. Subscribers (notably the
    /// hook-IPC bridge) re-derive the peer virtual IP set and resend
    /// it to the hook so AoE3's sendto interception stays aligned with
    /// the live mesh.
    ///
    /// Phase 2.c: Failed peers are now "routable" via the Cloudflare
    /// Worker WS as a TURN fallback, so transitions in / out of Failed
    /// also belong in the hook's peer set and fire this event.
    ///
    /// Fires on whatever thread caused the state change — usually the
    /// receive loop or the lobby WS handler. Keep handlers cheap or
    /// dispatch elsewhere.
    /// </summary>
    public event EventHandler? PeersChanged;

    /// <summary>Snapshot of all channels, for UI rendering.</summary>
    public IReadOnlyCollection<PeerChannel> Peers => _peers.Values.ToArray();

    /// <summary>
    /// User ids of every peer currently in <see cref="PeerLinkState.Connected"/>.
    /// Used by callers that only care about peers reachable by direct
    /// UDP. The hook-IPC bridge should prefer <see cref="RoutableUserIds"/>
    /// so it also intercepts sendtos targeted at Failed peers (which
    /// we relay through the Worker WS).
    /// </summary>
    public IEnumerable<string> ConnectedUserIds =>
        _peers.Values
            .Where(p => p.State == PeerLinkState.Connected)
            .Select(p => p.UserId);

    /// <summary>
    /// User ids of every peer the launcher can deliver a game packet
    /// to right now: either a direct UDP hole-punched path
    /// (<see cref="PeerLinkState.Connected"/>) OR a Worker-relayed
    /// fallback (<see cref="PeerLinkState.Failed"/>, Phase 2.c TURN).
    ///
    /// The hook DLL uses this set to decide which sendto destinations
    /// to divert into the launcher: anything aimed at a Failed peer
    /// must still hit the bridge, because BroadcastGamePacketAsync
    /// reports those peers via its needsRelay list and the caller
    /// tunnels through RoomSocket.SendGameRelayAsync.
    /// </summary>
    public IEnumerable<string> RoutableUserIds =>
        _peers.Values
            .Where(p => p.State == PeerLinkState.Connected
                     || p.State == PeerLinkState.Failed)
            .Select(p => p.UserId);

    /// <summary>Endpoints to announce over the lobby WS so peers know how to reach us.</summary>
    public IReadOnlyList<WsPeerEndpoint> LocalEndpoints { get; private set; } =
        Array.Empty<WsPeerEndpoint>();

    /// <summary>
    /// User id used for our half of the mesh. Empty string before
    /// <see cref="StartAsync"/> has set it. Exposed so peer-side code
    /// (notably <c>AoeP2pBridgeService</c>'s outbound IP rewriter)
    /// can derive our virtual IP via <c>VirtualIpAllocator.DeriveFor</c>
    /// without piping it through every constructor.
    /// </summary>
    public string OwnUserId => _ownUserId;

    private readonly ConcurrentDictionary<string, PeerChannel> _peers = new();
    private readonly CancellationTokenSource _cts = new();
    private UdpClient? _socket;
    private string _ownUserId = "";
    private Task? _receiveLoop;
    private Task? _tickLoop;

    /// <summary>
    /// Bind the shared UDP socket, run a STUN probe to discover the
    /// public endpoint, and snapshot LAN endpoints. The result is
    /// stashed in <see cref="LocalEndpoints"/> for the caller to
    /// publish via <c>peer_announce</c>.
    ///
    /// <paramref name="relayOnly"/> = privacy mode. When true, the
    /// STUN probe is skipped entirely and the local endpoint list
    /// contains only LAN candidates. Peers can't hole-punch us
    /// directly; their game packets fall back to the WS-relayed
    /// <c>game_relay</c> path through the Worker. Net effect: this
    /// user's public IP never reaches another player.
    /// </summary>
    public async Task StartAsync(string ownUserId, bool relayOnly = false, CancellationToken ct = default)
    {
        if (_socket != null) throw new InvalidOperationException("Already started.");
        _ownUserId = ownUserId;

        // Make sure Windows Firewall isn't silently dropping our
        // inbound UDP traffic. Best-effort; logged failures don't
        // block the rest of the mesh setup.
        EnsureFirewallRule();

        _socket = new UdpClient(0);                 // ephemeral local port

        // STUN probe to discover our public address. Reuses the same
        // socket so the public mapping we announce is the same one
        // peers will hole-punch against. SKIPPED in relay-only mode.
        IPEndPoint? publicEp = null;
        if (!relayOnly)
        {
            foreach (var (host, port) in NatTypeDetector.DefaultServers)
            {
                try
                {
                    var addrs = await Dns.GetHostAddressesAsync(host, ct);
                    var v4 = addrs.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
                    if (v4 == null) continue;
                    var result = await StunClient.BindingRequestAsync(
                        _socket, new IPEndPoint(v4, port), TimeSpan.FromSeconds(2), ct);
                    publicEp = result.Mapped;
                    break;
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Write($"PeerMesh: STUN {host}:{port} failed: {ex.Message}");
                }
            }
        }
        else
        {
            DiagnosticLog.Write("PeerMesh: relay-only mode — skipping STUN, hiding public IP from peers.");
        }

        // Build the candidate list: public address from STUN (if not
        // in relay-only) plus every IPv4 LAN address.
        var endpoints = new List<WsPeerEndpoint>();
        var localPort = ((IPEndPoint)_socket.Client.LocalEndPoint!).Port;
        if (publicEp != null)
        {
            endpoints.Add(new WsPeerEndpoint
            {
                Ip = publicEp.Address.ToString(),
                Port = publicEp.Port,
                Kind = "stun",
            });
        }
        foreach (var lan in EnumerateLanAddresses())
        {
            endpoints.Add(new WsPeerEndpoint
            {
                Ip = lan.ToString(),
                Port = localPort,
                Kind = "lan",
            });
        }
        LocalEndpoints = endpoints;

        // Kick the background loops.
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_cts.Token));
        _tickLoop = Task.Run(() => TickLoopAsync(_cts.Token));
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    public async Task StopAsync()
    {
        try { _cts.Cancel(); } catch { /* already cancelled */ }
        try { _socket?.Close(); } catch { }
        _socket = null;

        if (_receiveLoop != null) { try { await _receiveLoop; } catch { } }
        if (_tickLoop != null)    { try { await _tickLoop;    } catch { } }

        _peers.Clear();
        try { _cts.Dispose(); } catch { }
    }

    /// <summary>
    /// Register a new room member. Idempotent — re-adding a known
    /// peer is a no-op so the lobby WS handler can call this on
    /// every <c>room_state</c> snapshot without filtering.
    /// </summary>
    public void OnMemberJoined(string userId, string login)
    {
        if (string.IsNullOrEmpty(userId)) return;
        if (userId == _ownUserId) return;          // skip ourselves

        bool wasNew = false;
        _peers.GetOrAdd(userId, _ =>
        {
            wasNew = true;
            var ch = new PeerChannel(userId, login, _ownUserId);
            // Capture the previous state across firings so we can fire
            // PeersChanged exactly when this peer crosses a "routable"
            // boundary in either direction. Phase 2.c made BOTH
            // Connected and Failed routable (direct UDP vs Worker
            // relay), so we treat both states as part of the set the
            // hook bridge cares about.
            var prevState = ch.State;
            ch.StateChanged += (s, _) =>
            {
                var asChannel = (PeerChannel)s!;
                PeerStateChanged?.Invoke(this, asChannel);
                var newState = asChannel.State;
                bool wasRoutable = prevState == PeerLinkState.Connected
                                || prevState == PeerLinkState.Failed;
                bool nowRoutable = newState == PeerLinkState.Connected
                                || newState == PeerLinkState.Failed;
                prevState = newState;
                if (wasRoutable != nowRoutable)
                {
                    try { PeersChanged?.Invoke(this, EventArgs.Empty); }
                    catch (Exception ex)
                    {
                        DiagnosticLog.Write($"PeerMesh.PeersChanged handler: {ex.Message}");
                    }
                }
            };
            return ch;
        });
        if (wasNew)
        {
            DiagnosticLog.Write($"PeerMesh.OnMemberJoined: created PeerChannel for user='{userId}' login='{login}'");
        }
    }

    /// <summary>Drop a member's channel when they leave the room.</summary>
    public void OnMemberLeft(string userId)
    {
        if (_peers.TryRemove(userId, out var ch))
        {
            PeerStateChanged?.Invoke(this, ch);    // last paint
            // Removing a routable peer (Connected OR Failed) shrinks
            // the set the bridge tracks — let subscribers refresh the
            // hook's peer list. Phase 2.c added Failed to the
            // "routable" set since it's now reachable via the Worker
            // WS relay.
            if (ch.State == PeerLinkState.Connected
                || ch.State == PeerLinkState.Failed)
            {
                try { PeersChanged?.Invoke(this, EventArgs.Empty); }
                catch (Exception ex)
                {
                    DiagnosticLog.Write($"PeerMesh.PeersChanged handler: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Feed a <c>peer_announce</c> frame from the lobby WS. Parses
    /// the endpoints into <see cref="IPEndPoint"/>s and seeds the
    /// matching channel's candidate list, which starts hole-punching.
    /// </summary>
    public void OnPeerAnnounceFromWs(WsPeerAnnounce frame)
    {
        // The DO echoes peer_announce back to the sender too — drop
        // our own announce instead of trying to hole-punch ourselves.
        if (string.IsNullOrEmpty(frame.UserId))
        {
            DiagnosticLog.Write("PeerMesh.OnPeerAnnounce: dropping frame with empty user_id");
            return;
        }
        if (frame.UserId == _ownUserId)
        {
            DiagnosticLog.Write($"PeerMesh.OnPeerAnnounce: ignoring own announce ({frame.UserId})");
            return;
        }

        OnMemberJoined(frame.UserId, frame.Login);

        var eps = new List<IPEndPoint>(frame.Endpoints.Count);
        foreach (var e in frame.Endpoints)
        {
            if (string.IsNullOrEmpty(e.Ip) || e.Port <= 0 || e.Port > 65535)
            {
                DiagnosticLog.Write($"PeerMesh.OnPeerAnnounce: skipping invalid endpoint ip='{e.Ip}' port={e.Port} kind={e.Kind}");
                continue;
            }
            if (!IPAddress.TryParse(e.Ip, out var addr))
            {
                DiagnosticLog.Write($"PeerMesh.OnPeerAnnounce: ip not parseable '{e.Ip}' (kind={e.Kind})");
                continue;
            }
            eps.Add(new IPEndPoint(addr, e.Port));
        }

        DiagnosticLog.Write(
            $"PeerMesh.OnPeerAnnounce: peer={frame.UserId} login={frame.Login} " +
            $"received {eps.Count} endpoint(s): [{string.Join(", ", eps)}]");

        if (_peers.TryGetValue(frame.UserId, out var ch))
        {
            ch.OnPeerAnnounce(eps);
            DiagnosticLog.Write($"PeerMesh.OnPeerAnnounce: handed candidates to PeerChannel for {frame.UserId} (state={ch.State})");
        }
        else
        {
            DiagnosticLog.Write($"PeerMesh.OnPeerAnnounce: no PeerChannel found for user '{frame.UserId}' — OnMemberJoined should have created it. Possible race.");
        }
    }

    /// <summary>
    /// Generic relay hook. Reserved for future hole-punching
    /// handshakes (e.g. ICE candidate priorities, TURN allocation
    /// requests). The current channel does all its work via UDP and
    /// the peer_announce broadcast — relays are unused for now.
    /// </summary>
    public void OnPeerRelayFromWs(WsPeerRelay _) { /* reserved */ }

    /// <summary>
    /// Counter of UDP packets received whose source IP+port didn't
    /// match any peer's candidate list. Useful for distinguishing
    /// "firewall blocking all inbound" (counter stays 0) from
    /// "packets arrive but get classified wrong" (counter grows).
    /// </summary>
    private long _unknownSourceCount;

    // ---------- loops ----------------------------------------------------

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var socket = _socket;
        if (socket == null) return;
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(ct);
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"PeerMesh.Receive: {ex.Message}");
                await Task.Delay(100, ct).ConfigureAwait(false);
                continue;
            }

            var src = result.RemoteEndPoint;
            var data = result.Buffer;

            // Route to the channel whose candidate list contains src.
            // O(N) over peers, but N is small (≤ 8 for AoE3 rooms).
            PeerChannel? owner = null;
            foreach (var ch in _peers.Values)
            {
                if (!ch.Matches(src)) continue;
                ch.OnPacket(src, data);
                owner = ch;
                break;
            }
            if (owner == null)
            {
                // Diagnostic: count "unknown source" packets so we can
                // tell from the log whether (a) the inbound is being
                // blocked by Windows Firewall (count stays 0) or (b)
                // packets ARE arriving but from an IP that's not in
                // any peer's candidate list (count climbs but state
                // stays Punching). The first ~5 are dumped in full.
                System.Threading.Interlocked.Increment(ref _unknownSourceCount);
                if (_unknownSourceCount <= 5)
                {
                    DiagnosticLog.Write(
                        $"PeerMesh.Receive: dropped packet from unknown source " +
                        $"{src} ({data.Length} bytes). " +
                        $"Known peers: {string.Join(", ", _peers.Keys)}");
                }
                continue;   // unknown source — drop
            }
            // First packet from a peer is worth logging — it's the
            // moment the hole-punch actually succeeded.
            if (!owner.LoggedFirstInbound)
            {
                owner.LoggedFirstInbound = true;
                DiagnosticLog.Write(
                    $"PeerMesh.Receive: FIRST inbound from peer {owner.UserId} ({owner.Login}) " +
                    $"at {src} ({data.Length} bytes) — hole-punch succeeded.");
            }

            // Ping/pong: respond to pings, update RTT on pongs. Both
            // halves of the dance are tiny — no need to dispatch
            // through the tick loop.
            if (PeerChannel.IsPingFrame(data, data.Length))
            {
                if (data.Length >= 5 && data[4] == 0x00)
                {
                    var pong = owner.OnPingReceived(data, data.Length);
                    if (pong != null && owner.ConfirmedEndpoint != null)
                    {
                        try { await socket.SendAsync(pong, pong.Length, owner.ConfirmedEndpoint); }
                        catch (ObjectDisposedException) { return; }
                        catch (Exception ex)
                        {
                            DiagnosticLog.Write($"PeerMesh.Pong → {owner.ConfirmedEndpoint}: {ex.Message}");
                        }
                    }
                }
                else
                {
                    owner.OnPongReceived(data, data.Length);
                    // Surface the updated RTT via PeerStateChanged so
                    // the UI can repaint the quality column. We don't
                    // change State here, but listeners only need the
                    // notification.
                    try { PeerStateChanged?.Invoke(this, owner); } catch { /* UI errors */ }
                }
                continue;
            }

            // Game-data frame? Decode and surface to the hook-IPC
            // bridge (via the GamePacketReceived subscriber). Hole-
            // punch probes already updated state above and need no
            // further routing.
            if (PeerChannel.IsGameFrame(data, data.Length))
            {
                if (PeerChannel.TryParseGameFrame(data, data.Length,
                        out var srcPort, out var dstPort, out var srcIp, out var dstIp, out var payload))
                {
                    // Diagnostic (Phase 2.c bring-up): trace every
                    // game frame we successfully decode + dispatch.
                    // Pairs with AoeP2pBridgeService's TX/RX traces
                    // to localise where the chain breaks.
                    DiagnosticLog.Write(
                        $"PeerMesh.RX GamePacket from={owner.UserId} src={srcIp}:{srcPort} " +
                        $"dst={dstIp}:{dstPort} len={payload.Length}");
                    try
                    {
                        GamePacketReceived?.Invoke(this,
                            (owner.UserId, new GamePacket(srcPort, dstPort, srcIp, dstIp, payload)));
                    }
                    catch (Exception ex)
                    {
                        DiagnosticLog.Write($"PeerMesh.GamePacketReceived handler: {ex.Message}");
                    }
                }
                else
                {
                    DiagnosticLog.Write(
                        $"PeerMesh.RX dropped malformed game frame from {owner.UserId} " +
                        $"(len={data.Length}).");
                }
            }
        }
    }

    /// <summary>
    /// Forward a captured AoE3 game packet to every peer. Connected
    /// peers get a direct UDP send; peers in <see cref="PeerLinkState.Failed"/>
    /// are returned in <paramref name="needsRelay"/> so the caller can
    /// route them through the lobby WS (Worker-as-TURN). Peers still
    /// hole-punching are silently skipped — they'll come online soon.
    /// </summary>
    public async Task BroadcastGamePacketAsync(
        GamePacket packet,
        List<string>? needsRelay = null,
        CancellationToken ct = default)
    {
        var socket = _socket;
        if (socket == null) return;
        var frame = PeerChannel.BuildGameFrame(packet.SrcPort, packet.DstPort, packet.SrcIp, packet.DstIp, packet.Payload);
        int sent = 0, skipPunching = 0, skipFailed = 0;
        foreach (var ch in _peers.Values)
        {
            if (ch.State == PeerLinkState.Failed)
            {
                needsRelay?.Add(ch.UserId);
                ++skipFailed;
                continue;
            }
            if (ch.State != PeerLinkState.Connected || ch.ConfirmedEndpoint == null)
            {
                ++skipPunching;
                continue;
            }
            try
            {
                await socket.SendAsync(frame, frame.Length, ch.ConfirmedEndpoint);
                ++sent;
            }
            catch (ObjectDisposedException) { return; }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"PeerMesh.BroadcastGamePacket → {ch.UserId}: {ex.Message}");
            }
        }
        // Diagnostic (Phase 2.c bring-up): one line per outbound game
        // frame so we can confirm the bridge actually invoked us. Pairs
        // with AoeP2pBridgeService's RX PACKET_OUT log on the same side
        // and the RX GamePacket log on the receiving side.
        DiagnosticLog.Write(
            $"PeerMesh.TX GamePacket dst={packet.DstIp}:{packet.DstPort} len={packet.Payload?.Length ?? 0} " +
            $"peers={_peers.Count} sent={sent} relay={skipFailed} punching={skipPunching}");
    }

    private async Task TickLoopAsync(CancellationToken ct)
    {
        // 100 ms tick keeps the punch interval (250 ms) responsive
        // without busy-spinning. Sleep is cheap on a Task.Delay.
        while (!ct.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var socket = _socket;
            if (socket == null) return;

            foreach (var ch in _peers.Values)
            {
                var sends = ch.Tick(now);
                foreach (var (dest, payload) in sends)
                {
                    try { await socket.SendAsync(payload, payload.Length, dest); }
                    catch (ObjectDisposedException) { return; }
                    catch (Exception ex)
                    {
                        // Send failures on transient ICMP unreachable
                        // are normal during hole-punch — log and move
                        // on, the next probe will retry.
                        DiagnosticLog.Write($"PeerMesh.Send → {dest}: {ex.Message}");
                    }
                }
            }

            try { await Task.Delay(100, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>
    /// Walk local NICs and return non-loopback IPv4 addresses that
    /// are *plausibly routable from another peer on the same LAN*.
    ///
    /// We deliberately filter out:
    ///   * <c>169.254.0.0/16</c> — APIPA link-local. Only valid on
    ///     the exact same physical link; never routable peer-to-peer.
    ///     A laptop with an unplugged Ethernet or a disabled WiFi
    ///     adapter often has 4-5 of these, each useless to anyone
    ///     trying to reach us.
    ///   * <c>10.147.0.0/16</c> — historical ZeroTier / WinDivert
    ///     virtual-LAN range. The bridge that produced these aliases
    ///     is gone, but the filter stays defensive in case a user
    ///     still has the orphaned NIC alias from an older build.
    ///   * Down interfaces — operational status must be Up.
    ///   * Tunnel / loopback / virtual adapters declared as such by
    ///     Windows.
    ///
    /// What stays: regular Wi-Fi / Ethernet RFC1918 ranges
    /// (10.0.0.0/8 outside our virtual range, 172.16.0.0/12,
    /// 192.168.0.0/16). Those are the addresses other devices on
    /// the same router can actually reach.
    /// </summary>
    private static IEnumerable<IPAddress> EnumerateLanAddresses()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
            foreach (var ip in ni.GetIPProperties().UnicastAddresses)
            {
                if (ip.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                if (IPAddress.IsLoopback(ip.Address)) continue;
                var bytes = ip.Address.GetAddressBytes();
                // APIPA link-local — never useful peer-to-peer.
                if (bytes[0] == 169 && bytes[1] == 254) continue;
                // The old WinDivert / Wintun virtual-LAN range.
                // Bridge is gone, but a leftover NIC alias from an
                // older build would still resolve here and waste
                // 15 seconds of hole-punch on a dead route.
                if (bytes[0] == 10 && bytes[1] == 147) continue;
                yield return ip.Address;
            }
        }
    }

    /// <summary>
    /// Best-effort Windows Firewall rule for the launcher's own
    /// process. P2P hole-punching needs INBOUND UDP allowed on the
    /// ephemeral port the launcher binds. By default Windows blocks
    /// unsolicited inbound for any private/public profile unless
    /// the user clicked "Allow access" on the first-launch prompt
    /// — which they almost never see because the launcher runs
    /// elevated and the prompt only appears for the desktop session.
    ///
    /// We add a permissive rule for the launcher's .exe path on
    /// startup. Idempotent — netsh silently no-ops if the rule
    /// already exists. Requires admin (the launcher manifest
    /// already declares requireAdministrator). Failures are
    /// logged but non-fatal: P2P may still work if the user has
    /// already granted access via the OS prompt.
    /// </summary>
    private static void EnsureFirewallRule()
    {
        try
        {
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath))
            {
                DiagnosticLog.Write("PeerMesh: cannot resolve own exe path; skipping firewall rule.");
                return;
            }

            // Rule name carries the path so multiple side-by-side
            // installs each get their own rule. Repeated runs of
            // the same install are no-ops because the rule
            // signature (name + program path) matches.
            const string ruleName = "Aoe3ModLauncher P2P (UDP inbound)";
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"advfirewall firewall add rule " +
                    $"name=\"{ruleName}\" dir=in action=allow " +
                    $"protocol=UDP program=\"{exePath}\" " +
                    $"enable=yes profile=any",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc != null)
            {
                proc.WaitForExit(5000);
                DiagnosticLog.Write(
                    $"PeerMesh: firewall rule '{ruleName}' for '{exePath}' " +
                    $"ensured (netsh exit={proc.ExitCode}).");
            }
        }
        catch (Exception ex)
        {
            // Most common cause: launcher not elevated (manifest
            // says requireAdministrator but a sideload or dev build
            // can bypass that). P2P over LAN still works *if* the
            // user granted access via the Windows firewall prompt
            // at first launch; otherwise it silently fails.
            DiagnosticLog.Write($"PeerMesh: could not ensure firewall rule: {ex.Message}");
        }
    }
}
