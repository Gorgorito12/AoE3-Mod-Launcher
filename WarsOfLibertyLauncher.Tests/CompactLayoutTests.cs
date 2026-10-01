using System.Collections.Generic;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The compact layout's switch (design handoff turn 36) and the compact header's width fit.
///
/// <para>The switch is a pure function of the window's size and of the answer a moment ago;
/// the HYSTERESIS cases are the ones that matter, because without them a slow drag along a
/// threshold flips the header between one row and two on every pixel.</para>
/// </summary>
public class CompactLayoutTests
{
    [Theory]
    [InlineData(1280, 720, true)]    // the handoff's own laptop
    [InlineData(1100, 700, true)]    // the launcher's default window
    [InlineData(1499, 1000, true)]   // narrow but tall
    [InlineData(1920, 899, true)]    // wide but short — a 1080p panel at 125 %
    [InlineData(1500, 900, false)]   // exactly the thresholds: wide
    [InlineData(1920, 1040, false)]  // a maximised 1080p window at 100 %
    public void FromWideTheThresholdsAreTheHandoffsOwn(double w, double h, bool compact)
        => Assert.Equal(compact, CompactLayout.IsCompact(w, h, wasCompact: false));

    [Fact]
    public void ACompactWindowHasToClearTheBandBeforeItTurnsWide()
    {
        // A few pixels past the threshold is not enough: that is a drag in progress.
        Assert.True(CompactLayout.IsCompact(1503, 1000, wasCompact: true));
        Assert.True(CompactLayout.IsCompact(1600, 905, wasCompact: true));

        Assert.False(CompactLayout.IsCompact(1508, 908, wasCompact: true));
    }

    [Fact]
    public void AWideWindowTurnsCompactTheMomentItCrossesAThreshold()
    {
        Assert.True(CompactLayout.IsCompact(1499.9, 1200, wasCompact: false));
        Assert.True(CompactLayout.IsCompact(2000, 899.9, wasCompact: false));
    }

    [Theory]
    [InlineData(0, 700)]
    [InlineData(1100, 0)]
    [InlineData(double.NaN, 700)]
    [InlineData(-1, -1)]
    public void ASizeThatIsNotLaidOutKeepsThePreviousAnswer(double w, double h)
    {
        Assert.True(CompactLayout.IsCompact(w, h, wasCompact: true));
        Assert.False(CompactLayout.IsCompact(w, h, wasCompact: false));
    }

    // ── the compact header's width fit ─────────────────────────────────────

    private static readonly IReadOnlyList<double> Savings = new double[] { 70, 110, 120, 70, 100, 40 };

    [Fact]
    public void ARowThatFitsGivesUpNothing()
        => Assert.Equal(0, CompactHeaderLayout.ReductionsNeeded(1000, 900, Savings));

    [Fact]
    public void ItGivesUpTheFewestThingsThatMakeItFit()
    {
        // 900 + 48 of drag space against 900: 48 short, and the version chip alone covers it.
        Assert.Equal(1, CompactHeaderLayout.ReductionsNeeded(900, 900, Savings));
        // 200 short: the chip (70) and the pill caption (110) are 180, the wordmark makes it.
        Assert.Equal(3, CompactHeaderLayout.ReductionsNeeded(748, 900, Savings));
    }

    [Fact]
    public void SomethingThatIsNotOnScreenSavesNothingAndIsSkippedPast()
    {
        // No update pill: its slot saves 0, so the count goes one further to cover the same gap.
        var noPill = new double[] { 70, 0, 120, 70, 100, 40 };
        Assert.Equal(3, CompactHeaderLayout.ReductionsNeeded(860, 900, noPill));
    }

    [Fact]
    public void ItNeverAnswersMoreThanThereIsToGiveUp()
        => Assert.Equal(Savings.Count, CompactHeaderLayout.ReductionsNeeded(100, 2000, Savings));

    [Fact]
    public void AnUnmeasuredRowDecidesNothing()
    {
        Assert.Equal(0, CompactHeaderLayout.ReductionsNeeded(0, 2000, Savings));
        Assert.Equal(0, CompactHeaderLayout.ReductionsNeeded(double.NaN, 2000, Savings));
        Assert.Equal(0, CompactHeaderLayout.ReductionsNeeded(900, 2000, new double[0]));
    }

    [Fact]
    public void TheDropOrderNamesEveryReductionOnce()
    {
        var all = System.Enum.GetValues<HeaderReduction>();
        Assert.Equal(all.Length, CompactHeaderLayout.DropOrder.Count);
        Assert.Equal(all.Length, new HashSet<HeaderReduction>(CompactHeaderLayout.DropOrder).Count);
        // The version is the cheapest thing in the row to lose — it is in the brand menu and
        // the brand button's tooltip — and the rating the dearest.
        Assert.Equal(HeaderReduction.VersionChip, CompactHeaderLayout.DropOrder[0]);
        Assert.Equal(HeaderReduction.AccountElo, CompactHeaderLayout.DropOrder[^1]);
    }
}
