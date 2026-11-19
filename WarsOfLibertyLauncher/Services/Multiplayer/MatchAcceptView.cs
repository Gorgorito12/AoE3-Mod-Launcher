namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Who has accepted a quick match (design 117). The server only says HOW MANY accepted
/// (<c>mm_accept_progress</c>), never who; with two players the rest follows: whatever the count
/// holds beyond my own answer is the rival's. Pure, pinned by <c>MatchAcceptViewTests</c>.
/// </summary>
public static class MatchAcceptView
{
    /// <param name="accepted">The server's count.</param>
    /// <param name="iAccepted">Whether I pressed Accept (or challenged, which is a yes).</param>
    /// <param name="rivalAlready">The rival was already seen as accepted: a later, lower count
    /// (a frame that crossed my own click) never takes that back.</param>
    /// <returns>Mine is what I pressed, whatever the count says; the rival's is marked only on
    /// the server's own count, never guessed.</returns>
    public static (bool Me, bool Rival) From(int accepted, bool iAccepted, bool rivalAlready = false)
        => (iAccepted, rivalAlready || accepted - (iAccepted ? 1 : 0) >= 1);
}
