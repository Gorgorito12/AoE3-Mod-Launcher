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
/// (<c>update.github.deltaPatches: true</c>) publishes the full <c>.zip</c> ONCE — on a
/// "baseline" release — and thereafter ships only a small
/// <c>patch-&lt;from&gt;-to-&lt;to&gt;.zip</c> (the changed/added files) + a <c>.json</c>
/// descriptor. <see cref="DeltaChainPlanner"/> then works out each player's cheapest route:
/// a cumulative patch from the baseline, an incremental one from the previous version, a short
/// chain of them, or the full download when that is cheaper.
///
/// <para>Note the full <c>.zip</c> does NOT go up on every release — only on a baseline. This
/// used to read "alongside the full <c>.zip</c> on each release", which is the model before
/// patch-only releases existed and is the opposite of the point.</para>
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

    /// <summary>
    /// Share of the full <c>.zip</c> at which a cumulative patch stops being worth shipping and
    /// the modder should publish a new BASELINE instead (a release that carries the full overlay
    /// again, restarting the chain).
    /// </summary>
    public const double RebaselineRatio = 0.5;

    /// <summary>
    /// Whether a generated patch has grown far enough from its baseline that a new baseline is
    /// cheaper for everyone than an ever-growing cumulative. Pure, so the boundary is pinned by a
    /// test rather than argued about.
    ///
    /// <para>Advice only — nothing enforces it. A modder who never re-baselines still works: the
    /// planner compares real sizes and simply stops choosing the patch once the full download is
    /// cheaper, so the endgame is graceful rather than a cliff. What this prevents is the modder
    /// never finding out they have been shipping patches that no longer save anybody anything.</para>
    /// </summary>
    public static bool ShouldAdviseRebaseline(long patchZipSize, long newFullZipSize)
        => newFullZipSize > 0 && patchZipSize > (long)(newFullZipSize * RebaselineRatio);

    /// <summary>
    /// THE single definition of "this release asset belongs to the delta mechanism".
    ///
    /// <para>Two consumers, pulling in opposite directions, and that is exactly why it lives in
    /// one place: <see cref="GitHubReleaseDownloader"/> EXCLUDES these when choosing the full
    /// payload, while the delta discovery INCLUDES them. Two copies of the rule would drift, and
    /// the drift re-opens the bug this was written for from whichever side was not updated.</para>
    ///
    /// <para><b>The bug:</b> asset selection used to be "the first <c>.zip</c> on the release".
    /// A delta release carries TWO (the full overlay and <c>patch-…-to-….zip</c>), GitHub returns
    /// assets in upload order, so uploading the patch first made it the "full payload" — and an
    /// update then computed "files the new release no longer ships" against those few files and
    /// deleted essentially the whole overlay.</para>
    /// </summary>
    public static class PatchAssetNaming
    {
        /// <summary>Filename prefix the generator stamps on both halves of a patch.</summary>
        public const string Prefix = "patch-";

        /// <summary>Separates the two tags inside a patch filename.</summary>
        public const string Separator = "-to-";

        /// <summary>
        /// Whether this asset name is either half of a delta patch (<c>patch-*.zip</c> /
        /// <c>patch-*.json</c>). The hyphen in <see cref="Prefix"/> is load-bearing: it keeps a
        /// mod's own <c>patchnotes.zip</c> from being mistaken for patch machinery.
        /// </summary>
        public static bool IsPatchAsset(string? assetName)
        {
            var name = assetName ?? "";
            if (!name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return false;
            return name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Whether this asset name is a patch DESCRIPTOR (<c>patch-*.json</c>).</summary>
        public static bool IsDescriptor(string? assetName)
        {
            var name = assetName ?? "";
            return name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The filename stem a patch between these two tags gets: <c>patch-&lt;from&gt;-to-&lt;to&gt;</c>.
        ///
        /// <para>Exists so the generator and the dialog's "what will be written" preview cannot
        /// disagree. The preview names files the modder is about to upload; if it derived them
        /// from its own copy of the rule it could name one thing and write another, and the
        /// modder would only find out by comparing the disk against a screen they had already
        /// believed. Same single-definition reasoning as the rest of this class.</para>
        /// </summary>
        public static string StemFor(string fromTag, string toTag)
            => Prefix + Sanitize(fromTag) + Separator + Sanitize(toTag);

        /// <summary>The payload zip that belongs beside a descriptor: same stem, <c>.zip</c>.</summary>
        public static string DescriptorToPayloadName(string descriptorName)
        {
            var name = descriptorName ?? "";
            return name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? name[..^5] + ".zip"
                : name + ".zip";
        }

        /// <summary>
        /// The <c>(from, to)</c> tags a patch FILENAME proposes, resolved back to the repo's real
        /// tags. Null when it cannot be resolved unambiguously.
        ///
        /// <para><b>A filename only ever PROPOSES.</b> <see cref="Sanitize"/> is lossy, so this
        /// inverts it by comparing <c>Sanitize(realTag) == token</c> rather than matching the raw
        /// text — that is what lets a tag like <c>v1.0+build</c> resolve at all. The downloaded
        /// descriptor's own <c>fromTag</c>/<c>toTag</c> is what CONFIRMS the hop before anything
        /// is applied.</para>
        ///
        /// <para><b>Every ambiguity is a rejection, never a guess.</b> A tag may itself contain
        /// <c>-to-</c>, so <c>patch-v1-to-v2-to-v3.zip</c> has two valid split points and is
        /// refused; so is a token matching no real tag, and so are two distinct tags that
        /// sanitize to the same token. A modder with exotic tags gets no delta rather than the
        /// wrong one.</para>
        /// </summary>
        public static (string From, string To)? ProposeHop(
            string? assetName, IReadOnlyCollection<string> knownTags)
        {
            var name = assetName ?? "";
            if (!name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return null;
            if (knownTags == null || knownTags.Count == 0) return null;

            var dot = name.LastIndexOf('.');
            if (dot <= Prefix.Length) return null;
            var stem = name[Prefix.Length..dot];

            // A token resolves only when EXACTLY ONE real tag sanitizes to it.
            string? Resolve(string token)
            {
                string? hit = null;
                foreach (var tag in knownTags)
                {
                    if (!string.Equals(Sanitize(tag), token, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (hit != null) return null;   // two tags collapse to the same token
                    hit = tag;
                }
                return hit;
            }

            (string From, string To)? found = null;
            int at = stem.IndexOf(Separator, StringComparison.OrdinalIgnoreCase);
            while (at > 0)
            {
                var from = Resolve(stem[..at]);
                var to = Resolve(stem[(at + Separator.Length)..]);
                if (from != null && to != null)
                {
                    if (found != null) return null;   // more than one split resolves
                    found = (from, to);
                }
                at = stem.IndexOf(Separator, at + 1, StringComparison.OrdinalIgnoreCase);
            }
            return found;
        }
    }

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

    /// <summary>
    /// The tag a delta route may aim at for this operation, or <c>null</c> when the delta path must
    /// not run at all. The eligibility half is <see cref="IsEligible"/>; what this adds is the two
    /// rules that depend on WHICH operation is running, which used to live as bare conditions
    /// inside <c>RepairInstallAsync</c> and were therefore pinned by nothing.
    ///
    /// <para><b>A plain REPAIR never patches</b> (<paramref name="asUpdate"/> false). A repair
    /// exists because the install is damaged, and a patch assumes the files it touches are exactly
    /// the ones its descriptor recorded — a guarantee <c>PreVerify</c> can only make when the
    /// descriptor carries <c>fromSha256</c>. Its degraded mode would otherwise let a patch land on
    /// bytes nobody can vouch for, which is the one place patching could make an install worse
    /// instead of faster.</para>
    ///
    /// <para><b>A user-chosen version wins over the effective tag.</b>
    /// <paramref name="targetReleaseTag"/> is set only by the version picker, so a non-empty value
    /// means "route to the version the player clicked" rather than to the recommended one. This is
    /// what lets a forward version change ride the same patch chain an ordinary update does; a
    /// ROLLBACK still finds no route, because patches are directional and nothing generates the
    /// inverse, so it falls through to the full download exactly as before.</para>
    /// </summary>
    internal static string? ResolveDeltaTarget(
        ModProfile? profile, bool asUpdate, string? targetReleaseTag, string? effectiveTag)
    {
        if (!asUpdate) return null;
        if (!IsEligible(profile)) return null;

        var target = string.IsNullOrWhiteSpace(targetReleaseTag) ? effectiveTag : targetReleaseTag;
        return string.IsNullOrWhiteSpace(target) ? null : target;
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
    /// pattern. Returns the two output paths.
    ///
    /// <para>The modder uploads BOTH to the release named by <paramref name="toTag"/>. The full
    /// new zip goes up only when that release is itself a baseline — this used to say "plus the
    /// full new zip" unconditionally, which is the pre-patch-only model and throws away the whole
    /// saving.</para>
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

            var stem = PatchAssetNaming.StemFor(fromTag, toTag);
            var patchZipName = stem + ".zip";
            var patchJsonName = stem + ".json";
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
            long fullSize = 0;
            try { fullSize = new FileInfo(newZipPath).Length; } catch { /* advice only */ }
            return new GenerateResult(
                patchZipPath, patchJsonPath, changed.Count, deleted.Count, patchSize,
                fullSize, ShouldAdviseRebaseline(patchSize, fullSize));
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

    /// <summary>
    /// Tag → filename-safe token. <b>Lossy on purpose</b>, which is why a patch FILENAME can only
    /// ever propose a hop and the downloaded descriptor has to confirm it — see
    /// <see cref="PatchAssetNaming.ProposeHop"/>. Internal so the planner can invert it against
    /// the repo's real tags.
    /// </summary>
    internal static string Sanitize(string tag)
    {
        var chars = (tag ?? "").Select(ch =>
            char.IsLetterOrDigit(ch) || ch == '.' || ch == '-' || ch == '_' ? ch : '_').ToArray();
        var s = new string(chars).Trim('_');
        return s.Length == 0 ? "x" : s;
    }

    // ---------------------------------------------------------------- consumer: prepare one hop

    /// <summary>
    /// Download, confirm and pre-verify ONE planned hop, ready to apply. Returns null for any
    /// doubt — the caller then stops the chain and falls back to the full download.
    ///
    /// <para><b>Makes no API call.</b> The planner already carried the hop's host-release assets
    /// in <paramref name="step"/>, so everything here comes off the asset CDN. That matters: the
    /// unauthenticated GitHub API allows 60 requests an hour, and the shape this replaced
    /// re-listed the release — and re-downloaded every descriptor on it — once per patch.</para>
    ///
    /// <para><b>The filename only PROPOSED this hop; here is where the descriptor confirms it.</b>
    /// <see cref="Sanitize"/> is lossy, so a name can in principle resolve to the wrong pair of
    /// tags — the downloaded descriptor's real <c>fromTag</c>/<c>toTag</c> must agree with what we
    /// came for, which is what <see cref="SelectPatch"/> is asked, one candidate at a time.</para>
    ///
    /// <para><paramref name="manifest"/> must be re-read by the caller BEFORE every hop: in a
    /// chain each hop's base state is the manifest the previous hop just wrote, and pre-verifying
    /// against a stale one is exactly what the per-hop commit exists to prevent.</para>
    /// </summary>
    /// <param name="confirmSpace">
    /// Called with the patch zip's compressed size BEFORE anything is downloaded. Optional; a
    /// chain checks space once for the whole plan instead and passes null.
    ///
    /// <para>To CANCEL, throw <see cref="OperationCanceledException"/> from inside — it is
    /// rethrown untouched and the whole update unwinds. Deliberately not "return false to fall
    /// back to the full path": the full download needs MORE room than the delta, so offering it
    /// to someone who just declined for lack of space would be nonsense.</para>
    /// </param>
    internal static async Task<PreparedPatch?> TryPrepareStepAsync(
        DeltaChainPlanner.PlanStep step, string installedTag, string installPath,
        InstallManifest? manifest, IReadOnlyList<string>? coveredFiles, CancellationToken ct,
        Func<long, bool>? confirmSpace = null)
    {
        try
        {
            if (step == null || step.Kind != DeltaChainPlanner.StepKind.Patch) return null;
            if (string.IsNullOrWhiteSpace(installedTag)) return null;
            if (!string.Equals(step.FromTag, installedTag, StringComparison.OrdinalIgnoreCase)) return null;
            if (manifest == null || !VerifyService.HasFileHashes(manifest)) return null; // need a hash baseline
            if (string.IsNullOrEmpty(step.DescriptorUrl)) return null;

            // 1. Fetch the descriptor and CONFIRM it really is the hop we planned.
            DeltaPatchDescriptor? descriptor;
            try
            {
                var json = await Http.GetStringAsync(step.DescriptorUrl, ct);
                descriptor = JsonSerializer.Deserialize<DeltaPatchDescriptor>(json, JsonOpts);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"Delta descriptor unreadable ({step.DescriptorName}): {ex.Message}");
                return null;
            }
            if (descriptor == null) return null;

            if (SelectPatch(new[] { descriptor }, installedTag, step.ToTag) == null)
            {
                DiagnosticLog.Write(
                    $"Delta descriptor '{step.DescriptorName}' declares '{descriptor.FromTag}'->'{descriptor.ToTag}', " +
                    $"not the planned '{step.FromTag}'->'{step.ToTag}' — falling back to full.");
                return null;
            }

            if (descriptor.Changed.Count == 0 && descriptor.Deleted.Count == 0) return null;
            if (descriptor.Changed.Count > MaxChangedFiles) return null;

            // 2. Pre-verify against the install (catch a diverged base) — cheap.
            if (!PreVerify(installPath, manifest, descriptor, coveredFiles)) return null;

            // 3. Resolve the payload the descriptor names, on the hop's OWN release.
            var payloadAsset = step.HostReleaseAssets.FirstOrDefault(x =>
                string.Equals(x.Name, descriptor.Payload, StringComparison.OrdinalIgnoreCase));
            if (payloadAsset == null || string.IsNullOrEmpty(payloadAsset.Url)) return null;

            if (confirmSpace != null && !confirmSpace(payloadAsset.Size)) return null;

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
        string PatchZipPath, string PatchJsonPath, int ChangedCount, int DeletedCount, long PatchZipSize,
        long NewFullZipSize, bool AdviseRebaseline);
}
