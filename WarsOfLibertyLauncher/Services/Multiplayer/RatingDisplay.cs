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
    /// Whether a rating may be painted beside a name. It may, whenever there is one.
    ///
    /// <para><b>This used to withhold a provisional rating</b> — the server's starting
    /// 1500 — on the grounds that showing it would pass a placeholder off as earned
    /// skill. That reasoning does not survive the fact that <b>every player who has not
    /// played is on exactly 1500</b>: a number everybody starts from, shown to everybody,
    /// claims nothing about anyone. What it did instead was leave the rating blank across
    /// the whole app for weeks, which read as broken.</para>
    ///
    /// <para>The refusal that REMAINS is the one that was always the point: a null rating
    /// paints nothing. That is not somebody's 1500, it is not knowing — the state the app
    /// was in the day the backend went down — and inventing a number there would be the
    /// actual lie.</para>
    ///
    /// <para>The ranking table is the deliberate exception and keeps its own filter:
    /// showing 1500 next to a name informs, but ordering a league table of people who
    /// never played does not.</para>
    /// </summary>
    public static bool ShouldShow(double? rating) => rating.HasValue;
}
