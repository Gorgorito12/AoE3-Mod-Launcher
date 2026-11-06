using System;
using System.Windows;
using System.Windows.Controls;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// One setting of the publish wizard (design 73): name and help on the left, the field on the
/// right at clamp(280, 40 %, 560), so a wide window gives the text the room instead of stretching
/// a field for a twenty-character id. <see cref="Stacked"/> puts the field under the text, for
/// the multi-line ones. <see cref="Compact"/> gives the right side only what it asks for, for a
/// switch or a button. A third child, when it shows, takes the full width underneath.
/// </summary>
public sealed class FieldRowPanel : Panel
{
    private const double Gap = 24;
    private const double RowGap = 10;

    public static readonly DependencyProperty StackedProperty = DependencyProperty.Register(
        nameof(Stacked), typeof(bool), typeof(FieldRowPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty CompactProperty = DependencyProperty.Register(
        nameof(Compact), typeof(bool), typeof(FieldRowPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public bool Stacked
    {
        get => (bool)GetValue(StackedProperty);
        set => SetValue(StackedProperty, value);
    }

    public bool Compact
    {
        get => (bool)GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    internal static double FieldWidth(double total) => Math.Min(Math.Clamp(total * .4, 280, 560), total);

    private double RightWidth(double total)
    {
        if (!Compact) return FieldWidth(total);
        InternalChildren[1].Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Math.Min(InternalChildren[1].DesiredSize.Width, total);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (InternalChildren.Count < 2) return default;
        var total = double.IsInfinity(availableSize.Width) ? 800 : availableSize.Width;
        var inf = double.PositiveInfinity;
        double height;
        if (Stacked)
        {
            InternalChildren[0].Measure(new Size(total, inf));
            InternalChildren[1].Measure(new Size(total, inf));
            height = InternalChildren[0].DesiredSize.Height + RowGap + InternalChildren[1].DesiredSize.Height;
        }
        else
        {
            var right = RightWidth(total);
            InternalChildren[0].Measure(new Size(Math.Max(0, total - right - Gap), inf));
            InternalChildren[1].Measure(new Size(right, inf));
            height = Math.Max(InternalChildren[0].DesiredSize.Height, InternalChildren[1].DesiredSize.Height);
        }
        if (InternalChildren.Count > 2 && InternalChildren[2].Visibility != Visibility.Collapsed)
        {
            InternalChildren[2].Measure(new Size(total, inf));
            height += RowGap + InternalChildren[2].DesiredSize.Height;
        }
        return new Size(total, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (InternalChildren.Count < 2) return finalSize;
        var total = finalSize.Width;
        double bottom;
        if (Stacked)
        {
            var h = InternalChildren[0].DesiredSize.Height;
            InternalChildren[0].Arrange(new Rect(0, 0, total, h));
            InternalChildren[1].Arrange(new Rect(0, h + RowGap, total, InternalChildren[1].DesiredSize.Height));
            bottom = h + RowGap + InternalChildren[1].DesiredSize.Height;
        }
        else
        {
            var right = RightWidth(total);
            var h = Math.Max(InternalChildren[0].DesiredSize.Height, InternalChildren[1].DesiredSize.Height);
            InternalChildren[0].Arrange(new Rect(0, 0, Math.Max(0, total - right - Gap), h));
            InternalChildren[1].Arrange(new Rect(total - right, 0, right, h));
            bottom = h;
        }
        if (InternalChildren.Count > 2 && InternalChildren[2].Visibility != Visibility.Collapsed)
            InternalChildren[2].Arrange(new Rect(0, bottom + RowGap, total, InternalChildren[2].DesiredSize.Height));
        return finalSize;
    }
}
