using System.Windows;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Explains, once, that the launcher is running under a different Windows account than the one
/// signed in — so the player's recordings, saves, decks and launcher settings are landing in that
/// other account's folders (see <see cref="RunningAccount"/> for the measured case that prompted
/// this).
///
/// <para><b>It shows the folders; it never opens them.</b> Launching <c>explorer.exe</c> from a
/// process running as the other account opens the shell as that account too, which is more of
/// exactly the thing this dialog is trying to explain. The paths are selectable text, so the
/// player can paste them into their own Explorer, in their own session.</para>
///
/// <para><b>Informational only.</b> Nothing here moves a file, writes into another account's
/// profile or registry, or changes which account anything runs as — the launcher cannot fix the
/// split without launching the game under a borrowed token, and that is the pattern antivirus
/// heuristics punish. The remedy is the player's, so the dialog's job is to make it obvious.</para>
/// </summary>
public partial class CrossUserAccountDialog : BaseDialog
{
    /// <summary>
    /// Internal rather than private so <c>DialogXamlTests</c> can build it without showing it: this
    /// window only ever opens on a misconfigured machine, so an unresolved <c>{StaticResource}</c>
    /// here would first be seen by the one person who cannot debug it.
    /// </summary>
    internal CrossUserAccountDialog(
        RunningAccount.AccountInfo info, string currentFolder, string? otherFolder)
    {
        InitializeComponent();

        SetBarTitle(Strings.Get("DlgCrossUserBar"));
        SetHeadline(Strings.Get("DlgCrossUserTitle"));
        SetLead(Strings.Format("DlgCrossUserBody", info.ProcessUser, info.SessionUser));
        AutoStartText.Text = Strings.Get("DlgCrossUserAutoStart");
        HowToText.Text = Strings.Get("DlgCrossUserHowTo");
        PrimaryAction.Content = Strings.Get("DlgCrossUserClose");
        HideCancel();

        CurrentLabel.Text = Strings.Format("DlgCrossUserWhereNow", info.ProcessUser);
        CurrentPathText.Text = currentFolder;

        // The other account's folder is resolved exactly or not at all, so it is genuinely absent
        // sometimes. An empty box under a caption reads as a bug, and a guessed path would be
        // worse than no path — see RunningAccount.ProfileFolderOf.
        var hasOther = !string.IsNullOrWhiteSpace(otherFolder);
        OtherLabel.Text = hasOther ? Strings.Format("DlgCrossUserWhereYours", info.SessionUser) : "";
        OtherLabel.Visibility = hasOther ? Visibility.Visible : Visibility.Collapsed;
        OtherPathText.Text = hasOther ? otherFolder : "";
        OtherPathText.Visibility = hasOther ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Shows the notice. There is nothing to decide, so there is no return value — the caller has
    /// already recorded that it was shown, before opening it.
    /// </summary>
    public static void ShowNotice(
        Window? owner, RunningAccount.AccountInfo info, string currentFolder, string? otherFolder)
        => new CrossUserAccountDialog(info, currentFolder, otherFolder).ShowOver(owner);
}
