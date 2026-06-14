using System.Windows;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Styled alert shown before a fresh install when we detect pre-existing
/// user data for the active mod under Documents. Newer save/metropolis
/// files written by a previous (more up-to-date) install can crash the
/// older binary on startup.
///
/// Three actions:
///   - Backup and continue: rename <c>Documents\My Games\&lt;folder&gt;</c>
///     to <c>&lt;folder&gt;.bak.&lt;timestamp&gt;</c>
///   - Open folder: launch Explorer so the user can clean up by hand
///   - Ignore: dismiss; the user takes the risk
///
/// The dialog is mod-agnostic: every visible string is templated with the
/// active mod's display name, and the underlying save folder name comes
/// from <see cref="ModProfile.UserDataFolder"/>.
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
    private readonly string _modDisplayName;
    private readonly string _userDataFolderName;

    /// <summary>
    /// Back-compat overload. Defaults to WoL-labelled copy and folder name.
    /// New call sites should use the three-argument form.
    /// </summary>
    public UserDataAlertDialog(string userDataPath)
        : this(userDataPath, "Wars of Liberty", "Wars of Liberty") { }

    /// <param name="userDataPath">
    /// Absolute path of the existing user-data folder we just detected
    /// (already resolved by <see cref="UserDataService.GetUserDataFolder(string)"/>).
    /// Displayed as the "FOUND AT" value.
    /// </param>
    /// <param name="modDisplayName">
    /// Display name of the active mod — templated into the dialog's header,
    /// description and recommendation lines so they read with the right
    /// mod's name regardless of which mod's install we're about to run.
    /// </param>
    /// <param name="userDataFolderName">
    /// Relative folder name under <c>Documents\My Games\</c> for this mod
    /// (i.e. <see cref="ModProfile.UserDataFolder"/>). Passed through to
    /// <see cref="UserDataService"/> so the backup and savegame-count
    /// methods know which folder to look at.
    /// </param>
    public UserDataAlertDialog(string userDataPath, string modDisplayName, string userDataFolderName)
    {
        InitializeComponent();
        _userDataPath = userDataPath;
        _modDisplayName = string.IsNullOrEmpty(modDisplayName) ? "the mod" : modDisplayName;
        _userDataFolderName = userDataFolderName ?? "";

        Title = Strings.Get("DlgUserDataAlertTitle");
        TitleBarControl.Title = Strings.Get("DlgUserDataAlertTitle");
        HeaderText.Text = Strings.Format("DlgUserDataAlertHeader", _modDisplayName);
        DescriptionText.Text = Strings.Format("DlgUserDataAlertDescription", _modDisplayName);
        WarningTitleText.Text = Strings.Get("DlgUserDataAlertFoundLabel");
        UserDataPathText.Text = userDataPath;
        RecommendationText.Text = Strings.Format("DlgUserDataAlertRecommendation", _modDisplayName);

        // If the user has saved games / metropolises, surface the count —
        // it's the most relevant piece of info for the compatibility risk.
        var savegameCount = UserDataService.CountSavegameFiles(_userDataFolderName);
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
        var newPath = UserDataService.BackupUserData(_userDataFolderName);
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
        UserDataService.OpenUserDataFolder(_userDataFolderName);
        // Don't close the dialog — the user might want to inspect and then
        // decide between Backup and Ignore.
    }

    private void IgnoreButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
