using System;
using System.Windows;
using System.Windows.Controls;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// Children in lines that wrap, and a child marked <see cref="SpringProperty"/> takes what is left
/// on its line: CSS <c>display:flex; flex-wrap:wrap</c> with a <c>flex:1</c> spacer, which
/// <see cref="WrapPanel"/> cannot do and a design uses for its toolbars.
/// </summary>
public sealed class FlexLine : Panel
{
    public static readonly DependencyProperty SpringProperty =
        DependencyProperty.RegisterAttached("Spring", typeof(bool), typeof(FlexLine),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static bool GetSpring(DependencyObject element) => (bool)element.GetValue(SpringProperty);

    public static void SetSpring(DependencyObject element, bool value) => element.SetValue(SpringProperty, value);

    public double Gap { get; set; } = 12;

    public double LineGap { get; set; } = 8;

    protected override Size MeasureOverride(Size availableSize)
    {
        double lineWidth = 0, lineHeight = 0, width = 0, height = 0;
        bool first = true;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            if (GetSpring(child)) continue;
            var size = child.DesiredSize;
            if (!first && lineWidth + Gap + size.Width > availableSize.Width + .5)
            {
                width = Math.Max(width, lineWidth);
                height += lineHeight + LineGap;
                lineWidth = 0;
                lineHeight = 0;
                first = true;
            }
            lineWidth += (first ? 0 : Gap) + size.Width;
            lineHeight = Math.Max(lineHeight, size.Height);
            first = false;
        }
        return new Size(Math.Max(width, lineWidth), height + lineHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double y = 0;
        int start = 0;
        var children = InternalChildren;
        while (start < children.Count)
        {
            // One line: as many as fit, then share what is left among its springs.
            double used = 0, lineHeight = 0;
            int springs = 0, visible = 0, end = start;
            for (; end < children.Count; end++)
            {
                var child = children[end];
                if (child.Visibility == Visibility.Collapsed) continue;
                if (GetSpring(child)) { springs++; continue; }
                var w = child.DesiredSize.Width;
                if (visible > 0 && used + Gap + w > finalSize.Width + .5) break;
                used += (visible > 0 ? Gap : 0) + w;
                lineHeight = Math.Max(lineHeight, child.DesiredSize.Height);
                visible++;
            }
            double spare = springs > 0 ? Math.Max(0, finalSize.Width - used) / springs : 0;
            double x = 0;
            bool placed = false;
            for (int i = start; i < end; i++)
            {
                var child = children[i];
                if (child.Visibility == Visibility.Collapsed) continue;
                if (GetSpring(child))
                {
                    child.Arrange(new Rect(x, y, spare, lineHeight));
                    x += spare;
                    continue;
                }
                if (placed) x += Gap;
                var size = child.DesiredSize;
                child.Arrange(new Rect(x, y + (lineHeight - size.Height) / 2, size.Width, size.Height));
                x += size.Width;
                placed = true;
            }
            y += lineHeight + LineGap;
            start = end > start ? end : end + 1;
        }
        return finalSize;
    }
}

/// <summary>
/// CSS <c>grid-template-columns: repeat(auto-fill, minmax(MIN, 1fr))</c>: as many equal columns as
/// fit at <see cref="MinColumn"/>, each row as tall as its tallest cell.
/// </summary>
public sealed class AutoFillPanel : Panel
{
    public double MinColumn { get; set; } = 220;

    public double Gap { get; set; } = 8;

    private int ColumnsFor(double width)
        => double.IsInfinity(width) ? 1 : Math.Max(1, (int)Math.Floor((width + Gap) / (MinColumn + Gap)));

    protected override Size MeasureOverride(Size availableSize)
    {
        int columns = ColumnsFor(availableSize.Width);
        double cell = double.IsInfinity(availableSize.Width) ? MinColumn : (availableSize.Width - Gap * (columns - 1)) / columns;
        double height = 0, row = 0;
        int index = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(cell, double.PositiveInfinity));
            row = Math.Max(row, child.DesiredSize.Height);
            if (++index % columns == 0)
            {
                height += row + Gap;
                row = 0;
            }
        }
        if (index % columns != 0) height += row + Gap;
        return new Size(double.IsInfinity(availableSize.Width) ? cell : availableSize.Width, Math.Max(0, height - Gap));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int columns = ColumnsFor(finalSize.Width);
        double cell = (finalSize.Width - Gap * (columns - 1)) / columns;
        double y = 0;
        var children = InternalChildren;
        for (int start = 0; start < children.Count; start += columns)
        {
            double row = 0;
            for (int i = start; i < Math.Min(start + columns, children.Count); i++)
                row = Math.Max(row, children[i].DesiredSize.Height);
            for (int i = start; i < Math.Min(start + columns, children.Count); i++)
                children[i].Arrange(new Rect((i - start) * (cell + Gap), y, cell, row));
            y += row + Gap;
        }
        return finalSize;
    }
}

/// <summary>
/// CSS <c>display:flex; flex-wrap:wrap; align-items:stretch</c> with each child's
/// <c>flex: GROW 1 BASIS</c> (design 109, the Statistics page): children are placed in order on a
/// line while their bases and the gaps fit; the width left on that line is shared out by
/// <see cref="GrowProperty"/>; every child of a line is as tall as the tallest. A child whose basis
/// alone is wider than the panel gets a line of its own at the panel's width.
/// </summary>
public sealed class FlexGrowPanel : Panel
{
    public static readonly DependencyProperty BasisProperty = DependencyProperty.RegisterAttached(
        "Basis", typeof(double), typeof(FlexGrowPanel),
        new FrameworkPropertyMetadata(200.0, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static readonly DependencyProperty GrowProperty = DependencyProperty.RegisterAttached(
        "Grow", typeof(double), typeof(FlexGrowPanel),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static double GetBasis(DependencyObject e) => (double)e.GetValue(BasisProperty);
    public static void SetBasis(DependencyObject e, double value) => e.SetValue(BasisProperty, value);
    public static double GetGrow(DependencyObject e) => (double)e.GetValue(GrowProperty);
    public static void SetGrow(DependencyObject e, double value) => e.SetValue(GrowProperty, value);

    public double Gap { get; set; } = 12;

    /// <summary>The lines: which children, and the width each gets. Pure, so it can be pinned.</summary>
    internal static List<List<(int Index, double Width)>> Lines(IReadOnlyList<(double Basis, double Grow)> items, double width, double gap)
    {
        var lines = new List<List<(int, double)>>();
        int i = 0;
        while (i < items.Count)
        {
            int start = i;
            double used = items[i].Basis;
            i++;
            while (i < items.Count && used + gap + items[i].Basis <= width) used += gap + items[i++].Basis;
            double spare = Math.Max(0, width - used);
            double grow = 0;
            for (int k = start; k < i; k++) grow += items[k].Grow;
            var line = new List<(int, double)>();
            for (int k = start; k < i; k++)
            {
                double w = items[k].Basis + (grow > 0 ? spare * items[k].Grow / grow : 0);
                line.Add((k, Math.Min(w, width)));
            }
            lines.Add(line);
        }
        return lines;
    }

    private List<(double, double)> Items()
    {
        var list = new List<(double, double)>();
        foreach (UIElement child in InternalChildren)
            list.Add(child.Visibility == Visibility.Collapsed ? (0, 0) : (GetBasis(child), GetGrow(child)));
        return list;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? Items().Sum(x => x.Item1) : availableSize.Width;
        double height = 0;
        var lines = Lines(Items(), width, Gap);
        foreach (var line in lines)
        {
            double row = 0;
            foreach (var (index, w) in line)
            {
                var child = InternalChildren[index];
                child.Measure(new Size(w, double.PositiveInfinity));
                row = Math.Max(row, child.DesiredSize.Height);
            }
            height += row;
        }
        height += Gap * Math.Max(0, lines.Count - 1);
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double y = 0;
        foreach (var line in Lines(Items(), finalSize.Width, Gap))
        {
            double row = 0;
            foreach (var (index, _) in line) row = Math.Max(row, InternalChildren[index].DesiredSize.Height);
            double x = 0;
            foreach (var (index, w) in line)
            {
                InternalChildren[index].Arrange(new Rect(x, y, w, row));
                x += w + Gap;
            }
            y += row + Gap;
        }
        return finalSize;
    }
}
