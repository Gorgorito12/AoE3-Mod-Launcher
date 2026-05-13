using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace WarsOfLibertyLauncher.Services.Multiplayer.P2P;

/// <summary>
/// Result of <see cref="WinDivertInstaller.EnsureInstalledAsync"/>. Mirrors
/// the structured-result shape used elsewhere in the multiplayer code
/// so the UI can branch cleanly without parsing exceptions.
/// </summary>
public enum WinDivertInstallResult
{
    AlreadyInstalled,
    InstalledNow,
    UserDeclinedElevation,
    DownloadFailed,
    InstallFailed,
}

/// <summary>
/// First-run bootstrap for the WinDivert userspace DLL + kernel driver.
///
/// We need three artefacts living next to the launcher's <c>.exe</c>:
///   * <c>WinDivert.dll</c>     — the userspace shim P/Invoke targets
///   * <c>WinDivert64.sys</c>   — the EV-signed kernel driver
///   * <c>WinDivert64.cat</c>   — the security catalogue
///
/// The official release page on GitHub ships them inside a zip. We
/// download it, extract just the files we need, and drop them in the
/// launcher's <see cref="AppContext.BaseDirectory"/>. The kernel
/// driver loads on first call to <see cref="WinDivertNative.WinDivertOpen"/>;
/// the OS handles the actual driver service registration via the
/// signed catalogue without a manual <c>sc create</c> step.
///
/// This service does not require admin to download/extract — it only
/// writes to the launcher's own folder. Admin IS required at runtime
/// the first time a process calls WinDivertOpen, because loading a
/// signed driver requires SeLoadDriverPrivilege. The launcher relaunches
/// itself elevated when the multiplayer flow needs WinDivert.
/// </summary>
public static class WinDivertInstaller
{
    /// <summary>
    /// Official release zip — pinned to a known-good version so the
    /// signature catalogue stays consistent across launchers. Update
    /// the URL + expected file names together when bumping.
    /// </summary>
    private const string ReleaseZipUrl =
        "https://github.com/basil00/WinDivert/releases/download/v2.2.2/WinDivert-2.2.2-A.zip";

    /// <summary>Files we lift out of the zip into the launcher folder.</summary>
    private static readonly string[] WantedNames = new[]
    {
        "WinDivert.dll",
        "WinDivert64.sys",
    };

    /// <summary>
    /// Check whether the required artefacts exist next to the .exe.
    /// </summary>
    public static bool IsInstalled()
    {
        var baseDir = AppContext.BaseDirectory;
        foreach (var name in WantedNames)
        {
            if (!File.Exists(Path.Combine(baseDir, name))) return false;
        }
        return true;
    }

    /// <summary>
    /// Ensure WinDivert is on disk next to the launcher. Downloads the
    /// official release zip if needed and extracts the two files we
    /// care about. Idempotent: a re-run when everything's in place
    /// returns <see cref="WinDivertInstallResult.AlreadyInstalled"/>
    /// without touching the network.
    /// </summary>
    public static async Task<WinDivertInstallResult> EnsureInstalledAsync(
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (IsInstalled()) return WinDivertInstallResult.AlreadyInstalled;

        string? tempZip = null;
        try
        {
            tempZip = await DownloadAsync(progress, ct);
            if (string.IsNullOrEmpty(tempZip))
                return WinDivertInstallResult.DownloadFailed;

            ExtractWantedFiles(tempZip, AppContext.BaseDirectory);
            return WinDivertInstallResult.InstalledNow;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"WinDivertInstaller.EnsureInstalledAsync: {ex.Message}");
            return WinDivertInstallResult.InstallFailed;
        }
        finally
        {
            if (tempZip != null)
            {
                try { File.Delete(tempZip); } catch { /* best-effort */ }
            }
        }
    }

    /// <summary>
    /// Relaunch the current process elevated so the first call to
    /// <see cref="WinDivertNative.WinDivertOpen"/> can load the kernel
    /// driver. Passes <paramref name="resumeArgs"/> on the command
    /// line so the elevated instance can pick up where the user
    /// clicked (e.g. <c>--multiplayer-bootstrap</c>).
    ///
    /// Returns false when the user cancelled the UAC prompt; the
    /// caller stays running un-elevated and surfaces the message.
    /// </summary>
    public static bool RelaunchElevated(string? resumeArgs = null)
    {
        try
        {
            var exe = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? "";
            if (string.IsNullOrEmpty(exe)) return false;

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = resumeArgs ?? "",
                UseShellExecute = true,
                Verb = "runas",
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            // The elevated instance takes over; exit ourselves so the
            // user doesn't end up with two launcher windows.
            System.Windows.Application.Current?.Shutdown();
            return true;
        }
        catch (System.ComponentModel.Win32Exception wex) when (wex.NativeErrorCode == 1223)
        {
            // User clicked No on the UAC prompt — caller decides what to do.
            return false;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"WinDivertInstaller.RelaunchElevated: {ex.Message}");
            return false;
        }
    }

    // ---------- internals -----------------------------------------------

    private static async Task<string?> DownloadAsync(IProgress<double>? progress, CancellationToken ct)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"WinDivert-{Guid.NewGuid():N}.zip");
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Aoe3ModLauncher/1.0");
            using var resp = await http.GetAsync(ReleaseZipUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
            {
                DiagnosticLog.Write($"WinDivert zip HTTP {(int)resp.StatusCode}");
                return null;
            }

            var total = resp.Content.Headers.ContentLength ?? -1;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(temp);

            var buf = new byte[80 * 1024];
            long copied = 0;
            int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                copied += n;
                if (total > 0)
                    progress?.Report((double)copied / total);
            }
            return temp;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"WinDivertInstaller.Download: {ex.Message}");
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            return null;
        }
    }

    /// <summary>
    /// Extract just the files we want from the release zip into
    /// <paramref name="destDir"/>. The zip typically contains a
    /// nested <c>x64/</c> folder with the 64-bit binaries; we walk
    /// every entry and pick by filename ignoring the path.
    /// </summary>
    private static void ExtractWantedFiles(string zipPath, string destDir)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            var name = Path.GetFileName(entry.FullName);
            if (string.IsNullOrEmpty(name)) continue;

            bool wanted = false;
            foreach (var w in WantedNames)
            {
                if (string.Equals(w, name, StringComparison.OrdinalIgnoreCase))
                {
                    wanted = true; break;
                }
            }
            if (!wanted) continue;

            // Prefer x64 entries when both archs are present in the zip.
            // Filter on the entry's parent folder name.
            var parent = Path.GetFileName(Path.GetDirectoryName(entry.FullName) ?? "");
            if (!string.IsNullOrEmpty(parent)
                && !parent.Equals("x64", StringComparison.OrdinalIgnoreCase)
                && parent.Contains("32", StringComparison.OrdinalIgnoreCase))
            {
                continue;       // skip 32-bit builds
            }

            var dest = Path.Combine(destDir, name);
            entry.ExtractToFile(dest, overwrite: true);
        }
    }
}
