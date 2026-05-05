using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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

        return found;
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
