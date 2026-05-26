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
/// Modal dialog that drives the sign-in flow against the lobby backend.
/// Currently backed by Discord OAuth, exposed as a device-flow-shaped
/// API so this dialog stayed the same after the GitHub → Discord swap;
/// the class name is kept for git-blame continuity.
///
/// Shown when the user clicks "Sign in with Discord" on the Multiplayer
/// tab.
///
/// Flow inside the dialog:
///   1. On Loaded → POST /auth/login/device, get verification_uri (and
///      for legacy flows a user_code).
///   2. Display the URL + "Open browser" button. For Discord, user_code
///      is empty, so the "type this code" panel is hidden.
///   3. Start polling /auth/login/poll in the background until either
///      the backend returns a JWT, the user cancels, or the state
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
            VerificationUriText.Text = _start.VerificationUri;
            OpenBrowserButton.IsEnabled = true;

            // Hide the "type this code" panel when the server didn't
            // give us one. Discord's flow doesn't have a user_code —
            // clicking Authorize in the browser is the whole thing.
            if (string.IsNullOrEmpty(_start.UserCode))
            {
                Step2Text.Visibility = Visibility.Collapsed;
                UserCodeBorder.Visibility = Visibility.Collapsed;
            }
            else
            {
                UserCodeText.Text = _start.UserCode;
                CopyButton.IsEnabled = true;
            }

            // Kick off polling. PollDeviceFlowAsync internally waits
            // `interval` between attempts, so we just await it here.
            var done = await _session.CompleteSignInAsync(_start, _cts.Token);
            CompletedSession = done;
            TrySetDialogResult(true);
            Close();
        }
        catch (OperationCanceledException)
        {
            DiagnosticLog.Write("DiscordLoginDialog: cancelled by user");
            TrySetDialogResult(false);
            Close();
        }
        catch (LobbyApiException ex)
        {
            DiagnosticLog.Write($"DiscordLoginDialog: LobbyApiException status={ex.Status} code={ex.Code} msg={ex.Message}");
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
            DiagnosticLog.Write($"DiscordLoginDialog: {details}");
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
            DiagnosticLog.Write($"DiscordLoginDialog: failed to open browser: {ex.Message}");
        }
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_start == null) return;
        try { Clipboard.SetText(_start.UserCode); }
        catch (Exception ex) { DiagnosticLog.Write($"DiscordLoginDialog: clipboard: {ex.Message}"); }
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

    /// <summary>
    /// Sets <see cref="Window.DialogResult"/> defensively. Used by
    /// the async OnLoaded sign-in flow because there's a window
    /// where the user can click <c>CancelButton</c> (which calls
    /// <c>_cts.Cancel()</c> + <c>Close()</c>) BEFORE the
    /// <see cref="OperationCanceledException"/> has propagated back
    /// to the catch block — at that point the dialog has already
    /// closed itself, so writing to DialogResult throws the WPF
    /// "DialogResult can only be set after the window has been shown
    /// as a dialog" exception and crashes the launcher. We swallow
    /// that specific exception because the user-visible state
    /// (dialog closed, no JWT delivered) is already correct;
    /// <c>ShowDialog()</c> just returns null instead of false, and
    /// the caller's <c>if (ok == true)</c> check handles that the
    /// same way it would handle an explicit false.
    /// </summary>
    private void TrySetDialogResult(bool? value)
    {
        try { DialogResult = value; }
        catch (InvalidOperationException)
        {
            // Dialog already closed — nothing more to do.
        }
    }
}
