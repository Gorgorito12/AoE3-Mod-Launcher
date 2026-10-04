namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>What the invite chip on a player's row does.</summary>
public enum InviteAction
{
    /// <summary>I am in no room, so there is nothing to invite them to: the chip is dimmed.</summary>
    Disabled,

    /// <summary>Send the invite.</summary>
    Send,

    /// <summary>
    /// They are in a match: warn me instead of sending. Their launcher shows no card while their
    /// game runs, so the invite would be lost — and would spend their 60-s per-sender cooldown, so
    /// an invite right after the match would be dropped too.
    /// </summary>
    WarnPlaying,
}

/// <summary>
/// Whether an invite to a player can reach them (asked for by a player: "warning whoever invites
/// that they are playing would be enough"). Pure, so the rule is tested rather than read off a row.
/// </summary>
public static class InviteTarget
{
    /// <summary>The presence status the server gives a player whose room's match is running.</summary>
    public const string InGameStatus = "in_game";

    /// <summary>
    /// The chip's action for a player whose presence status is <paramref name="status"/>, when I
    /// am (<paramref name="inRoom"/>) or am not in a room. Being in no room comes first. An unknown
    /// or missing status SENDS — the warning rests on the server's word, never on a guess, so an
    /// older backend behaves exactly as before.
    /// </summary>
    public static InviteAction Decide(bool inRoom, string? status)
    {
        if (!inRoom) return InviteAction.Disabled;
        return string.Equals(status, InGameStatus, System.StringComparison.Ordinal)
            ? InviteAction.WarnPlaying
            : InviteAction.Send;
    }
}
