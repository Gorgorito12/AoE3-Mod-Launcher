using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The Clasificación page's pure rules (design 93): who stands on the podium and in which order,
/// how the table is grouped, and what "Your position" says. What can go wrong silently here is a
/// figure nobody sent: a neighbour invented for a viewer past the server's page, or a "you need N"
/// that leaves him level with the player above instead of ahead.
/// </summary>
public class RankingViewTests
{
    private static List<LeaderboardRow> Ladder(params double[] ratings)
        => ratings.Select((r, i) => new LeaderboardRow
        {
            Rank = i + 1, UserId = "u" + (i + 1), DisplayName = "P" + (i + 1), DiscordUsername = "p" + (i + 1), Rating = r,
        }).ToList();

    // ---------------------------------------------------------------- the podium

    [Fact]
    public void ThePodiumIsDrawnSecondFirstThird()
    {
        var ranked = Ladder(1800, 1700, 1600, 1500);
        Assert.Equal(new[] { 1, 2, 3 }, RankingGroups.Podium(ranked).Select(r => r.Rank));
        Assert.Equal(new[] { 2, 1, 3 }, RankingGroups.PodiumDrawOrder(ranked).Select(r => r.Rank));
    }

    /// <summary>With one or two ranked there is no middle to keep: they stand in the server's order.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void FewerThanThreeStandInOrder(int count)
    {
        var ranked = Ladder(Enumerable.Range(0, count).Select(i => 1800.0 - i * 50).ToArray());
        Assert.Equal(Enumerable.Range(1, count), RankingGroups.PodiumDrawOrder(ranked).Select(r => r.Rank));
    }

    // ---------------------------------------------------------------- the table

    /// <summary>
    /// THE ONE THAT MATTERS: the table starts at the fourth, keeps the server's order and its
    /// places, and loses nobody - every ranked player is on the podium or in exactly one group.
    /// </summary>
    [Theory]
    [InlineData(4)]
    [InlineData(9)]
    [InlineData(12)]
    [InlineData(40)]
    public void THE_ONE_THAT_MATTERS_EveryoneIsOnThePodiumOrInOneGroup(int count)
    {
        var ranked = Ladder(Enumerable.Range(0, count).Select(i => 1800.0 - i * 10).ToArray());
        var groups = RankingGroups.Table(ranked, count);
        var inTable = groups.SelectMany(g => g.Rows).Select(r => r.Rank).ToList();

        Assert.Equal(Enumerable.Range(4, count - 3), inTable);
        foreach (var g in groups)
        {
            Assert.All(g.Rows, r => Assert.Equal(g.Age, RankAges.For(r.Rank, count)));
            Assert.All(g.Rows, r => Assert.InRange(r.Rank, g.FirstPlace, g.LastPlace));
        }
        // One heading per age, the best age first.
        Assert.Equal(groups.Select(g => g.Age).Distinct().Count(), groups.Count);
        Assert.Equal(groups.Select(g => g.Age).OrderByDescending(a => a), groups.Select(g => g.Age));
    }

    /// <summary>A group's range is the age's on the whole ladder, so it stays true when part of
    /// the age is on the podium.</summary>
    [Fact]
    public void AGroupsRangeIsTheAgesOnTheWholeLadder()
    {
        var ranked = Ladder(Enumerable.Range(0, 12).Select(i => 1800.0 - i * 10).ToArray());
        var bounds = RankAges.BoundsFor(12);
        var first = RankingGroups.Table(ranked, 12)[0];
        var age = RankAges.For(4, 12);
        Assert.Equal(age, first.Age);
        var expectedFirst = age switch
        {
            RankAge.Sovereign => 1,
            RankAge.Imperial => bounds[0] + 1,
            RankAge.Industrial => bounds[1] + 1,
            RankAge.Fortress => bounds[2] + 1,
            _ => bounds[3] + 1,
        };
        Assert.Equal(expectedFirst, first.FirstPlace);
        Assert.True(first.FirstPlace <= 4);
    }

    [Fact]
    public void ThreeOrFewerLeaveTheTableEmpty()
    {
        Assert.Empty(RankingGroups.Table(Ladder(1800, 1700, 1600), 3));
        Assert.Empty(RankingGroups.Table(Ladder(), 0));
    }

    // ---------------------------------------------------------------- your position

    [Fact]
    public void ARankedViewerSeesTheOneAboveAndTheOneBelow()
    {
        var ranked = Ladder(1800, 1700, 1650, 1600, 1590);
        var you = YourStandingView.For(ranked, new List<PlacementRow>(), "u4", 5, own: null)!;

        Assert.True(you.Ranked);
        Assert.Equal(4, you.Place);
        Assert.Equal(5, you.Of);
        Assert.Equal(1600, you.Elo);
        Assert.Equal(new[] { 3, 4, 5 }, you.Neighbours.Select(n => n.Place));
        Assert.Equal(new[] { 50, 0, -10 }, you.Neighbours.Select(n => n.Gap));
        Assert.Equal(new[] { false, true, false }, you.Neighbours.Select(n => n.Self));
    }

    /// <summary>
    /// THE ONE THAT MATTERS: "you need N" is one more than the gap. Level on ELO is not ahead, so
    /// the gap alone would promise a place the viewer does not get.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheElosNeededPutYouAheadNotLevel()
    {
        var ranked = Ladder(1800, 1700, 1650, 1600);
        var you = YourStandingView.For(ranked, new List<PlacementRow>(), "u4", 4, own: null)!;
        Assert.Equal(51, you.ToNextPlace);
        Assert.Equal(3, you.NextPlace);

        // Level with the player above: still one point to find, never zero.
        var level = YourStandingView.For(Ladder(1800, 1700, 1700), new List<PlacementRow>(), "u3", 3, own: null)!;
        Assert.Equal(1, level.ToNextPlace);
    }

    [Fact]
    public void TheNextAgeIsNamedOnlyWhenThePlaceAboveWearsAHigherOne()
    {
        var ranked = Ladder(Enumerable.Range(0, 12).Select(i => 1800.0 - i * 10).ToArray());
        for (var place = 2; place <= 12; place++)
        {
            var you = YourStandingView.For(ranked, new List<PlacementRow>(), "u" + place, 12, own: null)!;
            var mine = RankAges.For(place, 12);
            var above = RankAges.For(place - 1, 12);
            Assert.Equal(above > mine ? above : (RankAge?)null, you.NextAge);
        }
    }

    [Fact]
    public void FirstPlaceHasNobodyAboveAndNothingToFind()
    {
        var you = YourStandingView.For(Ladder(1800, 1700), new List<PlacementRow>(), "u1", 2, own: null)!;
        Assert.True(you.First);
        Assert.Equal(0, you.ToNextPlace);
        Assert.Equal(new[] { 1, 2 }, you.Neighbours.Select(n => n.Place));
    }

    /// <summary>Being placed: the progress, the viewer's own number when it is known, no place.</summary>
    [Fact]
    public void AViewerBeingPlacedSeesHisProgress()
    {
        var placing = new List<PlacementRow>
        {
            new() { UserId = "me", DisplayName = "Me", PlacementPlayed = 4, PlacementRequired = 10 },
        };
        var you = YourStandingView.For(Ladder(1800), placing, "me", 1,
            own: new LadderStanding { Rating = 1534.4, PlacementPlayed = 4, PlacementRequired = 10 })!;

        Assert.False(you.Ranked);
        Assert.Equal(RankAge.Discovery, you.Age);
        Assert.Equal(1534, you.Elo);
        Assert.Equal(4, you.PlacementPlayed);
        Assert.Equal(6, you.PlacementLeft);
        Assert.Empty(you.Neighbours);

        // No standing of his own: the progress still shows, and no number is made up.
        var bare = YourStandingView.For(Ladder(1800), placing, "me", 1, own: null)!;
        Assert.Null(bare.Elo);
    }

    /// <summary>
    /// THE ONE THAT MATTERS: a viewer the server's page does not reach gets his place from his own
    /// standing and NO neighbours. The players above and below him are not on the page, and a line
    /// drawn for them would be invented.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_PastTheServersPageThereAreNoNeighbours()
    {
        var ranked = Ladder(1800, 1700, 1600);
        var own = new LadderStanding { Rating = 1420, LadderRank = 57, LadderSize = 300, StreakCurrent = 2 };
        var you = YourStandingView.For(ranked, new List<PlacementRow>(), "far-away", 300, own)!;

        Assert.True(you.Ranked);
        Assert.Equal(57, you.Place);
        Assert.Equal(300, you.Of);
        Assert.Equal(1420, you.Elo);
        Assert.Empty(you.Neighbours);
        Assert.Equal(0, you.ToNextPlace);
        Assert.Equal(RankAges.For(57, 300), you.Age);
    }

    [Fact]
    public void NobodySignedInAndNobodyKnownShowNothing()
    {
        var ranked = Ladder(1800, 1700);
        Assert.Null(YourStandingView.For(ranked, new List<PlacementRow>(), null, 2, own: null));
        Assert.Null(YourStandingView.For(ranked, new List<PlacementRow>(), "", 2, own: null));
        // Signed in, never played a rated match: neither on a list nor with a standing.
        Assert.Null(YourStandingView.For(ranked, new List<PlacementRow>(), "stranger", 2, own: null));
        Assert.Null(YourStandingView.For(ranked, new List<PlacementRow>(), "stranger", 2,
            own: new LadderStanding { Rating = 1500 }));
    }

    // ---------------------------------------------------------------- sizes

    /// <summary>The prototype's clamp()s: the minimums on a laptop, the maximums on a wide screen,
    /// and the second and third pedestals lower than the first.</summary>
    [Fact]
    public void ThePodiumSizesFollowThePageWidth()
    {
        var narrow = RankingPodiumFluid.For(1342);
        Assert.Equal(30, narrow.Pedestal);
        Assert.Equal(36, narrow.Avatar);
        Assert.Equal(17, narrow.EloSize);
        Assert.Equal(13.5, narrow.NameSize);

        var wide = RankingPodiumFluid.For(2528);
        Assert.Equal(52, wide.Pedestal);
        Assert.Equal(64, wide.Avatar);
        Assert.Equal(25, wide.EloSize);

        Assert.True(wide.PedestalOf(1) > wide.PedestalOf(2));
        Assert.True(wide.PedestalOf(2) > wide.PedestalOf(3));
        Assert.True(wide.NumberOf(1) > wide.NumberOf(2));

        // Never laid out: the minimums, not zeros.
        Assert.Equal(narrow, RankingPodiumFluid.For(0));
    }

    /// <summary>Every age has its two colours, and Discovery - no colour of its own - the neutral pair.</summary>
    [Fact]
    public void EveryAgeHasItsColours()
    {
        var seen = new HashSet<string>();
        foreach (var age in System.Enum.GetValues<RankAge>())
        {
            var (main, light) = RankAgeColors.Of(age);
            Assert.Matches("^#[0-9A-F]{6}$", main);
            Assert.Matches("^#[0-9A-F]{6}$", light);
            Assert.True(seen.Add(main), $"{age} shares its colour with another age.");
        }
    }

    // ---------------------------------------------------------------- highlights

    /// <summary>93f: all the cards in one row when each gets 300 px, else two rows, three, two
    /// columns, one.</summary>
    [Theory]
    [InlineData(7, 1888, 16, 4)]   // 1920: seven do not fit in a row, four do
    [InlineData(7, 1342, 12, 4)]   // 1366
    [InlineData(7, 2528, 16, 7)]   // 2560: all seven
    [InlineData(7, 1000, 12, 3)]
    [InlineData(7, 700, 12, 2)]
    [InlineData(7, 400, 12, 1)]
    [InlineData(3, 1888, 16, 3)]
    [InlineData(1, 1888, 16, 1)]
    public void TheHighlightCardsFillTheRowTheyFitIn(int cards, double pageWidth, double gap, int expected)
        => Assert.Equal(expected, MultiplayerTab.LeaderColumns(cards, pageWidth, gap));
}
