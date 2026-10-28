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
        foreach (var set in new[] { RankingTableLayout.All, RankingTableLayout.WithoutPeak, RankingTableLayout.Narrow })
            Assert.Equal(set.Count, set.Select(c => c.Column).Distinct().Count());
    }

    /// <summary>
    /// Design 59's widths with the PEAK where its W-L stood (the maintainer's call, 2026-10-28):
    /// <c>40px minmax(180px,2fr) minmax(200px,1.3fr) minmax(64px,.45fr)</c> — only the place is
    /// fixed, the rest share the width in proportion and never go under their minimum.
    /// </summary>
    [Fact]
    public void TheWideTableIsTheEloAndThePeak()
    {
        Assert.Equal(
            new[] { RankingColumn.Rank, RankingColumn.Player, RankingColumn.Rating, RankingColumn.Peak },
            RankingTableLayout.All.Select(c => c.Column));
        Assert.Equal(new double?[] { 40, null, null, null }, RankingTableLayout.All.Select(c => c.FixedWidth));
        Assert.Equal(new[] { 2, 1.3, 0.45 }, RankingTableLayout.All.Skip(1).Select(c => c.Star));
        Assert.Equal(new double[] { 180, 200, 64 }, RankingTableLayout.All.Skip(1).Select(c => c.MinWidth));

        // Without a peak from the server: the same first three columns, nothing invented after them.
        Assert.Equal(RankingTableLayout.All.Take(3), RankingTableLayout.WithoutPeak);
    }

    /// <summary>55b: under 600 px the peak goes and the rest narrows — <c>28 · 1fr · 92</c>.</summary>
    [Fact]
    public void UnderSixHundredPixelsTheTableKeepsThreeColumns()
    {
        Assert.Equal(
            new[] { RankingColumn.Rank, RankingColumn.Player, RankingColumn.Rating },
            RankingTableLayout.Narrow.Select(c => c.Column));
        Assert.Equal(new double?[] { 28, null, 92 }, RankingTableLayout.Narrow.Select(c => c.FixedWidth));

        Assert.Same(RankingTableLayout.Narrow, RankingTableLayout.For(599, showPeak: true));
        Assert.Same(RankingTableLayout.Narrow, RankingTableLayout.For(599, showPeak: false));
        Assert.Same(RankingTableLayout.All, RankingTableLayout.For(600, showPeak: true));
        Assert.Same(RankingTableLayout.WithoutPeak, RankingTableLayout.For(600, showPeak: false));
        // Not laid out yet is not narrow: the first draw is the wide table.
        Assert.Same(RankingTableLayout.All, RankingTableLayout.For(0, showPeak: true));
        Assert.Same(RankingTableLayout.WithoutPeak, RankingTableLayout.For(0, showPeak: false));
    }

    /// <summary>
    /// THE ONE THAT MATTERS for a server that sends no peak: its table has three columns, like the
    /// 55b one, and the rows used to decide "narrow" by COUNTING — so it lost its bar and its
    /// "Placement 6/10". Narrow is the 55b set itself, never a count.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_APeaklessTableIsStillTheWideOne()
    {
        Assert.Equal(RankingTableLayout.Narrow.Count, RankingTableLayout.WithoutPeak.Count);
        Assert.False(RankingTableLayout.IsNarrowSet(RankingTableLayout.WithoutPeak));
        Assert.False(RankingTableLayout.IsNarrowSet(RankingTableLayout.All));
        Assert.True(RankingTableLayout.IsNarrowSet(RankingTableLayout.Narrow));
    }

    /// <summary>
    /// THE ONE THAT MATTERS for a wide window: NO column takes the surplus alone. With the name
    /// flexible the rating ended a metre away, with the ELO flexible the bar measured ~1350 px and
    /// the last columns sat at the far edge; in proportion the peak keeps its distance from the
    /// name, at under half the name's share.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheColumnsGrowInProportion()
    {
        var flexible = RankingTableLayout.All.Where(c => c.FixedWidth == null).ToList();
        Assert.Equal(3, flexible.Count);
        var player = flexible.Single(c => c.Column == RankingColumn.Player).Star;
        var rating = flexible.Single(c => c.Column == RankingColumn.Rating).Star;
        var peak = flexible.Single(c => c.Column == RankingColumn.Peak).Star;
        Assert.True(peak < player / 2);
        Assert.True(peak < rating);

        // 55b, narrow: only the name stretches, as before.
        Assert.Equal(RankingColumn.Player, Assert.Single(RankingTableLayout.Narrow, c => c.FixedWidth == null).Column);
    }

    /// <summary>
    /// Design 59's clamp() sizes at its three frames. A "cqw" is a hundredth of the frame's
    /// content width — the ranking page — so 59a's 1366-px frame is a 1338-px page.
    /// </summary>
    [Theory]
    //          page    gap  row  place  name  elo   match
    [InlineData(1338,   14,  48,  54,    13.5, 13,   13)]    // 59a: everything at its minimum
    [InlineData(1892,   19,  48,  54,    13.5, 13,   13)]    // 59b: only the gap has moved
    [InlineData(2532,   25,  58,  63,    15,   14.5, 14)]    // 59c: 32-inch 4K at 150 %
    [InlineData(4000,   28,  62,  68,    16,   16,   15.5)]  // and they stop at their maximums
    public void TheSizesFollowThePageLikeTheHandoffsClamps(
        double page, double gap, double row, double place, double name, double elo, double match)
    {
        var f = RankingTableLayout.Fluid(page);
        Assert.Equal(gap, f.Gap);
        Assert.Equal(row, f.RowHeight);
        Assert.Equal(place, f.PlacementRowHeight);
        Assert.Equal(name, f.NameSize);
        Assert.Equal(elo, f.EloSize);
        Assert.Equal(match, f.MatchLineSize);
    }

    /// <summary>Not laid out yet is the minimums; the type follows the text-size setting.</summary>
    [Fact]
    public void AnUnmeasuredPageIsTheMinimumsAndTheTypeFollowsTheTextSize()
    {
        Assert.Equal(RankingTableLayout.Fluid(1000), RankingTableLayout.Fluid(0));
        Assert.Equal(RankingTableLayout.Fluid(1000), RankingTableLayout.Fluid(double.NaN));
        var big = RankingTableLayout.Fluid(1338, textFactor: 1.25);
        Assert.Equal(17, big.NameSize);   // 13.5 x 1.25 = 16.875, to the half point
        Assert.Equal(48, big.RowHeight);  // heights are minimums: the row grows with its text
    }

    /// <summary>
    /// THE ONE THAT MATTERS for the page: the table and the match list share it 60/40 of what is
    /// left past their 640 / 340 bases (59's flex: 3 1 640 and flex: 2 1 340, 14 apart), and under
    /// 994 px they go one above the other.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_ThePageIsShared60_40()
    {
        var s = RankingTableLayout.Split(1338);
        Assert.False(s.Stacked);
        Assert.Equal(846.4, s.TableWidth, 3);
        Assert.Equal(477.6, s.PanelWidth, 3);
        Assert.Equal(1338, s.TableWidth + RankingTableLayout.SplitGap + s.PanelWidth, 3);

        var wide = RankingTableLayout.Split(2532);
        Assert.Equal(1562.8, wide.TableWidth, 3);
        Assert.Equal(955.2, wide.PanelWidth, 3);

        Assert.False(RankingTableLayout.Split(994).Stacked);
        Assert.True(RankingTableLayout.Split(993).Stacked);
        Assert.Equal(993, RankingTableLayout.Split(993).TableWidth);
        Assert.Equal(0, RankingTableLayout.Split(0).TableWidth);
    }

    /// <summary>The percentage was the only right-aligned column; it went with the W-L.</summary>
    [Fact]
    public void NoColumnIsRightAligned()
    {
        foreach (var set in new[] { RankingTableLayout.All, RankingTableLayout.WithoutPeak, RankingTableLayout.Narrow })
            Assert.DoesNotContain(set, c => c.RightAligned);
    }

    [Fact]
    public void EveryColumnHasAHeaderKey_InBothLanguages()
    {
        foreach (var set in new[] { RankingTableLayout.All, RankingTableLayout.WithoutPeak, RankingTableLayout.Narrow })
        {
            foreach (var c in set)
            {
                var key = RankingTableLayout.HeaderKey(c.Column);
                Assert.False(string.IsNullOrEmpty(key));
                Assert.NotEqual(key, WarsOfLibertyLauncher.Localization.Strings.GetIn(WarsOfLibertyLauncher.Localization.Strings.LangEn, key));
                Assert.NotEqual(key, WarsOfLibertyLauncher.Localization.Strings.GetIn(WarsOfLibertyLauncher.Localization.Strings.LangEs, key));
            }
        }
        // The peak's heading explains itself on hover; the others say enough in one word.
        Assert.Equal("MpRankColPeakTip", RankingTableLayout.HeaderTipKey(RankingColumn.Peak));
        Assert.Null(RankingTableLayout.HeaderTipKey(RankingColumn.Rating));
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
