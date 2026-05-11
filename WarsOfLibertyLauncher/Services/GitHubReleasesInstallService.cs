using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Install / update orchestrator for mods that use the
/// <see cref="ModUpdateMechanism.GitHubReleases"/> mechanism.
///
/// Compared to the WoL pipeline (<c>NativeInstallService</c>, etc.) this
/// is much simpler: a GitHub-Releases mod ships its files as a single
/// .zip asset on a tagged release in the modder's own repo, and the
/// launcher just needs to download + extract that asset over the install
/// folder (typically the AoE3 root for InPlaceOverlay mods).
///
/// Flow:
///   1. Resolve the asset URL + size for the pinned release tag (one
///      GitHub API call).
///   2. Download the .zip into a per-session temp folder (with progress).
///   3. Extract over <paramref name="targetFolder"/> using
///      <see cref="ArchiveService.ExtractZipWithBackupAsync"/> — same
///      backup-and-rollback safety as the WoL .tar.xz path. A failed
///      extract leaves the folder in its pre-install state.
///   4. Clean the temp .zip up.
///
/// Cancellation is supported throughout: each step honours the token and
/// the extract step rolls back on cancel.
/// </summary>
public class GitHubReleasesInstallService
{
    private readonly GitHubReleaseDownloader _downloader;
    private readonly ArchiveService _archive;

    public GitHubReleasesInstallService(
        GitHubReleaseDownloader? downloader = null,
        ArchiveService? archive = null)
    {
        _downloader = downloader ?? new GitHubReleaseDownloader();
        _archive = archive ?? new ArchiveService();
    }

    /// <summary>
    /// Per-session scratch folder. We keep it under the launcher's
    /// existing TEMP namespace so the "Clear temp files" Settings button
    /// wipes it along with the rest.
    /// </summary>
    public static string TempDirectory =>
        Path.Combine(Path.GetTempPath(), "WarsOfLibertyLauncher", "gh-releases");

    /// <summary>
    /// Install / overlay a GitHub-Releases mod into <paramref name="targetFolder"/>.
    ///
    /// The caller is responsible for choosing the target — for an
    /// InPlaceOverlay mod that's the AoE3 install root; for an
    /// IsolatedFolder mod it's the mod-specific folder. Either way the
    /// launcher passes whatever the user picked in the install dialog.
    ///
    /// <paramref name="byteProgress"/> reports running bytesDone /
    /// bytesTotal for the download phase — the launcher's progress panel
    /// can drive a per-file bar off this. The extract phase reports its
    /// own progress via <paramref name="extractProgress"/>.
    /// </summary>
    public async Task InstallAsync(
        ModProfile profile,
        string targetFolder,
        IProgress<string>? status = null,
        IProgress<(long BytesDone, long BytesTotal)>? byteProgress = null,
        IProgress<ArchiveExtractProgress>? extractProgress = null,
        CancellationToken ct = default)
    {
        if (profile.GitHubReleases == null
            || string.IsNullOrEmpty(profile.GitHubReleases.SourceRepo)
            || string.IsNullOrEmpty(profile.GitHubReleases.ApprovedReleaseTag))
        {
            throw new InvalidOperationException(
                $"Profile '{profile.Id}' has UpdateMechanism=GitHubReleases but is missing SourceRepo or ApprovedReleaseTag.");
        }
        if (string.IsNullOrWhiteSpace(targetFolder))
        {
            throw new ArgumentException("Target folder is required.", nameof(targetFolder));
        }

        DiagnosticLog.WriteSection($"GitHubReleases install: {profile.Id}");
        var s = profile.GitHubReleases;

        // 1. Resolve the asset URL + size.
        status?.Report(WarsOfLibertyLauncher.Localization.Strings.Format(
            "StatusDetectingInstall", profile.DisplayName));
        var (url, size) = await _downloader.ResolveAssetAsync(s, ct);

        // 2. Download to a per-mod temp file. Including the mod id +
        //    sanitised tag in the filename keeps two parallel mods from
        //    clobbering each other if a future iteration ever runs
        //    installs in parallel.
        Directory.CreateDirectory(TempDirectory);
        var safeTag = SanitiseForFilename(s.ApprovedReleaseTag);
        var zipPath = Path.Combine(TempDirectory, $"{profile.Id}-{safeTag}.zip");

        status?.Report(WarsOfLibertyLauncher.Localization.Strings.Format(
            "StatusDownloadingPatch", $"{profile.DisplayName} {s.ApprovedReleaseTag}"));
        await _downloader.DownloadAsync(url, zipPath, size, byteProgress, ct);

        // 3. Extract over the target folder with backup-and-rollback.
        //    Backup folder lives next to the zip, scoped per install run
        //    so partial backups don't survive between attempts.
        var backupFolder = Path.Combine(TempDirectory, $"{profile.Id}-{safeTag}.backup");
        if (Directory.Exists(backupFolder))
        {
            try { Directory.Delete(backupFolder, recursive: true); }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"Could not clean stale backup folder: {ex.Message}");
            }
        }

        status?.Report(WarsOfLibertyLauncher.Localization.Strings.Format(
            "StatusExtracting", profile.DisplayName));
        await _archive.ExtractZipWithBackupAsync(
            zipPath, targetFolder, backupFolder, status, extractProgress, ct);

        // 4. Cleanup. Leave the temp zip in place during the install in
        //    case of crash mid-extract (so a retry doesn't re-download
        //    1 GB), then nuke it once we know the extract succeeded.
        //    Backup folder also goes — it served its purpose.
        TryDelete(zipPath);
        TryDeleteDir(backupFolder);

        DiagnosticLog.Write(
            $"GitHubReleases install complete: profile='{profile.Id}' tag='{s.ApprovedReleaseTag}' target='{targetFolder}'");
    }

    /// <summary>Best-effort cleanup of the temp folder. Used by the launcher's general
    /// "clean temp files" maintenance action.</summary>
    public static void TryCleanupTemp()
    {
        TryDeleteDir(TempDirectory);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Could not delete '{path}': {ex.Message}");
        }
    }

    private static void TryDeleteDir(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Could not delete '{path}': {ex.Message}");
        }
    }

    /// <summary>
    /// Strip filesystem-unsafe characters from a release tag so we can use
    /// it in a local filename. Tags like <c>v1.2.3</c> are fine as-is, but
    /// modders sometimes get creative (e.g. <c>v1.0/beta</c>).
    /// </summary>
    private static string SanitiseForFilename(string raw)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(raw.Length);
        foreach (var c in raw)
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        return sb.ToString();
    }
}
