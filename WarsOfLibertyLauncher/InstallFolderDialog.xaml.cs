using System;
using System.IO;
using System.Windows;
using WarsOfLibertyLauncher.Localization;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Dialog shown before launching the silent installer. The user can accept
/// the default folder or pick a different one. We default to a Steam/Epic-style
/// pre-filled path so the common case is "click OK and go".
///
/// The standard OpenFolderDialog isn't suitable here because it can only
/// navigate to existing folders — and on a fresh machine, the destination
/// folder for WoL doesn't exist yet.
/// </summary>
public partial class InstallFolderDialog : Window
{
    /// <summary>The folder the user confirmed. Set when <see cref="Window.DialogResult"/> is true.</summary>
    public string SelectedFolder { get; private set; } = "";

    public InstallFolderDialog(string defaultFolder)
    {
        InitializeComponent();
        ApplyLanguage();
        FolderTextBox.Text = defaultFolder;
        FolderTextBox.SelectAll();
        FolderTextBox.Focus();
    }

    private void ApplyLanguage()
    {
        Title = Strings.Get("DlgPickInstallFolderTitle");
        HeaderText.Text = Strings.Get("DlgPickInstallFolderHeader");
        DescriptionText.Text = Strings.Get("DlgPickInstallFolderDescription");
        LblFolder.Text = Strings.Get("DlgPickInstallFolderLabel");
        BrowseButton.Content = Strings.Get("ChangePathButton");
        OkButton.Content = Strings.Get("BtnInstall");
        CancelButton.Content = Strings.Get("BtnCancel");
    }

    private void FolderTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        ValidateFolder();
    }

    /// <summary>
    /// Live validation: make sure the path is plausible. We don't require it to
    /// exist (the installer creates it). We just check it looks like a real
    /// Windows path and doesn't point inside system folders.
    /// </summary>
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
                // Path.GetFullPath rejects clearly malformed paths
                var full = Path.GetFullPath(path);

                // Reject paths inside Windows system folders — Inno Setup
                // would refuse anyway and we want a friendly error first.
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

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        // Used as an "advanced" escape hatch if the user wants to navigate
        // visually. Note: this dialog requires the folder to already exist.
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Strings.Get("DlgPickInstallFolderTitle"),
            Multiselect = false
        };

        var current = FolderTextBox.Text.Trim();
        // Walk up to the first existing parent so the dialog opens somewhere
        // meaningful even when the target folder doesn't exist yet.
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
        catch
        {
            // No initial directory; the dialog will use Windows defaults
        }

        if (dialog.ShowDialog(this) == true)
        {
            // Append "Wars of Liberty" if the user picked a generic parent
            var picked = dialog.FolderName.TrimEnd('\\', '/');
            if (!picked.EndsWith("Wars of Liberty", StringComparison.OrdinalIgnoreCase))
                picked = Path.Combine(picked, "Wars of Liberty");
            FolderTextBox.Text = picked;
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var chosen = FolderTextBox.Text.Trim().TrimEnd('\\', '/');

        // Final sanity check before accepting: warn the user if the chosen
        // folder isn't inside an AoE3 install, since WoL won't actually work
        // there (the AoE3 engine only loads mods from its own directory tree).
        if (!Services.AoE3Detector.LooksLikeInsideAoE3(chosen))
        {
            var warn = MessageBox.Show(
                this,
                Localization.Strings.Format("DlgFolderNotInAoE3Body", chosen),
                Localization.Strings.Get("DlgFolderNotInAoE3Title"),
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

            // OK = "I know, install anyway"; Cancel = "let me pick again"
            if (warn != MessageBoxResult.OK) return;
        }

        SelectedFolder = chosen;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
