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

    /// <summary>
    /// Splits a path into the three pieces the install dialog paints: the ROOT, an elided
    /// MIDDLE, and the LAST FOLDER. The root and the last folder are never dropped.
    ///
    /// <para><b>Why this is not <see cref="CompactPathMiddle"/>.</b> That one returns a
    /// single string, and the install dialog needs the three pieces separately because it
    /// colours them differently — the root and the middle dim, the folder you are actually
    /// choosing bright. Which is the point: a plain TextBox loses the END of the source
    /// path ("…Age Of Emp") and the START of the destination ("…\Knights and Barbarians",
    /// no drive letter), so between them a window whose one job is to confirm two paths
    /// confirms neither.</para>
    ///
    /// <para>The middle keeps as many TRAILING segments as fit, because the folders nearest
    /// the leaf are the ones that identify it; anything dropped is replaced by an ellipsis.
    /// At least one middle segment always survives, so the result never reads as a bare
    /// "C:\ … \Name" when one more word would have fitted.</para>
    /// </summary>
    public static (string Root, string Middle, string Leaf) SplitForDisplay(
        string? path, int maxMiddleChars = 26)
    {
        var raw = (path ?? "").Trim();
        if (raw.Length == 0) return ("", "", "");
        raw = raw.Replace('/', '\\');

        string root;
        try { root = Path.GetPathRoot(raw) ?? ""; }
        catch { root = ""; }

        var rest = raw.Substring(Math.Min(root.Length, raw.Length))
                      .Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (rest.Length == 0) return (root, "", "");

        var leaf = rest[^1];
        if (rest.Length == 1) return (root, "", leaf);

        var mid = rest[..^1];
        var kept = new List<string>();
        int budget = Math.Max(1, maxMiddleChars);
        for (int i = mid.Length - 1; i >= 0; i--)
        {
            int cost = mid[i].Length + 1;          // the segment plus its separator
            if (kept.Count > 0 && cost > budget) break;
            budget -= cost;
            kept.Insert(0, mid[i]);
        }

        var middle = string.Join("\\", kept) + "\\";
        if (kept.Count < mid.Length) middle = " … \\" + middle;
        return (root, middle, leaf);
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
