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
        long DownloadSize);

    /// <summary>
    /// The version baked into this assembly at build time (from .csproj Version).
    /// </summary>
    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    /// <summary>
    /// Queries GitHub for the latest release and returns whether an update
    /// is available. Does NOT download anything.
    /// </summary>
    public static async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        var current = CurrentVersion;
        DiagnosticLog.Write($"Launcher self-update check. Current version: {current}");

        try
        {
            var release = await Http.GetFromJsonAsync<GitHubRelease>(GitHubApiUrl, ct);
            if (release == null)
                return NoUpdate(current);

            var remoteVersion = ParseVersion(release.TagName);
            if (remoteVersion == null || remoteVersion <= current)
            {
                DiagnosticLog.Write($"No launcher update needed (remote: {release.TagName}).");
                return NoUpdate(current);
            }

            var asset = FindExeAsset(release);
            if (asset == null)
            {
                DiagnosticLog.Write("Remote release has no .exe asset.");
                return NoUpdate(current);
            }

            DiagnosticLog.Write(
                $"Launcher update available: {current} -> {remoteVersion} " +
                $"({asset.Name}, {asset.Size} bytes)");

            return new UpdateCheckResult(
                UpdateAvailable: true,
                CurrentVersion: current.ToString(3),
                LatestVersion: remoteVersion.ToString(3),
                DownloadUrl: asset.BrowserDownloadUrl,
                DownloadSize: asset.Size);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Launcher update check failed: {ex.Message}");
            return NoUpdate(current);
        }
    }

    /// <summary>
    /// Downloads the new launcher .exe and replaces the current one.
    /// The old binary is renamed to .exe.old (cleaned up on next launch).
    /// After replacing, starts the new binary and returns true so the
    /// caller can shut down the current process.
    /// </summary>
    public static async Task<bool> ApplyUpdateAsync(
        string downloadUrl,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        var currentExe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentExe))
            throw new InvalidOperationException("Cannot determine current executable path.");

        var dir = Path.GetDirectoryName(currentExe)!;
        var newExe = Path.Combine(dir, "WarsOfLibertyLauncher_new.exe");
        var oldExe = currentExe + ".old";

        DiagnosticLog.Write($"Downloading launcher update from: {downloadUrl}");

        // Download to a temp file first
        var downloader = new DownloadService();
        await downloader.DownloadFileAsync(downloadUrl, newExe, progress, ct);

        DiagnosticLog.Write("Download complete. Replacing executable...");

        // Clean up any leftover .old from a previous update
        try { if (File.Exists(oldExe)) File.Delete(oldExe); } catch { }

        // Rename current -> .old, then new -> current
        File.Move(currentExe, oldExe);
        File.Move(newExe, currentExe);

        DiagnosticLog.Write("Executable replaced. Starting new version...");

        Process.Start(new ProcessStartInfo
        {
            FileName = currentExe,
            UseShellExecute = true
        });

        return true;
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

    private static UpdateCheckResult NoUpdate(Version current) =>
        new(false, current.ToString(3), current.ToString(3), null, 0);

    private static Version? ParseVersion(string tag)
    {
        var cleaned = tag.TrimStart('v', 'V');
        return Version.TryParse(cleaned, out var v) ? v : null;
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
