using System;
using System.Windows;
using System.Windows.Controls;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// A mosaic of cards (design 119): equal columns, the cards placed row by row, each as tall as its
/// own content and aligned to the top of its row, and a card that asks for it lying over two
/// columns. CSS <c>grid</c> with <c>align-items: start</c> and one <c>span 2</c>.
///
/// <para><b>How many columns is the panel's own answer</b>, from the width it is given
/// (<see cref="HighlightsMosaic.Columns"/>), so a window crossing the line reflows the cards that
/// are already there: nothing is rebuilt and nobody has to tell it.</para>
/// </summary>
public sealed class BentoPanel : Panel
{
    /// <summary>How many columns a card lies over: 1, or 2 for the wide one.</summary>
    public static readonly DependencyProperty SpanProperty = DependencyProperty.RegisterAttached(
        "Span", typeof(int), typeof(BentoPanel),
        new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static int GetSpan(UIElement element) => (int)element.GetValue(SpanProperty);

    public static void SetSpan(UIElement element, int value) => element.SetValue(SpanProperty, value);

    /// <summary>Between columns and between rows.</summary>
    public double Gap { get; set; } = 14;

    /// <summary>The columns the last layout used (a test reads it).</summary>
    public int Columns { get; private set; } = 1;

    private (int Column, int Span, double Top)[] _slots = Array.Empty<(int, int, double)>();
    private double _columnWidth;

    protected override Size MeasureOverride(Size availableSize)
    {
        var count = InternalChildren.Count;
        _slots = new (int, int, double)[count];
        if (count == 0) return default;

        // With no width to divide there is nothing to lay out in columns: one under another.
        var bounded = !double.IsInfinity(availableSize.Width);
        Columns = bounded ? HighlightsMosaic.Columns(availableSize.Width) : 1;
        _columnWidth = bounded ? Math.Max(0, (availableSize.Width - (Columns - 1) * Gap) / Columns) : double.PositiveInfinity;

        double top = 0, rowHeight = 0, widest = 0;
        var column = 0;
        for (var i = 0; i < count; i++)
        {
            var child = InternalChildren[i];
            if (child.Visibility == Visibility.Collapsed) { _slots[i] = (0, 0, top); continue; }
            var span = Math.Clamp(GetSpan(child), 1, Columns);
            if (column + span > Columns)
            {
                top += rowHeight + Gap;
                rowHeight = 0;
                column = 0;
            }
            child.Measure(new Size(bounded ? WidthOf(span) : double.PositiveInfinity, double.PositiveInfinity));
            _slots[i] = (column, span, top);
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            widest = Math.Max(widest, child.DesiredSize.Width);
            column += span;
        }
        return new Size(bounded ? availableSize.Width : widest, top + rowHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        for (var i = 0; i < InternalChildren.Count && i < _slots.Length; i++)
        {
            var child = InternalChildren[i];
            var (column, span, top) = _slots[i];
            if (span == 0) continue;
            var width = double.IsInfinity(_columnWidth) ? finalSize.Width : WidthOf(span);
            var left = double.IsInfinity(_columnWidth) ? 0 : column * (_columnWidth + Gap);
            // Its own height, from the top of its row: a short card never stretches to its neighbour's.
            child.Arrange(new Rect(left, top, width, child.DesiredSize.Height));
        }
        return finalSize;
    }

    private double WidthOf(int span) => span * _columnWidth + (span - 1) * Gap;
}
