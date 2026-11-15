using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>The Statistics page as numbers (design 109, docs/design_14nov).</summary>
public class StatsPageLayoutTests
{
    [Theory]
    [InlineData(1599, 12)]
    [InlineData(1600, 16)]
    public void TheGutterChangesAt1600AndTheRowsAre34(double width, double gutter)
    {
        var layout = StatsPageLayout.For(width);
        Assert.Equal(gutter, layout.Gutter);
        Assert.Equal(34, layout.RowHeight);
    }

    /// <summary>
    /// THE ONE THAT MATTERS. The balance takes the least of three, one column per five
    /// civilizations shown, and what fits at 450 beside the maps (and the matchups when there are
    /// any) - and never fewer than one. 2560 wide with 22 civilizations and no matchups is 109a: 3.
    /// </summary>
    [Theory]
    [InlineData(2560, 22, 0, 3)]
    [InlineData(2560, 12, 0, 2)]    // two columns of six: a third would hold four
    [InlineData(2560, 4, 0, 1)]
    [InlineData(1366, 22, 0, 2)]
    [InlineData(1366, 22, 400, 1)]  // the matchups take the room a second column needed
    [InlineData(900, 22, 0, 1)]     // never below one
    public void THE_ONE_THAT_MATTERS_TheBalanceTakesTheColumnsItCanFill(double width, int civs, double pairs, int columns)
        => Assert.Equal(columns, StatsPageLayout.For(width).BalanceColumns(width, civs, pairs));

    [Fact]
    public void TheBalanceBasisIsItsColumnsAndTheirGaps()
    {
        Assert.Equal(482, StatsPageLayout.BalanceBasis(1));
        Assert.Equal(450 * 3 + 28 * 2 + 32, StatsPageLayout.BalanceBasis(3));
    }

    /// <summary>"Played together" goes beside Matchups only when all four cards fit on one line.</summary>
    [Theory]
    [InlineData(1366, false)]
    [InlineData(1920, true)]
    public void TheAlliesSitBesideTheMatchupsOnlyWhenEverythingFits(double width, bool beside)
        => Assert.Equal(beside, StatsPageLayout.For(width).AlliesBeside(width));

    /// <summary>The row of cards: bases on a line while they fit, the rest shared by grow, a
    /// card wider than the page alone on its line.</summary>
    [Fact]
    public void TheCardsShareTheirLineByGrowAndWrapWhenTheyDoNotFit()
    {
        var items = new List<(double, double)> { (482, 1), (400, 1), (340, 1) };
        var one = FlexGrowPanel.Lines(items, 1262, 12);
        Assert.Single(one);
        Assert.Equal(1262 - 24, one[0].Sum(x => x.Width), 3);

        var two = FlexGrowPanel.Lines(items, 1000, 12);
        Assert.Equal(2, two.Count);
        Assert.Equal(new[] { 0, 1 }, two[0].Select(x => x.Index));
        Assert.Equal(1000, two[1].Single().Width, 3);

        var wide = FlexGrowPanel.Lines(new List<(double, double)> { (1500, 1) }, 900, 12);
        Assert.Equal(900, wide.Single().Single().Width);
    }
}
