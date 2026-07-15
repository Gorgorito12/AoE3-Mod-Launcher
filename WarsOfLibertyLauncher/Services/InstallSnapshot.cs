using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Builds the install-integrity summary that ships inside a diagnostic bundle.
///
/// Why this exists: a bundle used to carry only logs, and that left two blind
/// spots that made real reports impossible to close without asking the user to
/// run commands by hand.
///
///   • Whether a file the manifest says should exist is simply GONE from disk.
///     That is the Windows Defender false-positive class (a quarantined file is
///     a deleted file) — invisible today, because the manifest is rewritten from
///     whatever was copied, so Verify afterwards reports the install as intact.
///   • Whether the LIVE data\stringtabley.xml still matches the canonical
///     translations\_originals\ snapshot. Version detection hashes the snapshot
///     on purpose (localization invariance), so a bundle carried no trace of the
///     live file — which is the one the game reads for the version string it
///     prints in its menu.
///
/// The file name must keep containing "snapshot": <see cref="DiagnosticLog.ExportBundle"/>
/// stages every top-level *.log / *snapshot* file, so the name alone is what
/// gets this into the zip.
/// </summary>
public static class InstallSnapshot
{
    /// <summary>Bundle file name. Contains "snapshot" so ExportBundle's glob picks it up.</summary>
    public const string FileName = "install-snapshot.txt";

    /// <summary>Cap the listed missing paths; the COUNT is always exact.</summary>
    internal const int MaxMissingListed = 40;

    /// <summary>
    /// The three files AoE3 keys its version off, install-relative with forward
    /// slashes — the same shape <see cref="InstallManifest.KeyFileHashes"/> uses.
    /// </summary>
    private static readonly string[] KeyFiles =
    {
        "data/protoy.xml",
        "data/techtreey.xml",
        "data/stringtabley.xml",
    };

    /// <summary>
    /// Compose the summary for <paramref name="installPath"/>. Never throws: a
    /// diagnostics helper must not be able to break the export it feeds.
    /// </summary>
    public static async Task<string> BuildAsync(
        string modId,
        string installPath,
        string activeTranslationId,
        string activeTranslationVersion,
        CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        try
        {
            sb.AppendLine("=== Install snapshot ===");
            sb.AppendLine($"Generated (UTC): {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Mod: {modId}");
            sb.AppendLine($"Install path: {installPath}");
            sb.AppendLine();

            if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
            {
                sb.AppendLine("Install folder does not exist — nothing else to report.");
                return sb.ToString();
            }

            var manifest = InstallManifest.TryLoad(installPath);
            AppendManifestSection(sb, manifest);
            await AppendKeyFilesSectionAsync(sb, installPath, manifest, ct);
            await AppendStringTableSectionAsync(sb, installPath, activeTranslationId, activeTranslationVersion, ct);
            AppendEngineSection(sb, installPath);
            AppendMissingSection(sb, installPath, manifest);
        }
        catch (Exception ex)
        {
            sb.AppendLine();
            sb.AppendLine($"!! install-snapshot generation failed: {ex}");
        }
        return sb.ToString();
    }

    private static void AppendManifestSection(StringBuilder sb, InstallManifest? manifest)
    {
        sb.AppendLine("--- Manifest ---");
        if (manifest == null)
        {
            sb.AppendLine("install-manifest.json: ABSENT (install predates the manifest, or was not made by this launcher)");
            sb.AppendLine();
            return;
        }
        sb.AppendLine("install-manifest.json: present");
        sb.AppendLine($"Recorded version : {(string.IsNullOrWhiteSpace(manifest.Version) ? "(empty)" : manifest.Version)}");
        sb.AppendLine($"Tracked files    : {manifest.Files.Count} ({manifest.Directories.Count} dirs)");
        sb.AppendLine($"Overlay files    : {manifest.OverlayFiles.Count} ({manifest.OverlayNetNew.Count} net-new)");
        sb.AppendLine($"Overlay hashes   : {manifest.FileHashes.Count}");
        sb.AppendLine($"Engine hashes    : {manifest.EngineFileHashes.Count}");
        sb.AppendLine($"Cloned AoE3      : {manifest.ClonedAoe3}" +
                      (string.IsNullOrWhiteSpace(manifest.Aoe3SourcePath) ? "" : $" (source: {manifest.Aoe3SourcePath})"));
        sb.AppendLine();
    }

    /// <summary>
    /// Manifest baseline vs what is on disk NOW. A DIFFERS here means the live
    /// files drifted from the version the manifest claims — which is what makes
    /// version recognition fall back or refuse to trust the baseline.
    /// </summary>
    private static async Task AppendKeyFilesSectionAsync(
        StringBuilder sb, string installPath, InstallManifest? manifest, CancellationToken ct)
    {
        sb.AppendLine("--- Version key files: manifest baseline vs live ---");
        if (manifest == null || manifest.KeyFileHashes.Count == 0)
        {
            sb.AppendLine("(no baseline recorded — manifest predates baseline recording)");
        }
        foreach (var rel in KeyFiles)
        {
            var live = await HashService.ComputeMd5Async(ToAbsolute(installPath, rel), ct);
            string baseline = "";
            manifest?.KeyFileHashes.TryGetValue(rel, out baseline!);

            string verdict;
            if (string.IsNullOrEmpty(live)) verdict = "LIVE FILE MISSING";
            else if (string.IsNullOrEmpty(baseline)) verdict = "(no baseline)";
            else verdict = string.Equals(live, baseline, StringComparison.OrdinalIgnoreCase)
                ? "match" : "DIFFERS";

            sb.AppendLine($"{rel,-24} baseline={Short(baseline)} live={Short(live)}  {verdict}");
        }
        sb.AppendLine();
    }

    /// <summary>
    /// The live vs snapshot comparison. This is the section that answers a
    /// "the launcher says X but the game shows an older Y" report: the game
    /// reads the LIVE file, while version detection reads the snapshot.
    /// </summary>
    private static async Task AppendStringTableSectionAsync(
        StringBuilder sb, string installPath, string activeTranslationId, string activeTranslationVersion,
        CancellationToken ct)
    {
        sb.AppendLine("--- stringtabley.xml (the file the GAME reads for its menu version string) ---");

        var translations = new TranslationService(installPath);
        var livePath = Path.Combine(installPath, "data", "stringtabley.xml");
        var hashPath = translations.ResolveHashableFile(Path.Combine("data", "stringtabley.xml"));

        var liveMd5 = await HashService.ComputeMd5Async(livePath, ct);
        sb.AppendLine($"live       : {Short(liveMd5)}{(string.IsNullOrEmpty(liveMd5) ? "  (MISSING)" : "")}");

        bool hasSnapshot = !string.Equals(hashPath, livePath, StringComparison.OrdinalIgnoreCase);
        if (!hasSnapshot)
        {
            sb.AppendLine("_originals : (no snapshot — launcher hashes the live file for version detection)");
        }
        else
        {
            var snapMd5 = await HashService.ComputeMd5Async(hashPath, ct);
            sb.AppendLine($"_originals : {Short(snapMd5)}   <- what version detection hashes");
            sb.AppendLine(string.Equals(liveMd5, snapMd5, StringComparison.OrdinalIgnoreCase)
                ? "=> same: no translation applied to this file."
                : "=> DIFFER: a translation is applied, or the live file drifted. If the game "
                  + "shows an older version than the launcher reports, the live file is the cause.");
        }

        sb.AppendLine(string.IsNullOrWhiteSpace(activeTranslationId)
            ? "Active translation: (none — English)"
            : $"Active translation: '{activeTranslationId}' v" +
              $"{(string.IsNullOrWhiteSpace(activeTranslationVersion) ? "?" : activeTranslationVersion)}");
        sb.AppendLine();
    }

    /// <summary>
    /// Engine files are NOT covered by Verify/Repair (they come from the AoE3
    /// clone), so a bundle is the only place their absence would ever surface.
    /// </summary>
    private static void AppendEngineSection(StringBuilder sb, string installPath)
    {
        sb.AppendLine("--- Engine files (from the AoE3 clone; Verify/Repair do NOT cover these) ---");
        foreach (var rel in VerifyService.EngineCandidates)
        {
            bool ok = File.Exists(ToAbsolute(installPath, rel));
            sb.AppendLine($"{(ok ? "OK  " : "MISS")}  {rel}");
        }
        sb.AppendLine();
    }

    /// <summary>
    /// EXISTENCE-only sweep, deliberately: re-hashing a multi-GB install takes
    /// minutes and the user is waiting on the export. A quarantined file is a
    /// deleted file, so File.Exists already catches the class this is for.
    /// Corruption (right path, wrong bytes) is out of scope here — that is what
    /// the gear's "Verify files" is for.
    /// </summary>
    private static void AppendMissingSection(StringBuilder sb, string installPath, InstallManifest? manifest)
    {
        sb.AppendLine("--- Missing files (manifest says they should be on disk) ---");
        if (!VerifyService.HasFileHashes(manifest))
        {
            sb.AppendLine("(no per-file hashes in the manifest — nothing to check)");
            return;
        }

        var missing = new List<string>();
        int checkedCount = 0;
        foreach (var rel in manifest!.FileHashes.Keys)
        {
            checkedCount++;
            if (!File.Exists(ToAbsolute(installPath, rel))) missing.Add(rel);
        }
        foreach (var rel in manifest.EngineFileHashes.Keys)
        {
            checkedCount++;
            if (!File.Exists(ToAbsolute(installPath, rel))) missing.Add(rel);
        }

        sb.AppendLine($"Checked {checkedCount} path(s) — existence only, no hashing.");
        sb.AppendLine($"MISSING: {missing.Count}");
        foreach (var rel in missing.Take(MaxMissingListed)) sb.AppendLine($"  {rel}");
        if (missing.Count > MaxMissingListed)
            sb.AppendLine($"  (… {missing.Count - MaxMissingListed} more not listed)");
    }

    /// <summary>Manifest keys are install-relative with forward slashes.</summary>
    private static string ToAbsolute(string installPath, string relForwardSlash) =>
        Path.Combine(installPath, relForwardSlash.Replace('/', Path.DirectorySeparatorChar));

    private static string Short(string? md5) =>
        string.IsNullOrEmpty(md5) ? "(none)" : md5;
}
