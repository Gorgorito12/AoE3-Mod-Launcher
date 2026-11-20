using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using WarsOfLibertyLauncher.Controls;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// The testbed's layout probe (harness only): walks every window on screen and reports text a
/// player could not read in full. A cut text and a text outside its panel throw nothing, build
/// clean and look normal to UI Automation; the only other way to find one was to look at every
/// capture, which misses things. It REPORTS — every hit is then looked at in a capture before it
/// counts as a defect.
///
/// <para>What it reports, one line each:</para>
/// <list type="bullet">
/// <item><c>CUT</c> — a one-line text narrower than its text with no ellipsis: the end is lost with
/// no sign that anything is missing.</item>
/// <item><c>OUTSIDE</c> — text drawn past the edge of a panel that clips it (its layout slot, a
/// <c>ClipToBounds</c> or a <c>Clip</c>); a scroll viewer's own clipping is left alone, since
/// scrolled text is where it should be.</item>
/// <item><c>OVERLAP</c> / <c>TOUCH</c> — two texts on the same line drawn over each other, or
/// closer than the house's 8 px.</item>
/// <item><c>TRIM</c> — a text that ends in an ellipsis right now (by design in many places; a
/// count, and the list, so a new one can be noticed).</item>
/// </list>
///
/// <para>Asked for through a file: the testbed writes <see cref="RequestFile"/> in the data folder
/// and reads the answer back from the path inside it. Polled once a second, harness only, so a
/// player's launcher has no timer and no file to read.</para>
/// </summary>
internal static class LayoutAudit
{
    internal const string RequestFile = "layout-audit.request";
    internal const double TouchGap = 8;

    private static DispatcherTimer? s_poll;

    /// <summary>Starts the request poll. Harness only.</summary>
    internal static void StartIfHarness(Dispatcher dispatcher)
    {
        if (!HarnessMode.Active || s_poll != null) return;
        s_poll = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Poll(), dispatcher);
        s_poll.Start();
    }

    private static void Poll()
    {
        var request = Path.Combine(AppPaths.DataDir, RequestFile);
        if (!File.Exists(request)) return;
        string target;
        try
        {
            target = File.ReadAllText(request).Trim();
            File.Delete(request);
        }
        catch { return; }
        if (string.IsNullOrEmpty(target)) target = Path.Combine(AppPaths.DataDir, "layout-audit.txt");
        // "memory|<file>": the leak loops' reading, after a full collection, so a number that
        // grows between two readings is memory something still holds.
        if (target.StartsWith("memory|", StringComparison.Ordinal))
        {
            target = target["memory|".Length..];
            try { File.WriteAllText(target, MemoryReading(), Encoding.UTF8); } catch { }
            return;
        }
        try
        {
            var report = Run(Application.Current.Windows.OfType<Window>());
            File.WriteAllText(target, report, Encoding.UTF8);
            DiagnosticLog.Write($"LAYOUT AUDIT  written to {target}");
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(target, "ERROR " + ex); } catch { }
        }
    }

    /// <summary>Managed heap after a full collection, open windows, handles and threads, as JSON.</summary>
    private static string MemoryReading()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        using var p = System.Diagnostics.Process.GetCurrentProcess();
        var gdi = GetGuiResources(p.Handle, 0);
        var user = GetGuiResources(p.Handle, 1);
        var windows = Application.Current?.Windows.Count ?? 0;
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            managed_mb = Math.Round(GC.GetTotalMemory(false) / 1048576.0, 2),
            private_mb = Math.Round(p.PrivateMemorySize64 / 1048576.0, 1),
            handles = p.HandleCount,
            gdi,
            user,
            threads = p.Threads.Count,
            windows,
        });
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetGuiResources(IntPtr process, uint flags);

    internal sealed record Finding(string Kind, string Window, string Where, string Text, string Detail);

    /// <summary>The whole report for these windows, as text.</summary>
    internal static string Run(IEnumerable<Window> windows)
    {
        var sb = new StringBuilder();
        foreach (var w in windows.Where(w => w.IsVisible && w.ActualWidth > 0))
        {
            var findings = Audit(w, w.Title);
            var title = string.IsNullOrWhiteSpace(w.Title) ? w.GetType().Name : w.Title;
            sb.AppendLine($"WINDOW {w.GetType().Name} \"{title}\" {w.ActualWidth:0}x{w.ActualHeight:0}  " +
                          $"{findings.Count(f => f.Kind is not ("TRIM" or "NEAR"))} problems, {findings.Count(f => f.Kind == "NEAR")} near, {findings.Count(f => f.Kind == "TRIM")} trimmed");
            foreach (var f in findings.OrderBy(f => f.Kind is "TRIM" or "NEAR" ? 1 : 0).ThenBy(f => f.Kind))
                sb.AppendLine($"  {f.Kind,-7} {f.Where} | \"{f.Text}\" | {f.Detail}");
        }
        return sb.ToString();
    }

    /// <summary>Every finding under <paramref name="window"/>: a window on screen, or (in a test) a
    /// tree that was measured and arranged but never shown, which is why "shown" is read from
    /// <c>Visibility</c> up the tree rather than from <c>IsVisible</c> there.</summary>
    internal static List<Finding> Audit(Visual window, string? title)
    {
        var onScreen = window is Window;
        title ??= "";
        var findings = new List<Finding>();
        var texts = new List<(TextBlock Tb, Rect Box, string Text)>();
        foreach (var tb in Descendants(window).OfType<TextBlock>())
        {
            if (!IsShown(tb, onScreen)) continue;
            var text = RevealText.PlainTextOf(tb);
            if (string.IsNullOrWhiteSpace(text)) continue;
            Rect slot;
            try { slot = tb.TransformToAncestor(window).TransformBounds(new Rect(tb.RenderSize)); }
            catch { continue; }
            if (slot.Width < 1 || slot.Height < 1) continue;
            var where = PathOf(tb);
            var shortText = text.Length > 60 ? text[..57] + "…" : text;
            var content = SafeContentWidth(tb);
            var oneLine = tb.TextWrapping == TextWrapping.NoWrap;
            // What the block really shows. A block given less room than its text is ARRANGED at
            // the text's own width (WPF never lays an element out below its unclipped desired
            // size) and then clipped by a layout clip — so ActualWidth reads the full text and
            // only the clip says where it stops.
            var shown = tb.ActualWidth;
            try
            {
                if (LayoutInformation.GetLayoutClip(tb) is { } own && !own.IsEmpty())
                    shown = Math.Min(shown, own.Bounds.Width);
            }
            catch { }

            // The text's own box: as wide as the text, aligned as the block aligns it.
            // The slot is in WINDOW units and the text's own widths are in the block's: a surface
            // scaled down to fit a small window (UiScale's LayoutTransform) makes them differ, so
            // the text's drawn width is carried across by the same factor.
            var scale = tb.RenderSize.Width > 0 ? slot.Width / tb.RenderSize.Width : 1;
            var drawn = Math.Min(content, oneLine ? shown : content) * scale;
            var left = tb.TextAlignment switch
            {
                TextAlignment.Right => slot.Right - drawn,
                TextAlignment.Center => slot.Left + (slot.Width - drawn) / 2,
                _ => slot.Left,
            };
            // A wrapping block whose text fits on one line draws one line: as wide as the text.
            var singleLine = oneLine || content <= shown + 1;
            var box = singleLine ? new Rect(left, slot.Top, Math.Max(0, drawn), slot.Height) : slot;

            if (oneLine && content > shown + 1)
                findings.Add(new Finding(tb.TextTrimming == TextTrimming.None ? "CUT" : "TRIM", title, where, shortText,
                    $"needs {content:0.#} px, has {shown:0.#}"));

            var region = VisibleRegion(tb, window, forOverflow: true);
            var seenRegion = VisibleRegion(tb, window, forOverflow: false);
            if (seenRegion is { } o && Rect.Intersect(o, box) is var seenNow && (seenNow.IsEmpty || seenNow.Width < 2 || seenNow.Height < 2)) continue;
            // Entirely clipped away — or given a slot with no height or width, which is how the
            // fitting panels hide what does not fit — so not on screen and not a reading problem.
            if (region is { } r0 && (r0.IsEmpty || r0.Width < 2 || r0.Height < 2)) continue;
            if (region is { } r1 && Rect.Intersect(r1, box) is var seenPart && (seenPart.IsEmpty || seenPart.Width < 2 || seenPart.Height < 2)) continue;
            if (region is { } visible)
            {
                var outL = visible.Left - box.Left;
                var outR = box.Right - visible.Right;
                var outT = visible.Top - box.Top;
                var outB = box.Bottom - visible.Bottom;
                var worst = Math.Max(Math.Max(outL, outR), Math.Max(outT, outB));
                // Vertical overflow of a few pixels is the font's own ascent/descent padding.
                if (outL > 1 || outR > 1 || outT > 3 || outB > 3)
                    findings.Add(new Finding("OUTSIDE", title, where, shortText,
                        $"{worst:0.#} px past {(outR > 1 ? "the right" : outL > 1 ? "the left" : outB > 3 ? "the bottom" : "the top")} edge of its panel"));
            }
            // Only the part on screen can collide with anything.
            texts.Add((tb, seenRegion is { } v ? Rect.Intersect(box, v) : box, shortText));
        }

        // Two texts on one line: drawn over each other, or closer than the house's 8 px.
        for (var i = 0; i < texts.Count; i++)
        for (var j = i + 1; j < texts.Count; j++)
        {
            var (a, ab, at) = texts[i];
            var (b, bb, bt) = texts[j];
            if (IsAncestorText(a, b) || IsAncestorText(b, a)) continue;
            // A glyph beside its label (a chevron, an arrow, an icon font) is not two texts, and
            // the same words drawn twice in one place are a shadow or a glow.
            if (IsGlyph(a) || IsGlyph(b) || at == bt) continue;
            // One sentence set in pieces (a path's root, middle and leaf) touches by design; and
            // a toast or an alert card is drawn OVER the page on purpose.
            if (ReferenceEquals(VisualTreeHelper.GetParent(a), VisualTreeHelper.GetParent(b)) && Math.Abs(ab.Left <= bb.Left ? bb.Left - ab.Right : ab.Left - bb.Right) <= 0.5) continue;
            if (InOverlay(a) != InOverlay(b)) continue;
            // The later of the two is drawn on top; when it sits on a painted card the earlier
            // one is hidden BEHIND that card (an alert over the page), not written over it.
            var vertical = Math.Min(ab.Bottom, bb.Bottom) - Math.Max(ab.Top, bb.Top);
            if (vertical < Math.Min(ab.Height, bb.Height) * 0.5) continue;
            var gap = ab.Left <= bb.Left ? bb.Left - ab.Right : ab.Left - bb.Right;
            if (gap >= TouchGap) continue;
            if (gap < 0 && SitsOnACardOver(b, a, Rect.Intersect(ab, bb), window)) continue;
            // Under 4 px the two read as one; 4 to 8 is short of the house rule but legible, and
            // listed apart so it does not bury the first kind.
            var kind = gap < -1 ? "OVERLAP" : gap < 4 ? "TOUCH" : "NEAR";
            findings.Add(new Finding(kind, title, PathOf(a) + "  <->  " + PathOf(b), at + "\" / \"" + bt,
                gap < 0 ? $"{-gap:0.#} px over each other" : $"{gap:0.#} px apart"));
        }
        return findings;
    }

    private static bool SitsOnACardOver(DependencyObject top, DependencyObject below, Rect overlap, Visual window)
    {
        if (overlap.IsEmpty) return false;
        var belowChain = new HashSet<DependencyObject>();
        for (var d = below; d != null; d = VisualTreeHelper.GetParent(d)) belowChain.Add(d);
        for (var d = VisualTreeHelper.GetParent(top); d != null && !belowChain.Contains(d); d = VisualTreeHelper.GetParent(d))
        {
            var fill = d switch { Border bd => bd.Background, Panel p => p.Background, Control c => c.Background, _ => null };
            if (fill is SolidColorBrush { Color.A: >= 200 } || fill is GradientBrush)
            {
                try
                {
                    var box = ((Visual)d).TransformToAncestor(window).TransformBounds(new Rect(((UIElement)d).RenderSize));
                    if (box.Contains(overlap)) return true;
                }
                catch { }
            }
        }
        return false;
    }

    private static bool InOverlay(DependencyObject e)
    {
        for (var d = e; d != null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is FrameworkElement fe
                && (fe.Name.Contains("Overlay", StringComparison.Ordinal) || fe.Name == "ToastHost"
                    || fe.GetType().Name.Contains("Overlay", StringComparison.Ordinal)
                    || fe.GetType().Name.Contains("Toast", StringComparison.Ordinal)))
                return true;
        }
        return false;
    }

    private static bool IsGlyph(TextBlock tb)
    {
        var family = tb.FontFamily?.Source ?? "";
        if (family.Contains("MDL2", StringComparison.OrdinalIgnoreCase)
            || family.Contains("Fluent", StringComparison.OrdinalIgnoreCase)
            || family.Contains("Symbol", StringComparison.OrdinalIgnoreCase)
            || family.Contains("Emoji", StringComparison.OrdinalIgnoreCase)) return true;
        var text = RevealText.PlainTextOf(tb).Trim();
        return text.Length <= 1 && !char.IsLetterOrDigit(text.FirstOrDefault());
    }

    private static bool IsAncestorText(DependencyObject a, DependencyObject b)
    {
        for (var d = VisualTreeHelper.GetParent(b); d != null; d = VisualTreeHelper.GetParent(d))
            if (ReferenceEquals(d, a)) return true;
        return false;
    }

    private static double SafeContentWidth(TextBlock tb)
    {
        try { return RevealText.MeasureContentWidth(tb); }
        catch { return tb.ActualWidth; }
    }

    /// <summary>On screen: visible all the way up, not transparent, inside a window.</summary>
    private static bool IsShown(FrameworkElement e, bool onScreen)
    {
        if (onScreen && !e.IsVisible) return false;
        for (DependencyObject? d = e; d != null; d = VisualTreeHelper.GetParent(d))
            if (d is UIElement u && (u.Opacity <= 0.01 || u.Visibility != Visibility.Visible)) return false;
        return true;
    }

    /// <summary>
    /// The part of the window where this text can be seen: the intersection of every clipping
    /// ancestor's box (layout clip, ClipToBounds, Clip), in window coordinates. A scroll viewer's
    /// presenter ends the walk — scrolled-away text is where it should be. Null when nothing clips.
    /// </summary>
    /// <param name="forOverflow">True: the region a text may not run past — a scroll viewer ends
    /// the vertical part (text scrolled out of view is where it belongs) and, when it scrolls
    /// sideways, all of it. False: what is actually on screen, every clip included.</param>
    private static Rect? VisibleRegion(FrameworkElement e, Visual window, bool forOverflow)
    {
        Rect? region = null;
        var sidesOnly = false;
        for (var d = VisualTreeHelper.GetParent(e); d != null && !ReferenceEquals(d, window); d = VisualTreeHelper.GetParent(d))
        {
            if (forOverflow && d is ScrollContentPresenter scp)
            {
                if (scp.CanHorizontallyScroll) break;
                sidesOnly = true;
            }
            if (d is not FrameworkElement fe || fe.ActualWidth <= 0) continue;
            Geometry? clip = null;
            try { clip = LayoutInformation.GetLayoutClip(fe); } catch { }
            if (clip == null && fe.ClipToBounds) clip = new RectangleGeometry(new Rect(fe.RenderSize));
            if (clip == null && fe.Clip != null) clip = fe.Clip;
            if (clip == null || clip.IsEmpty()) continue;
            Rect box;
            try { box = fe.TransformToAncestor(window).TransformBounds(clip.Bounds); }
            catch { continue; }
            if (sidesOnly) box = new Rect(box.X, -1e6, box.Width, 2e6);
            region = region is { } r ? Rect.Intersect(r, box) : box;
            if (region.Value.IsEmpty) return Rect.Empty;
        }
        return region;
    }

    private static string PathOf(FrameworkElement e)
    {
        var parts = new List<string>();
        for (DependencyObject? d = e; d != null && parts.Count < 4; d = VisualTreeHelper.GetParent(d))
        {
            if (d is not FrameworkElement fe) continue;
            var id = AutomationProperties.GetAutomationId(fe);
            var name = !string.IsNullOrEmpty(fe.Name) ? fe.Name : !string.IsNullOrEmpty(id) ? id
                : fe.Tag is string tag && !string.IsNullOrEmpty(tag) ? "[" + tag + "]" : null;
            if (name != null) parts.Add(name);
            else if (ReferenceEquals(fe, e)) parts.Add(fe.GetType().Name);
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var d = stack.Pop();
            yield return d;
            int n;
            try { n = VisualTreeHelper.GetChildrenCount(d); } catch { continue; }
            for (var i = n - 1; i >= 0; i--) stack.Push(VisualTreeHelper.GetChild(d, i));
        }
    }
}
