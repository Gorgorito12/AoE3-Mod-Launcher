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
/// Translator-facing dialog that turns a folder of translated XMLs into a
/// ready-to-publish .zip pack + a JSON snippet for translations-index.json.
/// Hides every technical step (hash computation, manifest authoring, zip
/// layout) behind a sectioned form.
///
/// Globalised across mods: the top "Mod a traducir" combo picks the target
/// profile. Switching it re-derives the originals snapshot path, the
/// compatibility version label, and the default output filename prefix —
/// so the same dialog works for WoL, Improvement Mod, and any catalog mod
/// the user has installed. The previous design hard-coded the launcher's
/// active mod, which forced the user to switch the active mod before
/// packaging a translation for a different one.
/// </summary>
public partial class TranslationPackagerDialog : Window
{
    private readonly LauncherConfig _config;

    /// <summary>
    /// Mods offered in the picker. Excludes <see cref="ModProfile.IsStockGame"/>
    /// because the stock detect-only TAD entry has no <c>data\</c> files
    /// the launcher manages — translating the base game isn't in scope.
    /// </summary>
    private readonly List<ModProfile> _profiles;

    /// <summary>The profile the combo currently has selected.</summary>
    private ModProfile? _selectedProfile;

    /// <summary>
    /// Service bound to the selected profile's install path. Re-created on
    /// every <see cref="ModCombo_SelectionChanged"/> so <see cref="OriginalsFolder"/>
    /// auto-fill and <see cref="HasOriginalsSnapshot"/> point at the right
    /// mod's snapshot.
    /// </summary>
    private TranslationService? _translationService;

    /// <summary>
    /// Installed version of the selected mod, for the "current version"
    /// compatibility checkbox label. "?" when the launcher hasn't detected
    /// one yet (fresh install, no startup check has run).
    /// </summary>
    private string _currentModVersion = "?";

    private string? _generatedZipPath;

    /// <summary>
    /// True after the user has clicked "Generate" once with a version that
    /// looks like a mod version (e.g. "1.2.0c2"). The first click shows a
    /// warning explaining the difference; a second click proceeds anyway in
    /// case the translator really does want that string.
    /// </summary>
    private bool _userAcknowledgedModVersionWarning;

    /// <summary>
    /// Tracks whether the user has manually edited the output path. When
    /// false, we keep the default in sync with the mod id + language id so
    /// switching mods or typing a new language auto-updates the suggested
    /// filename. As soon as the user types into OutputBox themselves, we
    /// stop overwriting it.
    /// </summary>
    private bool _outputIsAutoSuggested = true;

    /// <summary>The XML files the translator picked (any name). Empty = folder mode.</summary>
    private readonly List<string> _selectedTranslatedFiles = new();
    /// <summary>True while we set FolderBox from the picker, so the TextChanged
    /// handler doesn't mistake it for a manual edit and clear the file list.</summary>
    private bool _settingFolderProgrammatically;

    /// <summary>The original (EN) XML files the user picked. Empty = folder/snapshot mode.</summary>
    private readonly List<string> _selectedOriginalFiles = new();
    private bool _settingOriginalsProgrammatically;

    private readonly string _desktopFolder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

    public TranslationPackagerDialog(LauncherConfig config)
    {
        _config = config;
        _profiles = ModRegistry.All
            .Where(p => !p.IsStockGame)
            .ToList();

        InitializeComponent();
        // A manual edit of the path box drops back to "folder" mode; a
        // programmatic set from the file picker keeps the picked file list.
        FolderBox.TextChanged += (_, _) =>
        {
            if (!_settingFolderProgrammatically) _selectedTranslatedFiles.Clear();
        };
        OriginalsBox.TextChanged += (_, _) =>
        {
            if (!_settingOriginalsProgrammatically) _selectedOriginalFiles.Clear();
        };
        ApplyLanguage();
        PopulateModCombo();

        // Pick a sensible default: the launcher's currently active mod if
        // it's in the list, otherwise the first installed mod, otherwise
        // the first item. Falling through to "first item" matters when no
        // mod is installed yet — the translator can still draft a pack
        // pointing at their own backup of the originals.
        ModProfile? defaultPick =
            _profiles.FirstOrDefault(p =>
                string.Equals(p.Id, _config.ActiveModId, StringComparison.OrdinalIgnoreCase))
            ?? _profiles.FirstOrDefault(p =>
                !string.IsNullOrEmpty(_config.GetState(p.Id).InstallPath))
            ?? _profiles.FirstOrDefault();

        if (defaultPick != null)
        {
            foreach (ComboBoxItem item in ModCombo.Items)
            {
                if (ReferenceEquals(item.Tag, defaultPick))
                {
                    ModCombo.SelectedItem = item;
                    break;
                }
            }
        }

        // Reset the "looks like a mod version" warning whenever the user
        // re-edits the version field, so the soft warning re-arms for any
        // further surprising value.
        VersionBox.TextChanged += (_, _) => _userAcknowledgedModVersionWarning = false;

        // Once the user types into the output box themselves, stop the
        // auto-suggest. The autosuggest writes the default on mod-switch
        // and id-change but never clobbers a hand-typed path.
        OutputBox.TextChanged += OutputBox_TextChanged;

        // Re-suggest the output filename when the language id changes
        // (still respects the user-edited flag — won't overwrite).
        IdBox.TextChanged += (_, _) => RefreshAutoSuggestedOutput();
    }

    private void OutputBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        // The auto-suggest path sets OutputBox.Text programmatically — we
        // suppress the dirty flag during those writes by tracking the
        // expected suggestion. If the current text matches what we would
        // have suggested, the user hasn't edited, and we keep tracking.
        if (BuildSuggestedOutput() != OutputBox.Text)
        {
            _outputIsAutoSuggested = false;
        }
    }

    private string BuildSuggestedOutput()
    {
        var prefix = (_selectedProfile?.Id ?? "mod").ToLowerInvariant();
        var id = string.IsNullOrWhiteSpace(IdBox.Text) ? "lang" : IdBox.Text.Trim();
        return Path.Combine(_desktopFolder, $"{prefix}-{id}.zip");
    }

    private void RefreshAutoSuggestedOutput()
    {
        if (!_outputIsAutoSuggested) return;
        var suggestion = BuildSuggestedOutput();
        if (OutputBox.Text != suggestion)
        {
            // Suppress the dirty flag while we rewrite, otherwise the
            // TextChanged handler would flip _outputIsAutoSuggested back
            // to false in mid-update.
            OutputBox.TextChanged -= OutputBox_TextChanged;
            OutputBox.Text = suggestion;
            OutputBox.TextChanged += OutputBox_TextChanged;
        }
    }

    private void PopulateModCombo()
    {
        ModCombo.Items.Clear();
        foreach (var profile in _profiles)
        {
            var state = _config.GetState(profile.Id);
            bool installed = !string.IsNullOrEmpty(state.InstallPath)
                && Directory.Exists(state.InstallPath);

            var item = new ComboBoxItem
            {
                Tag = profile,
                Content = installed
                    ? profile.DisplayName
                    : $"{profile.DisplayName}  ·  {Strings.Get("DlgPackagerModNotInstalled")}",
            };
            ModCombo.Items.Add(item);
        }
    }

    private void ModCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var profile = (ModCombo.SelectedItem as ComboBoxItem)?.Tag as ModProfile;
        if (profile == null) return;

        _selectedProfile = profile;

        // Resolve install path (empty = not installed; ExportPackageAsync
        // still works as long as the translator provides an explicit
        // OriginalsFolder).
        var installPath = _config.GetState(profile.Id).InstallPath ?? "";
        // Pass the picked mod's covered files so the packager hashes/packs the
        // RIGHT files (not WoL's) for non-WoL mods.
        _translationService = new TranslationService(installPath, profile.Translations?.CoveredFiles);

        // Compatibility version: prefer the LastKnownVersion the launcher
        // detected on its last check. "?" is the sentinel the existing
        // export logic understands ("don't auto-include").
        var lastKnown = _config.GetState(profile.Id).LastKnownVersion;
        _currentModVersion = string.IsNullOrWhiteSpace(lastKnown) ? "?" : lastKnown;

        // If the picked mod has a usable snapshot, pre-fill it; otherwise
        // clear so the translator visibly knows they need to point at a
        // backup of the English files.
        if (!string.IsNullOrEmpty(installPath) && _translationService.HasOriginalsSnapshot())
        {
            OriginalsBox.Text = _translationService.OriginalsFolder;
        }
        else
        {
            OriginalsBox.Text = "";
        }

        // Refresh the compat-checkbox label so it reflects the new
        // mod's version (or "?" if unknown).
        CompatCurrentCheck.Content = Strings.Format("DlgPackagerCompatCurrent", _currentModVersion);
        CompatCurrentCheck.IsEnabled = _currentModVersion != "?";
        if (_currentModVersion == "?") CompatCurrentCheck.IsChecked = false;

        // Refresh the output filename suggestion (e.g. wol-es.zip → imp-es.zip
        // when switching from Wars of Liberty to Improvement Mod). Respects
        // the user-edited flag so a manually-typed path stays put.
        RefreshAutoSuggestedOutput();
    }

    private void ApplyLanguage()
    {
        Title = Strings.Get("DlgPackagerTitle");
        TitleBarControl.Title = Strings.Get("DlgPackagerTitle");
        HeaderText.Text = Strings.Get("DlgPackagerHeader");
        DescriptionText.Text = Strings.Get("DlgPackagerDescription");

        SectionModHeader.Text = Strings.Get("DlgPackagerSectionMod");
        SectionIdentityHeader.Text = Strings.Get("DlgPackagerSectionIdentity");
        SectionSourceHeader.Text = Strings.Get("DlgPackagerSectionSource");
        SectionCompatHeader.Text = Strings.Get("DlgPackagerSectionCompat");
        SectionOutputHeader.Text = Strings.Get("DlgPackagerSectionOutput");

        LblMod.Text = Strings.Get("DlgPackagerFieldMod");
        HintMod.Text = Strings.Get("DlgPackagerHintMod");

        LblId.Text = Strings.Get("DlgPackagerFieldId");
        HintId.Text = Strings.Get("DlgPackagerHintId");
        LblName.Text = Strings.Get("DlgPackagerFieldName");
        LblAuthor.Text = Strings.Get("DlgPackagerFieldAuthor");
        LblVersion.Text = Strings.Get("DlgPackagerFieldVersion");
        HintVersion.Text = Strings.Get("DlgPackagerHintVersion");
        LblDescription.Text = Strings.Get("DlgPackagerFieldDescription");
        HintDescription.Text = Strings.Get("DlgPackagerHintDescription");
        LblFolder.Text = Strings.Get("DlgPackagerFieldFolder");
        HintFolder.Text = Strings.Get("DlgPackagerHintFolder");
        LblOriginals.Text = Strings.Get("DlgPackagerFieldOriginals");
        HintOriginals.Text = Strings.Get("DlgPackagerHintOriginals");
        BrowseOriginalsButton.Content = Strings.Get("ChangePathButton");
        LblCompat.Text = Strings.Get("DlgPackagerFieldCompat");
        HintCompatExtra.Text = Strings.Get("DlgPackagerHintCompatExtra");
        LblOutput.Text = Strings.Get("DlgPackagerFieldOutput");
        BrowseFolderButton.Content = Strings.Get("ChangePathButton");
        BrowseOutputButton.Content = Strings.Get("ChangePathButton");
        CancelButton.Content = Strings.Get("BtnCancel");
        GenerateButton.Content = Strings.Get("DlgPackagerBtnGenerate");

        // Result-panel labels
        OpenFolderButton.Content = Strings.Get("DlgPackagerBtnOpenFolder");
        DoneButton.Content = Strings.Get("DlgPackagerBtnDone");
    }

    private void BrowseFolderButton_Click(object sender, RoutedEventArgs e)
    {
        // Pick the XML FILE(S) directly — a folder picker hides files and confused
        // translators ("nothing to select"). We store the containing folder; the
        // exporter finds the translated files inside it, matching the canonical
        // names even if the files were renamed (e.g. stringtabley_translated.xml).
        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = Strings.Get("DlgPackagerHintFolder"),
            Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*",
            Multiselect = true,
        };
        if (Directory.Exists(FolderBox.Text)) picker.InitialDirectory = FolderBox.Text;
        if (picker.ShowDialog(this) == true && picker.FileNames.Length > 0)
        {
            _selectedTranslatedFiles.Clear();
            _selectedTranslatedFiles.AddRange(picker.FileNames);
            // Show the picked file path(s) so it's clear a FILE was selected.
            _settingFolderProgrammatically = true;
            FolderBox.Text = string.Join("; ", picker.FileNames);
            _settingFolderProgrammatically = false;
        }
    }

    private void BrowseOriginalsButton_Click(object sender, RoutedEventArgs e)
    {
        // Same as the translated picker: choose the original English XML FILE(S)
        // directly (any name). Auto-fill from the snapshot still works as a folder.
        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = Strings.Get("DlgPackagerHintOriginals"),
            Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*",
            Multiselect = true,
        };
        if (Directory.Exists(OriginalsBox.Text)) picker.InitialDirectory = OriginalsBox.Text;
        else if (Directory.Exists(FolderBox.Text)) picker.InitialDirectory = FolderBox.Text;
        if (picker.ShowDialog(this) == true && picker.FileNames.Length > 0)
        {
            _selectedOriginalFiles.Clear();
            _selectedOriginalFiles.AddRange(picker.FileNames);
            _settingOriginalsProgrammatically = true;
            OriginalsBox.Text = string.Join("; ", picker.FileNames);
            _settingOriginalsProgrammatically = false;
        }
    }

    private void BrowseOutputButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.SaveFileDialog
        {
            Title = Strings.Get("DlgPackagerFieldOutput"),
            Filter = "ZIP archive (*.zip)|*.zip",
            FileName = string.IsNullOrWhiteSpace(OutputBox.Text)
                ? Path.GetFileName(BuildSuggestedOutput())
                : Path.GetFileName(OutputBox.Text),
        };
        try
        {
            var dir = Path.GetDirectoryName(OutputBox.Text);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                picker.InitialDirectory = dir;
        }
        catch { /* ignore */ }
        if (picker.ShowDialog(this) == true)
            OutputBox.Text = picker.FileName;
    }

    private async void GenerateButton_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Visibility = Visibility.Collapsed;

        if (_translationService == null || _selectedProfile == null)
        {
            ShowError(Strings.Get("DlgPackagerErrorNoMod"));
            return;
        }

        // ---- Validate fields ----
        if (string.IsNullOrWhiteSpace(IdBox.Text))
        {
            ShowError(Strings.Get("DlgPackagerErrorIdMissing"));
            return;
        }
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            ShowError(Strings.Get("DlgPackagerErrorNameMissing"));
            return;
        }
        if (string.IsNullOrWhiteSpace(VersionBox.Text))
        {
            ShowError(Strings.Get("DlgPackagerErrorVersionMissing"));
            return;
        }
        // Soft warning: "1.2.0c2" looks like a mod version, not a pack
        // version. The translation pack version is the translator's own
        // semver (1.0, 1.1, ...) and is independent of the mod version.
        if (LooksLikeModVersion(VersionBox.Text) && !_userAcknowledgedModVersionWarning)
        {
            ShowError(Strings.Format("DlgPackagerVersionLooksLikeMod", VersionBox.Text.Trim()));
            _userAcknowledgedModVersionWarning = true;
            return;
        }
        // Accept either picked files (any name) or a folder path typed/pasted in.
        bool hasPickedFiles = _selectedTranslatedFiles.Count > 0;
        if (hasPickedFiles ? !_selectedTranslatedFiles.All(File.Exists)
                           : !Directory.Exists(FolderBox.Text))
        {
            ShowError(Strings.Get("DlgPackagerErrorFolderMissing"));
            return;
        }
        if (string.IsNullOrWhiteSpace(OutputBox.Text))
        {
            ShowError(Strings.Get("DlgPackagerErrorOutputMissing"));
            return;
        }

        // ---- Build compatibleWith list ----
        var compat = new List<string>();
        if (CompatCurrentCheck.IsChecked == true && _currentModVersion != "?")
            compat.Add(_currentModVersion);
        if (!string.IsNullOrWhiteSpace(CompatExtraBox.Text))
        {
            foreach (var v in CompatExtraBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = v.Trim();
                if (!string.IsNullOrEmpty(trimmed) && !compat.Contains(trimmed))
                    compat.Add(trimmed);
            }
        }
        if (compat.Count == 0)
        {
            ShowError(Strings.Get("DlgPackagerErrorNoCompat"));
            return;
        }

        GenerateButton.IsEnabled = false;
        try
        {
            // ---- Run the export ----
            var inputs = new TranslationService.ExportInputs(
                Id: IdBox.Text.Trim(),
                Name: NameBox.Text.Trim(),
                Author: AuthorBox.Text?.Trim() ?? "",
                Version: VersionBox.Text.Trim(),
                Language: IdBox.Text.Trim(),
                CompatibleWith: compat,
                TranslatedFolder: hasPickedFiles
                    ? (Path.GetDirectoryName(_selectedTranslatedFiles[0]) ?? "")
                    : FolderBox.Text.Trim(),
                OutputZipPath: OutputBox.Text.Trim(),
                Description: string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim(),
                OriginalsFolder: (_selectedOriginalFiles.Count > 0 || string.IsNullOrWhiteSpace(OriginalsBox.Text))
                    ? null
                    : OriginalsBox.Text.Trim(),
                TargetMod: _selectedProfile?.Id ?? "",
                TranslatedFiles: hasPickedFiles ? _selectedTranslatedFiles : null,
                OriginalFiles: _selectedOriginalFiles.Count > 0 ? _selectedOriginalFiles : null);

            var result = await _translationService.ExportPackageAsync(inputs);
            if (!result.Success)
            {
                ShowError(result.ErrorMessage ?? "Unknown error.");
                return;
            }

            // ---- Show the result panel ----
            _generatedZipPath = result.ZipPath;
            ResultHeaderText.Text = Strings.Get("DlgPackagerResultHeader");

            // Show both files the translator now has on disk: the .zip
            // (translation pack) and the sibling translation.json — both
            // need to be uploaded as assets to a GitHub release.
            var pathLines = Strings.Format("DlgPackagerResultPath",
                result.ZipPath, FormatBytes(result.ZipSize));
            if (!string.IsNullOrEmpty(result.JsonPath))
            {
                pathLines += "\n" + Strings.Format("DlgPackagerResultJsonPath", result.JsonPath);
            }
            ResultPathText.Text = pathLines;

            // Point the translator at the SELECTED mod's translations repo, not a
            // hardcoded one — otherwise non-WoL packs get the wrong upload target.
            var repo = _selectedProfile?.Translations?.Repo;
            if (string.IsNullOrWhiteSpace(repo)) repo = _config.TranslationsRepo;
            ResultInstructionsText.Text = Strings.Format("DlgPackagerResultInstructions", repo);

            // Build a "this is how players will see it" preview that mirrors
            // BuildLanguageMenuItem in MainWindow.xaml.cs — gives the translator
            // a sanity check (catches "wait, that says v1.2.0c2 not v1.0").
            LblPreview.Text = Strings.Get("DlgPackagerResultPreviewLabel");
            PreviewText.Text = BuildMenuPreview(inputs);

            FormPanel.Visibility = Visibility.Collapsed;
            ResultPanel.Visibility = Visibility.Visible;
            FormFooter.Visibility = Visibility.Collapsed;
            ResultFooter.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Packager: unexpected error: {ex}");
            ShowError(ex.Message);
        }
        finally
        {
            GenerateButton.IsEnabled = true;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();
    private void DoneButton_Click(object sender, RoutedEventArgs e) => Close();

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_generatedZipPath)) return;
        // Open Explorer with the zip selected
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{_generatedZipPath}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Packager: open folder failed: {ex.Message}");
        }
    }

    private void ShowError(string message)
    {
        StatusText.Text = message;
        StatusText.Visibility = Visibility.Visible;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "?";
        string[] units = { "B", "KB", "MB", "GB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:0.#} {units[unit]}";
    }

    /// <summary>
    /// Mirrors the BuildLanguageMenuItem logic in MainWindow.xaml.cs so the
    /// translator sees an exact preview of how their pack will appear in the
    /// gear menu. Catches things like "wait, that says v1.2.0c2 — that's the
    /// mod version, not my translation version" before they publish.
    /// </summary>
    private static string BuildMenuPreview(TranslationService.ExportInputs inputs)
    {
        var flag = inputs.Id.ToLowerInvariant() switch
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
        // Mirror the menu format from MainWindow.BuildLanguageMenuItem:
        // pack version + mod compatibility + author.
        var preview = $"{flag}  {inputs.Name}";
        if (!string.IsNullOrEmpty(inputs.Version)) preview += $"  v{inputs.Version}";
        if (inputs.CompatibleWith != null && inputs.CompatibleWith.Count > 0)
        {
            var label = Strings.Format(
                "MenuLangModVersionLabel",
                string.Join(", ", inputs.CompatibleWith));
            preview += $"  {label}";
        }
        if (!string.IsNullOrEmpty(inputs.Author)) preview += $"  · {inputs.Author}";
        return preview;
    }

    /// <summary>
    /// Heuristic: does this string look like a WoL mod version (1.2.0c2)
    /// rather than a translation pack version (1.0)? Mod versions tend to
    /// have either 3+ dot-separated parts or letters at the end. Pack
    /// versions are usually X.Y or X.Y.Z without letters.
    /// </summary>
    private static bool LooksLikeModVersion(string s)
    {
        var v = s.Trim();
        if (string.IsNullOrEmpty(v)) return false;

        // 3+ dotted parts (e.g. 1.2.0c2 → ["1","2","0c2"]).
        var parts = v.Split('.');
        if (parts.Length >= 3) return true;

        // Final segment ends in a letter (e.g. "1.0c", "1.0.15d").
        var last = parts[^1];
        if (last.Length > 0 && char.IsLetter(last[^1])) return true;

        return false;
    }
}
