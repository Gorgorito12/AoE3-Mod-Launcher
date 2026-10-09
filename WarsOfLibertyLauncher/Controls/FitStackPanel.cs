using System;
using System.Windows;
using System.Windows.Controls;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// A vertical stack that shows only the children that fit WHOLE, and nothing of the rest.
///
/// <para>Built for the community panel's lists (design handoff turns 38-40): the panel's height
/// is the layout's decision — 248 px, or a little more when larger text makes its capped rows
/// taller — and each list shows as many matches or ranking rows as fit inside it, never one cut
/// in half. A StackPanel in a
/// fixed-height card clips its last child mid-line, which is exactly what the handoff calls
/// out; a fixed <c>Take(n)</c> either wastes the space a tall window has or overflows a short
/// one.</para>
///
/// <para>Children are measured at INFINITE height, so each reports its natural size, and the
/// arrange pass stops at the first child whose bottom would pass the panel's own. The rest
/// are arranged at the panel's WIDTH into a slot of no height: laid out exactly as a shown
/// child would be, drawn nowhere and hit by nothing. Order is preserved: what is shown is
/// always a prefix of the list.</para>
///
/// <para><b>Never a 0×0 slot.</b> WPF does not lay an element out smaller than its
/// DesiredSize: a slot that is too small is INFLATED to it, and the element clipped. So a row
/// arranged into 0×0 was laid out at its OWN content's width — and the community card's match
/// rows change their own content from that width (<see cref="InlineFlagFit"/>, on the line's
/// SizeChanged), which changed the row's width, which ran it again. It never settled: WPF gave
/// up after 153 layout passes a frame, every frame, from v1.0.15 to v1.0.15f. At the panel's
/// width, nothing a hidden row does to its own content can move its own size. Pinned by
/// <c>RoomsActivityLayoutTests.ARowThatDoesNotFitIsLaidOutAtThePanelsWidth</c>.</para>
///
/// <para>"At the panel's width" holds for a row that STRETCHES — the default
/// <c>HorizontalAlignment</c>, and every row this panel holds today. A row aligned Left or
/// Center is arranged at its own DesiredSize inside that slot, so its width would follow its
/// content again; don't give a row here another alignment without re-reading the paragraph
/// above.</para>
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

    /// <summary>
    /// The height EVERY child needs, whatever the panel was given — the sum the measure pass
    /// computes before clamping it to the constraint.
    ///
    /// <para>What the community panel grows by when the text is larger than the reference: its
    /// lists are capped at four matches and five ranks, and those have to fit WHOLE, so above the
    /// reference text size the panel needs more than the handoff's 248 px. Asking
    /// <c>DesiredSize</c> instead would answer the clamped figure, i.e. the height it already
    /// has.</para>
    /// </summary>
    public double NaturalHeight { get; private set; }

    /// <summary>Raised when <see cref="NaturalHeight"/> changes — a refill, a text-size change,
    /// a language that wraps differently.</summary>
    public event EventHandler? NaturalHeightChanged;

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

        if (!NaturalHeight.Equals(height))
        {
            NaturalHeight = height;
            NaturalHeightChanged?.Invoke(this, EventArgs.Empty);
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
            if (heights[i] < 0)
            {
                // Collapsed: WPF lays nothing out for it, whatever the slot.
                c.Arrange(new Rect());
                continue;
            }
            if (n >= shown)
            {
                // No room: the panel's width and no height — never 0×0, which WPF inflates to
                // the child's own DesiredSize (see the class remarks).
                c.Arrange(new Rect(0, 0, finalSize.Width, 0));
                n++;
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
