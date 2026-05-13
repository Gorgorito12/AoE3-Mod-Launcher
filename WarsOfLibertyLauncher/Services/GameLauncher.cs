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

        foreach (var root in AoE3Detector.EnumerateProbeRoots())
        foreach (var sub in commonSubpaths)
            yield return Path.Combine(root, sub);

        // 4. Registry-discovered installs (Steam libraries on any drive, GOG
        //    entries, Microsoft Games retail) — the cached + hardcoded passes
        //    miss these when AoE3 lives outside Program Files / Program Files
        //    (x86). Sourced from AoE3Detector so the install dialog and the
        //    runtime status / menu lookups agree on what counts as "found".
        foreach (var install in AoE3Detector.FindAll())
        {
            // age3y.exe / age3m.exe sits either next to the install root or
            // inside its bin\ subfolder (Steam layout); yield both shapes.
            yield return Path.Combine(install.ModRoot, exeName);
            yield return Path.Combine(install.ModRoot, "bin", exeName);
            yield return Path.Combine(install.GameFolder, exeName);
        }
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
                WarsOfLibertyLauncher.Localization.Strings.Format(
                    "ErrGameExeNotFound", profile.DisplayName));
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

    /// <summary>
    /// Launch the active mod's game and return the underlying
    /// <see cref="Process"/> so the caller can subscribe to
    /// <see cref="Process.Exited"/> (e.g. the multiplayer flow that
    /// wants to upload a replay once AoE3 closes).
    ///
    /// Differs from <see cref="Launch"/> only in <c>UseShellExecute=false</c>
    /// and <c>EnableRaisingEvents=true</c>; both are needed to get a
    /// usable <c>Exited</c> event back from <see cref="Process.Start"/>.
    /// Returns null if the OS couldn't spawn the process (rare; same
    /// rules as <see cref="Process.Start(ProcessStartInfo)"/>).
    ///
    /// <paramref name="extraArgs"/> is appended to the resolved
    /// command line after the profile/config args. The multiplayer
    /// flow uses it to inject AoE3's real (binary-verified) startup
    /// flags — typically
    /// <c>+noIntroCinematics +disableESOProfile +dontDetectNAT
    /// +OverrideAddress &lt;vip&gt; +OverridePort 2300 +hostPort 2300</c>.
    /// The lowercase tokens we initially tried (<c>+nointro</c>,
    /// <c>+mp</c>, <c>+hostmpgame</c>, <c>+joinIPaddr</c>) are NOT in
    /// age3y.exe, so they no-op silently — be careful what you add.
    /// </summary>
    public static Process? LaunchAndWatch(
        LauncherConfig config,
        string? modInstallPath,
        ModProfile profile,
        EventHandler onExited,
        string? extraArgs = null)
    {
        var exePath = Find(config, modInstallPath, profile);
        if (exePath == null)
        {
            throw new FileNotFoundException(
                WarsOfLibertyLauncher.Localization.Strings.Format(
                    "ErrGameExeNotFound", profile.DisplayName));
        }

        if (config.GameExecutable != exePath)
        {
            config.GameExecutable = exePath;
            config.Save();
        }

        var arguments = !string.IsNullOrWhiteSpace(profile.GameArguments)
            ? profile.GameArguments
            : config.GameArguments;
        if (!string.IsNullOrWhiteSpace(extraArgs))
        {
            arguments = string.IsNullOrWhiteSpace(arguments)
                ? extraArgs
                : $"{arguments} {extraArgs}";
        }

        DiagnosticLog.Write(
            $"Launching game (watched): {exePath} (profile '{profile.Id}') args='{arguments}'");

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments ?? "",
            WorkingDirectory = Path.GetDirectoryName(exePath),
            // Must be false so Process.Exited can fire — ShellExecute
            // returns true on launch but never raises events on the
            // returned object.
            UseShellExecute = false,
        };

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };
        process.Exited += onExited;
        if (!process.Start()) return null;
        return process;
    }
}
