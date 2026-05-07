using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Helpers for the user-data folder Wars of Liberty creates under the user's
/// Documents — typically:
///
///   C:\Users\&lt;name&gt;\Documents\My Games\Wars of Liberty\
///
/// That folder holds saves (Savegame\), custom metropolises, replays and a
/// few config files. AoE3 itself uses the same "My Games" parent folder,
/// which is the standard Microsoft convention.
///
/// None of this is touched by the launcher's install/uninstall flow (it's
/// user data and should survive a reinstall), but if the user installs an
/// OLDER version of WoL on top of newer save files the game may crash on
/// startup — newer metropolis formats can't be parsed by older binaries.
///
/// This service only DETECTS and offers to back up. It never deletes.
/// </summary>
public static class UserDataService
{
    /// <summary>
    /// The default WoL user-data folder under Documents. Returns null if we
    /// can't determine the user's Documents path.
    /// </summary>
    public static string? GetUserDataFolder()
    {
        try
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrEmpty(docs)) return null;
            return Path.Combine(docs, "My Games", "Wars of Liberty");
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// True if the user has a populated WoL data folder under Documents.
    /// "Populated" means at least one file exists somewhere under it.
    /// </summary>
    public static bool HasExistingUserData()
    {
        var folder = GetUserDataFolder();
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
    /// WoL keeps custom metropolises and saved games — the files most
    /// likely to be in a newer format that the freshly-installed older
    /// binary can't parse, causing the loading-screen hang.
    /// Returns 0 if the folder doesn't exist or can't be read.
    /// </summary>
    public static int CountSavegameFiles()
    {
        var folder = GetUserDataFolder();
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
    /// Renames the WoL Documents folder to "Wars of Liberty.bak.&lt;timestamp&gt;"
    /// so the game starts with a clean slate. Returns the new backup path on
    /// success, or null if there was nothing to back up / the rename failed.
    ///
    /// We never DELETE — the user can manually clean up the .bak folder later
    /// once they've confirmed the new install works.
    /// </summary>
    public static string? BackupUserData()
    {
        var folder = GetUserDataFolder();
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return null;

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var backupPath = folder + ".bak." + stamp;

        try
        {
            Directory.Move(folder, backupPath);
            DiagnosticLog.Write($"Backed up WoL user data: '{folder}' -> '{backupPath}'");
            return backupPath;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Failed to back up WoL user data: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Opens the WoL user-data folder in Explorer so the user can inspect /
    /// move / delete files manually. No-op if the folder doesn't exist.
    /// </summary>
    public static void OpenUserDataFolder()
    {
        var folder = GetUserDataFolder();
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
}
