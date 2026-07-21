using System;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace WarsOfLibertyLauncher.Services;

/// <summary>Raised when a Heaven download can't be completed, with a reason worth showing.</summary>
public sealed class HeavenDownloadException : Exception
{
    public HeavenDownloadException(string message) : base(message) { }
}

/// <summary>
/// Downloads a file from AoE3 Heaven's download section, so a player can get an
/// addon with one click instead of finding it, downloading it and importing it.
///
/// The site does it in two steps, and the direct link is not stable enough to
/// store in the catalog:
///
///   1. <c>showfile.php?fileid=N</c> renders the file page, which carries a
///      per-file token inside an inline handler:
///      <c>onclick="get_file('1932','caf2c858…')"</c>.
///   2. <c>getfile.php?id=N&amp;dd=1&amp;s=&lt;token&gt;</c> returns the archive.
///
/// Pasting a link copied out of a browser does NOT work — that was the first
/// thing tried here, and it returns a page rather than a file once the token has
/// aged out. The token has to be read fresh, which is exactly what this does.
/// </summary>
public static class HeavenDownloader
{
    private const string BaseUrl = "https://aoe3.heavengames.com/downloads/";

    /// <summary>
    /// Identifies the launcher honestly rather than impersonating a browser.
    /// If the site ever refuses it, prefer asking them over spoofing — the
    /// catalog re-host path exists precisely so this scrape is optional.
    /// </summary>
    private const string UserAgent = "Aoe3ModLauncher/1.0 (+https://github.com/Gorgorito12)";

    public static string PageUrlFor(string fileId) => $"{BaseUrl}showfile.php?fileid={fileId}";

    /// <summary>
    /// Pulls the download token out of a file page.
    ///
    /// Pure and separately testable on purpose: this regex is the part that will
    /// break the day Heaven changes their markup, and a unit test over a saved
    /// page is the only way to notice without hitting the network.
    /// </summary>
    public static string? ParseToken(string? html, string fileId)
    {
        if (string.IsNullOrEmpty(html) || string.IsNullOrWhiteSpace(fileId)) return null;

        var m = Regex.Match(
            html,
            @"get_file\s*\(\s*['""]" + Regex.Escape(fileId) + @"['""]\s*,\s*['""](?<t>[0-9a-fA-F]{8,64})['""]",
            RegexOptions.IgnoreCase);

        return m.Success ? m.Groups["t"].Value : null;
    }

    /// <summary>
    /// Resolves the token and downloads the archive to <paramref name="destPath"/>.
    /// </summary>
    public static async Task DownloadAsync(
        string fileId, string destPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fileId))
            throw new HeavenDownloadException("No file id was given.");

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);

        var pageUrl = PageUrlFor(fileId);
        var html = await http.GetStringAsync(pageUrl, ct);

        var token = ParseToken(html, fileId);
        if (token == null)
            throw new HeavenDownloadException(
                $"Could not find the download token on {pageUrl} — the site's page layout " +
                "probably changed. The catalog's re-hosted copy is the stable path.");

        var fileUrl = $"{BaseUrl}getfile.php?id={fileId}&dd=1&s={token}";
        using var req = new HttpRequestMessage(HttpMethod.Get, fileUrl);
        req.Headers.Referrer = new Uri(pageUrl);

        using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();

        var bytes = await res.Content.ReadAsByteArrayAsync(ct);

        // Validate by MAGIC BYTES, not Content-Type and not the status code.
        // Every failed attempt during development came back as a perfectly valid
        // HTTP 200 serving an HTML interstitial; writing that out as a .zip would
        // produce a corrupt addon and an error surfacing much later, far from the
        // cause. Checking the actual bytes is the only honest test.
        if (!LooksLikeZip(bytes))
            throw new HeavenDownloadException(
                $"{fileUrl} returned {bytes.Length} bytes that are not a ZIP archive " +
                "(most likely an HTML page). The download was not saved.");

        var dir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(destPath, bytes, ct);

        DiagnosticLog.Write($"Heaven: downloaded file {fileId} ({bytes.Length:N0} bytes).");
    }

    /// <summary>ZIP local file header — <c>PK\x03\x04</c>.</summary>
    internal static bool LooksLikeZip(byte[]? bytes) =>
        bytes is { Length: >= 4 } &&
        bytes[0] == 0x50 && bytes[1] == 0x4B && bytes[2] == 0x03 && bytes[3] == 0x04;
}
