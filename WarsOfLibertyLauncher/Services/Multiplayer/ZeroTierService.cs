using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Result of <see cref="ZeroTierService.InstallAsync"/>. Mirrors the same
/// "what really happened" shape we use for <see cref="DirectPlayService"/>.
/// </summary>
public enum ZeroTierInstallResult
{
    AlreadyInstalled,
    InstalledNow,
    UserDeclinedElevation,
    DownloadFailed,
    InstallFailed,
}

/// <summary>
/// High-level facade for everything ZeroTier-related on the user's
/// machine. Anything above this layer (UI, lobby flow) talks to
/// <c>ZeroTierService</c> and never touches the local daemon or the
/// installer directly.
///
/// Responsibilities:
///   * Detect install + service state.
///   * Download and install ZeroTier One (elevated).
///   * Bring up the Windows service if it's installed but stopped.
///   * Provide an authenticated <see cref="ZeroTierClient"/> for join/leave.
///
/// Authtoken bootstrap: ZeroTier writes its API token to
/// <c>%PROGRAMDATA%\ZeroTier\One\authtoken.secret</c>, which is admin-read
/// only. ZeroTier's own tray UI copies it to
/// <c>%LOCALAPPDATA%\…\authtoken.secret</c> the first time it opens. If
/// the user never opens the tray, we trigger that copy ourselves via
/// <see cref="EnsureUserAuthTokenAsync"/> (one-shot UAC).
/// </summary>
public static class ZeroTierService
{
    /// <summary>Public MSI URL — official direct download, stable since 2018.</summary>
    public const string InstallerUrl = "https://download.zerotier.com/dist/ZeroTier%20One.msi";

    /// <summary>The Windows service installed by the MSI.</summary>
    public const string ServiceName = "ZeroTierOneService";

    private static readonly string ProgramDataRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ZeroTier", "One");

    private static readonly string LocalAppRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ZeroTier", "One");

    private static readonly string ProgramDataAuthToken =
        Path.Combine(ProgramDataRoot, "authtoken.secret");

    private static readonly string LocalAuthToken =
        Path.Combine(LocalAppRoot, "authtoken.secret");

    /// <summary>
    /// Detect what state ZeroTier is in. Cheap and side-effect-free; safe
    /// to call from the UI thread on launcher start, the same way we
    /// probe DirectPlay.
    /// </summary>
    public static async Task<ZeroTierState> DetectAsync(CancellationToken ct = default)
    {
        // Step 1: is the service registered with Windows?
        if (!IsServiceRegistered())
            return ZeroTierState.NotInstalled;

        // Step 2: is the daemon answering on 9993?
        if (!await ZeroTierClient.IsDaemonReachableAsync(ct: ct))
        {
            // Service may be installed but stopped — UAC-elevated start
            // is one click away.
            return ZeroTierState.InstalledServiceDown;
        }

        // Step 3: do we have the authtoken? Without it the API answers
        // 401 to everything useful.
        var token = ZeroTierClient.TryReadAuthToken();
        if (string.IsNullOrEmpty(token))
            return ZeroTierState.RunningNotAuthorized;

        return ZeroTierState.Running;
    }

    /// <summary>
    /// Build an authenticated client backed by the local authtoken. Returns
    /// null when the token can't be read — callers should surface the
    /// "authorise me" UAC prompt and retry.
    /// </summary>
    public static ZeroTierClient? CreateClient()
    {
        var token = ZeroTierClient.TryReadAuthToken();
        if (string.IsNullOrEmpty(token)) return null;
        return new ZeroTierClient(token);
    }

    /// <summary>
    /// Download the official MSI to a temp file and run msiexec with UAC.
    /// Designed to be called from a "Set up ZeroTier" button — emits no
    /// dialogs itself.
    ///
    /// The MSI registers a service and starts it; on return, the caller
    /// should poll <see cref="DetectAsync"/> until the state is
    /// <see cref="ZeroTierState.Running"/> (typically within 10 s).
    /// </summary>
    public static async Task<ZeroTierInstallResult> InstallAsync(
        IProgress<double>? downloadProgress = null,
        CancellationToken ct = default)
    {
        if (IsServiceRegistered())
            return ZeroTierInstallResult.AlreadyInstalled;

        string? tempPath = null;
        try
        {
            tempPath = await DownloadInstallerAsync(downloadProgress, ct);
            if (string.IsNullOrEmpty(tempPath))
                return ZeroTierInstallResult.DownloadFailed;

            var psi = new ProcessStartInfo
            {
                FileName = "msiexec.exe",
                // /qn = no UI, /norestart = never auto-reboot. The MSI
                // installs the service and starts it automatically.
                Arguments = $"/i \"{tempPath}\" /qn /norestart",
                UseShellExecute = true,         // for the runas verb
                Verb = "runas",                 // UAC prompt
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            using var process = Process.Start(psi);
            if (process == null) return ZeroTierInstallResult.InstallFailed;

            await process.WaitForExitAsync(ct);

            if (process.ExitCode == 0 || process.ExitCode == 3010)
                return ZeroTierInstallResult.InstalledNow;

            DiagnosticLog.Write($"ZeroTierService.InstallAsync: msiexec exit {process.ExitCode}");
            return ZeroTierInstallResult.InstallFailed;
        }
        catch (System.ComponentModel.Win32Exception wex) when (wex.NativeErrorCode == 1223)
        {
            // 1223 = user clicked No on UAC.
            return ZeroTierInstallResult.UserDeclinedElevation;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"ZeroTierService.InstallAsync: {ex.Message}");
            return ZeroTierInstallResult.InstallFailed;
        }
        finally
        {
            if (tempPath != null) TryDelete(tempPath);
        }
    }

    /// <summary>
    /// Best-effort start of the ZeroTier service when the user already
    /// has it installed but it's not running. Pops a UAC prompt because
    /// <c>sc start</c> against a system service requires admin rights.
    /// </summary>
    public static async Task<bool> StartServiceAsync(CancellationToken ct = default)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"start {ServiceName}",
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using var process = Process.Start(psi);
            if (process == null) return false;
            await process.WaitForExitAsync(ct);
            // `sc start` returns 0 on launch even when the service was
            // already running, so we don't gate on exit code — we just
            // poll DetectAsync from the caller.
            return true;
        }
        catch (System.ComponentModel.Win32Exception wex) when (wex.NativeErrorCode == 1223)
        {
            return false;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"ZeroTierService.StartServiceAsync: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// One-shot UAC copy of the ProgramData authtoken into the user-local
    /// path so subsequent runs can talk to the daemon without elevation.
    /// Equivalent to "open the ZeroTier tray once" but unattended.
    /// </summary>
    public static async Task<bool> EnsureUserAuthTokenAsync(CancellationToken ct = default)
    {
        if (File.Exists(LocalAuthToken)) return true;
        if (!File.Exists(ProgramDataAuthToken))
        {
            // Service hasn't generated the token yet — daemon probably
            // not fully up. Give the caller a chance to retry.
            DiagnosticLog.Write("ZeroTierService.EnsureUserAuthTokenAsync: ProgramData token missing.");
            return false;
        }

        try
        {
            // Use PowerShell with the runas verb. A direct File.Copy
            // would fail under non-admin; this asks the user once and
            // then every later call can read it without UAC.
            var localDir = Path.GetDirectoryName(LocalAuthToken)!;
            Directory.CreateDirectory(localDir); // doesn't need admin
            var script =
                $"$ErrorActionPreference='Stop';" +
                $"$src='{ProgramDataAuthToken.Replace("'", "''")}';" +
                $"$dst='{LocalAuthToken.Replace("'", "''")}';" +
                $"Copy-Item -Path $src -Destination $dst -Force";

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            await p.WaitForExitAsync(ct);
            return File.Exists(LocalAuthToken);
        }
        catch (System.ComponentModel.Win32Exception wex) when (wex.NativeErrorCode == 1223)
        {
            return false;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"ZeroTierService.EnsureUserAuthTokenAsync: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Tests if ZeroTier is installed. Two cheap probes (no admin):
    ///
    ///   * The MSI always installs <c>zerotier-one.exe</c> under
    ///     <c>%ProgramFiles(x86)%\ZeroTier\One\</c> (the MSI is 32-bit
    ///     even on 64-bit Windows, so it never lands in Program Files).
    ///   * If the path probe is inconclusive — e.g. the user moved the
    ///     install — fall back to <c>sc query</c>, which returns exit
    ///     code 0 only when the service is registered with Windows.
    ///
    /// We deliberately avoid the <c>System.ServiceProcess.ServiceController</c>
    /// type to keep the launcher off an extra NuGet reference.
    /// </summary>
    private static bool IsServiceRegistered()
    {
        var exeCandidates = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "ZeroTier", "One", "zerotier-one.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "ZeroTier", "One", "zerotier-one.exe"),
        };
        foreach (var p in exeCandidates)
        {
            try { if (File.Exists(p)) return true; }
            catch { /* permissions-protected path; fall through to sc */ }
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"query {ServiceName}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            // 4s is plenty — `sc query` is local and synchronous; if it
            // hangs, something is more wrong than service detection.
            if (!p.WaitForExit(4000))
            {
                try { p.Kill(); } catch { /* best-effort */ }
                return false;
            }
            return p.ExitCode == 0;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"ZeroTierService.IsServiceRegistered: sc query failed: {ex.Message}");
            return false;
        }
    }

    private static async Task<string?> DownloadInstallerAsync(
        IProgress<double>? progress,
        CancellationToken ct)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"ZeroTier-One-{Guid.NewGuid():N}.msi");
        try
        {
            // Reuse the launcher's standard download settings: long
            // timeout, UA, redirect support. We don't share the project's
            // DownloadService because it's tuned for resumable .tar.xz
            // and adds complexity we don't need for a one-shot MSI grab.
            using var http = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(5),
            };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Aoe3ModLauncher/1.0");

            using var resp = await http.GetAsync(InstallerUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
            {
                DiagnosticLog.Write($"ZeroTierService: MSI download HTTP {(int)resp.StatusCode}");
                return null;
            }

            var total = resp.Content.Headers.ContentLength ?? -1;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(tempPath);

            var buffer = new byte[80 * 1024];
            long copied = 0;
            int read;
            while ((read = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                copied += read;
                if (total > 0 && progress != null)
                {
                    progress.Report((double)copied / total);
                }
            }

            return tempPath;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"ZeroTierService.DownloadInstallerAsync: {ex.Message}");
            TryDelete(tempPath);
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort */ }
    }
}
