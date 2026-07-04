namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Derived connection state for a single peer in a room/match, shown as the
/// health dot + status text in the roster and in-game panels.
/// </summary>
public enum PeerLinkState
{
    /// <summary>The peer hasn't reported a Radmin IP yet, so we can't probe them.
    /// Neutral/grey — NOT a failure ("Esperando VPN").</summary>
    WaitingVpn,
    /// <summary>Last ICMP probe answered — peer is reachable on the virtual LAN.</summary>
    Online,
    /// <summary>Peer has a Radmin IP but the last probe(s) didn't answer, below the
    /// sustained-failure threshold — transient / "…" (amber).</summary>
    Unstable,
    /// <summary>Sustained ICMP failure past <see cref="PeerNetHealth.LostThreshold"/>.
    /// INDICATIVE only — Windows/Radmin frequently block inbound ICMP echo while the
    /// game still works, so this is a soft "no responde", not an authoritative
    /// disconnect. The authoritative "left" signal is the server's member_left.</summary>
    Lost,
}

/// <summary>
/// Pure, WPF-free classifier for a peer's connection health from its last ICMP
/// result and a short failure history. Kept separate + public so it can be
/// unit-tested off the UI thread (same pattern as TranslationCompat).
/// </summary>
public static class PeerNetHealth
{
    /// <summary>Consecutive failed 1-s probes before a peer is flagged "Lost".
    /// Conservative on purpose — one dropped packet must not raise a false alarm.</summary>
    public const int LostThreshold = 5;

    /// <summary>
    /// Classify a peer.
    /// <paramref name="hasRadminIp"/>: has the peer reported a Radmin IP we can ping.
    /// <paramref name="lastPingMs"/>: last ICMP RTT (>=0 = answered, -1 = no answer/unknown).
    /// <paramref name="consecutiveFails"/>: number of consecutive non-answering probes.
    /// </summary>
    public static PeerLinkState Classify(bool hasRadminIp, int lastPingMs, int consecutiveFails)
    {
        if (!hasRadminIp) return PeerLinkState.WaitingVpn;
        if (lastPingMs >= 0) return PeerLinkState.Online;
        if (consecutiveFails >= LostThreshold) return PeerLinkState.Lost;
        return PeerLinkState.Unstable;
    }
}
