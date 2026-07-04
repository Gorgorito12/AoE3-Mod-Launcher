using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Helpers for the per-mod user-data folder under the user's Documents.
/// Each mod that opts into this feature declares a folder name (relative to
/// <c>%USERPROFILE%\Documents\My Games\</c>) in its catalog manifest as
/// <c>userDataFolder</c>; WoL's built-in profile sets it to
/// <c>"Wars of Liberty"</c>. Other mods can leave it empty to opt out.
///
/// Typical folder shape:
///
///   C:\Users\&lt;name&gt;\Documents\My Games\&lt;folder&gt;\
///
/// Holds saves (<c>Savegame\</c>), custom metropolises, replays and config
/// files. AoE3 itself uses the same "My Games" parent folder — that's the
/// standard Microsoft convention.
///
/// None of this is touched by the launcher's install/uninstall flow (it's
/// user data and should survive a reinstall), but if the user installs an
/// OLDER mod version on top of newer save files the game can crash on
/// startup — newer metropolis formats can't be parsed by older binaries.
///
/// This service only DETECTS and offers to back up. It never deletes.
/// </summary>
public static class UserDataService
{
    /// <summary>
    /// Resolves the absolute path of a mod's user-data folder. Returns null
    /// when <paramref name="folderName"/> is empty (mod doesn't opt in) or
    /// when we can't determine the user's Documents path.
    ///
    /// TWO candidate Documents roots are probed, because they can diverge and
    /// the 2007 engine's saves may live in either (the "backup went to a
    /// totally different path" report from a German user):
    ///   1. The SYSTEM Documents folder (GetFolderPath(MyDocuments)) — follows
    ///      Windows redirections, e.g. OneDrive Known Folder Move, where on a
    ///      German system the REAL path is "...\OneDrive\Dokumente".
    ///   2. The PHYSICAL "%USERPROFILE%\Documents" — where saves written
    ///      BEFORE a redirection was enabled (or by software that ignores it)
    ///      still live.
    /// The first candidate whose "<root>\My Games\<folder>" EXISTS wins; when
    /// neither exists the redirected one wins (creation case — new data should
    /// follow the system convention). Divergence is logged once per session so
    /// a diagnostic bundle carries the evidence.
    /// </summary>
    public static string? GetUserDataFolder(string folderName)
    {
        if (string.IsNullOrEmpty(folderName)) return null;
        try
        {
            var candidates = GetCandidateUserDataFolders(folderName);
            var chosen = PickUserDataFolder(candidates, Directory.Exists);
            LogDivergenceOnce(folderName, candidates, chosen);
            return chosen;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Ordered, deduped candidate paths of "<Documents root>\My Games\<folder>"
    /// — redirected Documents first, physical %USERPROFILE%\Documents second
    /// (only when it differs). Exposed so the UI/diagnostics can surface both.
    /// </summary>
    public static IReadOnlyList<string> GetCandidateUserDataFolders(string folderName)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(folderName)) return result;

        void Add(string? docsRoot)
        {
            if (string.IsNullOrEmpty(docsRoot)) return;
            string path;
            try { path = Path.Combine(docsRoot, "My Games", folderName); }
            catch { return; }
            if (!result.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)))
                result.Add(path);
        }

        try { Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)); } catch { }
        try
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(profile)) Add(Path.Combine(profile, "Documents"));
        }
        catch { }
        return result;
    }

    /// <summary>
    /// Pure candidate-selection rule (testable with an injected existence
    /// probe): first candidate folder that exists wins; none exists → the
    /// first candidate (creation follows the system Documents convention);
    /// empty candidate list → null.
    /// </summary>
    internal static string? PickUserDataFolder(
        IReadOnlyList<string> candidates, Func<string, bool> directoryExists)
    {
        if (candidates.Count == 0) return null;
        foreach (var c in candidates)
        {
            try { if (directoryExists(c)) return c; }
            catch { /* unreadable candidate — treat as absent */ }
        }
        return candidates[0];
    }

    /// <summary>
    /// The NON-chosen candidate that exists and holds at least one file, or
    /// null. Surfaced as a warning in the USER DATA tab — data in two places
    /// means a Documents redirection happened mid-life and the user should
    /// know both locations exist.
    /// </summary>
    public static string? GetAlternateDataFolderWithFiles(string folderName)
    {
        try
        {
            var candidates = GetCandidateUserDataFolders(folderName);
            if (candidates.Count < 2) return null;
            var chosen = PickUserDataFolder(candidates, Directory.Exists);
            foreach (var c in candidates)
            {
                if (string.Equals(c, chosen, StringComparison.OrdinalIgnoreCase)) continue;
                if (Directory.Exists(c)
                    && Directory.EnumerateFiles(c, "*", SearchOption.AllDirectories).Any())
                    return c;
            }
        }
        catch { /* best-effort informational probe */ }
        return null;
    }

    // Divergence is interesting once per mod per session — GetUserDataFolder
    // is called from every tab refresh and would otherwise spam the log.
    private static readonly HashSet<string> s_divergenceLogged = new(StringComparer.OrdinalIgnoreCase);

    private static void LogDivergenceOnce(
        string folderName, IReadOnlyList<string> candidates, string? chosen)
    {
        if (candidates.Count < 2 || chosen == null) return;
        if (!s_divergenceLogged.Add(folderName)) return;
        var flags = candidates
            .Select(c => $"'{c}' (exists={SafeExists(c)})");
        DiagnosticLog.Write(
            $"User-data roots diverge for '{folderName}': {string.Join(" vs ", flags)} -> using '{chosen}'.");

        static bool SafeExists(string p)
        {
            try { return Directory.Exists(p); } catch { return false; }
        }
    }

    /// <summary>
    /// True if the user has a populated data folder for this mod under
    /// Documents. "Populated" means at least one file exists somewhere
    /// under it. Returns false when the mod doesn't opt into the feature.
    /// </summary>
    public static bool HasExistingUserData(string folderName)
    {
        var folder = GetUserDataFolder(folderName);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return false;
        try
        {
            return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Any();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Count of files inside the <c>Savegame\</c> subfolder. This is where
    /// the game keeps custom metropolises and saved games — the files most
    /// likely to be in a newer format that a freshly-installed older
    /// binary can't parse, causing the loading-screen hang.
    /// Returns 0 if the folder doesn't exist or can't be read.
    /// </summary>
    public static int CountSavegameFiles(string folderName)
    {
        var folder = GetUserDataFolder(folderName);
        if (string.IsNullOrEmpty(folder)) return 0;
        var savegame = Path.Combine(folder, "Savegame");
        if (!Directory.Exists(savegame)) return 0;
        try
        {
            return Directory.EnumerateFiles(savegame, "*", SearchOption.AllDirectories).Count();
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Renames the user-data folder to "&lt;folderName&gt;.bak.&lt;timestamp&gt;"
    /// so the game starts with a clean slate. Returns the new backup path on
    /// success, or null if there was nothing to back up / the rename failed.
    ///
    /// We never DELETE — the user can manually clean up the .bak folder later
    /// once they've confirmed the new install works.
    /// </summary>
    public static string? BackupUserData(string folderName)
    {
        var folder = GetUserDataFolder(folderName);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return null;

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var backupPath = folder + ".bak." + stamp;

        try
        {
            Directory.Move(folder, backupPath);
            DiagnosticLog.Write($"Backed up user data: '{folder}' -> '{backupPath}'");
            return backupPath;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Failed to back up user data ('{folderName}'): {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Opens the user-data folder in Explorer so the user can inspect /
    /// move / delete files manually. No-op if the folder doesn't exist or
    /// the mod doesn't opt into the feature.
    /// </summary>
    public static void OpenUserDataFolder(string folderName)
    {
        var folder = GetUserDataFolder(folderName);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Failed to open user-data folder: {ex.Message}");
        }
    }

    /// <summary>
    /// Information about a single backup the launcher created on a previous
    /// install (renamed from <c>&lt;folder&gt;</c> to
    /// <c>&lt;folder&gt;.bak.&lt;ts&gt;</c>).
    /// </summary>
    public record BackupInfo(
        string Path,
        DateTime CreatedAt,
        int FileCount,
        int SavegameCount,
        long TotalBytes);

    /// <summary>
    /// Lists every backup folder that lives next to the active user-data
    /// folder — in EVERY candidate Documents root, so a backup made before a
    /// OneDrive/moved-Documents redirection still shows up. Sorted by
    /// creation time, most recent first. Returns an empty list when the mod
    /// doesn't opt into the feature or there are no backups.
    /// </summary>
    public static List<BackupInfo> ListBackups(string folderName)
    {
        var result = new List<BackupInfo>();
        var parents = GetCandidateUserDataFolders(folderName)
            .Select(Path.GetDirectoryName)
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(Directory.Exists)
            .ToList();

        // Folders we created look like: "<folderName>.bak.20260507-123456"
        try
        {
            var pattern = $"{folderName}.bak.*";
            foreach (var dir in parents.SelectMany(parent => Directory.EnumerateDirectories(parent, pattern)))
            {
                int count = 0;
                long totalBytes = 0;
                try
                {
                    foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    {
                        count++;
                        try { totalBytes += new FileInfo(f).Length; }
                        catch { /* unreadable file; skip its size */ }
                    }
                }
                catch { /* unreadable; report 0 */ }

                int savegameCount = 0;
                try
                {
                    var savegame = Path.Combine(dir, "Savegame");
                    if (Directory.Exists(savegame))
                        savegameCount = Directory.EnumerateFiles(savegame, "*", SearchOption.AllDirectories).Count();
                }
                catch { /* unreadable; report 0 */ }

                DateTime created;
                try { created = Directory.GetCreationTime(dir); }
                catch { created = DateTime.MinValue; }

                result.Add(new BackupInfo(dir, created, count, savegameCount, totalBytes));
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Failed to enumerate user-data backups: {ex.Message}");
        }

        result.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));
        return result;
    }

    /// <summary>
    /// Restores a backup folder by swapping it with the active user-data
    /// folder. If the active folder currently has files, those files are
    /// renamed to a new ".bak.&lt;ts&gt;" first so nothing is lost — the user
    /// can swap back and forth between snapshots indefinitely.
    /// </summary>
    /// <returns>
    /// The path of the new backup that was created from the active data
    /// (so the caller can mention it to the user), or null if the active
    /// folder was empty / didn't exist.
    /// </returns>
    public static string? RestoreBackup(string folderName, string backupPath)
    {
        if (string.IsNullOrEmpty(backupPath) || !Directory.Exists(backupPath))
            throw new DirectoryNotFoundException($"Backup not found: {backupPath}");

        var folder = GetUserDataFolder(folderName);
        if (string.IsNullOrEmpty(folder))
            throw new InvalidOperationException(
                "Could not resolve Documents path / mod doesn't declare userDataFolder.");

        string? newBackupOfCurrent = null;

        // Step 1: if the active folder has anything in it, snapshot it as a
        // fresh backup. We never overwrite without preserving.
        if (Directory.Exists(folder))
        {
            bool hasFiles = false;
            try { hasFiles = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Any(); }
            catch { hasFiles = true; /* be conservative — don't lose data we can't read */ }

            if (hasFiles)
            {
                var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                newBackupOfCurrent = folder + ".bak." + stamp;
                Directory.Move(folder, newBackupOfCurrent);
                DiagnosticLog.Write($"Snapshotted active data before restore: '{folder}' -> '{newBackupOfCurrent}'");
            }
            else
            {
                // Empty folder; just delete so Move below can create cleanly.
                try { Directory.Delete(folder, recursive: true); } catch { }
            }
        }

        // Step 2: rename the chosen backup back into the active path. With
        // dual-root listing the backup may live under the OTHER Documents
        // root (e.g. physical Documents while the active folder resolves to
        // OneDrive) — a cross-volume Move throws IOException, so fall back to
        // a recursive copy. The source backup is deliberately LEFT IN PLACE
        // on that path (never delete on a degraded path; the user can clean
        // it up once the restore is confirmed good).
        try
        {
            Directory.Move(backupPath, folder);
        }
        catch (IOException)
        {
            CopyDirectory(backupPath, folder);
            DiagnosticLog.Write(
                $"Restore crossed volumes; backup copied and the original was left at '{backupPath}'.");
        }
        DiagnosticLog.Write($"Restored backup: '{backupPath}' -> '{folder}'");

        return newBackupOfCurrent;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), overwrite: true);
    }
}
