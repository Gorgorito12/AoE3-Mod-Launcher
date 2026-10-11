using System;
using System.ComponentModel;
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
/// A write that needs admin and nothing else — a mod's private registry key —
/// is done by running ourselves elevated for that one job instead
/// (<see cref="RunSelfElevatedAsync"/>, <see cref="Decide"/>).
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
            var launch = ResolveSelfLaunch(CurrentProcessPath(), AppContext.BaseDirectory, File.Exists);
            if (launch == null) return false;

            var startInfo = new ProcessStartInfo
            {
                FileName = launch.Value.FileName,
                UseShellExecute = true,    // required for Verb to take effect
                Verb = "runas",            // triggers the UAC consent prompt
                Arguments = launch.Value.ArgumentsPrefix + (arguments ?? "")
            };

            Process.Start(startInfo);
            DiagnosticLog.Write($"Successfully launched elevated instance: {startInfo.FileName} {startInfo.Arguments}");
            return true;
        }
        catch (Exception ex)
        {
            // The most common cause is the user clicking "No" on the UAC dialog
            DiagnosticLog.Write($"Failed to relaunch elevated: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// The running executable. <see cref="Environment.ProcessPath"/> is the right primitive — it
    /// works in single-file published builds, where <c>Assembly.Location</c> is empty (IL3000) —
    /// with <c>Process.MainModule</c> kept as a secondary in case it is unavailable.
    /// </summary>
    private static string? CurrentProcessPath()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrEmpty(path))
        {
            try { path = Process.GetCurrentProcess().MainModule?.FileName; }
            catch { path = null; }
        }
        return string.IsNullOrEmpty(path) ? null : path;
    }

    /// <summary>
    /// What to start to run THIS launcher again: the file, and anything that must precede our own
    /// arguments. Null when nothing usable can be found. Pure, so the smoke-test case is pinned.
    ///
    /// <para><b>The case that matters is the <c>dotnet X.dll</c> smoke test.</b> There the process
    /// is <c>dotnet.exe</c> itself, so "start the running executable with these arguments" starts
    /// the .NET HOST, elevated, with arguments meant for the launcher. The old code tried to swap a
    /// <c>.dll</c> path for the <c>.exe</c>, but the process path is never the <c>.dll</c> — that
    /// branch never ran. Here the host is recognised and replaced by the apphost beside the
    /// assembly, or, failing that, by <c>dotnet "&lt;the dll&gt;"</c>.</para>
    /// </summary>
    internal static (string FileName, string ArgumentsPrefix)? ResolveSelfLaunch(
        string? processPath, string baseDirectory, Func<string, bool> fileExists)
    {
        if (!string.IsNullOrEmpty(processPath) && !IsDotnetHost(processPath))
            return (processPath, "");

        if (string.IsNullOrEmpty(baseDirectory)) return null;

        var apphost = Path.Combine(baseDirectory, AutoUpdatePolicy.ExpectedExecutableName);
        if (fileExists(apphost)) return (apphost, "");

        var assembly = Path.Combine(baseDirectory,
            Path.ChangeExtension(AutoUpdatePolicy.ExpectedExecutableName, ".dll"));
        if (!string.IsNullOrEmpty(processPath) && fileExists(assembly))
            return (processPath, $"\"{assembly}\" ");

        return null;
    }

    private static bool IsDotnetHost(string path)
        => string.Equals(Path.GetFileNameWithoutExtension(path), "dotnet", StringComparison.OrdinalIgnoreCase);

    /// <summary>How an elevated run of this launcher ended.</summary>
    public enum ElevatedRunStatus
    {
        /// <summary>It ran and exited; see <see cref="ElevatedRun.ExitCode"/>.</summary>
        Exited,
        /// <summary>The player said no to Windows' consent prompt.</summary>
        Declined,
        /// <summary>Windows would not start it for another reason.</summary>
        StartFailed,
        /// <summary>Still running when we stopped waiting.</summary>
        TimedOut,
        /// <summary>Started, but no handle came back to wait on.</summary>
        NoHandle,
        /// <summary>No executable of ours could be found to start.</summary>
        NoExecutable,
    }

    public readonly record struct ElevatedRun(ElevatedRunStatus Status, int ExitCode = 0, string? Error = null);

    /// <summary>
    /// Runs this launcher elevated with <paramref name="arguments"/> and waits for it to finish —
    /// for a single privileged write (<see cref="PrivateKeyHelper"/>) rather than a whole elevated
    /// session. Off the UI thread: <see cref="Process.Start(ProcessStartInfo)"/> with
    /// <c>runas</c> blocks until the consent prompt is answered.
    /// </summary>
    public static Task<ElevatedRun> RunSelfElevatedAsync(string arguments, TimeSpan timeout)
        => Task.Run(() => RunSelfElevated(arguments, timeout));

    private static ElevatedRun RunSelfElevated(string arguments, TimeSpan timeout)
    {
        var launch = ResolveSelfLaunch(CurrentProcessPath(), AppContext.BaseDirectory, File.Exists);
        if (launch == null) return new ElevatedRun(ElevatedRunStatus.NoExecutable);

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = launch.Value.FileName,
                Arguments = launch.Value.ArgumentsPrefix + arguments,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (process == null) return new ElevatedRun(ElevatedRunStatus.NoHandle);
            if (!process.WaitForExit((int)Math.Min(int.MaxValue, timeout.TotalMilliseconds)))
                return new ElevatedRun(ElevatedRunStatus.TimedOut);
            return new ElevatedRun(ElevatedRunStatus.Exited, process.ExitCode);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return new ElevatedRun(ElevatedRunStatus.Declined);
        }
        catch (Exception ex)
        {
            return new ElevatedRun(ElevatedRunStatus.StartFailed, Error: ex.Message);
        }
    }

    /// <summary>ERROR_CANCELLED: the player answered "No" to the consent prompt.</summary>
    private const int ErrorCancelled = 1223;

    /// <summary>What a write that may need administrator rights should do.</summary>
    public enum ElevationRoute
    {
        /// <summary>Nothing stands in the way.</summary>
        Proceed,
        /// <summary>The folder is writable and only the mod's registry key is missing: ask for that write alone.</summary>
        WritePrivateKey,
        /// <summary>The whole launcher has to restart as administrator.</summary>
        RelaunchElevated,
    }

    /// <summary>
    /// The one rule for choosing between those. Pure, so its point is pinned by a test: <b>a
    /// writable folder never restarts the launcher</b> — when only the registry key is missing,
    /// Windows is asked for that write and the launcher carries on unelevated. The full restart is
    /// left for what really needs it: a folder this account cannot write, or a key that cannot be
    /// expressed on a command line.
    /// </summary>
    public static ElevationRoute Decide(bool folderWritable, bool keyNeedsWrite, bool helperExpressible)
    {
        if (!folderWritable) return ElevationRoute.RelaunchElevated;
        if (!keyNeedsWrite) return ElevationRoute.Proceed;
        return helperExpressible ? ElevationRoute.WritePrivateKey : ElevationRoute.RelaunchElevated;
    }
}
