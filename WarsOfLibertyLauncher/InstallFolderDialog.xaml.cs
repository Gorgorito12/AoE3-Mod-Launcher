using System;
using System.IO;
using System.Windows;
using WarsOfLibertyLauncher.Localization;

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

    /// <param name="modDisplayName">
    /// Display name of the mod being installed (e.g. "Wars of Liberty",
    /// "Improvement Mod"). Templated into the dialog's title and the
    /// "&lt;mod&gt; will be installed in its own '&lt;mod&gt;' folder" copy so
    /// every mod sees its own name instead of WoL.
    /// </param>
    public InstallFolderDialog(string defaultFolder, string? aoe3Path, string? aoe3SourceLabel, string modDisplayName)
    {
        InitializeComponent();
        Aoe3SourcePath = aoe3Path;
        _aoe3SourceLabel = aoe3SourceLabel;
        _modDisplayName = string.IsNullOrEmpty(modDisplayName) ? "the mod" : modDisplayName;

        ApplyLanguage();
        FolderTextBox.Text = defaultFolder;
        FolderTextBox.SelectAll();
        FolderTextBox.Focus();

        UpdateAoE3Display();
        UpdateDiskSpace();
    }

    private void ApplyLanguage()
    {
        Title = Strings.Format("DlgPickInstallFolderTitle", _modDisplayName);
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

    private void UpdateDiskSpace()
    {
        try
        {
            var path = FolderTextBox.Text.Trim();
            var root = Path.GetPathRoot(path);
            if (!string.IsNullOrEmpty(root))
            {
                var drive = new DriveInfo(root);
                DiskSpaceText.Text = Strings.Format("InstallDiskSpace",
                    FormatBytes(drive.AvailableFreeSpace), root);
                return;
            }
        }
        catch { }
        DiskSpaceText.Text = "";
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
