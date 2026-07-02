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

    private readonly string _installPath;

    /// <summary>The files THIS mod's translations replace (per-mod, not WoL-fixed).</summary>
    private readonly IReadOnlyList<string> _coveredFiles;

    /// <param name="coveredFiles">
    /// The mod's <c>ModProfile.Translations.CoveredFiles</c>. When null/empty the
    /// WoL default is used, preserving old behaviour for callers that don't pass it.
    /// </param>
    public TranslationService(string installPath, IReadOnlyList<string>? coveredFiles = null)
    {
        _installPath = installPath;
        _coveredFiles = (coveredFiles != null && coveredFiles.Count > 0)
            ? coveredFiles
            : DefaultCoveredFiles;
    }

    /// <summary>Folder where translations live: &lt;install&gt;\translations\</summary>
    public string TranslationsRoot => Path.Combine(_installPath, TranslationsFolderName);

    /// <summary>Snapshot of the canonical English files: &lt;install&gt;\translations\_originals\</summary>
    public string OriginalsFolder => Path.Combine(TranslationsRoot, OriginalsFolderName);

    /// <summary>Folder for a specific translation pack: &lt;install&gt;\translations\&lt;id&gt;\</summary>
    public string GetPackFolder(string id) => Path.Combine(TranslationsRoot, id);

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
    public void RefreshOriginalsSnapshot()
    {
        try
        {
            Directory.CreateDirectory(OriginalsFolder);
            int copied = 0;
            foreach (var rel in _coveredFiles)
            {
                var src = Path.Combine(_installPath, rel);
                if (!File.Exists(src)) continue;

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
    /// </summary>
    public List<TranslationManifest> ListInstalled()
    {
        var result = new List<TranslationManifest>();
        if (!Directory.Exists(TranslationsRoot)) return result;

        foreach (var dir in Directory.EnumerateDirectories(TranslationsRoot))
        {
            var name = Path.GetFileName(dir);
            if (string.Equals(name, OriginalsFolderName, StringComparison.OrdinalIgnoreCase))
                continue;

            var manifestPath = Path.Combine(dir, TranslationManifest.ManifestFileName);
            if (!File.Exists(manifestPath)) continue;

            try
            {
                var json = File.ReadAllText(manifestPath);
                var manifest = JsonSerializer.Deserialize<TranslationManifest>(json);
                if (manifest != null && !string.IsNullOrEmpty(manifest.Id))
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
        if (string.IsNullOrEmpty(id)) return null;
        return ListInstalled().FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    // ------------------------------------------------------------------------
    // Install / Apply / Revert
    // ------------------------------------------------------------------------

    /// <summary>
    /// Extracts a downloaded translation pack .zip into
    /// <c>translations\&lt;id&gt;\</c>. The id is derived from the pack's
    /// own manifest, NOT the zip filename.
    /// </summary>
    /// <returns>The parsed manifest of the freshly installed pack.</returns>
    public async Task<TranslationManifest> InstallPackFromZipAsync(
        string zipPath, CancellationToken ct = default)
    {
        if (!File.Exists(zipPath))
            throw new FileNotFoundException("Translation pack zip not found.", zipPath);

        // Open the zip, find the manifest, infer the id, then extract.
        TranslationManifest manifest = await Task.Run(() => ReadManifestFromZip(zipPath), ct);
        if (string.IsNullOrEmpty(manifest.Id))
            throw new InvalidDataException("Translation pack is missing an 'id' in translation.json.");

        var packFolder = GetPackFolder(manifest.Id);
        // Replace any prior install of the same id (older version of the
        // same translation, partial extraction, etc.).
        if (Directory.Exists(packFolder))
        {
            try { Directory.Delete(packFolder, recursive: true); }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"Translations: could not clear old pack folder: {ex.Message}");
            }
        }
        Directory.CreateDirectory(packFolder);

        await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, packFolder), ct);
        DiagnosticLog.Write(
            $"Translations: installed pack '{manifest.Id}' v{manifest.Version} by {manifest.Author}.");
        return manifest;
    }

    /// <summary>
    /// Applies an already-installed translation pack — copies its files
    /// over the live <c>data\</c> folder. Make sure the originals snapshot
    /// is up to date before calling so the user can revert.
    /// </summary>
    public ApplyResult Apply(string id)
    {
        var manifest = GetInstalled(id);
        if (manifest == null)
            return ApplyResult.Fail($"Translation '{id}' is not installed.");

        // Snapshot must exist so revert is possible. If it doesn't, build
        // it now from whatever's currently in data\ (which we assume is the
        // English version since we're about to overwrite with translation).
        if (!HasOriginalsSnapshot())
        {
            DiagnosticLog.Write("Translations: snapshot missing; building from current data\\ before apply.");
            RefreshOriginalsSnapshot();
        }

        var packFolder = GetPackFolder(id);
        int copied = 0;
        foreach (var file in manifest.Files)
        {
            // Translation manifests use forward-slash paths; normalize.
            var relative = file.Path.Replace('/', Path.DirectorySeparatorChar);
            var src = Path.Combine(packFolder, Path.GetFileName(relative));
            var dst = Path.Combine(_installPath, relative);

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
                DiagnosticLog.Write($"Translations: failed copying {relative}: {ex.Message}");
            }
        }

        DiagnosticLog.Write($"Translations: applied '{id}' ({copied}/{manifest.Files.Count} files).");
        return copied > 0
            ? ApplyResult.Ok(manifest)
            : ApplyResult.Fail("No files were applied — pack may be corrupt.");
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
        // pack is bit-exact compatible regardless of version strings.
        if (HasOriginalsSnapshot())
        {
            bool allMatch = true;
            foreach (var file in manifest.Files)
            {
                var snapshot = Path.Combine(
                    OriginalsFolder,
                    Path.GetFileName(file.Path.Replace('/', Path.DirectorySeparatorChar)));
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
            if (manifest == null) return null;

            var compat = CheckCompatibility(manifest, newModVersion);
            if (compat == CompatibilityResult.Unknown)
            {
                // Revert the files to English AND clear the active id so config
                // and disk stay consistent, then report it for the UI to surface.
                RevertToOriginal();
                var notice = new TranslationRevertNotice(
                    manifest.Id, manifest.Name, manifest.CompatibleWith, newModVersion);
                state.ActiveTranslationId = "";
                state.ActiveTranslationVersion = "";
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
    public record ExportResult(
        bool Success,
        string? ZipPath,
        long ZipSize,
        string? JsonPath,
        string? ErrorMessage,
        string? FolderPath = null);

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

            DiagnosticLog.Write(
                $"Export: created '{inputs.OutputZipPath}' " +
                $"({filesIncluded} files, {zipSize} bytes) for translation '{manifest.Id}' v{manifest.Version}; " +
                $"folder='{folderPath}'.");

            return new ExportResult(true, inputs.OutputZipPath, zipSize, siblingJsonPath, null, folderPath);
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

    private static TranslationManifest ReadManifestFromZip(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.GetEntry(TranslationManifest.ManifestFileName);
        if (entry == null)
            throw new InvalidDataException(
                $"Translation pack is missing '{TranslationManifest.ManifestFileName}'.");

        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        var manifest = JsonSerializer.Deserialize<TranslationManifest>(json);
        if (manifest == null)
            throw new InvalidDataException("translation.json is empty or unparseable.");
        return manifest;
    }
}

/// <summary>Outcome of a translation Apply call.</summary>
public record ApplyResult(bool Success, TranslationManifest? Manifest, string? ErrorMessage)
{
    public static ApplyResult Ok(TranslationManifest m) => new(true, m, null);
    public static ApplyResult Fail(string err) => new(false, null, err);
}

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
