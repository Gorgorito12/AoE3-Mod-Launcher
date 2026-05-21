using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Services.Multiplayer.P2P;

namespace WarsOfLibertyLauncher.Services.Multiplayer.NativeHook;

/// <summary>
/// TCP-on-loopback IPC server that talks to <c>AoeP2pHook.dll</c> after
/// the hook has been injected into <c>age3y.exe</c>. This is the
/// launcher side of the Fase 2 "LAN ↔ mesh" bridge.
///
/// Architecture:
///   * The .NET launcher is x64; age3y.exe (and the hook DLL loaded
///     inside it) is x86. They live in different process contexts.
///   * A TCP socket on 127.0.0.1 is the cheapest cross-bitness IPC on
///     Windows that gives us bidirectional, byte-stream, low-latency
///     transport without overlapped-pipe footguns. WinSock's
///     synchronous <c>recv</c> / <c>send</c> on a connected socket
///     never produce the read-after-write freeze we kept hitting on
///     <c>NamedPipeServerStream(PipeOptions.Asynchronous)</c>.
///   * The launcher opens the listener BEFORE spawning age3y.exe, then
///     passes the chosen ephemeral port into the spawned process via
///     the <c>AOE_P2P_HOOK_PORT</c> environment variable. The hook DLL
///     reads that env var inside DllMain and connect()'s back.
///
/// Phase 2.d (this commit): transport migration only. Frame format,
/// the writer-thread + bounded queue on the hook side, the warmup
/// buffer, ephemeral socket auto-tracking, and the <c>Hooked_*</c>
/// shapes are all unchanged.
///
/// Lifecycle:
///   1. Caller does <see cref="CreateAndStart"/> right before
///      launching age3y.exe. The bridge becomes the
///      <see cref="Current"/> singleton.
///   2. <see cref="AoeP2pHookInjector"/> reads
///      <see cref="Current"/>.<see cref="Port"/> and passes it through
///      to age3y.exe as an env var.
///   3. The hook connects, sends a HELLO frame, server logs and
///      surfaces it through <see cref="HookConnected"/>.
///   4. Caller calls <see cref="DisposeAsync"/> when the game exits
///      (typically wired to <c>Process.Exited</c>). Socket closes, the
///      singleton clears.
///
/// Singleton justification: the launcher only ever has one active
/// multiplayer game running at a time (it spawns inside a single
/// in-game phase managed by <c>MultiplayerSession</c>). Threading a
/// bridge instance through three layers of callback signatures
/// (MultiplayerTab → MainWindow.LaunchGame callback → GameLauncher
/// → AoeP2pHookInjector) would touch a lot of code without buying
/// anything. The static <see cref="Current"/> property is read once,
/// at the moment of process spawn, and is cleared on disposal.
/// </summary>
public sealed class AoeP2pBridgeService : IAsyncDisposable
{
    // ---- protocol constants (must match dllmain.cpp) ----------------

    /// <summary>Wire-format version. Bumped on breaking changes.</summary>
    private const byte ProtocolVersion = 1;

    /// <summary>Frame kind: hook → launcher, "I'm attached".</summary>
    internal const byte FrameKindHello = 1;

    /// <summary>Frame kind: hook → launcher, captured outbound packet.</summary>
    internal const byte FrameKindPacketOut = 2;

    /// <summary>Frame kind: launcher → hook, injected inbound packet.</summary>
    internal const byte FrameKindPacketIn = 3;

    /// <summary>
    /// Frame kind: launcher → hook, refreshed set of peer virtual IPs.
    /// Payload = N × little-endian uint32 (each = one virtual IPv4 in
    /// wire / LE form). The hook replaces its in-memory peer set with
    /// these values; the addresses dictate which sendto destinations
    /// get diverted into the bridge (broadcast is always diverted).
    /// </summary>
    internal const byte FrameKindPeerSet = 4;

    /// <summary>
    /// Fixed-size header that precedes every frame on the wire. Kept
    /// small and field-aligned so the C++ side can blit it straight
    /// out of a struct without bit fiddling.
    ///
    /// Layout (little-endian, 16 bytes):
    ///   uint8_t  kind        // see FrameKind* constants
    ///   uint8_t  version     // ProtocolVersion; rejects mismatched builds
    ///   uint16_t payloadLen  // bytes that follow this header (0 for HELLO)
    ///   uint32_t srcIp       // IPv4 as little-endian uint32 (0 for HELLO)
    ///   uint32_t dstIp       // IPv4 as little-endian uint32 (HELLO=hook PID)
    ///   uint16_t srcPort     // 0 for HELLO
    ///   uint16_t dstPort     // 0 for HELLO
    /// </summary>
    private const int HeaderBytes = 16;

    // ---- singleton --------------------------------------------------

    private static AoeP2pBridgeService? _current;

    /// <summary>
    /// The bridge that's currently active for the launcher's
    /// "in-game" phase, or <c>null</c> when no game is running.
    /// Set by <see cref="CreateAndStart"/>, cleared by
    /// <see cref="DisposeAsync"/>.
    /// </summary>
    public static AoeP2pBridgeService? Current => Volatile.Read(ref _current);

    // ---- public surface --------------------------------------------

    /// <summary>
    /// Ephemeral TCP port the listener is bound to on 127.0.0.1.
    /// Passed to the spawned <c>age3y.exe</c> via the
    /// <c>AOE_P2P_HOOK_PORT</c> environment variable so the hook DLL
    /// can <c>connect()</c> back. The OS picks the port for us
    /// (<c>TcpListener(IPAddress.Loopback, 0)</c>) — never collides
    /// across concurrent launcher processes.
    /// </summary>
    public int Port { get; private set; }

    /// <summary>
    /// Fires once after the hook DLL sends its HELLO frame. The argument
    /// is the PID of <c>age3y.exe</c> as observed from inside the
    /// hooked process — handy for cross-referencing with
    /// <c>%LOCALAPPDATA%\AoeP2pHook.log</c>.
    /// </summary>
    public event EventHandler<int>? HookConnected;

    /// <summary>
    /// Fires every time the hook captured an outbound AoE3 datagram
    /// (sendto destined for a peer or broadcast) and forwarded it to
    /// the launcher. Subscribers typically forward the packet into
    /// <see cref="PeerMesh.BroadcastGamePacketAsync"/> so it reaches
    /// every connected peer. The handler runs on the socket reader
    /// thread — keep it fast or marshal to another scheduler.
    /// </summary>
    public event EventHandler<GamePacket>? PacketCapturedFromGame;

    // ---- internals --------------------------------------------------

    private readonly CancellationTokenSource _cts = new();
    private TcpListener? _listener;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private Task? _runLoop;
    private int _disposed;

    // ---- Phase 2.f: local-IP → virtual-IP rewriter -----------------
    //
    // The problem: AoE3's LAN lobby response (1669-byte payload) carries
    // the host's *real* LAN address baked into the payload itself
    // (not just in the IP header). When papillo's AoE3 reads the
    // response we injected, it sees:
    //   - source IP   = 10.147.250.205 (gorgorito's virtual mesh IP,
    //                                   set by our PACKET_IN frame)
    //   - host IP     = 192.168.68.67  (gorgorito's REAL LAN address,
    //                                   embedded in the bytes by AoE3)
    // Source ≠ embedded — AoE3 silently ignores the lobby because the
    // host isn't "reachable on this subnet". Even if the lobby DID
    // show, clicking Join would dial 192.168.68.67 directly (an IP
    // papillo cannot reach from his mobile network), so the connect
    // would never make it through the mesh diversion.
    //
    // Fix: before forwarding any outbound PACKET_OUT to the mesh,
    // sweep the payload for any 4-byte sequence matching one of our
    // own local NIC addresses and overwrite it with our virtual IP.
    // The receiver then sees its peer's virtual IP both as source AND
    // as the embedded host address — the lobby validates and any
    // unicast follow-up the joiner sends to the host IP gets diverted
    // by the hook because that virtual IP is in g_peerIps.
    //
    // We scan in BOTH byte orders because we don't know how AoE3
    // serialises IPs internally — some games use network order (the
    // wire format), others use host order (a uint32 on x86). Trying
    // both is cheap and avoids guesswork.
    private readonly object _rewriteLock = new();
    private byte[] _rewriteTo = Array.Empty<byte>();           // 4 bytes, network order
    private readonly List<byte[]> _rewritePatterns = new();    // each 4 bytes, network order
    private readonly List<byte[]> _rewritePatternsLe = new();  // same IPs in LE byte order

    /// <summary>
    /// Serialises writes to <see cref="_stream"/>. The hook can read a
    /// frame at any time, and the launcher may write a PEER_SET, a
    /// PACKET_IN, or any future frame kind from arbitrary threads
    /// (UI dispatcher, mesh receive loop, peer-state callbacks). A
    /// dedicated lock prevents two header + payload writes from
    /// interleaving on the wire — which would desync the hook's
    /// framer and force it to drop the socket.
    /// </summary>
    private readonly object _writeLock = new();

    private AoeP2pBridgeService()
    {
    }

    /// <summary>
    /// Create the bridge, start the TCP listener, and publish it as
    /// the <see cref="Current"/> singleton. Must be called BEFORE
    /// launching age3y.exe so the listener is already accepting when
    /// the hook DLL tries to connect.
    /// </summary>
    public static AoeP2pBridgeService CreateAndStart()
    {
        var bridge = new AoeP2pBridgeService();
        var prev = Interlocked.Exchange(ref _current, bridge);
        if (prev != null)
        {
            // The launcher's UI flow normally tears down the previous
            // bridge on game exit, but a crashed game or a launcher
            // bug could leave a stale singleton. Dispose it
            // defensively so we don't leak a socket handle.
            DiagnosticLog.Write(
                "AoeP2pBridgeService: previous bridge was still set when " +
                "CreateAndStart was called — disposing the stale instance.");
            _ = prev.DisposeAsync();
        }
        bridge.Start();
        return bridge;
    }

    private void Start()
    {
        // Bind to 127.0.0.1:0 → kernel picks an ephemeral port. Going
        // through loopback (rather than 0.0.0.0 + firewall handwaving)
        // means there's nothing for Windows Firewall to prompt on, no
        // accidental exposure to the LAN, and the connect from the
        // injected hook never leaves the box.
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

        DiagnosticLog.Write(
            $"AoeP2pBridgeService: TCP listener bound to 127.0.0.1:{Port}; " +
            "waiting for AoeP2pHook.dll to connect.");

        _runLoop = Task.Run(() => RunAsync(_cts.Token));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var listener = _listener ?? throw new InvalidOperationException("listener not initialised");
        try
        {
            // AcceptTcpClientAsync returns the first inbound connection.
            // The hook is the only thing that should ever dial 127.0.0.1
            // on our ephemeral port, so we don't bother accepting more
            // than one client. If the game never starts or the hook
            // fails to load, ct will eventually cancel.
            _client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            // Tear down the listener as soon as we have our one client —
            // a stray accept later in the lifetime would just confuse
            // the run loop.
            try { listener.Stop(); } catch { /* best-effort */ }

            // No Nagle: AoE3 LAN frames are small (typically < 200 B)
            // and we want them on the wire immediately. The hook side
            // does one send() per frame, so coalescing would just
            // add latency without saving syscalls.
            _client.NoDelay = true;
            _stream = _client.GetStream();
            DiagnosticLog.Write("AoeP2pBridgeService: hook DLL connected.");

            // Frame read loop. Phase 2.a only handled HELLO; we now
            // also dispatch PACKET_OUT (Phase 2.b).
            var header = new byte[HeaderBytes];
            while (!ct.IsCancellationRequested)
            {
                if (!await ReadExactAsync(_stream, header, ct).ConfigureAwait(false))
                    break;

                var kind        = header[0];
                var version     = header[1];
                var payloadLen  = (ushort)(header[2] | (header[3] << 8));
                var srcIp       = ReadLE32(header, 4);
                var dstIp       = ReadLE32(header, 8);
                var srcPort     = (ushort)(header[12] | (header[13] << 8));
                var dstPort     = (ushort)(header[14] | (header[15] << 8));

                if (version != ProtocolVersion)
                {
                    DiagnosticLog.Write(
                        $"AoeP2pBridgeService: protocol mismatch — hook sent " +
                        $"version={version}, launcher expects {ProtocolVersion}. " +
                        "Closing socket; rebuild the native hook DLL.");
                    break;
                }

                byte[]? payload = null;
                if (payloadLen > 0)
                {
                    payload = new byte[payloadLen];
                    if (!await ReadExactAsync(_stream, payload, ct).ConfigureAwait(false))
                        break;
                }

                switch (kind)
                {
                    case FrameKindHello:
                        // Phase 2.a: log + raise event. The HELLO frame
                        // carries the hook's PID in dstIp so operators
                        // can cross-check the running game against the
                        // hook's local log file. The (optional) payload
                        // is the absolute path of that local log file
                        // as UTF-8 — handy when the user can't find
                        // AoeP2pHook.log on their machine because
                        // LOCALAPPDATA resolved somewhere weird (UAC
                        // elevation as another user, OneDrive redirect,
                        // AV quarantine, etc.).
                        var hookPid = (int)dstIp;
                        var hookLogPath = (payload != null && payload.Length > 0)
                            ? Encoding.UTF8.GetString(payload)
                            : "(hook reported no writable log path)";
                        DiagnosticLog.Write(
                            $"AoeP2pBridgeService: HELLO from hook " +
                            $"(age3y PID={hookPid}, hook log file: '{hookLogPath}').");
                        try { HookConnected?.Invoke(this, hookPid); }
                        catch (Exception ex)
                        {
                            DiagnosticLog.Write(
                                $"AoeP2pBridgeService.HookConnected handler: {ex.Message}");
                        }
                        break;

                    case FrameKindPacketOut:
                        // Phase 2.b: hook captured an AoE3 sendto whose
                        // destination matched a peer (or broadcast).
                        // Hand it to subscribers (typically PeerMesh)
                        // for fan-out to the right peer channels. The
                        // hook already swallowed the original sendto,
                        // so the only way this packet reaches anyone
                        // is through us.
                        var capturedPayload = payload ?? Array.Empty<byte>();
                        try
                        {
                            // IPAddress(byte[]) expects bytes in
                            // network order = dotted-quad left-to-right.
                            // The wire form is the same little-endian
                            // uint32 the C++ side reads from sockaddr_in
                            // on x86: byte 0 = first dotted octet.
                            var srcIpAddr = new IPAddress(LeUint32ToBytes(srcIp));
                            var dstIpAddr = new IPAddress(LeUint32ToBytes(dstIp));

                            // Phase 2.f: BEFORE the diagnostic dump and
                            // BEFORE handing the packet to subscribers,
                            // rewrite any embedded local LAN IP to our
                            // virtual mesh IP. The peer will see "host
                            // is at our virtual IP" both in the source
                            // address AND in the AoE3-encoded payload,
                            // so the LAN browser accepts the lobby and
                            // any follow-up unicast lands back on our
                            // hook via the mesh divert.
                            int rewrites = RewriteOutboundPayloadInPlace(capturedPayload);

                            // Diagnostic (Phase 2.c bring-up): trace
                            // every PACKET_OUT we ferry from the hook
                            // so we can correlate against the writer
                            // thread's "FWD sendto" log on the C++ side
                            // and against the mesh-side forward log
                            // hooked up in MultiplayerTab.
                            DiagnosticLog.Write(
                                $"AoeP2pBridgeService: RX PACKET_OUT src={srcIpAddr}:{srcPort} " +
                                $"dst={dstIpAddr}:{dstPort} len={capturedPayload.Length}" +
                                (rewrites > 0 ? $" rewroteLocalIp×{rewrites}" : ""));

                            // Phase 2.f: hex-dump the first chunk of
                            // anything bigger than a discovery probe
                            // (>32 B). The 21-byte probes are repetitive
                            // and skipping them keeps the log focused
                            // on the interesting payloads — the 1669-
                            // byte lobby info, join replies, in-game
                            // state, etc.
                            if (capturedPayload.Length > 32)
                                DumpPayloadHex("AoeP2pBridgeService: PACKET_OUT", capturedPayload);

                            var gp = new GamePacket(
                                SrcPort: srcPort,
                                DstPort: dstPort,
                                SrcIp: srcIpAddr,
                                DstIp: dstIpAddr,
                                Payload: capturedPayload);
                            PacketCapturedFromGame?.Invoke(this, gp);
                        }
                        catch (Exception ex)
                        {
                            DiagnosticLog.Write(
                                $"AoeP2pBridgeService.PacketCapturedFromGame handler: {ex.Message}");
                        }
                        break;

                    default:
                        DiagnosticLog.Write(
                            $"AoeP2pBridgeService: unknown frame kind={kind} " +
                            $"(payloadLen={payloadLen}); ignored.");
                        break;
                }
            }
        }
        catch (OperationCanceledException) { /* expected on Dispose */ }
        catch (IOException ex)
        {
            // Connection closed is normal when age3y.exe exits — log at
            // info level rather than as a scary stack trace.
            DiagnosticLog.Write(
                $"AoeP2pBridgeService: socket closed ({ex.GetType().Name}: {ex.Message}).");
        }
        catch (SocketException ex)
        {
            DiagnosticLog.Write(
                $"AoeP2pBridgeService: socket error (WSA {ex.SocketErrorCode}): {ex.Message}");
        }
        catch (ObjectDisposedException)
        {
            // Disposal raced with an in-flight read. Normal during teardown.
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write(
                $"AoeP2pBridgeService.RunAsync unexpected: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            DiagnosticLog.Write("AoeP2pBridgeService: run loop exited.");
        }
    }

    /// <summary>
    /// Read exactly <paramref name="buf"/>.Length bytes from the stream,
    /// blocking (asynchronously) until they all arrive. Returns false
    /// if the connection is closed mid-read — the run loop treats that
    /// as "we're done" and unwinds cleanly.
    /// </summary>
    private static async Task<bool> ReadExactAsync(
        NetworkStream stream, byte[] buf, CancellationToken ct)
    {
        int read = 0;
        while (read < buf.Length)
        {
            int n;
            try
            {
                n = await stream
                    .ReadAsync(buf.AsMemory(read, buf.Length - read), ct)
                    .ConfigureAwait(false);
            }
            catch (IOException)        { return false; }
            catch (SocketException)    { return false; }
            catch (ObjectDisposedException) { return false; }
            if (n == 0) return false; // EOF (peer closed cleanly)
            read += n;
        }
        return true;
    }

    private static uint ReadLE32(byte[] buf, int offset) =>
        (uint)(buf[offset]
            | (buf[offset + 1] << 8)
            | (buf[offset + 2] << 16)
            | (buf[offset + 3] << 24));

    private static void WriteLE16(byte[] buf, int offset, ushort value)
    {
        buf[offset]     = (byte)(value & 0xFF);
        buf[offset + 1] = (byte)((value >> 8) & 0xFF);
    }

    private static void WriteLE32(byte[] buf, int offset, uint value)
    {
        buf[offset]     = (byte)(value & 0xFF);
        buf[offset + 1] = (byte)((value >> 8) & 0xFF);
        buf[offset + 2] = (byte)((value >> 16) & 0xFF);
        buf[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    /// <summary>
    /// Convert an IPv4 stored as a little-endian uint32 (the wire form
    /// the bridge protocol uses) into the dotted-quad byte order
    /// <see cref="IPAddress(byte[])"/> wants. Bytes come out
    /// {firstOctet, secondOctet, thirdOctet, fourthOctet}.
    /// </summary>
    private static byte[] LeUint32ToBytes(uint le) => new byte[]
    {
        (byte)(le & 0xFF),
        (byte)((le >> 8) & 0xFF),
        (byte)((le >> 16) & 0xFF),
        (byte)((le >> 24) & 0xFF),
    };

    /// <summary>
    /// Convert a dotted-quad IPv4 (10.147.x.y) into the same little-
    /// endian uint32 wire form. Mirrors <see cref="LeUint32ToBytes"/>.
    /// </summary>
    private static uint IPv4ToLeUint32(IPAddress ip)
    {
        var b = ip.MapToIPv4().GetAddressBytes();
        return (uint)b[0]
             | ((uint)b[1] << 8)
             | ((uint)b[2] << 16)
             | ((uint)b[3] << 24);
    }

    /// <summary>
    /// Push a PACKET_IN frame to the hook so AoE3's next recvfrom on
    /// the matching bound socket returns this payload (as if a real
    /// LAN peer had sent it).
    ///
    /// Wire format: 16-byte header (kind=PacketIn, version=1) + raw
    /// payload bytes. SrcIp = the synthesised virtual IP we want
    /// AoE3 to see ("from this peer"); DstIp is what the sender
    /// originally targeted (broadcast vs unicast lets the hook pick
    /// the right local socket).
    /// </summary>
    public void InjectIntoGame(
        IPAddress srcVirtualIp,
        ushort srcPort,
        IPAddress dstIp,
        ushort dstPort,
        byte[] payload)
    {
        if (payload == null) throw new ArgumentNullException(nameof(payload));
        if (payload.Length > ushort.MaxValue)
        {
            // The header's payloadLen is a u16 — anything larger would
            // wrap. AoE3 LAN datagrams are well below MTU, so a frame
            // this big is almost certainly bogus; drop with a log.
            DiagnosticLog.Write(
                $"AoeP2pBridgeService.InjectIntoGame: payload {payload.Length} bytes " +
                "exceeds u16; dropping.");
            return;
        }
        // Diagnostic (Phase 2.c bring-up): trace every PACKET_IN we
        // try to push so we can correlate against the C++ side's
        // "INJECT recvfrom" log. Helps verify whether the chain
        // mesh→bridge→hook is alive when the user reports the LAN
        // list never populating.
        DiagnosticLog.Write(
            $"AoeP2pBridgeService: TX PACKET_IN src={srcVirtualIp}:{srcPort} " +
            $"dst={dstIp}:{dstPort} len={payload.Length}");

        // Phase 2.f: hex-dump big payloads. Lets us verify that the
        // sender's outbound rewrite actually replaced their real LAN
        // IP with the virtual one — same bytes should show up here
        // on the receiver side. Skip small discovery probes (≤32 B).
        if (payload.Length > 32)
            DumpPayloadHex("AoeP2pBridgeService: PACKET_IN", payload);

        WriteFrame(
            kind: FrameKindPacketIn,
            srcIp: IPv4ToLeUint32(srcVirtualIp),
            dstIp: IPv4ToLeUint32(dstIp),
            srcPort: srcPort,
            dstPort: dstPort,
            payload: payload);
    }

    /// <summary>
    /// Tell the hook the current set of peer virtual IPs. Sendtos
    /// targeting any of these (or the IPv4 broadcast address) get
    /// intercepted and forwarded to the launcher; everything else
    /// continues straight to Real_sendto.
    ///
    /// Resend whenever the set changes (peer joins / leaves a room,
    /// reconnects under a different mesh state). Idempotent on the
    /// hook side — it just swaps the in-memory set.
    /// </summary>
    public void SendPeerSet(IEnumerable<IPAddress> peerVirtualIps)
    {
        if (peerVirtualIps == null) throw new ArgumentNullException(nameof(peerVirtualIps));
        var list = new List<uint>();
        foreach (var ip in peerVirtualIps)
        {
            if (ip == null) continue;
            list.Add(IPv4ToLeUint32(ip));
        }
        // Each peer = 4 bytes (LE uint32). With AoE3 capped at 8
        // players an empty or single-element set is normal.
        if (list.Count * 4 > ushort.MaxValue)
        {
            DiagnosticLog.Write(
                $"AoeP2pBridgeService.SendPeerSet: {list.Count} peers would overflow " +
                "the u16 payload length; truncating to first 16384.");
            list = list.GetRange(0, 16384);
        }
        var payload = new byte[list.Count * 4];
        for (int i = 0; i < list.Count; i++)
            WriteLE32(payload, i * 4, list[i]);
        WriteFrame(
            kind: FrameKindPeerSet,
            srcIp: 0u,
            dstIp: 0u,
            srcPort: 0,
            dstPort: 0,
            payload: payload);
    }

    /// <summary>
    /// Configure the outbound IP rewriter (Phase 2.f). Should be called
    /// right after the bridge is created, BEFORE the hook reports its
    /// first PACKET_OUT. The rewriter is idempotent — calling it again
    /// with a different virtual IP just swaps the target; the local
    /// NIC list is re-enumerated each time so a network change between
    /// games refreshes the patterns.
    ///
    /// <paramref name="localVirtualIp"/> = the virtual mesh IP that
    /// represents us to peers (derived from our user id via
    /// <c>VirtualIpAllocator</c>). Any 4-byte sequence in an outbound
    /// payload that matches one of our local NIC IPv4 addresses (in
    /// either byte order) gets overwritten with this virtual IP so
    /// the receiver's AoE3 sees an "address that's reachable through
    /// the mesh hook" instead of our private LAN IP.
    ///
    /// Loopback (127.x.x.x), link-local autoconf (169.254.x.x), and
    /// addresses already inside the 10.147.0.0/16 virtual range are
    /// excluded from the search list: rewriting them would either be
    /// nonsensical (loopback) or break virtual-IP traffic we want to
    /// keep verbatim.
    /// </summary>
    public void SetLocalRewriteContext(IPAddress localVirtualIp)
    {
        if (localVirtualIp == null) throw new ArgumentNullException(nameof(localVirtualIp));
        var vipBytes = localVirtualIp.MapToIPv4().GetAddressBytes();
        if (vipBytes.Length != 4)
            throw new ArgumentException("Virtual IP must be IPv4.", nameof(localVirtualIp));

        var patterns = new List<byte[]>();
        var patternsLe = new List<byte[]>();
        try
        {
            // System.Net.NetworkInformation enumerates EVERY adapter
            // including down ones. Filter to "up + has IPv4" so we
            // don't waste cycles searching for IPs of disabled NICs
            // (a common false-positive source on machines with VPN
            // adapters parked in disconnected state).
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                var props = nic.GetIPProperties();
                foreach (var ua in props.UnicastAddresses)
                {
                    var ip = ua.Address;
                    if (ip.AddressFamily != AddressFamily.InterNetwork) continue;
                    var ipBytes = ip.GetAddressBytes();
                    if (ipBytes.Length != 4) continue;
                    // Skip loopback, link-local, and our own virtual range.
                    if (ipBytes[0] == 127) continue;
                    if (ipBytes[0] == 169 && ipBytes[1] == 254) continue;
                    if (ipBytes[0] == 10 && ipBytes[1] == 147) continue;
                    // Skip the rewrite target itself — replacing an
                    // IP with itself is a no-op but it'd show up in
                    // logs as "matched 192.168.x.y" which is confusing.
                    if (ipBytes[0] == vipBytes[0] && ipBytes[1] == vipBytes[1]
                     && ipBytes[2] == vipBytes[2] && ipBytes[3] == vipBytes[3]) continue;

                    // Store BOTH byte orders so the payload scan covers
                    // either layout AoE3 might use internally.
                    patterns.Add(new[] { ipBytes[0], ipBytes[1], ipBytes[2], ipBytes[3] });
                    patternsLe.Add(new[] { ipBytes[3], ipBytes[2], ipBytes[1], ipBytes[0] });
                }
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write(
                $"AoeP2pBridgeService.SetLocalRewriteContext: NIC enumeration failed: {ex.Message}");
            // Even on failure, install the new target IP so any later
            // call to AppendRewriteIp can still register patterns.
        }

        lock (_rewriteLock)
        {
            _rewriteTo = vipBytes;
            _rewritePatterns.Clear();
            _rewritePatternsLe.Clear();
            _rewritePatterns.AddRange(patterns);
            _rewritePatternsLe.AddRange(patternsLe);
        }

        // Log what we registered so the user can sanity-check the
        // search set against `ipconfig /all` if rewrites aren't firing.
        var sb = new StringBuilder();
        sb.Append("AoeP2pBridgeService: outbound rewriter armed. ");
        sb.Append($"localVirtualIp={localVirtualIp}, ");
        sb.Append($"localIpPatterns=[");
        for (int i = 0; i < patterns.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append($"{patterns[i][0]}.{patterns[i][1]}.{patterns[i][2]}.{patterns[i][3]}");
        }
        sb.Append("]");
        DiagnosticLog.Write(sb.ToString());
    }

    /// <summary>
    /// In-place sweep over <paramref name="payload"/>: every 4-byte
    /// window matching one of the registered local-NIC patterns gets
    /// overwritten with the local virtual IP (same byte order as the
    /// match). Returns the count of replacements made so the caller
    /// can log when something interesting happens.
    ///
    /// Worst case is O(payload.Length × patterns.Count) which for a
    /// 1669-byte AoE3 lobby response with ~3 local IPs is well under
    /// 6000 byte comparisons — negligible compared to the cross-process
    /// IPC and mesh I/O around it. We keep the lock for the whole
    /// scan so a concurrent <see cref="SetLocalRewriteContext"/> can't
    /// pull patterns out from under us mid-sweep.
    /// </summary>
    private int RewriteOutboundPayloadInPlace(byte[] payload)
    {
        if (payload == null || payload.Length < 4) return 0;
        int replacements = 0;
        lock (_rewriteLock)
        {
            if (_rewriteTo.Length != 4) return 0;
            int patternCount = _rewritePatterns.Count;
            if (patternCount == 0) return 0;

            // Build LE / BE forms of the target ONCE per call so the
            // inner loop is a straight memcpy.
            byte[] toBe = _rewriteTo;
            byte[] toLe = new[] { _rewriteTo[3], _rewriteTo[2], _rewriteTo[1], _rewriteTo[0] };

            int end = payload.Length - 4;
            for (int i = 0; i <= end; i++)
            {
                for (int p = 0; p < patternCount; p++)
                {
                    var pat = _rewritePatterns[p];
                    if (payload[i] == pat[0] && payload[i+1] == pat[1]
                     && payload[i+2] == pat[2] && payload[i+3] == pat[3])
                    {
                        payload[i] = toBe[0]; payload[i+1] = toBe[1];
                        payload[i+2] = toBe[2]; payload[i+3] = toBe[3];
                        replacements++;
                        // Skip the 3 bytes we just wrote so we don't
                        // double-match overlapping windows.
                        i += 3;
                        goto next;
                    }
                    var patLe = _rewritePatternsLe[p];
                    if (payload[i] == patLe[0] && payload[i+1] == patLe[1]
                     && payload[i+2] == patLe[2] && payload[i+3] == patLe[3])
                    {
                        payload[i] = toLe[0]; payload[i+1] = toLe[1];
                        payload[i+2] = toLe[2]; payload[i+3] = toLe[3];
                        replacements++;
                        i += 3;
                        goto next;
                    }
                }
                next: ;
            }
        }
        return replacements;
    }

    /// <summary>
    /// Hex+ASCII dump of the first <paramref name="maxBytes"/> bytes
    /// of <paramref name="payload"/>, prefixed with <paramref name="tag"/>.
    /// Written to the launcher debug log so we can correlate what AoE3
    /// actually put on the wire against what the receiver sees post-
    /// inject. Capped tightly (default 80 bytes) — the embedded host
    /// IP and any session ids live in the header of the LAN lobby
    /// packet, so the first ~64 bytes are usually enough to diagnose.
    /// </summary>
    private static void DumpPayloadHex(string tag, byte[] payload, int maxBytes = 80)
    {
        if (payload == null || payload.Length == 0) return;
        int n = Math.Min(payload.Length, maxBytes);
        var hex = new StringBuilder(n * 3 + n + 16);
        var ascii = new StringBuilder(n + 4);
        for (int i = 0; i < n; i++)
        {
            hex.Append(payload[i].ToString("X2"));
            hex.Append(' ');
            byte b = payload[i];
            ascii.Append(b >= 0x20 && b <= 0x7E ? (char)b : '.');
        }
        DiagnosticLog.Write(
            $"{tag} hex(0..{n - 1}/{payload.Length})=[{hex.ToString().TrimEnd()}] ascii=[{ascii}]");
    }

    /// <summary>
    /// Build the 16-byte header and write it followed by the payload,
    /// holding <see cref="_writeLock"/> across both writes so a
    /// concurrent <see cref="InjectIntoGame"/> / <see cref="SendPeerSet"/>
    /// can't interleave bytes on the wire.
    /// </summary>
    private void WriteFrame(
        byte kind,
        uint srcIp,
        uint dstIp,
        ushort srcPort,
        ushort dstPort,
        byte[] payload)
    {
        var stream = _stream;
        var client = _client;
        if (stream == null || client == null || !client.Connected) return;

        var header = new byte[HeaderBytes];
        header[0] = kind;
        header[1] = ProtocolVersion;
        WriteLE16(header, 2, (ushort)payload.Length);
        WriteLE32(header, 4, srcIp);
        WriteLE32(header, 8, dstIp);
        WriteLE16(header, 12, srcPort);
        WriteLE16(header, 14, dstPort);

        // NetworkStream's synchronous Write blocks until the bytes are
        // in the kernel's TCP send buffer — no overlapped surprises,
        // no async state machine to keep coherent with the hook's
        // single-threaded reader. We still serialise across writers
        // ourselves so a second thread cannot splice its header
        // between our header and our payload.
        lock (_writeLock)
        {
            try
            {
                stream.Write(header, 0, header.Length);
                if (payload.Length > 0)
                    stream.Write(payload, 0, payload.Length);
            }
            catch (Exception ex)
            {
                // Most common: game exited mid-write, socket is broken.
                // Don't propagate — the dispose path will cancel the
                // run loop and the subscribers don't expect to handle
                // socket failures.
                DiagnosticLog.Write(
                    $"AoeP2pBridgeService.WriteFrame(kind={kind}): {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        // Clear the singleton first so a racing CreateAndStart from
        // the next game launch doesn't see us as still-current.
        Interlocked.CompareExchange(ref _current, null, this);

        try { _cts.Cancel(); } catch { /* already cancelled */ }

        // Order: stream → client → listener. Closing the stream unblocks
        // any in-flight ReadAsync on the run loop with EOF; closing the
        // client tears down the TCP connection so the hook's recv()
        // unblocks too; stopping the listener prevents any (unlikely)
        // second accept.
        try { _stream?.Dispose(); } catch (Exception ex)
        {
            DiagnosticLog.Write($"AoeP2pBridgeService.Dispose(stream): {ex.Message}");
        }
        try { _client?.Dispose(); } catch (Exception ex)
        {
            DiagnosticLog.Write($"AoeP2pBridgeService.Dispose(client): {ex.Message}");
        }
        try { _listener?.Stop(); } catch (Exception ex)
        {
            DiagnosticLog.Write($"AoeP2pBridgeService.Dispose(listener): {ex.Message}");
        }

        if (_runLoop != null)
        {
            try { await _runLoop.ConfigureAwait(false); }
            catch { /* surfaced inside RunAsync */ }
        }

        _cts.Dispose();
        DiagnosticLog.Write("AoeP2pBridgeService: disposed.");
    }
}
