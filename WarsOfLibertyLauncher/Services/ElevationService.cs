using System;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Helpers for dealing with Windows UAC / administrator privileges.
/// The launcher runs un-elevated by default, but some operations
/// (like writing into C:\Program Files) require admin rights. When that
/// happens, we relaunch ourselves elevated, optionally passing along a
/// command-line flag so the elevated instance can resume what we were doing.
/// </summary>
public static class ElevationService
{
    /// <summary>
    /// True if the current process is running with administrator privileges.
    /// </summary>
    public static bool IsRunningAsAdmin()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Heuristic check: can the current process write into the given folder?
    /// We probe by attempting to create a tiny file in it. This is the most
    /// reliable test on Windows — checking ACLs directly is unreliable
    /// because of token elevation, virtualization, and SYSTEM groups.
    /// </summary>
    public static bool CanWriteTo(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            return false;

        var probePath = Path.Combine(folderPath, $".launcher_probe_{Guid.NewGuid():N}");
        try
        {
            using (File.Create(probePath)) { }
            File.Delete(probePath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Relaunch the launcher with administrator privileges. The current
    /// process exits immediately on success; on failure (user denied UAC)
    /// it returns false and stays running.
    ///
    /// <paramref name="arguments"/> are passed to the new process so the
    /// elevated instance can pick up where this one left off — for example,
    /// passing "--update-now" so the new launcher starts the update
    /// immediately instead of waiting for a button click.
    /// </summary>
    public static bool RelaunchElevated(string? arguments = null)
    {
        try
        {
            // Environment.ProcessPath is the right primitive for "the
            // path of the running .exe" — in particular, it works
            // correctly in single-file published builds. The old fallback
            // here was Assembly.GetEntryAssembly().Location, which the
            // single-file packager warns about (IL3000) because it
            // returns an empty string when assemblies are embedded.
            // Process.MainModule.FileName is kept as a secondary in case
            // ProcessPath is unavailable for some reason.
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
                exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath))
                return false;

            // .NET sometimes returns the .dll path. Replace with the wrapper .exe.
            if (exePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                var exe = Path.ChangeExtension(exePath, ".exe");
                if (File.Exists(exe)) exePath = exe;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,    // required for Verb to take effect
                Verb = "runas",            // triggers the UAC consent prompt
                Arguments = arguments ?? ""
            };

            Process.Start(startInfo);
            DiagnosticLog.Write($"Successfully launched elevated instance: {exePath} {arguments}");
            return true;
        }
        catch (Exception ex)
        {
            // The most common cause is the user clicking "No" on the UAC dialog
            DiagnosticLog.Write($"Failed to relaunch elevated: {ex.Message}");
            return false;
        }
    }
}
