using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using WarsOfLibertyLauncher.Localization;
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

    public InstallFolderDialog(string defaultFolder)
        : this(defaultFolder, null, null, "Wars of Liberty") { }

    public InstallFolderDialog(string defaultFolder, string? aoe3Path, string? aoe3SourceLabel)
        : this(defaultFolder, aoe3Path, aoe3SourceLabel, "Wars of Liberty") { }

    private string? _aoe3SourceLabel;
    private readonly string _modDisplayName;

    // Disk-space estimate state. _cloneBytes = -1 means "not measured yet"; it's
    // filled off-thread by measuring the AoE3 source we'd clone. _spaceWarning is
    // set by UpdateDiskSpace when free space is below the conservative estimate —
    // it drives the amber warning line and the confirm-to-proceed on OK.
    private long _cloneBytes = -1;
    private string? _measuredSource;
    private bool _spaceWarning;
    private Brush? _diskSpaceDefaultBrush;

    /// <param name="modDisplayName">
    /// Display name of the mod being installed (e.g. "Wars of Liberty",
    /// "Improvement Mod"). Templated into the dialog's title and the
    /// "&lt;mod&gt; will be installed in its own '&lt;mod&gt;' folder" copy so
    /// every mod sees its own name instead of WoL.
    /// </param>
    public InstallFolderDialog(string defaultFolder, string? aoe3Path, string? aoe3SourceLabel, string modDisplayName)
    {
        InitializeComponent();
        _diskSpaceDefaultBrush = DiskSpaceText.Foreground;
        Aoe3SourcePath = aoe3Path;
        _aoe3SourceLabel = aoe3SourceLabel;
        _modDisplayName = string.IsNullOrEmpty(modDisplayName) ? "the mod" : modDisplayName;

        ApplyLanguage();
        FolderTextBox.Text = defaultFolder;
        FolderTextBox.SelectAll();
        FolderTextBox.Focus();

        UpdateAoE3Display();
        UpdateDiskSpace();
        UpdateFirstRunWarning();
    }

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
        FirstRunWarningText.Visibility = launchedBefore
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void ApplyLanguage()
    {
        Title = Strings.Format("DlgPickInstallFolderTitle", _modDisplayName);
        TitleBarControl.Title = Strings.Format("DlgPickInstallFolderTitle", _modDisplayName);
        HeaderText.Text = Strings.Get("DlgPickInstallFolderHeader");
        DescriptionText.Text = Strings.Format("DlgPickInstallFolderDescription", _modDisplayName);
        LblAoE3Folder.Text = Strings.Get("LblGamePath");
        LblFolder.Text = Strings.Get("DlgPickInstallFolderLabel");
        BrowseButton.Content = Strings.Get("ChangePathButton");
        BrowseAoE3InDialogButton.Content = Strings.Get("ChangePathButton");
        OkButton.Content = Strings.Get("BtnInstall");
        CancelButton.Content = Strings.Get("BtnCancel");
    }

    /// <summary>
    /// Refresh the AoE3 path field and its status message based on
    /// <see cref="Aoe3SourcePath"/>. Called on init and after the user
    /// picks a new folder via the Change button.
    /// </summary>
    private void UpdateAoE3Display()
    {
        if (!string.IsNullOrEmpty(Aoe3SourcePath))
        {
            Aoe3PathTextBox.Text = Aoe3SourcePath;
            Aoe3PathTextBox.BorderBrush = (System.Windows.Media.Brush)
                new System.Windows.Media.BrushConverter().ConvertFromString("#3a8c3a")!;

            // Green status line under the field
            Aoe3StatusText.Text = string.IsNullOrEmpty(_aoe3SourceLabel)
                ? "✓ " + Strings.Get("DlgAoe3DetectedTitle")
                : "✓ " + Strings.Format("DlgAoe3DetectedTitleWithSource", _aoe3SourceLabel);
            Aoe3StatusText.Foreground = (System.Windows.Media.Brush)
                new System.Windows.Media.BrushConverter().ConvertFromString("#9bd99b")!;
        }
        else
        {
            Aoe3PathTextBox.Text = "";
            Aoe3PathTextBox.BorderBrush = (System.Windows.Media.Brush)
                new System.Windows.Media.BrushConverter().ConvertFromString("#8c6c3a")!;

            // Orange status line guiding the user
            Aoe3StatusText.Text = Strings.Get("InstallAoe3NotDetected");
            Aoe3StatusText.Foreground = (System.Windows.Media.Brush)
                new System.Windows.Media.BrushConverter().ConvertFromString("#d4a04a")!;
        }

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
        // without a separate Browse-for-AoE3 step.
        TryInferAoe3FromDestination();
        ValidateInputs();
        UpdateDiskSpace();
    }

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

        // AoE3 source is mandatory — checked after the folder so a bad
        // folder warning takes priority (the user fixes one thing at a time).
        if (warning == null && string.IsNullOrEmpty(Aoe3SourcePath))
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
        DiskSpaceText.Foreground = _diskSpaceDefaultBrush;
        DiskSpaceText.Text = Strings.Get("DiskSpaceCalculating");

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
            DiskSpaceText.Foreground = _diskSpaceDefaultBrush;
            DiskSpaceText.Text = (free >= 0 && !string.IsNullOrEmpty(root))
                ? Strings.Format("InstallDiskSpace", DiskSpaceService.FormatBytes(free), root)
                : "";
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
            DiskSpaceText.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#E0A82E"));
            DiskSpaceText.Text = "⚠ " + Strings.Format("DiskSpaceWarningLine",
                DiskSpaceService.FormatBytes(shortReq),
                DiskSpaceService.FormatBytes(shortFree),
                shortDrive);
        }
        else
        {
            _spaceWarning = false;
            DiskSpaceText.Foreground = _diskSpaceDefaultBrush;
            DiskSpaceText.Text = (free >= 0 && !string.IsNullOrEmpty(root))
                ? Strings.Format("InstallDiskSpace", DiskSpaceService.FormatBytes(free), root)
                : "";
        }
    }

    private static string SafeRoot(string path)
    {
        try { return Path.GetPathRoot(path) ?? ""; }
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

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var chosen = FolderTextBox.Text.Trim().TrimEnd('\\', '/');

        // AoE3 source is mandatory — the live inference + Browse button
        // normally fill it before the button enables, but guard here so
        // the install can never start mod-only (an unplayable folder with
        // no base-game files).
        if (string.IsNullOrEmpty(Aoe3SourcePath))
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
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:0.##} {units[unit]}";
    }
}
