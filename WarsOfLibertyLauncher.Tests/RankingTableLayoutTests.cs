using System.Linq;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="RankingTableLayout"/> — the one definition of the Clasificación table's
/// columns (design handoff 55a/55b, rating v3), and the rule that turns a rating into the bar
/// beside it.
///
/// <para>The columns used to be a list of literals written twice, in the header builder and in
/// the row builder. The two drifting apart misaligns every row in the table, and it is a break no
/// compile can see and no screenshot on a wide monitor shows.</para>
/// </summary>
public class RankingTableLayoutTests
{
    [Fact]
    public void EveryColumnIsDefinedExactlyOnce()
    {
        foreach (var set in new[] { RankingTableLayout.All, RankingTableLayout.Narrow })
            Assert.Equal(set.Count, set.Select(c => c.Column).Distinct().Count());
    }

    /// <summary>The handoff's own grid: <c>40 · minmax(0,1fr) · 140 · 64 · 52</c>.</summary>
    [Fact]
    public void TheWideTableIsTheHandoffsFiveColumns()
    {
        Assert.Equal(
            new[] { RankingColumn.Rank, RankingColumn.Player, RankingColumn.Rating, RankingColumn.Record, RankingColumn.Percent },
            RankingTableLayout.All.Select(c => c.Column));
        Assert.Equal(new double?[] { 40, null, 140, 64, 52 }, RankingTableLayout.All.Select(c => c.FixedWidth));
    }

    /// <summary>55b: under 600 px W-L and % go and the rest narrows — <c>28 · 1fr · 92</c>.</summary>
    [Fact]
    public void UnderSixHundredPixelsTheTableKeepsThreeColumns()
    {
        Assert.Equal(
            new[] { RankingColumn.Rank, RankingColumn.Player, RankingColumn.Rating },
            RankingTableLayout.Narrow.Select(c => c.Column));
        Assert.Equal(new double?[] { 28, null, 92 }, RankingTableLayout.Narrow.Select(c => c.FixedWidth));

        Assert.Same(RankingTableLayout.Narrow, RankingTableLayout.For(599));
        Assert.Same(RankingTableLayout.All, RankingTableLayout.For(600));
        // Not laid out yet is not narrow: the first draw is the full table.
        Assert.Same(RankingTableLayout.All, RankingTableLayout.For(0));
    }

    /// <summary>Only the NAME stretches: it is the one cell that trims.</summary>
    [Fact]
    public void OnlyThePlayerStretches()
    {
        foreach (var set in new[] { RankingTableLayout.All, RankingTableLayout.Narrow })
            Assert.Equal(RankingColumn.Player, Assert.Single(set, c => c.FixedWidth == null).Column);
    }

    [Fact]
    public void OnlyThePercentageIsRightAligned()
    {
        Assert.Equal(RankingColumn.Percent, Assert.Single(RankingTableLayout.All, c => c.RightAligned).Column);
    }

    [Fact]
    public void EveryColumnHasAHeaderKey()
    {
        foreach (var c in RankingTableLayout.All)
            Assert.False(string.IsNullOrEmpty(RankingTableLayout.HeaderKey(c.Column)));
    }

    // --------------------------------------------------------------- the bar

    /// <summary>
    /// The handoff's bars, measured off its own drawing: proportional to first place, counted from
    /// 1000 — 1748 is the full bar, 1612 about 80 %, 1502 about two thirds.
    /// </summary>
    [Fact]
    public void TheBarMatchesTheHandoffsWidths()
    {
        Assert.Equal(1.0, RankingTableLayout.BarFraction(1748, 1748), 3);
        Assert.InRange(RankingTableLayout.BarFraction(1612, 1748), 0.79, 0.83);
        Assert.InRange(RankingTableLayout.BarFraction(1502, 1748), 0.64, 0.69);
    }

    /// <summary>
    /// THE ONE THAT MATTERS: rating v3 orders the table by the ELO it prints, so the bars descend
    /// with the rows. (The old bar drew rating − 2·rd for a table ordered by that floor.)
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheBarsDescendWithTheTable()
    {
        var ratings = new[] { 1823.0, 1771, 1744, 1702, 1688, 1612, 1590, 1455 };
        var bars = ratings.Select(r => RankingTableLayout.BarFraction(r, ratings[0])).ToList();
        for (var i = 1; i < bars.Count; i++) Assert.True(bars[i] <= bars[i - 1]);
        Assert.Equal(1.0, bars[0], 6);
    }

    [Fact]
    public void TheLastPlaceStillHasABar_AndNoBarLeavesItsTrack()
    {
        Assert.Equal(RankingTableLayout.MinBarFraction, RankingTableLayout.BarFraction(700, 1800));
        Assert.Equal(1.0, RankingTableLayout.BarFraction(1900, 1800));
        // A table whose top is at or under the floor: every bar full rather than a division by zero.
        Assert.Equal(1.0, RankingTableLayout.BarFraction(950, 980));
    }

    // --------------------------------------------------------------- the colour

    /// <summary>The handoff's two colours: green from 50 %, amber under it — and amber for an
    /// INACTIVE player whatever the figure, as the handoff draws El Taita's 52.</summary>
    [Theory]
    [InlineData(100, false, "MpOkTextAlt")]
    [InlineData(50, false, "MpOkTextAlt")]
    [InlineData(49, false, "MpCaution")]
    [InlineData(0, false, "MpCaution")]
    [InlineData(52, true, "MpCaution")]
    public void ThePercentageIsColouredByBand(int percent, bool inactive, string expected)
    {
        Assert.Equal(expected, RankingTableLayout.PercentBrushKey(percent, inactive));
    }
}
