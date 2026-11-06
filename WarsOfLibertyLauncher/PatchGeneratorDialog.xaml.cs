using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Modder-facing delta-patch generator (design 101). Diffs overlay <c>.zip</c>s by SHA-256
/// (<see cref="DeltaPatchService.GeneratePatchAsync"/>) and writes the small
/// <c>patch-&lt;from&gt;-to-&lt;to&gt;.zip</c> + <c>.json</c> that go on the modder's NEW GitHub
/// release: the INCREMENTAL (previous → new) and, when a baseline is supplied, the CUMULATIVE
/// (baseline → new). Mod-agnostic — it operates purely on the zips the modder supplies.
///
/// <para><b>The zips picked here are read for comparison only; they are not what gets uploaded.</b>
/// The full overlay is needed on a release only when that release is itself a baseline.</para>
///
/// <para>Launched from Launcher Settings → ADVANCED → DEVELOPER → "Incremental patch generator".
/// The Packager button beside it is the TRANSLATION packager, a different tool.</para>
/// </summary>
public partial class PatchGeneratorDialog : Window
{
    private const string MissingMark = "missing";

    private readonly Models.LauncherConfig? _config;
    private bool _howOpen;
    private bool _working;
    private bool _finished;
    private CancellationTokenSource? _run;
    private string? _revealPath;

    public PatchGeneratorDialog() : this(null) { }

    /// <param name="config">Remembers whether the explainer was left open.</param>
    /// <param name="previewVariant"><c>--preview-dialog=patchgen:…</c>: paint that state, generate nothing.</param>
    public PatchGeneratorDialog(Models.LauncherConfig? config, string? previewVariant = null)
    {
        InitializeComponent();
        ModDialogChrome.AttachGlobal(this);
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
        UpdateFooter();

        OldZipBox.TextChanged += (_, _) => ZipsChanged();
        NewZipBox.TextChanged += (_, _) => ZipsChanged();
        BaselineZipBox.TextChanged += (_, _) => ZipsChanged();
        FromTagBox.TextChanged += (_, _) => TagsChanged();
        ToTagBox.TextChanged += (_, _) => TagsChanged();
        BaselineTagBox.TextChanged += (_, _) => TagsChanged();

        Closing += OnClosing;

        // The tag of the release being published is the one field a modder always types.
        Loaded += (_, _) => ToTagBox.Focus();
        if (previewVariant != null) ShowPreview(previewVariant);
    }

    private void ApplyLanguage()
    {
        TitleBarControl.Title = Strings.Get("DlgPatchGenTitle");
        Title = Strings.Get("DlgPatchGenTitle");
        HeaderText.Text = Strings.Get("DlgPatchGenHeader");
        DescriptionText.Text = Strings.Get("DlgPatchGenDescription");
        ModDialogChrome.SetLineHeight(1.25, HeaderText);
        ModDialogChrome.SetLineHeight(1.55, DescriptionText);

        HowTitleText.Text = Strings.Get("DlgPatchGenHowTitle").ToUpper(Strings.Culture);
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
        ErrorTitle.Text = Strings.Get("DlgPatchGenErrorPrefix");

        BrowseOldBtn.Content = Strings.Get("DlgPatchGenBrowse");
        BrowseNewBtn.Content = Strings.Get("DlgPatchGenBrowse");
        BrowseBaselineBtn.Content = Strings.Get("DlgPatchGenBrowse");
        BrowseOutBtn.Content = Strings.Get("DlgPatchGenBrowse");
        ShowInFolderBtn.Content = Strings.Get("DlgPatchGenShowInFolder");
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
    /// Fill the diagram's three nodes from the tags the modder has actually typed. An empty field
    /// leaves its node blank rather than inventing a version: a plausible invented tag in a
    /// diagram is the one thing a reader would take at face value.
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
        DiagIncrementalText.Text = Strings.Get("DlgPatchGenDiagIncremental");
        DiagCumulativeText.Text = Strings.Get("DlgPatchGenDiagCumulative");
    }

    /// <summary>The three short lines under the diagram: what each patch is for, and who picks.</summary>
    private void BuildHowBullets()
    {
        HowBullets.Children.Clear();
        AddBullet("WindowIncremental", "DlgPatchGenBulletIncremental");
        AddBullet("WindowCumulative", "DlgPatchGenBulletCumulative");
        AddBullet("WindowFineText", "DlgPatchGenBulletRoute");
    }

    private void AddBullet(string dotBrushKey, string textKey)
    {
        var grid = new Grid { Margin = new Thickness(0, HowBullets.Children.Count == 0 ? 0 : 6, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 5,
            Height = 5,
            Margin = new Thickness(0, 7, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, dotBrushKey);
        var text = new TextBlock { Text = Strings.Get(textKey), TextWrapping = TextWrapping.Wrap };
        text.SetResourceReference(TextBlock.FontSizeProperty, "SetRowDescSize");
        text.SetResourceReference(TextBlock.ForegroundProperty, "WindowSoftText");
        Grid.SetColumn(text, 1);
        grid.Children.Add(dot);
        grid.Children.Add(text);
        HowBullets.Children.Add(grid);
    }

    private void ZipsChanged()
    {
        UpdatePlaceholders();
        UpdateFooter();
    }

    /// <summary>Everything downstream of a tag: the filenames, and the diagram's nodes.</summary>
    private void TagsChanged()
    {
        if (FromTagBox.Text.Length > 0) FromTagShell.ClearValue(TagProperty);
        if (ToTagBox.Text.Length > 0) ToTagShell.ClearValue(TagProperty);
        UpdatePlaceholders();
        UpdateOutputPreview();
        UpdateDiagram();
        UpdateFooter();
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
        static Visibility V(TextBox b) => string.IsNullOrEmpty(b.Text) ? Visibility.Visible : Visibility.Collapsed;

        OldZipPlaceholder.Visibility = V(OldZipBox);
        NewZipPlaceholder.Visibility = V(NewZipBox);
        BaselineZipPlaceholder.Visibility = V(BaselineZipBox);
        FromTagPlaceholder.Visibility = V(FromTagBox);
        ToTagPlaceholder.Visibility = V(ToTagBox);
        BaselineTagPlaceholder.Visibility = V(BaselineTagBox);

        // Optional, so it sits back until somebody starts on it.
        BaseCard.Opacity = BaselineZipBox.Text.Length > 0 || BaselineTagBox.Text.Length > 0 ? 1 : 0.78;
    }

    /// <summary>
    /// Live preview of the files this run will write, and the release they belong on. The names
    /// come from <see cref="DeltaPatchService.PatchAssetNaming.StemFor"/>, the same rule the
    /// generator writes with, so the screen and the disk cannot name different things.
    /// </summary>
    private void UpdateOutputPreview()
    {
        var from = FromTagBox.Text?.Trim() ?? "";
        var to = ToTagBox.Text?.Trim() ?? "";
        var baseTag = BaselineTagBox.Text?.Trim() ?? "";

        PreviewRows.Children.Clear();

        // Nothing can be named until both ends of the hop are known.
        bool haveHop = from.Length > 0 && to.Length > 0;
        PreviewTarget.Text = haveHop ? Strings.Format("DlgPatchGenPreviewTarget", to) : "";

        if (haveHop)
        {
            var stem = DeltaPatchService.PatchAssetNaming.StemFor(from, to);
            AddFileRow(PreviewRows, stem + ".zip", Strings.Get("DlgPatchGenPreviewIncremental"), "WindowIncremental");
            AddFileRow(PreviewRows, stem + ".json", Strings.Get("DlgPatchGenPreviewIndex"), "WindowFineText");
        }

        // The cumulative exists only under the same condition the generator applies:
        // a baseline that is filled in AND is not simply the previous release.
        bool cumulative = haveHop && baseTag.Length > 0
            && !string.Equals(baseTag, from, StringComparison.OrdinalIgnoreCase);
        if (cumulative)
        {
            var stem = DeltaPatchService.PatchAssetNaming.StemFor(baseTag, to);
            AddFileRow(PreviewRows, stem + ".zip", Strings.Get("DlgPatchGenPreviewCumulative"), "WindowCumulative");
            AddFileRow(PreviewRows, stem + ".json", Strings.Get("DlgPatchGenPreviewIndex"), "WindowFineText");
        }

        PreviewCumulativeHint.Visibility = cumulative ? Visibility.Collapsed : Visibility.Visible;
        PreviewCumulativeText.Text = Strings.Get(haveHop
            ? "DlgPatchGenPreviewCumulativeHint"
            : "DlgPatchGenPreviewNeedTags");
    }

    /// <summary>One file on a dark row: its name in mono, trimmed, and what it is or weighs.</summary>
    private static void AddFileRow(Panel target, string fileName, string detail, string detailBrushKey, bool detailIsMono = false)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var name = new TextBlock
        {
            Text = fileName,
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };
        name.SetResourceReference(TextBlock.FontSizeProperty, "SetRowDescSize");
        name.SetResourceReference(TextBlock.ForegroundProperty, "WindowStrongText");

        var tag = new TextBlock { Text = detail, VerticalAlignment = VerticalAlignment.Center };
        if (detailIsMono) tag.FontFamily = name.FontFamily;
        tag.SetResourceReference(TextBlock.FontSizeProperty, detailIsMono ? "SetControlSize" : "SetDescSize");
        tag.SetResourceReference(TextBlock.ForegroundProperty, detailBrushKey);
        Grid.SetColumn(tag, 1);
        grid.Children.Add(name);
        grid.Children.Add(tag);

        target.Children.Add(new Border
        {
            Height = 32,
            Background = new SolidColorBrush(Color.FromArgb(0x66, 0x05, 0x0A, 0x12)),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(11, 0, 11, 0),
            Margin = new Thickness(0, target.Children.Count == 0 ? 8 : 5, 0, 0),
            Child = grid,
        });
    }

    private void BrowseOldBtn_Click(object sender, RoutedEventArgs e) => PickZip(OldZipBox);
    private void BrowseNewBtn_Click(object sender, RoutedEventArgs e) => PickZip(NewZipBox);
    private void BrowseBaselineBtn_Click(object sender, RoutedEventArgs e) => PickZip(BaselineZipBox);

    private void PickZip(TextBox target)
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

    /// <summary>"Close" at rest, "Cancel" while a run is going.</summary>
    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_working) _run?.Cancel();
        else Close();
    }

    private void OnClosing(object? sender, CancelEventArgs e) => _run?.Cancel();

    /// <summary>The footer's sentence and whether the button can be pressed. A tag can be missing
    /// with the button alive: pressing it is what marks the empty one.</summary>
    private void UpdateFooter()
    {
        if (_working)
        {
            FooterNote.Text = Strings.Get("DlgPatchGenFootWorking");
            return;
        }
        if (_finished)
        {
            FooterNote.Text = Strings.Get("DlgPatchGenFootDone");
            return;
        }
        var haveZips = OldZipBox.Text.Trim().Length > 0 && NewZipBox.Text.Trim().Length > 0;
        var haveTags = FromTagBox.Text.Trim().Length > 0 && ToTagBox.Text.Trim().Length > 0;
        GenerateBtn.IsEnabled = haveZips;
        FooterNote.Text = Strings.Get(haveZips && haveTags ? "DlgPatchGenFootReady" : "DlgPatchGenFootMissing");
    }

    /// <summary>The amber rim, only once somebody tried to generate without the tag.</summary>
    internal bool MarkMissingTags()
    {
        var fromMissing = FromTagBox.Text.Trim().Length == 0;
        var toMissing = ToTagBox.Text.Trim().Length == 0;
        if (fromMissing) FromTagShell.Tag = MissingMark;
        if (toMissing) ToTagShell.Tag = MissingMark;
        return fromMissing || toMissing;
    }

    /// <summary>While a run goes the form stays on screen and the footer offers only Cancel.</summary>
    internal void SetWorking(bool working)
    {
        _working = working;
        WorkCard.Visibility = working ? Visibility.Visible : Visibility.Collapsed;
        TagWarning.Visibility = working || _finished ? Visibility.Collapsed : Visibility.Visible;
        GenerateBtn.Visibility = working ? Visibility.Collapsed : Visibility.Visible;
        CloseBtn.Content = Strings.Get(working ? "DlgPatchGenCancel" : "DlgPatchGenClose");
        if (working)
        {
            ErrorBox.Visibility = Visibility.Collapsed;
            ResultPanel.Visibility = Visibility.Collapsed;
            BodyScroll.ScrollToTop();
        }
        UpdateFooter();
    }

    internal void ShowWork(string title, string count, double fraction, string detail)
    {
        WorkTitle.Text = title;
        WorkCount.Text = count;
        WorkBar.Value = fraction;
        WorkSub.Text = detail;
    }

    /// <summary>The form untouched, the reason in plain words, the exception underneath.</summary>
    internal void ShowFailure(Exception ex)
    {
        ErrorBody.Text = Strings.Get(ex is InvalidDataException or IOException ? "DlgPatchGenFailZip" : "DlgPatchGenFailOther");
        ErrorDetail.Text = $"{ex.GetType().Name}: {ex.Message}";
        ErrorDetail.Visibility = Visibility.Visible;
        ErrorBox.Visibility = Visibility.Visible;
        BringIntoViewOnceLaidOut(ErrorBox);
    }

    private void ShowInputsProblem()
    {
        ErrorBody.Text = Strings.Get("DlgPatchGenNeedInputs");
        ErrorDetail.Visibility = Visibility.Collapsed;
        ErrorBox.Visibility = Visibility.Visible;
        BringIntoViewOnceLaidOut(ErrorBox);
    }

    internal void ShowDone(bool two, string toTag, IEnumerable<(string Name, long Bytes)> files,
        long judgedBytes = 0, long fullBytes = 0)
    {
        _finished = true;
        SetWorking(false);
        ResultText.Text = Strings.Get(two ? "DlgPatchGenResultBoth" : "DlgPatchGenResult");
        ReminderText.Inlines.Clear();
        ModDialogChrome.AddMarkedText(ReminderText, Strings.Format("DlgPatchGenUploadTo", toTag));
        ResultFiles.Children.Clear();
        foreach (var (name, bytes) in files)
            AddFileRow(ResultFiles, name, DiskSpaceService.FormatBytes(bytes), "WindowMuted", detailIsMono: true);

        // Advice, not a rule: once the patch approaches the size of the mod itself it has stopped
        // saving anybody anything, and the next release should carry the full .zip again.
        RebaselineText.Inlines.Clear();
        if (fullBytes > 0)
            ModDialogChrome.AddMarkedText(RebaselineText, Strings.Format("DlgPatchGenRebaseline",
                DiskSpaceService.FormatBytes(judgedBytes), DiskSpaceService.FormatBytes(fullBytes)));
        RebaselinePanel.Visibility = fullBytes > 0 ? Visibility.Visible : Visibility.Collapsed;

        ResultPanel.Visibility = Visibility.Visible;
        GenerateBtn.Content = Strings.Get("DlgPatchGenAnother");
        GenerateBtn.IsEnabled = true;
        UpdateFooter();
        BringIntoViewOnceLaidOut(ResultPanel);
    }

    private void BringIntoViewOnceLaidOut(FrameworkElement element)
        => Dispatcher.BeginInvoke(new Action(() => element.BringIntoView()), System.Windows.Threading.DispatcherPriority.Loaded);

    private void BackToTheForm()
    {
        _finished = false;
        _revealPath = null;
        ResultPanel.Visibility = Visibility.Collapsed;
        ErrorBox.Visibility = Visibility.Collapsed;
        TagWarning.Visibility = Visibility.Visible;
        GenerateBtn.Content = Strings.Get("DlgPatchGenGenerate");
        UpdateFooter();
    }

    private async void GenerateBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_finished)
        {
            BackToTheForm();
            return;
        }
        ErrorBox.Visibility = Visibility.Collapsed;
        if (MarkMissingTags()) return;

        var oldZip = OldZipBox.Text.Trim();
        var newZip = NewZipBox.Text.Trim();
        var fromTag = FromTagBox.Text.Trim();
        var toTag = ToTagBox.Text.Trim();
        var outDir = OutputBox.Text.Trim();
        var baseZip = BaselineZipBox.Text.Trim();
        var baseTag = BaselineTagBox.Text.Trim();

        if (!File.Exists(oldZip) || !File.Exists(newZip) || outDir.Length == 0)
        {
            ShowInputsProblem();
            return;
        }

        // The CUMULATIVE patch (baseline -> new), when a baseline was supplied: it keeps a FRESH
        // install at two downloads however many releases go by, and its deleted[] comes from a
        // single diff so it carries no ordering hazard. Skipped when it would duplicate the
        // incremental.
        var wantCumulative = File.Exists(baseZip) && baseTag.Length > 0
            && !string.Equals(baseTag, fromTag, StringComparison.OrdinalIgnoreCase);
        var patches = wantCumulative ? 2 : 1;

        using var run = new CancellationTokenSource();
        _run = run;
        var written = new List<string>();
        SetWorking(true);
        try
        {
            ShowWork(Strings.Format("DlgPatchGenComparing", Path.GetFileName(oldZip)), "", 0,
                Strings.Get("DlgPatchGenSubCompare"));

            var incremental = await DeltaPatchService.GeneratePatchAsync(
                oldZip, newZip, fromTag, toTag, outDir, run.Token,
                new Progress<DeltaPatchService.PatchGenerationProgress>(p => Report(
                    p, 0, patches, oldZip, newZip, Strings.Get("DlgPatchGenWritingIncremental"),
                    Strings.Format("DlgPatchGenSubIncremental", fromTag, toTag))));
            written.Add(incremental.PatchZipPath);
            written.Add(incremental.PatchJsonPath);

            DeltaPatchService.GenerateResult? cumulative = null;
            if (wantCumulative)
            {
                cumulative = await DeltaPatchService.GeneratePatchAsync(
                    baseZip, newZip, baseTag, toTag, outDir, run.Token,
                    new Progress<DeltaPatchService.PatchGenerationProgress>(p => Report(
                        p, 1, patches, baseZip, newZip, Strings.Get("DlgPatchGenWritingCumulative"),
                        Strings.Format("DlgPatchGenSubCumulative", baseTag))));
                written.Add(cumulative.PatchZipPath);
                written.Add(cumulative.PatchJsonPath);
            }

            var files = written.Select(path => (Path.GetFileName(path), new FileInfo(path).Length)).ToList();
            // Judged on the cumulative when there is one: the incremental stays small for ever.
            var judged = cumulative ?? incremental;
            var advise = judged.AdviseRebaseline && judged.NewFullZipSize > 0;
            _revealPath = incremental.PatchZipPath;
            ShowDone(cumulative != null, toTag, files,
                advise ? judged.PatchZipSize : 0, advise ? judged.NewFullZipSize : 0);
            RevealInFolder();
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested)
        {
            DiagnosticLog.Write("Patch generation cancelled.");
            RemoveFinishedParts(written);
            SetWorking(false);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Patch generation failed: {ex}");
            SetWorking(false);
            ShowFailure(ex);
        }
        finally
        {
            _run = null;
        }
    }

    /// <summary>A cancel means none of this run: a patch that finished before it goes as well.</summary>
    private static void RemoveFinishedParts(IEnumerable<string> written)
    {
        foreach (var path in written)
        {
            try { File.Delete(path); }
            catch (Exception ex) { DiagnosticLog.Write($"Patch generation: could not remove {path}: {ex.Message}"); }
        }
    }

    private void Report(DeltaPatchService.PatchGenerationProgress p, int patchIndex, int patches,
        string oldZip, string newZip, string writingTitle, string writingDetail)
    {
        var overall = p.Overall(patchIndex, patches);
        if (p.Phase == DeltaPatchService.PatchGenerationPhase.Writing)
        {
            ShowWork(writingTitle, $"{Math.Round(overall * 100)} %", overall, writingDetail);
            return;
        }
        var zip = p.Phase == DeltaPatchService.PatchGenerationPhase.ComparingOld ? oldZip : newZip;
        ShowWork(Strings.Format("DlgPatchGenComparing", Path.GetFileName(zip)),
            Strings.Format("DlgPatchGenFiles", p.Done, p.Total), overall, Strings.Get("DlgPatchGenSubCompare"));
    }

    private void ShowInFolderBtn_Click(object sender, RoutedEventArgs e) => RevealInFolder();

    private void RevealInFolder()
    {
        if (_revealPath == null) return;
        try
        {
            HarnessMode.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{_revealPath}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex) { DiagnosticLog.Write($"Patch generation: could not open the folder: {ex.Message}"); }
    }

    private void ShowPreview(string variant)
    {
        void Fill(bool withBase)
        {
            OldZipBox.Text = @"D:\WoL\releases\WarsOfLiberty-1.6.2.zip";
            FromTagBox.Text = "v1.6.2";
            NewZipBox.Text = @"D:\WoL\releases\WarsOfLiberty-1.6.3.zip";
            ToTagBox.Text = "v1.6.3";
            if (!withBase) return;
            BaselineZipBox.Text = @"D:\WoL\releases\WarsOfLiberty-1.6.0.zip";
            BaselineTagBox.Text = "v1.6.0";
        }

        switch (variant.ToLowerInvariant())
        {
            case "empty":
                break;
            case "how":
                Fill(true);
                _howOpen = true;
                ApplyHowVisibility();
                break;
            case "working":
                Fill(true);
                SetWorking(true);
                ShowWork(Strings.Get("DlgPatchGenWritingIncremental"), "62 %", 0.62,
                    Strings.Format("DlgPatchGenSubIncremental", "v1.6.2", "v1.6.3"));
                break;
            case "error":
                Fill(false);
                ShowFailure(new InvalidDataException("End of Central Directory record could not be found. (WarsOfLiberty-1.6.3.zip)"));
                break;
            case "done":
                Fill(true);
                const long mb = 1024 * 1024;
                ShowDone(true, "v1.6.3", new (string, long)[]
                {
                    ("patch-v1.6.2-to-v1.6.3.zip", 38 * mb),
                    ("patch-v1.6.2-to-v1.6.3.json", 12 * 1024),
                    ("patch-v1.6.0-to-v1.6.3.zip", 182 * mb),
                    ("patch-v1.6.0-to-v1.6.3.json", 41 * 1024),
                }, 182 * mb, 214 * mb);
                break;
            default:
                Fill(true);
                break;
        }
    }
}
