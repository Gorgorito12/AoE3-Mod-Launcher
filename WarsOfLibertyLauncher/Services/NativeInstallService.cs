using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Progress reported during the native installation.
/// </summary>
public record NativeInstallProgress(
    double Percentage,
    long BytesDone,
    long BytesTotal,
    string CurrentStep,
    string CurrentFile,
    double BytesPerSecond);

/// <summary>
/// Distinct stages of the install pipeline. The UI uses this to render a
/// breadcrumb across the top of the progress block, change the speed label
/// from "Download" to "Copy" / "Files" depending on what's actually being
/// measured, and weight overall progress correctly.
/// </summary>
public enum InstallPhase
{
    /// <summary>Idle / not started.</summary>
    None,
    /// <summary>Downloading the ZIP parts from GitHub.</summary>
    Download,
    /// <summary>Concatenating + extracting the combined ZIP to temp.</summary>
    Extract,
    /// <summary>Cloning AoE3 (and flattening bin\) to the destination.</summary>
    Clone,
    /// <summary>Copying the WoL mod files on top of the clone.</summary>
    ModOverlay,
    /// <summary>Shortcuts, registry, manifest, verification.</summary>
    Finalize,
    /// <summary>Install completed (used so the UI can mark every dot done).</summary>
    Complete,
}

/// <summary>
/// Replaces Inno Setup entirely. Performs a full Wars of Liberty installation
/// natively in C#:
///   1. Downloads the WoL payload ZIP parts and concatenates them
///   2. Extracts to a temp folder
///   3. Clones AoE3:TAD from source to destination
///   4. Copies WoL mod files on top of the clone
///   5. Creates Start Menu and Desktop shortcuts
///   6. Writes registry entries for detection by the updater
///
/// The payload ZIP is split into multiple parts (.zip.001, .zip.002, ...)
/// to work around GitHub's 2 GB file size limit.
///
/// **This is the single canonical install entry-point for every mod**
/// — Wars of Liberty (multi-part WolPatcher payloads), Improvement Mod
/// (single-asset GitHub Releases), and any future mod profile all flow
/// through <see cref="InstallAsync"/> for fresh installs and through
/// <see cref="InstallModOnlyAsync"/> for repair / mod-only overlays.
/// Keeping a single pipeline guarantees every mod gets the same
/// guarantees: sibling-mod exclusion during clone, payload SHA-256
/// verification when the catalog pins one, atomic backup-and-rollback
/// on extract, and consistent manifest / registry / shortcut writes.
/// </summary>
public class NativeInstallService
{
    /// <summary>
    /// Fallback publisher when the profile doesn't declare an author.
    /// Generic on purpose — community mods rarely set a "Publisher" string
    /// and this keeps Add/Remove Programs entries from looking empty.
    /// </summary>
    private const string DefaultPublisher = "AoE3 Mod";

    private readonly DownloadService _downloader;
    private readonly FolderCloneService _cloneService;

    /// <summary>Pause flag — propagated to both downloader and clone service.</summary>
    public bool Pause
    {
        get => _downloader.Pause;
        set
        {
            _downloader.Pause = value;
            _cloneService.Pause = value;
        }
    }

    /// <summary>The clone service, exposed so the UI can read its progress.</summary>
    public FolderCloneService CloneService => _cloneService;

    public NativeInstallService(DownloadService? downloader = null)
    {
        _downloader = downloader ?? new DownloadService();
        _cloneService = new FolderCloneService();
    }

    /// <summary>Where temp files for installation live.</summary>
    public static string TempDirectory =>
        Path.Combine(Path.GetTempPath(), "WarsOfLibertyLauncher", "native-install");

    /// <summary>
    /// Wipes the native-install temp folder (downloaded ZIP parts, the
    /// combined ZIP, and the extracted payload). Used by the corruption
    /// retry path in the UI: when extraction fails with a corrupt ZIP
    /// we can't trust any of the on-disk bytes for the affected parts,
    /// so the safe thing is to start the next attempt with a clean slate.
    /// </summary>
    /// <remarks>
    /// Failures here are swallowed and logged — if a file is locked
    /// (antivirus scan in progress, Explorer preview, etc.) we'd rather
    /// let the next install attempt fail loudly than throw from a
    /// helper that's meant to be best-effort.
    /// </remarks>
    public static void CleanupTempPayload()
    {
        try
        {
            if (Directory.Exists(TempDirectory))
            {
                Directory.Delete(TempDirectory, recursive: true);
                DiagnosticLog.Write($"Cleaned up native-install temp folder: {TempDirectory}");
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Could not clean up temp folder '{TempDirectory}': {ex.Message}");
        }
    }

    /// <summary>
    /// Full installation pipeline using multiple ZIP part URLs.
    /// </summary>
    /// <param name="profile">
    /// Active mod profile. Drives the names written to shortcuts, registry,
    /// and the install manifest, plus selects the right executable for the
    /// desktop shortcut (<c>age3y.exe</c> for WoL, <c>age3m.exe</c> for IM,
    /// etc.).
    /// </param>
    /// <param name="version">
    /// Free-form version string written into the registry's DisplayVersion
    /// and the install manifest. Caller picks the right source (latest WoL
    /// version, the approved GitHub release tag, etc.).
    /// </param>
    /// <param name="payloadSha256">
    /// Optional. Parallel array to <paramref name="payloadZipUrls"/>:
    /// for each URL at index <c>i</c>, the expected lowercase-hex
    /// SHA-256 of the downloaded part. When provided (non-null and
    /// non-empty at that index), the launcher verifies the downloaded
    /// file before concatenation and throws <see cref="InvalidDataException"/>
    /// on mismatch — the install is aborted, no partial state is left
    /// behind. Null or all-empty values skip verification (current
    /// behaviour, e.g. legacy GitHub-Release downloads where we trust
    /// the asset CDN).
    /// </param>
    public async Task InstallAsync(
        ModProfile profile,
        string version,
        string[] payloadZipUrls,
        string aoe3SourcePath,
        string destinationFolder,
        IProgress<DownloadProgress>? downloadProgress = null,
        IProgress<CloneProgress>? cloneProgress = null,
        IProgress<string>? statusProgress = null,
        IProgress<InstallPhase>? phaseProgress = null,
        IProgress<ExtractProgress>? extractProgress = null,
        IProgress<ModOverlayProgress>? overlayProgress = null,
        string[]? payloadSha256 = null,
        IEnumerable<string>? extraExcludedSubtrees = null,
        CancellationToken ct = default)
    {
        DiagnosticLog.Write($"=== Native Install Start ({profile.DisplayName}) ===");
        DiagnosticLog.Write($"  Parts: {payloadZipUrls.Length}");
        DiagnosticLog.Write($"  AoE3 Source: {aoe3SourcePath}");
        DiagnosticLog.Write($"  Destination: {destinationFolder}");

        // ---- Phase 0: Pre-flight the clone plan BEFORE downloading ----
        // The post-clone gate (Phase 3) also catches a 0-file clone, but only
        // AFTER the multi-GB payload download has finished. Counting up-front
        // (a cheap dir enumeration, no copy) lets a misconfigured AoE3 source or
        // exclusion set fail FAST — saving the user a long download just to abort
        // at the clone step. Same exclusion logic as the real clone, so the count
        // is authoritative.
        int plannedCloneFiles = _cloneService.CountCloneableFiles(
            aoe3SourcePath, destinationFolder, extraExcludedSubtrees, ct);
        if (plannedCloneFiles == 0)
        {
            DiagnosticLog.Write(
                $"ABORT (pre-flight): cloning '{aoe3SourcePath}' would copy 0 files — " +
                "aborting BEFORE download (source missing/empty or fully excluded).");
            throw new InstallBaseGameMissingException(aoe3SourcePath);
        }
        DiagnosticLog.Write($"  Pre-flight: clone would copy {plannedCloneFiles} base files.");

        // ---- Phase 1: Download all parts and concatenate ----
        phaseProgress?.Report(InstallPhase.Download);
        statusProgress?.Report($"Downloading {profile.DisplayName} files...");
        var zipPath = await DownloadAndConcatenatePartsAsync(
            payloadZipUrls, payloadSha256, downloadProgress, statusProgress, ct);

        // ---- Phase 2: Extract payload to temp ----
        phaseProgress?.Report(InstallPhase.Extract);
        statusProgress?.Report("Extracting mod files...");
        var extractedFolder = await ExtractPayloadAsync(zipPath, statusProgress, extractProgress, ct);

        // ---- Phase 3: Clone AoE3 to destination ----
        // extraExcludedSubtrees carries the install paths of every OTHER
        // mod profile (e.g. when installing Improvement Mod, the user's
        // existing Wars of Liberty install path is passed here). Without
        // it, AoE3 clones nested mod folders into the new install — see
        // the "improvement mod copies wars of liberty" regression.
        phaseProgress?.Report(InstallPhase.Clone);
        statusProgress?.Report("Copying Age of Empires III files...");
        int clonedFiles = await _cloneService.CloneAsync(aoe3SourcePath, destinationFolder, cloneProgress, ct,
            extraExcludedSubtrees: extraExcludedSubtrees);

        // Integrity gate: a full clone install MUST copy the AoE3 base (it's
        // thousands of files). If the clone produced NOTHING, the source is
        // missing/empty or an exclusion removed it (the canonical case: the
        // stock-game `…\bin` path landing in the sibling-exclusion list — see
        // LauncherConfig.GetSiblingInstallPaths' IsStockGame guard). Abort
        // LOUDLY here — BEFORE overlay / registry / shortcuts — instead of
        // shipping a mod overlaid on an empty base that can't launch (missing
        // engine DLLs + data\*.xml; the game just exits with "RockallDLL.dll
        // not found"). The install flow catches this and shows a clear,
        // localized error; retrying wouldn't help (it's a config/source issue).
        if (clonedFiles == 0)
        {
            DiagnosticLog.Write(
                $"ABORT: AoE3 clone copied 0 files from '{aoe3SourcePath}' — " +
                "not overlaying the mod onto an empty base game.");
            throw new InstallBaseGameMissingException(aoe3SourcePath);
        }

        // ---- Phase 3b: Flatten bin\ subfolder if present ----
        // (Still part of the Clone phase from the UI's perspective.)
        FlattenBinSubfolder(destinationFolder, statusProgress);

        // ---- Phase 4: Copy mod files on top ----
        phaseProgress?.Report(InstallPhase.ModOverlay);
        statusProgress?.Report($"Installing {profile.DisplayName} mod files...");
        await CopyPayloadToDestinationAsync(extractedFolder, destinationFolder, statusProgress, overlayProgress, ct);

        // ---- Phase 5: Finalize (shortcuts, registry, manifest) ----
        phaseProgress?.Report(InstallPhase.Finalize);
        statusProgress?.Report("Creating shortcuts...");
        var shortcuts = CreateShortcuts(profile, destinationFolder, out var startMenuFolder);

        statusProgress?.Report("Writing registry entries...");
        WriteRegistryEntries(profile, version, destinationFolder);

        WriteManifest(profile, version, destinationFolder, aoe3SourcePath, clonedAoe3: true,
            shortcuts, startMenuFolder);

        // Translation snapshot is WoL-specific (it relies on the WoL-style
        // <c>data\stringtabley.xml</c> and <c>unithelpstringsy.xml</c> layout
        // for hash-based version detection). Skip it for other profiles to
        // avoid scary error logs about missing files.
        if (profile.Translations != null && profile.Translations.CoveredFiles.Count > 0)
        {
            try { new TranslationService(destinationFolder).RefreshOriginalsSnapshot(); }
            catch (Exception ex) { DiagnosticLog.Write($"Translations snapshot failed: {ex.Message}"); }
        }

        // ---- Phase 5b: Remove stale precompiled XMB files ----
        // The legacy WolPayload.zip ships .xml.xmb files alongside
        // their .xml originals. AoE3 prefers .xmb when present (it's
        // the parsed binary form, faster to load) and falls back to
        // re-compiling from .xml when the .xmb is missing.
        //
        // Two reasons we strip the shipped .xmb:
        //   1. They're built against an older revision of the .xml.
        //      Patches update the .xml but NOT the .xmb, so post-
        //      patch the .xmb is stale and represents a different
        //      game state than what the .xml describes.
        //   2. AoE3's LAN "version match" handshake hashes the .xmb
        //      bytes. A peer who installed via the original installer
        //      (no .xmb shipped — gets generated at first run from
        //      the patched .xml) ends up with a DIFFERENT .xmb hash,
        //      so the two installs can't see each other on LAN.
        //
        // Removing the shipped .xmb files (plus a small set of known
        // dev-leftover files we found by diffing canonical vs
        // launcher installs) lets AoE3 regenerate them fresh on
        // first launch, matching what original-installer peers have.
        RemoveStaleBuildArtifacts(profile, destinationFolder);

        // ---- Phase 6: Post-install integrity dump ----
        // Counts + MD5s of the three files AoE3 uses for its version
        // matching, plus a per-subfolder file count breakdown. This
        // is purely diagnostic — it doesn't change install behaviour.
        // When users report "version mismatch" against a peer who
        // installed manually, the log lets us diff their install
        // against the canonical layout without asking them to ship
        // gigabytes of game files.
        LogInstallIntegritySnapshot(profile, destinationFolder);

        phaseProgress?.Report(InstallPhase.Complete);
        DiagnosticLog.Write($"=== Native Install Complete ({profile.DisplayName}) ===");
    }

    /// <summary>
    /// Strip files that ship in the WoL payload zip but cause LAN
    /// version-mismatch errors when the launcher install meets a
    /// peer who installed via the legacy Inno installer.
    ///
    /// Two categories:
    ///
    ///   * <b>Precompiled <c>.xml.xmb</c> files</b> — binary
    ///     copies of XML game data (protoy.xml.xmb,
    ///     techtreey.xml.xmb, stringtabley.xml.xmb, ui*.xml.xmb).
    ///     The payload zip ships them precompiled against an older
    ///     revision of the .xml. Patches refresh the .xml but not
    ///     the .xmb, leaving the install with mismatched pairs.
    ///     AoE3 hashes the .xmb for LAN match — peers regenerated
    ///     from the patched .xml get a different hash, blocking
    ///     the connection. Deleting them lets AoE3 rebuild fresh
    ///     on first launch.
    ///
    ///   * <b>Dev leftovers</b> — files with garbled Unicode names
    ///     (Portuguese "cópia" mis-encoded as "c¢pia" / "cã³pia"),
    ///     `.bak` backups, and files without extensions
    ///     (`nativebolasmatrero`). These slipped into the bootstrap
    ///     zip but aren't part of any released WoL patch; the
    ///     canonical install doesn't have them.
    ///
    /// Only runs for the WoL profile; other mods aren't affected.
    /// Idempotent — re-running on an already-clean folder is a no-op.
    /// </summary>
    public static void RemoveStaleBuildArtifacts(ModProfile profile, string destinationFolder)
    {
        // Currently only the WoL payload is known to ship stale
        // precompiled .xmb files. Other mods that route through this
        // pipeline (Improvement Mod, future ones) might legitimately
        // need their own .xmb files — keep them as-is.
        if (!string.Equals(profile.Id, ModRegistry.WolId, StringComparison.OrdinalIgnoreCase))
            return;

        var dataFolder = Path.Combine(destinationFolder, "data");
        if (!Directory.Exists(dataFolder)) return;

        int removedXmb = 0;
        int removedJunk = 0;
        long bytesFreed = 0;

        // 1) Strip every .xml.xmb. AoE3 will regenerate them from
        //    the .xml at first launch and they'll match what other
        //    peers' installs produce.
        try
        {
            foreach (var xmb in Directory.EnumerateFiles(dataFolder, "*.xml.xmb", SearchOption.AllDirectories))
            {
                try
                {
                    var len = new FileInfo(xmb).Length;
                    File.Delete(xmb);
                    removedXmb++;
                    bytesFreed += len;
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Write($"  RemoveStaleBuildArtifacts: could not delete '{xmb}': {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"RemoveStaleBuildArtifacts: enumerating .xmb in '{dataFolder}' failed: {ex.Message}");
        }

        // 2) Strip .bak files (XML backups left behind by editor
        //    tools) and known dev-leftover file names. Be defensive
        //    with the EnumerateFiles call — corrupt filenames have
        //    been known to throw here on Windows.
        try
        {
            foreach (var bak in Directory.EnumerateFiles(dataFolder, "*.bak", SearchOption.AllDirectories))
            {
                try
                {
                    var len = new FileInfo(bak).Length;
                    File.Delete(bak);
                    removedJunk++;
                    bytesFreed += len;
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Write($"  RemoveStaleBuildArtifacts: could not delete '{bak}': {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"RemoveStaleBuildArtifacts: enumerating .bak in '{dataFolder}' failed: {ex.Message}");
        }

        // 3) Named-file blacklist — observed only-in-launcher files
        //    that aren't part of the canonical install. Hard-coded
        //    here because they have no common pattern. We also use
        //    fuzzy detection below for the cases where the exact
        //    filename has mojibake characters that don't survive a
        //    source-file → .exe round-trip cleanly.
        string[] devLeftovers = new[]
        {
            // Bayonet conscript tactics — typo in filename
            // ("consripto" vs "conscripto"); not referenced from
            // any techtree we checked.
            @"data\tactics\musketbayonetconsripto.tactics",
        };
        foreach (var rel in devLeftovers)
        {
            var full = Path.Combine(destinationFolder, rel);
            if (!File.Exists(full)) continue;
            try
            {
                var len = new FileInfo(full).Length;
                File.Delete(full);
                removedJunk++;
                bytesFreed += len;
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"  RemoveStaleBuildArtifacts: could not delete '{rel}': {ex.Message}");
            }
        }

        // 4) Fuzzy detection inside data\tactics\ — catches the
        //    Portuguese "cópia" duplicates (mojibake-encoded as
        //    "C¢pia"/"CÃ³pia") and orphan files without any
        //    extension. Using a pattern scan instead of hardcoded
        //    paths because the non-ASCII characters in the filenames
        //    don't survive cleanly through the source file → CLR
        //    string → Win32 file-system roundtrip — File.Exists on a
        //    hardcoded path matches the wrong bytes. Globbing the
        //    folder reads the actual on-disk names so we don't care
        //    about encoding.
        var tacticsFolder = Path.Combine(destinationFolder, "data", "tactics");
        if (Directory.Exists(tacticsFolder))
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(tacticsFolder, "*",
                             SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileName(f);

                    // Pattern A: " - C{something}pia.tactics" — the
                    // Portuguese "cópia" duplicates regardless of how
                    // the non-ASCII byte renders. Anchored to .tactics
                    // so we don't accidentally match a legitimate
                    // tactic that happens to contain "pia".
                    bool isCopiaDuplicate =
                        name.IndexOf(" - C", StringComparison.OrdinalIgnoreCase) >= 0
                        && name.EndsWith("pia.tactics", StringComparison.OrdinalIgnoreCase);

                    // Pattern B: any file in data\tactics\ with no
                    // extension at all. Legitimate tactics always end
                    // in .tactics — anything else is an orphan from a
                    // dev machine's accidental save.
                    bool hasNoExtension = string.IsNullOrEmpty(Path.GetExtension(f));

                    if (!isCopiaDuplicate && !hasNoExtension) continue;

                    try
                    {
                        var len = new FileInfo(f).Length;
                        File.Delete(f);
                        removedJunk++;
                        bytesFreed += len;
                    }
                    catch (Exception ex)
                    {
                        DiagnosticLog.Write(
                            $"  RemoveStaleBuildArtifacts: could not delete '{name}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write(
                    $"RemoveStaleBuildArtifacts: enumerating data\\tactics failed: {ex.Message}");
            }
        }

        // 5) Heavier dev-leftover sweeps observed in the WoL payload
        //    but absent from a setup+updater install. Each sweep is
        //    surgical (specific subtree + specific file pattern) so
        //    we don't risk deleting legitimate game assets via a
        //    too-broad glob.
        //
        //    a) art\WoL\interns\* — internal dev scratch folder. The
        //       canonical install has no such folder; the payload zip
        //       included it from someone's working copy. ~hundreds of
        //       .ddt files that AoE3 still ends up hashing, which
        //       causes multiplayer desync vs peers without them.
        //
        //    b) art\**\*.rar — uncompressed archive files. A legit
        //       WoL install never ships .rar inside art\ (assets are
        //       loose .ddt / .tga / .xml). The payload happens to
        //       contain an unextracted "DLC.rar" which is dead weight
        //       at best, sync-breaker at worst.
        //
        //    c) Sound\WoL\**\*(enhanced)*.wav — "enhanced" variants
        //       of base sounds. Duplicates the .wav of the same name
        //       without the "(enhanced)" suffix. The canonical
        //       install only keeps the non-suffixed version.
        var artFolder = Path.Combine(destinationFolder, "art");
        if (Directory.Exists(artFolder))
        {
            // a) Whole-subtree removal of art\WoL\interns\
            var internsFolder = Path.Combine(artFolder, "WoL", "interns");
            if (Directory.Exists(internsFolder))
            {
                try
                {
                    long internsBytes = 0;
                    int internsFiles = 0;
                    foreach (var f in Directory.EnumerateFiles(internsFolder, "*", SearchOption.AllDirectories))
                    {
                        try { internsBytes += new FileInfo(f).Length; internsFiles++; }
                        catch { /* ignore size probe failures */ }
                    }
                    Directory.Delete(internsFolder, recursive: true);
                    removedJunk += internsFiles;
                    bytesFreed += internsBytes;
                    DiagnosticLog.Write(
                        $"  RemoveStaleBuildArtifacts: removed art\\WoL\\interns\\ ({internsFiles} files, {FormatBytes(internsBytes)})");
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Write(
                        $"  RemoveStaleBuildArtifacts: could not remove art\\WoL\\interns: {ex.Message}");
                }
            }

            // b) Loose .rar inside art\ — never legitimate.
            try
            {
                foreach (var rar in Directory.EnumerateFiles(artFolder, "*.rar", SearchOption.AllDirectories))
                {
                    try
                    {
                        var len = new FileInfo(rar).Length;
                        File.Delete(rar);
                        removedJunk++;
                        bytesFreed += len;
                    }
                    catch (Exception ex)
                    {
                        DiagnosticLog.Write(
                            $"  RemoveStaleBuildArtifacts: could not delete '{rar}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write(
                    $"RemoveStaleBuildArtifacts: enumerating *.rar under art\\ failed: {ex.Message}");
            }
        }

        // c) "(enhanced)" wav duplicates under Sound\WoL\
        var soundWol = Path.Combine(destinationFolder, "Sound", "WoL");
        if (Directory.Exists(soundWol))
        {
            try
            {
                foreach (var wav in Directory.EnumerateFiles(soundWol, "*.wav", SearchOption.AllDirectories))
                {
                    var name = Path.GetFileName(wav);
                    if (name.IndexOf("(enhanced)", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    try
                    {
                        var len = new FileInfo(wav).Length;
                        File.Delete(wav);
                        removedJunk++;
                        bytesFreed += len;
                    }
                    catch (Exception ex)
                    {
                        DiagnosticLog.Write(
                            $"  RemoveStaleBuildArtifacts: could not delete '{name}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write(
                    $"RemoveStaleBuildArtifacts: enumerating Sound\\WoL\\ failed: {ex.Message}");
            }
        }

        if (removedXmb > 0 || removedJunk > 0)
        {
            DiagnosticLog.Write(
                $"RemoveStaleBuildArtifacts ({profile.Id}): " +
                $"removed {removedXmb} .xml.xmb + {removedJunk} other junk files " +
                $"({FormatBytes(bytesFreed)} freed). " +
                $"AoE3 will regenerate .xmb files from .xml on first launch.");
        }
        else
        {
            DiagnosticLog.Write($"RemoveStaleBuildArtifacts ({profile.Id}): no stale artifacts found (install is clean).");
        }
    }

    /// <summary>
    /// Walk the destination folder after install and write to the log:
    ///   * total file count + total bytes
    ///   * file count per top-level subdirectory (data/, art/, sound/, etc.)
    ///   * MD5 hashes of the three files AoE3 keys its LAN-version match on:
    ///     <c>data\protoy.xml</c>, <c>data\techtreey.xml</c>,
    ///     <c>data\stringtabley.xml</c>
    ///   * Whether the legacy <c>bin\</c> subfolder is still present
    ///     (it shouldn't be — FlattenBinSubfolder removes it)
    ///
    /// Users can paste this snapshot into a bug report when their
    /// install rejects a peer's "version mismatch", letting us
    /// compare hashes against the canonical 1.2.0c2 layout.
    /// </summary>
    private static void LogInstallIntegritySnapshot(ModProfile profile, string destinationFolder)
    {
        try
        {
            DiagnosticLog.Write($"--- Install integrity snapshot for '{profile.Id}' at '{destinationFolder}' ---");
            if (!Directory.Exists(destinationFolder))
            {
                DiagnosticLog.Write("  (destination folder doesn't exist — install may have failed silently)");
                return;
            }

            long totalBytes = 0;
            int totalFiles = 0;
            var perTopFolder = new Dictionary<string, (int Files, long Bytes)>(StringComparer.OrdinalIgnoreCase);

            foreach (var f in Directory.EnumerateFiles(destinationFolder, "*", SearchOption.AllDirectories))
            {
                FileInfo fi;
                try { fi = new FileInfo(f); }
                catch { continue; }
                totalFiles++;
                totalBytes += fi.Length;

                // Identify the top-level subfolder (or "<root>" if the
                // file is directly under destinationFolder).
                var rel = Path.GetRelativePath(destinationFolder, f);
                var firstSep = rel.IndexOfAny(new[] { '\\', '/' });
                var topName = firstSep < 0 ? "<root>" : rel.Substring(0, firstSep);
                if (!perTopFolder.TryGetValue(topName, out var agg))
                    agg = (0, 0L);
                perTopFolder[topName] = (agg.Files + 1, agg.Bytes + fi.Length);
            }

            DiagnosticLog.Write($"  total: {totalFiles} files, {FormatBytes(totalBytes)}");
            foreach (var kv in perTopFolder.OrderByDescending(k => k.Value.Files))
            {
                DiagnosticLog.Write($"    {kv.Key}/  {kv.Value.Files} files  {FormatBytes(kv.Value.Bytes)}");
            }

            // MD5 of AoE3's version-key files. These are the ones the
            // legacy WoL Java updater computes against UpdateInfo.xml
            // to match an install to a release; LAN matchmaking in
            // age3y.exe uses the same hashes for peer compatibility.
            string[] keyFiles = new[]
            {
                @"data\protoy.xml",
                @"data\techtreey.xml",
                @"data\stringtabley.xml",
            };
            using var md5 = System.Security.Cryptography.MD5.Create();
            foreach (var rel in keyFiles)
            {
                var full = Path.Combine(destinationFolder, rel);
                if (!File.Exists(full))
                {
                    DiagnosticLog.Write($"  MD5  {rel} = (MISSING)");
                    continue;
                }
                try
                {
                    using var fs = File.OpenRead(full);
                    var hash = md5.ComputeHash(fs);
                    var hex = Convert.ToHexString(hash).ToLowerInvariant();
                    DiagnosticLog.Write($"  MD5  {rel} = {hex}");
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Write($"  MD5  {rel} = (error: {ex.Message})");
                }
            }

            // bin\ shouldn't be there after FlattenBinSubfolder. If it
            // is, we kept a duplicate copy of part of AoE3 — explains
            // a few GB of extra size but doesn't affect LAN matching.
            var leftoverBin = Path.Combine(destinationFolder, "bin");
            if (Directory.Exists(leftoverBin))
            {
                var binCount = Directory.EnumerateFiles(leftoverBin, "*", SearchOption.AllDirectories).Count();
                DiagnosticLog.Write($"  WARNING: leftover bin/ subfolder with {binCount} files — FlattenBinSubfolder did not clean it up.");
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Install integrity snapshot failed: {ex.Message}");
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    /// <summary>
    /// Lighter install: skips AoE3 clone. Use this when the destination folder
    /// already contains AoE3 (e.g., user picked an existing AoE3 folder as dest).
    /// </summary>
    /// <param name="payloadSha256">
    /// Optional. Parallel array to <paramref name="payloadZipUrls"/> — see
    /// <see cref="InstallAsync"/> for semantics.
    /// </param>
    public async Task InstallModOnlyAsync(
        ModProfile profile,
        string version,
        string[] payloadZipUrls,
        string destinationFolder,
        IProgress<DownloadProgress>? downloadProgress = null,
        IProgress<string>? statusProgress = null,
        IProgress<InstallPhase>? phaseProgress = null,
        IProgress<ExtractProgress>? extractProgress = null,
        IProgress<ModOverlayProgress>? overlayProgress = null,
        string[]? payloadSha256 = null,
        CancellationToken ct = default)
    {
        DiagnosticLog.Write($"=== Native Install (mod-only) Start ({profile.DisplayName}) ===");
        DiagnosticLog.Write($"  Parts: {payloadZipUrls.Length}");
        DiagnosticLog.Write($"  Destination: {destinationFolder}");

        // ---- Phase 1: Download ----
        phaseProgress?.Report(InstallPhase.Download);
        statusProgress?.Report($"Downloading {profile.DisplayName} files...");
        var zipPath = await DownloadAndConcatenatePartsAsync(
            payloadZipUrls, payloadSha256, downloadProgress, statusProgress, ct);

        // ---- Phase 2: Extract ----
        phaseProgress?.Report(InstallPhase.Extract);
        statusProgress?.Report("Extracting mod files...");
        var extractedFolder = await ExtractPayloadAsync(zipPath, statusProgress, extractProgress, ct);

        // ---- Phase 3: Copy mod on top (no Clone phase in mod-only) ----
        phaseProgress?.Report(InstallPhase.ModOverlay);
        statusProgress?.Report($"Installing {profile.DisplayName} mod files...");
        await CopyPayloadToDestinationAsync(extractedFolder, destinationFolder, statusProgress, overlayProgress, ct);

        // ---- Phase 4: Finalize ----
        phaseProgress?.Report(InstallPhase.Finalize);
        statusProgress?.Report("Creating shortcuts...");
        var shortcuts = CreateShortcuts(profile, destinationFolder, out var startMenuFolder);
        WriteRegistryEntries(profile, version, destinationFolder);

        WriteManifest(profile, version, destinationFolder, aoe3SourcePath: null, clonedAoe3: false,
            shortcuts, startMenuFolder);

        // Translation snapshot only applies to mods that opt into the WoL-
        // style translation overlay system (CoveredFiles non-empty).
        if (profile.Translations != null && profile.Translations.CoveredFiles.Count > 0)
        {
            try { new TranslationService(destinationFolder).RefreshOriginalsSnapshot(); }
            catch (Exception ex) { DiagnosticLog.Write($"Translations snapshot failed: {ex.Message}"); }
        }

        phaseProgress?.Report(InstallPhase.Complete);
        DiagnosticLog.Write($"=== Native Install (mod-only) Complete ({profile.DisplayName}) ===");
    }

    // =========================================================================
    // Implementation
    // =========================================================================

    /// <summary>
    /// Downloads all ZIP parts sequentially and concatenates them into a single
    /// ZIP file. Progress reports accumulated bytes across all parts. When
    /// <paramref name="partSha256"/> is non-null and a non-empty value is
    /// present at the same index as a part URL, the launcher verifies the
    /// downloaded file's SHA-256 before continuing. A mismatch throws
    /// <see cref="InvalidDataException"/> and aborts the install — no
    /// concatenation, no extract. Empty / missing values for a given index
    /// skip verification for that part (preserves backwards-compatibility
    /// with downloads that don't pin a hash, e.g. legacy GitHub assets).
    /// </summary>
    private async Task<string> DownloadAndConcatenatePartsAsync(
        string[] partUrls,
        string[]? partSha256,
        IProgress<DownloadProgress>? progress,
        IProgress<string>? statusProgress,
        CancellationToken ct)
    {
        Directory.CreateDirectory(TempDirectory);

        var combinedZipPath = Path.Combine(TempDirectory, "WolPayload.zip");

        // Delete any previous combined file
        try { if (File.Exists(combinedZipPath)) File.Delete(combinedZipPath); } catch { }

        DiagnosticLog.Write($"Downloading {partUrls.Length} ZIP parts...");

        // Pre-compute the total size across all parts via HEAD requests so the
        // progress bar has a real denominator from the very first byte. Without
        // this the bar can stay invisible during the first download because GitHub
        // doesn't always send Content-Length on the GET for releases.
        statusProgress?.Report("Checking download size...");
        var partSizes = new long[partUrls.Length];
        long totalAcrossAllParts = 0;
        bool sizesKnown = true;
        for (int i = 0; i < partUrls.Length; i++)
        {
            partSizes[i] = await _downloader.TryGetRemoteSizeAsync(partUrls[i], ct);
            if (partSizes[i] <= 0) { sizesKnown = false; }
            else { totalAcrossAllParts += partSizes[i]; }
        }
        if (sizesKnown)
            DiagnosticLog.Write($"Pre-computed download total: {totalAcrossAllParts} bytes");
        else
            DiagnosticLog.Write("Could not pre-compute download total (HEAD not supported); progress will be approximate.");

        long totalDownloaded = 0;

        for (int i = 0; i < partUrls.Length; i++)
        {
            ct.ThrowIfCancellationRequested();

            var partUrl = partUrls[i];
            var partFileName = $"WolPayload.zip.{(i + 1):D3}";
            var partPath = Path.Combine(TempDirectory, partFileName);

            DiagnosticLog.Write($"  Part {i + 1}/{partUrls.Length}: {partUrl}");
            statusProgress?.Report($"Downloading part {i + 1} of {partUrls.Length}...");

            // Wrap progress to show overall across all parts. Always pass the
            // pre-computed total so the bar fills smoothly across all parts.
            long partStartBytes = totalDownloaded;
            long globalTotal = totalAcrossAllParts;
            var partProgress = new Progress<DownloadProgress>(p =>
            {
                long bytesSoFar = partStartBytes + p.BytesReceived;
                double pct = globalTotal > 0
                    ? Math.Min(100.0, (double)bytesSoFar / globalTotal * 100.0)
                    : 0;
                progress?.Report(new DownloadProgress(
                    BytesReceived: bytesSoFar,
                    TotalBytes: globalTotal,
                    Percentage: pct));
            });

            await _downloader.DownloadFileAsync(partUrl, partPath, partProgress, ct);

            totalDownloaded += new FileInfo(partPath).Length;
            DiagnosticLog.Write($"  Part {i + 1} done ({new FileInfo(partPath).Length} bytes).");

            // Verify SHA-256 if the catalog pinned one for this part.
            // Done immediately after download (not after concat) so a
            // tampered part trips the abort before we waste cycles on
            // I/O for the remaining parts. Empty / missing pin = skip
            // (legacy GitHub asset path).
            var expectedSha = partSha256 != null && i < partSha256.Length
                ? (partSha256[i] ?? "").Trim().ToLowerInvariant()
                : "";
            if (!string.IsNullOrEmpty(expectedSha))
            {
                statusProgress?.Report($"Verifying part {i + 1} of {partUrls.Length}...");
                var actualSha = await HashService.ComputeSha256Async(partPath, ct);
                if (!string.Equals(actualSha, expectedSha, StringComparison.OrdinalIgnoreCase))
                {
                    // Wipe the bad part so a retry doesn't pick it up
                    // from cache. Keep earlier parts — they passed
                    // their own verifications and a manual retry can
                    // resume from this index.
                    try { File.Delete(partPath); }
                    catch (Exception ex)
                    {
                        DiagnosticLog.Write($"Could not delete tampered part '{partPath}': {ex.Message}");
                    }

                    throw new InvalidDataException(
                        $"Payload verification failed for part {i + 1}: " +
                        $"expected SHA-256 '{expectedSha}' but got '{actualSha}'. " +
                        $"The downloaded file does not match the hash approved in the catalog — " +
                        $"the host may have been compromised or the download corrupted.");
                }
                DiagnosticLog.Write($"  Part {i + 1} SHA-256 verified.");
            }
        }

        // Concatenate all parts into one ZIP
        statusProgress?.Report("Combining downloaded parts...");
        DiagnosticLog.Write("Concatenating parts into single ZIP...");
        await ConcatenateFilesAsync(partUrls.Length, combinedZipPath, ct);

        DiagnosticLog.Write($"Combined ZIP: {new FileInfo(combinedZipPath).Length} bytes.");
        return combinedZipPath;
    }

    /// <summary>
    /// Concatenates the downloaded part files (.001, .002, ...) into a single file.
    /// </summary>
    private async Task ConcatenateFilesAsync(int partCount, string outputPath, CancellationToken ct)
    {
        await using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write,
            FileShare.None, 81920, useAsync: true);

        var buffer = new byte[81920];

        for (int i = 1; i <= partCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            var partPath = Path.Combine(TempDirectory, $"WolPayload.zip.{i:D3}");

            await using var input = new FileStream(partPath, FileMode.Open, FileAccess.Read,
                FileShare.Read, 81920, useAsync: true);

            int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
            }
        }
    }

    /// <summary>
    /// Progress reported during extraction. Carries enough info for the UI
    /// to render a real progress bar (not just a status string) and a
    /// "X files/s" speed indicator.
    /// </summary>
    public record ExtractProgress(
        int EntriesDone,
        int EntriesTotal,
        long BytesDone,
        long BytesTotal);

    private Task<string> ExtractPayloadAsync(
        string zipPath,
        IProgress<string>? statusProgress,
        IProgress<ExtractProgress>? extractProgress,
        CancellationToken ct)
    {
        return Task.Run(() =>
        {
            var extractFolder = Path.Combine(TempDirectory, "extracted");

            if (Directory.Exists(extractFolder))
            {
                try { Directory.Delete(extractFolder, recursive: true); } catch { }
            }
            Directory.CreateDirectory(extractFolder);

            DiagnosticLog.Write($"Extracting payload to: {extractFolder}");

            using var archive = ZipFile.OpenRead(zipPath);
            int total = archive.Entries.Count;
            // Pre-compute total uncompressed bytes so the bar has an honest
            // denominator — not the compressed size of the .zip on disk.
            long bytesTotal = 0;
            foreach (var e in archive.Entries) bytesTotal += e.Length;

            int done = 0;
            long bytesDone = 0;

            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                done++;

                if (done == 1 || done % 50 == 0 || done == total)
                {
                    statusProgress?.Report($"Extracting mod files ({done}/{total})...");
                    extractProgress?.Report(new ExtractProgress(done, total, bytesDone, bytesTotal));
                }

                if (string.IsNullOrEmpty(entry.Name)) continue;

                // Zip-slip defence: reject entries whose resolved path would
                // escape extractFolder (a crafted "..\..\foo" entry). The
                // launcher runs elevated, so an unguarded payload could write
                // anywhere on disk. Mirrors ArchiveService.ExtractZipWithBackupAsync.
                var destPath = Path.GetFullPath(Path.Combine(extractFolder, entry.FullName));
                var extractRoot = Path.GetFullPath(extractFolder)
                    .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!destPath.StartsWith(extractRoot, StringComparison.OrdinalIgnoreCase))
                {
                    DiagnosticLog.Write(
                        $"Zip-slip: rejecting entry '{entry.FullName}' that would escape '{extractFolder}'.");
                    continue;
                }
                var destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destDir))
                    Directory.CreateDirectory(destDir);

                entry.ExtractToFile(destPath, overwrite: true);
                bytesDone += entry.Length;
            }

            // Final 100% report so the bar tops out cleanly before the next phase.
            extractProgress?.Report(new ExtractProgress(done, total, bytesDone, bytesTotal));

            DiagnosticLog.Write($"Extraction complete: {done} entries.");
            return extractFolder;
        }, ct);
    }

    /// <summary>
    /// If <paramref name="destinationFolder"/> contains a `bin\` subfolder
    /// (Steam layout), copies its contents up to the root next to where the
    /// WoL mod binary will land, then deletes `bin\` itself.
    ///
    /// Why: the Steam layout puts age3y.exe + DLLs inside bin\, but the WoL
    /// mod's binary lives at the root and expects its DLLs alongside it
    /// (legacy retail layout). Promoting bin\* to the root resolves this.
    /// Removing bin\ afterwards saves ~3.7 GB of duplicated files AND
    /// prevents the shortcut creator from picking the wrong age3y.exe
    /// (the unmodded one in bin\) instead of the WoL-patched one at root.
    ///
    /// Existing files at the root are NOT overwritten during the promote
    /// step (the next phase — the WoL payload overlay — handles overrides
    /// explicitly).
    /// </summary>
    public static void FlattenBinSubfolder(string destinationFolder, IProgress<string>? statusProgress)
    {
        var binFolder = Path.Combine(destinationFolder, "bin");
        if (!Directory.Exists(binFolder)) return;

        statusProgress?.Report("Promoting bin/ files to root...");
        DiagnosticLog.Write($"Flattening bin\\ subfolder of '{destinationFolder}' to root...");

        int copied = 0;
        int skippedConflict = 0;
        int skippedError = 0;
        // Log up to N conflicts by full filename so we can spot which
        // files inside bin\ were silently dropped because they
        // overlapped with a root-level filename. Unbounded logging
        // here would balloon the debug file if a developer ever
        // points the clone at a weird source folder.
        const int MaxConflictsLogged = 20;
        int conflictsLogged = 0;

        foreach (var srcFile in Directory.EnumerateFiles(binFolder, "*", SearchOption.AllDirectories))
        {
            // Preserve directory structure relative to bin\ when promoting,
            // so e.g. bin\Microsoft.VC80.CRT\msvcr80.dll goes to
            // <root>\Microsoft.VC80.CRT\msvcr80.dll.
            var relative = Path.GetRelativePath(binFolder, srcFile);
            var destFile = Path.Combine(destinationFolder, relative);

            // Don't clobber whatever's already at the root — that file might
            // be a WoL-provided file from the payload (we run before the
            // payload overlay, but in practice the clone runs first so the
            // root is mostly empty here).
            if (File.Exists(destFile))
            {
                skippedConflict++;
                if (conflictsLogged < MaxConflictsLogged)
                {
                    DiagnosticLog.Write($"  flatten conflict (already at root, skipped): {relative}");
                    conflictsLogged++;
                }
                continue;
            }

            try
            {
                var destDir = Path.GetDirectoryName(destFile);
                if (!string.IsNullOrEmpty(destDir))
                    Directory.CreateDirectory(destDir);
                File.Copy(srcFile, destFile, overwrite: false);
                copied++;
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"  flatten error: {relative} — {ex.Message}");
                skippedError++;
            }
        }

        if (conflictsLogged < skippedConflict)
        {
            DiagnosticLog.Write($"  (… {skippedConflict - conflictsLogged} more conflict-skips omitted)");
        }
        DiagnosticLog.Write(
            $"Flatten bin\\ complete: {copied} files promoted, " +
            $"{skippedConflict} skipped (root collision), {skippedError} skipped (error).");

        // Now drop the bin\ subfolder entirely. It's redundant after the
        // promotion (the files are already at the root) and keeping it
        // confuses the shortcut step which can't tell which age3y.exe to use.
        try
        {
            Directory.Delete(binFolder, recursive: true);
            DiagnosticLog.Write("Removed redundant bin\\ subfolder after flatten.");
        }
        catch (Exception ex)
        {
            // Non-fatal: install can still succeed with bin\ still in place.
            // The shortcut creator will need to handle the duplicate (the
            // WoL-patched age3y.exe at the root takes priority).
            DiagnosticLog.Write($"Could not remove bin\\ after flatten: {ex.Message}");
        }
    }

    /// <summary>
    /// Copies all files from the extracted WoL payload into the destination,
    /// overwriting existing files. This is what puts the mod on top of the
    /// cloned AoE3.
    /// </summary>
    /// <summary>
    /// Progress reported while copying the extracted mod payload onto the
    /// cloned AoE3 destination. Mirrors <see cref="ExtractProgress"/> but for
    /// the overlay step. Bytes are computed from FileInfo.Length so the UI
    /// can show "X / Y MB" alongside file counts.
    /// </summary>
    public record ModOverlayProgress(
        int FilesDone,
        int FilesTotal,
        long BytesDone,
        long BytesTotal);

    private async Task CopyPayloadToDestinationAsync(
        string extractedFolder,
        string destinationFolder,
        IProgress<string>? statusProgress,
        IProgress<ModOverlayProgress>? overlayProgress,
        CancellationToken ct)
    {
        await Task.Run(() =>
        {
            var files = Directory.GetFiles(extractedFolder, "*", SearchOption.AllDirectories);
            int total = files.Length;
            int done = 0;

            // Pre-sum file sizes so the bar / speed indicator track real bytes,
            // not just file count. Counts are kept around for the status text.
            long bytesTotal = 0;
            foreach (var f in files)
            {
                try { bytesTotal += new FileInfo(f).Length; }
                catch { /* unreadable; skip its size */ }
            }
            long bytesDone = 0;

            DiagnosticLog.Write($"Copying {total} WoL mod files to destination...");

            foreach (var srcFile in files)
            {
                ct.ThrowIfCancellationRequested();

                while (Pause && !ct.IsCancellationRequested)
                    Thread.Sleep(200);

                done++;
                var relativePath = Path.GetRelativePath(extractedFolder, srcFile);
                var destPath = Path.Combine(destinationFolder, relativePath);

                var destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destDir))
                    Directory.CreateDirectory(destDir);

                long srcSize = 0;
                try { srcSize = new FileInfo(srcFile).Length; } catch { }
                File.Copy(srcFile, destPath, overwrite: true);
                bytesDone += srcSize;

                if (done == 1 || done % 100 == 0 || done == total)
                {
                    statusProgress?.Report($"Installing mod files ({done}/{total})...");
                    overlayProgress?.Report(new ModOverlayProgress(done, total, bytesDone, bytesTotal));
                }
            }

            DiagnosticLog.Write($"WoL mod file copy complete: {done} files.");
        }, ct);
    }

    /// <summary>
    /// Creates a Desktop and Start Menu shortcut pointing to the profile's
    /// game executable inside the destination folder. Returns the absolute
    /// paths of the shortcuts created (so the install manifest can record
    /// them for later cleanup).
    /// </summary>
    private static List<string> CreateShortcuts(
        ModProfile profile, string installFolder, out string? startMenuFolder)
    {
        var created = new List<string>();
        startMenuFolder = null;

        try
        {
            var exePath = FindGameExecutable(installFolder, profile.GameExecutable);
            if (exePath == null)
            {
                DiagnosticLog.Write(
                    $"Cannot create shortcuts: '{profile.GameExecutable}' not found in '{installFolder}'.");
                return created;
            }

            string? iconPath = FindShortcutIcon(installFolder, profile);

            var appName = profile.DisplayName;
            var description = $"{appName} - Age of Empires III Mod";

            // Desktop shortcut
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var desktopLink = Path.Combine(desktopPath, $"{appName}.lnk");
            CreateShortcutFile(desktopLink, exePath, installFolder, description, iconPath);
            created.Add(desktopLink);

            // Start Menu shortcut
            var startMenuPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                appName);
            Directory.CreateDirectory(startMenuPath);
            startMenuFolder = startMenuPath;
            var startMenuLink = Path.Combine(startMenuPath, $"{appName}.lnk");
            CreateShortcutFile(startMenuLink, exePath, installFolder, description, iconPath);
            created.Add(startMenuLink);

            DiagnosticLog.Write(iconPath != null
                ? $"Shortcuts created successfully (icon: {Path.GetFileName(iconPath)})."
                : "Shortcuts created successfully (using default exe icon).");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Shortcut creation failed (non-fatal): {ex.Message}");
        }
        return created;
    }

    /// <summary>
    /// Picks the icon for the desktop / Start Menu shortcut. A Windows
    /// <c>.lnk</c> IconLocation only renders <c>.ico</c> / <c>.exe</c> /
    /// <c>.dll</c> — a <c>.png</c> path silently falls back to the target
    /// exe's embedded icon — so this method must NEVER return a non-<c>.ico</c>
    /// path. Priority:
    ///   1. Any <c>.ico</c> already in the install-folder root (e.g. the WoL
    ///      payload ships <c>WoL.ico</c>) — used as-is, no conversion.
    ///   2. <c>profile.LocalIconPath</c> only if it is itself a <c>.ico</c>.
    ///   3. Otherwise, if <c>LocalIconPath</c> is a PNG (the common case —
    ///      catalog icons are <c>icon.png</c>), wrap it into a valid
    ///      single-frame <c>.ico</c> written into the install root and return
    ///      that. Writing into the install folder (not the mod-assets cache)
    ///      means the manifest records it for clean uninstall AND it survives
    ///      the "clear icons cache" button.
    ///   4. Otherwise null — the shortcut falls back to the exe icon.
    /// </summary>
    private static string? FindShortcutIcon(string installFolder, ModProfile profile)
    {
        // 1. A real .ico shipped in the install root wins outright.
        try
        {
            var shippedIco = Directory
                .EnumerateFiles(installFolder, "*.ico", SearchOption.TopDirectoryOnly)
                .FirstOrDefault();
            if (!string.IsNullOrEmpty(shippedIco)) return shippedIco;
        }
        catch { /* non-fatal — fall through */ }

        // 2/3. Fall back to the cached community/profile icon.
        var local = profile.LocalIconPath;
        if (!string.IsNullOrEmpty(local) && File.Exists(local))
        {
            // 2. Already an .ico — usable directly.
            if (local.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                return local;

            // 3. A PNG — Windows can't use it as a .lnk icon. Convert to a
            //    real .ico in the install root.
            try
            {
                var icoPath = Path.Combine(
                    installFolder, $"{SanitizeForFileName(profile.Id)}-shortcut.ico");
                if (IconConverter.TryWritePngAsIco(local, icoPath))
                    return icoPath;
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"PNG->ICO conversion failed (non-fatal): {ex.Message}");
            }
        }

        // 4. Nothing usable — caller falls back to the exe's embedded icon.
        return null;
    }

    /// <summary>
    /// Maps a mod id to a safe file-name stem for the generated shortcut
    /// <c>.ico</c>. Mod ids are slug-like today ("wol", "improvement-mod") so
    /// this is a no-op for them, but it guards against a future id carrying a
    /// path-illegal char.
    /// </summary>
    private static string SanitizeForFileName(string id)
    {
        if (string.IsNullOrEmpty(id)) return "mod";
        var invalid = Path.GetInvalidFileNameChars();
        var chars = id.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }

    /// <summary>
    /// Best-effort repair of an already-installed mod's shortcut icons. Older
    /// installs wrote a <c>.png</c> path into the shortcut's IconLocation,
    /// which Windows can't render (it falls back to the exe icon). This reads
    /// the manifest's recorded shortcuts and, for any <c>.lnk</c> whose
    /// IconLocation points at a non-<c>.ico</c> / missing file, rewrites it to
    /// the icon <see cref="FindShortcutIcon"/> resolves now (e.g. the shipped
    /// <c>WoL.ico</c>). Only the IconLocation is touched. Runs off the UI
    /// thread; every failure is swallowed (logged) so it can never break
    /// startup. For WoL this is a pure re-point of the desktop / Start Menu
    /// <c>.lnk</c> (user-writable) to the already-present <c>WoL.ico</c> — no
    /// elevation, no re-download.
    /// </summary>
    public static void TryHealShortcutIcons(ModProfile profile, string installFolder)
    {
        try
        {
            if (string.IsNullOrEmpty(installFolder) || !Directory.Exists(installFolder))
                return;

            var manifest = InstallManifest.TryLoad(installFolder);
            if (manifest == null || manifest.Shortcuts.Count == 0) return;

            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;

            dynamic shell = Activator.CreateInstance(shellType)!;
            try
            {
                string? resolved = null;     // resolve lazily, only if needed
                bool resolvedComputed = false;

                foreach (var lnk in manifest.Shortcuts)
                {
                    try
                    {
                        if (string.IsNullOrEmpty(lnk) || !File.Exists(lnk)) continue;

                        var shortcut = shell.CreateShortcut(lnk);
                        string current = shortcut.IconLocation ?? "";
                        // IconLocation is "<path>,<index>".
                        var iconFile = current.Split(',')[0];
                        bool alreadyOk =
                            iconFile.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)
                            && File.Exists(iconFile);
                        if (alreadyOk) continue;

                        if (!resolvedComputed)
                        {
                            resolved = FindShortcutIcon(installFolder, profile);
                            resolvedComputed = true;
                        }
                        if (string.IsNullOrEmpty(resolved)) return; // nothing better to offer

                        shortcut.IconLocation = $"{resolved},0";
                        shortcut.Save();
                        DiagnosticLog.Write($"Healed shortcut icon: {Path.GetFileName(lnk)}");
                    }
                    catch (Exception exInner)
                    {
                        DiagnosticLog.Write($"Shortcut heal skipped '{lnk}': {exInner.Message}");
                    }
                }
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Shortcut heal failed (non-fatal): {ex.Message}");
        }
    }

    /// <summary>
    /// Writes the install manifest at the root of the install folder. The
    /// manifest records every file and directory the installer placed on
    /// disk so the uninstaller can later delete them safely without touching
    /// anything else (especially Age of Empires III base game files), and
    /// also stamps the mod-id / appName / productGuid / publisher used at
    /// install time so the uninstaller doesn't have to re-derive them from
    /// the active profile (which may have changed across launcher versions).
    /// </summary>
    private static void WriteManifest(
        ModProfile profile,
        string version,
        string installFolder,
        string? aoe3SourcePath,
        bool clonedAoe3,
        List<string> shortcuts,
        string? startMenuFolder)
    {
        try
        {
            var (files, dirs) = EnumerateInstalledItems(installFolder);

            var manifest = new InstallManifest
            {
                ModId = profile.Id,
                ProductGuid = profile.EffectiveProductGuid,
                AppName = profile.DisplayName,
                Publisher = string.IsNullOrEmpty(profile.Author) ? DefaultPublisher : profile.Author,
                Version = version,
                InstallPath = installFolder,
                InstalledAt = DateTime.UtcNow,
                Aoe3SourcePath = aoe3SourcePath,
                ClonedAoe3 = clonedAoe3,
                Files = files,
                Directories = dirs,
                Shortcuts = shortcuts,
                StartMenuFolder = startMenuFolder,
            };
            manifest.Save();
            DiagnosticLog.Write($"Install manifest written: {files.Count} files, {dirs.Count} dirs.");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Manifest write failed (non-fatal): {ex.Message}");
        }
    }

    /// <summary>
    /// Walks the install folder and returns the relative paths of every file
    /// and every directory the installer placed there. Excludes the manifest
    /// itself (it'll be deleted last during uninstall).
    /// </summary>
    private static (List<string> Files, List<string> Dirs) EnumerateInstalledItems(string installFolder)
    {
        var files = new List<string>();
        var dirs = new List<string>();

        if (!Directory.Exists(installFolder))
            return (files, dirs);

        // Files (excluding the manifest itself)
        foreach (var f in Directory.EnumerateFiles(installFolder, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(installFolder, f).Replace('\\', '/');
            if (string.Equals(rel, InstallManifest.FileName, StringComparison.OrdinalIgnoreCase))
                continue;
            files.Add(rel);
        }

        // Directories — sort by depth so we record parents before children;
        // the uninstaller will walk this list in reverse to remove leaves first.
        foreach (var d in Directory.EnumerateDirectories(installFolder, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(installFolder, d).Replace('\\', '/');
            dirs.Add(rel);
        }
        dirs.Sort((a, b) => a.Length.CompareTo(b.Length));

        return (files, dirs);
    }

    /// <summary>
    /// Writes the Windows registry entries that make the install visible in
    /// Add/Remove Programs and let the launcher's RegistryService detect this
    /// installation on future runs. Subkey, display name, publisher and
    /// version all come from the active profile / caller — no WoL-specific
    /// constants here.
    /// </summary>
    private static void WriteRegistryEntries(
        ModProfile profile, string version, string installFolder)
    {
        try
        {
            // Write to HKLM (requires admin) first, fall back to HKCU.
            if (!TryWriteRegistryTo(Registry.LocalMachine, profile, version, installFolder))
            {
                DiagnosticLog.Write("HKLM write failed (no admin); trying HKCU...");
                TryWriteRegistryTo(Registry.CurrentUser, profile, version, installFolder);
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Registry write failed (non-fatal): {ex.Message}");
        }
    }

    private static bool TryWriteRegistryTo(
        RegistryKey root, ModProfile profile, string version, string installFolder)
    {
        try
        {
            var keyPath =
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + profile.EffectiveProductGuid;
            using var key = root.CreateSubKey(keyPath, writable: true);
            if (key == null) return false;

            key.SetValue("DisplayName", profile.DisplayName);
            key.SetValue("Inno Setup: App Path", installFolder);
            key.SetValue("Path", installFolder);
            key.SetValue("InstallLocation", installFolder);
            key.SetValue("Publisher",
                string.IsNullOrEmpty(profile.Author) ? DefaultPublisher : profile.Author);
            key.SetValue("DisplayVersion", string.IsNullOrEmpty(version) ? "1.0" : version);
            key.SetValue("UninstallString", ""); // No uninstaller — user deletes folder
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);

            DiagnosticLog.Write($"Registry written to {root.Name}\\{keyPath}");
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Resolves the absolute path of the game executable inside an install
    /// folder. Prefers <c>&lt;install&gt;\bin\&lt;exe&gt;</c> (Steam layout),
    /// then <c>&lt;install&gt;\&lt;exe&gt;</c>, then a recursive search as
    /// last resort. Returns null when nothing matches — the caller logs the
    /// missing-exe and skips shortcut creation rather than failing the
    /// install.
    /// </summary>
    private static string? FindGameExecutable(string installFolder, string exeName)
    {
        if (string.IsNullOrEmpty(exeName)) return null;

        var binCandidate = Path.Combine(installFolder, "bin", exeName);
        if (File.Exists(binCandidate)) return binCandidate;

        var rootCandidate = Path.Combine(installFolder, exeName);
        if (File.Exists(rootCandidate)) return rootCandidate;

        try
        {
            return Directory.GetFiles(installFolder, exeName, SearchOption.AllDirectories)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Creates a .lnk shortcut file using the Windows Script Host COM object.
    /// When <paramref name="iconPath"/> is provided, the shortcut uses that
    /// .ico file (with index 0) instead of the default icon embedded in the
    /// target executable.
    /// </summary>
    private static void CreateShortcutFile(
        string linkPath, string targetExe, string workingDir,
        string description, string? iconPath = null)
    {
        // Use IWshRuntimeLibrary via dynamic COM interop (available on all Windows)
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null) return;

        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            var shortcut = shell.CreateShortcut(linkPath);
            shortcut.TargetPath = targetExe;
            shortcut.WorkingDirectory = workingDir;
            shortcut.Description = description;
            if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
            {
                // "<path>,<index>" — index 0 = first icon in the .ico file
                shortcut.IconLocation = $"{iconPath},0";
            }
            shortcut.Save();
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
        }
    }

    /// <summary>
    /// Cleanup temp files. Call on next startup or after success.
    /// </summary>
    public static void TryCleanupTemp()
    {
        try
        {
            if (Directory.Exists(TempDirectory))
                Directory.Delete(TempDirectory, recursive: true);
        }
        catch { /* ignore */ }
    }
}
