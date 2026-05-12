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
    /// opening the detail panel via <see cref="ShowDetail"/>.
    /// </summary>
    public event EventHandler<ModProfile>? CardClicked;

    /// <summary>
    /// Raised from the detail panel's primary action when the user wants
    /// to make the displayed mod active. MainWindow forwards it to the
    /// existing LoadModProfile flow.
    /// </summary>
    public event EventHandler<ModProfile>? SwitchActiveRequested;

    /// <summary>
    /// Raised from the detail panel when the user wants to visit the mod's
    /// official website. MainWindow opens the URL in the default browser.
    /// </summary>
    public event EventHandler<string>? OpenWebsiteRequested;

    /// <summary>
    /// Raised from the detail panel when the user wants to install the
    /// displayed mod. MainWindow builds a fresh UpdateService for that
    /// profile and calls InstallAsync(service) — the active mod stays put.
    /// </summary>
    public event EventHandler<ModProfile>? InstallRequested;

    /// <summary>
    /// Raised from the detail panel when the user wants to uninstall the
    /// displayed mod. MainWindow resolves the per-mod install path and
    /// runs UninstallService.UninstallAsync against this profile only.
    /// </summary>
    public event EventHandler<ModProfile>? UninstallRequested;

    /// <summary>
    /// Raised when the user clicks the "Publish my mod" button in the
    /// header. MainWindow opens <see cref="PublishModDialog"/> in
    /// response. The wizard's forms / JSON generation arrive in commit 8.
    /// </summary>
    public event EventHandler? PublishRequested;

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
        DetailBackButton.Click += (_, _) => HideDetail();
        PublishButton.Click += (_, _) => PublishRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Label shown on the header "Publish my mod" button.</summary>
    public string PublishButtonLabel
    {
        get => (string)(PublishButton.Content ?? "");
        set => PublishButton.Content = value;
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

    /// <summary>Breadcrumb shown next to the back arrow in the detail panel.</summary>
    public string DetailBreadcrumbText { get; set; } = "";

    /// <summary>Primary action label in the detail panel ("Switch to this mod").</summary>
    public string DetailSwitchActiveLabel { get; set; } = "Switch to this mod";

    /// <summary>Secondary action label ("Open website").</summary>
    public string DetailOpenWebsiteLabel { get; set; } = "Open website";

    /// <summary>Localised label for the InstallType / UpdateMechanism rows.</summary>
    public string DetailInstallTypeLabel { get; set; } = "Install type";
    public string DetailUpdateMechLabel { get; set; } = "Updates";
    public string DetailWebsiteLabel { get; set; } = "Website";
    public string DetailActiveLabel { get; set; } = "Active mod";
    public string DetailInstallLabel { get; set; } = "Install";
    public string DetailUninstallLabel { get; set; } = "Uninstall";

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
        // Keep the detail overlay in sync after a refresh: pick the matching
        // profile from the new snapshot so renamed display names / changed
        // descriptions / new install paths show up without the user having
        // to close and re-open the panel. If the previously-shown profile
        // disappeared from the list (catalog rebuild evicted it), close the
        // overlay so we don't show stale data.
        if (_detailProfile is not null)
        {
            var match = _allProfiles.FirstOrDefault(p =>
                string.Equals(p.Id, _detailProfile.Id, StringComparison.OrdinalIgnoreCase));
            if (match is null) HideDetail();
            else ShowDetail(match);
        }
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
        // Click opens the detail panel for that mod. The legacy "click =
        // switch active mod" behaviour now lives behind the detail panel's
        // primary action button — see ShowDetail. CardClicked still fires
        // so MainWindow can drive richer flows (e.g. analytics, deep links).
        card.MouseLeftButtonDown += (_, _) =>
        {
            CardClicked?.Invoke(this, profile);
            ShowDetail(profile);
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

    // ------------------------------------------------------------------------
    // Detail panel
    // ------------------------------------------------------------------------

    private ModProfile? _detailProfile;

    /// <summary>Currently displayed profile inside the detail overlay, or null if hidden.</summary>
    public ModProfile? DetailProfile => _detailProfile;

    public bool IsDetailVisible => DetailOverlay.Visibility == Visibility.Visible;

    /// <summary>
    /// Populates and reveals the detail overlay for <paramref name="profile"/>.
    /// Safe to call repeatedly: each call rebuilds the action bar so callers
    /// (MainWindow) can refresh it after install / uninstall / mod-switch.
    /// </summary>
    public void ShowDetail(ModProfile profile)
    {
        _detailProfile = profile;
        if (_probeStateText is null) return;

        var accent = ParseColorBrush(profile.AccentColor) ?? (Brush)FindResource("AccentBrush");
        DetailIcon.Background = accent;
        DetailMonogram.Text = string.IsNullOrEmpty(profile.DisplayName)
            ? "?"
            : profile.DisplayName[..1].ToUpperInvariant();
        DetailTitle.Text = profile.DisplayName;
        DetailAuthor.Text = profile.Author ?? "";
        DetailAuthor.Visibility = string.IsNullOrWhiteSpace(profile.Author)
            ? Visibility.Collapsed
            : Visibility.Visible;

        bool isActive = string.Equals(profile.Id, _activeId, StringComparison.OrdinalIgnoreCase);
        DetailStateBadge.Text = isActive
            ? DetailActiveLabel + " · " + _probeStateText(profile)
            : _probeStateText(profile);
        DetailStateBadge.Foreground = accent;

        DetailDescription.Text = ResolveDescription(profile, _uiLanguage);
        DetailDescription.Visibility = string.IsNullOrWhiteSpace(DetailDescription.Text)
            ? Visibility.Collapsed
            : Visibility.Visible;

        DetailBreadcrumb.Text = DetailBreadcrumbText;
        BuildDetailMeta(profile);
        BuildDetailActions(profile, isActive);

        DetailOverlay.Visibility = Visibility.Visible;
    }

    /// <summary>Hides the detail overlay and clears the cached profile.</summary>
    public void HideDetail()
    {
        DetailOverlay.Visibility = Visibility.Collapsed;
        _detailProfile = null;
    }

    private void BuildDetailMeta(ModProfile profile)
    {
        DetailMetaPanel.Children.Clear();
        DetailMetaPanel.Children.Add(BuildMetaRow(
            DetailInstallTypeLabel,
            FormatInstallType(profile.InstallType)));
        DetailMetaPanel.Children.Add(BuildMetaRow(
            DetailUpdateMechLabel,
            FormatUpdateMechanism(profile.UpdateMechanism)));
        if (!string.IsNullOrWhiteSpace(profile.OfficialWebsite))
        {
            DetailMetaPanel.Children.Add(BuildMetaRow(
                DetailWebsiteLabel,
                profile.OfficialWebsite));
        }
    }

    private FrameworkElement BuildMetaRow(string label, string value)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lbl = new TextBlock
        {
            Text = label,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextSecondary"),
        };
        var val = new TextBlock
        {
            Text = value,
            FontSize = 12,
            Foreground = (Brush)FindResource("TextPrimary"),
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetColumn(lbl, 0);
        Grid.SetColumn(val, 1);
        row.Children.Add(lbl);
        row.Children.Add(val);
        return row;
    }

    private static string FormatInstallType(ModInstallType t) => t switch
    {
        ModInstallType.IsolatedFolder => "Isolated folder",
        ModInstallType.InPlaceOverlay => "In-place overlay",
        _ => t.ToString(),
    };

    private static string FormatUpdateMechanism(ModUpdateMechanism m) => m switch
    {
        ModUpdateMechanism.WolPatcher => "WoL patcher (UpdateInfo.xml)",
        ModUpdateMechanism.GitHubReleases => "GitHub Releases",
        ModUpdateMechanism.DelegatedExternal => "External updater",
        ModUpdateMechanism.Manual => "Manual",
        _ => m.ToString(),
    };

    private void BuildDetailActions(ModProfile profile, bool isActive)
    {
        DetailActionPanel.Children.Clear();
        bool isInstalled = !IsNotInstalled(
            _probeStateText!(profile), GetNotInstalledMarker());

        // Open website (secondary). Visible only when the profile has a URL.
        if (!string.IsNullOrWhiteSpace(profile.OfficialWebsite))
        {
            var webBtn = BuildSecondaryButton(DetailOpenWebsiteLabel);
            webBtn.Click += (_, _) =>
                OpenWebsiteRequested?.Invoke(this, profile.OfficialWebsite);
            DetailActionPanel.Children.Add(webBtn);
        }

        // Install / Uninstall (secondary). Mutually exclusive — driven by
        // the probe state passed in from MainWindow. Both fire events that
        // let MainWindow build an off-active UpdateService for the target.
        if (isInstalled)
        {
            var uninBtn = BuildSecondaryButton(DetailUninstallLabel);
            uninBtn.Click += (_, _) =>
                UninstallRequested?.Invoke(this, profile);
            DetailActionPanel.Children.Add(uninBtn);
        }
        else
        {
            var instBtn = BuildSecondaryButton(DetailInstallLabel);
            instBtn.Click += (_, _) =>
                InstallRequested?.Invoke(this, profile);
            DetailActionPanel.Children.Add(instBtn);
        }

        // Switch active (primary). Skipped when this card already represents
        // the active mod — the badge above already says so.
        if (!isActive)
        {
            var accent = ParseColorBrush(profile.AccentColor) ?? (Brush)FindResource("AccentBrush");
            var primary = BuildPrimaryButton(DetailSwitchActiveLabel, accent);
            primary.Click += (_, _) =>
                SwitchActiveRequested?.Invoke(this, profile);
            DetailActionPanel.Children.Add(primary);
        }
    }

    private Button BuildSecondaryButton(string label)
    {
        return new Button
        {
            Content = label,
            Background = (Brush)FindResource("BgPanelAlt"),
            Foreground = (Brush)FindResource("TextPrimary"),
            BorderBrush = (Brush)FindResource("BorderSubtle"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 8, 16, 8),
            Margin = new Thickness(0, 0, 10, 0),
            Cursor = Cursors.Hand,
            FontSize = 12,
        };
    }

    private Button BuildPrimaryButton(string label, Brush accent)
    {
        return new Button
        {
            Content = label,
            Background = accent,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(20, 8, 20, 8),
            Cursor = Cursors.Hand,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
        };
    }
}
