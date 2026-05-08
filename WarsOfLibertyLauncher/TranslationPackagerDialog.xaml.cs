using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Translator-facing dialog that turns a folder of translated XMLs into a
/// ready-to-publish .zip pack + a JSON snippet for translations-index.json.
/// Hides every technical step (hash computation, manifest authoring, zip
/// layout) behind a simple form.
/// </summary>
public partial class TranslationPackagerDialog : Window
{
    private readonly TranslationService _translationService;
    private readonly string _currentModVersion;
    private string? _generatedZipPath;

    /// <summary>
    /// True after the user has clicked "Generate" once with a version that
    /// looks like a mod version (e.g. "1.2.0c2"). The first click shows a
    /// warning explaining the difference; a second click proceeds anyway in
    /// case the translator really does want that string.
    /// </summary>
    private bool _userAcknowledgedModVersionWarning;

    public TranslationPackagerDialog(TranslationService translationService, string? currentModVersion)
    {
        InitializeComponent();
        _translationService = translationService;
        _currentModVersion = currentModVersion ?? "?";

        ApplyLanguage();

        // If the launcher already has a snapshot of the original English
        // files (created during install/update), pre-fill it. Otherwise
        // leave empty — the translator will point at their own backup.
        if (_translationService.HasOriginalsSnapshot())
        {
            OriginalsBox.Text = _translationService.OriginalsFolder;
        }

        // Reset the mod-version-looks-suspicious warning whenever the
        // user types in the version field — so editing re-arms the
        // warning for any further surprising values.
        VersionBox.TextChanged += (_, _) => _userAcknowledgedModVersionWarning = false;

        // Default output: user's Desktop, named after the current id field.
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        OutputBox.Text = Path.Combine(desktop, $"wol-{IdBox.Text}.zip");
        IdBox.TextChanged += (_, _) =>
        {
            // Keep the default output filename in sync with the id field
            // unless the user manually edited it elsewhere.
            var current = OutputBox.Text;
            if (string.IsNullOrEmpty(current) ||
                Path.GetDirectoryName(current) == desktop &&
                Path.GetFileName(current).StartsWith("wol-", StringComparison.OrdinalIgnoreCase))
            {
                OutputBox.Text = Path.Combine(desktop, $"wol-{IdBox.Text}.zip");
            }
        };
    }

    private void ApplyLanguage()
    {
        Title = Strings.Get("DlgPackagerTitle");
        HeaderText.Text = Strings.Get("DlgPackagerHeader");
        DescriptionText.Text = Strings.Get("DlgPackagerDescription");

        LblId.Text = Strings.Get("DlgPackagerFieldId");
        HintId.Text = Strings.Get("DlgPackagerHintId");
        LblName.Text = Strings.Get("DlgPackagerFieldName");
        LblAuthor.Text = Strings.Get("DlgPackagerFieldAuthor");
        LblVersion.Text = Strings.Get("DlgPackagerFieldVersion");
        HintVersion.Text = Strings.Get("DlgPackagerHintVersion");
        LblFolder.Text = Strings.Get("DlgPackagerFieldFolder");
        HintFolder.Text = Strings.Get("DlgPackagerHintFolder");
        LblOriginals.Text = Strings.Get("DlgPackagerFieldOriginals");
        HintOriginals.Text = Strings.Get("DlgPackagerHintOriginals");
        BrowseOriginalsButton.Content = Strings.Get("ChangePathButton");
        LblCompat.Text = Strings.Get("DlgPackagerFieldCompat");
        CompatCurrentCheck.Content = Strings.Format("DlgPackagerCompatCurrent", _currentModVersion);
        HintCompatExtra.Text = Strings.Get("DlgPackagerHintCompatExtra");
        LblOutput.Text = Strings.Get("DlgPackagerFieldOutput");
        BrowseFolderButton.Content = Strings.Get("ChangePathButton");
        BrowseOutputButton.Content = Strings.Get("ChangePathButton");
        CancelButton.Content = Strings.Get("BtnCancel");
        GenerateButton.Content = Strings.Get("DlgPackagerBtnGenerate");

        // Result-panel labels
        LblJsonSnippet.Text = Strings.Get("DlgPackagerResultJsonLabel");
        OpenFolderButton.Content = Strings.Get("DlgPackagerBtnOpenFolder");
        CopyJsonButton.Content = Strings.Get("DlgPackagerBtnCopyJson");
        DoneButton.Content = Strings.Get("DlgPackagerBtnDone");
    }

    private void BrowseFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Strings.Get("DlgPackagerHintFolder"),
            Multiselect = false,
        };
        if (Directory.Exists(FolderBox.Text)) picker.InitialDirectory = FolderBox.Text;
        if (picker.ShowDialog(this) == true)
            FolderBox.Text = picker.FolderName;
    }

    private void BrowseOriginalsButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Strings.Get("DlgPackagerHintOriginals"),
            Multiselect = false,
        };
        if (Directory.Exists(OriginalsBox.Text)) picker.InitialDirectory = OriginalsBox.Text;
        else if (Directory.Exists(FolderBox.Text)) picker.InitialDirectory = FolderBox.Text;
        if (picker.ShowDialog(this) == true)
            OriginalsBox.Text = picker.FolderName;
    }

    private void BrowseOutputButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.SaveFileDialog
        {
            Title = Strings.Get("DlgPackagerFieldOutput"),
            Filter = "ZIP archive (*.zip)|*.zip",
            FileName = string.IsNullOrWhiteSpace(OutputBox.Text)
                ? $"wol-{IdBox.Text}.zip"
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
            // Re-enable the button so a second click proceeds despite the warning.
            return;
        }
        if (!Directory.Exists(FolderBox.Text))
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
                TranslatedFolder: FolderBox.Text.Trim(),
                OutputZipPath: OutputBox.Text.Trim(),
                Description: null,
                OriginalsFolder: string.IsNullOrWhiteSpace(OriginalsBox.Text)
                    ? null
                    : OriginalsBox.Text.Trim());

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

            ResultInstructionsText.Text = Strings.Get("DlgPackagerResultInstructions");
            JsonSnippetBox.Text = result.IndexJsonSnippet;

            // Build a "this is how players will see it" preview that mirrors
            // BuildLanguageMenuItem in MainWindow.xaml.cs — gives the translator
            // a sanity check (catches "wait, that says v1.2.0c2 not v1.0").
            LblPreview.Text = Strings.Get("DlgPackagerResultPreviewLabel");
            PreviewText.Text = BuildMenuPreview(inputs);

            FormPanel.Visibility = Visibility.Collapsed;
            ResultPanel.Visibility = Visibility.Visible;
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

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void DoneButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;

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

    private void CopyJsonButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(JsonSnippetBox.Text);
            CopyJsonButton.Content = Strings.Get("DlgPackagerBtnCopied");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Packager: clipboard copy failed: {ex.Message}");
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
