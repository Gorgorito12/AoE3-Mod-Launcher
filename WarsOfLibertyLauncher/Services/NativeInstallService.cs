using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Progress reported during the native installation.
/// </summary>
public record NativeInstallProgress(
    double Percentage,
    long BytesDone,
    long BytesTotal,
    string CurrentStep,
    string CurrentFile,
    double BytesPerSecond);

/// <summary>
/// Distinct stages of the install pipeline. The UI uses this to render a
/// breadcrumb across the top of the progress block, change the speed label
/// from "Download" to "Copy" / "Files" depending on what's actually being
/// measured, and weight overall progress correctly.
/// </summary>
public enum InstallPhase
{
    /// <summary>Idle / not started.</summary>
    None,
    /// <summary>Downloading the ZIP parts from GitHub.</summary>
    Download,
    /// <summary>Concatenating + extracting the combined ZIP to temp.</summary>
    Extract,
    /// <summary>Cloning AoE3 (and flattening bin\) to the destination.</summary>
    Clone,
    /// <summary>Copying the WoL mod files on top of the clone.</summary>
    ModOverlay,
    /// <summary>Shortcuts, registry, manifest, verification.</summary>
    Finalize,
    /// <summary>Install completed (used so the UI can mark every dot done).</summary>
    Complete,
}

/// <summary>
/// Replaces Inno Setup entirely. Performs a full Wars of Liberty installation
/// natively in C#:
///   1. Downloads the WoL payload ZIP parts and concatenates them
///   2. Extracts to a temp folder
///   3. Clones AoE3:TAD from source to destination
///   4. Copies WoL mod files on top of the clone
///   5. Creates Start Menu and Desktop shortcuts
///   6. Writes registry entries for detection by the updater
///
/// The payload ZIP is split into multiple parts (.zip.001, .zip.002, ...)
/// to work around GitHub's 2 GB file size limit.
/// </summary>
public class NativeInstallService
{
    private const string ProductGuid = "{EB448764-CABB-4766-8055-495AEA292020}_is1";
    private const string AppName = "Wars of Liberty";

    private readonly DownloadService _downloader;
    private readonly FolderCloneService _cloneService;

    /// <summary>Pause flag — propagated to both downloader and clone service.</summary>
    public bool Pause
    {
        get => _downloader.Pause;
        set
        {
            _downloader.Pause = value;
            _cloneService.Pause = value;
        }
    }

    /// <summary>The clone service, exposed so the UI can read its progress.</summary>
    public FolderCloneService CloneService => _cloneService;

    public NativeInstallService(DownloadService? downloader = null)
    {
        _downloader = downloader ?? new DownloadService();
        _cloneService = new FolderCloneService();
    }

    /// <summary>Where temp files for installation live.</summary>
    public static string TempDirectory =>
        Path.Combine(Path.GetTempPath(), "WarsOfLibertyLauncher", "native-install");

    /// <summary>
    /// Full installation pipeline using multiple ZIP part URLs.
    /// </summary>
    public async Task InstallAsync(
        string[] payloadZipUrls,
        string aoe3SourcePath,
        string destinationFolder,
        IProgress<DownloadProgress>? downloadProgress = null,
        IProgress<CloneProgress>? cloneProgress = null,
        IProgress<string>? statusProgress = null,
        IProgress<InstallPhase>? phaseProgress = null,
        IProgress<ExtractProgress>? extractProgress = null,
        IProgress<ModOverlayProgress>? overlayProgress = null,
        CancellationToken ct = default)
    {
        DiagnosticLog.Write($"=== Native Install Start ===");
        DiagnosticLog.Write($"  Parts: {payloadZipUrls.Length}");
        DiagnosticLog.Write($"  AoE3 Source: {aoe3SourcePath}");
        DiagnosticLog.Write($"  Destination: {destinationFolder}");

        // ---- Phase 1: Download all parts and concatenate ----
        phaseProgress?.Report(InstallPhase.Download);
        statusProgress?.Report("Downloading Wars of Liberty files...");
        var zipPath = await DownloadAndConcatenatePartsAsync(payloadZipUrls, downloadProgress, statusProgress, ct);

        // ---- Phase 2: Extract payload to temp ----
        phaseProgress?.Report(InstallPhase.Extract);
        statusProgress?.Report("Extracting mod files...");
        var extractedFolder = await ExtractPayloadAsync(zipPath, statusProgress, extractProgress, ct);

        // ---- Phase 3: Clone AoE3 to destination ----
        phaseProgress?.Report(InstallPhase.Clone);
        statusProgress?.Report("Copying Age of Empires III files...");
        await _cloneService.CloneAsync(aoe3SourcePath, destinationFolder, cloneProgress, ct);

        // ---- Phase 3b: Flatten bin\ subfolder if present ----
        // (Still part of the Clone phase from the UI's perspective.)
        FlattenBinSubfolder(destinationFolder, statusProgress);

        // ---- Phase 4: Copy WoL files on top ----
        phaseProgress?.Report(InstallPhase.ModOverlay);
        statusProgress?.Report("Installing Wars of Liberty mod files...");
        await CopyPayloadToDestinationAsync(extractedFolder, destinationFolder, statusProgress, overlayProgress, ct);

        // ---- Phase 5: Finalize (shortcuts, registry, manifest) ----
        phaseProgress?.Report(InstallPhase.Finalize);
        statusProgress?.Report("Creating shortcuts...");
        var shortcuts = CreateShortcuts(destinationFolder, out var startMenuFolder);

        statusProgress?.Report("Writing registry entries...");
        WriteRegistryEntries(destinationFolder);

        WriteManifest(destinationFolder, aoe3SourcePath, clonedAoe3: true,
            shortcuts, startMenuFolder);

        // Snapshot the canonical English files for translation overlay support.
        // Captures stringtabley.xml + unithelpstringsy.xml so the launcher can
        // (a) hash them for version detection even when a translation is active,
        // (b) revert to English on demand.
        try { new TranslationService(destinationFolder).RefreshOriginalsSnapshot(); }
        catch (Exception ex) { DiagnosticLog.Write($"Translations snapshot failed: {ex.Message}"); }

        phaseProgress?.Report(InstallPhase.Complete);
        DiagnosticLog.Write("=== Native Install Complete ===");
    }

    /// <summary>
    /// Lighter install: skips AoE3 clone. Use this when the destination folder
    /// already contains AoE3 (e.g., user picked an existing AoE3 folder as dest).
    /// </summary>
    public async Task InstallModOnlyAsync(
        string[] payloadZipUrls,
        string destinationFolder,
        IProgress<DownloadProgress>? downloadProgress = null,
        IProgress<string>? statusProgress = null,
        IProgress<InstallPhase>? phaseProgress = null,
        IProgress<ExtractProgress>? extractProgress = null,
        IProgress<ModOverlayProgress>? overlayProgress = null,
        CancellationToken ct = default)
    {
        DiagnosticLog.Write($"=== Native Install (mod-only) Start ===");
        DiagnosticLog.Write($"  Parts: {payloadZipUrls.Length}");
        DiagnosticLog.Write($"  Destination: {destinationFolder}");

        // ---- Phase 1: Download ----
        phaseProgress?.Report(InstallPhase.Download);
        statusProgress?.Report("Downloading Wars of Liberty files...");
        var zipPath = await DownloadAndConcatenatePartsAsync(payloadZipUrls, downloadProgress, statusProgress, ct);

        // ---- Phase 2: Extract ----
        phaseProgress?.Report(InstallPhase.Extract);
        statusProgress?.Report("Extracting mod files...");
        var extractedFolder = await ExtractPayloadAsync(zipPath, statusProgress, extractProgress, ct);

        // ---- Phase 3: Copy WoL on top (no Clone phase in mod-only) ----
        phaseProgress?.Report(InstallPhase.ModOverlay);
        statusProgress?.Report("Installing Wars of Liberty mod files...");
        await CopyPayloadToDestinationAsync(extractedFolder, destinationFolder, statusProgress, overlayProgress, ct);

        // ---- Phase 4: Finalize ----
        phaseProgress?.Report(InstallPhase.Finalize);
        statusProgress?.Report("Creating shortcuts...");
        var shortcuts = CreateShortcuts(destinationFolder, out var startMenuFolder);
        WriteRegistryEntries(destinationFolder);

        WriteManifest(destinationFolder, aoe3SourcePath: null, clonedAoe3: false,
            shortcuts, startMenuFolder);

        // Same snapshot as the full install — stringtabley.xml /
        // unithelpstringsy.xml saved for translation overlay support.
        try { new TranslationService(destinationFolder).RefreshOriginalsSnapshot(); }
        catch (Exception ex) { DiagnosticLog.Write($"Translations snapshot failed: {ex.Message}"); }

        phaseProgress?.Report(InstallPhase.Complete);
        DiagnosticLog.Write("=== Native Install (mod-only) Complete ===");
    }

    // =========================================================================
    // Implementation
    // =========================================================================

    /// <summary>
    /// Downloads all ZIP parts sequentially and concatenates them into a single
    /// ZIP file. Progress reports accumulated bytes across all parts.
    /// </summary>
    private async Task<string> DownloadAndConcatenatePartsAsync(
        string[] partUrls,
        IProgress<DownloadProgress>? progress,
        IProgress<string>? statusProgress,
        CancellationToken ct)
    {
        Directory.CreateDirectory(TempDirectory);

        var combinedZipPath = Path.Combine(TempDirectory, "WolPayload.zip");

        // Delete any previous combined file
        try { if (File.Exists(combinedZipPath)) File.Delete(combinedZipPath); } catch { }

        DiagnosticLog.Write($"Downloading {partUrls.Length} ZIP parts...");

        // Pre-compute the total size across all parts via HEAD requests so the
        // progress bar has a real denominator from the very first byte. Without
        // this the bar can stay invisible during the first download because GitHub
        // doesn't always send Content-Length on the GET for releases.
        statusProgress?.Report("Checking download size...");
        var partSizes = new long[partUrls.Length];
        long totalAcrossAllParts = 0;
        bool sizesKnown = true;
        for (int i = 0; i < partUrls.Length; i++)
        {
            partSizes[i] = await _downloader.TryGetRemoteSizeAsync(partUrls[i], ct);
            if (partSizes[i] <= 0) { sizesKnown = false; }
            else { totalAcrossAllParts += partSizes[i]; }
        }
        if (sizesKnown)
            DiagnosticLog.Write($"Pre-computed download total: {totalAcrossAllParts} bytes");
        else
            DiagnosticLog.Write("Could not pre-compute download total (HEAD not supported); progress will be approximate.");

        long totalDownloaded = 0;

        for (int i = 0; i < partUrls.Length; i++)
        {
            ct.ThrowIfCancellationRequested();

            var partUrl = partUrls[i];
            var partFileName = $"WolPayload.zip.{(i + 1):D3}";
            var partPath = Path.Combine(TempDirectory, partFileName);

            DiagnosticLog.Write($"  Part {i + 1}/{partUrls.Length}: {partUrl}");
            statusProgress?.Report($"Downloading part {i + 1} of {partUrls.Length}...");

            // Wrap progress to show overall across all parts. Always pass the
            // pre-computed total so the bar fills smoothly across all parts.
            long partStartBytes = totalDownloaded;
            long globalTotal = totalAcrossAllParts;
            var partProgress = new Progress<DownloadProgress>(p =>
            {
                long bytesSoFar = partStartBytes + p.BytesReceived;
                double pct = globalTotal > 0
                    ? Math.Min(100.0, (double)bytesSoFar / globalTotal * 100.0)
                    : 0;
                progress?.Report(new DownloadProgress(
                    BytesReceived: bytesSoFar,
                    TotalBytes: globalTotal,
                    Percentage: pct));
            });

            await _downloader.DownloadFileAsync(partUrl, partPath, partProgress, ct);

            totalDownloaded += new FileInfo(partPath).Length;
            DiagnosticLog.Write($"  Part {i + 1} done ({new FileInfo(partPath).Length} bytes).");
        }

        // Concatenate all parts into one ZIP
        statusProgress?.Report("Combining downloaded parts...");
        DiagnosticLog.Write("Concatenating parts into single ZIP...");
        await ConcatenateFilesAsync(partUrls.Length, combinedZipPath, ct);

        DiagnosticLog.Write($"Combined ZIP: {new FileInfo(combinedZipPath).Length} bytes.");
        return combinedZipPath;
    }

    /// <summary>
    /// Concatenates the downloaded part files (.001, .002, ...) into a single file.
    /// </summary>
    private async Task ConcatenateFilesAsync(int partCount, string outputPath, CancellationToken ct)
    {
        await using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write,
            FileShare.None, 81920, useAsync: true);

        var buffer = new byte[81920];

        for (int i = 1; i <= partCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            var partPath = Path.Combine(TempDirectory, $"WolPayload.zip.{i:D3}");

            await using var input = new FileStream(partPath, FileMode.Open, FileAccess.Read,
                FileShare.Read, 81920, useAsync: true);

            int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
            }
        }
    }

    /// <summary>
    /// Progress reported during extraction. Carries enough info for the UI
    /// to render a real progress bar (not just a status string) and a
    /// "X files/s" speed indicator.
    /// </summary>
    public record ExtractProgress(
        int EntriesDone,
        int EntriesTotal,
        long BytesDone,
        long BytesTotal);

    private Task<string> ExtractPayloadAsync(
        string zipPath,
        IProgress<string>? statusProgress,
        IProgress<ExtractProgress>? extractProgress,
        CancellationToken ct)
    {
        return Task.Run(() =>
        {
            var extractFolder = Path.Combine(TempDirectory, "extracted");

            if (Directory.Exists(extractFolder))
            {
                try { Directory.Delete(extractFolder, recursive: true); } catch { }
            }
            Directory.CreateDirectory(extractFolder);

            DiagnosticLog.Write($"Extracting payload to: {extractFolder}");

            using var archive = ZipFile.OpenRead(zipPath);
            int total = archive.Entries.Count;
            // Pre-compute total uncompressed bytes so the bar has an honest
            // denominator — not the compressed size of the .zip on disk.
            long bytesTotal = 0;
            foreach (var e in archive.Entries) bytesTotal += e.Length;

            int done = 0;
            long bytesDone = 0;

            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                done++;

                if (done == 1 || done % 50 == 0 || done == total)
                {
                    statusProgress?.Report($"Extracting mod files ({done}/{total})...");
                    extractProgress?.Report(new ExtractProgress(done, total, bytesDone, bytesTotal));
                }

                if (string.IsNullOrEmpty(entry.Name)) continue;

                var destPath = Path.Combine(extractFolder, entry.FullName);
                var destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destDir))
                    Directory.CreateDirectory(destDir);

                entry.ExtractToFile(destPath, overwrite: true);
                bytesDone += entry.Length;
            }

            // Final 100% report so the bar tops out cleanly before the next phase.
            extractProgress?.Report(new ExtractProgress(done, total, bytesDone, bytesTotal));

            DiagnosticLog.Write($"Extraction complete: {done} entries.");
            return extractFolder;
        }, ct);
    }

    /// <summary>
    /// If <paramref name="destinationFolder"/> contains a `bin\` subfolder
    /// (Steam layout), copies its contents up to the root next to where the
    /// WoL mod binary will land, then deletes `bin\` itself.
    ///
    /// Why: the Steam layout puts age3y.exe + DLLs inside bin\, but the WoL
    /// mod's binary lives at the root and expects its DLLs alongside it
    /// (legacy retail layout). Promoting bin\* to the root resolves this.
    /// Removing bin\ afterwards saves ~3.7 GB of duplicated files AND
    /// prevents the shortcut creator from picking the wrong age3y.exe
    /// (the unmodded one in bin\) instead of the WoL-patched one at root.
    ///
    /// Existing files at the root are NOT overwritten during the promote
    /// step (the next phase — the WoL payload overlay — handles overrides
    /// explicitly).
    /// </summary>
    public static void FlattenBinSubfolder(string destinationFolder, IProgress<string>? statusProgress)
    {
        var binFolder = Path.Combine(destinationFolder, "bin");
        if (!Directory.Exists(binFolder)) return;

        statusProgress?.Report("Promoting bin/ files to root...");
        DiagnosticLog.Write($"Flattening bin\\ subfolder of '{destinationFolder}' to root...");

        int copied = 0;
        int skipped = 0;
        foreach (var srcFile in Directory.EnumerateFiles(binFolder, "*", SearchOption.AllDirectories))
        {
            // Preserve directory structure relative to bin\ when promoting,
            // so e.g. bin\Microsoft.VC80.CRT\msvcr80.dll goes to
            // <root>\Microsoft.VC80.CRT\msvcr80.dll.
            var relative = Path.GetRelativePath(binFolder, srcFile);
            var destFile = Path.Combine(destinationFolder, relative);

            // Don't clobber whatever's already at the root — that file might
            // be a WoL-provided file from the payload (we run before the
            // payload overlay, but in practice the clone runs first so the
            // root is mostly empty here).
            if (File.Exists(destFile)) { skipped++; continue; }

            try
            {
                var destDir = Path.GetDirectoryName(destFile);
                if (!string.IsNullOrEmpty(destDir))
                    Directory.CreateDirectory(destDir);
                File.Copy(srcFile, destFile, overwrite: false);
                copied++;
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"  flatten skip: {relative} — {ex.Message}");
                skipped++;
            }
        }

        DiagnosticLog.Write($"Flatten bin\\ complete: {copied} files promoted, {skipped} skipped.");

        // Now drop the bin\ subfolder entirely. It's redundant after the
        // promotion (the files are already at the root) and keeping it
        // confuses the shortcut step which can't tell which age3y.exe to use.
        try
        {
            Directory.Delete(binFolder, recursive: true);
            DiagnosticLog.Write("Removed redundant bin\\ subfolder after flatten.");
        }
        catch (Exception ex)
        {
            // Non-fatal: install can still succeed with bin\ still in place.
            // The shortcut creator will need to handle the duplicate (the
            // WoL-patched age3y.exe at the root takes priority).
            DiagnosticLog.Write($"Could not remove bin\\ after flatten: {ex.Message}");
        }
    }

    /// <summary>
    /// Copies all files from the extracted WoL payload into the destination,
    /// overwriting existing files. This is what puts the mod on top of the
    /// cloned AoE3.
    /// </summary>
    /// <summary>
    /// Progress reported while copying the extracted mod payload onto the
    /// cloned AoE3 destination. Mirrors <see cref="ExtractProgress"/> but for
    /// the overlay step. Bytes are computed from FileInfo.Length so the UI
    /// can show "X / Y MB" alongside file counts.
    /// </summary>
    public record ModOverlayProgress(
        int FilesDone,
        int FilesTotal,
        long BytesDone,
        long BytesTotal);

    private async Task CopyPayloadToDestinationAsync(
        string extractedFolder,
        string destinationFolder,
        IProgress<string>? statusProgress,
        IProgress<ModOverlayProgress>? overlayProgress,
        CancellationToken ct)
    {
        await Task.Run(() =>
        {
            var files = Directory.GetFiles(extractedFolder, "*", SearchOption.AllDirectories);
            int total = files.Length;
            int done = 0;

            // Pre-sum file sizes so the bar / speed indicator track real bytes,
            // not just file count. Counts are kept around for the status text.
            long bytesTotal = 0;
            foreach (var f in files)
            {
                try { bytesTotal += new FileInfo(f).Length; }
                catch { /* unreadable; skip its size */ }
            }
            long bytesDone = 0;

            DiagnosticLog.Write($"Copying {total} WoL mod files to destination...");

            foreach (var srcFile in files)
            {
                ct.ThrowIfCancellationRequested();

                while (Pause && !ct.IsCancellationRequested)
                    Thread.Sleep(200);

                done++;
                var relativePath = Path.GetRelativePath(extractedFolder, srcFile);
                var destPath = Path.Combine(destinationFolder, relativePath);

                var destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destDir))
                    Directory.CreateDirectory(destDir);

                long srcSize = 0;
                try { srcSize = new FileInfo(srcFile).Length; } catch { }
                File.Copy(srcFile, destPath, overwrite: true);
                bytesDone += srcSize;

                if (done == 1 || done % 100 == 0 || done == total)
                {
                    statusProgress?.Report($"Installing mod files ({done}/{total})...");
                    overlayProgress?.Report(new ModOverlayProgress(done, total, bytesDone, bytesTotal));
                }
            }

            DiagnosticLog.Write($"WoL mod file copy complete: {done} files.");
        }, ct);
    }

    /// <summary>
    /// Creates a Desktop and Start Menu shortcut pointing to age3y.exe
    /// inside the destination folder. Returns the absolute paths of the
    /// shortcuts created (so the install manifest can record them for
    /// later cleanup).
    /// </summary>
    private static List<string> CreateShortcuts(string installFolder, out string? startMenuFolder)
    {
        var created = new List<string>();
        startMenuFolder = null;

        try
        {
            var age3yExe = FindAge3yExe(installFolder);
            if (age3yExe == null)
            {
                DiagnosticLog.Write("Cannot create shortcuts: age3y.exe not found.");
                return created;
            }

            // Use the mod's WoL.ico as the shortcut icon if it shipped with
            // the payload. Falls back to the .exe's own icon if not present.
            var wolIcon = Path.Combine(installFolder, "WoL.ico");
            string? iconPath = File.Exists(wolIcon) ? wolIcon : null;

            // Desktop shortcut
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var desktopLink = Path.Combine(desktopPath, $"{AppName}.lnk");
            CreateShortcutFile(desktopLink, age3yExe, installFolder, iconPath);
            created.Add(desktopLink);

            // Start Menu shortcut
            var startMenuPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                AppName);
            Directory.CreateDirectory(startMenuPath);
            startMenuFolder = startMenuPath;
            var startMenuLink = Path.Combine(startMenuPath, $"{AppName}.lnk");
            CreateShortcutFile(startMenuLink, age3yExe, installFolder, iconPath);
            created.Add(startMenuLink);

            DiagnosticLog.Write(iconPath != null
                ? $"Shortcuts created successfully (icon: {Path.GetFileName(iconPath)})."
                : "Shortcuts created successfully (using default exe icon).");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Shortcut creation failed (non-fatal): {ex.Message}");
        }
        return created;
    }

    /// <summary>
    /// Writes the install manifest (wol-manifest.json) at the root of the
    /// install folder. The manifest records every file and directory the
    /// installer placed on disk so the uninstaller can later delete them
    /// safely without touching anything else (especially Age of Empires III
    /// base game files).
    /// </summary>
    private static void WriteManifest(
        string installFolder,
        string? aoe3SourcePath,
        bool clonedAoe3,
        List<string> shortcuts,
        string? startMenuFolder)
    {
        try
        {
            var (files, dirs) = EnumerateInstalledItems(installFolder);

            var manifest = new InstallManifest
            {
                Version = "1.0.15d",
                InstallPath = installFolder,
                InstalledAt = DateTime.UtcNow,
                Aoe3SourcePath = aoe3SourcePath,
                ClonedAoe3 = clonedAoe3,
                Files = files,
                Directories = dirs,
                Shortcuts = shortcuts,
                StartMenuFolder = startMenuFolder,
            };
            manifest.Save();
            DiagnosticLog.Write($"Install manifest written: {files.Count} files, {dirs.Count} dirs.");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Manifest write failed (non-fatal): {ex.Message}");
        }
    }

    /// <summary>
    /// Walks the install folder and returns the relative paths of every file
    /// and every directory the installer placed there. Excludes the manifest
    /// itself (it'll be deleted last during uninstall).
    /// </summary>
    private static (List<string> Files, List<string> Dirs) EnumerateInstalledItems(string installFolder)
    {
        var files = new List<string>();
        var dirs = new List<string>();

        if (!Directory.Exists(installFolder))
            return (files, dirs);

        // Files (excluding the manifest itself)
        foreach (var f in Directory.EnumerateFiles(installFolder, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(installFolder, f).Replace('\\', '/');
            if (string.Equals(rel, InstallManifest.FileName, StringComparison.OrdinalIgnoreCase))
                continue;
            files.Add(rel);
        }

        // Directories — sort by depth so we record parents before children;
        // the uninstaller will walk this list in reverse to remove leaves first.
        foreach (var d in Directory.EnumerateDirectories(installFolder, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(installFolder, d).Replace('\\', '/');
            dirs.Add(rel);
        }
        dirs.Sort((a, b) => a.Length.CompareTo(b.Length));

        return (files, dirs);
    }

    /// <summary>
    /// Writes the Windows registry entries that allow the launcher's
    /// RegistryService to detect this installation on future runs. Also makes
    /// the installation appear in Add/Remove Programs.
    /// </summary>
    private static void WriteRegistryEntries(string installFolder)
    {
        try
        {
            // Write to HKLM (requires admin) first, fall back to HKCU
            if (!TryWriteRegistryTo(Registry.LocalMachine, installFolder))
            {
                DiagnosticLog.Write("HKLM write failed (no admin); trying HKCU...");
                TryWriteRegistryTo(Registry.CurrentUser, installFolder);
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Registry write failed (non-fatal): {ex.Message}");
        }
    }

    private static bool TryWriteRegistryTo(RegistryKey root, string installFolder)
    {
        try
        {
            var keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + ProductGuid;
            using var key = root.CreateSubKey(keyPath, writable: true);
            if (key == null) return false;

            key.SetValue("DisplayName", AppName);
            key.SetValue("Inno Setup: App Path", installFolder);
            key.SetValue("Path", installFolder);
            key.SetValue("InstallLocation", installFolder);
            key.SetValue("Publisher", "Wars of Liberty Team");
            key.SetValue("DisplayVersion", "1.0.15d");
            key.SetValue("UninstallString", ""); // No uninstaller — user deletes folder
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);

            DiagnosticLog.Write($"Registry written to {root.Name}\\{keyPath}");
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? FindAge3yExe(string installFolder)
    {
        // age3y.exe lives in the bin\ folder inside the AoE3/WoL install
        var binFolder = Path.Combine(installFolder, "bin");
        if (Directory.Exists(binFolder))
        {
            var exe = Path.Combine(binFolder, "age3y.exe");
            if (File.Exists(exe)) return exe;
        }

        // Fallback: search recursively
        var candidates = Directory.GetFiles(installFolder, "age3y.exe", SearchOption.AllDirectories);
        return candidates.FirstOrDefault();
    }

    /// <summary>
    /// Creates a .lnk shortcut file using the Windows Script Host COM object.
    /// When <paramref name="iconPath"/> is provided, the shortcut uses that
    /// .ico file (with index 0) instead of the default icon embedded in the
    /// target executable.
    /// </summary>
    private static void CreateShortcutFile(
        string linkPath, string targetExe, string workingDir, string? iconPath = null)
    {
        // Use IWshRuntimeLibrary via dynamic COM interop (available on all Windows)
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null) return;

        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            var shortcut = shell.CreateShortcut(linkPath);
            shortcut.TargetPath = targetExe;
            shortcut.WorkingDirectory = workingDir;
            shortcut.Description = "Wars of Liberty - Age of Empires III Mod";
            if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
            {
                // "<path>,<index>" — index 0 = first icon in the .ico file
                shortcut.IconLocation = $"{iconPath},0";
            }
            shortcut.Save();
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
        }
    }

    /// <summary>
    /// Cleanup temp files. Call on next startup or after success.
    /// </summary>
    public static void TryCleanupTemp()
    {
        try
        {
            if (Directory.Exists(TempDirectory))
                Directory.Delete(TempDirectory, recursive: true);
        }
        catch { /* ignore */ }
    }
}
