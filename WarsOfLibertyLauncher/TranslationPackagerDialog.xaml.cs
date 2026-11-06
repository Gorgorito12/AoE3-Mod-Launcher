using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarsOfLibertyLauncher.Controls;
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
    private const string Marked = "missing";

    /// <summary>One line of the mod selector: its icon, its name and the version it is on.</summary>
    public sealed record ModOption(ModProfile Profile, Brush? Icon, string Name, string Version, Brush NameBrush)
    {
        /// <summary>The name alone: a record would print its brushes, which belong to one thread.</summary>
        public override string ToString() => Name;
    }

    private readonly LauncherConfig _config;
    private readonly string? _previewVariant;

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

    /// <summary>
    /// True until the translator types a version of their own: the field then follows the mod
    /// version and the next free revision (<c>1.2.0e-r1</c>, <c>-r2</c>…).
    /// </summary>
    private bool _versionIsAutoSuggested = true;
    private bool _settingVersionProgrammatically;

    /// <summary>"id/version" folders already published in the selected mod's official repo.</summary>
    private readonly HashSet<string> _publishedVersionFolders = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The last successful export — what "Add to my index" writes.</summary>
    private TranslationService.ExportResult? _lastExport;

    /// <param name="config">The launcher's settings.</param>
    /// <param name="previewVariant"><c>--preview-dialog=packager:…</c>: paint that state, write nothing.</param>
    public TranslationPackagerDialog(LauncherConfig config, string? previewVariant = null)
    {
        _config = config;
        _previewVariant = previewVariant;
        _profiles = ModRegistry.All
            .Where(p => !p.IsStockGame)
            .ToList();

        InitializeComponent();
        ModDialogChrome.AttachGlobal(this);
        // A manual edit of the path box drops back to "folder" mode; a
        // programmatic set from the file picker keeps the picked file list.
        FolderBox.TextChanged += (_, _) =>
        {
            if (!_settingFolderProgrammatically) _selectedTranslatedFiles.Clear();
            ClearMark(FolderShell, HintFolder, "DlgPackagerHintFolder");
            RefreshFileFields();
        };
        OriginalsBox.TextChanged += (_, _) =>
        {
            if (!_settingOriginalsProgrammatically) _selectedOriginalFiles.Clear();
            RefreshFileFields();
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
            ModCombo.SelectedItem = ModCombo.Items.OfType<ModOption>()
                .FirstOrDefault(o => ReferenceEquals(o.Profile, defaultPick));
        }

        // Reset the "looks like a mod version" warning whenever the user
        // re-edits the version field, so the soft warning re-arms for any
        // further surprising value. A hand-typed version also stops the
        // field from following the mod version.
        VersionBox.TextChanged += (_, _) =>
        {
            _userAcknowledgedModVersionWarning = false;
            if (!_settingVersionProgrammatically) _versionIsAutoSuggested = false;
            ClearMark(VersionShell, HintVersion, VersionHintKey);
            GenerateButton.Content = Strings.Get("DlgPackagerBtnGenerate");
            UpdateFooterNote();
            UpdateProposedChips();
            UpdateVersionExistsWarning();
            UpdatePreview();
        };
        IdBox.TextChanged += (_, _) =>
        {
            ClearMark(IdShell, HintId, "DlgPackagerHintId");
            RefreshVersionSuggestion();
            UpdateVersionExistsWarning();
        };
        NameBox.TextChanged += (_, _) =>
        {
            ClearMark(NameShell, HintName, "DlgPackagerHintName");
            UpdatePreview();
        };
        AuthorBox.TextChanged += (_, _) => UpdatePreview();
        DescriptionBox.TextChanged += (_, _) => UpdatePlaceholders();
        CompatExtraBox.TextChanged += (_, _) =>
        {
            ClearCompatMark();
            UpdatePlaceholders();
            UpdatePreview();
        };
        CompatCurrentCheck.Checked += (_, _) => { ClearCompatMark(); UpdatePreview(); };
        CompatCurrentCheck.Unchecked += (_, _) => UpdatePreview();
        IndexZipUrlBox.TextChanged += (_, _) => UpdatePlaceholders();

        // Once the user types into the output box themselves, stop the
        // auto-suggest. The autosuggest writes the default on mod-switch
        // and id-change but never clobbers a hand-typed path.
        OutputBox.TextChanged += OutputBox_TextChanged;

        // Re-suggest the output filename when the language id changes
        // (still respects the user-edited flag — won't overwrite).
        IdBox.TextChanged += (_, _) => RefreshAutoSuggestedOutput();

        ModDialogChrome.SetLineHeight(1.25, HeaderText, ResultHeaderText);
        ModDialogChrome.SetLineHeight(1.55, DescriptionText);
        ModDialogChrome.SetLineHeight(1.5, ResultSummaryText, IndexHint);
        UpdatePlaceholders();
        UpdateProposedChips();
        UpdatePreview();
        UpdateFooterNote();
        if (_previewVariant != null) ShowPreview(_previewVariant);
    }

    private string VersionHintKey => _currentModVersion == "?" ? "DlgPackagerHintVersionUnknown" : "DlgPackagerHintVersion";

    /// <summary>A placeholder shows only while its box is empty.</summary>
    private void UpdatePlaceholders()
    {
        static Visibility V(TextBox b) => string.IsNullOrEmpty(b.Text) ? Visibility.Visible : Visibility.Collapsed;
        DescriptionPlaceholder.Visibility = V(DescriptionBox);
        CompatPlaceholder.Visibility = V(CompatExtraBox);
        IndexZipUrlPlaceholder.Visibility = V(IndexZipUrlBox);
    }

    private void UpdateProposedChips()
    {
        VersionProposed.Visibility = _versionIsAutoSuggested && VersionBox.Text.Length > 0
            ? Visibility.Visible : Visibility.Collapsed;
        OutputProposed.Visibility = _outputIsAutoSuggested ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateFooterNote()
    {
        FooterNote.Text = Strings.Get(
            ResultPanel.Visibility == Visibility.Visible ? "DlgPackagerFootDone"
            : _userAcknowledgedModVersionWarning ? "DlgPackagerFootConfirm"
            : "DlgPackagerFootReady");
    }

    /// <summary>What was picked shows as chips; a typed folder stays a path in the box.</summary>
    private void RefreshFileFields()
    {
        ShowFiles(_selectedTranslatedFiles, FolderChips, FolderBox, FolderPlaceholder);
        ShowFiles(_selectedOriginalFiles, OriginalsChips, OriginalsBox, OriginalsPlaceholder);
    }

    private static void ShowFiles(List<string> files, WrapPanel chips, TextBox box, TextBlock placeholder)
    {
        chips.Children.Clear();
        foreach (var file in files)
        {
            var name = new TextBlock
            {
                Text = Path.GetFileName(file),
                FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            name.SetResourceReference(TextBlock.FontSizeProperty, "SetDescSize");
            name.SetResourceReference(TextBlock.ForegroundProperty, "UiBodyText");
            chips.Children.Add(new Border
            {
                Height = 22,
                CornerRadius = new CornerRadius(11),
                Padding = new Thickness(8, 0, 8, 0),
                Margin = new Thickness(0, 4, 8, 4),
                Background = new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                Child = name,
            });
        }
        var picked = files.Count > 0;
        chips.Visibility = picked ? Visibility.Visible : Visibility.Collapsed;
        box.Visibility = picked ? Visibility.Collapsed : Visibility.Visible;
        placeholder.Visibility = !picked && box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ClearMark(Border shell, TextBlock hint, string hintKey)
    {
        if (shell.Tag == null) return;
        shell.ClearValue(TagProperty);
        hint.ClearValue(TextBlock.ForegroundProperty);
        hint.Text = Strings.Get(hintKey);
    }

    private void ClearCompatMark()
    {
        CompatShell.ClearValue(TagProperty);
        HintCompat.Visibility = Visibility.Collapsed;
    }

    private void Mark(Border shell, TextBlock hint, string message)
    {
        shell.Tag = Marked;
        hint.Text = message;
        hint.SetResourceReference(TextBlock.ForegroundProperty, "WindowNoticeTitle");
        hint.Visibility = Visibility.Visible;
    }

    private List<string> CompatVersions()
    {
        var compat = new List<string>();
        if (CompatCurrentCheck.IsChecked == true && _currentModVersion != "?")
            compat.Add(_currentModVersion);
        foreach (var v in (CompatExtraBox.Text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = v.Trim();
            if (trimmed.Length > 0 && !compat.Contains(trimmed)) compat.Add(trimmed);
        }
        return compat;
    }

    /// <summary>The line the language menu will show, in the form as it is typed and in the result.
    /// It mirrors MainWindow.BuildLanguageMenuItem: name, version, compatibility and author.</summary>
    private void UpdatePreview()
    {
        var name = NameBox.Text.Trim();
        var version = VersionBox.Text.Trim();
        var compat = CompatVersions();
        var compatText = compat.Count > 0
            ? Strings.Format("DlgPackagerPreviewCompat", string.Join(", ", compat))
            : Strings.Get("DlgPackagerPreviewNoCompat");
        var author = AuthorBox.Text.Trim();
        var authorText = author.Length > 0 ? "· " + author : "";
        var versionText = "v" + (version.Length > 0 ? version : "…");

        FormPreviewName.Text = ResultPreviewName.Text = name.Length > 0 ? name : "…";
        FormPreviewVersion.Text = ResultPreviewVersion.Text = versionText;
        FormPreviewCompat.Text = ResultPreviewCompat.Text = compatText;
        FormPreviewAuthor.Text = ResultPreviewAuthor.Text = authorText;
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
            UpdateProposedChips();
        }
        OutputShell.ClearValue(TagProperty);
        HintOutput.Visibility = Visibility.Collapsed;
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
        var strong = (Brush)FindResource("WindowStrongText");
        var dim = (Brush)FindResource("WindowFineText");
        foreach (var profile in _profiles)
        {
            var state = _config.GetState(profile.Id);
            bool installed = !string.IsNullOrEmpty(state.InstallPath)
                && Directory.Exists(state.InstallPath);

            ModCombo.Items.Add(new ModOption(
                profile,
                MainWindow.TryLoadTileImage(profile.ResolveIconSource()),
                installed
                    ? profile.DisplayName
                    : $"{profile.DisplayName} · {Strings.Get("DlgPackagerModNotInstalled")}",
                installed ? state.LastKnownVersion ?? "" : "",
                installed ? strong : dim));
        }
    }

    private void ModCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var profile = (ModCombo.SelectedItem as ModOption)?.Profile;
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
        var known = _currentModVersion != "?";
        CompatCurrentText.Text = known
            ? Strings.Format("DlgPackagerCompatCurrent", _currentModVersion)
            : Strings.Get("DlgPackagerCompatUnknown");
        CompatCurrentHint.Text = Strings.Get(known ? "DlgPackagerCompatHint" : "DlgPackagerCompatHintUnknown");
        CompatCurrentCheck.IsEnabled = known;
        CompatCurrentCheck.IsChecked = known;

        var installed = !string.IsNullOrEmpty(installPath) && Directory.Exists(installPath);
        HintMod.Text = Strings.Get(installed ? "DlgPackagerHintMod" : "DlgPackagerHintModMissing");
        HintOriginals.Text = Strings.Get(installed ? "DlgPackagerHintOriginals" : "DlgPackagerHintOriginalsMissing");
        HintVersion.Text = Strings.Get(VersionHintKey);

        // Refresh the output filename suggestion (e.g. wol-es.zip → imp-es.zip
        // when switching from Wars of Liberty to Improvement Mod). Respects
        // the user-edited flag so a manually-typed path stays put.
        RefreshAutoSuggestedOutput();

        // The version follows the mod version (1.2.0e-r1…); the official repo's published
        // folders are read in the background so the next free revision is proposed and a
        // collision is flagged before anything is built.
        RefreshVersionSuggestion();
        _ = LoadPublishedVersionsAsync(profile);
    }

    /// <summary>
    /// Reads which version folders the mod's official repository already has (one Git Trees
    /// call). Best-effort: offline or rate-limited, the packager still works and only the
    /// local output folder is checked.
    /// </summary>
    private async System.Threading.Tasks.Task LoadPublishedVersionsAsync(ModProfile profile)
    {
        _publishedVersionFolders.Clear();
        var repo = profile.Translations?.FolderRepo;
        if (string.IsNullOrWhiteSpace(repo)) return;
        try
        {
            var paths = await new TranslationRegistryService().ListManifestPathsAsync(repo!);
            if (paths == null || !ReferenceEquals(_selectedProfile, profile)) return;
            foreach (var p in paths)
            {
                var seg = p.Split('/');
                if (seg.Length == 4 && seg[0] == "translations") _publishedVersionFolders.Add($"{seg[1]}/{seg[2]}");
            }
            RefreshVersionSuggestion();
            UpdateVersionExistsWarning();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Packager: could not list published versions: {ex.Message}");
        }
    }

    /// <summary>Version folders already taken for <paramref name="id"/>: published, or built in the output folder.</summary>
    private List<string> ExistingVersionsFor(string id)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(id)) return result;
        foreach (var f in _publishedVersionFolders)
        {
            var slash = f.IndexOf('/');
            if (slash > 0 && string.Equals(f[..slash], id, StringComparison.OrdinalIgnoreCase)) result.Add(f[(slash + 1)..]);
        }
        try
        {
            var dir = Path.GetDirectoryName(OutputBox.Text.Trim());
            if (!string.IsNullOrEmpty(dir) && TranslationPathPolicy.IsSafePackId(id))
            {
                var local = Path.Combine(dir, "translations", id);
                if (Directory.Exists(local))
                    result.AddRange(Directory.GetDirectories(local).Select(Path.GetFileName).OfType<string>());
            }
        }
        catch { /* a half-typed path is not worth an error */ }
        return result;
    }

    private void RefreshVersionSuggestion()
    {
        if (!_versionIsAutoSuggested || VersionBox == null) return;
        var suggestion = TranslationIndexWriter.NextRevision(_currentModVersion, ExistingVersionsFor(IdBox.Text.Trim()));
        if (VersionBox.Text == suggestion) return;
        _settingVersionProgrammatically = true;
        VersionBox.Text = suggestion;
        _settingVersionProgrammatically = false;
        UpdateProposedChips();
    }

    /// <summary>
    /// Warns when the version folder already exists. A published version may already be on
    /// players' PCs; publishing different files under it would make "which 1.1 do I have?"
    /// unanswerable again (and the translations repo's CI refuses it).
    /// </summary>
    private void UpdateVersionExistsWarning()
    {
        if (VersionExistsText == null) return;
        var id = IdBox.Text.Trim();
        var version = VersionBox.Text.Trim();
        var existing = ExistingVersionsFor(id);
        var taken = version.Length > 0 && existing.Contains(version, StringComparer.OrdinalIgnoreCase);
        if (taken)
            ShowVersionExists(version, TranslationIndexWriter.NextRevision(_currentModVersion, existing));
        VersionExistsBox.Visibility = taken ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowVersionExists(string version, string next)
    {
        VersionExistsText.Inlines.Clear();
        ModDialogChrome.AddMarkedText(VersionExistsText, Strings.Format("DlgPackagerVersionExists", version, next));
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

        HintMod.Text = Strings.Get("DlgPackagerHintMod");

        LblId.Text = Strings.Get("DlgPackagerFieldId");
        HintId.Text = Strings.Get("DlgPackagerHintId");
        LblName.Text = Strings.Get("DlgPackagerFieldName");
        HintName.Text = Strings.Get("DlgPackagerHintName");
        LblAuthor.Text = Strings.Get("DlgPackagerFieldAuthor");
        LblVersion.Text = Strings.Get("DlgPackagerFieldVersion");
        HintVersion.Text = Strings.Get("DlgPackagerHintVersion");
        LblDescription.Text = Strings.Get("DlgPackagerFieldDescription");
        DescriptionPlaceholder.Text = Strings.Get("DlgPackagerHintDescription");
        LblPreviewForm.Text = Strings.Get("DlgPackagerResultPreviewLabel");
        LblPreview.Text = Strings.Get("DlgPackagerResultPreviewLabel");
        LblFolder.Text = Strings.Get("DlgPackagerFieldFolder");
        HintFolder.Text = Strings.Get("DlgPackagerHintFolder");
        LblOriginals.Text = Strings.Get("DlgPackagerFieldOriginals");
        HintOriginals.Text = Strings.Get("DlgPackagerHintOriginals");
        FolderPlaceholder.Text = OriginalsPlaceholder.Text = Strings.Get("DlgPatchGenNotChosen");
        BrowseFolderButton.Content = Strings.Get("DlgPackagerChooseFiles");
        BrowseOriginalsButton.Content = Strings.Get("DlgPackagerChooseFiles");
        LblCompatExtra.Text = Strings.Get("DlgPackagerHintCompatExtra");
        CompatPlaceholder.Text = Strings.Get("DlgPackagerCompatPlaceholder");
        LblOutput.Text = Strings.Get("DlgPackagerFieldOutput");
        BrowseOutputButton.Content = Strings.Get("DlgPackagerBrowse");
        VersionProposedText.Text = OutputProposedText.Text = Strings.Get("DlgPackagerProposed");
        VersionProposed.ToolTip = TooltipHelper.Wrap(Strings.Get("DlgPackagerProposedVersionTip"));
        OutputProposed.ToolTip = TooltipHelper.Wrap(Strings.Get("DlgPackagerProposedOutputTip"));
        CancelButton.Content = Strings.Get("BtnCancel");
        GenerateButton.Content = Strings.Get("DlgPackagerBtnGenerate");

        // Result-panel labels
        ResultHeaderText.Text = Strings.Get("DlgPackagerResultHeader");
        ResultFilesHeader.Text = Strings.Get("DlgPackagerWritten");
        ResultInstructionsHeader.Text = Strings.Get("DlgPackagerPublishHeader");
        ResultInstructionsNote.Text = Strings.Get("DlgPackagerPublishNote");
        OpenFolderButton.Content = Strings.Get("DlgPackagerBtnOpenFolder");
        OpenFolderInCardButton.Content = Strings.Get("DlgPackagerBtnOpenFolder");
        DoneButton.Content = Strings.Get("DlgPackagerBtnDone");
        IndexSectionHeader.Text = Strings.Get("DlgPackagerIndexHeader");
        IndexHint.Inlines.Clear();
        ModDialogChrome.AddMarkedText(IndexHint, Strings.Get("DlgPackagerIndexHint"));
        LblIndexZipUrl.Text = Strings.Get("DlgPackagerIndexZipUrl");
        IndexZipUrlPlaceholder.Text = Strings.Get("DlgPackagerIndexZipUrlHint");
        AddToIndexButton.Content = Strings.Get("DlgPackagerIndexAddButton");
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
        ErrorBox.Visibility = Visibility.Collapsed;

        if (_translationService == null || _selectedProfile == null)
        {
            ShowError(Strings.Get("DlgPackagerFailTitle"), Strings.Get("DlgPackagerErrorNoMod"));
            return;
        }

        bool hasPickedFiles = _selectedTranslatedFiles.Count > 0;
        var compat = CompatVersions();
        if (ValidateFields(hasPickedFiles, compat) is var problems && problems > 0)
        {
            ShowError(
                problems == 1 ? Strings.Get("DlgPackagerMissingOne") : Strings.Format("DlgPackagerMissingMany", problems),
                Strings.Get("DlgPackagerMissingBody"));
            return;
        }

        // Soft warning: "1.2.0c2" looks like a mod version, not a pack version. The translation
        // pack version is the translator's own and is independent of the mod version. The proposed
        // "<mod version>-rN" deliberately names the mod version, so it doesn't warn.
        if (LooksLikeModVersion(VersionBox.Text)
            && !TranslationIndexWriter.IsRevisionOfModVersion(VersionBox.Text)
            && !_userAcknowledgedModVersionWarning)
        {
            _userAcknowledgedModVersionWarning = true;
            ShowError(Strings.Get("DlgPackagerLooksLikeModTitle"),
                Strings.Format("DlgPackagerVersionLooksLikeMod", VersionBox.Text.Trim()));
            GenerateButton.Content = Strings.Get("DlgPackagerGenerateAnyway");
            UpdateFooterNote();
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
                ShowError(Strings.Get("DlgPackagerFailTitle"), result.ErrorMessage ?? "Unknown error.");
                return;
            }

            _lastExport = result;
            IndexZipUrlBox.Text = "";
            IndexResultBox.Visibility = Visibility.Collapsed;
            // Point "Open folder" at the ready-to-commit translations/<id>/ folder
            // when we built one (the new path); else fall back to the loose zip.
            _generatedZipPath = !string.IsNullOrEmpty(result.FolderPath)
                ? result.FolderPath
                : result.ZipPath;

            // Point the translator at the SELECTED mod's translations repo, not a hardcoded one:
            // otherwise non-WoL packs get the wrong upload target. Prefer the folder repo, fall
            // back to the legacy releases repo / global override.
            var publishRepo = _selectedProfile?.Translations?.FolderRepo;
            if (string.IsNullOrWhiteSpace(publishRepo)) publishRepo = _selectedProfile?.Translations?.Repo;
            if (string.IsNullOrWhiteSpace(publishRepo)) publishRepo = _config.TranslationsRepo;

            ShowResult(inputs.Name, _selectedProfile?.DisplayName ?? "", inputs.Version, inputs.Id,
                Path.GetDirectoryName(result.ZipPath) ?? "", publishRepo ?? "",
                result.FolderPath, result.ZipPath, result.ZipSize, result.JsonPath);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Packager: unexpected error: {ex}");
            ShowError(Strings.Get("DlgPackagerFailTitle"), ex.Message);
        }
        finally
        {
            GenerateButton.IsEnabled = true;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();
    private void DoneButton_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Writes this version into the translator's <c>translations-index.json</c> — creating it, or
    /// adding to the one they already publish — with the zip's SHA-256 filled in, so nobody types
    /// a hash. The <c>zip</c> is the link they pasted (Drive, Dropbox…) or, when empty, the
    /// relative path next to the index (a website or a repo that hosts both).
    /// </summary>
    private void AddToIndexButton_Click(object sender, RoutedEventArgs e)
    {
        var export = _lastExport;
        var m = export?.Manifest;
        if (export == null || m == null || string.IsNullOrEmpty(export.Sha256) || string.IsNullOrEmpty(export.ZipPath)) return;

        string zip;
        var typed = (IndexZipUrlBox.Text ?? "").Trim();
        if (typed.Length > 0)
        {
            var resolved = ShareLinkResolver.Resolve(typed);
            if (!typed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || resolved.Kind == ShareLinkKind.Rejected)
            {
                ShowIndexResult(Strings.Format("DlgPackagerIndexBadZipUrl",
                    Strings.Get(resolved.ReasonKey ?? "TxSrcErrNotHttps")), error: true);
                return;
            }
            zip = typed;
        }
        else
        {
            zip = $"translations/{m.Id}/{m.Version}/{Path.GetFileName(export.ZipPath)}";
        }

        var outputDir = Path.GetDirectoryName(export.ZipPath) ?? _desktopFolder;
        var picker = new Microsoft.Win32.SaveFileDialog
        {
            Title = Strings.Get("DlgPackagerIndexAddButton"),
            Filter = "translations-index.json|translations-index.json|JSON (*.json)|*.json",
            FileName = "translations-index.json",
            OverwritePrompt = false,
            InitialDirectory = Directory.Exists(outputDir) ? outputDir : _desktopFolder,
        };
        if (picker.ShowDialog(this) != true) return;

        try
        {
            var existing = File.Exists(picker.FileName) ? File.ReadAllText(picker.FileName) : null;
            var draft = new IndexItemDraft(
                m.Id, m.Name, m.Author, m.Language, m.TargetMod, m.Version, m.CompatibleWith,
                zip, export.Sha256!, export.ZipSize, TranslationCompat.EffectiveContentHash(m),
                m.Date ?? "", m.Description);
            var sourceName = string.IsNullOrWhiteSpace(m.Author) ? null : Strings.Format("DlgPackagerIndexDefaultName", m.Author);
            if (!TranslationIndexWriter.Merge(existing, draft, sourceName, out var json, out var err) || json == null)
            {
                ShowIndexResult(Strings.Format("DlgPackagerIndexUnreadable", err ?? ""), error: true);
                return;
            }
            File.WriteAllText(picker.FileName, json);
            DiagnosticLog.Write($"Packager: wrote '{m.Id}' v{m.Version} into '{picker.FileName}'.");
            ShowIndexResult(Strings.Format("DlgPackagerIndexWritten", picker.FileName), error: false);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Packager: could not write the index: {ex.Message}");
            ShowIndexResult(Strings.Format("DlgPackagerIndexUnreadable", ex.Message), error: true);
        }
    }

    private void ShowIndexResult(string text, bool error)
    {
        IndexResultText.Inlines.Clear();
        ModDialogChrome.AddMarkedText(IndexResultText, text);
        IndexResultMark.Text = error ? "!" : "\u2713";
        IndexResultMark.SetResourceReference(TextBlock.ForegroundProperty, error ? "WindowNoticeDot" : "WindowSafeTick");
        IndexResultBox.SetResourceReference(Border.BackgroundProperty, error ? "WindowNoticeFill" : "WindowSafeFill");
        IndexResultBox.BorderBrush = new SolidColorBrush(error
            ? Color.FromArgb(0x73, 0xF4, 0x90, 0x0C)
            : Color.FromArgb(0x5C, 0x4F, 0xD6, 0x8A));
        IndexResultBox.Visibility = Visibility.Visible;
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_generatedZipPath)) return;
        // Open the translations/<id>/ folder directly when we built one; for a
        // plain file path, open Explorer with the file selected.
        try
        {
            bool isDir = System.IO.Directory.Exists(_generatedZipPath);
            Services.HarnessMode.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = isDir ? $"\"{_generatedZipPath}\"" : $"/select,\"{_generatedZipPath}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Packager: open folder failed: {ex.Message}");
        }
    }

    /// <summary>Marks each field that is not ready and answers how many are not.</summary>
    internal int ValidateFields(bool hasPickedFiles, List<string> compat)
    {
        var problems = 0;
        void Fail(Border shell, TextBlock hint, string key)
        {
            Mark(shell, hint, Strings.Get(key));
            problems++;
        }

        // Both become folder names (translations/<id>/<version>/) and the launcher refuses a pack
        // whose id isn't a plain folder name: say so here, before anything is built.
        var id = IdBox.Text.Trim();
        if (id.Length == 0) Fail(IdShell, HintId, "DlgPackagerErrorIdMissing");
        else if (!TranslationPathPolicy.IsSafePackId(id)) Fail(IdShell, HintId, "DlgPackagerErrorIdInvalid");

        if (NameBox.Text.Trim().Length == 0) Fail(NameShell, HintName, "DlgPackagerErrorNameMissing");

        var version = VersionBox.Text.Trim();
        if (version.Length == 0) Fail(VersionShell, HintVersion, "DlgPackagerErrorVersionMissing");
        else if (!TranslationPathPolicy.IsSafeVersionSegment(version)) Fail(VersionShell, HintVersion, "DlgPackagerErrorVersionInvalid");

        // Accept either picked files (any name) or a folder path typed/pasted in.
        if (hasPickedFiles ? !_selectedTranslatedFiles.All(File.Exists) : !Directory.Exists(FolderBox.Text))
            Fail(FolderShell, HintFolder, "DlgPackagerErrorFolderMissing");

        if (compat.Count == 0) Fail(CompatShell, HintCompat, "DlgPackagerErrorNoCompat");
        if (OutputBox.Text.Trim().Length == 0) Fail(OutputShell, HintOutput, "DlgPackagerErrorOutputMissing");
        return problems;
    }

    private void ShowError(string title, string detail)
    {
        ErrorTitle.Text = title;
        ErrorBody.Text = detail;
        ErrorBox.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(new Action(() => ErrorBox.BringIntoView()), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>The form gives way to what was written, how it will look and how to publish it.</summary>
    internal void ShowResult(string name, string mod, string version, string id, string outputDir, string repo,
        string? folder, string? zip, long zipSize, string? json)
    {
        var where = string.Equals(outputDir.TrimEnd('\\'), _desktopFolder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
            ? Strings.Get("DlgPackagerOnTheDesktop")
            : outputDir;
        ResultSummaryText.Inlines.Clear();
        ModDialogChrome.AddMarkedText(ResultSummaryText, Strings.Format("DlgPackagerResultSummary", name, mod, version, where));

        ResultFiles.Children.Clear();
        if (!string.IsNullOrEmpty(folder))
            AddResultFile(Path.GetRelativePath(outputDir, folder) + "\\", "", "DlgPackagerRoleFolder");
        if (!string.IsNullOrEmpty(zip))
            AddResultFile(Path.GetFileName(zip), DiskSpaceService.FormatBytes(zipSize), "DlgPackagerRoleZip");
        if (!string.IsNullOrEmpty(json) && File.Exists(json))
            AddResultFile(Path.GetFileName(json), DiskSpaceService.FormatBytes(new FileInfo(json).Length), "DlgPackagerRoleJson");

        ResultSteps.Children.Clear();
        AddStep(1, Strings.Format("DlgPackagerStepFork", repo));
        AddStep(2, Strings.Format("DlgPackagerStepCopy", $"translations\\{id}\\"));
        AddStep(3, Strings.Get("DlgPackagerStepPr"));

        UpdatePreview();
        FormPanel.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Visible;
        FormFooter.Visibility = Visibility.Collapsed;
        ResultFooter.Visibility = Visibility.Visible;
        BodyScroll.ScrollToTop();
        UpdateFooterNote();
    }

    private void AddResultFile(string name, string size, string roleKey)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var mono = new FontFamily("Consolas, Courier New, monospace");

        var file = new TextBlock { Text = name, FontFamily = mono, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 12, 0) };
        file.SetResourceReference(TextBlock.FontSizeProperty, "SetRowDescSize");
        file.SetResourceReference(TextBlock.ForegroundProperty, "WindowStrongText");
        grid.Children.Add(file);

        var weight = new TextBlock { Text = size, FontFamily = mono, VerticalAlignment = VerticalAlignment.Center };
        weight.SetResourceReference(TextBlock.FontSizeProperty, "SetControlSize");
        weight.SetResourceReference(TextBlock.ForegroundProperty, "WindowMuted");
        Grid.SetColumn(weight, 1);
        grid.Children.Add(weight);

        var role = new TextBlock { Text = Strings.Get(roleKey), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        role.SetResourceReference(TextBlock.FontSizeProperty, "SetDescSize");
        role.SetResourceReference(TextBlock.ForegroundProperty, "WindowFineText");
        Grid.SetRow(role, 1);
        Grid.SetColumnSpan(role, 2);
        grid.Children.Add(role);

        ResultFiles.Children.Add(new Border
        {
            MinHeight = 40,
            Background = new SolidColorBrush(Color.FromArgb(0x66, 0x05, 0x0A, 0x12)),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(11, 6, 11, 6),
            Margin = new Thickness(0, 8, 0, 0),
            Child = grid,
        });
    }

    private void AddStep(int number, string text)
    {
        var row = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var digit = new TextBlock
        {
            Text = number.ToString(),
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        digit.SetResourceReference(TextBlock.ForegroundProperty, "WindowIncremental");
        row.Children.Add(new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0x2F, 0x7F, 0xE0)),
            Child = digit,
        });

        var line = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 2, 0, 0) };
        line.SetResourceReference(TextBlock.FontSizeProperty, "DialogBodySize");
        line.SetResourceReference(TextBlock.ForegroundProperty, "UiBodyText");
        ModDialogChrome.AddMarkedText(line, text);
        Grid.SetColumn(line, 1);
        row.Children.Add(line);
        ResultSteps.Children.Add(row);
    }

    private void ShowPreview(string variant)
    {
        IdBox.Text = "es";
        NameBox.Text = "Español";
        AuthorBox.Text = "Gorgo";
        switch (variant.ToLowerInvariant())
        {
            case "exists":
                ShowVersionExists("1.6.2-r1", "1.6.2-r2");
                VersionExistsBox.Visibility = Visibility.Visible;
                break;
            case "invalid":
                IdBox.Text = "";
                GenerateButton_Click(this, new RoutedEventArgs());
                break;
            case "result":
                ShowResult("Español", "Wars of Liberty", "1.6.2-r2", "es", _desktopFolder, "Gorgorito12/wol-translations",
                    Path.Combine(_desktopFolder, "translations", "es", "1.6.2-r2"), Path.Combine(_desktopFolder, "wol-es.zip"), 318 * 1024, null);
                ShowIndexResult(Strings.Format("DlgPackagerIndexWritten", Path.Combine(_desktopFolder, "translations-index.json")), error: false);
                break;
        }
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
