using System;
using System.Diagnostics;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Single gate for opening a URL that the launcher did NOT author — a catalog
/// <c>mod.json</c> field, a hand-edited <c>launcher-config.json</c>, a built-in
/// profile. Every such string used to reach
/// <c>Process.Start(FileName = url, UseShellExecute = true)</c> directly, and with
/// <c>UseShellExecute</c> the shell happily runs whatever it is handed: a
/// <c>file:///</c> URI, a UNC path, an <c>.exe</c>. The catalog schema's
/// <c>^https?://</c> pattern only guards the CI — built-in profiles never pass
/// through it and a config file is user-writable — so validation has to happen
/// here, at open time, or it doesn't happen at all.
///
/// Pure + WPF-free so it unit-tests off the UI thread (same shape as
/// <see cref="PathDisplay"/>).
/// </summary>
internal static class SafeUrl
{
    /// <summary>
    /// True when <paramref name="url"/> is safe to hand to the shell: an absolute
    /// http/https URI with a real host and no embedded credentials.
    /// </summary>
    /// <remarks>
    /// The <c>UserInfo</c> check blocks the classic <c>https://real-site.com@evil/</c>
    /// display trick — the browser navigates to <c>evil</c> while the string reads as
    /// the real site.
    /// </remarks>
    public static bool IsAllowed(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        if (!string.IsNullOrEmpty(uri.UserInfo)) return false;
        return !string.IsNullOrEmpty(uri.Host);
    }

    /// <summary>
    /// Host of an allowed URL, for showing the user where a link actually goes.
    /// Empty when the url is rejected by <see cref="IsAllowed"/>.
    /// </summary>
    public static string HostOf(string? url)
    {
        if (!IsAllowed(url)) return "";
        return new Uri(url!.Trim()).Host;
    }

    /// <summary>
    /// A url as it should READ in a narrow place: the site, and the part of the path that says
    /// WHICH thing it is — never the scheme, the www or a query nobody can parse by eye.
    ///
    /// <para>Written for the mod window's rail footer, where the alternative was the raw
    /// <c>mod.json</c> string. It is a readability helper and nothing more: the geometry is
    /// guaranteed by the ceiling on the footer itself (<c>SetModRailTextWidth</c>), because a
    /// helper that shortens by CHARACTERS cannot promise pixels — the type scale moves and the
    /// glyph widths with it.</para>
    ///
    /// <para><b>Whole segments, never half a word.</b> The path is elided between the host and
    /// the LAST segment, which is the one that identifies the thing — the same reasoning
    /// <see cref="PathDisplay.SplitForDisplay"/> is built on. And when even that does not fit,
    /// it falls back to the bare host rather than stacking a second ellipsis onto the first:
    /// measured in the rail, <c>moddb.com/…/knights-and-barbarians</c> wants 190 px of the 181
    /// there are, and a line reading <c>moddb.com/…/knights-and-barbaria…</c> is worse than one
    /// reading <c>moddb.com</c>.</para>
    ///
    /// <para>A url this class would refuse comes back UNCHANGED rather than empty. Most of
    /// those are harmless — a site written without its scheme does not parse as absolute — and
    /// blanking the line would hide the one thing the mod author did supply. It is display
    /// text, never something handed to the shell; that still goes through
    /// <see cref="TryOpen"/>.</para>
    /// </summary>
    /// <param name="maxChars">Budget in characters. The default is what fits the rail at the
    /// reference text size: 181 px at Consolas 10.5 measures 5.6 px a glyph.</param>
    public static string CompactForDisplay(string? url, int maxChars = 32)
    {
        var raw = (url ?? "").Trim();
        if (!IsAllowed(raw)) return raw;

        var uri = new Uri(raw);

        var host = uri.Host;
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            host = host[4..];

        var path = uri.AbsolutePath.Trim('/');
        if (path.Length == 0) return host;

        var full = host + "/" + path;
        if (full.Length <= maxChars) return full;

        var lastSlash = path.LastIndexOf('/');
        var leaf = lastSlash >= 0 ? path[(lastSlash + 1)..] : path;
        var elided = host + "/…/" + leaf;

        return elided.Length <= maxChars ? elided : host;
    }

    /// <summary>
    /// Validates and opens in the user's default browser. Never throws: a rejected
    /// or unopenable url is logged and reported as <c>false</c>, because failing to
    /// open a link must never take the launcher down.
    /// </summary>
    public static bool TryOpen(string? url)
    {
        if (!IsAllowed(url))
        {
            DiagnosticLog.Write($"SafeUrl: refused to open non-http(s) url '{url}'");
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url!.Trim(),
                UseShellExecute = true,
            });
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"SafeUrl: open failed for '{url}': {ex.Message}");
            return false;
        }
    }
}
