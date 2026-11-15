using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>One choice of an <see cref="OptionRows"/>: what it is called and what it means.</summary>
internal sealed record OptionRow(object Key, string Title, string Description);

/// <summary>
/// The glow under a selected choice (design 108: <c>0 2 12</c> of the mod's colour at .28). An
/// attached brush the templates of <c>SegmentButton</c> and <c>OptionRowButton</c> paint a blurred
/// sibling with, so the blur never sits over the text, and so a choice that is not selected (no
/// brush) draws nothing at all.
/// </summary>
internal static class ChoiceGlow
{
    public static readonly DependencyProperty BrushProperty = DependencyProperty.RegisterAttached(
        "Brush", typeof(Brush), typeof(ChoiceGlow), new PropertyMetadata(null));

    public static Brush? GetBrush(DependencyObject element) => (Brush?)element.GetValue(BrushProperty);
    public static void SetBrush(DependencyObject element, Brush? value) => element.SetValue(BrushProperty, value);

    /// <summary>The glow of a selected choice in <paramref name="accent"/>, or none.</summary>
    internal static Brush? For(bool on, Color accent)
        => on ? Frozen(new SolidColorBrush(Color.FromArgb(0x47, accent.R, accent.G, accent.B))) : null;

    private static Brush Frozen(Brush b)
    {
        b.Freeze();
        return b;
    }
}

/// <summary>
/// The rows of choice of the new-tournament window (design 108): a circle, a title and what the
/// choice means, the explanation INSIDE the row instead of a line under a segmented control.
/// Stacked one per row, or side by side in equal columns of equal height (how teams are formed).
///
/// <para><b>Selecting changes the fill and the rim, never the weight or the size.</b> The text
/// measures the same chosen or not; that is the whole of design 108, whose case was "First come,
/// first served" growing past its box when it turned bold in English.</para>
/// </summary>
internal sealed class OptionRows : Grid
{
    private readonly List<(OptionRow Option, Button Button, Ellipse Dot, TextBlock Title, TextBlock Text)> _items = new();
    private object? _selected;
    private Color _accent = Color.FromRgb(0x2F, 0x7F, 0xE0);

    /// <summary>1 stacks the rows; more puts them side by side in equal columns.</summary>
    public int Columns { get; set; } = 1;

    public double Gap { get; set; } = 6;

    /// <summary>Raised when the player picks a row, not when the code does.</summary>
    public event Action<object>? Picked;

    public IEnumerable<Button> Buttons
    {
        get
        {
            foreach (var item in _items) yield return item.Button;
        }
    }

    public object? Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            Repaint();
        }
    }

    public Color Accent
    {
        get => _accent;
        set
        {
            _accent = value;
            Repaint();
        }
    }

    /// <summary>The description shown under the selected row's title, for tests and tooltips.</summary>
    public string? SelectedDescription
    {
        get
        {
            foreach (var item in _items)
                if (Equals(item.Option.Key, _selected)) return item.Option.Description;
            return null;
        }
    }

    public void SetOptions(IReadOnlyList<OptionRow> options)
    {
        Children.Clear();
        ColumnDefinitions.Clear();
        RowDefinitions.Clear();
        _items.Clear();
        var across = Math.Max(1, Columns);
        for (var c = 0; c < across; c++) ColumnDefinitions.Add(new ColumnDefinition());
        var rows = (options.Count + across - 1) / across;
        for (var r = 0; r < rows; r++) RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (var i = 0; i < options.Count; i++)
        {
            var (button, dot, title, text) = Build(options[i]);
            var col = i % across;
            var row = i / across;
            button.Margin = new Thickness(
                col == 0 ? 0 : Gap / 2, row == 0 ? 0 : Gap / 2,
                col == across - 1 ? 0 : Gap / 2, row == rows - 1 ? 0 : Gap / 2);
            SetColumn(button, col);
            SetRow(button, row);
            Children.Add(button);
            _items.Add((options[i], button, dot, title, text));
        }
        Repaint();
    }

    private (Button, Ellipse, TextBlock, TextBlock) Build(OptionRow option)
    {
        var dot = new Ellipse
        {
            Width = 16, Height = 16,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 1, 0, 0),
        };
        var title = new TextBlock { Text = option.Title, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold };
        title.SetResourceReference(TextBlock.FontSizeProperty, "DialogBodySize");
        var text = new TextBlock { Text = option.Description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        text.SetResourceReference(TextBlock.FontSizeProperty, "SetControlSize");
        ModDialogChrome.SetTightLine(1.3, title);
        ModDialogChrome.SetTightLine(1.4, text);
        var words = new StackPanel();
        words.Children.Add(title);
        words.Children.Add(text);
        SetColumn(words, 1);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(dot);
        grid.Children.Add(words);

        var button = new Button { Content = grid, VerticalAlignment = VerticalAlignment.Stretch };
        button.SetResourceReference(StyleProperty, "OptionRowButton");
        var key = option.Key;
        button.Click += (_, _) =>
        {
            Selected = key;
            Picked?.Invoke(key);
        };
        return (button, dot, title, text);
    }

    private void Repaint()
    {
        foreach (var (option, button, dot, title, text) in _items)
            Paint(button, dot, title, text, Equals(option.Key, _selected), _accent);
    }

    /// <summary>The two states of design 108's table. Same weight and size in both.</summary>
    internal static void Paint(Button button, Ellipse dot, TextBlock title, TextBlock text, bool on, Color accent)
    {
        button.Tag = on ? "active" : null;
        button.Background = new SolidColorBrush(on ? ModAccent.SelectionFill(accent) : Color.FromRgb(0x1A, 0x2C, 0x48));
        button.BorderBrush = new SolidColorBrush(on ? Color.FromArgb(0xF2, accent.R, accent.G, accent.B) : Color.FromArgb(0x38, 0x82, 0xAF, 0xFF));
        button.BorderThickness = new Thickness(on ? 1.5 : 1);
        ChoiceGlow.SetBrush(button, ChoiceGlow.For(on, accent));
        title.Foreground = new SolidColorBrush(on ? Colors.White : Color.FromRgb(0xDC, 0xE7, 0xF5));
        text.Foreground = new SolidColorBrush(on ? Color.FromRgb(0xDC, 0xE7, 0xF5) : Color.FromRgb(0xA8, 0xBC, 0xD2));
        // Selected: the mod's colour with a white ring of 5 inside, which leaves a dot of the colour.
        dot.Fill = on ? new SolidColorBrush(accent) : Brushes.Transparent;
        dot.Stroke = new SolidColorBrush(on ? Colors.White : Color.FromArgb(0x80, 0x82, 0xAF, 0xFF));
        dot.StrokeThickness = on ? 5 : 1.5;
    }
}
