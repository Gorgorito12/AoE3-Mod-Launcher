using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Downloads and parses the UpdateInfo.xml file from the official servers.
/// Has automatic fallback from the primary URL to the alternate URL.
/// </summary>
public class UpdateInfoService
{
    private readonly HttpClient _http;

    public UpdateInfoService(HttpClient? http = null)
    {
        _http = http ?? CreateDefaultClient();
    }

    private static HttpClient CreateDefaultClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("WarsOfLibertyLauncher/0.3");
        return client;
    }

    /// <summary>
    /// Whether a parsed manifest is worth acting on. A manifest with no
    /// <c>&lt;version&gt;</c> elements carries no information: it can neither identify
    /// the installed build nor name a latest one.
    ///
    /// <para><b>Why this is a hard gate and not a warning.</b> aoe3wol's HTTP endpoint
    /// has been observed serving a TRUNCATED body. When the truncation lands on an
    /// element boundary the result still parses, so the old code accepted it, reported
    /// the network as reachable, and handed <c>CheckCoreAsync</c> an empty version
    /// list — which reads exactly like "your perfectly good 1.2.0e install matches no
    /// known version". That is the failure that once had users re-downloading 4 GB
    /// into a second folder. Treating it as a fetch FAILURE instead means the
    /// alternate URL gets its turn, and if that is empty too the caller degrades to
    /// the offline path, which keeps PLAY working and never proposes a reinstall.</para>
    ///
    /// Pure, so the rule is unit-testable without a server.
    /// </summary>
    internal static bool IsUsable(UpdateInfo? info) => info != null && info.Versions.Count > 0;

    /// <summary>
    /// Fetches UpdateInfo.xml from the primary URL, falling back to the alternate
    /// if the primary fails or returns a manifest that carries no versions.
    ///
    /// <paramref name="cacheKey"/> (the mod id) enables the last-known-good fallback:
    /// every manifest that passes <see cref="IsUsable"/> is kept on disk, and is used
    /// when BOTH URLs fail. Pass null to disable caching entirely.
    /// </summary>
    public async Task<UpdateInfo> FetchAsync(
        string primaryUrl,
        string alternateUrl,
        CancellationToken ct = default,
        string? cacheKey = null)
    {
        try
        {
            var info = await FetchFromUrlAsync(primaryUrl, cacheKey, ct);
            ConnectivityState.ReportSuccess();   // reached the network
            return info;
        }
        catch (Exception primaryEx) when (!ct.IsCancellationRequested)
        {
            DiagnosticLog.Write(
                $"UpdateInfo.xml from primary '{primaryUrl}' unusable " +
                $"({primaryEx.GetType().Name}: {primaryEx.Message}); trying '{alternateUrl}'.");
            try
            {
                var info = await FetchFromUrlAsync(alternateUrl, cacheKey, ct);
                ConnectivityState.ReportSuccess();   // reached the network (mirror)
                return info;
            }
            catch (Exception altEx)
            {
                // Last resort: the newest manifest we ever validated. Better than
                // degrading to offline, which can't offer updates at all — and it is
                // safe in the direction that matters, because a stale manifest can
                // only list FEWER patches than the live one, never invent one
                // (ComputePendingDownloads filters by MinReqDownload and id).
                var cached = LoadFromCache(cacheKey);
                if (cached != null)
                {
                    DiagnosticLog.Write(
                        $"Both UpdateInfo.xml URLs failed; using the last validated copy " +
                        $"({cached.Versions.Count} versions).");
                    return cached;
                }

                throw new InvalidOperationException(
                    Strings.Format("ErrManifestUnreachable",
                        primaryUrl, primaryEx.Message,
                        alternateUrl, altEx.Message),
                    altEx);
            }
        }
    }

    private async Task<UpdateInfo> FetchFromUrlAsync(
        string url, string? cacheKey, CancellationToken ct)
    {
        DiagnosticLog.Write($"Requesting UpdateInfo.xml from: {url}");
        var xml = await _http.GetStringAsync(url, ct);
        DiagnosticLog.Write($"Response received: {xml.Length} characters");

        // Save raw XML for inspection — invaluable when debugging parsing issues.
        // Note this snapshot is written even for a body we go on to REJECT below,
        // which is exactly what makes it useful in a diagnostic bundle; it is not
        // the same thing as the validated cache.
        DiagnosticLog.SaveSnapshot("UpdateInfo-snapshot.xml", xml);

        var parsed = ParseXml(xml);
        DiagnosticLog.Write($"Parser: {parsed.Versions.Count} versions, " +
                            $"{parsed.Downloads.Count} downloads found.");

        if (!IsUsable(parsed))
        {
            throw new InvalidDataException(
                $"UpdateInfo.xml from '{url}' parsed but lists no versions " +
                $"({xml.Length} characters received) — treating as a failed fetch.");
        }

        if (parsed.Downloads.Count > 0)
        {
            var first = parsed.Downloads[0];
            var last = parsed.Downloads[^1];
            DiagnosticLog.Write($"First download: id={first.Id}, version={first.Version}, " +
                                $"size={first.Size}, link={first.Link}");
            DiagnosticLog.Write($"Last download: id={last.Id}, version={last.Version}, " +
                                $"size={last.Size}");
        }

        // Only a manifest that got this far is cached — the point of the cache is
        // that its contents were validated, so it can be trusted when both hosts are
        // unreachable.
        SaveToCache(cacheKey, xml);
        return parsed;
    }

    // -- Last-known-good cache ---------------------------------------------------

    /// <summary>
    /// Where a validated manifest is kept, per mod. Deliberately separate from
    /// <c>DiagnosticLog.SaveSnapshot</c>'s <c>UpdateInfo-snapshot.xml</c>, which is a
    /// single shared file written even for garbage bodies — reading that back would
    /// reintroduce the very truncation this guards against.
    /// </summary>
    private static string CacheFileFor(string cacheKey) =>
        Path.Combine(AppPaths.DataDir, $"updateinfo-cache-{Sanitize(cacheKey)}.xml");

    private static string Sanitize(string key)
    {
        var chars = key.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (Array.IndexOf(Path.GetInvalidFileNameChars(), chars[i]) >= 0) chars[i] = '_';
        return new string(chars);
    }

    /// <summary>Best-effort: a cache we can't write just means no fallback next time.</summary>
    private static void SaveToCache(string? cacheKey, string? xml)
    {
        if (string.IsNullOrEmpty(cacheKey) || string.IsNullOrEmpty(xml)) return;
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            File.WriteAllText(CacheFileFor(cacheKey), xml);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Could not cache UpdateInfo.xml for '{cacheKey}': {ex.Message}");
        }
    }

    /// <summary>
    /// The cached manifest, or null when there isn't one / it no longer parses. Re-runs
    /// <see cref="IsUsable"/> rather than trusting the file, so a half-written cache
    /// can't do what a truncated download would have.
    /// </summary>
    private static UpdateInfo? LoadFromCache(string? cacheKey)
    {
        if (string.IsNullOrEmpty(cacheKey)) return null;
        try
        {
            var path = CacheFileFor(cacheKey);
            if (!File.Exists(path)) return null;
            var parsed = ParseXml(File.ReadAllText(path));
            return IsUsable(parsed) ? parsed : null;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Cached UpdateInfo.xml for '{cacheKey}' unreadable: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Parses the UpdateInfo.xml content into our object model.
    ///
    /// The actual XML structure (verified against http://aoe3wol.com/updates/UpdateInfo.xml)
    /// is:
    ///
    ///   &lt;updatedata&gt;
    ///       &lt;updaterinfo ver="1.4" /&gt;
    ///       &lt;versions&gt;
    ///           &lt;version .../&gt;
    ///           &lt;version .../&gt;
    ///       &lt;/versions&gt;
    ///       &lt;downloads&gt;
    ///           &lt;download .../&gt;
    ///           &lt;download .../&gt;
    ///       &lt;/downloads&gt;
    ///   &lt;/updatedata&gt;
    ///
    /// Versions appear in descending order (newest first), so the LATEST
    /// version is the first &lt;version&gt; element.
    /// </summary>
    public static UpdateInfo ParseXml(string xml)
    {
        var info = new UpdateInfo();
        var doc = new XmlDocument();
        doc.LoadXml(xml);

        if (doc.DocumentElement == null)
            throw new InvalidDataException(Strings.Get("ErrManifestEmpty"));

        // Walk the document recursively so we don't depend on the exact root
        // element name or nesting depth — any <version> or <download> we find
        // anywhere is processed.
        ProcessNode(doc.DocumentElement, info);

        return info;
    }

    private static void ProcessNode(XmlNode node, UpdateInfo info)
    {
        if (node.NodeType == XmlNodeType.Element && node.Attributes != null)
        {
            switch (node.Name.ToLowerInvariant())
            {
                case "version":
                    info.Versions.Add(new VersionInfo
                    {
                        Ver = Attr(node, "ver"),
                        TechMd5 = Attr(node, "techmd5").ToLowerInvariant(),
                        StrMd5 = Attr(node, "strmd5").ToLowerInvariant(),
                        ProtoMd5 = Attr(node, "protomd5").ToLowerInvariant(),
                        MinReqDownload = ParseInt(Attr(node, "minreqdownload"))
                    });
                    return;     // no need to recurse into a <version>

                case "download":
                    // The XML attribute for the alternate link is "alt"
                    // (not "altLink") — confirmed by disassembling the original
                    // Java updater's SAX handler.
                    info.Downloads.Add(new DownloadInfo
                    {
                        Id = ParseInt(Attr(node, "id")),
                        Size = ParseLong(Attr(node, "size")),
                        Crc32 = Attr(node, "crc32").ToLowerInvariant(),
                        Link = Attr(node, "link"),
                        AltLink = Attr(node, "alt"),
                        DeleteList = Attr(node, "deleteList"),
                        Version = Attr(node, "version"),
                        PostUpdatePage = Attr(node, "postUpdatePage")
                    });
                    return;     // no need to recurse into a <download>
            }
        }

        // Recurse into wrappers like <versions> and <downloads>
        foreach (XmlNode child in node.ChildNodes)
            ProcessNode(child, info);
    }

    private static string Attr(XmlNode node, string name) =>
        node.Attributes?[name]?.Value ?? "";

    private static int ParseInt(string s) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;

    private static long ParseLong(string s) =>
        long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
}
