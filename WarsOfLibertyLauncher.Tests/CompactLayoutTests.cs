using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The compact layout's switch (design handoff turns 36 and 38-39).
///
/// <para>The switch is a pure function of the window's size and of the answer a moment ago;
/// the HYSTERESIS cases are the ones that matter, because without them a slow drag along a
/// threshold flips the header between its two heights on every pixel.</para>
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
}
