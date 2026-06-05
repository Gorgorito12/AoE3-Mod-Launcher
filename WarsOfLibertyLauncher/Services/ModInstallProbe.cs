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
public static class ModInstallProbe
{
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
    /// True if <paramref name="path"/> looks like a real install of
    /// <paramref name="profile"/> by content (probe file + optional marker),
    /// regardless of the folder's name. See the type doc for the rule.
    /// </summary>
    public static bool LooksLikeModInstall(string path, ModProfile profile)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return false;

        // Probe file: the mod's own data file that lands here on install.
        if (!string.IsNullOrEmpty(profile.InstallProbeFile)
            && !File.Exists(Path.Combine(path, profile.InstallProbeFile)))
            return false;

        // Content marker: distinguishes the mod from the base game it
        // clones/overlays, when the probe file alone is ambiguous.
        if (!string.IsNullOrEmpty(profile.InstallMarker)
            && !MarkerExists(path, profile.InstallMarker))
            return false;

        return true;
    }
}
