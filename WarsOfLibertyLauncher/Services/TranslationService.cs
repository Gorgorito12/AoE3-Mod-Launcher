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

    /// <summary>Files that the snapshot covers — the ones translations replace.</summary>
    private static readonly string[] CoveredFiles =
    {
        @"data\stringtabley.xml",
        @"data\unithelpstringsy.xml",
    };

    private readonly string _installPath;

    public TranslationService(string installPath)
    {
        _installPath = installPath;
    }

    /// <summary>Folder where translations live: &lt;install&gt;\translations\</summary>
    public string TranslationsRoot => Path.Combine(_installPath, TranslationsFolderName);

    /// <summary>Snapshot of the canonical English files: &lt;install&gt;\translations\_originals\</summary>
    public string OriginalsFolder => Path.Combine(TranslationsRoot, OriginalsFolderName);

    /// <summary>Folder for a specific translation pack: &lt;install&gt;\translations\&lt;id&gt;\</summary>
    public string GetPackFolder(string id) => Path.Combine(TranslationsRoot, id);

    /// <summary>The list of files covered by translations (relative to install root).</summary>
    public static IReadOnlyList<string> CoveredFilePaths => CoveredFiles;

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
            foreach (var rel in CoveredFiles)
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
        foreach (var rel in CoveredFiles)
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
        foreach (var rel in CoveredFiles)
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
    /// Determines how cleanly <paramref name="manifest"/> applies to the
    /// current install. Used to decide whether to warn the user and to
    /// auto-revert when a mod update breaks an active translation.
    /// </summary>
    public CompatibilityResult CheckCompatibility(TranslationManifest manifest, string? currentModVersion)
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
                var actual = HashService.ComputeMd5Async(snapshot).GetAwaiter().GetResult();
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
        string? OriginalsFolder = null);

    /// <summary>
    /// Output of <see cref="ExportPackageAsync"/>.
    /// <list type="bullet">
    ///   <item><description><c>ZipPath</c> — the generated translation pack .zip</description></item>
    ///   <item><description><c>JsonPath</c> — a copy of <c>translation.json</c> written next to the zip,
    ///         ready to be uploaded as a separate asset on the GitHub release.
    ///         The launcher's registry service reads it directly, so no
    ///         translations-index.json file is needed.</description></item>
    ///   <item><description><c>IndexJsonSnippet</c> — pre-formatted JSON the maintainer can paste into
    ///         <c>translations-index.json</c>. Kept for the legacy index-file
    ///         workflow; not required for the GitHub-releases-API path.</description></item>
    /// </list>
    /// </summary>
    public record ExportResult(
        bool Success,
        string? ZipPath,
        long ZipSize,
        string? JsonPath,
        string? IndexJsonSnippet,
        string? ErrorMessage);

    /// <summary>
    /// Builds a translation pack from a folder of translated XML files plus
    /// some metadata. Computes hashes automatically (originalHash from the
    /// install's _originals snapshot, translatedHash from the files the
    /// translator provides). The output is a ready-to-upload .zip + a JSON
    /// snippet for translations-index.json.
    /// </summary>
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
            if (!Directory.Exists(inputs.TranslatedFolder))
                return ExportFail($"Translated folder doesn't exist: {inputs.TranslatedFolder}");

            // Resolve the originals folder: explicit path wins, otherwise
            // fall back to the launcher-managed snapshot. We need at least
            // one of the two — without originals there are no originalHash
            // values to put in the manifest.
            string originalsRoot;
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
                    "point the dialog at your own backup folder.");
            }

            // ---- For each covered file, gather hashes ----
            var manifestFiles = new List<TranslationFile>();
            int filesIncluded = 0;
            foreach (var rel in CoveredFiles)
            {
                var fileName = Path.GetFileName(rel);
                var translatedPath = Path.Combine(inputs.TranslatedFolder, fileName);
                var originalPath = Path.Combine(originalsRoot, fileName);

                // Skip files the translator didn't provide — pack only what's there.
                if (!File.Exists(translatedPath))
                {
                    DiagnosticLog.Write($"Export: skipping {fileName} (not in translator folder)");
                    continue;
                }
                if (!File.Exists(originalPath))
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

                // 2. Copy the translated files
                foreach (var file in manifestFiles)
                {
                    var fileName = Path.GetFileName(file.Path.Replace('/', Path.DirectorySeparatorChar));
                    File.Copy(
                        Path.Combine(inputs.TranslatedFolder, fileName),
                        Path.Combine(stagingFolder, fileName));
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

            // ---- Also write translation.json as a sibling next to the zip ----
            // This is the file the launcher's registry service reads directly
            // when listing GitHub releases — the translator uploads both
            // wol-<id>.zip AND translation.json as separate assets, and the
            // launcher discovers the pack with no central index needed.
            string? siblingJsonPath = null;
            try
            {
                var outputDir = Path.GetDirectoryName(inputs.OutputZipPath);
                if (!string.IsNullOrEmpty(outputDir))
                {
                    siblingJsonPath = Path.Combine(outputDir, TranslationManifest.ManifestFileName);
                    File.WriteAllText(siblingJsonPath, manifestJson);
                }
            }
            catch (Exception ex)
            {
                // Non-fatal: the zip was created successfully; the translator
                // can still extract translation.json from it manually.
                DiagnosticLog.Write($"Export: could not write sibling translation.json: {ex.Message}");
                siblingJsonPath = null;
            }

            var zipSize = new FileInfo(inputs.OutputZipPath).Length;
            var indexSnippet = BuildIndexEntrySnippet(manifest, zipSize);

            DiagnosticLog.Write(
                $"Export: created '{inputs.OutputZipPath}' " +
                $"({filesIncluded} files, {zipSize} bytes) for translation '{manifest.Id}' v{manifest.Version}.");

            return new ExportResult(true, inputs.OutputZipPath, zipSize, siblingJsonPath, indexSnippet, null);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Export failed: {ex}");
            return ExportFail(ex.Message);
        }
    }

    private static ExportResult ExportFail(string err) =>
        new(false, null, 0, null, null, err);

    /// <summary>
    /// Renders a copy-pasteable JSON entry for translations-index.json. The
    /// downloadUrl is left as a placeholder string — the maintainer fills it
    /// in after they upload the zip to GitHub releases.
    /// </summary>
    private static string BuildIndexEntrySnippet(TranslationManifest manifest, long zipSize)
    {
        var entry = new TranslationIndexEntry
        {
            Id = manifest.Id,
            Name = manifest.Name,
            Language = manifest.Language,
            Author = manifest.Author,
            Version = manifest.Version,
            CompatibleWith = manifest.CompatibleWith,
            DownloadUrl = "REPLACE_WITH_GITHUB_RELEASE_URL",
            Size = zipSize,
            Description = manifest.Description,
        };
        return JsonSerializer.Serialize(entry, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });
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
