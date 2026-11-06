using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher;

/// <summary>
/// The launcher's self-update (design 77). One window through the whole of it: what is new, the
/// download, the check of the file, the restart, and each way that can fail. It opens on "ready"
/// when this session already downloaded and checked the file, so "later" costs nothing.
/// </summary>
public partial class LauncherUpdateDialog : Window
{
    internal enum Phase
    {
        Idle, Downloading, Verifying, Ready,
        FailedDownload, FailedFolder, FailedCheck, FailedRestart, FailedName,
    }

    private static readonly Brush VerifyingBar = FrozenBrush(0x8C, 0x2F, 0x7F, 0xE0);

    private readonly LauncherUpdateService.UpdateCheckResult _update;
    private readonly LauncherConfig? _config;
    private readonly Func<bool>? _inMatch;
    private readonly Func<string?>? _modBusy;
    private readonly bool _isPreview;
    private readonly DiskSpaceShortfall? _shortfall;
    private readonly DispatcherTimer _restartGate = new() { Interval = TimeSpan.FromSeconds(2) };
    private CancellationTokenSource? _cts;
    private Phase _phase = Phase.Idle;
    private string? _notesLink;
    private bool _closed;

    /// <param name="inMatch">True while a game or a match result keeps the launcher needed.</param>
    /// <param name="modBusy">The mod an operation is downloading right now, or null.</param>
    /// <param name="preview">A <c>--preview-dialog</c> state: paints that state and touches nothing.</param>
    public LauncherUpdateDialog(
        LauncherUpdateService.UpdateCheckResult update,
        LauncherConfig? config = null,
        Func<bool>? inMatch = null,
        Func<string?>? modBusy = null,
        string? preview = null,
        bool startNow = false)
    {
        InitializeComponent();
        ModDialogChrome.AttachGlobal(this);
        _update = update;
        _config = config;
        _inMatch = inMatch;
        _modBusy = modBusy;
        _isPreview = preview != null;

        var title = Strings.Get("DlgLauncherUpdateTitle");
        Title = title;
        TitleBarControl.Title = title;
        CurrentVersionText.Text = VersionTag(update.CurrentVersion);
        LatestVersionText.Text = VersionTag(update.LatestVersion);
        DownloadSizeText.Text = update.DownloadSize > 0 ? "\u00B7 " + Mega(update.DownloadSize, "0") : "";
        ReleaseNotesHeader.Text = Strings.Get("DlgLauncherUpdateWhatsNew");
        ReleaseLinkHeader.Text = Strings.Get("DlgLauncherUpdateWhatsNew");
        ReleaseLinkBody.Text = Strings.Get("DlgLauncherUpdateNotesElsewhere");
        SeeNotesButton.Content = Strings.Get("DlgLauncherUpdateSeeNotes");
        SeeOnGitHubLink.Content = Strings.Get("DlgLauncherUpdateSeeOnGitHub");
        ModDialogChrome.AddMarkedText(ReadyText,
            Strings.Format("DlgLauncherUpdateReadyBody", VersionTag(update.LatestVersion)));
        InMatchText.Text = Strings.Get("DlgLauncherUpdateInMatch");
        ModDialogChrome.SetLineHeight(1.25, HeaderText);
        ModDialogChrome.SetLineHeight(1.45, BodyText, LowSpaceText, ReadyText, InMatchText, ModBusyText);
        FillReleaseNotes();

        _shortfall = preview == "link"
            ? new DiskSpaceShortfall("C:\\", 340L * 1024 * 1024, 210L * 1024 * 1024)
            : update.DownloadSize > 0 && !_isPreview ? MeasureRoom(update.DownloadSize) : null;
        if (_shortfall != null)
        {
            LowSpaceText.Inlines.Clear();
            ModDialogChrome.AddMarkedText(LowSpaceText, Strings.Format("DlgLauncherUpdateLowSpace",
                Mega(_shortfall.RequiredBytes, "0"), Mega(_shortfall.FreeBytes, "0"),
                _shortfall.Drive.TrimEnd('\\')));
        }

        _restartGate.Tick += (_, _) => RefreshRestartGate();
        Closed += OnClosed;

        if (preview != null) ShowPreview(preview);
        else ApplyPhase(LauncherUpdateService.IsStaged(update.RemoteTag) ? Phase.Ready : Phase.Idle);

        if (startNow) Loaded += (_, _) =>
        {
            // The same press as the window's own button, gate and all: during a match it does nothing.
            if (ActionButton.IsEnabled) ActionButton_Click(ActionButton, new RoutedEventArgs());
        };
    }

    /// <summary>The new exe lands beside the running one, a volume nothing else here looks at, and
    /// sits next to it until the swap, hence twice the size. Told inside this window, and it does
    /// not stop anybody: the numbers are an estimate and the player knows their disk.</summary>
    private static DiskSpaceShortfall? MeasureRoom(long downloadSize) => DiskSpaceService.Check(
        LauncherUpdateService.GetPendingUpdatePath(), downloadSize * 2, tempPath: null, tempRequired: 0);

    private void ShowPreview(string state)
    {
        switch (state)
        {
            case "down":
                ApplyPhase(Phase.Downloading);
                ShowProgress(TotalBytes() * 42 / 100 + 1, 12.4 * 1024 * 1024, TimeSpan.FromSeconds(8));
                break;
            case "verify":
                ApplyPhase(Phase.Verifying);
                break;
            case "ready" or "game" or "busy":
                ApplyPhase(Phase.Ready);
                break;
            case "fdown":
                ApplyPhase(Phase.FailedDownload);
                break;
            case "fblock":
                ShowFolder(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
                ApplyPhase(Phase.FailedFolder);
                break;
            case "fverify":
                ApplyPhase(Phase.FailedCheck);
                break;
            case "frestart":
                ApplyPhase(Phase.FailedRestart);
                break;
            case "fname":
                ShowNameProblem("Aoe3ModLauncher (1).exe");
                ApplyPhase(Phase.FailedName);
                break;
            default:
                ApplyPhase(Phase.Idle);
                break;
        }
    }

    internal Phase CurrentPhase => _phase;

    /// <summary>Everything that changes between phases, so no path through the download can leave
    /// a button or a notice from the one before.</summary>
    private void ApplyPhase(Phase phase)
    {
        var failed = phase >= Phase.FailedDownload;
        _phase = phase;

        HeaderText.Text = Strings.Get(HeadlineKey(phase));
        FailMark.Visibility = Show(failed);
        if (!failed)
        {
            BodyText.Inlines.Clear();
            BodyText.Visibility = Visibility.Collapsed;
        }
        if (phase != Phase.FailedFolder) FolderBox.Visibility = Visibility.Collapsed;
        if (BodyKey(phase) is { } key)
        {
            BodyText.Text = Strings.Get(key);
            BodyText.Visibility = Visibility.Visible;
        }
        else if (phase == Phase.FailedName)
        {
            BodyText.Visibility = Visibility.Visible;
        }

        var idle = phase == Phase.Idle;
        ReleaseNotesSection.Visibility = Show(idle && ReleaseNotesBody.Children.Count > 0);
        ReleaseLinkSection.Visibility = Show(idle && _notesLink != null);
        LowSpaceNotice.Visibility = Show(idle && _shortfall != null);

        var busy = phase is Phase.Downloading or Phase.Verifying;
        ProgressCard.Visibility = Show(busy);
        if (phase == Phase.Verifying)
        {
            DownloadProgress.Value = 100;
            DownloadProgress.Foreground = VerifyingBar;
            ProgressPercentText.Text = "100 %";
            ProgressBytesText.Text = Mega(TotalBytes(), "0");
            SpeedText.Text = Strings.Get("DlgLauncherUpdateVerifying");
            EtaText.Text = "";
        }
        else if (phase == Phase.Downloading)
        {
            DownloadProgress.Value = 0;
            DownloadProgress.SetResourceReference(ForegroundProperty, "MpAction");
            ProgressPercentText.Text = "0 %";
            ProgressBytesText.Text = Strings.Format("DlgLauncherUpdateBytes",
                Mega(0, "0.0"), Mega(TotalBytes(), "0"));
            SpeedText.Text = "";
            EtaText.Text = "";
        }

        ReadyNotice.Visibility = Show(phase == Phase.Ready);

        SecondaryButton.Content = Strings.Get(phase switch
        {
            Phase.Idle or Phase.Ready => "DlgLauncherUpdateBtnRestartLater",
            Phase.Downloading or Phase.Verifying => "BtnCancel",
            _ => "BtnClose",
        });
        ActionButton.Visibility = Show(!busy);
        ActionButton.IsEnabled = true;
        ActionButton.Content = Strings.Get(phase switch
        {
            Phase.Ready => "DlgLauncherUpdateBtnRestart",
            Phase.FailedDownload or Phase.FailedFolder => "DlgLauncherUpdateBtnRetry",
            Phase.FailedCheck or Phase.FailedRestart or Phase.FailedName => "DlgLauncherUpdateOpenPage",
            _ => "DlgLauncherUpdateBtnDownload",
        });
        OpenReleasePageButton.Content = Strings.Get("DlgLauncherUpdateOpenPage");
        OpenReleasePageButton.Visibility = Show(phase is Phase.FailedDownload or Phase.FailedFolder);

        RefreshRestartGate();
    }

    private static string HeadlineKey(Phase phase) => phase switch
    {
        Phase.Downloading => "DlgLauncherUpdateHeadDown",
        Phase.Verifying => "DlgLauncherUpdateHeadVerify",
        Phase.Ready => "DlgLauncherUpdateHeadReady",
        Phase.FailedDownload => "DlgLauncherUpdateDownloadFailed",
        Phase.FailedFolder => "DlgLauncherUpdateBlocked",
        Phase.FailedCheck => "DlgLauncherUpdateVerifyFailed",
        Phase.FailedRestart => "DlgLauncherUpdateRestartFailed",
        Phase.FailedName => "DlgLauncherUpdateCannotReplaceSelf",
        _ => "DlgLauncherUpdateHeadIdle",
    };

    private static string? BodyKey(Phase phase) => phase switch
    {
        Phase.FailedDownload => "DlgLauncherUpdateDownloadFailedBody",
        Phase.FailedFolder => "DlgLauncherUpdateBlockedBody",
        Phase.FailedCheck => "DlgLauncherUpdateVerifyFailedBody",
        Phase.FailedRestart => "DlgLauncherUpdateRestartFailedBody",
        _ => null,
    };

    /// <summary>"Restart now" waits while a game is open: the launcher has to stay to send the
    /// result. Asked again every two seconds, so the button comes back when the match ends.</summary>
    private void RefreshRestartGate()
    {
        var ready = _phase == Phase.Ready;
        var inMatch = ready && _inMatch?.Invoke() == true;
        InMatchNotice.Visibility = Show(inMatch);
        if (ready) ActionButton.IsEnabled = !inMatch;

        var mod = ready ? _modBusy?.Invoke() : null;
        ModBusyNotice.Visibility = Show(mod != null);
        if (mod != null)
        {
            ModBusyText.Inlines.Clear();
            ModDialogChrome.AddMarkedText(ModBusyText, Strings.Format("DlgLauncherUpdateModBusy", mod));
        }

        var watch = ready && (_inMatch != null || _modBusy != null) && !_closed;
        if (watch != _restartGate.IsEnabled) _restartGate.IsEnabled = watch;
    }

    private void FillReleaseNotes()
    {
        var view = ReleaseNotes.Parse(_update.ReleaseNotes, Strings.Language == Strings.LangEs);
        _notesLink = view.OnlyLink;
        ReleaseNotesBody.Children.Clear();
        foreach (var section in view.Sections)
        {
            var block = new StackPanel();
            if (ReleaseNotesBody.Children.Count > 0) block.Margin = new Thickness(0, 12, 0, 0);
            if (section.Heading.Length > 0)
            {
                var heading = new TextBlock
                {
                    Text = section.Heading,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 6),
                };
                heading.SetResourceReference(TextBlock.FontSizeProperty, "DialogBodySize");
                heading.SetResourceReference(TextBlock.ForegroundProperty, "WindowStrongText");
                block.Children.Add(heading);
            }
            for (var i = 0; i < section.Items.Count; i++)
                block.Children.Add(BuildNoteItem(section.Items[i], first: i == 0));
            ReleaseNotesBody.Children.Add(block);
        }
    }

    private static FrameworkElement BuildNoteItem(ReleaseNotes.Item item, bool first)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap };
        text.SetResourceReference(TextBlock.FontSizeProperty, "SetRowDescSize");
        text.SetResourceReference(TextBlock.ForegroundProperty, "WindowSoftText");
        foreach (var piece in item.Pieces)
        {
            var run = new Run(piece.Text);
            if (piece.Bold)
            {
                run.FontWeight = FontWeights.SemiBold;
                run.Foreground = Brushes.White;
            }
            text.Inlines.Add(run);
        }

        var margin = new Thickness(item.Level * 16, first ? 0 : 6, 0, 0);
        if (!item.Bullet)
        {
            text.Margin = margin;
            return text;
        }

        var row = new Grid { Margin = margin };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        var dot = new TextBlock { Text = "\u2022" };
        dot.SetResourceReference(TextBlock.FontSizeProperty, "DialogBodySize");
        dot.SetResourceReference(TextBlock.ForegroundProperty, "WindowFineText");
        Grid.SetColumn(text, 1);
        row.Children.Add(dot);
        row.Children.Add(text);
        return row;
    }

    private void SeeNotesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_notesLink != null) SafeUrl.TryOpen(_notesLink);
    }

    /// <summary>The page of the version that was offered, not /latest, so its notes carry the
    /// SHA-256 of that same file for anyone who wants to check it by hand.</summary>
    private void OpenReleasePageButton_Click(object sender, RoutedEventArgs e) => OpenReleasePage();

    private void OpenReleasePage()
    {
        var tag = (_update.RemoteTag ?? "").Trim();
        SafeUrl.TryOpen(string.IsNullOrEmpty(tag)
            ? LauncherUpdateService.ReleasesPageUrl
            : $"{LauncherUpdateService.ReleasesPageUrl}/tag/{Uri.EscapeDataString(tag)}");
    }

    private void SecondaryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_phase is Phase.Downloading or Phase.Verifying)
        {
            _cts?.Cancel();
            return;
        }
        DialogResult = false;
    }

    private async void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        switch (_phase)
        {
            case Phase.Ready:
                Restart();
                break;
            case Phase.Idle or Phase.FailedDownload or Phase.FailedFolder:
                await DownloadAsync();
                break;
            default:
                OpenReleasePage();
                break;
        }
    }

    private void Restart()
    {
        if (_isPreview)
        {
            DiagnosticLog.Write("Update dialog preview: restart not run.");
            DialogResult = true;
            return;
        }

        // Persist the new tag BEFORE relaunching so the freshly-started binary sees itself as
        // already on this tag and doesn't loop the prompt. RelaunchUpdated touches only the .exe.
        if (_config != null && !string.IsNullOrEmpty(_update.RemoteTag))
        {
            try
            {
                _config.LastInstalledLauncherTag = _update.RemoteTag;
                _config.SkippedLauncherTag = ""; // no longer relevant
                _config.Save();
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"Failed to persist new launcher tag: {ex.Message}");
            }
        }

        try
        {
            LauncherUpdateService.RelaunchUpdated();
        }
        catch (LauncherNotReplaceableException ex)
        {
            // Refused because of what this binary is CALLED. Nothing about the download was
            // wrong, so say what to change and leave the manual route open.
            ShowNameProblem(ex.ProcessFileName);
            ApplyPhase(Phase.FailedName);
            DiagnosticLog.Write($"Launcher self-update restart refused: {ex}");
            return;
        }
        catch (Exception ex)
        {
            ApplyPhase(Phase.FailedRestart);
            DiagnosticLog.Write($"Launcher self-update restart failed: {ex}");
            return;
        }

        DialogResult = true;
        // Real exit: the updated binary is taking over. Without this MainWindow's close-to-tray
        // would hide instead of quitting and the single-instance guard would block the new exe.
        WarsOfLibertyLauncher.MainWindow.HardExitRequested = true;
        Application.Current.Shutdown();
    }

    private async Task DownloadAsync()
    {
        if (_isPreview)
        {
            await SimulateDownloadAsync();
            return;
        }

        ApplyPhase(Phase.Downloading);
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        var speed = new SpeedTracker();
        var progress = new Progress<DownloadProgress>(p =>
        {
            if (_phase != Phase.Downloading) return;
            speed.Sample(p.BytesReceived);
            ShowProgress(p.BytesReceived, speed.BytesPerSecond,
                speed.EstimateTimeRemaining(p.TotalBytes - p.BytesReceived), p.TotalBytes);
        });

        try
        {
            // Task.Run: the tail of DownloadUpdateAsync SHA-256s ~170 MB through a hash that awaits
            // without ConfigureAwait(false), so on the UI thread every chunk would come back here
            // and freeze the dialog for seconds. The Progress still marshals home.
            await Task.Run(() => LauncherUpdateService.DownloadUpdateAsync(
                _update.DownloadUrl!, _update.ExpectedSha256, progress, token,
                onVerifying: () => Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_phase == Phase.Downloading) ApplyPhase(Phase.Verifying);
                }))));

            LauncherUpdateService.MarkStaged(_update.RemoteTag);
            if (!_closed) ApplyPhase(Phase.Ready);
        }
        catch (OperationCanceledException)
        {
            if (!_closed) ApplyPhase(Phase.Idle);
        }
        catch (UpdateVerificationException ex)
        {
            // The service already deleted the suspect file, so retrying would fetch the same
            // thing: only the manual route is offered.
            DiagnosticLog.Write($"Launcher self-update verification failed: {ex.Message}");
            if (!_closed) ApplyPhase(Phase.FailedCheck);
        }
        catch (DownloadDestinationBlockedException ex)
        {
            // Read-only, held by an antivirus, or Windows' controlled folder access. Name the
            // folder: it is the one thing the player can act on.
            DiagnosticLog.Write($"Launcher self-update destination blocked: {ex}");
            if (_closed) return;
            ShowFolder(Path.GetDirectoryName(ex.DestinationPath) ?? ex.DestinationPath);
            ApplyPhase(Phase.FailedFolder);
        }
        catch (Exception ex)
        {
            // The exception goes to the log, where the detail belongs. On screen it would be the
            // framework's English, with nothing to tell a dropped connection from anything else.
            DiagnosticLog.Write($"Launcher self-update failed: {ex}");
            if (!_closed) ApplyPhase(Phase.FailedDownload);
        }
    }

    /// <summary>The preview's download, so the sequence can be pressed through with nothing at
    /// stake. Cancel works as it does for real.</summary>
    private async Task SimulateDownloadAsync()
    {
        ApplyPhase(Phase.Downloading);
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        try
        {
            for (var pct = 0; pct <= 100; pct += 4)
            {
                ShowProgress(TotalBytes() * pct / 100, 12.4 * 1024 * 1024,
                    TimeSpan.FromSeconds((100 - pct) / 8));
                await Task.Delay(60, token);
            }
            ApplyPhase(Phase.Verifying);
            await Task.Delay(1400, token);
            ApplyPhase(Phase.Ready);
        }
        catch (OperationCanceledException)
        {
            if (!_closed) ApplyPhase(Phase.Idle);
        }
    }

    private void ShowProgress(long received, double bytesPerSecond, TimeSpan? eta, long total = 0)
    {
        if (total <= 0) total = TotalBytes();
        var pct = total > 0 ? Math.Min(100.0, received * 100.0 / total) : 0;
        DownloadProgress.Value = pct;
        ProgressPercentText.Text = $"{Math.Floor(pct).ToString("0", Strings.Culture)} %";
        ProgressBytesText.Text = Strings.Format("DlgLauncherUpdateBytes", Mega(received, "0.0"), Mega(total, "0"));
        SpeedText.Text = bytesPerSecond > 0 ? Mega((long)bytesPerSecond, "0.0") + "/s" : "";
        EtaText.Text = eta is { } left ? FormatEta(left) : "";
    }

    private static string FormatEta(TimeSpan left)
    {
        var seconds = (int)Math.Max(0, Math.Round(left.TotalSeconds));
        return seconds >= 60
            ? Strings.Format("DlgLauncherUpdateEtaMin", seconds / 60, (seconds % 60).ToString("00"))
            : Strings.Format("DlgLauncherUpdateEtaSec", seconds);
    }

    private void ShowFolder(string folder)
    {
        FolderText.Text = PathDisplay.BreakAtSeparators(folder);
        FolderBox.Visibility = Visibility.Visible;
    }

    private void ShowNameProblem(string runningAs)
    {
        BodyText.Inlines.Clear();
        ModDialogChrome.AddMarkedText(BodyText, Strings.Format("DlgLauncherUpdateCannotReplaceSelfBody",
            runningAs, AutoUpdatePolicy.ExpectedExecutableName));
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _closed = true;
        _restartGate.Stop();
        _cts?.Cancel();
    }

    private long TotalBytes() => _update.DownloadSize;

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private static string VersionTag(string? version)
    {
        var v = (version ?? "").Trim();
        if (v.Length == 0) return "\u2014";
        return v.StartsWith('v') ? v : "v" + v;
    }

    private static string Mega(long bytes, string format) =>
        (bytes / (1024.0 * 1024.0)).ToString(format, Strings.Culture) + " MB";

    private static Brush FrozenBrush(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}
