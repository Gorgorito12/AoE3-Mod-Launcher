using System.IO;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Vestige of the legacy Inno-Setup installer flow. The whole download /
/// extract / run-setup pipeline was replaced by <see cref="NativeInstallService"/>;
/// only the temp-dir sweep (<see cref="TryCleanupTemp"/>) and the download pause
/// flag (<see cref="IsPaused"/>) are still wired into the UI. Don't reintroduce
/// the Inno methods — do install work through <see cref="NativeInstallService"/>.
/// </summary>
public class InstallerService
{
    private readonly DownloadService _downloader;

    public InstallerService(DownloadService? downloader = null)
    {
        _downloader = downloader ?? new DownloadService();
    }

    /// <summary>True while a download is paused.</summary>
    public bool IsPaused
    {
        get => _downloader.Pause;
        set => _downloader.Pause = value;
    }

    /// <summary>Where temp files for installation live.</summary>
    public static string TempDirectory =>
        Path.Combine(Path.GetTempPath(), "WarsOfLibertyLauncher", "installer");

    /// <summary>
    /// Best-effort cleanup of the temp folder. Called after a successful
    /// installation, but failure here is non-fatal — Windows will eventually
    /// clear %TEMP% anyway.
    /// </summary>
    public static void TryCleanupTemp()
    {
        try
        {
            if (Directory.Exists(TempDirectory))
                Directory.Delete(TempDirectory, recursive: true);
        }
        catch
        {
            // Files may still be locked by the installer or Windows Explorer
        }
    }
}
