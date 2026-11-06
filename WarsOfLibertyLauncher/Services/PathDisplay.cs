using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Pure path → display-string helpers for the install-copy switcher (no WPF deps, so
/// they're unit-testable off the UI thread). Used by MainWindow's
/// <c>AppendInstallCopiesToModPopup</c> to disambiguate copies that share a folder name.
/// </summary>
internal static class PathDisplay
{
    /// <summary>
    /// Leaf name of a path's PARENT folder — the segment that distinguishes two copies
    /// sharing the same folder name under different parents. Empty at a drive root.
    /// </summary>
    public static string ParentFolderName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        try
        {
            var parent = Path.GetDirectoryName(path.TrimEnd('\\', '/'));
            return string.IsNullOrEmpty(parent) ? "" : Path.GetFileName(parent.TrimEnd('\\', '/'));
        }
        catch { return ""; }
    }

    /// <summary>
    /// A full path made to WRAP AT ITS SEPARATORS instead of at its spaces: a zero-width
    /// space (U+200B) after every <c>\</c> and <c>/</c>, and every space turned into a
    /// non-breaking one. Without it a wrapping TextBlock splits "Age Of / Empires 3" in the
    /// middle of a folder name. DISPLAY ONLY — never hand the result to the file system or
    /// the clipboard; the characters are invisible, so a copied path would look right and
    /// not resolve.
    /// </summary>
    public static string BreakAtSeparators(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        var sb = new System.Text.StringBuilder(path.Length + 16);
        foreach (var c in path)
        {
            if (c == ' ') { sb.Append(' '); continue; }
            sb.Append(c);
            if (c == '\\' || c == '/') sb.Append('​');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Shorten a path with an ellipsis in the MIDDLE, keeping a short head (drive/root)
    /// and a longer TAIL (the copy's own folder — the part that distinguishes it). WPF
    /// <c>TextTrimming</c> only trims the END, which would hide exactly that tail.
    /// A no-op when the path already fits <paramref name="maxChars"/>.
    /// </summary>
    public static string CompactPathMiddle(string? path, int maxChars = 52)
    {
        path ??= "";
        if (path.Length <= maxChars || maxChars < 8) return path;
        int head = Math.Max(6, maxChars / 3);
        int tail = maxChars - head - 1;   // room for the ellipsis
        return path.Substring(0, head) + "…" + path.Substring(path.Length - tail);
    }

    /// <summary>The same three pieces with the middle whole, for a line that trims it to the
    /// width it really has (<see cref="Controls.PathLinePanel"/>).</summary>
    public static (string Root, string Middle, string Leaf) SplitWhole(string? path)
    {
        var raw = (path ?? "").Trim().Replace('/', '\\');
        if (raw.Length == 0) return ("", "", "");

        string root;
        try { root = Path.GetPathRoot(raw) ?? ""; }
        catch { root = ""; }

        var rest = raw.Substring(Math.Min(root.Length, raw.Length)).TrimEnd('\\');
        var cut = rest.LastIndexOf('\\');
        return cut < 0 ? (root, "", rest) : (root, rest[..(cut + 1)], rest[(cut + 1)..]);
    }

    /// <summary>
    /// Make every install-copy label UNIQUE for display, in the same order. Two passes:
    /// (1) append the distinguishing parent folder to labels shared by more than one copy;
    /// (2) any label STILL shared (copies side-by-side under the same parent) gets a stable
    /// <c>#N</c> suffix by order — so the switcher never shows two identical rows.
    /// </summary>
    public static List<string> DisambiguateLabels(IReadOnlyList<(string Label, string Path)> items)
    {
        var labels = items.Select(x => x.Label ?? "").ToList();

        var collide = labels.GroupBy(l => l, StringComparer.OrdinalIgnoreCase)
                            .Where(g => g.Count() > 1).Select(g => g.Key)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < labels.Count; i++)
        {
            if (!collide.Contains(labels[i])) continue;
            var parent = ParentFolderName(items[i].Path);
            if (!string.IsNullOrEmpty(parent)) labels[i] = $"{labels[i]}  ·  {parent}";
        }

        var stillCollide = labels.GroupBy(l => l, StringComparer.OrdinalIgnoreCase)
                                .Where(g => g.Count() > 1).Select(g => g.Key)
                                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var counter = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < labels.Count; i++)
        {
            if (!stillCollide.Contains(labels[i])) continue;
            int n = counter.TryGetValue(labels[i], out var c) ? c + 1 : 1;
            counter[labels[i]] = n;
            labels[i] = $"{labels[i]}  #{n}";
        }
        return labels;
    }
}
