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
