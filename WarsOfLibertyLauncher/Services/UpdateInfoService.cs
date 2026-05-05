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
    /// Fetches UpdateInfo.xml from the primary URL, falling back to the alternate
    /// if the primary fails.
    /// </summary>
    public async Task<UpdateInfo> FetchAsync(
        string primaryUrl,
        string alternateUrl,
        CancellationToken ct = default)
    {
        try
        {
            return await FetchFromUrlAsync(primaryUrl, ct);
        }
        catch (Exception primaryEx) when (!ct.IsCancellationRequested)
        {
            try
            {
                return await FetchFromUrlAsync(alternateUrl, ct);
            }
            catch (Exception altEx)
            {
                throw new InvalidOperationException(
                    Strings.Format("ErrManifestUnreachable",
                        primaryUrl, primaryEx.Message,
                        alternateUrl, altEx.Message),
                    altEx);
            }
        }
    }

    private async Task<UpdateInfo> FetchFromUrlAsync(string url, CancellationToken ct)
    {
        DiagnosticLog.Write($"Requesting UpdateInfo.xml from: {url}");
        var xml = await _http.GetStringAsync(url, ct);
        DiagnosticLog.Write($"Response received: {xml.Length} characters");

        // Save raw XML for inspection — invaluable when debugging parsing issues.
        DiagnosticLog.SaveSnapshot("UpdateInfo-snapshot.xml", xml);

        var parsed = ParseXml(xml);
        DiagnosticLog.Write($"Parser: {parsed.Versions.Count} versions, " +
                            $"{parsed.Downloads.Count} downloads found.");

        if (parsed.Downloads.Count > 0)
        {
            var first = parsed.Downloads[0];
            var last = parsed.Downloads[^1];
            DiagnosticLog.Write($"First download: id={first.Id}, version={first.Version}, " +
                                $"size={first.Size}, link={first.Link}");
            DiagnosticLog.Write($"Last download: id={last.Id}, version={last.Version}, " +
                                $"size={last.Size}");
        }

        return parsed;
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
        // element name or nesting depth — any <version>, <download>, or
        // <updaterinfo> we find anywhere is processed.
        ProcessNode(doc.DocumentElement, info);

        return info;
    }

    private static void ProcessNode(XmlNode node, UpdateInfo info)
    {
        if (node.NodeType == XmlNodeType.Element && node.Attributes != null)
        {
            switch (node.Name.ToLowerInvariant())
            {
                case "updaterinfo":
                    info.UpdaterInfo = new UpdaterInfo
                    {
                        Ver = Attr(node, "ver"),
                        Link = Attr(node, "link")
                    };
                    break;

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
