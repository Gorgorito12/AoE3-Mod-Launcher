using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Discovers community mods by listing the contents of <c>/mods/</c> in a
/// catalog repository (default <c>papillo12/aoe3-mods-catalog</c>) and
/// reading the <c>mod.json</c> manifest in each subfolder.
///
/// Modelled directly on <see cref="TranslationRegistryService"/>: same
/// HTTP client style, same defensive parsing (skip-on-error, never throw
/// from <see cref="FetchAsync"/>), same diagnostic-log breadcrumbs. The
/// only structural difference is that mods live in folders (one
/// long-lived entry each) instead of releases (one snapshot each), which
/// matches how mods are versioned: their internal version comes from
/// their own update server, not from the catalog.
///
/// Returns null when:
///   - the repo string is empty (the user hasn't enabled the catalog yet)
///   - the GitHub API is unreachable / rate-limited
///   - the JSON is malformed
/// In all those cases, callers fall back to the built-in
/// <see cref="ModRegistry"/> entries — the launcher always has at least
/// the WoL profile available offline. Improvement Mod and any other
/// community mod live in the catalog only, so a cold start with no
/// network shows just WoL until the first successful fetch (then the
/// 24h cache keeps them available offline).
/// </summary>
public class ModCatalogService
{
    private static readonly HttpClient Http = CreateHttpClient();

    /// <summary>
    /// How long a cached catalog is considered fresh. After this window
    /// the launcher will refresh the cache in the background (a stale
    /// cache is still rendered immediately so the UI never waits on the
    /// network).
    ///
    /// 24h is the right grain for community-mod catalogs: new entries
    /// land via PR and don't need to be seen within minutes, while
    /// keeping the user out of GitHub's anonymous rate limit (60
    /// req/hour per IP) even if they launch the app many times a day.
    /// </summary>
    public static TimeSpan CacheTtl { get; } = TimeSpan.FromHours(24);

    /// <summary>
    /// On-disk path for the cache file. Lives under per-user LocalAppData
    /// so we can write without UAC, and so the cache survives launcher
    /// upgrades that may overwrite the install folder.
    /// </summary>
    public static string CacheFilePath { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AoE3ModLauncher",
        "catalog-cache.json");

    /// <summary>
    /// Fetches the manifest of every mod folder in <paramref name="repo"/>'s
    /// <c>/mods/</c> directory. Returns the raw <see cref="ModCatalogManifest"/>
    /// list paired with the resolved asset URLs (icon / banner) — the
    /// projection to <see cref="ModProfile"/> is done by the caller, in the
    /// same step that merges with the built-in registry.
    ///
    /// One GitHub API call (the <c>contents/mods</c> listing, ~5 KB) plus
    /// one CDN call per mod folder for the manifest itself (each ~1-3 KB,
    /// not rate-limited because <c>raw.githubusercontent.com</c> is a
    /// separate host from <c>api.github.com</c>).
    /// </summary>
    public async Task<List<ModCatalogEntry>?> FetchAsync(
        string repo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(repo))
        {
            DiagnosticLog.Write("ModCatalog: no repo configured — skipping fetch.");
            return null;
        }

        // List the /mods directory. The API returns one entry per child;
        // we only care about subdirectories (each a candidate mod).
        var listingUrl = $"https://api.github.com/repos/{repo}/contents/mods";
        DiagnosticLog.Write($"ModCatalog: listing {listingUrl}");

        List<GitHubContent>? listing;
        try
        {
            listing = await Http.GetFromJsonAsync<List<GitHubContent>>(listingUrl, ct);
        }
        catch (HttpRequestException ex)
        {
            // Most common reason in the wild: rate limit (60 req/h
            // unauthenticated) or transient 5xx. Either way, the user gets
            // the built-in mods and we'll try again next session.
            DiagnosticLog.Write($"ModCatalog: API unreachable: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"ModCatalog: listing failed: {ex.Message}");
            return null;
        }

        if (listing == null || listing.Count == 0)
        {
            DiagnosticLog.Write("ModCatalog: /mods is empty.");
            return new List<ModCatalogEntry>();
        }

        var entries = new List<ModCatalogEntry>();
        foreach (var item in listing)
        {
            ct.ThrowIfCancellationRequested();
            if (!string.Equals(item.Type, "dir", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrEmpty(item.Name))
                continue;

            // Each mod folder must contain mod.json. We pull it via the raw
            // CDN to avoid burning API quota — raw.githubusercontent.com
            // isn't rate-limited the same way.
            var manifestUrl =
                $"https://raw.githubusercontent.com/{repo}/main/mods/{item.Name}/mod.json";

            try
            {
                var json = await Http.GetStringAsync(manifestUrl, ct);
                var manifest = JsonSerializer.Deserialize<ModCatalogManifest>(json);
                if (manifest == null || string.IsNullOrEmpty(manifest.Id))
                {
                    DiagnosticLog.Write(
                        $"  '{item.Name}': mod.json missing 'id' — skipped");
                    continue;
                }

                // Folder name and manifest id must agree. If they don't,
                // someone messed up the PR — skip rather than guess which
                // is right.
                if (!string.Equals(manifest.Id, item.Name, StringComparison.Ordinal))
                {
                    DiagnosticLog.Write(
                        $"  '{item.Name}': mod.json id='{manifest.Id}' mismatches " +
                        "folder name — skipped");
                    continue;
                }

                // Resolve relative asset filenames (icon, banner) into
                // absolute raw-CDN URLs. The manifest only carries the
                // filename ("icon.png") and the CDN base depends on the
                // folder, so we splice them here, not in the manifest DTO.
                var entry = new ModCatalogEntry
                {
                    Manifest = manifest,
                    IconUrl = ResolveAssetUrl(repo, item.Name, manifest.Icon),
                    BannerUrl = ResolveAssetUrl(repo, item.Name, manifest.Banner),
                };
                entries.Add(entry);
                DiagnosticLog.Write(
                    $"  '{item.Name}': loaded ('{manifest.DisplayName}' v{manifest.ApprovedReleaseTag ?? "n/a"})");
            }
            catch (Exception ex)
            {
                // One bad manifest shouldn't kill the whole listing — log
                // and keep going. Same defensive strategy as
                // TranslationRegistryService.
                DiagnosticLog.Write(
                    $"  '{item.Name}': manifest fetch/parse failed: {ex.Message}");
            }
        }

        DiagnosticLog.Write(
            $"ModCatalog: {entries.Count} valid manifests from {listing.Count} entries.");

        // Persist successful fetches so the next launcher session can skip
        // the network entirely (or at least render from cache while a
        // background refresh runs). Failures here are non-fatal: the user
        // just doesn't get the speed-up.
        try { SaveCache(repo, entries); }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"ModCatalog: cache save failed (non-fatal): {ex.Message}");
        }

        return entries;
    }

    // -- Cache load/save ------------------------------------------------------

    /// <summary>
    /// Read the on-disk catalog cache synchronously. Returns null when the
    /// file doesn't exist, can't be parsed, or was built from a different
    /// repo than the one currently configured (so a user switching their
    /// <c>modsCatalogRepo</c> doesn't see stale entries from the old one).
    ///
    /// Caller is responsible for deciding what to do with the result;
    /// freshness is checked separately via <see cref="IsFresh"/> so the
    /// "stale cache" code path can still use the data for an immediate
    /// render while a background fetch runs.
    /// </summary>
    public ModCatalogCache? LoadFromCache(string repo)
    {
        if (string.IsNullOrWhiteSpace(repo)) return null;
        if (!File.Exists(CacheFilePath)) return null;

        try
        {
            var json = File.ReadAllText(CacheFilePath);
            var cache = JsonSerializer.Deserialize<ModCatalogCache>(json);
            if (cache == null) return null;
            if (!string.Equals(cache.Repo, repo, StringComparison.OrdinalIgnoreCase))
            {
                DiagnosticLog.Write(
                    $"ModCatalog: cache repo '{cache.Repo}' doesn't match active '{repo}' — discarding.");
                return null;
            }
            return cache;
        }
        catch (Exception ex)
        {
            // Corrupt cache → log + behave as if there's no cache. The next
            // successful fetch will overwrite with a clean file.
            DiagnosticLog.Write($"ModCatalog: cache load failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// True if the cache is still within the TTL window. A fresh cache
    /// should be used directly without touching the network; a stale one
    /// is OK to render but should be refreshed in the background.
    /// </summary>
    public bool IsFresh(ModCatalogCache cache) =>
        cache != null && (DateTime.UtcNow - cache.FetchedAt) < CacheTtl;

    /// <summary>
    /// Write the cache to disk. Called automatically by <see cref="FetchAsync"/>
    /// after a successful online fetch; also exposed so external callers
    /// (e.g. a "force refresh" button) can trigger persistence after their
    /// own fetch if needed.
    /// </summary>
    public void SaveCache(string repo, List<ModCatalogEntry> entries)
    {
        if (string.IsNullOrWhiteSpace(repo) || entries == null) return;

        var dir = Path.GetDirectoryName(CacheFilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var cache = new ModCatalogCache
        {
            FetchedAt = DateTime.UtcNow,
            Repo = repo,
            Manifests = entries,
        };

        // Write through a temp file + atomic move so a process crash mid-
        // write doesn't leave a half-serialised JSON that the next session
        // chokes on.
        var tmpPath = CacheFilePath + ".tmp";
        var json = JsonSerializer.Serialize(
            cache, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(tmpPath, json);
        if (File.Exists(CacheFilePath)) File.Delete(CacheFilePath);
        File.Move(tmpPath, CacheFilePath);

        DiagnosticLog.Write(
            $"ModCatalog: cache saved ({entries.Count} entries, repo='{repo}').");
    }

    // -- Internals ------------------------------------------------------------

    /// <summary>
    /// Builds the raw-CDN URL for an asset that the manifest references by
    /// bare filename. Returns null if either the asset is unset or the
    /// filename contains anything that smells like a path-traversal
    /// attempt — the schema regex blocks this server-side, but treat
    /// downloaded JSON as untrusted defensively.
    /// </summary>
    private static string? ResolveAssetUrl(string repo, string folder, string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        if (fileName.Contains('/') || fileName.Contains('\\') || fileName.Contains(".."))
            return null;
        return $"https://raw.githubusercontent.com/{repo}/main/mods/{folder}/{fileName}";
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            // Listing + manifest fetches are tiny. A short timeout avoids
            // making the user wait if GitHub is having a bad day.
            Timeout = TimeSpan.FromSeconds(15),
        };
        // GitHub requires a User-Agent on API requests; without one the
        // response is a 403 with a confusing message.
        client.DefaultRequestHeaders.Add("User-Agent", "WarsOfLibertyLauncher");
        // Avoid the new "tarball" media type GitHub now sends by default
        // for some endpoints; we want plain JSON.
        client.DefaultRequestHeaders.Add(
            "Accept", "application/vnd.github+json");
        return client;
    }

    // GitHub /repos/.../contents response: one item per file/dir.
    private class GitHubContent
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("path")]
        public string Path { get; set; } = "";
    }
}

/// <summary>
/// One mod entry as returned by <see cref="ModCatalogService.FetchAsync"/>:
/// the parsed manifest plus the resolved asset URLs. Kept as a separate
/// type from <see cref="ModCatalogManifest"/> because the URLs are
/// derived (not part of the JSON) and live next to the manifest in
/// memory rather than serialised back out.
/// </summary>
public class ModCatalogEntry
{
    public ModCatalogManifest Manifest { get; set; } = new();

    /// <summary>Absolute URL of the icon, or null if the manifest didn't ship one.</summary>
    public string? IconUrl { get; set; }

    /// <summary>Absolute URL of the banner, or null if the manifest didn't ship one.</summary>
    public string? BannerUrl { get; set; }
}
