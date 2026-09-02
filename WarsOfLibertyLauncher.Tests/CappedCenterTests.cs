using WarsOfLibertyLauncher.Controls;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins the rule that decides where a width-capped settings column sits.
///
/// <para>The behaviour it belongs to cannot be tested here — it needs a loaded visual tree
/// — but the arithmetic can, and the arithmetic is the part with a decision in it: how much
/// air goes on each side, and the boundary at which the answer is "none".</para>
/// </summary>
public class CappedCenterTests
{
    /// <summary>
    /// THE CASE THAT MATTERS. At the default window size the content area is narrower than
    /// the cap, so an armed element must lay out exactly as it did before the behaviour
    /// existed. A regression here would be invisible in the wide window this was built for
    /// and would quietly re-margin every settings page at its normal size.
    /// </summary>
    [Theory]
    [InlineData(564)]   // the launcher settings window at its 820 default, minus rail and padding
    [InlineData(660)]   // the mod properties window at its 900 default
    [InlineData(860)]   // exactly the cap
    [InlineData(0)]     // before the first layout pass
    public void BelowOrAtTheCapThereIsNoMarginAtAll(double available)
    {
        Assert.Equal(0, CappedCenter.SideMargin(available, 860));
    }

    [Fact]
    public void TheSurplusIsSplitEvenlyBetweenTheTwoSides()
    {
        // A maximised 2560 panel, minus the rail and the content padding.
        Assert.Equal(732, CappedCenter.SideMargin(2324, 860));
        // Twice the margin plus the cap is the whole width back again — which is what
        // "centred" means, and the only property worth asserting about the number.
        Assert.Equal(2324, CappedCenter.SideMargin(2324, 860) * 2 + 860);
    }

    /// <summary>
    /// A negative margin would pull the content OUTSIDE its parent and clip it — the exact
    /// damage this is supposed to prevent — so the floor is not a nicety.
    /// </summary>
    [Theory]
    [InlineData(100, 860)]
    [InlineData(0, 860)]
    [InlineData(-50, 860)]
    public void ItNeverReturnsANegativeMargin(double available, double cap)
    {
        Assert.True(CappedCenter.SideMargin(available, cap) >= 0);
    }

    /// <summary>
    /// A cap of zero means "not armed": the attached property's default. It must be inert
    /// rather than centring against nothing, because every element that never opts in still
    /// runs through the same code path.
    /// </summary>
    [Fact]
    public void AnUnsetCapIsInert()
    {
        Assert.Equal(0, CappedCenter.SideMargin(2560, 0));
        Assert.Equal(0, CappedCenter.SideMargin(2560, -1));
    }

    /// <summary>
    /// WPF hands out an infinite constraint during measure and a NaN width before layout.
    /// Either one arriving here would produce an infinite or NaN margin, which throws when
    /// it reaches a Thickness — at startup, on a window nobody has resized.
    /// </summary>
    [Fact]
    public void AnUnmeasuredWidthIsTreatedAsNoRoom()
    {
        Assert.Equal(0, CappedCenter.SideMargin(double.NaN, 860));
        Assert.Equal(0, CappedCenter.SideMargin(double.PositiveInfinity, 860));
    }
}
