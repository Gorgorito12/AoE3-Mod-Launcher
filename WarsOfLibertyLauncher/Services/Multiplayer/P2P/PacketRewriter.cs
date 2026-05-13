using System;
using System.Buffers.Binary;
using System.Net;

namespace WarsOfLibertyLauncher.Services.Multiplayer.P2P;

/// <summary>
/// Parser / rewriter for the IPv4 + UDP headers that AoE3 emits.
/// All packet manipulation that bridges WinDivert capture with our
/// peer-to-peer transport flows through here.
///
/// What we need to do, end-to-end:
///   * **On capture**: take a raw IP packet from
///     <see cref="WinDivertNative.WinDivertRecv"/>, extract the UDP
///     payload + ports, and forward (payload, src-port, dst-port) over
///     the mesh to peers. Drop the captured packet so AoE3's local
///     subnet doesn't see a duplicate.
///   * **On inject**: receive (payload, src-port, dst-port, peer's
///     virtual LAN IP) from a peer, synthesise a fresh IP+UDP frame
///     with src=peer-vlan, dst=255.255.255.255 (or our own LAN IP),
///     compute checksums, and inject via
///     <see cref="WinDivertNative.WinDivertSend"/> so AoE3 sees it as
///     a real LAN packet.
///
/// Why all the bit-pushing instead of a NuGet?
///   * Existing libraries (PacketDotNet, SharpPcap) are huge and pull
///     in libpcap dependencies. We only need IPv4 + UDP, ~60 lines.
///   * The checksum math is well-known (RFC 1071) and trivial.
///   * Performance: AoE3 in-game can push hundreds of packets/s; a
///     thin in-place parser avoids the GC churn that an OO library
///     introduces.
/// </summary>
internal static class PacketRewriter
{
    /// <summary>Slice of a parsed UDP-over-IPv4 packet pointing back into the original buffer.</summary>
    public readonly ref struct UdpPacket
    {
        public readonly Span<byte> Buffer;
        public readonly int IpHeaderOffset;
        public readonly int IpHeaderLength;
        public readonly int UdpHeaderOffset;
        public readonly int PayloadOffset;
        public readonly int PayloadLength;

        public UdpPacket(Span<byte> buffer, int ipHdrOffset, int ipHdrLen,
            int udpHdrOffset, int payloadOffset, int payloadLen)
        {
            Buffer = buffer;
            IpHeaderOffset = ipHdrOffset;
            IpHeaderLength = ipHdrLen;
            UdpHeaderOffset = udpHdrOffset;
            PayloadOffset = payloadOffset;
            PayloadLength = payloadLen;
        }

        public IPAddress SrcAddr => new(Buffer.Slice(IpHeaderOffset + 12, 4).ToArray());
        public IPAddress DstAddr => new(Buffer.Slice(IpHeaderOffset + 16, 4).ToArray());
        public ushort SrcPort => BinaryPrimitives.ReadUInt16BigEndian(Buffer.Slice(UdpHeaderOffset + 0, 2));
        public ushort DstPort => BinaryPrimitives.ReadUInt16BigEndian(Buffer.Slice(UdpHeaderOffset + 2, 2));
        public Span<byte> Payload => Buffer.Slice(PayloadOffset, PayloadLength);
    }

    /// <summary>
    /// Parse a captured raw IP packet. Returns false if the packet is
    /// not IPv4-UDP or any header field is inconsistent. WinDivert
    /// guarantees we'll get well-formed packets in practice, but we
    /// validate anyway so a malformed driver event can't crash us.
    /// </summary>
    public static bool TryParseUdp(Span<byte> packet, out UdpPacket parsed)
    {
        parsed = default;
        if (packet.Length < 20) return false;

        // IP version + IHL nibble.
        var versionIhl = packet[0];
        var version = versionIhl >> 4;
        if (version != 4) return false;
        var ihl = (versionIhl & 0x0F) * 4;
        if (ihl < 20 || ihl > packet.Length) return false;

        var protocol = packet[9];
        if (protocol != 17) return false;     // 17 = UDP

        var totalLen = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(2, 2));
        if (totalLen > packet.Length) return false;

        var udpStart = ihl;
        if (udpStart + 8 > totalLen) return false;

        var udpLen = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(udpStart + 4, 2));
        if (udpLen < 8 || udpStart + udpLen > totalLen) return false;

        parsed = new UdpPacket(
            buffer: packet,
            ipHdrOffset: 0,
            ipHdrLen: ihl,
            udpHdrOffset: udpStart,
            payloadOffset: udpStart + 8,
            payloadLen: udpLen - 8);
        return true;
    }

    /// <summary>
    /// Build a fresh IPv4-UDP packet with the given source/destination
    /// addresses + ports + payload. The result is suitable for direct
    /// injection via <see cref="WinDivertNative.WinDivertSend"/>.
    /// </summary>
    public static byte[] BuildUdpPacket(
        IPAddress srcAddr,
        IPAddress dstAddr,
        ushort srcPort,
        ushort dstPort,
        ReadOnlySpan<byte> payload)
    {
        if (srcAddr.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            dstAddr.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            throw new ArgumentException("Only IPv4 addresses supported.");

        // Layout: [20 bytes IPv4 header] [8 bytes UDP header] [payload]
        int total = 20 + 8 + payload.Length;
        if (total > ushort.MaxValue) throw new ArgumentException("Packet too big for UDP.");

        var pkt = new byte[total];
        var span = pkt.AsSpan();

        // ---- IPv4 header (no options) ----
        span[0] = 0x45;                                              // version=4, ihl=5
        span[1] = 0x00;                                              // DSCP/ECN
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(2, 2), (ushort)total);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(4, 2), 0); // identification (driver may rewrite)
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(6, 2), 0x4000); // flags=DF (don't fragment)
        span[8] = 64;                                                 // TTL
        span[9] = 17;                                                 // protocol = UDP
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(10, 2), 0); // header checksum (fill below)
        srcAddr.GetAddressBytes().CopyTo(span.Slice(12, 4));
        dstAddr.GetAddressBytes().CopyTo(span.Slice(16, 4));
        var ipCsum = OnesComplementChecksum(span.Slice(0, 20));
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(10, 2), ipCsum);

        // ---- UDP header ----
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(20, 2), srcPort);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(22, 2), dstPort);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(24, 2), (ushort)(8 + payload.Length));
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(26, 2), 0); // checksum (optional for IPv4)
        payload.CopyTo(span.Slice(28));

        // UDP checksum is computed over a pseudo-header + UDP datagram.
        // It's optional in IPv4 (a zero value means "not used"); we
        // compute it anyway because some game engines and firewalls
        // will drop UDP with checksum=0.
        var udpCsum = UdpChecksum(srcAddr, dstAddr, span.Slice(20, 8 + payload.Length));
        // RFC 768: transmitted value is the 1's complement of the sum;
        // if it computes to zero, send 0xFFFF (zero means "no checksum").
        if (udpCsum == 0) udpCsum = 0xFFFF;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(26, 2), udpCsum);

        return pkt;
    }

    /// <summary>16-bit one's complement checksum over an even-length span. Pads odd lengths with a zero.</summary>
    private static ushort OnesComplementChecksum(ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        for (int i = 0; i + 1 < data.Length; i += 2)
            sum += (uint)((data[i] << 8) | data[i + 1]);
        if ((data.Length & 1) != 0)
            sum += (uint)(data[^1] << 8);
        while ((sum >> 16) != 0)
            sum = (sum & 0xFFFF) + (sum >> 16);
        return (ushort)~sum;
    }

    /// <summary>UDP checksum: 1's complement over a synthetic IPv4 pseudo-header + UDP datagram.</summary>
    private static ushort UdpChecksum(IPAddress src, IPAddress dst, ReadOnlySpan<byte> udpDatagram)
    {
        // Pseudo-header: srcIp(4) + dstIp(4) + zero(1) + protocol(1) + udpLen(2)
        Span<byte> pseudo = stackalloc byte[12];
        src.GetAddressBytes().CopyTo(pseudo.Slice(0, 4));
        dst.GetAddressBytes().CopyTo(pseudo.Slice(4, 4));
        pseudo[8] = 0;
        pseudo[9] = 17;
        BinaryPrimitives.WriteUInt16BigEndian(pseudo.Slice(10, 2), (ushort)udpDatagram.Length);

        uint sum = 0;
        // Accumulate pseudo-header.
        for (int i = 0; i + 1 < pseudo.Length; i += 2)
            sum += (uint)((pseudo[i] << 8) | pseudo[i + 1]);
        // Accumulate UDP datagram (with its checksum field temporarily zeroed).
        for (int i = 0; i + 1 < udpDatagram.Length; i += 2)
        {
            ushort word;
            if (i == 6) word = 0;  // skip the checksum slot itself
            else word = (ushort)((udpDatagram[i] << 8) | udpDatagram[i + 1]);
            sum += word;
        }
        if ((udpDatagram.Length & 1) != 0)
            sum += (uint)(udpDatagram[^1] << 8);
        while ((sum >> 16) != 0)
            sum = (sum & 0xFFFF) + (sum >> 16);
        return (ushort)~sum;
    }
}
