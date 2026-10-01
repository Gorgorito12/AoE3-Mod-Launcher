using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Manages community-made translation packs. The launcher treats them as
/// an optional overlay layer applied AFTER the mod patches:
///
///   AoE3 base  →  WoL mod patches  →  [optional translation overlay]
///
/// The English files patched by the mod stay in
/// <c>&lt;install&gt;\translations\_originals\</c> as the canonical version.
/// When a translation is active, <see cref="UpdateService"/> hashes the
/// snapshot instead of the live files so version detection still works.
/// </summary>
public class TranslationService
{
    public const string TranslationsFolderName = "translations";
    public const string OriginalsFolderName = "_originals";

    /// <summary>
    /// WoL-shaped default covered files, used when the caller doesn't pass a
    /// per-mod list (e.g. legacy call sites or a mod whose profile declares none).
    /// </summary>
    private static readonly string[] DefaultCoveredFiles =
    {
        @"data\stringtabley.xml",
        @"data\unithelpstringsy.xml",
    };

    /// <summary>Largest pack <c>.zip</c> the launcher downloads or opens. Real packs are ~1.3 MB.</summary>
    public const long MaxPackZipBytes = 64L * 1024 * 1024;

    /// <summary>Largest single file a pack may extract (the zip's declared size is not trusted).</summary>
    public const long MaxPackEntryBytes = 64L * 1024 * 1024;

    /// <summary>Largest total a pack may extract — the guard against a zip bomb.</summary>
    public const long MaxPackTotalBytes = 128L * 1024 * 1024;

    /// <summary>Largest <c>translation.json</c> the launcher reads. Real ones are under 1 KB.</summary>
    public const long MaxManifestBytes = 256 * 1024;

    /// <summary>
    /// Scratch folders the installer uses under <c>translations\</c>. Both start with a dot, which
    /// no valid pack id can, so <see cref="ListInstalled"/> can never mistake one for a pack.
    /// </summary>
    private const string IncomingFolderPrefix = ".incoming-";
    private const string OldFolderPrefix = ".old-";
    private static readonly TimeSpan StaleScratchAge = TimeSpan.FromHours(1);

    private readonly string _installPath;

    /// <summary>The files THIS mod's translations replace (per-mod, not WoL-fixed).</summary>
    private readonly IReadOnlyList<string> _coveredFiles;

    /// <summary>
    /// Whether a PACK may write into this install at all. False for a mod whose profile has no
    /// Translations block (see <see cref="ForProfile"/>): without it the covered list falls back
    /// to WoL's files, and a pack would be allowed to overwrite another game's string table. The
    /// launcher's own snapshot and revert are trusted data and are not gated by this.
    /// </summary>
    private readonly bool _allowWrites;

    /// <param name="coveredFiles">
    /// The mod's <c>ModProfile.Translations.CoveredFiles</c>. When null/empty the
    /// WoL default is used, preserving old behaviour for callers that don't pass it.
    /// </param>
    public TranslationService(string installPath, IReadOnlyList<string>? coveredFiles = null)
        : this(installPath, coveredFiles, allowWrites: true)
    {
    }

    private TranslationService(string installPath, IReadOnlyList<string>? coveredFiles, bool allowWrites)
    {
        _installPath = installPath;
        _coveredFiles = (coveredFiles != null && coveredFiles.Count > 0)
            ? coveredFiles
            : DefaultCoveredFiles;
        _allowWrites = allowWrites;
    }

    /// <summary>
    /// The service every path that INSTALLS or APPLIES a pack must use. A profile with no
    /// Translations block accepts no pack at all; one with an empty covered list keeps the WoL
    /// default, exactly as the plain constructor does.
    /// </summary>
    public static TranslationService ForProfile(string installPath, ModProfile? profile) =>
        new(installPath, profile?.Translations?.CoveredFiles, allowWrites: profile?.Translations != null);

    /// <summary>Folder where translations live: &lt;install&gt;\translations\</summary>
    public string TranslationsRoot => Path.Combine(_installPath, TranslationsFolderName);

    /// <summary>Snapshot of the canonical English files: &lt;install&gt;\translations\_originals\</summary>
    public string OriginalsFolder => Path.Combine(TranslationsRoot, OriginalsFolderName);

    /// <summary>
    /// Folder for a specific translation pack: &lt;install&gt;\translations\&lt;id&gt;\. Throws for an
    /// id that is not a single safe folder name — callers check
    /// <see cref="TranslationPathPolicy.IsSafePackId"/> first, and this is the backstop that keeps
    /// <c>..\..</c> from ever reaching a recursive delete.
    /// </summary>
    public string GetPackFolder(string id)
    {
        if (!TranslationPathPolicy.IsSafePackId(id))
            throw new ArgumentException($"'{id}' is not a valid translation pack id.", nameof(id));
        return Path.Combine(TranslationsRoot, id);
    }

    // ------------------------------------------------------------------------
    // Originals snapshot — keeps the canonical EN versions for version
    // detection and for reverting the install back to English.
    // ------------------------------------------------------------------------

    /// <summary>
    /// Copies the current English files from <c>data\</c> into
    /// <c>translations\_originals\</c>. Called after the install completes
    /// AND after every mod patch is applied so the snapshot always reflects
    /// the latest English content shipped by the mod.
    /// </summary>
    public void RefreshOriginalsSnapshot() => RefreshOriginalsSnapshot(null);

    /// <param name="skipFileNames">Covered files (by name) to leave out of the snapshot.</param>
    private void RefreshOriginalsSnapshot(IReadOnlySet<string>? skipFileNames)
    {
        try
        {
            Directory.CreateDirectory(OriginalsFolder);
            int copied = 0;
            foreach (var rel in _coveredFiles)
            {
                var src = Path.Combine(_installPath, rel);
                if (!File.Exists(src)) continue;
                if (skipFileNames != null && skipFileNames.Contains(Path.GetFileName(rel))) continue;

                var dst = Path.Combine(OriginalsFolder, Path.GetFileName(rel));
                File.Copy(src, dst, overwrite: true);
                copied++;
            }
            DiagnosticLog.Write($"Translations: refreshed _originals\\ snapshot ({copied} files).");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Translations: refresh snapshot failed: {ex.Message}");
        }
    }

    /// <summary>
    /// True if the snapshot folder has the canonical English files. The
    /// version-detection code uses this to decide whether to hash the
    /// snapshot (when present) or the live files.
    /// </summary>
    public bool HasOriginalsSnapshot()
    {
        if (!Directory.Exists(OriginalsFolder)) return false;
        foreach (var rel in _coveredFiles)
        {
            var snapshot = Path.Combine(OriginalsFolder, Path.GetFileName(rel));
            if (!File.Exists(snapshot)) return false;
        }
        return true;
    }

    /// <summary>
    /// Resolves a path that the launcher should hash for version detection.
    /// If the snapshot exists and covers <paramref name="relativePath"/>,
    /// returns the snapshot path. Otherwise returns the live install path.
    /// </summary>
    public string ResolveHashableFile(string relativePath)
    {
        var snapshot = Path.Combine(OriginalsFolder, Path.GetFileName(relativePath));
        if (File.Exists(snapshot)) return snapshot;
        return Path.Combine(_installPath, relativePath);
    }

    // ------------------------------------------------------------------------
    // Pack discovery
    // ------------------------------------------------------------------------

    /// <summary>
    /// Returns every translation pack currently extracted under
    /// <c>translations\</c> (excluding the _originals snapshot folder).
    ///
    /// <para>A folder counts only when its NAME is the manifest's id. <see cref="Apply"/> reads the
    /// files from <c>translations\&lt;id&gt;\</c>, so a folder <c>foo\</c> whose manifest says
    /// <c>bar</c> would make <c>GetInstalled("bar")</c> succeed while the copy read a different
    /// folder. Folders starting with a dot are the installer's own scratch space and never a
    /// pack.</para>
    /// </summary>
    public List<TranslationManifest> ListInstalled()
    {
        var result = new List<TranslationManifest>();
        if (!Directory.Exists(TranslationsRoot)) return result;

        foreach (var dir in Directory.EnumerateDirectories(TranslationsRoot))
        {
            var name = Path.GetFileName(dir);
            if (name.StartsWith('.')) continue;
            if (string.Equals(name, OriginalsFolderName, StringComparison.OrdinalIgnoreCase))
                continue;

            var manifestPath = Path.Combine(dir, TranslationManifest.ManifestFileName);
            if (!File.Exists(manifestPath)) continue;

            if (!TranslationPathPolicy.IsSafePackId(name))
            {
                DiagnosticLog.Write($"Translations: skipped folder '{name}' — not a valid pack folder name.");
                continue;
            }

            try
            {
                var json = File.ReadAllText(manifestPath);
                var manifest = JsonSerializer.Deserialize<TranslationManifest>(json);
                if (manifest == null || string.IsNullOrEmpty(manifest.Id)) continue;
                if (!string.Equals(manifest.Id, name, StringComparison.OrdinalIgnoreCase))
                {
                    DiagnosticLog.Write(
                        $"Translations: skipped folder '{name}' — its translation.json is for '{manifest.Id}'.");
                    continue;
                }
                manifest.Files ??= new List<TranslationFile>();
                result.Add(manifest);
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"Translations: bad manifest at '{manifestPath}': {ex.Message}");
            }
        }
        return result;
    }

    /// <summary>
    /// Returns the manifest for a specific installed pack, or null if not present.
    /// </summary>
    public TranslationManifest? GetInstalled(string id)
    {
        if (!TranslationPathPolicy.IsSafePackId(id)) return null;
        return ListInstalled().FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The manifest's files that this install may receive, each paired with the COVERED path the
    /// copy will write to. Everything else is dropped — and logged when <paramref name="log"/> is
    /// set, because a pack that names a file it may not replace is either broken or hostile, and
    /// either way the next person reading the log needs to see it.
    /// </summary>
    private List<(TranslationFile File, string Canonical)> ResolveApprovedFiles(
        TranslationManifest manifest, bool log)
    {
        var allowList = _allowWrites ? _coveredFiles : Array.Empty<string>();
        var approved = new List<(TranslationFile, string)>();
        foreach (var file in manifest.Files ?? new List<TranslationFile>())
        {
            if (file != null && TranslationPathPolicy.TryResolveCoveredTarget(file.Path, allowList, out var canonical))
                approved.Add((file, canonical));
            else if (log)
                DiagnosticLog.Write(
                    $"Translations: '{manifest.Id}' — rejected '{file?.Path}' (not a file this mod's translations may replace).");
        }
        return approved;
    }

    // ------------------------------------------------------------------------
    // Install / Apply / Revert
    // ------------------------------------------------------------------------

    /// <summary>
    /// Extracts a downloaded translation pack .zip into
    /// <c>translations\&lt;id&gt;\</c>. The id is derived from the pack's
    /// own manifest, NOT the zip filename.
    ///
    /// <para><b>The zip is untrusted.</b> Only <c>translation.json</c> and the root-level files
    /// named after the mod's covered files are extracted — never the whole archive — with the
    /// sizes counted as they are copied rather than taken from the zip's own headers. The id must
    /// be a safe folder name and, when the caller knows which pack it asked for, that pack. The
    /// result lands in a scratch folder first and replaces the previous install only once it is
    /// complete, so a refused or broken pack leaves the old one exactly as it was.</para>
    /// </summary>
    /// <param name="expectedId">The id of the pack the player chose, or null when unknown.</param>
    /// <returns>The parsed manifest of the freshly installed pack.</returns>
    public Task<TranslationManifest> InstallPackFromZipAsync(
        string zipPath, string? expectedId = null, CancellationToken ct = default) =>
        Task.Run(() => InstallPackFromZip(zipPath, expectedId, ct), ct);

    /// <summary>Synchronous core of <see cref="InstallPackFromZipAsync"/>: stage, then promote.</summary>
    internal TranslationManifest InstallPackFromZip(string zipPath, string? expectedId, CancellationToken ct)
    {
        var staged = StagePackFromZip(zipPath, new PackExpectation(expectedId), ct);
        try
        {
            PromoteStaged(staged);
        }
        catch
        {
            DiscardStaged(staged);
            throw;
        }
        return staged.Manifest;
    }

    /// <summary>
    /// Extracts and VERIFIES a pack into a scratch folder without touching the installed one.
    /// The apply dialog applies from there and only then promotes it (<see cref="PromoteStaged"/>),
    /// so <c>translations\&lt;id&gt;\</c> always holds the pack that was last applied successfully
    /// — the stale-state check compares the live files against exactly that folder.
    /// </summary>
    public Task<StagedPack> StagePackFromZipAsync(string zipPath, PackExpectation expect, CancellationToken ct = default) =>
        Task.Run(() => StagePackFromZip(zipPath, expect, ct), ct);

    /// <summary>
    /// Synchronous core of <see cref="StagePackFromZipAsync"/>. Beyond the safety rules of
    /// <see cref="TranslationPathPolicy"/> it checks that the pack is the one that was LISTED:
    /// its id, its target mod (required when the source isn't the mod's own), every extracted
    /// file's MD5 against the pack's own <c>translatedHash</c>, and the content hash against the
    /// one the listing advertised. A different version TEXT is tolerated — real packs carry one
    /// in the folder and another inside the zip — because the bytes are what matter.
    /// </summary>
    internal StagedPack StagePackFromZip(string zipPath, PackExpectation expect, CancellationToken ct)
    {
        var expectedId = expect.Id;
        if (!File.Exists(zipPath))
            throw new FileNotFoundException("Translation pack zip not found.", zipPath);
        if (new FileInfo(zipPath).Length > MaxPackZipBytes)
            throw new InvalidDataException(
                $"The translation pack is larger than {FormatLimit(MaxPackZipBytes)}.");
        if (!_allowWrites)
            throw new InvalidOperationException("This mod does not accept community translations.");

        using var archive = ZipFile.OpenRead(zipPath);
        var (manifest, manifestBytes) = ReadManifestFromArchive(archive);

        if (!TranslationPathPolicy.IsSafePackId(manifest.Id))
            throw new InvalidDataException(
                $"The translation pack id '{manifest.Id}' is not a valid folder name.");
        if (!string.IsNullOrEmpty(expectedId)
            && !string.Equals(manifest.Id, expectedId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"The downloaded pack is '{manifest.Id}', not the '{expectedId}' that was chosen.");
        if (TranslationPathPolicy.HasDuplicateFileNames(manifest.Files.Select(f => f?.Path)))
            throw new InvalidDataException("The translation pack lists two files with the same name.");
        if (!string.IsNullOrWhiteSpace(expect.ModId))
        {
            if (!string.IsNullOrWhiteSpace(manifest.TargetMod))
            {
                if (!string.Equals(manifest.TargetMod.Trim(), expect.ModId.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        $"This translation was made for '{manifest.TargetMod}', not for '{expect.ModId}'.");
            }
            else if (expect.RequireTargetMod)
            {
                throw new InvalidDataException(
                    "This translation doesn't say which mod it is for, and it doesn't come from the mod's own source.");
            }
        }

        var approved = ResolveApprovedFiles(manifest, log: true);
        if (approved.Count == 0)
            throw new InvalidDataException(
                "The translation pack contains none of the files this mod's translations may replace.");

        // Root-level entries only, grouped by name so a duplicated entry is noticed instead of
        // one copy silently winning.
        var rootEntries = new Dictionary<string, List<ZipArchiveEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            if (entry.Name.Length == 0 || entry.FullName.IndexOfAny(new[] { '/', '\\' }) >= 0) continue;
            if (!rootEntries.TryGetValue(entry.Name, out var list))
                rootEntries[entry.Name] = list = new List<ZipArchiveEntry>();
            list.Add(entry);
        }

        SweepStaleScratch();
        Directory.CreateDirectory(TranslationsRoot);
        var staging = Path.Combine(TranslationsRoot, IncomingFolderPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            File.WriteAllBytes(Path.Combine(staging, TranslationManifest.ManifestFileName), manifestBytes);

            long total = 0;
            int written = 0;
            foreach (var (_, canonical) in approved)
            {
                ct.ThrowIfCancellationRequested();
                var name = Path.GetFileName(canonical);
                if (!rootEntries.TryGetValue(name, out var matches))
                {
                    DiagnosticLog.Write($"Translations: '{manifest.Id}' lists '{name}' but its zip doesn't contain it.");
                    continue;
                }
                if (matches.Count > 1)
                    throw new InvalidDataException($"The translation pack contains '{name}' more than once.");

                using (var src = matches[0].Open())
                using (var dst = File.Create(Path.Combine(staging, name)))
                    total += CopyCapped(src, dst, MaxPackEntryBytes, MaxPackTotalBytes - total, name, ct);
                written++;
            }
            if (written == 0)
                throw new InvalidDataException(
                    "The translation pack's zip holds none of the files its translation.json lists.");

            var ignored = archive.Entries.Count(e => e.Name.Length > 0) - written - 1;
            if (ignored > 0)
                DiagnosticLog.Write($"Translations: '{manifest.Id}' — ignored {ignored} other file(s) in the zip.");

            VerifyStagedFiles(staging, manifest, approved);

            if (!string.IsNullOrWhiteSpace(expect.AdvertisedContentHash))
            {
                // The files' MD5s were just checked against the manifest, so the hash computed
                // from it describes the real bytes. The manifest's own declared value is accepted
                // too, since the listing advertises "declared, else computed".
                var advertised = expect.AdvertisedContentHash.Trim();
                var computed = TranslationCompat.ComputeContentHash(manifest.Files);
                if (!string.Equals(advertised, computed, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(advertised, manifest.ContentHash?.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        "The downloaded pack is not the version that was listed (its content hash differs).");
            }
        }
        catch
        {
            TryDeleteDirectory(staging);
            throw;
        }

        DiagnosticLog.Write(
            $"Translations: staged pack '{manifest.Id}' v{manifest.Version} by {manifest.Author}.");
        return new StagedPack(manifest, staging, TranslationCompat.EffectiveContentHash(manifest));
    }

    /// <summary>
    /// Every extracted file must hash to the <c>translatedHash</c> its manifest records. That is
    /// what ties the bytes to the listing: the advertised content hash is derived from those
    /// hashes, so a pack whose files don't match them is damaged or not the pack it claims to be.
    /// </summary>
    private static void VerifyStagedFiles(
        string staging, TranslationManifest manifest, List<(TranslationFile File, string Canonical)> approved)
    {
        foreach (var (file, canonical) in approved)
        {
            var name = Path.GetFileName(canonical);
            var path = Path.Combine(staging, name);
            if (!File.Exists(path)) continue;
            if (string.IsNullOrWhiteSpace(file.TranslatedHash))
                throw new InvalidDataException($"The translation pack records no hash for '{name}'.");
            string md5;
            using (var fs = File.OpenRead(path))
                md5 = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(fs));
            if (!string.Equals(md5, file.TranslatedHash.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"'{name}' doesn't match the hash its translation.json records — the pack is damaged.");
        }
    }

    /// <summary>Copies a staged pack's files over the live install. Nothing is promoted yet.</summary>
    public ApplyResult ApplyStaged(StagedPack staged)
    {
        if (staged == null) return ApplyResult.Fail("Nothing to apply.");
        if (!TranslationPathPolicy.IsSafePackId(staged.Manifest.Id))
            return ApplyResult.Fail($"'{staged.Manifest.Id}' is not a valid translation id.");
        return ApplyFromFolder(staged.Folder, staged.Manifest);
    }

    /// <summary>Makes a staged pack the installed one (only call after it applied successfully).</summary>
    public void PromoteStaged(StagedPack staged)
    {
        if (staged == null || staged.Promoted) return;
        PromoteFolder(staged.Folder, GetPackFolder(staged.Manifest.Id));
        staged.Promoted = true;
        DiagnosticLog.Write(
            $"Translations: installed pack '{staged.Manifest.Id}' v{staged.Manifest.Version} by {staged.Manifest.Author}.");
    }

    /// <summary>Deletes a staged pack that was never promoted (cancel, refusal, failed apply).</summary>
    public void DiscardStaged(StagedPack? staged)
    {
        if (staged == null || staged.Promoted) return;
        TryDeleteDirectory(staged.Folder);
    }

    /// <summary>
    /// Copies <paramref name="src"/> into <paramref name="dst"/>, refusing once more than
    /// <paramref name="maxBytes"/> (or <paramref name="remainingTotal"/>) have been read. A zip
    /// entry's declared size is whatever its author wrote, so the bytes themselves are counted.
    /// </summary>
    internal static long CopyCapped(
        Stream src, Stream dst, long maxBytes, long remainingTotal, string name, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long copied = 0;
        int read;
        while ((read = src.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            copied += read;
            if (copied > maxBytes)
                throw new InvalidDataException($"'{name}' is larger than {FormatLimit(maxBytes)}.");
            if (copied > remainingTotal)
                throw new InvalidDataException(
                    $"The translation pack is larger than {FormatLimit(MaxPackTotalBytes)} once extracted.");
            dst.Write(buffer, 0, read);
        }
        return copied;
    }

    private static string FormatLimit(long bytes) =>
        bytes >= 1024 * 1024 ? $"{bytes / (1024 * 1024)} MB" : $"{bytes / 1024} KB";

    /// <summary>
    /// Replaces <paramref name="target"/> with the finished <paramref name="staging"/> folder. The
    /// old pack is moved aside first and only deleted once the new one is in place; if the swap
    /// fails it is moved back, so the install never ends up with no pack where it had one.
    /// </summary>
    private void PromoteFolder(string staging, string target)
    {
        string? previous = null;
        if (Directory.Exists(target))
        {
            previous = Path.Combine(TranslationsRoot, OldFolderPrefix + Guid.NewGuid().ToString("N"));
            Directory.Move(target, previous);
        }
        try
        {
            Directory.Move(staging, target);
        }
        catch
        {
            if (previous != null)
            {
                try { Directory.Move(previous, target); }
                catch (Exception ex)
                {
                    DiagnosticLog.Write($"Translations: could not restore the previous pack folder: {ex.Message}");
                }
            }
            throw;
        }
        if (previous != null) TryDeleteDirectory(previous);
    }

    /// <summary>Removes scratch folders an interrupted install left behind more than an hour ago.</summary>
    private void SweepStaleScratch()
    {
        try
        {
            if (!Directory.Exists(TranslationsRoot)) return;
            foreach (var dir in Directory.EnumerateDirectories(TranslationsRoot))
            {
                var name = Path.GetFileName(dir);
                if (!name.StartsWith(IncomingFolderPrefix, StringComparison.Ordinal)
                    && !name.StartsWith(OldFolderPrefix, StringComparison.Ordinal)) continue;
                if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(dir) < StaleScratchAge) continue;
                TryDeleteDirectory(dir);
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Translations: scratch sweep failed: {ex.Message}");
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Translations: could not delete '{path}': {ex.Message}");
        }
    }

    /// <summary>
    /// Applies an already-installed translation pack — copies its files
    /// over the live <c>data\</c> folder. Make sure the originals snapshot
    /// is up to date before calling so the user can revert.
    ///
    /// <para>Only files the mod's covered list allows are written, and each is written to the
    /// COVERED path rather than the one the pack spells out. See <see cref="TranslationPathPolicy"/>.</para>
    /// </summary>
    public ApplyResult Apply(string id)
    {
        if (!TranslationPathPolicy.IsSafePackId(id))
        {
            DiagnosticLog.Write($"Translations: refused to apply '{id}' — not a valid pack id.");
            return ApplyResult.Fail($"'{id}' is not a valid translation id.");
        }
        if (!_allowWrites)
        {
            DiagnosticLog.Write($"Translations: refused to apply '{id}' — this mod accepts no translations.");
            return ApplyResult.Fail("This mod does not accept community translations.");
        }

        var manifest = GetInstalled(id);
        if (manifest == null)
            return ApplyResult.Fail($"Translation '{id}' is not installed.");
        return ApplyFromFolder(GetPackFolder(id), manifest);
    }

    /// <summary>
    /// The copy itself, shared by <see cref="Apply"/> (from <c>translations\&lt;id&gt;\</c>) and
    /// <see cref="ApplyStaged"/> (from the scratch folder a fresh download was verified in).
    /// </summary>
    private ApplyResult ApplyFromFolder(string packFolder, TranslationManifest manifest)
    {
        var id = manifest.Id;
        if (!_allowWrites)
        {
            DiagnosticLog.Write($"Translations: refused to apply '{id}' — this mod accepts no translations.");
            return ApplyResult.Fail("This mod does not accept community translations.");
        }
        if (TranslationPathPolicy.HasDuplicateFileNames(manifest.Files.Select(f => f?.Path)))
            return ApplyResult.Fail("The translation pack lists two files with the same name.");

        var approved = ResolveApprovedFiles(manifest, log: true);
        int rejected = manifest.Files.Count - approved.Count;
        if (approved.Count == 0)
            return ApplyResult.Fail(
                $"None of the pack's {manifest.Files.Count} file(s) is one this mod's translations may replace.");

        // Snapshot must exist so revert is possible. If it doesn't, build
        // it now from whatever's currently in data\ (which we assume is the
        // English version since we're about to overwrite with translation) —
        // EXCEPT a live file that already IS this pack's file: snapshotting it
        // would store translated text as the "English" backup, and revert would
        // then restore the translation.
        if (!HasOriginalsSnapshot())
        {
            DiagnosticLog.Write("Translations: snapshot missing; building from current data\\ before apply.");
            var alreadyTranslated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (_, canonical) in approved)
                if (FilesEqual(Path.Combine(_installPath, canonical), Path.Combine(packFolder, Path.GetFileName(canonical))))
                    alreadyTranslated.Add(Path.GetFileName(canonical));
            if (alreadyTranslated.Count > 0)
                DiagnosticLog.Write(
                    $"Translations: not snapshotting {string.Join(", ", alreadyTranslated)} — the live copy already is the translation.");
            RefreshOriginalsSnapshot(alreadyTranslated);
        }

        int copied = 0;
        foreach (var (_, canonical) in approved)
        {
            var src = Path.Combine(packFolder, Path.GetFileName(canonical));
            var dst = Path.Combine(_installPath, canonical);

            if (!TranslationPathPolicy.IsUnderRoot(_installPath, dst))
            {
                DiagnosticLog.Write($"Translations: refused '{canonical}' — it resolves outside the install.");
                rejected++;
                continue;
            }
            if (!File.Exists(src))
            {
                DiagnosticLog.Write($"Translations: pack file missing: {src}");
                continue;
            }

            try
            {
                var dstDir = Path.GetDirectoryName(dst);
                if (!string.IsNullOrEmpty(dstDir)) Directory.CreateDirectory(dstDir);
                File.Copy(src, dst, overwrite: true);
                copied++;
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"Translations: failed copying {canonical}: {ex.Message}");
            }
        }

        DiagnosticLog.Write(
            $"Translations: applied '{id}' ({copied}/{manifest.Files.Count} files"
            + (rejected > 0 ? $", {rejected} refused" : "") + ").");
        if (copied == 0) return ApplyResult.Fail("No files were applied — pack may be corrupt.");

        var shadows = FindShadowingCompiled(_installPath, approved.Select(a => a.Canonical));
        if (shadows.Count > 0)
            DiagnosticLog.Write(
                $"Translations: '{id}' was written, but the game reads compiled twins first: {string.Join(", ", shadows)}.");
        return ApplyResult.Ok(manifest) with { ShadowingFiles = shadows };
    }

    /// <summary>
    /// The compiled twin (<c>data/stringtabley.xml.XMB</c>) of every file in
    /// <paramref name="relativePaths"/> that has one on disk, as forward-slash relative paths.
    ///
    /// <para><b>AoE3 reads the compiled <c>.XMB</c> in preference to the loose <c>.xml</c></b>
    /// (the rule <c>RemoveSupersededCompiledXml</c> and <c>CloneFilesRemovedByPatches</c> exist
    /// for). A translation only ever writes the <c>.xml</c>, so with a twin beside it the copy
    /// succeeds, the launcher marks the pack "in use", and the game goes on showing the text of
    /// whatever the twin holds — for an install made before those rules, the player's own AoE3
    /// in its own language. That is precisely a player's report of "Spanish is on and it does
    /// not work", and nothing said why.</para>
    ///
    /// <para>Read-only on purpose. Whether such a file may be removed is decided by
    /// <c>Repair/LeftoverCleanup</c>, which knows what the mod shipped; this only notices.</para>
    /// </summary>
    internal static IReadOnlyList<string> FindShadowingCompiled(
        string installPath, IEnumerable<string> relativePaths)
    {
        var found = new List<string>();
        if (string.IsNullOrWhiteSpace(installPath)) return found;
        foreach (var raw in relativePaths)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var rel = raw.Replace('\\', '/').TrimStart('/');
            if (!rel.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
            var twin = rel + ".XMB";
            try
            {
                if (File.Exists(Path.Combine(installPath, twin.Replace('/', Path.DirectorySeparatorChar))))
                    found.Add(twin);
            }
            catch { /* an unreadable path hides nothing we can name */ }
        }
        return found;
    }

    /// <summary>
    /// Whether the pack the config calls active is what the game will actually read.
    ///
    /// <para>"In use" was taken from <c>ActiveTranslationId</c> alone — a note the launcher wrote
    /// to itself when the copy succeeded — so it stayed true after an update or a repair put the
    /// English files back, and it was true while a compiled twin hid the copy. Both are checked
    /// here against the disk: a twin first (the files may even match and still not be read), then
    /// each file's MD5 against the pack's own <c>translatedHash</c>. A pack that records no hashes
    /// gives <see cref="TranslationLiveState.Unknown"/> rather than a guess. Hashes a few MB, so a
    /// caller on the UI thread runs it off it.</para>
    /// </summary>
    public TranslationLiveCheck CheckLive(string id)
    {
        var manifest = GetInstalled(id);
        if (manifest == null || manifest.Files.Count == 0)
            return new TranslationLiveCheck(TranslationLiveState.Unknown, Array.Empty<string>());

        // Only the files Apply could have written are evidence; anything else the pack names was
        // never copied, so comparing it would report a stale pack where there is none.
        var approved = ResolveApprovedFiles(manifest, log: false);
        if (approved.Count == 0)
            return new TranslationLiveCheck(TranslationLiveState.Unknown, Array.Empty<string>());

        var shadows = FindShadowingCompiled(_installPath, approved.Select(a => a.Canonical));
        if (shadows.Count > 0)
            return new TranslationLiveCheck(TranslationLiveState.Shadowed, shadows);

        bool compared = false;
        foreach (var (file, canonical) in approved)
        {
            if (string.IsNullOrWhiteSpace(file.TranslatedHash)) continue;
            var live = Path.Combine(_installPath, canonical);
            string? md5;
            try
            {
                if (!File.Exists(live)) return new TranslationLiveCheck(TranslationLiveState.NotOnDisk, shadows);
                using var fs = File.OpenRead(live);
                md5 = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(fs));
            }
            catch { continue; }   // a locked file is not evidence either way
            compared = true;
            if (!string.Equals(md5, file.TranslatedHash, StringComparison.OrdinalIgnoreCase))
                return new TranslationLiveCheck(TranslationLiveState.NotOnDisk, shadows);
        }
        return new TranslationLiveCheck(
            compared ? TranslationLiveState.Live : TranslationLiveState.Unknown, shadows);
    }

    /// <summary>
    /// Reverts the install to canonical English by copying every file from
    /// the snapshot back into <c>data\</c>. No-op if the snapshot doesn't
    /// exist (in which case we have no canonical EN to restore from).
    /// </summary>
    public bool RevertToOriginal()
    {
        if (!HasOriginalsSnapshot())
        {
            DiagnosticLog.Write("Translations: cannot revert — no _originals snapshot present.");
            return false;
        }

        int copied = 0;
        foreach (var rel in _coveredFiles)
        {
            var src = Path.Combine(OriginalsFolder, Path.GetFileName(rel));
            var dst = Path.Combine(_installPath, rel);
            try
            {
                File.Copy(src, dst, overwrite: true);
                copied++;
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"Translations: revert failed for {rel}: {ex.Message}");
            }
        }
        DiagnosticLog.Write($"Translations: reverted to original ({copied} files).");
        return copied > 0;
    }

    /// <summary>
    /// What the DISK says about a pack the config calls active, comparing the live files byte
    /// for byte with the installed copy of the pack — not with the manifest's
    /// <c>translatedHash</c>, which only states what the author believed they shipped.
    ///
    /// <para>The case it was written for, from a real bundle: the config said Spanish was
    /// active, the live string table was English, there was no <c>_originals</c>, and the id had
    /// outlived a reinstall into another folder. The card read "In use" and couldn't be clicked,
    /// and "English" failed with "cannot revert". Rules:</para>
    /// <list type="bullet">
    ///   <item><description>every compared file equal → <see cref="TranslationAppliedState.Applied"/>;</description></item>
    ///   <item><description>none equal → <see cref="TranslationAppliedState.NotApplied"/>;</description></item>
    ///   <item><description>some equal → <see cref="TranslationAppliedState.Mixed"/> (the caller leaves it alone);</description></item>
    ///   <item><description>nothing to compare (no pack on disk): <see cref="TranslationAppliedState.NotApplied"/>
    ///     when there is no English snapshot either, or when the live files ARE the snapshot;
    ///     otherwise <see cref="TranslationAppliedState.Unknown"/>;</description></item>
    ///   <item><description>a locked or unreadable file → <see cref="TranslationAppliedState.Unknown"/>.</description></item>
    /// </list>
    /// <para>Reads files of a few MB — callers on the UI thread run it off it.</para>
    /// </summary>
    public TranslationAppliedState AssessApplied(string? id)
    {
        var manifest = TranslationPathPolicy.IsSafePackId(id) ? GetInstalled(id!) : null;
        var approved = manifest != null
            ? ResolveApprovedFiles(manifest, log: false)
            : new List<(TranslationFile File, string Canonical)>();

        int matched = 0, differ = 0;
        try
        {
            foreach (var (_, canonical) in approved)
            {
                var pack = Path.Combine(GetPackFolder(manifest!.Id), Path.GetFileName(canonical));
                if (!File.Exists(pack)) continue;
                var live = Path.Combine(_installPath, canonical);
                if (!File.Exists(live)) { differ++; continue; }
                if (FilesEqualOrThrow(live, pack)) matched++; else differ++;
            }

            int compared = matched + differ;
            if (compared == 0)
            {
                if (!HasOriginalsSnapshot()) return TranslationAppliedState.NotApplied;
                return LiveEqualsSnapshot() ? TranslationAppliedState.NotApplied : TranslationAppliedState.Unknown;
            }
            if (matched == compared) return TranslationAppliedState.Applied;
            return matched == 0 ? TranslationAppliedState.NotApplied : TranslationAppliedState.Mixed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DiagnosticLog.Write($"Translations: could not read the files to check '{id}': {ex.Message}");
            return TranslationAppliedState.Unknown;
        }
    }

    /// <summary>
    /// "Back to English": restore the snapshot when there is one. Without one there is nothing
    /// to copy — but if the disk proves the pack isn't applied (<see cref="AssessApplied"/>), the
    /// game already IS in English and the only thing wrong is the launcher's note, so the answer
    /// is <see cref="RevertOutcome.AlreadyEnglish"/> rather than an error the player can't act on.
    /// </summary>
    public RevertOutcome RevertOrConfirmEnglish(string? activeId)
    {
        if (HasOriginalsSnapshot())
            return RevertToOriginal() ? RevertOutcome.Reverted : RevertOutcome.Failed;
        if (!string.IsNullOrEmpty(activeId) && AssessApplied(activeId) == TranslationAppliedState.NotApplied)
        {
            DiagnosticLog.Write($"Translations: no snapshot, but '{activeId}' isn't on disk — the game is already in English.");
            return RevertOutcome.AlreadyEnglish;
        }
        DiagnosticLog.Write("Translations: cannot revert — no _originals snapshot present.");
        return RevertOutcome.Failed;
    }

    /// <summary>True when every covered file has a snapshot and the live file equals it.</summary>
    private bool LiveEqualsSnapshot()
    {
        foreach (var rel in _coveredFiles)
        {
            var snapshot = Path.Combine(OriginalsFolder, Path.GetFileName(rel));
            var live = Path.Combine(_installPath, rel);
            if (!File.Exists(snapshot) || !File.Exists(live)) return false;
            if (!FilesEqualOrThrow(live, snapshot)) return false;
        }
        return true;
    }

    /// <summary>Byte equality; false when either file is missing or unreadable.</summary>
    internal static bool FilesEqual(string a, string b)
    {
        try
        {
            return File.Exists(a) && File.Exists(b) && FilesEqualOrThrow(a, b);
        }
        catch
        {
            return false;
        }
    }

    private static bool FilesEqualOrThrow(string a, string b)
    {
        using var fa = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var fb = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (fa.Length != fb.Length) return false;
        var ba = new byte[81920];
        var bb = new byte[81920];
        while (true)
        {
            int ra = fa.Read(ba, 0, ba.Length);
            if (ra == 0) return true;
            int filled = 0;
            while (filled < ra)
            {
                int rb = fb.Read(bb, filled, ra - filled);
                if (rb == 0) return false;
                filled += rb;
            }
            if (!ba.AsSpan(0, ra).SequenceEqual(bb.AsSpan(0, ra))) return false;
        }
    }

    // ------------------------------------------------------------------------
    // Compatibility check
    // ------------------------------------------------------------------------

    /// <summary>
    /// Async version — call this from UI code. Determines how cleanly
    /// <paramref name="manifest"/> applies to the current install. Used to
    /// decide whether to warn the user and to auto-revert when a mod update
    /// breaks an active translation.
    /// </summary>
    public async Task<CompatibilityResult> CheckCompatibilityAsync(
        TranslationManifest manifest, string? currentModVersion, CancellationToken ct = default)
    {
        // Hash-level check first: if the snapshot exists and the originalHash
        // declared by every covered file matches what we have on disk, this
        // pack is bit-exact compatible regardless of version strings. Only files
        // Apply would actually write count — and a pack with none is never "exact".
        var approved = ResolveApprovedFiles(manifest, log: false);
        if (approved.Count > 0 && HasOriginalsSnapshot())
        {
            bool allMatch = true;
            foreach (var (file, canonical) in approved)
            {
                var snapshot = Path.Combine(OriginalsFolder, Path.GetFileName(canonical));
                if (!File.Exists(snapshot))
                {
                    allMatch = false;
                    break;
                }
                var actual = await HashService.ComputeMd5Async(snapshot, ct).ConfigureAwait(false);
                if (!string.Equals(actual, file.OriginalHash, StringComparison.OrdinalIgnoreCase))
                {
                    allMatch = false;
                    break;
                }
            }
            if (allMatch) return CompatibilityResult.Exact;
        }

        // Fall back to the declared compatibleWith list — this is the
        // translator's "I tested this for these versions" promise.
        if (!string.IsNullOrEmpty(currentModVersion)
            && manifest.CompatibleWith.Contains(currentModVersion))
        {
            return CompatibilityResult.Declared;
        }

        return CompatibilityResult.Unknown;
    }

    /// <summary>
    /// Synchronous wrapper over <see cref="CheckCompatibilityAsync"/>. Safe to
    /// call from the threadpool / non-UI threads (e.g. <c>UpdateService</c>'s
    /// auto-revert logic that already runs on a background thread). DO NOT
    /// call this from the WPF UI thread — use the async version, otherwise the
    /// hashing's continuation will deadlock against this method's <c>GetResult</c>.
    /// </summary>
    public CompatibilityResult CheckCompatibility(TranslationManifest manifest, string? currentModVersion)
    {
        // Run on the threadpool so there's no captured SynchronizationContext
        // to deadlock against, even if a caller accidentally invokes us on the
        // UI thread.
        return Task.Run(() => CheckCompatibilityAsync(manifest, currentModVersion))
            .GetAwaiter().GetResult();
    }

    /// <summary>
    /// Post-update translation reconciliation, shared by the WolPatcher and
    /// GitHubReleases update paths. Refreshes the originals snapshot, then if a
    /// translation is active: re-applies it when still compatible, or reverts to
    /// English (files AND the active id) when not — returning a notice so the UI
    /// can tell the user. Returns null when nothing changed visibly (no active
    /// pack, or it stayed active). Safe on a background thread.
    /// </summary>
    public TranslationRevertNotice? ReconcileAfterUpdate(
        LauncherConfig config, string modId, string? newModVersion)
    {
        try
        {
            RefreshOriginalsSnapshot();

            var state = config.GetState(modId);
            if (string.IsNullOrEmpty(state.ActiveTranslationId)) return null;

            var manifest = GetInstalled(state.ActiveTranslationId);
            if (manifest == null)
            {
                // The pack is gone, so nothing can be re-applied — and the update just laid
                // English. Keeping the id would leave a card saying "in use" for a language
                // the game no longer shows.
                if (AssessApplied(state.ActiveTranslationId) == TranslationAppliedState.NotApplied)
                {
                    DiagnosticLog.Write(
                        $"Translation '{state.ActiveTranslationId}' was active but its pack is gone; cleared after update.");
                    state.ClearActiveTranslation();
                    config.Save();
                }
                return null;
            }

            var compat = CheckCompatibility(manifest, newModVersion);
            if (compat == CompatibilityResult.Unknown)
            {
                // Revert the files to English AND clear the active id so config
                // and disk stay consistent, then report it for the UI to surface.
                RevertToOriginal();
                var notice = new TranslationRevertNotice(
                    manifest.Id, manifest.Name, manifest.CompatibleWith, newModVersion);
                state.ClearActiveTranslation();
                config.Save();
                DiagnosticLog.Write(
                    $"Translation '{manifest.Id}' incompatible with new mod version; reverted to English.");
                return notice;
            }

            var apply = Apply(manifest.Id);
            DiagnosticLog.Write(apply.Success
                ? $"Translation '{manifest.Id}' re-applied after update."
                : $"Translation re-apply failed: {apply.ErrorMessage}");
            return null;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Post-update translation reconcile failed (non-fatal): {ex.Message}");
            return null;
        }
    }

    // ------------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------------

    // ------------------------------------------------------------------------
    // Pack export — for translators authoring new packs
    // ------------------------------------------------------------------------

    /// <summary>
    /// Inputs collected from the translator in the packaging dialog.
    /// </summary>
    /// <param name="OriginalsFolder">
    /// Folder containing the un-translated English XML files. Optional —
    /// when null/empty the launcher uses its own snapshot at
    /// <see cref="OriginalsFolder"/>. Set this when the snapshot doesn't
    /// exist yet and the translator has their own backup of the originals.
    /// </param>
    public record ExportInputs(
        string Id,
        string Name,
        string Author,
        string Version,
        string Language,
        List<string> CompatibleWith,
        string TranslatedFolder,
        string OutputZipPath,
        string? Description,
        string? OriginalsFolder = null,
        string TargetMod = "",
        // Explicit translated files the user picked (any name). When non-empty,
        // these are used instead of scanning TranslatedFolder by canonical name.
        IReadOnlyList<string>? TranslatedFiles = null,
        // Explicit original (English) files the user picked. Same idea as
        // TranslatedFiles but for the EN baseline. When non-empty, used instead
        // of OriginalsFolder / the launcher snapshot.
        IReadOnlyList<string>? OriginalFiles = null);

    /// <summary>
    /// Output of <see cref="ExportPackageAsync"/>.
    /// <list type="bullet">
    ///   <item><description><c>ZipPath</c> — the generated translation pack .zip</description></item>
    ///   <item><description><c>JsonPath</c> — a copy of <c>translation.json</c> written next to the zip,
    ///         ready to be uploaded as a separate asset on the GitHub release.
    ///         The launcher's registry service reads it directly, so no
    ///         central index file is needed.</description></item>
    /// </list>
    /// </summary>
    /// <param name="Sha256">SHA-256 of the zip — what a <c>translations-index.json</c> must publish.</param>
    /// <param name="Manifest">The manifest written into the pack.</param>
    public record ExportResult(
        bool Success,
        string? ZipPath,
        long ZipSize,
        string? JsonPath,
        string? ErrorMessage,
        string? FolderPath = null,
        string? Sha256 = null,
        TranslationManifest? Manifest = null);

    /// <summary>
    /// Builds a translation pack from a folder of translated XML files plus
    /// some metadata. Computes hashes automatically (originalHash from the
    /// install's _originals snapshot, translatedHash from the files the
    /// translator provides). The output is a ready-to-upload .zip + a JSON
    /// snippet for translations-index.json.
    /// </summary>
    /// <summary>
    /// Resolves the translator's source file for a covered file. Prefers the
    /// exact canonical name (e.g. "stringtabley.xml"); if absent, accepts a file
    /// whose base name CONTAINS the canonical base (e.g.
    /// "stringtabley_translated.xml" → "stringtabley.xml") so translators don't
    /// have to rename their files. Returns null when nothing matches.
    /// </summary>
    internal static string? ResolveTranslatedFile(string folder, string coveredFileName)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return null;
        var exact = Path.Combine(folder, coveredFileName);
        if (File.Exists(exact)) return exact;

        var baseName = Path.GetFileNameWithoutExtension(coveredFileName);
        try
        {
            return Directory.EnumerateFiles(folder, "*.xml")
                .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f)
                    .Contains(baseName, StringComparison.OrdinalIgnoreCase));
        }
        catch { return null; }
    }

    /// <summary>
    /// Picks, from an explicit list of files the user selected, the one matching
    /// a covered file. Prefers an exact base-name match, then a file whose base
    /// name CONTAINS the canonical base (e.g. "stringtabley_translated.xml" →
    /// "stringtabley.xml"). Returns null when none matches.
    /// </summary>
    internal static string? ResolveFromList(IReadOnlyList<string> files, string coveredFileName)
    {
        foreach (var f in files)
            if (string.Equals(Path.GetFileName(f), coveredFileName, StringComparison.OrdinalIgnoreCase))
                return f;

        var baseName = Path.GetFileNameWithoutExtension(coveredFileName);
        foreach (var f in files)
            if (Path.GetFileNameWithoutExtension(f).Contains(baseName, StringComparison.OrdinalIgnoreCase))
                return f;
        return null;
    }

    public async Task<ExportResult> ExportPackageAsync(ExportInputs inputs, CancellationToken ct = default)
    {
        try
        {
            // ---- Validate inputs ----
            if (string.IsNullOrWhiteSpace(inputs.Id))
                return ExportFail("Missing language id (e.g. 'es', 'fr').");
            if (string.IsNullOrWhiteSpace(inputs.Name))
                return ExportFail("Missing translation name.");
            if (string.IsNullOrWhiteSpace(inputs.Version))
                return ExportFail("Missing pack version.");
            // The id and version become folder names (translations/<id>/<version>/) both here and
            // in the launcher that installs the pack, which refuses anything else. Refusing them
            // now keeps the packager from building a pack nobody can install — and keeps a ".."
            // from escaping the output folder.
            if (!TranslationPathPolicy.IsSafePackId(inputs.Id.Trim()))
                return ExportFail(
                    $"'{inputs.Id.Trim()}' can't be used as a translation id. Use letters, digits, '.', '_' or '-', starting with a letter or digit (e.g. 'es', 'pt-br').");
            if (!TranslationPathPolicy.IsSafeVersionSegment(inputs.Version.Trim()))
                return ExportFail(
                    $"'{inputs.Version.Trim()}' can't be used as a pack version. Use letters, digits, '.', '_', '-' or '+', starting with a letter or digit (e.g. '1.2.0e-r1').");
            // The user can either pick explicit files (any name) or point at a
            // folder. Files win when present.
            bool useFiles = inputs.TranslatedFiles != null && inputs.TranslatedFiles.Count > 0;
            if (!useFiles && !Directory.Exists(inputs.TranslatedFolder))
                return ExportFail($"Translated folder doesn't exist: {inputs.TranslatedFolder}");

            // Originals source: explicit picked files win, else a folder path,
            // else the launcher-managed snapshot. We need one of them — without
            // originals there are no originalHash values for the manifest.
            bool useOriginalFiles = inputs.OriginalFiles != null && inputs.OriginalFiles.Count > 0;
            string originalsRoot = "";
            if (!useOriginalFiles)
            {
                if (!string.IsNullOrWhiteSpace(inputs.OriginalsFolder))
                {
                    if (!Directory.Exists(inputs.OriginalsFolder))
                        return ExportFail($"Originals folder doesn't exist: {inputs.OriginalsFolder}");
                    originalsRoot = inputs.OriginalsFolder;
                }
                else if (HasOriginalsSnapshot())
                {
                    originalsRoot = OriginalsFolder;
                }
                else
                {
                    return ExportFail(
                        "No source for the original English files. Either install/update " +
                        "the mod with this launcher (auto-generates the snapshot), or " +
                        "point the dialog at your own backup files.");
                }
            }

            // ---- For each covered file, gather hashes ----
            // Maps the canonical covered file name (e.g. "stringtabley.xml") to
            // the actual source the translator provided (which may be named
            // differently, e.g. "stringtabley_translated.xml"). The pack always
            // ships the CANONICAL name so it overwrites the right game file.
            var manifestFiles = new List<TranslationFile>();
            var sourceByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int filesIncluded = 0;
            foreach (var rel in _coveredFiles)
            {
                var fileName = Path.GetFileName(rel);
                var translatedPath = useFiles
                    ? ResolveFromList(inputs.TranslatedFiles!, fileName)
                    : ResolveTranslatedFile(inputs.TranslatedFolder, fileName);
                var originalPath = useOriginalFiles
                    ? ResolveFromList(inputs.OriginalFiles!, fileName)
                    : Path.Combine(originalsRoot, fileName);

                // Skip files the translator didn't provide — pack only what's there.
                if (translatedPath == null)
                {
                    DiagnosticLog.Write($"Export: skipping {fileName} (not in translator folder)");
                    continue;
                }
                if (originalPath == null || !File.Exists(originalPath))
                {
                    DiagnosticLog.Write($"Export: skipping {fileName} (no original snapshot)");
                    continue;
                }

                var originalHash = await HashService.ComputeMd5Async(originalPath, ct);
                var translatedHash = await HashService.ComputeMd5Async(translatedPath, ct);
                var size = new FileInfo(translatedPath).Length;

                manifestFiles.Add(new TranslationFile
                {
                    // Always normalize to forward slashes — matches the format
                    // we use everywhere in the manifest schema.
                    Path = rel.Replace('\\', '/'),
                    OriginalHash = originalHash,
                    TranslatedHash = translatedHash,
                    Size = size,
                });
                sourceByName[fileName] = translatedPath;
                filesIncluded++;
            }

            if (filesIncluded == 0)
                return ExportFail(
                    "No covered files found in the translator folder. Expected " +
                    "stringtabley.xml and/or unithelpstringsy.xml.");

            // ---- Build the manifest object ----
            var manifest = new TranslationManifest
            {
                Id = inputs.Id.Trim(),
                Name = inputs.Name.Trim(),
                Language = string.IsNullOrWhiteSpace(inputs.Language) ? inputs.Id : inputs.Language,
                Author = inputs.Author?.Trim() ?? "",
                Version = inputs.Version.Trim(),
                CompatibleWith = inputs.CompatibleWith ?? new List<string>(),
                Files = manifestFiles,
                Description = string.IsNullOrWhiteSpace(inputs.Description) ? null : inputs.Description.Trim(),
                TargetMod = inputs.TargetMod?.Trim() ?? "",
                // Content fingerprint (folder publication): a changed pack yields a
                // new hash → the launcher re-notifies without a release tag. Same
                // recipe the launcher + notifier recompute, so all three agree.
                ContentHash = TranslationCompat.ComputeContentHash(manifestFiles),
                // The zip's filename, so a folder-published manifest can point the
                // launcher at translations/<id>/<version>/<zip> on raw CDN.
                Zip = Path.GetFileName(inputs.OutputZipPath),
                // Build timestamp — orders the version history (newest first)
                // reliably without parsing arbitrary version strings.
                Date = DateTime.UtcNow.ToString("o"),
            };

            // Serialize the manifest once — same bytes go into the zip AND
            // into the sibling translation.json next to the zip.
            var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            });

            // ---- Stage everything in a temp folder, then zip it ----
            var stagingFolder = Path.Combine(
                Path.GetTempPath(), $"wol-translation-pack-{Guid.NewGuid():N}");
            Directory.CreateDirectory(stagingFolder);
            try
            {
                // 1. Write the manifest
                File.WriteAllText(
                    Path.Combine(stagingFolder, TranslationManifest.ManifestFileName),
                    manifestJson);

                // 2. Copy the translated files — read from the actual source the
                //    translator provided, but write under the CANONICAL name.
                foreach (var file in manifestFiles)
                {
                    var fileName = Path.GetFileName(file.Path.Replace('/', Path.DirectorySeparatorChar));
                    var source = sourceByName.TryGetValue(fileName, out var sp)
                        ? sp
                        : Path.Combine(inputs.TranslatedFolder, fileName);
                    File.Copy(source, Path.Combine(stagingFolder, fileName));
                }

                // 3. Zip the staging folder's contents (NOT the folder itself —
                //    we want the files at the zip's root)
                if (File.Exists(inputs.OutputZipPath))
                {
                    try { File.Delete(inputs.OutputZipPath); } catch { }
                }
                ZipFile.CreateFromDirectory(stagingFolder, inputs.OutputZipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            }
            finally
            {
                // Clean up staging
                try { Directory.Delete(stagingFolder, recursive: true); }
                catch (Exception ex)
                {
                    DiagnosticLog.Write($"Export: staging cleanup failed: {ex.Message}");
                }
            }

            // ---- Assemble a ready-to-commit translations/<id>/ folder ----
            // The new publication path is "commit files to main": the launcher
            // discovers packs by listing translations/<id>/ and reading the
            // translation.json + <zip> inside. So we build that folder next to
            // the chosen zip with BOTH files in it — the translator just drags
            // the translations/ folder into their repo. (The standalone zip at
            // OutputZipPath stays for the legacy "upload as release assets" path
            // and for the Open-folder button.)
            string? folderPath = null;
            string? siblingJsonPath = null;
            try
            {
                var outputDir = Path.GetDirectoryName(inputs.OutputZipPath);
                if (!string.IsNullOrEmpty(outputDir))
                {
                    // translations/<id>/<version>/ — one subfolder per version so a
                    // history accumulates append-only (the translator commits the
                    // new subfolder; old versions are never touched).
                    var versionSeg = SafeFolderSegment(manifest.Version);
                    folderPath = Path.Combine(outputDir, "translations", manifest.Id, versionSeg);
                    Directory.CreateDirectory(folderPath);
                    // The manifest INSIDE the folder is the canonical one the
                    // launcher reads; siblingJsonPath points at it for the result panel.
                    siblingJsonPath = Path.Combine(folderPath, TranslationManifest.ManifestFileName);
                    File.WriteAllText(siblingJsonPath, manifestJson);
                    // Copy the zip in under its own name (manifest.Zip already
                    // records that name → raw URL = translations/<id>/<zip>).
                    File.Copy(
                        inputs.OutputZipPath,
                        Path.Combine(folderPath, Path.GetFileName(inputs.OutputZipPath)),
                        overwrite: true);
                }
            }
            catch (Exception ex)
            {
                // Non-fatal: the zip was created successfully; the translator can
                // still extract translation.json from it and lay out the folder.
                DiagnosticLog.Write($"Export: could not assemble translations/ folder: {ex.Message}");
                folderPath = null;
                siblingJsonPath = null;
            }

            var zipSize = new FileInfo(inputs.OutputZipPath).Length;
            var zipSha256 = await HashService.ComputeSha256Async(inputs.OutputZipPath, ct);

            DiagnosticLog.Write(
                $"Export: created '{inputs.OutputZipPath}' " +
                $"({filesIncluded} files, {zipSize} bytes, sha256 {zipSha256}) for translation '{manifest.Id}' v{manifest.Version}; " +
                $"folder='{folderPath}'.");

            return new ExportResult(true, inputs.OutputZipPath, zipSize, siblingJsonPath, null, folderPath,
                zipSha256, manifest);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Export failed: {ex}");
            return ExportFail(ex.Message);
        }
    }

    private static ExportResult ExportFail(string err) =>
        new(false, null, 0, null, err);

    /// <summary>Turns a version string into a safe folder name (invalid chars → '-').</summary>
    private static string SafeFolderSegment(string? version)
    {
        var v = (version ?? "").Trim();
        if (string.IsNullOrEmpty(v)) v = "1.0";
        foreach (var c in Path.GetInvalidFileNameChars())
            v = v.Replace(c, '-');
        return v.Replace(' ', '-');
    }

    /// <summary>
    /// Reads the pack's own <c>translation.json</c> — exactly one, at the zip's root, no larger than
    /// <see cref="MaxManifestBytes"/>. Returns the raw bytes too, so the installed copy is the
    /// author's file byte for byte rather than a re-serialization of it.
    /// </summary>
    private static (TranslationManifest Manifest, byte[] Bytes) ReadManifestFromArchive(ZipArchive archive)
    {
        var entries = archive.Entries
            .Where(e => string.Equals(e.FullName, TranslationManifest.ManifestFileName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (entries.Count == 0)
            throw new InvalidDataException(
                $"Translation pack is missing '{TranslationManifest.ManifestFileName}'.");
        if (entries.Count > 1)
            throw new InvalidDataException(
                $"Translation pack contains '{TranslationManifest.ManifestFileName}' more than once.");

        byte[] bytes;
        using (var stream = entries[0].Open())
        using (var buffer = new MemoryStream())
        {
            CopyCapped(stream, buffer, MaxManifestBytes, MaxManifestBytes,
                TranslationManifest.ManifestFileName, CancellationToken.None);
            bytes = buffer.ToArray();
        }

        // Decoded through a StreamReader so a byte-order mark is understood, as File.ReadAllText
        // does for the installed copy.
        string json;
        using (var reader = new StreamReader(new MemoryStream(bytes)))
            json = reader.ReadToEnd();
        var manifest = JsonSerializer.Deserialize<TranslationManifest>(json);
        if (manifest == null)
            throw new InvalidDataException("translation.json is empty or unparseable.");
        manifest.Files ??= new List<TranslationFile>();
        return (manifest, bytes);
    }
}

/// <summary>
/// What the apply dialog expects of a downloaded pack. <paramref name="Id"/> is the pack the
/// player chose; <paramref name="ModId"/> the mod it is being applied to;
/// <paramref name="AdvertisedContentHash"/> what the listing promised; and
/// <paramref name="RequireTargetMod"/> is true when the source is not the mod's own, so the pack
/// has to say which mod it is for.
/// </summary>
public sealed record PackExpectation(
    string? Id, string? ModId = null, string? AdvertisedContentHash = null, bool RequireTargetMod = false);

/// <summary>A verified pack waiting in a scratch folder under <c>translations\</c>.</summary>
public sealed class StagedPack
{
    public StagedPack(TranslationManifest manifest, string folder, string contentHash)
    {
        Manifest = manifest;
        Folder = folder;
        ContentHash = contentHash;
    }

    public TranslationManifest Manifest { get; }
    public string Folder { get; }
    public string ContentHash { get; }
    public bool Promoted { get; internal set; }
}

/// <summary>What <see cref="TranslationService.AssessApplied"/> found on disk.</summary>
public enum TranslationAppliedState { Applied, NotApplied, Mixed, Unknown }

/// <summary>What <see cref="TranslationService.RevertOrConfirmEnglish"/> did.</summary>
public enum RevertOutcome { Reverted, AlreadyEnglish, Failed }

/// <summary>Outcome of a translation Apply call.</summary>
public record ApplyResult(bool Success, TranslationManifest? Manifest, string? ErrorMessage)
{
    public static ApplyResult Ok(TranslationManifest m) => new(true, m, null);
    public static ApplyResult Fail(string err) => new(false, null, err);

    /// <summary>
    /// Compiled twins (<c>…xml.XMB</c>) sitting beside files the pack just wrote. The copy
    /// SUCCEEDED, and the game will still not show it — see
    /// <see cref="TranslationService.FindShadowingCompiled"/>.
    /// </summary>
    public IReadOnlyList<string> ShadowingFiles { get; init; } = Array.Empty<string>();
}

/// <summary>What the disk says about a translation the config calls active.</summary>
public enum TranslationLiveState
{
    /// <summary>Nothing to compare against: the pack is gone, or it records no hashes.</summary>
    Unknown,
    /// <summary>The pack's files are on disk and nothing hides them from the game.</summary>
    Live,
    /// <summary>A compiled <c>.XMB</c> twin makes the game read something else.</summary>
    Shadowed,
    /// <summary>The live files are not the pack's any more (an update or repair replaced them).</summary>
    NotOnDisk,
}

public sealed record TranslationLiveCheck(TranslationLiveState State, IReadOnlyList<string> ShadowingFiles);

/// <summary>How cleanly a translation pack matches the current install.</summary>
public enum CompatibilityResult
{
    /// <summary>The translator's originalHash matches our snapshot bit-exactly.</summary>
    Exact,
    /// <summary>The translator declared this mod version as compatible (but hash differs).</summary>
    Declared,
    /// <summary>No exact hash match and version not in compatibleWith — apply at risk.</summary>
    Unknown,
}
