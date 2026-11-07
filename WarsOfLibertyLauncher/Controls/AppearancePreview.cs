using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// The small Library at the top of Settings › Interface (design 69): it changes the moment a setting
/// does, so the player sees what a level of transparency means before going back to the real one.
/// </summary>
public sealed class AppearancePreview : Border
{
    private readonly Grid _root = new() { Height = 150 };

    public AppearancePreview()
    {
        CornerRadius = new CornerRadius(8);
        ClipToBounds = true;
        BorderThickness = new Thickness(1);
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x14, 255, 255, 255));
        Child = _root;
    }

    public void Show(ModProfile? mod, IReadOnlyList<ModProfile> rows, string glassLevel, bool showImage, bool useColor, bool reduced)
    {
        var accent = ModTheme.AccentOf(mod?.AccentColor, useColor);
        _root.Children.Clear();

        Brush? image = showImage && !reduced && mod != null
            ? MainWindow.TryLoadTileImage(mod.ResolveHeroSources().FirstOrDefault(), 640)
            : null;
        _root.Children.Add(new Border { Background = image ?? ModTheme.QuietBackground(accent) });

        var shade = new LinearGradientBrush { StartPoint = new Point(0, .5), EndPoint = new Point(1, .5) };
        shade.GradientStops.Add(new GradientStop(Color.FromArgb(0xD9, 15, 28, 46), 0));
        shade.GradientStops.Add(new GradientStop(Color.FromArgb(0x8C, 15, 28, 46), .4));
        shade.GradientStops.Add(new GradientStop(Color.FromArgb(0x26, 15, 28, 46), 1));
        _root.Children.Add(new Rectangle { Fill = shade });

        var alpha = GlassTheme.Normalize(glassLevel) switch
        {
            GlassTheme.Solid => .86,
            GlassTheme.Maximum => .28,
            _ => .5,
        };
        _root.Children.Add(Rail(rows, mod, accent, alpha));
        _root.Children.Add(Copy(mod, accent, alpha));

        var caption = new Border
        {
            Height = 20,
            Padding = new Thickness(8, 0, 8, 0),
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(0x99, 10, 19, 33)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 10, 10, 0),
            Child = new TextBlock
            {
                Text = Strings.Get("DlgSetPreviewCaption"),
                FontSize = 9.5,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0xB9, 0xC9, 0xDE)),
            },
        };
        _root.Children.Add(caption);
    }

    private static UIElement Rail(IReadOnlyList<ModProfile> rows, ModProfile? selected, Color accent, double alpha)
    {
        var list = new StackPanel();
        foreach (var row in rows.Take(4))
        {
            var on = selected != null && row.Id == selected.Id;
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            line.Children.Add(new Border { Width = 16, Height = 16, Background = MainWindow.TryLoadTileImage(row.ResolveIconSource()) });
            line.Children.Add(new TextBlock
            {
                Text = row.DisplayName,
                Margin = new Thickness(7, 0, 0, 0),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 106,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xEE, 0xF6)),
            });
            list.Children.Add(new Border
            {
                Height = 26,
                Margin = new Thickness(0, 0, 0, 3),
                CornerRadius = new CornerRadius(5),
                Background = on ? new SolidColorBrush(Color.FromArgb(0x4D, accent.R, accent.G, accent.B)) : Brushes.Transparent,
                Child = line,
            });
        }
        return new Border
        {
            Width = 150,
            Margin = new Thickness(10),
            Padding = new Thickness(6),
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(Color.FromArgb((byte)(alpha * 255), 10, 19, 33)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x1A, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Child = list,
        };
    }

    private static UIElement Copy(ModProfile? mod, Color accent, double alpha)
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(178, 0, 0, 16),
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        var title = new TextBlock
        {
            Text = mod?.DisplayName ?? "",
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 8),
        };
        title.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        panel.Children.Add(title);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(new Border
        {
            Height = 26,
            Padding = new Thickness(14, 0, 14, 0),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(accent),
            Child = new TextBlock
            {
                Text = Strings.Get("LibPlay"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(ModAccent.OnAccent(accent)),
            },
        });
        buttons.Children.Add(new Border
        {
            Height = 26,
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(10, 0, 10, 0),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromArgb((byte)(System.Math.Min(.86, alpha + .2) * 255), 10, 19, 33)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x29, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = Strings.Get("DlgSetPreviewSettings"),
                FontSize = 11,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0xE7, 0xF5)),
            },
        });
        panel.Children.Add(buttons);
        return panel;
    }
}
