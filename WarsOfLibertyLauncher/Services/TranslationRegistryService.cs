using System;
using System.Collections.Generic;
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

    private static readonly System.Text.RegularExpressions.Regex TranslationManifestPathRegex =
        new(@"^translations/([^/]+)(?:/([^/]+))?/translation\.json$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Discovers translations published as FILES on a repo's <c>main</c> branch.
    /// Each pack lives in <c>translations/&lt;id&gt;/translation.json</c> (single
    /// version) OR keeps a HISTORY under
    /// <c>translations/&lt;id&gt;/&lt;version&gt;/translation.json</c> (subfolder
    /// per version). The whole tree is fetched in ONE call (the Git Trees API,
    /// recursive) and each manifest is read via the raw CDN (not rate-limited).
    /// Versions are grouped per id into a single <see cref="TranslationIndexEntry"/>
    /// whose top-level fields describe the NEWEST version (so the menu / dedup /
    /// notification stay unchanged) and whose <see cref="TranslationIndexEntry.Versions"/>
    /// lists the history (newest first, capped). The dedup key is
    /// <c>id@contentHash</c> of the newest. A repo with no <c>translations/</c>
    /// returns an empty index (not an error).
    /// </summary>
    public async Task<TranslationIndex?> FetchFromRepoFolderAsync(
        string repo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(repo)) return null;

        var apiUrl = $"https://api.github.com/repos/{repo}/git/trees/main?recursive=1";
        DiagnosticLog.Write($"Fetching translation tree from: {apiUrl}");

        GitHubTree? tree;
        try
        {
            tree = await Http.GetFromJsonAsync<GitHubTree>(apiUrl, ct);
        }
        catch (HttpRequestException ex)
        {
            // 404 = the repo/branch has no tree we can read → empty, not a failure.
            DiagnosticLog.Write($"Translation tree unavailable ({repo}): {ex.Message}");
            return new TranslationIndex { Translations = new List<TranslationIndexEntry>() };
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Translation tree fetch failed ({repo}): {ex.Message}");
            return null;
        }

        if (tree?.Tree == null)
            return new TranslationIndex { Translations = new List<TranslationIndexEntry>() };
        if (tree.Truncated)
            DiagnosticLog.Write($"  WARNING: tree for {repo} was truncated — some packs may be missed.");

        // Group every translation.json path by language id.
        var pathsByLang = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in tree.Tree)
        {
            if (!string.Equals(node.Type, "blob", StringComparison.Ordinal)) continue;
            var m = TranslationManifestPathRegex.Match(node.Path ?? "");
            if (!m.Success) continue;
            var lang = m.Groups[1].Value;
            if (!pathsByLang.TryGetValue(lang, out var list)) { list = new(); pathsByLang[lang] = list; }
            list.Add(node.Path!);
        }

        var entries = new List<TranslationIndexEntry>();
        foreach (var (lang, paths) in pathsByLang)
        {
            ct.ThrowIfCancellationRequested();

            // Read each version's manifest (raw CDN), pairing it with the
            // TranslationVersion we build from it.
            var pairs = new List<(TranslationVersion ver, TranslationManifest manifest)>();
            foreach (var path in paths)
            {
                try
                {
                    var manifestJson = await Http.GetStringAsync(
                        $"https://raw.githubusercontent.com/{repo}/main/{path}", ct);
                    var manifest = JsonSerializer.Deserialize<TranslationManifest>(manifestJson);
                    if (manifest == null || string.IsNullOrEmpty(manifest.Id))
                    {
                        DiagnosticLog.Write($"  '{path}': bad manifest — skipped");
                        continue;
                    }
                    // Folder holding this manifest (translations/<lang>[/<version>]).
                    var dir = path.Substring(0, path.LastIndexOf('/'));
                    var zipName = !string.IsNullOrWhiteSpace(manifest.Zip) ? manifest.Zip! : $"{manifest.Id}.zip";
                    var contentHash = !string.IsNullOrWhiteSpace(manifest.ContentHash)
                        ? manifest.ContentHash!
                        : TranslationCompat.ComputeContentHash(manifest.Files);
                    pairs.Add((new TranslationVersion
                    {
                        Version = manifest.Version,
                        DownloadUrl = $"https://raw.githubusercontent.com/{repo}/main/{dir}/{zipName}",
                        ContentHash = contentHash,
                        CompatibleWith = manifest.CompatibleWith,
                        Date = manifest.Date ?? "",
                        Size = 0,
                    }, manifest));
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Write($"  '{path}': could not read manifest — {ex.Message}");
                }
            }
            if (pairs.Count == 0) continue;

            // Newest-first + cap. The newest drives the entry's top-level fields.
            var versions = TranslationCompat.OrderVersions(pairs.Select(p => p.ver));
            var newestVer = versions[0];
            var newestManifest = pairs.First(p => ReferenceEquals(p.ver, newestVer)).manifest;

            entries.Add(new TranslationIndexEntry
            {
                Id = newestManifest.Id,
                Name = newestManifest.Name,
                Language = string.IsNullOrEmpty(newestManifest.Language) ? newestManifest.Id : newestManifest.Language,
                Author = newestManifest.Author,
                Version = newestVer.Version,
                CompatibleWith = newestVer.CompatibleWith,
                DownloadUrl = newestVer.DownloadUrl,
                Size = newestVer.Size,
                Description = newestManifest.Description,
                TargetMod = newestManifest.TargetMod,
                ContentHash = newestVer.ContentHash,
                FromFolder = true,
                Versions = versions,
            });
            DiagnosticLog.Write(
                $"  '{lang}': {versions.Count} version(s), newest v{newestVer.Version} ({newestVer.ContentHash}).");
        }

        DiagnosticLog.Write($"Translation tree scanned: {entries.Count} translation(s).");
        return new TranslationIndex { Translations = entries };
    }

    /// <summary>
    /// Combined discovery (DUAL MODE): folder-published packs from
    /// <paramref name="folderRepo"/> + legacy release-published packs from
    /// <paramref name="releasesRepo"/>, merged by id with FOLDER packs winning.
    /// Folder packs are listed first so they rank as "newest" in
    /// <see cref="TranslationCompat.OrderForDisplay"/>. Returns null only when
    /// both sources are unreachable; an empty-but-reachable source contributes []
    /// rather than failing the whole index.
    /// </summary>
    public async Task<TranslationIndex?> FetchAsync(
        string? folderRepo, string? releasesRepo, CancellationToken ct = default)
    {
        TranslationIndex? folder = null, releases = null;
        if (!string.IsNullOrWhiteSpace(folderRepo))
            folder = await FetchFromRepoFolderAsync(folderRepo!, ct);
        if (!string.IsNullOrWhiteSpace(releasesRepo))
            releases = await FetchFromReleasesAsync(releasesRepo!, ct);

        if (folder == null && releases == null) return null;

        var folderEntries = folder?.Translations ?? new List<TranslationIndexEntry>();
        var releaseEntries = releases?.Translations ?? new List<TranslationIndexEntry>();
        var folderIds = new HashSet<string>(
            folderEntries.Select(e => e.Id), StringComparer.OrdinalIgnoreCase);

        var combined = new List<TranslationIndexEntry>(folderEntries);
        combined.AddRange(releaseEntries.Where(e => !folderIds.Contains(e.Id)));
        return new TranslationIndex { Translations = combined };
    }

    // Minimal DTOs for the GitHub Releases API response. Only the fields we
    // actually need — the API returns ~30 more we don't care about.
    private class GitHubTree
    {
        [JsonPropertyName("tree")]
        public List<GitHubTreeNode>? Tree { get; set; }

        [JsonPropertyName("truncated")]
        public bool Truncated { get; set; }
    }

    private class GitHubTreeNode
    {
        [JsonPropertyName("path")]
        public string Path { get; set; } = "";

        /// <summary>"blob" (file) or "tree" (directory).</summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = "";
    }

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
