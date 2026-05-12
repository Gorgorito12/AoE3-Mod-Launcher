using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Fetches the <c>news.json</c> feed from the catalog repo (or any URL the
/// user configured) and renders it in the Noticias tab. Mirrors
/// ModCatalogService's defensive pattern: returns null instead of throwing,
/// caches to <c>%LocalAppData%</c> with a short TTL, and falls back to the
/// cached copy when the network is unreachable.
/// </summary>
public class NewsService
{
    private static readonly HttpClient Http = CreateHttpClient();

    /// <summary>
    /// Cache window for the news feed. Shorter than the catalog cache
    /// because news is time-sensitive (e.g. tournament announcements,
    /// patch notes). 1 hour balances freshness against not hammering
    /// raw.githubusercontent.com on every launcher start.
    /// </summary>
    public static TimeSpan CacheTtl { get; } = TimeSpan.FromHours(1);

    public static string CacheFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AoE3ModLauncher",
        "news-cache.json");

    /// <summary>
    /// Fetches the news feed. Returns the fresh feed on success, the cached
    /// feed if fetch fails but the cache is readable, or null when neither
    /// is available. Never throws.
    /// </summary>
    public async Task<NewsFeed?> FetchAsync(string url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return ReadCache();

        try
        {
            // Honour the on-disk cache if it's within the TTL — the news
            // feed doesn't need to be sub-second fresh, and the cache hit
            // keeps us under GitHub's anonymous rate limit on rapid restarts.
            var cached = ReadCache();
            if (cached != null && IsCacheFresh()) return cached;

            using var resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return cached;

            var feed = await resp.Content.ReadFromJsonAsync<NewsFeed>(cancellationToken: ct).ConfigureAwait(false);
            if (feed == null) return cached;

            // Refuse a future incompatible schema rather than render garbage.
            if (feed.SchemaVersion > 1)
            {
                DiagnosticLog.Write($"NewsService: feed schemaVersion {feed.SchemaVersion} not supported.");
                return cached;
            }

            WriteCache(resp.Content);
            return feed;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"NewsService fetch failed: {ex.Message}");
            return ReadCache();
        }
    }

    private static NewsFeed? ReadCache()
    {
        try
        {
            if (!File.Exists(CacheFilePath)) return null;
            using var fs = File.OpenRead(CacheFilePath);
            return JsonSerializer.Deserialize<NewsFeed>(fs);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsCacheFresh()
    {
        try
        {
            return File.Exists(CacheFilePath)
                && DateTime.UtcNow - File.GetLastWriteTimeUtc(CacheFilePath) < CacheTtl;
        }
        catch
        {
            return false;
        }
    }

    private static void WriteCache(HttpContent content)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CacheFilePath)!);
            using var src = content.ReadAsStream();
            using var dst = File.Create(CacheFilePath);
            src.CopyTo(dst);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"NewsService cache write failed: {ex.Message}");
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.Add("User-Agent", "Aoe3ModLauncher");
        return c;
    }
}
