using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>Everything <see cref="InstallFolderSuggestion.Suggest"/> needs, gathered by the caller
/// so the decision itself touches no disk and no registry.</summary>
/// <param name="InPlaceRoot">The AoE3 folder the engine reads (the one holding <c>data\</c>);
/// only looked at for an in-place overlay.</param>
/// <param name="LastInstallPath">Where this mod was installed last time (per-mod state).</param>
/// <param name="RegisteredInstallPaths">Every install of this mod the launcher knows, for
/// numbering an additional copy.</param>
internal sealed record SuggestionInputs(
    ModProfile Profile,
    bool AddNewSlot,
    string? Aoe3SourcePath,
    string? InPlaceRoot,
    string? LastInstallPath,
    IReadOnlyCollection<string> RegisteredInstallPaths,
    string ProgramFilesX86);

/// <summary>
/// The folder the install dialog opens with. It used to live inline in
/// <c>MainWindow.InstallAsync</c>, built straight from the display name — so a name with a colon
/// produced a folder Windows cannot create, and two of its fallbacks returned a RELATIVE path
/// that resolved against whatever the process's current folder happened to be.
///
/// <para>Every name that reaches a path goes through <see cref="SafeFolderName"/>, and nothing
/// here returns a relative path. A saved path is trusted when its leaf is one of the mod's
/// <see cref="SafeFolderName.KnownLeaves"/> — the raw name AND the cleaned one, so an install
/// made under either is still recognised.</para>
/// </summary>
internal static class InstallFolderSuggestion
{
    /// <summary>
    /// The suggested destination, by install kind:
    /// <list type="bullet">
    ///   <item>an additional copy: a numbered, collision-free folder beside the AoE3 clone source;</item>
    ///   <item>an in-place overlay: the folder the engine reads;</item>
    ///   <item>an isolated folder: last time's folder if it still looks like this mod's and is
    ///   still valid, else <c>&lt;AoE3&gt;\&lt;clean name&gt;</c>.</item>
    /// </list>
    /// With no AoE3 detected, the catalog's <c>defaultFolder</c> when it is a valid full path, else
    /// <c>Program Files (x86)\&lt;clean name&gt;</c> — the convention the catalog itself follows.
    /// </summary>
    internal static string Suggest(SuggestionInputs inputs, Func<string, bool> directoryExists,
        IReadOnlyCollection<string>? driveRoots)
    {
        var profile = inputs.Profile;
        var name = SafeFolderName.ForProfile(profile);
        var aoe3 = string.IsNullOrWhiteSpace(inputs.Aoe3SourcePath) ? null : inputs.Aoe3SourcePath;
        var fallback = UsableDefaultFolder(profile, driveRoots) ?? Path.Combine(inputs.ProgramFilesX86, name);

        // An ADDITIONAL copy is always an isolated clone in a NEW folder — even for an in-place
        // overlay, since two overlays can't share one AoE3 folder.
        if (inputs.AddNewSlot)
            return MakeUnique(aoe3 != null ? Path.Combine(aoe3, name) : fallback,
                inputs.RegisteredInstallPaths, directoryExists);

        // An in-place overlay goes INTO the game the engine reads: the folder holding data\. A
        // game at the root of a drive can arrive as "E:" (detection trims the separator), which is
        // drive-RELATIVE when used as a folder — so it goes back to "E:\", which the dialog then
        // refuses with the real reason (a drive root) instead of asking for a whole path.
        if (profile.InstallType == ModInstallType.InPlaceOverlay)
        {
            var game = FirstNonEmpty(inputs.InPlaceRoot, aoe3);
            return game != null ? WholeRoot(game.Trim()) : fallback;
        }

        // Only trust last time's folder if its leaf looks like THIS mod's: a value saved by an
        // earlier detection that fell through to the AoE3 candidates (Steam's "…\Age Of Empires 3\bin")
        // would otherwise be suggested — and installing a clone there overwrites the player's game.
        if (IsTrustedSavedPath(inputs.LastInstallPath, profile))
        {
            var last = inputs.LastInstallPath!;
            if (InstallFolderRules.Validate(last, driveRoots).Problem == null) return last;

            // Recorded under a name Windows trims ("Mod v1." lives on disk as "Mod v1"): the
            // trimmed path is the same folder, and the one the dialog will accept.
            var onDisk = TryFullPath(last);
            if (onDisk != null && InstallFolderRules.Validate(onDisk, driveRoots).Problem == null)
                return onDisk;
        }

        // The same convention for every isolated mod: a folder named after it, beside the base
        // game it clones.
        return aoe3 != null ? Path.Combine(aoe3, name) : fallback;
    }

    /// <summary>
    /// True when <paramref name="savedPath"/>'s last folder is one of the mod's
    /// <see cref="SafeFolderName.KnownLeaves"/>: the display name as written, its cleaned form, or
    /// the catalog's <c>defaultFolder</c> leaf (Knights and Barbarians-Remastered lives in
    /// "Knights and Barbarians"). Accepting the raw name is what keeps an install made before the
    /// cleaning rule recognised.
    /// </summary>
    internal static bool IsTrustedSavedPath(string? savedPath, ModProfile profile)
    {
        if (string.IsNullOrWhiteSpace(savedPath)) return false;
        string leaf;
        try { leaf = Path.GetFileName(savedPath.TrimEnd('\\', '/')); }
        catch { return false; }
        return !string.IsNullOrEmpty(leaf)
               && SafeFolderName.KnownLeaves(profile).Contains(leaf, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <paramref name="baseFolder"/>, or the first of "<c>… (2)</c>", "<c>… (3)</c>"… that is
    /// neither a registered install of this mod nor a folder already on disk.
    /// </summary>
    internal static string MakeUnique(string baseFolder, IEnumerable<string> taken, Func<string, bool> directoryExists)
    {
        var bas = baseFolder.TrimEnd('\\', '/');
        var takenSet = new HashSet<string>(
            taken.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.TrimEnd('\\', '/')),
            StringComparer.OrdinalIgnoreCase);
        var candidate = bas;
        var n = 2;
        while (takenSet.Contains(candidate) || directoryExists(candidate))
            candidate = $"{bas} ({n++})";
        return candidate;
    }

    /// <summary>The catalog's <c>defaultFolder</c> when it is a valid FULL path, else null.
    /// It is a manifest field nobody validated before, so a relative or colon-bearing value used
    /// to go straight into the dialog.</summary>
    internal static string? UsableDefaultFolder(ModProfile profile, IReadOnlyCollection<string>? driveRoots)
    {
        if (string.IsNullOrWhiteSpace(profile.DefaultInstallFolder)) return null;
        var check = InstallFolderRules.Validate(profile.DefaultInstallFolder, driveRoots);
        return check.Problem == null ? check.FullPath : null;
    }

    /// <summary>
    /// What the dialog's folder picker should put in the box: the picked folder when it already IS
    /// this mod's folder (its leaf is one of <paramref name="knownLeaves"/>), else a folder named
    /// after the mod inside it. A picked drive root stays a root (<c>D:\</c>, never <c>D:</c>).
    /// </summary>
    internal static string WithModFolder(string picked, string modFolderName, IEnumerable<string> knownLeaves)
    {
        var p = (picked ?? "").Trim();
        if (p.Length == 0) return p;
        p = Path.TrimEndingDirectorySeparator(p);
        var leaf = Path.GetFileName(p);
        if (!string.IsNullOrEmpty(leaf) && knownLeaves.Contains(leaf, StringComparer.OrdinalIgnoreCase))
            return p;
        return Path.Combine(p, modFolderName);
    }

    /// <summary>Program Files (x86), else Program Files, else the usual spelling.</summary>
    internal static string ProgramFilesX86()
    {
        var x86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrEmpty(x86)) return x86;
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        return !string.IsNullOrEmpty(programFiles) ? programFiles : @"C:\Program Files (x86)";
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    /// <summary>"E:" becomes "E:\"; anything else is returned as it is.</summary>
    private static string WholeRoot(string path) =>
        path.Length == 2 && path[1] == ':' && char.IsAsciiLetter(path[0]) ? path + "\\" : path;

    private static string? TryFullPath(string path)
    {
        try
        {
            return Path.IsPathFullyQualified(path)
                ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))
                : null;
        }
        catch { return null; }
    }
}
