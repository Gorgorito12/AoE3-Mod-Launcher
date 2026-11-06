using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>The small pills of the mod window's rows (design 71): a version and a status.</summary>
internal static class ModChips
{
    private const string TagShape =
        "M1.5 8.6V2.2c0-.4.3-.7.7-.7h6.4l6 6c.3.3.3.7 0 1l-5.7 5.7c-.3.3-.7.3-1 0z";

    internal static Border Version(string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Path
        {
            Data = Geometry.Parse(TagShape),
            Stroke = new SolidColorBrush(Color.FromRgb(0x7E, 0xE0, 0x8B)),
            StrokeThickness = 1.7,
            StrokeLineJoin = PenLineJoin.Round,
            Width = 11,
            Height = 11,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0),
        });
        var label = Label(text);
        label.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        row.Children.Add(label);
        return Shell(row, new Thickness(7, 0, 9, 0));
    }

    /// <summary>A status with its dot. <paramref name="dotHex"/> is the handoff's colour.</summary>
    internal static Border Status(string text, string dotHex)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Ellipse
        {
            Width = 6,
            Height = 6,
            Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dotHex)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        });
        row.Children.Add(Label(text));
        return Shell(row, new Thickness(9, 0, 9, 0));
    }

    private static TextBlock Label(string text)
    {
        var label = new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.SetResourceReference(TextBlock.FontSizeProperty, "SetGroupLabelSize");
        label.SetResourceReference(TextBlock.ForegroundProperty, "ModChipText");
        return label;
    }

    private static Border Shell(UIElement content, Thickness padding)
    {
        var chip = new Border
        {
            Height = 22,
            Padding = padding,
            CornerRadius = new CornerRadius(11),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            SnapsToDevicePixels = true,
            Child = content,
        };
        chip.SetResourceReference(Border.BackgroundProperty, "ModChipFill");
        chip.SetResourceReference(Border.BorderBrushProperty, "LibraryChipRim");
        return chip;
    }
}
