using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Who accepted a quick match (design 117): the server sends a count, never names. The rule only
/// ever marks the rival on that count, and never takes my own answer back.
/// </summary>
public class MatchAcceptViewTests
{
    [Theory]
    [InlineData(0, false, false, false)]
    [InlineData(1, false, false, true)]    // not me, so him
    [InlineData(1, true, true, false)]     // the one is mine
    [InlineData(2, true, true, true)]
    [InlineData(2, false, false, true)]
    public void TheCountBeyondMyOwnAnswerIsTheRivals(int accepted, bool iAccepted, bool me, bool rival)
        => Assert.Equal((me, rival), MatchAcceptView.From(accepted, iAccepted));

    /// <summary>I pressed Accept and the server's frame has not caught up: mine stays marked.</summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_ACountThatLagsNeverUnmarksMe()
        => Assert.Equal((true, false), MatchAcceptView.From(0, iAccepted: true));

    /// <summary>The rival said yes, then I did, and the next frame still says 1: his stays.</summary>
    [Fact]
    public void ARivalSeenAcceptingStaysAccepted()
        => Assert.Equal((true, true), MatchAcceptView.From(1, iAccepted: true, rivalAlready: true));

    [Fact]
    public void TheRivalIsNeverMarkedWithoutTheServersCount()
    {
        Assert.False(MatchAcceptView.From(0, false).Rival);
        Assert.False(MatchAcceptView.From(1, true).Rival);
        Assert.False(MatchAcceptView.From(-3, false).Rival);
    }
}
