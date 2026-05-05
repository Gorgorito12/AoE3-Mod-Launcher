using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Detects existing Age of Empires III: The Asian Dynasties installations.
/// Used as the source for cloning when installing Wars of Liberty as a
/// self-contained mod (TAD assets + DLLs are copied alongside WoL's data).
///
/// Wars of Liberty needs the TAD expansion specifically — not vanilla AoE3,
/// not Definitive Edition. We verify by checking for:
///   - bin\age3y.exe        (the TAD engine binary; "y" = Asian Dynasties)
///   - data\protoy.xml      (TAD-specific game data)
///   - bin\RockallDLL.dll   (one of the engine DLLs WoL needs at runtime)
/// </summary>
public static class Aoe3DetectorService
{
    public enum InstallSource
    {
        Steam,
        Gog,
        MicrosoftGames,
        Manual,
    }

    public record Aoe3Install(InstallSource Source, string Path, long ApproximateSizeBytes);

    /// <summary>
    /// Returns every detected valid AoE3:TAD install on the machine, ordered
    /// by source preference (Steam → GOG → retail).
    /// </summary>
    public static List<Aoe3Install> Detect()
    {
        var results = new List<Aoe3Install>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void TryAdd(InstallSource source, string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            var normalized = path.TrimEnd('\\', '/');
            if (!seenPaths.Add(normalized)) return;
            if (!IsValidAoe3Install(normalized)) return;

            long size = TryGetFolderSize(normalized);
            results.Add(new Aoe3Install(source, normalized, size));
        }

        // Steam
        foreach (var p in DetectSteam())
            TryAdd(InstallSource.Steam, p);

        // GOG
        foreach (var p in DetectGog())
            TryAdd(InstallSource.Gog, p);

        // Microsoft Games (retail)
        foreach (var p in DetectMicrosoftGames())
            TryAdd(InstallSource.MicrosoftGames, p);

        return results;
    }

    /// <summary>
    /// Checks whether <paramref name="path"/> is a valid AoE3:TAD install root.
    /// We test for engine binary + Asian Dynasties data + an engine DLL so we
    /// don't accidentally match vanilla AoE3 or AoE3:DE.
    /// </summary>
    public static bool IsValidAoe3Install(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;

        var requiredFiles = new[]
        {
            Path.Combine(path, "bin", "age3y.exe"),
            Path.Combine(path, "bin", "RockallDLL.dll"),
            Path.Combine(path, "data", "protoy.xml"),
        };

        return requiredFiles.All(File.Exists);
    }

    // ---- Steam detection ----

    private static IEnumerable<string> DetectSteam()
    {
        // Steam stores its install path in HKLM\Software\WOW6432Node\Valve\Steam
        var steamPaths = new[]
        {
            ReadRegistryString(RegistryHive.LocalMachine, RegistryView.Registry32,
                @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
            ReadRegistryString(RegistryHive.LocalMachine, RegistryView.Registry64,
                @"SOFTWARE\Valve\Steam", "InstallPath"),
            ReadRegistryString(RegistryHive.CurrentUser, RegistryView.Default,
                @"Software\Valve\Steam", "SteamPath"),
        };

        foreach (var steamRoot in steamPaths.Where(p => !string.IsNullOrEmpty(p)).Distinct())
        {
            // Steam keeps a list of additional library folders in
            // steamapps\libraryfolders.vdf. We parse it (simple key/value format)
            // and check each library for AoE3.
            foreach (var library in EnumerateSteamLibraries(steamRoot!))
            {
                var candidate = Path.Combine(library, "steamapps", "common", "Age Of Empires 3");
                if (Directory.Exists(candidate)) yield return candidate;

                // Steam sometimes uses different capitalizations
                candidate = Path.Combine(library, "steamapps", "common", "Age of Empires 3");
                if (Directory.Exists(candidate)) yield return candidate;
            }
        }
    }

    private static IEnumerable<string> EnumerateSteamLibraries(string steamRoot)
    {
        // The main Steam library is the install folder itself
        yield return steamRoot;

        // Extra libraries are listed in steamapps\libraryfolders.vdf
        var vdfPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdfPath)) yield break;

        string content;
        try { content = File.ReadAllText(vdfPath); }
        catch { yield break; }

        // Parse "path" entries from VDF. Format examples:
        //   "path"		"D:\\SteamLibrary"
        //   "path"     "E:\\Games\\Steam"
        // A regex is more than enough — we don't need a full VDF parser.
        var matches = System.Text.RegularExpressions.Regex.Matches(
            content, "\"path\"\\s*\"([^\"]+)\"");

        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            // VDF escapes backslashes as "\\" — unescape them
            var path = m.Groups[1].Value.Replace("\\\\", "\\");
            if (Directory.Exists(path))
                yield return path;
        }
    }

    // ---- GOG detection ----

    private static IEnumerable<string> DetectGog()
    {
        // GOG registers each game under HKLM\Software\GOG.com\Games\<gameId>
        // AoE3 Complete Collection has appeared under multiple IDs over the years.
        var gogRootKey = @"SOFTWARE\WOW6432Node\GOG.com\Games";
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            using var games = hklm.OpenSubKey(gogRootKey);
            if (games == null) yield break;

            foreach (var subKeyName in games.GetSubKeyNames())
            {
                using var sub = games.OpenSubKey(subKeyName);
                var name = sub?.GetValue("gameName") as string ?? "";
                var path = sub?.GetValue("path") as string ?? sub?.GetValue("PATH") as string ?? "";
                if (string.IsNullOrEmpty(path)) continue;

                // Match any GOG entry whose name mentions Age of Empires 3 / III
                if (name.Contains("Age of Empires", StringComparison.OrdinalIgnoreCase) &&
                    (name.Contains("III", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("3", StringComparison.OrdinalIgnoreCase)))
                {
                    yield return path;
                }
            }
        }
        finally { /* yield-friendly enumeration */ }
    }

    // ---- Microsoft Games / retail detection ----

    private static IEnumerable<string> DetectMicrosoftGames()
    {
        // Common retail install locations
        var common = new[]
        {
            @"C:\Program Files (x86)\Microsoft Games\Age of Empires III",
            @"C:\Program Files\Microsoft Games\Age of Empires III",
        };

        foreach (var p in common)
            if (Directory.Exists(p))
                yield return p;

        // Also try the registry — the retail AoE3 installer registers under
        // HKLM\Software\Microsoft\Microsoft Games\Age of Empires 3
        var keyPaths = new[]
        {
            @"SOFTWARE\WOW6432Node\Microsoft\Microsoft Games\Age of Empires 3",
            @"SOFTWARE\Microsoft\Microsoft Games\Age of Empires 3",
        };
        foreach (var key in keyPaths)
        {
            var path = ReadRegistryString(RegistryHive.LocalMachine, RegistryView.Default, key, "EXE Path");
            if (!string.IsNullOrEmpty(path))
            {
                // The "EXE Path" value points to the bin folder; the install
                // root is one level up.
                var root = Path.GetDirectoryName(path.TrimEnd('\\', '/'));
                if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
                    yield return root;
            }
        }
    }

    // ---- Utilities ----

    private static string? ReadRegistryString(
        RegistryHive hive, RegistryView view, string keyPath, string valueName)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(keyPath);
            return key?.GetValue(valueName) as string;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Sums the size of all files in a folder. Used to display "AoE3 install
    /// (5.2 GB)" in the picker UI. Best-effort — failures return 0.
    /// </summary>
    public static long TryGetFolderSize(string path)
    {
        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Sum(f =>
                {
                    try { return new FileInfo(f).Length; }
                    catch { return 0L; }
                });
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Returns available free space on the drive containing <paramref name="path"/>.
    /// Used to verify the destination has enough room before starting a copy.
    /// </summary>
    public static long GetFreeSpace(string path)
    {
        try
        {
            // The path may not exist yet — walk up to the first existing parent
            var dir = path;
            while (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                var parent = Path.GetDirectoryName(dir);
                if (parent == dir) break;
                dir = parent ?? "";
            }
            if (string.IsNullOrEmpty(dir)) return 0;

            var root = Path.GetPathRoot(Path.GetFullPath(dir));
            if (string.IsNullOrEmpty(root)) return 0;
            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch
        {
            return 0;
        }
    }
}
