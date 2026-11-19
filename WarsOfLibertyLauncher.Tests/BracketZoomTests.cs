using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>The bracket's zoom: a small draw fills the panel by itself, a big one still opens at
/// 100 % and scrolls.</summary>
public class BracketZoomTests
{
    [Fact]
    public void THE_ONE_THAT_MATTERS_ASmallDrawFitsItselfButNeverBecomesAPoster()
    {
        // A 4-player draw with its champion: 752 x 200 in a 1500 x 700 panel would fit at ~190 %.
        Assert.Equal(BracketZoom.AutoMax, BracketZoom.Auto(752, 200, 1500, 700));
        // In a narrower panel it fits below the cap.
        Assert.Equal(129, BracketZoom.Auto(752, 200, 1000, 700));
    }

    [Fact]
    public void ABigDrawStillOpensAt100()
    {
        // 32 places: far wider and taller than the panel. Fitting would shrink every name.
        Assert.Equal(100, BracketZoom.Auto(1600, 1900, 1200, 600));
    }

    [Fact]
    public void AnUnknownSizeOpensAt100()
    {
        Assert.Equal(100, BracketZoom.Auto(0, 0, 1200, 600));
        Assert.Equal(100, BracketZoom.Auto(752, 200, 0, 0));
        Assert.Null(BracketZoom.FitPercent(752, 200, 10, 10));
    }

    [Fact]
    public void FitGoesTheWholeRange()
    {
        Assert.Equal(BracketZoom.Max, BracketZoom.Fit(300, 100, 4000, 2000));
        Assert.Equal(BracketZoom.Min, BracketZoom.Fit(5000, 5000, 1000, 600));
        Assert.Equal(77, BracketZoom.Fit(1000, 400, 800, 600));
    }

    [Theory]
    [InlineData(40, 60)]
    [InlineData(60, 60)]
    [InlineData(140, 140)]
    [InlineData(200, 200)]
    [InlineData(260, 200)]
    [InlineData(double.NaN, 100)]
    public void TheManualRangeIs60To200(double asked, double expected) =>
        Assert.Equal(expected, BracketZoom.Clamp(asked));
}
