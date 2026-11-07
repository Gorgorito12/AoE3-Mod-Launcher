using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher;

/// <summary>
/// The small window the launcher shows while it replaces itself, BEFORE the main window
/// exists.
///
/// <para>It reports; it does not decide. <see cref="StartupUpdateGate"/> owns the download
/// and the restart — this only paints progress and offers the one escape hatch, so the whole
/// surface can be replaced without touching the policy.</para>
///
/// <para><b>Two things differ from every other small dialog in the launcher and both are
/// forced.</b> <c>WindowStartupLocation</c> is <c>CenterScreen</c>, not <c>CenterOwner</c>:
/// at this point in <c>App.OnStartup</c> there is no <c>MainWindow</c> to own it. And
/// <c>ShowInTaskbar</c> is <c>True</c>, where a modal child would be False: this is the only
/// window the process has, so with no taskbar button a user who clicks away has no way back
/// to it.</para>
///
/// <para>Closing it is the same as pressing Skip — the ✕ is the affordance people reach for
/// first, and a window you cannot dismiss in front of a launcher that has not opened yet is
/// the worst possible first impression of a feature nobody asked for.</para>
/// </summary>
public partial class StartupUpdateWindow : BaseDialog
{
    private readonly SpeedTracker _speed = new();
    private bool _closingByOwner;
    private bool _verifying;

    /// <summary>
    /// Raised when the user declines this update — the Skip button, the ✕, Alt+F4. The gate
    /// cancels the download and carries on into the normal launcher, where the gold pill
    /// still offers the update by hand.
    /// </summary>
    public event Action? SkipRequested;

    /// <summary>The download failed and the user asked to try it again (103g).</summary>
    public event Action? RetryRequested;

    internal TextBlock HeaderText => HeadlineText;
    internal Button SkipButton => CancelAction;

    public StartupUpdateWindow(string latestVersion)
    {
        InitializeComponent();

        // Both: TitleBar mirrors its Title into the Window's only when the window set none,
        // and this window IS in the taskbar (it is the process's only one), so the taskbar
        // button would otherwise read as the class name.
        SetBarTitle(Strings.Get("StartupUpdateTitle"));
        SetHeadline(Strings.Format("StartupUpdateHeadline", latestVersion));
        SetLead(Strings.Format("StartupUpdateBody", latestVersion));
        ProgressLabelText.Text = Strings.Get("StartupUpdateProgressLabel");
        SkipButton.Content = Strings.Get("StartupUpdateBtnSkip");
        SkipButton.MinWidth = 150;
        // Not a modal: IsCancel would ask a window with no DialogResult for one.
        SkipButton.IsCancel = false;
        HidePrimary();
    }

    /// <summary>
    /// The download failed: the amber tone, what failed, and a way to try again. The way out stays
    /// the same button, so an update is still never what keeps somebody out of the launcher.
    /// </summary>
    public void ShowFailed(string detail)
    {
        Tone = DialogTone.Caution;
        Width = 480;
        SetHeadline(Strings.Get("StartupUpdateFailedTitle"));
        SetLead(Strings.Get("StartupUpdateFailedBody"));
        SetDetail(Strings.Get("DlgWhatFailed"), detail);
        ProgressBlock.Visibility = Visibility.Collapsed;
        Body = null;
        PrimaryAction.Content = Strings.Get("StartupUpdateRetry");
        PrimaryAction.Visibility = Visibility.Visible;
        PrimaryAction.IsDefault = true;
        SkipButton.IsEnabled = true;
        Chrome.ShowClose = true;
    }

    /// <summary>Back to the progress bar for another attempt.</summary>
    public void ShowRetrying(string latestVersion)
    {
        Tone = DialogTone.None;
        Width = 460;
        SetHeadline(Strings.Format("StartupUpdateHeadline", latestVersion));
        SetLead(Strings.Format("StartupUpdateBody", latestVersion));
        SetDetail(null, null);
        ProgressBlock.Visibility = Visibility.Visible;
        Body = ProgressBlock;
        ProgressLabelText.Text = Strings.Get("StartupUpdateProgressLabel");
        DownloadProgress.Value = 0;
        HidePrimary();
        _verifying = false;
    }

    /// <summary>Paint one download tick. Safe to call from the download's progress callback.</summary>
    public void Report(DownloadProgress p)
    {
        // The last byte is not the end: the service hashes the file and checks its signature
        // before anything is swapped, which takes seconds on a ~170 MB binary. Saying so HERE
        // rather than after the await is the only way the label is ever true - the caller only
        // regains control once verification has already finished.
        if (p.Percentage >= 100 && !_verifying)
        {
            _verifying = true;
            ShowVerifying();
            return;
        }

        _speed.Sample(p.BytesReceived);
        DownloadProgress.Value = p.Percentage;
        ProgressPercentText.Text = $"{p.Percentage:0} %";
        var amount = Strings.Format("StartupUpdateAmount", FormatBytes(p.BytesReceived), FormatBytes(p.TotalBytes));
        ProgressLabelText.Text = _speed.BytesPerSecond > 0
            ? $"{amount} · {FormatBytes((long)_speed.BytesPerSecond)}/s"
            : amount;
    }

    /// <summary>
    /// The download finished and the bytes are being checked. Skipping stops being offered
    /// here: verification is seconds, and the next step rewrites the executable.
    /// </summary>
    public void ShowVerifying() => EnterFinalPhase("StartupUpdateVerifying");

    /// <summary>The swap is done and the new binary is starting.</summary>
    public void ShowRestarting() => EnterFinalPhase("StartupUpdateRestarting");

    private void EnterFinalPhase(string labelKey)
    {
        DownloadProgress.Value = 100;
        ProgressLabelText.Text = Strings.Get(labelKey);
        ProgressPercentText.Text = "";
        SkipButton.IsEnabled = false;
        Chrome.ShowClose = false;
    }

    /// <summary>
    /// Close without reading it as a refusal. The gate calls this when the update finished
    /// or failed on its own; without it, tidying the window away would fire
    /// <see cref="SkipRequested"/> and log the launcher's own success as the user's refusal.
    /// </summary>
    public void CloseByOwner()
    {
        _closingByOwner = true;
        Close();
    }

    protected override void OnCancel()
    {
        if (SkipButton.IsEnabled) CloseByOwner_Skip();
    }

    protected override void OnPrimary() => RetryRequested?.Invoke();

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!_closingByOwner) SkipRequested?.Invoke();
    }

    private void CloseByOwner_Skip()
    {
        // Raise first, then close: the handler cancels the download, and OnClosing's own
        // raise is suppressed by the flag so the gate is told exactly once.
        _closingByOwner = true;
        SkipRequested?.Invoke();
        Close();
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "?";
        string[] units = { "B", "KB", "MB", "GB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:0.##} {units[unit]}";
    }
}
