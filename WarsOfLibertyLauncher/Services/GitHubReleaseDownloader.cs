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
    /// Result of resolving a GitHubReleases asset reference into a
    /// concrete download URL. <see cref="ExpectedSha256"/> is non-null
    /// only when the modder pinned the payload to an external host (via
    /// <see cref="GitHubReleasesSettings.ExternalAssetUrlTemplate"/>) and
    /// declared its SHA in the catalog — in that case the caller MUST
    /// verify the downloaded file's hash against this value and reject
    /// mismatches. For regular GitHub-hosted assets it stays null because
    /// the launcher trusts the GitHub asset CDN inherently.
    /// <see cref="Size"/> is <c>-1</c> when unknown (external URLs that
    /// the launcher hasn't probed); callers should fall back to the
    /// response's Content-Length header at download time.
    /// </summary>
    public record ResolvedAsset(string Url, long Size, string? ExpectedSha256);

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
    public async Task<ResolvedAsset> ResolveAssetAsync(
        GitHubReleasesSettings settings, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(settings.SourceRepo))
            throw new ArgumentException("SourceRepo is required.", nameof(settings));
        if (string.IsNullOrWhiteSpace(settings.ApprovedReleaseTag))
            throw new ArgumentException("ApprovedReleaseTag is required.", nameof(settings));

        // --- External-hosting path -------------------------------------------
        // When the modder hosts the binary outside GitHub Releases (their
        // own CDN, S3, archive.org, ...), the catalog points at a URL
        // template and a pinned SHA-256. The GitHub release exists only
        // to anchor the version tag; we never call the GitHub API here.
        if (!string.IsNullOrWhiteSpace(settings.ExternalAssetUrlTemplate))
        {
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
                "{tag}", settings.ApprovedReleaseTag);
            DiagnosticLog.Write(
                $"GitHubReleases: resolved external asset '{external}' " +
                $"(sha256={settings.ExternalAssetSha256.ToLowerInvariant()})");
            // Size unknown without a HEAD probe — let the download path
            // pick it up from Content-Length. The downloader tolerates
            // -1 by falling through to the response header.
            return new ResolvedAsset(external, -1, settings.ExternalAssetSha256.ToLowerInvariant());
        }

        // --- Regular GitHub Release asset path -------------------------------
        var apiUrl = $"https://api.github.com/repos/{settings.SourceRepo}/releases/tags/{settings.ApprovedReleaseTag}";
        DiagnosticLog.Write($"GitHubReleases: fetching {apiUrl}");

        var release = await Http.GetFromJsonAsync<GitHubRelease>(apiUrl, ct)
            ?? throw new InvalidOperationException(
                $"GitHub release '{settings.ApprovedReleaseTag}' in '{settings.SourceRepo}' returned empty.");

        if (release.Assets == null || release.Assets.Count == 0)
            throw new InvalidOperationException(
                $"Release '{settings.ApprovedReleaseTag}' has no downloadable assets.");

        var asset = PickAsset(release.Assets, settings.AssetNamePattern)
            ?? throw new InvalidOperationException(
                $"No asset matching '{settings.AssetNamePattern}' (or *.zip fallback) in release '{settings.ApprovedReleaseTag}'.");

        DiagnosticLog.Write(
            $"GitHubReleases: resolved asset '{asset.Name}' ({asset.Size} bytes) at {asset.BrowserDownloadUrl}");
        return new ResolvedAsset(asset.BrowserDownloadUrl, asset.Size, null);
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
    /// Pick the right asset for the launcher to download.
    /// Priority:
    ///   1. If <paramref name="pattern"/> is set, return the first asset
    ///      whose name matches it (glob with * → regex .*). Case-
    ///      insensitive.
    ///   2. Otherwise the first asset whose name ends in <c>.zip</c>.
    ///   3. Otherwise null — caller throws.
    /// </summary>
    private static GitHubAsset? PickAsset(IEnumerable<GitHubAsset> assets, string? pattern)
    {
        var list = assets.ToList();

        if (!string.IsNullOrWhiteSpace(pattern))
        {
            var rx = GlobToRegex(pattern);
            var match = list.FirstOrDefault(a => rx.IsMatch(a.Name ?? ""));
            if (match != null) return match;
            // Pattern was set but matched nothing — fall through to .zip
            // heuristic. If the modder intended strict matching they can
            // raise a stricter pattern (e.g. "^modname-v.*\\.zip$").
        }

        return list.FirstOrDefault(a =>
            (a.Name ?? "").EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
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
