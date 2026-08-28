using WarsOfLibertyLauncher;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The window-size scaler's arithmetic — specifically the dead band at the top of its range.
///
/// <para><b>The bug these pin was invisible on screen until someone resized a window.</b>
/// <c>UiScale.SetTextCrispForScale</c> swaps a whole surface between
/// <c>Display/ClearType/Fixed</c> and <c>Ideal/Grayscale/Animated</c> at <c>scale &lt; 0.999</c>.
/// The Multiplayer tab is driven by <c>min(W/1100, H/560)</c> against a window that is 1100
/// wide by default, so the width term sat at exactly 1.0 with about two pixels of slack.
/// Narrowing the window three pixels turned ClearType off for every glyph on the tab — to buy
/// a 0.2% size reduction that fits no extra content, because these surfaces are laid out with
/// star columns and scroll.</para>
/// </summary>
public class UiScaleTests
{
    // The Multiplayer tab's real reference (MultiplayerTab.xaml.cs) and the default window's
    // content height (700 minus 91 of chrome).
    private const double RefW = 1100, RefH = 560;

    [Fact]
    public void TheDefaultWindowIsExactlyOne()
        => Assert.Equal(1.0, UiScale.Clamp(1100, 609, RefW, RefH));

    /// <summary>
    /// The regression. Three pixels of width used to cost the whole surface its ClearType.
    /// </summary>
    [Theory]
    [InlineData(1097)]   // the three pixels that used to flip it
    [InlineData(1090)]
    [InlineData(1070)]   // 0.973 — still inside the band
    public void AHairNarrowerThanTheReferenceStaysAtOne(double width)
        => Assert.Equal(1.0, UiScale.Clamp(width, 609, RefW, RefH));

    /// <summary>
    /// The band is not a licence to stop scaling: past it the transform does its job, which is
    /// what keeps a small window usable at all.
    /// </summary>
    [Theory]
    [InlineData(1000)]
    [InlineData(950)]
    public void PastTheBandItStillScales(double width)
    {
        var scale = UiScale.Clamp(width, 609, RefW, RefH);
        Assert.True(scale < 1.0, $"expected a real scale, got {scale}");
        Assert.True(scale >= UiScale.MinScale);
    }

    [Fact]
    public void TheFloorStillHolds()
        => Assert.Equal(UiScale.MinScale, UiScale.Clamp(600, 400, RefW, RefH));

    /// <summary>Growing past the reference must never magnify — the 1.0 ceiling.</summary>
    [Fact]
    public void ALargeWindowNeverGrowsPastOne()
        => Assert.Equal(1.0, UiScale.Clamp(2560, 1400, RefW, RefH));

    /// <summary>
    /// A collapsed tab measures zero. Returning a scale from that would divide the surface to
    /// nothing the moment it is shown again.
    /// </summary>
    [Theory]
    [InlineData(0, 609)]
    [InlineData(1100, 0)]
    public void ACollapsedSurfaceIsNeutral(double w, double h)
        => Assert.Equal(1.0, UiScale.Clamp(w, h, RefW, RefH));

    /// <summary>
    /// The height term has fifty pixels of slack where the width has two, so the width is what
    /// decides — worth pinning, because it is why the flip was reachable by a normal resize.
    /// </summary>
    [Fact]
    public void WidthIsTheBindingConstraintAtTheDefaultSize()
    {
        // Same width, far more height: still 1.0, so height was never the limiter.
        Assert.Equal(1.0, UiScale.Clamp(1100, 2000, RefW, RefH));
        // Same height, much less width: now it scales.
        Assert.True(UiScale.Clamp(900, 609, RefW, RefH) < 1.0);
    }
}
