using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Styled dialog for choosing the Wars of Liberty install folder.
/// Shows AoE3 detection status, destination folder picker, and disk space.
/// This is the single dialog for the entire install flow — no additional
/// popups or MessageBoxes.
/// </summary>
public partial class InstallFolderDialog : Window
{
    /// <summary>The folder the user confirmed.</summary>
    public string SelectedFolder { get; private set; } = "";

    /// <summary>The detected AoE3 source path (for cloning), or null.</summary>
    public string? Aoe3SourcePath { get; private set; }

    /// <summary>
    /// The mod the player chose to copy graphics, sound and hotkeys from, or null for none.
    ///
    /// <para>An answer, not an action: this dialog never touches a profile. The caller records it
    /// and the copy happens when there is somewhere to put it — see
    /// <c>ModState.PendingSettingsImportFrom</c>.</para>
    /// </summary>
    public string? CopySettingsFromModId { get; private set; }

    public InstallFolderDialog(string defaultFolder)
        : this(defaultFolder, null, null, "Wars of Liberty") { }

    public InstallFolderDialog(string defaultFolder, string? aoe3Path, string? aoe3SourceLabel)
        : this(defaultFolder, aoe3Path, aoe3SourceLabel, "Wars of Liberty") { }

    private string? _aoe3SourceLabel;
    private readonly string _modDisplayName;
    private readonly bool _requiresAoe3Source = true;

    // Disk-space estimate state. _cloneBytes = -1 means "not measured yet"; it's
    // filled off-thread by measuring the AoE3 source we'd clone. _spaceWarning is
    // set by UpdateDiskSpace when free space is below the conservative estimate —
    // it drives the amber warning line and the confirm-to-proceed on OK.
    private long _cloneBytes = -1;
    private string? _measuredSource;
    private bool _spaceWarning;

    /// <param name="modDisplayName">
    /// Display name of the mod being installed (e.g. "Wars of Liberty",
    /// "Improvement Mod"). Templated into the dialog's title and the
    /// "&lt;mod&gt; will be installed in its own '&lt;mod&gt;' folder" copy so
    /// every mod sees its own name instead of WoL.
    /// </param>
    /// <param name="requiresAoe3Source">
    /// False for an IN-PLACE overlay: it adds its files to the AoE3 the player already has,
    /// so there is nothing to clone and the destination IS the game folder. Demanding a
    /// clone source there would block an install that needs none, so the whole AoE3 row is
    /// hidden and the OK button stops waiting for it. Every other install type clones and
    /// keeps the requirement — without a source the result is an unplayable mod-only folder.
    /// </param>
    /// <param name="settingsSources">
    /// Mods this one may copy graphics, sound and hotkeys from — already filtered by the caller,
    /// which owns the "is it installed" check. An empty list hides the whole row, which is what
    /// a first-ever install gets: a question nobody could answer is worse than no question.
    /// </param>
    public InstallFolderDialog(string defaultFolder, string? aoe3Path, string? aoe3SourceLabel,
        string modDisplayName, bool requiresAoe3Source = true,
        IReadOnlyList<ModProfile>? settingsSources = null)
    {
        InitializeComponent();
        Aoe3SourcePath = aoe3Path;
        _aoe3SourceLabel = aoe3SourceLabel;
        _requiresAoe3Source = requiresAoe3Source;
        _modDisplayName = string.IsNullOrEmpty(modDisplayName) ? "the mod" : modDisplayName;

        if (!requiresAoe3Source) Aoe3Row.Visibility = Visibility.Collapsed;

        ApplyLanguage();
        FolderTextBox.Text = defaultFolder;
        FolderTextBox.SelectAll();
        FolderTextBox.Focus();

        BuildCopySettingsRow(settingsSources);

        UpdateAoE3Display();
        UpdateDestDisplay();
        UpdateDiskSpace();
        UpdateFirstRunWarning();
    }

    /// <summary>
    /// Fill the "copy my settings from" row, or hide it when there is nothing to offer.
    ///
    /// <para>The list arrives already filtered — the eligibility rules live in
    /// <c>GameSettingsStore.CanImportFrom</c> so they stay testable and are not restated here.
    /// This method only decides whether the row is worth showing at all.</para>
    /// </summary>
    private void BuildCopySettingsRow(IReadOnlyList<ModProfile>? sources)
    {
        if (sources == null || sources.Count == 0)
        {
            CopySettingsRow.Visibility = Visibility.Collapsed;
            return;
        }

        foreach (var p in sources)
            CopySettingsCombo.Items.Add(new ComboBoxItem { Content = p.DisplayName, Tag = p.Id });

        CopySettingsCombo.SelectedIndex = 0;
        CopySettingsRow.Visibility = Visibility.Visible;
    }

    /// <summary>The combo is inert until the box is ticked, so "no" stays the default.</summary>
    private void CopySettingsCheck_Click(object sender, RoutedEventArgs e)
        => CopySettingsCombo.IsEnabled = CopySettingsCheck.IsChecked == true;

    /// <summary>
    /// Shows a non-blocking reminder to open the original Age of Empires III
    /// at least once before installing the mod. The mod overlays a full AoE3
    /// clone, but the base game generates its per-user configuration files on
    /// first launch — so installing before that first run can leave the mod
    /// without those files. Heuristic: the absence of AoE3's user-data folder
    /// (<c>Documents\My Games\Age of Empires 3</c>) suggests the game was never
    /// run. This only warns; it never disables the Install button (the folder
    /// can be missing for unrelated reasons, e.g. a moved profile).
    /// </summary>
    private void UpdateFirstRunWarning()
    {
        bool launchedBefore = false;
        try
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrEmpty(docs))
                launchedBefore = Directory.Exists(
                    Path.Combine(docs, "My Games", "Age of Empires 3"));
        }
        catch { }

        if (!launchedBefore)
            FirstRunWarningText.Text = Strings.Get("InstallGameNotLaunchedWarning");
        FirstRunWarningBox.Visibility = launchedBefore
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void ApplyLanguage()
    {
        Title = Strings.Format("DlgPickInstallFolderTitle", _modDisplayName);
        TitleBarControl.Title = Strings.Format("DlgPickInstallFolderTitle", _modDisplayName);
        HeaderText.Text = Strings.Get("DlgPickInstallFolderHeader");
        DescriptionText.Text = Strings.Format("DlgPickInstallFolderDescription", _modDisplayName);
        LblAoE3Folder.Text = Strings.Get("DlgInstallCopiedFrom");
        LblFolder.Text = Strings.Get("DlgInstallInstalledIn");
        BrowseButton.Content = Strings.Get("ChangePathButton");
        BrowseAoE3InDialogButton.Content = Strings.Get("ChangePathButton");
        SearchAoe3Button.Content = Strings.Get("DlgSearchAoe3Button");
        CopySettingsTitle.Text = Strings.Get("DlgInstallCopySettings");
        CopySettingsHint.Text = Strings.Get("DlgInstallCopySettingsHint");
        NestNote.Text = Strings.Get("DlgInstallNestedNote");
        OkButton.Content = Strings.Get("DlgInstallConfirm");
        CancelButton.Content = Strings.Get("BtnCancel");
    }

    /// <summary>
    /// Refresh the AoE3 path field and its status message based on
    /// <see cref="Aoe3SourcePath"/>. Called on init and after the user
    /// picks a new folder via the Change button.
    /// </summary>
    private void UpdateAoE3Display()
    {
        bool found = !string.IsNullOrEmpty(Aoe3SourcePath);
        SetPathParts(Aoe3PathRoot, Aoe3PathMid, Aoe3PathLeaf, Aoe3SourcePath);

        // The long "AoE3 was not detected, here is what to do" paragraph belongs UNDER the
        // empty field, not inside the status chip beside the label: it is four lines of
        // instructions, and a chip is two words.
        Aoe3PathEmpty.Text = found ? "" : Strings.Get("DlgInstallAoe3Empty");
        Aoe3MissingText.Text = found ? "" : Strings.Get("InstallAoe3NotDetected");
        Aoe3MissingText.Visibility = found ? Visibility.Collapsed : Visibility.Visible;

        Aoe3StatusDot.Visibility = Visibility.Visible;
        Aoe3StatusDot.Fill = found ? BrushOf("MpOk") : BrushOf("MpCaution");
        Aoe3StatusText.Foreground = found ? BrushOf("MpOkText") : BrushOf("MpCautionText");
        Aoe3StatusText.Text = found
            ? (string.IsNullOrEmpty(_aoe3SourceLabel)
                ? Strings.Get("DlgAoe3DetectedChip")
                : Strings.Format("DlgAoe3DetectedChipWithSource", _aoe3SourceLabel))
            : Strings.Get("DlgAoe3MissingChip");

        // Offer the manual "search my Asian Dynasties" button only while no
        // source is set (a real AoE3 in a non-standard folder that neither the
        // fast probes nor the automatic pre-scan found).
        SearchAoe3Button.Visibility = found ? Visibility.Collapsed : Visibility.Visible;

        // The nesting note is only TRUE while the destination really is inside this folder.
        UpdateNesting();

        // Setting / clearing AoE3 flips whether the install can proceed,
        // so re-run validation to enable/disable the OK button.
        ValidateInputs();

        // The clone size depends on the AoE3 source — (re)measure it off-thread
        // whenever the source changes, then the disk-space warning updates.
        MeasureCloneSizeAsync();
    }

    private void FolderTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        // Picking a destination INSIDE an AoE3 folder is a valid way to
        // resolve the source — infer it live so the button can enable
        // without a separate Browse-for-AoE3 step. Do NOT debounce this away:
        // it is what lets the Install button enable with no separate Browse step,
        // so removing it changes the installer's behaviour rather than its looks.
        TryInferAoe3FromDestination();
        UpdateDestDisplay();
        ValidateInputs();
        UpdateDiskSpace();
    }

    // The overlay is what you SEE; the TextBox under it always holds the whole path and is
    // what SelectedFolder reads. Clicking the field focuses the box, and the overlay gets
    // out of the way so you can edit the real thing.
    private void FolderTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        => DestDisplay.Visibility = Visibility.Collapsed;

    private void FolderTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        UpdateDestDisplay();
        DestDisplay.Visibility = Visibility.Visible;
    }

    private void UpdateDestDisplay()
    {
        SetPathParts(DestPathRoot, DestPathMid, DestPathLeaf, FolderTextBox.Text.Trim());
        UpdateNesting();
    }

    /// <summary>
    /// Paints one path as root + elided middle + last folder. The rule lives in
    /// <see cref="PathDisplay.SplitForDisplay"/> so both rows and their tests share it.
    /// </summary>
    private static void SetPathParts(TextBlock root, TextBlock mid, TextBlock leaf, string? path)
    {
        var (r, m, l) = PathDisplay.SplitForDisplay(path);
        root.Text = r;
        mid.Text = m;
        leaf.Text = l;
    }

    /// <summary>
    /// The "↳ … inside the folder above" pair. Shown only when the destination really IS
    /// under the source — the default is <c>&lt;aoe3&gt;\&lt;mod&gt;</c>, but the folder is
    /// editable and a note that quietly stops being true is worse than no note.
    /// </summary>
    private void UpdateNesting()
    {
        bool nested = false;
        try
        {
            var src = (Aoe3SourcePath ?? "").Trim();
            var dst = FolderTextBox.Text.Trim();
            if (src.Length > 0 && dst.Length > 0)
            {
                var full = Path.GetFullPath(src).TrimEnd('\\', '/') + "\\";
                nested = Path.GetFullPath(dst).StartsWith(full, StringComparison.OrdinalIgnoreCase);
            }
        }
        catch { nested = false; }

        var v = nested && Aoe3Row.Visibility == Visibility.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;
        NestGlyph.Visibility = v;
        NestNote.Visibility = v;
    }

    private Brush BrushOf(string key) => (Brush)FindResource(key);

    /// <summary>
    /// If no AoE3 source is set yet and the chosen destination's parent
    /// folder looks like an AoE3 install, adopt it as the clone source.
    /// Only fills when null — never overrides a path the user explicitly
    /// picked via the Browse button.
    /// </summary>
    private void TryInferAoe3FromDestination()
    {
        if (!string.IsNullOrEmpty(Aoe3SourcePath)) return;

        var chosen = FolderTextBox.Text.Trim().TrimEnd('\\', '/');
        var parentDir = Path.GetDirectoryName(chosen);
        if (!string.IsNullOrEmpty(parentDir) && Services.AoE3Detector.LooksLikeAoE3(parentDir))
        {
            Aoe3SourcePath = parentDir;
            _aoe3SourceLabel = null; // inferred, no named source
            UpdateAoE3Display();     // re-renders the field + re-validates
        }
    }

    /// <summary>
    /// Validates the destination folder AND that an AoE3 source is set.
    /// AoE3 is mandatory: the mod is installed on top of a full AoE3 clone,
    /// so with no source there's nothing to copy and the result would be an
    /// unplayable mod-only folder. The OK button stays disabled until both
    /// the folder is valid and an AoE3 source exists.
    /// </summary>
    private void ValidateInputs()
    {
        var path = FolderTextBox.Text.Trim();
        string? warning = null;

        if (string.IsNullOrEmpty(path))
        {
            warning = Strings.Get("WarnPathEmpty");
        }
        else
        {
            try
            {
                var full = Path.GetFullPath(path);
                var lowered = full.ToLowerInvariant();
                if (lowered.StartsWith(@"c:\windows\")
                    || lowered.StartsWith(@"c:\program files\windowsapps"))
                {
                    warning = Strings.Get("WarnPathSystem");
                }
            }
            catch
            {
                warning = Strings.Get("WarnPathInvalid");
            }
        }

        // AoE3 source is mandatory for a CLONING install — checked after the folder so a
        // bad folder warning takes priority (the user fixes one thing at a time). An
        // in-place overlay clones nothing, so it never waits for a source.
        if (warning == null && _requiresAoe3Source && string.IsNullOrEmpty(Aoe3SourcePath))
        {
            warning = Strings.Get("DlgInstallAoe3Required");
        }

        if (warning != null)
        {
            WarningText.Text = warning;
            WarningText.Visibility = Visibility.Visible;
            OkButton.IsEnabled = false;
        }
        else
        {
            WarningText.Visibility = Visibility.Collapsed;
            OkButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Measure the AoE3 clone size off the UI thread (it enumerates the whole
    /// source tree), then refresh the disk-space line. No-op / clears when there's
    /// no source; cached per source so re-opening the same source doesn't re-scan.
    /// Fire-and-forget UI handler.
    /// </summary>
    private async void MeasureCloneSizeAsync()
    {
        var source = Aoe3SourcePath;
        var dest = FolderTextBox.Text.Trim();

        if (string.IsNullOrEmpty(source))
        {
            _cloneBytes = -1;
            _measuredSource = null;
            UpdateDiskSpace();
            return;
        }

        // Already measured this exact source — just recompute against current free space.
        if (_cloneBytes >= 0
            && string.Equals(source, _measuredSource, StringComparison.OrdinalIgnoreCase))
        {
            UpdateDiskSpace();
            return;
        }

        // Transient "calculating" state while we enumerate.
        _cloneBytes = -1;
        _spaceWarning = false;
        PaintSpaceLine("neutral", "…", Strings.Get("DiskSpaceCalculating"));

        try
        {
            var svc = new FolderCloneService();
            long bytes = await Task.Run(() => svc.CountCloneableBytes(source!, dest));
            _cloneBytes = bytes;
            _measuredSource = source;
        }
        catch
        {
            _cloneBytes = -1; // couldn't measure → no warning, just show free space
        }

        UpdateDiskSpace();
    }

    /// <summary>
    /// What the install NEEDS and what the drive HAS, in one sentence. They used to be
    /// ~200 px apart and only half true: the requirement was the tail of the header
    /// paragraph ("About 12 GB of free space recommended", a constant), while the figure
    /// actually MEASURED from the AoE3 clone only ever surfaced when it was short.
    /// </summary>
    private void UpdateDiskSpace()
    {
        var dest = FolderTextBox.Text.Trim();
        long free = DiskSpaceService.SafeFreeSpace(dest);
        var root = SafeRoot(dest);

        // Until the clone is measured (or with no source), just show free space —
        // never warn on an unknown requirement.
        if (_cloneBytes < 0)
        {
            _spaceWarning = false;
            if (free >= 0 && !string.IsNullOrEmpty(root))
                PaintSpaceLine("neutral", "•",
                    Strings.Format("InstallDiskSpace", DiskSpaceService.FormatBytes(free), root));
            else
                PaintSpaceLine("neutral", "•", "");
            return;
        }

        long required = DiskSpaceService.EstimateInstallRequirement(_cloneBytes);
        long freeTemp = DiskSpaceService.SafeFreeSpace(Path.GetTempPath());

        bool destShort = DiskSpaceService.IsShort(free, required);
        // If %TEMP% is on a DIFFERENT volume than the destination, it needs room
        // for the payload download + extraction independently — check it too.
        bool tempDifferent = !SameRoot(dest, Path.GetTempPath());
        bool tempShort = tempDifferent
            && DiskSpaceService.IsShort(freeTemp, DiskSpaceService.InstallExtraAllowanceBytes);

        _spaceWarning = destShort || tempShort;

        if (_spaceWarning)
        {
            long shortFree = destShort ? free : freeTemp;
            long shortReq = destShort ? required : DiskSpaceService.InstallExtraAllowanceBytes;
            var shortDrive = destShort ? root : SafeRoot(Path.GetTempPath());
            PaintSpaceLine("warn", "⚠", Strings.Format("DiskSpaceWarningLine",
                DiskSpaceService.FormatBytes(shortReq),
                DiskSpaceService.FormatBytes(shortFree),
                shortDrive));
            return;
        }

        PaintSpaceLine("ok", "✓", Strings.Format("InstallSpaceLine",
            DiskSpaceService.FormatBytes(required),
            DiskSpaceService.FormatBytes(free),
            root));
    }

    /// <summary>Green when it fits, amber when it does not, quiet while unknown.</summary>
    private void PaintSpaceLine(string tone, string glyph, string text)
    {
        DiskSpaceBox.Visibility = string.IsNullOrEmpty(text)
            ? Visibility.Collapsed
            : Visibility.Visible;
        DiskSpaceGlyph.Text = glyph;
        DiskSpaceText.Text = text;

        switch (tone)
        {
            case "ok":
                DiskSpaceBox.Background = BrushOf("MpOkBg");
                DiskSpaceBox.BorderBrush = BrushOf("MpOkRim");
                DiskSpaceGlyph.Foreground = BrushOf("MpOk");
                DiskSpaceText.Foreground = BrushOf("MpOkText");
                break;
            case "warn":
                DiskSpaceBox.Background = BrushOf("MpCautionBg");
                DiskSpaceBox.BorderBrush = BrushOf("MpCautionRim");
                DiskSpaceGlyph.Foreground = BrushOf("MpCaution");
                DiskSpaceText.Foreground = BrushOf("MpCautionText");
                break;
            default:
                DiskSpaceBox.Background = Brushes.Transparent;
                DiskSpaceBox.BorderBrush = BrushOf("UiRimSeam");
                DiskSpaceGlyph.Foreground = BrushOf("UiTextDim");
                DiskSpaceText.Foreground = BrushOf("MpTextMuted");
                break;
        }
    }

    /// <summary>
    /// The volume a path lives on, NAMED rather than pathed: GetPathRoot hands back
    /// "C:\\", and "free on C:\\" reads as a folder when it is meant to read as a drive.
    /// </summary>
    private static string SafeRoot(string path)
    {
        try { return (Path.GetPathRoot(path) ?? "").TrimEnd('\\', '/'); }
        catch { return ""; }
    }

    /// <summary>True when both paths live on the same volume (conservative: on
    /// any error assume same, so we don't add a spurious temp-drive warning).</summary>
    private static bool SameRoot(string a, string b)
    {
        try
        {
            var ra = Path.GetPathRoot(Path.GetFullPath(a));
            var rb = Path.GetPathRoot(Path.GetFullPath(b));
            return string.Equals(ra, rb, StringComparison.OrdinalIgnoreCase);
        }
        catch { return true; }
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Strings.Get("DlgPickInstallFolderTitle"),
            Multiselect = false
        };

        var current = FolderTextBox.Text.Trim();
        try
        {
            var dir = current;
            while (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                var parent = Path.GetDirectoryName(dir);
                if (parent == dir) break;
                dir = parent ?? "";
            }
            if (!string.IsNullOrEmpty(dir))
                dialog.InitialDirectory = dir;
        }
        catch { }

        if (dialog.ShowDialog(this) == true)
        {
            var picked = dialog.FolderName.TrimEnd('\\', '/');
            if (!picked.EndsWith(_modDisplayName, StringComparison.OrdinalIgnoreCase))
                picked = Path.Combine(picked, _modDisplayName);
            FolderTextBox.Text = picked;
        }
    }

    private void BrowseAoE3InDialogButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Strings.Get("DlgAoE3FolderPickerTitle"),
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true) return;

        var chosen = dialog.FolderName.TrimEnd('\\', '/');

        // Resolve the AoE3 install ROOT — the folder that contains the entire
        // game tree (bin\, data\, sound\, art\, ...). This is what we clone
        // when installing the mod. We accept three layouts:
        //   1. User picked the root directly, with bin\age3y.exe inside (Steam)
        //   2. User picked the root directly, with age3y.exe inside (GOG/retail)
        //   3. User picked the bin\ subfolder by mistake — walk up one level
        string? gameFolder = null;
        if (File.Exists(Path.Combine(chosen, "bin", "age3y.exe")))
        {
            gameFolder = chosen;                     // Steam-style root
        }
        else if (File.Exists(Path.Combine(chosen, "age3y.exe")))
        {
            // Could be a flat retail/GOG layout, OR the user picked bin\ by mistake.
            // If the parent has data\, the user picked bin\ — walk up.
            var parent = Path.GetDirectoryName(chosen);
            var leaf = Path.GetFileName(chosen);
            if (string.Equals(leaf, "bin", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(parent)
                && Directory.Exists(Path.Combine(parent, "data")))
            {
                gameFolder = parent;
            }
            else
            {
                gameFolder = chosen;                 // GOG/retail flat layout
            }
        }

        if (gameFolder == null)
        {
            MessageBox.Show(this,
                Strings.Get("DlgInvalidAoE3FolderBody"),
                Strings.Get("DlgInvalidAoE3FolderTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Success — update the dialog state
        Aoe3SourcePath = gameFolder;
        _aoe3SourceLabel = null; // user-picked, no source label
        UpdateAoE3Display();

        // Also suggest installing inside this AoE3 folder
        var suggestedWolPath = Path.Combine(gameFolder!, _modDisplayName);
        FolderTextBox.Text = suggestedWolPath;
    }

    /// <summary>
    /// Exhaustive, user-initiated content search for a clean Age of Empires III
    /// base — the manual counterpart of the automatic pre-scan in
    /// <c>MainWindow.InstallAsync</c>. Runs <see cref="AoE3Detector.FindAllDeep"/>
    /// off the UI thread WITH drive roots (a broad scan is expected when the user
    /// asks for it). A mod folder (e.g. an existing WoL) is never returned as a
    /// base — <see cref="AoE3Detector.IsCleanAoE3Folder"/> excludes it — so this
    /// can't seed a contaminated clone.
    /// </summary>
    private async void SearchAoe3Button_Click(object sender, RoutedEventArgs e)
    {
        SearchAoe3Button.IsEnabled = false;
        Aoe3StatusDot.Visibility = Visibility.Collapsed;
        Aoe3StatusText.Text = Strings.Get("DlgSearchAoe3Searching");
        Aoe3StatusText.Foreground = BrushOf("MpTextMuted");

        try
        {
            var hits = await Task.Run(
                () => AoE3Detector.FindAllDeep(includeDriveRoots: true, maxDirs: 20_000));
            var hit = hits.FirstOrDefault(h => !string.IsNullOrEmpty(h.ModRoot));

            if (hit != null)
            {
                Aoe3SourcePath = hit.ModRoot;
                _aoe3SourceLabel = null;   // found by content, no named source
                UpdateAoE3Display();       // green status, hides this button, re-validates
                return;
            }

            Aoe3StatusText.Text = Strings.Get("DlgSearchAoe3NotFound");
            Aoe3StatusText.Foreground = BrushOf("MpCautionText");
        }
        catch
        {
            Aoe3StatusText.Text = Strings.Get("DlgSearchAoe3NotFound");
            Aoe3StatusText.Foreground = BrushOf("MpCautionText");
        }
        finally
        {
            if (string.IsNullOrEmpty(Aoe3SourcePath))
                SearchAoe3Button.IsEnabled = true;
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var chosen = FolderTextBox.Text.Trim().TrimEnd('\\', '/');

        // AoE3 source is mandatory for a CLONING install — the live inference + Browse
        // button normally fill it before the button enables, but guard here so such an
        // install can never start mod-only (an unplayable folder with no base-game files).
        // An in-place overlay is exempt: it adds to the game that is already there.
        if (_requiresAoe3Source && string.IsNullOrEmpty(Aoe3SourcePath))
        {
            ValidateInputs();
            return;
        }

        // Low-disk-space warning (warn-but-allow): if the conservative estimate
        // says the drive is short, confirm before proceeding. Never blocks.
        if (_spaceWarning)
        {
            var res = MessageBox.Show(this,
                Strings.Get("DiskSpaceConfirmInstallBody"),
                Strings.Get("DiskSpaceConfirmTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res != MessageBoxResult.Yes) return;
        }

        SelectedFolder = chosen;
        // Read the choice HERE, not as the combo changes: a selection the player made and then
        // unticked must not travel, and Cancel must leave nothing behind.
        CopySettingsFromModId = CopySettingsCheck.IsChecked == true
            && CopySettingsCombo.SelectedItem is ComboBoxItem item
            && item.Tag is string id
            && !string.IsNullOrWhiteSpace(id)
                ? id
                : null;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

}
