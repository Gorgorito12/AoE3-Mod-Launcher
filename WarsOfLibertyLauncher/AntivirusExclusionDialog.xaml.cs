using System;
using System.Windows;
using System.Windows.Threading;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Names the folders the user should add to their antivirus exclusions, and lets
/// them copy both with one click.
///
/// <para><b>Two modes, one dialog.</b> The PREVENTIVE mode runs before installing a
/// mod that declares a known false positive
/// (<see cref="Models.ModProfile.AntivirusFalsePositiveFile"/>); the BLOCKED mode
/// runs after an antivirus actually removed a payload file and
/// <see cref="PayloadFileBlockedException"/> aborted the install. They show the same
/// paths and the same copy button, so they are one dialog rather than two that would
/// drift apart — only the explanation, the buttons and the "don't show again"
/// checkbox differ.</para>
///
/// <para><b>The launcher never edits antivirus configuration.</b> No elevation, no
/// <c>Add-MpPreference</c>: an installer that excludes itself from Defender is
/// exactly the behaviour AV heuristics punish, and this project already fights that
/// battle over its own executable. Naming the two folders is the honest fix for the
/// real problem, which is that the user cannot guess which paths to exclude.</para>
/// </summary>
public partial class AntivirusExclusionDialog : Window
{
    /// <summary>Long enough to notice, short enough not to hide the caption.</summary>
    private static readonly TimeSpan CopiedFlash = TimeSpan.FromSeconds(1.4);

    private readonly string _clipboardText;
    private DispatcherTimer? _flashTimer;

    private AntivirusExclusionDialog(string title, string body, string installFolder, bool preventive)
    {
        InitializeComponent();

        Chrome.Title = title;
        BodyText.Text = body;
        PathsLabel.Text = Strings.Get("DlgAntivirusPathsLabel");
        HowToText.Text = Strings.Get("DlgAntivirusHowTo");
        CopyButton.Content = Strings.Get("DlgAntivirusCopyPaths");

        var tempRoot = AppPaths.InstallTempRoot;
        TempPathText.Text = tempRoot;

        // The install folder is unknown on some paths (a failure before the
        // destination was resolved). Showing an empty box would read as a bug, and
        // copying a blank line would be worse than copying one real path.
        var hasInstallFolder = !string.IsNullOrWhiteSpace(installFolder);
        InstallPathText.Text = hasInstallFolder ? installFolder : "";
        InstallPathText.Visibility = hasInstallFolder ? Visibility.Visible : Visibility.Collapsed;

        _clipboardText = hasInstallFolder
            ? tempRoot + Environment.NewLine + installFolder
            : tempRoot;

        if (preventive)
        {
            DontShowAgainCheck.Content = Strings.Get("DlgAntivirusDontShowAgain");
            ContinueButton.Content = Strings.Get("DlgAntivirusContinue");
            CancelButton.Content = Strings.Get("DlgAntivirusCancel");
        }
        else
        {
            // Someone who hit the real failure needs these paths regardless of what
            // they dismissed earlier, so this mode offers no way to silence it.
            DontShowAgainCheck.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;
            ContinueButton.Content = Strings.Get("DlgAntivirusClose");
        }
    }

    /// <summary>True when the user ticked "don't show this again" (preventive only).</summary>
    public bool DontShowAgain => DontShowAgainCheck.IsChecked == true;

    /// <summary>
    /// Warns BEFORE a multi-GB install that this mod carries a file antivirus is
    /// known to remove. Returns false when the user cancelled, in which case the
    /// install must not start.
    /// </summary>
    public static bool ShowNotice(
        Window? owner, string modDisplayName, string antivirusFile,
        string installFolder, out bool dontShowAgain)
    {
        var dlg = new AntivirusExclusionDialog(
            Strings.Get("DlgAntivirusTitleNotice"),
            Strings.Format("DlgAntivirusNoticeBody", modDisplayName, antivirusFile),
            installFolder,
            preventive: true);

        if (owner != null) dlg.Owner = owner;

        var proceed = dlg.ShowDialog() == true;
        // Read the checkbox even when they cancelled: "I know, don't ask me again"
        // is a statement about the notice, not about this particular install.
        dontShowAgain = dlg.DontShowAgain;
        return proceed;
    }

    /// <summary>
    /// Explains an install that an antivirus just broke, and names the folders to
    /// exclude before trying again.
    /// </summary>
    public static void ShowBlocked(Window? owner, string blockedFile, string installFolder)
    {
        var dlg = new AntivirusExclusionDialog(
            Strings.Get("DlgAntivirusTitleBlocked"),
            Strings.Format("DlgAntivirusBlockedBody", blockedFile),
            installFolder,
            preventive: false);

        if (owner != null) dlg.Owner = owner;
        dlg.ShowDialog();
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_clipboardText);
        }
        catch (Exception ex)
        {
            // Another process can hold the clipboard open; failing to copy must not
            // kill the dialog, and the paths are selectable text right above.
            DiagnosticLog.Write($"Antivirus dialog: could not copy paths: {ex.Message}");
            return;
        }

        CopyButton.Content = Strings.Get("DlgAntivirusCopied");

        _flashTimer?.Stop();
        _flashTimer = new DispatcherTimer { Interval = CopiedFlash };
        _flashTimer.Tick += (_, _) =>
        {
            _flashTimer?.Stop();
            _flashTimer = null;
            CopyButton.Content = Strings.Get("DlgAntivirusCopyPaths");
        };
        _flashTimer.Start();
    }

    private void ContinueButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        // A DispatcherTimer keeps a reference to its handler, which keeps this
        // window alive after it closes.
        _flashTimer?.Stop();
        _flashTimer = null;
        base.OnClosed(e);
    }
}
