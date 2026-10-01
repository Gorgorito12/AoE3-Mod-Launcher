using System;
using System.Text.RegularExpressions;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// A place translations come from: a GitHub repository (<c>owner/repo</c>, read through its
/// <c>translations/&lt;id&gt;/&lt;version&gt;/</c> folders) or a <c>translations-index.json</c>
/// at any https address — Google Drive, Dropbox, a gist, a translator's own website.
///
/// <para><see cref="TryParse"/> is the ONE door every source comes through: the Language tab's
/// "Add" box, the Settings list and the <c>wol-launcher://add-source</c> link. Three copies of the
/// validation would drift, and the first to drift would be the one that accepts what the others
/// refuse.</para>
///
/// <para><see cref="Key"/> is the source's identity, so the same source pasted two different
/// ways (<c>Owner/Repo</c> and <c>owner/repo</c>, or Drive's <c>/file/d/…/view</c> and
/// <c>open?id=…</c>) is recognised as already added.</para>
/// </summary>
public sealed class TranslationSourceRef
{
    public const int MaxInputLength = 2048;

    private static readonly Regex RepoRegex = new(
        @"^[A-Za-z0-9][A-Za-z0-9._-]{0,99}/[A-Za-z0-9._-]{1,100}\z", RegexOptions.CultureInvariant);

    public TranslationSourceKind Kind { get; }

    /// <summary><c>owner/repo</c>, or the https address exactly as the player gave it.</summary>
    public string Location { get; }

    private TranslationSourceRef(TranslationSourceKind kind, string location)
    {
        Kind = kind;
        Location = location;
    }

    /// <summary>A GitHub repository's translation folders. The caller has already validated it.</summary>
    public static TranslationSourceRef Repo(string ownerRepo) =>
        new(TranslationSourceKind.GitHubFolder, ownerRepo.Trim());

    /// <summary>A GitHub repository's releases (the legacy publication path).</summary>
    public static TranslationSourceRef Releases(string ownerRepo) =>
        new(TranslationSourceKind.GitHubReleases, ownerRepo.Trim());

    /// <summary>An index file at an https address. The caller has already validated it.</summary>
    public static TranslationSourceRef IndexUrl(string url) =>
        new(TranslationSourceKind.Index, url.Trim());

    /// <summary>
    /// Stable identity. A repository compares case-insensitively, as GitHub does; an index is
    /// identified by the address it is actually DOWNLOADED from, so two share links to the same
    /// Drive file are one source.
    /// </summary>
    public string Key => Kind switch
    {
        TranslationSourceKind.GitHubFolder => "gh:" + Location.ToLowerInvariant(),
        TranslationSourceKind.GitHubReleases => "ghr:" + Location.ToLowerInvariant(),
        TranslationSourceKind.Index => "url:" + NormalizeUrl(ShareLinkResolver.Resolve(Location) is { Kind: not ShareLinkKind.Rejected } r
            ? r.Url : Location),
        _ => "local",
    };

    /// <summary>What a list shows: the repository, or a shortened address (the tooltip has the full one).</summary>
    public string DisplayLocation =>
        Kind == TranslationSourceKind.Index ? SafeUrl.CompactForDisplay(Location, 48) : Location;

    /// <summary>
    /// Validates what the player typed or pasted. On failure <paramref name="reasonKey"/> is a
    /// string-table key naming the problem (a Mega link, a Drive folder, plain http…), because
    /// "invalid source" tells nobody what to ask the translator for.
    /// </summary>
    public static bool TryParse(string? input, out TranslationSourceRef? source, out string reasonKey)
    {
        source = null;
        reasonKey = "";
        var s = (input ?? "").Trim();
        if (s.Length == 0) { reasonKey = "TxSrcErrEmpty"; return false; }
        if (s.Length > MaxInputLength) { reasonKey = "TxSrcErrTooLong"; return false; }
        foreach (var c in s)
            if (char.IsControl(c)) { reasonKey = "TxSrcErrInvalid"; return false; }

        if (!s.Contains("://", StringComparison.Ordinal))
        {
            if (!TryNormalizeRepo(s, out var repo)) { reasonKey = "TxSrcErrInvalid"; return false; }
            source = Repo(repo);
            return true;
        }

        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri)) { reasonKey = "TxSrcErrInvalid"; return false; }
        if (uri.Scheme == Uri.UriSchemeHttp) { reasonKey = "TxSrcErrNotHttps"; return false; }
        if (uri.Scheme != Uri.UriSchemeHttps || !SafeUrl.IsAllowed(s)) { reasonKey = "TxSrcErrInvalid"; return false; }

        var host = uri.IdnHost.ToLowerInvariant();
        if (host == "github.com" || host == "www.github.com")
        {
            var segs = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            // A repository page (optionally a branch view) is the repository's folders...
            if (segs.Length == 2 || (segs.Length >= 3 && segs[2] == "tree"))
            {
                if (!TryNormalizeRepo(segs[0] + "/" + segs[1], out var repo)) { reasonKey = "TxSrcErrInvalid"; return false; }
                source = Repo(repo);
                return true;
            }
            // ...while a file in it is an index file.
            if (segs.Length >= 5 && (segs[2] == "blob" || segs[2] == "raw"))
            {
                source = IndexUrl(s);
                return true;
            }
            reasonKey = "TxSrcErrGitHubForm";
            return false;
        }

        var resolved = ShareLinkResolver.Resolve(s);
        if (resolved.Kind == ShareLinkKind.Rejected)
        {
            reasonKey = resolved.ReasonKey ?? "TxSrcErrInvalid";
            return false;
        }
        source = IndexUrl(s);
        return true;
    }

    /// <summary>
    /// <c>owner/repo</c>, with a trailing <c>.git</c> dropped. <c>.</c> and <c>..</c> are refused
    /// as either part: they are path syntax, not names, and this string becomes part of a URL.
    /// </summary>
    internal static bool TryNormalizeRepo(string raw, out string repo)
    {
        repo = "";
        var s = raw.Trim().TrimEnd('/');
        if (s.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) s = s[..^4];
        if (!RepoRegex.IsMatch(s)) return false;
        var parts = s.Split('/');
        if (parts[1] == "." || parts[1] == "..") return false;
        repo = s;
        return true;
    }

    private static string NormalizeUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return url;
        var path = u.AbsolutePath.TrimEnd('/');
        return (u.Scheme + "://" + u.IdnHost).ToLowerInvariant() + path + u.Query;
    }

    public override string ToString() => $"{Kind}:{Location}";
}
