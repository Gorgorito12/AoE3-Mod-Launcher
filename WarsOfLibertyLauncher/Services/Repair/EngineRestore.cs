using System.IO;
using System.Threading;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services.Repair;

/// <summary>What happened to one damaged engine file.</summary>
internal enum EngineRestoreResult
{
    Restored,
    /// <summary>Between the diagnosis and the swap the file became right again: left alone.</summary>
    AlreadyGood,
    /// <summary>No AoE3 folder holds a byte-identical copy (or the file must never be restored).</summary>
    NoSource,
    /// <summary>The damaged file is held open by another program.</summary>
    InUse,
    Failed,
}

internal sealed record EngineRestoreItem(string Path, EngineRestoreResult Result, string? Source = null);

/// <summary>Why an install cannot have its engine restored at all.</summary>
internal enum EngineRestoreRefusal
{
    None,
    /// <summary>Only a launcher-made isolated clone owns its engine bytes.</summary>
    NotIsolated,
    StockGame,
    NoManifest,
    ForeignManifest,
    /// <summary>An old manifest recorded no engine fingerprints: nothing to prove a copy against.</summary>
    NoFingerprints,
    /// <summary>The install IS, or holds, a real AoE3 folder — never write there.</summary>
    Aoe3Folder,
}

/// <summary>
/// Restores damaged engine files (<see cref="InstallManifest.EngineFileHashes"/>) of an isolated
/// clone from the player's OWN Age of Empires III — the folder the clone was copied from — and
/// only with a byte-identical copy.
///
/// <para><b>Why.</b> Engine files are not in the mod's payload, so a re-lay cannot fix them and the
/// only answer used to be "reinstall the mod" — a fresh clone plus a multi-GB download to replace a
/// one-megabyte DLL. The clone copied these very bytes from the player's game, so the player's game
/// still has them.</para>
///
/// <para><b>The rules are all refusals.</b> A source is used only when its size AND SHA-256 equal
/// the fingerprint recorded when the clone was made (checked again on the STAGED copy, so a file
/// that changed underneath cannot slip through). A candidate inside ANY registered install of ANY
/// mod, equal to the destination, or in a redirect's "(AoE3 vanilla)" aside is never a source. Only
/// keys already in the engine map are written — never a file the mod ships, a translation covers,
/// an addon owns, or a patch removed. The destination must be a launcher clone: an install that is,
/// holds or sits inside a real AoE3 folder is refused, because there the "engine" is the player's
/// own game. Every replaced file is backed up first.</para>
///
/// <para>It writes no manifest, stamps no version, re-applies no addon and refreshes no snapshot:
/// the bytes it puts back are exactly the ones the manifest already describes.</para>
/// </summary>
internal static class EngineRestore
{
    internal const string StagingSuffix = ".aoe3ml-new";
    private const string AsideMarker = "(AoE3 vanilla)";
    private const int KeepBackupSets = 5;

    internal static EngineRestoreRefusal Eligibility(
        ModProfile profile, string installPath, InstallManifest? manifest, IReadOnlyCollection<string> aoe3Roots)
    {
        if (profile.IsStockGame) return EngineRestoreRefusal.StockGame;
        if (profile.InstallType != ModInstallType.IsolatedFolder) return EngineRestoreRefusal.NotIsolated;
        if (manifest == null) return EngineRestoreRefusal.NoManifest;
        if (!InstallIdentity.BelongsTo(manifest, profile)) return EngineRestoreRefusal.ForeignManifest;
        if (manifest.EngineFileHashes.Count == 0) return EngineRestoreRefusal.NoFingerprints;

        var install = NormalizeDir(installPath);
        foreach (var root in aoe3Roots.Where(r => !string.IsNullOrWhiteSpace(r)))
        {
            var r = NormalizeDir(root);
            // The install IS a real AoE3 folder, or HOLDS one. An install merely INSIDE one is the
            // normal Steam layout (…\Age Of Empires 3\Wars of Liberty) and is a clone of its own:
            // every path written is install + a manifest key, so it can never reach the game's files.
            if (string.Equals(r, install, StringComparison.OrdinalIgnoreCase) || IsUnder(install, r))
                return EngineRestoreRefusal.Aoe3Folder;
        }
        return EngineRestoreRefusal.None;
    }

    /// <summary>
    /// The files no restore may ever write, from profile and manifest data: what the mod ships,
    /// what a translation covers, what a patch removed, and what an addon owns.
    /// </summary>
    internal static HashSet<string> NeverRestore(ModProfile profile, InstallManifest manifest, IEnumerable<string>? addonOwned)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in manifest.OverlayFiles) set.Add(Normalize(p));
        foreach (var p in manifest.FileHashes.Keys) set.Add(Normalize(p));
        if (profile.Translations?.CoveredFiles != null)
            foreach (var p in profile.Translations.CoveredFiles) set.Add(Normalize(p));
        foreach (var p in profile.CloneFilesRemovedByPatches) set.Add(Normalize(p));
        if (addonOwned != null)
            foreach (var p in addonOwned) set.Add(Normalize(p));
        return set;
    }

    /// <summary>
    /// The first byte-identical copy of <paramref name="rel"/> under <paramref name="sourceRoots"/>
    /// (each root tried flat, then under <c>bin\</c> — the clone flattened <c>bin\</c> into its
    /// root), or null.
    /// </summary>
    internal static string? FindSource(
        string rel, FileFingerprint expected, string destination,
        IReadOnlyList<string> sourceRoots, IReadOnlyCollection<string> registeredInstalls)
    {
        var relOs = rel.Replace('/', Path.DirectorySeparatorChar);
        var dest = NormalizeFile(destination);
        foreach (var root in sourceRoots.Where(r => !string.IsNullOrWhiteSpace(r)))
        {
            foreach (var candidate in new[] { Path.Combine(root, relOs), Path.Combine(root, "bin", relOs) })
            {
                string full;
                try { full = NormalizeFile(candidate); } catch { continue; }
                if (!File.Exists(full)) continue;
                if (string.Equals(full, dest, StringComparison.OrdinalIgnoreCase)) continue;
                if (full.Contains(AsideMarker, StringComparison.OrdinalIgnoreCase)) continue;
                if (registeredInstalls.Any(i => !string.IsNullOrWhiteSpace(i) && IsUnder(NormalizeDir(i), full)))
                    continue;
                try
                {
                    if (new FileInfo(full).Length != expected.Size) continue;
                    if (Matches(VerifyService.ComputeFingerprintOf(full), expected)) return full;
                }
                catch { }
            }
        }
        return null;
    }

    /// <summary>
    /// Restores each of <paramref name="damaged"/> it can prove, one file at a time: a failure
    /// touches only that file. Originals go to <paramref name="backupRoot"/> first.
    /// </summary>
    internal static IReadOnlyList<EngineRestoreItem> Restore(
        string installPath,
        InstallManifest manifest,
        IEnumerable<string> damaged,
        ISet<string> neverRestore,
        IReadOnlyList<string> sourceRoots,
        IReadOnlyCollection<string> registeredInstalls,
        string backupRoot,
        CancellationToken ct = default)
    {
        var results = new List<EngineRestoreItem>();
        foreach (var raw in damaged.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            var rel = Normalize(raw);
            var expected = manifest.EngineFileHashes
                .FirstOrDefault(kv => string.Equals(Normalize(kv.Key), rel, StringComparison.OrdinalIgnoreCase)).Value;
            if (expected == null || neverRestore.Contains(rel))
            {
                results.Add(new EngineRestoreItem(rel, EngineRestoreResult.NoSource));
                continue;
            }

            var dest = Path.Combine(installPath, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!IsUnder(NormalizeDir(installPath), NormalizeFile(dest)))
            {
                results.Add(new EngineRestoreItem(rel, EngineRestoreResult.NoSource));
                continue;
            }
            results.Add(RestoreOne(rel, dest, expected, sourceRoots, registeredInstalls, backupRoot));
        }
        PruneBackups(Path.GetDirectoryName(backupRoot));
        return results;
    }

    private static EngineRestoreItem RestoreOne(
        string rel, string dest, FileFingerprint expected,
        IReadOnlyList<string> sourceRoots, IReadOnlyCollection<string> registeredInstalls, string backupRoot)
    {
        var source = FindSource(rel, expected, dest, sourceRoots, registeredInstalls);
        if (source == null) return new EngineRestoreItem(rel, EngineRestoreResult.NoSource);

        var staged = dest + StagingSuffix;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(source, staged, overwrite: true);
            // Proven on the bytes about to be moved, not only on the source a moment ago.
            if (!Matches(VerifyService.ComputeFingerprintOf(staged), expected))
                return new EngineRestoreItem(rel, EngineRestoreResult.NoSource, source);

            if (File.Exists(dest))
            {
                try
                {
                    if (Matches(VerifyService.ComputeFingerprintOf(dest), expected))
                        return new EngineRestoreItem(rel, EngineRestoreResult.AlreadyGood, source);
                }
                catch (IOException ex) when (VerifyService.ClassifyReadFailure(ex) == VerifyService.FileProblem.Unreadable)
                {
                    return new EngineRestoreItem(rel, EngineRestoreResult.InUse, source);
                }
                catch { /* unreadable for another reason: it is damaged, replace it */ }

                var backup = Path.Combine(backupRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Copy(dest, backup, overwrite: true);
            }

            File.Move(staged, dest, overwrite: true);
            DiagnosticLog.Write($"Engine restore: '{rel}' restored from '{source}'.");
            return new EngineRestoreItem(rel, EngineRestoreResult.Restored, source);
        }
        catch (IOException ex) when (VerifyService.ClassifyReadFailure(ex) == VerifyService.FileProblem.Unreadable)
        {
            DiagnosticLog.Write($"Engine restore: '{rel}' is in use: {ex.Message}");
            return new EngineRestoreItem(rel, EngineRestoreResult.InUse, source);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Engine restore: '{rel}' failed: {ex.Message}");
            return new EngineRestoreItem(rel, EngineRestoreResult.Failed, source);
        }
        finally
        {
            try { if (File.Exists(staged)) File.Delete(staged); } catch { }
        }
    }

    /// <summary>Keeps the newest few backup sets of an install; older ones are dropped.</summary>
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

    private static bool Matches(FileFingerprint a, FileFingerprint b)
        => a.Size == b.Size && string.Equals(a.Sha256, b.Sha256, StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string rel) => rel.Replace('\\', '/');

    private static string NormalizeDir(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string NormalizeFile(string path) => Path.GetFullPath(path);

    /// <summary>True when <paramref name="path"/> is strictly inside <paramref name="dir"/>.</summary>
    private static bool IsUnder(string dir, string path)
        => path.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
