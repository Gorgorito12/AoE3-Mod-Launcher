using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Modal "Launcher Settings" dialog. Reads the current <see cref="LauncherConfig"/>
/// state on open, lets the user tweak launcher-wide preferences (NOT
/// per-mod state — that lives in the sidebar gear menu), and persists the
/// changes back when the user hits Save.
///
/// The dialog is mostly value mapping: each control mirrors one field in
/// LauncherConfig. The only side-effect not covered by a config write is
/// the Windows registry mutation for "Start with Windows" — handled by
/// <see cref="StartupRegistrationService.Apply"/>.
///
/// On Save, sets <see cref="Window.DialogResult"/>=true so the caller can
/// react (e.g. apply the new language live without re-rendering everything
/// from scratch). Cancel/Close set false.
/// </summary>
public partial class LauncherSettingsDialog : Window
{
    private readonly LauncherConfig _config;

    /// <summary>
    /// Regex for a valid "owner/repo" GitHub identifier. Mirrors the
    /// pattern used by mod.schema.json for the same field — so the
    /// dialog's UX feels consistent with what the catalog accepts.
    /// </summary>
    private static readonly Regex RepoRegex =
        new(@"^[a-zA-Z0-9._-]+/[a-zA-Z0-9._-]+$", RegexOptions.Compiled);

    private const string DefaultCatalogRepo = "Gorgorito12/aoe3-mods-catalog";

    public LauncherSettingsDialog(LauncherConfig config)
    {
        InitializeComponent();
        _config = config;
        ApplyLanguage();
        LoadFromConfig();
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

        SectionGeneralText.Text = Strings.Get("DlgLauncherSettingsSectionGeneral");
        SectionUpdatesText.Text = Strings.Get("DlgLauncherSettingsSectionUpdates");
        SectionCatalogText.Text = Strings.Get("DlgLauncherSettingsSectionCatalog");

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

        SectionMaintenanceText.Text = Strings.Get("DlgLauncherSettingsSectionMaintenance");
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

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
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

        DialogResult = true;
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
