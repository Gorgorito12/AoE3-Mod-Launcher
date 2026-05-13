using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace WarsOfLibertyLauncher.Services.Multiplayer.P2P;

/// <summary>
/// Result of a STUN Binding Request.
/// </summary>
public sealed record StunBindingResult(IPEndPoint Mapped, IPEndPoint LocalSource);

/// <summary>
/// Minimal RFC 5389 STUN client. Implements only what's needed for the
/// launcher's NAT discovery flow: a single Binding Request to a public
/// STUN server, parsing the XOR-MAPPED-ADDRESS attribute from the
/// response.
///
/// Why hand-rolled instead of a NuGet?
///   * The Binding Request is ~20 bytes of header + 0 attributes.
///   * The response parser needs ~50 lines.
///   * No NuGet for STUN that's both maintained AND has a tiny surface.
///     The popular ones (Microsoft.Mixer.Client, SIPSorcery) drag in
///     hundreds of unrelated types for SIP/WebRTC stacks.
///
/// Public free STUN servers we'll target (rotated for load balancing):
///   * stun.l.google.com:19302
///   * stun.cloudflare.com:3478
///   * stun.nextcloud.com:443
///
/// The launcher uses the result to:
///   * Detect NAT type (Open / Moderate / Strict / Symmetric).
///   * Discover its own public IP:port to hand to peers via the lobby
///     WebSocket for hole-punching.
/// </summary>
public static class StunClient
{
    private const ushort BindingRequest = 0x0001;
    private const ushort BindingResponse = 0x0101;
    private const uint MagicCookie = 0x2112A442;
    private const ushort AttrXorMappedAddress = 0x0020;
    private const ushort AttrMappedAddress = 0x0001;

    /// <summary>
    /// Send a single STUN Binding Request to <paramref name="server"/>
    /// from the given local UDP socket. Returns the mapped (public)
    /// address the server saw us coming from. Throws on timeout or
    /// malformed response.
    ///
    /// The caller owns the <see cref="UdpClient"/>: we deliberately
    /// don't create our own socket so the same socket can later be
    /// reused for hole-punching against discovered peers (otherwise the
    /// public port the STUN server saw would be a different port from
    /// the one peers try to reach us on).
    /// </summary>
    public static async Task<StunBindingResult> BindingRequestAsync(
        UdpClient socket,
        IPEndPoint server,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        var txId = new byte[12];
        RandomNumberGenerator.Fill(txId);

        var request = new byte[20];
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(0, 2), BindingRequest);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(2, 2), 0);            // 0 attributes
        BinaryPrimitives.WriteUInt32BigEndian(request.AsSpan(4, 4), MagicCookie);
        Buffer.BlockCopy(txId, 0, request, 8, 12);

        await socket.SendAsync(request, request.Length, server);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        UdpReceiveResult result;
        try
        {
            result = await socket.ReceiveAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"STUN server {server} did not respond within {timeout}.");
        }

        var resp = result.Buffer;
        if (resp.Length < 20) throw new InvalidOperationException("STUN reply too short.");

        var msgType = BinaryPrimitives.ReadUInt16BigEndian(resp.AsSpan(0, 2));
        if (msgType != BindingResponse)
            throw new InvalidOperationException($"Expected Binding Response (0x{BindingResponse:X4}), got 0x{msgType:X4}.");

        // Validate transaction id so we don't accidentally accept a stray
        // packet from someone else hitting the same local port.
        for (int i = 0; i < 12; i++)
            if (resp[8 + i] != txId[i])
                throw new InvalidOperationException("STUN transaction id mismatch.");

        var msgLen = BinaryPrimitives.ReadUInt16BigEndian(resp.AsSpan(2, 2));
        if (resp.Length < 20 + msgLen)
            throw new InvalidOperationException("STUN reply truncated.");

        // Walk attributes looking for XOR-MAPPED-ADDRESS (preferred) or
        // MAPPED-ADDRESS (legacy). The xor form is required to defeat
        // NAT devices that rewrite IP addresses they see in payloads.
        int offset = 20;
        IPEndPoint? mapped = null;

        while (offset + 4 <= 20 + msgLen)
        {
            var attrType = BinaryPrimitives.ReadUInt16BigEndian(resp.AsSpan(offset, 2));
            var attrLen = BinaryPrimitives.ReadUInt16BigEndian(resp.AsSpan(offset + 2, 2));
            offset += 4;

            if (attrType == AttrXorMappedAddress && attrLen >= 8)
            {
                mapped = ParseXorMappedAddress(resp.AsSpan(offset, attrLen), txId);
                break;
            }
            if (attrType == AttrMappedAddress && attrLen >= 8 && mapped == null)
            {
                mapped = ParseMappedAddress(resp.AsSpan(offset, attrLen));
                // Don't break — still prefer XOR if it appears later.
            }

            // Attributes are padded to 4 bytes.
            offset += (attrLen + 3) & ~3;
        }

        if (mapped == null)
            throw new InvalidOperationException("STUN reply had no mapped address.");

        var local = (IPEndPoint?)socket.Client.LocalEndPoint
            ?? new IPEndPoint(IPAddress.Any, 0);
        return new StunBindingResult(mapped, local);
    }

    /// <summary>Parse an XOR-MAPPED-ADDRESS attribute (RFC 5389 §15.2).</summary>
    private static IPEndPoint ParseXorMappedAddress(ReadOnlySpan<byte> data, byte[] txId)
    {
        // data layout: 0=reserved | 1=family | 2-3=xport | 4..=xaddr
        var family = data[1];
        var xport = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(2, 2));
        var port = (ushort)(xport ^ (MagicCookie >> 16));

        if (family == 0x01)         // IPv4
        {
            Span<byte> addrBytes = stackalloc byte[4];
            data.Slice(4, 4).CopyTo(addrBytes);
            // XOR address with magic cookie
            addrBytes[0] ^= (byte)((MagicCookie >> 24) & 0xFF);
            addrBytes[1] ^= (byte)((MagicCookie >> 16) & 0xFF);
            addrBytes[2] ^= (byte)((MagicCookie >>  8) & 0xFF);
            addrBytes[3] ^= (byte)((MagicCookie >>  0) & 0xFF);
            return new IPEndPoint(new IPAddress(addrBytes), port);
        }

        if (family == 0x02)         // IPv6 — XOR with cookie ++ tx-id
        {
            Span<byte> addrBytes = stackalloc byte[16];
            data.Slice(4, 16).CopyTo(addrBytes);
            // First 4 bytes: XOR with magic cookie. Next 12: XOR with tx-id.
            addrBytes[0] ^= (byte)((MagicCookie >> 24) & 0xFF);
            addrBytes[1] ^= (byte)((MagicCookie >> 16) & 0xFF);
            addrBytes[2] ^= (byte)((MagicCookie >>  8) & 0xFF);
            addrBytes[3] ^= (byte)((MagicCookie >>  0) & 0xFF);
            for (int i = 0; i < 12; i++) addrBytes[4 + i] ^= txId[i];
            return new IPEndPoint(new IPAddress(addrBytes), port);
        }

        throw new InvalidOperationException($"Unknown address family 0x{family:X2}.");
    }

    /// <summary>Parse a legacy MAPPED-ADDRESS attribute (RFC 3489).</summary>
    private static IPEndPoint ParseMappedAddress(ReadOnlySpan<byte> data)
    {
        var family = data[1];
        var port = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(2, 2));
        if (family == 0x01)
        {
            return new IPEndPoint(new IPAddress(data.Slice(4, 4).ToArray()), port);
        }
        if (family == 0x02)
        {
            return new IPEndPoint(new IPAddress(data.Slice(4, 16).ToArray()), port);
        }
        throw new InvalidOperationException($"Unknown address family 0x{family:X2}.");
    }
}
