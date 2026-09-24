using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// Shows a path shortened in the MIDDLE to exactly the width it is given, in a monospace
/// TextBlock.
///
/// <para>A fixed character budget (<see cref="PathDisplay.CompactPathMiddle"/> with a
/// number picked by hand) is right at one width and wrong at every other: too long and the
/// block's own end-trimming cuts it AGAIN, which removes the tail — the copy's own folder,
/// the one part that tells two copies apart. Measuring one glyph of the monospace font
/// and dividing the real width by it gives the exact budget, so the middle is what goes and
/// the tail always survives.</para>
///
/// <para>The block must stretch (the default) so its ActualWidth is the slot it was given,
/// not the width of whatever text it currently holds; the end-trimming stays on as a
/// safety net for the last pixel.</para>
/// </summary>
internal static class MiddlePathText
{
    public static void Bind(TextBlock block, string? path, string suffix = "")
    {
        block.Tag = (path ?? "", suffix);
        block.ToolTip = string.IsNullOrEmpty(path) ? null : path;
        block.TextTrimming = TextTrimming.CharacterEllipsis;
        block.SizeChanged -= OnSizeChanged;
        block.SizeChanged += OnSizeChanged;
        Refit(block);
    }

    private static void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged) Refit((TextBlock)sender);
    }

    private static void Refit(TextBlock block)
    {
        if (block.Tag is not (string path, string suffix)) return;
        double width = block.ActualWidth;
        if (width <= 0) { block.Text = path + suffix; return; }

        double glyph = GlyphWidth(block);
        // Two characters of slack: layout rounding and the ellipsis glyph itself are a hair
        // wider than the measured digit, and one character too many is what lets the
        // block's own end-trimming cut the tail again.
        int budget = glyph > 0 ? (int)Math.Floor(width / glyph) - suffix.Length - 2 : path.Length;
        block.Text = PathDisplay.CompactPathMiddle(path, Math.Max(8, budget)) + suffix;
    }

    private static double GlyphWidth(TextBlock block)
    {
        try
        {
            var typeface = new Typeface(block.FontFamily, block.FontStyle, block.FontWeight, block.FontStretch);
            // Measured in the block's OWN formatting mode: the launcher renders in Display
            // mode, which snaps every advance to a whole device pixel, so an Ideal-mode
            // measurement comes out a little narrow and the budget one character too long.
            var ft = new FormattedText("0000000000", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                typeface, block.FontSize, Brushes.Black, null,
                TextOptions.GetTextFormattingMode(block),
                VisualTreeHelper.GetDpi(block).PixelsPerDip);
            return ft.WidthIncludingTrailingWhitespace / 10.0;
        }
        catch { return 0; }
    }
}
