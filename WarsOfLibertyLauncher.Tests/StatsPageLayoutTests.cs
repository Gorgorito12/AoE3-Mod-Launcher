using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>The prototype's CSS for the Statistics page, as numbers (design 98).</summary>
public class StatsPageLayoutTests
{
    [Theory]
    [InlineData(1000, 300)]   // the floor: 24 % would be 240
    [InlineData(1366, 327.84)]
    [InlineData(1920, 380)]   // the ceiling: 24 % would be 460
    [InlineData(2560, 380)]
    public void TheRightColumnIsAClamp(double width, double right)
        => Assert.Equal(right, StatsPageLayout.For(width).RightColumn, 2);

    [Theory]
    [InlineData(1599, 12, 40)]
    [InlineData(1600, 16, 44)]
    public void TheGutterAndTheRowsChangeTogetherAt1600(double width, double gutter, double row)
    {
        var layout = StatsPageLayout.For(width);
        Assert.Equal(gutter, layout.Gutter);
        Assert.Equal(row, layout.RowHeight);
    }

    /// <summary>
    /// <c>repeat(auto-fit, minmax(560px, 1fr))</c>: the two tables share a row only while each
    /// keeps its 560, which on a laptop they cannot.
    /// </summary>
    [Theory]
    [InlineData(1366, false)]
    [InlineData(1500, false)]
    [InlineData(1600, true)]
    [InlineData(1920, true)]
    public void TheTablesShareARowOnlyWhenBothKeepTheirWidth(double width, bool together)
        => Assert.Equal(together, StatsPageLayout.For(width).SideBySide);
}
