using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// v0.9 mod browser — replaces the "Mods" top-tab placeholder. Renders a
/// card per <see cref="ModProfile"/> coming from <see cref="Services.ModRegistry.All"/>
/// (built-ins + community catalog merged). MainWindow drives population
/// so the browser stays decoupled from the runtime services it would
/// otherwise need to reach for (active profile id, install-state probe).
/// </summary>
public partial class ModsBrowser : UserControl
{
    /// <summary>
    /// Raised when the user clicks a mod card. MainWindow handles it by
    /// switching the active profile (MVP) or — once Commit 5 lands — by
    /// opening the detail panel.
    /// </summary>
    public event EventHandler<ModProfile>? CardClicked;

    public ModsBrowser()
    {
        InitializeComponent();
    }

    public string HeaderTitleText
    {
        get => HeaderTitle.Text;
        set => HeaderTitle.Text = value;
    }

    public string HeaderSubtitleText
    {
        get => HeaderSubtitle.Text;
        set => HeaderSubtitle.Text = value;
    }

    public string EmptyMessage
    {
        get => EmptyText.Text;
        set => EmptyText.Text = value;
    }

    /// <summary>
    /// Replaces the visible card list. Called by MainWindow on first show
    /// and after every event that can change which mod is active or which
    /// community mods are catalog-visible.
    /// </summary>
    /// <param name="profiles">The mods to render.</param>
    /// <param name="activeId">Currently active profile id (highlighted card).</param>
    /// <param name="uiLanguage">User's launcher language for localised descriptions.</param>
    /// <param name="probeStateText">
    /// Callback that returns "Installed (vX)" / "Not installed" / etc. for
    /// the given profile. Provided by MainWindow so we reuse its existing
    /// ProbeInstalledState logic instead of duplicating disk probes here.
    /// </param>
    public void Populate(
        IEnumerable<ModProfile> profiles,
        string activeId,
        string uiLanguage,
        Func<ModProfile, string> probeStateText)
    {
        CardsPanel.Children.Clear();
        int count = 0;
        foreach (var profile in profiles)
        {
            CardsPanel.Children.Add(BuildCard(profile, activeId, uiLanguage, probeStateText));
            count++;
        }
        EmptyText.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private FrameworkElement BuildCard(
        ModProfile profile,
        string activeId,
        string lang,
        Func<ModProfile, string> probeStateText)
    {
        bool isActive = string.Equals(profile.Id, activeId, StringComparison.OrdinalIgnoreCase);
        var accentBrush = ParseColorBrush(profile.AccentColor)
            ?? (Brush)FindResource("BgNeutral");

        // Icon column: 56x56 disc tinted with the mod's accent, monogram
        // overlay until we wire the cached banner / icon image in a later
        // commit. Keeps initial render zero-disk so the grid paints instantly.
        var monogram = new TextBlock
        {
            Text = string.IsNullOrEmpty(profile.DisplayName)
                ? "?"
                : profile.DisplayName[..1].ToUpperInvariant(),
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var icon = new Border
        {
            Width = 56,
            Height = 56,
            CornerRadius = new CornerRadius(28),
            Background = accentBrush,
            VerticalAlignment = VerticalAlignment.Top,
            Child = monogram,
        };

        var titleText = new TextBlock
        {
            Text = profile.DisplayName,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimary"),
            TextWrapping = TextWrapping.Wrap,
        };
        var authorText = new TextBlock
        {
            Text = profile.Author ?? "",
            FontSize = 10,
            Foreground = (Brush)FindResource("TextSecondary"),
            Margin = new Thickness(0, 2, 0, 6),
            Visibility = string.IsNullOrWhiteSpace(profile.Author)
                ? Visibility.Collapsed
                : Visibility.Visible,
        };
        var descText = new TextBlock
        {
            Text = ResolveDescription(profile, lang),
            FontSize = 11,
            Foreground = (Brush)FindResource("TextPrimary"),
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 48,
            Margin = new Thickness(0, 0, 0, 8),
        };
        var stateText = new TextBlock
        {
            Text = probeStateText(profile),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = accentBrush,
        };

        var right = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
        right.Children.Add(titleText);
        right.Children.Add(authorText);
        right.Children.Add(descText);
        right.Children.Add(stateText);

        var inner = new Grid();
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(icon, 0);
        Grid.SetColumn(right, 1);
        inner.Children.Add(icon);
        inner.Children.Add(right);

        var card = new Border
        {
            Width = 320,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(isActive ? 2 : 1),
            BorderBrush = isActive ? accentBrush : (Brush)FindResource("BorderSubtle"),
            Background = (Brush)FindResource("BgPanel"),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 14, 14),
            Cursor = Cursors.Hand,
            Tag = profile,
            Child = inner,
        };

        // Click handlers + hover effects ship in the next commit; the Tag
        // already carries the ModProfile so wiring them is just a couple
        // of event lines.
        return card;
    }

    private static string ResolveDescription(ModProfile profile, string lang)
    {
        if (profile.Description != null)
        {
            var key = string.IsNullOrEmpty(lang) ? "en" : lang;
            if (profile.Description.TryGetValue(key, out var localized)
                && !string.IsNullOrWhiteSpace(localized))
                return localized;
            if (profile.Description.TryGetValue("en", out var en)
                && !string.IsNullOrWhiteSpace(en))
                return en;
        }
        return profile.Subtitle ?? "";
    }

    private static Brush? ParseColorBrush(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var b = new SolidColorBrush(color);
            b.Freeze();
            return b;
        }
        catch
        {
            return null;
        }
    }
}
