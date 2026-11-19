using System.Collections.Generic;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Progress to the next badge (2026-11-18). The age cuts are pinned against the SAME literal table
/// as the server's src/elo/ages.test.ts: change the shares in one place and this fails. The refusals
/// are the point: two different ages on one screen would be worse than no line.
/// </summary>
public class BadgeProgressViewTests
{
    public static IEnumerable<object[]> Table() => new[]
    {
        new object[] { 1, new[] { 1, 2, 3, 4 } }, new object[] { 2, new[] { 1, 2, 3, 4 } },
        new object[] { 3, new[] { 1, 2, 3, 4 } }, new object[] { 4, new[] { 1, 2, 3, 4 } },
        new object[] { 5, new[] { 1, 2, 3, 4 } }, new object[] { 6, new[] { 1, 2, 3, 5 } },
        new object[] { 7, new[] { 1, 2, 4, 5 } }, new object[] { 8, new[] { 1, 2, 4, 6 } },
        new object[] { 9, new[] { 1, 3, 5, 7 } }, new object[] { 10, new[] { 1, 3, 5, 7 } },
        new object[] { 11, new[] { 2, 3, 5, 8 } }, new object[] { 12, new[] { 2, 3, 6, 9 } },
        new object[] { 13, new[] { 2, 4, 6, 10 } }, new object[] { 14, new[] { 2, 4, 7, 10 } },
        new object[] { 15, new[] { 2, 4, 7, 11 } }, new object[] { 16, new[] { 2, 4, 8, 12 } },
        new object[] { 17, new[] { 2, 5, 8, 12 } }, new object[] { 18, new[] { 2, 5, 9, 13 } },
        new object[] { 19, new[] { 2, 5, 9, 14 } }, new object[] { 20, new[] { 2, 5, 9, 14 } },
        new object[] { 25, new[] { 3, 7, 12, 18 } }, new object[] { 30, new[] { 3, 8, 14, 21 } },
        new object[] { 40, new[] { 4, 10, 18, 28 } }, new object[] { 50, new[] { 5, 13, 23, 35 } },
        new object[] { 60, new[] { 6, 15, 27, 42 } },
    };

    [Theory]
    [MemberData(nameof(Table))]
    public void THE_ONE_THAT_MATTERS_TheAgeCutsMatchTheServers(int size, int[] bounds)
        => Assert.Equal(bounds, RankAges.Bounds(size));

    private static NextBadge Next(string age, int atRank, int places, double floor)
        => new() { Age = age, AtRank = atRank, Places = places, FloorRating = floor };

    [Fact]
    public void PointsAreOneMoreThanTheGap_AndThePrintedSumIsTheThreshold()
    {
        // 18 players: 1-2 / 3-5 / 6-9 / 10-13 / 14-18. Seventh place, Imperial ends at 5.
        var p = BadgeProgressView.For(Next("imperial", 5, 2, 1622.4), 7, 18, 1600, ranked: true, privateRating: false)!;
        Assert.Equal(RankAge.Imperial, p.NextAge);
        Assert.Equal(23, p.Points); // 1622 + 1 - 1600
        Assert.Equal(2, p.Places);
        // Industrial is 6-9: seventh is the third place from its bottom of four.
        Assert.Equal(0.75, p.Fill, 3);
    }

    [Fact]
    public void NeverLessThanOnePoint()
        => Assert.Equal(1, BadgeProgressView.For(Next("imperial", 5, 2, 1590), 7, 18, 1600, true, false)!.Points);

    [Fact]
    public void THE_ONE_THAT_MATTERS_ADisagreementDrawsNothing()
    {
        // The page says seventh, the server's answer was for eighth (a match a minute ago).
        Assert.Null(BadgeProgressView.For(Next("imperial", 5, 3, 1622), 7, 18, 1600, true, false));
        // The server names an age the badge above does not wear.
        Assert.Null(BadgeProgressView.For(Next("sovereign", 5, 2, 1622), 7, 18, 1600, true, false));
        // A ladder of another size moves the cuts.
        Assert.Null(BadgeProgressView.For(Next("imperial", 5, 2, 1622), 7, 30, 1600, true, false));
    }

    [Fact]
    public void NothingWhilePlacingPrivateOrWithoutTheServersAnswer()
    {
        var n = Next("imperial", 5, 2, 1622);
        Assert.Null(BadgeProgressView.For(n, 7, 18, 1600, ranked: false, privateRating: false));
        Assert.Null(BadgeProgressView.For(n, 7, 18, 1600, ranked: true, privateRating: true));
        Assert.Null(BadgeProgressView.For(null, 7, 18, 1600, true, false));
    }
}
