using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// What a release's notes say, ready to draw (design 77a/77b). A release body is Markdown in two
/// languages under a quote holding the download link, and showing it as written gave the player
/// the asterisks, the link and the other language. So: only the player's language, only the basics
/// (<c>#</c>, <c>-</c>, <c>**</c>), and no quote. A body that is nothing but an address, which is
/// what most releases carry, is reported as that so the dialog can offer a button instead.
/// </summary>
public static class ReleaseNotes
{
    public sealed record Piece(string Text, bool Bold);

    public sealed record Item(IReadOnlyList<Piece> Pieces, int Level, bool Bullet);

    public sealed record Section(string Heading, IReadOnlyList<Item> Items);

    public sealed record View(IReadOnlyList<Section> Sections, string? OnlyLink)
    {
        public bool IsEmpty => Sections.Count == 0 && OnlyLink == null;
    }

    private static readonly Regex Heading = new(@"^(#{1,6})\s+(.+?)\s*#*$", RegexOptions.Compiled);
    private static readonly Regex Bullet = new(@"^(\s*)[-*+]\s+(.+)$", RegexOptions.Compiled);
    private static readonly Regex Link = new(@"\[([^\]]+)\]\([^)]*\)", RegexOptions.Compiled);
    private static readonly Regex Italic = new(@"(?<![\*\w])\*(?![\s\*])([^*]+?)\*(?![\*\w])", RegexOptions.Compiled);

    public static View Parse(string? body, bool spanish)
    {
        var text = (body ?? "").Replace("\r\n", "\n").Trim();
        if (text.Length == 0) return new View(Array.Empty<Section>(), null);
        if (TheOnlyLink(text) is { } link) return new View(Array.Empty<Section>(), link);

        var sections = new List<Section>();
        var heading = "";
        var items = new List<Item>();
        StringBuilder? open = null;
        var level = 0;
        var bullet = false;

        void CloseItem()
        {
            if (open != null) items.Add(new Item(Pieces(open.ToString()), level, bullet));
            open = null;
        }
        void CloseSection()
        {
            CloseItem();
            if (items.Count > 0) sections.Add(new Section(heading, items));
            items = new List<Item>();
        }

        foreach (var line in LinesFor(text, spanish))
        {
            var t = line.Trim();
            if (t.Length == 0 || t[0] == '>')
            {
                CloseItem();
                continue;
            }

            var h = Heading.Match(t);
            if (h.Success)
            {
                CloseSection();
                // The h1 is the release's title, which the dialog already says with its chips.
                heading = h.Groups[1].Length == 1 ? "" : Plain(h.Groups[2].Value);
                continue;
            }

            var b = Bullet.Match(line);
            if (b.Success)
            {
                CloseItem();
                open = new StringBuilder(b.Groups[2].Value.Trim());
                level = b.Groups[1].Length >= 2 ? 1 : 0;
                bullet = true;
                continue;
            }

            if (open != null)
            {
                open.Append(' ').Append(t);
                continue;
            }

            open = new StringBuilder(t);
            level = 0;
            bullet = false;
        }
        CloseSection();
        return new View(sections, null);
    }

    /// <summary>The player's half: in Spanish from the title ending in "(Español)", otherwise up
    /// to the first rule. A note with one language has no such title and no rule, and is shown
    /// whole.</summary>
    private static IEnumerable<string> LinesFor(string text, bool spanish)
    {
        var lines = text.Split('\n');
        var start = 0;
        if (spanish)
        {
            var title = Array.FindIndex(lines, l => l.TrimStart().StartsWith('#')
                && l.TrimEnd().EndsWith("(Español)", StringComparison.OrdinalIgnoreCase));
            if (title >= 0) start = title + 1;
        }
        return lines.Skip(start).TakeWhile(l => l.Trim() != "---");
    }

    private static string? TheOnlyLink(string text)
    {
        var parts = LinkedText.Split(text);
        var links = parts.Where(p => p.IsLink).ToList();
        return links.Count == 1 && parts.Where(p => !p.IsLink).All(p => string.IsNullOrWhiteSpace(p.Text))
            ? links[0].Text
            : null;
    }

    /// <summary>A line cut at its <c>**</c> marks. An unmatched mark is left out rather than shown:
    /// a stray asterisk says nothing a player can use.</summary>
    private static IReadOnlyList<Piece> Pieces(string line)
    {
        var parts = line.Split("**");
        var pieces = new List<Piece>();
        for (var i = 0; i < parts.Length; i++)
        {
            var text = Plain(parts[i]);
            if (text.Length == 0) continue;
            pieces.Add(new Piece(text, i % 2 == 1 && i < parts.Length - 1));
        }
        return pieces;
    }

    private static string Plain(string text)
    {
        text = Link.Replace(text, "$1");
        text = Italic.Replace(text, "$1");
        return text.Replace("`", "");
    }
}
