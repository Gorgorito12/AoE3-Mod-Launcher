using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Result of an uninstall operation. Carries enough info for the UI to
/// show a clear success / partial-success / failure message.
/// </summary>
public record UninstallResult(
    bool Success,
    int FilesDeleted,
    int DirectoriesDeleted,
    int ShortcutsDeleted,
    bool RegistryRemoved,
    bool ConfigRemoved,
    List<string> Errors,
    string? Message);

/// <summary>
/// Optional cleanup steps the user can opt into in the uninstall dialog.
/// </summary>
public class UninstallOptions
{
    /// <summary>Always true. Mod files are the whole point of uninstalling.</summary>
    public bool DeleteModFiles { get; set; } = true;

    /// <summary>Remove desktop and Start Menu shortcuts created at install time.</summary>
    public bool DeleteShortcuts { get; set; } = true;

    /// <summary>Remove the Inno Setup-style entry from the Windows registry
    /// (Add/Remove Programs visibility).</summary>
    public bool RemoveRegistry { get; set; } = true;

    /// <summary>Reset the launcher's config back to defaults.</summary>
    public bool ResetConfig { get; set; } = false;
}

/// <summary>
/// Removes a mod installation produced by the launcher's native installer.
/// Works for any profile — the install folder is always dedicated to a
/// single mod (a clone of AoE3 + the mod overlay), separate from the user's
/// original AoE3 install.
///
/// Validity check: the folder must contain the profile's probe file
/// (<see cref="ModProfile.InstallProbeFile"/>). Without it we abort — we
/// won't delete a folder that isn't actually a recognised install.
/// </summary>
public class UninstallService
{
    /// <summary>
    /// Builds an <see cref="UninstallPlan"/> describing what we'll remove.
    /// Validates that the path looks like a real install for
    /// <paramref name="profile"/> by checking the probe file.
    /// </summary>
    public UninstallPlan Plan(ModProfile profile, string installPath)
    {
        if (string.IsNullOrEmpty(installPath) || !Directory.Exists(installPath))
            return new UninstallPlan(UninstallMode.NothingToDo, installPath ?? "", 0, 0);

        // Hard safety net: the stock Age of Empires III profile points at the
        // user's real, legally-owned base-game install (it's detect-only — we
        // never installed it). Uninstall is a blanket recursive delete of the
        // install folder, so proceeding here would wipe their AoE3. Refuse
        // outright. The UI also hides Uninstall for stock profiles; this guard
        // guarantees safety even if some surface slips a button through.
        if (profile.IsStockGame)
        {
            DiagnosticLog.Write(
                $"Uninstall refused: '{profile.DisplayName}' is the stock game (detect-only) — " +
                $"refusing to delete '{installPath}'.");
            return new UninstallPlan(UninstallMode.NotAValidInstall, installPath, 0, 0);
        }

        // Probe check: only delete folders that look like the mod we expect.
        // Rules out catastrophic accidents — user pointing at the AoE3 root,
        // an unrelated folder, or a drive root. An empty ProbeFile means the
        // profile didn't declare one, in which case we require a manifest
        // (read by callers) before allowing deletion. With neither, refuse.
        if (!string.IsNullOrEmpty(profile.InstallProbeFile))
        {
            var probe = Path.Combine(installPath, profile.InstallProbeFile);
            if (!File.Exists(probe))
                return new UninstallPlan(UninstallMode.NotAValidInstall, installPath, 0, 0);
        }
        else if (InstallManifest.TryLoad(installPath) == null)
        {
            return new UninstallPlan(UninstallMode.NotAValidInstall, installPath, 0, 0);
        }

        // Count files / dirs for the dialog summary
        int fileCount = 0, dirCount = 0;
        try
        {
            fileCount = Directory.EnumerateFiles(installPath, "*", SearchOption.AllDirectories).Count();
            dirCount = Directory.EnumerateDirectories(installPath, "*", SearchOption.AllDirectories).Count();
        }
        catch { /* counts are best-effort, not load-bearing */ }

        return new UninstallPlan(UninstallMode.Valid, installPath, fileCount, dirCount);
    }

    /// <summary>
    /// Performs the uninstall according to the plan + chosen options.
    /// Reports progress as a percentage (0–100) and current step text. The
    /// profile is used for registry / shortcut fallbacks when the on-disk
    /// install manifest is missing or doesn't carry the relevant fields.
    /// </summary>
    public async Task<UninstallResult> UninstallAsync(
        ModProfile profile,
        UninstallPlan plan,
        UninstallOptions options,
        IProgress<(double Percent, string Step)>? progress = null,
        CancellationToken ct = default)
    {
        DiagnosticLog.Write(
            $"=== Uninstall start ({profile.DisplayName}, mode={plan.Mode}, path='{plan.InstallPath}') ===");

        if (plan.Mode == UninstallMode.NotAValidInstall)
        {
            return new UninstallResult(
                Success: false,
                FilesDeleted: 0, DirectoriesDeleted: 0, ShortcutsDeleted: 0,
                RegistryRemoved: false, ConfigRemoved: false,
                Errors: new(),
                Message: $"Path does not look like a valid {profile.DisplayName} install.");
        }

        if (plan.Mode == UninstallMode.NothingToDo)
        {
            return new UninstallResult(true, 0, 0, 0, false, false, new(), "Nothing to do.");
        }

        return await Task.Run(() => DoUninstall(profile, plan, options, progress, ct), ct);
    }

    private UninstallResult DoUninstall(
        ModProfile profile,
        UninstallPlan plan,
        UninstallOptions options,
        IProgress<(double, string)>? progress,
        CancellationToken ct)
    {
        var errors = new List<string>();
        int filesDeleted = 0;
        int dirsDeleted = 0;
        int shortcutsDeleted = 0;
        bool registryRemoved = false;
        bool configRemoved = false;

        progress?.Report((0, "Starting uninstall..."));

        // ---- Phase 1: shortcuts (do this BEFORE deleting files so the
        //              manifest reference paths still resolve) ----
        if (options.DeleteShortcuts)
        {
            shortcutsDeleted = DeleteShortcuts(profile, plan.InstallPath, errors);
        }

        // ---- Phase 2: delete the install folder, recursively ----
        if (options.DeleteModFiles)
        {
            (filesDeleted, dirsDeleted) =
                DeleteInstallFolder(plan.InstallPath, plan.FileCount, progress, ct, errors);
        }

        // ---- Phase 3: registry ----
        if (options.RemoveRegistry)
        {
            registryRemoved = RemoveRegistryEntries(profile, plan.InstallPath, errors);
        }

        // ---- Phase 4: launcher config ----
        if (options.ResetConfig)
        {
            configRemoved = ResetLauncherConfig(errors);
        }

        progress?.Report((100, "Done."));
        DiagnosticLog.Write($"Uninstall complete: files={filesDeleted}, dirs={dirsDeleted}, " +
                            $"shortcuts={shortcutsDeleted}, registry={registryRemoved}, config={configRemoved}, " +
                            $"errors={errors.Count}");

        bool success = errors.Count == 0;
        string? message = success ? null : $"{errors.Count} item(s) could not be removed.";

        return new UninstallResult(
            success, filesDeleted, dirsDeleted, shortcutsDeleted,
            registryRemoved, configRemoved, errors, message);
    }

    // -------------------------------------------------------------------------
    // Folder delete (the simple path)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Deletes the install folder recursively. Streams progress as it goes
    /// so the dialog can show a percentage on multi-GB clones.
    /// </summary>
    private static (int FilesDeleted, int DirsDeleted) DeleteInstallFolder(
        string installPath,
        int expectedFileCount,
        IProgress<(double, string)>? progress,
        CancellationToken ct,
        List<string> errors)
    {
        int filesDeleted = 0;
        int dirsDeleted = 0;

        if (!Directory.Exists(installPath)) return (0, 0);

        try
        {
            // Pass 1: delete every file with progress reporting. Doing this
            // by hand (instead of Directory.Delete recursive=true in one
            // shot) lets us update the UI mid-delete and surface per-file
            // errors instead of failing the whole operation on one locked
            // file.
            var allFiles = Directory.EnumerateFiles(installPath, "*", SearchOption.AllDirectories).ToList();
            int total = expectedFileCount > 0 ? expectedFileCount : allFiles.Count;
            int done = 0;

            foreach (var f in allFiles)
            {
                ct.ThrowIfCancellationRequested();
                done++;

                try
                {
                    File.SetAttributes(f, FileAttributes.Normal);
                    File.Delete(f);
                    filesDeleted++;
                }
                catch (Exception ex)
                {
                    errors.Add($"{f}: {ex.Message}");
                }

                if (done % 200 == 0 || done == allFiles.Count)
                {
                    var pct = total > 0 ? 100.0 * done / total : 0;
                    progress?.Report((pct, $"Deleting files ({done}/{allFiles.Count})..."));
                }
            }

            // Pass 2: delete directories bottom-up. Sorting by descending
            // path length means leaves come first.
            var allDirs = Directory.EnumerateDirectories(installPath, "*", SearchOption.AllDirectories)
                                   .OrderByDescending(d => d.Length)
                                   .ToList();
            foreach (var d in allDirs)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (Directory.Exists(d))
                    {
                        Directory.Delete(d, recursive: false);
                        dirsDeleted++;
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"dir {d}: {ex.Message}");
                }
            }

            // Pass 3: delete the install root itself if it's now empty.
            if (Directory.Exists(installPath))
            {
                try
                {
                    Directory.Delete(installPath, recursive: false);
                    dirsDeleted++;
                }
                catch (Exception ex)
                {
                    errors.Add($"root {installPath}: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            errors.Add($"folder delete: {ex.Message}");
        }

        return (filesDeleted, dirsDeleted);
    }

    // -------------------------------------------------------------------------
    // Shortcuts / registry / config
    // -------------------------------------------------------------------------

    private static int DeleteShortcuts(ModProfile profile, string installPath, List<string> errors)
    {
        int count = 0;
        var paths = new List<string>();
        string? startMenuFolder = null;

        // Prefer the manifest if it's still there (more accurate paths).
        var manifest = InstallManifest.TryLoad(installPath);
        if (manifest != null)
        {
            paths.AddRange(manifest.Shortcuts);
            startMenuFolder = manifest.StartMenuFolder;
        }
        else
        {
            // No manifest — try the well-known default locations using the
            // active profile's display name as the .lnk basename.
            var appName = profile.DisplayName;
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            paths.Add(Path.Combine(desktop, $"{appName}.lnk"));
            startMenuFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Programs), appName);
            paths.Add(Path.Combine(startMenuFolder, $"{appName}.lnk"));
        }

        foreach (var p in paths)
        {
            try
            {
                if (File.Exists(p)) { File.Delete(p); count++; }
            }
            catch (Exception ex)
            {
                errors.Add($"shortcut {p}: {ex.Message}");
            }
        }

        // Remove the Start Menu folder if we created it and it's now empty
        if (!string.IsNullOrEmpty(startMenuFolder))
        {
            try
            {
                if (Directory.Exists(startMenuFolder) && IsDirectoryEmpty(startMenuFolder))
                    Directory.Delete(startMenuFolder);
            }
            catch { /* non-fatal */ }
        }

        return count;
    }

    private static bool RemoveRegistryEntries(
        ModProfile profile, string installPath, List<string> errors)
    {
        // Manifest is the source of truth for the registry subkey we wrote
        // at install time. If it's missing or doesn't carry one (e.g. old
        // builds before this field existed), derive from the active profile.
        var manifest = InstallManifest.TryLoad(installPath);
        var productGuid = !string.IsNullOrEmpty(manifest?.ProductGuid)
            ? manifest!.ProductGuid
            : profile.EffectiveProductGuid;

        bool removedAny = false;
        var keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + productGuid;
        var wow64KeyPath = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\" + productGuid;

        foreach (var (root, name) in new[]
        {
            (Registry.LocalMachine, "HKLM"),
            (Registry.CurrentUser, "HKCU"),
        })
        {
            foreach (var path in new[] { keyPath, wow64KeyPath })
            {
                try
                {
                    using var sub = root.OpenSubKey(path);
                    if (sub != null)
                    {
                        sub.Close();
                        root.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
                        removedAny = true;
                        DiagnosticLog.Write($"Removed registry: {name}\\{path}");
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    errors.Add($"registry {name}\\{path}: needs admin");
                }
                catch (Exception ex)
                {
                    errors.Add($"registry {name}\\{path}: {ex.Message}");
                }
            }
        }
        return removedAny;
    }

    private static bool ResetLauncherConfig(List<string> errors)
    {
        try
        {
            var path = AppPaths.ConfigFile;
            if (File.Exists(path)) File.Delete(path);
            return true;
        }
        catch (Exception ex)
        {
            errors.Add($"config: {ex.Message}");
            return false;
        }
    }

    private static bool IsDirectoryEmpty(string path)
    {
        try
        {
            return !Directory.EnumerateFileSystemEntries(path).Any();
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// How an uninstall can proceed (or not) given the current state.
/// </summary>
public enum UninstallMode
{
    /// <summary>Folder has the WoL marker; safe to delete the whole thing.</summary>
    Valid,
    /// <summary>Folder doesn't have the WoL marker — we refuse to delete it.</summary>
    NotAValidInstall,
    /// <summary>Path doesn't exist or no install was found.</summary>
    NothingToDo,
}

public record UninstallPlan(
    UninstallMode Mode,
    string InstallPath,
    int FileCount,
    int DirectoryCount);
