using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Caches mod-icon and mod-banner images on disk so the launcher only
/// downloads them once. Lives under
/// <c>%LocalAppData%\AoE3ModLauncher\mod-assets\</c> — that's per-user and
/// outside Program Files, so no UAC dance to write.
///
/// The cache is content-addressed by mod id + asset role (icon / banner).
/// We deliberately do NOT version by URL hash: when the catalog updates an
/// icon, the new file overwrites the old one, and the launcher picks it
/// up on the next refresh. If we hashed URLs, we'd accumulate orphan files
/// forever.
///
/// All operations are best-effort. If a download fails (no net, GitHub
/// blocked, etc.) we return null and the caller falls back to the
/// monogram / accent-color gradient that the UI already supports for
/// asset-less mods.
/// </summary>
public class ModAssetCacheService
{
    private static readonly HttpClient Http = CreateHttpClient();

    /// <summary>Folder where cached assets live. Created lazily on first write.</summary>
    public static string CacheDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AoE3ModLauncher", "mod-assets");

    /// <summary>
    /// Returns a local file path for the icon of the given mod, downloading
    /// it from <paramref name="remoteUrl"/> if it's not already cached. The
    /// extension is preserved from the URL so WPF's image decoder picks the
    /// right codec.
    ///
    /// Returns <c>null</c> if the URL is empty, or if the download fails —
    /// callers should treat null as "no icon, fall back to monogram".
    /// </summary>
    public Task<string?> GetIconPathAsync(
        string modId, string? remoteUrl, CancellationToken ct = default)
        => GetAssetAsync(modId, "icon", remoteUrl, ct);

    /// <summary>
    /// Same as <see cref="GetIconPathAsync"/> but for the banner image. A
    /// missing banner is more common than a missing icon (banners are
    /// optional in the schema), so callers should expect <c>null</c>
    /// frequently and have a synthetic-gradient fallback ready.
    /// </summary>
    public Task<string?> GetBannerPathAsync(
        string modId, string? remoteUrl, CancellationToken ct = default)
        => GetAssetAsync(modId, "banner", remoteUrl, ct);

    /// <summary>
    /// Same as <see cref="GetBannerPathAsync"/> but for the dashboard hero
    /// image. The hero is the large 1920×1080 background painted behind
    /// the title + PLAY button on the dashboard — distinct from the
    /// banner (which is a 1200×300 horizontal thumbnail used in the
    /// Workshop mod card). Hero is optional; callers should expect
    /// <c>null</c> and fall through to the banner / gradient.
    /// </summary>
    public Task<string?> GetHeroImagePathAsync(
        string modId, string? remoteUrl, CancellationToken ct = default)
        => GetAssetAsync(modId, "hero", remoteUrl, ct);

    /// <summary>
    /// Removes every cached file for a single mod. Called when the mod is
    /// removed from the catalog so the disk doesn't slowly fill with
    /// orphaned assets.
    /// </summary>
    public void Clear(string modId)
    {
        if (string.IsNullOrEmpty(modId)) return;
        try
        {
            if (!Directory.Exists(CacheDir)) return;
            // Match any extension — icon.png, banner.jpg, etc.
            foreach (var file in Directory.EnumerateFiles(CacheDir, modId + "-*"))
            {
                try { File.Delete(file); }
                catch { /* best-effort */ }
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"ModAssetCache: clear for '{modId}' failed: {ex.Message}");
        }
    }

    // -- Internals -----------------------------------------------------------

    private async Task<string?> GetAssetAsync(
        string modId, string role, string? remoteUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(modId) || string.IsNullOrWhiteSpace(remoteUrl))
            return null;

        // Local file name pattern: <modId>-<role><ext>. The role distinguishes
        // icon vs banner inside the same mod's folder; the extension comes
        // from the URL so WPF picks the right decoder.
        var ext = Path.GetExtension(remoteUrl);
        if (string.IsNullOrEmpty(ext)) ext = ".png";
        var localPath = Path.Combine(CacheDir, $"{modId}-{role}{ext}");

        if (File.Exists(localPath))
            return localPath;

        try
        {
            Directory.CreateDirectory(CacheDir);

            using var response = await Http.GetAsync(
                remoteUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                DiagnosticLog.Write(
                    $"ModAssetCache: GET {remoteUrl} -> {(int)response.StatusCode}");
                return null;
            }

            // Sanity guard: refuse to cache anything wildly larger than the
            // schema's documented per-asset limits (icon ≤100 KB, banner
            // ≤500 KB, hero ≤2 MB). Pick the ceiling per role so we don't
            // accidentally reject a valid hero image while keeping the
            // tight icon/banner cap. Hostile catalog can't fill the disk
            // either way — the schema's CI validation rejects images
            // outside spec before the modder can merge.
            long maxBytes = role switch
            {
                "hero" => 2L * 1024 * 1024,   // 2 MB — hero images
                _      => 1L * 1024 * 1024,   // 1 MB — icon + banner with headroom
            };
            if (response.Content.Headers.ContentLength is long len && len > maxBytes)
            {
                DiagnosticLog.Write(
                    $"ModAssetCache: {remoteUrl} too large ({len} > {maxBytes}) — skipped");
                return null;
            }

            // Stream-copy to a temp file first, then atomically move into
            // place. Avoids leaving a half-written file in the cache that
            // a parallel reader would trip on.
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
                        return null;
                    }
                    await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                }
            }

            // Move is atomic on the same volume; LocalAppData and the temp
            // file are always on the same drive so this is safe.
            if (File.Exists(localPath)) File.Delete(localPath);
            File.Move(tmpPath, localPath);
            return localPath;
        }
        catch (OperationCanceledException)
        {
            // Caller cancelled (typical: user closed the window during refresh).
            // No log noise — cancellation isn't a failure.
            return null;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write(
                $"ModAssetCache: download {remoteUrl} failed: {ex.Message}");
            return null;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            // Mod assets are tiny (a few hundred KB at most). Don't make
            // the user wait minutes if GitHub is unhealthy.
            Timeout = TimeSpan.FromSeconds(20),
        };
        client.DefaultRequestHeaders.Add("User-Agent", "WarsOfLibertyLauncher");
        return client;
    }
}
