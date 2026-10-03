using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>What one mode card of the Profile draws (design 55d-55f).</summary>
public enum ProfileModeShape
{
    /// <summary>No rated match on this ladder: one sentence and nothing else.</summary>
    NoGames,

    /// <summary>Still being placed: the rating with a "?", the segments, what is left.</summary>
    Placement,

    /// <summary>On the table: place, rating, peak and low, the three streak cells.</summary>
    Ranked,

    /// <summary>On the table but 30 days without a rated match: rating, peak and the notice.</summary>
    Inactive,
}

/// <summary>The "current streak" cell of a mode card: what it says, in which colour, and the line
/// under it when the streak ran out for lack of play.</summary>
public sealed record StreakCell(string Text, string BrushKey, StreakView.Look Look, string? Note);

/// <summary>
/// The rules behind the Profile's mode cards (design 55d-55f), kept pure so they are pinned by
/// <c>ProfileModeViewTests</c>.
///
/// <para><b>Everything here reads what the SERVER decided</b> — the place, the placement count,
/// the streak and when it ended, whether the player is inactive. Nothing is worked out on this
/// side; a field the server did not send leaves its part of the card out rather than inventing
/// it.</para>
/// </summary>
public static class ProfileModeView
{
    /// <summary>
    /// Which card a ladder gets. Nothing played → <see cref="ProfileModeShape.NoGames"/>; still
    /// being placed (or the server's place is 0, "not on the table") → placement; inactive →
    /// inactive; otherwise ranked.
    /// </summary>
    public static ProfileModeShape ShapeOf(LadderStanding? standing)
    {
        if (standing == null || standing.GamesPlayed <= 0) return ProfileModeShape.NoGames;
        if (standing.InPlacement || standing.LadderRank == 0) return ProfileModeShape.Placement;
        return standing.Inactive ? ProfileModeShape.Inactive : ProfileModeShape.Ranked;
    }

    /// <summary>Whether the player has no rated match on EITHER ladder — the header then says so,
    /// and no mode card is drawn (55f).</summary>
    public static bool NoGamesAnywhere(LadderStandings? ladders)
        => ShapeOf(ladders?.Default) == ProfileModeShape.NoGames
           && ShapeOf(ladders?.Team) == ProfileModeShape.NoGames;

    /// <summary>
    /// The age a ranked or inactive card names, from the server's place. Null when the place is
    /// unknown: the status line then leaves this mode out rather than guess an age.
    /// </summary>
    public static RankAge? AgeOf(LadderStanding? standing, int? fallbackLadderSize)
    {
        if (standing?.LadderRank is not int rank || rank <= 0) return null;
        var size = standing.LadderSize is > 0 ? standing.LadderSize : fallbackLadderSize;
        return RankAges.For(rank, size);
    }

    /// <summary>
    /// One mode's part of the header's status line: "Industrial en 1v1" or "en posicionamiento en
    /// Equipos". Null when the mode has nothing to say (no matches, or a place nobody sent).
    /// </summary>
    public static string? StatusSegment(LadderStanding? standing, string modeName, int? fallbackLadderSize)
    {
        switch (ShapeOf(standing))
        {
            case ProfileModeShape.Placement:
                return Strings.Format("MpProfileStatusPlacement", modeName);
            case ProfileModeShape.Ranked:
            case ProfileModeShape.Inactive:
                return AgeOf(standing, fallbackLadderSize) is { } age
                    ? Strings.Format("MpProfileStatusRanked", Strings.Get(RankAges.NameKey(age)), modeName)
                    : null;
            default:
                return null;
        }
    }

    /// <summary>
    /// The header's status line: the modes' segments joined with " · ", its first letter in
    /// capitals ("En posicionamiento en 1v1"). Null when no mode had anything to say.
    /// </summary>
    public static string? StatusLine(IEnumerable<string?> segments)
    {
        var parts = segments.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        if (parts.Count == 0) return null;
        var line = string.Join(" · ", parts);
        return char.ToUpper(line[0], Strings.Culture) + line[1..];
    }

    /// <summary>
    /// The current-streak cell: 🔥N from three wins, the bare number for one or two, and "—" for
    /// none — with "Ended 18 Sep: 14 days without playing" under it when that is why (the server
    /// sends the date only then; a streak a loss ended has nothing to explain).
    /// </summary>
    public static StreakCell CurrentStreak(LadderStanding standing, DateTimeOffset now, string language)
    {
        var current = Math.Max(0, standing.StreakCurrent);
        var look = StreakView.LookOf(current);
        string? note = null;
        if (look == StreakView.Look.None
            && StreakView.ExpiredOn(standing.StreakEndedAt) is { } ended)
        {
            note = Strings.Format("MpProfileStreakExpired", ShortDate.Format(ended, now, language));
        }
        return look switch
        {
            StreakView.Look.Flame => new StreakCell("🔥" + current, "MpStreakText", look, null),
            StreakView.Look.Plain => new StreakCell(current.ToString(), "MpTextPrimary", look, null),
            _ => new StreakCell(Strings.Get("MpDash"), "MpTextFade", look, note),
        };
    }

    /// <summary>
    /// How many opponents the "against each opponent" card shows before "See all": four, as
    /// drawn. Everything the server sent is shown once the player asks for it.
    /// </summary>
    public const int HeadToHeadFolded = 4;

    /// <summary>The rows to draw, folded or not.</summary>
    public static IReadOnlyList<HeadToHeadEntry> HeadToHeadRows(LadderStanding? standing, bool expanded)
    {
        var all = standing?.HeadToHead ?? new List<HeadToHeadEntry>();
        return expanded ? all : all.Take(HeadToHeadFolded).ToList();
    }

    /// <summary>
    /// How many opponents the player has met on this ladder: the server's total when it sent one,
    /// otherwise the rows it sent. The "See all N opponents" link quotes it.
    /// </summary>
    public static int HeadToHeadTotal(LadderStanding? standing)
        => Math.Max(standing?.HeadToHeadTotal ?? 0, standing?.HeadToHead?.Count ?? 0);

    /// <summary>The share of the matches against one opponent that were won, 0..1; 0 for none.</summary>
    public static double WinShare(HeadToHeadEntry entry)
        => entry.Games > 0 ? (double)entry.Wins / entry.Games : 0;

    /// <summary>
    /// Which ladder the "against each opponent" card opens on: 1v1, unless 1v1 has nobody and the
    /// team ladder does.
    /// </summary>
    public static bool HeadToHeadOpensOnTeams(LadderStandings? ladders)
        => HeadToHeadTotal(ladders?.Default) == 0 && HeadToHeadTotal(ladders?.Team) > 0;
}

/// <summary>
/// "today", "2 days ago", "3 weeks ago" — how long since a date, in whole local days, for the
/// "against each opponent" card's last-match column. In the LAUNCHER's language.
/// </summary>
public static class RelativeDay
{
    public static string Format(DateTimeOffset when, DateTimeOffset now)
    {
        var days = (now.ToLocalTime().Date - when.ToLocalTime().Date).Days;
        if (days <= 0) return Strings.Get("MpAgoToday");
        if (days == 1) return Strings.Get("MpAgoYesterday");
        if (days < 7) return Strings.Format("MpAgoDays", days);
        if (days < 30)
        {
            var weeks = days / 7;
            return weeks == 1 ? Strings.Get("MpAgoWeek") : Strings.Format("MpAgoWeeks", weeks);
        }
        if (days < 365)
        {
            var months = days / 30;
            return months == 1 ? Strings.Get("MpAgoMonth") : Strings.Format("MpAgoMonths", months);
        }
        var years = days / 365;
        return years == 1 ? Strings.Get("MpAgoYear") : Strings.Format("MpAgoYears", years);
    }

    /// <summary>The same from the server's ISO text; null when it is absent or unreadable.</summary>
    public static string? Format(string? iso, DateTimeOffset now)
        => ShortDate.Parse(iso) is { } when ? Format(when, now) : null;
}
