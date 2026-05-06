using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Checks GitHub Releases for a newer version of the launcher and applies
/// the update by replacing the running executable.
///
/// Release tags must follow semantic versioning (e.g. "v1.0.0" or "1.0.0").
/// The release must contain a single .exe asset — the new launcher binary.
/// </summary>
public class LauncherUpdateService
{
    private const string GitHubApiUrl =
        "https://api.github.com/repos/Gorgorito12/Updater/releases/latest";

    private static readonly HttpClient Http = CreateHttpClient();

    public record UpdateCheckResult(
        bool UpdateAvailable,
        string CurrentVersion,
        string LatestVersion,
        string? DownloadUrl,
        long DownloadSize,
        string RemoteTag);

    /// <summary>
    /// The AssemblyVersion baked into this binary. Kept for diagnostics only —
    /// update detection is tag-based, not version-based.
    /// </summary>
    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    /// <summary>
    /// Queries GitHub for the latest release. Update detection is tag-based:
    /// the launcher considers an update available when GitHub's latest release
    /// tag differs from <paramref name="lastInstalledTag"/> (and isn't the tag
    /// the user previously dismissed via "Later").
    ///
    /// This decouples the update flow from the binary's AssemblyVersion, which
    /// means publishing a new release is just "upload to GitHub" — no need to
    /// bump csproj or coordinate version numbers.
    /// </summary>
    /// <param name="lastInstalledTag">
    /// The tag of the currently-running launcher (saved after the last
    /// successful self-update). Empty for fresh installs — in that case the
    /// launcher will prompt once and save the tag the user picks.
    /// </param>
    /// <param name="skippedTag">
    /// A tag the user previously dismissed. We won't re-prompt for it.
    /// </param>
    public static async Task<UpdateCheckResult> CheckAsync(
        string? lastInstalledTag = null,
        string? skippedTag = null,
        CancellationToken ct = default)
    {
        DiagnosticLog.Write(
            $"Launcher self-update check. Current tag: '{lastInstalledTag ?? ""}', " +
            $"AssemblyVersion: {CurrentVersion}");

        try
        {
            var release = await Http.GetFromJsonAsync<GitHubRelease>(GitHubApiUrl, ct);
            if (release == null || string.IsNullOrEmpty(release.TagName))
                return NoUpdate(lastInstalledTag);

            var remoteTag = release.TagName;

            // Already on the latest tag.
            if (string.Equals(remoteTag, lastInstalledTag, StringComparison.OrdinalIgnoreCase))
            {
                DiagnosticLog.Write($"Already on latest tag ({remoteTag}); no update.");
                return NoUpdate(lastInstalledTag);
            }

            // The user dismissed this exact tag before.
            if (string.Equals(remoteTag, skippedTag, StringComparison.OrdinalIgnoreCase))
            {
                DiagnosticLog.Write(
                    $"User previously dismissed tag {remoteTag}; skipping prompt.");
                return NoUpdate(lastInstalledTag);
            }

            var asset = FindExeAsset(release);
            if (asset == null)
            {
                DiagnosticLog.Write("Remote release has no .exe asset.");
                return NoUpdate(lastInstalledTag);
            }

            DiagnosticLog.Write(
                $"Launcher update available: {lastInstalledTag ?? "(unknown)"} -> {remoteTag} " +
                $"({asset.Name}, {asset.Size} bytes)");

            return new UpdateCheckResult(
                UpdateAvailable: true,
                CurrentVersion: string.IsNullOrEmpty(lastInstalledTag) ? "—" : lastInstalledTag!,
                LatestVersion: remoteTag,
                DownloadUrl: asset.BrowserDownloadUrl,
                DownloadSize: asset.Size,
                RemoteTag: remoteTag);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Launcher update check failed: {ex.Message}");
            return NoUpdate(lastInstalledTag);
        }
    }

    /// <summary>
    /// Downloads the new launcher .exe to a sibling temp file. Doesn't replace
    /// the running binary yet — call <see cref="RelaunchUpdated"/> after the
    /// user confirms.
    /// </summary>
    public static async Task DownloadUpdateAsync(
        string downloadUrl,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        var currentExe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentExe))
            throw new InvalidOperationException("Cannot determine current executable path.");

        var newExe = GetPendingUpdatePath(currentExe);

        DiagnosticLog.Write($"Downloading launcher update from: {downloadUrl}");
        DiagnosticLog.Write($"  -> {newExe}");

        // Best-effort cleanup of any prior aborted attempt
        try { if (File.Exists(newExe)) File.Delete(newExe); } catch { }

        var downloader = new DownloadService();
        await downloader.DownloadFileAsync(downloadUrl, newExe, progress, ct);

        DiagnosticLog.Write("Launcher update download complete.");
    }

    /// <summary>
    /// Renames the running executable to .old, swaps in the freshly downloaded
    /// one, and starts it. Caller should shut down the current process
    /// immediately after this returns.
    /// </summary>
    public static void RelaunchUpdated()
    {
        var currentExe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentExe))
            throw new InvalidOperationException("Cannot determine current executable path.");

        var newExe = GetPendingUpdatePath(currentExe);
        if (!File.Exists(newExe))
            throw new InvalidOperationException("No pending launcher update was downloaded.");

        var oldExe = currentExe + ".old";

        try { if (File.Exists(oldExe)) File.Delete(oldExe); } catch { }

        DiagnosticLog.Write("Replacing launcher executable...");
        File.Move(currentExe, oldExe);
        File.Move(newExe, currentExe);

        DiagnosticLog.Write("Starting updated launcher...");
        Process.Start(new ProcessStartInfo
        {
            FileName = currentExe,
            UseShellExecute = true
        });
    }

    private static string GetPendingUpdatePath(string currentExe)
    {
        var dir = Path.GetDirectoryName(currentExe)!;
        return Path.Combine(dir, "WarsOfLibertyLauncher_new.exe");
    }

    /// <summary>
    /// Removes leftover .old files from a previous self-update.
    /// Call this early on startup.
    /// </summary>
    public static void CleanupOldVersion()
    {
        var currentExe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentExe)) return;

        var oldExe = currentExe + ".old";
        try
        {
            if (File.Exists(oldExe))
            {
                File.Delete(oldExe);
                DiagnosticLog.Write("Cleaned up old launcher version.");
            }
        }
        catch
        {
            // File may still be locked briefly after startup; ignore.
        }
    }

    private static UpdateCheckResult NoUpdate(string? currentTag)
    {
        var label = string.IsNullOrEmpty(currentTag) ? "—" : currentTag!;
        return new(false, label, label, null, 0, currentTag ?? "");
    }

    private static GitHubAsset? FindExeAsset(GitHubRelease release)
    {
        if (release.Assets == null) return null;
        foreach (var asset in release.Assets)
        {
            if (asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return asset;
        }
        return null;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent", "WarsOfLibertyLauncher");
        client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
        return client;
    }

    // Minimal DTOs for the GitHub Releases API response
    private class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        [JsonPropertyName("assets")]
        public GitHubAsset[]? Assets { get; set; }
    }

    private class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = "";

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }
}
