using System;
using Microsoft.Win32;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Reads / writes the launcher's per-user "start with Windows" registration.
///
/// Windows offers two ways to do this: the registry Run key, and a shortcut
/// in the user's Startup folder. We use the registry key because:
///   * It survives launcher reinstalls (a Startup-folder shortcut becomes
///     stale if the .exe is moved; the registry key just points at a path
///     we update on every save).
///   * It writes under <c>HKCU</c>, so no UAC is needed.
///   * It's the same mechanism most modern apps use, so the user can audit
///     and remove it through Task Manager → Startup tab.
///
/// All operations are best-effort. If the registry write fails (e.g. policy
/// restriction on a managed PC), the launcher logs and continues — the
/// setting just doesn't take effect, the rest of the app still works.
/// </summary>
public static class StartupRegistrationService
{
    /// <summary>
    /// Registry path under HKCU where Windows looks for user-scoped auto-
    /// start entries. <c>HKLM</c> equivalents exist but require admin and
    /// affect every account on the machine; we want per-user.
    /// </summary>
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// Name we register the launcher under. Stable across launcher
    /// versions — if a future build changes branding, keep this string so
    /// we can find and remove the old entry on upgrade.
    /// </summary>
    private const string ValueName = "Aoe3ModLauncher";

    /// <summary>
    /// What the launcher should do about auto-start on this launch. Produced by
    /// <see cref="PlanStartup"/>; executed by <c>MainWindow</c>'s ctor.
    /// </summary>
    /// <param name="SeedNow">
    /// Apply the ON-by-default "run in background" preference to this config: force
    /// the three flags on, set <c>BackgroundDefaultSeeded</c>, and save. The caller
    /// must set the marker BEFORE attempting the registry write, so a failed write
    /// doesn't leave the seed retrying every launch.
    /// </param>
    /// <param name="Register">Write the Run key (true) or clear it (false).</param>
    /// <param name="ShowNotice">Fire the one-time "running in the background" balloon.</param>
    public readonly record struct BackgroundStartupPlan(bool SeedNow, bool Register, bool ShowNotice);

    /// <summary>
    /// Decides auto-start for this launch. Pure — no registry, no config writes — so
    /// the invariants below are unit-testable (<c>BackgroundStartupPlanTests</c>).
    ///
    /// "Run in background" defaults ON, but the flags alone are inert: the Settings
    /// checkbox reads the REGISTRY (<see cref="IsRegistered"/>), and the Run key is
    /// only ever written by <see cref="Apply"/>. So a config that has never been
    /// seeded gets the key written once, which is what makes the default real. A
    /// pre-existing <c>startWithWindows:false</c> is treated as "never chose" rather
    /// than "declined", because the toggle used to default off — hence the seed
    /// FORCES the flags rather than reading them.
    ///
    /// THE INVARIANT: once seeded, the user's flag is obeyed literally and forever.
    /// Keying the write off "the Run key is missing" instead of the marker would
    /// silently re-enable auto-start the launch after an opt-out — a default that
    /// won't stay off is malware behaviour. That is what <paramref name="alreadySeeded"/>
    /// exists to prevent; it is not an optimisation.
    /// </summary>
    /// <param name="alreadySeeded">The config's <c>BackgroundDefaultSeeded</c>.</param>
    /// <param name="startWithWindows">The config's <c>StartWithWindows</c>.</param>
    /// <param name="alreadyRegistered">
    /// <see cref="IsRegistered"/>. Only used to suppress the notice for someone who
    /// had already switched auto-start on by hand — for them the seed is a no-op and
    /// announcing it would be noise.
    /// </param>
    public static BackgroundStartupPlan PlanStartup(bool alreadySeeded, bool startWithWindows, bool alreadyRegistered)
    {
        if (!alreadySeeded)
            return new BackgroundStartupPlan(SeedNow: true, Register: true, ShowNotice: !alreadyRegistered);

        // Seeded: the flag is the user's own answer. Re-applying it each launch
        // self-heals the registered path (the portable exe moves) and clears a stale
        // key after an opt-out; it can never re-arm, because Register mirrors the flag.
        return new BackgroundStartupPlan(SeedNow: false, Register: startWithWindows, ShowNotice: false);
    }

    /// <summary>
    /// True if the registry currently contains an autostart entry for this
    /// launcher (regardless of whether the path it points at still exists).
    /// Used by the Settings dialog to pre-populate the checkbox.
    /// </summary>
    public static bool IsRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            if (key == null) return false;
            return key.GetValue(ValueName) is string s && !string.IsNullOrWhiteSpace(s);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"StartupRegistration: read failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Idempotently apply the user's preference. When <paramref name="enabled"/>
    /// is true, writes a registry value pointing at the running executable's
    /// path. When false, removes any pre-existing entry.
    ///
    /// We always rewrite the path on enable (rather than skipping when the
    /// entry already exists) so that a launcher that has moved on disk gets
    /// the registry pointer updated automatically.
    ///
    /// When <paramref name="startMinimized"/> is true, the registered command
    /// gets a trailing <c>--minimized</c> argument so the AUTO-START launch (this
    /// registry entry, fired at Windows login) opens straight to the tray, while
    /// a manual double-click of the same .exe — which carries no argument — still
    /// shows the window. This is the "run in background" experience: always
    /// running, no window popping up at every login.
    ///
    /// <paramref name="exePathOverride"/> registers a SPECIFIC executable path
    /// instead of the running process's own path. This is needed by the
    /// self-install flow: it runs from the PORTABLE exe but must register the
    /// INSTALLED exe (<see cref="SelfInstallService.CanonicalExe"/>) for auto-start,
    /// so the Run-key points at the copy that will actually persist. Null (the
    /// default) uses <see cref="Environment.ProcessPath"/> — correct for every
    /// normal Settings save, where the running exe IS the one to auto-start.
    ///
    /// Returns true on success, false if the registry operation failed.
    /// </summary>
    public static bool Apply(bool enabled, bool startMinimized = false, string? exePathOverride = null)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (key == null)
            {
                DiagnosticLog.Write($"StartupRegistration: could not open or create '{RunKey}'.");
                return false;
            }

            if (enabled)
            {
                // Environment.ProcessPath is the right primitive for "the
                // running .exe's path" — works in single-file published
                // builds where Assembly.Location returns empty. An explicit
                // override wins (self-install registers the installed copy while
                // still running the portable one).
                var exePath = string.IsNullOrWhiteSpace(exePathOverride)
                    ? Environment.ProcessPath
                    : exePathOverride;
                if (string.IsNullOrWhiteSpace(exePath))
                {
                    DiagnosticLog.Write("StartupRegistration: no exe path; can't register.");
                    return false;
                }

                // Quote the path because Run-key values are interpreted as
                // a command line — a path with spaces (e.g. "Program Files")
                // would otherwise be split at the first space. The optional
                // --minimized arg lives OUTSIDE the quotes so the app's arg
                // parser sees it as a separate token.
                var command = startMinimized ? $"\"{exePath}\" --minimized" : $"\"{exePath}\"";
                key.SetValue(ValueName, command, RegistryValueKind.String);
                DiagnosticLog.Write($"StartupRegistration: registered '{command}'.");
            }
            else
            {
                // DeleteValue throws if the value doesn't exist; the
                // overload with throwOnMissingValue=false is what we want
                // so toggling off twice in a row doesn't crash.
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                DiagnosticLog.Write("StartupRegistration: unregistered.");
            }
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"StartupRegistration: apply failed: {ex.Message}");
            return false;
        }
    }
}
