using System.Windows;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Styled alert shown after a fresh install when we detect pre-existing
/// WoL user data under Documents. The freshly installed payload is the
/// 1.0.15d base — newer save/metropolis files written by a previous (more
/// up-to-date) install of WoL can crash the older binary on startup.
///
/// Three actions:
///   - Backup and continue: rename Documents\Wars of Liberty to .bak.&lt;timestamp&gt;
///   - Open folder: launch Explorer so the user can clean up by hand
///   - Ignore: dismiss; the user takes the risk
/// </summary>
public partial class UserDataAlertDialog : Window
{
    /// <summary>
    /// True if the user clicked "Backup and continue" AND the backup
    /// succeeded. Lets the caller report status accurately.
    /// </summary>
    public bool BackupPerformed { get; private set; }

    /// <summary>
    /// The new path of the backed-up folder, or null if no backup happened.
    /// </summary>
    public string? BackupPath { get; private set; }

    private readonly string _userDataPath;

    public UserDataAlertDialog(string userDataPath)
    {
        InitializeComponent();
        _userDataPath = userDataPath;

        Title = Strings.Get("DlgUserDataAlertTitle");
        HeaderText.Text = Strings.Get("DlgUserDataAlertHeader");
        DescriptionText.Text = Strings.Get("DlgUserDataAlertDescription");
        WarningTitleText.Text = Strings.Get("DlgUserDataAlertFoundLabel");
        UserDataPathText.Text = userDataPath;
        RecommendationText.Text = Strings.Get("DlgUserDataAlertRecommendation");

        // If the user has saved games / metropolises, surface the count —
        // it's the most relevant piece of info for the compatibility risk.
        var savegameCount = UserDataService.CountSavegameFiles();
        if (savegameCount > 0)
        {
            SavegameCountText.Text = Strings.Format("DlgUserDataAlertSavegameCount", savegameCount);
            SavegameCountText.Visibility = Visibility.Visible;
        }

        OpenFolderButton.Content = Strings.Get("DlgUserDataAlertBtnOpen");
        IgnoreButton.Content = Strings.Get("DlgUserDataAlertBtnIgnore");
        BackupButton.Content = Strings.Get("DlgUserDataAlertBtnBackup");
    }

    private void BackupButton_Click(object sender, RoutedEventArgs e)
    {
        var newPath = UserDataService.BackupUserData();
        BackupPerformed = newPath != null;
        BackupPath = newPath;

        if (!BackupPerformed)
        {
            // Surface the error inline rather than silently failing
            MessageBox.Show(this,
                Strings.Get("DlgUserDataAlertBackupFailedBody"),
                Strings.Get("DlgUserDataAlertBackupFailedTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        UserDataService.OpenUserDataFolder();
        // Don't close the dialog — the user might want to inspect and then
        // decide between Backup and Ignore.
    }

    private void IgnoreButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
