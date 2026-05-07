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
        : this(defaultFolder, null, null) { }

    public InstallFolderDialog(string defaultFolder, string? aoe3Path, string? aoe3SourceLabel)
    {
        InitializeComponent();
        Aoe3SourcePath = aoe3Path;

        ApplyLanguage();
        FolderTextBox.Text = defaultFolder;
        FolderTextBox.SelectAll();
        FolderTextBox.Focus();

        if (!string.IsNullOrEmpty(aoe3Path))
        {
            // AoE3 detected — show green panel
            Aoe3DetectionTitleText.Text = string.IsNullOrEmpty(aoe3SourceLabel)
                ? Strings.Get("DlgAoe3DetectedTitle")
                : Strings.Format("DlgAoe3DetectedTitleWithSource", aoe3SourceLabel);
            Aoe3DetectionPathText.Text = aoe3Path;
            Aoe3DetectionPanel.Visibility = Visibility.Visible;
            Aoe3NotDetectedPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            // AoE3 NOT detected — show orange warning inline
            Aoe3NotDetectedText.Text = Strings.Get("InstallAoe3NotDetected");
            Aoe3DetectionPanel.Visibility = Visibility.Collapsed;
            Aoe3NotDetectedPanel.Visibility = Visibility.Visible;
        }

        UpdateDiskSpace();
    }

    private void ApplyLanguage()
    {
        Title = Strings.Get("DlgPickInstallFolderTitle");
        HeaderText.Text = Strings.Get("DlgPickInstallFolderHeader");
        DescriptionText.Text = Strings.Get("DlgPickInstallFolderDescription");
        LblFolder.Text = Strings.Get("DlgPickInstallFolderLabel");
        BrowseButton.Content = Strings.Get("ChangePathButton");
        BrowseAoE3InDialogButton.Content = Strings.Get("BrowseAoE3Button");
        OkButton.Content = Strings.Get("BtnInstall");
        CancelButton.Content = Strings.Get("BtnCancel");
    }

    private void FolderTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        ValidateFolder();
        UpdateDiskSpace();
    }

    private void ValidateFolder()
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
            if (!picked.EndsWith("Wars of Liberty", StringComparison.OrdinalIgnoreCase))
                picked = Path.Combine(picked, "Wars of Liberty");
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

        // Try to find age3y.exe in the selected folder or its bin\ subfolder
        string? resolvedExe = null;
        string? gameFolder = null;
        var candidates = new[]
        {
            (exe: Path.Combine(chosen, "age3y.exe"),          folder: chosen),
            (exe: Path.Combine(chosen, "bin", "age3y.exe"),   folder: chosen),
        };
        foreach (var (exe, folder) in candidates)
        {
            if (File.Exists(exe))
            {
                resolvedExe = exe;
                gameFolder = folder;
                break;
            }
        }

        if (resolvedExe == null)
        {
            MessageBox.Show(this,
                Strings.Get("DlgInvalidAoE3FolderBody"),
                Strings.Get("DlgInvalidAoE3FolderTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Success — update the dialog state
        Aoe3SourcePath = gameFolder;

        // Switch from orange "not detected" to green "detected"
        Aoe3DetectionTitleText.Text = Strings.Get("DlgAoe3DetectedTitle");
        Aoe3DetectionPathText.Text = gameFolder;
        Aoe3DetectionPanel.Visibility = Visibility.Visible;
        Aoe3NotDetectedPanel.Visibility = Visibility.Collapsed;

        // Also suggest installing inside this AoE3 folder
        var suggestedWolPath = Path.Combine(gameFolder!, "Wars of Liberty");
        FolderTextBox.Text = suggestedWolPath;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var chosen = FolderTextBox.Text.Trim().TrimEnd('\\', '/');

        // If no AoE3 detected, try to infer from the parent folder
        if (Aoe3SourcePath == null)
        {
            var parentDir = Path.GetDirectoryName(chosen);
            if (!string.IsNullOrEmpty(parentDir) && Services.AoE3Detector.LooksLikeAoE3(parentDir))
                Aoe3SourcePath = parentDir;
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
