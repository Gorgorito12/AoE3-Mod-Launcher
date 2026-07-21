using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>Why an addon could not be applied.</summary>
public enum AddonApplyStatus
{
    Applied,
    /// <summary><see cref="AddonRisk"/> refused it — it writes a protected file.</summary>
    Blocked,
    /// <summary>The archive contained nothing to apply.</summary>
    Empty,
    /// <summary>The download's SHA-256 did not match the catalog's.</summary>
    HashMismatch,
    /// <summary>Another enabled addon already owns one of these files.</summary>
    Conflict,
    Failed,
}

public sealed record AddonApplyResult(
    AddonApplyStatus Status,
    IReadOnlyList<string> Files,
    IReadOnlyList<string> OffendingFiles,
    string? ConflictingAddonId = null,
    IReadOnlyList<string>? SkippedFiles = null)
{
    /// <summary>
    /// Entries deliberately not written — executables and author documentation.
    /// Surfaced by NAME rather than counted: "1 file skipped" is useless when the
    /// addon then doesn't work, while naming <c>Building Rotator.exe</c> tells the
    /// user (or its author) exactly what was left out and why.
    /// </summary>
    public IReadOnlyList<string> SkippedFiles { get; init; } =
        SkippedFiles ?? Array.Empty<string>();
}

/// <summary>
/// Applies and removes optional community addons inside one mod install.
///
/// The hard part is not copying files, it is staying compatible with the four
/// systems that already have opinions about the contents of an install:
///
///   1. <b>Verify</b> compares every overlay file against
///      <c>InstallManifest.FileHashes</c>, so an addon-modified file would be
///      reported corrupt. Solved by RE-CAPTURING the touched files' fingerprints
///      after applying (<see cref="NativeInstallService.RecaptureHashes"/>, the
///      same call the post-patch path makes): the manifest then describes what is
///      actually on disk, and verify passes without any addon special-case.
///   2. <b>Repair / update</b> re-lays the whole overlay, wiping addons. Solved
///      by <see cref="ReapplyAllAsync"/>, called from those flows' tails.
///   3. <b>Version detection and the multiplayer fingerprint</b> read three
///      specific files. Solved by <see cref="AddonRisk"/> refusing any addon that
///      writes them.
///   4. <b>Uninstall</b> deletes the install folder wholesale, so addon files and
///      their backups go with it — nothing to do.
/// </summary>
public static class AddonService
{
    /// <summary>
    /// Where pre-addon originals are kept, mirroring
    /// <c>translations\_originals\</c>. Inside the install on purpose: uninstall
    /// then reclaims them for free, and they travel with a moved install.
    /// </summary>
    public const string BackupFolderName = "addons";

    public static string BackupRootOf(string installPath) =>
        Path.Combine(installPath, BackupFolderName, "_originals");

    public static string BackupFolderOf(string installPath, string addonId) =>
        Path.Combine(BackupRootOf(installPath), Sanitize(addonId));

    /// <summary>
    /// Reads the archive's file list without extracting anything, so the risk
    /// check runs before a single byte is written into the game folder.
    /// Directory entries are dropped — they carry no content.
    /// </summary>
    public static List<string> ReadArchiveEntries(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var raw = zip.Entries
            .Where(e => !string.IsNullOrEmpty(e.Name))   // "" == directory entry
            .Select(e => e.FullName)
            .ToList();

        // Report the paths the files will actually LAND on, not the ones the
        // packager happened to nest them under, so the risk check and the extract
        // agree on what is being written.
        var prefix = AddonPaths.StripCommonRoot(raw);
        return raw
            .Select(n => AddonPaths.RemovePrefix(n, prefix))
            .Where(n => n.Length > 0)
            .ToList();
    }

    /// <summary>
    /// Applies an addon's archive to <paramref name="installPath"/>.
    ///
    /// Order is load-bearing: risk check → conflict check → BACK UP → extract →
    /// record → re-capture. Backing up before extracting is what makes disabling
    /// reversible; re-capturing last is what stops verify from calling the result
    /// corrupt.
    /// </summary>
    /// <param name="allowMultiplayerRisk">
    /// Set only after the user confirmed a <see cref="AddonRiskLevel.MultiplayerRisk"/>
    /// addon. Never bypasses <see cref="AddonRiskLevel.Blocked"/>.
    /// </param>
    /// <param name="includeOnly">
    /// When given, ONLY these install-relative entries are applied — the addon's
    /// catalog manifest declaring exactly what it touches. Real archives bundle
    /// more than the mod: the "building rotator" ships an executable, a PDF and a
    /// screenshot alongside the one config file that does the work. Declaring the
    /// list makes a catalog PR auditable, because the reviewer sees precisely
    /// which game files the addon will write before any player runs it. Null (an
    /// imported archive, which has no manifest) falls back to the automatic skip
    /// rules.
    /// </param>
    public static Task<AddonApplyResult> ApplyAsync(
        string installPath,
        string addonId,
        string zipPath,
        ModProfile profile,
        bool allowMultiplayerRisk,
        IReadOnlyList<string>? includeOnly = null,
        CancellationToken ct = default)
        => ApplyCoreAsync(installPath, addonId, new ZipAddonSource(zipPath), profile,
                          allowMultiplayerRisk, includeOnly, ct);

    /// <summary>
    /// Applies files already sitting in a folder - the shape an unpacked NSIS
    /// installer leaves behind (see <see cref="NsisExtractor"/>).
    ///
    /// Shares every rule with the archive path: same risk gate, same conflict
    /// check, same backups, same manifest re-capture. Only where the bytes come
    /// from differs, which is why the core is shared rather than copied - a second
    /// implementation would be a second place for the safety rules to drift.
    /// </summary>
    public static Task<AddonApplyResult> ApplyFromFolderAsync(
        string installPath,
        string addonId,
        string sourceDir,
        ModProfile profile,
        bool allowMultiplayerRisk,
        IReadOnlyList<string>? includeOnly = null,
        CancellationToken ct = default)
        => ApplyCoreAsync(installPath, addonId, new FolderAddonSource(sourceDir), profile,
                          allowMultiplayerRisk, includeOnly, ct);

    private static async Task<AddonApplyResult> ApplyCoreAsync(
        string installPath,
        string addonId,
        IAddonSource source,
        ModProfile profile,
        bool allowMultiplayerRisk,
        IReadOnlyList<string>? includeOnly,
        CancellationToken ct)
    {
        var entries = await Task.Run(() => source.ListEntries(), ct);
        var risk = AddonRisk.Assess(entries);

        if (risk.Level == AddonRiskLevel.Blocked)
            return new AddonApplyResult(AddonApplyStatus.Blocked, Array.Empty<string>(), risk.BlockingFiles);
        if (risk.Level == AddonRiskLevel.Empty)
            return new AddonApplyResult(AddonApplyStatus.Empty, Array.Empty<string>(), Array.Empty<string>());
        if (risk.Level == AddonRiskLevel.MultiplayerRisk && !allowMultiplayerRisk)
            return new AddonApplyResult(AddonApplyStatus.Blocked, Array.Empty<string>(), risk.SimulationFiles);

        var include = BuildIncludeSet(includeOnly);

        // The manifest is OPTIONAL. Addons apply to the player's own unmodded
        // Age of Empires III too, and that install has none — the launcher never
        // installed it. Ownership is tracked separately (see AddonOwnership) so
        // this path does not depend on it.
        var owned = AddonOwnership.Load(installPath);

        // Two addons writing the same file cannot both be reverted — whoever
        // disabled second would restore the FIRST one's file as the "original".
        // There is no merging overlay binaries, so the second one is refused.
        var planned = entries
            .Select(NormalizeRelative)
            .Where(p => p.Length > 0 && ShouldApply(p, include))
            .ToList();

        if (planned.Count == 0)
            return new AddonApplyResult(
                AddonApplyStatus.Empty, Array.Empty<string>(), Array.Empty<string>());

        foreach (var (otherId, ownedFiles) in owned)
        {
            if (string.Equals(otherId, addonId, StringComparison.OrdinalIgnoreCase)) continue;
            var clash = ownedFiles.FirstOrDefault(f =>
                planned.Any(p => string.Equals(p, f, StringComparison.OrdinalIgnoreCase)));
            if (clash != null)
                return new AddonApplyResult(
                    AddonApplyStatus.Conflict, Array.Empty<string>(), new[] { clash }, otherId);
        }

        try
        {
            var (written, skipped) = await Task.Run(
                () => CopyWithBackup(installPath, addonId, source, include, ct), ct);

            owned[addonId] = written;
            AddonOwnership.Save(installPath, owned);

            // Only a modded install has a manifest, and re-capturing is what stops
            // "Verify files" reporting the addon's files as corrupt. The stock game
            // has no verify, so there is nothing to keep in sync there.
            var manifest = InstallManifest.TryLoad(installPath);
            if (manifest != null)
            {
                manifest.AddonFiles[addonId] = written;
                RecaptureInto(manifest, installPath, written, profile, ct);
                manifest.Save();
            }

            DiagnosticLog.Write(
                $"Addon '{addonId}' applied: {written.Count} file(s), risk={risk.Level}" +
                (skipped.Count > 0 ? $"; skipped {string.Join(", ", skipped)}" : "") + ".");
            return new AddonApplyResult(
                AddonApplyStatus.Applied, written, Array.Empty<string>(), null, skipped);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Addon '{addonId}' failed to apply: {ex.Message}");
            return new AddonApplyResult(AddonApplyStatus.Failed, Array.Empty<string>(), Array.Empty<string>());
        }
    }

    /// <summary>
    /// Reverts an addon: restores every file that had an original and deletes the
    /// ones it added, then re-captures so verify sees the restored state.
    /// </summary>
    public static async Task<bool> DisableAsync(
        string installPath,
        string addonId,
        ModProfile profile,
        CancellationToken ct = default)
    {
        var record = AddonOwnership.Load(installPath);

        if (!record.TryGetValue(addonId, out var owned) || owned.Count == 0)
        {
            // Nothing recorded — treat as already off rather than an error, so a
            // half-applied state can always be cleared from the UI.
            record.Remove(addonId);
            AddonOwnership.Save(installPath, record);
            return true;
        }

        try
        {
            await Task.Run(() => RestoreFiles(installPath, addonId, owned, ct), ct);

            record.Remove(addonId);
            AddonOwnership.Save(installPath, record);

            // Modded installs only — the stock game has no manifest and no verify.
            var manifest = InstallManifest.TryLoad(installPath);
            if (manifest != null)
            {
                manifest.AddonFiles.Remove(addonId);
                RecaptureInto(manifest, installPath, owned, profile, ct);
                manifest.FileHashes = NativeInstallService.PruneMissingHashes(installPath, manifest.FileHashes);
                manifest.Save();
            }

            TryDeleteDirectory(BackupFolderOf(installPath, addonId));
            DiagnosticLog.Write($"Addon '{addonId}' disabled: {owned.Count} file(s) reverted.");
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Addon '{addonId}' failed to disable: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Re-applies the addons that should be on, after an update or repair has
    /// re-laid the overlay. Called from those flows' tails.
    ///
    /// <b>The backups are deliberately discarded first.</b> After a re-overlay the
    /// files on disk are the NEW version's, but <c>addons\_originals\</c> still
    /// holds the PREVIOUS version's bytes. Re-applying without clearing them would
    /// leave a backup that, when the addon is later disabled, restores the old
    /// version's file over the new one — a silent downgrade that verify would then
    /// bless, because re-capturing makes the manifest agree with whatever is on
    /// disk. Taking a fresh backup from the freshly-laid files is the only correct
    /// order.
    ///
    /// Never throws: a cosmetic addon failing to re-apply must not fail an update.
    /// </summary>
    public static async Task ReapplyAllAsync(
        string installPath,
        IEnumerable<string> addonIds,
        Func<string, CancellationToken, Task<string?>> resolveZip,
        ModProfile profile,
        CancellationToken ct = default)
    {
        foreach (var id in addonIds)
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                // Stale-backup guard — see the remarks above.
                TryDeleteDirectory(BackupFolderOf(installPath, id));

                // The files the addon owned last time ARE its include list: they
                // already went through the declared list or the skip rules when it
                // was first applied, so re-applying reproduces the same set without
                // needing the catalog manifest here. Captured BEFORE the entry is
                // cleared.
                var record = AddonOwnership.Load(installPath);
                IReadOnlyList<string>? previouslyOwned = null;
                if (record.TryGetValue(id, out var owned)) previouslyOwned = owned.ToList();
                if (record.Remove(id)) AddonOwnership.Save(installPath, record);

                var zip = await resolveZip(id, ct);
                if (string.IsNullOrEmpty(zip) || !File.Exists(zip))
                {
                    DiagnosticLog.Write($"Addon '{id}': archive unavailable, not re-applied.");
                    continue;
                }

                // allowMultiplayerRisk: the user already accepted this addon's risk
                // when they enabled it; re-prompting mid-update isn't possible.
                var result = await ApplyAsync(
                    installPath, id, zip, profile, true, previouslyOwned, ct);
                if (result.Status != AddonApplyStatus.Applied)
                    DiagnosticLog.Write($"Addon '{id}': re-apply returned {result.Status}.");
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"Addon '{id}': re-apply failed — {ex.Message}");
            }
        }
    }

    // -- internals -----------------------------------------------------------

    /// <summary>
    /// Extracts every file entry, copying any pre-existing file into the addon's
    /// backup folder first. Returns the install-relative paths written.
    /// </summary>
    private static (List<string> Written, List<string> Skipped) CopyWithBackup(
        string installPath, string addonId, IAddonSource source,
        HashSet<string>? include, CancellationToken ct)
    {
        var backupRoot = BackupFolderOf(installPath, addonId);
        Directory.CreateDirectory(backupRoot);

        var written = new List<string>();
        var skipped = new List<string>();
        var installRoot = Path.GetFullPath(installPath);

        foreach (var rel in source.ListEntries())
        {
            ct.ThrowIfCancellationRequested();
            if (rel.Length == 0) continue;

            if (!ShouldApply(rel, include)) { skipped.Add(rel); continue; }

            var dest = Path.GetFullPath(Path.Combine(installPath, rel));
            // Path traversal: an entry escaping the install root is never legitimate.
            if (!dest.StartsWith(installRoot, StringComparison.OrdinalIgnoreCase))
            {
                DiagnosticLog.Write("Addon rejected entry outside install root: " + rel);
                continue;
            }

            var destDir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

            // Back up BEFORE overwriting - this is what makes disabling reversible.
            if (File.Exists(dest))
            {
                var backup = Path.Combine(backupRoot, rel);
                var backupDir = Path.GetDirectoryName(backup);
                if (!string.IsNullOrEmpty(backupDir)) Directory.CreateDirectory(backupDir);
                File.Copy(dest, backup, overwrite: true);
            }

            source.CopyTo(rel, dest);
            written.Add(rel);
        }

        return (written, skipped);
    }

    /// <summary>
    /// Install-relative paths a folder would contribute, with the wrapper folder
    /// stripped. Lets a caller classify an unpacked installer's output before
    /// deciding whether to apply it.
    /// </summary>
    public static List<string> ListFolderEntries(string sourceDir)
        => new FolderAddonSource(sourceDir).ListEntries();

    /// <summary>Where an addon's files come from: an archive, or a folder.</summary>
    private interface IAddonSource
    {
        /// <summary>Install-relative paths, wrapper folder already stripped.</summary>
        List<string> ListEntries();
        void CopyTo(string relativePath, string destinationPath);
    }

    private sealed class ZipAddonSource : IAddonSource
    {
        private readonly string _zipPath;
        public ZipAddonSource(string zipPath) => _zipPath = zipPath;

        public List<string> ListEntries() => ReadArchiveEntries(_zipPath);

        public void CopyTo(string relativePath, string destinationPath)
        {
            using var zip = ZipFile.OpenRead(_zipPath);
            var prefix = AddonPaths.StripCommonRoot(
                zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).Select(e => e.FullName));

            var entry = zip.Entries.FirstOrDefault(e =>
                !string.IsNullOrEmpty(e.Name) &&
                string.Equals(AddonPaths.RemovePrefix(e.FullName, prefix), relativePath,
                              StringComparison.OrdinalIgnoreCase));
            if (entry == null) throw new FileNotFoundException("Missing zip entry: " + relativePath);

            entry.ExtractToFile(destinationPath, overwrite: true);
        }
    }

    private sealed class FolderAddonSource : IAddonSource
    {
        private readonly string _root;
        public FolderAddonSource(string root) => _root = root;

        public List<string> ListEntries()
        {
            if (!Directory.Exists(_root)) return new List<string>();

            var all = Directory
                .EnumerateFiles(_root, "*", SearchOption.AllDirectories)
                .Select(p => AddonPaths.Normalize(Path.GetRelativePath(_root, p)))
                .Where(p => p.Length > 0)
                .ToList();

            // Same wrapper rule as archives: an installer that unpacks everything
            // under one folder would otherwise write it into the install verbatim.
            var prefix = AddonPaths.StripCommonRoot(all);
            return all
                .Select(p => AddonPaths.RemovePrefix(p, prefix))
                .Where(p => p.Length > 0)
                .ToList();
        }

        public void CopyTo(string relativePath, string destinationPath)
        {
            var source = FindSource(relativePath);
            if (source == null) throw new FileNotFoundException("Missing source file: " + relativePath);
            File.Copy(source, destinationPath, overwrite: true);
        }

        /// <summary>Resolves back through the wrapper prefix ListEntries removed.</summary>
        private string? FindSource(string relativePath)
        {
            var direct = Path.Combine(_root, relativePath);
            if (File.Exists(direct)) return direct;

            foreach (var p in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                var rel = AddonPaths.Normalize(Path.GetRelativePath(_root, p));
                if (rel.EndsWith(relativePath, StringComparison.OrdinalIgnoreCase)) return p;
            }
            return null;
        }
    }

    /// <summary>
    /// Normalizes a declared include list for lookup, or null when the addon
    /// didn't declare one.
    /// </summary>
    private static HashSet<string>? BuildIncludeSet(IReadOnlyList<string>? includeOnly)
    {
        if (includeOnly == null || includeOnly.Count == 0) return null;
        return new HashSet<string>(
            includeOnly.Select(NormalizeRelative).Where(p => p.Length > 0),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A declared list is exhaustive — it overrides the extension rules, because
    /// the reviewer who wrote it already decided. Without one, skip executables
    /// and documentation.
    /// </summary>
    private static bool ShouldApply(string rel, HashSet<string>? include) =>
        include != null ? include.Contains(rel) : !AddonRisk.IsSkippable(rel);

    private static void RestoreFiles(
        string installPath, string addonId, IReadOnlyList<string> owned, CancellationToken ct)
    {
        var backupRoot = BackupFolderOf(installPath, addonId);

        foreach (var rel in owned)
        {
            ct.ThrowIfCancellationRequested();

            var dest = Path.Combine(installPath, rel);
            var backup = Path.Combine(backupRoot, rel);

            if (File.Exists(backup))
            {
                var destDir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);
                File.Copy(backup, dest, overwrite: true);
            }
            else if (File.Exists(dest))
            {
                // No backup means the addon ADDED this file — reverting is deleting.
                File.Delete(dest);
            }
        }
    }

    /// <summary>
    /// Folds fresh fingerprints for <paramref name="touched"/> into the manifest.
    /// This is what keeps "Verify files" honest about an install that legitimately
    /// differs from the payload.
    /// </summary>
    private static void RecaptureInto(
        InstallManifest manifest,
        string installPath,
        IReadOnlyList<string> touched,
        ModProfile profile,
        CancellationToken ct)
    {
        var (overlay, engine) = NativeInstallService.RecaptureHashes(
            installPath, touched, manifest.OverlayFiles,
            profile.Translations?.CoveredFiles, null, ct);

        foreach (var kv in overlay) manifest.FileHashes[kv.Key] = kv.Value;
        manifest.EngineFileHashes = engine;
    }

    /// <summary>
    /// Normalizes a zip entry to the manifest's path convention: install-relative
    /// with FORWARD slashes.
    ///
    /// Load-bearing, and not obvious from the type system.
    /// <see cref="NativeInstallService.RecaptureHashes"/> converts its input with
    /// <c>Replace('\\','/')</c> and then matches it against
    /// <c>InstallManifest.OverlayFiles</c>, so a backslash path silently fails to
    /// match: the fingerprint never lands in <c>FileHashes</c>, verify keeps
    /// reporting the addon's files as corrupt, and Repair wipes the addon — the
    /// exact failure this whole service exists to prevent. Paths stay usable with
    /// <see cref="Path.Combine"/> either way on Windows.
    /// </summary>
    private static string NormalizeRelative(string entryName)
        => AddonPaths.Normalize(entryName);

    private static string Sanitize(string id)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(id.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (Exception ex) { DiagnosticLog.Write($"Addon backup cleanup failed for {path}: {ex.Message}"); }
    }
}
