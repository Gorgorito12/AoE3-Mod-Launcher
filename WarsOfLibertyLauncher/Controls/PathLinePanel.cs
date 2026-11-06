using System;
using System.Windows;
using System.Windows.Controls;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// A path on one line: root, middle, last folder (design 74). The root and the folder you are
/// choosing are always whole and the middle takes what is left and ends in an ellipsis. A Grid
/// cannot do this: a star column would push the last folder to the far right on a short path,
/// where it has to follow the middle.
/// </summary>
public sealed class PathLinePanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        if (InternalChildren.Count < 3) return default;
        var whole = new Size(double.PositiveInfinity, availableSize.Height);
        InternalChildren[0].Measure(whole);
        InternalChildren[2].Measure(whole);
        var room = Math.Max(0, availableSize.Width
            - InternalChildren[0].DesiredSize.Width - InternalChildren[2].DesiredSize.Width);
        InternalChildren[1].Measure(new Size(room, availableSize.Height));

        var width = InternalChildren[0].DesiredSize.Width + InternalChildren[1].DesiredSize.Width
            + InternalChildren[2].DesiredSize.Width;
        var height = Math.Max(InternalChildren[0].DesiredSize.Height,
            Math.Max(InternalChildren[1].DesiredSize.Height, InternalChildren[2].DesiredSize.Height));
        return new Size(Math.Min(width, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (InternalChildren.Count < 3) return finalSize;
        var x = 0.0;
        for (var i = 0; i < 3; i++)
        {
            var child = InternalChildren[i];
            child.Arrange(new Rect(x, 0, child.DesiredSize.Width, finalSize.Height));
            x += child.DesiredSize.Width;
        }
        return finalSize;
    }
}
