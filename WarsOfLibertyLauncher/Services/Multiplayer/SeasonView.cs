using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>How a top-3 finish in an ended season is drawn: the place decides the metal.</summary>
public enum SeasonMedal { None, Gold, Silver, Bronze }

/// <summary>
/// The rating seasons as the launcher DRAWS them. Pure, so the rules can be tested and every
/// surface — the ranking's selector, the profile, the medal beside a name — reaches the same
/// answer.
///
/// <para><b>Nothing here works a season out.</b> The calendar, the boundaries and which season a
/// match belongs to are the server's (<c>src/elo/seasons.ts</c>); this only reads what
/// <c>/stats/community</c>, <c>/matches/elo</c> and <c>/stats/season/:n</c> said. A second copy
/// of the calendar in the launcher is exactly the kind of opinion that drifts — and the day it
/// did, the selector would offer a season the server has never heard of.</para>
///
/// <para><b>Every field it reads is null on a backend older than seasons</b>, and every answer
/// then degrades to what the launcher drew before: no selector, no medal, no season in a
/// title. Not knowing is never drawn as "season 1".</para>
/// </summary>
public static class SeasonView
{
    /// <summary>
    /// The seasons the ranking's selector offers, newest first — the running one, then every
    /// ended one. Empty when the server sent no calendar.
    /// </summary>
    public static IReadOnlyList<SeasonListEntry> SelectorEntries(SeasonInfo? season)
    {
        if (season == null || season.Current < 1 || season.List == null)
            return Array.Empty<SeasonListEntry>();
        return season.List
            .Where(s => s != null && s.Number >= 1 && s.Number <= season.Current)
            .GroupBy(s => s.Number)
            .Select(g => g.First())
            .OrderByDescending(s => s.Number)
            .ToList();
    }

    /// <summary>
    /// Whether the selector is drawn at all. A choice with one option is not a choice — and that
    /// is the state the whole first season is in, when "Season 1" is the only thing there is to
    /// pick. The subtitle already names the running season, so nothing is lost by hiding it.
    /// </summary>
    public static bool OffersAChoice(SeasonInfo? season) => SelectorEntries(season).Count >= 2;

    /// <summary>
    /// The season a selection really points at: an ENDED season the server listed, or null for
    /// the running one. A selection that no longer names an ended season — the calendar moved
    /// on, or a stale value from before a restart — reads as the running season rather than as
    /// a table that can never load.
    /// </summary>
    public static int? PastSeasonOrNull(int? selected, SeasonInfo? season)
    {
        if (selected is not int n || season == null || season.Current < 1) return null;
        if (n < 1 || n >= season.Current) return null;
        return SelectorEntries(season).Any(s => s.Number == n) ? n : null;
    }

    /// <summary>
    /// The last day of a season in the VIEWER's time zone: the instant before the exclusive end
    /// the server sent. Its seasons end at 06:00 UTC, which is midnight in Central America, the
    /// small hours of the 1st in Argentina and the morning of the 1st in Spain — so "until 28
    /// Feb" is true in the first two and "until 1 Mar" in the third, and each reader is told
    /// their own. Null when the server's value does not parse.
    /// </summary>
    public static DateTime? LastLocalDay(string? endsAtIso, TimeZoneInfo? zone = null)
    {
        if (string.IsNullOrWhiteSpace(endsAtIso)) return null;
        if (!DateTimeOffset.TryParse(endsAtIso, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var end))
            return null;
        var last = end.AddSeconds(-1);
        return TimeZoneInfo.ConvertTime(last, zone ?? TimeZoneInfo.Local).Date;
    }

    /// <summary>The metal a final place earns. Only the top three have one.</summary>
    public static SeasonMedal MedalFor(int place) => place switch
    {
        1 => SeasonMedal.Gold,
        2 => SeasonMedal.Silver,
        3 => SeasonMedal.Bronze,
        _ => SeasonMedal.None,
    };

    /// <summary>
    /// A medal worth drawing: a real season and a top-3 place. Anything else — including a
    /// title the server sent with a place it should not have — draws nothing, because a medal is
    /// a claim about a finish and a wrong one is worse than none.
    /// </summary>
    public static bool IsDrawable(SeasonTitleInfo? title)
        => title != null && title.Season >= 1 && MedalFor(title.Place) != SeasonMedal.None;

    /// <summary>
    /// One line per ended season and ladder for the profile's SEASONS card: newest season first,
    /// and inside one season the 1v1 ladder before the team one. Lines without a real place are
    /// dropped. Empty when the server sent no history or the player finished none.
    /// </summary>
    public static IReadOnlyList<PastSeasonEntry> ProfileLines(EloSnapshot? standing)
    {
        if (standing?.PastSeasons == null) return Array.Empty<PastSeasonEntry>();
        return standing.PastSeasons
            .Where(p => p != null && p.Season >= 1 && p.Place >= 1)
            .OrderByDescending(p => p.Season)
            .ThenBy(p => p.IsTeam ? 1 : 0)
            .ToList();
    }

    /// <summary>
    /// The player's history scoped to one season, for the profile's rating curve.
    ///
    /// <para><b>Why the curve has to be scoped at all:</b> a season starts everybody halfway back
    /// to 1500, and drawn across that boundary the reset reads as a collapse nobody suffered —
    /// a 2000 player's line would fall 250 points at a single stroke between two matches he
    /// won. Rows the server did not stamp with a season (a backend older than seasons) are kept,
    /// so not knowing never empties the curve.</para>
    /// </summary>
    public static IReadOnlyList<MatchHistoryRow>? RowsOfSeason(
        IReadOnlyList<MatchHistoryRow>? rows, int? season)
    {
        if (rows == null || season is not int n) return rows;
        return rows.Where(r => r.Season == null || r.Season == n).ToList();
    }

    /// <summary>
    /// The profile header's "+12" beside the rating — or nothing, when the match it came from
    /// belongs to an EARLIER season than the rating it would be printed beside.
    ///
    /// <para>On the first day of a season the header shows the soft-reset rating, and the last
    /// match anybody played was in the season before. "1750 +15" would read as fifteen points
    /// gained to reach 1750, which is false: the 1750 came from the reset, and the fifteen were
    /// won on a rating that no longer exists. Same row <see cref="MatchHistoryView.Summarise"/>
    /// took the delta from — the newest rated one that has both ends.</para>
    /// </summary>
    public static int? ScopedDelta(int? delta, IReadOnlyList<MatchHistoryRow>? rows, int? currentSeason)
    {
        if (delta == null || currentSeason is not int season || rows == null) return delta;
        foreach (var row in rows)
        {
            if (!MatchHistoryView.IsRated(row)) continue;
            if (MatchOutcomeView.Delta(row.RatingBefore, row.RatingAfter) == null) continue;
            return row.Season is int s && s != season ? null : delta;
        }
        return delta;
    }
}
