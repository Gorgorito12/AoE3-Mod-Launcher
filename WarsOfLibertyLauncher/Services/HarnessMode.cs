using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// What lets <c>tools/testbed</c> run the REAL launcher beside the player's own: a second
/// instance with its own mutex, its own data folder, and nothing it does reaching the machine.
///
/// <para>Without it a test launch is worthless or dangerous. The single-instance mutex makes a
/// second copy raise the running launcher and exit "successfully" having parsed nothing, and
/// <see cref="AppPaths.DataDirOverrideVariable"/> alone still re-registers auto-start and the
/// <c>wol-launcher://</c> scheme for that copy, and heals junctions a running game may need.</para>
///
/// <para><b>Three conditions, all required, and the refusals are the point</b>
/// (<c>HarnessModeTests</c>): the variable, a developer build, and a data folder that is not the
/// player's. A published launcher ignores the variable, so no setting or stray environment can
/// turn off its auto-start or stop it opening a link.</para>
///
/// <para>Every process the launcher starts goes through <see cref="Start"/>, and the operations
/// that write outside the data folder ask <see cref="Block"/> at their entry. A blocked action
/// leaves a <c>HARNESS BLOCKED</c> log line, which is how the testbed proves that a button did
/// its job without anything opening.</para>
/// </summary>
public static class HarnessMode
{
    public const string Variable = "AOE3ML_HARNESS";

    /// <summary>The Documents folder a test run reads and writes instead of the player's.</summary>
    public const string DocumentsVariable = "AOE3ML_DOCUMENTS";

    /// <summary>The folder a test run may install into for real, for the writes nothing else proves.</summary>
    public const string SandboxVariable = "AOE3ML_SANDBOX";

    /// <summary>The prefix the testbed reads back to assert a button reached its action.</summary>
    public const string BlockedPrefix = "HARNESS BLOCKED";

    private static readonly Lazy<bool> s_active = new(() => Decide(
        Environment.GetEnvironmentVariable(Variable),
        Environment.GetEnvironmentVariable(AppPaths.DataDirOverrideVariable),
        AppPaths.DefaultDataDir,
        LauncherUpdateGate.IsDeveloperBuild(
            AppContext.BaseDirectory,
            LauncherUpdateService.RunningImageLength(Environment.ProcessPath) ?? long.MaxValue)));

    /// <summary>Read once: a run cannot change its mind halfway.</summary>
    public static bool Active => s_active.Value;

    /// <summary>
    /// Null outside the harness, so a stray variable can never move a player's saved games.
    /// Inside it, backups, restores and imports land in the test run's own folder.
    /// </summary>
    public static string? DocumentsRoot => DocumentsRootFor(
        Active, Environment.GetEnvironmentVariable(DocumentsVariable));

    /// <summary>
    /// For writes to the saved-game folders: the harness blocks them, unless the run has its own
    /// copy of Documents to write into.
    /// </summary>
    public static bool BlockUserData(string what) => DocumentsRoot == null && Block(what);

    /// <summary>
    /// For an operation on an install: the harness blocks it unless that install lives inside the
    /// run's own sandbox, so Repair can be pressed for real on a mod that is there to be damaged.
    /// </summary>
    public static bool BlockOutsideSandbox(string what, string? installPath)
        => !IsInsideSandbox(Active, Environment.GetEnvironmentVariable(SandboxVariable), installPath)
           && Block(what);

    /// <summary>Whole path segments: <c>out\sandbox2</c> is not inside <c>out\sandbox</c>.</summary>
    internal static bool IsInsideSandbox(bool active, string? sandbox, string? path)
    {
        if (!active || string.IsNullOrWhiteSpace(sandbox) || string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sandbox)) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    internal static string? DocumentsRootFor(bool active, string? value)
        => active && !string.IsNullOrWhiteSpace(value) ? value : null;

    internal static bool Decide(string? flag, string? dataDirOverride, string defaultDataDir, bool developerBuild)
    {
        if (flag != "1" || !developerBuild) return false;
        if (string.IsNullOrWhiteSpace(dataDirOverride)) return false;
        return !SamePath(dataDirOverride, defaultDataDir);
    }

    private static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            // A path that cannot be resolved cannot be proved different from the player's.
            return true;
        }
    }

    /// <summary>
    /// Empty outside the harness, so the single-instance names stay byte for byte what every
    /// installed launcher uses. Derived from the data folder so two runs with one folder still
    /// exclude each other.
    /// </summary>
    public static string InstanceSuffix => Active ? SuffixFor(AppPaths.DataDir) : "";

    internal static string SuffixFor(string dataDir)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDir)).ToUpperInvariant()));
        return ".harness-" + Convert.ToHexString(bytes)[..12].ToLowerInvariant();
    }

    /// <summary>True when the caller must skip its work; the skipped work is on the log.</summary>
    public static bool Block(string what)
    {
        if (!Active) return false;
        DiagnosticLog.Write($"{BlockedPrefix} {what}");
        return true;
    }

    /// <summary>
    /// <see cref="Process.Start(ProcessStartInfo)"/> for every process the launcher starts.
    /// Null when blocked, which the callers already handle: Process.Start returns null too.
    /// </summary>
    public static Process? Start(ProcessStartInfo startInfo)
    {
        var what = string.IsNullOrEmpty(startInfo.Arguments)
            ? startInfo.FileName
            : $"{startInfo.FileName} {startInfo.Arguments}";
        return Block($"start {what}") ? null : Process.Start(startInfo);
    }
}
