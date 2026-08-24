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
