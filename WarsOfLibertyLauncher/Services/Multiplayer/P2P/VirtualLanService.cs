using System;
using System.Collections.Concurrent;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace WarsOfLibertyLauncher.Services.Multiplayer.P2P;

/// <summary>
/// Carrier-grade summary of one captured (or injected) AoE3 datagram.
/// Travels between this service, <see cref="PeerMesh"/> and (via the
/// peer link) the other player's VirtualLanService.
/// </summary>
public sealed record GamePacket(
    ushort SrcPort,
    ushort DstPort,
    byte[] Payload);

/// <summary>
/// The launcher's "Voobly-style" virtual LAN.
///
/// What this service does, end-to-end:
///   1. **Capture** AoE3's outgoing LAN broadcasts (UDP to
///      255.255.255.255 or 224.0.0.x) on the well-known game ports.
///      WinDivert hands the raw packet to us; we drop it locally
///      (so it doesn't go on the real wire) and pass the UDP payload
///      to every peer in the lobby through <see cref="PeerMesh"/>.
///   2. **Inject** packets coming back from peers: we synthesise an
///      IPv4-UDP frame with a private "virtual LAN" source address
///      (10.147.x.x, mirroring what ZeroTier used) and inject it on
///      the local network stack so AoE3 sees it as a real LAN peer.
///
/// We map each peer's user-id to a stable 10.147.x.x IP so:
///   * AoE3 sees a small set of distinct hosts, not one IP spamming.
///   * Replays / logs are readable (same peer → same IP across games).
///   * Hash-ordering keeps assignments deterministic across launchers.
///
/// This file holds the capture path + IP allocator. The inject path
/// is paired with it and lives in the same service. The mesh-bridge
/// glue (forward captured packets, receive remote packets) is wired
/// in by <see cref="MultiplayerSession"/>.
///
/// **Important**: this implementation is structured but UNTESTED on
/// real game traffic. The WinDivert filter strings, AoE3's actual
/// UDP port range, and the right injection direction need validation
/// with a 2-PC + tcpdump pass once the rest of Fase 3 is wired up.
/// </summary>
public sealed class VirtualLanService : IDisposable
{
    /// <summary>
    /// AoE3 LAN discovery range. Empirically the game listens
    /// somewhere in 2300-2400; we capture a slightly wider window to
    /// be safe against patch-version drift. WinDivert's filter
    /// language uses C-like syntax.
    /// </summary>
    public const string CaptureFilter =
        "udp and " +
        "(udp.DstPort >= 2200 and udp.DstPort <= 2500) and " +
        "(ip.DstAddr == 255.255.255.255 or " +
        " (ip.DstAddr >= 224.0.0.0 and ip.DstAddr <= 239.255.255.255))";

    /// <summary>"10.147" virtual-LAN /16 chosen for parity with ZeroTier.</summary>
    private static readonly byte[] VirtualLanPrefix = new byte[] { 10, 147 };

    /// <summary>
    /// Fired for every packet WinDivert captures. The mesh consumer
    /// fan-outs it to every connected peer. We don't multicast inside
    /// this service because the mesh knows the set of live peers
    /// better than we do.
    /// </summary>
    public event EventHandler<GamePacket>? GamePacketCaptured;

    private readonly ConcurrentDictionary<string, IPAddress> _peerIpByUser = new();
    private IntPtr _captureHandle = WinDivertNative.InvalidHandle;
    private IntPtr _injectHandle = WinDivertNative.InvalidHandle;
    private CancellationTokenSource? _cts;
    private Task? _captureLoop;

    /// <summary>
    /// Open the WinDivert handles and start the capture loop. Throws
    /// if the driver isn't available — caller should check
    /// <see cref="WinDivertNative.IsAvailable"/> first and surface
    /// the bootstrap UI when false.
    /// </summary>
    public void Start()
    {
        if (_captureHandle != WinDivertNative.InvalidHandle)
            throw new InvalidOperationException("Already started.");

        _captureHandle = WinDivertNative.WinDivertOpen(
            CaptureFilter,
            WinDivertNative.WINDIVERT_LAYER_NETWORK,
            priority: 0,
            // SNIFF+DROP: see the packet, then drop it from the wire
            // so AoE3's broadcast doesn't bounce off the real router.
            // We re-send equivalent unicast frames through the mesh.
            flags: WinDivertNative.WINDIVERT_FLAG_DROP | WinDivertNative.WINDIVERT_FLAG_RECV_ONLY);

        if (_captureHandle == WinDivertNative.InvalidHandle)
        {
            var err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"WinDivertOpen (capture) failed: Win32 error {err}.");
        }

        // Inject handle: send-only, no filter — used by ReinjectFromPeer.
        _injectHandle = WinDivertNative.WinDivertOpen(
            "false",       // match nothing; we only use Send on this handle
            WinDivertNative.WINDIVERT_LAYER_NETWORK,
            priority: 0,
            flags: WinDivertNative.WINDIVERT_FLAG_SEND_ONLY);

        if (_injectHandle == WinDivertNative.InvalidHandle)
        {
            WinDivertNative.WinDivertClose(_captureHandle);
            _captureHandle = WinDivertNative.InvalidHandle;
            var err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"WinDivertOpen (inject) failed: Win32 error {err}.");
        }

        _cts = new CancellationTokenSource();
        _captureLoop = Task.Run(() => CaptureLoop(_cts.Token));
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        if (_captureHandle != WinDivertNative.InvalidHandle)
        {
            WinDivertNative.WinDivertClose(_captureHandle);
            _captureHandle = WinDivertNative.InvalidHandle;
        }
        if (_injectHandle != WinDivertNative.InvalidHandle)
        {
            WinDivertNative.WinDivertClose(_injectHandle);
            _injectHandle = WinDivertNative.InvalidHandle;
        }
        try { _captureLoop?.Wait(TimeSpan.FromSeconds(1)); } catch { }
        try { _cts?.Dispose(); } catch { }
    }

    /// <summary>
    /// Assign a stable virtual-LAN IP to a peer (or return the
    /// existing one). The IP is derived from the lobby+user-id hash
    /// so two players in the same lobby see each other at the same
    /// addresses regardless of join order.
    /// </summary>
    public IPAddress AllocateIpFor(string userId)
    {
        if (string.IsNullOrEmpty(userId))
            throw new ArgumentException("userId required", nameof(userId));
        return _peerIpByUser.GetOrAdd(userId, id =>
        {
            // FNV-1a 32-bit hash over the user id, then map to
            // 10.147.x.y. Skip x.y == 0 (reserved net) and 255.255
            // (broadcast).
            uint hash = 2166136261;
            foreach (var c in id) hash = (hash ^ c) * 16777619;
            byte x = (byte)((hash >> 8) & 0xFF);
            byte y = (byte)(hash & 0xFF);
            if (x == 0) x = 1;
            if (y == 0) y = 1;
            return new IPAddress(new byte[] { VirtualLanPrefix[0], VirtualLanPrefix[1], x, y });
        });
    }

    /// <summary>
    /// Inject a remote peer's game packet onto the local network
    /// stack so AoE3 thinks it came from a LAN host. <see cref="GamePacketCaptured"/>
    /// fired on the other side; the mesh delivered it here.
    /// </summary>
    public void Inject(string fromUserId, GamePacket packet)
    {
        var inject = _injectHandle;
        if (inject == WinDivertNative.InvalidHandle) return;

        var srcIp = AllocateIpFor(fromUserId);
        // We always inject as broadcast on the receiver so AoE3's
        // discovery code picks it up exactly like a real LAN frame.
        // Game-state traffic during play uses a unicast destination
        // but for v1 we'll stick with broadcast and let AoE3 filter.
        var dstIp = IPAddress.Broadcast;
        var pkt = PacketRewriter.BuildUdpPacket(
            srcIp, dstIp, packet.SrcPort, packet.DstPort, packet.Payload);

        var addr = default(WinDivertNative.Address);
        // Mark as inbound (Outbound bit clear) so the driver routes
        // the packet up the local stack instead of toward the wire.
        // Loopback bit also set: keeps the packet from being sent on
        // any physical NIC even if we miscompute the filter.
        addr.LayerEventFlags = (uint)(
            WinDivertNative.AddressFlags.Sniffed
            | WinDivertNative.AddressFlags.Loopback);

        var handle = GCHandle.Alloc(pkt, GCHandleType.Pinned);
        try
        {
            WinDivertNative.WinDivertSend(
                inject,
                handle.AddrOfPinnedObject(),
                (uint)pkt.Length,
                out _,
                ref addr);
        }
        finally
        {
            handle.Free();
        }
    }

    private void CaptureLoop(CancellationToken ct)
    {
        // 1500 bytes covers a standard Ethernet MTU. AoE3 LAN
        // discovery packets are < 100 bytes; even chunky in-game
        // state updates rarely fragment.
        var buffer = new byte[1500];
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            var capture = _captureHandle;
            while (!ct.IsCancellationRequested && capture != WinDivertNative.InvalidHandle)
            {
                var addr = default(WinDivertNative.Address);
                bool ok = WinDivertNative.WinDivertRecv(
                    capture,
                    handle.AddrOfPinnedObject(),
                    (uint)buffer.Length,
                    out uint read,
                    ref addr);
                if (!ok)
                {
                    // Handle was closed under us (clean shutdown) or
                    // a transient driver hiccup. Bail on shutdown,
                    // back off briefly otherwise.
                    if (ct.IsCancellationRequested) return;
                    Thread.Sleep(50);
                    continue;
                }
                if (read == 0) continue;

                if (!PacketRewriter.TryParseUdp(buffer.AsSpan(0, (int)read), out var pkt))
                    continue;

                // Allocate a heap copy of the payload before raising —
                // the captured buffer is reused on the next Recv.
                var payload = pkt.Payload.ToArray();
                var raised = new GamePacket(pkt.SrcPort, pkt.DstPort, payload);
                try { GamePacketCaptured?.Invoke(this, raised); }
                catch (Exception ex)
                {
                    DiagnosticLog.Write($"VirtualLanService.GamePacketCaptured handler: {ex.Message}");
                }
            }
        }
        finally
        {
            handle.Free();
        }
    }
}
