using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// Hides the pictures embedded in a trimmed line that fall past its ellipsis.
///
/// <para><b>Why it exists.</b> <c>TextTrimming</c> cuts a line's RUNS, not the elements
/// embedded in it with an <c>InlineUIContainer</c>, and WPF goes on drawing those after the
/// "…". A community match row puts each player's flag before the name, so a four-player match
/// that did not fit read "Kaise…" followed by the NEXT player's flag — a flag with no name,
/// beside a name it does not belong to (reported with a screenshot). The cut has to take the
/// pictures with it.</para>
///
/// <para><b>The decision is made on NOMINAL widths</b> — every picture at its declared size,
/// whether it is currently shown or not. Hiding a picture frees room on the line, and a rule
/// that read the line as it is drawn now would see that room, show the picture again, and
/// flip back and forth on every layout pass.</para>
/// </summary>
internal static class InlineFlagFit
{
    /// <summary>
    /// Which items stay visible, in line order. Text items are always reported visible; the
    /// question is only ever asked of pictures (<c>IsObject</c>).
    ///
    /// <para>When everything fits, everything is shown. Otherwise a picture is shown only if
    /// it ENDS before the ellipsis — one that straddles the cut is hidden, because half a flag
    /// beside "…" says nothing and reads as damage.</para>
    /// </summary>
    public static bool[] VisibleObjects(IReadOnlyList<(bool IsObject, double Width)> items,
        double available, double ellipsisWidth)
    {
        var shown = new bool[items.Count];
        for (var i = 0; i < shown.Length; i++) shown[i] = true;
        if (!(available > 0) || double.IsInfinity(available)) return shown;

        double total = 0;
        foreach (var item in items) total += Math.Max(0, item.Width);
        if (!RevealText.Overflows(total, available)) return shown;

        var cut = available - Math.Max(0, ellipsisWidth);
        double x = 0;
        for (var i = 0; i < items.Count; i++)
        {
            var width = Math.Max(0, items[i].Width);
            if (items[i].IsObject) shown[i] = x + width <= cut + RevealText.OverflowSlack;
            x += width;
        }
        return shown;
    }

    /// <summary>
    /// Measures <paramref name="tb"/>'s runs and pictures and shows or hides each picture by
    /// <see cref="VisibleObjects"/>. Writes a picture's visibility only when it changes, so a
    /// line that is laid out again with the same width does nothing.
    /// </summary>
    public static void Apply(TextBlock tb)
    {
        if (tb.TextTrimming == TextTrimming.None || tb.Inlines.Count == 0) return;
        var available = tb.ActualWidth - tb.Padding.Left - tb.Padding.Right;
        if (!(available > 0)) return;

        var dpi = VisualTreeHelper.GetDpi(tb).PixelsPerDip;
        var items = new List<(bool, double)>();
        var pictures = new List<FrameworkElement?>();
        foreach (var inline in tb.Inlines)
        {
            switch (inline)
            {
                case InlineUIContainer { Child: FrameworkElement picture }:
                    items.Add((true, RevealText.NominalWidth(picture)));
                    pictures.Add(picture);
                    break;
                case Run run:
                    items.Add((false, RevealText.MeasureOne(run.Text, run.FontFamily, run.FontStyle,
                        run.FontWeight, run.FontStretch, run.FontSize, tb.FlowDirection, dpi)));
                    pictures.Add(null);
                    break;
                default:
                    items.Add((false, 0));
                    pictures.Add(null);
                    break;
            }
        }

        var ellipsis = RevealText.MeasureOne("…", tb.FontFamily, tb.FontStyle, tb.FontWeight,
            tb.FontStretch, tb.FontSize, tb.FlowDirection, dpi);
        var shown = VisibleObjects(items, available, ellipsis);
        for (var i = 0; i < pictures.Count; i++)
        {
            var picture = pictures[i];
            if (picture == null) continue;
            var visibility = shown[i] ? Visibility.Visible : Visibility.Collapsed;
            if (picture.Visibility != visibility) picture.Visibility = visibility;
        }
    }
}
