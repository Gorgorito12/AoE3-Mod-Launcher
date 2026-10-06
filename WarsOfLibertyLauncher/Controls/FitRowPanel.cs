using System;
using System.Windows;
using System.Windows.Controls;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// A horizontal row that shows only the children that fit WHOLE, and drops the rest FROM THE END —
/// the sideways sibling of <see cref="FitStackPanel"/>.
///
/// <para>Built for the community block's header line (design 61): the facts sit beside the title
/// on ONE line, each «LABEL value», and when they do not fit, whole facts are hidden by priority —
/// last first (Most played, then Players, then Matches…). It never wraps to a second line, which is
/// what 60's <c>WrapPanel</c> did and what made the block two lines taller on a laptop.</para>
///
/// <para>Children are measured at INFINITE width, so each reports its natural size, and the
/// arrange pass stops at the first child whose right edge would pass the panel's own. The rest are
/// arranged into an empty rect, which draws nothing and takes no hits. What is shown is always a
/// prefix of the children.</para>
///
/// <para><b>Unlike <see cref="FitStackPanel"/>, a hidden fact keeps the 0×0 slot</b>, which WPF
/// inflates to the fact's own DesiredSize — here the same width a SHOWN fact is given, so the two
/// are laid out alike. What keeps that safe is that nothing inside a fact changes its own layout
/// from its size: the facts are text, and their hover reveal only ever sets a ToolTip. A fact
/// that wrote layout from SizeChanged would never settle, shown or hidden — the v1.0.15 storm,
/// see <see cref="FitStackPanel"/>.</para>
/// </summary>
public sealed class FitRowPanel : Panel
{
    /// <summary>How many children the last arrange pass showed. For tests and diagnostics.</summary>
    public int VisibleCount { get; private set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        var child = new Size(double.PositiveInfinity, availableSize.Height);
        double width = 0, height = 0;
        foreach (UIElement c in InternalChildren)
        {
            if (c == null) continue;
            c.Measure(child);
            if (c.Visibility == Visibility.Collapsed) continue;
            width += c.DesiredSize.Width;
            height = Math.Max(height, c.DesiredSize.Height);
        }

        // Asking for the whole natural width is what lets an Auto column size to the content; a
        // finite constraint caps it, and the arrange pass decides what the cap leaves room for.
        return new Size(
            double.IsInfinity(availableSize.Width) ? width : Math.Min(width, availableSize.Width),
            double.IsInfinity(availableSize.Height) ? height : Math.Min(height, availableSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var widths = new double[InternalChildren.Count];
        for (var i = 0; i < widths.Length; i++)
        {
            var c = InternalChildren[i];
            widths[i] = c == null || c.Visibility == Visibility.Collapsed ? -1 : c.DesiredSize.Width;
        }

        var shown = CountThatFit(widths, finalSize.Width);
        double x = 0;
        var n = 0;
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var c = InternalChildren[i];
            if (c == null) continue;
            if (widths[i] < 0 || n >= shown)
            {
                c.Arrange(new Rect(0, 0, 0, 0));
                if (widths[i] >= 0) n++;
                continue;
            }
            c.Arrange(new Rect(x, 0, widths[i], finalSize.Height));
            x += widths[i];
            n++;
        }
        VisibleCount = shown;
        return finalSize;
    }

    /// <summary>
    /// How many of <paramref name="widths"/>, taken in order, fit WHOLE in
    /// <paramref name="available"/>. A negative width is a collapsed child: skipped, and never
    /// counted. Half a pixel of slack, so layout rounding cannot drop a fact that fits exactly.
    /// </summary>
    internal static int CountThatFit(double[] widths, double available)
    {
        double used = 0;
        var count = 0;
        foreach (var w in widths)
        {
            if (w < 0) continue;
            if (used + w > available + 0.5) break;
            used += w;
            count++;
        }
        return count;
    }
}
