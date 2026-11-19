using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The automatic start of a room's countdown. The refusals are the point: a start nobody asked
/// for is worse than a click the host has to make.
/// </summary>
public class AutoStartRuleTests
{
    private static AutoStartRule.Facts Ready() => new(
        IsHost: true, InLobbyPhase: true, Players: 2, NotReady: 0, Seats: 2, GateOpen: true,
        ReadyEpoch: 3, SpentEpoch: 0);

    [Fact]
    public void AFullRoomWithEveryoneReadyStarts() => Assert.True(AutoStartRule.ShouldFire(Ready()));

    [Fact]
    public void THE_ONE_THAT_MATTERS_ACancelledCountdownDoesNotComeStraightBack()
    {
        // The server keeps everyone ready after a cancel; only a new ready change starts again.
        var cancelled = Ready() with { SpentEpoch = 3 };
        Assert.False(AutoStartRule.ShouldFire(cancelled));
        Assert.True(AutoStartRule.ShouldFire(cancelled with { ReadyEpoch = 4 }));
    }

    [Fact]
    public void ARoomThatIsNotFullWaitsForTheHost()
    {
        Assert.False(AutoStartRule.ShouldFire(Ready() with { Players = 3, Seats = 6 }));
        Assert.False(AutoStartRule.ShouldFire(Ready() with { Seats = 0 }));
    }

    [Fact]
    public void NobodyButTheHostStartsAndNeverAlone()
    {
        Assert.False(AutoStartRule.ShouldFire(Ready() with { IsHost = false }));
        Assert.False(AutoStartRule.ShouldFire(Ready() with { Players = 1, Seats = 1 }));
        Assert.False(AutoStartRule.ShouldFire(Ready() with { InLobbyPhase = false }));
    }

    [Fact]
    public void SomebodyNotReadyOrAClosedGateStopsIt()
    {
        Assert.False(AutoStartRule.ShouldFire(Ready() with { NotReady = 1 }));
        Assert.False(AutoStartRule.ShouldFire(Ready() with { GateOpen = false }));
    }
}
