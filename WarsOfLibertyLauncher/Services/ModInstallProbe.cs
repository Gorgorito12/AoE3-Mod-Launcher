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
    /// <summary>
    /// Every content signal is present, but the folder is still carrying the
    /// in-progress marker an install writes before it starts laying files down and
    /// removes only once the manifest is written — so this is a half-written
    /// install, not a finished one. Ranked just below <see cref="Match"/> because
    /// it is the most install-like thing that still must not be adopted.
    /// </summary>
    InstallInProgress = 4,
    /// <summary>All required signals present — a real install of this mod.</summary>
    Match = 5,
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
    /// The engine at <paramref name="path"/> or one level down in <c>bin\</c> — the two
    /// shapes an <see cref="ModInstallType.InPlaceOverlay"/> install path can take. On a
    /// Steam layout the overlay lands in <c>…\Age Of Empires 3\bin</c> (which holds the
    /// engine, <c>data\</c> and everything else), while other layouts keep <c>data\</c> at
    /// the root with the executables in <c>bin\</c>. Accepting either is what lets the
    /// engine requirement apply to overlays without guessing the layout.
    /// </summary>
    private static bool HasEngineNearby(string path) =>
        HasEngine(path) || HasEngine(Path.Combine(path, "bin"));

    /// <summary>
    /// True if <paramref name="marker"/> — a path (file or directory) relative
    /// to <paramref name="installPath"/> — exists on disk. An empty marker
    /// returns false; callers treat "no marker declared" as a separate case.
    /// </summary>
    /// <summary>
    /// File an install writes into the destination BEFORE it lays anything down and
    /// deletes only once the manifest has been written. Its presence means "a previous
    /// install died in the middle of this folder".
    ///
    /// <para><b>Why it has to exist.</b> A half-written install passes every content
    /// signal: the AoE3 clone supplies the probe file (WoL's
    /// <c>data\stringtabley.xml</c> ships in vanilla) and the engine DLLs, and the mod's
    /// marker lands early in the payload. So an interrupted install used to leave a
    /// folder that <see cref="Inspect"/> called a real install — and
    /// <see cref="UpdateService"/>'s broad fallback scan adopts the first content match
    /// it finds WITHOUT asking, which meant the launcher could silently start offering
    /// PLAY on a mod that is missing most of its files.</para>
    ///
    /// <para>Written only for an install into a folder that has no manifest yet — a
    /// reinstall over a working install must not be able to strand it behind this
    /// marker if it fails. Kept out of the manifest by deleting it before
    /// <c>WriteManifest</c> enumerates the folder.</para>
    /// </summary>
    public const string InstallInProgressMarker = ".aoe3ml-install-in-progress";

    /// <summary>True when an interrupted install left its in-progress marker behind.</summary>
    public static bool InstallIsInProgress(string path) =>
        !string.IsNullOrEmpty(path) && File.Exists(Path.Combine(path, InstallInProgressMarker));

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

        // Engine: EVERY install type sits with the base game, so a folder holding the
        // probe file and nothing else is only the mod's overlay (a leftover manual
        // download), NOT an install — adopting it makes the launcher offer a bogus
        // "update", show PLAY, and launch an executable that dies on the spot for want of
        // an engine to load.
        //
        // The two types differ only in WHERE the engine is allowed to be. An
        // IsolatedFolder install is a full AoE3 clone with bin\ flattened into the root,
        // so it must be at the root exactly. An InPlaceOverlay install path is the base
        // game's own folder, whose shape depends on the layout — hence HasEngineNearby.
        // The overlay case used to be exempt entirely, on the reasoning that the engine
        // "lives in bin\, not the install root"; that was true only while overlays were
        // installed to the AoE3 ROOT. They now go to the folder the engine reads from, so
        // the exemption stopped protecting anything and started hiding the exact failure
        // it was written next to: an engine-less Napoleonic Era folder read as installed.
        bool engineOk = profile.InstallType == ModInstallType.IsolatedFolder
            ? HasEngine(path)
            : HasEngineNearby(path);
        if (!engineOk) return ProbeOutcome.EngineMissing;

        // Last, because it is the only check that can reject a folder holding every
        // other signal: an install that died mid-write looks complete to all of them.
        if (InstallIsInProgress(path)) return ProbeOutcome.InstallInProgress;

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
