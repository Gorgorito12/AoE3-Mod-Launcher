using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Modal dialog that drives a GitHub OAuth Device Flow against the lobby
/// Worker. Shown when the user clicks "Sign in with GitHub" on the
/// Multiplayer tab.
///
/// Flow inside the dialog:
///   1. On Loaded → POST /auth/github/device, get user_code + URL.
///   2. Display both, plus "Open browser" and "Copy code" buttons.
///   3. Start polling /auth/github/poll in the background until either
///      the Worker returns a JWT, the user cancels, or the device code
///      expires.
///   4. On success → set DialogResult = true; caller reads
///      <see cref="CompletedSession"/>.
/// </summary>
public partial class GitHubLoginDialog : Window
{
    private readonly MultiplayerSession _session;
    private readonly CancellationTokenSource _cts = new();

    private DeviceFlowStart? _start;

    public DeviceFlowComplete? CompletedSession { get; private set; }

    public GitHubLoginDialog(MultiplayerSession session)
    {
        InitializeComponent();
        _session = session;

        Title = Strings.Get("MpSignInDialogTitle");
        TitleText.Text = Strings.Get("MpSignInDialogTitle");
        Step1Text.Text = Strings.Get("MpSignInStep1");
        Step2Text.Text = Strings.Get("MpSignInStep2");
        StatusText.Text = Strings.Get("MpSignInWaiting");
        OpenBrowserButton.Content = Strings.Get("MpSignInOpenBrowser");
        CopyButton.Content = Strings.Get("MpSignInCopy");
        CancelButton.Content = Strings.Get("MpSignInCancel");

        // Disable until /device returns; otherwise the user could open
        // an empty browser tab.
        OpenBrowserButton.IsEnabled = false;
        CopyButton.IsEnabled = false;

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _start = await _session.StartSignInAsync(_cts.Token);
            UserCodeText.Text = _start.UserCode;
            VerificationUriText.Text = _start.VerificationUri;
            OpenBrowserButton.IsEnabled = true;
            CopyButton.IsEnabled = true;

            // Kick off polling. PollDeviceFlowAsync internally waits
            // `interval` between attempts, so we just await it here.
            var done = await _session.CompleteSignInAsync(_start, _cts.Token);
            CompletedSession = done;
            DialogResult = true;
            Close();
        }
        catch (OperationCanceledException)
        {
            DiagnosticLog.Write("GitHubLoginDialog: cancelled by user");
            DialogResult = false;
            Close();
        }
        catch (LobbyApiException ex)
        {
            DiagnosticLog.Write($"GitHubLoginDialog: LobbyApiException status={ex.Status} code={ex.Code} msg={ex.Message}");
            StatusText.Text = ex.Message;
            StatusText.Foreground = System.Windows.Media.Brushes.Salmon;
            CancelButton.Content = Strings.Get("DlgClose");
        }
        catch (Exception ex)
        {
            // Walk inner exceptions so transport-level details (e.g.
            // SocketException) survive into the log. Generic catch is
            // last so it never swallows context.
            var details = new System.Text.StringBuilder();
            details.Append(ex.GetType().Name).Append(": ").Append(ex.Message);
            var inner = ex.InnerException;
            while (inner != null)
            {
                details.Append(" || ").Append(inner.GetType().Name).Append(": ").Append(inner.Message);
                inner = inner.InnerException;
            }
            DiagnosticLog.Write($"GitHubLoginDialog: {details}");
            StatusText.Text = details.ToString();
            StatusText.Foreground = System.Windows.Media.Brushes.Salmon;
            CancelButton.Content = Strings.Get("DlgClose");
        }
    }

    private void OpenBrowserButton_Click(object sender, RoutedEventArgs e)
    {
        if (_start == null) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _start.VerificationUri,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"GitHubLoginDialog: failed to open browser: {ex.Message}");
        }
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_start == null) return;
        try { Clipboard.SetText(_start.UserCode); }
        catch (Exception ex) { DiagnosticLog.Write($"GitHubLoginDialog: clipboard: {ex.Message}"); }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _cts.Cancel();
        DialogResult = false;
        Close();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (!_cts.IsCancellationRequested) _cts.Cancel();
        _cts.Dispose();
    }
}
