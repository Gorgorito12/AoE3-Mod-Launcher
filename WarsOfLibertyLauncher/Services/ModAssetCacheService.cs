using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Result of a background asset revalidation.
/// </summary>
public enum RevalidateOutcome
{
    /// <summary>304 / transient error — the cached file is kept as-is.</summary>
    Unchanged,
    /// <summary>200 — the image was replaced under the same name; file rewritten.</summary>
    Replaced,
    /// <summary>404/410 — the asset was deleted at the source; cached file purged.</summary>
    Removed,
}

/// <summary>
/// Caches mod-icon / banner / hero / screenshot images on disk so the launcher
/// only downloads them once. Lives under
/// <c>%LocalAppData%\AoE3ModLauncher\mod-assets\</c> — per-user and outside
/// Program Files, so no UAC dance to write.
///
/// The cache is content-addressed by mod id + asset role (icon / banner / hero /
/// shot-N). The file name is <c>{modId}-{role}{ext}</c>, with a sibling
/// <c>{modId}-{role}.meta</c> JSON ({ url, etag }) recording which catalog URL
/// produced the file and its HTTP ETag.
///
/// Two-track resolution (<b>stale-while-revalidate</b>) so a stale catalog
/// image never lingers forever yet the UI still paints at disk speed and works
/// offline:
///   * <see cref="GetIconPathAsync"/> &amp; friends — the FAST path. No network
///     when the cached file already matches the requested URL (instant paint);
///     a download only happens on a cold cache or when the URL/extension
///     changed; an empty URL means "removed from the catalog" → purge + null.
///   * <see cref="RevalidateIconAsync"/> &amp; friends — the BACKGROUND path.
///     Conditional GET (If-None-Match): a 304 is free, a 200 means the modder
///     replaced the image under the same name → re-download and report
///     <c>changed</c> so the caller can repaint. Network errors keep the cached
///     copy (offline-safe — we NEVER delete a cached file because the net is down;
///     the only deletes are an explicit empty URL or a confirmed 200 replacement).
///
/// All operations are best-effort. A failed download returns null / false and
/// the caller falls back to the monogram / accent-gradient the UI already shows
/// for asset-less mods.
/// </summary>
public class ModAssetCacheService
{
    private static readonly HttpClient Http = CreateHttpClient();

    /// <summary>Highest screenshot index the gallery supports (schema maxItems).</summary>
    private const int MaxShots = 8;

    /// <summary>Max rotating dashboard heroes (must match the catalog's MAX_HEROES).</summary>
    private const int MaxHeroes = 6;

    /// <summary>Folder where cached assets live. Created lazily on first write.</summary>
    public static string CacheDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AoE3ModLauncher", "mod-assets");

    // -- Fast path: resolve from disk, download only when needed -------------

    /// <summary>
    /// Returns the local file path for the mod's icon, downloading it from
    /// <paramref name="remoteUrl"/> only if the cache is cold or the URL changed.
    /// An empty/null URL means the catalog no longer declares an icon → the
    /// cached file is purged and <c>null</c> is returned (UI falls back to the
    /// monogram). No network when the cached file already matches the URL.
    /// </summary>
    public Task<string?> GetIconPathAsync(
        string modId, string? remoteUrl, CancellationToken ct = default)
        => GetAssetAsync(modId, "icon", remoteUrl, ct);

    /// <summary>Same as <see cref="GetIconPathAsync"/> for the Workshop-card banner.</summary>
    public Task<string?> GetBannerPathAsync(
        string modId, string? remoteUrl, CancellationToken ct = default)
        => GetAssetAsync(modId, "banner", remoteUrl, ct);

    /// <summary>Same as <see cref="GetIconPathAsync"/> for the dashboard hero image.</summary>
    public Task<string?> GetHeroImagePathAsync(
        string modId, string? remoteUrl, CancellationToken ct = default)
        => GetAssetAsync(modId, "hero", remoteUrl, ct);

    /// <summary>
    /// Downloads (and caches) the rotating dashboard heroes for a mod and returns
    /// the local paths in order. Each file is named <c>{modId}-hero-{index}{ext}</c>
    /// (distinct from the single <c>{modId}-hero</c>). Mirrors
    /// <see cref="GetScreenshotPathsAsync"/>: best-effort, surplus
    /// <c>hero-{i}</c> beyond the new count is purged, a null/empty list purges
    /// the whole rotating set.
    /// </summary>
    public async Task<List<string>> GetHeroImagePathsAsync(
        string modId, IReadOnlyList<string>? remoteUrls, CancellationToken ct = default)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(modId))
            return result;
        if (remoteUrls is null || remoteUrls.Count == 0)
        {
            PurgeHeroesFrom(modId, 0);
            return result;
        }
        int count = Math.Min(remoteUrls.Count, MaxHeroes);
        for (int i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var path = await GetAssetAsync(modId, $"hero-{i}", remoteUrls[i], ct);
            if (path is not null)
                result.Add(path);
        }
        // Drop heroes the rotation no longer declares (shrunk set).
        PurgeHeroesFrom(modId, count);
        return result;
    }

    /// <summary>
    /// Downloads (and caches) the gallery screenshots/GIFs for a mod and returns
    /// the local paths in order. Each file is named <c>{modId}-shot-{index}{ext}</c>.
    /// Best effort: failures are skipped (the returned list may be shorter than
    /// the input). After resolving the current set, any surplus
    /// <c>shot-{i}</c> beyond the new count is purged so a gallery that shrank
    /// doesn't leave orphans. A null/empty url list purges the whole gallery.
    /// </summary>
    public async Task<List<string>> GetScreenshotPathsAsync(
        string modId, IReadOnlyList<string>? remoteUrls, CancellationToken ct = default)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(modId))
            return result;
        if (remoteUrls is null || remoteUrls.Count == 0)
        {
            PurgeShotsFrom(modId, 0);
            return result;
        }
        int count = Math.Min(remoteUrls.Count, MaxShots);
        for (int i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var path = await GetAssetAsync(modId, $"shot-{i}", remoteUrls[i], ct);
            if (path is not null)
                result.Add(path);
        }
        // Drop screenshots the gallery no longer declares (shrunk set).
        PurgeShotsFrom(modId, count);
        return result;
    }

    // -- Background path: conditional revalidation (replacement detection) ---

    /// <summary>
    /// Conditionally revalidates the mod's icon against the catalog.
    /// <see cref="RevalidateOutcome.Replaced"/> when the remote image was
    /// replaced under the same name (HTTP 200) — caller invalidates any bitmap
    /// memo and repaints. <see cref="RevalidateOutcome.Removed"/> when the asset
    /// was deleted at the source (404/410, e.g. the modder deleted the file but
    /// the manifest still references it) — the cached file is purged and the
    /// caller should null its local path. <see cref="RevalidateOutcome.Unchanged"/>
    /// on 304 / transient error / missing file (cached copy kept — offline-safe).
    /// A 304 carries no body, so the common case is cheap.
    /// </summary>
    public Task<RevalidateOutcome> RevalidateIconAsync(
        string modId, string? remoteUrl, CancellationToken ct = default)
        => RevalidateAsync(modId, "icon", remoteUrl, ct);

    /// <summary>Same as <see cref="RevalidateIconAsync"/> for the banner.</summary>
    public Task<RevalidateOutcome> RevalidateBannerAsync(
        string modId, string? remoteUrl, CancellationToken ct = default)
        => RevalidateAsync(modId, "banner", remoteUrl, ct);

    /// <summary>Same as <see cref="RevalidateIconAsync"/> for the hero image.</summary>
    public Task<RevalidateOutcome> RevalidateHeroAsync(
        string modId, string? remoteUrl, CancellationToken ct = default)
        => RevalidateAsync(modId, "hero", remoteUrl, ct);

    /// <summary>
    /// Conditionally revalidates every rotating hero. Returns <c>true</c> if ANY
    /// hero was replaced OR removed (so the caller repaints the rotation).
    /// Mirrors <see cref="RevalidateScreenshotsAsync"/>.
    /// </summary>
    public async Task<bool> RevalidateHeroesAsync(
        string modId, IReadOnlyList<string>? remoteUrls, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(modId) || remoteUrls is null)
            return false;
        bool any = false;
        int count = Math.Min(remoteUrls.Count, MaxHeroes);
        for (int i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (await RevalidateAsync(modId, $"hero-{i}", remoteUrls[i], ct) != RevalidateOutcome.Unchanged)
                any = true;
        }
        return any;
    }

    /// <summary>
    /// Conditionally revalidates every gallery screenshot. Returns <c>true</c>
    /// if ANY shot was replaced OR removed (so the caller refreshes the strip).
    /// </summary>
    public async Task<bool> RevalidateScreenshotsAsync(
        string modId, IReadOnlyList<string>? remoteUrls, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(modId) || remoteUrls is null)
            return false;
        bool any = false;
        int count = Math.Min(remoteUrls.Count, MaxShots);
        for (int i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (await RevalidateAsync(modId, $"shot-{i}", remoteUrls[i], ct) != RevalidateOutcome.Unchanged)
                any = true;
        }
        return any;
    }

    // -- Cleanup -------------------------------------------------------------

    /// <summary>
    /// Removes every cached file for a single mod (all roles + their meta).
    /// Called when the mod is removed from the catalog so the disk doesn't
    /// slowly fill with orphaned assets. Anchored to the known roles rather
    /// than a loose <c>{modId}-*</c> glob, so clearing <c>"wol"</c> can't sweep
    /// a prefix-colliding id's files (e.g. <c>wol-extra-icon.png</c>).
    /// </summary>
    public void Clear(string modId)
    {
        if (string.IsNullOrEmpty(modId)) return;
        PurgeRole(modId, "icon");
        PurgeRole(modId, "banner");
        PurgeRole(modId, "hero");
        PurgeHeroesFrom(modId, 0);
        PurgeShotsFrom(modId, 0);
    }

    // -- Internals -----------------------------------------------------------

    private async Task<string?> GetAssetAsync(
        string modId, string role, string? remoteUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(modId))
            return null;

        // Empty URL = the catalog no longer declares this asset. Purge the
        // cached file (and its meta) so the deletion is reflected, and report
        // "no asset" so the UI falls back to the monogram / gradient.
        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            PurgeRole(modId, role);
            return null;
        }

        var (localPath, metaPath) = PathsFor(modId, role, remoteUrl);

        // Fast hit: the file exists AND was produced by this exact URL → serve
        // it instantly with no network. Replacement detection is the
        // background RevalidateAsync's job, not this one.
        var meta = ReadMeta(metaPath);
        if (File.Exists(localPath) && meta is not null
            && string.Equals(meta.Value.url, remoteUrl, StringComparison.Ordinal))
        {
            return localPath;
        }

        // Cold cache, or the URL/extension changed since we last cached it
        // (this also covers the one-time migration of pre-meta caches: an
        // existing file with no .meta sidecar fails the fast-path guard above).
        //
        // Download FIRST, purge stale variants ONLY after a successful write.
        // This keeps the path offline-safe: a failed/aborted fetch never
        // deletes a usable cached image — we keep serving it and self-heal
        // (write the .meta) on the next online launch. WriteResponseAsync
        // overwrites a same-name file atomically (tmp→move), so the only
        // leftovers to purge are other-extension variants (png→jpg).
        try
        {
            Directory.CreateDirectory(CacheDir);
            using var req = new HttpRequestMessage(HttpMethod.Get, remoteUrl);
            using var response = await Http.SendAsync(
                req, HttpCompletionOption.ResponseHeadersRead, ct);
            // 404/410: the asset doesn't exist at the source — e.g. the modder
            // deleted the file while the manifest (or a hardcoded built-in URL)
            // still references it, so the URL never goes null. Definitive (a
            // successful connection saying "gone", not a transient/offline
            // error), so purge any stale cached copy and report "no asset". This
            // is what lets a deletion reflect even for a PRE-meta cached file
            // (the meta-based revalidation path can't see it).
            if (response.StatusCode == HttpStatusCode.NotFound
                || response.StatusCode == HttpStatusCode.Gone)
            {
                PurgeRole(modId, role);
                DiagnosticLog.Write(
                    $"ModAssetCache: GET {remoteUrl} -> {(int)response.StatusCode} — asset missing, purged");
                return null;
            }
            if (!response.IsSuccessStatusCode)
            {
                // Transient (5xx / throttle): keep whatever we have, offline-safe.
                DiagnosticLog.Write(
                    $"ModAssetCache: GET {remoteUrl} -> {(int)response.StatusCode}");
                return File.Exists(localPath) ? localPath : null;
            }
            if (!await WriteResponseAsync(response, role, localPath, metaPath, remoteUrl, ct))
                return File.Exists(localPath) ? localPath : null;
            // Success: drop only stale-extension leftovers for this role,
            // keeping the file + meta we just wrote.
            PurgeRole(modId, role, keep: new[] { localPath, metaPath });
            return localPath;
        }
        catch (OperationCanceledException)
        {
            // Caller cancelled (typical: window closed during refresh). Not a failure.
            return null;
        }
        catch (Exception ex)
        {
            // Offline / transient error: keep the cached image if we have one.
            DiagnosticLog.Write($"ModAssetCache: download {remoteUrl} failed: {ex.Message}");
            return File.Exists(localPath) ? localPath : null;
        }
    }

    private async Task<RevalidateOutcome> RevalidateAsync(
        string modId, string role, string? remoteUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(modId) || string.IsNullOrWhiteSpace(remoteUrl))
            return RevalidateOutcome.Unchanged;

        var (localPath, metaPath) = PathsFor(modId, role, remoteUrl);

        // Only revalidate something we already have cached for this exact URL.
        // A missing file or a changed URL is GetAssetAsync's domain, not ours.
        if (!File.Exists(localPath))
            return RevalidateOutcome.Unchanged;
        var meta = ReadMeta(metaPath);
        if (meta is null || !string.Equals(meta.Value.url, remoteUrl, StringComparison.Ordinal))
            return RevalidateOutcome.Unchanged;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, remoteUrl);
            if (!string.IsNullOrEmpty(meta.Value.etag))
                req.Headers.TryAddWithoutValidation("If-None-Match", meta.Value.etag);

            using var response = await Http.SendAsync(
                req, HttpCompletionOption.ResponseHeadersRead, ct);

            // Unchanged — the common case, body-less and cheap.
            if (response.StatusCode == HttpStatusCode.NotModified)
                return RevalidateOutcome.Unchanged;

            // 404/410 — the asset was DELETED at the source. This is the case
            // where the modder deleted e.g. hero.jpg from the repo but the
            // manifest (or a hardcoded built-in URL) still references it, so the
            // URL never goes null and the deletion can only be seen here. A 404
            // is definitive for a known path (rate-limits return 403/429, not
            // 404), so it's safe to treat as removal: purge the cached copy and
            // report it so the UI drops the stale image.
            if (response.StatusCode == HttpStatusCode.NotFound
                || response.StatusCode == HttpStatusCode.Gone)
            {
                PurgeRole(modId, role);
                DiagnosticLog.Write(
                    $"ModAssetCache: revalidate {remoteUrl} -> {(int)response.StatusCode} — asset deleted, purged");
                return RevalidateOutcome.Removed;
            }

            // Any other non-success (5xx, throttling): keep the cached copy
            // (offline-safe — never delete on a transient error).
            if (!response.IsSuccessStatusCode)
            {
                DiagnosticLog.Write(
                    $"ModAssetCache: revalidate {remoteUrl} -> {(int)response.StatusCode} (kept cache)");
                return RevalidateOutcome.Unchanged;
            }

            // 200 → the modder replaced the image under the same name. Rewrite
            // the file in place and report the change so the UI repaints.
            return await WriteResponseAsync(response, role, localPath, metaPath, remoteUrl, ct)
                ? RevalidateOutcome.Replaced : RevalidateOutcome.Unchanged;
        }
        catch (OperationCanceledException)
        {
            return RevalidateOutcome.Unchanged;
        }
        catch (Exception ex)
        {
            // Net hiccup: keep the cached image, never delete it.
            DiagnosticLog.Write($"ModAssetCache: revalidate {remoteUrl} failed: {ex.Message}");
            return RevalidateOutcome.Unchanged;
        }
    }

    /// <summary>
    /// Streams a successful HTTP response into the cache (atomic tmp→move),
    /// enforcing the per-role size cap, then records the ETag in the meta
    /// sidecar. Returns true if the file landed.
    /// </summary>
    private async Task<bool> WriteResponseAsync(
        HttpResponseMessage response, string role,
        string localPath, string metaPath, string remoteUrl, CancellationToken ct)
    {
        Directory.CreateDirectory(CacheDir);

        // Sanity guard: refuse anything wildly larger than the schema's
        // documented per-asset limits. The catalog CI rejects out-of-spec
        // images before merge, so this is just a belt-and-suspenders cap.
        long maxBytes = MaxBytesFor(role);
        if (response.Content.Headers.ContentLength is long len && len > maxBytes)
        {
            DiagnosticLog.Write(
                $"ModAssetCache: {remoteUrl} too large ({len} > {maxBytes}) — skipped");
            return false;
        }

        var tmpPath = localPath + ".tmp";
        await using (var src = await response.Content.ReadAsStreamAsync(ct))
        await using (var dst = File.Create(tmpPath))
        {
            long copied = 0;
            var buffer = new byte[81920];
            int read;
            while ((read = await src.ReadAsync(buffer, ct)) > 0)
            {
                copied += read;
                if (copied > maxBytes)
                {
                    DiagnosticLog.Write(
                        $"ModAssetCache: {remoteUrl} streamed past {maxBytes} bytes — aborting");
                    try { File.Delete(tmpPath); } catch { /* best-effort */ }
                    return false;
                }
                await dst.WriteAsync(buffer.AsMemory(0, read), ct);
            }
        }

        // Move is atomic on the same volume; LocalAppData and the temp file are
        // always on the same drive so this is safe.
        if (File.Exists(localPath)) File.Delete(localPath);
        File.Move(tmpPath, localPath);
        WriteMeta(metaPath, remoteUrl, response.Headers.ETag?.ToString());
        return true;
    }

    /// <summary>Local file + meta paths for a role, extension derived from the URL.</summary>
    private static (string localPath, string metaPath) PathsFor(
        string modId, string role, string remoteUrl)
    {
        var ext = Path.GetExtension(remoteUrl);
        if (string.IsNullOrEmpty(ext)) ext = ".png";
        return (
            Path.Combine(CacheDir, $"{modId}-{role}{ext}"),
            Path.Combine(CacheDir, $"{modId}-{role}.meta"));
    }

    private static long MaxBytesFor(string role) => role switch
    {
        // Hero(s) and screenshots allow up to 4K source images (≤5 MB; modders
        // are told to use JPEG for 4K since a 4K JPEG fits well under this and a
        // 4K PNG can be 10 MB+). These caps MUST be ≥ the catalog CI's
        // (validate_images.py) so an image the CI approved is never silently
        // dropped here at runtime.
        "hero" => 5L * 1024 * 1024,   // 5 MB — single dashboard hero
        _ when role.StartsWith("hero-", StringComparison.Ordinal)
               => 5L * 1024 * 1024,   // 5 MB — rotating dashboard heroes
        _ when role.StartsWith("shot-", StringComparison.Ordinal)
               => 5L * 1024 * 1024,   // 5 MB — gallery screenshots / GIFs
        "banner" => 2L * 1024 * 1024, // 2 MB — Workshop card banner (up to 4:1 4800px)
        _        => 1L * 1024 * 1024, // 1 MB — icon (up to 1024×1024)
    };

    /// <summary>
    /// Deletes files for one role (any extension + the .meta sidecar), except
    /// any full paths listed in <paramref name="keep"/>. Anchored to the exact
    /// <c>{modId}-{role}.</c> prefix so role "icon" never matches "icon2" and id
    /// "wol" never matches "wol-extra". With no <paramref name="keep"/> it wipes
    /// the role entirely (deletion / Clear); with it, callers keep the file +
    /// meta they just wrote and drop only stale-extension leftovers.
    /// </summary>
    private static void PurgeRole(string modId, string role, IReadOnlyCollection<string>? keep = null)
    {
        try
        {
            if (!Directory.Exists(CacheDir)) return;
            var prefix = $"{modId}-{role}.";
            foreach (var file in Directory.EnumerateFiles(CacheDir, $"{modId}-{role}.*"))
            {
                // Windows glob can over-match; confirm the exact role prefix.
                if (!Path.GetFileName(file).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (keep != null && keep.Any(
                        k => string.Equals(k, file, StringComparison.OrdinalIgnoreCase)))
                    continue;
                try { File.Delete(file); }
                catch { /* best-effort */ }
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"ModAssetCache: purge {modId}-{role} failed: {ex.Message}");
        }
    }

    private static void PurgeShotsFrom(string modId, int startIndex)
    {
        for (int i = startIndex; i < MaxShots; i++)
            PurgeRole(modId, $"shot-{i}");
    }

    private static void PurgeHeroesFrom(string modId, int startIndex)
    {
        for (int i = startIndex; i < MaxHeroes; i++)
            PurgeRole(modId, $"hero-{i}");
    }

    private static (string url, string? etag)? ReadMeta(string metaPath)
    {
        try
        {
            if (!File.Exists(metaPath)) return null;
            using var fs = File.OpenRead(metaPath);
            using var doc = JsonDocument.Parse(fs);
            var root = doc.RootElement;
            if (!root.TryGetProperty("url", out var u)) return null;
            var url = u.GetString();
            if (string.IsNullOrEmpty(url)) return null;
            string? etag = root.TryGetProperty("etag", out var e) ? e.GetString() : null;
            return (url, etag);
        }
        catch
        {
            // Corrupt / unreadable meta → treat as "needs fresh download".
            return null;
        }
    }

    private static void WriteMeta(string metaPath, string url, string? etag)
    {
        try
        {
            File.WriteAllText(metaPath, JsonSerializer.Serialize(new { url, etag }));
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"ModAssetCache: write meta failed: {ex.Message}");
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            // Mod assets are small, but a 4K hero/screenshot can reach ~5 MB,
            // so allow a bit more headroom on slow links without making the user
            // wait minutes if GitHub is unhealthy.
            Timeout = TimeSpan.FromSeconds(30),
        };
        client.DefaultRequestHeaders.Add("User-Agent", "WarsOfLibertyLauncher");
        return client;
    }
}
