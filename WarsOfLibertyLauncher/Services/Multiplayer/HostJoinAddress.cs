using System.Collections.Generic;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// What the joiner can be told about the host's address, as one closed set.
/// Every value other than <see cref="Ready"/> is a refusal, and the refusals are
/// the point: handing a player the wrong address sends them into somebody else's
/// game, and in a competitive room that lands a real result on the wrong people.
/// </summary>
public enum HostAddressState
{
    /// <summary>Not in a room, or the room has no host yet. Nothing to show.</summary>
    NoRoom,

    /// <summary>We ARE the host. There is no address to join — the host creates the
    /// game, everyone else comes to him — so the row says so instead of offering the
    /// player his own IP, which pasted into his own game is a confusing no-op.</summary>
    YouAreTheHost,

    /// <summary>The host is in the room but has not reported a Radmin IP yet. Normal
    /// for the first seconds: <c>set_radmin_ip</c> is sent on room entry and again at
    /// match launch, so this resolves itself. Distinct from <see cref="NoRoom"/>
    /// because here there IS someone to wait for.</summary>
    WaitingForHost,

    /// <summary>We have a usable 26.x address for the host.</summary>
    Ready,
}

/// <summary>
/// The host's address as the joiner should see it.
/// <see cref="Ip"/> is non-null if and only if <see cref="State"/> is
/// <see cref="HostAddressState.Ready"/>.
/// </summary>
public readonly record struct HostAddress(HostAddressState State, string? Ip)
{
    /// <summary>True when there is an address worth copying or typing into the game.</summary>
    public bool HasAddress => State == HostAddressState.Ready && !string.IsNullOrEmpty(Ip);
}

/// <summary>
/// Pure, WPF-free resolution of "which address does a joiner point AoE3 at".
///
/// <para>Kept separate and public so it can be unit-tested off the UI thread, the same
/// reason <see cref="PeerNetHealth"/> and <see cref="MatchTeamMap"/> are — and for the
/// same reason those are all refusals: the only failure mode that matters here is a
/// CONFIDENT WRONG ANSWER. An address that is merely missing shows a grey "waiting"
/// line, which a player can read and understand; an address that is present but wrong
/// is indistinguishable from a correct one until two people are in different games.</para>
///
/// <para><b>Do not confuse this with <c>OverrideAddress</c>.</b> That launch flag carries
/// each machine's OWN Radmin IP and merely BINDS AoE3's LAN discovery to the right NIC;
/// it never names a peer. This is the other direction — the host's address, for the
/// joiner to aim at — and the two are never interchangeable.</para>
/// </summary>
public static class HostJoinAddress
{
    /// <summary>
    /// Radmin hands every machine an address in 26.0.0.0/8 and that block reaches a home
    /// PC by no other route, so the prefix — not a name, not the server's word for it —
    /// is what says an address is reachable by the rest of the room. Same identity test
    /// <see cref="Services.RadminVpnService"/> applies to the local adapter.
    /// </summary>
    public const string RadminPrefix = "26.";

    /// <summary>
    /// Resolve the address the local player should point AoE3 at.
    /// </summary>
    /// <param name="hostUserId">The room's host user id, or null/blank when unknown.</param>
    /// <param name="myUserId">The signed-in user's id, or null/blank when signed out.</param>
    /// <param name="radminIpByUserId">
    /// Every member's last reported Radmin IP, keyed by user id. Values may be null —
    /// a member who has not sent <c>set_radmin_ip</c> yet has no entry worth reading.
    /// </param>
    public static HostAddress Resolve(
        string? hostUserId,
        string? myUserId,
        IReadOnlyDictionary<string, string?> radminIpByUserId)
    {
        if (string.IsNullOrWhiteSpace(hostUserId)) return new HostAddress(HostAddressState.NoRoom, null);

        // Ordinal, like every other user-id comparison on this path. A host id that
        // differs from ours only by case is a different account, not us.
        if (!string.IsNullOrWhiteSpace(myUserId)
            && string.Equals(hostUserId, myUserId, System.StringComparison.Ordinal))
        {
            return new HostAddress(HostAddressState.YouAreTheHost, null);
        }

        if (radminIpByUserId is null
            || !radminIpByUserId.TryGetValue(hostUserId, out var raw)
            || string.IsNullOrWhiteSpace(raw))
        {
            return new HostAddress(HostAddressState.WaitingForHost, null);
        }

        var ip = raw.Trim();

        // REFUSAL, and the one that earns this whole file. The backend validates 26.x
        // server-side, but an older backend does not, and a member could otherwise
        // report a 192.168.x address from their physical LAN. Pasted into the game that
        // resolves on the JOINER's own network — so it does not fail, it quietly reaches
        // a different machine. Treat anything that is not Radmin's as "not reported".
        if (!ip.StartsWith(RadminPrefix, System.StringComparison.Ordinal))
        {
            return new HostAddress(HostAddressState.WaitingForHost, null);
        }

        return new HostAddress(HostAddressState.Ready, ip);
    }
}
