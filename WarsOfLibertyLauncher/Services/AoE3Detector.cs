using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Detects Age of Empires III installations on disk.
///
/// Wars of Liberty must be installed inside the AoE3 folder because the AoE3
/// engine only loads mod data files (with the "y" suffix — protoy.xml,
/// techtreey.xml, etc.) from its own directory tree. Installing the mod
/// elsewhere means the engine never sees the mod files at all.
///
/// Two distinct concepts are tracked:
///   - GameFolder: the folder containing age3y.exe (this is what AoE3 considers
///                 its install root, e.g. "...\Age Of Empires 3\bin\" for Steam)
///   - ModRoot:    the folder where the WoL files belong (one level up from
///                 GameFolder when bin\ is involved, otherwise same as GameFolder)
/// </summary>
public static class AoE3Detector
{
    /// <summary>
    /// One detected AoE3 installation. Multiple may exist on the same machine
    /// (Steam + GOG, or older retail + Steam).
    /// </summary>
    public record Installation(string GameFolder, string ModRoot, string Source);

    /// <summary>
    /// Find every AoE3 installation we can locate. Ordered by likelihood that
    /// the user wants to install WoL there (Steam first, then GOG, then retail).
    /// Returns an empty list if none are found.
    /// </summary>
    public static List<Installation> FindAll()
    {
        var found = new List<Installation>();
        var seenFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Each entry: relative path to age3y.exe + a friendly source label
        var probes = new[]
        {
            // Steam — most common today
            (Path: @"Steam\steamapps\common\Age Of Empires 3\bin\age3y.exe", Source: "Steam"),
            (Path: @"Steam\steamapps\common\Age of Empires 3\bin\age3y.exe", Source: "Steam"),
            (Path: @"Steam\steamapps\common\Age of Empires III\bin\age3y.exe", Source: "Steam"),

            // GOG
            (Path: @"GOG Games\Age of Empires III\age3y.exe", Source: "GOG"),
            (Path: @"GOG.com\Age of Empires III\age3y.exe", Source: "GOG"),

            // Microsoft Games (legacy retail)
            (Path: @"Microsoft Games\Age of Empires III\age3y.exe", Source: "Retail"),
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
        foreach (var (relativePath, source) in probes)
        {
            var fullPath = Path.Combine(root, relativePath);
            if (!File.Exists(fullPath)) continue;

            var gameFolder = Path.GetDirectoryName(fullPath)!;
            var modRoot = ResolveModRoot(gameFolder);

            // Avoid reporting the same install twice when multiple probe
            // patterns happen to match the same folder.
            if (!seenFolders.Add(modRoot)) continue;

            found.Add(new Installation(gameFolder, modRoot, source));
        }

        // Second pass: Steam libraries added by the user in non-default
        // locations (D:\SteamLibrary, E:\Games\Steam, …). Read them from
        // libraryfolders.vdf via Steam's registry-stored install path so
        // the install dialog finds AoE3 on any drive Steam knows about.
        var steamSubpaths = new[]
        {
            @"steamapps\common\Age Of Empires 3\bin\age3y.exe",
            @"steamapps\common\Age of Empires 3\bin\age3y.exe",
            @"steamapps\common\Age of Empires III\bin\age3y.exe",
        };
        foreach (var library in EnumerateSteamLibraries())
        {
            foreach (var sub in steamSubpaths)
            {
                var fullPath = Path.Combine(library, sub);
                if (!File.Exists(fullPath)) continue;

                var gameFolder = Path.GetDirectoryName(fullPath)!;
                var modRoot = ResolveModRoot(gameFolder);
                if (!seenFolders.Add(modRoot)) continue;

                found.Add(new Installation(gameFolder, modRoot, "Steam"));
            }
        }

        return found;
    }

    /// <summary>
    /// Steam install paths: the main one from the registry plus every extra
    /// library the user added via Steam's "Add library folder" feature, which
    /// Steam tracks in <c>steamapps\libraryfolders.vdf</c>. Yields whatever
    /// looks usable; callers probe each for a Steam-layout AoE3 install.
    /// </summary>
    private static IEnumerable<string> EnumerateSteamLibraries()
    {
        var steamRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in ReadSteamRootCandidates())
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            steamRoots.Add(root);
        }

        var seenLibraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var steamRoot in steamRoots)
        {
            // The Steam main install is always a library.
            if (Directory.Exists(steamRoot) && seenLibraries.Add(steamRoot))
                yield return steamRoot;

            var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;

            string content;
            try { content = File.ReadAllText(vdf); }
            catch { continue; }

            // Pull every "path" entry. VDF escapes backslashes as "\\";
            // unescape to get a usable filesystem path.
            var matches = System.Text.RegularExpressions.Regex.Matches(
                content, "\"path\"\\s*\"([^\"]+)\"");
            foreach (System.Text.RegularExpressions.Match m in matches)
            {
                var path = m.Groups[1].Value.Replace("\\\\", "\\");
                if (Directory.Exists(path) && seenLibraries.Add(path))
                    yield return path;
            }
        }
    }

    private static IEnumerable<string?> ReadSteamRootCandidates()
    {
        yield return ReadRegistryString(RegistryHive.LocalMachine, RegistryView.Registry32,
            @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
        yield return ReadRegistryString(RegistryHive.LocalMachine, RegistryView.Registry64,
            @"SOFTWARE\Valve\Steam", "InstallPath");
        yield return ReadRegistryString(RegistryHive.CurrentUser, RegistryView.Default,
            @"Software\Valve\Steam", "SteamPath");
    }

    private static string? ReadRegistryString(RegistryHive hive, RegistryView view, string subKey, string valueName)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(subKey);
            return key?.GetValue(valueName) as string;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Given the folder containing age3y.exe, figure out where Wars of Liberty
    /// files belong. For Steam, age3y.exe lives in "...\Age Of Empires 3\bin\"
    /// but the mod files belong one level up in "...\Age Of Empires 3\".
    /// For retail, the .exe is at the root, so they're the same folder.
    /// </summary>
    public static string ResolveModRoot(string gameFolder)
    {
        if (string.IsNullOrEmpty(gameFolder)) return gameFolder;

        var trimmed = gameFolder.TrimEnd('\\', '/');
        var leaf = Path.GetFileName(trimmed);

        // Steam layout: walk up out of bin\
        if (string.Equals(leaf, "bin", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Path.GetDirectoryName(trimmed);
            if (!string.IsNullOrEmpty(parent))
                return parent;
        }

        return trimmed;
    }

    /// <summary>
    /// Validate that <paramref name="folder"/> is plausibly inside an AoE3
    /// installation. We look for telltale files like age3y.exe nearby —
    /// either inside the folder itself, in a `bin\` subfolder, or in the
    /// immediate parent's `bin\` subfolder.
    /// </summary>
    /// <summary>
    /// Returns true if <paramref name="folder"/> itself looks like an AoE3 install
    /// (has age3y.exe directly or in bin\).
    /// </summary>
    public static bool LooksLikeAoE3(string folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return false;
        return File.Exists(Path.Combine(folder, "age3y.exe"))
            || File.Exists(Path.Combine(folder, "bin", "age3y.exe"));
    }

    public static bool LooksLikeInsideAoE3(string folder)
    {
        if (string.IsNullOrEmpty(folder)) return false;

        var current = new DirectoryInfo(folder);
        // Walk up at most 2 levels: the chosen folder, its parent, and grandparent.
        // age3y.exe typically lives 0–1 levels above where the mod files go.
        for (int i = 0; i < 3 && current != null; i++)
        {
            if (File.Exists(Path.Combine(current.FullName, "age3y.exe"))
                || File.Exists(Path.Combine(current.FullName, "bin", "age3y.exe")))
            {
                return true;
            }
            current = current.Parent;
        }
        return false;
    }
}
