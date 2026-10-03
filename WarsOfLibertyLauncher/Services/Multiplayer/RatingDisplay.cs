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

    /// <summary>
    /// The rating deviation an OLDER server (before rating v3) handed somebody it had never rated.
    /// Only consulted when no game count travels — i.e. only against that older server.
    /// </summary>
    public const double UnratedRd = 350;

    /// <summary>
    /// Whether this player has never played a rated match — and so should read "unrated"
    /// instead of the 1500 everybody starts from.
    ///
    /// <para><b>This narrows <see cref="ShouldShow"/>, which was itself a reversal</b>, so both
    /// turns are written down. That one argued a number everybody starts from claims nothing
    /// about anyone; true, and it still left the same 1500 beside a name that had earned it and
    /// a name that had not, with no way to tell which. Saying "unrated" says the one thing the
    /// number could not.</para>
    ///
    /// <para><b>Not knowing is not the same as unrated, and it keeps painting the number.</b>
    /// A backend older than these fields sends neither, and turning that silence into a claim
    /// about the player would be inventing a state — the same refusal <see cref="ShouldShow"/>
    /// makes about a null rating.</para>
    ///
    /// <para><b>Rating v3: the game count decides whenever it travels, and the deviation is a
    /// fallback for an older server only.</b> Since v3 every surface carries
    /// <c>games_played</c>, and the deviation can no longer stand in for it: a new player starts
    /// at 500, and a veteran who stops playing has his deviation GROW back toward that same 500
    /// (0.21436 rating periods per day). Read off the deviation, a long-absent veteran would turn
    /// "unrated". So the count answers when present, and the old untouched-350 test is kept only
    /// for a server that sends no count — the only server whose newcomers sat on exactly 350.
    /// (Seasons are gone, so a count of zero means "never", as it did before them.)</para>
    /// </summary>
    public static bool IsUnrated(double? rd, int? gamesPlayed)
        => gamesPlayed is int played ? played <= 0
         // Float-safe: the older server sent its own constant back through JSON.
         : rd is double dev && Math.Abs(dev - UnratedRd) < 0.5;

    /// <summary>The three ways a rating can read beside a name.</summary>
    public enum Look
    {
        /// <summary>The plain number.</summary>
        Number,
        /// <summary>The number with a "?" (design 55): still in placement.</summary>
        Provisional,
        /// <summary>The word "unrated": no rated match on this ladder.</summary>
        Unrated,
    }

    /// <summary>
    /// How a rating reads beside a name. <paramref name="inPlacement"/> is the SERVER's flag
    /// (<c>inPlacement</c> / <c>in_placement</c>), never worked out here: null — an older backend —
    /// draws the plain number, as before.
    /// </summary>
    public static Look LookOf(double? rd, int? gamesPlayed, bool? inPlacement)
        => IsUnrated(rd, gamesPlayed) ? Look.Unrated
         : inPlacement == true ? Look.Provisional
         : Look.Number;
}
