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
    List<string> Skipped,
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
/// Removes a Wars of Liberty installation safely.
///
/// Strategy (in priority order):
///   1. Manifest-driven: if wol-manifest.json exists, delete only the files
///      and directories listed there. This is the only fully safe mode when
///      WoL was installed merged into an AoE3 root.
///   2. Subfolder fallback: if no manifest but the install path is clearly
///      a "Wars of Liberty" subfolder (does NOT contain age3y.exe at root or
///      in bin\), delete the whole folder.
///   3. Refuse: if the install path looks like an AoE3 root (has age3y.exe),
///      we cannot tell mod files from base game files. Don't delete anything.
/// </summary>
public class UninstallService
{
    private const string ProductGuid = "{EB448764-CABB-4766-8055-495AEA292020}_is1";
    private const string AppName = "Wars of Liberty";

    /// <summary>
    /// File names that we refuse to delete even if listed in the manifest —
    /// they are part of the AoE3 base game.
    /// </summary>
    private static readonly HashSet<string> ProtectedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "age3y.exe", "age3.exe", "age3x.exe",
        "proto.xml", "techtree.xml", "stringtable.xml",
    };

    /// <summary>
    /// Tells the UI whether we can fully uninstall, only partially, or not at all.
    /// </summary>
    public UninstallPlan Plan(string installPath)
    {
        if (string.IsNullOrEmpty(installPath) || !Directory.Exists(installPath))
            return new UninstallPlan(UninstallMode.NothingToDo, null, 0, 0);

        var manifest = InstallManifest.TryLoad(installPath);
        if (manifest != null)
        {
            return new UninstallPlan(UninstallMode.Manifest, manifest,
                manifest.Files.Count, manifest.Directories.Count)
            { InstallPath = manifest.InstallPath };
        }

        // No manifest. Decide between subfolder-delete vs refuse.
        if (LooksLikeStandaloneSubfolder(installPath))
        {
            // Roughly count items for the dialog
            int fileCount = 0, dirCount = 0;
            try
            {
                fileCount = Directory.EnumerateFiles(installPath, "*", SearchOption.AllDirectories).Count();
                dirCount = Directory.EnumerateDirectories(installPath, "*", SearchOption.AllDirectories).Count();
            }
            catch { }
            return new UninstallPlan(UninstallMode.SubfolderFallback, null, fileCount, dirCount)
            { InstallPath = installPath };
        }

        return new UninstallPlan(UninstallMode.RefusedMergedWithAoe3, null, 0, 0)
        { InstallPath = installPath };
    }

    /// <summary>
    /// Performs the uninstall according to the plan + chosen options.
    /// Reports progress as a percentage (0–100) and current step text.
    /// </summary>
    public async Task<UninstallResult> UninstallAsync(
        UninstallPlan plan,
        UninstallOptions options,
        IProgress<(double Percent, string Step)>? progress = null,
        CancellationToken ct = default)
    {
        DiagnosticLog.Write($"=== Uninstall start (mode={plan.Mode}) ===");

        if (plan.Mode == UninstallMode.RefusedMergedWithAoe3)
        {
            return new UninstallResult(
                Success: false,
                FilesDeleted: 0, DirectoriesDeleted: 0, ShortcutsDeleted: 0,
                RegistryRemoved: false, ConfigRemoved: false,
                Skipped: new(), Errors: new(),
                Message: "Refused: install path looks like an AoE3 root.");
        }

        if (plan.Mode == UninstallMode.NothingToDo)
        {
            return new UninstallResult(true, 0, 0, 0, false, false, new(), new(), "Nothing to do.");
        }

        return await Task.Run(() => DoUninstall(plan, options, progress, ct), ct);
    }

    private UninstallResult DoUninstall(
        UninstallPlan plan,
        UninstallOptions options,
        IProgress<(double, string)>? progress,
        CancellationToken ct)
    {
        var skipped = new List<string>();
        var errors = new List<string>();
        int filesDeleted = 0;
        int dirsDeleted = 0;
        int shortcutsDeleted = 0;
        bool registryRemoved = false;
        bool configRemoved = false;

        progress?.Report((0, "Starting uninstall..."));

        // ---- Phase 1: shortcuts (do this BEFORE deleting files so paths still resolve) ----
        if (options.DeleteShortcuts)
        {
            shortcutsDeleted = DeleteShortcuts(plan.Manifest, errors);
        }

        // ---- Phase 2: mod files ----
        if (options.DeleteModFiles)
        {
            if (plan.Mode == UninstallMode.Manifest && plan.Manifest != null)
            {
                (filesDeleted, dirsDeleted) =
                    DeleteFromManifest(plan.Manifest, progress, ct, skipped, errors);
            }
            else if (plan.Mode == UninstallMode.SubfolderFallback)
            {
                (filesDeleted, dirsDeleted) =
                    DeleteSubfolder(plan.InstallPath, progress, ct, errors);
            }
        }

        // ---- Phase 3: registry ----
        if (options.RemoveRegistry)
        {
            registryRemoved = RemoveRegistryEntries(errors);
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
            registryRemoved, configRemoved, skipped, errors, message);
    }

    // -------------------------------------------------------------------------
    // Manifest-driven delete (the safe path)
    // -------------------------------------------------------------------------

    private (int FilesDeleted, int DirsDeleted) DeleteFromManifest(
        InstallManifest manifest,
        IProgress<(double, string)>? progress,
        CancellationToken ct,
        List<string> skipped,
        List<string> errors)
    {
        int filesDeleted = 0;
        int dirsDeleted = 0;
        int totalSteps = manifest.Files.Count + manifest.Directories.Count + 1; // +1 for manifest itself
        int currentStep = 0;

        var installRoot = Path.GetFullPath(manifest.InstallPath).TrimEnd('\\', '/');

        // Files first
        foreach (var rel in manifest.Files)
        {
            ct.ThrowIfCancellationRequested();
            currentStep++;

            var name = Path.GetFileName(rel);
            if (ProtectedFileNames.Contains(name))
            {
                skipped.Add(rel);
                DiagnosticLog.Write($"  [skip protected] {rel}");
                continue;
            }

            var abs = Path.Combine(installRoot, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!IsInsideRoot(abs, installRoot))
            {
                skipped.Add(rel);
                continue;
            }

            try
            {
                if (File.Exists(abs))
                {
                    File.SetAttributes(abs, FileAttributes.Normal); // in case it's read-only
                    File.Delete(abs);
                    filesDeleted++;
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{rel}: {ex.Message}");
            }

            if (currentStep % 50 == 0 || currentStep == totalSteps)
            {
                progress?.Report((100.0 * currentStep / totalSteps,
                    $"Deleting files ({filesDeleted}/{manifest.Files.Count})..."));
            }
        }

        // Directories — reverse order so we delete leaves first
        foreach (var rel in manifest.Directories.AsEnumerable().Reverse())
        {
            ct.ThrowIfCancellationRequested();
            currentStep++;

            var abs = Path.Combine(installRoot, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!IsInsideRoot(abs, installRoot)) continue;

            try
            {
                if (Directory.Exists(abs) && IsDirectoryEmpty(abs))
                {
                    Directory.Delete(abs);
                    dirsDeleted++;
                }
            }
            catch (Exception ex)
            {
                errors.Add($"dir {rel}: {ex.Message}");
            }
        }

        // Manifest file itself
        try
        {
            var manifestPath = InstallManifest.GetManifestPath(installRoot);
            if (File.Exists(manifestPath)) File.Delete(manifestPath);
        }
        catch { /* non-fatal */ }

        // Install root itself, if empty after everything
        try
        {
            if (Directory.Exists(installRoot) && IsDirectoryEmpty(installRoot))
            {
                Directory.Delete(installRoot);
                dirsDeleted++;
            }
        }
        catch { /* non-fatal */ }

        return (filesDeleted, dirsDeleted);
    }

    // -------------------------------------------------------------------------
    // Subfolder-fallback delete (no manifest, but path is clearly WoL-only)
    // -------------------------------------------------------------------------

    private (int FilesDeleted, int DirsDeleted) DeleteSubfolder(
        string installPath,
        IProgress<(double, string)>? progress,
        CancellationToken ct,
        List<string> errors)
    {
        int filesDeleted = 0;
        int dirsDeleted = 0;

        try
        {
            var allFiles = Directory.EnumerateFiles(installPath, "*", SearchOption.AllDirectories).ToList();
            int total = allFiles.Count;
            int done = 0;

            foreach (var f in allFiles)
            {
                ct.ThrowIfCancellationRequested();
                done++;

                if (ProtectedFileNames.Contains(Path.GetFileName(f)))
                {
                    // Shouldn't happen in a true WoL subfolder, but be defensive.
                    continue;
                }
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

                if (done % 100 == 0 || done == total)
                    progress?.Report((100.0 * done / total, $"Deleting files ({done}/{total})..."));
            }

            // Now remove directories bottom-up
            var allDirs = Directory.EnumerateDirectories(installPath, "*", SearchOption.AllDirectories)
                                   .OrderByDescending(d => d.Length)
                                   .ToList();
            foreach (var d in allDirs)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (Directory.Exists(d) && IsDirectoryEmpty(d))
                    {
                        Directory.Delete(d);
                        dirsDeleted++;
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"dir {d}: {ex.Message}");
                }
            }

            // Finally the install root itself
            if (Directory.Exists(installPath) && IsDirectoryEmpty(installPath))
            {
                Directory.Delete(installPath);
                dirsDeleted++;
            }
        }
        catch (Exception ex)
        {
            errors.Add($"subfolder delete: {ex.Message}");
        }

        return (filesDeleted, dirsDeleted);
    }

    // -------------------------------------------------------------------------
    // Shortcuts / registry / config
    // -------------------------------------------------------------------------

    private static int DeleteShortcuts(InstallManifest? manifest, List<string> errors)
    {
        int count = 0;
        var paths = new List<string>();
        string? startMenuFolder = null;

        if (manifest != null)
        {
            paths.AddRange(manifest.Shortcuts);
            startMenuFolder = manifest.StartMenuFolder;
        }
        else
        {
            // No manifest — try the well-known default locations
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            paths.Add(Path.Combine(desktop, $"{AppName}.lnk"));
            startMenuFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName);
            paths.Add(Path.Combine(startMenuFolder, $"{AppName}.lnk"));
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

    private static bool RemoveRegistryEntries(List<string> errors)
    {
        bool removedAny = false;
        var keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + ProductGuid;
        var wow64KeyPath = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\" + ProductGuid;

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
            var path = Path.Combine(AppContext.BaseDirectory, "launcher-config.json");
            if (File.Exists(path)) File.Delete(path);
            return true;
        }
        catch (Exception ex)
        {
            errors.Add($"config: {ex.Message}");
            return false;
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// True if <paramref name="folder"/> looks like a WoL-only container that
    /// can be safely deleted in its entirety. When this returns true and there's
    /// no manifest, we can fall back to removing the whole folder.
    ///
    /// Two signals are accepted:
    ///   1. The folder is NAMED "Wars of Liberty" / "WarsOfLiberty" — even if
    ///      it contains age3y.exe, this means the installer cloned AoE3 INTO
    ///      this container (full install), and the whole thing is launcher-owned.
    ///   2. The folder has the WoL marker AND does NOT have age3y.exe at root
    ///      or in bin\ — i.e. it's clearly mod-only (no AoE3 base game inside).
    /// </summary>
    private static bool LooksLikeStandaloneSubfolder(string folder)
    {
        if (!Directory.Exists(folder)) return false;

        // Must have the WoL marker either way
        if (!Directory.Exists(Path.Combine(folder, "art", "zulushield")))
            return false;

        // Signal 1: folder name. "Wars of Liberty" is the only name the
        // launcher ever creates. If we see it, treat the whole folder as
        // launcher-owned regardless of contents.
        var name = Path.GetFileName(folder.TrimEnd('\\', '/'));
        if (string.Equals(name, "Wars of Liberty", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "WarsOfLiberty", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Signal 2: no AoE3 base game inside.
        if (File.Exists(Path.Combine(folder, "age3y.exe"))) return false;
        if (File.Exists(Path.Combine(folder, "age3.exe"))) return false;
        if (File.Exists(Path.Combine(folder, "bin", "age3y.exe"))) return false;
        if (File.Exists(Path.Combine(folder, "bin", "age3.exe"))) return false;

        return true;
    }

    private static bool IsInsideRoot(string path, string root)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
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

/// <summary>How an uninstall can proceed (or not) given the current state.</summary>
public enum UninstallMode
{
    /// <summary>wol-manifest.json exists — safe, precise delete.</summary>
    Manifest,
    /// <summary>No manifest, but the path is clearly a WoL-only subfolder.</summary>
    SubfolderFallback,
    /// <summary>Path looks like an AoE3 root — refuse to avoid breaking the base game.</summary>
    RefusedMergedWithAoe3,
    /// <summary>Path doesn't exist or no install was found.</summary>
    NothingToDo,
}

public record UninstallPlan(
    UninstallMode Mode,
    InstallManifest? Manifest,
    int FileCount,
    int DirectoryCount)
{
    /// <summary>Set externally for SubfolderFallback mode where Manifest is null.</summary>
    public string InstallPath { get; set; } = "";
}
