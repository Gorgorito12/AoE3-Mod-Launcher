namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>What a room row's PING cell says (design 86).</summary>
internal enum HostPingKind
{
    /// <summary>My own room, or a row with nothing to say: the cell stays empty.</summary>
    None,
    /// <summary>I have no Radmin address, so nothing can be measured from here.</summary>
    NoMyRadmin,
    /// <summary>A room whose host's address the server does not publish: behind a password, or a
    /// tournament match. Said as such, never as "not on Radmin", which would be a guess.</summary>
    NotPublished,
    /// <summary>The host has not reported a Radmin address yet.</summary>
    HostNotOnRadmin,
    /// <summary>The host has an address and no reading has come back yet.</summary>
    Measuring,
    /// <summary>The host answered: the figure is my latency to him over Radmin.</summary>
    Answered,
    /// <summary>The host did not answer twice running — his firewall usually blocks the probe.</summary>
    NoAnswer,
}

/// <summary>The facts one row's ping cell is decided from.</summary>
/// <param name="LastMs">The last answered round trip, or null when the last probe got no answer.</param>
/// <param name="Fails">Probes in a row that got no answer.</param>
/// <param name="Published">The server publishes this room's host (public, not a tournament match).</param>
internal readonly record struct HostPingFacts(bool Mine, bool IHaveRadmin, bool Published, bool HostHasIp, int? LastMs, int Fails);

/// <summary>
/// The rooms list's per-host ping, decided in one place (design 86). The figure is the ICMP round
/// trip to the host's Radmin address, which is the link a match actually uses. It is never invented:
/// a host that cannot be measured says why in the tooltip instead of showing a number.
/// </summary>
internal static class HostPing
{
    /// <summary>Probes in a row without an answer before a row says "no answer". One dropped packet
    /// must not read as a firewall.</summary>
    internal const int FailsBeforeNoAnswer = 2;

    internal static HostPingKind Classify(HostPingFacts f)
    {
        if (f.Mine) return HostPingKind.None;
        if (!f.IHaveRadmin) return HostPingKind.NoMyRadmin;
        if (!f.Published) return HostPingKind.NotPublished;
        if (!f.HostHasIp) return HostPingKind.HostNotOnRadmin;
        if (f.Fails >= FailsBeforeNoAnswer) return HostPingKind.NoAnswer;
        return f.LastMs.HasValue ? HostPingKind.Answered : HostPingKind.Measuring;
    }

    /// <summary>The cell's text: the figure, an ellipsis while measuring, a dash otherwise.</summary>
    internal static string Text(HostPingKind kind, int? ms) => kind switch
    {
        HostPingKind.None => "",
        HostPingKind.Answered => $"{ms} ms",
        HostPingKind.Measuring => "…",
        _ => "—",
    };

    /// <summary>The string key of the tooltip that says what the cell means; null for none.</summary>
    internal static string? TipKey(HostPingKind kind) => kind switch
    {
        HostPingKind.NoMyRadmin => "RmPingTipNoRadmin",
        HostPingKind.NotPublished => "RmPingTipNotPublished",
        HostPingKind.HostNotOnRadmin => "RmPingTipHostNoRadmin",
        HostPingKind.NoAnswer => "RmPingTipNoAnswer",
        HostPingKind.Answered => "RmPingTipAnswered",
        HostPingKind.Measuring => "RmPingTipMeasuring",
        _ => null,
    };
}
