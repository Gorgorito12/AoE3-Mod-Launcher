using System.Collections.Generic;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The automatic update of a mod that is NOT on screen decides from that mod's own check, never
/// from the screen's. The refusals are the point: a pinned version, an unknown version and a
/// release already installed must never start a download nobody asked for.
/// </summary>
public class OtherModAutoUpdateTests
{
    private static ModProfile Gh() => new() { Id = "im", UpdateMechanism = ModUpdateMechanism.GitHubReleases };
    private static ModProfile Wol() => new() { Id = "wol", UpdateMechanism = ModUpdateMechanism.WolPatcher };

    private static UpdateService.CheckResult Result(string? current, string? latest, int patches = 0)
    {
        var pending = new List<DownloadInfo>();
        for (var i = 0; i < patches; i++) pending.Add(new DownloadInfo());
        return new UpdateService.CheckResult(new UpdateInfo(),
            current == null ? null : new VersionInfo { Ver = current },
            latest == null ? null : new VersionInfo { Ver = latest },
            pending, IsValidInstall: true);
    }

    [Fact]
    public void AGitHubModBehindIsUpdated()
        => Assert.True(MainWindow.OtherModUpdatePending(Gh(), new ModState(), Result("19.07.2026", "24.07.2026")));

    [Fact]
    public void THE_ONE_THAT_MATTERS_APinnedVersionIsNeverMovedByItself()
        => Assert.False(MainWindow.OtherModUpdatePending(Gh(), new ModState { PinnedVersion = "19.07.2026" },
            Result("19.07.2026", "24.07.2026")));

    [Theory]
    [InlineData(null, "24.07.2026")]
    [InlineData("24.07.2026", "24.07.2026")]
    [InlineData("24.07.2026", null)]
    public void NothingToDoOrNothingKnownIsLeftAlone(string? current, string? latest)
        => Assert.False(MainWindow.OtherModUpdatePending(Gh(), new ModState(), Result(current, latest)));

    [Fact]
    public void AWolPatcherModMovesOnlyWithPatchesToApply()
    {
        Assert.True(MainWindow.OtherModUpdatePending(Wol(), new ModState(), Result("1.2.0d", "1.2.0e", patches: 1)));
        Assert.False(MainWindow.OtherModUpdatePending(Wol(), new ModState(), Result("1.2.0d", "1.2.0e", patches: 0)));
    }
}
