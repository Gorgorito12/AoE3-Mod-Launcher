using System;
using System.Collections.Generic;
using System.Linq;
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

    // Cached inputs from the last Populate() call so the filter handlers
    // can re-render without making MainWindow re-supply the data each
    // keystroke. ModRegistry.All is the source of truth; we only stash
    // a snapshot so search/installed filtering stays in this control.
    private IReadOnlyList<ModProfile> _allProfiles = Array.Empty<ModProfile>();
    private string _activeId = "";
    private string _uiLanguage = "";
    private Func<ModProfile, string>? _probeStateText;

    public ModsBrowser()
    {
        InitializeComponent();
        SearchBox.TextChanged += (_, _) =>
        {
            SearchPlaceholderText.Visibility =
                string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            ApplyFilters();
        };
        OnlyInstalledToggle.Checked += (_, _) => ApplyFilters();
        OnlyInstalledToggle.Unchecked += (_, _) => ApplyFilters();
    }

    /// <summary>Placeholder text shown inside the search box when empty.</summary>
    public string SearchPlaceholder
    {
        get => SearchPlaceholderText.Text;
        set => SearchPlaceholderText.Text = value;
    }

    /// <summary>Label next to the "only installed" checkbox.</summary>
    public string OnlyInstalledLabel
    {
        get => (string)(OnlyInstalledToggle.Content ?? "");
        set => OnlyInstalledToggle.Content = value;
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
        _allProfiles = profiles.ToList();
        _activeId = activeId ?? "";
        _uiLanguage = uiLanguage ?? "";
        _probeStateText = probeStateText;
        ApplyFilters();
    }

    /// <summary>
    /// (Re)renders the card grid against the cached profile list, filtered
    /// by the search box query (case-insensitive substring on display name
    /// + author) and — when the toggle is on — the install-state probe.
    /// Called by the input handlers and by Populate(); cheap because cards
    /// are plain Borders with frozen brushes.
    /// </summary>
    private void ApplyFilters()
    {
        if (_probeStateText is null)
        {
            CardsPanel.Children.Clear();
            EmptyText.Visibility = Visibility.Collapsed;
            return;
        }

        var notInstalledMarker = GetNotInstalledMarker();
        var query = (SearchBox.Text ?? "").Trim();
        bool onlyInstalled = OnlyInstalledToggle.IsChecked == true;

        CardsPanel.Children.Clear();
        int count = 0;
        foreach (var profile in _allProfiles)
        {
            if (!MatchesQuery(profile, query)) continue;
            string state = _probeStateText(profile);
            if (onlyInstalled && IsNotInstalled(state, notInstalledMarker)) continue;
            CardsPanel.Children.Add(
                BuildCard(profile, _activeId, _uiLanguage, _ => state));
            count++;
        }
        EmptyText.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool MatchesQuery(ModProfile profile, string query)
    {
        if (string.IsNullOrEmpty(query)) return true;
        string name = profile.DisplayName ?? "";
        string author = profile.Author ?? "";
        return name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || author.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// We don't have a structured install-state enum at the browser layer
    /// — MainWindow's <c>ProbeInstalledState</c> returns localised strings.
    /// To classify "installed vs not" we sample the marker once (by passing
    /// an obviously-uninstalled profile id is overkill; instead we let
    /// MainWindow inject the marker via <see cref="NotInstalledStateText"/>).
    /// </summary>
    public string NotInstalledStateText { get; set; } = "";

    private string GetNotInstalledMarker() => NotInstalledStateText;

    private static bool IsNotInstalled(string state, string marker)
    {
        if (string.IsNullOrEmpty(marker)) return false;
        return string.Equals(state, marker, StringComparison.Ordinal);
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

        var bgIdle = (Brush)FindResource("BgPanel");
        var bgHover = (Brush)FindResource("BgPanelAlt");
        var borderIdle = (Brush)FindResource("BorderSubtle");

        var card = new Border
        {
            Width = 320,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(isActive ? 2 : 1),
            BorderBrush = isActive ? accentBrush : borderIdle,
            Background = bgIdle,
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 14, 14),
            Cursor = Cursors.Hand,
            Tag = profile,
            Child = inner,
        };

        // Hover paints the alt-panel background and bumps the border to
        // the accent so inactive cards still feel tappable. The active
        // card keeps its accent border permanently, so we leave its
        // hover state alone (skip-if-active) to avoid a flicker.
        card.MouseEnter += (_, _) =>
        {
            if (IsActive(card)) return;
            card.Background = bgHover;
            card.BorderBrush = accentBrush;
        };
        card.MouseLeave += (_, _) =>
        {
            if (IsActive(card)) return;
            card.Background = bgIdle;
            card.BorderBrush = borderIdle;
        };
        // Fire on mouse-down to match the top-strip tiles. CardClicked
        // is raised even for the active card — consumers (MainWindow)
        // decide whether to no-op or, in commit 5, open the detail panel.
        card.MouseLeftButtonDown += (_, _) =>
        {
            CardClicked?.Invoke(this, profile);
        };

        return card;
    }

    private bool IsActive(Border card)
    {
        // The card is active when its border thickness matches the active
        // branch in BuildCard. Simpler than passing activeId around and
        // good enough since the only way a card flips active is via a
        // full Populate() rebuild.
        return card.BorderThickness.Left >= 2;
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
