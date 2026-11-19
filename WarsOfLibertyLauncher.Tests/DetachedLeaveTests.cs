using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;
using Phase = WarsOfLibertyLauncher.Services.Multiplayer.RoomMatchState.ResultPhase;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// A room window closed during the result hold disappears at once and the launcher leaves the
/// room behind it later. Leaving too early is what costs a match its result, so the waits are
/// the point — and so is the ceiling, which keeps a stalled chain from holding anybody.
/// </summary>
public class DetachedLeaveTests
{
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheHostStaysUntilHisResultIsSent()
    {
        Assert.False(RoomMatchState.FinishDetachedLeave(isHost: true, Phase.ReadingRecording, false, 3));
        Assert.False(RoomMatchState.FinishDetachedLeave(isHost: true, Phase.SendingResult, false, 12));
        Assert.True(RoomMatchState.FinishDetachedLeave(isHost: true, Phase.None, false, 12));
    }

    [Fact]
    public void TheGuestStaysUntilHisResultArrives()
    {
        Assert.False(RoomMatchState.FinishDetachedLeave(isHost: false, Phase.None, resultKnown: false, 10));
        Assert.True(RoomMatchState.FinishDetachedLeave(isHost: false, Phase.None, resultKnown: true, 10));
        // His own reading still running: the result alone is not enough.
        Assert.False(RoomMatchState.FinishDetachedLeave(isHost: false, Phase.ReadingRecording, resultKnown: true, 10));
    }

    [Theory]
    [InlineData(true, Phase.SendingResult)]
    [InlineData(false, Phase.WaitingForHost)]
    [InlineData(false, Phase.None)]
    public void NobodyIsKeptPastTheHoldsCeiling(bool host, Phase phase)
        => Assert.True(RoomMatchState.FinishDetachedLeave(host, phase, false, RoomMatchState.ResultGraceSeconds));
}
