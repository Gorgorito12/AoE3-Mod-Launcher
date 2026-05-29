using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Non-modal "Launcher Settings" dialog. Reads the current
/// <see cref="LauncherConfig"/> state on open, lets the user tweak
/// launcher-wide preferences (NOT per-mod state — that lives in the
/// sidebar gear menu), and persists the changes back when the user
/// hits Save.
///
/// The dialog is mostly value mapping: each control mirrors one field in
/// LauncherConfig. The only side-effect not covered by a config write is
/// the Windows registry mutation for "Start with Windows" — handled by
/// <see cref="StartupRegistrationService.Apply"/>.
///
/// Opened via <see cref="Window.Show()"/> from MainWindow (not
/// <see cref="Window.ShowDialog()"/>) so the user can keep interacting
/// with the main window while it's open. On Save, sets
/// <see cref="ChangesSaved"/>=true; the caller reads that flag in its
/// <see cref="Window.Closed"/> handler to decide whether to refresh
/// dependent UI. Cancel / ✕ / Esc leave ChangesSaved=false.
/// </summary>
public partial class LauncherSettingsDialog : Window
{
    private readonly LauncherConfig _config;

    /// <summary>
    /// True after the user clicked Save (changes were persisted). The
    /// caller reads this in its <see cref="Window.Closed"/> handler to
    /// decide whether to refresh dependent UI. We can't use
    /// <see cref="Window.DialogResult"/> for this any more — the dialog
    /// is shown non-modally via <see cref="Window.Show()"/> now, and
    /// DialogResult is only settable when the window was opened with
    /// <see cref="Window.ShowDialog()"/> (otherwise WPF throws
    /// InvalidOperationException). Default false handles the Cancel /
    /// ✕ / Esc paths in one branch.
    /// </summary>
    public bool ChangesSaved { get; private set; }

    /// <summary>
    /// Regex for a valid "owner/repo" GitHub identifier. Mirrors the
    /// pattern used by mod.schema.json for the same field — so the
    /// dialog's UX feels consistent with what the catalog accepts.
    /// </summary>
    private static readonly Regex RepoRegex =
        new(@"^[a-zA-Z0-9._-]+/[a-zA-Z0-9._-]+$", RegexOptions.Compiled);

    private const string DefaultCatalogRepo = "Gorgorito12/aoe3-mods-catalog";

    /// <summary>
    /// In-memory working copy of the top-tab order (tab ids). Seeded
    /// from <see cref="LauncherConfig.GetTopTabOrder"/> in
    /// <see cref="LoadFromConfig"/>, mutated by the ↑/↓ buttons, and
    /// written back to <see cref="LauncherConfig.TopTabOrder"/> only on
    /// Save — so Cancel discards the reorder like every other edit.
    /// </summary>
    private readonly System.Collections.Generic.List<string> _tabOrder = new();

    public LauncherSettingsDialog(LauncherConfig config)
    {
        InitializeComponent();
        _config = config;
        ApplyLanguage();
        LoadFromConfig();
        // Land on GENERAL by default. The visibility of every panel is
        // also set to Collapsed except GeneralPanel in the XAML, so
        // this call is mainly to paint TabGeneralBtn's Tag="active"
        // accent stripe (the SidebarNavButton style reads Tag).
        SetActiveTab(TabGeneralBtn);
    }

    /// <summary>
    /// Pulls every visible string from the localisation table so the
    /// dialog respects the user's current launcher language. Called once
    /// on construction; the dialog doesn't react to live language changes
    /// (the user can just close + reopen if they switch on the fly).
    /// </summary>
    private void ApplyLanguage()
    {
        Title = Strings.Get("DlgLauncherSettingsTitle");
        TitleText.Text = Strings.Get("DlgLauncherSettingsTitle");

        // Sidebar tab labels. We reuse the original "Section*" strings
        // (uppercase: "GENERAL", "UPDATES", etc.) because they already
        // match the visual style ModPropertiesDialog uses for its own
        // sidebar tabs — no need to duplicate them under "Tab*" keys.
        TabGeneralLabel.Text = Strings.Get("DlgLauncherSettingsSectionGeneral");
        TabInterfaceLabel.Text = Strings.Get("DlgLauncherSettingsSectionInterface");
        TabUpdatesLabel.Text = Strings.Get("DlgLauncherSettingsSectionUpdates");
        TabCatalogLabel.Text = Strings.Get("DlgLauncherSettingsSectionCatalog");
        TabTranslationsLabel.Text = Strings.Get("DlgLauncherSettingsSectionTranslations");
        TabMaintenanceLabel.Text = Strings.Get("DlgLauncherSettingsSectionMaintenance");

        TabOrderLabel.Text = Strings.Get("DlgLauncherSettingsTabOrderLabel");
        TabOrderHint.Text = Strings.Get("DlgLauncherSettingsTabOrderHint");

        LanguageLabel.Text = Strings.Get("DlgLauncherSettingsLanguageLabel");
        // Theme picker removed — see LauncherSettingsDialog.xaml comment.

        StartWithWindowsCheck.Content = Strings.Get("DlgLauncherSettingsStartWithWindows");
        StartWithWindowsHint.Text = Strings.Get("DlgLauncherSettingsStartWithWindowsHint");
        CloseOnGameCheck.Content = Strings.Get("DlgLauncherSettingsCloseOnGame");
        CloseOnGameHint.Text = Strings.Get("DlgLauncherSettingsCloseOnGameHint");
        MinimizeToTrayCheck.Content = Strings.Get("DlgLauncherSettingsMinimizeToTray");
        MinimizeToTrayHint.Text = Strings.Get("DlgLauncherSettingsMinimizeToTrayHint");
        ShowToastsCheck.Content = Strings.Get("DlgLauncherSettingsShowToasts");
        ShowToastsHint.Text = Strings.Get("DlgLauncherSettingsShowToastsHint");

        // Radmin assistant mode picker. Combo items tagged with the
        // raw enum strings ("Auto"/"OnRequest"/"Never") so saving is
        // a one-line lookup. Built here (not in XAML) so the labels
        // can pull from Strings.* and follow the locale switch.
        RadAsstLabelText.Text = Strings.Get("SettingsRadAsstLabel");
        RadAsstHintText.Text = Strings.Get("SettingsRadAsstHint");
        RadAsstCombo.Items.Clear();
        RadAsstCombo.Items.Add(new ComboBoxItem
        {
            Content = Strings.Get("SettingsRadAsstAuto"),
            Tag = "Auto",
        });
        RadAsstCombo.Items.Add(new ComboBoxItem
        {
            Content = Strings.Get("SettingsRadAsstOnRequest"),
            Tag = "OnRequest",
        });
        RadAsstCombo.Items.Add(new ComboBoxItem
        {
            Content = Strings.Get("SettingsRadAsstNever"),
            Tag = "Never",
        });

        AutoCheckCheck.Content = Strings.Get("DlgLauncherSettingsAutoCheck");
        AutoCheckHint.Text = Strings.Get("DlgLauncherSettingsAutoCheckHint");
        OpenPostUpdateCheck.Content = Strings.Get("DlgLauncherSettingsOpenPostUpdate");
        OpenPostUpdateHint.Text = Strings.Get("DlgLauncherSettingsOpenPostUpdateHint");

        CatalogDefaultRadio.Content = Strings.Get("DlgLauncherSettingsCatalogDefault")
            + $"  ({DefaultCatalogRepo})";
        CatalogCustomRadio.Content = Strings.Get("DlgLauncherSettingsCatalogCustom");
        CatalogDisabledRadio.Content = Strings.Get("DlgLauncherSettingsCatalogDisabled");

        ClearCacheButton.Content = Strings.Get("DlgLauncherSettingsClearCache");
        ClearCacheHint.Text = Strings.Get("DlgLauncherSettingsClearCacheHint");

        TranslationsHeader.Text = Strings.Get("DlgLauncherSettingsTranslationsHeader");
        TranslationsDescription.Text = Strings.Get("DlgLauncherSettingsTranslationsDescription");
        OpenPackagerButton.Content = "📦  " + Strings.Get("DlgLauncherSettingsOpenPackager");
        TranslationsHint.Text = Strings.Get("DlgLauncherSettingsTranslationsHint");

        ClearAssetsButton.Content = Strings.Get("DlgLauncherSettingsClearAssets");
        ClearAssetsHint.Text = Strings.Get("DlgLauncherSettingsClearAssetsHint");
        ClearTempButton.Content = Strings.Get("DlgLauncherSettingsClearTemp");
        ClearTempHint.Text = Strings.Get("DlgLauncherSettingsClearTempHint");

        CancelButton.Content = Strings.Get("BtnCancel");
        SaveButton.Content = Strings.Get("BtnSave");
    }

    /// <summary>
    /// Initialises each control from the persisted config. Called once
    /// after the constructor — subsequent changes to the controls are the
    /// user's edits and live in-memory until they hit Save.
    /// </summary>
    private void LoadFromConfig()
    {
        // Language combo: select by Tag so adding more languages later is
        // a one-line change.
        foreach (ComboBoxItem item in LanguageCombo.Items)
        {
            if (string.Equals(item.Tag as string, _config.Language, StringComparison.OrdinalIgnoreCase))
            {
                LanguageCombo.SelectedItem = item;
                break;
            }
        }
        if (LanguageCombo.SelectedItem == null)
            LanguageCombo.SelectedIndex = 0;

        // Start-with-Windows: trust the registry as the source of truth
        // (the user may have removed our entry manually via Task Manager).
        // The launcher's own field is rewritten on Save anyway.
        StartWithWindowsCheck.IsChecked = StartupRegistrationService.IsRegistered();
        CloseOnGameCheck.IsChecked = _config.CloseLauncherOnGameStart;
        MinimizeToTrayCheck.IsChecked = _config.MinimizeToTray;
        ShowToastsCheck.IsChecked = _config.ShowToastNotifications;
        AutoCheckCheck.IsChecked = _config.CheckUpdatesOnStartup;
        OpenPostUpdateCheck.IsChecked = _config.OpenPostUpdatePages;

        // Radmin assistant mode — match by Tag against the persisted
        // "Auto"/"OnRequest"/"Never" value. Unknown / missing values
        // fall back to Auto (the default for new installs), so a
        // legacy config without the field still ends up on Auto.
        var modeTag = string.IsNullOrEmpty(_config.RadminAssistantMode)
            ? "Auto" : _config.RadminAssistantMode;
        foreach (ComboBoxItem item in RadAsstCombo.Items)
        {
            if (string.Equals(item.Tag as string, modeTag, StringComparison.OrdinalIgnoreCase))
            {
                RadAsstCombo.SelectedItem = item;
                break;
            }
        }
        if (RadAsstCombo.SelectedItem == null)
            RadAsstCombo.SelectedIndex = 0;

        // Top-tab order: seed the working copy from the sanitised config
        // value and render the reorderable rows.
        _tabOrder.Clear();
        _tabOrder.AddRange(_config.GetTopTabOrder());
        RenderTabOrderList();

        // Catalog source: map the three-way config ("" / "none" / repo)
        // back into the radio buttons + text box.
        var rawRepo = _config.ModsCatalogRepo ?? "";
        if (string.IsNullOrEmpty(rawRepo))
        {
            CatalogDefaultRadio.IsChecked = true;
            CatalogCustomBox.Text = "";
        }
        else if (string.Equals(rawRepo, "none", StringComparison.OrdinalIgnoreCase))
        {
            CatalogDisabledRadio.IsChecked = true;
            CatalogCustomBox.Text = "";
        }
        else
        {
            CatalogCustomRadio.IsChecked = true;
            CatalogCustomBox.Text = rawRepo;
        }
    }

    /// <summary>
    /// No-op handler — the actual save happens in
    /// <see cref="SaveButton_Click"/>; we just need the
    /// SelectionChanged hook so the combo isn't a dead control if
    /// XAML wires up a Click somewhere accidentally. Kept as a
    /// method (vs lambda) so the XAML reference resolves cleanly.
    /// </summary>
    private void RadAsstCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // Live-preview would go here if we ever wanted to show
        // a hint about what each mode does — intentionally empty
        // right now because the hint text below the combo is
        // mode-agnostic and the change only commits on Save.
    }

    // -- Tab switching ------------------------------------------------------
    //
    // Copy of the ModPropertiesDialog pattern: each tab button toggles
    // Tag="active" on itself (the SidebarNavButton style draws the gold
    // right-rail accent off that), and the panels' Visibility is set to
    // Visible only on the matching one. Same predictable contract, same
    // SidebarNavButton style, so the two dialogs read as siblings.

    private void SetActiveTab(System.Windows.Controls.Button activeBtn)
    {
        TabGeneralBtn.Tag = ReferenceEquals(activeBtn, TabGeneralBtn) ? "active" : null;
        TabInterfaceBtn.Tag = ReferenceEquals(activeBtn, TabInterfaceBtn) ? "active" : null;
        TabUpdatesBtn.Tag = ReferenceEquals(activeBtn, TabUpdatesBtn) ? "active" : null;
        TabCatalogBtn.Tag = ReferenceEquals(activeBtn, TabCatalogBtn) ? "active" : null;
        TabTranslationsBtn.Tag = ReferenceEquals(activeBtn, TabTranslationsBtn) ? "active" : null;
        TabMaintenanceBtn.Tag = ReferenceEquals(activeBtn, TabMaintenanceBtn) ? "active" : null;

        GeneralPanel.Visibility = ReferenceEquals(activeBtn, TabGeneralBtn) ? Visibility.Visible : Visibility.Collapsed;
        InterfacePanel.Visibility = ReferenceEquals(activeBtn, TabInterfaceBtn) ? Visibility.Visible : Visibility.Collapsed;
        UpdatesPanel.Visibility = ReferenceEquals(activeBtn, TabUpdatesBtn) ? Visibility.Visible : Visibility.Collapsed;
        CatalogPanel.Visibility = ReferenceEquals(activeBtn, TabCatalogBtn) ? Visibility.Visible : Visibility.Collapsed;
        TranslationsPanel.Visibility = ReferenceEquals(activeBtn, TabTranslationsBtn) ? Visibility.Visible : Visibility.Collapsed;
        MaintenancePanel.Visibility = ReferenceEquals(activeBtn, TabMaintenanceBtn) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TabGeneralBtn_Click(object sender, RoutedEventArgs e) => SetActiveTab(TabGeneralBtn);
    private void TabInterfaceBtn_Click(object sender, RoutedEventArgs e) => SetActiveTab(TabInterfaceBtn);
    private void TabUpdatesBtn_Click(object sender, RoutedEventArgs e) => SetActiveTab(TabUpdatesBtn);
    private void TabCatalogBtn_Click(object sender, RoutedEventArgs e) => SetActiveTab(TabCatalogBtn);
    private void TabTranslationsBtn_Click(object sender, RoutedEventArgs e) => SetActiveTab(TabTranslationsBtn);
    private void TabMaintenanceBtn_Click(object sender, RoutedEventArgs e) => SetActiveTab(TabMaintenanceBtn);

    /// <summary>
    /// Launches the translator-facing packaging dialog modally over this
    /// settings window. The dialog is globalised across mods (its own
    /// mod picker decides which install path to bind to), so no profile
    /// argument is needed from here.
    /// </summary>
    private void OpenPackagerButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new TranslationPackagerDialog(_config)
        {
            Owner = this,
        };
        dialog.ShowDialog();
    }

    // -- Top-tab reorder (Interface section) --------------------------------

    /// <summary>
    /// Rebuild the reorderable tab rows from <see cref="_tabOrder"/>.
    /// Each row: a position number, the tab's display name, and ↑/↓
    /// buttons (the first row's ↑ and last row's ↓ are disabled). The
    /// first row carries a small "opens on launch" badge so the
    /// order→startup link is obvious. Called on load and after every
    /// move; cheap (3 rows) so a full re-render beats fiddly in-place
    /// swaps.
    /// </summary>
    private void RenderTabOrderList()
    {
        TabOrderList.Children.Clear();

        for (int i = 0; i < _tabOrder.Count; i++)
        {
            string id = _tabOrder[i];
            bool isFirst = i == 0;
            bool isLast = i == _tabOrder.Count - 1;

            var row = new Border
            {
                Background = (Brush)FindResource("MpSurface"),
                BorderBrush = (Brush)FindResource("BorderSubtle"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 8, 8),
                Margin = new Thickness(0, 0, 0, 8),
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // position
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // name
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // up
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // down

            var pos = new TextBlock
            {
                Text = (i + 1).ToString() + ".",
                Foreground = (Brush)FindResource("TextSecondary"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
            };
            Grid.SetColumn(pos, 0);
            grid.Children.Add(pos);

            var nameStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };
            nameStack.Children.Add(new TextBlock
            {
                Text = TabDisplayName(id),
                Foreground = (Brush)FindResource("TextPrimary"),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            });
            if (isFirst)
            {
                // "opens on launch" badge on whatever sits first.
                nameStack.Children.Add(new TextBlock
                {
                    Text = "  " + Strings.Get("DlgLauncherSettingsTabOrderOpensFirst"),
                    Foreground = (Brush)FindResource("AccentBrush"),
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }
            Grid.SetColumn(nameStack, 1);
            grid.Children.Add(nameStack);

            var upBtn = new Button
            {
                Style = (Style)FindResource("PropertyActionButton"),
                Content = "↑",
                MinWidth = 40,
                Margin = new Thickness(6, 0, 0, 0),
                IsEnabled = !isFirst,
                Tag = i,
            };
            upBtn.Click += MoveTabUp_Click;
            Grid.SetColumn(upBtn, 2);
            grid.Children.Add(upBtn);

            var downBtn = new Button
            {
                Style = (Style)FindResource("PropertyActionButton"),
                Content = "↓",
                MinWidth = 40,
                Margin = new Thickness(6, 0, 0, 0),
                IsEnabled = !isLast,
                Tag = i,
            };
            downBtn.Click += MoveTabDown_Click;
            Grid.SetColumn(downBtn, 3);
            grid.Children.Add(downBtn);

            row.Child = grid;
            TabOrderList.Children.Add(row);
        }
    }

    private void MoveTabUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int i } && i > 0)
        {
            (_tabOrder[i - 1], _tabOrder[i]) = (_tabOrder[i], _tabOrder[i - 1]);
            RenderTabOrderList();
        }
    }

    private void MoveTabDown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int i } && i < _tabOrder.Count - 1)
        {
            (_tabOrder[i + 1], _tabOrder[i]) = (_tabOrder[i], _tabOrder[i + 1]);
            RenderTabOrderList();
        }
    }

    /// <summary>
    /// Localised display name for a top-tab id. Reuses the same strings
    /// the nav bar paints (TopTabPlay/Mods/Multiplayer) so the reorder
    /// list reads identically to the bar it controls.
    /// </summary>
    private static string TabDisplayName(string id) => id switch
    {
        "workshop" => Strings.Get("TopTabMods"),
        "multiplayer" => Strings.Get("TopTabMultiplayer"),
        _ => Strings.Get("TopTabPlay"),
    };

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        // Non-modal: just Close(). ChangesSaved stays false by default,
        // which the caller treats as "nothing to refresh".
        Close();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        // 1. Resolve the catalog source first because it can fail
        //    validation; we don't want to write half the changes if the
        //    user typed an invalid custom repo.
        string newCatalogRepo;
        if (CatalogDefaultRadio.IsChecked == true)
        {
            newCatalogRepo = "";
        }
        else if (CatalogDisabledRadio.IsChecked == true)
        {
            newCatalogRepo = "none";
        }
        else
        {
            // Custom selected — must be a syntactically valid owner/repo.
            var typed = (CatalogCustomBox.Text ?? "").Trim();
            if (!RepoRegex.IsMatch(typed))
            {
                CatalogInvalidText.Text = Strings.Get("DlgLauncherSettingsInvalidRepo");
                CatalogInvalidText.Visibility = Visibility.Visible;
                // Switch to the Catalog tab so the user actually sees
                // the inline error + the textbox they need to fix. The
                // tab redesign means the user could be on Updates or
                // Maintenance when they hit Save, and a silent failure
                // there is a UX dead end.
                SetActiveTab(TabCatalogBtn);
                CatalogCustomBox.Focus();
                return;
            }
            CatalogInvalidText.Visibility = Visibility.Collapsed;
            newCatalogRepo = typed;
        }

        // 2. Language: persist + apply live so the launcher main window
        //    re-localises on close without a restart. Strings.SetLanguage
        //    raises the LanguageChanged event the rest of the app listens
        //    on.
        var newLang = (LanguageCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "en";

        // (Theme picker removed — see LauncherSettingsDialog.xaml comment.
        //  Old configs with a "theme" key just get the key dropped on
        //  the next save; nothing reads it anymore.)

        // 3. Write all the bools / strings into the config object.
        _config.Language = newLang;
        _config.CloseLauncherOnGameStart = CloseOnGameCheck.IsChecked == true;
        _config.MinimizeToTray = MinimizeToTrayCheck.IsChecked == true;
        _config.ShowToastNotifications = ShowToastsCheck.IsChecked == true;
        _config.CheckUpdatesOnStartup = AutoCheckCheck.IsChecked == true;
        _config.OpenPostUpdatePages = OpenPostUpdateCheck.IsChecked == true;
        _config.ModsCatalogRepo = newCatalogRepo;
        _config.StartWithWindows = StartWithWindowsCheck.IsChecked == true;

        // Top-tab order (Interface section). Persist the working copy;
        // MainWindow re-applies it to the nav bar on the post-save
        // refresh (ApplyTopTabOrder), and the FIRST entry becomes the
        // tab that opens on the next launch.
        _config.TopTabOrder = _tabOrder.ToArray();

        // Radmin assistant mode — keep "Auto" as the fallback if for
        // some reason the combo had no selection (shouldn't happen
        // because LoadFromConfig forces SelectedIndex=0).
        var newMode = (RadAsstCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Auto";
        // Switching off Skipped when the user changes mode away from
        // OnRequest/Never — they're re-engaging with the assistant
        // so we shouldn't continue to silently suppress it.
        if (!string.Equals(_config.RadminAssistantMode, newMode, StringComparison.OrdinalIgnoreCase))
        {
            _config.RadminAssistantSkipped = false;
        }
        _config.RadminAssistantMode = newMode;

        // 4. Side effects beyond the config file:
        //    * Registry write for the autostart entry.
        //    * Language change goes through Strings so the rest of the
        //      app updates immediately.
        StartupRegistrationService.Apply(_config.StartWithWindows);
        Strings.SetLanguage(newLang);

        // 5. Persist to disk.
        try
        {
            _config.Save();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"LauncherSettings save failed: {ex.Message}");
            // We still close — the in-memory config is correct, and the
            // next manual save will flush.
        }

        ChangesSaved = true;
        Close();
    }

    /// <summary>
    /// "Clear catalog cache" button — deletes the on-disk
    /// <c>catalog-cache.json</c> so the next refresh hits the network
    /// fresh. Useful when a user has added a mod via PR and wants to see
    /// it without waiting for the 24h TTL.
    ///
    /// Does NOT close the dialog (the user may want to keep tweaking
    /// settings) and does NOT touch the in-memory list — the next
    /// <c>ModRegistry.RefreshFromCatalogAsync</c> call will rebuild.
    /// </summary>
    private void ClearCacheButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            bool hadAny = File.Exists(ModCatalogService.CacheFilePath);
            if (hadAny)
                File.Delete(ModCatalogService.CacheFilePath);
            SetHint(ClearCacheHint,
                Strings.Get(hadAny ? "DlgLauncherSettingsCacheCleared" : "DlgLauncherSettingsNothingToClean"),
                success: true);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Clear catalog cache failed: {ex.Message}");
            SetHint(ClearCacheHint, ex.Message, success: false);
        }
    }

    /// <summary>
    /// "Clear mod icons cache" button — wipes
    /// <c>%LocalAppData%\AoE3ModLauncher\mod-assets\</c>. Useful when a
    /// modder uploaded a new icon and the user wants to see it without
    /// waiting for the launcher's per-mod fetch flag to reset (it
    /// re-attempts the download on next launch).
    /// </summary>
    private void ClearAssetsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = ModAssetCacheService.CacheDir;
            int deleted = 0;
            if (Directory.Exists(dir))
            {
                // Delete file-by-file (not the whole directory) so a
                // running launcher that has an Image bound to a cached
                // file doesn't choke on a missing parent folder.
                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    try { File.Delete(file); deleted++; }
                    catch (Exception ex)
                    {
                        // One locked file shouldn't abort the whole sweep
                        // (WPF Image cache can briefly hold handles).
                        DiagnosticLog.Write($"Could not delete '{file}': {ex.Message}");
                    }
                }
            }
            var msg = deleted == 0
                ? Strings.Get("DlgLauncherSettingsNothingToClean")
                : Strings.Format("DlgLauncherSettingsAssetsCleared", deleted);
            SetHint(ClearAssetsHint, msg, success: true);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Clear assets cache failed: {ex.Message}");
            SetHint(ClearAssetsHint, ex.Message, success: false);
        }
    }

    /// <summary>
    /// "Clear temp files" button — empties the launcher's scratch dir
    /// (<c>%TEMP%\WarsOfLibertyLauncher\</c>), where mid-update download
    /// fragments and extracted .tar.xz contents accumulate when the user
    /// cancels mid-way. Safe to delete any time the launcher isn't busy.
    /// </summary>
    private void ClearTempButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "WarsOfLibertyLauncher");
            if (!Directory.Exists(tempDir))
            {
                SetHint(ClearTempHint, Strings.Get("DlgLauncherSettingsNothingToClean"), success: true);
                return;
            }

            // Recursive delete then recreate so the install pipeline still
            // has a known-good scratch folder to write into.
            Directory.Delete(tempDir, recursive: true);
            Directory.CreateDirectory(tempDir);

            SetHint(ClearTempHint, Strings.Get("DlgLauncherSettingsTempCleared"), success: true);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Clear temp files failed: {ex.Message}");
            SetHint(ClearTempHint, ex.Message, success: false);
        }
    }

    /// <summary>
    /// Tints a hint TextBlock with the standard success-green or error-red
    /// the rest of the launcher uses, and replaces its text. Shared
    /// helper so the three "Clear X" buttons behave consistently.
    /// </summary>
    private static void SetHint(System.Windows.Controls.TextBlock hint, string text, bool success)
    {
        hint.Text = text;
        var color = success
            ? System.Windows.Media.Color.FromRgb(0x9b, 0xd9, 0x9b) // green
            : System.Windows.Media.Color.FromRgb(0xe6, 0x39, 0x50); // red
        hint.Foreground = new System.Windows.Media.SolidColorBrush(color);
    }
}
