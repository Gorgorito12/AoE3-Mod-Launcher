using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace WarsOfLibertyLauncher.Services;

public enum ShareLinkKind { Direct, Converted, Rejected }

/// <param name="Url">The address to actually download from (the input, or its converted form).</param>
/// <param name="ReasonKey">For <see cref="ShareLinkKind.Rejected"/>: the string-table key that says why.</param>
/// <param name="IsShareHost">
/// True for hosts where one file's address says nothing about another's (Drive, Dropbox, a
/// gist): a RELATIVE <c>zip</c> in an index hosted there can't be resolved.
/// </param>
public sealed record ShareLinkResult(ShareLinkKind Kind, string Url, string? ReasonKey, bool IsShareHost);

/// <summary>
/// Turns the link a translator copies from a sharing service into one that returns the FILE.
///
/// <para>A share link normally answers with a web page — the service's viewer — and a launcher
/// that saved that page as a zip would only fail much later, far from the cause. Google Drive
/// and Dropbox have documented direct-download forms, so those links are converted; GitHub
/// "blob" pages become raw links; and a gist raw link pinned to one revision is unpinned,
/// because a pinned link would never show the translator's next version — which is the whole
/// point of adding a source. Services with no direct form (Mega encrypts in the browser,
/// MediaFire needs its landing page) and folder links are REFUSED, each with its own reason, so
/// the player is told what to ask the translator for instead of seeing a generic error.</para>
///
/// <para>Pure and WPF-free so every case can be pinned by tests.</para>
/// </summary>
public static class ShareLinkResolver
{
    private const string DriveDownloadHost = "drive.usercontent.google.com";

    private static readonly Regex DriveIdRegex = new(@"^[A-Za-z0-9_-]{10,200}\z", RegexOptions.CultureInvariant);
    private static readonly Regex Hex40Regex = new(@"^[0-9a-fA-F]{40}\z", RegexOptions.CultureInvariant);

    public static ShareLinkResult Resolve(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return Rejected("TxSrcErrInvalid");
        if (uri.Scheme == Uri.UriSchemeHttp) return Rejected("TxSrcErrNotHttps");
        if (uri.Scheme != Uri.UriSchemeHttps || !SafeUrl.IsAllowed(url)) return Rejected("TxSrcErrInvalid");

        var trimmed = url.Trim();
        var host = uri.IdnHost.ToLowerInvariant();
        var segs = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (HostIs(host, "mega.nz") || HostIs(host, "mega.co.nz") || HostIs(host, "mega.io"))
            return Rejected("TxSrcErrMega");
        if (HostIs(host, "mediafire.com"))
            return Rejected("TxSrcErrMediaFire");
        if (HostIs(host, "onedrive.live.com") || HostIs(host, "1drv.ms") || HostIs(host, "sharepoint.com"))
            return Rejected("TxSrcErrOneDrive");
        if (HostIs(host, "docs.google.com"))
            return Rejected("TxSrcErrGoogleDocs");

        if (host == "drive.google.com") return ResolveDrive(uri, segs);
        if (host == DriveDownloadHost) return new(ShareLinkKind.Direct, trimmed, null, true);

        if (host == "dropbox.com" || host == "www.dropbox.com") return ResolveDropbox(uri, segs, trimmed);
        if (host == "dl.dropboxusercontent.com") return new(ShareLinkKind.Direct, trimmed, null, true);

        if (host == "github.com" || host == "www.github.com")
        {
            // github.com/<o>/<r>/blob/<ref>/<path…> (and /raw/) → the raw file.
            if (segs.Length >= 5 && (segs[2] == "blob" || segs[2] == "raw"))
                return new(ShareLinkKind.Converted,
                    $"https://raw.githubusercontent.com/{segs[0]}/{segs[1]}/{string.Join('/', segs.Skip(3))}{uri.Query}",
                    null, false);
            return new(ShareLinkKind.Direct, trimmed, null, false);
        }

        if (host == "gist.github.com")
        {
            // A gist page, or its /raw/ link: point at the raw file, never at one revision of it.
            if (segs.Length == 2)
                return new(ShareLinkKind.Converted,
                    $"https://gist.githubusercontent.com/{segs[0]}/{segs[1]}/raw", null, true);
            if (segs.Length >= 3 && segs[2] == "raw")
                return new(ShareLinkKind.Converted, UnpinGist(segs[0], segs[1], segs.Skip(3).ToArray()), null, true);
            return new(ShareLinkKind.Direct, trimmed, null, true);
        }

        if (host == "gist.githubusercontent.com")
        {
            if (segs.Length >= 4 && segs[2] == "raw" && Hex40Regex.IsMatch(segs[3]))
                return new(ShareLinkKind.Converted, UnpinGist(segs[0], segs[1], segs.Skip(3).ToArray()), null, true);
            return new(ShareLinkKind.Direct, trimmed, null, true);
        }

        return new(ShareLinkKind.Direct, trimmed, null, false);
    }

    /// <summary>
    /// Reads Google Drive's "can't scan this file for viruses" page and returns the address its
    /// download button submits to. Accepted only when that form posts to Drive's own download
    /// host over https — the page is HTML from the network, and the only thing it may decide is
    /// which Drive URL to try once more. The bytes it leads to are still SHA-256-checked.
    /// </summary>
    public static bool TryParseDriveConfirmForm(string? html, out string? url)
    {
        url = null;
        if (string.IsNullOrEmpty(html)) return false;

        var form = Regex.Match(html,
            @"(<form\b[^>]*\bid\s*=\s*[""']download-form[""'][^>]*>)(.*?)</form>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        if (!form.Success) return false;

        var action = Attribute(form.Groups[1].Value, "action");
        if (action == null
            || !Uri.TryCreate(action, UriKind.Absolute, out var target)
            || target.Scheme != Uri.UriSchemeHttps
            || !string.Equals(target.IdnHost, DriveDownloadHost, StringComparison.OrdinalIgnoreCase))
            return false;

        var fields = new List<(string Name, string Value)>();
        foreach (Match input in Regex.Matches(form.Groups[2].Value, @"<input\b[^>]*>",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var tag = input.Value;
            if (!string.Equals(Attribute(tag, "type"), "hidden", StringComparison.OrdinalIgnoreCase)) continue;
            var name = Attribute(tag, "name");
            if (string.IsNullOrEmpty(name)) continue;
            fields.Add((name, Attribute(tag, "value") ?? ""));
        }
        if (!fields.Any(f => f.Name == "id")) return false;

        url = target.GetLeftPart(UriPartial.Path) + "?" + string.Join("&",
            fields.Select(f => Uri.EscapeDataString(f.Name) + "=" + Uri.EscapeDataString(f.Value)));
        return true;
    }

    /// <summary>True for Google Drive's own hosts (where the confirmation page can appear).</summary>
    public static bool IsDriveHost(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u)
        && (u.IdnHost.Equals("drive.google.com", StringComparison.OrdinalIgnoreCase)
            || u.IdnHost.Equals(DriveDownloadHost, StringComparison.OrdinalIgnoreCase));

    private static ShareLinkResult ResolveDrive(Uri uri, string[] segs)
    {
        if (segs.Contains("folders", StringComparer.OrdinalIgnoreCase))
            return Rejected("TxSrcErrDriveFolder");

        string? id = null;
        for (int i = 0; i + 1 < segs.Length; i++)
            if (segs[i] == "d" && i > 0 && segs[i - 1] == "file") { id = segs[i + 1]; break; }

        var query = ParseQuery(uri.Query);
        id ??= query.FirstOrDefault(q => q.Key == "id").Value;
        if (id == null || !DriveIdRegex.IsMatch(id)) return Rejected("TxSrcErrDriveForm");

        var url = $"https://{DriveDownloadHost}/download?id={id}&export=download&confirm=t";
        var resourceKey = query.FirstOrDefault(q => q.Key == "resourcekey").Value;
        if (!string.IsNullOrEmpty(resourceKey)) url += "&resourcekey=" + Uri.EscapeDataString(resourceKey);
        return new(ShareLinkKind.Converted, url, null, true);
    }

    private static ShareLinkResult ResolveDropbox(Uri uri, string[] segs, string original)
    {
        if (segs.Length > 0 && segs[0] == "sh") return Rejected("TxSrcErrDropboxFolder");
        if (segs.Length > 1 && segs[0] == "scl" && segs[1] == "fo") return Rejected("TxSrcErrDropboxFolder");

        bool isFile = (segs.Length > 0 && segs[0] == "s") || (segs.Length > 1 && segs[0] == "scl" && segs[1] == "fi");
        if (!isFile) return new(ShareLinkKind.Direct, original, null, true);

        // Keep everything the link carries (rlkey is what makes a new-style link work) and force
        // dl=1, which is Dropbox's documented "give me the file" switch.
        var query = ParseQuery(uri.Query).Where(q => q.Key != "dl").ToList();
        query.Add(new KeyValuePair<string, string>("dl", "1"));
        var url = uri.GetLeftPart(UriPartial.Path) + "?" + string.Join("&",
            query.Select(q => Uri.EscapeDataString(q.Key) + "=" + Uri.EscapeDataString(q.Value)));
        return new(ShareLinkKind.Converted, url, null, true);
    }

    /// <summary>Drops a 40-hex revision right after <c>/raw/</c>, so the link follows the gist's latest.</summary>
    private static string UnpinGist(string user, string id, string[] afterRaw)
    {
        var rest = afterRaw.Length > 0 && Hex40Regex.IsMatch(afterRaw[0]) ? afterRaw.Skip(1) : afterRaw;
        var tail = string.Join('/', rest);
        return $"https://gist.githubusercontent.com/{user}/{id}/raw" + (tail.Length > 0 ? "/" + tail : "");
    }

    private static bool HostIs(string host, string domain) =>
        host == domain || host.EndsWith("." + domain, StringComparison.Ordinal);

    private static List<KeyValuePair<string, string>> ParseQuery(string query)
    {
        var result = new List<KeyValuePair<string, string>>();
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            var key = Uri.UnescapeDataString(eq >= 0 ? part[..eq] : part);
            // '+' is left as it is: re-emitted, it must still mean what the service meant by it.
            var value = eq >= 0 ? Uri.UnescapeDataString(part[(eq + 1)..]) : "";
            result.Add(new KeyValuePair<string, string>(key, value));
        }
        return result;
    }

    private static string? Attribute(string tag, string name)
    {
        var m = Regex.Match(tag, @"\b" + name + @"\s*=\s*(?:""([^""]*)""|'([^']*)')",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!m.Success) return null;
        return WebUtility.HtmlDecode(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
    }

    private static ShareLinkResult Rejected(string reasonKey) => new(ShareLinkKind.Rejected, "", reasonKey, false);
}
