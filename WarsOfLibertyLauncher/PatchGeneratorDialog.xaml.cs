using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Modder-facing delta-patch generator. Diffs overlay <c>.zip</c>s by SHA-256
/// (<see cref="DeltaPatchService.GeneratePatchAsync"/>) and writes the small
/// <c>patch-&lt;from&gt;-to-&lt;to&gt;.zip</c> + <c>.json</c> that go on the modder's NEW GitHub
/// release: the INCREMENTAL (previous → new) and, when a baseline is supplied, the CUMULATIVE
/// (baseline → new). Mod-agnostic — it operates purely on the zips the modder supplies.
///
/// <para><b>The zips picked here are read for comparison only; they are not what gets uploaded.</b>
/// The full overlay is needed on a release only when that release is itself a baseline. This
/// bullet is spelled out because the dialog's own copy used to say the opposite ("you still upload
/// this one too") — the pre-patch-only model — and following it costs the modder the entire
/// bandwidth saving with nothing failing to tell them.</para>
///
/// <para>Launched from Launcher Settings → ADVANCED → DEVELOPER → "Incremental patch generator".
/// The Packager button beside it is the TRANSLATION packager, a different tool.</para>
/// </summary>
public partial class PatchGeneratorDialog : Window
{
    private readonly Models.LauncherConfig? _config;
    private bool _howOpen;

    public PatchGeneratorDialog() : this(null) { }

    public PatchGeneratorDialog(Models.LauncherConfig? config)
    {
        InitializeComponent();
        _config = config;
        _howOpen = config?.PatchGenHowToOpen ?? false;

        // The declared height is what the form needs to open without a scrollbar; on a screen
        // that cannot give it, take what there is instead of opening a window taller than the
        // display. Below that the ScrollViewer does its job, which is the correct outcome.
        try
        {
            var room = SystemParameters.WorkArea.Height * 0.9;
            if (room > MinHeight && Height > room) Height = room;
        }
        catch { /* no work area to consult; the declared height stands */ }

        try
        {
            OutputBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }
        catch { /* leave empty; the user can browse */ }

        ApplyLanguage();
        UpdatePlaceholders();
        UpdateOutputPreview();
        UpdateDiagram();

        OldZipBox.TextChanged += (_, _) => UpdatePlaceholders();
        NewZipBox.TextChanged += (_, _) => UpdatePlaceholders();
        BaselineZipBox.TextChanged += (_, _) => UpdatePlaceholders();
        FromTagBox.TextChanged += (_, _) => TagsChanged();
        ToTagBox.TextChanged += (_, _) => TagsChanged();
        BaselineTagBox.TextChanged += (_, _) => TagsChanged();

        // The tag of the release being published is the one field a modder always types.
        Loaded += (_, _) => ToTagBox.Focus();
    }

    private void ApplyLanguage()
    {
        TitleBarControl.Title = Strings.Get("DlgPatchGenTitle");
        HeaderText.Text = Strings.Get("DlgPatchGenHeader");
        DescriptionText.Text = Strings.Get("DlgPatchGenDescription");

        HowTitleText.Text = Strings.Get("DlgPatchGenHowTitle");
        HowBodyText.Text = Strings.Get("DlgPatchGenHowBody");
        HowHideButton.Content = Strings.Get("DlgPatchGenHowHide");

        // One card per release. The field labels are the SAME on all three — which
        // release they belong to is the card's title, not a qualifier on the label.
        var zipLabel = Strings.Get("DlgPatchGenFieldZip");
        var tagLabel = Strings.Get("DlgPatchGenFieldTag");
        PrevZipLabel.Text = NewZipLabel.Text = BaseZipLabel.Text = zipLabel;
        PrevTagLabel.Text = NewTagLabel.Text = BaseTagLabel.Text = tagLabel;

        CardPrevTitle.Text = Strings.Get("DlgPatchGenCardPrev");
        CardPrevNote.Text = Strings.Get("DlgPatchGenCardPrevNote");
        HintOldZip.Text = Strings.Get("DlgPatchGenOldZipHint");

        CardNewTitle.Text = Strings.Get("DlgPatchGenCardNew");
        CardNewNote.Text = Strings.Get("DlgPatchGenCardNewNote");
        HintNewZip.Text = Strings.Get("DlgPatchGenNewZipHint");

        CardBaseTitle.Text = Strings.Get("DlgPatchGenCardBase");
        CardBaseNote.Text = Strings.Get("DlgPatchGenCardBaseNote");
        HintBaseline.Text = Strings.Get("DlgPatchGenBaselineHint");

        var notChosen = Strings.Get("DlgPatchGenNotChosen");
        var notFilled = Strings.Get("DlgPatchGenNotFilled");
        OldZipPlaceholder.Text = NewZipPlaceholder.Text = BaselineZipPlaceholder.Text = notChosen;
        FromTagPlaceholder.Text = ToTagPlaceholder.Text = BaselineTagPlaceholder.Text = notFilled;

        PreviewHeader.Text = Strings.Get("DlgPatchGenPreviewHeader");
        TagWarnTitle.Text = Strings.Get("DlgPatchGenTagWarnTitle");
        TagWarnBody.Text = Strings.Get("DlgPatchGenTagWarnBody");
        LblOutput.Text = Strings.Get("DlgPatchGenOutputFolder");

        BrowseOldBtn.Content = Strings.Get("DlgPatchGenBrowse");
        BrowseNewBtn.Content = Strings.Get("DlgPatchGenBrowse");
        BrowseBaselineBtn.Content = Strings.Get("DlgPatchGenBrowse");
        BrowseOutBtn.Content = Strings.Get("DlgPatchGenBrowse");
        CloseBtn.Content = Strings.Get("DlgPatchGenClose");
        GenerateBtn.Content = Strings.Get("DlgPatchGenGenerate");
        ApplyHowVisibility();
        UpdateOutputPreview();
        BuildHowBullets();
        UpdateDiagram();
    }

    /// <summary>
    /// The explainer is born folded and stays as the modder leaves it. Folded, because it
    /// used to occupy ~250 px of the opening view, so what a maintainer saw on opening the
    /// window was the instruction rather than the six fields it explains.
    /// </summary>
    private void ApplyHowVisibility()
    {
        HowPanel.Visibility = _howOpen ? Visibility.Visible : Visibility.Collapsed;
        HowToggleButton.Content = Strings.Get("DlgPatchGenHowTitle");
        HowToggleButton.Visibility = _howOpen ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// Fill the diagram's three nodes from the tags the modder has actually typed.
    ///
    /// <para>The prototype draws <c>v1.2.0a / v1.2.0d / v1.2.0e</c>; those are illustrative.
    /// An empty field leaves its node blank rather than inventing a version, because a
    /// plausible invented tag in a diagram is worse than a gap — it is the one thing a reader
    /// would take at face value.</para>
    /// </summary>
    private void UpdateDiagram()
    {
        var dash = Strings.Get("DlgPatchGenDiagramEmpty");
        string Or(string s) => string.IsNullOrWhiteSpace(s) ? dash : s.Trim();

        DiagBaseTag.Text = Or(BaselineTagBox.Text ?? "");
        DiagPrevTag.Text = Or(FromTagBox.Text ?? "");
        DiagNewTag.Text = Or(ToTagBox.Text ?? "");

        DiagBaseRole.Text = Strings.Get("DlgPatchGenDiagramBase");
        DiagPrevRole.Text = Strings.Get("DlgPatchGenDiagramPrev");
        DiagNewRole.Text = Strings.Get("DlgPatchGenDiagramNew");
        DiagIncrementalText.Text = Strings.Get("DlgPatchGenPreviewIncremental");
        DiagCumulativeText.Text = Strings.Get("DlgPatchGenPreviewCumulative");
    }

    /// <summary>The three short lines under the diagram: what each patch is for, and who picks.</summary>
    private void BuildHowBullets()
    {
        HowBullets.Children.Clear();
        AddBullet("MpActionText", "DlgPatchGenBulletIncremental");
        AddBullet("MpPrivateText", "DlgPatchGenBulletCumulative");
        AddBullet("UiTextDim", "DlgPatchGenBulletRoute");
    }

    private void AddBullet(string dotBrushKey, string textKey)
    {
        var grid = new System.Windows.Controls.Grid { Margin = new Thickness(0, 0, 0, 7) };
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
        { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
        { Width = new GridLength(9) });
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
        { Width = new GridLength(1, GridUnitType.Star) });

        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 4,
            Height = 4,
            Margin = new Thickness(0, 6, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Fill = (System.Windows.Media.Brush)FindResource(dotBrushKey),
        };
        var text = new System.Windows.Controls.TextBlock
        {
            Text = Strings.Get(textKey),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 17,
            FontSize = (double)FindResource("SetDescSize"),
            Foreground = (System.Windows.Media.Brush)FindResource("MpTextBody"),
        };
        System.Windows.Controls.Grid.SetColumn(text, 2);
        grid.Children.Add(dot);
        grid.Children.Add(text);
        HowBullets.Children.Add(grid);
    }

    /// <summary>Everything downstream of a tag: the filenames, and the diagram's nodes.</summary>
    private void TagsChanged()
    {
        UpdatePlaceholders();
        UpdateOutputPreview();
        UpdateDiagram();
    }

    private void HowToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _howOpen = !_howOpen;
        if (_config != null)
        {
            _config.PatchGenHowToOpen = _howOpen;
            _config.Save();
        }
        ApplyHowVisibility();
    }

    /// <summary>
    /// A placeholder shows only while its box is empty. The boxes are chrome-less inside a
    /// shell that owns the inset, so the placeholder is a sibling with no margin of its own.
    /// </summary>
    private void UpdatePlaceholders()
    {
        static Visibility V(System.Windows.Controls.TextBox b) =>
            string.IsNullOrEmpty(b.Text) ? Visibility.Visible : Visibility.Collapsed;

        OldZipPlaceholder.Visibility = V(OldZipBox);
        NewZipPlaceholder.Visibility = V(NewZipBox);
        BaselineZipPlaceholder.Visibility = V(BaselineZipBox);
        FromTagPlaceholder.Visibility = V(FromTagBox);
        ToTagPlaceholder.Visibility = V(ToTagBox);
        BaselineTagPlaceholder.Visibility = V(BaselineTagBox);
    }

    /// <summary>
    /// Live preview of the files this run will write, and the release they belong on.
    ///
    /// <para><b>The names come from <see cref="DeltaPatchService.PatchAssetNaming.StemFor"/>,
    /// the same rule the generator writes with.</b> Deriving them here from a second copy
    /// would let the screen name one thing and the disk hold another, and the only way the
    /// modder would find out is by comparing a folder against a screen they had believed.</para>
    /// </summary>
    private void UpdateOutputPreview()
    {
        var from = FromTagBox.Text?.Trim() ?? "";
        var to = ToTagBox.Text?.Trim() ?? "";
        var baseTag = BaselineTagBox.Text?.Trim() ?? "";

        PreviewRows.Children.Clear();

        // Nothing can be named until both ends of the hop are known.
        bool haveHop = from.Length > 0 && to.Length > 0;
        PreviewTarget.Text = haveHop
            ? Strings.Format("DlgPatchGenPreviewTarget", to)
            : "";

        if (haveHop)
        {
            AddPreviewRow(DeltaPatchService.PatchAssetNaming.StemFor(from, to) + ".zip",
                          Strings.Get("DlgPatchGenPreviewIncremental"));
            AddPreviewRow(DeltaPatchService.PatchAssetNaming.StemFor(from, to) + ".json",
                          Strings.Get("DlgPatchGenPreviewIndex"));
        }

        // The cumulative exists only under the same condition the generator applies:
        // a baseline that is filled in AND is not simply the previous release.
        bool cumulative = haveHop && baseTag.Length > 0
            && !string.Equals(baseTag, from, StringComparison.OrdinalIgnoreCase);
        if (cumulative)
        {
            AddPreviewRow(DeltaPatchService.PatchAssetNaming.StemFor(baseTag, to) + ".zip",
                          Strings.Get("DlgPatchGenPreviewCumulative"));
            AddPreviewRow(DeltaPatchService.PatchAssetNaming.StemFor(baseTag, to) + ".json",
                          Strings.Get("DlgPatchGenPreviewIndex"));
        }

        PreviewCumulativeHint.Visibility = cumulative ? Visibility.Collapsed : Visibility.Visible;
        PreviewCumulativeText.Text = Strings.Get(haveHop
            ? "DlgPatchGenPreviewCumulativeHint"
            : "DlgPatchGenPreviewNeedTags");
    }

    private void AddPreviewRow(string fileName, string role)
    {
        var grid = new System.Windows.Controls.Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
        { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
        { Width = new GridLength(10) });
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
        { Width = GridLength.Auto });

        // A filename is data the modder copies, so it is monospaced and trims rather than
        // wrapping — the Grid is what lets the trim actually fire.
        var name = new System.Windows.Controls.TextBlock
        {
            Text = fileName,
            FontFamily = (System.Windows.Media.FontFamily)FindResource("MonoFont"),
            FontSize = (double)FindResource("SetMonoSize"),
            Foreground = (System.Windows.Media.Brush)FindResource("MpTextPrimary"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var tag = new System.Windows.Controls.TextBlock
        {
            Text = role,
            FontSize = (double)FindResource("SetGroupLabelSize"),
            Foreground = (System.Windows.Media.Brush)FindResource("UiTextDim"),
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        System.Windows.Controls.Grid.SetColumn(tag, 2);
        grid.Children.Add(name);
        grid.Children.Add(tag);

        var row = new System.Windows.Controls.Border
        {
            Background = (System.Windows.Media.Brush)FindResource("MpAppBg"),
            CornerRadius = (CornerRadius)FindResource("RadiusControl"),
            Padding = new Thickness(11, 8, 11, 8),
            Child = grid,
        };
        PreviewRows.Children.Add(row);
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
            Title = Strings.Get("DlgPatchGenPickOutput"),
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

        RebaselinePanel.Visibility = Visibility.Collapsed;

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

            // Name the files and the release they belong on. The old reminder said "upload BOTH
            // the patch .zip and .json" without naming either and never said WHICH release —
            // and a patch attached to the wrong one is ignored in silence, so the modder would
            // never learn they had lost the saving. Everything here is already in hand.
            var files = new List<string> { result.PatchZipPath, result.PatchJsonPath };
            if (cumulative != null)
            {
                files.Add(cumulative.PatchZipPath);
                files.Add(cumulative.PatchJsonPath);
            }
            var uploadList = string.Join(Environment.NewLine,
                files.Select(f => "    • " + Path.GetFileName(f)));
            ReminderText.Text =
                Strings.Format("DlgPatchGenUploadTo", toTag) + Environment.NewLine
                + uploadList + Environment.NewLine + Environment.NewLine
                + Strings.Get(cumulative == null ? "DlgPatchGenReminder" : "DlgPatchGenReminderBoth");
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
                RebaselinePanel.Visibility = Visibility.Visible;
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
