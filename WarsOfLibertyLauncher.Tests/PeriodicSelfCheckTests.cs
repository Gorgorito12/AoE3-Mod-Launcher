using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The open launcher looks for its own update about every 30 minutes, and a release found then
/// never closes multiplayer under a player who is in a room, a match or a search.
/// </summary>
public class PeriodicSelfCheckTests
{
    [Fact]
    public void THE_ONE_THAT_MATTERS_MultiplayerNeverClosesUnderAMatch()
    {
        Assert.False(LauncherUpdateGate.ShouldCloseNow(inRoom: true, matchActive: false, searching: false));
        Assert.False(LauncherUpdateGate.ShouldCloseNow(inRoom: false, matchActive: true, searching: false));
        Assert.False(LauncherUpdateGate.ShouldCloseNow(inRoom: false, matchActive: false, searching: true));
        Assert.True(LauncherUpdateGate.ShouldCloseNow(false, false, false));
    }

    [Fact]
    public void ThePeriodicCheckRespectsTheSettingAndStaysQuietWhenItCannotHelp()
    {
        Assert.True(LauncherUpdateGate.ShouldCheckPeriodically(true, offline: false, updatePending: false, gameRunning: false));
        Assert.False(LauncherUpdateGate.ShouldCheckPeriodically(false, false, false, false));
        Assert.False(LauncherUpdateGate.ShouldCheckPeriodically(true, offline: true, false, false));
        Assert.False(LauncherUpdateGate.ShouldCheckPeriodically(true, false, updatePending: true, false));
        Assert.False(LauncherUpdateGate.ShouldCheckPeriodically(true, false, false, gameRunning: true));
    }
}
