using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Discovers community translations from every source a mod reads (see
/// <see cref="TranslationSources"/>) and downloads the pack the player picks.
///
/// <para>Three kinds of source, each read in its own try/catch so one that is offline, private
/// or broken never empties the list: a GitHub repository's
/// <c>translations/&lt;id&gt;/&lt;version&gt;/</c> folders (one Git Trees call, manifests via the
/// raw CDN), a GitHub repository's releases (the legacy path), and a
/// <c>translations-index.json</c> at any https address (<see cref="TranslationIndexSource"/>).
/// Whatever they list becomes ONE CARD PER TRANSLATOR (<see cref="TranslationSourceGrouping"/>).</para>
/// </summary>
public class TranslationRegistryService
{
    private static readonly HttpClient Http = CreateHttpClient();

    private static readonly System.Text.RegularExpressions.Regex TranslationManifestPathRegex =
        new(@"^translations/([^/]+)(?:/([^/]+))?/translation\.json$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    // ------------------------------------------------------------------------
    // Discovery
    // ------------------------------------------------------------------------

    /// <summary>
    /// The cards <paramref name="modId"/> shows: every source fetched, then filtered per version
    /// and grouped per translator. Null only when there were sources and NONE could be reached,
    /// so the caller can tell "offline" from "nothing published".
    /// </summary>
    public async Task<TranslationIndex?> FetchAsync(
        TranslationSources sources, string modId, CancellationToken ct = default)
    {
        var results = await FetchSourcesAsync(sources, ct);
        if (results.Count > 0 && results.All(r => !r.Reachable)) return null;
        return new TranslationIndex { Translations = TranslationSourceGrouping.BuildForMod(results, modId) };
    }

    /// <summary>Fetches every source, each isolated from the others' failures.</summary>
    public async Task<List<SourceFetchResult>> FetchSourcesAsync(TranslationSources sources, CancellationToken ct = default)
    {
        var results = new List<SourceFetchResult>();
        foreach (var (source, official) in (sources ?? TranslationSources.Empty).All)
        {
            try
            {
                results.Add(await FetchSourceAsync(source, official, ct));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"Translation source '{source}' failed, skipping: {ex.Message}");
                results.Add(SourceFetchResult.Failed(source, official, "TxSrcErrUnreachable", ex.Message));
            }
        }
        return results;
    }

    /// <summary>Reads one source. Only a cancellation by the caller escapes as an exception.</summary>
    public Task<SourceFetchResult> FetchSourceAsync(TranslationSourceRef source, bool isOfficial, CancellationToken ct = default) =>
        source.Kind switch
        {
            TranslationSourceKind.GitHubFolder => FetchRepoFolderAsync(source, isOfficial, ct),
            TranslationSourceKind.GitHubReleases => FetchReleasesAsync(source, isOfficial, ct),
            TranslationSourceKind.Index => FetchIndexAsync(source, isOfficial, ct),
            _ => Task.FromResult(SourceFetchResult.Failed(source, isOfficial, "TxSrcErrInvalid")),
        };

    /// <summary>
    /// A repository's <c>translations/&lt;id&gt;/&lt;version&gt;/translation.json</c> folders (or the
    /// legacy flat <c>translations/&lt;id&gt;/</c>). Any <c>translation.json</c> at another depth
    /// is LOGGED with the shape expected — it used to be skipped in silence, which is how a whole
    /// version history in <c>translations/historial/…</c> stayed invisible with nobody knowing why.
    /// </summary>
    private async Task<SourceFetchResult> FetchRepoFolderAsync(TranslationSourceRef source, bool isOfficial, CancellationToken ct)
    {
        var repo = source.Location;
        var apiUrl = $"https://api.github.com/repos/{repo}/git/trees/main?recursive=1";
        DiagnosticLog.Write($"Fetching translation tree from: {apiUrl}");

        GitHubTree? tree;
        try
        {
            tree = await Http.GetFromJsonAsync<GitHubTree>(apiUrl, ct);
        }
        catch (HttpRequestException ex)
        {
            DiagnosticLog.Write($"Translation tree unavailable ({repo}): {ex.Message}");
            var key = ex.StatusCode switch
            {
                HttpStatusCode.NotFound => "TxSrcErrRepoNotFound",
                HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests => "TxSrcErrRateLimited",
                _ => "TxSrcErrUnreachable",
            };
            return SourceFetchResult.Failed(source, isOfficial, key, ex.Message);
        }

        var result = new SourceFetchResult
        {
            Source = source, IsOfficial = isOfficial, Reachable = true, Label = repo,
        };
        if (tree?.Tree == null) return result;
        if (tree.Truncated)
            DiagnosticLog.Write($"  WARNING: tree for {repo} was truncated — some packs may be missed.");

        var paths = new List<(string Path, string Folder)>();
        foreach (var node in tree.Tree)
        {
            if (!string.Equals(node.Type, "blob", StringComparison.Ordinal)) continue;
            var p = node.Path ?? "";
            var m = TranslationManifestPathRegex.Match(p);
            if (m.Success)
                paths.Add((p, m.Groups[1].Value));
            else if (p.StartsWith("translations/", StringComparison.Ordinal)
                     && p.EndsWith("/translation.json", StringComparison.Ordinal))
                DiagnosticLog.Write(
                    $"  '{repo}': ignored '{p}' — a translation must live in translations/<id>/<version>/translation.json.");
        }

        foreach (var (path, folder) in paths)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var manifestJson = await Http.GetStringAsync($"https://raw.githubusercontent.com/{repo}/main/{path}", ct);
                var manifest = JsonSerializer.Deserialize<TranslationManifest>(manifestJson);
                if (manifest == null || !TranslationPathPolicy.IsSafePackId(manifest.Id))
                {
                    DiagnosticLog.Write($"  '{path}': bad manifest or id — skipped");
                    continue;
                }
                if (!string.Equals(manifest.Id, folder, StringComparison.OrdinalIgnoreCase))
                    DiagnosticLog.Write($"  '{path}': the folder is '{folder}' but the manifest says '{manifest.Id}'.");

                var dir = path.Substring(0, path.LastIndexOf('/'));
                var zipName = !string.IsNullOrWhiteSpace(manifest.Zip) ? manifest.Zip! : $"{manifest.Id}.zip";
                result.Versions.Add(new SourcedVersion(
                    manifest.Id, manifest.Name, manifest.Language, manifest.Description,
                    new TranslationVersion
                    {
                        Version = manifest.Version,
                        DownloadUrl = $"https://raw.githubusercontent.com/{repo}/main/{dir}/{zipName}",
                        ContentHash = TranslationCompat.EffectiveContentHash(manifest),
                        CompatibleWith = manifest.CompatibleWith ?? new List<string>(),
                        Date = manifest.Date ?? "",
                        Size = 0,
                        SourceRepo = repo,
                        TargetMod = manifest.TargetMod ?? "",
                        Author = manifest.Author ?? "",
                        SourceKey = source.Key,
                        SourceLabel = repo,
                        SourceKind = TranslationSourceKind.GitHubFolder,
                        IsOfficial = isOfficial,
                    }));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"  '{path}': could not read manifest — {ex.Message}");
            }
        }

        DiagnosticLog.Write($"Translation tree scanned ({repo}): {result.Versions.Count} version(s).");
        return result;
    }

    /// <summary>
    /// The legacy path: each release ships a <c>translation.json</c> and a <c>.zip</c>. The
    /// manifest is downloaded anyway, so its content hash is recorded — that is what lets the
    /// Language tab tell two releases apart when both call themselves "1.1".
    /// </summary>
    private async Task<SourceFetchResult> FetchReleasesAsync(TranslationSourceRef source, bool isOfficial, CancellationToken ct)
    {
        var repo = source.Location;
        var apiUrl = $"https://api.github.com/repos/{repo}/releases?per_page=100";
        DiagnosticLog.Write($"Fetching translation releases from: {apiUrl}");

        List<GitHubRelease>? releases;
        try
        {
            releases = await Http.GetFromJsonAsync<List<GitHubRelease>>(apiUrl, ct);
        }
        catch (HttpRequestException ex)
        {
            DiagnosticLog.Write($"GitHub releases API unavailable: {ex.Message}");
            return SourceFetchResult.Failed(source, isOfficial,
                ex.StatusCode == HttpStatusCode.NotFound ? "TxSrcErrRepoNotFound" : "TxSrcErrUnreachable", ex.Message);
        }

        var result = new SourceFetchResult
        {
            Source = source, IsOfficial = isOfficial, Reachable = true, Label = repo,
        };
        foreach (var release in releases ?? new List<GitHubRelease>())
        {
            ct.ThrowIfCancellationRequested();
            if (release.Draft || release.Assets == null) continue;

            var manifestAsset = release.Assets.FirstOrDefault(a =>
                string.Equals(a.Name, TranslationManifest.ManifestFileName, StringComparison.OrdinalIgnoreCase));
            var zipAsset = release.Assets.LastOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            if (manifestAsset == null || zipAsset == null)
            {
                DiagnosticLog.Write($"  release '{release.TagName}': missing translation.json or .zip — skipped");
                continue;
            }

            try
            {
                var manifestJson = await Http.GetStringAsync(manifestAsset.BrowserDownloadUrl, ct);
                var manifest = JsonSerializer.Deserialize<TranslationManifest>(manifestJson);
                if (manifest == null || !TranslationPathPolicy.IsSafePackId(manifest.Id))
                {
                    DiagnosticLog.Write($"  release '{release.TagName}': bad manifest or id — skipped");
                    continue;
                }
                result.Versions.Add(new SourcedVersion(
                    manifest.Id, manifest.Name, manifest.Language, manifest.Description,
                    new TranslationVersion
                    {
                        Version = manifest.Version,
                        DownloadUrl = zipAsset.BrowserDownloadUrl,
                        ContentHash = TranslationCompat.EffectiveContentHash(manifest),
                        CompatibleWith = manifest.CompatibleWith ?? new List<string>(),
                        Date = manifest.Date ?? "",
                        Size = zipAsset.Size,
                        SourceRepo = repo,
                        TargetMod = manifest.TargetMod ?? "",
                        Author = manifest.Author ?? "",
                        SourceKey = source.Key,
                        SourceLabel = repo,
                        SourceKind = TranslationSourceKind.GitHubReleases,
                        IsOfficial = isOfficial,
                    }));
                DiagnosticLog.Write($"  release '{release.TagName}': loaded '{manifest.Id}' v{manifest.Version}");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"  release '{release.TagName}': could not read manifest — {ex.Message}");
            }
        }

        DiagnosticLog.Write($"Translation releases scanned ({repo}): {result.Versions.Count} valid entries.");
        return result;
    }

    /// <summary>A <c>translations-index.json</c> at any https address.</summary>
    private static async Task<SourceFetchResult> FetchIndexAsync(TranslationSourceRef source, bool isOfficial, CancellationToken ct)
    {
        DiagnosticLog.Write($"Fetching translation index from: {source.Location}");
        var parsed = await TranslationIndexSource.FetchAsync(Http, source, ct);
        if (!parsed.Ok)
        {
            DiagnosticLog.Write($"Translation index unavailable ({source.Location}): {parsed.ErrorKey} {parsed.ErrorDetail}");
            return SourceFetchResult.Failed(source, isOfficial, parsed.ErrorKey!, parsed.ErrorDetail);
        }

        var label = TranslationIndexSource.LabelFor(source, parsed.SourceName);
        var result = new SourceFetchResult
        {
            Source = source, IsOfficial = isOfficial, Reachable = true, Label = label,
        };
        foreach (var r in parsed.Records)
            result.Versions.Add(new SourcedVersion(r.Id, r.Name, r.Language, r.Description, new TranslationVersion
            {
                Version = r.Version,
                DownloadUrl = r.ZipUrl,
                ContentHash = r.ContentHash,
                CompatibleWith = r.CompatibleWith,
                Date = r.Date,
                Size = r.Size,
                SourceRepo = source.Location,
                Sha256 = r.Sha256,
                TargetMod = r.TargetMod,
                Author = r.Author,
                SourceKey = source.Key,
                SourceLabel = label,
                SourceKind = TranslationSourceKind.Index,
                IsOfficial = isOfficial,
            }));
        DiagnosticLog.Write($"Translation index read ({label}): {result.Versions.Count} version(s).");
        return result;
    }

    /// <summary>
    /// The <c>translation.json</c> paths a repository holds — one Git Trees call, no manifest
    /// reads. The packager uses it to warn that a version folder already exists before the
    /// translator builds a pack that would collide with it.
    /// </summary>
    public async Task<IReadOnlyList<string>?> ListManifestPathsAsync(string repo, CancellationToken ct = default)
    {
        if (!TranslationSourceRef.TryNormalizeRepo(repo, out var normalized)) return null;
        try
        {
            var tree = await Http.GetFromJsonAsync<GitHubTree>(
                $"https://api.github.com/repos/{normalized}/git/trees/main?recursive=1", ct);
            return tree?.Tree?
                .Where(n => n.Type == "blob" && (n.Path ?? "").EndsWith("/translation.json", StringComparison.Ordinal))
                .Select(n => n.Path)
                .ToList() ?? new List<string>();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Translation tree listing failed ({repo}): {ex.Message}");
            return null;
        }
    }

    // Minimal DTOs for the GitHub API responses. Only the fields we
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

    // ------------------------------------------------------------------------
    // Download
    // ------------------------------------------------------------------------

    /// <summary>
    /// Downloads the .zip file for a single translation pack to the given
    /// destination path on disk. Throws on failure.
    ///
    /// <para>The URL came from a source's listing, which may be one the player added, so it is
    /// held to the same rules as any other foreign link. A share link is first turned into its
    /// direct-download form (<see cref="ShareLinkResolver"/>), and only https is fetched. The body
    /// is capped at <see cref="TranslationService.MaxPackZipBytes"/> — counted as it arrives —
    /// and must start like a zip; Google Drive's "can't scan this file" page is followed once.
    /// When <paramref name="expectedSha256"/> is given (always, for an index source) the file must
    /// hash to it, or it is deleted and nothing is installed.</para>
    /// </summary>
    public async Task DownloadPackAsync(
        string downloadUrl,
        string destinationPath,
        string? expectedSha256,
        CancellationToken ct = default)
    {
        var resolved = ShareLinkResolver.Resolve(downloadUrl);
        if (resolved.Kind == ShareLinkKind.Rejected)
            throw new InvalidDataException(Strings.Get(resolved.ReasonKey ?? "TxSrcErrInvalid"));
        var url = resolved.Url;

        string? sha256 = null;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (!IsAllowedPackUrl(url))
                throw new InvalidDataException(Strings.Get("TxSrcErrNotHttps"));

            DiagnosticLog.Write($"Downloading translation pack: {url}");
            using (var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                var max = TranslationService.MaxPackZipBytes;
                if (response.Content.Headers.ContentLength is long declared && declared > max)
                    throw new InvalidDataException(
                        $"The translation pack is larger than {max / (1024 * 1024)} MB.");

                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using (var src = await response.Content.ReadAsStreamAsync(ct))
                await using (var dst = File.Create(destinationPath))
                    await CopyCappedAsync(src, dst, max, ct, hash);
                sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            }

            if (StartsLikeZip(destinationPath)) break;

            if (attempt == 0 && ShareLinkResolver.IsDriveHost(url)
                && ShareLinkResolver.TryParseDriveConfirmForm(ReadSmallText(destinationPath), out var next)
                && next != null)
            {
                DiagnosticLog.Write("Translation pack: Google Drive asked for confirmation — following its download form once.");
                url = next;
                continue;
            }

            TryDelete(destinationPath);
            throw new InvalidDataException(Strings.Get("DlgLangNotAZip"));
        }

        if (!StartsLikeZip(destinationPath))
        {
            TryDelete(destinationPath);
            throw new InvalidDataException(Strings.Get("DlgLangNotAZip"));
        }
        if (!string.IsNullOrWhiteSpace(expectedSha256)
            && !string.Equals(sha256, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            DiagnosticLog.Write($"Translation pack SHA-256 mismatch: expected {expectedSha256}, got {sha256}.");
            TryDelete(destinationPath);
            throw new InvalidDataException(Strings.Get("DlgLangShaMismatch"));
        }
        DiagnosticLog.Write($"Translation pack downloaded to: {destinationPath} (sha256 {sha256}).");
    }

    /// <summary>A pack may only be fetched over https, from a link <see cref="SafeUrl"/> accepts.</summary>
    internal static bool IsAllowedPackUrl(string? url) =>
        SafeUrl.IsAllowed(url)
        && Uri.TryCreate(url!.Trim(), UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps;

    /// <summary>
    /// Streams <paramref name="src"/> into <paramref name="dst"/> and gives up once more than
    /// <paramref name="maxBytes"/> have arrived. A missing or lying Content-Length must not be
    /// able to fill the disk. When <paramref name="hash"/> is given, every byte is fed to it on
    /// the way through, so the file is hashed without being read twice.
    /// </summary>
    internal static async Task<long> CopyCappedAsync(
        Stream src, Stream dst, long maxBytes, CancellationToken ct, IncrementalHash? hash = null)
    {
        var buffer = new byte[81920];
        long copied = 0;
        int read;
        while ((read = await src.ReadAsync(buffer, ct)) > 0)
        {
            copied += read;
            if (copied > maxBytes)
                throw new InvalidDataException(
                    $"The translation pack is larger than {maxBytes / (1024 * 1024)} MB.");
            hash?.AppendData(buffer, 0, read);
            await dst.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return copied;
    }

    /// <summary>True when the file at <paramref name="path"/> begins with the zip signature.</summary>
    internal static bool StartsLikeZip(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var head = new byte[4];
            return fs.Read(head, 0, head.Length) == head.Length && HeavenDownloader.LooksLikeZip(head);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The start of a downloaded file as text — enough to read a confirmation page.</summary>
    private static string ReadSmallText(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var buffer = new byte[Math.Min(fs.Length, 512 * 1024)];
            var read = fs.Read(buffer, 0, buffer.Length);
            return Encoding.UTF8.GetString(buffer, 0, read);
        }
        catch
        {
            return "";
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* the caller's finally retries */ }
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
