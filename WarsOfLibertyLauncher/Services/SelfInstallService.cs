using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using WarsOfLibertyLauncher.Models;

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
    /// between the two so neither is misclassified. Shared with
    /// <see cref="LauncherUpdateGate.IsDeveloperBuild"/>, which draws the same line for the
    /// opposite purpose — there a bundle is a RELEASE even with stale build files beside it.
    /// </summary>
    internal const long SelfContainedMinBytes = 50L * 1024 * 1024;

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
        foreach (var lnk in ShortcutPaths())
        {
            try
            {
                if (File.Exists(lnk)) File.Delete(lnk);
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"SelfInstall.RemoveShortcuts: {ex.Message}");
            }
        }
    }

    /// <summary>The two shortcuts <see cref="CreateShortcuts"/> writes, wherever they are.</summary>
    private static IEnumerable<string> ShortcutPaths()
    {
        foreach (var folder in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                     Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                 })
        {
            if (!string.IsNullOrWhiteSpace(folder)) yield return Path.Combine(folder, $"{ShortcutName}.lnk");
        }
    }

    // ---- removing the launcher from this PC -----------------------------------------
    //
    // What goes: the installed copy's folder, the install scratch folder in %TEMP%, the
    // self-update leftovers beside a portable exe, and — only when asked — the data folder.
    // What NEVER goes: an installed mod, the player's Age of Empires III, their saved games, or
    // the folder a portable exe was started from. A portable exe is not deleted either: the
    // launcher deletes only folders and files it made, and where somebody chose to keep a
    // download is theirs.

    /// <summary>What the uninstall deletes once the launcher has exited.</summary>
    /// <param name="Folders">Whole folders, each one passed by <see cref="IsSafeToDeleteTree"/>.</param>
    /// <param name="Files">Exact files: the self-update leftovers beside a portable exe.</param>
    /// <param name="RunningInstalledCopy">The running exe IS the installed copy.</param>
    /// <param name="InstalledCopyFolder">The installed copy's folder when it is among <paramref name="Folders"/>.</param>
    /// <param name="TempFolder">The install scratch folder in <c>%TEMP%</c> when it is among <paramref name="Folders"/>.</param>
    /// <param name="DataFolder">The data folder when it is among <paramref name="Folders"/>.</param>
    internal sealed record RemovalPlan(
        IReadOnlyList<string> Folders,
        IReadOnlyList<string> Files,
        bool RunningInstalledCopy,
        string? InstalledCopyFolder,
        string? TempFolder,
        string? DataFolder)
    {
        public bool RemovesInstalledCopy => InstalledCopyFolder != null;
        public bool RemovesTempFiles => TempFolder != null;
        public bool RemovesUserData => DataFolder != null;
    }

    /// <summary>
    /// Pure: what to delete. Each folder must exist, pass <see cref="IsSafeToDeleteTree"/>, carry
    /// the name the launcher gives it, and NOT hold the running exe — except the installed copy's
    /// folder when that IS what is running, which is the point of uninstalling it. That exception
    /// is narrow on purpose: a portable exe that happens to sit inside one of these folders keeps
    /// that folder, because "never delete the file the player started" outranks tidiness.
    /// </summary>
    internal static RemovalPlan PlanRemoval(
        string runningExe,
        string canonicalDir,
        string installTempRoot,
        string dataDir,
        bool removeUserData,
        IReadOnlyCollection<string> protectedFolders,
        Func<string, bool> directoryExists,
        Func<string, bool> fileExists)
    {
        var running = TryNormalize(runningExe);
        var canonicalExe = TryNormalize(Path.Combine(canonicalDir ?? "", "Aoe3ModLauncher.exe"));
        var installed = running != null && canonicalExe != null
                        && string.Equals(running, canonicalExe, StringComparison.OrdinalIgnoreCase);

        var folders = new List<string>();

        string? Consider(string? folder, Func<string, bool> isOurs, bool mayHoldRunningExe)
        {
            var full = TryNormalize(folder);
            if (full == null || !directoryExists(full)) return null;
            if (!IsSafeToDeleteTree(full, protectedFolders) || !isOurs(full)) return null;
            if (running != null && IsInside(running, full) && !mayHoldRunningExe) return null;
            if (folders.Any(f => string.Equals(f, full, StringComparison.OrdinalIgnoreCase) || IsInside(full, f)))
                return null;
            folders.RemoveAll(f => IsInside(f, full));
            folders.Add(full);
            return full;
        }

        var canonical = Consider(canonicalDir, f => LeafIs(f, "Aoe3ModLauncher"), mayHoldRunningExe: installed);
        var temp = Consider(installTempRoot, f => LeafIs(f, "WarsOfLibertyLauncher"), mayHoldRunningExe: false);
        var data = removeUserData
            ? Consider(dataDir,
                f => LeafIs(f, "AoE3ModLauncher") || fileExists(Path.Combine(f, AppPaths.ConfigFileName)),
                mayHoldRunningExe: false)
            : null;

        var files = new List<string>();
        if (!installed && running != null)
        {
            var leftovers = new List<string>(LauncherUpdateService.SelectStaleSelfUpdateFiles(running))
            {
                // The CURRENT partial download too: the startup sweep keeps it so an update can
                // resume, and with the launcher gone nothing will ever resume it.
                LauncherUpdateService.GetPendingUpdatePath(running) + ".part",
            };
            foreach (var leftover in leftovers)
            {
                var full = TryNormalize(leftover);
                if (full == null || !fileExists(full)) continue;
                if (string.Equals(full, running, StringComparison.OrdinalIgnoreCase)) continue;
                if (folders.Any(f => IsInside(full, f))) continue;
                if (!files.Contains(full, StringComparer.OrdinalIgnoreCase)) files.Add(full);
            }
        }

        string? Kept(string? folder) => folder != null && folders.Contains(folder) ? folder : null;
        return new RemovalPlan(folders, files, installed, Kept(canonical), Kept(temp), Kept(data));
    }

    /// <summary>
    /// Pure: whether a whole folder may be handed to <c>rmdir /s</c>. Never a drive root, never a
    /// relative path, and never a folder that is — or CONTAINS — one of
    /// <paramref name="protectedFolders"/> (the profile, Documents, Program Files, Windows…). The
    /// folder-name checks in <see cref="PlanRemoval"/> already make a wrong target unlikely; this
    /// is what makes it impossible.
    /// </summary>
    internal static bool IsSafeToDeleteTree(string? folder, IEnumerable<string> protectedFolders)
    {
        if (string.IsNullOrWhiteSpace(folder)) return false;
        var full = TryNormalize(folder);
        if (full == null) return false;
        var root = TryNormalize(Path.GetPathRoot(full));
        if (root == null || string.Equals(full, root, StringComparison.OrdinalIgnoreCase)) return false;

        foreach (var candidate in protectedFolders)
        {
            var guarded = TryNormalize(candidate);
            if (guarded == null) continue;
            if (string.Equals(full, guarded, StringComparison.OrdinalIgnoreCase) || IsInside(guarded, full))
                return false;
        }
        return true;
    }

    /// <summary>The folders no uninstall may ever delete or contain. Never throws.</summary>
    internal static IReadOnlyList<string> ProtectedFolders()
    {
        var folders = new List<string>();
        foreach (var special in new[]
                 {
                     Environment.SpecialFolder.UserProfile,
                     Environment.SpecialFolder.Desktop,
                     Environment.SpecialFolder.DesktopDirectory,
                     Environment.SpecialFolder.MyDocuments,
                     Environment.SpecialFolder.MyMusic,
                     Environment.SpecialFolder.MyPictures,
                     Environment.SpecialFolder.MyVideos,
                     Environment.SpecialFolder.LocalApplicationData,
                     Environment.SpecialFolder.ApplicationData,
                     Environment.SpecialFolder.CommonApplicationData,
                     Environment.SpecialFolder.Programs,
                     Environment.SpecialFolder.StartMenu,
                     Environment.SpecialFolder.Startup,
                     Environment.SpecialFolder.CommonDesktopDirectory,
                     Environment.SpecialFolder.CommonDocuments,
                     Environment.SpecialFolder.CommonPrograms,
                     Environment.SpecialFolder.CommonStartMenu,
                     Environment.SpecialFolder.ProgramFiles,
                     Environment.SpecialFolder.ProgramFilesX86,
                     Environment.SpecialFolder.Windows,
                     Environment.SpecialFolder.System,
                     Environment.SpecialFolder.SystemX86,
                 })
        {
            try
            {
                var path = Environment.GetFolderPath(special);
                if (!string.IsNullOrWhiteSpace(path)) folders.Add(path);
            }
            catch { /* an unknown folder protects nothing */ }
        }
        try
        {
            folders.Add(Path.GetTempPath());
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(profile)) folders.Add(Path.Combine(profile, "Downloads"));
        }
        catch { }
        return folders;
    }

    /// <summary>A full path without a trailing separator, or null when it is not one.</summary>
    private static string? TryNormalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            if (!Path.IsPathFullyQualified(path)) return null;
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Whether <paramref name="path"/> lies strictly inside <paramref name="folder"/>.
    /// Both already normalised.</summary>
    private static bool IsInside(string path, string folder)
        => path.Length > folder.Length + 1
           && path.StartsWith(Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar,
               StringComparison.OrdinalIgnoreCase);

    private static bool LeafIs(string folder, string name)
        => string.Equals(Path.GetFileName(folder), name, StringComparison.OrdinalIgnoreCase);

    /// <summary>The script and the environment it needs. Paths live ONLY in <see cref="Variables"/>.</summary>
    internal sealed record DeferredDelete(string Script, IReadOnlyDictionary<string, string> Variables);

    /// <summary>How many one-second ticks the script waits for the launcher before giving up.</summary>
    internal const int MaxWaitTicks = 300;

    /// <summary>
    /// The batch script that deletes the plan AFTER this process has exited — it cannot delete the
    /// folder its own exe runs from while running. Pure, so every property below is a test.
    ///
    /// <para><b>The script names no path; every one arrives in an environment variable.</b> cmd
    /// reads a script in the console's code page, so a path written into it was mangled for any
    /// player whose user folder is not plain ASCII (<c>C:\Users\José</c>) — and that build deleted
    /// nothing at all, in silence. In a variable the path stays Unicode, and a <c>%</c>, <c>&amp;</c>
    /// or <c>!</c> in it stays a character instead of becoming syntax.</para>
    ///
    /// <para><b>It waits for a LOCK FILE, not for a process id.</b> The launcher holds
    /// <paramref name="lockPath"/> open until the process ends, when Windows closes it and deletes
    /// it. A process id can be reused within a second, and asking <c>tasklist</c> fails in ways
    /// that read as "gone" (a broken query prints nothing, and nothing piped into <c>find</c> is a
    /// "not found") — which deleted folders out from under a launcher still running. A lock that
    /// exists while we live cannot be misread either way.</para>
    ///
    /// <para><b>Every external program is named by its absolute path in System32</b>, and the
    /// script is started from there: cmd looks for a bare <c>ping</c> in the current folder first,
    /// and a portable exe usually lives in Downloads, where anything can be waiting under that
    /// name. A variable that did not arrive ends the script before anything is deleted, rather
    /// than turning <c>rmdir "%X%"</c> into <c>rmdir ""</c>. After <see cref="MaxWaitTicks"/>
    /// seconds without the lock released it gives up and deletes nothing.</para>
    /// </summary>
    internal static DeferredDelete BuildDeferredDelete(
        string lockPath, IReadOnlyList<string> folders, IReadOnlyList<string> files, string systemDirectory)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AOE3ML_SYS"] = Path.TrimEndingDirectorySeparator(systemDirectory),
            ["AOE3ML_LOCK"] = lockPath,
        };
        for (var i = 0; i < folders.Count; i++) variables[$"AOE3ML_DEL_{i + 1}"] = folders[i];
        for (var i = 0; i < files.Count; i++) variables[$"AOE3ML_FILE_{i + 1}"] = files[i];

        var sb = new StringBuilder();
        void Line(string text) => sb.Append(text).Append("\r\n");

        Line("@echo off");
        foreach (var name in variables.Keys) Line($"if not defined {name} goto done");
        Line("set /a AOE3ML_N=0");
        Line(":wait");
        Line("if not exist \"%AOE3ML_LOCK%\" goto gone");
        Line("set /a AOE3ML_N+=1");
        Line($"if %AOE3ML_N% geq {MaxWaitTicks} goto done");
        Line("\"%AOE3ML_SYS%\\ping.exe\" -n 2 127.0.0.1 >nul");
        Line("goto wait");
        Line(":gone");
        // Twice, a few seconds apart: an antivirus scanning the files the exit just released can
        // hold one for a moment, and a second pass is cheaper than a folder left half deleted.
        for (var pass = 0; pass < 2; pass++)
        {
            Line("\"%AOE3ML_SYS%\\ping.exe\" -n 3 127.0.0.1 >nul");
            for (var i = 1; i <= folders.Count; i++) Line($"rmdir /s /q \"%AOE3ML_DEL_{i}%\" >nul 2>&1");
            for (var i = 1; i <= files.Count; i++) Line($"del /f /q \"%AOE3ML_FILE_{i}%\" >nul 2>&1");
        }
        Line(":done");
        Line("del /f /q \"%~f0\" >nul 2>&1");
        return new DeferredDelete(sb.ToString(), variables);
    }

    /// <summary>
    /// What a kept configuration forgets when the launcher is removed: auto-start (or the next copy
    /// that runs — another portable exe — would register itself again) and the Discord session,
    /// whose token sits in that file in plain text. Everything else is the player's and stays.
    /// </summary>
    internal static void ForgetOnUninstall(LauncherConfig config)
    {
        config.StartWithWindows = false;
        config.MinimizeToTray = false;
        config.StartMinimized = false;
        config.Multiplayer.SessionToken = "";
        config.Multiplayer.SessionExpiresAt = 0;
        config.Multiplayer.CachedUser = null;
    }

    /// <summary>
    /// Held open until the process ends: <see cref="BuildDeferredDelete"/>'s script waits for
    /// Windows to close and delete it. A STATIC on purpose — a stream only a local referenced
    /// could be finalized while the launcher still runs, and the script would start deleting.
    /// </summary>
    private static FileStream? s_exitLock;

    /// <summary>
    /// Remove the launcher from this PC and exit. Nothing is removed until the delete script is
    /// running, so a failure to start it leaves everything as it was; then Windows forgets the
    /// launcher (auto-start, Task Manager's record, <c>wol-launcher://</c>, the shortcuts), a kept
    /// configuration forgets what it must (<see cref="ForgetOnUninstall"/>), and the launcher
    /// exits through <paramref name="exitLauncher"/>. If that does not close it, the script is
    /// stopped before it can delete anything and false comes back.
    ///
    /// <para>NEVER touches an installed mod or the player's Age of Empires III: mods are
    /// uninstalled from their own menu, and the base game is the player's own copy. The caller
    /// restores the game folders a redirect may have moved and removes leftover mod keys BEFORE
    /// this (<see cref="RestoreGameFolders"/>, <see cref="PrivateKeySweep"/>).</para>
    /// </summary>
    /// <param name="exitLauncher">Closes the launcher and says whether it really closed. Null shuts
    /// the application down directly.</param>
    public static bool UninstallAndExit(LauncherConfig config, bool removeUserData, Func<bool>? exitLauncher)
    {
        Process? script = null;
        string? scriptPath = null;
        try
        {
            var plan = PlanRemoval(
                Environment.ProcessPath ?? "", CanonicalDir, AppPaths.InstallTempRoot, AppPaths.DataDir,
                removeUserData, ProtectedFolders(), Directory.Exists, File.Exists);

            if (plan.Folders.Count > 0 || plan.Files.Count > 0)
            {
                var stem = Path.Combine(Path.GetTempPath(), $"aoe3ml-uninstall-{Guid.NewGuid():N}");
                var lockPath = stem + ".lock";
                scriptPath = stem + ".cmd";
                s_exitLock = new FileStream(lockPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                    bufferSize: 1, FileOptions.DeleteOnClose);

                script = StartDeferredDelete(
                    BuildDeferredDelete(lockPath, plan.Folders, plan.Files, Environment.SystemDirectory),
                    scriptPath);
            }

            StartupRegistrationService.Apply(enabled: false);
            StartupRegistrationService.ForgetStartupApproval();
            DeepLinkService.EnsureUnregistered();
            RemoveShortcuts();
            if (!removeUserData)
            {
                ForgetOnUninstall(config);
                config.Save();
            }

            DiagnosticLog.Write(
                $"SelfInstall: removing the launcher — {plan.Folders.Count} folder(s), {plan.Files.Count} file(s) " +
                $"after exit (installed copy: {plan.RemovesInstalledCopy}, data: {plan.RemovesUserData}).");

            var closed = exitLauncher?.Invoke() ?? ShutDown();
            if (closed) return true;

            DiagnosticLog.Write("SelfInstall: the launcher did not close — nothing will be deleted.");
            CancelDeferredDelete(script, scriptPath);
            return false;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"SelfInstall.UninstallAndExit: {ex.Message}");
            CancelDeferredDelete(script, scriptPath);
            return false;
        }
    }

    /// <summary>
    /// Writes <paramref name="delete"/>'s script to <paramref name="scriptPath"/> and starts it,
    /// hidden. Separate from <see cref="UninstallAndExit"/> so a test can run the real mechanism
    /// against a folder of its own.
    /// </summary>
    internal static Process StartDeferredDelete(DeferredDelete delete, string scriptPath)
    {
        File.WriteAllText(scriptPath, delete.Script, Encoding.ASCII);
        var system = Environment.SystemDirectory;
        var psi = new ProcessStartInfo
        {
            FileName = Path.Combine(system, "cmd.exe"),
            // /d: no AutoRun commands. /v:off: '!' stays a character. /s with the doubled quotes:
            // cmd strips exactly the outer pair, whatever the path holds — and the path itself is
            // expanded from the environment, never written on this line.
            Arguments = "/d /v:off /s /c \"\"%AOE3ML_SCRIPT%\"\"",
            WorkingDirectory = system,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        foreach (var (name, value) in delete.Variables) psi.Environment[name] = value;
        psi.Environment["AOE3ML_SCRIPT"] = scriptPath;
        return Process.Start(psi) ?? throw new IOException("cmd.exe did not start.");
    }

    /// <summary>Stops the delete script before it can act, and drops its lock and its file.</summary>
    private static void CancelDeferredDelete(Process? script, string? scriptPath)
    {
        try { if (script is { HasExited: false }) script.Kill(entireProcessTree: true); }
        catch (Exception ex) { DiagnosticLog.Write($"SelfInstall: could not stop the delete script: {ex.Message}"); }
        try { s_exitLock?.Dispose(); } catch { }
        s_exitLock = null;
        try { if (scriptPath != null && File.Exists(scriptPath)) File.Delete(scriptPath); } catch { }
    }

    private static bool ShutDown()
    {
        // Past the close-to-tray intercept, which would otherwise keep the exe running and locked.
        WarsOfLibertyLauncher.MainWindow.HardExitRequested = true;
        System.Windows.Application.Current?.Shutdown();
        return true;
    }

    /// <summary>
    /// Why removing the launcher NOW would be wrong, as the line Settings shows under the button —
    /// or null. Pure, so the order is pinned: a running game first (the commonest, and the one that
    /// holds files open), then a match whose result is still owed, then an install or update, then
    /// a launcher running as ANOTHER Windows account — whose data folder, registry and shortcuts
    /// are that account's, so "uninstall" would remove somebody else's launcher and leave this
    /// player's in place.
    /// </summary>
    internal static string? UninstallBlockerFor(
        bool gameRunning, bool matchActive, bool operationRunning, RunningAccount.AccountInfo account)
    {
        if (gameRunning) return Localization.Strings.Get("DlgSelfUninstallBlockedGame");
        if (matchActive) return Localization.Strings.Get("DlgSelfUninstallBlockedMatch");
        if (operationRunning) return Localization.Strings.Get("DlgSelfUninstallBlockedBusy");
        if (account.Mismatch)
            return Localization.Strings.Format("DlgSelfUninstallBlockedAccount", account.ProcessUser, account.SessionUser);
        return null;
    }

    /// <summary>
    /// Puts back the player's own <c>My Games\Age of Empires 3</c> and <c>bin\</c> if a mod's
    /// launch redirect is still in place, and says whether both are back. Run BEFORE anything is
    /// removed: the self-heal that restores them lives in this launcher's startup, so once it is
    /// gone a redirect left behind would keep the real folders renamed "(AoE3 vanilla)" for good.
    /// </summary>
    public static bool RestoreGameFolders()
    {
        AoE3UserDataRedirect.EnsureDefault();
        AoE3SetupPathRedirect.EnsureDefault();
        var savesStillMoved = AoE3UserDataRedirect.HasOwnedRedirect();
        var gameStillMoved = AoE3SetupPathRedirect.HasOwnedRedirect();
        if (savesStillMoved || gameStillMoved)
            DiagnosticLog.Write(
                $"SelfInstall: a redirect is still in place (saves: {savesStillMoved}, game: {gameStillMoved}) — not uninstalling.");
        return !savesStillMoved && !gameStillMoved;
    }

    /// <summary>What the uninstall window says, gathered once before it opens. Every line it shows
    /// is a fact about this PC: nothing is listed as removed that is not there.</summary>
    /// <param name="RunningInstalledCopy">The running exe is the installed copy.</param>
    /// <param name="RunningExe">The running exe, for the "this file stays" line of a portable one.</param>
    /// <param name="InstalledCopyDir">The installed copy's folder when it will be deleted, else null.</param>
    /// <param name="ShortcutsPresent">A Desktop or Start-menu shortcut of ours exists.</param>
    /// <param name="RemovesTempFiles">The install scratch folder exists and goes.</param>
    /// <param name="LeftoverFiles">Self-update leftovers beside a portable exe that go.</param>
    /// <param name="DataDir">The data folder.</param>
    /// <param name="CanRemoveUserData">The data folder can be deleted; otherwise the option is hidden.</param>
    /// <param name="InstalledMods">The mods still installed — named, because they are what stays.</param>
    /// <param name="LeftoverKeys">How many leftover mod keys go (<see cref="PrivateKeySweep"/>).</param>
    /// <param name="PendingUploads">Competitive recordings still waiting to upload.</param>
    /// <param name="HasRepairBackups">Repair kept backups of replaced game files.</param>
    public sealed record UninstallFacts(
        bool RunningInstalledCopy,
        string RunningExe,
        string? InstalledCopyDir,
        bool ShortcutsPresent,
        bool RemovesTempFiles,
        int LeftoverFiles,
        string DataDir,
        bool CanRemoveUserData,
        IReadOnlyList<string> InstalledMods,
        int LeftoverKeys,
        int PendingUploads,
        bool HasRepairBackups);

    /// <summary>Reads the disk; run it off the UI thread.</summary>
    public static UninstallFacts GatherUninstallFacts(LauncherConfig config, int leftoverKeys)
    {
        var running = Environment.ProcessPath ?? "";
        var plan = PlanRemoval(running, CanonicalDir, AppPaths.InstallTempRoot, AppPaths.DataDir,
            removeUserData: true, ProtectedFolders(), Directory.Exists, File.Exists);

        var pending = 0;
        try { pending = Multiplayer.ReplayUploadQueue.Default.Snapshot().Count; }
        catch (Exception ex) { DiagnosticLog.Write($"SelfInstall: could not count waiting uploads: {ex.Message}"); }

        var shortcuts = false;
        try { shortcuts = ShortcutPaths().Any(File.Exists); }
        catch { }

        return new UninstallFacts(
            plan.RunningInstalledCopy,
            running,
            plan.InstalledCopyFolder,
            shortcuts,
            plan.RemovesTempFiles,
            plan.Files.Count,
            AppPaths.DataDir,
            plan.RemovesUserData,
            InstalledModNames(config, ModRegistry.Find, Directory.Exists),
            leftoverKeys,
            pending,
            HasRepairBackupsIn(AppPaths.DataDir));
    }

    /// <summary>
    /// Pure: the mods with an install still on disk, by the name the player knows. The base game is
    /// not one of them, and a mod that left the catalogue is named by its folder rather than by an id
    /// nobody would recognise.
    /// </summary>
    internal static IReadOnlyList<string> InstalledModNames(
        LauncherConfig config, Func<string, ModProfile?> find, Func<string, bool> directoryExists)
    {
        var names = new List<string>();
        foreach (var (id, state) in config.Mods)
        {
            if (state == null) continue;
            var profile = find(id);
            if (profile?.IsStockGame == true) continue;
            var present = state.AllInstallPaths().FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && directoryExists(p));
            if (present == null) continue;
            var name = profile?.DisplayName;
            if (string.IsNullOrWhiteSpace(name))
                name = Path.GetFileName(Path.TrimEndingDirectorySeparator(present));
            if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name, StringComparer.OrdinalIgnoreCase))
                names.Add(name);
        }
        names.Sort(StringComparer.CurrentCultureIgnoreCase);
        return names;
    }

    private static bool HasRepairBackupsIn(string dataDir)
    {
        foreach (var name in new[] { "engine-backup", "leftover-backup", "granular-backup" })
        {
            try
            {
                var folder = Path.Combine(dataDir, name);
                if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any()) return true;
            }
            catch { }
        }
        return false;
    }
}
