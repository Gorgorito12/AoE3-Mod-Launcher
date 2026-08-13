using System;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>Which of the three things a finished match turned out to be.</summary>
public enum MatchVerdict
{
    /// <summary>We won it.</summary>
    Win,

    /// <summary>We lost it.</summary>
    Loss,

    /// <summary>
    /// Nobody knows. Not a draw — see <see cref="MatchOutcomeView.Classify"/>.
    /// </summary>
    NoResult,
}

/// <summary>
/// Everything the end-of-match card shows, and the pure rules that decide it.
///
/// <para>Free of WPF, like its siblings <see cref="MatchResultResolver"/> and
/// <see cref="PlayerStanding"/>, so the three claims that actually matter — a 0.5 is not
/// a draw, an unknown rating is not a zero delta, and an unknown win rate is not 0 % —
/// are testable rather than buried in a control builder.</para>
/// </summary>
/// <param name="Verdict">Win / Loss / NoResult, from <see cref="Classify"/>.</param>
/// <param name="ModId">The room's mod, for the subtitle.</param>
/// <param name="MapName">The real map, when the recording gave one.</param>
/// <param name="DurationSeconds">How long the match ran.</param>
/// <param name="PlayerCount">Participants, as reported. 0 on an older backend.</param>
/// <param name="RatingBefore">Our rating before the match, when the server said.</param>
/// <param name="RatingAfter">Our rating after it.</param>
/// <param name="RivalLogin">The other player, in a 1v1. Null past two players.</param>
/// <param name="RivalRating">Their rating after the match, when known.</param>
/// <param name="Wins">Decided wins, all-time — for the DECIDED cell.</param>
/// <param name="Losses">Decided losses, all-time.</param>
/// <param name="Rd">Glicko rating deviation, for the provisional note.</param>
public sealed record MatchOutcomeView(
    MatchVerdict Verdict,
    string? ModId,
    string? MapName,
    int DurationSeconds,
    int PlayerCount,
    double? RatingBefore,
    double? RatingAfter,
    string? RivalLogin,
    double? RivalRating,
    int Wins,
    int Losses,
    double? Rd)
{
    /// <summary>
    /// Turn a stored per-player score into a verdict.
    ///
    /// <para><b>0.5 is NoResult, never "draw".</b> The backend stores 0.5 whenever the
    /// outcome could not be read — no recording, a team game, a match reported before the
    /// launcher could read one — and those are the majority of stored rows. Labelling them
    /// as drawn games would show, as a fact about the match, something that is only a fact
    /// about our ability to read it.</para>
    ///
    /// <para>The thresholds match the backend's own tally, which counts a win at
    /// <c>&gt;= 0.999</c> and a loss at <c>&lt;= 0.001</c>, so the card and the profile can
    /// never disagree about the same row.</para>
    /// </summary>
    public static MatchVerdict Classify(double result)
        => result >= 0.999 ? MatchVerdict.Win
         : result <= 0.001 ? MatchVerdict.Loss
         : MatchVerdict.NoResult;

    /// <summary>
    /// The rating change to show, or null when there is nothing honest to show.
    ///
    /// <para>Null — not 0 — when either end is missing: "+0" claims the match was played
    /// for nothing, which is a different statement from "we don't know what it did". An
    /// older backend that sends neither value lands here, so it shows no delta rather than
    /// a fabricated one.</para>
    /// </summary>
    public static int? Delta(double? before, double? after)
        => before.HasValue && after.HasValue
            ? (int)Math.Round(after.Value - before.Value, MidpointRounding.AwayFromZero)
            : null;

    /// <summary>
    /// Whether the rating is still provisional — a high Glicko deviation means the server
    /// is not yet confident, so a big swing says less than it looks like.
    /// </summary>
    /// <remarks>
    /// The threshold is the one Glicko itself uses to call a rating settled; new players
    /// start at 350 and fall below this after a handful of decided games.
    /// </remarks>
    public static bool IsProvisional(double? rd) => rd.HasValue && rd.Value > ProvisionalRd;

    /// <summary>Rating deviation above which a rating is still finding its level.</summary>
    public const double ProvisionalRd = 110.0;

    /// <summary>The delta for this outcome, or null when it cannot be stated.</summary>
    public int? RatingDelta => Delta(RatingBefore, RatingAfter);

    /// <summary>
    /// Decided games behind the win rate. Exposed so the card and the profile tab read the
    /// same number from the same place.
    /// </summary>
    public int DecidedGames => PlayerStanding.DecidedGames(Wins, Losses);

    /// <summary>Win rate over DECIDED games, or null when nothing has been decided.</summary>
    public int? WinPercent => PlayerStanding.WinPercent(Wins, Losses);
}
