using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Locates and launches the executable that actually runs the active mod.
///
/// For Wars of Liberty that is <c>age3y.exe</c> (Age of Empires III: TAD)
/// because WoL patches the base game's data files and reuses its binary.
/// For Improvement Mod that is <c>age3m.exe</c>, IM's own .exe shipped
/// alongside the AoE3 binaries. Each <see cref="ModProfile"/> declares
/// its own <see cref="ModProfile.GameExecutable"/>; this class is just the
/// "where on disk does it actually live" resolver.
///
/// Layout examples observed in the wild:
///   - Steam:           ...\Steam\steamapps\common\Age Of Empires 3\bin\age3y.exe
///   - Microsoft Games: ...\Microsoft Games\Age of Empires III\age3y.exe
///   - GOG:             ...\GOG Games\Age of Empires III\age3y.exe
///
/// Note that Steam puts the executable in a <c>bin\</c> subfolder, while
/// the older retail layout has it at the root.
/// </summary>
public static class GameLauncher
{
    /// <summary>
    /// Find the active mod's executable by checking known locations in
    /// priority order. Returns null if no candidate is found anywhere.
    /// </summary>
    public static string? Find(LauncherConfig config, string? modInstallPath, ModProfile profile)
    {
        var exeName = string.IsNullOrEmpty(profile.GameExecutable)
            ? "age3y.exe"
            : profile.GameExecutable;

        var checkedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in EnumerateCandidates(config, modInstallPath, exeName))
        {
            if (string.IsNullOrEmpty(candidate)) continue;
            if (!checkedPaths.Add(candidate)) continue;       // skip duplicates
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// Locate the base Age of Empires III install on this machine, independent
    /// of which mod is currently active. Used by the UI to tell whether AoE3
    /// is installed at all (the "Age of Empires III not found" badge in the
    /// status card), so the answer doesn't flip-flop when the user switches
    /// between mods that ship different executables.
    ///
    /// Probes <c>age3y.exe</c> specifically — the canonical TAD binary. Every
    /// AoE3 mod in this launcher relies on TAD being installed; mods that
    /// ship their own .exe (e.g. Improvement Mod's <c>age3m.exe</c>) still
    /// live next to <c>age3y.exe</c> inside an existing AoE3 install. So
    /// "found age3y.exe somewhere" is the right test for "AoE3 is here".
    ///
    /// Returns the full path to <c>age3y.exe</c> if found, null otherwise.
    /// </summary>
    public static string? FindAoe3Install(LauncherConfig config)
    {
        // We intentionally pass modInstallPath=null: the search should not be
        // biased by whichever mod folder the active profile happens to point
        // at. We want a clean "scan the disk for age3y.exe" pass.
        var checkedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in EnumerateCandidates(config, modInstallPath: null, exeName: "age3y.exe"))
        {
            if (string.IsNullOrEmpty(candidate)) continue;
            if (!checkedPaths.Add(candidate)) continue;
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    /// <summary>
    /// Find the Age of Empires III install ROOT (the folder that *contains*
    /// the game executable, walking up from a Steam-style bin/ subfolder if
    /// needed). Returns null if AoE3 cannot be located on this machine.
    ///
    /// This is what mod installers care about: isolated mods (WoL) need to
    /// be installed alongside AoE3's data files, and in-place mods (IM)
    /// install directly into this folder.
    /// </summary>
    public static string? FindAoe3InstallRoot(LauncherConfig config, string? modInstallPath, ModProfile profile)
    {
        var exePath = Find(config, modInstallPath, profile);
        if (string.IsNullOrEmpty(exePath)) return null;

        // The "install root" is the folder containing AoE3's data — typically
        // the parent of either `<game>.exe` (legacy retail) or `bin\<game>.exe`
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
    /// Best-guess install folder for the active mod: the recommended
    /// subfolder of AoE3 if we can find AoE3, otherwise null. The launcher
    /// uses this to pre-fill the folder picker dialog with a sensible
    /// default. For in-place mods this equals the AoE3 root itself.
    /// </summary>
    public static string? SuggestModInstallFolder(LauncherConfig config, string? modInstallPath, ModProfile profile)
    {
        var aoe3Root = FindAoe3InstallRoot(config, modInstallPath, profile);
        if (string.IsNullOrEmpty(aoe3Root)) return null;

        if (profile.InstallType == ModInstallType.InPlaceOverlay)
            return aoe3Root;

        // Isolated-folder mod: append the profile's folder name so the
        // user lands on something like "C:\Program Files (x86)\Wars of Liberty".
        var folderName = string.IsNullOrEmpty(profile.DefaultInstallFolder)
            ? profile.DisplayName
            : Path.GetFileName(profile.DefaultInstallFolder.TrimEnd('\\', '/'));
        return Path.Combine(aoe3Root, folderName);
    }

    /// <summary>Lazy enumeration of likely paths, in priority order.</summary>
    private static IEnumerable<string> EnumerateCandidates(
        LauncherConfig config,
        string? modInstallPath,
        string exeName)
    {
        // 1. Cached path from config (set after a successful previous launch).
        //    Only used when its filename matches the active profile's exe — a
        //    cached age3y.exe is no good for IM, and vice versa.
        if (!string.IsNullOrWhiteSpace(config.GameExecutable)
            && string.Equals(
                Path.GetFileName(config.GameExecutable),
                exeName,
                StringComparison.OrdinalIgnoreCase))
        {
            yield return config.GameExecutable;
        }

        // 2. Walk up from the mod install folder, checking each level for
        //    the exe both at the root and inside a `bin\` subfolder
        //    (Steam layout).
        if (!string.IsNullOrWhiteSpace(modInstallPath))
        {
            var current = new DirectoryInfo(modInstallPath);
            for (int i = 0; i < 4 && current != null; i++)
            {
                yield return Path.Combine(current.FullName, exeName);
                yield return Path.Combine(current.FullName, "bin", exeName);
                current = current.Parent;
            }
        }

        // 3. Common installation roots — try each on every available drive.
        var commonSubpaths = new[]
        {
            // Steam
            $@"Steam\steamapps\common\Age Of Empires 3\bin\{exeName}",
            $@"Steam\steamapps\common\Age of Empires 3\bin\{exeName}",
            $@"Steam\steamapps\common\Age of Empires III\bin\{exeName}",
            // Microsoft Games (legacy retail)
            $@"Microsoft Games\Age of Empires III\{exeName}",
            // GOG
            $@"GOG Games\Age of Empires III\{exeName}",
            $@"GOG.com\Age of Empires III\{exeName}",
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
    /// Launch the active mod's game. If we can't find the executable anywhere,
    /// throws so the UI can surface a friendly "please point us to your AoE3
    /// install" dialog.
    /// </summary>
    public static void Launch(LauncherConfig config, string? modInstallPath, ModProfile profile)
    {
        var exePath = Find(config, modInstallPath, profile);

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

        DiagnosticLog.Write($"Launching game: {exePath} (profile '{profile.Id}')");

        var arguments = !string.IsNullOrWhiteSpace(profile.GameArguments)
            ? profile.GameArguments
            : config.GameArguments;

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments ?? "",
            WorkingDirectory = Path.GetDirectoryName(exePath),
            UseShellExecute = true
        };
        Process.Start(startInfo);
    }
}
