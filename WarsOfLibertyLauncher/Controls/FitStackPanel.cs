using System;
using System.Windows;
using System.Windows.Controls;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// A vertical stack that shows only the children that fit WHOLE, and nothing of the rest.
///
/// <para>Built for the community panel's lists (design handoff turns 38-39): the panel's height
/// is the layout's decision — 248 px, or everything the rooms leave — and each list shows as
/// many matches or ranking rows as fit inside it, never one cut in half. A StackPanel in a
/// fixed-height card clips its last child mid-line, which is exactly what the handoff calls
/// out; a fixed <c>Take(n)</c> either wastes the space a tall window has or overflows a short
/// one.</para>
///
/// <para>Children are measured at INFINITE height, so each reports its natural size, and the
/// arrange pass stops at the first child whose bottom would pass the panel's own. The rest
/// are arranged into an empty rect, which draws nothing and takes no hits. Order is
/// preserved: what is shown is always a prefix of the list.</para>
/// </summary>
public sealed class FitStackPanel : Panel
{
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(FitStackPanel),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>The gap between two shown children.</summary>
    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>How many children the last arrange pass showed. For tests and diagnostics.</summary>
    public int VisibleCount { get; private set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        var child = new Size(availableSize.Width, double.PositiveInfinity);
        double width = 0, height = 0;
        var first = true;
        foreach (UIElement c in InternalChildren)
        {
            if (c == null) continue;
            c.Measure(child);
            if (c.Visibility == Visibility.Collapsed) continue;
            width = Math.Max(width, c.DesiredSize.Width);
            height += (first ? 0 : Spacing) + c.DesiredSize.Height;
            first = false;
        }

        // Asking for the whole natural height is what lets an Auto row size to the content;
        // a finite constraint caps it, and the arrange pass decides what the cap leaves room for.
        return new Size(
            double.IsInfinity(availableSize.Width) ? width : Math.Min(width, availableSize.Width),
            double.IsInfinity(availableSize.Height) ? height : Math.Min(height, availableSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var heights = new double[InternalChildren.Count];
        for (var i = 0; i < heights.Length; i++)
        {
            var c = InternalChildren[i];
            heights[i] = c == null || c.Visibility == Visibility.Collapsed ? -1 : c.DesiredSize.Height;
        }

        var shown = CountThatFit(heights, Spacing, finalSize.Height);
        double y = 0;
        var n = 0;
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var c = InternalChildren[i];
            if (c == null) continue;
            if (heights[i] < 0 || n >= shown)
            {
                c.Arrange(new Rect(0, 0, 0, 0));
                if (heights[i] >= 0) n++;
                continue;
            }
            if (n > 0) y += Spacing;
            c.Arrange(new Rect(0, y, finalSize.Width, heights[i]));
            y += heights[i];
            n++;
        }
        VisibleCount = shown;
        return finalSize;
    }

    /// <summary>
    /// How many of <paramref name="heights"/>, taken in order, fit WHOLE in
    /// <paramref name="available"/> with <paramref name="spacing"/> between them. A negative
    /// height is a collapsed child: skipped, and never counted.
    /// </summary>
    internal static int CountThatFit(double[] heights, double spacing, double available)
    {
        if (!(available > 0)) return 0;
        // A hair of tolerance: layout rounding can leave an exact fit 0.01 px short.
        const double slack = 0.5;
        double used = 0;
        var count = 0;
        foreach (var h in heights)
        {
            if (h < 0) continue;
            var next = used + (count > 0 ? spacing : 0) + h;
            if (next > available + slack) break;
            used = next;
            count++;
        }
        return count;
    }
}
