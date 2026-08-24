namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// How a rating is allowed to appear on screen. Pure, so the rules can be tested
/// and — more importantly — so every surface reaches the same answer.
///
/// <para>Two decisions live here, and both are refusals. What a rating change looks
/// like, and whether a rating may be shown at all. They were previously spelled out
/// inline at each place that paints one, which is how the end-of-match card and the
/// roster came to disagree about whether an unearned 1500 counts as a number.</para>
/// </summary>
public static class RatingDisplay
{
    /// <summary>
    /// A rating change, or null when there is nothing honest to write.
    ///
    /// <para>The sign is always explicit, because "12" and "+12" read as different
    /// claims. Null propagates from <see cref="MatchOutcomeView.Delta"/>, which
    /// returns it whenever either end of the change is missing — an older backend,
    /// or a match that was stored without ever being rated.</para>
    ///
    /// <para><b>Zero is not null.</b> A delta that rounds to zero with both ends known
    /// is a fact — the match moved nothing — and is written "+0". Not knowing what a
    /// match did is a different statement, and it is the one that paints nothing.
    /// Don't "tidy" the zero away.</para>
    /// </summary>
    public static string? FormatDelta(int? delta)
        => delta == null ? null
         : delta.Value >= 0 ? $"+{delta.Value}"
         : delta.Value.ToString();

    /// <summary>
    /// Whether someone else's rating may be painted beside their name.
    ///
    /// <para>Both halves are required. A rating without its deviation comes from a
    /// backend that only tells half the story, and the missing half is exactly what
    /// decides whether the number means anything yet — so in that doubt, nothing is
    /// shown.</para>
    ///
    /// <para>And a provisional rating is withheld: the server hands every new player
    /// 1500, and rendering that next to a name turns a placeholder into a claim about
    /// their skill. The player's own Profile tab is the one place it does appear,
    /// because there it sits beside the word "provisional"; a roster line has no room
    /// for the qualifier, and the bare number would read as a ranking.</para>
    /// </summary>
    public static bool ShouldShow(double? rating, double? rd)
        => rating.HasValue && rd.HasValue && !MatchOutcomeView.IsProvisional(rd);
}
