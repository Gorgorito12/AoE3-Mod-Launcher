using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>One choice of a <see cref="SegmentRow"/>. <paramref name="Gold"/> is what marks a
/// competitive option: gold is the colour of a match that counts, whatever the mod's is.</summary>
internal sealed record SegmentOption(object Key, string Text, string? Sub = null, bool Gold = false);

/// <summary>
/// The row of equal segments of the two creation dialogs (88, 81): room type, seats, observers,
/// tournament format. The selected one takes the mod's colour, so the colour is handed in rather
/// than read from a resource: a dialog can be about another mod than the one on screen.
/// </summary>
internal sealed class SegmentRow : Grid
{
    private readonly List<(SegmentOption Option, Button Button)> _items = new();
    private object? _selected;
    private Color _accent = Color.FromRgb(0x2F, 0x7F, 0xE0);

    /// <summary>The space between segments, in pixels (6, or 4 in the row of seven seats).</summary>
    public double Gap { get; set; } = 6;

    public double SegmentHeight { get; set; } = 34;

    /// <summary>Raised when the player picks a segment, not when the code does.</summary>
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

    public void SetOptions(IReadOnlyList<SegmentOption> options)
    {
        Children.Clear();
        ColumnDefinitions.Clear();
        _items.Clear();
        for (var i = 0; i < options.Count; i++)
        {
            ColumnDefinitions.Add(new ColumnDefinition());
            var button = Build(options[i]);
            button.Margin = new Thickness(i == 0 ? 0 : Gap / 2, 0, i == options.Count - 1 ? 0 : Gap / 2, 0);
            SetColumn(button, i);
            Children.Add(button);
            _items.Add((options[i], button));
        }
        Repaint();
    }

    private Button Build(SegmentOption option)
    {
        var button = new Button { Height = SegmentHeight };
        button.SetResourceReference(StyleProperty, "SegmentButton");
        var key = option.Key;
        button.Click += (_, _) =>
        {
            Selected = key;
            Picked?.Invoke(key);
        };
        if (option.Sub == null)
        {
            button.Content = option.Text;
            return button;
        }
        var title = new TextBlock { Text = option.Text, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        title.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
        var sub = new TextBlock { Text = option.Sub, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
        sub.SetResourceReference(TextBlock.FontSizeProperty, "SetCaptionSize");
        ModDialogChrome.SetTightLine(1.1, title, sub);
        var stack = new StackPanel();
        stack.Children.Add(title);
        stack.Children.Add(sub);
        button.Content = stack;
        button.FontFamily = (FontFamily?)Application.Current?.TryFindResource("BodyFont") ?? button.FontFamily;
        return button;
    }

    private void Repaint()
    {
        foreach (var (option, button) in _items) Paint(button, Equals(option.Key, _selected), option.Gold, _accent);
    }

    /// <summary>The look of one segment; a loose pill of the same family (a copy) borrows it.</summary>
    internal static void Paint(Button button, bool on, bool gold, Color accent)
    {
        button.Tag = on ? "active" : null;
        var look = Look(gold, on, accent);
        button.Background = look.Fill;
        button.BorderBrush = new SolidColorBrush(look.Rim);
        button.Foreground = new SolidColorBrush(look.Text);
        if (button.Content is StackPanel { Children.Count: 2 } stack)
        {
            ((TextBlock)stack.Children[0]).Foreground = button.Foreground;
            ((TextBlock)stack.Children[1]).Foreground = new SolidColorBrush(look.Sub);
        }
    }

    /// <summary>The four looks of the prototype's <c>seg()</c> and its gold variant.</summary>
    private static (Brush Fill, Color Rim, Color Text, Color Sub) Look(bool gold, bool on, Color accent)
    {
        if (gold && on)
        {
            var fill = new LinearGradientBrush(Color.FromRgb(0xD9, 0xB2, 0x5A), Color.FromRgb(0xB8, 0x90, 0x2F), 90);
            return (fill, Color.FromArgb(0xE6, 0xFF, 0xE2, 0x96), Color.FromRgb(0x1C, 0x14, 0x04), Color.FromRgb(0x3A, 0x2A, 0x08));
        }
        if (gold)
        {
            var mixed = ModAccent.Mix(Color.FromRgb(0xE6, 0xC0, 0x6A), ModAccent.Navy, .86);
            return (new SolidColorBrush(mixed), Color.FromArgb(0x80, 0xE6, 0xC0, 0x6A), Color.FromRgb(0xF2, 0xD9, 0x8F), Color.FromRgb(0xE6, 0xC0, 0x6A));
        }
        if (on)
        {
            return (new SolidColorBrush(ModAccent.SelectionFill(accent)), Color.FromArgb(0xA6, accent.R, accent.G, accent.B),
                    Colors.White, Color.FromRgb(0xDC, 0xE7, 0xF5));
        }
        return (new SolidColorBrush(Color.FromRgb(0x0D, 0x18, 0x28)), Color.FromArgb(0x38, 0x82, 0xAF, 0xFF),
                Color.FromRgb(0xC3, 0xD2, 0xE5), Color.FromRgb(0x8C, 0x9C, 0xB1));
    }
}
