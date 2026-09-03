using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarsOfLibertyLauncher.Controls;
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
        ShowSection(SectionGeneral);

        // NO UiScale HERE, and that is a decision rather than an omission — the same one
        // the Workshop made, for the same reason.
        //
        // It used to carry a LayoutTransform against a 800x520 reference. That transform is
        // a ZOOM: it shrinks the padding, the gutters and the row heights along with the
        // type, and any scale below 1.0 flips the whole subtree from ClearType to grayscale,
        // so the text also goes thin and grey. The WIDTH term is the one that binds, and the
        // margin was 44 px: at 800 x 0.97 = 776 the window started shrinking, while its own
        // MinWidth is 760. Dragging the dialog a finger's width narrower dimmed every glyph
        // in it, next to a multiplayer tab still on the crisp path.
        //
        // Nothing is lost by dropping it: the section is one full-width column that reflows
        // to whatever width it is given, and the launcher-wide text-size setting
        // (Services/TextScale.cs) is what answers "the text is too small" now.

        // Last, so the snapshot is taken with every control already at its loaded value.
        ArmFooterTracking();
        RefreshRecordingBanner();
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

        // Sidebar entries — FIVE now, not seven. They reuse the "Section*" keys, which
        // moved to sentence case with the redesign: the same string is both the rail
        // entry and the section title above the content, and a rail is a list of names,
        // not of headers. The UPPERCASE keys that used to be rail labels (UPDATES,
        // CATALOG & SOURCES, MAINTENANCE, PRIVACY, DEVELOPER) survive as the GROUP
        // labels inside the two merged sections.
        TabGeneralLabel.Text = Strings.Get("DlgLauncherSettingsSectionGeneral");
        TabInterfaceLabel.Text = Strings.Get("DlgLauncherSettingsSectionInterface");
        TabGamesLabel.Text = Strings.Get("DlgLauncherSettingsSectionGames");
        TabModsLabel.Text = Strings.Get("DlgLauncherSettingsSectionModsUpdates");
        TabAdvancedLabel.Text = Strings.Get("DlgLauncherSettingsSectionAdvanced");

        // Which launcher this is and which build — the first question of any bug
        // report, answered without hunting for an About box.
        SettingsSearchPlaceholder.Text = Strings.Get("DlgSettingsSearchPlaceholder");
        SettingsNoResults.Text = Strings.Get("DlgSettingsSearchNoResults");
        RailProductText.Text = Strings.Get("AppProductName");
        RailVersionText.Text = LauncherUpdateService.CurrentInformationalTag;

        GroupStartupLabel.Text = Strings.Get("DlgSettingsGroupStartup");
        GroupNoticesLabel.Text = Strings.Get("DlgSettingsGroupNotices");
        GroupConnectionLabel.Text = Strings.Get("DlgSettingsGroupConnection");
        GroupRecordingLabel.Text = Strings.Get("DlgSettingsGroupRecording");
        GamesIntroText.Text = Strings.Get("DlgSettingsGamesIntro");
        GroupRankingLabel.Text = Strings.Get("DlgSettingsGroupRanking");
        RecordingOffTitle.Text = Strings.Get("DlgSettingsRecOffTitle");
        RecordingOffButton.Content = Strings.Get("DlgSettingsRecOffAction");
        ShowEloTitle.Text = Strings.Get("DlgSettingsShowEloTitle");
        ShowEloHint.Text = Strings.Get("DlgSettingsShowEloDesc");
        ReplayUpTitle.Text = Strings.Get("DlgSettingsReplayUpTitle");
        ReplayUpHint.Text = Strings.Get("DlgSettingsReplayUpDesc");
        ReplayAskRadio.Content = Strings.Get("DlgSettingsReplayAsk");
        ReplayAlwaysRadio.Content = Strings.Get("DlgSettingsReplayAlways");
        ReplayNeverRadio.Content = Strings.Get("DlgSettingsReplayNever");

        // The badge replaces the "(recommended)" that used to be glued onto three
        // labels — and, with it, the defensive paragraph that explained why.
        StartWithWindowsBadge.Text = Strings.Get("DlgSettingsBadgeRecommended");
        NotifyNewRoomsBadge.Text = Strings.Get("DlgSettingsBadgeRecommended");
        GameRecordingBadge.Text = Strings.Get("DlgSettingsBadgeRecommended");

        LanguageInstantHint.Text = Strings.Get("DlgSettingsLanguageInstant");
        SoundTestButton.Content = Strings.Get("DlgSettingsSoundTest");
        SetTip(SoundTestButton, "DlgSettingsSoundTestTip");

        TextScaleLabel.Text = Strings.Get("DlgSettingsTextScaleLabel");
        TextScaleHint.Text = Strings.Get("DlgSettingsTextScaleHint");
        SetTip(TextScaleCombo, "DlgSettingsTextScaleTip");
        BuildTextScaleItems();

        TabOrderLabel.Text = Strings.Get("DlgLauncherSettingsTabOrderLabel");  // now the group label
        TabOrderHint.Text = Strings.Get("DlgLauncherSettingsTabOrderHint");

        LanguageLabel.Text = Strings.Get("DlgLauncherSettingsLanguageLabel");
        // Theme picker removed — see LauncherSettingsDialog.xaml comment.

        StartWithWindowsTitle.Text = Strings.Get("DlgLauncherSettingsStartWithWindows");
        StartWithWindowsHint.Text = Strings.Get("DlgLauncherSettingsStartWithWindowsHint");
        SetTip(StartWithWindowsCheck, "DlgLauncherSettingsStartWithWindowsTip");
        EnableJoinLinksTitle.Text = Strings.Get("DlgLauncherSettingsJoinLinks");
        EnableJoinLinksHint.Text = Strings.Get("DlgLauncherSettingsJoinLinksHint");
        SetTip(EnableJoinLinksCheck, "DlgLauncherSettingsJoinLinksTip");
        GameRecordingTitle.Text = Strings.Get("DlgSettingsGameRecording");
        GameRecordingHint.Text = Strings.Get("DlgSettingsGameRecordingHint");
        SetTip(GameRecordingCheck, "DlgSettingsGameRecordingTip");
        RecordReminderTitle.Text = Strings.Get("DlgSettingsRecordReminder");
        RecordReminderHint.Text = Strings.Get("DlgSettingsRecordReminderHint");
        SetTip(RecordReminderCheck, "DlgSettingsRecordReminderTip");
        CloseOnGameTitle.Text = Strings.Get("DlgLauncherSettingsCloseOnGame");
        CloseOnGameHint.Text = Strings.Get("DlgLauncherSettingsCloseOnGameHint");
        SetTip(CloseOnGameCheck, "DlgLauncherSettingsCloseOnGameTip");
        MinimizeToTrayTitle.Text = Strings.Get("DlgLauncherSettingsMinimizeToTray");
        MinimizeToTrayHint.Text = Strings.Get("DlgLauncherSettingsMinimizeToTrayHint");
        SetTip(MinimizeToTrayCheck, "DlgLauncherSettingsMinimizeToTrayTip");
        ShowToastsTitle.Text = Strings.Get("DlgLauncherSettingsShowToasts");
        SetTip(ShowToastsCheck, "DlgLauncherSettingsShowToastsTip");
        NotifyNewRoomsTitle.Text = Strings.Get("DlgSettingsNotifyRooms");
        NotifyNewRoomsHint.Text = Strings.Get("DlgSettingsNotifyRoomsHint");
        SetTip(NotifyNewRoomsCheck, "DlgSettingsNotifyRoomsTip");
        SoundsTitle.Text = Strings.Get("DlgSettingsSounds");
        SoundsHint.Text = Strings.Get("DlgSettingsSoundsHint");
        SetTip(SoundsCheck, "DlgSettingsSoundsTip");
        ReceiveInvitesTitle.Text = Strings.Get("DlgSettingsReceiveInvites");
        ReceiveInvitesHint.Text = Strings.Get("DlgSettingsReceiveInvitesHint");
        SetTip(ReceiveInvitesCheck, "DlgSettingsReceiveInvitesTip");
        PreviewToastsHint.Text = Strings.Get("DlgSettingsPreviewToastsHint");
        DeveloperModeTitle.Text = Strings.Get("DlgSettingsDeveloperMode");
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

        // MODS AND UPDATES. Four of these rows are drawn and unread — see the block
        // comment on LauncherConfig.AutoUpdateMods.
        UpdatesGroupLabel.Text = Strings.Get("DlgLauncherSettingsSectionUpdates");
        AutoUpdateTitle.Text = Strings.Get("DlgSettingsUpdAutoTitle");
        AutoUpdateBadge.Text = Strings.Get("DlgSettingsBadgeRecommended");
        AutoUpdateHint.Text = Strings.Get("DlgSettingsUpdAutoDesc");
        DeltaOnlyTitle.Text = Strings.Get("DlgSettingsUpdDeltaTitle");
        DeltaOnlyHint.Text = Strings.Get("DlgSettingsUpdDeltaDesc");
        ChannelTitle.Text = Strings.Get("DlgSettingsUpdChannelTitle");
        ChannelHint.Text = Strings.Get("DlgSettingsUpdChannelDesc");
        ChannelStableRadio.Content = Strings.Get("DlgSettingsUpdChannelStable");
        ChannelBetaRadio.Content = Strings.Get("DlgSettingsUpdChannelBeta");
        DownloadLimitTitle.Text = Strings.Get("DlgSettingsUpdLimitTitle");
        DownloadLimitHint.Text = Strings.Get("DlgSettingsUpdLimitDesc");
        BuildDownloadLimitItems();

        AutoCheckTitle.Text = Strings.Get("DlgLauncherSettingsAutoCheck");
        AutoCheckHint.Text = Strings.Get("DlgLauncherSettingsAutoCheckHint");
        SetTip(AutoCheckCheck, "DlgLauncherSettingsAutoCheckTip");
        OpenPostUpdateTitle.Text = Strings.Get("DlgLauncherSettingsOpenPostUpdate");
        OpenPostUpdateHint.Text = Strings.Get("DlgLauncherSettingsOpenPostUpdateHint");
        SetTip(OpenPostUpdateCheck, "DlgLauncherSettingsOpenPostUpdateTip");

        InstalledModsLabel.Text = Strings.Get("DlgSettingsGroupInstalledMods");
        InstalledModsEmpty.Text = Strings.Get("DlgSettingsModsNone");
        RenderInstalledMods();

        CatalogSubheader.Text = Strings.Get("DlgLauncherSettingsSectionCatalog");
        CatalogSourceTitle.Text = Strings.Get("DlgSettingsCatalogSourceTitle");
        CatalogChangeButton.Content = Strings.Get("BtnChange");
        CatalogDefaultRadio.Content = Strings.Get("DlgLauncherSettingsCatalogDefault")
            + $"  ({DefaultCatalogRepo})";
        CatalogCustomRadio.Content = Strings.Get("DlgLauncherSettingsCatalogCustom");
        CatalogDisabledRadio.Content = Strings.Get("DlgLauncherSettingsCatalogDisabled");

        CatalogRefreshTitle.Text = Strings.Get("DlgSettingsCatalogRefreshTitle");
        ClearCacheButton.Content = Strings.Get("BtnRefresh");
        ClearCacheHint.Text = Strings.Format("DlgSettingsCatalogCount", ModRegistry.All.Count);
        SetTip(ClearCacheButton, "DlgLauncherSettingsClearCacheTip");

        VerifyDownloadsTitle.Text = Strings.Get("DlgSettingsVerifyTitle");
        VerifyDownloadsBadge.Text = Strings.Get("DlgSettingsBadgeRecommended");
        VerifyDownloadsHint.Text = Strings.Get("DlgSettingsVerifyDesc");

        TxSourcesHeader.Text = Strings.Get("DlgLauncherSettingsTxSourcesHeader");
        TxDefaultLabel.Text = Strings.Format("DlgLauncherSettingsTxDefaultLabel", DefaultTranslationsRepo);
        TxAddHeader.Text = Strings.Get("DlgLauncherSettingsTxAddHeader");
        TxAddButton.Content = Strings.Get("DlgLauncherSettingsTxAddButton");
        TxDisabledTitle.Text = Strings.Get("DlgLauncherSettingsTxDisableToggle");
        ClearTxCacheTitle.Text = Strings.Get("DlgLauncherSettingsClearTxCache");
        ClearTranslationsCacheButton.Content = Strings.Get("BtnClear");
        ClearTranslationsCacheHint.Text = Strings.Get("DlgLauncherSettingsClearTxCacheHint");

        TranslationsHeader.Text = Strings.Get("DlgLauncherSettingsTranslationsHeader");
        TranslationsDescription.Text = Strings.Get("DlgLauncherSettingsTranslationsDescription");
        OpenPackagerButton.Content = Strings.Get("DlgLauncherSettingsOpenPackager");
        TranslationsHint.Text = Strings.Get("DlgLauncherSettingsTranslationsHint");
        PatchGenHeader.Text = Strings.Get("DlgPatchGenSectionHeader");
        PatchGenDescription.Text = Strings.Get("DlgPatchGenSectionDescription");
        OpenPatchGeneratorButton.Content = Strings.Get("DlgPatchGenOpen");
        PatchGenHint.Text = Strings.Get("DlgPatchGenSectionHint");

        // MAINTENANCE. The verb moved onto a fixed-width button and the subject into the
        // row title beside it, so every button here is one word and the sentence lives in
        // the title — which is what stops the right edge of the column zigzagging.
        MaintenanceGroupLabel.Text = Strings.Get("DlgLauncherSettingsSectionMaintenance");
        ClearAssetsTitle.Text = Strings.Get("DlgSettingsAdvIconsTitle");
        ClearAssetsButton.Content = Strings.Get("BtnClear");
        ClearAssetsHint.Text = Strings.Get("DlgLauncherSettingsClearAssetsHint");
        SetTip(ClearAssetsButton, "DlgLauncherSettingsClearAssetsTip");
        ClearTempTitle.Text = Strings.Get("DlgSettingsAdvTempTitle");
        ClearTempButton.Content = Strings.Get("BtnDelete");
        ClearTempHint.Text = Strings.Get("DlgLauncherSettingsClearTempHint");
        SetTip(ClearTempButton, "DlgLauncherSettingsClearTempTip");
        OpenDataFolderTitle.Text = Strings.Get("DlgSettingsAdvDataFolderTitle");
        OpenDataFolderButton.Content = Strings.Get("BtnOpen");
        // The path IS the description here, monospaced, like every other path in the
        // redesign. An action's result still overwrites it, which is the same trade the
        // dialog already made everywhere else.
        OpenDataFolderHint.Text = Services.AppPaths.DataDir;
        SetTip(OpenDataFolderButton, "DlgLauncherSettingsOpenDataFolderTip");
        LauncherVersionTitle.Text = Strings.Get("DlgSettingsAdvVersionTitle");
        LauncherVersionBadge.Text = LauncherUpdateService.CurrentInformationalTag;
        CheckLauncherUpdateButton.Content = Strings.Get("BtnCheck");
        CheckLauncherUpdateHint.Text = Strings.Get("DlgLauncherSettingsCheckUpdateHint");
        SetTip(CheckLauncherUpdateButton, "DlgLauncherSettingsCheckUpdateTip");

        SelfInstallTitle.Text = Strings.Get("DlgSettingsAdvInstallTitle");
        SelfInstallButton.Content = Strings.Get("BtnInstallHere");
        SelfInstallHint.Text = Strings.Get("DlgLauncherSettingsInstallHint");
        SetTip(SelfInstallButton, "DlgLauncherSettingsInstallTip");
        // Hide the whole row once we're running from the installed location —
        // there's nothing to install then.
        SelfInstallRow.Visibility = Services.SelfInstallService.IsInstalled()
            ? Visibility.Collapsed : Visibility.Visible;

        UninstallTitle.Text = Strings.Get("DlgSettingsAdvUninstallTitle");
        UninstallButton.Content = Strings.Get("BtnUninstallHere");
        UninstallHint.Text = Strings.Get("DlgLauncherSettingsUninstallHint");
        SetTip(UninstallButton, "DlgLauncherSettingsUninstallTip");
        // Exact counterpart of SelfInstallRow: only offer to uninstall when we're
        // actually running from the installed copy (a portable exe isn't "installed").
        UninstallRow.Visibility = Services.SelfInstallService.IsInstalled()
            ? Visibility.Visible : Visibility.Collapsed;

        // PRIVACY. The old section header + paragraph became the group label above the
        // card and a one-sentence description on the row it belongs to.
        PrivacyHeader.Text = Strings.Get("DlgLauncherSettingsSectionPrivacy");
        TelemetryTitle.Text = Strings.Get("DlgSettingsAdvTelemetryTitle");
        ShareDecksTitle.Text = Strings.Get("DlgSettingsShareDecks");
        ShareDecksHint.Text = Strings.Get("DlgSettingsShareDecksHint");
        TelemetryHint.Text = Strings.Get("DlgLauncherSettingsTelemetryHint");
        SetTip(TelemetryCheck, "DlgLauncherSettingsTelemetryTip");
        PrivacyPolicyTitle.Text = Strings.Get("DlgSettingsAdvPrivacyTitle");
        PrivacyPolicyButton.Content = Strings.Get("BtnView");
        PrivacyPolicyHint.Text = Strings.Get("DlgSettingsAdvPrivacyDesc");
        SetTip(PrivacyPolicyButton, "DlgLauncherSettingsPrivacyTip");

        // DEVELOPER, folded. The panel is always in ADVANCED now; the switch in GENERAL
        // opens DevTools. Before, the whole thing vanished and nothing said it existed.
        DevGroupLabel.Text = Strings.Get("DlgLauncherSettingsSectionDeveloper");
        DevTitle.Text = Strings.Get("DlgSettingsAdvDevTitle");
        DevDesc.Text = Strings.Get("DlgSettingsAdvDevDesc");
        DevOffHint.Text = Strings.Get("DlgSettingsAdvDevOff");
        PreviewToastsTitle.Text = Strings.Get("DlgSettingsPreviewToasts");
        PreviewToastsButton.Content = Strings.Get("BtnView");

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
        StartWithWindowsAccountBox.Visibility =
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

        // The four drawn-but-unread update settings, plus the verify switch. They persist
        // so a choice survives; nothing consumes them yet. See LauncherConfig.AutoUpdateMods.
        AutoUpdateCheck.IsChecked = _config.AutoUpdateMods;
        DeltaOnlyCheck.IsChecked = _config.DeltaDownloadsOnly;
        bool beta = string.Equals(_config.UpdateChannel, "beta", StringComparison.OrdinalIgnoreCase);
        ChannelBetaRadio.IsChecked = beta;
        ChannelStableRadio.IsChecked = !beta;
        SelectDownloadLimit(_config.DownloadLimitKbps);
        VerifyDownloadsCheck.IsChecked = _config.VerifyDownloadSignatures;
        ShowEloCheck.IsChecked = _config.ShowMyElo;
        string replay = (_config.ReplayUploadPolicy ?? "ask").ToLowerInvariant();
        ReplayAlwaysRadio.IsChecked = replay == "always";
        ReplayNeverRadio.IsChecked = replay == "never";
        ReplayAskRadio.IsChecked = replay != "always" && replay != "never";
        RefreshCatalogSourceValue();
        TelemetryCheck.IsChecked = _config.MultiplayerTelemetryEnabled;
        ShareDecksCheck.IsChecked = _config.ShareDeckStats;

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
    /// Shows or hides the DEVELOPER tools to match the switch, live.
    ///
    /// <para>It used to be its own rail entry, which needed a fallback to GENERAL when
    /// the entry being hidden was the one on screen — otherwise the content pane went
    /// blank with nothing marked in the sidebar, reading as the dialog breaking rather
    /// than as a setting taking effect. It is a block inside ADVANCED now, so there is
    /// no entry to hide and nowhere to fall back to: re-showing the current section is
    /// the whole job, and ShowSection already owns the "developer mode is on" half of
    /// the rule.</para>
    /// </summary>
    private void ApplyDeveloperModeVisibility() => ShowSection(_activeSection);

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
                Foreground = (System.Windows.Media.Brush)FindResource("MpTextMuted"),
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

    /// <summary>
    /// What every setting on this page read when the dialog opened.
    ///
    /// <para>The footer counts what is pending instead of showing a permanent
    /// Cancel/Save pair, so an untouched window has nothing to decide — which is only
    /// possible if the dialog can tell "touched" from "opened". Comparing against a
    /// snapshot is what does that, and it is honest in the direction that matters: a
    /// setting toggled twice back to where it started reports as unchanged, because it
    /// IS unchanged.</para>
    /// </summary>
    private System.Collections.Generic.Dictionary<string, string>? _openedWith;

    private System.Collections.Generic.Dictionary<string, string> SettingsFingerprint()
    {
        static string B(System.Windows.Controls.Primitives.ToggleButton t) => t.IsChecked == true ? "1" : "0";
        static string T(System.Windows.Controls.ComboBox c) =>
            (c.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

        // Which of the three catalog radios is on, as one value: three separate
        // entries would count a switch between them as two changes.
        string catalog = CatalogCustomRadio.IsChecked == true ? "custom"
            : CatalogDisabledRadio.IsChecked == true ? "disabled"
            : "default";

        return new System.Collections.Generic.Dictionary<string, string>
        {
            ["language"] = T(LanguageCombo),
            ["startWithWindows"] = B(StartWithWindowsCheck),
            ["minimizeToTray"] = B(MinimizeToTrayCheck),
            ["closeOnGame"] = B(CloseOnGameCheck),
            ["notifyRooms"] = B(NotifyNewRoomsCheck),
            ["receiveInvites"] = B(ReceiveInvitesCheck),
            ["showToasts"] = B(ShowToastsCheck),
            ["sounds"] = B(SoundsCheck),
            ["joinLinks"] = B(EnableJoinLinksCheck),
            ["radmin"] = T(RadAsstCombo),
            ["developerMode"] = B(DeveloperModeCheck),
            ["gameRecording"] = B(GameRecordingCheck),
            ["recordReminder"] = B(RecordReminderCheck),
            ["textScale"] = T(TextScaleCombo),
            ["tabOrder"] = string.Join(",", _tabOrder),
            ["autoCheck"] = B(AutoCheckCheck),
            ["openPostUpdate"] = B(OpenPostUpdateCheck),
            ["autoUpdateMods"] = B(AutoUpdateCheck),
            ["deltaOnly"] = B(DeltaOnlyCheck),
            ["channel"] = ChannelBetaRadio.IsChecked == true ? "beta" : "stable",
            ["downloadLimit"] = SelectedDownloadLimit().ToString(),
            ["verifyDownloads"] = B(VerifyDownloadsCheck),
            ["showElo"] = B(ShowEloCheck),
            ["replayUpload"] = ReplayAlwaysRadio.IsChecked == true ? "always"
                : ReplayNeverRadio.IsChecked == true ? "never" : "ask",
            ["catalog"] = catalog,
            ["catalogRepo"] = CatalogCustomBox.Text?.Trim() ?? "",
            ["txRepos"] = string.Join(",", _extraTxRepos),
            ["txDisabled"] = B(TxDisabledCheck),
            ["telemetry"] = B(TelemetryCheck),
        };
    }

    /// <summary>
    /// Applies whatever just changed, then repaints the footer from what is still
    /// PENDING. Cheap enough to call on every keystroke and every toggle.
    ///
    /// <para>Two jobs in one place on purpose: the set of settings that apply instantly is
    /// exactly the complement of <see cref="DeferredSettingKeys"/>, and computing it twice
    /// is how the footer would come to promise something the write path does not do.</para>
    /// </summary>
    private void RefreshFooter()
    {
        if (_openedWith == null) return;

        var now = SettingsFingerprint();

        // Instant half: anything outside the deferred set is written and persisted as
        // soon as it differs from the snapshot, and the snapshot moves with it, so the
        // footer never counts a change that is already on disk.
        bool instantChanged = false;
        foreach (var kv in now)
        {
            if (System.Array.IndexOf(DeferredSettingKeys, kv.Key) >= 0) continue;
            if (!_openedWith.TryGetValue(kv.Key, out var was) || was != kv.Value)
                instantChanged = true;
        }
        if (instantChanged && !_applyingInstant)
        {
            ApplyInstantSettings();
            foreach (var kv in now)
                if (System.Array.IndexOf(DeferredSettingKeys, kv.Key) < 0)
                    _openedWith[kv.Key] = kv.Value;
        }

        // Deferred half: only the two that can be refused are ever pending.
        int changed = 0;
        foreach (var key in DeferredSettingKeys)
        {
            if (!now.TryGetValue(key, out var value)) continue;
            if (!_openedWith.TryGetValue(key, out var was) || was != value) changed++;
        }

        if (changed > 0)
        {
            UnsavedIndicator.Visibility = Visibility.Visible;
            UnsavedIndicatorDot.Visibility = Visibility.Visible;
            UnsavedText.Foreground = (System.Windows.Media.Brush)FindResource("MpCautionText");
            UnsavedText.Text = changed == 1
                ? Strings.Get("DlgSettingsUnsavedOne")
                : Strings.Format("DlgSettingsUnsavedMany", changed);
            CancelButton.Content = Strings.Get("BtnDiscard");
            SaveButton.Visibility = Visibility.Visible;
        }
        else
        {
            // Nothing pending, so the footer says what is true of everything else on the
            // page: it is already applied. The amber dot goes with the pending state; the
            // line stays, because "no unsaved changes" is worth stating once rather than
            // leaving the reader to infer it from an empty strip.
            UnsavedIndicator.Visibility = Visibility.Visible;
            UnsavedIndicatorDot.Visibility = Visibility.Collapsed;
            UnsavedText.Foreground = (System.Windows.Media.Brush)FindResource("MpTextMuted");
            UnsavedText.Text = Strings.Get("DlgSettingsAppliesInstantly");
            // With nothing pending there is nothing to discard, so the one button left
            // says what it does: close. Save is hidden rather than disabled - a disabled
            // button invites a click that will not come.
            CancelButton.Content = Strings.Get("BtnClose");
            SaveButton.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Arms the footer.
    ///
    /// <para>The handlers are attached to the content root as CLASS handlers rather than
    /// wired per control: Checked/Unchecked come from ToggleButton, which CheckBox,
    /// RadioButton and the new switch all derive from, so four subscriptions cover every
    /// input on all five sections — including the ones a later pass adds. Wiring them one
    /// by one is how a new setting silently stops counting.</para>
    ///
    /// <para><c>handledEventsToo: true</c> matters: a ComboBox marks its
    /// SelectionChanged handled on the way up.</para>
    /// </summary>
    private void ArmFooterTracking()
    {
        _openedWith = SettingsFingerprint();

        SettingsContentRoot.AddHandler(
            System.Windows.Controls.Primitives.ToggleButton.CheckedEvent,
            new RoutedEventHandler((_, _) => RefreshFooter()), true);
        SettingsContentRoot.AddHandler(
            System.Windows.Controls.Primitives.ToggleButton.UncheckedEvent,
            new RoutedEventHandler((_, _) => RefreshFooter()), true);
        SettingsContentRoot.AddHandler(
            System.Windows.Controls.Primitives.Selector.SelectionChangedEvent,
            new SelectionChangedEventHandler((_, _) => RefreshFooter()), true);
        SettingsContentRoot.AddHandler(
            System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler((_, _) => RefreshFooter()), true);

        RefreshFooter();
    }

    /// <summary>
    /// Whether AoE3 is currently set to record games, read from disk for one mod.
    ///
    /// <para>THREE answers, not two — <c>true</c>, <c>false</c> and <b>null</b>, and the
    /// null is the one that matters. The game writes its profile on its first run, so a
    /// freshly installed mod has none, and a row that said "recording is OFF" there would
    /// be stating something the disk does not say. Same distinction
    /// <c>SettingsImportResult.NoTargetProfile</c> exists to keep.</para>
    ///
    /// <para><c>ModState.GameRecordingApplied</c> is NOT this: it records what the
    /// launcher last WROTE, and the game rewrites the whole profile when it exits.
    /// Reading the file is the only truth.</para>
    /// </summary>
    private bool? ReadRecordingState(ModProfile profile)
    {
        try
        {
            var path = Services.GameSettingsStore.ProfilePathFor(profile, _config);
            if (path == null) return null;

            var current = GameSettingsSync.ReadSetting(
                System.IO.File.ReadAllText(path, System.Text.Encoding.Unicode),
                GameSettingsSync.GameOptionsSection,
                GameSettingsSync.RecordGameSetting);

            if (string.Equals(current, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(current, "false", StringComparison.OrdinalIgnoreCase)) return false;
            return null;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"LauncherSettings.ReadRecordingState: {ex.Message}");
            return null;
        }
    }


    /// <summary>
    /// Filters the settings by text, across every section.
    ///
    /// <para>It walks the panels looking for the shared row Borders rather than for a list
    /// the dialog maintains, so a row added later is searchable the day it is added — a
    /// hand-kept index is exactly how a search silently stops covering half a screen.</para>
    ///
    /// <para>An empty query restores the active section untouched. A query hides the rows
    /// that do not match, hides any group whose rows all went, and — if nothing in the
    /// current section matched — switches to the first section that has a hit, which is
    /// what makes it a search rather than a filter.</para>
    /// </summary>
    private void SettingsSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        string q = (SettingsSearchBox.Text ?? "").Trim();
        SettingsSearchPlaceholder.Visibility =
            q.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        var sections = SearchSections().ToList();

        if (q.Length == 0)
        {
            SectionSearch.Restore(sections);
            SettingsNoResults.Visibility = Visibility.Collapsed;
            // Still needed after a Restore that now puts things back exactly as they were:
            // ShowSection is what re-decides DevTools / DevOffHint, which the developer-mode
            // switch can have changed WHILE the search was running.
            ShowSection(_activeSection);
            return;
        }

        var hit = SectionSearch.Apply(q, sections);
        SettingsNoResults.Visibility = hit is null ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The sections the search covers, in the order that decides the first hit.</summary>
    private IEnumerable<SectionSearch.Section> SearchSections()
    {
        foreach (var (section, panel) in AllSectionPanelsWithId())
        {
            var id = section;
            yield return new SectionSearch.Section(panel, () => ShowSection(id));
        }
    }

    private IEnumerable<(string Section, Panel Panel)> AllSectionPanelsWithId()
    {
        yield return (SectionGeneral, GeneralPanel);
        yield return (SectionInterface, InterfacePanel);
        yield return (SectionGames, GamesPanel);
        yield return (SectionMods, UpdatesPanel);
        yield return (SectionMods, CatalogPanel);
        yield return (SectionAdvanced, MaintenancePanel);
        // The wrapper, NOT the two panels inside it. SectionSearch treats each direct child
        // as one group and descends into it, so PRIVACY and DEVELOPER each collapse whole when
        // nothing matches; yielding them separately would make it read each PANEL as a single
        // group and hide or keep it entire.
        yield return (SectionAdvanced, AdvancedExtrasPanel);
    }

    /// <summary>
    /// Opens the three ways to point the catalogue somewhere else.
    ///
    /// <para>The reference shows one line with the current source and a Change button;
    /// the radios are what Change reveals. Folding them keeps the row quiet without
    /// removing the choice, which is the point — a dialog that replaced them would be a
    /// second place to get the same thing wrong.</para>
    /// </summary>
    private void CatalogChangeButton_Click(object sender, RoutedEventArgs e)
    {
        bool open = CatalogEditBlock.Visibility == Visibility.Visible;
        CatalogEditBlock.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        if (!open) CatalogCustomBox.Focus();
    }

    /// <summary>The catalogue this launcher is actually reading, spelled out.</summary>
    private void RefreshCatalogSourceValue()
    {
        var repo = (_config.ModsCatalogRepo ?? "").Trim();
        CatalogSourceValue.Text = repo.Length == 0
            ? DefaultCatalogRepo
            : string.Equals(repo, "none", StringComparison.OrdinalIgnoreCase)
                ? Strings.Get("DlgLauncherSettingsCatalogDisabled")
                : repo;
    }

    /// <summary>
    /// The download-limit choices.
    ///
    /// <para>Nothing throttles anything yet — the value is stored and unread, like the
    /// three settings beside it. The entries are plain KB/s so that when
    /// <c>DownloadService</c>'s copy loop does learn to sleep, the number it needs is
    /// already the one on screen.</para>
    /// </summary>
    private void BuildDownloadLimitItems()
    {
        int current = (DownloadLimitCombo.SelectedItem as ComboBoxItem)?.Tag as int? ?? _config.DownloadLimitKbps;
        DownloadLimitCombo.Items.Clear();
        DownloadLimitCombo.Items.Add(new ComboBoxItem
        {
            Content = Strings.Get("DlgSettingsUpdLimitNone"),
            Tag = 0,
        });
        foreach (var kbps in new[] { 1024, 2048, 5120, 10240 })
        {
            DownloadLimitCombo.Items.Add(new ComboBoxItem
            {
                Content = $"{kbps / 1024} MB/s",
                Tag = kbps,
            });
        }
        SelectDownloadLimit(current);
    }

    private void SelectDownloadLimit(int kbps)
    {
        foreach (ComboBoxItem item in DownloadLimitCombo.Items)
        {
            if (item.Tag is int t && t == kbps) { DownloadLimitCombo.SelectedItem = item; return; }
        }
        if (DownloadLimitCombo.Items.Count > 0) DownloadLimitCombo.SelectedIndex = 0;
    }

    private int SelectedDownloadLimit()
        => (DownloadLimitCombo.SelectedItem as ComboBoxItem)?.Tag as int? ?? 0;

    /// <summary>
    /// One row per installed mod: icon, name, version, and either "Up to date" or an
    /// Update button.
    ///
    /// <para>Everything here comes from the config the dialog already holds — no disk, no
    /// network — which is why the reference's SIZE and FINGERPRINT are missing rather than
    /// approximated: one needs a walk of a multi-gigabyte tree and the other needs several
    /// files hashed, and neither belongs in a window opening.</para>
    ///
    /// <para>The verdict is the same comparison the launcher already trusts elsewhere:
    /// the version last installed against the latest one the last check saw. Both are
    /// stored, so a mod that has never been checked simply reports nothing rather than
    /// claiming to be current.</para>
    /// </summary>
    private void RenderInstalledMods()
    {
        InstalledModsList.Children.Clear();

        var rows = new List<(ModProfile Profile, ModState State)>();
        foreach (var profile in ModRegistry.All)
        {
            if (profile.IsStockGame) continue;
            if (!_config.Mods.TryGetValue(profile.Id, out var st)) continue;
            if (string.IsNullOrEmpty(st.InstallPath)) continue;
            rows.Add((profile, st));
        }

        InstalledModsCount.Text = rows.Count.ToString();
        InstalledModsEmptyRow.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        for (int i = 0; i < rows.Count; i++)
        {
            var (profile, st) = rows[i];
            bool last = i == rows.Count - 1;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var disc = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center,
                SnapsToDevicePixels = true,
                Background = MainWindow.TryLoadTileImage(profile.ResolveIconSource())
                    ?? (Brush?)Application.Current.TryFindResource("MpSurfaceAlt")
                    ?? Brushes.Transparent,
            };
            Grid.SetColumn(disc, 0);
            grid.Children.Add(disc);

            var text = new StackPanel { Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock
            {
                Text = profile.DisplayName,
                Style = (Style)Application.Current.FindResource("SetRowTitle"),
            });
            if (!string.IsNullOrWhiteSpace(st.LastKnownVersion))
            {
                text.Children.Add(new TextBlock
                {
                    Text = "v" + st.LastKnownVersion,
                    Style = (Style)Application.Current.FindResource("SetMonoValue"),
                    Margin = new Thickness(0, 4, 0, 0),
                });
            }
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            bool behind = !string.IsNullOrWhiteSpace(st.LastKnownVersion)
                && !string.IsNullOrWhiteSpace(st.LastKnownLatestVersion)
                && !string.Equals(st.LastKnownVersion, st.LastKnownLatestVersion, StringComparison.OrdinalIgnoreCase);

            FrameworkElement action;
            if (behind)
            {
                action = new Button
                {
                    Content = Strings.Get("DlgSettingsModsUpdate"),
                    Style = (Style)Application.Current.FindResource("SetActionButtonPrimary"),
                };
            }
            else
            {
                // Dimmed text, not a disabled button: a disabled button invites a click
                // that will not come.
                action = new TextBlock
                {
                    Text = Strings.Get("DlgSettingsModsUpToDate"),
                    Style = (Style)Application.Current.FindResource("SetActionQuiet"),
                };
            }
            Grid.SetColumn(action, 2);
            grid.Children.Add(action);

            InstalledModsList.Children.Add(new Border
            {
                Style = (Style)Application.Current.FindResource(last ? "SetActionRowLast" : "SetActionRow"),
                Child = grid,
            });
        }
    }

    /// <summary>
    /// Shows the amber banner when a mod on this PC is not set to record.
    ///
    /// <para>Recording off means the next match cannot be rated, and nothing else on this
    /// page can be wrong without the player knowing. A mod with NO profile yet is not
    /// wrong — the game writes it on its first run — so it does not raise the banner.
    /// </para>
    /// </summary>
    private void RefreshRecordingBanner()
    {
        ModProfile? offender = null;
        foreach (var profile in ModRegistry.All)
        {
            if (profile.IsStockGame) continue;
            if (!_config.Mods.TryGetValue(profile.Id, out var st)) continue;
            if (string.IsNullOrEmpty(st.InstallPath)) continue;
            if (ReadRecordingState(profile) == false) { offender = profile; break; }
        }

        if (offender == null)
        {
            RecordingOffBanner.Visibility = Visibility.Collapsed;
        }
        else
        {
            RecordingOffBanner.Visibility = Visibility.Visible;
            RecordingOffBody.Text = Strings.Format("DlgSettingsRecOffBody", offender.DisplayName);
        }

        TabGamesDot.Visibility = offender == null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// Switches AoE3's recording back on for every installed mod that has it off.
    ///
    /// <para>It clears <c>GameRecordingApplied</c> first, and that is the whole trick:
    /// <c>EnsureGameRecording</c> only writes when the marker disagrees with the
    /// preference, so a profile the launcher already switched on once — and the player
    /// later switched off inside the game — looks settled and would be left alone.
    /// Clearing the marker makes it "never touched", which is the one state that writes.
    /// </para>
    /// </summary>
    private void RecordingOffButton_Click(object sender, RoutedEventArgs e)
    {
        // "Switch it on" has to mean the launcher is allowed to, or the write is undone
        // by the next launch.
        _config.EnableGameRecording = true;
        GameRecordingCheck.IsChecked = true;

        foreach (var profile in ModRegistry.All)
        {
            if (profile.IsStockGame) continue;
            if (!_config.Mods.TryGetValue(profile.Id, out var st)) continue;
            if (string.IsNullOrEmpty(st.InstallPath)) continue;
            if (ReadRecordingState(profile) != false) continue;

            st.GameRecordingApplied = null;
            var result = Services.GameSettingsStore.EnsureGameRecording(profile, _config);
            DiagnosticLog.Write($"LauncherSettings: recording re-armed for '{profile.Id}' → {result}");
        }

        RefreshRecordingBanner();
    }

    /// <summary>The five rail sections. Stable ids, not indices, so the order can move.</summary>
    private const string SectionGeneral = "general";
    private const string SectionInterface = "interface";
    private const string SectionGames = "games";
    private const string SectionMods = "mods";
    private const string SectionAdvanced = "advanced";

    /// <summary>The section on screen. Needed because two things can now change what
    /// is visible: clicking the rail, and toggling developer mode.</summary>
    private string _activeSection = SectionGeneral;

    /// <summary>
    /// Shows one section.
    ///
    /// <para>A section is no longer one panel: the redesign merged seven rail entries
    /// into five, so MODS AND UPDATES shows two panels and ADVANCED shows three. The
    /// panels themselves kept their names and their contents, which is what lets the
    /// merge happen without touching a single LoadFromConfig or SaveButton_Click line —
    /// only the grouping moved.</para>
    ///
    /// <para>The DEVELOPER block is the one panel whose visibility is not decided by the
    /// section alone: it also needs the developer-mode switch on. Deciding both here, in
    /// one place, is what stops the two rules drifting apart.</para>
    /// </summary>
    private void ShowSection(string section)
    {
        _activeSection = section;

        TabGeneralBtn.Tag = section == SectionGeneral ? "active" : null;
        TabInterfaceBtn.Tag = section == SectionInterface ? "active" : null;
        TabGamesBtn.Tag = section == SectionGames ? "active" : null;
        TabModsBtn.Tag = section == SectionMods ? "active" : null;
        TabAdvancedBtn.Tag = section == SectionAdvanced ? "active" : null;

        GeneralPanel.Visibility = Vis(section == SectionGeneral);
        InterfacePanel.Visibility = Vis(section == SectionInterface);
        GamesPanel.Visibility = Vis(section == SectionGames);

        // MODS AND UPDATES = the old Updates + Catalog. They explained each other:
        // the update channel used to sit apart from the catalog the mods come from.
        UpdatesPanel.Visibility = Vis(section == SectionMods);
        CatalogPanel.Visibility = Vis(section == SectionMods);

        // ADVANCED = the old Maintenance + Privacy + Developer, which between them
        // filled half a screen across three rail entries. Privacy and Developer hold
        // one block each, so they share one panel (AdvancedExtrasPanel) and sit side by
        // side; separately they took a column each and left the other blank.
        MaintenancePanel.Visibility = Vis(section == SectionAdvanced);
        AdvancedExtrasPanel.Visibility = Vis(section == SectionAdvanced);

        // The developer block is always PRESENT in ADVANCED; the switch in GENERAL only
        // opens it. Hiding the whole panel — which is what this did — left nothing on
        // screen to say the tools existed or how to get them back.
        bool dev = DeveloperModeCheck.IsChecked == true;
        DevTools.Visibility = Vis(dev);
        DevOffHint.Visibility = Vis(!dev);
        DevChevron.Text = dev ? "" : "";   // Segoe MDL2: ChevronDown / ChevronRight

        SectionTitleText.Text = section switch
        {
            SectionInterface => Strings.Get("DlgLauncherSettingsSectionInterface"),
            SectionGames => Strings.Get("DlgLauncherSettingsSectionGames"),
            SectionMods => Strings.Get("DlgLauncherSettingsSectionModsUpdates"),
            SectionAdvanced => Strings.Get("DlgLauncherSettingsSectionAdvanced"),
            _ => Strings.Get("DlgLauncherSettingsSectionGeneral"),
        };

        static Visibility Vis(bool on) => on ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TabGeneralBtn_Click(object sender, RoutedEventArgs e) => ShowSection(SectionGeneral);
    private void TabInterfaceBtn_Click(object sender, RoutedEventArgs e) => ShowSection(SectionInterface);
    private void TabGamesBtn_Click(object sender, RoutedEventArgs e) => ShowSection(SectionGames);
    private void TabModsBtn_Click(object sender, RoutedEventArgs e) => ShowSection(SectionMods);
    private void TabAdvancedBtn_Click(object sender, RoutedEventArgs e) => ShowSection(SectionAdvanced);

    /// <summary>
    /// Plays the notification sound at the user's current volume.
    ///
    /// <para>The only way to judge a sound setting is to hear it, and the alternative
    /// was joining a room and waiting for somebody to type. It deliberately ignores the
    /// switch beside it: you press this to find out what the sound IS, which is a
    /// question worth answering while the setting is still off.</para>
    /// </summary>
    private void SoundTestButton_Click(object sender, RoutedEventArgs e)
    {
        bool was = SoundService.Enabled;
        try
        {
            SoundService.Enabled = true;
            SoundService.PlayNotification();
        }
        finally
        {
            SoundService.Enabled = was;
        }
    }

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
        // The two list-backed settings (tab order, extra translation repos) live in
        // fields, not controls, so the routed-event handlers on the content root cannot
        // see them change. Repainting the footer here covers every mutation, because
        // every mutation re-renders. At load _openedWith is still null and this is a
        // no-op, which is what keeps the snapshot from counting itself.
        RefreshFooter();
        TabOrderList.Children.Clear();

        for (int i = 0; i < _tabOrder.Count; i++)
        {
            string id = _tabOrder[i];
            bool isFirst = i == 0;
            bool isLast = i == _tabOrder.Count - 1;

            var row = new Border
            {
                Background = (Brush)FindResource("MpSurface"),
                BorderBrush = (Brush)FindResource("MpRimSoft"),
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
                Foreground = (Brush)FindResource("MpTextMuted"),
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
                Foreground = (Brush)FindResource("MpTextPrimary"),
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
                    Foreground = (Brush)FindResource("MpActionText"),
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
        RefreshFooter();  // see the note in RenderTabOrderList
        TxRepoList.Children.Clear();

        if (_extraTxRepos.Count == 0)
        {
            TxRepoList.Children.Add(new TextBlock
            {
                Text = Strings.Get("DlgLauncherSettingsTxNoneYet"),
                Foreground = (Brush)FindResource("MpTextMuted"),
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
                BorderBrush = (Brush)FindResource("MpRimSoft"),
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
                Foreground = (Brush)FindResource("MpTextPrimary"),
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
                ShowSection(SectionMods);
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
            ShowSection(SectionGeneral);
            return;
        }

        // 2. The catalog repo and the background trio are the DEFERRED settings: they
        //    are the two that can refuse, and they are written only here, after both
        //    checks above have passed.
        _config.ModsCatalogRepo = newCatalogRepo;
        // A single "Run in background" toggle drives the three background flags
        // together: auto-start with Windows, keep the tray icon resident, and
        // auto-start opens straight to the tray.
        _config.StartWithWindows = wantBackground;
        _config.MinimizeToTray = wantBackground;
        _config.StartMinimized = wantBackground;

        // 3. Everything else has already been applied as it was touched; this call
        //    writes whatever the user changed in the same gesture as pressing Save
        //    and persists the two above with it.
        ApplyInstantSettings();
        Close();
    }

    /// <summary>
    /// Writes every setting that has NO validation and no side effect that can fail,
    /// performs their live side effects, and persists.
    ///
    /// <para><b>This is what makes the footer's "changes apply instantly" true.</b> It runs
    /// from <see cref="RefreshFooter"/> the moment one of those settings changes, and again
    /// from Save so a deferred change lands together with them. The three that do NOT come
    /// through here are the catalog repo and the background trio: the first is validated
    /// against <c>RepoRegex</c> and the second writes a Run key that a managed PC or an AV
    /// can refuse, and a setting that can be REJECTED cannot honestly claim to apply the
    /// instant you touch it. Those two keep Save, which is exactly what SPEC-1 describes.</para>
    /// </summary>
    private void ApplyInstantSettings()
    {
        // Re-entrancy guard. Strings.SetLanguage below re-runs ApplyLanguage, which
        // rebuilds the text-size combo, which raises SelectionChanged, which lands
        // back here. Once is correct; twice is a loop.
        if (_applyingInstant) return;
        _applyingInstant = true;
        try
        {
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
        _config.AutoUpdateMods = AutoUpdateCheck.IsChecked == true;
        _config.DeltaDownloadsOnly = DeltaOnlyCheck.IsChecked == true;
        _config.UpdateChannel = ChannelBetaRadio.IsChecked == true ? "beta" : "stable";
        _config.DownloadLimitKbps = SelectedDownloadLimit();
        _config.VerifyDownloadSignatures = VerifyDownloadsCheck.IsChecked == true;
        _config.ShowMyElo = ShowEloCheck.IsChecked == true;
        _config.ReplayUploadPolicy = ReplayAlwaysRadio.IsChecked == true ? "always"
            : ReplayNeverRadio.IsChecked == true ? "never" : "ask";
        _config.MultiplayerTelemetryEnabled = TelemetryCheck.IsChecked == true;
        _config.ShareDeckStats = ShareDecksCheck.IsChecked == true;
        _config.ExtraTranslationsFolderRepos = _extraTxRepos.ToArray();
        _config.CommunityTranslationsDisabled = TxDisabledCheck.IsChecked == true;
        // Close-to-tray is INDEPENDENT of the master toggle: it governs only the
        // X / close-button behaviour (default on; unchecking restores "X = quit").
        _config.CloseToTray = MinimizeToTrayCheck.IsChecked == true;
        // Remembered across the assignment: registering the URL scheme writes registry
        // keys, and this method now runs on EVERY change to any setting on the page.
        // Doing that write because somebody moved the text-size combo would be pure churn.
        bool joinLinksChanged = _config.EnableJoinLinks != (EnableJoinLinksCheck.IsChecked == true);
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
        if (joinLinksChanged)
        {
            if (_config.EnableJoinLinks) Services.DeepLinkService.EnsureRegistered();
            else Services.DeepLinkService.EnsureUnregistered();
        }
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
            // The in-memory config is correct and the next write will flush; a failed
            // save must not stop the dialog from working.
        }

        // The caller refreshes on Closed when this is set, so it is set here rather
        // than in Save: with instant apply there IS something to refresh even when the
        // user never pressed a button.
        ChangesSaved = true;
        }
        finally
        {
            _applyingInstant = false;
        }

        // The dialog does not subscribe to LanguageChanged, so re-pull its own strings:
        // applying a language everywhere EXCEPT the window you chose it in is worse than
        // not applying it at all. ApplyLanguage is written to be re-runnable and
        // BuildTextScaleItems preserves the current selection.
        if (_lastAppliedLanguage != Strings.Language)
        {
            _lastAppliedLanguage = Strings.Language;
            ApplyLanguage();
            RenderTabOrderList();
            RefreshFooter();
        }
    }

    /// <summary>Guards <see cref="ApplyInstantSettings"/> against re-entering itself.</summary>
    private bool _applyingInstant;

    /// <summary>
    /// The language the dialog's own text was last drawn in, so a live change re-localises
    /// it exactly once.
    /// </summary>
    private string _lastAppliedLanguage = Strings.Language;

    /// <summary>
    /// The settings that still need Save, by their <see cref="SettingsFingerprint"/> key.
    /// Everything NOT in here is applied the moment it is touched.
    ///
    /// <para>These three are here because they can be REFUSED: <c>catalogRepo</c> is
    /// validated against <c>RepoRegex</c>, and <c>startWithWindows</c> writes a Run key
    /// that policy or an AV can block. A setting that can fail cannot apply instantly, and
    /// pretending otherwise would leave the config saying "on" while the control that reads
    /// the registry comes back off.</para>
    /// </summary>
    private static readonly string[] DeferredSettingKeys =
        { "startWithWindows", "catalog", "catalogRepo" };

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
