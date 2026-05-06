using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

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
        CancellationToken ct = default)
    {
        DiagnosticLog.Write($"=== Native Install Start ===");
        DiagnosticLog.Write($"  Parts: {payloadZipUrls.Length}");
        DiagnosticLog.Write($"  AoE3 Source: {aoe3SourcePath}");
        DiagnosticLog.Write($"  Destination: {destinationFolder}");

        // ---- Phase 1: Download all parts and concatenate ----
        statusProgress?.Report("Downloading Wars of Liberty files...");
        var zipPath = await DownloadAndConcatenatePartsAsync(payloadZipUrls, downloadProgress, ct);

        // ---- Phase 2: Extract payload to temp ----
        statusProgress?.Report("Extracting mod files...");
        var extractedFolder = await ExtractPayloadAsync(zipPath, statusProgress, ct);

        // ---- Phase 3: Clone AoE3 to destination ----
        statusProgress?.Report("Copying Age of Empires III files...");
        await _cloneService.CloneAsync(aoe3SourcePath, destinationFolder, cloneProgress, ct);

        // ---- Phase 4: Copy WoL files on top ----
        statusProgress?.Report("Installing Wars of Liberty mod files...");
        await CopyPayloadToDestinationAsync(extractedFolder, destinationFolder, statusProgress, ct);

        // ---- Phase 5: Create shortcuts ----
        statusProgress?.Report("Creating shortcuts...");
        CreateShortcuts(destinationFolder);

        // ---- Phase 6: Write registry ----
        statusProgress?.Report("Writing registry entries...");
        WriteRegistryEntries(destinationFolder);

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
        CancellationToken ct = default)
    {
        DiagnosticLog.Write($"=== Native Install (mod-only) Start ===");
        DiagnosticLog.Write($"  Parts: {payloadZipUrls.Length}");
        DiagnosticLog.Write($"  Destination: {destinationFolder}");

        // ---- Phase 1: Download ----
        statusProgress?.Report("Downloading Wars of Liberty files...");
        var zipPath = await DownloadAndConcatenatePartsAsync(payloadZipUrls, downloadProgress, ct);

        // ---- Phase 2: Extract ----
        statusProgress?.Report("Extracting mod files...");
        var extractedFolder = await ExtractPayloadAsync(zipPath, statusProgress, ct);

        // ---- Phase 3: Copy WoL on top ----
        statusProgress?.Report("Installing Wars of Liberty mod files...");
        await CopyPayloadToDestinationAsync(extractedFolder, destinationFolder, statusProgress, ct);

        // ---- Phase 4: Shortcuts + Registry ----
        statusProgress?.Report("Creating shortcuts...");
        CreateShortcuts(destinationFolder);
        WriteRegistryEntries(destinationFolder);

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
        CancellationToken ct)
    {
        Directory.CreateDirectory(TempDirectory);

        var combinedZipPath = Path.Combine(TempDirectory, "WolPayload.zip");

        // Delete any previous combined file
        try { if (File.Exists(combinedZipPath)) File.Delete(combinedZipPath); } catch { }

        DiagnosticLog.Write($"Downloading {partUrls.Length} ZIP parts...");

        long totalDownloaded = 0;

        for (int i = 0; i < partUrls.Length; i++)
        {
            ct.ThrowIfCancellationRequested();

            var partUrl = partUrls[i];
            var partFileName = $"WolPayload.zip.{(i + 1):D3}";
            var partPath = Path.Combine(TempDirectory, partFileName);

            DiagnosticLog.Write($"  Part {i + 1}/{partUrls.Length}: {partUrl}");

            // Wrap progress to show overall across all parts
            int partIndex = i;
            long partStartBytes = totalDownloaded;
            var partProgress = new Progress<DownloadProgress>(p =>
            {
                // Report combined progress: this part's bytes + previous parts
                var combined = new DownloadProgress(
                    BytesReceived: partStartBytes + p.BytesReceived,
                    TotalBytes: 0, // We don't know total across all parts upfront
                    Percentage: (double)(partIndex * 100 + p.Percentage) / partUrls.Length);
                progress?.Report(combined);
            });

            await _downloader.DownloadFileAsync(partUrl, partPath, partProgress, ct);

            totalDownloaded += new FileInfo(partPath).Length;
            DiagnosticLog.Write($"  Part {i + 1} done ({new FileInfo(partPath).Length} bytes).");
        }

        // Concatenate all parts into one ZIP
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

    private Task<string> ExtractPayloadAsync(
        string zipPath,
        IProgress<string>? statusProgress,
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
            int done = 0;

            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                done++;

                if (done == 1 || done % 50 == 0 || done == total)
                    statusProgress?.Report($"Extracting mod files ({done}/{total})...");

                if (string.IsNullOrEmpty(entry.Name)) continue;

                var destPath = Path.Combine(extractFolder, entry.FullName);
                var destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destDir))
                    Directory.CreateDirectory(destDir);

                entry.ExtractToFile(destPath, overwrite: true);
            }

            DiagnosticLog.Write($"Extraction complete: {done} entries.");
            return extractFolder;
        }, ct);
    }

    /// <summary>
    /// Copies all files from the extracted WoL payload into the destination,
    /// overwriting existing files. This is what puts the mod on top of the
    /// cloned AoE3.
    /// </summary>
    private async Task CopyPayloadToDestinationAsync(
        string extractedFolder,
        string destinationFolder,
        IProgress<string>? statusProgress,
        CancellationToken ct)
    {
        await Task.Run(() =>
        {
            var files = Directory.GetFiles(extractedFolder, "*", SearchOption.AllDirectories);
            int total = files.Length;
            int done = 0;

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

                File.Copy(srcFile, destPath, overwrite: true);

                if (done == 1 || done % 100 == 0 || done == total)
                    statusProgress?.Report($"Installing mod files ({done}/{total})...");
            }

            DiagnosticLog.Write($"WoL mod file copy complete: {done} files.");
        }, ct);
    }

    /// <summary>
    /// Creates a Desktop and Start Menu shortcut pointing to age3y.exe
    /// inside the destination folder.
    /// </summary>
    private static void CreateShortcuts(string installFolder)
    {
        try
        {
            var age3yExe = FindAge3yExe(installFolder);
            if (age3yExe == null)
            {
                DiagnosticLog.Write("Cannot create shortcuts: age3y.exe not found.");
                return;
            }

            // Desktop shortcut
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var desktopLink = Path.Combine(desktopPath, $"{AppName}.lnk");
            CreateShortcutFile(desktopLink, age3yExe, installFolder);

            // Start Menu shortcut
            var startMenuPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                AppName);
            Directory.CreateDirectory(startMenuPath);
            var startMenuLink = Path.Combine(startMenuPath, $"{AppName}.lnk");
            CreateShortcutFile(startMenuLink, age3yExe, installFolder);

            DiagnosticLog.Write("Shortcuts created successfully.");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Shortcut creation failed (non-fatal): {ex.Message}");
        }
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
    /// </summary>
    private static void CreateShortcutFile(string linkPath, string targetExe, string workingDir)
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
