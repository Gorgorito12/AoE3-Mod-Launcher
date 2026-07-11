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
