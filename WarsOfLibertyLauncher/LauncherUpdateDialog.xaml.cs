using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Dialog that drives a launcher self-update: shows download progress with a
/// bar, percentage, speed and ETA, then prompts the user to restart so the
/// new launcher binary takes over.
/// </summary>
public partial class LauncherUpdateDialog : Window
{
    private enum Phase { Idle, Downloading, ReadyToRestart, Failed }

    private readonly LauncherUpdateService.UpdateCheckResult _update;
    private CancellationTokenSource? _cts;
    private Phase _phase = Phase.Idle;

    public LauncherUpdateDialog(LauncherUpdateService.UpdateCheckResult update)
    {
        InitializeComponent();
        _update = update;

        Title = Strings.Get("DlgLauncherUpdateTitle");
        HeaderText.Text = Strings.Get("DlgLauncherUpdateTitle");
        VersionInfoText.Text = Strings.Format(
            "DlgLauncherUpdateVersionInfo",
            update.CurrentVersion,
            update.LatestVersion,
            FormatBytes(update.DownloadSize));

        ProgressLabelText.Text = Strings.Get("DlgLauncherUpdateReadyToDownload");
        StatusText.Text = Strings.Get("DlgLauncherUpdateConfirmPrompt");

        ActionButton.Content = Strings.Get("DlgLauncherUpdateBtnDownload");
        CancelButton.Content = Strings.Get("BtnCancel");
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_phase == Phase.Downloading)
        {
            _cts?.Cancel();
            return;
        }

        DialogResult = false;
    }

    private async void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_phase == Phase.ReadyToRestart)
        {
            // The user clicked "Restart now" — start the new binary and close.
            try
            {
                LauncherUpdateService.RelaunchUpdated();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error: {ex.Message}";
                return;
            }
            DialogResult = true;
            Application.Current.Shutdown();
            return;
        }

        if (_phase == Phase.Failed)
        {
            DialogResult = false;
            return;
        }

        // Idle → start the download
        _phase = Phase.Downloading;
        ActionButton.IsEnabled = false;
        ProgressLabelText.Text = Strings.Get("DlgLauncherUpdateDownloading");
        StatusText.Text = "";
        _cts = new CancellationTokenSource();

        var speed = new SpeedTracker();
        var progress = new Progress<DownloadProgress>(p =>
        {
            speed.Sample(p.BytesReceived);
            DownloadProgress.Value = p.Percentage;
            ProgressPercentText.Text = $"{p.Percentage:0.0}%  ({FormatBytes(p.BytesReceived)} / {FormatBytes(p.TotalBytes)})";
            SpeedText.Text = speed.BytesPerSecond > 0
                ? Strings.Format("ProgressSpeed", FormatBytes((long)speed.BytesPerSecond))
                : "";
            var eta = speed.EstimateTimeRemaining(p.TotalBytes - p.BytesReceived);
            EtaText.Text = eta.HasValue
                ? Strings.Format("ProgressEta", FormatDuration(eta.Value))
                : "";
        });

        try
        {
            await LauncherUpdateService.DownloadUpdateAsync(_update.DownloadUrl!, progress, _cts.Token);

            _phase = Phase.ReadyToRestart;
            ActionButton.IsEnabled = true;
            ActionButton.Content = Strings.Get("DlgLauncherUpdateBtnRestart");
            CancelButton.Content = Strings.Get("DlgLauncherUpdateBtnRestartLater");
            DownloadProgress.Value = 100;
            ProgressLabelText.Text = Strings.Get("DlgLauncherUpdateDownloadComplete");
            StatusText.Text = Strings.Get("DlgLauncherUpdateRestartPrompt");
            SpeedText.Text = "";
            EtaText.Text = "";
        }
        catch (OperationCanceledException)
        {
            DialogResult = false;
        }
        catch (Exception ex)
        {
            _phase = Phase.Failed;
            ActionButton.IsEnabled = true;
            ActionButton.Content = Strings.Get("BtnClose");
            StatusText.Text = $"Error: {ex.Message}";
            DiagnosticLog.Write($"Launcher self-update failed: {ex}");
        }
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
