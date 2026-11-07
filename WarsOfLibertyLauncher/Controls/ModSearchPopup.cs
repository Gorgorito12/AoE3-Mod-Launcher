using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// The list of a mod picker that can hold every mod in the catalogue (design 81): a search box over
/// two groups, what is installed here and the rest.
/// </summary>
internal static class ModSearchPopup
{
    /// <summary>The mods a search leaves, split into installed and others, each in the order given.</summary>
    internal static (IReadOnlyList<ModProfile> Installed, IReadOnlyList<ModProfile> Other) Split(
        IEnumerable<ModProfile> mods, Func<ModProfile, bool> isInstalled, string? query)
    {
        var text = (query ?? "").Trim();
        var found = mods.Where(m => text.Length == 0
            || m.DisplayName.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
        return (found.Where(isInstalled).ToList(), found.Where(m => !isInstalled(m)).ToList());
    }

    internal static Popup Build(
        FrameworkElement target,
        IReadOnlyList<ModProfile> mods,
        Func<ModProfile, bool> isInstalled,
        ModProfile? selected,
        Func<ModProfile, Brush?> iconOf,
        Func<ModProfile, Color> accentOf,
        Action<ModProfile> pick)
    {
        var box = new TextBox();
        box.SetResourceReference(FrameworkElement.StyleProperty, "DialogBareText");
        box.SetResourceReference(Control.FontSizeProperty, "SetRowDescSize");
        var hint = new TextBlock { Text = Strings.Get("NtSearchMod"), IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center };
        hint.SetResourceReference(TextBlock.FontSizeProperty, "SetRowDescSize");
        hint.SetResourceReference(TextBlock.ForegroundProperty, "WindowFineText");
        var glyph = new TextBlock
        {
            Text = "",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8C, 0x9C, 0xB1)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var field = new Grid { Margin = new Thickness(8, 0, 0, 0) };
        field.Children.Add(box);
        field.Children.Add(hint);
        Grid.SetColumn(field, 1);
        var line = new Grid();
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition());
        line.Children.Add(glyph);
        line.Children.Add(field);
        var search = new Border
        {
            Height = 34,
            Padding = new Thickness(10, 0, 10, 0),
            CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1),
            Child = line,
        };
        search.SetResourceReference(Border.BackgroundProperty, "MpField");
        search.SetResourceReference(Border.BorderBrushProperty, "WindowSecondaryRim");

        var rows = new StackPanel();
        var list = new ScrollViewer
        {
            MaxHeight = 290,
            Margin = new Thickness(0, 4, 0, 0),
            Padding = new Thickness(0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = rows,
        };
        var body = new StackPanel { Width = target.ActualWidth - 14 };
        body.Children.Add(search);
        body.Children.Add(list);

        var card = new Border
        {
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Child = body,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 36, ShadowDepth = 14, Direction = 270, Opacity = .45, Color = Colors.Black },
        };
        card.SetResourceReference(Border.BackgroundProperty, "LibraryMenuBg");
        card.SetResourceReference(Border.BorderBrushProperty, "GlassRim");
        var popup = new Popup
        {
            Child = card,
            PlacementTarget = target,
            Placement = PlacementMode.Bottom,
            VerticalOffset = 6,
            StaysOpen = false,
            AllowsTransparency = true,
        };

        ModProfile? first = null;
        void Choose(ModProfile mod)
        {
            popup.IsOpen = false;
            pick(mod);
        }
        void Fill()
        {
            rows.Children.Clear();
            var (installed, other) = Split(mods, isInstalled, box.Text);
            first = installed.FirstOrDefault() ?? other.FirstOrDefault();
            void Group(string key, IReadOnlyList<ModProfile> group)
            {
                if (group.Count == 0) return;
                rows.Children.Add(BuildHead(Strings.Get(key), group.Count));
                foreach (var mod in group)
                    rows.Children.Add(BuildRow(mod, ReferenceEquals(mod, selected), iconOf(mod), accentOf(mod), () => Choose(mod)));
            }
            Group("NtGroupInstalled", installed);
            Group("NtGroupOther", other);
            if (first != null) return;
            var none = new TextBlock
            {
                Text = Strings.Format("NtNoMod", box.Text.Trim()),
                Margin = new Thickness(10, 14, 10, 14),
                TextWrapping = TextWrapping.Wrap,
            };
            none.SetResourceReference(TextBlock.FontSizeProperty, "SetRowDescSize");
            none.SetResourceReference(TextBlock.ForegroundProperty, "UiMutedText");
            rows.Children.Add(none);
        }

        box.TextChanged += (_, _) =>
        {
            hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            Fill();
        };
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                popup.IsOpen = false;
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && first is { } mod)
            {
                Choose(mod);
                e.Handled = true;
            }
        };
        popup.Opened += (_, _) => box.Dispatcher.BeginInvoke(() => box.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        Fill();
        return popup;
    }

    private static TextBlock BuildHead(string title, int count)
    {
        var head = new TextBlock { FontWeight = FontWeights.Bold, Margin = new Thickness(10, 9, 10, 4) };
        head.Inlines.Add(new System.Windows.Documents.Run(title + " "));
        head.Inlines.Add(new System.Windows.Documents.Run(count.ToString())
        {
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            FontWeight = FontWeights.SemiBold,
        });
        head.SetResourceReference(TextBlock.FontSizeProperty, "SetGroupLabelSize");
        head.Foreground = new SolidColorBrush(Color.FromRgb(0x8C, 0x9C, 0xB1));
        return head;
    }

    private static Button BuildRow(ModProfile mod, bool on, Brush? icon, Color accent, Action pick)
    {
        var tile = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(6),
            Background = (Brush)Application.Current.FindResource("WindowSecondary"),
            Child = new Border { CornerRadius = new CornerRadius(6), Background = icon },
        };
        var name = new TextBlock
        {
            Text = mod.DisplayName,
            Margin = new Thickness(10, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = new SolidColorBrush(on ? Colors.White : Color.FromRgb(0xDC, 0xE7, 0xF5)),
        };
        name.SetResourceReference(TextBlock.FontSizeProperty, "DialogBodySize");
        Grid.SetColumn(name, 1);
        var line = new Grid();
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition());
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.Children.Add(tile);
        line.Children.Add(name);
        if (on)
        {
            var check = new TextBlock { Text = "✓", FontWeight = FontWeights.Bold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
            check.SetResourceReference(TextBlock.FontSizeProperty, "SetControlSize");
            Grid.SetColumn(check, 2);
            line.Children.Add(check);
        }
        var row = new Button { Content = line, Height = 38, Margin = new Thickness(0, 0, 0, 2), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        row.SetResourceReference(FrameworkElement.StyleProperty, "MenuRowButton");
        if (on) row.Background = new SolidColorBrush(ModAccent.SelectionFill(accent));
        row.Click += (_, _) => pick();
        return row;
    }
}
