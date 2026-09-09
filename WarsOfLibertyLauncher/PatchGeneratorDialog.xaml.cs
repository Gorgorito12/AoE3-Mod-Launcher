using System;
using System.IO;
using System.Windows;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Modder-facing incremental delta-patch generator. Takes the previous release's overlay
/// <c>.zip</c> and the new one, diffs them by SHA-256 (<see cref="DeltaPatchService.GeneratePatchAsync"/>),
/// and writes a small <c>patch-&lt;from&gt;-to-&lt;to&gt;.zip</c> + <c>.json</c> the modder uploads
/// to their new GitHub release (alongside the full zip). Mod-agnostic — it operates purely on the
/// two zips the modder supplies. Launched from Launcher Settings → Packager.
/// </summary>
public partial class PatchGeneratorDialog : Window
{
    public PatchGeneratorDialog()
    {
        InitializeComponent();

        try
        {
            OutputBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }
        catch { /* leave empty; the user can browse */ }

        ApplyLanguage();
    }

    private void ApplyLanguage()
    {
        TitleBarControl.Title = Strings.Get("DlgPatchGenTitle");
        HeaderText.Text = Strings.Get("DlgPatchGenHeader");
        DescriptionText.Text = Strings.Get("DlgPatchGenDescription");

        SectionSourcesHeader.Text = Strings.Get("DlgPatchGenSectionSources");
        LblOldZip.Text = Strings.Get("DlgPatchGenOldZip");
        HintOldZip.Text = Strings.Get("DlgPatchGenOldZipHint");
        LblNewZip.Text = Strings.Get("DlgPatchGenNewZip");
        HintNewZip.Text = Strings.Get("DlgPatchGenNewZipHint");

        SectionVersionsHeader.Text = Strings.Get("DlgPatchGenSectionVersions");
        LblFromTag.Text = Strings.Get("DlgPatchGenFromTag");
        LblToTag.Text = Strings.Get("DlgPatchGenToTag");
        HintVersions.Text = Strings.Get("DlgPatchGenVersionsHint");

        SectionBaselineHeader.Text = Strings.Get("DlgPatchGenSectionBaseline");
        LblBaselineZip.Text = Strings.Get("DlgPatchGenBaselineZip");
        LblBaselineTag.Text = Strings.Get("DlgPatchGenBaselineTag");
        HintBaseline.Text = Strings.Get("DlgPatchGenBaselineHint");

        SectionOutputHeader.Text = Strings.Get("DlgPatchGenSectionOutput");
        LblOutput.Text = Strings.Get("DlgPatchGenOutputFolder");

        BrowseOldBtn.Content = Strings.Get("DlgPatchGenBrowse");
        BrowseNewBtn.Content = Strings.Get("DlgPatchGenBrowse");
        BrowseBaselineBtn.Content = Strings.Get("DlgPatchGenBrowse");
        BrowseOutBtn.Content = Strings.Get("DlgPatchGenBrowse");
        CloseBtn.Content = Strings.Get("DlgPatchGenClose");
        GenerateBtn.Content = Strings.Get("DlgPatchGenGenerate");
    }

    private void BrowseOldBtn_Click(object sender, RoutedEventArgs e) => PickZip(OldZipBox);
    private void BrowseNewBtn_Click(object sender, RoutedEventArgs e) => PickZip(NewZipBox);
    private void BrowseBaselineBtn_Click(object sender, RoutedEventArgs e) => PickZip(BaselineZipBox);

    private void PickZip(System.Windows.Controls.TextBox target)
    {
        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "ZIP archives (*.zip)|*.zip|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (picker.ShowDialog(this) == true)
            target.Text = picker.FileName;
    }

    private void BrowseOutBtn_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Strings.Get("DlgPatchGenOutputFolder"),
        };
        if (!string.IsNullOrWhiteSpace(OutputBox.Text) && Directory.Exists(OutputBox.Text))
            picker.InitialDirectory = OutputBox.Text;
        if (picker.ShowDialog(this) == true)
            OutputBox.Text = picker.FolderName;
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

    private async void GenerateBtn_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Collapsed;

        RebaselineText.Visibility = Visibility.Collapsed;

        var oldZip = OldZipBox.Text?.Trim() ?? "";
        var newZip = NewZipBox.Text?.Trim() ?? "";
        var fromTag = FromTagBox.Text?.Trim() ?? "";
        var toTag = ToTagBox.Text?.Trim() ?? "";
        var outDir = OutputBox.Text?.Trim() ?? "";
        var baseZip = BaselineZipBox.Text?.Trim() ?? "";
        var baseTag = BaselineTagBox.Text?.Trim() ?? "";

        if (!File.Exists(oldZip) || !File.Exists(newZip)
            || fromTag.Length == 0 || toTag.Length == 0 || outDir.Length == 0)
        {
            ShowError(Strings.Get("DlgPatchGenNeedInputs"));
            return;
        }

        GenerateBtn.IsEnabled = false;
        var prevContent = GenerateBtn.Content;
        GenerateBtn.Content = Strings.Get("DlgPatchGenWorking");
        try
        {
            // The INCREMENTAL patch (previous release -> new): smallest download for anyone who
            // updates every version.
            var result = await DeltaPatchService.GeneratePatchAsync(oldZip, newZip, fromTag, toTag, outDir);

            // The CUMULATIVE patch (baseline -> new), when a baseline was supplied. Optional but
            // recommended: it is what keeps a FRESH install at two downloads no matter how many
            // releases have gone by, and its deleted[] comes from a single diff so it carries no
            // ordering hazard at all. Skipped when it would duplicate the incremental.
            DeltaPatchService.GenerateResult? cumulative = null;
            bool wantCumulative = File.Exists(baseZip) && baseTag.Length > 0
                && !string.Equals(baseTag, fromTag, StringComparison.OrdinalIgnoreCase);
            if (wantCumulative)
                cumulative = await DeltaPatchService.GeneratePatchAsync(
                    baseZip, newZip, baseTag, toTag, outDir);

            ResultText.Text = cumulative == null
                ? Strings.Format("DlgPatchGenResult",
                    result.ChangedCount, result.DeletedCount, FormatSize(result.PatchZipSize))
                : Strings.Format("DlgPatchGenResultBoth",
                    FormatSize(result.PatchZipSize), FormatSize(cumulative.PatchZipSize));
            ReminderText.Text = Strings.Get(cumulative == null
                ? "DlgPatchGenReminder" : "DlgPatchGenReminderBoth");
            ResultPanel.Visibility = Visibility.Visible;

            // Advice, not a rule: once the cumulative approaches the size of the mod itself it has
            // stopped saving anybody anything, and the next release should carry the full .zip
            // again. Judged on the cumulative when there is one — the incremental stays small
            // forever and would never trigger it.
            var judged = cumulative ?? result;
            if (judged.AdviseRebaseline && judged.NewFullZipSize > 0)
            {
                RebaselineText.Text = Strings.Format("DlgPatchGenRebaseline",
                    FormatSize(judged.PatchZipSize), FormatSize(judged.NewFullZipSize));
                RebaselineText.Visibility = Visibility.Visible;
            }

            // Reveal the two produced files in Explorer for a quick drag-to-release.
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{result.PatchZipPath}\"",
                    UseShellExecute = true,
                });
            }
            catch { /* best-effort reveal */ }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Patch generation failed: {ex}");
            ShowError(Strings.Get("DlgPatchGenErrorPrefix") + " " + ex.Message);
        }
        finally
        {
            GenerateBtn.Content = prevContent;
            GenerateBtn.IsEnabled = true;
        }
    }

    private void ShowError(string msg)
    {
        ErrorText.Text = msg;
        ErrorText.Visibility = Visibility.Visible;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):0.0} GB";
        if (bytes >= 1024L * 1024) return $"{bytes / (1024.0 * 1024):0.0} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:0.0} KB";
        return $"{bytes} B";
    }
}
