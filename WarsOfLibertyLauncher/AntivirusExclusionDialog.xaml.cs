using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
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
/// real problem, which is that the user cannot guess which paths to exclude. The
/// "Open Windows Security" link only OPENS that screen; it writes nothing.</para>
/// </summary>
public partial class AntivirusExclusionDialog : Window
{
    /// <summary>Long enough to notice, short enough not to hide the caption.</summary>
    private static readonly TimeSpan CopiedFlash = TimeSpan.FromSeconds(1.4);

    /// <summary>Windows Security's virus-and-threat settings page. Opening it is all this
    /// does — the same thing the user would reach through the four steps by hand.</summary>
    internal const string WindowsSecurityUri = "windowsdefender://threatsettings";

    private readonly string _clipboardText;
    private DispatcherTimer? _flashTimer;

    internal AntivirusExclusionDialog(string modDisplayName, string file, string installFolder, bool preventive)
    {
        InitializeComponent();

        var hasInstallFolder = !string.IsNullOrWhiteSpace(installFolder);

        Chrome.Title = Strings.Get(preventive ? "DlgAntivirusTitleNotice" : "DlgAntivirusTitleBlocked");
        HeadlineText.Text = Strings.Get(preventive ? "DlgAntivirusNoticeHeadline" : "DlgAntivirusBlockedHeadline");
        FileTagText.Text = file;
        FileTag.Tag = preventive ? null : "danger";
        FileTagCaption.Text = Strings.Get(preventive ? "DlgAntivirusNoticeTag" : "DlgAntivirusBlockedTag");

        if (preventive)
        {
            Controls.MarkedText.Set(BodyText, Strings.Format(
                hasInstallFolder ? "DlgAntivirusNoticeBody" : "DlgAntivirusNoticeBodyOne",
                string.IsNullOrWhiteSpace(modDisplayName) ? "the mod" : modDisplayName));
        }
        else
        {
            Controls.MarkedText.Set(BodyText, Strings.Get(
                hasInstallFolder ? "DlgAntivirusBlockedBody" : "DlgAntivirusBlockedBodyOne"));
        }

        ApplyShield(preventive);

        FoldersTitle.Text = Strings.Get("DlgAntivirusFoldersTitle");
        CopyButton.Content = Strings.Get(hasInstallFolder ? "DlgAntivirusCopyBoth" : "DlgAntivirusCopyOne");
        TempRowLabel.Text = Strings.Get("DlgAntivirusRowDownload");
        InstallRowLabel.Text = Strings.Get("DlgAntivirusRowModFolder");
        StepsTitle.Text = Strings.Get("DlgAntivirusStepsTitle");
        OpenSecurityButton.Content = Strings.Get("DlgAntivirusOpenSecurity");
        BuildSteps();
        SupportLinkHost.Content = Controls.SupportLink.Build();

        var tempRoot = AppPaths.InstallTempRoot;
        TempPathText.Text = PathDisplay.BreakAtSeparators(tempRoot);

        // The install folder is unknown on some paths (a failure before the
        // destination was resolved). Showing an empty box would read as a bug, and
        // copying a blank line would be worse than copying one real path.
        InstallPathText.Text = hasInstallFolder ? PathDisplay.BreakAtSeparators(installFolder) : "";
        InstallRow.Visibility = hasInstallFolder ? Visibility.Visible : Visibility.Collapsed;

        // The clipboard gets the untouched paths: the display text carries invisible
        // break marks that would make a pasted path look right and not resolve.
        _clipboardText = hasInstallFolder
            ? tempRoot + Environment.NewLine + installFolder
            : tempRoot;

        if (preventive)
        {
            DontShowAgainCheck.Content = Strings.Get("DlgAntivirusDontShowAgain");
            ContinueButton.Content = Strings.Get("DlgAntivirusContinueInstall");
            CancelButton.Content = Strings.Get("DlgAntivirusCancel");
        }
        else
        {
            // Someone who hit the real failure needs these paths regardless of what
            // they dismissed earlier, so this mode offers no way to silence it.
            DontShowAgainCheck.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;
            ContinueButton.Content = Strings.Get("DlgAntivirusClose");
            ContinueButton.MinWidth = 120;
        }
    }

    /// <summary>Amber shield with "!" before installing; red with "✕" once a file is gone.
    /// It used to be the lock glyph, which says "all safe" — the opposite of the notice.</summary>
    private void ApplyShield(bool preventive)
    {
        var (top, bottom, ink, glyph) = preventive
            ? (Color.FromRgb(0xF0, 0xC5, 0x6A), Color.FromRgb(0xB8, 0x80, 0x1F), Color.FromRgb(0x2B, 0x1A, 0x02), "!")
            : (Color.FromRgb(0xE0, 0x85, 0x8B), Color.FromRgb(0x9C, 0x3A, 0x42), Color.FromRgb(0x2A, 0x05, 0x08), "✕");
        ShieldPath.Fill = new LinearGradientBrush(top, bottom, 90);
        ShieldGlyph.Text = glyph;
        ShieldGlyph.Foreground = new SolidColorBrush(ink);
    }

    /// <summary>The four steps in Windows Security, numbered, instead of one arrow sentence.</summary>
    private void BuildSteps()
    {
        StepsHost.Children.Clear();
        for (int i = 1; i <= 4; i++)
        {
            var row = new Grid { Margin = new Thickness(0, i == 1 ? 0 : 5, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock
            {
                Text = i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                FontFamily = (FontFamily)FindResource("MonoFont"),
                FontSize = (double)FindResource("SetGroupLabelSize"),
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("UiTextGhost"),
                Margin = new Thickness(0, 1, 10, 0),
            });
            var text = new TextBlock
            {
                FontSize = (double)FindResource("SetControlSize"),
                Foreground = (Brush)FindResource("MpTextBody"),
                TextWrapping = TextWrapping.Wrap,
            };
            Controls.MarkedText.Set(text, Strings.Get("DlgAntivirusStep" + i));
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            StepsHost.Children.Add(row);
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
        var dlg = new AntivirusExclusionDialog(modDisplayName, antivirusFile, installFolder, preventive: true);

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
        var dlg = new AntivirusExclusionDialog("", blockedFile, installFolder, preventive: false);

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
            // kill the dialog.
            DiagnosticLog.Write($"Antivirus dialog: could not copy paths: {ex.Message}");
            return;
        }

        var idle = CopyButton.Content;
        CopyButton.Content = Strings.Get("DlgAntivirusCopied");
        CopyButton.Tag = "ok";

        _flashTimer?.Stop();
        _flashTimer = new DispatcherTimer { Interval = CopiedFlash };
        _flashTimer.Tick += (_, _) =>
        {
            _flashTimer?.Stop();
            _flashTimer = null;
            CopyButton.Content = idle;
            CopyButton.Tag = null;
        };
        _flashTimer.Start();
    }

    private void OpenSecurityButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(WindowsSecurityUri) { UseShellExecute = true });
            DiagnosticLog.Write("Antivirus dialog: opened Windows Security.");
        }
        catch (Exception ex)
        {
            // Some editions ship without Windows Security, or a third-party antivirus
            // replaced it. The steps above still say where to go.
            DiagnosticLog.Write($"Antivirus dialog: could not open Windows Security: {ex.Message}");
        }
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
