using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// What the startup gate may hand MainWindow.
///
/// <para>A check with no answer — offline, no reply within six seconds, an HTTP error — came back
/// shaped exactly like "nothing newer", and MainWindow, consuming it instead of asking GitHub
/// again, cleared the pending update, collapsed the pill and opened multiplayer. Nothing asked
/// again, so a logon start before the network was up spent the whole tray session that way.</para>
/// </summary>
public class StartupUpdateHandoverTests
{
    private static LauncherUpdateService.UpdateCheckResult Result(bool available) => new(
        UpdateAvailable: available,
        CurrentVersion: "v1.0.15h",
        LatestVersion: available ? "v1.0.15i" : "v1.0.15h",
        DownloadUrl: available ? "https://example.invalid/Aoe3ModLauncher.exe" : null,
        DownloadSize: 0,
        RemoteTag: available ? "v1.0.15i" : "v1.0.15h");

    [Fact]
    public void THE_ONE_THAT_MATTERS_AFailedStartupCheckIsNeverHandedOver()
    {
        var failed = LauncherUpdateService.FailedCheck("v1.0.15h", "W/\"h\"", "v1.0.15h");
        Assert.Null(StartupUpdateGate.HandOver(failed));

        // A real answer goes through untouched, the same instance — "nothing newer" included,
        // because that one IS an answer and re-asking would spend a rate-limited request on it.
        var nothingNewer = Result(false);
        var available = Result(true);
        Assert.Same(nothingNewer, StartupUpdateGate.HandOver(nothingNewer));
        Assert.Same(available, StartupUpdateGate.HandOver(available));
        Assert.Null(StartupUpdateGate.HandOver(null));
    }
}
