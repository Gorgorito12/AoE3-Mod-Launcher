using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>What <see cref="SeasonNotice.Plan"/> asks the caller to do.</summary>
public enum SeasonNoticeStep
{
    /// <summary>Nothing to say, and nothing to record.</summary>
    Nothing,

    /// <summary>
    /// Record the running season SILENTLY. The first time a launcher sees the calendar, and
    /// whenever the season it remembers is ahead of the server's — never a bell.
    /// </summary>
    Seed,

    /// <summary>
    /// A season has ended, but the standing in hand still describes the old one: fetch it again
    /// (the final place comes with it) and plan once more when it lands.
    /// </summary>
    NeedStanding,

    /// <summary>Ring the bell for <see cref="SeasonNoticePlan.Ended"/>, then record the season.</summary>
    Ring,
}

/// <param name="Step">What to do.</param>
/// <param name="Current">The running season, as the server stated it.</param>
/// <param name="Ended">The season whose end is being announced (meaningful for <see cref="SeasonNoticeStep.Ring"/>).</param>
/// <param name="Places">The player's final place in it, 1v1 first then teams; empty when they finished on no table.</param>
public sealed record SeasonNoticePlan(
    SeasonNoticeStep Step, int Current, int Ended, IReadOnlyList<PastSeasonEntry> Places);

/// <summary>
/// "Season 1 is over — you finished #3 of 18 in 1v1." The rule for when the bell says so,
/// pure so the traps below are tested rather than remembered.
///
/// <para><b>The latch is one number, <c>LauncherConfig.LastSeenSeason</c></b>: the running
/// season the launcher last saw. A season advancing past it is what rings, and ringing moves it,
/// so a payload read twice — or once before a restart and again after — rings once.</para>
///
/// <para><b>TRAP 1, the flood.</b> The first sight of the calendar records it and says nothing.
/// Without that, everybody who installs the launcher in Season 4 is told that Season 3 ended.
/// Same rule the announcement feed, the catalog listing and the translation index each had to
/// learn.</para>
///
/// <para><b>TRAP 2, seeding from absence.</b> A backend older than seasons sends no calendar,
/// and that must NOT be recorded as anything. A launcher that seeded "no season" would treat the
/// first payload that carries one as a change, and ring for a season that ended before it ever
/// looked.</para>
///
/// <para><b>TRAP 3, the place arrives separately.</b> The final place comes with
/// <c>/matches/elo</c>, not with the calendar, so a season that has just ended has to wait for
/// a standing fetched AFTER it ended — one that names the new season. Ringing earlier would
/// announce the end with no place in it, or with last week's.</para>
/// </summary>
public static class SeasonNotice
{
    private static readonly IReadOnlyList<PastSeasonEntry> None = Array.Empty<PastSeasonEntry>();

    /// <param name="lastSeen">The running season the launcher recorded last; null if it never has.</param>
    /// <param name="season">The calendar from <c>/stats/community</c>; null on an older backend.</param>
    /// <param name="standing">The player's own standing from <c>/matches/elo</c>, if fetched.</param>
    public static SeasonNoticePlan Plan(int? lastSeen, SeasonInfo? season, EloSnapshot? standing)
    {
        // TRAP 2: nothing to read means nothing to do — and above all nothing to record.
        if (season == null || season.Current < 1)
            return new SeasonNoticePlan(SeasonNoticeStep.Nothing, 0, 0, None);

        var current = season.Current;

        // TRAP 1, and its mirror: a remembered season AHEAD of the server's (a config carried
        // over from a test server, a clock that was wrong) is re-recorded silently too. Left as
        // it was, the bell would stay mute until the real calendar caught up with it.
        if (lastSeen is not int seen || seen > current)
            return new SeasonNoticePlan(SeasonNoticeStep.Seed, current, 0, None);

        if (current == seen)
            return new SeasonNoticePlan(SeasonNoticeStep.Nothing, current, 0, None);

        // Away for more than one season: only the most recent end is announced. The older one's
        // result is in the profile, and two bells for one launch would read as a malfunction.
        var ended = current - 1;

        // TRAP 3: the standing must already describe the NEW season, or its history does not
        // contain the season that just ended yet.
        if (standing == null || standing.Season != current)
            return new SeasonNoticePlan(SeasonNoticeStep.NeedStanding, current, ended, None);

        var places = (standing.PastSeasons ?? new List<PastSeasonEntry>())
            .Where(p => p != null && p.Season == ended && p.Place >= 1)
            .OrderBy(p => p.IsTeam ? 1 : 0)
            .ToList();
        return new SeasonNoticePlan(SeasonNoticeStep.Ring, current, ended, places);
    }

    /// <summary>
    /// The bell's wording for a <see cref="SeasonNoticeStep.Ring"/> plan: the title names the
    /// season that ended, and the body says where the player finished — on each ladder he
    /// finished on — and what the new season does to every rating.
    ///
    /// <para>A player who finished on no table gets the same news without a place, never a
    /// "you finished last": he was not on the table, which is a different fact.</para>
    /// </summary>
    public static (string Title, string Body) Text(SeasonNoticePlan plan)
    {
        var title = Strings.Format("NotifSeasonEndedTitle", plan.Ended);
        if (plan.Places.Count == 0)
            return (title, Strings.Format("NotifSeasonEndedBodyStarted", plan.Current));

        var phrases = plan.Places.Select(p => Strings.Format(
            "NotifSeasonEndedPlace", p.Place, p.Size,
            Strings.Get(p.IsTeam ? "MpBadgeModeTeams" : "MpBadgeMode1v1")));
        var joined = string.Join(Strings.Get("NotifSeasonEndedAnd"), phrases);
        return (title, Strings.Format("NotifSeasonEndedBodyPlaces", joined, plan.Current));
    }
}
