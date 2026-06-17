using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Reads the central NOTIFICATION FEED — a tiny JSON manifest published by the
/// self-hosted notifier service (a second, separate Oracle VM that polls GitHub
/// once for everyone) describing each mod's latest available version and the set
/// of published translation keys. One cheap REST call here replaces the per-mod
/// <c>UpdateService.CheckAsync</c> + translation-release listing that
/// <c>MainWindow.SweepInstalledModsForNotificationsAsync</c> otherwise fires
/// against GitHub for EACH installed mod — which quickly burns the unauthenticated
/// 60 req/h-per-IP budget behind shared NAT / Radmin.
///
/// The launcher still owns the DIFF and the dedup: it compares the feed's
/// <c>latestVersion</c> against the locally-cached installed version
/// (<c>ModState.LastKnownVersion</c>) and the feed's translation keys against
/// <c>ModState.NotifiedTranslationKeys</c>. The feed only changes the data
/// SOURCE, never the notification logic.
///
/// Modelled on <see cref="ModCatalogService"/>: shared static <see cref="HttpClient"/>,
/// defensive parsing (never throws from <see cref="FetchAsync"/>), an on-disk cache
/// so a <c>304 Not Modified</c> can still be served without the network, and
/// <see cref="DiagnosticLog"/> breadcrumbs. A failed fetch returns
/// <see cref="NotificationFeedFetch.Failed"/> so the caller falls back to the
/// direct-GitHub checks — the notifier is never a single point of failure.
/// </summary>
public sealed class NotificationFeedService
{
    private static readonly HttpClient Http = CreateHttpClient();

    /// <summary>
    /// On-disk cache of the last successfully-fetched feed, so a <c>304</c> (the
    /// feed is unchanged) can be applied against the CURRENT local install state
    /// without re-downloading. Lives under the per-user data dir, beside the
    /// catalog/asset caches. A cache miss on a 304 is treated as a soft failure
    /// (the caller falls back to GitHub) rather than silently doing nothing.
    /// </summary>
    public static string CacheFilePath { get; } =
        Path.Combine(AppPaths.DataDir, "notification-feed-cache.json");

    /// <summary>
    /// Fetches the feed at <paramref name="url"/>, sending <paramref name="etag"/>
    /// as <c>If-None-Match</c>. Never throws.
    /// <list type="bullet">
    ///   <item><b>200</b> → fresh <see cref="NotificationFeed"/> + its new ETag
    ///     (also written to the on-disk cache).</item>
    ///   <item><b>304</b> → the cached feed + the same ETag (nothing changed).</item>
    ///   <item><b>failure</b> (network / bad JSON / non-success status / 304 with
    ///     no cache) → <see cref="NotificationFeedFetch.Failed"/> = true.</item>
    /// </list>
    /// </summary>
    public async Task<NotificationFeedFetch> FetchAsync(
        string? url, string? etag, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            return new NotificationFeedFetch { Failed = true };

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(etag))
                req.Headers.TryAddWithoutValidation("If-None-Match", etag);

            using var resp = await Http.SendAsync(
                req, HttpCompletionOption.ResponseContentRead, ct);

            if (resp.StatusCode == HttpStatusCode.NotModified)
            {
                var cached = LoadCache();
                if (cached == null)
                {
                    DiagnosticLog.Write(
                        "NotificationFeed: 304 but no cache on disk — falling back to GitHub.");
                    return new NotificationFeedFetch { Failed = true };
                }
                DiagnosticLog.Write("NotificationFeed: 304 Not Modified — using cached feed.");
                return new NotificationFeedFetch { Feed = cached, ETag = etag };
            }

            if (!resp.IsSuccessStatusCode)
            {
                DiagnosticLog.Write(
                    $"NotificationFeed: HTTP {(int)resp.StatusCode} — falling back to GitHub.");
                return new NotificationFeedFetch { Failed = true };
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            var feed = ParseFeed(json);
            if (feed == null)
            {
                DiagnosticLog.Write("NotificationFeed: empty/invalid JSON — falling back to GitHub.");
                return new NotificationFeedFetch { Failed = true };
            }

            // ETag header value (e.g. W/"abc" or "abc"); echoed verbatim next time.
            var newEtag = resp.Headers.ETag?.ToString();
            try { SaveCache(json); }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"NotificationFeed: cache save failed (non-fatal): {ex.Message}");
            }

            DiagnosticLog.Write(
                $"NotificationFeed: loaded {feed.Mods.Count} mod entries (etag={newEtag ?? "none"}).");
            return new NotificationFeedFetch { Feed = feed, ETag = newEtag };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"NotificationFeed: fetch failed: {ex.Message}");
            return new NotificationFeedFetch { Failed = true };
        }
    }

    /// <summary>
    /// Parses a feed JSON string into a <see cref="NotificationFeed"/>, or null on
    /// invalid/empty JSON. Pure (no I/O) so it can be unit-tested directly.
    /// </summary>
    public static NotificationFeed? ParseFeed(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var feed = JsonSerializer.Deserialize<NotificationFeed>(json);
            if (feed == null) return null;
            feed.Mods ??= new(StringComparer.OrdinalIgnoreCase);
            // Re-key case-insensitively so a manifest using a different id casing
            // still matches a profile id (mod ids are matched OrdinalIgnoreCase
            // everywhere else in the launcher).
            if (feed.Mods.Comparer != StringComparer.OrdinalIgnoreCase)
            {
                var ci = new Dictionary<string, NotificationFeedMod>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in feed.Mods)
                    if (!string.IsNullOrEmpty(kvp.Key) && kvp.Value != null)
                        ci[kvp.Key] = kvp.Value;
                feed.Mods = ci;
            }
            return feed;
        }
        catch
        {
            return null;
        }
    }

    private static NotificationFeed? LoadCache()
    {
        try
        {
            if (!File.Exists(CacheFilePath)) return null;
            return ParseFeed(File.ReadAllText(CacheFilePath));
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"NotificationFeed: cache load failed: {ex.Message}");
            return null;
        }
    }

    private static void SaveCache(string json)
    {
        var dir = Path.GetDirectoryName(CacheFilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // Temp-file + atomic move so a crash mid-write can't leave half a file
        // the next session chokes on (same idiom as ModCatalogService.SaveCache).
        var tmp = CacheFilePath + ".tmp";
        File.WriteAllText(tmp, json);
        if (File.Exists(CacheFilePath)) File.Delete(CacheFilePath);
        File.Move(tmp, CacheFilePath);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            // Tiny payload; a short timeout keeps the startup WhenAll snappy when
            // the notifier VM is slow/down (we fall back to GitHub anyway).
            Timeout = TimeSpan.FromSeconds(15),
        };
        client.DefaultRequestHeaders.Add("User-Agent", "WarsOfLibertyLauncher");
        return client;
    }
}

/// <summary>Result of <see cref="NotificationFeedService.FetchAsync"/>.</summary>
public sealed class NotificationFeedFetch
{
    /// <summary>
    /// The feed when the server answered (200 fresh, or 304 served from cache);
    /// null on failure. When non-null the caller applies it to every installed
    /// mod; when null the caller falls back to the per-mod GitHub checks.
    /// </summary>
    public NotificationFeed? Feed { get; init; }

    /// <summary>
    /// The ETag to persist for the next <c>If-None-Match</c>: the new one on 200,
    /// the echoed one on 304, null on failure. The caller writes it to config
    /// only when it actually changed.
    /// </summary>
    public string? ETag { get; init; }

    /// <summary>
    /// True when the fetch could not produce a usable feed (network error, bad
    /// JSON, non-success status, or a 304 with no cached copy). The caller falls
    /// back to the direct-GitHub checks.
    /// </summary>
    public bool Failed { get; init; }
}

/// <summary>
/// The notification-feed manifest. Deliberately tiny: per mod, the LATEST
/// available version and the set of published translation keys. The launcher
/// diffs these against its local state — the feed carries no per-user data.
/// </summary>
public sealed class NotificationFeed
{
    /// <summary>Schema version, for forward-compatible evolution.</summary>
    [JsonPropertyName("version")]
    public int Version { get; set; }

    /// <summary>ISO-8601 timestamp the manifest was generated (informational).</summary>
    [JsonPropertyName("generatedAt")]
    public string GeneratedAt { get; set; } = "";

    /// <summary>Per-mod entries keyed by mod id (matched case-insensitively).</summary>
    [JsonPropertyName("mods")]
    public Dictionary<string, NotificationFeedMod> Mods { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>One mod's entry in the <see cref="NotificationFeed"/>.</summary>
public sealed class NotificationFeedMod
{
    /// <summary>
    /// The LATEST available version (NOT the installed one — the server can't know
    /// what each user has). The launcher compares this against its cached
    /// <c>ModState.LastKnownVersion</c> to decide whether to bell "update available".
    /// </summary>
    [JsonPropertyName("latestVersion")]
    public string LatestVersion { get; set; } = "";

    /// <summary>
    /// The published translation dedup keys for this mod, each the SAME value the
    /// launcher computes locally (the GitHub release tag, falling back to
    /// <c>id@version</c>) — see <c>NotifyNewTranslations</c>'s <c>KeyOf</c>. Using
    /// the identical key format keeps dedup consistent whether the data came from
    /// the feed or from the direct-GitHub fallback.
    /// </summary>
    [JsonPropertyName("translations")]
    public List<string> Translations { get; set; } = new();
}
