using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// State of the Windows "DirectPlay" optional feature, which AoE3 2007
/// requires for its LAN multiplayer code paths (it predates DirectX 9's
/// removal of DirectPlay in modern SDKs).
/// </summary>
public enum DirectPlayState
{
    /// <summary>Feature query could not be performed (no DISM, weird OS).</summary>
    Unknown,

    /// <summary>DirectPlay DLLs are present and AoE3 LAN should work.</summary>
    Enabled,

    /// <summary>The feature is missing from this Windows install; AoE3 LAN will not work until it's enabled.</summary>
    Disabled,
}

/// <summary>
/// Result of an attempted <see cref="DirectPlayService.EnableAsync"/> call.
/// </summary>
public enum DirectPlayEnableResult
{
    /// <summary>The feature was already enabled — nothing to do.</summary>
    AlreadyEnabled,

    /// <summary>DISM enabled the feature successfully.</summary>
    EnabledNow,

    /// <summary>The user denied the UAC prompt.</summary>
    UserDeclinedElevation,

    /// <summary>DISM returned a non-zero exit code or could not be launched.</summary>
    DismFailed,
}

/// <summary>
/// Detects and enables Windows' "DirectPlay" optional feature.
///
/// Background: AoE3 (2007) uses Microsoft's DirectPlay networking API.
/// DirectPlay was deprecated in DirectX 9 and removed from the default
/// install on Windows 8 onward. On Windows 10/11 it's an optional
/// "Windows Feature" that must be installed explicitly — without it, the
/// "LAN game" menu in AoE3 fails silently (no broadcast, no peer list),
/// which is exactly the path our v1.0 multiplayer over ZeroTier rides on.
///
/// We use a two-stage approach to stay friendly to non-admin users:
///
///   1. <see cref="DetectAsync"/> probes the System32 DLLs that ship with
///      DirectPlay (`dpnet.dll`, `dplayx.dll`). It's read-only, fast, and
///      doesn't need admin rights — good for the launcher's startup banner.
///
///   2. <see cref="EnableAsync"/> runs DISM elevated (UAC prompt) to
///      actually install the feature. We don't elevate the whole launcher
///      for this — a focused single-shot elevation is much friendlier.
/// </summary>
public static class DirectPlayService
{
    private const string FeatureName = "DirectPlay";

    /// <summary>
    /// Best-effort check: are the DirectPlay runtime DLLs present in
    /// <c>System32</c>? Both files ship with the feature when it's enabled
    /// and disappear when the user (or `dism /disable-feature`) removes it.
    ///
    /// The probe is intentionally lightweight: no DISM call, no admin. It
    /// can be called from the UI thread on launcher start without blocking.
    /// </summary>
    public static Task<DirectPlayState> DetectAsync(CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            try
            {
                var system32 = Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);
                if (string.IsNullOrEmpty(system32))
                    system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);

                // Both DLLs are part of the feature payload. `dplayx.dll`
                // is the classic DirectPlay surface AoE3 uses; `dpnet.dll`
                // is the newer DirectPlay8 layer also included.
                var dplayx = Path.Combine(system32, "dplayx.dll");
                var dpnet = Path.Combine(system32, "dpnet.dll");

                if (File.Exists(dplayx) && File.Exists(dpnet))
                    return DirectPlayState.Enabled;

                // One DLL missing usually means the feature was never
                // installed or someone disabled it manually.
                return DirectPlayState.Disabled;
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"DirectPlayService.DetectAsync: probe failed: {ex.Message}");
                return DirectPlayState.Unknown;
            }
        }, ct);
    }

    /// <summary>
    /// Try to enable the DirectPlay optional feature. Pops a UAC prompt and
    /// blocks until DISM finishes (which can take 10–60 seconds depending
    /// on the disk and on whether Windows needs to fetch the feature
    /// payload). Returns a structured result the UI can map to a status
    /// message.
    ///
    /// Safe to call when the feature is already enabled — the detect probe
    /// short-circuits with <see cref="DirectPlayEnableResult.AlreadyEnabled"/>.
    /// </summary>
    public static async Task<DirectPlayEnableResult> EnableAsync(CancellationToken ct = default)
    {
        var current = await DetectAsync(ct);
        if (current == DirectPlayState.Enabled)
            return DirectPlayEnableResult.AlreadyEnabled;

        var dismPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "dism.exe");

        if (!File.Exists(dismPath))
        {
            DiagnosticLog.Write("DirectPlayService.EnableAsync: dism.exe not found at expected location.");
            return DirectPlayEnableResult.DismFailed;
        }

        var psi = new ProcessStartInfo
        {
            // /quiet + /norestart keeps the DISM console invisible; we
            // surface progress via the launcher UI, not a black box that
            // pops up on the user's screen. /all installs feature
            // dependencies in one go (DirectPlay drags in a tiny shim).
            FileName = dismPath,
            Arguments = $"/online /enable-feature /featurename:{FeatureName} /all /norestart /quiet",
            UseShellExecute = true,         // required for Verb=runas
            Verb = "runas",                 // triggers the UAC prompt
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        try
        {
            using var process = Process.Start(psi);
            if (process == null)
            {
                DiagnosticLog.Write("DirectPlayService.EnableAsync: Process.Start returned null.");
                return DirectPlayEnableResult.DismFailed;
            }

            await process.WaitForExitAsync(ct);

            // DISM exit codes: 0 = success, 3010 = success but reboot
            // pending. Either way DirectPlay is installed and AoE3 will
            // pick it up at next launch (the DLLs are in System32 right
            // away — the reboot is just a defender-side housekeeping
            // request, not a hard requirement for our DLLs).
            if (process.ExitCode == 0 || process.ExitCode == 3010)
                return DirectPlayEnableResult.EnabledNow;

            DiagnosticLog.Write($"DirectPlayService.EnableAsync: DISM exit code {process.ExitCode}.");
            return DirectPlayEnableResult.DismFailed;
        }
        catch (System.ComponentModel.Win32Exception wex)
        {
            // 1223 (ERROR_CANCELLED) = user clicked "No" on the UAC
            // prompt. Treat that as a soft decline, not a hard error.
            if (wex.NativeErrorCode == 1223)
                return DirectPlayEnableResult.UserDeclinedElevation;

            DiagnosticLog.Write($"DirectPlayService.EnableAsync: Win32Exception {wex.NativeErrorCode}: {wex.Message}");
            return DirectPlayEnableResult.DismFailed;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"DirectPlayService.EnableAsync: {ex.Message}");
            return DirectPlayEnableResult.DismFailed;
        }
    }
}
