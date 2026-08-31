using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// The two rules behind the community strip's ranking and peak-hours cards. Pure and
/// WPF-free, like its siblings, so the arithmetic that decides what the player is told
/// can be tested rather than buried in a control builder.
/// </summary>
public static class CommunityStatsView
{
    /// <summary>
    /// A community match reduced to what the strip draws: was it decided, and by whom.
    /// </summary>
    /// <param name="Decided">A clean 1v1 whose winner is known. Everything else is false.</param>
    /// <param name="Winner">The winner's name, or null when <paramref name="Decided"/> is false.</param>
    /// <param name="Loser">The loser's name, same rule.</param>
    public sealed record CommunityMatchLine(bool Decided, string? Winner, string? Loser);

    /// <summary>
    /// Who beat whom in this match — or nobody, which is the usual answer.
    ///
    /// <para><b>Only a two-player match with one winner and one loser is described.</b>
    /// Most stored matches carry 0.5 for everyone because the outcome could not be read,
    /// and past two players a single reported loser does not name a winner at all. In both
    /// cases the caller falls back to naming the mod and the map, which is what the card
    /// has always shown.</para>
    ///
    /// <para>Built on <see cref="MatchParticipantsView.Build"/> so the ordering and the
    /// 0.5 rule live in one place: this method never looks at a raw score itself.</para>
    /// </summary>
    public static CommunityMatchLine Describe(CommunityMatch? match)
    {
        var lines = MatchParticipantsView.Build(match?.Participants, null);
        if (lines.Count != 2) return new CommunityMatchLine(false, null, null);
        if (lines[0].Verdict != MatchVerdict.Win) return new CommunityMatchLine(false, null, null);
        if (lines[1].Verdict != MatchVerdict.Loss) return new CommunityMatchLine(false, null, null);
        return new CommunityMatchLine(true, lines[0].Name, lines[1].Name);
    }

    /// <summary>
    /// How many decided games the ladder demands, or null when the server did not say.
    ///
    /// <para>A backend older than the field deserializes it to 0, and "you get in with 0
    /// decided matches" is worse than saying nothing — it is both wrong and impossible.
    /// The empty-state note is shown only when there is a real number to put in it.</para>
    /// </summary>
    public static int? RequiredDecided(CommunityStats? stats)
        => stats is { MinDecided: > 0 } ? stats.MinDecided : null;

    /// <summary>
    /// The community's recent numbers, or null when this backend does not report them.
    ///
    /// <para>Null is not zero. A backend that predates the field tells us nothing, and
    /// drawing zeroes for it would report a dead community — the same refusal the rating
    /// makes about a rating it could not fetch. Genuine zeroes DO show: "0 matches in 30
    /// days" is a fact, and an unwelcome one is still worth knowing.</para>
    /// </summary>
    public static CommunityTotals? Totals(CommunityStats? stats) => stats?.Totals;

    /// <summary>
    /// The community's last matches, newest first as the server ordered them.
    ///
    /// <para>Never reordered here: the server sorts by when it RECORDED each match, which
    /// is the one timestamp a wrong clock on somebody's PC cannot move.</para>
    /// </summary>
    public static IReadOnlyList<CommunityMatch> RecentMatches(CommunityStats? stats)
        => stats?.RecentMatches ?? new List<CommunityMatch>();

    /// <summary>
    /// Fewest rooms in the window before a peak hour is worth naming.
    ///
    /// <para>The point of the threshold is that a histogram will always have a tallest
    /// bar. Over four rooms that bar means nothing, and drawing it under the words "peak
    /// hours" dresses noise up as a finding — the same refusal the rest of this code
    /// makes about an unknown rating or an unread result. Below the line the card is not
    /// shown at all.</para>
    /// </summary>
    public const int MinSampleRooms = 20;

    /// <summary>
    /// Shift a server histogram into the viewer's own day.
    ///
    /// <para>The server counts in UTC — it has no idea where any given player lives — and
    /// says so in the payload. Here is where that becomes a local hour, because this is
    /// the only side that knows the answer. Without the shift a Latin American player is
    /// told the community peaks four to six hours away from when it actually does.</para>
    ///
    /// <para>Whole hours only. Offsets like India's +5:30 land on the nearest hour, which
    /// is a half-bucket error on a chart whose buckets are an hour wide; every offset in
    /// the Americas is whole.</para>
    /// </summary>
    public static int[] ToLocalHours(IReadOnlyList<int> utcCounts, TimeSpan offset)
    {
        var local = new int[24];
        if (utcCounts == null) return local;

        var shift = (int)Math.Round(offset.TotalHours);
        for (var h = 0; h < 24 && h < utcCounts.Count; h++)
        {
            // (h + shift) can go negative, and C# % keeps the sign — so a player west of
            // UTC would index outside the array. The extra + 24 is what stops that.
            var target = ((h + shift) % 24 + 24) % 24;
            local[target] += utcCounts[h];
        }
        return local;
    }

    /// <summary>
    /// The busiest local hour, or null when there is not enough to go on.
    ///
    /// <para>Null on too small a sample, on an empty histogram, and on a null payload —
    /// all three mean the same thing to the caller, which is "don't show the card".</para>
    /// </summary>
    public static int? PeakHour(int[]? localCounts, int total)
    {
        if (localCounts == null || localCounts.Length == 0) return null;
        if (total < MinSampleRooms) return null;

        var best = -1;
        var bestCount = 0;
        for (var h = 0; h < localCounts.Length; h++)
        {
            if (localCounts[h] > bestCount) { bestCount = localCounts[h]; best = h; }
        }
        return bestCount > 0 ? best : null;
    }

    /// <summary>
    /// The ladder rows worth drawing, in the order the server gave them.
    ///
    /// <para>Deliberately does NOT renumber. The rank is decided by the query that
    /// produced the list, and a client that recomputed it after dropping a row would
    /// report the fourth player as the third — a table that quietly disagrees with itself
    /// between two people looking at it.</para>
    /// </summary>
    /// <summary>
    /// The TEAM ladder's rows, or null when this backend has none.
    ///
    /// <para>Null and empty mean different things and the caller needs both: null is an older
    /// server that never had a team ladder, empty is one that has it and nobody has qualified
    /// yet. The first hides the selector; the second explains itself, like the 1v1 table
    /// does through <see cref="RequiredDecided"/>.</para>
    /// </summary>
    public static IReadOnlyList<LeaderboardRow>? TeamRows(CommunityStats? stats)
        => stats?.LeaderboardTeam;

    public static IReadOnlyList<LeaderboardRow> Rows(CommunityStats? stats)
        => stats?.Leaderboard ?? new List<LeaderboardRow>();

    /// <summary>
    /// The win rate for a ladder row, or null when nothing has been decided.
    ///
    /// <para>Straight through to <see cref="PlayerStanding.WinPercent"/> so the table and
    /// the Profile tab can never state different percentages for the same player, and so
    /// "no decided games" keeps rendering as an empty cell rather than as 0 %.</para>
    /// </summary>
    public static int? WinPercent(LeaderboardRow row)
        => row == null ? null : PlayerStanding.WinPercent(row.Wins, row.Losses);
}
