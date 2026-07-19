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

    /// <summary>
    /// A self-contained single-file exe is huge (~165 MB); the framework-dependent
    /// apphost stub is ~0.29 MB. Anything at/above this threshold is treated as the
    /// self-contained build (runnable on its own with no sibling DLLs). Comfortably
    /// between the two so neither is misclassified.
    /// </summary>
    private const long SelfContainedMinBytes = 50L * 1024 * 1024;

    /// <summary>
    /// Pure decision for "which exe should the auto-start Run key point at".
    /// Prefer the stable installed copy when it is present AND RUNNABLE, otherwise the
    /// running exe. Split out (no IO) so it's unit-testable.
    ///
    /// This stops a VOLATILE launch — a portable exe in Downloads, or a dev build in
    /// <c>publish\</c> / <c>bin\Debug\</c> — from clobbering a good Run-key
    /// registration with a path that may be GONE by the next Windows login (the
    /// confirmed first cause of "auto-start did nothing": the Run key pointed at a
    /// deleted <c>publish\</c> build). The <paramref name="canonicalRunnable"/> gate
    /// (not mere existence) is the SECOND fix: a canonical copy that exists but is a
    /// broken framework-dependent apphost with no DLLs beside it would launch nothing
    /// at login, so registering it is no better than the volatile path — fall back.
    /// </summary>
    internal static string? SelectAutoStartExe(bool canonicalRunnable, string? canonicalExe, string? runningExe)
        => canonicalRunnable && !string.IsNullOrWhiteSpace(canonicalExe) ? canonicalExe : runningExe;

    /// <summary>
    /// Pure: does a canonical copy look actually RUNNABLE (not just present)? True when
    /// the exe exists AND either a sibling <c>Aoe3ModLauncher.dll</c> is present (a
    /// complete framework-dependent install) OR the exe is large enough to be the
    /// self-contained single-file build. A ~0.29 MB apphost with no DLL is the broken
    /// case this rejects — copying only the stub is exactly what produced the silent
    /// "auto-start launches nothing" bug. Split out (no IO) for unit tests.
    /// </summary>
    internal static bool CanonicalRunnable(bool exeExists, bool siblingDllExists, long exeLength)
        => exeExists && (siblingDllExists || exeLength >= SelfContainedMinBytes);

    /// <summary>
    /// <see cref="CanonicalRunnable"/> against the real filesystem. Never throws.
    /// </summary>
    public static bool CanonicalLooksRunnable()
    {
        try
        {
            if (!File.Exists(CanonicalExe)) return false;
            var siblingDll = File.Exists(Path.Combine(CanonicalDir, "Aoe3ModLauncher.dll"));
            return CanonicalRunnable(true, siblingDll, new FileInfo(CanonicalExe).Length);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"SelfInstall.CanonicalLooksRunnable: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// The path the auto-start Run key should register (see
    /// <see cref="SelectAutoStartExe"/>). Prefers the canonical copy only when it's
    /// RUNNABLE (<see cref="CanonicalLooksRunnable"/>), else the running exe. Never
    /// throws. Passed as <c>exePathOverride</c> to
    /// <see cref="StartupRegistrationService.Apply"/> from the ctor self-heal and the
    /// Settings save. Does NOT install anything (that stays OPT-IN via
    /// <see cref="Install"/>).
    /// </summary>
    public static string? ResolveAutoStartExe()
        => SelectAutoStartExe(CanonicalLooksRunnable(), CanonicalExe, Environment.ProcessPath);

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
    /// Copy the running build into <see cref="CanonicalDir"/> and create Start-Menu
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
            if (IsInstalled())
                return (true, "Already installed.");

            var (ok, message) = CopyPayload(Environment.ProcessPath ?? "", CanonicalDir);
            if (!ok) return (ok, message);

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
    /// Copy the full runnable payload of the build that <paramref name="sourceExe"/>
    /// belongs to into <paramref name="destDir"/>. Split from <see cref="Install"/>
    /// (explicit source/dest, no shortcuts, no <see cref="Environment.ProcessPath"/>)
    /// so it's unit-testable against temp directories.
    ///
    /// TWO build shapes, and copying only the exe is correct for just ONE of them:
    ///   * Self-contained single-file (the shipped release): the exe IS the whole
    ///     payload, no sibling DLLs — a plain <c>File.Copy</c> of the exe.
    ///   * Framework-dependent (a dev <c>bin\Debug\</c> / <c>bin\Release\</c> build):
    ///     the exe is a ~0.29 MB apphost stub that needs <c>Aoe3ModLauncher.dll</c>
    ///     and the dependency DLLs / <c>*.runtimeconfig.json</c> / <c>*.deps.json</c>
    ///     BESIDE it. Copying only the stub produced a canonical install that launched
    ///     nothing at login (the confirmed second cause of "auto-start did nothing").
    ///     So copy the WHOLE build directory recursively.
    /// The two are told apart by whether a sibling managed <c>Aoe3ModLauncher.dll</c>
    /// sits next to the exe.
    /// </summary>
    internal static (bool ok, string message) CopyPayload(string sourceExe, string destDir)
    {
        if (string.IsNullOrWhiteSpace(sourceExe) || !File.Exists(sourceExe))
            return (false, "Could not resolve the running executable path.");

        var sourceDir = Path.GetDirectoryName(Path.GetFullPath(sourceExe));
        if (string.IsNullOrEmpty(sourceDir))
            return (false, "Could not resolve the source directory.");

        Directory.CreateDirectory(destDir);
        var destExe = Path.Combine(destDir, "Aoe3ModLauncher.exe");

        // Framework-dependent iff a managed DLL of the same name sits beside the exe.
        var frameworkDependent = File.Exists(Path.Combine(sourceDir, "Aoe3ModLauncher.dll"));
        if (frameworkDependent)
        {
            CopyDirectory(sourceDir, destDir);
            DiagnosticLog.Write(
                $"SelfInstall: copied framework-dependent build folder '{sourceDir}' -> '{destDir}'.");
        }
        else
        {
            // Self-contained single-file: the exe is the entire payload.
            File.Copy(sourceExe, destExe, overwrite: true);
            DiagnosticLog.Write($"SelfInstall: copied single-file exe '{sourceExe}' -> '{destExe}'.");
        }
        return (true, destExe);
    }

    /// <summary>Recursively copy every file + subdirectory of <paramref name="sourceDir"/>
    /// into <paramref name="destDir"/>, overwriting. Used for a framework-dependent build
    /// whose exe alone is not runnable.</summary>
    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
        foreach (var sub in Directory.EnumerateDirectories(sourceDir))
            CopyDirectory(sub, Path.Combine(destDir, Path.GetFileName(sub)));
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

    /// <summary>Base name (without extension) of the Desktop / Start-Menu shortcuts.
    /// Shared by <see cref="CreateShortcuts"/> and <see cref="RemoveShortcuts"/> so
    /// uninstall finds exactly what install created — don't let the two drift.</summary>
    private const string ShortcutName = "AoE3 Mod Launcher";

    /// <summary>Start-Menu + Desktop shortcuts pointing at the installed exe. The
    /// exe is its own icon source (a Windows .lnk IconLocation renders .exe icons
    /// directly), so no separate .ico is needed. Reuses
    /// <see cref="NativeInstallService.CreateShortcutFile"/>.</summary>
    private static void CreateShortcuts()
    {
        const string description = "Age of Empires III mod launcher";
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            NativeInstallService.CreateShortcutFile(
                Path.Combine(desktop, $"{ShortcutName}.lnk"),
                CanonicalExe, CanonicalDir, description, iconPath: CanonicalExe);

            var startMenu = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Programs));
            Directory.CreateDirectory(startMenu);
            NativeInstallService.CreateShortcutFile(
                Path.Combine(startMenu, $"{ShortcutName}.lnk"),
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

    /// <summary>Delete the Desktop + Start-Menu shortcuts <see cref="CreateShortcuts"/>
    /// wrote. Best-effort per file (a missing / locked .lnk is logged and skipped) —
    /// a stray shortcut is harmless, so it must never abort an uninstall.</summary>
    private static void RemoveShortcuts()
    {
        foreach (var folder in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                     Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                 })
        {
            try
            {
                var lnk = Path.Combine(folder, $"{ShortcutName}.lnk");
                if (File.Exists(lnk)) File.Delete(lnk);
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"SelfInstall.RemoveShortcuts: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Build the deferred-delete batch script that removes the install folder AFTER
    /// this process exits. The running exe lives inside <paramref name="canonicalDir"/>,
    /// so it cannot delete its own folder while running — a detached <c>cmd</c> waits
    /// for the PID to exit, then <c>rmdir</c>s. Pure (string only) so the exact
    /// commands are unit-testable.
    ///
    /// <paramref name="dataDir"/> (the per-user data folder) is deleted too ONLY when
    /// non-null — that's the "also delete my settings" choice. The delay uses
    /// <c>ping</c>, not <c>timeout</c> (which needs a console stdin and fails in a
    /// detached process). The script self-deletes with <c>del "%~f0"</c>.
    /// </summary>
    internal static string BuildDeferredDeleteScript(int pid, string canonicalDir, string? dataDir)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine(":wait");
        // While the launcher process still exists, wait ~1s and re-check.
        sb.AppendLine($"tasklist /FI \"PID eq {pid}\" 2>nul | find \"{pid}\" >nul");
        sb.AppendLine("if not errorlevel 1 (");
        sb.AppendLine("  ping -n 2 127.0.0.1 >nul");
        sb.AppendLine("  goto wait");
        sb.AppendLine(")");
        // Small extra settle so the handle is fully released before rmdir.
        sb.AppendLine("ping -n 2 127.0.0.1 >nul");
        sb.AppendLine($"rmdir /s /q \"{canonicalDir}\"");
        if (!string.IsNullOrWhiteSpace(dataDir))
            sb.AppendLine($"rmdir /s /q \"{dataDir}\"");
        sb.AppendLine("del /f /q \"%~f0\"");
        return sb.ToString();
    }

    /// <summary>
    /// Uninstall the canonical copy of the launcher and exit. In-process it removes
    /// the auto-start Run key (<see cref="StartupRegistrationService.Apply"/> false),
    /// the <c>wol-launcher://</c> deep-link scheme
    /// (<see cref="DeepLinkService.EnsureUnregistered"/>), and the Desktop / Start-Menu
    /// shortcuts (<see cref="RemoveShortcuts"/>) — none of which touch the running exe.
    /// The install FOLDER (which holds the running exe) can't be deleted while running,
    /// so a detached <c>cmd</c> (<see cref="BuildDeferredDeleteScript"/>) waits for this
    /// process to exit and then removes it. When <paramref name="removeUserData"/> is
    /// true the per-user data folder (<c>%LocalAppData%\AoE3ModLauncher</c>) is removed
    /// too; the open log handle is released on exit, so the script deletes it cleanly.
    ///
    /// NEVER touches installed mods (WoL / AoE3) — those are the user's own game
    /// folders, uninstalled separately via <see cref="UninstallService"/>.
    ///
    /// On success this hard-exits (bypassing the close-to-tray intercept) and returns
    /// true. If the deferred script can't be written/launched it returns false WITHOUT
    /// exiting, so the caller can report the failure — the in-process removals already
    /// done (Run key / shortcuts / scheme) are a harmless partial state.
    /// </summary>
    public static bool UninstallAndExit(bool removeUserData)
    {
        try
        {
            StartupRegistrationService.Apply(enabled: false);
            DeepLinkService.EnsureUnregistered();
            RemoveShortcuts();

            var pid = Environment.ProcessId;
            var dataDir = removeUserData ? AppPaths.DataDir : null;
            var script = BuildDeferredDeleteScript(pid, CanonicalDir, dataDir);
            var scriptPath = Path.Combine(Path.GetTempPath(), $"aoe3ml-uninstall-{Guid.NewGuid():N}.cmd");
            File.WriteAllText(scriptPath, script);

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{scriptPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            DiagnosticLog.Write($"SelfInstall: uninstall scheduled (removeUserData={removeUserData}); shutting down.");

            // Hard-exit so the close-to-tray OnClosing intercept doesn't hide us to
            // the tray (which would keep the exe locked and the rmdir would fail).
            WarsOfLibertyLauncher.MainWindow.HardExitRequested = true;
            System.Windows.Application.Current?.Shutdown();
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"SelfInstall.UninstallAndExit: {ex.Message}");
            return false;
        }
    }
}
