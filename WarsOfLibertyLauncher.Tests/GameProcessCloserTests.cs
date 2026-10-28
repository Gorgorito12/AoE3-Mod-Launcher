using System;
using System.ComponentModel;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// "Stopped by the launcher" is what keeps a deliberate quit from sending <c>game_exited</c> (a
/// game row would take the player out of the abandonment verdict) and from reading as a crash. It
/// must therefore be TRUE: a kill that was refused — an elevated AoE3 does that to a
/// medium-integrity launcher — leaves a game running whose later exit is NOT ours.
/// </summary>
public class GameProcessCloserTests
{
    /// <summary>Pids no real process will be asked about inside this test run.</summary>
    private const int RefusedPid = 2_000_000_011;
    private const int KilledPid = 2_000_000_013;
    private const int AlreadyGonePid = 2_000_000_017;

    [Fact]
    public void THE_ONE_THAT_MATTERS_AKillThatWasRefusedLeavesNoStamp()
    {
        var stopped = GameProcessCloser.StopCore(
            hasExited: () => false,
            pid: () => RefusedPid,
            kill: () => throw new Win32Exception(5, "Access is denied."),
            waitForExit: _ => { },
            reason: "test");

        Assert.False(stopped);
        Assert.False(GameProcessCloser.WasStoppedByLauncher(RefusedPid));
    }

    [Fact]
    public void AKillThatWentThroughIsStamped()
    {
        var stopped = GameProcessCloser.StopCore(() => false, () => KilledPid, () => { }, _ => { }, "test");

        Assert.True(stopped);
        Assert.True(GameProcessCloser.WasStoppedByLauncher(KilledPid));
    }

    /// <summary>A game that had already closed on its own was not stopped by anybody.</summary>
    [Fact]
    public void AGameThatHadAlreadyExitedIsNotStamped()
    {
        var killed = false;
        var stopped = GameProcessCloser.StopCore(() => true, () => AlreadyGonePid, () => killed = true, _ => { }, "test");

        Assert.True(stopped);
        Assert.False(killed);
        Assert.False(GameProcessCloser.WasStoppedByLauncher(AlreadyGonePid));
    }
}
