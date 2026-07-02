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
    public static string? Find(LauncherConfig config, string? modInstallPath, ModProfile profile,
        bool trustConfigCache = true)
    {
        var exeName = string.IsNullOrEmpty(profile.GameExecutable)
            ? "age3y.exe"
            : profile.GameExecutable;

        var checkedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in EnumerateCandidates(config, modInstallPath, exeName, trustConfigCache))
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

    /// <summary>Lazy enumeration of likely paths, in priority order.</summary>
    private static IEnumerable<string> EnumerateCandidates(
        LauncherConfig config,
        string? modInstallPath,
        string exeName,
        bool trustConfigCache = true)
    {
        // 1. Cached path from config (set after a successful previous launch).
        //    Only used when its filename matches the active profile's exe — a
        //    cached age3y.exe is no good for IM, and vice versa.
        //    The multiplayer launch passes trustConfigCache=false and SKIPS
        //    this: a room can use a DIFFERENT mod than whatever's active on the
        //    dashboard, and since both AoE3 (aoe3-tad) and WoL ship age3y.exe,
        //    the active mod's cached path would satisfy the filename match and
        //    open the WRONG game (host a WoL room while AoE3 is active → it
        //    launched AoE3). MP resolves purely from the room mod's folder.
        if (trustConfigCache
            && !string.IsNullOrWhiteSpace(config.GameExecutable)
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

        // Launch DETACHED (re-parented under explorer.exe) so a forced Task Manager
        // "End task" on the launcher doesn't cascade-kill the game. Falls back to a
        // plain launch if re-parenting isn't available — the game must always start.
        int pid = DetachedProcessLauncher.StartReparented(
            exePath, arguments, Path.GetDirectoryName(exePath));
        if (pid > 0)
        {
            DiagnosticLog.Write($"Game launched detached (pid {pid}).");
            return;
        }

        DiagnosticLog.Write("Detached launch unavailable; falling back to Process.Start.");
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
    /// <c>+noIntroCinematics +disableESOProfile +dontDetectNAT</c>.
    /// The lowercase tokens we initially tried (<c>+nointro</c>,
    /// <c>+mp</c>, <c>+hostmpgame</c>, <c>+joinIPaddr</c>) are NOT in
    /// age3y.exe, so they no-op silently — be careful what you add.
    /// </summary>
    public static Process? LaunchAndWatch(
        LauncherConfig config,
        string? modInstallPath,
        ModProfile profile,
        EventHandler onExited,
        string? extraArgs = null,
        bool trustConfigCache = true)
    {
        var exePath = Find(config, modInstallPath, profile, trustConfigCache);
        if (exePath == null)
        {
            throw new FileNotFoundException(
                WarsOfLibertyLauncher.Localization.Strings.Format(
                    "ErrGameExeNotFound", profile.DisplayName));
        }

        // Only persist the resolved path to the SHARED cache when we trust it.
        // The multiplayer launch (trustConfigCache=false) resolves a room mod
        // that may differ from the active dashboard mod; writing its exe back
        // would make the dashboard's PLAY open the wrong game next time (the
        // reverse of the bug this flag fixes).
        if (trustConfigCache && config.GameExecutable != exePath)
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

        // Previous launcher versions wrapped this in a DLL-injection
        // path (AoeP2pHookInjector) that pre-loaded AoeP2pHook.dll into
        // age3y.exe to detour ws2_32 and rewrite LAN traffic on the
        // fly. That approach didn't survive contact with AoE3's host
        // discovery (which reads a single probe and stops polling the
        // socket forever), so we now ship a virtual NIC instead: an
        // n2n edge bound to 10.99.0.X presents the room as a real LAN
        // and AoE3's stock multiplayer code path takes care of itself.
        // The launch path is back to a plain Process.Start.

        DiagnosticLog.Write(
            $"Launching game (watched): {exePath} (profile '{profile.Id}') args='{arguments}'");

        // Launch DETACHED (re-parented under explorer.exe) so a forced Task Manager
        // "End task" on the launcher doesn't cascade-kill the game mid-match. We still
        // get the Exited callback (and a Process handle for the cancel/leave
        // tree-kills) by opening the new pid — Process.Exited works for any process we
        // hold a handle to, not only ones we started as a child. Falls back to the
        // original watched child launch if re-parenting isn't available.
        int pid = DetachedProcessLauncher.StartReparented(
            exePath, arguments, Path.GetDirectoryName(exePath));
        if (pid > 0)
        {
            try
            {
                var watched = Process.GetProcessById(pid);
                watched.EnableRaisingEvents = true;
                watched.Exited += onExited;
                DiagnosticLog.Write($"Game launched detached + watched (pid {pid}).");
                return watched;
            }
            catch (Exception ex)
            {
                // The game IS running (detached); we just couldn't attach the watcher
                // (rare race if it exited instantly). Don't fall through to a second
                // launch — return null so the caller degrades gracefully (no exit
                // callback) instead of spawning a duplicate game.
                DiagnosticLog.Write(
                    $"Game launched detached (pid {pid}) but watcher attach failed: {ex.Message}");
                return null;
            }
        }

        DiagnosticLog.Write("Detached launch unavailable; falling back to watched child launch.");
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
