using System;
using System.IO;

namespace WarsOfLibertyLauncher.Services;

/// <summary>Why the launcher did or did not replace itself on this launch.</summary>
public enum AutoUpdateDecision
{
    /// <summary>Go ahead: download the new binary and restart into it.</summary>
    Apply,

    /// <summary>The check ran and nothing newer exists.</summary>
    NotAvailable,

    /// <summary>A newer release exists but carries no usable .exe asset.</summary>
    NoDownloadUrl,

    /// <summary>The user turned startup checks off — "metered, stay off the network".</summary>
    StartupChecksOff,

    /// <summary>A debug build, a debugger, or <c>--no-update-gate</c>: the maintainer.</summary>
    Bypassed,

    /// <summary>
    /// This launch was started to do one specific job — an elevated mod update
    /// (<c>--update-now</c>), a self-install handoff, or the tail of an update we just did.
    /// Hijacking it would lose the argument that job depends on.
    /// </summary>
    ExplicitTask,

    /// <summary>Not enough room on the volume holding the executable.</summary>
    NotEnoughDisk,

    /// <summary>We are not running as our own .exe (the dotnet host, a renamed copy).</summary>
    NotOurExecutable,

    /// <summary>This exact tag has already been tried and did not stick.</summary>
    AttemptsExhausted,
}

/// <summary>
/// Whether this launch may replace the launcher with a newer release and restart into it,
/// unattended, before the main window opens.
///
/// <para>Pure on purpose — no window, no network, no disk. The orchestration lives in
/// <see cref="StartupUpdateGate"/>; everything that decides lives here, so every refusal
/// below is pinned by a test rather than by someone remembering it.</para>
///
/// <para><b>The three refusals are the point.</b> Each is a silent, expensive failure:</para>
///
/// <para><b>1. We must be running as our own .exe.</b> The swap renames
/// <see cref="Environment.ProcessPath"/>, and the smoke test this repository documents is
/// <c>dotnet bin/Release/net8.0-windows/Aoe3ModLauncher.dll</c> — where ProcessPath is
/// <c>dotnet.exe</c>. Unattended, that would rename the machine's .NET host aside and drop
/// our binary in its place, on every Release smoke test. <see cref="LauncherUpdateService"/>
/// refuses the same thing at the swap itself, which is what also covers the manual dialog.</para>
///
/// <para><b>2. A relaunch that does not stick must never loop.</b> If we restart for tag X
/// and X is still offered on the next launch — a release published without <c>-Version</c>,
/// so its binary reports an older informational tag; the wrong asset; a verification failure
/// — then every launch re-downloads ~165 MB and restarts, for ever. Writing
/// <c>lastInstalledLauncherTag</c> does NOT protect against it:
/// <see cref="LauncherUpdateService.EvaluateUpdate"/> deliberately trusts the running
/// binary's own tag over the saved one. Hence the per-tag latch, which after
/// <see cref="MaxAttemptsPerTag"/> gives up on that tag for good and falls back to the gold
/// pill — the manual path still works, and the user is not held hostage.</para>
///
/// <para><b>3. The maintainer is never auto-updated.</b> A locally published build calls
/// itself <c>v1.0.14</c> (the letter only exists as an argument to <c>publish.ps1</c>), so
/// against a released <c>v1.0.14d</c> it would replace itself with the published build on
/// every single launch. Same bypass as <see cref="LauncherUpdateGate"/>.</para>
/// </summary>
public static class AutoUpdatePolicy
{
    /// <summary>The shipped <c>&lt;AssemblyName&gt;</c>. Anything else is not ours to replace.</summary>
    public const string ExpectedExecutableName = "Aoe3ModLauncher.exe";

    /// <summary>
    /// How many unattended attempts one tag gets. Two, not one: a download can fail for a
    /// reason that has nothing to do with the release (a truncated body that still fails
    /// verification, a half-written swap), and one retry costs a launch while a wrong "give up
    /// for ever" costs the update entirely. Beyond that we stop and let the user decide.
    /// </summary>
    public const int MaxAttemptsPerTag = 2;

    /// <summary>
    /// The half of the decision that is knowable BEFORE the network call, so a launch that
    /// could never apply an update does not spend a request against GitHub's 60/h budget —
    /// or a second of startup — finding that out. <see cref="AutoUpdateDecision.Apply"/>
    /// here means only "the check is worth making".
    /// </summary>
    public static AutoUpdateDecision DecideBeforeCheck(
        bool checkUpdatesOnStartup,
        bool explicitTask,
        bool bypassed,
        string? processPath)
    {
        // The order is load-bearing and is pinned in that order by the tests.
        // StartupChecksOff comes FIRST because it is the only opt-out a metered user has —
        // this feature ships with no setting of its own, and inventing a second way to say
        // "stay off the network" would leave two answers to one question.
        if (!checkUpdatesOnStartup) return AutoUpdateDecision.StartupChecksOff;
        if (explicitTask) return AutoUpdateDecision.ExplicitTask;
        if (bypassed) return AutoUpdateDecision.Bypassed;
        if (!IsOurExecutable(processPath)) return AutoUpdateDecision.NotOurExecutable;
        return AutoUpdateDecision.Apply;
    }

    /// <summary>
    /// The whole decision. Re-runs <see cref="DecideBeforeCheck"/> rather than trusting the
    /// caller to have done it: two entry points that can disagree about the same rule is how
    /// a refusal quietly stops applying.
    /// </summary>
    public static AutoUpdateDecision Decide(
        bool updateAvailable,
        string? downloadUrl,
        string? remoteTag,
        bool checkUpdatesOnStartup,
        bool explicitTask,
        bool bypassed,
        string? processPath,
        string? attemptTag,
        int attemptCount,
        bool enoughDisk)
    {
        var pre = DecideBeforeCheck(checkUpdatesOnStartup, explicitTask, bypassed, processPath);
        if (pre != AutoUpdateDecision.Apply) return pre;

        if (!updateAvailable) return AutoUpdateDecision.NotAvailable;
        if (string.IsNullOrWhiteSpace(downloadUrl) || string.IsNullOrWhiteSpace(remoteTag))
            return AutoUpdateDecision.NoDownloadUrl;

        // The manual dialog warns and lets the user continue anyway. Unattended there is
        // nobody to ask, and filling the volume that holds the executable is not a thing to
        // do on somebody's behalf — so here a shortfall is a refusal, and the pill offers it.
        if (!enoughDisk) return AutoUpdateDecision.NotEnoughDisk;

        if (SameTag(remoteTag, attemptTag) && attemptCount >= MaxAttemptsPerTag)
            return AutoUpdateDecision.AttemptsExhausted;

        return AutoUpdateDecision.Apply;
    }

    /// <summary>
    /// True when the running process is the launcher's own executable — the only file this
    /// feature may rename. Refusal 1 above; see the class remarks for what it costs.
    /// </summary>
    public static bool IsOurExecutable(string? processPath)
    {
        if (string.IsNullOrWhiteSpace(processPath)) return false;
        string name;
        try { name = Path.GetFileName(processPath!); }
        catch { return false; }
        return string.Equals(name, ExpectedExecutableName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The latch value to store before restarting for <paramref name="remoteTag"/>: the same
    /// tag advances its count, a different tag starts over. Written BEFORE the relaunch, so a
    /// restart that never reports back is still counted.
    /// </summary>
    public static int NextAttemptCount(string? remoteTag, string? attemptTag, int attemptCount)
        => SameTag(remoteTag, attemptTag) ? Math.Max(0, attemptCount) + 1 : 1;

    /// <summary>
    /// The latch value that ends a tag's unattended life immediately. Used when the download
    /// verified as something other than what the release promised — a second attempt would
    /// re-download the same bytes and reach the same conclusion.
    /// </summary>
    public static int BurnedAttemptCount => MaxAttemptsPerTag;

    private static bool SameTag(string? a, string? b)
        => !string.IsNullOrWhiteSpace(a)
        && !string.IsNullOrWhiteSpace(b)
        && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
