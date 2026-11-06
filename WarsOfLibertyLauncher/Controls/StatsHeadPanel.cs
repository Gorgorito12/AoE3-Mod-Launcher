using System.Windows;
using System.Windows.Controls;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// The head of the Statistics page: the first child on the left, the rest pushed to the right,
/// and whatever does not fit goes down a row, still on the right.
///
/// <para>A <c>flex-wrap</c> with a spring in it, which WPF does not have: a WrapPanel cannot push
/// anything right, and a <c>* | Auto</c> Grid paints the Auto side over the star side instead of
/// wrapping, which is how the rooms toolbar and the Workshop's filters came to overlap their
/// neighbours. A launcher with five mods installed is the ordinary case for this row.</para>
/// </summary>
public sealed class StatsHeadPanel : Panel
{
    private const double Gap = 16;
    private const double RowGap = 8;

    /// <summary>Which children share a row, the first row holding the left-hand child.</summary>
    private List<List<UIElement>> Rows(double width)
    {
        var rows = new List<List<UIElement>>();
        var current = new List<UIElement>();
        double used = 0;

        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed) continue;

            double need = child.DesiredSize.Width + (current.Count > 0 ? Gap : 0);
            if (current.Count > 0 && used + need > width)
            {
                rows.Add(current);
                current = new List<UIElement>();
                used = 0;
                need = child.DesiredSize.Width;
            }
            current.Add(child);
            used += need;
        }

        if (current.Count > 0) rows.Add(current);
        return rows;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (UIElement child in InternalChildren)
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));

        double height = 0;
        double widest = 0;
        var rows = Rows(availableSize.Width);
        for (int i = 0; i < rows.Count; i++)
        {
            height += rows[i].Max(c => c.DesiredSize.Height) + (i > 0 ? RowGap : 0);
            widest = Math.Max(widest, rows[i].Sum(c => c.DesiredSize.Width)
                                      + Gap * (rows[i].Count - 1));
        }

        return new Size(
            double.IsInfinity(availableSize.Width) ? widest : availableSize.Width,
            height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var first = InternalChildren.Count > 0 ? InternalChildren[0] : null;
        double top = 0;

        foreach (var row in Rows(finalSize.Width))
        {
            double height = row.Max(c => c.DesiredSize.Height);

            // Right to left, so the spring is whatever is left over.
            double right = finalSize.Width;
            for (int i = row.Count - 1; i >= 0; i--)
            {
                var child = row[i];
                double width = Math.Min(child.DesiredSize.Width, finalSize.Width);

                // The left-hand child keeps the left edge; everything else hangs off the right
                // and sits on the row's bottom edge, the way the prototype aligns it.
                double x = ReferenceEquals(child, first) ? 0 : Math.Max(0, right - width);
                child.Arrange(new Rect(
                    x, top + height - child.DesiredSize.Height, width, child.DesiredSize.Height));
                right = x - Gap;
            }

            top += height + RowGap;
        }

        return finalSize;
    }
}
