namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>Which colour a latency figure is drawn in.</summary>
public enum PingBand
{
    /// <summary>No measurement yet: drawn as an em dash, in no colour that claims anything.</summary>
    None,

    /// <summary>Under <see cref="RoomPingBand.GoodBelowMs"/>.</summary>
    Good,

    /// <summary>Under <see cref="RoomPingBand.MediumBelowMs"/>.</summary>
    Medium,

    /// <summary>Everything slower.</summary>
    Bad,
}

/// <summary>
/// The rooms table's PING colours — design handoff turn 36: green under 60 ms, amber under 120,
/// red beyond. The amber band used to run to 150, which coloured a ping most players feel as
/// laggy the same as a comfortable one.
///
/// <para>The figure is still YOUR internet latency, the same on every row (the room list carries
/// no per-host address); this only decides its colour. Pure, pinned by <c>RoomPingBandTests</c>.</para>
/// </summary>
public static class RoomPingBand
{
    /// <summary>Below this many milliseconds a ping is good.</summary>
    public const double GoodBelowMs = 60;

    /// <summary>Below this many milliseconds a ping is acceptable.</summary>
    public const double MediumBelowMs = 120;

    /// <summary>The band for <paramref name="ms"/>; null, negative or NaN is no measurement.</summary>
    public static PingBand For(double? ms)
    {
        if (ms is not { } v || v < 0 || double.IsNaN(v)) return PingBand.None;
        if (v < GoodBelowMs) return PingBand.Good;
        if (v < MediumBelowMs) return PingBand.Medium;
        return PingBand.Bad;
    }
}
