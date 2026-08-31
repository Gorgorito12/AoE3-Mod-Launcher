using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Explains, once, that the launcher is running under a different Windows account than the one
/// signed in — so the player's recordings, saves, decks and launcher settings are landing in that
/// other account's folders (see <see cref="RunningAccount"/> for the measured case that prompted
/// this).
///
/// <para><b>It shows the folders and copies them; it never opens them.</b> Launching
/// <c>explorer.exe</c> from a process running as the other account opens the shell as that account
/// too, which is more of exactly the thing this dialog is trying to explain. Copying the path lets
/// the player paste it into their own Explorer, in their own session.</para>
///
/// <para><b>Informational only.</b> Nothing here moves a file, writes into another account's
/// profile or registry, or changes which account anything runs as — the launcher cannot fix the
/// split without launching the game under a borrowed token, and that is the pattern antivirus
/// heuristics punish. The remedy is the player's, so the dialog's job is to make it obvious.</para>
/// </summary>
public partial class CrossUserAccountDialog : Window
{
    /// <summary>Long enough to notice, short enough not to hide the caption.</summary>
    private static readonly TimeSpan CopiedFlash = TimeSpan.FromSeconds(1.4);

    private readonly string _clipboardText;
    private DispatcherTimer? _flashTimer;

    /// <summary>
    /// Internal rather than private so <c>DialogXamlTests</c> can build it without showing it: this
    /// window only ever opens on a misconfigured machine, so an unresolved <c>{StaticResource}</c>
    /// here would first be seen by the one person who cannot debug it.
    /// </summary>
    internal CrossUserAccountDialog(
        RunningAccount.AccountInfo info, string currentFolder, string? otherFolder)
    {
        InitializeComponent();

        Chrome.Title = Strings.Get("DlgCrossUserTitle");
        BodyText.Text = Strings.Format("DlgCrossUserBody", info.ProcessUser, info.SessionUser);
        AutoStartText.Text = Strings.Get("DlgCrossUserAutoStart");
        HowToText.Text = Strings.Get("DlgCrossUserHowTo");
        CopyButton.Content = Strings.Get("DlgCrossUserCopyPaths");
        CloseButton.Content = Strings.Get("DlgCrossUserClose");

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

        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(currentFolder)) lines.Add(currentFolder);
        if (hasOther) lines.Add(otherFolder!);
        _clipboardText = string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Shows the notice. There is nothing to decide, so there is no return value — the caller has
    /// already recorded that it was shown, before opening it.
    /// </summary>
    public static void ShowNotice(
        Window? owner, RunningAccount.AccountInfo info, string currentFolder, string? otherFolder)
    {
        var dlg = new CrossUserAccountDialog(info, currentFolder, otherFolder);
        if (owner != null) dlg.Owner = owner;
        dlg.ShowDialog();
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_clipboardText.Length == 0) return;

        try
        {
            Clipboard.SetText(_clipboardText);
        }
        catch (Exception ex)
        {
            // Another process can hold the clipboard open; failing to copy must not kill the
            // dialog, and the paths are selectable text right above.
            DiagnosticLog.Write($"Cross-user dialog: could not copy paths: {ex.Message}");
            return;
        }

        CopyButton.Content = Strings.Get("DlgCrossUserCopied");

        _flashTimer?.Stop();
        _flashTimer = new DispatcherTimer { Interval = CopiedFlash };
        _flashTimer.Tick += (_, _) =>
        {
            _flashTimer?.Stop();
            _flashTimer = null;
            CopyButton.Content = Strings.Get("DlgCrossUserCopyPaths");
        };
        _flashTimer.Start();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        // A DispatcherTimer keeps a reference to its handler, which keeps this window alive after
        // it closes.
        _flashTimer?.Stop();
        _flashTimer = null;
        base.OnClosed(e);
    }
}
