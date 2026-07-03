using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Optional incremental "delta patch" support for the <see cref="ModUpdateMechanism.GitHubReleases"/>
/// update mechanism — a modder-friendly, GitHub-native alternative to WoL's UpdateInfo.xml/.tar.xz
/// pipeline. Instead of re-downloading the FULL overlay every version, a mod that opts in
/// (<c>update.github.deltaPatches: true</c>) ships, alongside the full <c>.zip</c> on each release,
/// a small <c>patch-&lt;from&gt;-to-&lt;to&gt;.zip</c> (only the changed/added files) + a
/// <c>.json</c> descriptor. The launcher applies the small patch when it can (single-hop, from the
/// immediately-previous version) and falls back to the full download for anything else.
///
/// Design rule: the delta is a best-effort shortcut with a GUARANTEED full fallback. Any doubt —
/// no descriptor, wrong base version, hash mismatch, external-hosted mod, network error — returns
/// null/false and the caller does the normal full re-overlay. So the delta can never make an
/// update worse than today, only faster when it succeeds. Hashes in the descriptor are OPTIONAL
/// (verified when present, degraded gracefully when absent) — the in-app generator always emits
/// them. See docs/MODDING.md "Incremental delta patches".
/// </summary>
public static class DeltaPatchService
{
    /// <summary>How many `changed` files a single patch may carry — a runaway guard.</summary>
    public const int MaxChangedFiles = 100_000;

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Aoe3ModLauncher");
        return c;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    // ---------------------------------------------------------------- eligibility

    /// <summary>
    /// True only for a GitHubReleases mod that opted into delta patches AND is NOT external-hosted.
    /// External-hosted mods pin their SHA in the catalog (human-reviewed); the delta descriptor
    /// lives on the release, outside that guarantee, so delta is disabled for them (they always
    /// take the full path). Pure — unit-testable.
    /// </summary>
    public static bool IsEligible(ModProfile? profile)
    {
        var gh = profile?.GitHubReleases;
        if (gh == null) return false;
        if (profile!.UpdateMechanism != ModUpdateMechanism.GitHubReleases) return false;
        if (!gh.DeltaPatches) return false;
        if (!string.IsNullOrEmpty(gh.ExternalAssetUrlTemplate)) return false;   // external-hosted → full only
        return true;
    }

    // ---------------------------------------------------------------- pure diff/select

    /// <summary>
    /// Diff two overlay file→hash maps (old vs new) into the descriptor's `changed` (added or
    /// hash-differing, each carrying the old + new hash) and `deleted` (present in old, gone in
    /// new). Pure — the heart of the generator, unit-tested. Ordinal-insensitive keys.
    /// </summary>
    public static (List<DeltaChangedFile> Changed, List<string> Deleted) ComputeDiff(
        IReadOnlyDictionary<string, string> oldHashes,
        IReadOnlyDictionary<string, string> newHashes)
    {
        var changed = new List<DeltaChangedFile>();
        var deleted = new List<string>();
        var oldCi = new Dictionary<string, string>(oldHashes, StringComparer.OrdinalIgnoreCase);
        var newCi = new Dictionary<string, string>(newHashes, StringComparer.OrdinalIgnoreCase);

        foreach (var (path, newHash) in newCi)
        {
            oldCi.TryGetValue(path, out var oldHash);
            if (oldHash == null || !string.Equals(oldHash, newHash, StringComparison.OrdinalIgnoreCase))
                changed.Add(new DeltaChangedFile { Path = path, FromSha256 = oldHash, Sha256 = newHash });
        }
        foreach (var path in oldCi.Keys)
            if (!newCi.ContainsKey(path)) deleted.Add(path);

        changed.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
        deleted.Sort(StringComparer.Ordinal);
        return (changed, deleted);
    }

    /// <summary>
    /// Pick the single-hop patch from a set of candidate descriptors: the one whose
    /// <c>toTag</c> is the update's target tag (the approved tag, or the resolved latest
    /// for follow-latest mods) AND whose <c>fromTag</c> is the installed tag
    /// (case-insensitive). Null when none matches (fresh install, version skip, or the mod
    /// ships no matching patch) — caller falls back to full. Pure — unit-tested.
    /// </summary>
    public static DeltaPatchDescriptor? SelectPatch(
        IEnumerable<DeltaPatchDescriptor> candidates, string? installedTag, string targetTag)
    {
        if (string.IsNullOrWhiteSpace(installedTag) || string.IsNullOrWhiteSpace(targetTag))
            return null;
        return candidates.FirstOrDefault(d =>
            d != null
            && string.Equals(d.ToTag, targetTag, StringComparison.OrdinalIgnoreCase)
            && string.Equals(d.FromTag, installedTag, StringComparison.OrdinalIgnoreCase));
    }

    // ---------------------------------------------------------------- generator

    /// <summary>
    /// Build a delta patch (<c>.zip</c> + <c>.json</c>) from the OLD overlay zip and the NEW
    /// overlay zip. Extracts both to temp, hashes every file, diffs, packs the changed/added files
    /// into the patch zip and writes the descriptor with per-file from/to hashes + the patch zip's
    /// own SHA-256. Mirrors <see cref="TranslationService.ExportPackageAsync"/>'s folder→zip+json
    /// pattern. Returns the two output paths. The modder uploads BOTH plus the full new zip to the
    /// new release.
    /// </summary>
    public static async Task<GenerateResult> GeneratePatchAsync(
        string oldZipPath, string newZipPath, string fromTag, string toTag,
        string outputFolder, CancellationToken ct = default)
    {
        if (!File.Exists(oldZipPath)) throw new FileNotFoundException("Old overlay zip not found.", oldZipPath);
        if (!File.Exists(newZipPath)) throw new FileNotFoundException("New overlay zip not found.", newZipPath);
        Directory.CreateDirectory(outputFolder);

        var work = Path.Combine(Path.GetTempPath(), "aoe3ml-delta-gen-" + Guid.NewGuid().ToString("N"));
        var oldDir = Path.Combine(work, "old");
        var newDir = Path.Combine(work, "new");
        try
        {
            Directory.CreateDirectory(oldDir);
            Directory.CreateDirectory(newDir);
            await Task.Run(() =>
            {
                ZipFile.ExtractToDirectory(oldZipPath, oldDir);
                ZipFile.ExtractToDirectory(newZipPath, newDir);
            }, ct);

            var oldHashes = await Task.Run(() => HashTree(oldDir), ct);
            var newHashes = await Task.Run(() => HashTree(newDir), ct);
            var (changed, deleted) = ComputeDiff(oldHashes, newHashes);

            var safeFrom = Sanitize(fromTag);
            var safeTo = Sanitize(toTag);
            var patchZipName = $"patch-{safeFrom}-to-{safeTo}.zip";
            var patchJsonName = $"patch-{safeFrom}-to-{safeTo}.json";
            var patchZipPath = Path.Combine(outputFolder, patchZipName);
            var patchJsonPath = Path.Combine(outputFolder, patchJsonName);

            // Pack the changed/added files (from the NEW tree) into the patch zip.
            if (File.Exists(patchZipPath)) File.Delete(patchZipPath);
            await Task.Run(() =>
            {
                using var fs = new FileStream(patchZipPath, FileMode.Create, FileAccess.Write);
                using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
                foreach (var c in changed)
                {
                    ct.ThrowIfCancellationRequested();
                    var src = Path.Combine(newDir, c.Path.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(src)) continue;
                    zip.CreateEntryFromFile(src, c.Path, CompressionLevel.Optimal);
                }
            }, ct);

            var payloadSha = (await Task.Run(() => VerifyService.ComputeFingerprintOf(patchZipPath), ct)).Sha256;

            var descriptor = new DeltaPatchDescriptor
            {
                FromTag = fromTag,
                ToTag = toTag,
                Payload = patchZipName,
                PayloadSha256 = payloadSha,
                Changed = changed,
                Deleted = deleted,
            };
            await File.WriteAllTextAsync(patchJsonPath, JsonSerializer.Serialize(descriptor, JsonOpts), ct);

            long patchSize = new FileInfo(patchZipPath).Length;
            return new GenerateResult(patchZipPath, patchJsonPath, changed.Count, deleted.Count, patchSize);
        }
        finally
        {
            try { if (Directory.Exists(work)) Directory.Delete(work, recursive: true); } catch { /* best-effort */ }
        }
    }

    private static Dictionary<string, string> HashTree(string root)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
            try { result[rel] = VerifyService.ComputeFingerprintOf(f).Sha256; }
            catch { /* unreadable file drops out */ }
        }
        return result;
    }

    private static string Sanitize(string tag)
    {
        var chars = (tag ?? "").Select(ch =>
            char.IsLetterOrDigit(ch) || ch == '.' || ch == '-' || ch == '_' ? ch : '_').ToArray();
        var s = new string(chars).Trim('_');
        return s.Length == 0 ? "x" : s;
    }

    // ---------------------------------------------------------------- consumer: discover + pre-verify

    /// <summary>
    /// Discover a single-hop patch for <paramref name="targetTag"/> (the update's target —
    /// the approved tag, or the resolved latest for follow-latest mods) whose <c>fromTag</c>
    /// equals <paramref name="installedTag"/>, download + hash-verify its zip, and pre-verify
    /// it against the install. Returns a prepared patch (local zip path + descriptor) or
    /// null → full fallback. Never throws for a "no delta" condition — any problem returns
    /// null. The target is a PARAMETER (not read from <paramref name="gh"/>) so the caller
    /// decides the policy and this service stays decoupled from the catalog pin.
    /// </summary>
    public static async Task<PreparedPatch?> TryPrepareAsync(
        GitHubReleasesSettings gh, string? installedTag, string targetTag, string installPath,
        InstallManifest? manifest, IReadOnlyList<string>? coveredFiles, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(installedTag) || string.IsNullOrWhiteSpace(targetTag)) return null;
            if (string.Equals(installedTag, targetTag, StringComparison.OrdinalIgnoreCase)) return null; // nothing to do
            if (manifest == null || !VerifyService.HasFileHashes(manifest)) return null; // need a hash baseline

            // 1. List the target release's assets (one API call, reused by the caller's full path too).
            var assets = await new GitHubReleaseDownloader().ListAssetsAsync(gh.SourceRepo, targetTag, ct);
            if (assets == null || assets.Count == 0) return null;

            // 2. Download every `patch-*.json` on the release and parse — pick by from/to tags.
            var descriptors = new List<DeltaPatchDescriptor>();
            foreach (var a in assets)
            {
                if (a.Name == null) continue;
                if (!a.Name.StartsWith("patch-", StringComparison.OrdinalIgnoreCase)) continue;
                if (!a.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var json = await Http.GetStringAsync(a.Url, ct);
                    var d = JsonSerializer.Deserialize<DeltaPatchDescriptor>(json, JsonOpts);
                    if (d != null) descriptors.Add(d);
                }
                catch (Exception ex) { DiagnosticLog.Write($"Delta descriptor parse failed ({a.Name}): {ex.Message}"); }
            }

            var descriptor = SelectPatch(descriptors, installedTag, targetTag);
            if (descriptor == null) return null;
            if (descriptor.Changed.Count == 0 && descriptor.Deleted.Count == 0) return null;
            if (descriptor.Changed.Count > MaxChangedFiles) return null;

            // 3. Pre-verify against the install (catch diverged base / mislabeled patch) — cheap.
            if (!PreVerify(installPath, manifest, descriptor, coveredFiles)) return null;

            // 4. Resolve + download the patch zip asset; verify payloadSha256 when present.
            var payloadAsset = assets.FirstOrDefault(x =>
                string.Equals(x.Name, descriptor.Payload, StringComparison.OrdinalIgnoreCase));
            if (payloadAsset == null || string.IsNullOrEmpty(payloadAsset.Url)) return null;

            var tempZip = Path.Combine(Path.GetTempPath(), "aoe3ml-delta-" + Guid.NewGuid().ToString("N") + ".zip");
            using (var resp = await Http.GetAsync(payloadAsset.Url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var dst = new FileStream(tempZip, FileMode.Create, FileAccess.Write);
                await src.CopyToAsync(dst, ct);
            }

            if (!string.IsNullOrWhiteSpace(descriptor.PayloadSha256))
            {
                var got = (await Task.Run(() => VerifyService.ComputeFingerprintOf(tempZip), ct)).Sha256;
                if (!string.Equals(got, descriptor.PayloadSha256, StringComparison.OrdinalIgnoreCase))
                {
                    DiagnosticLog.Write("Delta patch zip SHA-256 mismatch — falling back to full.");
                    TryDelete(tempZip);
                    return null;
                }
            }

            return new PreparedPatch(descriptor, tempZip);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Delta prepare failed (falling back to full): {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Cheap pre-apply check: every `changed` file's recorded pre-state must match what the
    /// install actually has. Uses the descriptor's <c>fromSha256</c> when present (strong
    /// cross-check: catches a patch built against a different base than the manifest records);
    /// otherwise verifies the live file on disk against the manifest's own recorded hash
    /// (self-integrity). Covered/localized files are read via the <c>_originals</c> snapshot
    /// (<see cref="VerifyService.ResolveHashTarget"/>) so a translated install doesn't false-fail.
    /// </summary>
    public static bool PreVerify(
        string installPath, InstallManifest manifest, DeltaPatchDescriptor descriptor,
        IReadOnlyList<string>? coveredFiles)
    {
        var covered = VerifyService.BuildCoveredSet(coveredFiles);
        var originals = VerifyService.OriginalsFolderOf(installPath);
        var fileHashes = manifest.FileHashes ?? new();

        foreach (var c in descriptor.Changed)
        {
            if (string.IsNullOrWhiteSpace(c.Path)) return false;
            var rel = c.Path.Replace('\\', '/');
            bool existsInManifest = fileHashes.TryGetValue(rel, out var recorded);

            if (!string.IsNullOrWhiteSpace(c.FromSha256))
            {
                // Strong path: the patch declares the pre-hash it was built against.
                if (existsInManifest)
                {
                    if (!string.Equals(recorded!.Sha256, c.FromSha256, StringComparison.OrdinalIgnoreCase))
                        return false;   // manifest says a different base → mislabeled/diverged
                }
                else
                {
                    // The file is an ADDITION (didn't exist before) — it must be absent on disk,
                    // or the base diverged. A blank fromSha256 marks a genuine addition.
                    // A non-blank fromSha256 for a file not in the manifest is inconsistent → bail.
                    return false;
                }
            }
            else
            {
                // Degraded path (no declared pre-hash). For an EXISTING overlay file, verify the
                // live bytes still match the manifest (self-integrity). An ADDITION (absent from
                // the manifest) has nothing to check here.
                if (existsInManifest)
                {
                    var target = VerifyService.ResolveHashTarget(installPath, rel, covered, originals);
                    if (target == null || !File.Exists(target)) return false;
                    string live;
                    try { live = VerifyService.ComputeFingerprintOf(target).Sha256; }
                    catch { return false; }
                    if (!string.Equals(live, recorded!.Sha256, StringComparison.OrdinalIgnoreCase))
                        return false;
                }
            }
        }
        return true;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
    }

    // ---------------------------------------------------------------- DTOs

    /// <summary>The <c>patch-&lt;from&gt;-to-&lt;to&gt;.json</c> descriptor (release asset).</summary>
    public sealed class DeltaPatchDescriptor
    {
        [JsonPropertyName("fromTag")] public string FromTag { get; set; } = "";
        [JsonPropertyName("toTag")] public string ToTag { get; set; } = "";
        [JsonPropertyName("payload")] public string Payload { get; set; } = "";
        [JsonPropertyName("payloadSha256")] public string? PayloadSha256 { get; set; }
        [JsonPropertyName("changed")] public List<DeltaChangedFile> Changed { get; set; } = new();
        [JsonPropertyName("deleted")] public List<string> Deleted { get; set; } = new();
    }

    /// <summary>One changed/added file: install-relative path + optional pre/post SHA-256.</summary>
    public sealed class DeltaChangedFile
    {
        [JsonPropertyName("path")] public string Path { get; set; } = "";
        [JsonPropertyName("fromSha256")] public string? FromSha256 { get; set; }
        [JsonPropertyName("sha256")] public string? Sha256 { get; set; }
    }

    /// <summary>A downloaded, hash-verified, pre-verified patch ready to apply.</summary>
    public sealed record PreparedPatch(DeltaPatchDescriptor Descriptor, string LocalZipPath);

    /// <summary>Result of <see cref="GeneratePatchAsync"/>.</summary>
    public sealed record GenerateResult(
        string PatchZipPath, string PatchJsonPath, int ChangedCount, int DeletedCount, long PatchZipSize);
}
