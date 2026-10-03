using System;
using System.Collections.Generic;

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
/// Why the LAUNCHER could not read a result out of this match, when it could not.
///
/// <para>Distinct from the server's <c>unrated_reason</c> and subordinate to it: the
/// server decides whether a match counts, and only the launcher knows why its own
/// reading of a recording failed. Every one of these ends up reported as an all-draws
/// match, so from the outside they are indistinguishable — which is exactly the problem
/// they exist to fix. Until now all five produced the same advice, "tick Record Game",
/// which is right for one of them and sends the player to fix the wrong thing in the
/// other four.</para>
/// </summary>
public enum LocalReadFailure
{
    /// <summary>Nothing went wrong locally — either it was read, or the reason lies
    /// with the server.</summary>
    None,

    /// <summary>The player's own AoE3 profile name could not be read, so there was no
    /// way to find them among the players in their own recording.</summary>
    NoProfileName,

    /// <summary>The room's participants were not known by the time the match ended, so
    /// there was no head count to check the recording against.</summary>
    RosterUnknown,

    /// <summary>No recording of this match was found. The common case, and the only one
    /// where "tick Record Game" is the right thing to say.</summary>
    NoRecordingFound,

    /// <summary>Recordings were found but none could be read — truncated, still being
    /// written, or corrupt.</summary>
    RecordingUnreadable,

    /// <summary>The recording was read and simply does not name a winner this launcher
    /// can use.</summary>
    RecordingAmbiguous,

    /// <summary>
    /// Recordings were found and read PERFECTLY, and none of them is this match.
    ///
    /// <para>Split out of <see cref="NoRecordingFound"/>, where it used to land — so a player
    /// whose recordings are all fine was told the match "was not recorded" and sent to tick a box
    /// that was already ticked. The likeliest cause is worth naming in the message: identifying a
    /// recording needs the player's AoE3 profile name to appear among its players, so a profile
    /// whose name differs from the one they play under fails this way in EVERY match, silently,
    /// for as long as it differs.</para>
    /// </summary>
    RecordingNotOurs,

    /// <summary>
    /// This match's recording was found, but the game never finished writing its outcome — the
    /// last 32 bytes do not carry the signature that precedes the winner.
    ///
    /// <para>Distinct from <see cref="RecordingAmbiguous"/> because the advice differs and is
    /// actionable: leave the match to the main menu before closing AoE3. Measured on a real file
    /// (an 18-minute 1v1) where 5 of the outcome block's 12 bytes had been written.</para>
    /// </summary>
    RecordingNoOutcome,

    /// <summary>
    /// Nothing has failed yet — the match was reported while this player's own AoE3 was still
    /// open, so their recording has not been read.
    ///
    /// <para>Exists because the card used to be built once, at the moment the report arrived, and
    /// a player who left the game open saw "the match was not recorded" for as long as it stayed
    /// open — while their own recording sat on disk naming the winner. It is a WAITING state, not
    /// a failure, and it is replaced when the reading lands.</para>
    /// </summary>
    ReadPending,
}

/// <summary>Which of the result card's cases a match is (design 55j): what the part under the
/// headline says.</summary>
public enum ResultCase
{
    /// <summary>A rated result with nothing more to say.</summary>
    Plain,

    /// <summary>A rated win on a streak of three or more: the 🔥N pill and "N wins in a row".</summary>
    Streak,

    /// <summary>Worth less for repeating the same opponent: the anti-farm sentence.</summary>
    AntiFarm,

    /// <summary>A decided result the server did not rate: NO PUNTUADA and the reason.</summary>
    Unrated,

    /// <summary>Nobody knows who won: the reason, and how to fix it.</summary>
    NoResult,

    /// <summary>The result was kept but moved no rating (a verified crash): the reason.</summary>
    KeptNoMove,

    /// <summary>Still being placed: "Placement 3/10".</summary>
    Placing,

    /// <summary>This match finished the placement: the new badge and the place on the table.</summary>
    PlacementDone,
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
/// <param name="Rd">Glicko rating deviation. Kept for the record; it no longer decides anything on
/// the card — "still settling" is the PLACEMENT now (<see cref="InPlacement"/>), a count of rated
/// matches the player can see, not a deviation nobody can.</param>
/// <param name="UnratedReason">
/// Why the server did not score this match, verbatim from its answer, or null when it
/// did. The launcher deliberately does NOT work this out for itself: the policy of what
/// counts lives on the server, and the last time a copy of it lived here too the two
/// drifted — the card told the player "it counted towards no one's rating" while the
/// backend was rating it. Trailing and defaulted so an older path can leave it out.
/// </param>
/// <param name="LocalFailureDetail">
/// The concrete particulars behind <paramref name="LocalFailure"/>, appended to its message —
/// today the AoE3 profile name that was read and the names the recordings actually carried.
///
/// <para>It is what turns "none of the recordings are yours" from a dead end into something the
/// player can act on: the two names side by side make a profile-name mismatch obvious, and that
/// mismatch fails EVERY match until it is fixed. Kept out of the localized string because it is
/// data, not prose, and it must not be translated.</para>
/// </param>
/// <param name="RecordingPath">
/// The full path of the recording this match was read from, or null when there was none.
///
/// <para><b>It is here because the file's NAME is not an answer.</b> AoE3 calls every recording
/// <c>Record Game N.age3Yrec</c> and renumbers after each match, so the newest is always
/// number 1 — measured on a real bundle where three matches in one evening were each read from
/// a file called <c>Record Game 1</c>. A player told that name goes looking for it after his
/// next match and finds a different game. The path exists so the card can SELECT the file in
/// Explorer, which is the only form of the answer that survives.</para>
///
/// <para>Trailing and defaulted, like the two above, so nothing that builds this without a
/// recording has to say so.</para>
/// </param>
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
    double? Rd,
    string? UnratedReason = null,
    LocalReadFailure LocalFailure = LocalReadFailure.None,
    string? LocalFailureDetail = null,
    string? RecordingPath = null,
    /// <summary>The civilization the player used, as their mod names it, or null. Null is the
    /// ordinary case for every match stored before civs were reported at all.</summary>
    string? MyCiv = null,
    /// <summary>The opponent's, and only in a 1v1 — past two players there is no "the
    /// opponent" to have one.</summary>
    string? RivalCiv = null,
    /// <summary>Which ladder moved: <c>default</c> (1v1) or <c>team</c>. Null on an older backend.</summary>
    string? RatingMode = null,
    /// <summary>The anti-farm factor the server applied (1 = full value, 0.2 the floor), or null.</summary>
    double? EloFactor = null,
    /// <summary>The winner's consecutive wins against this exact opponent, including this one.</summary>
    int? FarmStreak = null,
    /// <summary>Rated matches played in this mode AFTER this one, while in placement.</summary>
    int? PlacementPlayed = null,
    /// <summary>The placement length for this mode (10 in 1v1, 5 in teams).</summary>
    int? PlacementRequired = null,
    /// <summary>True on the one match that finished the player's placement.</summary>
    bool? PlacementCompleted = null,
    /// <summary>The place the player entered the table at, on that match.</summary>
    int? EnteredRank = null,
    /// <summary>Wins in a row in this mode, after this match, when known.</summary>
    int? StreakCurrent = null,
    /// <summary>The sides as the GAME had them, by name, for a teams_mismatch note.</summary>
    IReadOnlyList<IReadOnlyList<string>>? IngameTeamNames = null,
    /// <summary>The opponent's name(s) for the anti-farm loser's note.</summary>
    string? FarmRivalNames = null,
    /// <summary>The room's format as a <c>Strings</c> key ("1v1", "2v2"), or null for a casual room.</summary>
    string? FormatLabelKey = null,
    /// <summary>A team match's two sides by name — ours, then theirs — for "Ana and Luis vs Pedro
    /// and Sara". Null outside a decided team match.</summary>
    IReadOnlyList<string>? OwnSide = null,
    IReadOnlyList<string>? OtherSide = null,
    /// <summary>How many are on the ladder the match moved — cuts the age of the badge a finished
    /// placement shows. Null: the fixed positions, as everywhere a size is unknown.</summary>
    int? LadderSize = null)
{
    /// <summary>A win streak from this length on gets the pill on the card (as on the ladder).</summary>
    public const int StreakPillFrom = StreakView.FlameFrom;

    /// <summary>
    /// Which case the card is (55j), in priority order: finishing placement outranks everything,
    /// then the result nobody could read, a kept result that moved nothing, a decided match the
    /// server refused, the anti-farm discount, a placement match, a streak worth a pill, plain.
    /// </summary>
    public ResultCase Case
    {
        get
        {
            if (FinishedPlacement) return ResultCase.PlacementDone;
            if (Verdict == MatchVerdict.NoResult) return ResultCase.NoResult;
            if (KeptResultButMovedNothing(UnratedReason)) return ResultCase.KeptNoMove;
            if (!string.IsNullOrEmpty(UnratedReason)) return ResultCase.Unrated;
            if (FarmDiscounted && RatingDelta != null) return ResultCase.AntiFarm;
            if (InPlacement) return ResultCase.Placing;
            if (Verdict == MatchVerdict.Win && StreakCurrent is int s && s >= StreakPillFrom) return ResultCase.Streak;
            return ResultCase.Plain;
        }
    }

    /// <summary>Whether the card is drawn as "did not count": dimmer, a grey stripe, "—" for the delta.</summary>
    public bool LooksUnrated => Case is ResultCase.Unrated or ResultCase.NoResult or ResultCase.KeptNoMove;

    /// <summary>
    /// Which explanation to show for a match that did not score.
    ///
    /// <para>The point is that the advice has to fit the cause. "Tick Record Game" is
    /// the right thing to say about a game nobody recorded, and useless about a team
    /// game or a mod with no ladder — recording those changes nothing, and telling
    /// someone otherwise sends them to fix something that was never the problem.</para>
    ///
    /// <para>An unrecognised reason — a server newer than this launcher — falls back
    /// to the recording message, which is the overwhelmingly common cause.</para>
    /// </summary>
    public static string UnratedNoteKey(string? reason, LocalReadFailure local = LocalReadFailure.None)
    {
        // The SERVER's reason wins whenever it is specific. It knows things the launcher
        // does not — whether the mod has a ladder, whether the players were really in the
        // room, whether this recording already scored — and a 2v2 must be told that only
        // 1v1s count even when the recording was also unreadable. Both are true; only one
        // is the thing to change.
        var fromServer = reason switch
        {
            "not_1v1" => "MpResultUnratedTeam",
            // Temporary, unlike every other reason here: the match HAS a winner and is
            // waiting for the other side to send its own reading of the recording. Worth
            // its own message because it is the only one the player can still change, by
            // asking the opponents to leave the launcher open when the game closes.
            "awaiting_confirmation" => "MpResultUnratedAwaitingTeam",
            "mod_not_ranked" => "MpResultUnratedMod",
            "not_competitive" => "MpResultUnratedNotCompetitive",
            "duplicate_recording" => "MpResultUnratedDuplicate",
            "participants_not_in_lobby" => "MpResultUnratedRoster",
            "implausible_timing" => "MpResultUnratedTiming",
            "no_lobby" => "MpResultUnratedNoLobby",
            // The loser's game crashed, verified against Windows, and the server voided the
            // match rather than scoring it. The card shows what the recording said and that
            // none of it moved anybody's rating.
            "game_crashed" => "MpResultUnratedGameCrashed",
            // A match the server founded from a reading and a later reading contradicted.
            "contradicted_founding" => "MpResultUnratedContradicted",
            // Its result arrived after its rating season had ended. The result is kept (and a
            // tournament still advances on it); an ended season's table is final, so nobody's
            // rating moved.
            "season_closed" => "MpResultUnratedSeasonClosed",
            // Rating v3. The room promised one line-up and the recording shows another; the
            // card that has both line-ups words it with names (MpResultTeamsMismatch).
            "teams_mismatch" => "MpResultUnratedTeamsMismatch",
            // A brand-new account, a very short match and an opponent on the same network.
            "new_account_short" => "MpResultNewAccount",
            _ => null,
        };
        if (fromServer != null) return fromServer;

        // Left: "no_decided_result", which says nobody won without saying why — and an
        // older backend, which says nothing at all. Both defer to whatever the launcher
        // learned while trying to read the recording itself.
        //
        // This is NOT the policy moving back to the client. The server still decides
        // WHETHER the match counts; the launcher only explains why its own reading
        // failed, which is the one thing the server cannot know.
        return local switch
        {
            LocalReadFailure.NoProfileName => "MpResultUnratedNoProfile",
            LocalReadFailure.RosterUnknown => "MpResultUnratedNoRoster",
            LocalReadFailure.RecordingUnreadable => "MpResultUnratedUnreadable",
            LocalReadFailure.RecordingAmbiguous => "MpResultUnratedAmbiguous",
            LocalReadFailure.RecordingNotOurs => "MpResultUnratedNotOurs",
            LocalReadFailure.RecordingNoOutcome => "MpResultUnratedNoOutcome",
            // Not a failure: the reading has not happened yet because the player still has
            // AoE3 open. Saying anything about their recording here would be a guess, and the
            // guess this used to make ("it was not recorded") was wrong precisely when their
            // recording was fine.
            LocalReadFailure.ReadPending => "MpResultUnratedReadPending",
            // NoRecordingFound and None both land on the original message, whose advice
            // — turn recording on — is exactly right for them.
            _ => "MpResultNoneBody",
        };
    }

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
    /// Whether this match was played during the player's PLACEMENT — the first 10 rated matches
    /// in 1v1, 5 in teams — and did not finish it.
    ///
    /// <para>It used to be "rd above 110", a deviation the player never sees and that, measured,
    /// stays above 110 for about fourteen matches and for ever for somebody who keeps winning. A
    /// count of matches is a state the player can see the end of.</para>
    /// </summary>
    public bool InPlacement => PlacementCompleted != true
                               && PlacementView.InPlacement(PlacementPlayed, PlacementRequired);

    /// <summary>The server discounted this match for repeating the same opponent.</summary>
    public bool FarmDiscounted => AntiFarmView.IsDiscounted(EloFactor);

    /// <summary>This match finished the placement and put the player on the table.</summary>
    public bool FinishedPlacement => PlacementCompleted == true;

    /// <summary>
    /// Whether the server KEPT this match's result but moved nobody's rating for it — so the card
    /// shows a real verdict AND the note saying why the rating did not change.
    ///
    /// <para>Two reasons do that: <c>season_closed</c> (the result arrived after its season had
    /// ended, and an ended season's table is final) and <c>game_crashed</c> (the loser's game
    /// crashed, verified against Windows, and the rating was voided). Every other reason comes with
    /// no result to keep, and its note is shown in place of a verdict, as before.</para>
    /// </summary>
    public static bool KeptResultButMovedNothing(string? reason)
        => reason is "season_closed" or "game_crashed";

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
