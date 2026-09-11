using System;
using System.ComponentModel;
using System.Windows;
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
public partial class StartupUpdateWindow : Window
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

    public StartupUpdateWindow(string latestVersion)
    {
        InitializeComponent();

        // Both: TitleBar mirrors its Title into the Window's only when the window set none,
        // and this window IS in the taskbar (it is the process's only one), so the taskbar
        // button would otherwise read as the class name.
        Title = Strings.Get("StartupUpdateTitle");
        Chrome.Title = Strings.Get("StartupUpdateTitle");
        HeaderText.Text = Strings.Get("StartupUpdateTitle");
        BodyText.Text = Strings.Format("StartupUpdateBody", latestVersion);
        ProgressLabelText.Text = Strings.Get("StartupUpdateProgressLabel");
        SkipButton.Content = Strings.Get("StartupUpdateBtnSkip");
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
        ProgressPercentText.Text =
            $"{p.Percentage:0.0}%  ({FormatBytes(p.BytesReceived)} / {FormatBytes(p.TotalBytes)})";
        SpeedText.Text = _speed.BytesPerSecond > 0
            ? Strings.Format("ProgressSpeed", FormatBytes((long)_speed.BytesPerSecond))
            : "";
        var eta = _speed.EstimateTimeRemaining(p.TotalBytes - p.BytesReceived);
        EtaText.Text = eta.HasValue
            ? Strings.Format("ProgressEta", FormatDuration(eta.Value))
            : "";
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
        SpeedText.Text = "";
        EtaText.Text = "";
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

    private void SkipButton_Click(object sender, RoutedEventArgs e) => CloseByOwner_Skip();

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

    private static string FormatDuration(TimeSpan ts)
    {
        if (ts.TotalSeconds < 60) return $"{(int)ts.TotalSeconds}s";
        if (ts.TotalMinutes < 60) return $"{(int)ts.TotalMinutes}m {ts.Seconds:00}s";
        return $"{(int)ts.TotalHours}h {ts.Minutes:00}m";
    }
}
