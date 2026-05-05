using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Locates and launches the Age of Empires III: The Asian Dynasties executable
/// (age3y.exe), which is the actual game binary used by Wars of Liberty.
///
/// Wars of Liberty does NOT have its own .exe — it patches AoE3's data files,
/// and the game launches via age3y.exe in the AoE3 folder.
///
/// Layout examples observed in the wild:
///   - Steam:           ...\Steam\steamapps\common\Age Of Empires 3\bin\age3y.exe
///   - Microsoft Games: ...\Microsoft Games\Age of Empires III\age3y.exe
///   - GOG:             ...\GOG Games\Age of Empires III\age3y.exe
///
/// Note that Steam puts the executable in a `bin\` subfolder, while the older
/// retail layout has it at the root.
/// </summary>
public static class GameLauncher
{
    /// <summary>
    /// Find age3y.exe by checking known locations in priority order.
    /// Returns null if no candidate is found anywhere.
    /// </summary>
    public static string? Find(LauncherConfig config, string? wolInstallPath)
    {
        var checkedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in EnumerateCandidates(config, wolInstallPath))
        {
            if (string.IsNullOrEmpty(candidate)) continue;
            if (!checkedPaths.Add(candidate)) continue;       // skip duplicates
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// Find the Age of Empires III install ROOT (the folder that *contains*
    /// age3y.exe, walking up from a Steam-style bin/ subfolder if needed).
    /// Returns null if AoE3 cannot be located on this machine.
    ///
    /// This is what mod installers care about: WoL needs to be installed
    /// alongside AoE3's data files, not in some random location.
    /// </summary>
    public static string? FindAoe3InstallRoot(LauncherConfig config, string? wolInstallPath)
    {
        var exePath = Find(config, wolInstallPath);
        if (string.IsNullOrEmpty(exePath)) return null;

        // The "install root" is the folder containing AoE3's data — typically
        // the parent of either `age3y.exe` (legacy retail) or `bin\age3y.exe`
        // (Steam). The marker for "this is the install root" is the presence
        // of a `data` subfolder.
        var dir = Path.GetDirectoryName(exePath);
        for (int i = 0; i < 3 && !string.IsNullOrEmpty(dir); i++)
        {
            if (Directory.Exists(Path.Combine(dir, "data")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }

        // Fallback: just return the folder that holds the .exe
        return Path.GetDirectoryName(exePath);
    }

    /// <summary>
    /// Best-guess WoL install folder: the recommended subfolder of AoE3 if we
    /// can find AoE3, otherwise null. The launcher uses this to pre-fill the
    /// folder picker dialog with a sensible default.
    /// </summary>
    public static string? SuggestModInstallFolder(LauncherConfig config, string? wolInstallPath)
    {
        var aoe3Root = FindAoe3InstallRoot(config, wolInstallPath);
        if (string.IsNullOrEmpty(aoe3Root)) return null;
        return Path.Combine(aoe3Root, "Wars of Liberty");
    }

    /// <summary>Lazy enumeration of likely paths, in priority order.</summary>
    private static IEnumerable<string> EnumerateCandidates(
        LauncherConfig config,
        string? wolInstallPath)
    {
        // 1. Cached path from config (set after a successful previous launch)
        if (!string.IsNullOrWhiteSpace(config.GameExecutable))
            yield return config.GameExecutable;

        // 2. Walk up from WoL install folder, checking each level for age3y.exe
        //    in both the root and a `bin\` subfolder (Steam layout).
        if (!string.IsNullOrWhiteSpace(wolInstallPath))
        {
            var current = new DirectoryInfo(wolInstallPath);
            for (int i = 0; i < 4 && current != null; i++)
            {
                yield return Path.Combine(current.FullName, "age3y.exe");
                yield return Path.Combine(current.FullName, "bin", "age3y.exe");
                current = current.Parent;
            }
        }

        // 3. Common installation roots — try each on every available drive.
        var commonSubpaths = new[]
        {
            // Steam
            @"Steam\steamapps\common\Age Of Empires 3\bin\age3y.exe",
            @"Steam\steamapps\common\Age of Empires 3\bin\age3y.exe",
            @"Steam\steamapps\common\Age of Empires III\bin\age3y.exe",
            // Microsoft Games (legacy retail)
            @"Microsoft Games\Age of Empires III\age3y.exe",
            // GOG
            @"GOG Games\Age of Empires III\age3y.exe",
            @"GOG.com\Age of Empires III\age3y.exe",
        };

        var roots = new[]
        {
            @"C:\Program Files (x86)\",
            @"C:\Program Files\",
            @"C:\",
            @"D:\Program Files (x86)\",
            @"D:\Program Files\",
            @"D:\",
            @"E:\Program Files (x86)\",
            @"E:\Program Files\",
            @"E:\",
        };

        foreach (var root in roots)
        foreach (var sub in commonSubpaths)
            yield return Path.Combine(root, sub);
    }

    /// <summary>
    /// Launch the game. If we can't find age3y.exe anywhere, throws so the UI
    /// can surface a friendly "please point us to your AoE3 install" dialog.
    /// </summary>
    public static void Launch(LauncherConfig config, string? wolInstallPath)
    {
        var exePath = Find(config, wolInstallPath);

        if (exePath == null)
        {
            throw new FileNotFoundException(
                WarsOfLibertyLauncher.Localization.Strings.Get("ErrGameExeNotFound"));
        }

        // Cache the resolved path so the next launch is instant — no need to
        // re-scan the filesystem if we already know where the game lives.
        if (config.GameExecutable != exePath)
        {
            config.GameExecutable = exePath;
            config.Save();
        }

        DiagnosticLog.Write($"Launching game: {exePath}");

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = config.GameArguments,
            WorkingDirectory = Path.GetDirectoryName(exePath),
            UseShellExecute = true
        };
        Process.Start(startInfo);
    }
}
