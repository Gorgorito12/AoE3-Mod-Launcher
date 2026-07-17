using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

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
    /// Invoked right after the user clears the icon/asset cache, so the (still
    /// open, non-modal) launcher can re-download the images live instead of
    /// requiring a restart. Set by the caller; null = no live refresh.
    /// </summary>
    public Action? AssetsCleared { get; set; }

    /// <summary>
    /// Invoked when the user clicks "Clear translations cache". Community
    /// translations have no on-disk cache (only MainWindow's in-memory index),
    /// so the caller wires this to null that index and re-fetch live. Set by
    /// the caller; null = no-op.
    /// </summary>
    public Action? TranslationsCacheCleared { get; set; }

    /// <summary>
    /// Regex for a valid "owner/repo" GitHub identifier. Mirrors the
    /// pattern used by mod.schema.json for the same field — so the
    /// dialog's UX feels consistent with what the catalog accepts.
    /// </summary>
    private static readonly Regex RepoRegex =
        new(@"^[a-zA-Z0-9._-]+/[a-zA-Z0-9._-]+$", RegexOptions.Compiled);

    private const string DefaultCatalogRepo = "Gorgorito12/aoe3-mods-catalog";

    /// <summary>
    /// Folder repo the WoL profile ships as its default translations source —
    /// shown in the "Default" radio label. Only accurate for WoL; for other
    /// mods the label reads generically (the effective default is the active
    /// profile's own FolderRepo, resolved in UpdateService).
    /// </summary>
    private const string DefaultTranslationsRepo = "Gorgorito12/translations";

    /// <summary>
    /// In-memory working copy of the top-tab order (tab ids). Seeded
    /// from <see cref="LauncherConfig.GetTopTabOrder"/> in
    /// <see cref="LoadFromConfig"/>, mutated by the ↑/↓ buttons, and
    /// written back to <see cref="LauncherConfig.TopTabOrder"/> only on
    /// Save — so Cancel discards the reorder like every other edit.
    /// </summary>
    private readonly System.Collections.Generic.List<string> _tabOrder = new();

    /// <summary>
    /// Working copy of the user's EXTRA translation folder repos (Settings →
    /// TRANSLATIONS). Seeded from config in <see cref="LoadFromConfig"/>, edited
    /// by the Add/✕ buttons, committed to config only on Save — so Cancel
    /// discards the edits, mirroring <see cref="_tabOrder"/>.
    /// </summary>
    private readonly System.Collections.Generic.List<string> _extraTxRepos = new();

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

        // Window-size scaling (Controls/UiScale.cs): the content area (Row 1,
        // between the fixed header and the sticky footer) shrinks to fit smaller
        // dialogs. sizeSource is the Window (window-sized → no feedback); the
        // header and footer stay at base scale. ref ≈ the default footprint, so
        // the default-sized dialog renders at 1.0.
        UiScale.Attach(SettingsContentRoot, this, 800, 520);
    }

    /// <summary>
    /// Pulls every visible string from the localisation table so the
    /// dialog respects the user's current launcher language. Called once
    /// on construction; the dialog doesn't react to live language changes
    /// (the user can just close + reopen if they switch on the fly).
    /// </summary>
    private void ApplyLanguage()
    {
        // Attach a localized hover tooltip (the "detail" a newcomer reads by
        // hovering) to any control. Kept as a local helper so every settings
        // control wires its tooltip in one line, re-localized whenever
        // ApplyLanguage runs.
        static void SetTip(FrameworkElement el, string key) => el.ToolTip = TooltipHelper.Wrap(Strings.Get(key));

        Title = Strings.Get("DlgLauncherSettingsTitle");
        TitleBarControl.Title = Strings.Get("DlgLauncherSettingsTitle");

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
        TabPrivacyLabel.Text = Strings.Get("DlgLauncherSettingsSectionPrivacy");

        TabOrderLabel.Text = Strings.Get("DlgLauncherSettingsTabOrderLabel");
        TabOrderHint.Text = Strings.Get("DlgLauncherSettingsTabOrderHint");

        LanguageLabel.Text = Strings.Get("DlgLauncherSettingsLanguageLabel");
        // Theme picker removed — see LauncherSettingsDialog.xaml comment.

        StartWithWindowsCheck.Content = Strings.Get("DlgLauncherSettingsStartWithWindows");
        StartWithWindowsHint.Text = Strings.Get("DlgLauncherSettingsStartWithWindowsHint");
        SetTip(StartWithWindowsCheck, "DlgLauncherSettingsStartWithWindowsTip");
        EnableJoinLinksCheck.Content = Strings.Get("DlgLauncherSettingsJoinLinks");
        EnableJoinLinksHint.Text = Strings.Get("DlgLauncherSettingsJoinLinksHint");
        SetTip(EnableJoinLinksCheck, "DlgLauncherSettingsJoinLinksTip");
        CloseOnGameCheck.Content = Strings.Get("DlgLauncherSettingsCloseOnGame");
        CloseOnGameHint.Text = Strings.Get("DlgLauncherSettingsCloseOnGameHint");
        SetTip(CloseOnGameCheck, "DlgLauncherSettingsCloseOnGameTip");
        MinimizeToTrayCheck.Content = Strings.Get("DlgLauncherSettingsMinimizeToTray");
        MinimizeToTrayHint.Text = Strings.Get("DlgLauncherSettingsMinimizeToTrayHint");
        SetTip(MinimizeToTrayCheck, "DlgLauncherSettingsMinimizeToTrayTip");
        ShowToastsCheck.Content = Strings.Get("DlgLauncherSettingsShowToasts");
        ShowToastsHint.Text = Strings.Get("DlgLauncherSettingsShowToastsHint");
        SetTip(ShowToastsCheck, "DlgLauncherSettingsShowToastsTip");
        NotifyNewRoomsCheck.Content = Strings.Get("DlgSettingsNotifyRooms");
        NotifyNewRoomsHint.Text = Strings.Get("DlgSettingsNotifyRoomsHint");
        SetTip(NotifyNewRoomsCheck, "DlgSettingsNotifyRoomsTip");
        SoundsCheck.Content = Strings.Get("DlgSettingsSounds");
        SoundsHint.Text = Strings.Get("DlgSettingsSoundsHint");
        SetTip(SoundsCheck, "DlgSettingsSoundsTip");
        ReceiveInvitesCheck.Content = Strings.Get("DlgSettingsReceiveInvites");
        ReceiveInvitesHint.Text = Strings.Get("DlgSettingsReceiveInvitesHint");
        SetTip(ReceiveInvitesCheck, "DlgSettingsReceiveInvitesTip");

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
        SetTip(AutoCheckCheck, "DlgLauncherSettingsAutoCheckTip");
        OpenPostUpdateCheck.Content = Strings.Get("DlgLauncherSettingsOpenPostUpdate");
        OpenPostUpdateHint.Text = Strings.Get("DlgLauncherSettingsOpenPostUpdateHint");
        SetTip(OpenPostUpdateCheck, "DlgLauncherSettingsOpenPostUpdateTip");

        CatalogSubheader.Text = Strings.Get("DlgLauncherSettingsCatalogSubheader");
        CatalogDefaultRadio.Content = Strings.Get("DlgLauncherSettingsCatalogDefault")
            + $"  ({DefaultCatalogRepo})";
        CatalogCustomRadio.Content = Strings.Get("DlgLauncherSettingsCatalogCustom");
        CatalogDisabledRadio.Content = Strings.Get("DlgLauncherSettingsCatalogDisabled");

        ClearCacheButton.Content = Strings.Get("DlgLauncherSettingsClearCache");
        ClearCacheHint.Text = Strings.Get("DlgLauncherSettingsClearCacheHint");
        SetTip(ClearCacheButton, "DlgLauncherSettingsClearCacheTip");

        TxSourcesHeader.Text = Strings.Get("DlgLauncherSettingsTxSourcesHeader");
        TxDefaultLabel.Text = Strings.Format("DlgLauncherSettingsTxDefaultLabel", DefaultTranslationsRepo);
        TxAddHeader.Text = Strings.Get("DlgLauncherSettingsTxAddHeader");
        TxAddButton.Content = Strings.Get("DlgLauncherSettingsTxAddButton");
        TxDisabledCheck.Content = Strings.Get("DlgLauncherSettingsTxDisableToggle");
        ClearTranslationsCacheButton.Content = Strings.Get("DlgLauncherSettingsClearTxCache");
        ClearTranslationsCacheHint.Text = Strings.Get("DlgLauncherSettingsClearTxCacheHint");

        TranslationsHeader.Text = Strings.Get("DlgLauncherSettingsTranslationsHeader");
        TranslationsDescription.Text = Strings.Get("DlgLauncherSettingsTranslationsDescription");
        OpenPackagerButton.Content = Strings.Get("DlgLauncherSettingsOpenPackager");
        TranslationsHint.Text = Strings.Get("DlgLauncherSettingsTranslationsHint");
        PatchGenHeader.Text = Strings.Get("DlgPatchGenSectionHeader");
        PatchGenDescription.Text = Strings.Get("DlgPatchGenSectionDescription");
        OpenPatchGeneratorButton.Content = Strings.Get("DlgPatchGenOpen");
        PatchGenHint.Text = Strings.Get("DlgPatchGenSectionHint");

        ClearAssetsButton.Content = Strings.Get("DlgLauncherSettingsClearAssets");
        ClearAssetsHint.Text = Strings.Get("DlgLauncherSettingsClearAssetsHint");
        SetTip(ClearAssetsButton, "DlgLauncherSettingsClearAssetsTip");
        ClearTempButton.Content = Strings.Get("DlgLauncherSettingsClearTemp");
        ClearTempHint.Text = Strings.Get("DlgLauncherSettingsClearTempHint");
        SetTip(ClearTempButton, "DlgLauncherSettingsClearTempTip");
        OpenDataFolderButton.Content = Strings.Get("DlgLauncherSettingsOpenDataFolder");
        OpenDataFolderHint.Text = Strings.Get("DlgLauncherSettingsOpenDataFolderHint");
        SetTip(OpenDataFolderButton, "DlgLauncherSettingsOpenDataFolderTip");

        SelfInstallButton.Content = Strings.Get("DlgLauncherSettingsInstall");
        SelfInstallHint.Text = Strings.Get("DlgLauncherSettingsInstallHint");
        SetTip(SelfInstallButton, "DlgLauncherSettingsInstallTip");
        // Hide the whole row once we're running from the installed location —
        // there's nothing to install then.
        SelfInstallRow.Visibility = Services.SelfInstallService.IsInstalled()
            ? Visibility.Collapsed : Visibility.Visible;

        PrivacyHeader.Text = Strings.Get("DlgLauncherSettingsPrivacyHeader");
        PrivacyDescription.Text = Strings.Get("DlgLauncherSettingsPrivacyDescription");
        TelemetryCheck.Content = Strings.Get("DlgLauncherSettingsTelemetry");
        TelemetryHint.Text = Strings.Get("DlgLauncherSettingsTelemetryHint");
        SetTip(TelemetryCheck, "DlgLauncherSettingsTelemetryTip");
        PrivacyPolicyButton.Content = Strings.Get("DlgLauncherSettingsViewPrivacy");
        PrivacyPolicyHint.Text = Strings.Get("DlgLauncherSettingsPrivacyHint");
        SetTip(PrivacyPolicyButton, "DlgLauncherSettingsPrivacyTip");

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

        // "Run in background" master toggle: on when auto-start is registered. The
        // REGISTRY is the source of truth here, not the config — which is why the
        // ON-by-default preference needs the one-time Run-key seed in MainWindow's
        // ctor to be visible at all (a config flag alone leaves this reading off).
        // Saving re-derives all three background flags from this one checkbox.
        //
        // Caveat: Task Manager's Startup tab DISABLES without deleting our value (it
        // writes Explorer\StartupApproved\Run instead), so a TM-disabled entry still
        // reads as registered here. We deliberately don't parse that blob — Windows
        // honours its own disable regardless of what we write.
        StartWithWindowsCheck.IsChecked = StartupRegistrationService.IsRegistered();
        EnableJoinLinksCheck.IsChecked = _config.EnableJoinLinks;
        CloseOnGameCheck.IsChecked = _config.CloseLauncherOnGameStart;
        // Close-to-tray opt-out — independent of the master toggle above.
        MinimizeToTrayCheck.IsChecked = _config.CloseToTray;
        ShowToastsCheck.IsChecked = _config.ShowToastNotifications;
        NotifyNewRoomsCheck.IsChecked = _config.NotifyNewRooms;
        SoundsCheck.IsChecked = _config.EnableSounds;
        ReceiveInvitesCheck.IsChecked = _config.ReceiveInvites;
        AutoCheckCheck.IsChecked = _config.CheckUpdatesOnStartup;
        OpenPostUpdateCheck.IsChecked = _config.OpenPostUpdatePages;
        TelemetryCheck.IsChecked = _config.MultiplayerTelemetryEnabled;

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

        // Translations: default repo is implicit/always-on; seed the user's
        // extra-repo working copy + the master disable toggle.
        _extraTxRepos.Clear();
        _extraTxRepos.AddRange(_config.GetExtraTranslationsFolderRepos());
        RenderTxRepoList();
        TxDisabledCheck.IsChecked = _config.CommunityTranslationsDisabled;
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
        TabPrivacyBtn.Tag = ReferenceEquals(activeBtn, TabPrivacyBtn) ? "active" : null;

        GeneralPanel.Visibility = ReferenceEquals(activeBtn, TabGeneralBtn) ? Visibility.Visible : Visibility.Collapsed;
        InterfacePanel.Visibility = ReferenceEquals(activeBtn, TabInterfaceBtn) ? Visibility.Visible : Visibility.Collapsed;
        UpdatesPanel.Visibility = ReferenceEquals(activeBtn, TabUpdatesBtn) ? Visibility.Visible : Visibility.Collapsed;
        CatalogPanel.Visibility = ReferenceEquals(activeBtn, TabCatalogBtn) ? Visibility.Visible : Visibility.Collapsed;
        TranslationsPanel.Visibility = ReferenceEquals(activeBtn, TabTranslationsBtn) ? Visibility.Visible : Visibility.Collapsed;
        MaintenancePanel.Visibility = ReferenceEquals(activeBtn, TabMaintenanceBtn) ? Visibility.Visible : Visibility.Collapsed;
        PrivacyPanel.Visibility = ReferenceEquals(activeBtn, TabPrivacyBtn) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TabGeneralBtn_Click(object sender, RoutedEventArgs e) => SetActiveTab(TabGeneralBtn);
    private void TabInterfaceBtn_Click(object sender, RoutedEventArgs e) => SetActiveTab(TabInterfaceBtn);
    private void TabUpdatesBtn_Click(object sender, RoutedEventArgs e) => SetActiveTab(TabUpdatesBtn);
    private void TabCatalogBtn_Click(object sender, RoutedEventArgs e) => SetActiveTab(TabCatalogBtn);
    private void TabTranslationsBtn_Click(object sender, RoutedEventArgs e) => SetActiveTab(TabTranslationsBtn);
    private void TabMaintenanceBtn_Click(object sender, RoutedEventArgs e) => SetActiveTab(TabMaintenanceBtn);
    private void TabPrivacyBtn_Click(object sender, RoutedEventArgs e) => SetActiveTab(TabPrivacyBtn);

    /// <summary>
    /// Opens the project's privacy policy (PRIVACY.md on GitHub) in the
    /// user's browser. The policy is also reachable from the Discord
    /// sign-in dialog — see <see cref="GitHubLoginDialog"/>.
    /// </summary>
    private void PrivacyPolicyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = LauncherConfig.PrivacyPolicyUrl,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Open privacy policy failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Opens the launcher's per-user data folder (%LocalAppData%\AoE3ModLauncher)
    /// in Explorer — where config, the debug log and caches live now that they no
    /// longer clutter the .exe's own folder. Creates it first so the open never
    /// fails on a brand-new install that hasn't written anything yet.
    /// </summary>
    private void OpenDataFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.IO.Directory.CreateDirectory(AppPaths.DataDir);
            Process.Start(new ProcessStartInfo
            {
                FileName = AppPaths.DataDir,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Open data folder failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Lightweight self-install: copy the portable exe to a stable per-user
    /// location + shortcuts (<see cref="SelfInstallService"/>), then offer to
    /// relaunch from there. Opt-in; the exe keeps self-updating in place.
    /// </summary>
    private void SelfInstallButton_Click(object sender, RoutedEventArgs e)
    {
        SelfInstallButton.IsEnabled = false;
        var (ok, message) = Services.SelfInstallService.Install();
        if (!ok)
        {
            SelfInstallHint.Text = Strings.Format("DlgLauncherSettingsInstallFailed", message);
            SelfInstallButton.IsEnabled = true;
            return;
        }

        SelfInstallHint.Text = Strings.Format(
            "DlgLauncherSettingsInstallDone", Services.SelfInstallService.CanonicalExe);

        // Whether the install also enables "run in background" (auto-start) is
        // governed by the SINGLE GENERAL toggle — there's no separate install-time
        // checkbox anymore (it duplicated / contradicted this one). If it's on,
        // enable the three background flags AND register auto-start pointing at the
        // INSTALLED exe (we're still running the portable one, so ProcessPath would
        // be wrong — pass the canonical path explicitly). The installed instance
        // reads the same %LocalAppData% config after relaunch, so the Settings
        // toggle stays consistent. If it's off, the install registers nothing (no
        // silent Run-key — AV-safe).
        if (StartWithWindowsCheck.IsChecked == true)
        {
            _config.StartWithWindows = true;
            _config.MinimizeToTray = true;
            _config.StartMinimized = true;
            try { _config.Save(); }
            catch (Exception ex) { DiagnosticLog.Write($"SelfInstall: config save failed: {ex.Message}"); }
            StartupRegistrationService.Apply(
                enabled: true, startMinimized: true,
                exePathOverride: Services.SelfInstallService.CanonicalExe);
        }

        var relaunch = MessageBox.Show(
            Strings.Get("DlgLauncherSettingsInstallRelaunchBody"),
            Strings.Get("DlgLauncherSettingsInstallRelaunchTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (relaunch == MessageBoxResult.Yes)
            Services.SelfInstallService.RelaunchInstalledAndExit();
        else
            SelfInstallButton.IsEnabled = true;
    }

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

    /// <summary>
    /// Launches the modder-facing incremental patch generator (diffs two overlay zips into a
    /// small delta patch + descriptor for a GitHubReleases mod). Mod-agnostic like the packager.
    /// </summary>
    private void OpenPatchGeneratorButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PatchGeneratorDialog
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
                FontSize = (double)Application.Current.FindResource("FontSizeBody"),
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
                FontSize = (double)Application.Current.FindResource("FontSizeBody"),
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
                    FontSize = (double)Application.Current.FindResource("FontSizeCaption"),
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
    /// Rebuilds the extra-translation-repos list (Settings → TRANSLATIONS) from
    /// <see cref="_extraTxRepos"/> — one row per repo with a ✕ remove button.
    /// Mirrors <see cref="RenderTabOrderList"/> (manual code-behind rows, full
    /// re-render on mutate). Shows a muted placeholder when the list is empty.
    /// </summary>
    private void RenderTxRepoList()
    {
        TxRepoList.Children.Clear();

        if (_extraTxRepos.Count == 0)
        {
            TxRepoList.Children.Add(new TextBlock
            {
                Text = Strings.Get("DlgLauncherSettingsTxNoneYet"),
                Foreground = (Brush)FindResource("TextSecondary"),
                FontSize = (double)Application.Current.FindResource("FontSizeCaption"),
                Margin = new Thickness(0, 0, 0, 8),
            });
            return;
        }

        for (int i = 0; i < _extraTxRepos.Count; i++)
        {
            var row = new Border
            {
                Background = (Brush)FindResource("MpSurface"),
                BorderBrush = (Brush)FindResource("BorderSubtle"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 6, 6, 6),
                Margin = new Thickness(0, 0, 0, 6),
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var name = new TextBlock
            {
                Text = _extraTxRepos[i],
                Foreground = (Brush)FindResource("TextPrimary"),
                FontSize = (double)Application.Current.FindResource("FontSizeBody"),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetColumn(name, 0);
            grid.Children.Add(name);

            var removeBtn = new Button
            {
                Style = (Style)FindResource("PropertyActionButton"),
                Content = "✕",
                MinWidth = 36,
                Margin = new Thickness(6, 0, 0, 0),
                ToolTip = Strings.Get("DlgLauncherSettingsTxRemoveTooltip"),
                Tag = i,
            };
            removeBtn.Click += RemoveTxRepo_Click;
            Grid.SetColumn(removeBtn, 1);
            grid.Children.Add(removeBtn);

            row.Child = grid;
            TxRepoList.Children.Add(row);
        }
    }

    private void RemoveTxRepo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int i } && i >= 0 && i < _extraTxRepos.Count)
        {
            _extraTxRepos.RemoveAt(i);
            RenderTxRepoList();
        }
    }

    /// <summary>
    /// Validates the typed "owner/repo" (same <see cref="RepoRegex"/> as the
    /// catalog), rejects blanks/dupes (vs the list and vs the default repo), and
    /// appends it to <see cref="_extraTxRepos"/>. Errors show inline via
    /// <c>TxInvalidText</c>; the change commits to config only on Save.
    /// </summary>
    private void TxAddButton_Click(object sender, RoutedEventArgs e)
    {
        var typed = (TxAddBox.Text ?? "").Trim();
        if (!RepoRegex.IsMatch(typed))
        {
            TxInvalidText.Text = Strings.Get("DlgLauncherSettingsInvalidRepo");
            TxInvalidText.Visibility = Visibility.Visible;
            TxAddBox.Focus();
            return;
        }
        if (string.Equals(typed, DefaultTranslationsRepo, StringComparison.OrdinalIgnoreCase)
            || _extraTxRepos.FindIndex(r => string.Equals(r, typed, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            TxInvalidText.Text = Strings.Get("DlgLauncherSettingsTxDuplicate");
            TxInvalidText.Visibility = Visibility.Visible;
            TxAddBox.Focus();
            return;
        }

        TxInvalidText.Visibility = Visibility.Collapsed;
        _extraTxRepos.Add(typed);
        TxAddBox.Text = "";
        RenderTxRepoList();
        TxAddBox.Focus();
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

        // (Translations extra-repo list is validated at Add time, so nothing to
        //  validate here — the working copy is committed below.)

        // 1b. Auto-start registry write. Done BEFORE any config mutation, for the
        //     same reason as step 1: it's the one side effect that can genuinely
        //     fail (managed-PC policy, AV blocking the Run key), and failing here
        //     leaves nothing half-applied.
        //
        //     This return value used to be discarded, which made the failure
        //     invisible AND self-contradicting: the config kept saying "on" while
        //     the checkbox — which reads the REGISTRY, not the config — came back
        //     UNCHECKED next open, with no explanation. Say it out loud instead.
        var wantBackground = StartWithWindowsCheck.IsChecked == true;
        if (!StartupRegistrationService.Apply(wantBackground, startMinimized: wantBackground))
        {
            SetHint(StartWithWindowsHint, Strings.Get("DlgLauncherSettingsStartupFailed"), success: false);
            SetActiveTab(TabGeneralBtn);
            return;
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
        _config.ShowToastNotifications = ShowToastsCheck.IsChecked == true;
        _config.NotifyNewRooms = NotifyNewRoomsCheck.IsChecked == true;
        _config.EnableSounds = SoundsCheck.IsChecked == true;
        Services.SoundService.Enabled = _config.EnableSounds;
        _config.ReceiveInvites = ReceiveInvitesCheck.IsChecked == true;
        _config.CheckUpdatesOnStartup = AutoCheckCheck.IsChecked == true;
        _config.OpenPostUpdatePages = OpenPostUpdateCheck.IsChecked == true;
        _config.MultiplayerTelemetryEnabled = TelemetryCheck.IsChecked == true;
        _config.ModsCatalogRepo = newCatalogRepo;
        _config.ExtraTranslationsFolderRepos = _extraTxRepos.ToArray();
        _config.CommunityTranslationsDisabled = TxDisabledCheck.IsChecked == true;
        // Single "Run in background" toggle drives the three background flags
        // together: auto-start with Windows, keep the tray icon resident, and
        // auto-start opens straight to the tray. See DlgLauncherSettingsStartWithWindows.
        var runInBackground = StartWithWindowsCheck.IsChecked == true;
        _config.StartWithWindows = runInBackground;
        _config.MinimizeToTray = runInBackground;
        _config.StartMinimized = runInBackground;
        // Close-to-tray is INDEPENDENT of the master toggle: it governs only the
        // X / close-button behaviour (default on; unchecking restores "X = quit").
        _config.CloseToTray = MinimizeToTrayCheck.IsChecked == true;
        _config.EnableJoinLinks = EnableJoinLinksCheck.IsChecked == true;

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
        //    * (The autostart registry write already happened in step 1b — it has
        //      to run before the config is touched so a failure can abort cleanly.)
        //    * Language change goes through Strings so the rest of the
        //      app updates immediately.
        if (_config.EnableJoinLinks) Services.DeepLinkService.EnsureRegistered();
        else Services.DeepLinkService.EnsureUnregistered();
        Strings.SetLanguage(newLang);
        // Re-apply the telemetry opt-in immediately so the change takes
        // effect this session without a restart (mirrors how MainWindow
        // wires it at startup).
        MultiplayerTelemetry.Enabled = _config.MultiplayerTelemetryEnabled;

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
    /// "Clear translations cache" button. Community translations have no
    /// on-disk cache — the index lives only in MainWindow's in-memory
    /// <c>_cachedTranslationIndex</c> — so this invokes the caller-provided
    /// <see cref="TranslationsCacheCleared"/> callback to null that index and
    /// re-fetch live. Does NOT close the dialog.
    /// </summary>
    private void ClearTranslationsCacheButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            TranslationsCacheCleared?.Invoke();
            SetHint(ClearTranslationsCacheHint,
                Strings.Get("DlgLauncherSettingsCacheCleared"),
                success: true);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Clear translations cache failed: {ex.Message}");
            SetHint(ClearTranslationsCacheHint, ex.Message, success: false);
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
            // Re-download live: the launcher window stays open (non-modal), so
            // ask it to revalidate now instead of leaving monograms until restart.
            if (deleted > 0)
                AssetsCleared?.Invoke();
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
