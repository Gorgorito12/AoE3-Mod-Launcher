using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Downloads release assets from a modder's own GitHub repository for the
/// "Pin to Release Tag" update mechanism (<see cref="ModUpdateMechanism.GitHubReleases"/>).
///
/// Flow:
///   1. Given <see cref="GitHubReleasesSettings.SourceRepo"/> + tag, GET
///      <c>/repos/{repo}/releases/tags/{tag}</c> from the GitHub API to
///      enumerate the release's assets.
///   2. Pick the asset whose filename matches
///      <see cref="GitHubReleasesSettings.AssetNamePattern"/> (default:
///      "first .zip wins").
///   3. Stream-download that asset to a local file path, reporting
///      progress so the launcher's progress panel can render a bar.
///
/// Failure modes — all surfaced as exceptions for the caller to wrap:
///   * Tag doesn't exist → HttpRequestException with 404.
///   * Repo is private or rate-limited → HttpRequestException with 403.
///   * Release has no matching asset → InvalidOperationException.
///   * Network glitch mid-download → IOException / HttpRequestException.
/// The InstallerService is responsible for catching these and
/// surfacing user-friendly status / retry UI.
/// </summary>
public class GitHubReleaseDownloader
{
    /// <summary>
    /// Shared HttpClient. GitHub's release API + asset CDN both want a
    /// User-Agent header; we set it once and reuse the client across all
    /// instances to avoid socket exhaustion on rapid-fire calls.
    /// </summary>
    private static readonly HttpClient Http = CreateHttpClient();

    /// <summary>
    /// Result of resolving a GitHubReleases payload reference into concrete download URLs.
    ///
    /// <para><see cref="Urls"/> holds ONE url in the common case and SEVERAL — in part order —
    /// when the modder split the payload across <c>.zip.001</c> / <c>.002</c> / … assets because
    /// it exceeds GitHub's 2 GB per-asset limit. The download side needs no special case: it is
    /// the same <c>string[]</c> the multi-part WoL payload has always used, so resume, retry and
    /// per-part verification come for free.</para>
    ///
    /// <para><see cref="ExpectedSha256"/> is non-null only when the modder pinned the payload to
    /// an external host (via <see cref="GitHubReleasesSettings.ExternalAssetUrlTemplate"/>) and
    /// declared its SHA in the catalog — in that case the caller MUST verify the downloaded file's
    /// hash and reject mismatches. That path is single-url by construction (one template, one
    /// pinned hash), so the list is parallel to <see cref="Urls"/> and never disagrees in length.
    /// For regular GitHub-hosted assets it stays null because the launcher trusts the asset CDN
    /// inherently.</para>
    ///
    /// <para><see cref="Size"/> is the TOTAL across every part, and <c>-1</c> when unknown
    /// (external URLs the launcher hasn't probed); callers fall back to Content-Length.</para>
    /// </summary>
    public record ResolvedPayload(
        IReadOnlyList<string> Urls, long Size, IReadOnlyList<string>? ExpectedSha256)
    {
        /// <summary>True when the modder split the payload across several release assets.</summary>
        public bool IsMultipart => Urls.Count > 1;
    }

    /// <summary>
    /// One selectable version from a mod's GitHub repo. <see cref="Prerelease"/>
    /// flags GitHub "pre-release" entries so the UI can mark them. Drafts are
    /// filtered out (no public assets).
    /// </summary>
    public record ReleaseInfo(string Tag, string Name, bool Prerelease);

    /// <summary>One asset on a release: filename, size, and download URL. Used by the delta-patch
    /// path (<see cref="DeltaPatchService"/>) to discover a mod's optional patch assets. A release
    /// may carry patches with NO full <c>.zip</c> beside them (a patch-only release); the patch
    /// assets are ignored by <see cref="PickAsset"/> either way.</summary>
    public record ReleaseAsset(string Name, long Size, string Url);

    /// <summary>
    /// Return EVERY asset attached to <paramref name="tag"/>'s release (name/size/url) — the full
    /// list <see cref="ResolveAssetAsync"/> already fetches but collapses to one. Lets the delta
    /// path find a <c>patch-*.zip</c>/<c>.json</c> without a second API call shape. Returns an
    /// empty list on any failure (the caller treats "no assets" as "no delta → full").
    /// </summary>
    public async Task<IReadOnlyList<ReleaseAsset>> ListAssetsAsync(
        string sourceRepo, string tag, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sourceRepo) || string.IsNullOrWhiteSpace(tag))
            return new List<ReleaseAsset>();
        try
        {
            var apiUrl = $"https://api.github.com/repos/{sourceRepo}/releases/tags/{tag}";
            var release = await Http.GetFromJsonAsync<GitHubRelease>(apiUrl, ct);
            if (release?.Assets == null) return new List<ReleaseAsset>();
            return release.Assets
                .Where(a => !string.IsNullOrEmpty(a.Name) && !string.IsNullOrEmpty(a.BrowserDownloadUrl))
                .Select(a => new ReleaseAsset(a.Name, a.Size, a.BrowserDownloadUrl))
                .ToList();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"GitHubReleases: ListAssetsAsync('{sourceRepo}','{tag}') failed: {ex.Message}");
            return new List<ReleaseAsset>();
        }
    }

    /// <summary>
    /// Enumerate the mod's published releases (newest first) so the user can
    /// pick a version to install instead of only the catalog's approved tag.
    /// Mirrors <see cref="TranslationRegistryService"/>'s use of the paginated
    /// <c>/releases</c> endpoint. Drafts are skipped; prereleases are kept and
    /// flagged. Returns at most the newest 100 (one API page) — a soft cap that
    /// covers every real mod's history; older entries are logged as omitted.
    /// Throws on network / auth failure for the caller to surface.
    /// </summary>
    public async Task<IReadOnlyList<ReleaseInfo>> ListReleasesAsync(
        string sourceRepo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sourceRepo))
            throw new ArgumentException("SourceRepo is required.", nameof(sourceRepo));

        var apiUrl = $"https://api.github.com/repos/{sourceRepo}/releases?per_page=100";
        DiagnosticLog.Write($"GitHubReleases: listing {apiUrl}");

        var releases = await Http.GetFromJsonAsync<List<GitHubRelease>>(apiUrl, ct)
            ?? new List<GitHubRelease>();

        var list = new List<ReleaseInfo>();
        foreach (var r in releases)
        {
            if (r.Draft) continue;                       // not installable
            if (string.IsNullOrWhiteSpace(r.TagName)) continue;
            list.Add(new ReleaseInfo(
                r.TagName,
                string.IsNullOrWhiteSpace(r.Name) ? r.TagName : r.Name,
                r.Prerelease));
        }

        if (releases.Count >= 100)
            DiagnosticLog.Write(
                "GitHubReleases: release list hit the 100-item page cap; older versions omitted.");

        return list;
    }

    /// <summary>
    /// Newest releases that ship a downloadable asset, WITH its URL and size —
    /// what <see cref="ModVersionFingerprint"/> needs to read each release's zip
    /// index remotely and work out which one the user actually has installed.
    ///
    /// Deliberately separate from <see cref="ListReleasesAsync"/> (which returns
    /// display info only) but backed by the SAME single API call, so adding this
    /// costs no extra rate-limit budget beyond the one listing.
    /// Prereleases and drafts are skipped: a user's install should never be
    /// identified as an unpublished build.
    /// </summary>
    public async Task<IReadOnlyList<ModVersionFingerprint.Candidate>> ListReleaseCandidatesAsync(
        string sourceRepo, int maxCount, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sourceRepo) || maxCount <= 0)
            return Array.Empty<ModVersionFingerprint.Candidate>();

        var apiUrl = $"https://api.github.com/repos/{sourceRepo}/releases?per_page=100";
        var releases = await Http.GetFromJsonAsync<List<GitHubRelease>>(apiUrl, ct)
            ?? new List<GitHubRelease>();

        var list = new List<ModVersionFingerprint.Candidate>();
        foreach (var r in releases)
        {
            if (list.Count >= maxCount) break;
            if (r.Draft || r.Prerelease) continue;
            if (string.IsNullOrWhiteSpace(r.TagName) || r.Assets == null) continue;

            var asset = PickAsset(r.Assets, null);
            if (asset == null || asset.Size <= 0
                || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl)) continue;

            list.Add(new ModVersionFingerprint.Candidate(
                r.TagName, asset.BrowserDownloadUrl, asset.Size));
        }
        return list;
    }

    /// <summary>
    /// Every non-draft release with EVERY asset (name, size, url) — the whole patch graph and all
    /// of its costs, for <see cref="DeltaChainPlanner"/> to plan a route over.
    ///
    /// <para>Backed by the SAME single <c>GET /releases?per_page=100</c> that
    /// <see cref="ListReleasesAsync"/> and <see cref="ListReleaseCandidatesAsync"/> already make,
    /// so — like that one — it costs no extra rate-limit budget beyond the one listing. That is
    /// what makes planning affordable at all against an unauthenticated 60/hour limit: one
    /// request answers "which releases exist, what does each carry, and how big is it".</para>
    ///
    /// <para>Returns an EMPTY list on any failure rather than throwing. The planner then finds no
    /// route and the caller does exactly what it does today — a rate-limited or offline launcher
    /// must lose the shortcut, never the update.</para>
    /// </summary>
    internal async Task<IReadOnlyList<DeltaChainPlanner.ReleaseSnapshot>> ListReleaseGraphAsync(
        string sourceRepo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sourceRepo))
            return Array.Empty<DeltaChainPlanner.ReleaseSnapshot>();

        try
        {
            var apiUrl = $"https://api.github.com/repos/{sourceRepo}/releases?per_page=100";
            var releases = await Http.GetFromJsonAsync<List<GitHubRelease>>(apiUrl, ct)
                ?? new List<GitHubRelease>();

            var list = new List<DeltaChainPlanner.ReleaseSnapshot>();
            foreach (var r in releases)
            {
                if (r.Draft) continue;
                if (string.IsNullOrWhiteSpace(r.TagName)) continue;

                var assets = new List<DeltaChainPlanner.ReleaseAssetSnapshot>();
                foreach (var a in r.Assets ?? new List<GitHubAsset>())
                {
                    if (string.IsNullOrWhiteSpace(a.Name)) continue;
                    if (string.IsNullOrWhiteSpace(a.BrowserDownloadUrl)) continue;
                    assets.Add(new DeltaChainPlanner.ReleaseAssetSnapshot(
                        a.Name, a.Size, a.BrowserDownloadUrl));
                }

                list.Add(new DeltaChainPlanner.ReleaseSnapshot(r.TagName, r.Prerelease, assets));
            }

            DiagnosticLog.Write(
                $"GitHubReleases: release graph for '{sourceRepo}' = {list.Count} release(s).");
            return list;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"GitHubReleases: ListReleaseGraphAsync('{sourceRepo}') failed: {ex.Message}");
            return Array.Empty<DeltaChainPlanner.ReleaseSnapshot>();
        }
    }

    /// <summary>
    /// Result of resolving <c>/releases/latest</c>. <see cref="NotModified"/>
    /// means "the caller's cached tag is still the latest" (HTTP 304).
    /// <see cref="Tag"/> null WITHOUT NotModified = failure — the caller falls
    /// back to its cached tag / approved tag. <see cref="ETag"/> is the value
    /// to persist: the fresh one on a 200, the one we sent on 304/failure
    /// (never lose a good ETag to a transient blip).
    /// </summary>
    public record LatestReleaseResult(string? Tag, string? ETag, bool NotModified);

    /// <summary>
    /// Resolve the repo's newest STABLE release tag via
    /// <c>GET /repos/{repo}/releases/latest</c> — GitHub excludes drafts and
    /// prereleases from that endpoint by definition, which is exactly the
    /// follow-latest contract (a modder's prerelease is never auto-published
    /// to users). NEVER throws except on cancellation: this runs inside
    /// <c>UpdateService.CheckCoreAsync</c>, and letting a failure bubble to
    /// CheckAsync's offline catch would degrade the WHOLE check to
    /// BuildOfflineResult — suppressing even the approved-tag update path,
    /// which needs no network. A failure here must only degrade the
    /// follow-latest bonus. Conditional request: pass the persisted ETag and
    /// a 304 (free — conditional requests don't count against the
    /// unauthenticated rate limit) confirms the cached tag is still current.
    /// Connectivity uses LauncherUpdateService's reachedServer pattern:
    /// success reported after SendAsync returns (a real network round-trip),
    /// failure only when the server was never reached and it wasn't a cancel.
    /// </summary>
    public async Task<LatestReleaseResult> GetLatestReleaseTagAsync(
        string sourceRepo, string? cachedETag = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sourceRepo))
            return new LatestReleaseResult(null, cachedETag, false);

        bool reachedServer = false;
        try
        {
            var apiUrl = $"https://api.github.com/repos/{sourceRepo}/releases/latest";
            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            if (!string.IsNullOrEmpty(cachedETag))
                request.Headers.TryAddWithoutValidation("If-None-Match", cachedETag);

            using var response = await Http.SendAsync(request, ct);
            reachedServer = true;
            ConnectivityState.ReportSuccess();

            if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
            {
                // Log it: this used to be the ONE branch that returned no tag and
                // left no trace, so the caller's silent fall-back to the cached tag
                // was indistinguishable from "there is no newer version" — which is
                // exactly what hid a stale cross-repo cache for hours.
                DiagnosticLog.Write(
                    $"GitHubReleases: /releases/latest for '{sourceRepo}' returned 304 " +
                    $"(unchanged) — caller keeps its cached tag.");
                return new LatestReleaseResult(null, cachedETag, true);
            }

            if (!response.IsSuccessStatusCode)
            {
                DiagnosticLog.Write(
                    $"GitHubReleases: /releases/latest for '{sourceRepo}' returned {(int)response.StatusCode}.");
                return new LatestReleaseResult(null, cachedETag, false);
            }

            var release = await response.Content.ReadFromJsonAsync<GitHubRelease>(cancellationToken: ct);
            // Defensive: the endpoint shouldn't return drafts/prereleases, but a
            // wrong tag here would auto-publish it to every user — treat as failure.
            if (release == null || string.IsNullOrWhiteSpace(release.TagName)
                || release.Draft || release.Prerelease)
                return new LatestReleaseResult(null, cachedETag, false);

            return new LatestReleaseResult(
                release.TagName, response.Headers.ETag?.ToString() ?? cachedETag, false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            if (!reachedServer && !ct.IsCancellationRequested)
                ConnectivityState.ReportFailure(ex);
            DiagnosticLog.Write(
                $"GitHubReleases: GetLatestReleaseTagAsync('{sourceRepo}') failed: {ex.Message}");
            return new LatestReleaseResult(null, cachedETag, false);
        }
    }

    /// <summary>
    /// Resolve the modder's release tag into a concrete .zip asset URL.
    /// Doesn't download the asset — just enumerates and picks (or, for
    /// external hosting, templates the URL). Use this to pre-flight
    /// before kicking the actual download.
    ///
    /// Two paths:
    ///   1. <see cref="GitHubReleasesSettings.ExternalAssetUrlTemplate"/>
    ///      is set: substitute <c>{tag}</c> with the approved release
    ///      tag and return that URL. The GitHub release itself is never
    ///      contacted — it exists purely as the catalog's version
    ///      marker. The SHA-256 from
    ///      <see cref="GitHubReleasesSettings.ExternalAssetSha256"/> is
    ///      returned so the caller can verify post-download.
    ///   2. Template is empty (the common case): hit the GitHub API and
    ///      pick the matching asset from the release. No SHA — we trust
    ///      GitHub's CDN.
    ///
    /// Throws when the tag doesn't exist, the release has no matching
    /// asset, or an external URL was configured without its SHA-256.
    /// </summary>
    public async Task<ResolvedPayload> ResolveAssetAsync(
        GitHubReleasesSettings settings, string? overrideTag = null, CancellationToken ct = default,
        string? nonPayloadAsset = null)
    {
        if (string.IsNullOrWhiteSpace(settings.SourceRepo))
            throw new ArgumentException("SourceRepo is required.", nameof(settings));
        if (string.IsNullOrWhiteSpace(settings.ApprovedReleaseTag))
            throw new ArgumentException("ApprovedReleaseTag is required.", nameof(settings));

        // The version to resolve: the user-chosen tag when set, else the
        // catalog's approved/recommended tag (the default path, unchanged).
        var tag = string.IsNullOrWhiteSpace(overrideTag)
            ? settings.ApprovedReleaseTag
            : overrideTag.Trim();

        // --- External-hosting path -------------------------------------------
        // When the modder hosts the binary outside GitHub Releases (their
        // own CDN, S3, archive.org, ...), the catalog points at a URL
        // template and a pinned SHA-256. The GitHub release exists only
        // to anchor the version tag; we never call the GitHub API here.
        if (!string.IsNullOrWhiteSpace(settings.ExternalAssetUrlTemplate))
        {
            // External hosts pin a SHA-256 for the APPROVED tag only, so we can't
            // verify any OTHER version's payload — refuse a version switch here
            // rather than install an unverifiable binary.
            if (!string.Equals(tag, settings.ApprovedReleaseTag, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Version selection isn't available for '{settings.SourceRepo}': it hosts its payload " +
                    $"externally with a single pinned hash, so only the recommended version can be verified.");

            if (string.IsNullOrWhiteSpace(settings.ExternalAssetSha256))
            {
                // Refuse external URLs without a hash. Otherwise a
                // compromised host could silently swap the payload and
                // the launcher would have no way to detect it. The
                // catalog schema marks SHA-256 as required when the
                // template is set, so this is a defence-in-depth check.
                throw new InvalidOperationException(
                    $"GitHubReleases settings for '{settings.SourceRepo}' declare an external asset URL " +
                    $"but no externalAssetSha256. Reject for safety — without a pinned hash a compromised " +
                    $"host could swap the payload undetected.");
            }

            var external = settings.ExternalAssetUrlTemplate.Replace(
                "{tag}", tag);
            DiagnosticLog.Write(
                $"GitHubReleases: resolved external asset '{external}' " +
                $"(sha256={settings.ExternalAssetSha256.ToLowerInvariant()})");
            // Size unknown without a HEAD probe — let the download path
            // pick it up from Content-Length. The downloader tolerates
            // -1 by falling through to the response header.
            return new ResolvedPayload(
                new[] { external }, -1, new[] { settings.ExternalAssetSha256.ToLowerInvariant() });
        }

        // --- Regular GitHub Release asset path -------------------------------
        var apiUrl = $"https://api.github.com/repos/{settings.SourceRepo}/releases/tags/{tag}";
        DiagnosticLog.Write($"GitHubReleases: fetching {apiUrl}");

        var release = await Http.GetFromJsonAsync<GitHubRelease>(apiUrl, ct)
            ?? throw new InvalidOperationException(
                $"GitHub release '{tag}' in '{settings.SourceRepo}' returned empty.");

        if (release.Assets == null || release.Assets.Count == 0)
            throw new InvalidOperationException(
                $"Release '{tag}' has no downloadable assets.");

        // A payload too big for GitHub's 2 GB per-asset limit is published split across
        // "<name>.zip.001", ".002", ... — resolve those first, in part order. Nothing downstream
        // changes: the concatenating downloader has taken a url array since the WoL payload.
        var names = release.Assets.Select(a => a.Name ?? "").ToList();
        var parts = PickPayloadPartIndices(names, settings.AssetNamePattern, nonPayloadAsset);
        if (parts.Count > 0)
        {
            var partUrls = parts.Select(i => release.Assets[i].BrowserDownloadUrl).ToList();
            var totalBytes = parts.Sum(i => release.Assets[i].Size);
            DiagnosticLog.Write(
                $"GitHubReleases: resolved {parts.Count}-part asset '{release.Assets[parts[0]].Name}' " +
                $"({totalBytes} bytes total) in release '{tag}'");
            return new ResolvedPayload(partUrls, totalBytes, null);
        }

        // Parts present but unusable: say WHICH part is missing rather than "no asset matching".
        var partProblem = DescribeUnusablePartSet(names);
        if (partProblem != null)
            throw new InvalidOperationException($"Release '{tag}': {partProblem}");

        var asset = PickAsset(release.Assets, settings.AssetNamePattern, nonPayloadAsset)
            ?? throw new InvalidOperationException(
                $"No asset matching '{settings.AssetNamePattern}' (or *.zip / *.zip.001 fallback) " +
                $"in release '{tag}'.");

        DiagnosticLog.Write(
            $"GitHubReleases: resolved asset '{asset.Name}' ({asset.Size} bytes) at {asset.BrowserDownloadUrl}");
        return new ResolvedPayload(new[] { asset.BrowserDownloadUrl }, asset.Size, null);
    }

    /// <summary>
    /// Streams the asset to <paramref name="destinationPath"/>. The
    /// caller is expected to have called <see cref="ResolveAssetAsync"/>
    /// first to get <paramref name="url"/> + <paramref name="totalBytes"/>.
    /// Reports byte progress through <paramref name="progress"/>; safe to
    /// pass null if the caller doesn't care.
    ///
    /// Writes to a <c>.tmp</c> file first and atomically moves on success
    /// so a partial download doesn't masquerade as a complete one if the
    /// launcher crashes mid-flight.
    /// </summary>
    public async Task DownloadAsync(
        string url,
        string destinationPath,
        long totalBytes,
        IProgress<(long bytesDone, long bytesTotal)>? progress = null,
        CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tmpPath = destinationPath + ".tmp";

        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        // Prefer the server's Content-Length when present (it should
        // match `totalBytes` from ResolveAssetAsync, but a mid-flight
        // mirror change might disagree — trust the response if so).
        long expected = response.Content.Headers.ContentLength ?? totalBytes;

        await using (var src = await response.Content.ReadAsStreamAsync(ct))
        await using (var dst = File.Create(tmpPath))
        {
            long copied = 0;
            var buffer = new byte[81920];
            int read;
            while ((read = await src.ReadAsync(buffer, ct)) > 0)
            {
                copied += read;
                await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                progress?.Report((copied, expected));
            }
        }

        // Atomic swap. If the destination already exists (e.g. resume
        // attempt with a stale .tmp), we wipe it first so File.Move
        // doesn't throw — the user explicitly asked for a fresh download
        // by triggering this code path.
        if (File.Exists(destinationPath)) File.Delete(destinationPath);
        File.Move(tmpPath, destinationPath);

        DiagnosticLog.Write($"GitHubReleases: download complete -> {destinationPath}");
    }

    // -- Asset selection ------------------------------------------------------

    /// <summary>
    /// Index of the FULL payload asset within <paramref name="assetNames"/>, or null when the
    /// release ships nothing installable. Pure and name-only so it can be unit-tested — the
    /// wrapper below is what deals in DTOs.
    ///
    /// <para>Priority:
    ///   1. If <paramref name="pattern"/> is set, the first name matching it (glob, <c>*</c> →
    ///      <c>.*</c>, case-insensitive).
    ///   2. Otherwise the first name ending in <c>.zip</c>.
    ///   3. Otherwise null — caller throws.</para>
    ///
    /// <para><b>Delta-patch assets are removed from the pool first, and that exclusion is the
    /// whole point of this function existing.</b> A delta release carries two <c>.zip</c>s and
    /// GitHub returns assets in upload order, so "first .zip wins" picked
    /// <c>patch-…-to-….zip</c> as the full overlay whenever the modder happened to upload it
    /// first — following this project's own documented recipe. Downstream,
    /// <see cref="NativeInstallService.ApplyUpdateDeletions"/> then computed "net-new files the
    /// new release no longer ships" against those few files and deleted essentially the entire
    /// mod overlay, discarding the backups on the way out because that is the SUCCESS path.
    /// <see cref="ListReleaseCandidatesAsync"/> shares the fix: it would otherwise index a
    /// patch's zip as a release's version fingerprint.</para>
    ///
    /// <para><b>If excluding the patches leaves nothing selectable, the selection is retried over
    /// the unfiltered list — but only when the release carries no patch DESCRIPTOR.</b> That
    /// condition is what separates the two cases this has to tell apart, and getting it wrong
    /// breaks one of them:</para>
    /// <list type="bullet">
    /// <item>A release with <c>patch-*.json</c> beside it is a genuine patch release. Its
    /// <c>patch-*.zip</c> is a patch, and a release that carries only patches has <b>no</b> full
    /// payload — saying otherwise re-opens the very bug above, since a patch-only release is now
    /// a supported thing to publish.</item>
    /// <item>A lone <c>patch-*.zip</c> with no descriptor anywhere is not patch machinery at all;
    /// it is a mod whose payload simply happens to be named that way, and it must keep working
    /// exactly as before. A fix that makes a working release uninstallable is not one.</item>
    /// </list>
    /// <para>Note the retry triggers on "nothing was selected", not on "the pool is empty": a
    /// release holding <c>readme.txt</c> next to <c>patch-a-to-b.zip</c> leaves a non-empty pool
    /// that still contains no payload.</para>
    /// </summary>
    /// <summary>
    /// An asset the manifest has DECLARED is not the mod payload — today that is
    /// <c>install.userDataPayload</c>, the second zip that seeds the player's <c>My Games</c>
    /// folder.
    ///
    /// <para><b>This is the patch-asset data-loss bug arriving through a second door.</b> The
    /// payload rule is "the first <c>.zip</c>", GitHub lists assets in UPLOAD-COMPLETION order,
    /// and a seed zip is a few kilobytes against a payload of hundreds of megabytes — so a modder
    /// who drags both files in at once will usually have the SEED finish first and be picked as
    /// the entire mod. The install then "succeeds" with no overlay at all, and the next update
    /// computes <c>ApplyUpdateDeletions</c> against those few files and removes the real overlay,
    /// discarding the backups because that is the success path. Exactly what a stray
    /// <c>patch-*.zip</c> did before <c>PatchAssetNaming</c> excluded it.</para>
    /// </summary>
    private static bool IsNonPayload(string? assetName, string? nonPayloadAsset)
        => !string.IsNullOrWhiteSpace(nonPayloadAsset)
           && string.Equals(assetName, nonPayloadAsset, StringComparison.OrdinalIgnoreCase);

    internal static int? PickAssetIndex(
        IReadOnlyList<string> assetNames, string? pattern, string? nonPayloadAsset = null)
    {
        if (assetNames == null || assetNames.Count == 0) return null;

        var kept = new List<int>();
        bool anyDescriptor = false;
        for (int i = 0; i < assetNames.Count; i++)
        {
            if (DeltaPatchService.PatchAssetNaming.IsDescriptor(assetNames[i])) anyDescriptor = true;
            if (DeltaPatchService.PatchAssetNaming.IsPatchAsset(assetNames[i])) continue;
            if (IsNonPayload(assetNames[i], nonPayloadAsset)) continue;
            kept.Add(i);
        }

        var selected = SelectFrom(kept);
        if (selected != null || anyDescriptor) return selected;

        // The patch exclusion is retried over the unfiltered list, because with no descriptor
        // anywhere a lone "patch-*.zip" is just a mod whose payload happens to be named that way.
        // The userDataPayload exclusion is NOT retried: the manifest DECLARED that asset is not the
        // payload, so re-admitting it here would hand the installer the seed as the whole mod
        // through the back door — which is the very failure this parameter exists to stop.
        var all = new List<int>();
        for (int i = 0; i < assetNames.Count; i++)
            if (!IsNonPayload(assetNames[i], nonPayloadAsset)) all.Add(i);
        return SelectFrom(all);

        int? SelectFrom(IReadOnlyList<int> pool)
        {
            if (pool.Count == 0) return null;

            if (!string.IsNullOrWhiteSpace(pattern))
            {
                var rx = GlobToRegex(pattern);
                foreach (var i in pool)
                    if (rx.IsMatch(assetNames[i] ?? "")) return i;
                // Pattern was set but matched nothing — fall through to the .zip
                // heuristic. If the modder intended strict matching they can
                // raise a stricter pattern (e.g. "^modname-v.*\\.zip$").
            }

            foreach (var i in pool)
                if ((assetNames[i] ?? "").EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    return i;

            return null;
        }
    }

    /// <summary>
    /// The part-number suffix a split payload uses: <c>&lt;name&gt;.zip.001</c>, <c>.002</c>, …
    /// Three digits is the convention 7-Zip and WinRAR emit, and keeping it strict is what stops
    /// an unrelated asset (<c>notes.zip.backup</c>) from being read as part of the payload.
    /// </summary>
    private static readonly Regex PartSuffix = new(
        @"^(?<base>.+\.zip)\.(?<n>\d{3})$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The ordered indices of a payload split across several release assets, or an EMPTY list when
    /// the release carries no such set (the caller then falls back to <see cref="PickAssetIndex"/>,
    /// i.e. to exactly the behaviour every mod had before).
    ///
    /// <para>Why this is separate from <see cref="PickAssetIndex"/> rather than folded into it:
    /// that method also answers "which asset can be range-read as a zip", which is how
    /// <see cref="ListReleaseCandidatesAsync"/> fingerprints an installed version. A
    /// <c>.zip.001</c> is NOT a zip — it has no end-of-central-directory record — so teaching that
    /// method about parts would hand <c>RemoteZipIndex</c> a file it cannot parse. A split release
    /// correctly yields nothing there and is skipped, which is the right degradation.</para>
    ///
    /// <para><b>A gap is refused, not worked around.</b> The parts are concatenated byte-for-byte
    /// into one zip, so a missing <c>.002</c> produces a corrupt archive after a multi-GB
    /// download — and the resulting <c>InvalidDataException</c> would be blamed on the network.
    /// Requiring a complete <c>001..N</c> run turns that into a clear failure before anything is
    /// fetched. For the same reason the order is the part NUMBER, never the order GitHub happens
    /// to list assets in.</para>
    /// </summary>
    internal static IReadOnlyList<int> PickPayloadPartIndices(
        IReadOnlyList<string> assetNames, string? pattern, string? nonPayloadAsset = null)
    {
        var empty = Array.Empty<int>();
        if (assetNames == null || assetNames.Count == 0) return empty;

        // base name -> part number -> index. Grouped case-insensitively because GitHub asset
        // names are compared that way everywhere else in this file.
        var groups = new Dictionary<string, SortedDictionary<int, int>>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < assetNames.Count; i++)
        {
            var name = assetNames[i] ?? "";
            var m = PartSuffix.Match(name);
            if (!m.Success) continue;

            // A patch's OWN parts are still patch machinery, not the mod's payload. Asking the
            // one definition of that (DeltaPatchService) rather than re-deriving it here is what
            // keeps the two from drifting apart.
            var baseName = m.Groups["base"].Value;
            if (DeltaPatchService.PatchAssetNaming.IsPatchAsset(baseName)) continue;
            // ...and neither is a declared non-payload asset that happens to be split.
            if (IsNonPayload(baseName, nonPayloadAsset)) continue;

            if (!int.TryParse(m.Groups["n"].Value, out var part)) continue;
            if (!groups.TryGetValue(baseName, out var parts))
                groups[baseName] = parts = new SortedDictionary<int, int>();
            // A duplicate part number means the release is malformed; refuse the whole group
            // rather than silently picking one of them.
            if (!parts.TryAdd(part, i)) parts[part] = -1;
        }
        if (groups.Count == 0) return empty;

        // With a pattern set the modder is naming their payload explicitly, so a group whose base
        // matches wins. Without one, the biggest complete set wins, tie-broken by name so the
        // answer never depends on GitHub's asset ordering.
        IEnumerable<KeyValuePair<string, SortedDictionary<int, int>>> ordered = groups;
        if (!string.IsNullOrWhiteSpace(pattern))
        {
            var rx = GlobToRegex(pattern);
            ordered = groups.Where(g => rx.IsMatch(g.Key));
            if (!ordered.Any()) ordered = groups;   // pattern matched nothing — same fall-through as PickAssetIndex
        }

        foreach (var g in ordered
                     .OrderByDescending(g => g.Value.Count)
                     .ThenBy(g => g.Key, StringComparer.Ordinal))
        {
            if (!IsCompleteRun(g.Value)) continue;
            return g.Value.Values.ToList();
        }
        return empty;

        // 001..N with nothing missing, nothing duplicated, and starting at 1.
        static bool IsCompleteRun(SortedDictionary<int, int> parts)
        {
            if (parts.Count < 2) return false;      // one ".001" alone is not a split payload
            int expected = 1;
            foreach (var kv in parts)
            {
                if (kv.Key != expected || kv.Value < 0) return false;
                expected++;
            }
            return true;
        }
    }

    /// <summary>
    /// When a release clearly MEANS to ship a split payload but the set is unusable, the specific
    /// reason — so the user is told "part 002 is missing" instead of the generic "no asset
    /// matching ''", which reads like a launcher bug and sends them to the wrong place. Returns
    /// null when the release ships no part-shaped asset at all (the ordinary single-asset case).
    /// </summary>
    internal static string? DescribeUnusablePartSet(IReadOnlyList<string> assetNames)
    {
        var groups = new Dictionary<string, SortedSet<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in assetNames)
        {
            var m = PartSuffix.Match(raw ?? "");
            if (!m.Success) continue;
            var baseName = m.Groups["base"].Value;
            if (DeltaPatchService.PatchAssetNaming.IsPatchAsset(baseName)) continue;
            if (!int.TryParse(m.Groups["n"].Value, out var part)) continue;
            if (!groups.TryGetValue(baseName, out var set))
                groups[baseName] = set = new SortedSet<int>();
            set.Add(part);
        }
        if (groups.Count == 0) return null;

        var g = groups.OrderByDescending(x => x.Value.Count)
                      .ThenBy(x => x.Key, StringComparer.Ordinal)
                      .First();
        int expected = 1;
        foreach (var n in g.Value)
        {
            if (n != expected)
                return $"'{g.Key}' is published in parts but part {expected:D3} is missing " +
                       $"(found {string.Join(", ", g.Value.Select(v => v.ToString("D3")))}). " +
                       "The complete set has to be re-uploaded.";
            expected++;
        }
        return $"'{g.Key}' is published in parts but only part 001 is present — " +
               "a split payload needs every part.";
    }

    /// <summary>DTO adapter over <see cref="PickAssetIndex"/>.</summary>
    private static GitHubAsset? PickAsset(
        IEnumerable<GitHubAsset> assets, string? pattern, string? nonPayloadAsset = null)
    {
        var list = assets.ToList();
        var i = PickAssetIndex(list.Select(a => a.Name ?? "").ToList(), pattern, nonPayloadAsset);
        return i == null ? null : list[i.Value];
    }

    /// <summary>
    /// Tiny glob → regex helper. Supports <c>*</c> as a wildcard; treats
    /// every other character as literal (escaped via Regex.Escape).
    /// Anchored at both ends so "napoleonic-*.zip" doesn't accidentally
    /// match "foo-napoleonic-x.zip".
    /// </summary>
    private static Regex GlobToRegex(string pattern)
    {
        var parts = pattern.Split('*');
        var escaped = string.Join(".*", parts.Select(Regex.Escape));
        return new Regex("^" + escaped + "$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            // Per-call timeout: release-asset .zips can be hundreds of MB.
            // 30 minutes matches the cap used by DownloadService for the
            // WoL pipeline.
            Timeout = TimeSpan.FromMinutes(30),
        };
        // GitHub returns 403 without a User-Agent.
        client.DefaultRequestHeaders.Add("User-Agent", "WarsOfLibertyLauncher");
        client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
        return client;
    }

    // -- API DTOs -------------------------------------------------------------
    //
    // Only the fields we actually consume; GitHub returns ~30 more per
    // release that we ignore.

    private class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset>? Assets { get; set; }
    }

    private class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = "";
    }
}
