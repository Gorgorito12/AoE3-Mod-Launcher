using System;
using System.Diagnostics;
using System.IO;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Lightweight "install to a stable location" for the portable single-file exe.
///
/// The launcher ships as one self-contained, signed .exe (no Inno-Setup / MSI —
/// see CLAUDE.md). That's great for distribution but fragile for the
/// "run in background / start with Windows" experience: if the user runs it from
/// Downloads and later moves or deletes that file, auto-start breaks. This service
/// copies the running .exe into a canonical per-user location, drops Start-Menu +
/// Desktop shortcuts, and relaunches from there — WITHOUT reintroducing an
/// installer toolchain. The existing self-update (<see cref="LauncherUpdateService"/>)
/// then swaps the binary in place at that stable path, and the auto-start
/// registration (<see cref="StartupRegistrationService"/>, which records
/// <see cref="Environment.ProcessPath"/>) points at it once we relaunch.
///
/// Everything is best-effort and OPT-IN — a portable user who never installs is
/// never forced to; nothing here runs automatically.
/// </summary>
public static class SelfInstallService
{
    /// <summary>Command-line flag the relaunched (installed) process carries so
    /// the single-instance guard waits for the exiting portable parent to release
    /// the mutex instead of treating the relaunch as a duplicate and exiting.</summary>
    public const string FromInstallArg = "--from-install";

    /// <summary>Per-user install directory: <c>%LocalAppData%\Programs\Aoe3ModLauncher</c>.
    /// Per-user (no admin/UAC), the conventional home for user-scoped apps, and on
    /// the same volume as <c>%LocalAppData%</c> so the copy is cheap.</summary>
    public static string CanonicalDir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "Aoe3ModLauncher");

    /// <summary>The installed executable path (matches the shipped
    /// <c>&lt;AssemblyName&gt;</c>, <c>Aoe3ModLauncher.exe</c>).</summary>
    public static string CanonicalExe => Path.Combine(CanonicalDir, "Aoe3ModLauncher.exe");

    /// <summary>True when the running process IS the installed copy (path-equal to
    /// <see cref="CanonicalExe"/>). Used to hide/disable the install action and to
    /// skip any first-run install prompt.</summary>
    public static bool IsInstalled()
    {
        try
        {
            var current = Environment.ProcessPath;
            if (string.IsNullOrEmpty(current)) return false;
            return string.Equals(
                Path.GetFullPath(current),
                Path.GetFullPath(CanonicalExe),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"SelfInstall.IsInstalled: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Copy the running .exe into <see cref="CanonicalDir"/> and create Start-Menu
    /// + Desktop shortcuts pointing at it. Does NOT enable auto-start (that's the
    /// separate "Run in background" toggle) and does NOT relaunch — the caller does
    /// that via <see cref="RelaunchInstalledAndExit"/> after confirming success.
    /// Returns (ok, message). Idempotent: re-installing overwrites the copy +
    /// shortcuts.
    /// </summary>
    public static (bool ok, string message) Install()
    {
        try
        {
            var source = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
                return (false, "Could not resolve the running executable path.");

            if (IsInstalled())
                return (true, "Already installed.");

            Directory.CreateDirectory(CanonicalDir);

            // Copy the single-file exe. It carries no side files (self-contained),
            // so a plain file copy is the whole payload. Overwrite a stale prior
            // install (not running — the single-instance guard guarantees no other
            // instance holds it).
            File.Copy(source, CanonicalExe, overwrite: true);
            DiagnosticLog.Write($"SelfInstall: copied '{source}' -> '{CanonicalExe}'.");

            CreateShortcuts();
            return (true, CanonicalExe);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"SelfInstall.Install failed: {ex.Message}");
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Launch the installed copy and shut this (portable) instance down. The child
    /// is started with <see cref="FromInstallArg"/> so the single-instance guard in
    /// <c>App.OnStartup</c> waits for this process to exit (releasing the mutex)
    /// instead of exiting itself as a duplicate. Best-effort: on failure the caller
    /// keeps running the portable instance.
    /// </summary>
    public static bool RelaunchInstalledAndExit()
    {
        try
        {
            if (!File.Exists(CanonicalExe))
                return false;

            Process.Start(new ProcessStartInfo
            {
                FileName = CanonicalExe,
                Arguments = FromInstallArg,
                UseShellExecute = true,
                WorkingDirectory = CanonicalDir,
            });
            DiagnosticLog.Write("SelfInstall: relaunched installed copy; shutting down portable instance.");

            // Shut down HARD (bypass the close-to-tray OnClosing intercept) so the
            // mutex frees and the child can take over — otherwise this Shutdown would
            // hide the portable instance to the tray and the child would time out on
            // the single-instance mutex.
            WarsOfLibertyLauncher.MainWindow.HardExitRequested = true;
            System.Windows.Application.Current?.Shutdown();
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"SelfInstall.RelaunchInstalledAndExit: {ex.Message}");
            return false;
        }
    }

    /// <summary>Start-Menu + Desktop shortcuts pointing at the installed exe. The
    /// exe is its own icon source (a Windows .lnk IconLocation renders .exe icons
    /// directly), so no separate .ico is needed. Reuses
    /// <see cref="NativeInstallService.CreateShortcutFile"/>.</summary>
    private static void CreateShortcuts()
    {
        const string appName = "AoE3 Mod Launcher";
        const string description = "Age of Empires III mod launcher";
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            NativeInstallService.CreateShortcutFile(
                Path.Combine(desktop, $"{appName}.lnk"),
                CanonicalExe, CanonicalDir, description, iconPath: CanonicalExe);

            var startMenu = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Programs));
            Directory.CreateDirectory(startMenu);
            NativeInstallService.CreateShortcutFile(
                Path.Combine(startMenu, $"{appName}.lnk"),
                CanonicalExe, CanonicalDir, description, iconPath: CanonicalExe);
            DiagnosticLog.Write("SelfInstall: created Desktop + Start-Menu shortcuts.");
        }
        catch (Exception ex)
        {
            // Shortcut failure shouldn't fail the install — the exe is already
            // copied and runnable from the canonical path.
            DiagnosticLog.Write($"SelfInstall.CreateShortcuts: {ex.Message}");
        }
    }
}
