using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using System.Threading.Tasks;
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
    /// Invoked after a local <c>mod.json</c> is added or removed, so the (still open,
    /// non-modal) launcher re-merges the catalog and the Workshop shows the change without
    /// a restart. Same shape as <see cref="AssetsCleared"/>: this dialog can't refresh the
    /// catalog itself. Null = no live refresh.
    /// </summary>
    public Action? LocalModsChanged { get; set; }

    /// <summary>
    /// Asks the shell to check GitHub for a newer launcher and report what it found, so the
    /// hint beside the button can say it.
    ///
    /// <para>A <c>Func</c> rather than an <c>Action</c> like its siblings, because this one has
    /// an answer worth showing and the dialog is NOT modal — the main window's status bar sits
    /// behind it, which is the same reason the mod's own "check for updates" reports inline.</para>
    ///
    /// <para>Null = the button hides itself: better than a control that does nothing.</para>
    /// </summary>
    public Func<Task<bool?>>? CheckLauncherUpdateRequested { get; set; }

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

    /// <summary>
    /// Guards <see cref="TextScaleCombo_SelectionChanged"/> while the combo is being
    /// populated or re-selected in code. Without it, rebuilding the items on a language
    /// change would fire the handler and apply a size the user never picked.
    /// </summary>
    private bool _suppressTextScale;

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
        // x:Name kept (TabTranslations*) while the label moved on — same call the repo
        // already made when this tab stopped being TRANSLATIONS and became PACKAGER.
        // Renaming it means touching XAML + code-behind for nothing and loses git blame.
        TabTranslationsLabel.Text = Strings.Get("DlgLauncherSettingsSectionDeveloper");
        TabMaintenanceLabel.Text = Strings.Get("DlgLauncherSettingsSectionMaintenance");
        TabPrivacyLabel.Text = Strings.Get("DlgLauncherSettingsSectionPrivacy");

        TextScaleLabel.Text = Strings.Get("DlgSettingsTextScaleLabel");
        TextScaleHint.Text = Strings.Get("DlgSettingsTextScaleHint");
        SetTip(TextScaleCombo, "DlgSettingsTextScaleTip");
        BuildTextScaleItems();

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
        GameRecordingCheck.Content = Strings.Get("DlgSettingsGameRecording");
        GameRecordingHint.Text = Strings.Get("DlgSettingsGameRecordingHint");
        SetTip(GameRecordingCheck, "DlgSettingsGameRecordingTip");
        RecordReminderCheck.Content = Strings.Get("DlgSettingsRecordReminder");
        RecordReminderHint.Text = Strings.Get("DlgSettingsRecordReminderHint");
        SetTip(RecordReminderCheck, "DlgSettingsRecordReminderTip");
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
        PreviewToastsButton.Content = Strings.Get("DlgSettingsPreviewToasts");
        PreviewToastsHint.Text = Strings.Get("DlgSettingsPreviewToastsHint");
        DeveloperModeCheck.Content = Strings.Get("DlgSettingsDeveloperMode");
        DeveloperModeHint.Text = Strings.Get("DlgSettingsDeveloperModeHint");
        SetTip(DeveloperModeCheck, "DlgSettingsDeveloperModeTip");
        LocalModsHeader.Text = Strings.Get("DlgSettingsLocalModsHeader");
        LocalModsDescription.Text = Strings.Get("DlgSettingsLocalModsDescription");
        AddLocalModButton.Content = Strings.Get("DlgSettingsLocalModsAdd");

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
        CheckLauncherUpdateButton.Content = Strings.Get("DlgLauncherSettingsCheckUpdate");
        CheckLauncherUpdateHint.Text = Strings.Get("DlgLauncherSettingsCheckUpdateHint");
        SetTip(CheckLauncherUpdateButton, "DlgLauncherSettingsCheckUpdateTip");
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

        UninstallButton.Content = Strings.Get("DlgLauncherSettingsUninstall");
        UninstallHint.Text = Strings.Get("DlgLauncherSettingsUninstallHint");
        SetTip(UninstallButton, "DlgLauncherSettingsUninstallTip");
        // Exact counterpart of SelfInstallRow: only offer to uninstall when we're
        // actually running from the installed copy (a portable exe isn't "installed").
        UninstallRow.Visibility = Services.SelfInstallService.IsInstalled()
            ? Visibility.Visible : Visibility.Collapsed;

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

        // ...and the second caveat, which is worse because it is invisible: the Run key lives in
        // the hive of the account the launcher RUNS as. Under someone else's account that hive is
        // not the one Windows reads at logon, so the box above can read "on" while nothing ever
        // starts. IsRegistered() is left alone — it reports truthfully about its own hive — and
        // the missing piece is said here instead.
        var account = Services.RunningAccount.Current();
        StartWithWindowsAccountWarning.Text = account.Mismatch
            ? Strings.Format("DlgSettingsStartupWrongAccount", account.ProcessUser, account.SessionUser)
            : "";
        StartWithWindowsAccountWarning.Visibility =
            account.Mismatch ? Visibility.Visible : Visibility.Collapsed;
        EnableJoinLinksCheck.IsChecked = _config.EnableJoinLinks;
        GameRecordingCheck.IsChecked = _config.EnableGameRecording;
        // Shown the positive way round — the config stores "muted", the box offers "remind me".
        RecordReminderCheck.IsChecked = !_config.GameRecordingReminderMuted;
        CloseOnGameCheck.IsChecked = _config.CloseLauncherOnGameStart;
        // Close-to-tray opt-out — independent of the master toggle above.
        MinimizeToTrayCheck.IsChecked = _config.CloseToTray;
        ShowToastsCheck.IsChecked = _config.ShowToastNotifications;
        NotifyNewRoomsCheck.IsChecked = _config.NotifyNewRooms;
        SoundsCheck.IsChecked = _config.EnableSounds;
        ReceiveInvitesCheck.IsChecked = _config.ReceiveInvites;
        DeveloperModeCheck.IsChecked = _config.DeveloperMode;
        ApplyDeveloperModeVisibility();
        RefreshLocalModsList();
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

        SelectTextScale(_config.EffectiveTextScale);

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

    /// <summary>
    /// Shows or hides the DEVELOPER tab to match the checkbox, live.
    ///
    /// <para>Includes a fallback to GENERAL when the tab being hidden is the one on screen:
    /// otherwise the content pane goes blank with nothing marked in the sidebar, which
    /// reads as the dialog breaking rather than a setting taking effect.</para>
    /// </summary>
    private void ApplyDeveloperModeVisibility()
    {
        bool dev = DeveloperModeCheck.IsChecked == true;
        TabTranslationsBtn.Visibility = dev ? Visibility.Visible : Visibility.Collapsed;

        if (!dev && TranslationsPanel.Visibility == Visibility.Visible)
            SetActiveTab(TabGeneralBtn);
    }

    private void DeveloperModeCheck_Changed(object sender, RoutedEventArgs e)
        => ApplyDeveloperModeVisibility();

    /// <summary>
    /// Rebuilds the list of local manifests from <b>the config</b>, not from the merged
    /// registry.
    ///
    /// <para>That is the point of having this list at all: a manifest that no longer loads
    /// — deleted, or broken halfway through an edit — produces no mod in the Workshop, so
    /// the Workshop's own "stop using this file" can't reach it. Reading the config shows
    /// the entry regardless and keeps it removable.</para>
    /// </summary>
    private void RefreshLocalModsList()
    {
        LocalModsList.Children.Clear();

        var paths = _config.LocalCatalogModPaths ?? new List<string>();
        LocalModsEmptyHint.Text = paths.Count == 0
            ? Strings.Get("DlgSettingsLocalModsEmpty")
            : "";

        foreach (var path in paths.ToList())
        {
            var row = new System.Windows.Controls.Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
            { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
            { Width = GridLength.Auto });

            // Middle-ellipsis: the distinguishing part of these paths is the TAIL (the mod
            // folder), which WPF's end-trimming would be the first thing to hide.
            var label = new System.Windows.Controls.TextBlock
            {
                Text = Services.PathDisplay.CompactPathMiddle(path, 64),
                Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary"),
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = (double)FindResource("FontSizeCaption"),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = TooltipHelper.Wrap(path),
            };
            System.Windows.Controls.Grid.SetColumn(label, 0);
            row.Children.Add(label);

            var remove = new System.Windows.Controls.Button
            {
                Content = Strings.Get("DlgSettingsLocalModsRemove"),
                Style = (Style)FindResource("PropertyActionButton"),
                Margin = new Thickness(12, 0, 0, 0),
                Tag = path,
            };
            remove.Click += RemoveLocalModButton_Click;
            System.Windows.Controls.Grid.SetColumn(remove, 1);
            row.Children.Add(remove);

            LocalModsList.Children.Add(row);
        }
    }

    private void AddLocalModButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = Strings.Get("ModsBrowserAddLocalPickTitle"),
            Filter = Strings.Get("ModsBrowserAddLocalFilter"),
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(this) != true) return;

        var path = System.IO.Path.GetFullPath(dlg.FileName);

        Services.ModCatalogEntry entry;
        try
        {
            entry = Services.ModCatalogService.LoadLocalEntry(path);
        }
        catch (Exception ex)
        {
            // Naming the cause is the feature: the catalog CI only tells a modder their
            // manifest is wrong after they have opened the PR.
            Services.DiagnosticLog.Write($"Add local mod: rejected '{path}': {ex.Message}");
            MessageBox.Show(this,
                Strings.Format("ModsBrowserAddLocalInvalidBody", ex.Message),
                Strings.Get("ModsBrowserAddLocalInvalidTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Re-adding the same file is a no-op, not a duplicate row — a modder iterating
        // will press this more than once on the same manifest.
        if (!_config.LocalCatalogModPaths.Any(p =>
                string.Equals(p, path, StringComparison.OrdinalIgnoreCase)))
        {
            _config.LocalCatalogModPaths.Add(path);
        }

        PersistLocalMods();
        Services.DiagnosticLog.Write($"Add local mod: '{entry.Manifest.Id}' from {path}.");
    }

    private void RemoveLocalModButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string path }) return;

        _config.LocalCatalogModPaths.RemoveAll(p =>
            string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        PersistLocalMods();
        Services.DiagnosticLog.Write($"Removed local manifest: {path}");
    }

    /// <summary>
    /// Saves immediately and re-merges, unlike the rest of this dialog which waits for
    /// "Save changes".
    ///
    /// <para>Adding a manifest is an ACTION with a visible result in the Workshop, not a
    /// preference — making it wait for Save (or vanish on Cancel) would mean pressing
    /// "choose file", seeing the row appear, and finding no mod. The file itself is never
    /// touched either way.</para>
    /// </summary>
    private void PersistLocalMods()
    {
        _config.Save();
        Services.ModRegistry.SetLocalModPaths(_config.LocalCatalogModPaths);
        RefreshLocalModsList();
        LocalModsChanged?.Invoke();
    }

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
    /// <summary>
    /// Asks GitHub, now, whether there is a newer launcher.
    ///
    /// <para>The shell's forced path is what runs: it sends no <c>If-None-Match</c>, so a 304
    /// cannot come back as "nothing new" — which is the whole point of a check somebody asked
    /// for by hand — and it opens the update dialog itself when there is something.</para>
    ///
    /// <para>The button disables itself while the request is out. Three outcomes and each says
    /// so: there is a new version, you are up to date, or it could not be reached.</para>
    /// </summary>
    private async void CheckLauncherUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        var ask = CheckLauncherUpdateRequested;
        if (ask == null) return;

        CheckLauncherUpdateButton.IsEnabled = false;
        SetHint(CheckLauncherUpdateHint, Strings.Get("DlgLauncherSettingsCheckUpdateBusy"), success: true);
        try
        {
            // null = the request never got an answer. That is a different statement from
            // "nothing new", and saying the wrong one is how a broken check looks healthy.
            var found = await ask();
            SetHint(
                CheckLauncherUpdateHint,
                Strings.Get(found == null
                    ? "DlgLauncherSettingsCheckUpdateFailed"
                    : found.Value
                        ? "DlgLauncherSettingsCheckUpdateFound"
                        : "DlgLauncherSettingsCheckUpdateNone"),
                success: found != null);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"LauncherSettingsDialog: manual update check failed — {ex.Message}");
            SetHint(CheckLauncherUpdateHint, Strings.Get("DlgLauncherSettingsCheckUpdateFailed"), success: false);
        }
        finally
        {
            CheckLauncherUpdateButton.IsEnabled = true;
        }
    }

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
    /// Uninstall the canonical launcher copy (the counterpart of "Install on this
    /// PC"). A YesNoCancel confirm maps to the three outcomes: Yes = uninstall AND
    /// delete settings/data; No = uninstall but KEEP settings; Cancel = do nothing.
    /// Installed MODS are never touched. On a real uninstall the process hard-exits
    /// (the deferred script removes the folder after we're gone), so only the
    /// failure path keeps the window open.
    /// </summary>
    private void UninstallButton_Click(object sender, RoutedEventArgs e)
    {
        var choice = MessageBox.Show(
            Strings.Get("DlgLauncherSettingsUninstallConfirmBody"),
            Strings.Get("DlgLauncherSettingsUninstallConfirmTitle"),
            MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        if (choice == MessageBoxResult.Cancel) return;

        var removeUserData = choice == MessageBoxResult.Yes;
        UninstallButton.IsEnabled = false;
        if (!Services.SelfInstallService.UninstallAndExit(removeUserData))
        {
            SetHint(UninstallHint, Strings.Get("DlgLauncherSettingsUninstallFailed"), success: false);
            UninstallButton.IsEnabled = true;
        }
        // On success the app shuts down; nothing more to do here.
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
    /// <summary>
    /// Show sample notification cards so their look can be judged on the spot.
    ///
    /// <para>Goes through <c>MainWindow.PreviewNotificationToasts</c>, which uses the same
    /// routing the real events use — a preview that drew the card by a shortcut could
    /// flatter it and hide exactly the bug worth finding.</para>
    ///
    /// <para>The dialog stays open: the cards land on the DESKTOP, not inside the
    /// launcher, so there is nothing for this window to be covering.</para>
    /// </summary>
    private void PreviewToastsButton_Click(object sender, RoutedEventArgs e)
    {
        (Application.Current?.MainWindow as MainWindow)?.PreviewNotificationToasts();
    }

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

    // ---------------------------------------------------------------- text size

    /// <summary>
    /// Fills the text-size combo. Rebuilt from <see cref="ApplyLanguage"/> rather than
    /// declared in XAML because "Automatic" is a word, and the percentages carry one too.
    /// </summary>
    private void BuildTextScaleItems()
    {
        var previous = SelectedTextScale();
        _suppressTextScale = true;
        try
        {
            TextScaleCombo.Items.Clear();
            foreach (var choice in Services.TextScale.Choices)
            {
                TextScaleCombo.Items.Add(new ComboBoxItem
                {
                    Tag = choice,
                    Content = choice == Services.TextScale.Auto
                        ? Strings.Get("DlgSettingsTextScaleAuto")
                        : Strings.Format("DlgSettingsTextScalePercent", choice),
                });
            }
        }
        finally
        {
            _suppressTextScale = false;
        }
        SelectTextScale(previous);
    }

    private void SelectTextScale(string? value)
    {
        var wanted = string.IsNullOrWhiteSpace(value) ? Services.TextScale.Auto : value.Trim();
        _suppressTextScale = true;
        try
        {
            foreach (ComboBoxItem item in TextScaleCombo.Items)
            {
                if (!string.Equals(item.Tag as string, wanted, StringComparison.OrdinalIgnoreCase))
                    continue;
                TextScaleCombo.SelectedItem = item;
                break;
            }
            // An unrecognised config value (hand-edited, or from a newer build) falls back to
            // the first entry rather than leaving the combo blank. That entry is Automatic,
            // which is also the default — so a config nobody can read shows what a fresh
            // install shows, instead of quietly selecting a size the user never picked.
            if (TextScaleCombo.SelectedItem == null && TextScaleCombo.Items.Count > 0)
                TextScaleCombo.SelectedIndex = 0;
        }
        finally
        {
            _suppressTextScale = false;
        }
        RefreshTextScaleResolvedLine();
    }

    private string SelectedTextScale()
        => (TextScaleCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? Services.TextScale.Auto;

    /// <summary>
    /// Previews the chosen size immediately. Every font size in the app is a
    /// <c>DynamicResource</c>, so this re-lays out the live windows — including this one,
    /// which is the point: a text size you cannot see until you close the dialog is not
    /// something anybody can choose between.
    /// </summary>
    private void TextScaleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTextScale) return;
        App.RefreshTextScale(SelectedTextScale());
        RefreshTextScaleResolvedLine();
    }

    /// <summary>
    /// Puts the live preview back to what is actually saved. Called from
    /// <see cref="OnClosed"/> rather than from Cancel, so the ✕ and Esc — which never
    /// reach a button handler — are covered by the same line.
    /// </summary>
    private void RevertTextScalePreview()
    {
        if (ChangesSaved) return;
        App.RefreshTextScale(_config.EffectiveTextScale);
    }

    protected override void OnClosed(EventArgs e)
    {
        RevertTextScalePreview();
        base.OnClosed(e);
    }

    /// <summary>
    /// The line under the combo: what the setting resolved to, and — on Automatic — what it
    /// resolved it FROM.
    ///
    /// <para>It is not decoration. Automatic is the default, so without this the setting
    /// would silently pick a size and never say which, and a panel that did not report its
    /// diagonal would be indistinguishable from one the launcher decided to leave alone.</para>
    /// </summary>
    private void RefreshTextScaleResolvedLine()
    {
        var screen = Services.TextScale.DescribePrimaryScreen();
        var percent = (int)Math.Round(App.TextScaleFactor * 100);

        if (!string.Equals(SelectedTextScale(), Services.TextScale.Auto, StringComparison.OrdinalIgnoreCase))
        {
            TextScaleResolvedText.Text = "";
            return;
        }

        TextScaleResolvedText.Text = App.TextScaleDiagonalInches is double inches
            ? Strings.Format("DlgSettingsTextScaleResolved",
                             inches.ToString("0.#"), screen.Width, screen.Height, percent)
            : Strings.Format("DlgSettingsTextScaleResolvedUnknown", percent);
    }

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

        // Reliability for the portable exe: auto-start is a Run key that points at a
        // specific file. If that file is the portable/dev exe (Downloads, publish\,
        // bin\Debug\) it can be moved/deleted/rebuilt, and then login launches nothing
        // — the confirmed "auto-start did nothing" cause. So when enabling background
        // with NO USABLE stable copy yet, OFFER (opt-in — never silent, per
        // SelfInstallService's contract) to install one; the Run key then points at
        // the durable canonical path. Declining registers the portable path as before.
        // The gate is CanonicalLooksRunnable, not mere existence: a canonical copy that
        // exists but is a broken framework-dependent apphost (no DLLs) must ALSO trigger
        // the offer, or the toggle would register a copy that launches nothing.
        if (wantBackground && !SelfInstallService.CanonicalLooksRunnable())
        {
            var choice = MessageBox.Show(
                Strings.Get("DlgSettingsBgInstallPromptBody"),
                Strings.Get("DlgSettingsBgInstallPromptTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (choice == MessageBoxResult.Yes)
            {
                var (ok, msg) = SelfInstallService.Install();
                if (!ok)
                {
                    DiagnosticLog.Write($"Background install (opt-in) failed: {msg}");
                    SetHint(StartWithWindowsHint, Strings.Get("DlgSettingsBgInstallFailed"), success: false);
                    // Fall through: register the portable path so the toggle still
                    // takes effect (fragile, but no worse than before this change).
                }
            }
        }

        // Point the Run key at the STABLE installed copy when one exists (the opt-in
        // install above may have just created it), else the running exe.
        if (!StartupRegistrationService.Apply(
                wantBackground,
                startMinimized: wantBackground,
                exePathOverride: SelfInstallService.ResolveAutoStartExe()))
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
        // Mark the language as EXPLICITLY chosen only when it actually changes, so
        // the launcher stops following the OS display language and holds this pick.
        // (Saving Settings without touching the language must NOT lock it — see
        // MainWindow.ApplyStartupLanguage.)
        if (!string.Equals(newLang, _config.Language, StringComparison.Ordinal))
            _config.LanguageExplicitlyChosen = true;
        _config.Language = newLang;
        _config.CloseLauncherOnGameStart = CloseOnGameCheck.IsChecked == true;
        _config.ShowToastNotifications = ShowToastsCheck.IsChecked == true;
        _config.NotifyNewRooms = NotifyNewRoomsCheck.IsChecked == true;
        _config.EnableSounds = SoundsCheck.IsChecked == true;
        Services.SoundService.Enabled = _config.EnableSounds;
        _config.ReceiveInvites = ReceiveInvitesCheck.IsChecked == true;
        _config.DeveloperMode = DeveloperModeCheck.IsChecked == true;
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
        // No side effect here on purpose: each mod's profile is written on its next launch, by
        // GameSettingsStore.EnsureGameRecording. Touching five profiles from a settings dialog
        // would be writing to files the launcher has no reason to hold open.
        _config.EnableGameRecording = GameRecordingCheck.IsChecked == true;
        _config.GameRecordingReminderMuted = RecordReminderCheck.IsChecked != true;

        // Text size (Interface section). Already applied live by the combo, so there is
        // nothing to re-apply here — only the choice to persist.
        //
        // The "explicitly chosen" flag is set ONLY when the value actually changes, exactly as
        // the language one is: saving Settings without touching this combo must not lock the
        // config out of a future default. Until it is set the config FOLLOWS the default, which
        // is what lets that default ever reach anybody — see LauncherConfig.TextScaleExplicitlyChosen.
        var pickedScale = SelectedTextScale();
        if (!string.Equals(pickedScale, _config.EffectiveTextScale, StringComparison.OrdinalIgnoreCase))
            _config.TextScaleExplicitlyChosen = true;
        _config.TextScale = pickedScale;

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
