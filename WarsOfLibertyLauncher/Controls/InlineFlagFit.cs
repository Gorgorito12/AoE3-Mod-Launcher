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
/// whether it is currently shown or not — and a picture that is not shown KEEPS that width on
/// the line.</para>
///
/// <para><b>Hidden, NEVER Collapsed — Collapsed is what froze the Rooms page from v1.0.15 to
/// v1.0.15f.</b> <see cref="Apply"/> runs from the line's own <c>SizeChanged</c>, and a
/// SizeChanged handler must never change the layout of the element it reacts to. Collapsed
/// does: the picture's width leaves the line, the TextBlock is measured again and its trimmed
/// width moves. In a match row a <see cref="FitStackPanel"/> had no room for, that trimmed width
/// WAS the row's width (the panel arranged it into a 0×0 slot, which WPF inflates to the row's
/// own DesiredSize), so the row resized, this ran again and changed its mind, and WPF gave up
/// after 153 layout passes — every frame, for hours, on a player's laptop ("LAYOUT STORM 0/s
/// layout passes … 246 ms a frame"). Hidden only stops drawing the picture: the line measures
/// the same either way, so nothing written here can feed back into the size it was decided
/// from. Pinned by <c>InlineFlagFitTests.THE_ONE_THAT_MATTERS_HidingAFlagNeverInvalidatesTheLine</c>
/// and <c>CompactRoomsLayoutTests.THE_ONE_THAT_MATTERS_FlaggedMatchRowsSettleAtEveryWidth</c>.</para>
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
    /// line that is laid out again with the same width does nothing — and hides it with
    /// <see cref="Visibility.Hidden"/>, never Collapsed (see the class remarks: this runs from
    /// the line's own SizeChanged).
    /// </summary>
    public static void Apply(TextBlock tb)
    {
        Services.PerfCounters.Increment("InlineFlagFit.Apply");
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
            // Hidden keeps the picture's width on the line; Collapsed would change the very
            // layout this was decided from (the v1.0.15 storm).
            var visibility = shown[i] ? Visibility.Visible : Visibility.Hidden;
            if (picture.Visibility != visibility) picture.Visibility = visibility;
        }
    }
}
