using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
    private readonly TranslationIndex? _translationIndex;

    // Existing callbacks (4) carried over from the original dialog.
    private readonly Action<TranslationIndexEntry> _applyTranslation;
    private readonly Action _revertToEnglish;
    private readonly Action _openVerify;
    private readonly Action _openRepair;

    // New callbacks (8) folded in from the SETTINGS popup. Each one
    // wraps a RaiseMenuClick on the legacy ActionPanelControl menu
    // item so all the original handlers + dialogs keep owning the
    // actual logic.
    private readonly Action _checkForUpdates;
    private readonly Action _openAoE3Folder;
    private readonly Action _changeModFolder;
    private readonly Action _changeAoE3Folder;
    private readonly Action _openUserDataFolder;
    private readonly Action _createBackup;
    private readonly Action _restoreBackup;
    private readonly Action _viewLogs;
    private readonly Action _uninstall;

    private bool _suppressLanguageChange;

    public ModPropertiesDialog(
        ModProfile profile,
        UpdateService service,
        LauncherConfig config,
        TranslationIndex? translationIndex,
        Action<TranslationIndexEntry> applyTranslation,
        Action revertToEnglish,
        Action openVerify,
        Action openRepair,
        Action checkForUpdates,
        Action openAoE3Folder,
        Action changeModFolder,
        Action changeAoE3Folder,
        Action openUserDataFolder,
        Action createBackup,
        Action restoreBackup,
        Action viewLogs,
        Action uninstall)
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

        InitializeComponent();
        ApplyStrings();
        LoadGeneral();
        LoadLocalFiles();
        LoadUserData();
        LoadLanguage();
        SetActiveTab(TabGeneralBtn);
    }

    private void ApplyStrings()
    {
        Title = Strings.Format("ModPropTitle", _profile.DisplayName ?? "");
        HeaderTitleText.Text = _profile.DisplayName ?? "";
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

        // LOCAL FILES tab
        LblInstallPath.Text = Strings.Get("ModPropInstallSection");
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
        LblLanguageCurrent.Text = Strings.Get("ModPropLanguageCurrent");
        LanguageEmptyHint.Text = Strings.Get("ModPropNoTranslations");

        // CloseBtn is now the custom ✕ glyph in the header (Steam-style
        // chrome) instead of a labelled footer button — its content is
        // a Segoe MDL2 glyph set in XAML, so we skip rebinding it from
        // Strings here. The ModPropClose string is kept in the
        // dictionary for ToolTip / future use.
        CloseBtn.ToolTip = Strings.Get("ModPropClose");
    }

    private void LoadGeneral()
    {
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
        _suppressLanguageChange = true;
        LanguageCombo.Items.Clear();

        var activeId = _config.GetActiveState().ActiveTranslationId ?? "";
        LanguageCombo.Items.Add(new ComboBoxItem
        {
            Content = Strings.Get("MenuLangEnglish"),
            Tag = null,
            IsSelected = string.IsNullOrEmpty(activeId),
        });

        var entries = new Dictionary<string, TranslationIndexEntry>(StringComparer.OrdinalIgnoreCase);
        if (_translationIndex != null)
        {
            foreach (var e in _translationIndex.Translations)
                entries[e.Id] = e;
        }
        try
        {
            if (!string.IsNullOrEmpty(_service.InstallPath))
            {
                var installed = new TranslationService(_service.InstallPath).ListInstalled();
                foreach (var m in installed)
                {
                    if (entries.ContainsKey(m.Id)) continue;
                    entries[m.Id] = new TranslationIndexEntry
                    {
                        Id = m.Id, Name = m.Name, Author = m.Author, Version = m.Version,
                    };
                }
            }
        }
        catch { /* probe failure is non-fatal */ }

        foreach (var entry in entries.Values.OrderBy(e => e.Name))
        {
            var label = string.IsNullOrWhiteSpace(entry.Version)
                ? entry.Name
                : $"{entry.Name}  (v{entry.Version})";
            var item = new ComboBoxItem { Content = label, Tag = entry };
            if (string.Equals(entry.Id, activeId, StringComparison.OrdinalIgnoreCase))
                item.IsSelected = true;
            LanguageCombo.Items.Add(item);
        }

        bool hasPacks = entries.Count > 0;
        LanguageEmptyHint.Visibility = hasPacks ? Visibility.Collapsed : Visibility.Visible;
        LanguageCombo.IsEnabled = hasPacks;
        _suppressLanguageChange = false;
    }

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
    // Most handlers close the dialog before invoking the callback so
    // the user lands directly on the dialog/flow the callback opens
    // (verify progress strip, uninstall confirmation, path picker,
    // etc.) without the Properties window covering it. The website /
    // language handlers don't close because they don't navigate
    // elsewhere — the user might want to keep poking around.
    //
    // None of these set DialogResult: the dialog is shown non-modally
    // via Show() from MainWindow, and setting DialogResult outside of
    // ShowDialog() throws InvalidOperationException. The caller never
    // read DialogResult here anyway — the post-close refresh
    // (RefreshIdlePanel + RefreshActiveModBanner) runs on the Closed
    // event regardless of how the dialog was dismissed.

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

    private void CheckUpdatesBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
        _checkForUpdates?.Invoke();
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
        Close();
        _openAoE3Folder?.Invoke();
    }

    private void ChangeModFolderBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
        _changeModFolder?.Invoke();
    }

    private void ChangeAoE3FolderBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
        _changeAoE3Folder?.Invoke();
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
        Close();
        _viewLogs?.Invoke();
    }

    private void UninstallBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
        _uninstall?.Invoke();
    }

    private void OpenUserDataFolderBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
        _openUserDataFolder?.Invoke();
    }

    private void CreateBackupBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
        _createBackup?.Invoke();
    }

    private void RestoreBackupBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
        _restoreBackup?.Invoke();
    }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressLanguageChange) return;
        if (LanguageCombo.SelectedItem is not ComboBoxItem item) return;
        if (item.Tag is TranslationIndexEntry entry)
        {
            _applyTranslation?.Invoke(entry);
        }
        else
        {
            _revertToEnglish?.Invoke();
        }
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
