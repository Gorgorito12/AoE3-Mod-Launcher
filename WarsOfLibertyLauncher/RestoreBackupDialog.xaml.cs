using System.Windows;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Styled confirmation dialog shown when the user picks "Restore previous
/// user data" from the gear menu. Replaces the system MessageBox so the
/// confirmation matches the launcher's dark theme.
///
/// Always offers to restore the most recent backup; older backups are
/// mentioned but left untouched (the user can manage them via Explorer).
/// </summary>
public partial class RestoreBackupDialog : Window
{
    public RestoreBackupDialog(UserDataService.BackupInfo newest, int olderCount)
    {
        InitializeComponent();

        Title = Strings.Get("DlgRestoreConfirmTitle");
        HeaderText.Text = Strings.Get("DlgRestoreConfirmTitle");
        DescriptionText.Text = Strings.Get("DlgRestoreDialogDescription");

        BackupTitleText.Text = Strings.Get("DlgRestoreSelectedBackupLabel");
        BackupDetailsText.Text = Strings.Format(
            "DlgRestoreSelectedBackupDetail",
            newest.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
            newest.FileCount);

        ExplanationText.Text = Strings.Get("DlgRestoreExplanation");

        if (olderCount > 0)
        {
            OlderBackupsText.Text = Strings.Format(
                "DlgRestoreOlderBackupsNote", olderCount);
            OlderBackupsText.Visibility = Visibility.Visible;
        }

        RestoreButton.Content = Strings.Get("DlgRestoreBtnRestore");
        CancelButton.Content = Strings.Get("BtnCancel");
    }

    private void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
