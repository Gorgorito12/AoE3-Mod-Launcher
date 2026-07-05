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
                        SourceRepo = repo,
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
                        SourceRepo = repo,
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
                SourceRepo = repo,
            });
            DiagnosticLog.Write(
                $"  '{lang}': {versions.Count} version(s), newest v{newestVer.Version} ({newestVer.ContentHash}).");
        }

        DiagnosticLog.Write($"Translation tree scanned: {entries.Count} translation(s).");
        return new TranslationIndex { Translations = entries };
    }

    /// <summary>
    /// Combined discovery (DUAL MODE), single folder repo — back-compat wrapper
    /// that delegates to the multi-repo <see cref="FetchAsync(IReadOnlyList{string}, string?, CancellationToken)"/>.
    /// </summary>
    public Task<TranslationIndex?> FetchAsync(
        string? folderRepo, string? releasesRepo, CancellationToken ct = default)
    {
        var folderRepos = string.IsNullOrWhiteSpace(folderRepo)
            ? (IReadOnlyList<string>)Array.Empty<string>()
            : new[] { folderRepo! };
        return FetchAsync(folderRepos, releasesRepo, ct);
    }

    /// <summary>
    /// Combined discovery (DUAL MODE): folder-published packs from EVERY repo in
    /// <paramref name="folderRepos"/> (the default profile repo first, then the
    /// user's extra repos) + legacy release-published packs from
    /// <paramref name="releasesRepo"/>. All folder repos are fetched and their
    /// packs merged by id (<see cref="MergeFolderEntries"/>); folder packs win
    /// over release packs on id collision, and folder entries rank first in
    /// <see cref="TranslationCompat.OrderForDisplay"/>.
    ///
    /// Each folder repo is fetched inside its own try/catch so one unreachable /
    /// rate-limited repo (403) doesn't blank the whole menu. Returns null only
    /// when NO folder repo was reachable AND the releases source was unreachable;
    /// an empty-but-reachable source contributes [] rather than failing.
    /// </summary>
    public async Task<TranslationIndex?> FetchAsync(
        IReadOnlyList<string> folderRepos, string? releasesRepo, CancellationToken ct = default)
    {
        // Fetch each folder repo independently; keep repo ORDER (default first)
        // because MergeFolderEntries uses index 0 as the authoritative default.
        var perRepo = new List<IReadOnlyList<TranslationIndexEntry>>();
        bool anyFolderReachable = false;
        foreach (var repo in folderRepos ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(repo)) continue;
            try
            {
                var idx = await FetchFromRepoFolderAsync(repo, ct);
                if (idx != null)
                {
                    anyFolderReachable = true;
                    perRepo.Add(idx.Translations ?? new List<TranslationIndexEntry>());
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"Translation folder repo '{repo}' failed, skipping: {ex.Message}");
            }
        }

        TranslationIndex? releases = null;
        if (!string.IsNullOrWhiteSpace(releasesRepo))
            releases = await FetchFromReleasesAsync(releasesRepo!, ct);

        if (!anyFolderReachable && releases == null) return null;

        var folderEntries = MergeFolderEntries(perRepo);
        var releaseEntries = releases?.Translations ?? new List<TranslationIndexEntry>();
        var folderIds = new HashSet<string>(
            folderEntries.Select(e => e.Id), StringComparer.OrdinalIgnoreCase);

        var combined = new List<TranslationIndexEntry>(folderEntries);
        combined.AddRange(releaseEntries.Where(e => !folderIds.Contains(e.Id)));
        return new TranslationIndex { Translations = combined };
    }

    /// <summary>
    /// Merges the per-repo folder-scan results into one entry per id. Pure (no
    /// I/O) so it's unit-testable. <paramref name="perRepoInOrder"/> is the list
    /// of each repo's entries, in repo order — index 0 is the DEFAULT repo.
    ///
    /// On an id collision (two repos publish the same pack id), the versions of
    /// all repos are UNIONED into one entry's <see cref="TranslationIndexEntry.Versions"/>
    /// list (de-duplicated by <c>ContentHash</c>, newest-first, capped by
    /// <see cref="TranslationCompat.OrderVersions"/>), each version keeping its
    /// <see cref="TranslationVersion.SourceRepo"/>. The BASE entry (whose
    /// display + one-click-apply metadata is used) is:
    ///   * the DEFAULT repo's entry for that id if present ("mine is the default"
    ///     — so an added repo can't rename or hijack the one-click apply of a pack
    ///     the default already ships); otherwise
    ///   * the entry that owns the globally-newest version.
    /// Entries are emitted in first-seen id order (default repo's ids first) so
    /// the display ranking stays stable.
    /// </summary>
    public static List<TranslationIndexEntry> MergeFolderEntries(
        IReadOnlyList<IReadOnlyList<TranslationIndexEntry>> perRepoInOrder)
    {
        var order = new List<string>();
        var byId = new Dictionary<string, List<(int repoIndex, TranslationIndexEntry entry)>>(
            StringComparer.OrdinalIgnoreCase);

        for (int ri = 0; ri < (perRepoInOrder?.Count ?? 0); ri++)
        {
            var repoEntries = perRepoInOrder![ri];
            if (repoEntries == null) continue;
            foreach (var e in repoEntries)
            {
                if (e == null || string.IsNullOrEmpty(e.Id)) continue;
                if (!byId.TryGetValue(e.Id, out var group))
                {
                    group = new();
                    byId[e.Id] = group;
                    order.Add(e.Id);
                }
                group.Add((ri, e));
            }
        }

        var merged = new List<TranslationIndexEntry>(order.Count);
        foreach (var id in order)
        {
            var group = byId[id];

            // Union all versions across repos for this id (default-first, so the
            // default's instance wins a contentHash tie), then order + cap.
            var union = new List<TranslationVersion>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (_, entry) in group)
            {
                if (entry.Versions == null) continue;
                foreach (var v in entry.Versions)
                {
                    var key = !string.IsNullOrWhiteSpace(v.ContentHash)
                        ? "h:" + v.ContentHash
                        : $"k:{v.SourceRepo}|{v.Version}|{v.DownloadUrl}";
                    if (seen.Add(key)) union.Add(v);
                }
            }
            var ordered = TranslationCompat.OrderVersions(union);

            // Base entry: the default repo's entry (repoIndex 0) if present, else
            // the entry owning the globally-newest version.
            TranslationIndexEntry baseEntry;
            var defaultOwned = group
                .Where(g => g.repoIndex == 0)
                .Select(g => g.entry)
                .FirstOrDefault();
            if (defaultOwned != null)
            {
                baseEntry = defaultOwned;
            }
            else if (ordered.Count > 0)
            {
                var newest = ordered[0];
                baseEntry = group
                    .Select(g => g.entry)
                    .FirstOrDefault(e => e.Versions != null
                        && e.Versions.Any(v => ReferenceEquals(v, newest)))
                    ?? group[0].entry;
            }
            else
            {
                baseEntry = group[0].entry;
            }

            merged.Add(new TranslationIndexEntry
            {
                Id = baseEntry.Id,
                Name = baseEntry.Name,
                Language = baseEntry.Language,
                Author = baseEntry.Author,
                Version = baseEntry.Version,
                CompatibleWith = baseEntry.CompatibleWith,
                DownloadUrl = baseEntry.DownloadUrl,
                Size = baseEntry.Size,
                Sha256 = baseEntry.Sha256,
                Description = baseEntry.Description,
                TargetMod = baseEntry.TargetMod,
                ContentHash = baseEntry.ContentHash,
                FromFolder = true,
                SourceRepo = baseEntry.SourceRepo,
                Versions = ordered,
            });
        }
        return merged;
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
