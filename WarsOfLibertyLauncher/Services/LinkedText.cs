using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Splits a block of plain text into runs of prose and runs that are links.
///
/// <para><b>Why this exists.</b> The self-update dialog shows a GitHub release's body, and the
/// maintainer's releases are a single bare URL pointing at the real notes — so the "What's new"
/// panel rendered a dead address and nothing else. The text is remote, which is exactly why the
/// caller opens the result through <see cref="SafeUrl"/> rather than handing it to the shell.</para>
///
/// <para>Pure and WPF-free so the splitting can be pinned by tests. The cases that matter are the
/// REFUSALS: a body with no URL has to come out byte-for-byte as one prose segment (that is every
/// release note ever written before this), a scheme the launcher will not open must stay prose,
/// and a link at the end of a sentence must not swallow the full stop.</para>
/// </summary>
public static class LinkedText
{
    /// <summary>One piece of the text: prose, or a link.</summary>
    public sealed record Segment(string Text, bool IsLink);

    /// <summary>
    /// Only http(s), because that is the whole set <see cref="SafeUrl.IsAllowed"/> will open —
    /// marking anything else as a link would produce something that looks clickable and is not.
    ///
    /// <para>The trailing class excludes the characters that end an English or Spanish sentence.
    /// A URL may legitimately contain <c>.</c> or <c>)</c>, so they are allowed INSIDE and only
    /// trimmed from the very end, which is what <see cref="TrimTrailingPunctuation"/> does.</para>
    /// </summary>
    private static readonly Regex UrlPattern = new(
        @"https?://[^\s<>""']+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The text as alternating prose and link segments, in order. A null or empty input yields
    /// nothing; text with no link yields exactly one prose segment.
    /// </summary>
    public static IReadOnlyList<Segment> Split(string? text)
    {
        var segments = new List<Segment>();
        if (string.IsNullOrEmpty(text)) return segments;

        var at = 0;
        foreach (Match m in UrlPattern.Matches(text))
        {
            var url = TrimTrailingPunctuation(m.Value);

            // Refused by SafeUrl — leave it as prose rather than offering a link that would do
            // nothing when clicked. The pattern already limits the scheme, so this is the
            // second gate (a bare host, credentials in the authority) rather than the first.
            if (!SafeUrl.IsAllowed(url)) continue;

            if (m.Index > at) segments.Add(new Segment(text[at..m.Index], false));
            segments.Add(new Segment(url, true));
            at = m.Index + url.Length;
        }

        if (at < text.Length) segments.Add(new Segment(text[at..], false));
        return segments;
    }

    /// <summary>
    /// Drops punctuation that belongs to the sentence rather than to the address.
    ///
    /// <para>A closing bracket is only dropped when it is unbalanced, so a real URL that carries
    /// brackets — Wikipedia-style — survives, while "(see https://example.com/a)" does not eat
    /// the bracket that opened outside it.</para>
    /// </summary>
    internal static string TrimTrailingPunctuation(string url)
    {
        while (url.Length > 0)
        {
            var last = url[^1];
            if (last is '.' or ',' or ';' or ':' or '!' or '?' or '"' or '\'')
            {
                url = url[..^1];
                continue;
            }
            if (last == ')' && CountOf(url, '(') < CountOf(url, ')'))
            {
                url = url[..^1];
                continue;
            }
            break;
        }
        return url;
    }

    private static int CountOf(string s, char c)
    {
        var n = 0;
        foreach (var ch in s) if (ch == c) n++;
        return n;
    }
}
