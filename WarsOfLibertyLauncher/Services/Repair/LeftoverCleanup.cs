using System.IO;
using System.Threading;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services.Repair;

/// <summary>Why an install cannot have its leftovers removed at all.</summary>
internal enum LeftoverRefusal
{
    None,
    StockGame,
    /// <summary>An <c>InPlaceOverlay</c> install folder IS the player's own game.</summary>
    NotIsolated,
    NoManifest,
    ForeignManifest,
    /// <summary>The manifest records no overlay files, so what the mod ships cannot be told apart.</summary>
    NoShippedList,
    /// <summary>The install IS, or holds, a real AoE3 folder.</summary>
    Aoe3Folder,
}

internal sealed record LeftoverItem(string Path, bool Removed, string? Error = null);

/// <summary>
/// Removes, from an install ALREADY made, the base-game files the install pipeline itself removes
/// at every overlay: <see cref="ModProfile.CloneFilesRemovedByPatches"/> and, for a mod that opts
/// in, the compiled <c>.XMB</c> its own <c>.xml</c> supersedes (<see cref="ModProfile.SupersedeCompiledXml"/>).
///
/// <para><b>Why.</b> Those rules run only when the overlay is laid, so an install made before them
/// keeps the files for ever: Age of Empires III reads the compiled <c>.XMB</c> over the mod's
/// <c>.xml</c>, and the mod runs with the player's own string table and proto/techtree on top of it.
/// A Repair of an otherwise intact install downloaded nothing and changed nothing, so the only
/// answer was a reinstall. This is the same selection, applied to what is on disk.</para>
///
/// <para><b>The rules are the install's rules, never wider.</b> The selection is
/// <see cref="NativeInstallService.SelectCloneFilesToRemove"/> and
/// <see cref="NativeInstallService.SelectSupersededCompiledXml"/> over the manifest's record of what
/// the payload shipped, so a file the mod ships is never touched; an addon-owned file is never
/// touched; only an isolated clone of THIS mod qualifies, never a folder that is or holds the
/// player's real game. Every removed file is MOVED to a backup, not deleted, and named in the log.</para>
/// </summary>
internal static class LeftoverCleanup
{
    private const int KeepBackupSets = 5;

    internal static LeftoverRefusal Eligibility(
        ModProfile profile, string installPath, InstallManifest? manifest, IReadOnlyCollection<string> aoe3Roots)
    {
        if (profile.IsStockGame) return LeftoverRefusal.StockGame;
        if (profile.InstallType != ModInstallType.IsolatedFolder) return LeftoverRefusal.NotIsolated;
        if (manifest == null) return LeftoverRefusal.NoManifest;
        if (!InstallIdentity.BelongsTo(manifest, profile)) return LeftoverRefusal.ForeignManifest;
        if (manifest.OverlayFiles.Count == 0 && manifest.FileHashes.Count == 0) return LeftoverRefusal.NoShippedList;

        var install = NormalizeDir(installPath);
        foreach (var root in aoe3Roots.Where(r => !string.IsNullOrWhiteSpace(r)))
        {
            var r = NormalizeDir(root);
            // The install IS a real AoE3 folder, or HOLDS one. An install merely INSIDE one is the
            // normal Steam layout (…\Age Of Empires 3\Wars of Liberty) and is a clone of its own:
            // every path written is install + a manifest key, so it can never reach the game's files.
            if (string.Equals(r, install, StringComparison.OrdinalIgnoreCase) || IsUnder(install, r))
                return LeftoverRefusal.Aoe3Folder;
        }
        return LeftoverRefusal.None;
    }

    /// <summary>
    /// Which leftovers are on disk, as forward-slash relative paths, sorted. Pure over its inputs;
    /// empty when the profile declares nothing to remove or the manifest cannot say what shipped.
    /// </summary>
    internal static IReadOnlyList<string> Select(
        ModProfile profile, InstallManifest manifest, IEnumerable<string>? addonOwned, Func<string, bool> exists)
    {
        bool patches = profile.CloneFilesRemovedByPatches is { Length: > 0 };
        bool superseded = profile.SupersedeCompiledXml;
        if (!patches && !superseded) return Array.Empty<string>();
        if (profile.InstallType != ModInstallType.IsolatedFolder) return Array.Empty<string>();

        var shipped = manifest.OverlayFiles.Concat(manifest.FileHashes.Keys)
            .Select(Normalize)
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (shipped.Count == 0) return Array.Empty<string>();

        var owned = new HashSet<string>((addonOwned ?? Enumerable.Empty<string>()).Select(Normalize),
            StringComparer.OrdinalIgnoreCase);

        var take = new SortedSet<string>(StringComparer.Ordinal);
        if (patches)
            foreach (var rel in NativeInstallService.SelectCloneFilesToRemove(
                         profile.CloneFilesRemovedByPatches, shipped, rel => exists(Normalize(rel))))
                take.Add(Normalize(rel));
        if (superseded)
            foreach (var rel in NativeInstallService.SelectSupersededCompiledXml(shipped, rel => exists(Normalize(rel))))
                take.Add(Normalize(rel));

        take.RemoveWhere(owned.Contains);
        return take.ToList();
    }

    /// <summary>
    /// Moves each selected file into <paramref name="backupRoot"/> (a fresh folder per run, the
    /// newest <see cref="KeepBackupSets"/> kept beside it). A file that cannot be moved stays put
    /// and is reported, never retried as a delete.
    /// </summary>
    internal static IReadOnlyList<LeftoverItem> Remove(
        string installPath, IReadOnlyList<string> selected, string backupRoot, CancellationToken ct = default)
    {
        var results = new List<LeftoverItem>();
        var root = NormalizeDir(installPath);
        foreach (var rel in selected)
        {
            ct.ThrowIfCancellationRequested();
            var full = Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsUnder(root, full))
            {
                results.Add(new LeftoverItem(rel, false, "outside the install"));
                continue;
            }
            try
            {
                if (!File.Exists(full)) continue;
                var backup = Path.Combine(backupRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Move(full, backup, overwrite: false);
                results.Add(new LeftoverItem(rel, true));
            }
            catch (Exception ex)
            {
                results.Add(new LeftoverItem(rel, false, ex.Message));
            }
        }
        PruneBackups(Path.GetDirectoryName(backupRoot));
        return results;
    }

    private static void PruneBackups(string? installBackupFolder)
    {
        try
        {
            if (string.IsNullOrEmpty(installBackupFolder) || !Directory.Exists(installBackupFolder)) return;
            foreach (var old in new DirectoryInfo(installBackupFolder).GetDirectories()
                         .OrderByDescending(d => d.Name, StringComparer.Ordinal).Skip(KeepBackupSets))
                old.Delete(recursive: true);
        }
        catch { }
    }

    private static string Normalize(string rel) => (rel ?? "").Trim().Replace('\\', '/').TrimStart('/');

    private static string NormalizeDir(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool IsUnder(string dir, string path)
    {
        var d = NormalizeDir(dir) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(d, StringComparison.OrdinalIgnoreCase);
    }
}
