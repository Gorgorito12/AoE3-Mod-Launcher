using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Handles the full Wars of Liberty installation flow when the mod is not yet
/// present on the machine.
///
/// Why a ZIP and not a single .exe:
///   The official installer is built with Inno Setup using DiskSpanning, which
///   produces a small launcher .exe plus several large .bin data files (~2.7 GB
///   total). The .exe alone won't work — it expects the .bin files in the same
///   folder. The aoe3wol.com server distributes all of them packaged in a ZIP,
///   which is what we download here.
///
/// Flow:
///   1. Download the ZIP to a temp folder (with progress + resume support)
///   2. Extract it to a sibling folder
///   3. Locate the .exe inside the extracted folder
///   4. Run it (Inno Setup wizard takes over from here)
///   5. Clean up after the user closes the wizard
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
    /// Download the installer ZIP from the given URL and save it to a temp file.
    /// Supports resume — if the download was interrupted, picks up where it left off.
    /// </summary>
    public async Task<string> DownloadInstallerZipAsync(
        string zipUrl,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(TempDirectory);

        // Use the ZIP's filename if possible, otherwise a generic one
        var fileName = "WarsOfLibertySetup.zip";
        try
        {
            var uri = new Uri(zipUrl);
            var lastSegment = Uri.UnescapeDataString(Path.GetFileName(uri.LocalPath));
            if (!string.IsNullOrEmpty(lastSegment) &&
                lastSegment.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                fileName = lastSegment;
            }
        }
        catch
        {
            // Use the default name if the URL is malformed
        }

        var destPath = Path.Combine(TempDirectory, fileName);

        DiagnosticLog.Write($"Downloading installer ZIP from: {zipUrl}");
        DiagnosticLog.Write($"  -> {destPath}");

        await _downloader.DownloadFileAsync(zipUrl, destPath, progress, ct);

        DiagnosticLog.Write($"Installer ZIP downloaded ({new FileInfo(destPath).Length} bytes).");
        return destPath;
    }

    /// <summary>
    /// Extract the ZIP into a sibling folder. Returns the folder path.
    /// </summary>
    public Task<string> ExtractInstallerZipAsync(
        string zipPath,
        IProgress<string>? statusProgress = null,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            // Extract to a folder named after the ZIP (without .zip extension)
            var extractFolder = Path.Combine(TempDirectory,
                Path.GetFileNameWithoutExtension(zipPath));

            // Clean up any prior extraction so we start fresh
            if (Directory.Exists(extractFolder))
            {
                try { Directory.Delete(extractFolder, recursive: true); } catch { /* ignored */ }
            }
            Directory.CreateDirectory(extractFolder);

            DiagnosticLog.Write($"Extracting ZIP to: {extractFolder}");

            using var archive = ZipFile.OpenRead(zipPath);
            int totalEntries = archive.Entries.Count;
            int extracted = 0;

            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                extracted++;

                // Report progress periodically (not on every file — too noisy)
                if (extracted == 1 || extracted % 5 == 0 || extracted == totalEntries)
                {
                    statusProgress?.Report(
                        $"Extracting installer files ({extracted}/{totalEntries})...");
                }

                // Skip directories
                if (string.IsNullOrEmpty(entry.Name)) continue;

                // Zip-slip defence: reject entries whose resolved path would
                // escape extractFolder. Mirrors ArchiveService.ExtractZipWithBackupAsync.
                var destPath = Path.GetFullPath(Path.Combine(extractFolder, entry.FullName));
                var extractRoot = Path.GetFullPath(extractFolder)
                    .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!destPath.StartsWith(extractRoot, StringComparison.OrdinalIgnoreCase))
                {
                    DiagnosticLog.Write(
                        $"Zip-slip: rejecting entry '{entry.FullName}' that would escape '{extractFolder}'.");
                    continue;
                }
                var destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destDir))
                    Directory.CreateDirectory(destDir);

                entry.ExtractToFile(destPath, overwrite: true);
            }

            DiagnosticLog.Write($"Extraction complete: {extracted} files.");
            return extractFolder;
        }, ct);
    }

    /// <summary>
    /// Find the installer's main .exe inside an extracted folder. We look for
    /// the largest .exe whose name contains "setup" — robust against the file
    /// being named slightly differently between versions (e.g. "Wars of Liberty
    /// Setup - v1.0.15d.exe", "WoL_Setup.exe", etc.).
    /// </summary>
    public static string? FindInstallerExe(string extractedFolder)
    {
        if (!Directory.Exists(extractedFolder)) return null;

        var candidates = Directory.GetFiles(extractedFolder, "*.exe", SearchOption.AllDirectories);
        if (candidates.Length == 0) return null;

        // Prefer files whose name contains "setup"; fall back to all .exes if none match
        var setupExes = candidates
            .Where(p => Path.GetFileName(p).Contains("setup", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var pool = setupExes.Length > 0 ? setupExes : candidates;

        // Pick the biggest — Inno Setup launchers are usually the largest .exe
        return pool
            .OrderByDescending(p => new FileInfo(p).Length)
            .First();
    }

    /// <summary>
    /// Launch the installer in silent mode at the specified directory.
    ///
    /// Inno Setup silent mode behavior:
    ///   /VERYSILENT        — no wizard window at all (still shows UAC prompt
    ///                        and a Windows-style progress dialog when admin
    ///                        is needed; we accept that)
    ///   /SUPPRESSMSGBOXES  — auto-answer "yes" to confirmations like overwrite
    ///   /DIR="..."         — install destination folder
    ///   /NOCANCEL          — disable the cancel button (we don't surface a
    ///                        cancel mid-install since rolling back is risky)
    ///   /LOG="..."         — write a detailed install log to this path so we
    ///                        can show progress and detect completion
    ///   /NORESTART         — never reboot the machine on our behalf
    /// </summary>
    public static Process RunInstallerSilent(
        string installerPath,
        string installFolder,
        string? logPath = null)
    {
        if (!File.Exists(installerPath))
            throw new FileNotFoundException($"Installer not found at {installerPath}");

        // Inno Setup expects the /DIR argument quoted only with the value,
        // not with the parameter name. Be careful with paths that contain
        // spaces or trailing backslashes.
        var dir = installFolder.TrimEnd('\\', '/');

        var args =
            "/VERYSILENT " +
            "/SUPPRESSMSGBOXES " +
            "/NOCANCEL " +
            "/NORESTART " +
            $"/DIR=\"{dir}\"";

        if (!string.IsNullOrEmpty(logPath))
            args += $" /LOG=\"{logPath}\"";

        DiagnosticLog.Write($"Running installer (silent): {installerPath}");
        DiagnosticLog.Write($"  args: {args}");

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = args,
            WorkingDirectory = Path.GetDirectoryName(installerPath),
            UseShellExecute = true     // required for UAC prompt
        });

        if (process == null)
            throw new InvalidOperationException("Process.Start returned null for installer.");

        return process;
    }

    /// <summary>
    /// Launch the installer with the standard Inno Setup wizard. The user
    /// chooses the install folder and walks through the steps normally.
    /// </summary>
    public static Process RunInstaller(string installerPath)
    {
        if (!File.Exists(installerPath))
            throw new FileNotFoundException($"Installer not found at {installerPath}");

        DiagnosticLog.Write($"Running installer: {installerPath}");

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            // The .bin data files live next to the .exe. Setting the working
            // directory ensures Inno Setup finds them on every Windows version.
            WorkingDirectory = Path.GetDirectoryName(installerPath),
            UseShellExecute = true     // required so Windows prompts for UAC
        });

        if (process == null)
            throw new InvalidOperationException("Process.Start returned null for installer.");

        return process;
    }

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

    /// <summary>Open a URL in the user's default browser.</summary>
    public static void OpenWebsite(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }
}
