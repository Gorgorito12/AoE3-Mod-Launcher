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
    /// <see cref="VirtualLanService"/> consumes these and injects them
    /// onto the local stack so AoE3 sees a LAN broadcast.
    /// </summary>
    public event EventHandler<(string FromUserId, GamePacket Packet)>? GamePacketReceived;

    /// <summary>Snapshot of all channels, for UI rendering.</summary>
    public IReadOnlyCollection<PeerChannel> Peers => _peers.Values.ToArray();

    /// <summary>Endpoints to announce over the lobby WS so peers know how to reach us.</summary>
    public IReadOnlyList<WsPeerEndpoint> LocalEndpoints { get; private set; } =
        Array.Empty<WsPeerEndpoint>();

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

        _peers.GetOrAdd(userId, _ =>
        {
            var ch = new PeerChannel(userId, login, _ownUserId);
            ch.StateChanged += (s, _) =>
                PeerStateChanged?.Invoke(this, (PeerChannel)s!);
            return ch;
        });
    }

    /// <summary>Drop a member's channel when they leave the room.</summary>
    public void OnMemberLeft(string userId)
    {
        if (_peers.TryRemove(userId, out var ch))
            PeerStateChanged?.Invoke(this, ch);    // last paint
    }

    /// <summary>
    /// Feed a <c>peer_announce</c> frame from the lobby WS. Parses
    /// the endpoints into <see cref="IPEndPoint"/>s and seeds the
    /// matching channel's candidate list, which starts hole-punching.
    /// </summary>
    public void OnPeerAnnounceFromWs(WsPeerAnnounce frame)
    {
        if (string.IsNullOrEmpty(frame.UserId) || frame.UserId == _ownUserId) return;
        OnMemberJoined(frame.UserId, frame.Login);

        var eps = new List<IPEndPoint>(frame.Endpoints.Count);
        foreach (var e in frame.Endpoints)
        {
            if (string.IsNullOrEmpty(e.Ip) || e.Port <= 0 || e.Port > 65535) continue;
            if (!IPAddress.TryParse(e.Ip, out var addr)) continue;
            eps.Add(new IPEndPoint(addr, e.Port));
        }

        if (_peers.TryGetValue(frame.UserId, out var ch))
            ch.OnPeerAnnounce(eps);
    }

    /// <summary>
    /// Generic relay hook. Reserved for future hole-punching
    /// handshakes (e.g. ICE candidate priorities, TURN allocation
    /// requests). The current channel does all its work via UDP and
    /// the peer_announce broadcast — relays are unused for now.
    /// </summary>
    public void OnPeerRelayFromWs(WsPeerRelay _) { /* reserved */ }

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
            if (owner == null) continue;   // unknown source — drop

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

            // Game-data frame? Decode and surface to VirtualLanService.
            // Hole-punch probes already updated state above and need
            // no further routing.
            if (PeerChannel.IsGameFrame(data, data.Length))
            {
                if (PeerChannel.TryParseGameFrame(data, data.Length,
                        out var srcPort, out var dstPort, out var payload))
                {
                    try
                    {
                        GamePacketReceived?.Invoke(this,
                            (owner.UserId, new GamePacket(srcPort, dstPort, payload)));
                    }
                    catch (Exception ex)
                    {
                        DiagnosticLog.Write($"PeerMesh.GamePacketReceived handler: {ex.Message}");
                    }
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
        var frame = PeerChannel.BuildGameFrame(packet.SrcPort, packet.DstPort, packet.Payload);
        foreach (var ch in _peers.Values)
        {
            if (ch.State == PeerLinkState.Failed)
            {
                needsRelay?.Add(ch.UserId);
                continue;
            }
            if (ch.State != PeerLinkState.Connected || ch.ConfirmedEndpoint == null)
                continue;
            try
            {
                await socket.SendAsync(frame, frame.Length, ch.ConfirmedEndpoint);
            }
            catch (ObjectDisposedException) { return; }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"PeerMesh.BroadcastGamePacket → {ch.UserId}: {ex.Message}");
            }
        }
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

    /// <summary>Walk local NICs and return non-loopback IPv4 addresses.</summary>
    private static IEnumerable<IPAddress> EnumerateLanAddresses()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var ip in ni.GetIPProperties().UnicastAddresses)
            {
                if (ip.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                if (IPAddress.IsLoopback(ip.Address)) continue;
                yield return ip.Address;
            }
        }
    }
}
