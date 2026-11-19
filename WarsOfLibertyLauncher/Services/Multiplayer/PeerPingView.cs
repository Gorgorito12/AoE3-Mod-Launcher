namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>What a roster row shows of the ping to another player.</summary>
public enum PeerPingKind
{
    /// <summary>Nothing: it is the viewer, or the player has no Radmin address yet ("Waiting for VPN" says it).</summary>
    None,

    /// <summary>No answer has come back yet.</summary>
    Measuring,

    /// <summary>A round trip in milliseconds.</summary>
    Answered,

    /// <summary>Two probes in a row went unanswered.</summary>
    NoAnswer,
}

/// <summary>A ping cell: the words, its colour, how many of the four bars are lit, and the tooltip.</summary>
public readonly record struct PeerPing(PeerPingKind Kind, string Text, string BrushKey, int LitBars, string? TipKey, int? Ms);

/// <summary>
/// The ping from this launcher to each player of the room, over Radmin (ICMP every 2.5 s,
/// <c>KickPeerPings</c>). The same bars, cuts and colours as the PING column of the Rooms page, so
/// a figure means the same thing in both places.
///
/// <para><b>Never an invented number.</b> One lost probe keeps the last answer on screen (a
/// single dropped packet is ordinary), two in a row show a dash, and the tooltip says what a
/// dash means: Windows and Radmin often block the echo while the game connects fine
/// (<see cref="PeerNetHealth"/>).</para>
/// </summary>
public static class PeerPingView
{
    /// <summary>Probes in a row without an answer before the cell gives up its last figure.</summary>
    public const int FailsBeforeNoAnswer = 2;

    public static PeerPing For(bool isMe, bool hasRadminIp, int pingMs, int lastGoodMs, int consecutiveFails)
    {
        if (isMe || !hasRadminIp) return new(PeerPingKind.None, "", "RmPingUnknown", 0, null, null);
        var ms = pingMs >= 0 ? pingMs
            : consecutiveFails < FailsBeforeNoAnswer && lastGoodMs >= 0 ? lastGoodMs
            : -1;
        if (ms >= 0)
            return new(PeerPingKind.Answered, ms + " ms", RoomActionRule.PingBrushKey(ms), LitBars(ms), "RoomPingTipAnswered", ms);
        return consecutiveFails >= FailsBeforeNoAnswer
            ? new(PeerPingKind.NoAnswer, "—", "RmPingUnknown", 0, "RoomPingTipNoAnswer", null)
            : new(PeerPingKind.Measuring, "…", "RmPingUnknown", 0, "RoomPingTipMeasuring", null);
    }

    /// <summary>Up to 60 ms all four bars, under 100 three, up to 150 two, slower one (the Rooms page's cuts).</summary>
    public static int LitBars(int ms) => ms < 0 ? 0 : ms <= 60 ? 4 : ms < 100 ? 3 : ms <= 150 ? 2 : 1;
}
