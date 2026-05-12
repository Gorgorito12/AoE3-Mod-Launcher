using System;
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
    /// </summary>
    public static string? GetUserDataFolder(string folderName)
    {
        if (string.IsNullOrEmpty(folderName)) return null;
        try
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrEmpty(docs)) return null;
            return Path.Combine(docs, "My Games", folderName);
        }
        catch
        {
            return null;
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
    /// folder. Sorted by creation time, most recent first. Returns an
    /// empty list when the mod doesn't opt into the feature or there are
    /// no backups.
    /// </summary>
    public static List<BackupInfo> ListBackups(string folderName)
    {
        var result = new List<BackupInfo>();
        var folder = GetUserDataFolder(folderName);
        if (string.IsNullOrEmpty(folder)) return result;

        var parent = Path.GetDirectoryName(folder);
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent)) return result;

        // Folders we created look like: "<folderName>.bak.20260507-123456"
        try
        {
            var pattern = $"{folderName}.bak.*";
            foreach (var dir in Directory.EnumerateDirectories(parent, pattern))
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

        // Step 2: rename the chosen backup back into the active path.
        Directory.Move(backupPath, folder);
        DiagnosticLog.Write($"Restored backup: '{backupPath}' -> '{folder}'");

        return newBackupOfCurrent;
    }
}
