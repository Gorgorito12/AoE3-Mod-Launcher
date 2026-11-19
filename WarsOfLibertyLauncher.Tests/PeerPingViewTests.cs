using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>The ping to each player in the room. The point is never to show a number nobody measured.</summary>
public class PeerPingViewTests
{
    [Fact]
    public void TheViewerAndAPlayerWithoutRadminShowNothing()
    {
        Assert.Equal(PeerPingKind.None, PeerPingView.For(isMe: true, true, 30, 30, 0).Kind);
        Assert.Equal(PeerPingKind.None, PeerPingView.For(false, hasRadminIp: false, 30, 30, 0).Kind);
    }

    [Fact]
    public void AnAnswerShowsItsMillisecondsColourAndBars()
    {
        var fast = PeerPingView.For(false, true, 38, 38, 0);
        Assert.Equal("38 ms", fast.Text);
        Assert.Equal("RmPingGood", fast.BrushKey);
        Assert.Equal(4, fast.LitBars);

        var middling = PeerPingView.For(false, true, 120, 120, 0);
        Assert.Equal("RmPingSlow", middling.BrushKey);
        Assert.Equal(2, middling.LitBars);

        var slow = PeerPingView.For(false, true, 180, 180, 0);
        Assert.Equal("RmPingBad", slow.BrushKey);
        Assert.Equal(1, slow.LitBars);
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_OneLostProbeKeepsTheLastFigureAndTwoShowADash()
    {
        var oneLost = PeerPingView.For(false, true, pingMs: -1, lastGoodMs: 42, consecutiveFails: 1);
        Assert.Equal(PeerPingKind.Answered, oneLost.Kind);
        Assert.Equal("42 ms", oneLost.Text);

        var twoLost = PeerPingView.For(false, true, -1, 42, 2);
        Assert.Equal(PeerPingKind.NoAnswer, twoLost.Kind);
        Assert.Equal("—", twoLost.Text);
        Assert.Equal(0, twoLost.LitBars);
        Assert.Null(twoLost.Ms);
    }

    [Fact]
    public void NothingYetIsMeasuringNotZero()
    {
        var first = PeerPingView.For(false, true, -1, -1, 0);
        Assert.Equal(PeerPingKind.Measuring, first.Kind);
        Assert.Equal("…", first.Text);
        Assert.Equal(PeerPingKind.Measuring, PeerPingView.For(false, true, -1, -1, 1).Kind);
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(60, 4)]
    [InlineData(61, 3)]
    [InlineData(99, 3)]
    [InlineData(100, 2)]
    [InlineData(150, 2)]
    [InlineData(151, 1)]
    public void TheBarsFollowTheRoomsPageCuts(int ms, int bars) => Assert.Equal(bars, PeerPingView.LitBars(ms));
}
