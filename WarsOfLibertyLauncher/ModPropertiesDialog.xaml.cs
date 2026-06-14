using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Per-mod Properties dialog (Steam-style). The single destination
/// for all per-mod actions — the old SETTINGS popup (gear button's
/// flat menu) was folded into this dialog's tabs:
///
///   GENERAL     — read-only metadata + Check for updates
///   LOCAL FILES — install path display + Open/Change paths +
///                 Verify / Repair + View logs + DANGER ZONE
///                 with Uninstall
///   USER DATA   — Open folder / Create backup / Restore backup
///   LANGUAGE    — translation pack picker
///
/// The buttons delegate to the same services MainWindow's existing
/// handlers use — the dialog is pure UI glue with no install /
/// uninstall / backup logic of its own; everything comes in via
/// the callback delegates the constructor receives.
/// </summary>
public partial class ModPropertiesDialog : Window
{
    private readonly ModProfile _profile;
    private readonly UpdateService _service;
    private readonly LauncherConfig _config;
    // Mutable: the "Buscar nuevas traducciones" button reassigns it after a re-fetch.
    private TranslationIndex? _translationIndex;
    // Re-fetches the translation index from GitHub and returns the fresh one.
    private readonly Func<Task<TranslationIndex?>>? _refreshTranslations;

    // Existing callbacks (4) carried over from the original dialog.
    private readonly Action<TranslationIndexEntry> _applyTranslation;
    private readonly Action _revertToEnglish;
    private readonly Action _openVerify;
    private readonly Action _openRepair;

    // New callbacks (8) folded in from the SETTINGS popup. Each one
    // wraps a RaiseMenuClick on the legacy ActionPanelControl menu
    // item so all the original handlers + dialogs keep owning the
    // actual logic.
    private readonly Func<Task<UpdateService.CheckResult?>> _checkForUpdates;
    private readonly Action _openAoE3Folder;
    private readonly Action _changeModFolder;
    private readonly Action _changeAoE3Folder;
    private readonly Action _openUserDataFolder;
    private readonly Action _createBackup;
    private readonly Action _restoreBackup;
    private readonly Action _viewLogs;
    private readonly Action _uninstall;

    /// <summary>Invoked after the user pins/unpins "stay on this version" so the
    /// main window can re-apply its cached check result (refresh PLAY/UPDATE +
    /// status) with no network call. Null = nothing to refresh.</summary>
    private readonly Action? _onUpdatePolicyChanged;

    // Fase 1 — version picker (GitHubReleases mods only). Null for other
    // mechanisms, which hides the whole "Version" section.
    private readonly Func<Task<IReadOnlyList<GitHubReleaseDownloader.ReleaseInfo>>>? _listVersions;
    private readonly Func<string, Task>? _installVersion;

    /// <summary>True while the mod is installing/updating/repairing — locks the
    /// whole language list (you must not swap data files mid-install). Driven by
    /// MainWindow.SetBusy via <see cref="SetModBusy"/>.</summary>
    private bool _modBusy;

    public ModPropertiesDialog(
        ModProfile profile,
        UpdateService service,
        LauncherConfig config,
        TranslationIndex? translationIndex,
        Action<TranslationIndexEntry> applyTranslation,
        Action revertToEnglish,
        Action openVerify,
        Action openRepair,
        Func<Task<UpdateService.CheckResult?>> checkForUpdates,
        Action openAoE3Folder,
        Action changeModFolder,
        Action changeAoE3Folder,
        Action openUserDataFolder,
        Action createBackup,
        Action restoreBackup,
        Action viewLogs,
        Action uninstall,
        Func<Task<TranslationIndex?>>? refreshTranslations = null,
        Action? onUpdatePolicyChanged = null,
        Func<Task<IReadOnlyList<GitHubReleaseDownloader.ReleaseInfo>>>? listVersions = null,
        Func<string, Task>? installVersion = null)
    {
        _profile = profile;
        _service = service;
        _config = config;
        _translationIndex = translationIndex;
        _applyTranslation = applyTranslation;
        _revertToEnglish = revertToEnglish;
        _openVerify = openVerify;
        _openRepair = openRepair;
        _checkForUpdates = checkForUpdates;
        _openAoE3Folder = openAoE3Folder;
        _changeModFolder = changeModFolder;
        _changeAoE3Folder = changeAoE3Folder;
        _openUserDataFolder = openUserDataFolder;
        _createBackup = createBackup;
        _restoreBackup = restoreBackup;
        _viewLogs = viewLogs;
        _uninstall = uninstall;
        _refreshTranslations = refreshTranslations;
        _onUpdatePolicyChanged = onUpdatePolicyChanged;
        _listVersions = listVersions;
        _installVersion = installVersion;

        InitializeComponent();
        ApplyStrings();
        LoadGeneral();
        LoadLocalFiles();
        LoadUserData();
        LoadLanguage();
        LoadVersions();
        SetActiveTab(TabGeneralBtn);

        // Window-size scaling (Controls/UiScale.cs): the content area (Row 1,
        // below the fixed header) shrinks to fit smaller dialogs. sizeSource is
        // the Window (window-sized → no feedback); the header stays at base
        // scale. ref ≈ the default footprint, so the default dialog is 1.0.
        UiScale.Attach(PropsContentRoot, this, 860, 540);
    }

    private void ApplyStrings()
    {
        Title = Strings.Format("ModPropTitle", _profile.DisplayName ?? "");
        TitleBarControl.Title = _profile.DisplayName ?? "";
        // (The neutral subtitle that used to sit under HeaderTitleText
        // was removed when the header was compacted to a single row —
        // the sidebar tabs already communicate "this is where you
        // manage settings/files/data/language", so the subtitle was
        // pure vertical filler.)

        TabGeneralLabel.Text = Strings.Get("ModPropTabGeneral");
        TabLocalFilesLabel.Text = Strings.Get("ModPropTabLocalFiles");
        TabUserDataLabel.Text = Strings.Get("ModPropTabUserData");
        TabLanguageLabel.Text = Strings.Get("ModPropTabLanguage");

        // GENERAL tab
        LblAboutSection.Text = Strings.Get("ModPropAboutSection");
        LblName.Text = Strings.Get("ModPropName");
        LblAuthor.Text = Strings.Get("ModPropAuthor");
        LblVersion.Text = Strings.Get("ModPropVersion");
        LblWebsite.Text = Strings.Get("ModPropWebsite");
        CheckUpdatesBtn.Content = Strings.Get("ModPropCheckUpdates");
        StayOnVersionHint.Text = Strings.Get("ModPropStayOnVersionHint");
        VersionSectionLabel.Text = Strings.Get("ModPropVersionSection");
        VersionSectionHint.Text = Strings.Get("ModPropVersionHint");
        InstallVersionBtn.Content = Strings.Get("ModPropVersionInstallBtn");

        // LOCAL FILES tab
        LblInstallPath.Text = Strings.Get("ModPropInstallSection");
        LblTempSection.Text = Strings.Get("ModPropTempSection");
        LblTempDesc.Text = Strings.Get("ModPropTempDesc");
        ClearTempBtn.Content = Strings.Get("ModPropClearTemp");
        LblPathsSection.Text = Strings.Get("ModPropPathsSection");
        OpenFolderBtn.Content = Strings.Get("ModPropOpenFolder");
        OpenAoE3FolderBtn.Content = Strings.Get("ModPropOpenAoE3Folder");
        ChangeModFolderBtn.Content = Strings.Get("ModPropChangeModFolder");
        ChangeAoE3FolderBtn.Content = Strings.Get("ModPropChangeAoE3Folder");
        LblMaintenanceSection.Text = Strings.Get("ModPropMaintenanceSection");
        VerifyBtn.Content = Strings.Get("ModContextVerify");
        RepairBtn.Content = Strings.Get("ModContextRepair");
        LblDiagnosticsSection.Text = Strings.Get("ModPropDiagnostics");
        ViewLogsBtn.Content = Strings.Get("ModPropViewLogs");
        LblDangerZone.Text = Strings.Get("ModPropDangerZone");
        LblDangerZoneDesc.Text = Strings.Get("ModPropDangerZoneDesc");
        UninstallBtn.Content = Strings.Get("ModPropUninstall");

        // USER DATA tab — action-card layout: each card has a long
        // descriptive title + short description, and a SHORT button
        // label ("Open" / "Backup" / "Restore") because the long
        // text already tells the user what the action does.
        LblOpenUserDataTitle.Text = Strings.Get("ModPropOpenUserDataFolder");
        LblOpenUserDataDesc.Text = Strings.Get("ModPropOpenUserDataDesc");
        OpenUserDataFolderBtn.Content = Strings.Get("ModPropOpenBtn");
        LblCreateBackupTitle.Text = Strings.Get("ModPropCreateBackup");
        LblCreateBackupDesc.Text = Strings.Get("ModPropCreateBackupDesc");
        CreateBackupBtn.Content = Strings.Get("ModPropBackupBtn");
        LblRestoreBackupTitle.Text = Strings.Get("ModPropRestoreBackup");
        LblRestoreBackupDesc.Text = Strings.Get("ModPropRestoreBackupDesc");
        RestoreBackupBtn.Content = Strings.Get("ModPropRestoreBtn");

        // LANGUAGE tab
        LblLanguageSectionTitle.Text = Strings.Get("ModPropLanguageSectionTitle");
        LblLanguageDesc.Text = Strings.Get("ModPropLanguageDesc");
        RefreshTranslationsBtn.Content = Strings.Get("DlgLangRefreshButton");
        LblLanguageCurrent.Text = Strings.Get("ModPropLanguageCurrent");
        LanguageBusyHintText.Text = Strings.Get("LanguageBusyHint");
        LanguageEmptyHint.Text = Strings.Get("ModPropNoTranslations");

        // The header close ✕ is now the shared controls:TitleBar's own
        // button (localized tooltip handled inside TitleBar).
    }

    private void LoadGeneral()
    {
        // Header mod/game icon (cached catalog icon.png or built-in packed
        // icon). Collapsed when the mod ships no icon — the title alone reads
        // fine then.
        // The shared title bar shows the icon (collapses it when null).
        TitleBarControl.TitleIcon = LoadIconBrush(_profile)?.ImageSource;

        ValName.Text = _profile.DisplayName ?? "";
        ValAuthor.Text = string.IsNullOrWhiteSpace(_profile.Author) ? "—" : _profile.Author;
        var ver = _service.CurrentVersion?.Ver;
        bool installed = !string.IsNullOrWhiteSpace(ver);
        ValVersion.Text = installed ? ver : Strings.Get("ModPropNotInstalled");
        ValWebsite.Text = string.IsNullOrWhiteSpace(_profile.OfficialWebsite) ? "—" : _profile.OfficialWebsite;

        // Mirror the version into the header's pill badge so the
        // user sees "v1.2.0c2" at the top regardless of which tab
        // they're on. When the mod isn't installed the badge
        // collapses so the header stays clean instead of showing
        // an empty pill.
        if (installed)
        {
            HeaderVersionText.Text = "v" + ver;
            HeaderVersionBadge.Visibility = Visibility.Visible;
        }
        else
        {
            HeaderVersionText.Text = string.Empty;
            HeaderVersionBadge.Visibility = Visibility.Collapsed;
        }

        // Stay-on-version pin (Fase 0): only meaningful once we know the installed
        // version. Checked only when the pin matches the version we actually have.
        if (installed)
        {
            var pinned = _config.GetState(_profile.Id).PinnedVersion;
            StayOnVersionCheck.Content = Strings.Format("ModPropStayOnVersion", ver);
            StayOnVersionCheck.IsChecked =
                !string.IsNullOrEmpty(pinned)
                && string.Equals(pinned, ver, StringComparison.OrdinalIgnoreCase);
            StayOnVersionCheck.Visibility = Visibility.Visible;
            StayOnVersionHint.Visibility = Visibility.Visible;
        }
        else
        {
            StayOnVersionCheck.Visibility = Visibility.Collapsed;
            StayOnVersionHint.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Pin / unpin "stay on this version". Checking it records the installed
    /// version in <see cref="ModState.PinnedVersion"/> (pausing update prompts for
    /// this mod); unchecking clears it (resume updates). Nothing is ever
    /// auto-updated — this only controls whether the prompt is shown. The main
    /// window is refreshed via the callback so PLAY/UPDATE updates instantly.
    /// </summary>
    private void StayOnVersionCheck_Click(object sender, RoutedEventArgs e)
    {
        var state = _config.GetState(_profile.Id);
        var ver = _service.CurrentVersion?.Ver;
        state.PinnedVersion =
            (StayOnVersionCheck.IsChecked == true && !string.IsNullOrWhiteSpace(ver))
                ? ver!
                : "";
        _config.Save();
        _onUpdatePolicyChanged?.Invoke();
    }

    /// <summary>
    /// Loads the profile's icon as an ImageBrush — the cached catalog icon
    /// (icon.png) if it's on disk, else the built-in packed icon (a
    /// <c>pack://</c> URI). Returns null when neither is available, so the
    /// caller hides the header icon host.
    /// </summary>
    private static System.Windows.Media.ImageBrush? LoadIconBrush(ModProfile profile)
    {
        string? uri =
            (!string.IsNullOrEmpty(profile.LocalIconPath) && System.IO.File.Exists(profile.LocalIconPath))
                ? profile.LocalIconPath
                : (!string.IsNullOrEmpty(profile.BannerImage) ? profile.BannerImage : null);
        if (string.IsNullOrEmpty(uri)) return null;
        try
        {
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.UriSource = new System.Uri(uri, System.UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            var br = new System.Windows.Media.ImageBrush(bmp)
            {
                Stretch = System.Windows.Media.Stretch.UniformToFill,
            };
            br.Freeze();
            return br;
        }
        catch
        {
            return null;
        }
    }

    private void LoadLocalFiles()
    {
        var path = _service.InstallPath;
        bool installed = !string.IsNullOrEmpty(path) && Directory.Exists(path);
        ValInstallPath.Text = installed ? path : Strings.Get("ModPropNotInstalled");

        // Per-button enablement: paths-related buttons need an
        // install on disk; maintenance buttons need the mod
        // installed; Open AoE3 + Change AoE3 are gated by AoE3
        // detection which we delegate to the legacy menu items
        // (their IsEnabled reflects the current detection state).
        OpenFolderBtn.IsEnabled = installed;
        OpenAoE3FolderBtn.IsEnabled = true;   // Always tries; warns if not found.
        ChangeModFolderBtn.IsEnabled = true;
        ChangeAoE3FolderBtn.IsEnabled = true;
        VerifyBtn.IsEnabled = installed;
        RepairBtn.IsEnabled = installed;
        ViewLogsBtn.IsEnabled = true;          // Logs are always available.
        UninstallBtn.IsEnabled = installed;

        // Stock Age of Empires III is detect-only: the launcher never
        // installed it, so there's no payload to verify/repair, and the
        // "install path" IS the user's real AoE3 folder — uninstalling it
        // (a blanket recursive delete) would wipe their base game. Hide the
        // Maintenance and Danger Zone sections outright for it.
        if (_profile.IsStockGame)
        {
            LblMaintenanceSection.Visibility = Visibility.Collapsed;
            VerifyBtn.Visibility = Visibility.Collapsed;
            RepairBtn.Visibility = Visibility.Collapsed;
            VerifyBtn.IsEnabled = false;
            RepairBtn.IsEnabled = false;

            LblDangerZone.Visibility = Visibility.Collapsed;
            LblDangerZoneDesc.Visibility = Visibility.Collapsed;
            UninstallBtn.Visibility = Visibility.Collapsed;
            UninstallBtn.IsEnabled = false;
        }
    }

    private void LoadUserData()
    {
        // Buttons enabled only when the mod is installed (no install
        // path → nothing to back up). The underlying handlers will
        // also surface their own "nothing to back up" message if
        // the call goes through for some edge case.
        var installed = !string.IsNullOrEmpty(_service.InstallPath)
                        && Directory.Exists(_service.InstallPath);
        OpenUserDataFolderBtn.IsEnabled = installed;
        CreateBackupBtn.IsEnabled = installed;
        RestoreBackupBtn.IsEnabled = installed;
    }

    private void LoadLanguage()
    {
        LanguageCardList.Children.Clear();

        // While the mod is installing/updating, lock the whole list: show the
        // banner and disable the Refresh button (cards render disabled below).
        LanguageBusyHint.Visibility = _modBusy ? Visibility.Visible : Visibility.Collapsed;
        RefreshTranslationsBtn.IsEnabled = !_modBusy;

        var activeId = _config.GetActiveState().ActiveTranslationId ?? "";
        var modVersion = _service.CurrentVersion?.Ver;

        // English (default) — always available.
        LanguageCardList.Children.Add(BuildLanguageCard(
            "🌐", Strings.Get("MenuLangEnglish"), "", null, "",
            isActive: string.IsNullOrEmpty(activeId), blocked: false, compatible: false,
            onUse: () => _revertToEnglish?.Invoke()));

        var entries = new Dictionary<string, TranslationIndexEntry>(StringComparer.OrdinalIgnoreCase);
        if (_translationIndex != null)
            foreach (var e in _translationIndex.Translations) entries[e.Id] = e;
        try
        {
            if (!string.IsNullOrEmpty(_service.InstallPath))
            {
                var installed = new TranslationService(
                    _service.InstallPath, _service.Profile.Translations?.CoveredFiles).ListInstalled();
                foreach (var m in installed)
                    if (!entries.ContainsKey(m.Id))
                        entries[m.Id] = new TranslationIndexEntry
                        {
                            Id = m.Id, Name = m.Name, Author = m.Author,
                            Version = m.Version, CompatibleWith = m.CompatibleWith,
                        };
            }
        }
        catch { /* probe failure is non-fatal */ }

        // Active first → compatible-with-installed-version → newest → name
        // (shared with the gear menu via TranslationCompat.OrderForDisplay).
        var ordered = TranslationCompat.OrderForDisplay(
            entries.Values, _translationIndex?.Translations, modVersion, activeId);
        foreach (var entry in ordered)
        {
            bool isActive = string.Equals(entry.Id, activeId, StringComparison.OrdinalIgnoreCase);
            // Block on version grounds only when NOT the active pack (the active
            // one demonstrably works); the apply dialog's hash check is the final
            // word, so this is a pre-filter, not the sole authority.
            bool blocked = !isActive
                && TranslationCompat.IsVersionBlocked(entry.CompatibleWith, modVersion);
            // Positive counterpart: the translator declared THIS installed version,
            // so affirm it (green ✓). "unknown" (empty declared list) gets neither.
            bool compatible = !isActive
                && TranslationCompat.IsCompatible(entry.CompatibleWith, modVersion);
            var captured = entry;
            LanguageCardList.Children.Add(BuildLanguageCard(
                LanguageFlag(entry.Id), entry.Name, entry.Author, entry.CompatibleWith, entry.Version,
                isActive, blocked, compatible, () => _applyTranslation?.Invoke(captured)));
        }

        bool hasPacks = entries.Count > 0;
        LanguageEmptyHint.Visibility = hasPacks ? Visibility.Collapsed : Visibility.Visible;
    }

    private FrameworkElement BuildLanguageCard(string flag, string name, string author,
        IReadOnlyList<string>? compatibleWith, string packVersion,
        bool isActive, bool blocked, bool compatible, Action onUse)
    {
        var col = new StackPanel();
        var title = new TextBlock
        {
            Text = $"{flag}  {name}" + (string.IsNullOrWhiteSpace(author) ? "" : $"    ·  {author}"),
            FontSize = 15, FontWeight = FontWeights.SemiBold,
            Foreground = Res("TextPrimary", "#E6EEF8"), TextWrapping = TextWrapping.Wrap,
        };
        col.Children.Add(title);

        var subParts = new List<string>();
        if (compatibleWith != null && compatibleWith.Count > 0)
            subParts.Add(Strings.Format("LangCardForMod", string.Join(", ", compatibleWith)));
        if (!string.IsNullOrWhiteSpace(packVersion))
            subParts.Add(Strings.Format("LangCardPackVer", packVersion));
        if (subParts.Count > 0)
            col.Children.Add(new TextBlock
            {
                Text = string.Join("       ", subParts), FontSize = 12,
                Foreground = Res("TextSecondary", "#9AA6B2"),
                Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap,
            });
        if (blocked)
            col.Children.Add(new TextBlock
            {
                Text = Strings.Get("LangCardBlockedHint"), FontSize = 12,
                Foreground = Res("ErrorBrush", "#E0708A"),
                Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap,
            });
        else if (compatible)
            col.Children.Add(new TextBlock
            {
                Text = "✓ " + Strings.Get("LangCardCompatibleHint"), FontSize = 12,
                Foreground = Res("SuccessBrush", "#9BD99B"),
                Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap,
            });

        // Status indicator (not a button anymore — the WHOLE card is the click
        // target, which is more forgiving than a small "Use" button). A version-
        // mismatched pack reads "Use anyway" in amber — a warning the user can
        // override, NOT a block (the apply dialog confirms first). While the mod
        // is installing/updating (_modBusy) every card reads "🔒 Unavailable".
        var status = new TextBlock
        {
            Text = _modBusy ? "🔒 " + Strings.Get("LangCardUnavailableBusy")
                : isActive ? Strings.Get("LangCardActive")
                : blocked ? Strings.Get("LangCardUseAnyway") : Strings.Get("LangCardUse"),
            FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = _modBusy ? Res("TextSecondary", "#9AA6B2")
                : isActive ? Res("SuccessBrush", "#9BD99B")
                : blocked ? Res("WarnBrush", "#E0B341") : Res("AccentBrush", "#C8A24A"),
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(10, 0, 0, 0),
        };

        var grid = new Grid { Margin = new Thickness(12, 10, 12, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(col, 0);
        Grid.SetColumn(status, 1);
        grid.Children.Add(col);
        grid.Children.Add(status);

        // A version-mismatched ("blocked") pack stays clickable: the user can apply
        // it under their own responsibility and the apply dialog confirms first.
        // The already-active pack is non-clickable (nothing to do); and while the
        // mod is installing/updating (_modBusy) the WHOLE list is locked.
        bool clickable = !isActive && !_modBusy;
        var border = new Border
        {
            Background = Res("BgPanel", "#1B2430"),
            BorderBrush = isActive ? Res("AccentBrush", "#C8A24A") : Res("BorderSubtle", "#2C313A"),
            BorderThickness = new Thickness(isActive ? 2 : 1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, 0, 8),
            // Busy = clearly disabled (0.5); a version-mismatch caution = slightly
            // dimmed (0.85, not inert); otherwise full.
            Opacity = _modBusy ? 0.5 : (blocked ? 0.85 : 1.0),
            Child = grid,
        };
        if (!clickable) return border;

        // Wrap the whole card in a CHROMELESS Button. Button.Click fires reliably
        // (MouseLeftButtonUp on a Border can be swallowed by the surrounding
        // ScrollViewer), and we get keyboard/focus behaviour for free. The custom
        // template strips the default button chrome so it still looks like a card.
        var template = new ControlTemplate(typeof(Button))
        {
            VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)),
        };
        var button = new Button
        {
            Content = border,
            Template = template,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        button.Click += (_, _) => onUse();
        return button;
    }

    private Brush Res(string key, string fallbackHex)
    {
        if (TryFindResource(key) is Brush b) return b;
        try { return (Brush)new BrushConverter().ConvertFromString(fallbackHex)!; }
        catch { return Brushes.Gray; }
    }

    private static string LanguageFlag(string id) => id.ToLowerInvariant() switch
    {
        "es" or "es-es" or "es-mx" or "es-ar" => "🇪🇸",
        "fr" or "fr-fr" => "🇫🇷",
        "de" or "de-de" => "🇩🇪",
        "it" or "it-it" => "🇮🇹",
        "pt" or "pt-pt" => "🇵🇹",
        "pt-br" => "🇧🇷",
        "ru" or "ru-ru" => "🇷🇺",
        "zh" or "zh-cn" or "zh-tw" => "🇨🇳",
        "ja" or "ja-jp" => "🇯🇵",
        "ko" or "ko-kr" => "🇰🇷",
        "pl" or "pl-pl" => "🇵🇱",
        _ => "🌐",
    };

    // -- Tab switching ------------------------------------------------------

    private void SetActiveTab(Button activeBtn)
    {
        TabGeneralBtn.Tag = ReferenceEquals(activeBtn, TabGeneralBtn) ? "active" : null;
        TabLocalFilesBtn.Tag = ReferenceEquals(activeBtn, TabLocalFilesBtn) ? "active" : null;
        TabUserDataBtn.Tag = ReferenceEquals(activeBtn, TabUserDataBtn) ? "active" : null;
        TabLanguageBtn.Tag = ReferenceEquals(activeBtn, TabLanguageBtn) ? "active" : null;

        GeneralPanel.Visibility = ReferenceEquals(activeBtn, TabGeneralBtn) ? Visibility.Visible : Visibility.Collapsed;
        LocalFilesPanel.Visibility = ReferenceEquals(activeBtn, TabLocalFilesBtn) ? Visibility.Visible : Visibility.Collapsed;
        UserDataPanel.Visibility = ReferenceEquals(activeBtn, TabUserDataBtn) ? Visibility.Visible : Visibility.Collapsed;
        LanguagePanel.Visibility = ReferenceEquals(activeBtn, TabLanguageBtn) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TabGeneralBtn_Click(object sender, RoutedEventArgs e) => SetActiveTab(TabGeneralBtn);
    private void TabLocalFilesBtn_Click(object sender, RoutedEventArgs e) => SetActiveTab(TabLocalFilesBtn);
    private void TabUserDataBtn_Click(object sender, RoutedEventArgs e) => SetActiveTab(TabUserDataBtn);
    private void TabLanguageBtn_Click(object sender, RoutedEventArgs e) => SetActiveTab(TabLanguageBtn);

    // -- Action handlers ----------------------------------------------------
    //
    // Only handlers whose flow lands on the MAIN WINDOW close this dialog:
    // Verify / Repair (their progress runs on the main-window progress strip,
    // which a non-modal Properties window would otherwise cover) and Uninstall
    // (the mod is gone afterwards, so the open view would be stale). Everything
    // else STAYS OPEN: the path pickers and the backup/restore dialogs are
    // modals that appear on top with nothing to uncover, so closing only
    // disoriented the user — instead those handlers refresh the displayed
    // paths/state in place via RefreshData() when the modal returns. Handlers
    // that just open Explorer/Notepad (Open folder, Open AoE3 folder, Open
    // user-data folder, View logs), the website/language handlers, and "Check
    // for updates" never closed (nothing to land on); check-for-updates shows
    // its result inline.
    //
    // None of these set DialogResult: the dialog is shown non-modally
    // via Show() from MainWindow, and setting DialogResult outside of
    // ShowDialog() throws InvalidOperationException. The caller never
    // read DialogResult here anyway — the post-close refresh
    // (RefreshIdlePanel + RefreshActiveModBanner) runs on the Closed
    // event regardless of how the dialog was dismissed.

    /// <summary>
    /// Re-reads config and repaints the data-bearing labels (General /
    /// Local Files / User Data) without disturbing the active tab or the
    /// language combo. Called by the stay-open action handlers after their
    /// modal returns, and by MainWindow once an async folder re-detection
    /// completes, so an open Properties window reflects the new paths /
    /// version / user-data state in place.
    /// </summary>
    public void RefreshData()
    {
        LoadGeneral();
        LoadLocalFiles();
        LoadUserData();
    }

    /// <summary>
    /// Rebuilds the language cards so the active/compatible state reflects the
    /// latest config (e.g. right after a translation is applied or reverted).
    /// Called by MainWindow once the apply/revert flow finishes.
    /// </summary>
    public void RefreshLanguageTab() => LoadLanguage();

    /// <summary>
    /// Lock / unlock the language list while the mod is installing or updating.
    /// Called from MainWindow.SetBusy (real ops only, not the read-only check).
    /// Rebuilds the cards so they render disabled + show the busy banner.
    /// </summary>
    public void SetModBusy(bool busy)
    {
        if (_modBusy == busy) return;
        _modBusy = busy;
        LoadLanguage();
    }

    private async void RefreshTranslationsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_refreshTranslations == null) return;
        var original = RefreshTranslationsBtn.Content;
        RefreshTranslationsBtn.IsEnabled = false;
        RefreshTranslationsBtn.Content = Strings.Get("DlgLangRefreshing");
        try
        {
            var idx = await _refreshTranslations();
            if (idx != null) _translationIndex = idx;
            LoadLanguage();   // rebuild the cards with the freshly fetched index
        }
        catch { /* re-fetch failure is non-fatal; cards keep the old index */ }
        finally
        {
            RefreshTranslationsBtn.Content = original;
            RefreshTranslationsBtn.IsEnabled = true;
        }
    }

    private async void ClearTempBtn_Click(object sender, RoutedEventArgs e)
    {
        var original = ClearTempBtn.Content;
        ClearTempBtn.IsEnabled = false;
        ClearTempBtn.Content = Strings.Get("DlgTempClearing");
        ClearTempResult.Visibility = Visibility.Collapsed;

        bool ok = false;
        try
        {
            await System.Threading.Tasks.Task.Run(NativeInstallService.TryCleanupTemp);
            ok = true;
            ClearTempResult.Text = Strings.Get("DlgTempCleared");
        }
        catch
        {
            ClearTempResult.Text = Strings.Get("DlgTempClearFailed");
        }
        finally
        {
            ClearTempResult.Visibility = Visibility.Visible;
            ClearTempBtn.Content = original;
            ClearTempBtn.IsEnabled = true;
        }

        // A clear popup so the user actually sees that something happened.
        MessageBox.Show(this,
            Strings.Get(ok ? "DlgTempCleared" : "DlgTempClearFailed"),
            Strings.Get("DlgTempClearedTitle"),
            MessageBoxButton.OK,
            ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void ValWebsite_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var url = _profile.OfficialWebsite;
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"ModPropertiesDialog.ValWebsite: open failed: {ex.Message}");
        }
    }

    /// <summary>
    /// "Check for updates" runs in-place: it does NOT close the dialog
    /// (the check has no separate window to land on — the result is just
    /// a yes/no), so closing left the user staring at the main window
    /// with no idea whether anything happened. Instead we disable the
    /// button, show a "checking…" line, run the real check on the main
    /// window (which also refreshes its PLAY/UPDATE button + cache), and
    /// render the outcome right here.
    /// </summary>
    private async void CheckUpdatesBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_checkForUpdates == null) return;

        CheckUpdatesBtn.IsEnabled = false;
        SetCheckResult(Strings.Get("ModPropChecking"), "TextSecondary");
        try
        {
            var result = await _checkForUpdates();

            // Refresh the version labels in case the check discovered a
            // newly-detected install / version.
            LoadGeneral();

            if (result == null || !result.IsValidInstall)
            {
                SetCheckResult(Strings.Get("ModPropCheckNotInstalled"), "TextSecondary");
            }
            else if (result.PendingDownloads.Count > 0)
            {
                SetCheckResult(Strings.Get("ModPropUpdateAvailable"), "AccentBrush");
            }
            else
            {
                SetCheckResult(Strings.Get("ModPropUpToDate"), "SuccessBrush");
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"ModPropertiesDialog.CheckUpdates failed: {ex.Message}");
            SetCheckResult(Strings.Get("ModPropCheckFailed"), "ErrorBrush");
        }
        finally
        {
            CheckUpdatesBtn.IsEnabled = true;
        }
    }

    /// <summary>
    /// Paints the inline check-for-updates result line with one of the
    /// theme brushes (resolved by key, with a graceful fallback so a
    /// missing brush can't crash the handler).
    /// </summary>
    private void SetCheckResult(string text, string brushKey)
    {
        CheckUpdatesResult.Text = text;
        CheckUpdatesResult.Foreground =
            TryFindResource(brushKey) as System.Windows.Media.Brush
            ?? System.Windows.Media.Brushes.White;
        CheckUpdatesResult.Visibility = Visibility.Visible;
    }

    // ------------------------------------------------------------------------
    // Version picker (Fase 1) — GitHubReleases mods only.
    // ------------------------------------------------------------------------

    /// <summary>
    /// Populates the "Version" section for GitHubReleases mods: fetches the
    /// repo's published releases, lists them newest-first (annotating the
    /// installed / recommended / pre-release ones) and pre-selects the installed
    /// version. Stays collapsed for any other mechanism (callbacks null). Network
    /// failures show an inline hint instead of throwing.
    /// </summary>
    private async void LoadVersions()
    {
        // Gate the whole section to: callbacks wired AND a GitHubReleases mod AND
        // actually installed. Version SWITCH re-overlays onto an existing install
        // (RepairInstallAsync needs the install path); a fresh first install picks
        // the recommended tag through the normal Install flow, not here.
        bool isInstalled = !string.IsNullOrWhiteSpace(_service.CurrentVersion?.Ver);
        // External-hosted payloads pin a SHA-256 for the approved tag ONLY, so no
        // other version can be verified — hide the picker rather than list
        // versions that would fail to install.
        bool externalHosted =
            !string.IsNullOrWhiteSpace(_profile.GitHubReleases?.ExternalAssetUrlTemplate);
        if (_listVersions == null || _installVersion == null
            || _profile.UpdateMechanism != ModUpdateMechanism.GitHubReleases
            || externalHosted
            || !isInstalled)
        {
            VersionSection.Visibility = Visibility.Collapsed;
            return;
        }

        VersionSection.Visibility = Visibility.Visible;
        VersionCombo.IsEnabled = false;
        InstallVersionBtn.IsEnabled = false;
        SetVersionStatus(Strings.Get("ModPropVersionsLoading"), "TextSecondary");

        IReadOnlyList<GitHubReleaseDownloader.ReleaseInfo> releases;
        try
        {
            releases = await _listVersions();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"ModProperties: load versions failed: {ex.Message}");
            SetVersionStatus(Strings.Get("ModPropVersionsFailed"), "ErrorBrush");
            return;
        }

        if (releases.Count == 0)
        {
            SetVersionStatus(Strings.Get("ModPropVersionsNone"), "TextSecondary");
            return;
        }

        var recommended = _profile.GitHubReleases?.ApprovedReleaseTag ?? "";
        var installed = _service.CurrentVersion?.Ver ?? "";

        VersionCombo.Items.Clear();
        int selectIdx = -1, recommendedIdx = -1;
        for (int i = 0; i < releases.Count; i++)
        {
            var r = releases[i];
            var tags = new List<string>();
            if (!string.IsNullOrEmpty(installed)
                && string.Equals(r.Tag, installed, StringComparison.OrdinalIgnoreCase))
            {
                tags.Add(Strings.Get("ModPropVersionInstalled"));
                selectIdx = i;
            }
            if (!string.IsNullOrEmpty(recommended)
                && string.Equals(r.Tag, recommended, StringComparison.OrdinalIgnoreCase))
            {
                tags.Add(Strings.Get("ModPropVersionRecommended"));
                recommendedIdx = i;
            }
            if (r.Prerelease) tags.Add(Strings.Get("ModPropVersionPrerelease"));

            var label = tags.Count > 0 ? $"{r.Tag}  —  {string.Join(", ", tags)}" : r.Tag;
            VersionCombo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = label, Tag = r.Tag });
        }

        VersionCombo.SelectedIndex = selectIdx >= 0 ? selectIdx : (recommendedIdx >= 0 ? recommendedIdx : 0);
        VersionCombo.IsEnabled = true;
        InstallVersionBtn.IsEnabled = true;
        VersionStatus.Visibility = Visibility.Collapsed;
    }

    private void SetVersionStatus(string text, string brushKey)
    {
        VersionStatus.Text = text;
        VersionStatus.Foreground =
            TryFindResource(brushKey) as System.Windows.Media.Brush
            ?? System.Windows.Media.Brushes.White;
        VersionStatus.Visibility = Visibility.Visible;
    }

    private void InstallVersionBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_installVersion == null) return;
        if (VersionCombo.SelectedItem is not System.Windows.Controls.ComboBoxItem item) return;
        if (item.Tag is not string tag || string.IsNullOrWhiteSpace(tag)) return;

        var installed = _service.CurrentVersion?.Ver ?? "";
        if (string.Equals(tag, installed, StringComparison.OrdinalIgnoreCase))
        {
            SetVersionStatus(Strings.Get("ModPropVersionAlready"), "TextSecondary");
            return;
        }

        // Install runs on the MAIN window's progress strip (like Verify / Repair),
        // so close this non-modal dialog first — otherwise it sits over the bar.
        var run = _installVersion;
        Close();
        _ = run(tag);
    }

    private void OpenFolderBtn_Click(object sender, RoutedEventArgs e)
    {
        var path = _service.InstallPath;
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"ModPropertiesDialog.OpenFolderBtn: open failed: {ex.Message}");
        }
    }

    private void OpenAoE3FolderBtn_Click(object sender, RoutedEventArgs e)
    {
        // Just opens Explorer — no covering window, so keep the dialog open.
        _openAoE3Folder?.Invoke();
    }

    private void ChangeModFolderBtn_Click(object sender, RoutedEventArgs e)
    {
        // Stays open: the folder picker is a modal that lands on top. The
        // new path is written to config before the callback's await, so the
        // immediate RefreshData() shows it; the re-detected version catches
        // up via MainWindow's post-CheckAsync RefreshData call.
        _changeModFolder?.Invoke();
        RefreshData();
    }

    private void ChangeAoE3FolderBtn_Click(object sender, RoutedEventArgs e)
    {
        _changeAoE3Folder?.Invoke();
        RefreshData();
    }

    private void VerifyBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
        _openVerify?.Invoke();
    }

    private void RepairBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
        _openRepair?.Invoke();
    }

    private void ViewLogsBtn_Click(object sender, RoutedEventArgs e)
    {
        // Opens the log in the external viewer — no covering window, keep open.
        _viewLogs?.Invoke();
    }

    private void UninstallBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
        _uninstall?.Invoke();
    }

    private void OpenUserDataFolderBtn_Click(object sender, RoutedEventArgs e)
    {
        // Just opens Explorer — no covering window, so keep the dialog open.
        _openUserDataFolder?.Invoke();
    }

    private void CreateBackupBtn_Click(object sender, RoutedEventArgs e)
    {
        // Stays open: the backup confirmation/MessageBox is modal and lands
        // on top. The callback is synchronous, so RefreshData() afterwards
        // sees the final user-data state.
        _createBackup?.Invoke();
        RefreshData();
    }

    private void RestoreBackupBtn_Click(object sender, RoutedEventArgs e)
    {
        _restoreBackup?.Invoke();
        RefreshData();
    }

}
