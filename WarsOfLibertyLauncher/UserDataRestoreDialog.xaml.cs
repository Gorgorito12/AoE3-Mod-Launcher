using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Styled list-of-backups dialog. The newest backup is preselected, but the
/// user can pick any of them. The "Open folder" button opens Documents\My
/// Games\ in Explorer for cases where they want to manage backups manually.
/// Restore renames the selected backup back into place and snapshots the
/// current data first (so swapping is reversible).
/// </summary>
public partial class UserDataRestoreDialog : Window
{
    /// <summary>True if the user clicked Restore AND the operation succeeded.</summary>
    public bool RestorePerformed { get; private set; }

    /// <summary>Backup that was restored (original path before the rename).</summary>
    public UserDataService.BackupInfo? RestoredBackup { get; private set; }

    /// <summary>
    /// Path of the new ".bak.&lt;ts&gt;" snapshot of the data that was active
    /// before the restore. Null if the active folder was empty.
    /// </summary>
    public string? PreviousDataSnapshotPath { get; private set; }

    private readonly List<BackupRow> _rows;
    private readonly string _userDataFolderName;

    /// <summary>Back-compat overload. Defaults to WoL's folder name.</summary>
    public UserDataRestoreDialog(IReadOnlyList<UserDataService.BackupInfo> backups)
        : this(backups, "Wars of Liberty") { }

    /// <param name="userDataFolderName">
    /// The active mod's <see cref="ModProfile.UserDataFolder"/>. Passed
    /// through to <see cref="UserDataService.RestoreBackup(string, string)"/>
    /// and to the Open-folder action so the dialog operates on the right
    /// mod's data when more than one mod uses the feature.
    /// </param>
    public UserDataRestoreDialog(
        IReadOnlyList<UserDataService.BackupInfo> backups, string userDataFolderName)
    {
        InitializeComponent();

        _rows = backups.Select(b => new BackupRow(b)).ToList();
        _userDataFolderName = userDataFolderName ?? "";

        Title = Strings.Get("DlgRestoreDialogTitle");
        TitleBarControl.Title = Strings.Get("DlgRestoreDialogTitle");
        HeaderText.Text = Strings.Get("DlgRestoreDialogHeader");
        DescriptionText.Text = backups.Count == 1
            ? Strings.Get("DlgRestoreDialogDescriptionSingle")
            : Strings.Format("DlgRestoreDialogDescriptionMultiple", backups.Count);
        ListLabelText.Text = Strings.Get("DlgRestoreDialogListLabel");
        ReassuranceText.Text = Strings.Get("DlgRestoreDialogReassurance");
        OpenFolderButton.Content = Strings.Get("DlgUserDataAlertBtnOpen");
        CancelButton.Content = Strings.Get("BtnCancel");
        RestoreButton.Content = Strings.Get("DlgRestoreDialogBtnRestore");

        BackupsList.ItemsSource = _rows;
        if (_rows.Count > 0)
            BackupsList.SelectedIndex = 0; // newest preselected
    }

    private void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (BackupsList.SelectedItem is not BackupRow row) return;

        try
        {
            PreviousDataSnapshotPath = UserDataService.RestoreBackup(
                _userDataFolderName, row.Info.Path);
            RestoredBackup = row.Info;
            RestorePerformed = true;
            DialogResult = true;
        }
        catch (System.Exception ex)
        {
            DiagnosticLog.Write($"Restore from dialog failed: {ex}");
            MessageBox.Show(this,
                Strings.Format("DlgRestoreFailedBody", ex.Message),
                Strings.Get("DlgRestoreFailedTitle"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        // Open the parent so the user sees the active folder + every .bak side by side
        var folder = UserDataService.GetUserDataFolder(_userDataFolderName);
        var parent = string.IsNullOrEmpty(folder) ? null : Path.GetDirectoryName(folder);
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent)) return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = parent,
                UseShellExecute = true,
            });
        }
        catch (System.Exception ex)
        {
            DiagnosticLog.Write($"Failed to open My Games folder: {ex.Message}");
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    /// <summary>
    /// View-model wrapper around <see cref="UserDataService.BackupInfo"/> with
    /// pre-formatted strings for display in the list. Keeping the formatting
    /// in the dialog instead of the service avoids polluting the service
    /// with localization concerns.
    /// </summary>
    private class BackupRow
    {
        public UserDataService.BackupInfo Info { get; }

        public string DateLabel => Info.CreatedAt == System.DateTime.MinValue
            ? "—"
            : Info.CreatedAt.ToString("yyyy-MM-dd HH:mm");

        public string DetailLabel => Info.SavegameCount > 0
            ? Strings.Format("DlgRestoreDialogRowDetailWithSaves", Info.FileCount, Info.SavegameCount)
            : Strings.Format("DlgRestoreDialogRowDetail", Info.FileCount);

        public string SizeLabel => FormatBytes(Info.TotalBytes);

        public BackupRow(UserDataService.BackupInfo info)
        {
            Info = info;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "—";
            string[] units = { "B", "KB", "MB", "GB" };
            double size = bytes;
            int unit = 0;
            while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
            return $"{size:0.#} {units[unit]}";
        }
    }
}
