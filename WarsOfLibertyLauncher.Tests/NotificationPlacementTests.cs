using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>Where the bell's panel goes (design 120): under the bell, never off the window.</summary>
public class NotificationPlacementTests
{
    /// <summary>The panel's right edge is the bell's, wherever the bell is: the update pill and
    /// the caption buttons beside it change width.</summary>
    [Theory]
    [InlineData(1400, 1262, 138)]
    [InlineData(1400, 1100, 300)]
    [InlineData(1920, 1782, 138)]
    public void TheRightEdgeIsTheBells(double host, double bellRight, double expected) =>
        Assert.Equal(expected, NotificationPlacement.RightMargin(host, bellRight));

    /// <summary>
    /// THE ONE THAT MATTERS: with the bell far to the left of a narrow window, the panel would
    /// start off the window. It is moved just enough to keep twelve from the left edge.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_ANarrowWindowKeepsTwelveFromTheLeft()
    {
        // Bell's right edge at 380 of a 900 host: 380 - 420 would start at -40.
        var right = NotificationPlacement.RightMargin(900, 380);
        Assert.Equal(900 - 420 - 12, right);
        Assert.Equal(12, 900 - right - NotificationPlacement.PanelWidth);
        // A host narrower than the panel and its gutter: against the right edge, never negative.
        Assert.Equal(0, NotificationPlacement.RightMargin(400, 380));
        Assert.Equal(NotificationPlacement.Gutter, NotificationPlacement.RightMargin(0, 0));
        Assert.Equal(NotificationPlacement.Gutter, NotificationPlacement.RightMargin(double.NaN, 100));
    }

    [Fact]
    public void ItMayGrowToTwelveFromTheBottom()
    {
        Assert.Equal(660 - 6 - 12, NotificationPlacement.MaxHeight(660));
        Assert.Equal(0, NotificationPlacement.MaxHeight(10));
        Assert.Equal(0, NotificationPlacement.MaxHeight(double.NaN));
    }

    /// <summary>The tip is centred under the bell: 14.5 from the panel's right edge when the panel
    /// hangs from a 46-wide bell.</summary>
    [Fact]
    public void TheTipIsUnderTheBell()
    {
        double host = 1400, bellLeft = 1216, bellRight = 1262;
        var panel = NotificationPlacement.RightMargin(host, bellRight);
        var tip = NotificationPlacement.CaretRightMargin(host, bellLeft, bellRight);
        Assert.Equal((46 - NotificationPlacement.CaretWidth) / 2, tip - panel, 3);
        Assert.True(NotificationPlacement.CaretFits(panel, tip));
    }

    /// <summary>A panel pushed away from the bell may no longer have it overhead: then no tip,
    /// rather than one pointing from the panel's corner or from outside it.</summary>
    [Fact]
    public void APushedPanelLosesItsTipWhenTheBellIsNoLongerOverIt()
    {
        double host = 900, bellLeft = 334, bellRight = 380;
        var panel = NotificationPlacement.RightMargin(host, bellRight);
        var tip = NotificationPlacement.CaretRightMargin(host, bellLeft, bellRight);
        // The panel spans 12..432; the bell's centre is at 357: still over it.
        Assert.True(NotificationPlacement.CaretFits(panel, tip));

        // The bell at the far left: its centre is over the panel's rounded corner.
        tip = NotificationPlacement.CaretRightMargin(host, 0, 30);
        Assert.False(NotificationPlacement.CaretFits(NotificationPlacement.RightMargin(host, 30), tip));
    }
}
