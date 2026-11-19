using WarsOfLibertyLauncher.Services;
using Xunit;
using V = WarsOfLibertyLauncher.Services.ModAutoUpdatePolicy.Verdict;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// "Update mods automatically". The refusals are the point, in their pinned order: an automatic
/// update never opens a window, never starts beside a game or in a room, and gives up on a version
/// after two attempts.
/// </summary>
public class ModAutoUpdatePolicyTests
{
    private static ModAutoUpdatePolicy.Facts Ready() => new(
        Enabled: true, ChecksAllowed: true, Offline: false, UpdatePending: true, VersionKnown: true,
        Attempts: 0, Busy: false, GameRunning: false, InMultiplayer: false, NeedsAdmin: false, ShortOnDisk: false);

    [Fact]
    public void WithNothingInTheWayItGoes() => Assert.Equal(V.Go, ModAutoUpdatePolicy.Decide(Ready()));

    [Fact]
    public void THE_ONE_THAT_MATTERS_NeverAWindowNorAGame()
    {
        Assert.Equal(V.NeedsAdmin, ModAutoUpdatePolicy.Decide(Ready() with { NeedsAdmin = true }));
        Assert.Equal(V.ShortOnDisk, ModAutoUpdatePolicy.Decide(Ready() with { ShortOnDisk = true }));
        Assert.Equal(V.GameRunning, ModAutoUpdatePolicy.Decide(Ready() with { GameRunning = true }));
        Assert.Equal(V.InMultiplayer, ModAutoUpdatePolicy.Decide(Ready() with { InMultiplayer = true }));
    }

    [Fact]
    public void OffByDefaultAndSilentOnAMeteredConnection()
    {
        Assert.Equal(V.NotEnabled, ModAutoUpdatePolicy.Decide(Ready() with { Enabled = false }));
        Assert.Equal(V.ChecksOff, ModAutoUpdatePolicy.Decide(Ready() with { ChecksAllowed = false }));
        Assert.False(new WarsOfLibertyLauncher.Models.LauncherConfig().AutoUpdateMods);
    }

    [Fact]
    public void ABrokenReleaseCannotLoop()
    {
        Assert.Equal(V.Go, ModAutoUpdatePolicy.Decide(Ready() with { Attempts = 1 }));
        Assert.Equal(V.GaveUp, ModAutoUpdatePolicy.Decide(Ready() with { Attempts = 2 }));
    }

    [Fact]
    public void AnInstallOfUnknownVersionIsNeverAutomated()
        => Assert.Equal(V.VersionUnknown, ModAutoUpdatePolicy.Decide(Ready() with { VersionKnown = false }));

    [Fact]
    public void TheOrderIsPinned_TheCheapestReasonFirst()
    {
        var all = Ready() with
        {
            Offline = true, GameRunning = true, NeedsAdmin = true, Busy = true,
        };
        Assert.Equal(V.Offline, ModAutoUpdatePolicy.Decide(all));
        Assert.Equal(V.Busy, ModAutoUpdatePolicy.Decide(all with { Offline = false }));
    }
}
