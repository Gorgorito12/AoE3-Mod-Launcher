using System;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace WarsOfLibertyLauncher.Services.Multiplayer.NativeHook;

/// <summary>
/// Deterministically derive a virtual IPv4 address from a user id.
///
/// Why we need this:
///   The hook DLL inside age3y.exe sees inbound LAN datagrams as if they
///   came from a real IPv4 source. The launcher synthesises that source
///   when it injects a packet through the bridge. We need a stable
///   IPv4 per peer so that:
///     1. AoE3's own session bookkeeping (which keys on source IP) stays
///        consistent across packets from the same peer.
///     2. Both endpoints agree on each other's virtual IP without any
///        exchange — the value is a pure function of the user id, so
///        the host's view of "peer X is at 10.147.a.b" matches the
///        joiner's "I am 10.147.a.b" with no negotiation needed.
///
/// Derivation (kept here so the algorithm can be audited or ported):
///   * Compute SHA-256 of the UTF-8 user id bytes.
///   * Take the LAST 4 bytes (bytes 28..31) of the digest as the
///     entropy source — arbitrary but reproducible.
///   * Read those 4 bytes as a little-endian uint32 and reduce modulo
///     (254 * 254) — 64516 distinct slots, way more than any AoE3
///     lobby ever needs (room caps at 8).
///   * Map the slot into the 10.147.x.y grid where x,y range 1..254
///     (avoid .0 and .255 so we never hit broadcast / network addrs):
///       x = (slot / 254) + 1   ->  1..254
///       y = (slot % 254) + 1   ->  1..254
///
/// Range: 10.147.1.1 ... 10.147.254.254
///   * Sits inside RFC1918 10.0.0.0/8, so AoE3 happily treats it as LAN.
///   * Picks the 10.147.0.0/16 subnet because PeerMesh's LAN scan
///     already filters that range out (legacy Wintun / WinDivert virtual
///     LAN) — guarantees the virtual IPs never collide with a real NIC
///     address the launcher might enumerate.
///   * .0 and .255 in each octet are excluded so we cannot accidentally
///     produce 10.147.x.0 (network), 10.147.x.255 (broadcast), or
///     10.147.0.y / 10.147.255.y (out-of-range edges).
///
/// SHA-256 is overkill for a 16-bit slot space, but it's in the BCL,
/// branchless, and removes any "are user ids well-distributed?" concern.
/// </summary>
public static class VirtualIpAllocator
{
    /// <summary>
    /// Derive the virtual IPv4 that represents <paramref name="userId"/>
    /// inside the launcher↔hook bridge. Same input always yields the
    /// same address; different ids almost never collide (≤ 1 in 64516).
    /// </summary>
    public static IPAddress DeriveFor(string userId)
    {
        if (string.IsNullOrEmpty(userId))
        {
            // Defensive: an empty id would otherwise land on a fixed
            // bucket, which is fine but masks bugs upstream. Stamp a
            // recognisable address so logs make the situation obvious
            // without crashing the launcher.
            return IPAddress.Parse("10.147.1.1");
        }

        var bytes = Encoding.UTF8.GetBytes(userId);
        byte[] hash;
        // SHA256.HashData is the BCL one-shot — no allocation of an
        // incremental hasher, no IDisposable to manage.
        hash = SHA256.HashData(bytes);

        // Last 4 bytes (28..31) read as little-endian uint32.
        uint entropy = (uint)hash[28]
                     | ((uint)hash[29] << 8)
                     | ((uint)hash[30] << 16)
                     | ((uint)hash[31] << 24);

        // 254 * 254 = 64516 distinct slots — covers every conceivable
        // AoE3 lobby size with room to spare.
        const uint slotCount = 254u * 254u;
        uint slot = entropy % slotCount;

        // Map slot -> (x,y) both in [1,254]. The +1 offsets avoid
        // network / broadcast octets so we never produce 10.147.0.* or
        // 10.147.*.0 / .255.
        byte x = (byte)((slot / 254u) + 1u);
        byte y = (byte)((slot % 254u) + 1u);

        // IPAddress(byte[]) takes bytes in network order = dotted-quad
        // left-to-right. 10.147.x.y is exactly { 10, 147, x, y }.
        return new IPAddress(new byte[] { 10, 147, x, y });
    }
}
