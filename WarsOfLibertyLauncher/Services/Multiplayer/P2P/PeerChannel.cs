using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace WarsOfLibertyLauncher.Services.Multiplayer.P2P;

/// <summary>
/// State of our hole-punched UDP path to a single peer.
/// </summary>
public enum PeerLinkState
{
    /// <summary>Haven't seen the peer's announce yet.</summary>
    Discovering,
    /// <summary>Sending hole-punch probes; no reply yet.</summary>
    Punching,
    /// <summary>Received a packet from the peer — hole is open.</summary>
    Connected,
    /// <summary>Was connected, missed heartbeats. Punching again.</summary>
    Lost,
    /// <summary>Gave up after retries. Caller should fall back to TURN.</summary>
    Failed,
}

/// <summary>
/// Per-peer hole-punching state machine.
///
/// One instance lives for the duration of a peer's presence in our
/// lobby. The owning <see cref="PeerMesh"/> feeds it announce frames
/// (candidate addresses) and relays inbound UDP packets that came from
/// this peer's known endpoints. The channel returns frames the mesh
/// should write onto the shared socket.
///
/// Hole-punching strategy:
///   1. <see cref="OnPeerAnnounce"/> stores the peer's candidate
///      endpoints (public-from-STUN, LAN). Each one is treated as a
///      possible reachable address.
///   2. Every <see cref="PunchInterval"/> we send a tiny probe
///      ("hp1" magic + our user id) to every candidate.
///   3. As soon as we see ANY packet from the peer's address (even
///      their own probe), we flip to <see cref="PeerLinkState.Connected"/>
///      and start using that endpoint as the canonical destination.
///   4. We keep sending keep-alives every <see cref="KeepaliveInterval"/>.
///      If we miss <see cref="LostAfterMissedBeats"/> in a row, we
///      transition to <see cref="PeerLinkState.Lost"/> and restart
///      hole-punching from the candidate list.
///
/// The same single UDP socket is shared between all peers (owned by
/// <see cref="PeerMesh"/>). That's the only way the STUN-discovered
/// public port we announced matches the port other peers actually
/// try to reach us at.
/// </summary>
public sealed class PeerChannel
{
    /// <summary>Tag prefix for our hole-punch + keepalive datagrams so
    /// we can recognise them on receive. 4 ASCII bytes: "WOLp".</summary>
    public static readonly byte[] PunchMagic = new byte[] { 0x57, 0x4f, 0x4c, 0x70 };

    /// <summary>Tag prefix for AoE3 game-data datagrams the launcher
    /// is bridging on behalf of the local AoE3 process. 4 ASCII bytes:
    /// "WOLg". Frame format after the magic:
    ///   2 bytes  src game port (big-endian)
    ///   2 bytes  dst game port (big-endian)
    ///   4 bytes  original IPv4 source (network order) — the address
    ///            AoE3 announced itself on; the receiver injects with
    ///            this src so the peer's AoE3 sees the lobby at the
    ///            host's real IP and can later send a Join to it.
    ///   4 bytes  original IPv4 destination (network order) — lets the
    ///            receiver pick broadcast vs unicast on injection
    ///   N bytes  raw UDP payload from AoE3
    /// </summary>
    public static readonly byte[] GameMagic = new byte[] { 0x57, 0x4f, 0x4c, 0x67 };

    /// <summary>
    /// Tag prefix for keepalive ping/pong used to measure RTT. 4
    /// ASCII bytes: "WOLk". Frame format after the magic:
    ///   1 byte   kind (0x00 = ping, 0x01 = pong)
    ///   8 bytes  ping timestamp (unix ms big-endian — echoed back so
    ///            the originator can compute RTT without keeping state)
    /// </summary>
    public static readonly byte[] PingMagic = new byte[] { 0x57, 0x4f, 0x4c, 0x6b };

    /// <summary>How often to send an RTT ping after Connected.</summary>
    public static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(2);

    /// <summary>Time between hole-punch probes during Punching.</summary>
    public static readonly TimeSpan PunchInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>Time between keepalives once Connected.</summary>
    public static readonly TimeSpan KeepaliveInterval = TimeSpan.FromSeconds(5);

    /// <summary>Lose the connection after this many missed keepalives.</summary>
    public const int LostAfterMissedBeats = 3;

    /// <summary>Total punching duration before declaring Failed.</summary>
    public static readonly TimeSpan PunchTimeout = TimeSpan.FromSeconds(15);

    public string UserId { get; }
    public string Login { get; }
    public PeerLinkState State { get; private set; } = PeerLinkState.Discovering;

    /// <summary>
    /// Diagnostic flag set by <see cref="PeerMesh"/> the first time
    /// any packet from this peer is routed to the channel. Prevents
    /// the "FIRST inbound" log line from firing on every datagram —
    /// once is enough to confirm hole-punch succeeded.
    /// </summary>
    public bool LoggedFirstInbound { get; set; }

    /// <summary>
    /// Address we've confirmed the peer is reachable at. Null until
    /// the first inbound packet from one of the candidates.
    /// </summary>
    public IPEndPoint? ConfirmedEndpoint { get; private set; }

    /// <summary>Last time we received any packet from this peer (UTC).</summary>
    public DateTime LastSeenUtc { get; private set; }

    /// <summary>
    /// Smoothed round-trip time in milliseconds, or -1 when no pings
    /// have come back yet. Updated whenever a WOLk pong arrives.
    /// EWMA with α=0.25 — responsive enough to surface network blips
    /// while damping single-sample noise.
    /// </summary>
    public double RttMs { get; private set; } = -1;

    public event EventHandler? StateChanged;

    private readonly List<IPEndPoint> _candidates = new();

    /// <summary>
    /// Snapshot of the addresses this peer announced via the lobby WS
    /// (typically one public STUN-mapped endpoint plus one or more LAN
    /// endpoints). Exposed so the hook's PEER_SET can include every IP
    /// this peer might be reachable on — that way AoE3's own LAN
    /// discovery, which often re-sends packets to the host's RAW LAN
    /// address rather than the virtual mesh IP we synthesised, still
    /// gets diverted into the bridge instead of leaving through the
    /// real wire (where it would never reach a peer on a different
    /// network, and where on the same WiFi it would create a confusing
    /// dual delivery path).
    ///
    /// Returns a fresh array each call so callers can iterate without
    /// holding any locks; the list is small (≤ a handful of endpoints).
    /// </summary>
    public IPAddress[] AnnouncedAddresses
    {
        get
        {
            var snap = _candidates.ToArray();
            var ips = new IPAddress[snap.Length];
            for (int i = 0; i < snap.Length; i++) ips[i] = snap[i].Address;
            return ips;
        }
    }

    private DateTime _punchingStartedUtc;
    private DateTime _lastSendUtc = DateTime.MinValue;
    private DateTime _lastPingSentUtc = DateTime.MinValue;
    private readonly byte[] _ownUserIdBytes;

    public PeerChannel(string userId, string login, string ownUserId)
    {
        UserId = userId;
        Login = login;
        _ownUserIdBytes = System.Text.Encoding.UTF8.GetBytes(ownUserId);
    }

    /// <summary>
    /// Called by the mesh when a peer_announce frame arrives for this
    /// peer. Refreshes our candidate list and (re)starts hole-punching
    /// if we aren't already connected.
    /// </summary>
    public void OnPeerAnnounce(IEnumerable<IPEndPoint> candidates)
    {
        _candidates.Clear();
        _candidates.AddRange(candidates.Distinct());

        if (State == PeerLinkState.Discovering || State == PeerLinkState.Lost)
        {
            State = PeerLinkState.Punching;
            _punchingStartedUtc = DateTime.UtcNow;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Called by the mesh's receive loop whenever it routes a datagram
    /// to this channel (matched by source IP+port against our
    /// candidates, OR against the ConfirmedEndpoint).
    /// </summary>
    public void OnPacket(IPEndPoint source, byte[] payload)
    {
        LastSeenUtc = DateTime.UtcNow;

        if (ConfirmedEndpoint == null)
        {
            ConfirmedEndpoint = source;
            State = PeerLinkState.Connected;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (State == PeerLinkState.Lost)
        {
            // Connection came back from a different address (NAT
            // rebind, mobile network switch). Pin the new endpoint.
            ConfirmedEndpoint = source;
            State = PeerLinkState.Connected;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Called periodically by the mesh tick. Returns the list of
    /// (endpoint, datagram) tuples we want to send NOW. The mesh
    /// writes them on the shared socket. Empty list = nothing to do.
    /// </summary>
    public IReadOnlyList<(IPEndPoint Dest, byte[] Payload)> Tick(DateTime nowUtc)
    {
        switch (State)
        {
            case PeerLinkState.Discovering:
            case PeerLinkState.Failed:
                return Array.Empty<(IPEndPoint, byte[])>();

            case PeerLinkState.Punching:
            case PeerLinkState.Lost:
                // Time out hole-punching after the deadline. Caller
                // (mesh) checks Failed state and may fall back to TURN.
                if (nowUtc - _punchingStartedUtc > PunchTimeout)
                {
                    State = PeerLinkState.Failed;
                    StateChanged?.Invoke(this, EventArgs.Empty);
                    return Array.Empty<(IPEndPoint, byte[])>();
                }

                if (nowUtc - _lastSendUtc < PunchInterval)
                    return Array.Empty<(IPEndPoint, byte[])>();
                _lastSendUtc = nowUtc;

                // Send a probe to every candidate. First one to reply
                // wins; the loop is cheap (a handful of UDP sendto's).
                var probe = BuildProbe();
                var sends = new List<(IPEndPoint, byte[])>(_candidates.Count);
                foreach (var ep in _candidates)
                    sends.Add((ep, probe));
                return sends;

            case PeerLinkState.Connected:
                // Heartbeat path. We fire two things on different
                // cadences here so a single Tick may return one,
                // both, or neither: a regular hole-punch keepalive
                // every KeepaliveInterval (5s) and an RTT ping every
                // PingInterval (2s).
                var outbound = new List<(IPEndPoint, byte[])>(2);

                // Fade detection.
                if (nowUtc - LastSeenUtc > KeepaliveInterval * LostAfterMissedBeats)
                {
                    State = PeerLinkState.Lost;
                    _punchingStartedUtc = nowUtc;
                    StateChanged?.Invoke(this, EventArgs.Empty);
                    return outbound;
                }

                if (nowUtc - _lastSendUtc >= KeepaliveInterval)
                {
                    _lastSendUtc = nowUtc;
                    outbound.Add((ConfirmedEndpoint!, BuildProbe()));
                }

                if (nowUtc - _lastPingSentUtc >= PingInterval)
                {
                    _lastPingSentUtc = nowUtc;
                    outbound.Add((ConfirmedEndpoint!, BuildPing(nowUtc)));
                }

                return outbound;
        }

        return Array.Empty<(IPEndPoint, byte[])>();
    }

    /// <summary>
    /// Called by the mesh when a recognised ping frame arrives. Builds
    /// the matching pong reply (with the original timestamp echoed)
    /// for the mesh to send back; returns null on malformed input.
    /// </summary>
    public byte[]? OnPingReceived(byte[] frame, int length)
    {
        // Frame layout: magic(4) + kind(1) + ts(8). Only 'ping' kind
        // gets a pong reply; any other kind we silently ignore so the
        // protocol can be extended without breaking old launchers.
        if (length < PingMagic.Length + 1 + 8) return null;
        if (frame[PingMagic.Length] != 0x00) return null;

        var pong = new byte[PingMagic.Length + 1 + 8];
        Buffer.BlockCopy(PingMagic, 0, pong, 0, PingMagic.Length);
        pong[PingMagic.Length] = 0x01;
        Buffer.BlockCopy(frame, PingMagic.Length + 1, pong, PingMagic.Length + 1, 8);
        return pong;
    }

    /// <summary>
    /// Called by the mesh when a pong frame arrives. Computes RTT
    /// against the embedded timestamp and updates <see cref="RttMs"/>
    /// using an EWMA. Caller decides whether to surface a state-changed
    /// event (the mesh already does, on a coarser cadence).
    /// </summary>
    public void OnPongReceived(byte[] frame, int length)
    {
        if (length < PingMagic.Length + 1 + 8) return;
        if (frame[PingMagic.Length] != 0x01) return;

        long ts =
              ((long)frame[PingMagic.Length + 1] << 56)
            | ((long)frame[PingMagic.Length + 2] << 48)
            | ((long)frame[PingMagic.Length + 3] << 40)
            | ((long)frame[PingMagic.Length + 4] << 32)
            | ((long)frame[PingMagic.Length + 5] << 24)
            | ((long)frame[PingMagic.Length + 6] << 16)
            | ((long)frame[PingMagic.Length + 7] <<  8)
            | ((long)frame[PingMagic.Length + 8]);
        var rtt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - ts;
        if (rtt < 0 || rtt > 60_000) return;        // clock skew / stale

        if (RttMs < 0) RttMs = rtt;
        else RttMs = (0.75 * RttMs) + (0.25 * rtt);
    }

    private byte[] BuildPing(DateTime nowUtc)
    {
        var buf = new byte[PingMagic.Length + 1 + 8];
        Buffer.BlockCopy(PingMagic, 0, buf, 0, PingMagic.Length);
        buf[PingMagic.Length] = 0x00;
        var ts = new DateTimeOffset(nowUtc, TimeSpan.Zero).ToUnixTimeMilliseconds();
        buf[PingMagic.Length + 1] = (byte)((ts >> 56) & 0xFF);
        buf[PingMagic.Length + 2] = (byte)((ts >> 48) & 0xFF);
        buf[PingMagic.Length + 3] = (byte)((ts >> 40) & 0xFF);
        buf[PingMagic.Length + 4] = (byte)((ts >> 32) & 0xFF);
        buf[PingMagic.Length + 5] = (byte)((ts >> 24) & 0xFF);
        buf[PingMagic.Length + 6] = (byte)((ts >> 16) & 0xFF);
        buf[PingMagic.Length + 7] = (byte)((ts >>  8) & 0xFF);
        buf[PingMagic.Length + 8] = (byte)((ts >>  0) & 0xFF);
        return buf;
    }

    /// <summary>True when a buffer looks like a ping/pong frame.</summary>
    public static bool IsPingFrame(byte[] buffer, int length) =>
        length >= PingMagic.Length
        && buffer[0] == PingMagic[0]
        && buffer[1] == PingMagic[1]
        && buffer[2] == PingMagic[2]
        && buffer[3] == PingMagic[3];

    /// <summary>
    /// Is the given UDP source address one of this peer's known
    /// candidates or the confirmed endpoint? The mesh uses this to
    /// route inbound packets to the right channel.
    /// </summary>
    public bool Matches(IPEndPoint source)
    {
        if (ConfirmedEndpoint != null && Equals(ConfirmedEndpoint, source))
            return true;
        foreach (var c in _candidates)
            if (Equals(c, source)) return true;
        return false;
    }

    /// <summary>
    /// Build a 4-byte magic + our user-id probe. Receivers use the
    /// magic to discriminate hole-punch traffic from game traffic and
    /// the user-id to verify the right peer.
    /// </summary>
    private byte[] BuildProbe()
    {
        var buf = new byte[PunchMagic.Length + _ownUserIdBytes.Length];
        Buffer.BlockCopy(PunchMagic, 0, buf, 0, PunchMagic.Length);
        Buffer.BlockCopy(_ownUserIdBytes, 0, buf, PunchMagic.Length, _ownUserIdBytes.Length);
        return buf;
    }

    /// <summary>True when a buffer looks like a hole-punch probe (magic prefix).</summary>
    public static bool IsProbe(byte[] buffer, int length) =>
        length >= PunchMagic.Length
        && buffer[0] == PunchMagic[0]
        && buffer[1] == PunchMagic[1]
        && buffer[2] == PunchMagic[2]
        && buffer[3] == PunchMagic[3];

    /// <summary>True when a buffer looks like a game-data frame.</summary>
    public static bool IsGameFrame(byte[] buffer, int length) =>
        length >= GameMagic.Length
        && buffer[0] == GameMagic[0]
        && buffer[1] == GameMagic[1]
        && buffer[2] == GameMagic[2]
        && buffer[3] == GameMagic[3];

    /// <summary>
    /// Wrap a UDP payload + (srcPort, dstPort, srcIp, dstIp) into the
    /// game-frame wire format described next to <see cref="GameMagic"/>.
    /// </summary>
    public static byte[] BuildGameFrame(
        ushort srcPort, ushort dstPort, IPAddress srcIp, IPAddress dstIp, byte[] payload)
    {
        // Force IPv4 — AoE3's LAN protocol is IPv4-only.
        var srcBytes = srcIp.MapToIPv4().GetAddressBytes();
        var dstBytes = dstIp.MapToIPv4().GetAddressBytes();
        var frame = new byte[GameMagic.Length + 4 + 4 + 4 + payload.Length];
        Buffer.BlockCopy(GameMagic, 0, frame, 0, GameMagic.Length);
        frame[4] = (byte)((srcPort >> 8) & 0xFF);
        frame[5] = (byte)(srcPort & 0xFF);
        frame[6] = (byte)((dstPort >> 8) & 0xFF);
        frame[7] = (byte)(dstPort & 0xFF);
        Buffer.BlockCopy(srcBytes, 0, frame, 8, 4);
        Buffer.BlockCopy(dstBytes, 0, frame, 12, 4);
        Buffer.BlockCopy(payload, 0, frame, 16, payload.Length);
        return frame;
    }

    /// <summary>
    /// Decode a buffer previously framed by <see cref="BuildGameFrame"/>.
    /// Returns false if the buffer isn't a recognisable game frame.
    /// </summary>
    public static bool TryParseGameFrame(byte[] buffer, int length,
        out ushort srcPort, out ushort dstPort,
        out IPAddress srcIp, out IPAddress dstIp, out byte[] payload)
    {
        srcPort = 0; dstPort = 0;
        srcIp = IPAddress.Any; dstIp = IPAddress.Broadcast;
        payload = Array.Empty<byte>();
        if (!IsGameFrame(buffer, length) || length < 16) return false;
        srcPort = (ushort)((buffer[4] << 8) | buffer[5]);
        dstPort = (ushort)((buffer[6] << 8) | buffer[7]);
        srcIp = new IPAddress(new byte[] { buffer[8], buffer[9], buffer[10], buffer[11] });
        dstIp = new IPAddress(new byte[] { buffer[12], buffer[13], buffer[14], buffer[15] });
        payload = new byte[length - 16];
        Buffer.BlockCopy(buffer, 16, payload, 0, payload.Length);
        return true;
    }
}
