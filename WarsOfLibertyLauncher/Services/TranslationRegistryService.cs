using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Discovers community translations by listing the releases of a GitHub
/// repository and reading the <c>translation.json</c> manifest attached to
/// each. Defaults to <c>papillo12/translations</c>; users can point the
/// launcher at a fork or private mirror via <c>launcher-config.json</c>.
/// </summary>
public class TranslationRegistryService
{
    private static readonly HttpClient Http = CreateHttpClient();

    /// <summary>
    /// Discovers translations by listing the releases of a GitHub repo and
    /// reading the <c>translation.json</c> asset attached to each. Each
    /// translation lives entirely inside its own release — no separate
    /// index file to maintain.
    ///
    /// Releases without a <c>translation.json</c> asset are silently
    /// skipped — the GitHub repo can host other artifacts alongside
    /// translation packs without breaking the launcher.
    ///
    /// Note about rate limits: the GitHub Releases API allows 60 requests
    /// per hour per IP without authentication. Each launcher session uses
    /// 1 call (the releases listing) plus 1 CDN download per release for
    /// the <c>translation.json</c> file. The CDN downloads are NOT
    /// rate-limited (different host than api.github.com).
    /// </summary>
    public async Task<TranslationIndex?> FetchFromReleasesAsync(
        string repo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(repo)) return null;

        var apiUrl = $"https://api.github.com/repos/{repo}/releases?per_page=100";
        DiagnosticLog.Write($"Fetching translation releases from: {apiUrl}");

        try
        {
            var releases = await Http.GetFromJsonAsync<List<GitHubRelease>>(apiUrl, ct);
            if (releases == null || releases.Count == 0)
            {
                DiagnosticLog.Write("No releases returned by GitHub API.");
                return new TranslationIndex { Translations = new List<TranslationIndexEntry>() };
            }

            var entries = new List<TranslationIndexEntry>();
            foreach (var release in releases)
            {
                ct.ThrowIfCancellationRequested();
                if (release.Assets == null) continue;

                // A valid translation release must ship BOTH translation.json
                // (manifest, ~600 bytes) AND a zip (the actual files).
                GitHubAsset? manifestAsset = null;
                GitHubAsset? zipAsset = null;
                foreach (var asset in release.Assets)
                {
                    if (string.Equals(asset.Name, "translation.json", StringComparison.OrdinalIgnoreCase))
                        manifestAsset = asset;
                    else if (asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        zipAsset = asset;
                }

                if (manifestAsset == null || zipAsset == null)
                {
                    DiagnosticLog.Write(
                        $"  release '{release.TagName}': missing translation.json or .zip — skipped");
                    continue;
                }

                // Download just the manifest (CDN, no rate limit), parse it,
                // and project it into a TranslationIndexEntry pointing at the
                // zip URL from the same release.
                try
                {
                    var manifestJson = await Http.GetStringAsync(
                        manifestAsset.BrowserDownloadUrl, ct);
                    var manifest = JsonSerializer.Deserialize<TranslationManifest>(manifestJson);
                    if (manifest == null || string.IsNullOrEmpty(manifest.Id))
                    {
                        DiagnosticLog.Write($"  release '{release.TagName}': bad manifest — skipped");
                        continue;
                    }

                    entries.Add(new TranslationIndexEntry
                    {
                        Id = manifest.Id,
                        Name = manifest.Name,
                        Language = string.IsNullOrEmpty(manifest.Language) ? manifest.Id : manifest.Language,
                        Author = manifest.Author,
                        Version = manifest.Version,
                        CompatibleWith = manifest.CompatibleWith,
                        DownloadUrl = zipAsset.BrowserDownloadUrl,
                        Size = zipAsset.Size,
                        Description = manifest.Description,
                        TargetMod = manifest.TargetMod,
                        ReleaseTag = release.TagName ?? "",
                    });
                    DiagnosticLog.Write(
                        $"  release '{release.TagName}': loaded '{manifest.Id}' v{manifest.Version}");
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Write(
                        $"  release '{release.TagName}': could not read manifest — {ex.Message}");
                }
            }

            DiagnosticLog.Write(
                $"Translation releases scanned: {entries.Count} valid entries from {releases.Count} releases.");
            return new TranslationIndex { Translations = entries };
        }
        catch (HttpRequestException ex)
        {
            DiagnosticLog.Write($"GitHub releases API unavailable: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"GitHub releases fetch failed: {ex.Message}");
            return null;
        }
    }

    // Minimal DTOs for the GitHub Releases API response. Only the fields we
    // actually need — the API returns ~30 more we don't care about.
    private class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }

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

    /// <summary>
    /// Downloads the .zip file for a single translation pack to the given
    /// destination path on disk. Throws on failure.
    /// </summary>
    public async Task DownloadPackAsync(
        string downloadUrl,
        string destinationPath,
        CancellationToken ct = default)
    {
        DiagnosticLog.Write($"Downloading translation pack: {downloadUrl}");
        using var response = await Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var src = await response.Content.ReadAsStreamAsync(ct);
        await using var dst = System.IO.File.Create(destinationPath);
        await src.CopyToAsync(dst, ct);
        DiagnosticLog.Write($"Translation pack downloaded to: {destinationPath}");
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(2),
        };
        client.DefaultRequestHeaders.Add("User-Agent", "WarsOfLibertyLauncher");
        return client;
    }
}
