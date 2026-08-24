using System;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The community strip's two rules: turning the server's UTC histogram into the viewer's
/// own day, and refusing to call anything a peak hour when there is nothing to go on.
/// </summary>
public class CommunityStatsViewTests
{
    private static int[] Utc(params (int Hour, int Count)[] entries)
    {
        var a = new int[24];
        foreach (var e in entries) a[e.Hour] = e.Count;
        return a;
    }

    [Fact]
    public void HoursShiftIntoTheViewersDay()
    {
        // Latin America: UTC-5. A room opened at 20:00 UTC happened at 15:00 for them,
        // and this is the whole reason the server refuses to guess a timezone.
        var local = CommunityStatsView.ToLocalHours(Utc((20, 7)), TimeSpan.FromHours(-5));
        Assert.Equal(7, local[15]);
        Assert.Equal(0, local[20]);
    }

    [Fact]
    public void ShiftingWestWrapsAroundMidnight_WithoutFallingOffTheArray()
    {
        // 02:00 UTC at UTC-5 is 21:00 the previous day. C#'s % keeps the sign, so a naive
        // (h + shift) % 24 would index -3 here and throw.
        var local = CommunityStatsView.ToLocalHours(Utc((2, 4)), TimeSpan.FromHours(-5));
        Assert.Equal(4, local[21]);
    }

    [Fact]
    public void ShiftingEastWrapsTheOtherWay()
    {
        var local = CommunityStatsView.ToLocalHours(Utc((23, 3)), TimeSpan.FromHours(2));
        Assert.Equal(3, local[1]);
    }

    [Fact]
    public void NoShiftLeavesTheHistogramAlone()
    {
        var local = CommunityStatsView.ToLocalHours(Utc((9, 5), (21, 2)), TimeSpan.Zero);
        Assert.Equal(5, local[9]);
        Assert.Equal(2, local[21]);
    }

    [Fact]
    public void TheBusiestHourIsTheAnswer()
    {
        var local = CommunityStatsView.ToLocalHours(
            Utc((18, 9), (19, 25), (20, 6)), TimeSpan.Zero);
        Assert.Equal(19, CommunityStatsView.PeakHour(local, 40));
    }

    [Fact]
    public void TooSmallASampleHasNoPeakHour()
    {
        // THE case this exists for. A histogram always has a tallest bar; over four rooms
        // that bar means nothing, and printing it under the words "peak hours" dresses
        // noise up as a finding. Below the threshold the card is not shown at all.
        var local = CommunityStatsView.ToLocalHours(Utc((3, 4)), TimeSpan.Zero);
        Assert.Null(CommunityStatsView.PeakHour(local, 4));
    }

    [Fact]
    public void AnEmptyHistogramHasNoPeakHour()
    {
        Assert.Null(CommunityStatsView.PeakHour(new int[24], 100));
        Assert.Null(CommunityStatsView.PeakHour(null, 100));
    }

    [Fact]
    public void RanksComeFromTheServer_NeverRenumberedHere()
    {
        // A client that recomputed the rank after filtering its copy would show the
        // fourth player as the third, and two people looking at the same table would
        // read different numbers.
        var stats = new CommunityStats();
        stats.Leaderboard.Add(new LeaderboardRow { Rank = 2, DisplayName = "b", Rating = 1600 });
        stats.Leaderboard.Add(new LeaderboardRow { Rank = 5, DisplayName = "e", Rating = 1500 });

        var rows = CommunityStatsView.Rows(stats);
        Assert.Equal(2, rows[0].Rank);
        Assert.Equal(5, rows[1].Rank);
    }

    [Fact]
    public void NoStatsIsAnEmptyLadder_NotACrash()
    {
        Assert.Empty(CommunityStatsView.Rows(null));
    }

    [Fact]
    public void TheWinRateDividesByDecidedGames()
    {
        var row = new LeaderboardRow { Wins = 3, Losses = 1, GamesPlayed = 40 };
        // 3 of 4 decided, NOT 3 of 40 played — which would read 8 %.
        Assert.Equal(75, CommunityStatsView.WinPercent(row));
    }

    [Fact]
    public void NothingDecidedIsNoPercentage_NotZero()
    {
        var row = new LeaderboardRow { Wins = 0, Losses = 0, GamesPlayed = 12 };
        Assert.Null(CommunityStatsView.WinPercent(row));
    }
}
