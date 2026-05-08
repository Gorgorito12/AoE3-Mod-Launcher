using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Aggregated progress information sent to the UI during an update operation.
/// Carries enough detail for the UI to render version transitions, per-patch
/// progress, overall progress across all patches, current speed, and ETA.
/// </summary>
public record UpdateProgress(
    string FromVersion,         // version the user is currently on (full chain start)
    string ToVersion,           // version the chain ends at (latest available)
    string PatchFromVersion,    // start version of the patch currently being applied
    string PatchToVersion,      // end version of the patch currently being applied
    int CurrentStep,            // 1-based index of the current patch
    int TotalSteps,             // total number of patches in the chain
    long PatchBytesDone,        // bytes downloaded of current patch
    long PatchBytesTotal,       // total bytes of current patch
    long OverallBytesDone,      // bytes downloaded across ALL patches so far
    long OverallBytesTotal,     // sum of sizes of all patches in the chain
    double BytesPerSecond,      // smoothed download speed
    TimeSpan? Eta);             // estimated time remaining (null if unknown)

/// <summary>
/// Sub-phases the update flow goes through for each individual patch. Drives
/// the mini-breadcrumb shown to the user (3 dots: download → verify → apply)
/// and lets the UI swap the speed label between "Download" and "Apply" so
/// the bytes/sec figure is honestly described.
/// </summary>
public enum UpdatePhase
{
    /// <summary>Idle / not started.</summary>
    None,
    /// <summary>Downloading the .tar.xz from the server.</summary>
    Download,
    /// <summary>Computing CRC32 to confirm the .tar.xz isn't corrupt.</summary>
    Verify,
    /// <summary>Backing up files about to be overwritten + extracting + applying delete list.</summary>
    Apply,
    /// <summary>This patch is finished; ready to start the next one (or all done).</summary>
    Complete,
}

/// <summary>
/// Orchestrates the full update flow, mirroring the original Java updater (v1.4):
///
///   1. Detect WoL install path (registry or config)
///   2. Fetch UpdateInfo.xml (primary, fallback to alt)
///   3. Compute MD5 of three key files (proto/tech/string XMLs)
///   4. Match against known versions to determine the current version
///   5. Determine which downloads (.tar.xz patches) are needed
///   6. For each download:
///       a. If a valid copy already exists locally, skip the download
///       b. Otherwise download with primary/alt fallback + resume
///       c. Verify CRC32
///       d. Extract with backup safety net
///       e. Apply delete list
///       f. Optionally open postUpdatePage in browser
/// </summary>
public class UpdateService
{
    public const string ProtoRelativePath = @"data\protoy.xml";
    public const string TechRelativePath = @"data\techtreey.xml";
    public const string StrRelativePath = @"data\stringtabley.xml";

    private readonly LauncherConfig _config;
    private readonly UpdateInfoService _infoService;
    private readonly DownloadService _downloader;
    private readonly ArchiveService _archive;

    public UpdateService(LauncherConfig config)
    {
        _config = config;
        _infoService = new UpdateInfoService();
        _downloader = new DownloadService();
        _archive = new ArchiveService();
    }

    /// <summary>True while a download is paused.</summary>
    public bool IsPaused
    {
        get => _downloader.Pause;
        set => _downloader.Pause = value;
    }

    /// <summary>The currently detected install path (set during CheckAsync).</summary>
    public string? InstallPath { get; private set; }

    /// <summary>The currently detected mod version (set during CheckAsync).</summary>
    public VersionInfo? CurrentVersion { get; private set; }

    /// <summary>The latest available version on the server.</summary>
    public VersionInfo? LatestVersion { get; private set; }

    /// <summary>Result of a check operation.</summary>
    public record CheckResult(
        UpdateInfo Info,
        VersionInfo? CurrentVersion,
        VersionInfo? LatestVersion,
        List<DownloadInfo> PendingDownloads,
        bool IsValidInstall);

    /// <summary>
    /// Step 1: detect install + fetch manifest + figure out what needs updating.
    /// </summary>
    public async Task<CheckResult> CheckAsync(
        IProgress<string>? status = null,
        CancellationToken ct = default)
    {
        DiagnosticLog.WriteSection("CheckAsync");

        status?.Report(Strings.Get("StatusDetectingInstall"));
        InstallPath = ResolveInstallPath();
        bool valid = !string.IsNullOrEmpty(InstallPath) && RegistryService.IsValidInstall(InstallPath);
        DiagnosticLog.Write($"Install path detected: '{InstallPath}' (valid: {valid})");

        status?.Report(Strings.Get("StatusFetchingManifest"));
        var info = await _infoService.FetchAsync(
            _config.UpdateInfoUrl, _config.UpdateInfoUrlAlt, ct);

        VersionInfo? current = null;
        if (valid)
        {
            status?.Report(Strings.Get("StatusIdentifyingVersion"));
            current = await DetectCurrentVersionAsync(InstallPath!, info.Versions, ct);
        }

        // The XML lists versions in descending order (newest first), so the
        // latest available version is the FIRST element.
        var latest = info.Versions.FirstOrDefault();
        DiagnosticLog.Write($"Detected local version: {current?.Ver ?? "(NO MATCH)"}");
        DiagnosticLog.Write($"Latest server version: {latest?.Ver ?? "(none)"}");
        if (current != null)
            DiagnosticLog.Write($"current.MinReqDownload = {current.MinReqDownload}");

        var pending = ComputePendingDownloads(info, current);
        DiagnosticLog.Write($"Pending downloads: {pending.Count}");
        foreach (var d in pending)
            DiagnosticLog.Write($"  -> id={d.Id} version={d.Version} size={d.Size}");

        CurrentVersion = current;
        LatestVersion = latest;

        return new CheckResult(info, current, latest, pending, valid);
    }

    /// <summary>
    /// Step 2: actually apply all pending downloads, reporting rich progress.
    /// </summary>
    public async Task ApplyUpdatesAsync(
        List<DownloadInfo> downloads,
        IProgress<UpdateProgress>? progress = null,
        IProgress<string>? status = null,
        IProgress<UpdatePhase>? phase = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(InstallPath))
            throw new InvalidOperationException(Strings.Get("ErrInstallPathMissing"));

        var tempDir = Path.Combine(Path.GetTempPath(), "WarsOfLibertyLauncher");
        Directory.CreateDirectory(tempDir);

        // Pre-compute totals so we can show overall progress across all patches.
        long overallBytesTotal = downloads.Sum(d => d.Size);
        long overallBytesDoneFromCompletedPatches = 0;
        var speedTracker = new SpeedTracker();

        string fromVersion = CurrentVersion?.Ver ?? "?";
        string toVersion = LatestVersion?.Ver ?? "?";

        for (int i = 0; i < downloads.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var dl = downloads[i];
            int stepIndex = i;
            int humanStep = i + 1;

            // Resolve the "from" version of this specific patch by looking up
            // the version with MinReqDownload == dl.Id - 1 in the chain.
            // If we can't find one, fall back to the previous patch's target.
            string patchFromVersion = i == 0
                ? fromVersion
                : downloads[i - 1].Version;
            string patchToVersion = string.IsNullOrEmpty(dl.Version) ? "?" : dl.Version;

            var archivePath = Path.Combine(tempDir, $"update_{dl.Id}.tar.xz");

            // Helper that builds an UpdateProgress snapshot reflecting the
            // current state of the operation. Captured by reference in the
            // download callback below.
            void Report(long patchBytesDone, long patchBytesTotal)
            {
                long overallDone = overallBytesDoneFromCompletedPatches + patchBytesDone;
                speedTracker.Sample(overallDone);
                var eta = speedTracker.EstimateTimeRemaining(overallBytesTotal - overallDone);
                progress?.Report(new UpdateProgress(
                    FromVersion: fromVersion,
                    ToVersion: toVersion,
                    PatchFromVersion: patchFromVersion,
                    PatchToVersion: patchToVersion,
                    CurrentStep: humanStep,
                    TotalSteps: downloads.Count,
                    PatchBytesDone: patchBytesDone,
                    PatchBytesTotal: patchBytesTotal,
                    OverallBytesDone: overallDone,
                    OverallBytesTotal: overallBytesTotal,
                    BytesPerSecond: speedTracker.BytesPerSecond,
                    Eta: eta));
            }

            // ---- 1. Skip download if a valid .tar.xz already exists ----
            // Saves bandwidth and time when a previous run downloaded the file
            // but crashed or was cancelled before extraction completed.
            bool needsDownload = true;
            if (File.Exists(archivePath) && !string.IsNullOrEmpty(dl.Crc32))
            {
                phase?.Report(UpdatePhase.Verify);
                status?.Report(Strings.Format("StatusVerifyingExisting", dl.Id));
                var existingCrc = await HashService.ComputeCrc32Async(archivePath, ct);
                if (CrcMatches(existingCrc, dl.Crc32))
                {
                    DiagnosticLog.Write(
                        $"Skipping download of #{dl.Id}: already present locally with valid CRC32.");
                    needsDownload = false;
                    Report(dl.Size, dl.Size);
                }
                else
                {
                    DiagnosticLog.Write(
                        $"Local file #{dl.Id} has invalid CRC ({existingCrc} vs {dl.Crc32}); will re-download.");
                    File.Delete(archivePath);
                }
            }

            // ---- 2. Download (if needed) ----
            if (needsDownload)
            {
                phase?.Report(UpdatePhase.Download);
                status?.Report(Strings.Format("StatusDownloading", dl.Id, patchToVersion));

                var dlProgress = new Progress<DownloadProgress>(p =>
                    Report(p.BytesReceived, Math.Max(p.TotalBytes, dl.Size)));

                await _downloader.DownloadWithFallbackAsync(
                    dl.Link, dl.AltLink, archivePath, dlProgress, ct);

                // ---- 3. Verify CRC32 of the freshly downloaded file ----
                phase?.Report(UpdatePhase.Verify);
                status?.Report(Strings.Format("StatusVerifyingDownload", dl.Id));
                if (!string.IsNullOrEmpty(dl.Crc32))
                {
                    var actual = await HashService.ComputeCrc32Async(archivePath, ct);
                    if (!CrcMatches(actual, dl.Crc32))
                    {
                        throw new InvalidDataException(
                            Strings.Format("ErrCorruptDownload", dl.Id, dl.Crc32, actual));
                    }
                }
            }

            // ---- 4. Extract with backup ----
            phase?.Report(UpdatePhase.Apply);
            status?.Report(Strings.Format("StatusApplying", dl.Id));
            var backupDir = Path.Combine(InstallPath, $"upd_backup_{dl.Id}");

            var extractProgress = new Progress<string>(s => status?.Report(s));
            await _archive.ExtractTarXzWithBackupAsync(
                archivePath, InstallPath, backupDir, extractProgress, ct);

            // ---- 5. Apply delete list ----
            if (!string.IsNullOrEmpty(dl.DeleteList))
            {
                try
                {
                    status?.Report(Strings.Format("StatusCleanup", dl.Id));
                    var deleteListContent = await _downloader.DownloadStringAsync(dl.DeleteList, ct);
                    ArchiveService.ApplyDeleteList(InstallPath, deleteListContent);
                }
                catch
                {
                    // Non-fatal: the original updater also tolerates delete-list failures.
                }
            }

            // ---- 6. Cleanup ----
            try
            {
                File.Delete(archivePath);
                if (Directory.Exists(backupDir))
                    Directory.Delete(backupDir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup; don't fail the update over leftover temp files.
            }

            // ---- 7. Roll forward overall progress and open post-update page ----
            overallBytesDoneFromCompletedPatches += dl.Size;
            Report(0, 0);   // signal that this patch is fully done
            phase?.Report(UpdatePhase.Complete);

            if (_config.OpenPostUpdatePages
                && !string.IsNullOrEmpty(dl.PostUpdatePage)
                && (dl.PostUpdatePage.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || dl.PostUpdatePage.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = dl.PostUpdatePage,
                        UseShellExecute = true
                    });
                }
                catch
                {
                    // Browser launch is non-critical
                }
            }
        }

        // ---- Post-update: refresh translation snapshot + re-apply active pack ----
        // The patches we just applied have overwritten data\stringtabley.xml and
        // data\unithelpstringsy.xml with the latest English versions. Capture
        // those as the new canonical snapshot, then if the user had a translation
        // active, re-apply it on top so they don't fall back to English silently.
        try
        {
            var translations = new TranslationService(InstallPath);
            translations.RefreshOriginalsSnapshot();

            if (!string.IsNullOrEmpty(_config.ActiveTranslationId))
            {
                var manifest = translations.GetInstalled(_config.ActiveTranslationId);
                if (manifest != null)
                {
                    var compat = translations.CheckCompatibility(manifest, LatestVersion?.Ver);
                    if (compat == CompatibilityResult.Unknown)
                    {
                        DiagnosticLog.Write(
                            $"Translation '{manifest.Id}' may be incompatible with the new mod " +
                            "version; reverting to English. User will need an updated pack.");
                        _config.ActiveTranslationId = "";
                        _config.Save();
                    }
                    else
                    {
                        var apply = translations.Apply(manifest.Id);
                        DiagnosticLog.Write(apply.Success
                            ? $"Translation '{manifest.Id}' re-applied after update."
                            : $"Translation re-apply failed: {apply.ErrorMessage}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Post-update translation step failed (non-fatal): {ex.Message}");
        }

        status?.Report(Strings.Get("StatusAllDone"));
    }

    // ---- Helpers ----

    /// <summary>
    /// Resolve install path. Priority:
    ///   1. Config (user-set or cached from previous detection)
    ///   2. Windows Registry (Inno Setup GUID)
    ///   3. Disk scan via AoE3Detector (finds WoL inside AoE3 folders)
    /// </summary>
    private string? ResolveInstallPath()
    {
        // 1. Config path
        if (!string.IsNullOrWhiteSpace(_config.ModInstallPath)
            && RegistryService.IsValidInstall(_config.ModInstallPath))
        {
            return _config.ModInstallPath.TrimEnd('\\', '/');
        }

        // 2. Registry (Inno Setup entries)
        var detected = RegistryService.FindInstallPath();
        if (!string.IsNullOrEmpty(detected))
        {
            _config.ModInstallPath = detected;
            _config.Save();
            return detected;
        }

        // 3. Disk scan — look for WoL inside detected AoE3 installations
        var aoe3Installs = AoE3Detector.FindAll();
        foreach (var install in aoe3Installs)
        {
            // Candidate locations where WoL files may live, in priority order:
            //   a) <AoE3 root>\Wars of Liberty\        (most common — installed as subfolder)
            //   b) <AoE3 root>\                        (mod files merged directly into AoE3)
            //   c) <bin folder>\Wars of Liberty\       (uncommon, but possible)
            //   d) <bin folder>\                       (uncommon)
            var candidates = new[]
            {
                Path.Combine(install.ModRoot, "Wars of Liberty"),
                install.ModRoot,
                Path.Combine(install.GameFolder, "Wars of Liberty"),
                install.GameFolder,
            };

            foreach (var candidate in candidates)
            {
                if (RegistryService.IsValidInstall(candidate))
                {
                    DiagnosticLog.Write($"Found WoL via disk scan: {candidate}");
                    _config.ModInstallPath = candidate;
                    _config.Save();
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Identifies the user's current mod version by computing MD5 of three key files
    /// and matching against the known versions in UpdateInfo.xml.
    ///
    /// IMPORTANT: when a community translation is active, the live
    /// <c>data\stringtabley.xml</c> is the translated version — its hash
    /// won't match any known mod version. We get around this by hashing
    /// the canonical English snapshot in <c>translations\_originals\</c>
    /// instead. <see cref="TranslationService"/> manages that snapshot.
    /// </summary>
    private static async Task<VersionInfo?> DetectCurrentVersionAsync(
        string installPath,
        List<VersionInfo> knownVersions,
        CancellationToken ct)
    {
        // protoy.xml and techtreey.xml are NOT covered by translations
        // (they're code/data, not localized strings) so always hash live.
        var protoMd5 = await HashService.ComputeMd5Async(
            Path.Combine(installPath, ProtoRelativePath), ct);
        var techMd5 = await HashService.ComputeMd5Async(
            Path.Combine(installPath, TechRelativePath), ct);

        // stringtabley.xml IS localized — defer to the translation service
        // for the right path to hash (snapshot if a translation is active,
        // live file otherwise).
        var translations = new TranslationService(installPath);
        var strHashPath = translations.ResolveHashableFile(StrRelativePath);
        var strMd5 = await HashService.ComputeMd5Async(strHashPath, ct);

        DiagnosticLog.Write("MD5 of local files:");
        DiagnosticLog.Write($"  protoy.xml       = {protoMd5}");
        DiagnosticLog.Write($"  techtreey.xml    = {techMd5}");
        DiagnosticLog.Write(strHashPath == Path.Combine(installPath, StrRelativePath)
            ? $"  stringtabley.xml = {strMd5}"
            : $"  stringtabley.xml = {strMd5}  (from _originals snapshot)");
        DiagnosticLog.Write($"Searching for a match among {knownVersions.Count} known versions...");

        var match = knownVersions.FirstOrDefault(v =>
            string.Equals(v.ProtoMd5, protoMd5, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(v.TechMd5, techMd5, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(v.StrMd5, strMd5, StringComparison.OrdinalIgnoreCase));

        if (match != null)
        {
            DiagnosticLog.Write($"Match: version {match.Ver}, MinReqDownload={match.MinReqDownload}");
        }
        else
        {
            DiagnosticLog.Write("No exact match. Per-version comparison:");
            foreach (var v in knownVersions)
            {
                bool p = string.Equals(v.ProtoMd5, protoMd5, StringComparison.OrdinalIgnoreCase);
                bool t = string.Equals(v.TechMd5, techMd5, StringComparison.OrdinalIgnoreCase);
                bool s = string.Equals(v.StrMd5, strMd5, StringComparison.OrdinalIgnoreCase);
                if (p || t || s)
                {
                    DiagnosticLog.Write(
                        $"  v{v.Ver}: proto={(p ? "OK" : "X")} tech={(t ? "OK" : "X")} str={(s ? "OK" : "X")}");
                }
            }
        }

        return match;
    }

    /// <summary>
    /// Determines which downloads need to be applied:
    ///  - If we recognize the current version, return all downloads with
    ///    id &gt;= current.MinReqDownload (these chain you up to the latest).
    ///  - If we don't recognize it, return all downloads (matches the
    ///    Java updater's "redownload everything" fallback).
    /// </summary>
    private static List<DownloadInfo> ComputePendingDownloads(
        UpdateInfo info,
        VersionInfo? current)
    {
        // If user is on the latest version (MinReqDownload=0 in the newest entry),
        // there's nothing to do.
        if (current != null && current.MinReqDownload == 0)
            return new List<DownloadInfo>();

        int startId = current?.MinReqDownload ?? 0;
        return info.Downloads
            .Where(d => d.Id >= startId)
            .OrderBy(d => d.Id)        // apply oldest patch first
            .ToList();
    }

    /// <summary>
    /// CRC32 comparison that tolerates leading-zero differences (e.g. "abc" vs "00000abc").
    /// The Java updater has a "fixCrc32" helper for the same reason.
    /// </summary>
    private static bool CrcMatches(string actual, string expected)
    {
        var a = actual.TrimStart('0').ToLowerInvariant();
        var e = expected.TrimStart('0').ToLowerInvariant();
        if (a.Length == 0) a = "0";
        if (e.Length == 0) e = "0";
        return a == e;
    }
}
