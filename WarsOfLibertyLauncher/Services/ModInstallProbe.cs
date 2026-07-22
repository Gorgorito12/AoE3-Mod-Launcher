using System.IO;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Decides whether a folder on disk is a real install of a given mod — purely
/// by CONTENT, never by folder name. Single source of truth shared by the
/// install-state fast-path and full re-detection (<see cref="UpdateService"/>),
/// the mod-selector tiles and the manual folder picker (<c>MainWindow</c>), so
/// every surface agrees on "is this mod installed here".
///
/// Detection rule (no folder-name check, on purpose):
///   1. The folder exists, and — if the profile declares one — its
///      <see cref="ModProfile.InstallProbeFile"/> is present (the mod's own
///      data file lands there on install).
///   2. If the profile declares an <see cref="ModProfile.InstallMarker"/> (a
///      file/dir unique to the mod, absent from the base game it clones or
///      overlays) it must be present too. This is what tells a real mod folder
///      apart from the base game whose files can satisfy an ambiguous probe
///      (WoL's <c>data\stringtabley.xml</c> ships in vanilla AoE3 as well).
///
/// A mod is therefore recognised in a folder with ANY name. Mods whose probe
/// file is already exclusive to them (e.g. an overlay mod's own .exe) simply
/// leave the marker empty. This replaces the old "leaf folder name must equal
/// the mod's DisplayName" heuristic, which broke the moment a user renamed the
/// install folder.
/// </summary>
/// <summary>
/// Which content signal (if any) a candidate folder is missing, so callers can
/// log the exact reason a folder was rejected and craft a specific user message
/// (e.g. "missing the WoL marker" vs "not a mod folder at all"). Ordered from
/// least to most "install-like" so a caller comparing candidates can pick the
/// most informative outcome: a folder that has the probe but lacks the marker
/// (<see cref="MarkerMissing"/>) is closer to a real install than one missing
/// the probe entirely.
/// </summary>
public enum ProbeOutcome
{
    /// <summary>The path doesn't exist or isn't a directory.</summary>
    NotADirectory = 0,
    /// <summary>The profile's probe file isn't present under the folder.</summary>
    ProbeMissing = 1,
    /// <summary>Probe present, but the mod's content marker is absent (looks like the base game).</summary>
    MarkerMissing = 2,
    /// <summary>
    /// Probe (and marker) present, but the base-game ENGINE is missing — the
    /// folder holds only the mod's overlay, not a full <see cref="ModInstallType.IsolatedFolder"/>
    /// install. This is what a leftover manual download of a mod looks like, and
    /// adopting it would make the launcher offer a bogus "update" for a mod it
    /// never installed. More install-like than <see cref="MarkerMissing"/>: the
    /// mod's own files ARE here, just not the cloned game underneath.
    /// </summary>
    EngineMissing = 3,
    /// <summary>All required signals present — a real install of this mod.</summary>
    Match = 4,
}

public static class ModInstallProbe
{
    /// <summary>
    /// Base-game engine files an <see cref="ModInstallType.IsolatedFolder"/>
    /// install always has at its ROOT (that model clones AoE3 and flattens
    /// <c>bin\</c> into the root). Requiring at least one separates a real
    /// install from a folder that holds only the mod's overlay — the shape of a
    /// leftover manual download. These are the non-data engine files; the data
    /// version-key files are NOT used because a mod may ship its own
    /// (Napoleonic Era has <c>proton.xml</c>, not <c>protoy.xml</c>).
    /// </summary>
    private static readonly string[] EngineFiles =
    {
        "RockallDLL.dll", "binkw32.dll", "granny2.dll", "deformerdlly.dll",
    };

    private static bool HasEngine(string path)
    {
        foreach (var dll in EngineFiles)
            if (File.Exists(Path.Combine(path, dll))) return true;
        return false;
    }

    /// <summary>
    /// True if <paramref name="marker"/> — a path (file or directory) relative
    /// to <paramref name="installPath"/> — exists on disk. An empty marker
    /// returns false; callers treat "no marker declared" as a separate case.
    /// </summary>
    public static bool MarkerExists(string installPath, string marker)
    {
        if (string.IsNullOrEmpty(installPath) || string.IsNullOrEmpty(marker))
            return false;

        var full = Path.Combine(installPath, marker);
        return File.Exists(full) || Directory.Exists(full);
    }

    /// <summary>
    /// Inspect <paramref name="path"/> against <paramref name="profile"/>'s
    /// content rule (existence → probe file → marker, same order as
    /// <see cref="LooksLikeModInstall"/>) and report the FIRST check that fails,
    /// or <see cref="ProbeOutcome.Match"/> when all pass. Lets callers name the
    /// missing signal instead of a blind "invalid folder".
    /// </summary>
    public static ProbeOutcome Inspect(string path, ModProfile profile)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return ProbeOutcome.NotADirectory;

        // Probe file: the mod's own data file that lands here on install.
        if (!string.IsNullOrEmpty(profile.InstallProbeFile)
            && !File.Exists(Path.Combine(path, profile.InstallProbeFile)))
            return ProbeOutcome.ProbeMissing;

        // Content marker: distinguishes the mod from the base game it
        // clones/overlays, when the probe file alone is ambiguous.
        if (!string.IsNullOrEmpty(profile.InstallMarker)
            && !MarkerExists(path, profile.InstallMarker))
            return ProbeOutcome.MarkerMissing;

        // Engine: an IsolatedFolder install is a full AoE3 clone + overlay, so
        // the engine sits at the root. A folder with the probe but no engine is
        // only the mod's overlay (a leftover manual download), NOT an install —
        // adopting it makes the launcher offer a bogus "update" for a mod it
        // never installed. Only IsolatedFolder: an InPlaceOverlay mod's files go
        // into the base game, whose engine lives in bin\, not the install root.
        if (profile.InstallType == ModInstallType.IsolatedFolder && !HasEngine(path))
            return ProbeOutcome.EngineMissing;

        return ProbeOutcome.Match;
    }

    /// <summary>
    /// True if <paramref name="path"/> looks like a real install of
    /// <paramref name="profile"/> by content (probe file + optional marker),
    /// regardless of the folder's name. See the type doc for the rule.
    /// </summary>
    public static bool LooksLikeModInstall(string path, ModProfile profile)
        => Inspect(path, profile) == ProbeOutcome.Match;
}
