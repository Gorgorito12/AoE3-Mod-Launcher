using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// One occupancy bar per seat, and the ping colours, of the rooms list (design handoff turn 36).
/// </summary>
public class RoomCapacityBarsTests
{
    [Theory]
    [InlineData(1, 2, 2, 1, 10)]   // a 1v1 waiting for its opponent
    [InlineData(4, 4, 4, 4, 10)]
    [InlineData(4, 6, 6, 4, 7)]    // above four seats the bars narrow
    [InlineData(2, 8, 8, 2, 7)]
    public void OneBarPerSeat(int current, int max, int bars, int filled, double width)
        => Assert.Equal((bars, filled, width), RoomCapacityBars.Layout(current, max));

    [Fact]
    public void TheEightBarsFitThePlayersColumn()
    {
        var (bars, _, width) = RoomCapacityBars.Layout(0, 8);
        var drawn = bars * width + (bars - 1) * 3;
        Assert.True(drawn <= 88, $"eight seats draw {drawn} px against the 88-px PLAYERS column");
    }

    [Fact]
    public void MorePlayersThanSeatsLightsEveryBarAndNoMore()
        => Assert.Equal((2, 2, 10.0), RoomCapacityBars.Layout(5, 2));

    [Fact]
    public void ARoomWithNoSizeDrawsNothing()
    {
        Assert.Equal(0, RoomCapacityBars.Layout(0, 0).Bars);
        Assert.Equal(0, RoomCapacityBars.Layout(3, -1).Bars);
    }

    [Fact]
    public void ARoomLargerThanEightIsDrawnAsEightProportionalBars_AndOnePlayerStillShows()
    {
        Assert.Equal((8, 4, 7.0), RoomCapacityBars.Layout(6, 12));
        // A room with one player in it must never read as empty.
        Assert.Equal(1, RoomCapacityBars.Layout(1, 40).Filled);
        Assert.Equal(0, RoomCapacityBars.Layout(0, 40).Filled);
    }

    // ── ping colours ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(0.0, PingBand.Good)]
    [InlineData(59.9, PingBand.Good)]
    [InlineData(60.0, PingBand.Medium)]
    [InlineData(119.9, PingBand.Medium)]
    [InlineData(120.0, PingBand.Bad)]
    [InlineData(450.0, PingBand.Bad)]
    public void PingIsBandedAtSixtyAndOneTwenty(double ms, PingBand band)
        => Assert.Equal(band, RoomPingBand.For(ms));

    [Fact]
    public void NoMeasurementIsNoColour()
    {
        Assert.Equal(PingBand.None, RoomPingBand.For(null));
        Assert.Equal(PingBand.None, RoomPingBand.For(-1));
        Assert.Equal(PingBand.None, RoomPingBand.For(double.NaN));
    }
}
