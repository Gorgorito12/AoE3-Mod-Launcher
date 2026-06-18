using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// Compact, single-frame status of a mod as seen by the catalog UI.
/// MainWindow builds this from its CheckResult cache, the per-mod
/// LauncherConfig.GetState, and the existing on-disk probe — the
/// browser then maps it to the right badge / button labels without
/// having to talk to runtime services itself.
/// </summary>
public enum ModRowStatus
{
    /// <summary>Not present on disk.</summary>
    NotInstalled,
    /// <summary>Installed and up to date.</summary>
    Installed,
    /// <summary>Installed but a newer version is available.</summary>
    UpdateAvailable,
    /// <summary>Cannot be installed in the current environment
    /// (missing AoE3 expansion, conflicting mod, ...).</summary>
    Incompatible,
    /// <summary>An install / update / detection error blocks this row.</summary>
    Error,
}

/// <summary>
/// Per-row state pushed in by MainWindow. Stays purely descriptive — no
/// disk probes, no services — so the browser can re-render synchronously
/// after any filter / sort change without a re-fetch.
/// </summary>
public sealed class ModRowState
{
    public ModRowStatus Status { get; init; }
    public string CurrentVersion { get; init; } = "";
    public string AvailableVersion { get; init; } = "";
    public bool IsActive { get; init; }
    /// <summary>Optional short reason string for Incompatible / Error rows.</summary>
    public string Note { get; init; } = "";
    /// <summary>
    /// Workshop redesign: true when the profile is in the user's
    /// personal mod collection (added via the Workshop's Add button
    /// or a built-in profile). Drives the per-row Add/Remove toggle.
    /// </summary>
    public bool IsInUserCollection { get; init; }
    /// <summary>
    /// True for hard-coded built-in profiles (currently only WoL).
    /// Built-ins always appear in the user's collection and can't be
    /// removed — the row button shows a disabled "Built-in" pill
    /// instead of Add/Remove.
    /// </summary>
    public bool IsBuiltIn { get; init; }
}

/// <summary>
/// v0.9.x catalog redesign — two-column layout. Left column is a
/// scrollable list of compact mod rows (icon + name + author + status
/// badge + version + actions); right column is a persistent detail
/// panel for the currently-selected row. Header carries the search box,
/// subtabs (Mis mods / Catálogo), filter chips, and sort selector.
///
/// MainWindow drives everything via the <see cref="Populate"/> method —
/// the browser caches the inputs so search / filter / sort interactions
/// re-render against the snapshot without bouncing back to the host.
/// </summary>
public partial class ModsBrowser : UserControl
{
    // ------------------------------------------------------------------------
    // Events consumed by MainWindow. The old per-mod action events
    // (Install/Update/Uninstall/Repair/Play/SwitchActiveRequested) were removed:
    // those flows live on the Dashboard now (PLAY state machine + gear menu); the
    // Workshop only browses, opens detail/website, toggles "my mods", refreshes
    // the catalog and opens the publish wizard.
    // ------------------------------------------------------------------------

    public event EventHandler<ModProfile>? CardClicked;
    public event EventHandler<string>? OpenWebsiteRequested;
    public event EventHandler? PublishRequested;
    public event EventHandler? RefreshCatalogRequested;
    public event EventHandler? AddLocalModRequested;

    /// <summary>Workshop "Add to my mods" button click.</summary>
    public event EventHandler<ModProfile>? AddToCollectionRequested;
    /// <summary>Workshop "Remove from my mods" button click.</summary>
    public event EventHandler<ModProfile>? RemoveFromCollectionRequested;
    // (RightClicked event removed — right-click on Workshop rows
    // no longer triggers a per-mod context popup. Per-mod admin
    // actions live in the dashboard gear button now.)

    // ------------------------------------------------------------------------
    // Filter / sort modes.
    // ------------------------------------------------------------------------

    public enum FilterMode { All, Installed, NotInstalled, Updates, Compatible }
    public enum SortMode { Recent, Name, Status }
    public enum SubTabMode { MyMods, Catalog }

    private FilterMode _filter = FilterMode.All;
    private SortMode _sort = SortMode.Recent;
    private SubTabMode _subTab = SubTabMode.Catalog;

    // ------------------------------------------------------------------------
    // Cached inputs from MainWindow.
    // ------------------------------------------------------------------------

    private IReadOnlyList<ModProfile> _allProfiles = Array.Empty<ModProfile>();
    private string _activeId = "";
    private string _uiLanguage = "";
    private Func<ModProfile, ModRowState>? _stateProvider;

    /// <summary>
    /// Asked by the detail panel to lazily fetch a mod's gallery screenshots
    /// (MainWindow wires this to <c>EnsureScreenshotsAsync</c>). When the
    /// download finishes, MainWindow calls <see cref="RefreshGallery"/>.
    /// </summary>
    public Action<ModProfile>? ScreenshotRequester { get; set; }

    private ModProfile? _selectedProfile;
    private readonly Dictionary<string, Border> _rowsByProfileId = new(StringComparer.OrdinalIgnoreCase);

    // Typography tokens from the App.xaml scale, resolved once. Cached
    // because BuildRow reads several per row and the values are app-lifetime
    // constants — same pattern the row builders use for the theme brushes.
    private readonly double _fsCaption;
    private readonly double _fsBody;
    private readonly double _fsBodyStrong;

    public ModsBrowser()
    {
        InitializeComponent();

        _fsCaption    = (double)FindResource("FontSizeCaption");
        _fsBody       = (double)FindResource("FontSizeBody");
        _fsBodyStrong = (double)FindResource("FontSizeBodyStrong");

        // Window-size scaling (Controls/UiScale.cs): the whole Workshop content
        // shrinks to fit windows smaller than MainWindow's default footprint
        // (ref 1100x604 → a default-sized window is exactly 1.0, no regression).
        // sizeSource is the UserControl itself: its size is set by the content
        // host and is NOT affected by the LayoutTransform on the content root,
        // so there's no measure feedback loop.
        if (Content is FrameworkElement modsRoot)
            UiScale.Attach(modsRoot, this, 1100, 604);

        // Search.
        SearchBox.TextChanged += (_, _) =>
        {
            SearchPlaceholderText.Visibility =
                string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            ApplyFilters();
        };

        // Subtabs.
        SubTabMyMods.Click += (_, _) => SetSubTab(SubTabMode.MyMods);
        SubTabCatalog.Click += (_, _) => SetSubTab(SubTabMode.Catalog);

        // Filter chips.
        FilterAll.Click          += (_, _) => SetFilter(FilterMode.All);
        FilterInstalled.Click    += (_, _) => SetFilter(FilterMode.Installed);
        FilterNotInstalled.Click += (_, _) => SetFilter(FilterMode.NotInstalled);
        FilterUpdates.Click      += (_, _) => SetFilter(FilterMode.Updates);
        FilterCompatible.Click   += (_, _) => SetFilter(FilterMode.Compatible);

        // Sort selector. Items added in code so SetSortItems() can map
        // localised strings while keeping the enum order canonical.
        SortBox.SelectionChanged += (_, _) =>
        {
            if (SortBox.SelectedItem is ComboBoxItem item && item.Tag is SortMode m)
            {
                _sort = m;
                ApplyFilters();
            }
        };

        // Header buttons.
        RefreshCatalogButton.Click += (_, _) => RefreshCatalogRequested?.Invoke(this, EventArgs.Empty);
        AddLocalModButton.Click += (_, _) => AddLocalModRequested?.Invoke(this, EventArgs.Empty);
        PublishModButton.Click += (_, _) => PublishRequested?.Invoke(this, EventArgs.Empty);
        MoreMenuButton.Click += (_, _) =>
        {
            // Manual open — Button's default Click doesn't pop the menu.
            if (MoreMenuButton.ContextMenu is null) return;
            MoreMenuButton.ContextMenu.PlacementTarget = MoreMenuButton;
            MoreMenuButton.ContextMenu.IsOpen = true;
        };

        // Default selection visual.
        SetFilter(FilterMode.All);
        SetSubTab(SubTabMode.Catalog);
    }

    // ------------------------------------------------------------------------
    // Public surface — localisable labels + the structured Populate API.
    // ------------------------------------------------------------------------

    public string HeaderTitleText { get => HeaderTitle.Text; set => HeaderTitle.Text = value; }
    public string HeaderSubtitleText { get => HeaderSubtitle.Text; set => HeaderSubtitle.Text = value; }
    public string SearchPlaceholder { get => SearchPlaceholderText.Text; set => SearchPlaceholderText.Text = value; }
    public string EmptyMessage { get => EmptyText.Text; set => EmptyText.Text = value; }
    public string DetailEmptyMessage { get => DetailEmptyText.Text; set => DetailEmptyText.Text = value; }
    public string ListSummaryFormat { get; set; } = "Available mods ({0})";

    public string RefreshCatalogLabel { get => (string)(RefreshCatalogButton.Content ?? ""); set => RefreshCatalogButton.Content = value; }
    public string AddLocalModLabel { get => (string)(AddLocalModButton.Content ?? ""); set => AddLocalModButton.Content = value; }
    public string PublishModLabel { get => (string)(PublishModButton.Content ?? ""); set => PublishModButton.Content = value; }
    public string SubTabMyModsLabel { get => (string)(SubTabMyMods.Content ?? ""); set => SubTabMyMods.Content = value; }
    public string SubTabCatalogLabel { get => (string)(SubTabCatalog.Content ?? ""); set => SubTabCatalog.Content = value; }
    public string FiltersLabelText { get => FiltersLabel.Text; set => FiltersLabel.Text = value; }
    public string SortLabelText { get => SortLabel.Text; set => SortLabel.Text = value; }

    /// <summary>Sets the chip labels. Order: All / Installed / NotInstalled / Updates / Compatible.</summary>
    public void SetFilterLabels(string all, string installed, string notInstalled, string updates, string compatible)
    {
        FilterAll.Content = all;
        FilterInstalled.Content = installed;
        FilterNotInstalled.Content = notInstalled;
        FilterUpdates.Content = updates;
        FilterCompatible.Content = compatible;
    }

    /// <summary>Sets the sort dropdown items. Order: Recent / Name / Status.</summary>
    public void SetSortItems(string recent, string name, string status)
    {
        SortBox.Items.Clear();
        SortBox.Items.Add(new ComboBoxItem { Content = recent, Tag = SortMode.Recent });
        SortBox.Items.Add(new ComboBoxItem { Content = name,   Tag = SortMode.Name });
        SortBox.Items.Add(new ComboBoxItem { Content = status, Tag = SortMode.Status });
        SortBox.SelectedIndex = 0;
    }

    /// <summary>
    /// Populates the header's three-dot context menu. Used by MainWindow
    /// to surface secondary actions (publish wizard, future telemetry
    /// toggles, …) that don't deserve their own button in the header.
    /// </summary>
    public void SetMoreMenuItems(params (string Label, Action OnClick)[] items)
    {
        MoreMenu.Items.Clear();
        foreach (var (label, onClick) in items)
        {
            var mi = new MenuItem { Header = label };
            mi.Click += (_, _) => onClick?.Invoke();
            MoreMenu.Items.Add(mi);
        }

        // An empty ⋮ would just open a blank popup, so hide the button
        // entirely when there's nothing in it (publish moved to its own
        // header button — the overflow is empty by default now).
        MoreMenuButton.Visibility = MoreMenu.Items.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    /// <summary>Detail panel action button labels — set per launcher language.</summary>
    public string DetailInstallLabel { get; set; } = "Install";
    public string DetailUpdateLabel { get; set; } = "Update";
    public string DetailPlayLabel { get; set; } = "Play";
    public string DetailRepairLabel { get; set; } = "Repair";
    public string DetailIncompatibleLabel { get; set; } = "Incompatible";
    public string DetailViewWebsiteLabel { get; set; } = "View mod page";
    public string DetailSwitchActiveLabel { get; set; } = "Set as active mod";
    public string DetailUninstallLabel { get; set; } = "Uninstall";

    /// <summary>
    /// Workshop redesign — per-row button labels for the user's
    /// personal mod collection. Workshop doesn't install/update/repair
    /// anymore; it just adds/removes profiles from the user's list.
    /// All maintenance lives on the Dashboard.
    /// </summary>
    public string BtnAddToCollectionLabel { get; set; } = "Add to my mods";
    public string BtnRemoveFromCollectionLabel { get; set; } = "Remove from my mods";
    public string BtnBuiltinLabel { get; set; } = "Built-in";

    /// <summary>Status badge labels (localised text shown inside each badge).</summary>
    public string BadgeNotInstalled { get; set; } = "No instalado";
    public string BadgeInstalled { get; set; } = "Instalado";
    public string BadgeUpdateAvailable { get; set; } = "Actualización disponible";
    public string BadgeIncompatible { get; set; } = "Incompatible";
    public string BadgeError { get; set; } = "Error";

    /// <summary>Labels used in the detail metadata grid.</summary>
    public string DetailDeveloperLabel { get; set; } = "Developer";
    public string DetailVersionLabel { get; set; } = "Version";
    public string DetailAvailableVersionLabel { get; set; } = "Available";
    public string DetailInstallTypeLabel { get; set; } = "Install type";
    public string DetailUpdateMechLabel { get; set; } = "Updates";
    public string DetailWebsiteLabel { get; set; } = "Website";
    public string DetailLanguagesLabel { get; set; } = "Languages";
    public string DetailFeaturesTitleText { get; set; } = "Features";
    public string GalleryTitleText { get; set; } = "Screenshots";

    /// <summary>
    /// Replaces the visible list. <paramref name="stateProvider"/> is
    /// asked for a per-profile <see cref="ModRowState"/> each render —
    /// MainWindow caches what it needs and returns synchronously so
    /// filter / sort interactions repaint without disk I/O.
    /// </summary>
    public void Populate(
        IEnumerable<ModProfile> profiles,
        string activeId,
        string uiLanguage,
        Func<ModProfile, ModRowState> stateProvider)
    {
        _allProfiles = profiles.ToList();
        _activeId = activeId ?? "";
        _uiLanguage = uiLanguage ?? "";
        _stateProvider = stateProvider;
        ApplyFilters();

        // Re-render the detail panel against the new snapshot so badges,
        // buttons, and the description follow whatever just changed (mod
        // switch, catalog refresh, install). If the previously-selected
        // profile is no longer in the list, blank out the right pane.
        if (_selectedProfile is not null)
        {
            var match = _allProfiles.FirstOrDefault(p =>
                string.Equals(p.Id, _selectedProfile.Id, StringComparison.OrdinalIgnoreCase));
            if (match is null) ClearDetail();
            else ShowDetail(match);
        }
        else
        {
            // Auto-select the active mod on first paint so the user lands
            // on something meaningful instead of an empty right pane.
            var activeProfile = _allProfiles.FirstOrDefault(p =>
                string.Equals(p.Id, _activeId, StringComparison.OrdinalIgnoreCase));
            if (activeProfile is not null) ShowDetail(activeProfile);
        }
    }

    // ------------------------------------------------------------------------
    // Filter / sort / render.
    // ------------------------------------------------------------------------

    private void SetFilter(FilterMode mode)
    {
        _filter = mode;
        PaintFilterChips();
        ApplyFilters();
    }

    private void SetSubTab(SubTabMode mode)
    {
        _subTab = mode;
        PaintSubTabs();
        ApplyFilters();
    }

    private void PaintFilterChips()
    {
        PaintChip(FilterAll,          _filter == FilterMode.All);
        PaintChip(FilterInstalled,    _filter == FilterMode.Installed);
        PaintChip(FilterNotInstalled, _filter == FilterMode.NotInstalled);
        PaintChip(FilterUpdates,      _filter == FilterMode.Updates);
        PaintChip(FilterCompatible,   _filter == FilterMode.Compatible);
    }

    private void PaintChip(Button chip, bool selected)
    {
        if (selected)
        {
            chip.Background = (Brush)FindResource("CatalogBlue");
            chip.Foreground = Brushes.White;
            chip.BorderBrush = (Brush)FindResource("CatalogBlue");
        }
        else
        {
            chip.Background = (Brush)FindResource("BgPanelAlt");
            chip.Foreground = (Brush)FindResource("TextPrimary");
            chip.BorderBrush = (Brush)FindResource("BorderSubtle");
        }
    }

    private void PaintSubTabs()
    {
        var active = (Brush)FindResource("CatalogBlue");
        var inactive = (Brush)FindResource("TextSecondary");
        SubTabMyMods.Foreground = _subTab == SubTabMode.MyMods ? Brushes.White : inactive;
        SubTabMyMods.BorderBrush = _subTab == SubTabMode.MyMods ? active : Brushes.Transparent;
        SubTabCatalog.Foreground = _subTab == SubTabMode.Catalog ? Brushes.White : inactive;
        SubTabCatalog.BorderBrush = _subTab == SubTabMode.Catalog ? active : Brushes.Transparent;
    }

    private void ApplyFilters()
    {
        RowsPanel.Children.Clear();
        _rowsByProfileId.Clear();
        if (_stateProvider is null)
        {
            EmptyText.Visibility = Visibility.Collapsed;
            ListSummary.Text = "";
            return;
        }

        var query = (SearchBox.Text ?? "").Trim();
        var items = _allProfiles
            .Select(p => (Profile: p, State: _stateProvider(p)))
            .Where(t => MatchesSubTab(t.State))
            .Where(t => MatchesFilter(t.State))
            .Where(t => MatchesQuery(t.Profile, query))
            .ToList();

        items = _sort switch
        {
            SortMode.Name => items.OrderBy(t => t.Profile.DisplayName, StringComparer.OrdinalIgnoreCase).ToList(),
            SortMode.Status => items.OrderBy(t => StatusOrder(t.State.Status)).ThenBy(t => t.Profile.DisplayName, StringComparer.OrdinalIgnoreCase).ToList(),
            _ => items, // Recent = catalog/registry order
        };

        foreach (var (profile, state) in items)
        {
            var row = BuildRow(profile, state);
            RowsPanel.Children.Add(row);
            _rowsByProfileId[profile.Id] = row;
        }
        ListSummary.Text = string.Format(ListSummaryFormat, items.Count);
        EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HighlightSelectedRow();
    }

    private bool MatchesSubTab(ModRowState s) => _subTab switch
    {
        SubTabMode.MyMods => s.Status != ModRowStatus.NotInstalled,
        _ => true,
    };

    private bool MatchesFilter(ModRowState s) => _filter switch
    {
        FilterMode.Installed    => s.Status == ModRowStatus.Installed || s.Status == ModRowStatus.UpdateAvailable,
        FilterMode.NotInstalled => s.Status == ModRowStatus.NotInstalled,
        FilterMode.Updates      => s.Status == ModRowStatus.UpdateAvailable,
        FilterMode.Compatible   => s.Status != ModRowStatus.Incompatible,
        _ => true,
    };

    private static bool MatchesQuery(ModProfile p, string query)
    {
        if (string.IsNullOrEmpty(query)) return true;
        return (p.DisplayName ?? "").Contains(query, StringComparison.OrdinalIgnoreCase)
            || (p.Author ?? "").Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static int StatusOrder(ModRowStatus s) => s switch
    {
        ModRowStatus.UpdateAvailable => 0,
        ModRowStatus.Installed => 1,
        ModRowStatus.NotInstalled => 2,
        ModRowStatus.Incompatible => 3,
        ModRowStatus.Error => 4,
        _ => 99,
    };

    // ------------------------------------------------------------------------
    // Row rendering.
    // ------------------------------------------------------------------------

    private Border BuildRow(ModProfile profile, ModRowState state)
    {
        var accentBrush = ParseColorBrush(profile.AccentColor) ?? (Brush)FindResource("BgNeutral");

        // 48x48 icon disc (smaller than the v0.9 cards) — leaves more
        // horizontal room for description + actions in the compact row.
        // ResolveIconUri also covers built-ins' packed icon so WoL etc. show
        // their real icon here instead of a letter monogram.
        var iconBg = TryLoadImageBrush(ResolveIconUri(profile));
        UIElement iconChild;
        Brush iconBack;
        if (iconBg != null)
        {
            iconChild = new Border();
            iconBack = iconBg;
        }
        else
        {
            iconChild = new TextBlock
            {
                Text = string.IsNullOrEmpty(profile.DisplayName) ? "?" : profile.DisplayName[..1].ToUpperInvariant(),
                // Disc-geometry, not a type-scale token — sized to fill the 48px circle.
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            iconBack = accentBrush;
        }
        var icon = new Border
        {
            Width = 48, Height = 48,
            CornerRadius = new CornerRadius(8),
            Background = iconBack,
            Child = iconChild,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 14, 0),
        };

        // Center stack: title, description, author.
        var titleText = new TextBlock
        {
            Text = profile.DisplayName,
            FontSize = _fsBodyStrong,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimary"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var descText = new TextBlock
        {
            Text = ResolveDescription(profile, _uiLanguage),
            FontSize = _fsBody,
            Foreground = (Brush)FindResource("TextSecondary"),
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            // Sized for two lines at the body token (~37px at 14px) plus
            // breathing room — keeps the second line's descenders from
            // clipping. Bump this if FontSizeBody grows again.
            MaxHeight = 42,
            Margin = new Thickness(0, 3, 0, 5),
        };
        var authorText = new TextBlock
        {
            Text = profile.Author ?? "",
            FontSize = _fsCaption,
            Foreground = (Brush)FindResource("TextSecondary"),
            Visibility = string.IsNullOrWhiteSpace(profile.Author) ? Visibility.Collapsed : Visibility.Visible,
        };
        var center = new StackPanel();
        center.Children.Add(titleText);
        center.Children.Add(descText);
        center.Children.Add(authorText);

        // Right stack: status badge, version, size, primary action.
        var badge = BuildStatusBadge(state.Status);
        badge.HorizontalAlignment = HorizontalAlignment.Right;
        badge.Margin = new Thickness(0, 0, 0, 6);

        var versionText = new TextBlock
        {
            Text = FormatVersionLine(state),
            FontSize = _fsCaption,
            Foreground = (Brush)FindResource("TextPrimary"),
            HorizontalAlignment = HorizontalAlignment.Right,
            Visibility = string.IsNullOrEmpty(FormatVersionLine(state)) ? Visibility.Collapsed : Visibility.Visible,
        };
        // Row-level primary action: install / update / play / repair /
        // disabled-incompatible. Single chip, no secondary button in the
        // compact row — the right pane has the full set.
        var rowAction = BuildRowAction(profile, state);
        rowAction.HorizontalAlignment = HorizontalAlignment.Right;
        rowAction.Margin = new Thickness(0, 8, 0, 0);

        var right = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0),
        };
        right.Children.Add(badge);
        right.Children.Add(versionText);
        right.Children.Add(rowAction);

        // Two-column inner grid: icon | (center expands) | right.
        var inner = new Grid();
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(icon, 0);
        Grid.SetColumn(center, 1);
        Grid.SetColumn(right, 2);
        inner.Children.Add(icon);
        inner.Children.Add(center);
        inner.Children.Add(right);

        var row = new Border
        {
            // RadiusMd (matches the App.xaml geometry token; card rows align
            // with every other card surface). The icon disc above keeps its
            // own rounding — it's a tile, not a card.
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)FindResource("BorderSubtle"),
            Background = (Brush)FindResource("BgPanel"),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 0, 0, 8),
            Cursor = Cursors.Hand,
            Tag = profile,
            Child = inner,
        };

        row.MouseEnter += (_, _) =>
        {
            if (IsSelected(row)) return;
            row.Background = (Brush)FindResource("BgPanelAlt");
        };
        row.MouseLeave += (_, _) =>
        {
            if (IsSelected(row)) return;
            row.Background = (Brush)FindResource("BgPanel");
        };
        row.MouseLeftButtonDown += (_, _) =>
        {
            CardClicked?.Invoke(this, profile);
            ShowDetail(profile);
        };
        // (Right-click handler removed — per-user redesign moved all
        // mod admin into the dashboard gear button's Administrar
        // submenu / Properties dialog. Workshop row left-click is
        // the only mouse interaction now.)

        return row;
    }

    private bool IsSelected(Border row)
    {
        if (_selectedProfile is null) return false;
        if (row.Tag is not ModProfile p) return false;
        return string.Equals(p.Id, _selectedProfile.Id, StringComparison.OrdinalIgnoreCase);
    }

    private void HighlightSelectedRow()
    {
        var blue = (Brush)FindResource("CatalogBlue");
        var subtle = (Brush)FindResource("BorderSubtle");
        var bgIdle = (Brush)FindResource("BgPanel");
        var bgSel = (Brush)FindResource("CatalogBlueSubtle");
        foreach (var row in _rowsByProfileId.Values)
        {
            bool selected = IsSelected(row);
            row.BorderBrush = selected ? blue : subtle;
            row.BorderThickness = new Thickness(selected ? 2 : 1);
            row.Background = selected ? bgSel : bgIdle;
        }
    }

    private string FormatVersionLine(ModRowState s)
    {
        if (s.Status == ModRowStatus.UpdateAvailable
            && !string.IsNullOrEmpty(s.CurrentVersion)
            && !string.IsNullOrEmpty(s.AvailableVersion))
        {
            return $"v{Strip(s.CurrentVersion)} → v{Strip(s.AvailableVersion)}";
        }
        if (!string.IsNullOrEmpty(s.CurrentVersion))
            return $"v{Strip(s.CurrentVersion)}";
        if (!string.IsNullOrEmpty(s.AvailableVersion))
            return $"v{Strip(s.AvailableVersion)}";
        return "";
    }

    private static string Strip(string v) => v.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? v[1..] : v;

    private Border BuildStatusBadge(ModRowStatus s)
    {
        (Brush bg, Brush fg, string label) = s switch
        {
            ModRowStatus.Installed       => ((Brush)FindResource("StatusInstalledBg"),       (Brush)FindResource("StatusInstalledFg"),       BadgeInstalled),
            ModRowStatus.UpdateAvailable => ((Brush)FindResource("StatusUpdateBg"),          (Brush)FindResource("StatusUpdateFg"),          BadgeUpdateAvailable),
            ModRowStatus.Incompatible    => ((Brush)FindResource("StatusIncompatibleBg"),    (Brush)FindResource("StatusIncompatibleFg"),    BadgeIncompatible),
            ModRowStatus.Error           => ((Brush)FindResource("StatusErrorBg"),           (Brush)FindResource("StatusErrorFg"),           BadgeError),
            _                            => ((Brush)FindResource("StatusNotInstalledBg"),    (Brush)FindResource("StatusNotInstalledFg"),    BadgeNotInstalled),
        };
        return new Border
        {
            Background = bg,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 3, 8, 3),
            Child = new TextBlock
            {
                Text = label,
                FontSize = _fsCaption,
                FontWeight = FontWeights.SemiBold,
                Foreground = fg,
            },
        };
    }

    private Button BuildRowAction(ModProfile profile, ModRowState state)
    {
        // Workshop redesign — the per-row CTA is now a toggle for the
        // user's personal collection, not an install/update/play
        // dispatcher. Three modes:
        //   1. Built-in profile (WoL) → small disabled "Built-in" pill.
        //      Can't be removed because the launcher needs something
        //      to fall back on if the user empties their collection.
        //   2. In user's collection → "Remove from my mods" (neutral
        //      ghost button — destructive-looking but recoverable).
        //   3. Not in user's collection → "Add to my mods" (primary
        //      CatalogBlue button — the main Workshop action).
        // All install / update / repair / uninstall happens on the
        // Dashboard (PLAY state machine + gear menu).
        if (state.IsBuiltIn)
        {
            return new Button
            {
                Content = BtnBuiltinLabel,
                Foreground = (Brush)FindResource("TextSecondary"),
                Background = (Brush)FindResource("BgPanelAlt"),
                BorderBrush = (Brush)FindResource("BorderSubtle"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 5, 14, 5),
                FontSize = _fsCaption,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Arrow,
                IsEnabled = false,
            };
        }

        bool added = state.IsInUserCollection;
        var btn = new Button
        {
            Content = added ? BtnRemoveFromCollectionLabel : BtnAddToCollectionLabel,
            Foreground = added ? (Brush)FindResource("TextSecondary") : Brushes.White,
            Background = added
                ? (Brush)FindResource("BgPanelAlt")
                : (Brush)FindResource("CatalogBlue"),
            BorderBrush = added
                ? (Brush)FindResource("BorderSubtle")
                : (Brush)FindResource("CatalogBlue"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 5, 14, 5),
            FontSize = _fsCaption,
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand,
        };
        btn.Click += (_, _) =>
        {
            ShowDetail(profile);
            if (added)
                RemoveFromCollectionRequested?.Invoke(this, profile);
            else
                AddToCollectionRequested?.Invoke(this, profile);
        };
        return btn;
    }

    // ------------------------------------------------------------------------
    // Detail panel.
    // ------------------------------------------------------------------------

    public ModProfile? SelectedProfile => _selectedProfile;

    /// <summary>Selects a profile and renders the right pane against it.</summary>
    public void ShowDetail(ModProfile profile)
    {
        _selectedProfile = profile;
        DetailEmptyPanel.Visibility = Visibility.Collapsed;
        DetailContent.Visibility = Visibility.Visible;
        if (_stateProvider is null) return;

        var state = _stateProvider(profile);
        var accent = ParseColorBrush(profile.AccentColor) ?? (Brush)FindResource("CatalogBlue");

        // Banner: prefer real banner image, fall back to gradient + monogram.
        var bannerSource = TryLoadBitmap(profile.LocalBannerPath);
        if (bannerSource != null)
        {
            DetailBannerImage.Source = bannerSource;
            DetailBannerImage.Visibility = Visibility.Visible;
            DetailBanner.Background = Brushes.Black;
            DetailMonogramHero.Visibility = Visibility.Collapsed;
        }
        else
        {
            DetailBannerImage.Source = null;
            DetailBannerImage.Visibility = Visibility.Collapsed;
            DetailBanner.Background = BuildBannerGradient(accent);
            DetailMonogramHero.Visibility = Visibility.Visible;

            // No banner: prefer the mod icon over the letter monogram.
            var iconBrush = TryLoadImageBrush(ResolveIconUri(profile));
            if (iconBrush != null)
            {
                DetailMonogramHero.Background = iconBrush;
                DetailMonogram.Visibility = Visibility.Collapsed;
            }
            else
            {
                DetailMonogramHero.Background = accent;
                DetailMonogram.Visibility = Visibility.Visible;
                DetailMonogram.Text = string.IsNullOrEmpty(profile.DisplayName)
                    ? "?"
                    : profile.DisplayName[..1].ToUpperInvariant();
            }
        }

        DetailTitle.Text = profile.DisplayName;
        PaintDetailBadge(state.Status);
        DetailDescription.Text = ResolveDescription(profile, _uiLanguage);
        DetailDescription.Visibility = string.IsNullOrWhiteSpace(DetailDescription.Text)
            ? Visibility.Collapsed
            : Visibility.Visible;
        BuildDetailMeta(profile, state);
        BuildDetailLanguages(profile);
        BuildDetailActions(profile, state);

        // Gallery: render whatever is already cached, then kick the lazy fetch
        // (MainWindow re-calls RefreshGallery when the download lands).
        BuildGallery(profile);
        ScreenshotRequester?.Invoke(profile);

        HighlightSelectedRow();
    }

    private void ClearDetail()
    {
        _selectedProfile = null;
        DetailEmptyPanel.Visibility = Visibility.Visible;
        DetailContent.Visibility = Visibility.Collapsed;
        ClearGallery();
    }

    // ------------------------------------------------------------------------
    // Screenshot / GIF gallery (detail panel).
    // ------------------------------------------------------------------------

    /// <summary>
    /// Re-render the gallery for <paramref name="profile"/> if it is still the
    /// selected one. Called by MainWindow once the lazy screenshot download
    /// completes (the user may have clicked away in the meantime).
    /// </summary>
    public void RefreshGallery(ModProfile profile)
    {
        if (ReferenceEquals(profile, _selectedProfile))
            BuildGallery(profile);
    }

    /// <summary>
    /// Builds the thumbnail strip + large viewer from the mod's cached
    /// screenshot paths. Hides the whole section when the mod ships none.
    /// </summary>
    private void BuildGallery(ModProfile profile)
    {
        ClearGallery();
        var paths = profile.LocalScreenshotPaths;
        if (paths is null || paths.Count == 0)
            return;

        DetailGallerySection.Visibility = Visibility.Visible;
        DetailGalleryTitle.Text = GalleryTitleText;

        foreach (var path in paths)
        {
            var thumb = new Border
            {
                Width = 96,
                Height = 54,
                Margin = new Thickness(0, 0, 6, 0),
                CornerRadius = new CornerRadius(4),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = path,
                // Thumbnails are static (a GIF decodes only its first frame here);
                // only the large viewer animates (see SelectGalleryShot).
                Background = new ImageBrush(TryLoadBitmap(path, decodeWidth: 480)) { Stretch = Stretch.UniformToFill },
            };
            thumb.MouseLeftButtonUp += (_, _) =>
            {
                if (thumb.Tag is string p) SelectGalleryShot(p);
            };
            DetailGalleryStrip.Children.Add(thumb);
        }

        SelectGalleryShot(paths[0]);
    }

    /// <summary>
    /// Shows one screenshot in the large viewer. A <c>.gif</c> is animated via
    /// XamlAnimatedGif (off its cached local path); everything else is a static
    /// bitmap. The thumbnails stay static either way.
    /// </summary>
    private void SelectGalleryShot(string path)
    {
        if (path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
        {
            DetailGalleryViewer.Source = null;   // drop any static bitmap first
            XamlAnimatedGif.AnimationBehavior.SetSourceUri(DetailGalleryViewer, new Uri(path));
        }
        else
        {
            XamlAnimatedGif.AnimationBehavior.SetSourceUri(DetailGalleryViewer, null); // stop a prior gif
            DetailGalleryViewer.Source = TryLoadBitmap(path, decodeWidth: 1920);
        }
    }

    private void ClearGallery()
    {
        // Stop/release any running gif animation before tearing the strip down.
        XamlAnimatedGif.AnimationBehavior.SetSourceUri(DetailGalleryViewer, null);
        DetailGallerySection.Visibility = Visibility.Collapsed;
        DetailGalleryStrip.Children.Clear();
        DetailGalleryViewer.Source = null;
    }

    private void PaintDetailBadge(ModRowStatus s)
    {
        (Brush bg, Brush fg, string label) = s switch
        {
            ModRowStatus.Installed       => ((Brush)FindResource("StatusInstalledBg"),       (Brush)FindResource("StatusInstalledFg"),       BadgeInstalled),
            ModRowStatus.UpdateAvailable => ((Brush)FindResource("StatusUpdateBg"),          (Brush)FindResource("StatusUpdateFg"),          BadgeUpdateAvailable),
            ModRowStatus.Incompatible    => ((Brush)FindResource("StatusIncompatibleBg"),    (Brush)FindResource("StatusIncompatibleFg"),    BadgeIncompatible),
            ModRowStatus.Error           => ((Brush)FindResource("StatusErrorBg"),           (Brush)FindResource("StatusErrorFg"),           BadgeError),
            _                            => ((Brush)FindResource("StatusNotInstalledBg"),    (Brush)FindResource("StatusNotInstalledFg"),    BadgeNotInstalled),
        };
        DetailStateBadge.Background = bg;
        DetailStateBadgeText.Foreground = fg;
        DetailStateBadgeText.Text = label;
    }

    private void BuildDetailMeta(ModProfile profile, ModRowState state)
    {
        DetailMetaLeft.Children.Clear();
        DetailMetaRight.Children.Clear();
        var rows = new List<(string Label, string Value)>();
        if (!string.IsNullOrWhiteSpace(profile.Author))
            rows.Add((DetailDeveloperLabel, profile.Author));
        if (!string.IsNullOrEmpty(state.CurrentVersion))
            rows.Add((DetailVersionLabel, "v" + Strip(state.CurrentVersion)));
        if (!string.IsNullOrEmpty(state.AvailableVersion)
            && state.AvailableVersion != state.CurrentVersion)
            rows.Add((DetailAvailableVersionLabel, "v" + Strip(state.AvailableVersion)));
        rows.Add((DetailInstallTypeLabel, FormatInstallType(profile.InstallType)));
        rows.Add((DetailUpdateMechLabel, FormatUpdateMechanism(profile.UpdateMechanism)));
        if (!string.IsNullOrWhiteSpace(profile.OfficialWebsite))
            rows.Add((DetailWebsiteLabel, profile.OfficialWebsite));

        for (int i = 0; i < rows.Count; i++)
        {
            var target = (i % 2 == 0) ? DetailMetaLeft : DetailMetaRight;
            target.Children.Add(BuildMetaRow(rows[i].Label, rows[i].Value));
        }
    }

    private FrameworkElement BuildMetaRow(string label, string value)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        sp.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = _fsCaption,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextSecondary"),
        });
        sp.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = _fsBody,
            Foreground = (Brush)FindResource("TextPrimary"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0),
        });
        return sp;
    }

    private void BuildDetailLanguages(ModProfile profile)
    {
        DetailFeaturesPanel.Children.Clear();
        if (profile.Description is null || profile.Description.Count == 0)
        {
            DetailFeaturesTitle.Visibility = Visibility.Collapsed;
            return;
        }
        DetailFeaturesTitle.Text = DetailLanguagesLabel;
        DetailFeaturesTitle.Visibility = Visibility.Visible;

        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var key in profile.Description.Keys.OrderBy(k => k))
        {
            wrap.Children.Add(new Border
            {
                Background = (Brush)FindResource("BgBase"),
                BorderBrush = (Brush)FindResource("BorderSubtle"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 0, 6, 6),
                Child = new TextBlock
                {
                    Text = key.ToUpperInvariant(),
                    FontSize = _fsCaption,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("TextSecondary"),
                },
            });
        }
        DetailFeaturesPanel.Children.Add(wrap);
    }

    private void BuildDetailActions(ModProfile profile, ModRowState state)
    {
        // Workshop redesign — primary CTA mirrors the per-row button:
        // Built-in (disabled "Built-in" pill) / Add to my mods (primary)
        // / Remove from my mods (ghost). Install / Update / Repair /
        // Uninstall live on the Dashboard via PLAY + gear menu.
        string label;
        Action? click;
        bool enabled;
        bool primaryStyle;

        if (state.IsBuiltIn)
        {
            label = BtnBuiltinLabel;
            click = null;
            enabled = false;
            primaryStyle = false;
        }
        else if (state.IsInUserCollection)
        {
            label = BtnRemoveFromCollectionLabel;
            click = () => RemoveFromCollectionRequested?.Invoke(this, profile);
            enabled = true;
            primaryStyle = false;
        }
        else
        {
            label = BtnAddToCollectionLabel;
            click = () => AddToCollectionRequested?.Invoke(this, profile);
            enabled = true;
            primaryStyle = true;
        }

        DetailPrimaryButton.Content = label;
        DetailPrimaryButton.IsEnabled = enabled;
        DetailPrimaryButton.Background = primaryStyle
            ? (Brush)FindResource("CatalogBlue")
            : (Brush)FindResource("BgPanelAlt");
        DetailPrimaryButton.Foreground = enabled && primaryStyle
            ? Brushes.White
            : (Brush)FindResource("TextSecondary");
        // Replace handler each rebuild — Click is rewired to whichever
        // action matches the current Add/Remove state.
        DetailPrimaryButton.Click -= OnPrimaryClick;
        _primaryAction = click;
        DetailPrimaryButton.Click += OnPrimaryClick;

        // Secondary — view mod page if URL present, otherwise hide.
        if (!string.IsNullOrWhiteSpace(profile.OfficialWebsite))
        {
            DetailSecondaryButton.Visibility = Visibility.Visible;
            DetailSecondaryButton.Content = DetailViewWebsiteLabel;
            DetailSecondaryButton.Click -= OnSecondaryClick;
            _secondaryUrl = profile.OfficialWebsite;
            DetailSecondaryButton.Click += OnSecondaryClick;
        }
        else
        {
            DetailSecondaryButton.Visibility = Visibility.Collapsed;
        }

        // Overflow menu items: Set active + Uninstall (when installed).
        BuildDetailMoreMenu(profile, state);
    }

    private Action? _primaryAction;
    private string _secondaryUrl = "";
    private void OnPrimaryClick(object sender, RoutedEventArgs e) => _primaryAction?.Invoke();
    private void OnSecondaryClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_secondaryUrl))
            OpenWebsiteRequested?.Invoke(this, _secondaryUrl);
    }

    private void BuildDetailMoreMenu(ModProfile profile, ModRowState state)
    {
        // Workshop redesign — More menu used to hold "Set as active" +
        // "Uninstall"; both moved to the Dashboard (MODS popup +
        // gear menu). Workshop is purely discovery + add/remove now,
        // so the menu has nothing to show and stays collapsed.
        DetailMoreMenu.Items.Clear();
        DetailMoreButton.Visibility = Visibility.Collapsed;
    }

    private void OnDetailMoreClick(object sender, RoutedEventArgs e)
    {
        if (DetailMoreButton.ContextMenu is null) return;
        DetailMoreButton.ContextMenu.PlacementTarget = DetailMoreButton;
        DetailMoreButton.ContextMenu.IsOpen = true;
    }

    // ------------------------------------------------------------------------
    // Helpers.
    // ------------------------------------------------------------------------

    private static Brush BuildBannerGradient(Brush accent)
    {
        if (accent is SolidColorBrush solid)
        {
            var color = solid.Color;
            var dim = Color.FromArgb(255,
                (byte)(color.R * 0.45),
                (byte)(color.G * 0.45),
                (byte)(color.B * 0.45));
            var lg = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
            };
            lg.GradientStops.Add(new GradientStop(color, 0));
            lg.GradientStops.Add(new GradientStop(dim, 1));
            lg.Freeze();
            return lg;
        }
        return accent;
    }

    /// <summary>
    /// Icon URI for a profile: the cached catalog icon if it's on disk, else
    /// the built-in packed icon (a <c>pack://</c> URI, e.g. WoL.ico), else
    /// null → caller renders the letter monogram.
    /// </summary>
    private static string? ResolveIconUri(ModProfile profile)
    {
        if (!string.IsNullOrEmpty(profile.LocalIconPath) && File.Exists(profile.LocalIconPath))
            return profile.LocalIconPath;
        if (!string.IsNullOrEmpty(profile.BannerImage))
            return profile.BannerImage;
        return null;
    }

    private static ImageBrush? TryLoadImageBrush(string? path)
    {
        var bmp = TryLoadBitmap(path);
        if (bmp is null) return null;
        var br = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
        br.Freeze();
        return br;
    }

    private static BitmapImage? TryLoadBitmap(string? path, int decodeWidth = 0)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        // Accept both on-disk cache files (catalog icon.png) and pack:// URIs
        // (built-in packed resources like WoL.ico). Only a file path needs an
        // existence check; a pack URI resolves against the assembly.
        bool isPack = path.StartsWith("pack:", StringComparison.OrdinalIgnoreCase);
        if (!isPack && !File.Exists(path)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            // IgnoreImageCache so a screenshot/icon REPLACED under the same file
            // name (same path, new bytes) re-decodes from disk instead of
            // serving WPF's stale per-URI cached bitmap.
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            // Cap the decode width so a 4K screenshot doesn't sit in RAM at full
            // ~33 MB — thumbnails need ~480, the big viewer ~1920. 0 = no cap
            // (icons/banners are already small). Skipped for .gif so the animated
            // viewer (XamlAnimatedGif) gets the original frames.
            if (decodeWidth > 0 && !path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                bmp.DecodePixelWidth = decodeWidth;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
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
