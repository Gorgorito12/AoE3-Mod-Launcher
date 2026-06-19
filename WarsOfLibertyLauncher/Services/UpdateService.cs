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
    private readonly ModProfile _profile;
    private readonly UpdateInfoService _infoService;
    private readonly DownloadService _downloader;
    private readonly ArchiveService _archive;

    public UpdateService(LauncherConfig config, ModProfile profile)
    {
        _config = config;
        _profile = profile;
        _infoService = new UpdateInfoService();
        _downloader = new DownloadService();
        _archive = new ArchiveService();

        // Fast-path: if a previous session already detected this mod's
        // install path AND that location is still valid on disk, populate
        // InstallPath right now (synchronously). Two file-system syscalls
        // — Directory.Exists + File.Exists for the probe file — so this is
        // essentially free.
        //
        // The UI relies on this: when the user switches to a mod, we want
        // to show "Installed" immediately if we've seen this mod before,
        // not flash "Not installed" while the async CheckAsync does its
        // work. CheckAsync will run shortly after and refine version info,
        // pending patches, etc. — but the install-state badge is correct
        // from frame zero.
        //
        // If the cache is stale (user uninstalled the mod manually between
        // sessions), CheckAsync's full ResolveInstallPath will catch up
        // and clear InstallPath. The worst case is a one-frame "Installed
        // → Not installed" flicker, which is much rarer and less jarring
        // than the previous "Not installed → Installed" flicker that
        // happened on every single mod switch.
        var state = _config.GetState(_profile.Id);

        // 1. Cached install path. A few file-system syscalls (Directory.Exists
        //    + File.Exists for the probe file + the content marker) →
        //    essentially free. Avoids flashing "Not installed" on mod switch.
        //    LooksLikeRealModInstall rejects stale entries that point at a
        //    vanilla AoE3 folder — the probe file alone isn't enough (vanilla
        //    AoE3 carries data\stringtabley.xml too), so we also require the
        //    mod's content marker (WoL: art\zulushield) when one is declared.
        var cachedPath = state.InstallPath;
        bool pathCacheHit = !string.IsNullOrWhiteSpace(cachedPath)
            && IsProfileInstalled(cachedPath)
            && LooksLikeRealModInstall(cachedPath);
        if (pathCacheHit)
        {
            InstallPath = cachedPath.TrimEnd('\\', '/');
        }

        // 2. Cached version strings. Only meaningful for mods that compute
        //    versions (i.e. WolPatcher). We synthesise placeholder
        //    VersionInfo objects carrying just the .Ver field — enough for
        //    the UI to render "Installed (v1.2.0c2)" and "Latest: 1.2.0c2"
        //    instead of the alarming "Unknown version" red badge and the
        //    "—" placeholder while CheckAsync's MD5 pass + manifest fetch
        //    run. The real VersionInfo (with TechMd5 / StrMd5 / ProtoMd5
        //    populated for matching) replaces them as soon as CheckAsync
        //    finishes — exactly one frame later for cache hits, ~1-2 s for
        //    cold sessions.
        var cachedVer = state.LastKnownVersion;
        if (pathCacheHit && !string.IsNullOrWhiteSpace(cachedVer))
        {
            CurrentVersion = new Models.VersionInfo { Ver = cachedVer };
        }

        var cachedLatest = state.LastKnownLatestVersion;
        if (!string.IsNullOrWhiteSpace(cachedLatest))
        {
            LatestVersion = new Models.VersionInfo { Ver = cachedLatest };
        }

        DiagnosticLog.Write(
            $"UpdateService ctor for '{_profile.Id}': cachedPath='{cachedPath ?? "(empty)"}' " +
            $"pathCacheHit={pathCacheHit} cachedVer='{cachedVer ?? "(empty)"}' " +
            $"cachedLatest='{cachedLatest ?? "(empty)"}' -> " +
            $"InstallPath='{InstallPath ?? "(null)"}' " +
            $"CurrentVersion='{CurrentVersion?.Ver ?? "(null)"}' " +
            $"LatestVersion='{LatestVersion?.Ver ?? "(null)"}'");
    }

    /// <summary>The mod profile this service is operating on (active mod).</summary>
    public ModProfile Profile => _profile;

    // ------------------------------------------------------------------------
    // Effective settings — the profile is the source of truth, but if the
    // user has explicitly written a value into launcher-config.json (a
    // non-empty override) we honour it. Lets advanced users redirect URLs
    // without us having to expose a UI for it. Public so the UI layer can
    // use the same resolution rules instead of re-implementing them.
    // ------------------------------------------------------------------------

    public string EffectiveUpdateInfoUrl() =>
        !string.IsNullOrWhiteSpace(_config.UpdateInfoUrl)
            ? _config.UpdateInfoUrl
            : _profile.Wol?.UpdateInfoUrl ?? "";

    public string EffectiveUpdateInfoUrlAlt() =>
        !string.IsNullOrWhiteSpace(_config.UpdateInfoUrlAlt)
            ? _config.UpdateInfoUrlAlt
            : _profile.Wol?.UpdateInfoUrlAlt ?? "";

    public string[] EffectivePayloadZipUrls()
    {
        // User override wins (allows pointing at a private mirror). An empty
        // override array means "no override" — fall through to the profile.
        if (_config.PayloadZipUrls != null && _config.PayloadZipUrls.Length > 0)
            return _config.PayloadZipUrls;
        return _profile.Wol?.PayloadZipUrls ?? System.Array.Empty<string>();
    }

    public string EffectiveDefaultInstallFolder() =>
        !string.IsNullOrWhiteSpace(_config.DefaultInstallFolder)
            ? _config.DefaultInstallFolder
            : _profile.DefaultInstallFolder;

    public string EffectiveOfficialWebsite() =>
        !string.IsNullOrWhiteSpace(_config.OfficialWebsite)
            ? _config.OfficialWebsite
            : _profile.Wol?.OfficialWebsite ?? "";

    /// <summary>
    /// GitHub repo (<c>owner/repo</c>) the launcher scans for community
    /// translations of the active mod. Returns empty when the active mod
    /// doesn't participate in the translation system.
    /// </summary>
    /// <remarks>
    /// The PROFILE decides participation: a mod only has community
    /// translations if its <see cref="ModProfile.Translations"/> block
    /// declares a repo. The stock base game (and any mod without a
    /// Translations block) returns "" so it shows no packs — we must NOT
    /// fall back to the global, WoL-centric <c>config.TranslationsRepo</c>
    /// for those, or every mod would inherit WoL's Spanish pack and could
    /// overwrite its own data files with WoL strings. For a participating
    /// mod, the global override still wins (lets a power user point WoL at
    /// a fork/mirror); otherwise the profile's own repo is used.
    /// </remarks>
    public string EffectiveTranslationsRepo()
    {
        var profileRepo = _profile.Translations?.Repo;
        if (string.IsNullOrWhiteSpace(profileRepo))
            return "";
        return !string.IsNullOrWhiteSpace(_config.TranslationsRepo)
            ? _config.TranslationsRepo!
            : profileRepo!;
    }

    /// <summary>
    /// The repo that hosts folder-published translations (files under
    /// <c>translations/&lt;id&gt;/</c> on main) for the active profile, or empty
    /// when the profile doesn't participate or declares no folder repo. Read
    /// alongside <see cref="EffectiveTranslationsRepo"/> for dual-mode discovery.
    /// </summary>
    public string EffectiveTranslationsFolderRepo()
    {
        if (_profile.Translations == null) return "";
        return _profile.Translations.FolderRepo ?? "";
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

    /// <summary>
    /// Set by <see cref="ApplyUpdatesAsync"/> when a just-updated mod's active
    /// translation was reverted to English for incompatibility. The UI reads it
    /// after the update to notify the user, then clears it. Null when nothing
    /// was reverted.
    /// </summary>
    public Models.TranslationRevertNotice? LastTranslationRevertNotice { get; set; }

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

        status?.Report(Strings.Format("StatusDetectingInstall", _profile.DisplayName));
        InstallPath = ResolveInstallPath();
        bool valid = !string.IsNullOrEmpty(InstallPath) && IsProfileInstalled(InstallPath);
        DiagnosticLog.Write($"Install path detected: '{InstallPath}' (valid: {valid})");

        // Short-circuit for mods that don't have a WoL-style updater (e.g.
        // Improvement Mod, which ships its own external patcher). We still
        // detect the install path so the PLAY button works and the gear
        // menu can open the right folder, but we skip the manifest fetch
        // and version match — those are WoL-specific concepts.
        //
        // GitHubReleases is a special case: the version IS the catalog's
        // approved release tag (no MD5-of-files calculus), and the
        // currently-installed tag was persisted into ModState at install
        // time. Both are cheap synchronous lookups, so we surface them in
        // the StatusCard instead of leaving "—" / "—" — the rendering path
        // already reads CurrentVersion/LatestVersion uniformly.
        if (_profile.UpdateMechanism != ModUpdateMechanism.WolPatcher)
        {
            DiagnosticLog.Write(
                $"Profile '{_profile.Id}' uses '{_profile.UpdateMechanism}'; skipping manifest fetch.");

            if (_profile.UpdateMechanism == ModUpdateMechanism.GitHubReleases)
            {
                var ghTag = _profile.GitHubReleases?.ApprovedReleaseTag ?? "";
                var ghState = _config.GetState(_profile.Id);
                var ghInstalled = ghState.LastKnownVersion;

                LatestVersion = !string.IsNullOrEmpty(ghTag)
                    ? new VersionInfo { Ver = ghTag }
                    : null;
                CurrentVersion = (!string.IsNullOrEmpty(ghInstalled) && valid)
                    ? new VersionInfo { Ver = ghInstalled }
                    : null;

                // Mirror the WolPatcher path: persist LastKnownLatestVersion
                // so a cold-start UI render sees the right "Latest" before
                // the next CheckAsync runs. The installed tag is persisted
                // separately at install time, so we don't touch it here.
                if (!string.IsNullOrEmpty(ghTag) && ghState.LastKnownLatestVersion != ghTag)
                {
                    ghState.LastKnownLatestVersion = ghTag;
                    try { _config.Save(); }
                    catch (Exception ex)
                    {
                        DiagnosticLog.Write($"Failed to persist GH latest-version cache: {ex.Message}");
                    }
                }

                DiagnosticLog.Write(
                    $"GitHubReleases: CurrentVersion='{CurrentVersion?.Ver ?? "(null)"}' " +
                    $"LatestVersion='{LatestVersion?.Ver ?? "(null)"}'");

                return new CheckResult(
                    new UpdateInfo(), CurrentVersion, LatestVersion,
                    new List<DownloadInfo>(), valid);
            }

            CurrentVersion = null;
            LatestVersion = null;
            return new CheckResult(new UpdateInfo(), null, null, new List<DownloadInfo>(), valid);
        }

        status?.Report(Strings.Get("StatusFetchingManifest"));
        var info = await _infoService.FetchAsync(
            EffectiveUpdateInfoUrl(), EffectiveUpdateInfoUrlAlt(), ct);

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

        // Persist the just-detected versions into per-mod state so the next
        // mod switch back to this profile can render the badges correctly
        // from frame zero (see ctor's cache-hit logic).
        //   * Current version: only saved when we actually identified one;
        //     leaving the cache stale on a failed detection is fine, the
        //     constructor's IsProfileInstalled gate prevents misleading the
        //     user, and the next successful CheckAsync will repair it.
        //   * Latest version: saved whenever the manifest fetch returns
        //     something — it's a server-side fact that doesn't depend on
        //     the local install being detected.
        var modState = _config.GetState(_profile.Id);
        bool dirty = false;

        if (current != null && valid && modState.LastKnownVersion != current.Ver)
        {
            modState.LastKnownVersion = current.Ver;
            dirty = true;
        }
        if (latest != null && !string.IsNullOrEmpty(latest.Ver)
            && modState.LastKnownLatestVersion != latest.Ver)
        {
            modState.LastKnownLatestVersion = latest.Ver;
            dirty = true;
        }
        if (dirty)
        {
            try { _config.Save(); }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"Failed to persist version cache: {ex.Message}");
            }
        }

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

            var extractStatus = new Progress<string>(s => status?.Report(s));

            // Forward archive-level extract progress to the same UpdateProgress
            // pipeline the download phase uses, so the bar keeps moving while
            // SharpCompress is decompressing the .tar.xz instead of freezing
            // at 100% from the download. We map BytesRead/BytesTotal from the
            // archive to PatchBytesDone/PatchBytesTotal so the existing UI
            // bindings (PatchProgress, PatchBytesText) light up automatically.
            var extractByteProgress = new Progress<ArchiveExtractProgress>(p =>
            {
                Report(p.BytesRead, p.BytesTotal);
            });

            await _archive.ExtractTarXzWithBackupAsync(
                archivePath, InstallPath, backupDir,
                extractStatus, extractByteProgress, ct);

            // ---- 5. Apply delete list ----
            // The official UpdateInfo.xml gives deleteList as an install-
            // RELATIVE PATH (e.g. "etc\1013c_delete.lst") to a text file the
            // patch itself just extracted — the Java updater reads it locally
            // and deletes the listed files. We must do the same; the older
            // "download it as a URL" path silently failed for every real WoL
            // patch (the value isn't a URL), so patch deletions never applied.
            // A http(s) URL is still honoured as a fallback for other mods.
            if (!string.IsNullOrEmpty(dl.DeleteList))
            {
                try
                {
                    status?.Report(Strings.Format("StatusCleanup", dl.Id));
                    string deleteListContent;
                    if (dl.DeleteList.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                        || dl.DeleteList.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        deleteListContent = await _downloader.DownloadStringAsync(dl.DeleteList, ct);
                    }
                    else
                    {
                        deleteListContent = ArchiveService.ReadLocalDeleteList(InstallPath, dl.DeleteList);
                    }
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

        // ---- Post-update: reconcile the active translation (shared helper) ----
        // The patches just overwrote the covered files with the latest English
        // versions. ReconcileAfterUpdate re-snapshots, then re-applies a still-
        // compatible pack or reverts to English (and reports it) when not. The
        // same helper runs on the GitHubReleases re-overlay path (MainWindow).
        try
        {
            var translations = new TranslationService(InstallPath, _profile.Translations?.CoveredFiles);
            LastTranslationRevertNotice =
                translations.ReconcileAfterUpdate(_config, _profile.Id, LatestVersion?.Ver);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Post-update translation step failed (non-fatal): {ex.Message}");
        }

        // Post-update cleanup of stale artifacts. NativeInstallService
        // runs the same pass at the end of the initial install, but
        // patch tarballs can ALSO re-introduce stale .xml.xmb files,
        // dev-leftover tactics, art\WoL\interns scratch folders and
        // assorted junk that the official setup+updater path never
        // had. Running RemoveStaleBuildArtifacts here keeps the
        // install canonically clean after every successful update —
        // multiplayer hashes match peers regardless of which version
        // you patched up from. Wrapped in try/catch so a cleanup
        // hiccup doesn't roll back a successful patch chain.
        try
        {
            NativeInstallService.RemoveStaleBuildArtifacts(_profile, InstallPath);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Post-update RemoveStaleBuildArtifacts failed (non-fatal): {ex.Message}");
        }

        status?.Report(Strings.Get("StatusAllDone"));
    }

    // ---- Helpers ----

    /// <summary>
    /// Resolve install path for the active mod profile. Priority:
    ///   1. Config (user-set or cached from previous detection)
    ///   2. Windows Registry (Inno Setup GUID — WoL only, since ModDB-style
    ///      mods like IM don't register with the OS uninstaller)
    ///   3. Disk scan: per profile install type — isolated mods are looked
    ///      for as subfolders of AoE3 installs, in-place mods are detected
    ///      by their probe file sitting inside the AoE3 folder itself.
    /// </summary>
    private string? ResolveInstallPath()
    {
        var state = _config.GetState(_profile.Id);

        // 1. Path cached for THIS mod from a previous detection (per-mod —
        //    cannot leak across profiles). On top of the probe-file check we
        //    require the mod's content marker when one is declared: a stale
        //    cache pointing at a vanilla AoE3 location (e.g.
        //    <Steam>\…\Age Of Empires 3\bin) can pass IsProfileInstalled by
        //    accident — the probe file (data\stringtabley.xml etc.) exists in
        //    vanilla AoE3 too — but it lacks the marker (WoL: art\zulushield).
        //    Detection is by content, not folder name, so a renamed install
        //    folder still resolves here instead of being wiped.
        if (!string.IsNullOrWhiteSpace(state.InstallPath))
        {
            if (IsProfileInstalled(state.InstallPath)
                && LooksLikeRealModInstall(state.InstallPath))
            {
                return state.InstallPath.TrimEnd('\\', '/');
            }
            // Cache is stale or pointing at vanilla AoE3 — wipe it so the
            // tile-side ProbeInstalledState and the next session both arrive
            // here with a clean slate. Versions stay (they're a separate
            // axis); the install-path miss alone is enough to force a fresh
            // detection.
            DiagnosticLog.Write(
                $"ResolveInstallPath: rejecting stale cache for '{_profile.Id}': '{state.InstallPath}' " +
                "(failed probe-file or content-marker check). Clearing.");
            state.InstallPath = "";
            try { _config.Save(); }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"Failed to persist cleared install path: {ex.Message}");
            }
        }

        // 2. Registry — only the WoL profile has a known Inno Setup product
        //    GUID. Other profiles skip this step.
        if (string.Equals(_profile.Id, ModRegistry.WolId, StringComparison.OrdinalIgnoreCase))
        {
            var detected = RegistryService.FindInstallPath();
            if (!string.IsNullOrEmpty(detected))
            {
                state.InstallPath = detected;
                _config.Save();
                return detected;
            }
        }

        // 3. Disk scan — strategy depends on whether the mod lives in its
        //    own folder or on top of AoE3.
        var aoe3Installs = AoE3Detector.FindAll();
        foreach (var install in aoe3Installs)
        {
            var candidates = _profile.InstallType == ModInstallType.IsolatedFolder
                ? IsolatedCandidates(install)
                : InPlaceCandidates(install);

            foreach (var candidate in candidates)
            {
                if (IsProfileInstalled(candidate) && LooksLikeRealModInstall(candidate))
                {
                    DiagnosticLog.Write(
                        $"Found '{_profile.Id}' via disk scan: {candidate}");
                    state.InstallPath = candidate;
                    _config.Save();
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Probe locations for an isolated-folder mod (e.g. WoL). Two passes:
    /// first the fast happy-path guesses where the folder is named after the
    /// mod (<c>profile.DisplayName</c> or the leaf of
    /// <c>profile.DefaultInstallFolder</c>); then a CONTENT scan of every
    /// child folder one level under the AoE3 root, its <c>bin\</c>, and the
    /// parent that may hold the mod as a sibling — yielding any that look like
    /// a real install of this mod by content (probe file + marker). The scan
    /// is what makes detection independent of the folder name, and the marker
    /// is what keeps it from matching vanilla AoE3 (whose <c>data\</c> files
    /// satisfy an ambiguous probe). Each candidate is re-validated by the
    /// caller, so emitting a few extra is harmless.
    /// </summary>
    private IEnumerable<string> IsolatedCandidates(AoE3Detector.Installation install)
    {
        var parent = Path.GetDirectoryName(install.ModRoot.TrimEnd('\\', '/'));

        // Pass 1 — folder named after the mod. Prefer the leaf of an absolute
        // DefaultInstallFolder ("C:\\…\\Improvement Mod" → "Improvement Mod"),
        // else fall back to the DisplayName.
        var folderName = string.IsNullOrEmpty(_profile.DefaultInstallFolder)
            ? _profile.DisplayName
            : Path.GetFileName(_profile.DefaultInstallFolder.TrimEnd('\\', '/'));

        if (!string.IsNullOrEmpty(folderName))
        {
            yield return Path.Combine(install.ModRoot, folderName);
            yield return Path.Combine(install.GameFolder, folderName);
            // Sibling-of-AoE3: when DefaultInstallFolder is empty the install
            // dialog suggests "<parent of AoE3>\<DisplayName>" as the default.
            if (!string.IsNullOrEmpty(parent))
                yield return Path.Combine(parent, folderName);
        }

        // Pass 2 — content scan, so the mod is found in a folder with ANY
        // name. Needs a content signal to match against; without one, every
        // subfolder would match.
        bool hasContentSignal = !string.IsNullOrEmpty(_profile.InstallProbeFile)
            || !string.IsNullOrEmpty(_profile.InstallMarker);
        if (!hasContentSignal)
            yield break;

        foreach (var baseDir in new[] { install.ModRoot, install.GameFolder, parent })
        {
            if (string.IsNullOrEmpty(baseDir) || !Directory.Exists(baseDir))
                continue;

            string[] subdirs;
            try { subdirs = Directory.GetDirectories(baseDir); }
            catch { continue; } // permission / IO — skip this base quietly

            foreach (var sub in subdirs)
            {
                if (ModInstallProbe.LooksLikeModInstall(sub, _profile))
                    yield return sub;
            }
        }
    }

    /// <summary>Probe locations for an in-place overlay mod (e.g. IM).</summary>
    private static IEnumerable<string> InPlaceCandidates(AoE3Detector.Installation install)
    {
        // Steam ships AoE3 with a `bin\` subfolder for the executables;
        // IM extracts there. Non-Steam users extract straight into the
        // game folder. Both are valid.
        yield return install.GameFolder;
        yield return install.ModRoot;
    }

    /// <summary>
    /// Generic "is the active mod installed at this path" check, driven by
    /// <see cref="ModProfile.InstallProbeFile"/>. When a profile doesn't
    /// declare a probe we fall back to the WoL-specific marker for
    /// backward compatibility.
    /// </summary>
    private bool IsProfileInstalled(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return false;

        if (string.IsNullOrEmpty(_profile.InstallProbeFile))
            return RegistryService.IsValidInstall(path);

        return File.Exists(Path.Combine(path, _profile.InstallProbeFile));
    }

    /// <summary>
    /// Extra content gate layered on top of <see cref="IsProfileInstalled"/>'s
    /// probe-file check: when the profile declares an
    /// <see cref="ModProfile.InstallMarker"/> (a file/dir unique to the mod,
    /// absent from the base game it clones/overlays) require it. This is what
    /// tells a real mod install apart from a stale path pointing at vanilla
    /// AoE3 — which also satisfies the probe file (it carries the same
    /// <c>data\</c> files). Detection is by CONTENT, not folder name, so the
    /// mod is recognised under any folder name. Profiles with no marker pass:
    /// their probe file is already exclusive to the mod (e.g. an overlay mod's
    /// own .exe).
    /// </summary>
    private bool LooksLikeRealModInstall(string path)
    {
        if (string.IsNullOrEmpty(_profile.InstallMarker)) return true;
        return ModInstallProbe.MarkerExists(path, _profile.InstallMarker);
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
