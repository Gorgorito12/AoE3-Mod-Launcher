using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Always-on-top assistant overlay that walks the user through joining
/// the AoE3 TAD Radmin VPN network.
///
/// Design choices vs the older fat banner in MultiplayerTab:
///
///   • LIVE — polls <see cref="RadminAssistantService.ProbeAsync"/>
///     on a 3-second DispatcherTimer; checklist auto-advances as
///     the user's Radmin state changes. The user never has to click
///     "next".
///   • POSITION — bottom-right of the primary screen by default,
///     where Radmin's own window typically lives. Easy to drag
///     around via the header (WindowChrome CaptionHeight=40).
///   • CHECKBOX — "Don't show again" writes <see cref="LauncherConfig.RadminAssistantSkipped"/>
///     so subsequent Multiplayer-tab loads skip the auto-open. The
///     compact banner still exposes a "Show steps" button so the
///     user can reopen on demand.
///   • SAFE — never touches Radmin's window. Open Radmin = our
///     Process.Start. Network name = our clipboard write. Detection
///     = registry + NIC enumeration (and, future, an ICMP ping to
///     a seed peer). Antivirus / TOS friendly.
///
/// Caller owns the close lifecycle: closing the window is fine
/// (Window_Closing flushes config), and reopening it is just
/// `new RadminAssistantWindow(config).Show()`.
/// </summary>
public partial class RadminAssistantWindow : Window
{
    private readonly LauncherConfig _config;
    private DispatcherTimer? _pollTimer;
    private RadminStage _lastStage = (RadminStage)(-1);

    public RadminAssistantWindow(LauncherConfig config)
    {
        _config = config;
        InitializeComponent();
        ApplyStrings();
        DontShowAgainCheck.IsChecked = _config.RadminAssistantSkipped;
        // Seed the network name box with the canonical AoE3 TAD
        // network — same constant the legacy banner used so they
        // can't drift apart. The string is bare ASCII so it copies
        // cleanly to the Windows clipboard.
        NetworkNameBox.Text = RadminVpnService.AoE3TadNetworkName;
    }

    // -- Lifecycle ------------------------------------------------------------

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Anchor bottom-right of the primary work area so the
        // overlay sits where Radmin's window usually opens — gives
        // the user a side-by-side view rather than dominating their
        // launcher. We do this in Loaded (not the ctor) because the
        // window's ActualWidth/Height aren't valid before measure.
        try
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth - 20;
            Top = area.Bottom - ActualHeight - 20;
        }
        catch
        {
            // SystemParameters.WorkArea throws on machines with
            // certain RDP / non-standard display configurations;
            // fall back to the default WindowStartupLocation
            // (Manual at 0,0). Not pretty but never crashes.
        }

        // Kick the first probe right away so the checklist isn't
        // visually empty for 3 seconds before the timer fires.
        Refresh();

        // 3-second polling — matches MultiplayerTab's existing
        // Radmin banner timer. Cheap (registry + NIC enumeration
        // take microseconds).
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _pollTimer.Tick += (_, _) => Refresh();
        _pollTimer.Start();
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _pollTimer?.Stop();
        _pollTimer = null;

        // Persist the "don't show again" choice. Wrapping in try/
        // catch because this is best-effort UI state — a config
        // save failure shouldn't crash close.
        try
        {
            bool skipped = DontShowAgainCheck.IsChecked == true;
            if (_config.RadminAssistantSkipped != skipped)
            {
                _config.RadminAssistantSkipped = skipped;
                _config.Save();
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"RadminAssistant: config save on close failed: {ex.Message}");
        }
    }

    // -- Polling --------------------------------------------------------------

    /// <summary>
    /// Refresh the entire checklist from the current Radmin stage.
    /// Idempotent — re-running with the same stage is a no-op other
    /// than a few setters. Calls auto-close when the stage hits
    /// <see cref="RadminStage.InAoE3Network"/>.
    /// </summary>
    private async void Refresh()
    {
        try
        {
            var snap = await RadminAssistantService.ProbeAsync();
            var stage = snap.Stage;

            // Only rebuild when stage actually changed — keeps the
            // overlay quiet (no flicker) during the long stretches
            // where the user is staring at Radmin's window.
            if (stage == _lastStage) return;
            _lastStage = stage;

            ApplyStage(stage, snap.Status);

            // Auto-close once we're confirmed in the network. The
            // user already did the work; no point keeping the
            // overlay in their face. Only happens when the seed-
            // peer ping reports InAoE3Network — until that's
            // wired, LoggedIn is as high as we get so the overlay
            // stays open and waits for the user to verify manually.
            if (stage == RadminStage.InAoE3Network)
            {
                // Tiny delay so the user sees the final ✓ flash
                // before the window disappears — feels like a
                // celebration instead of "where did the window go?".
                _ = System.Threading.Tasks.Task.Delay(1200).ContinueWith(
                    _ => Dispatcher.Invoke(Close));
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"RadminAssistant.Refresh: {ex.Message}");
        }
    }

    // -- Step rendering -------------------------------------------------------

    /// <summary>
    /// Map the current stage to the 4 step badges. Each step badge
    /// has 3 visual states:
    ///   ✓ done       (green)
    ///   ⏳ in progress (gold, current step)
    ///   ○ pending    (grey, future step)
    /// </summary>
    private void ApplyStage(RadminStage stage, RadminStatus status)
    {
        // Step 1 — Open Radmin. Done once Radmin is installed and
        // its process is running (i.e. anything ≥ InstalledNotRunning
        // can't be checked without poking the process list, but
        // the overlay treats "NotInstalled" as step-1-pending and
        // everything else as step-1-done because if the GUI exists,
        // they got past it).
        if (stage == RadminStage.NotInstalled)
        {
            SetStep(1, StepState.InProgress);
            Step1Body.Text = Strings.Get("RadAsstStep1BodyNotInstalled");
            Step1OpenBtn.Content = Strings.Get("RadAsstStep1BtnInstall");
            Step1OpenBtn.Visibility = Visibility.Visible;
        }
        else
        {
            SetStep(1, StepState.Done);
            Step1Body.Text = Strings.Get("RadAsstStep1BodyDone");
            Step1OpenBtn.Content = Strings.Get("RadAsstStep1BtnReopen");
            Step1OpenBtn.Visibility = Visibility.Visible;
        }

        // Step 2 — Sign in. Done once a 26.x adapter is up.
        if (stage <= RadminStage.InstalledNotRunning)
        {
            SetStep(2, stage == RadminStage.InstalledNotRunning ? StepState.InProgress : StepState.Pending);
            Step2Body.Text = Strings.Get("RadAsstStep2BodyWaiting");
        }
        else
        {
            SetStep(2, StepState.Done);
            Step2Body.Text = string.Format(
                Strings.Get("RadAsstStep2BodyDone"),
                status.AdapterIp ?? "26.?.?.?");
        }

        // Step 3 — Paste + Join. In-progress once they're signed in
        // (we can't observe the actual paste/click, so we say
        // "your turn" the moment step 2 lands).
        if (stage <= RadminStage.InstalledNotRunning)
        {
            SetStep(3, StepState.Pending);
            Step3Body.Text = Strings.Get("RadAsstStep3BodyPending");
            Step3Hint.Text = "";
        }
        else if (stage == RadminStage.LoggedIn)
        {
            SetStep(3, StepState.InProgress);
            Step3Body.Text = Strings.Get("RadAsstStep3BodyActive");
            Step3Hint.Text = Strings.Get("RadAsstStep3Hint");
        }
        else
        {
            SetStep(3, StepState.Done);
            Step3Body.Text = Strings.Get("RadAsstStep3BodyDone");
            Step3Hint.Text = "";
        }

        // Step 4 — Confirmation. Wired through end-to-end but never
        // reaches "Done" until the seed-peer ping is implemented.
        // The body copy makes that explicit: "verify in Radmin"
        // until automatic detection is available.
        if (stage < RadminStage.LoggedIn)
        {
            SetStep(4, StepState.Pending);
            Step4Body.Text = Strings.Get("RadAsstStep4BodyPending");
        }
        else if (stage == RadminStage.LoggedIn)
        {
            SetStep(4, StepState.InProgress);
            Step4Body.Text = Strings.Get("RadAsstStep4BodyManual");
        }
        else
        {
            SetStep(4, StepState.Done);
            Step4Body.Text = Strings.Get("RadAsstStep4BodyDone");
        }
    }

    private enum StepState { Pending, InProgress, Done }

    private void SetStep(int n, StepState state)
    {
        Border badge = n switch
        {
            1 => Step1Badge, 2 => Step2Badge, 3 => Step3Badge, 4 => Step4Badge,
            _ => throw new ArgumentOutOfRangeException(nameof(n)),
        };
        TextBlock glyph = n switch
        {
            1 => Step1Glyph, 2 => Step2Glyph, 3 => Step3Glyph, 4 => Step4Glyph,
            _ => throw new ArgumentOutOfRangeException(nameof(n)),
        };

        switch (state)
        {
            case StepState.Done:
                badge.Background = (Brush)FindResource("SuccessBrush");
                glyph.Text = "✓"; // ✓
                break;
            case StepState.InProgress:
                badge.Background = (Brush)FindResource("AccentBrush");
                glyph.Text = n.ToString();
                break;
            case StepState.Pending:
                badge.Background = (Brush)FindResource("BgNeutral");
                glyph.Text = n.ToString();
                break;
        }
    }

    // -- Strings --------------------------------------------------------------

    /// <summary>
    /// Translate all static labels in one pass. Step bodies are
    /// re-translated on every Refresh (they vary by stage) — this
    /// is for the header, titles, button labels, footer.
    /// </summary>
    private void ApplyStrings()
    {
        Title = Strings.Get("RadAsstWindowTitle");
        TitleBarControl.Title = Strings.Get("RadAsstHeaderTitle");
        HeaderSubtitleText.Text = Strings.Get("RadAsstHeaderSubtitle");
        Step1Title.Text = Strings.Get("RadAsstStep1Title");
        Step2Title.Text = Strings.Get("RadAsstStep2Title");
        Step3Title.Text = Strings.Get("RadAsstStep3Title");
        Step4Title.Text = Strings.Get("RadAsstStep4Title");
        DontShowAgainCheck.Content = Strings.Get("RadAsstDontShowAgain");
        CloseBtn.Content = Strings.Get("RadAsstClose");
        CopyNetworkBtn.ToolTip = Strings.Get("RadAsstCopyNetwork");
    }

    // -- Handlers -------------------------------------------------------------

    private void Step1OpenBtn_Click(object sender, RoutedEventArgs e)
    {
        var status = RadminVpnService.GetStatus();
        if (status.InstallState == RadminInstallState.NotInstalled)
        {
            // No MSI on disk → bounce to the download page in
            // the user's browser. We don't run the silent install
            // from inside the overlay because that triggers UAC
            // and a long download — MultiplayerTab's banner has
            // the proper installer flow with progress reporting.
            RadminVpnService.OpenDownloadPageInBrowser();
            return;
        }
        if (!string.IsNullOrEmpty(status.ExePath))
        {
            RadminVpnService.LaunchGui(status.ExePath);
        }
    }

    private void CopyNetworkBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(RadminVpnService.AoE3TadNetworkName);
            // Briefly flip the icon to a checkmark so the user
            // gets visible confirmation the paste landed. WPF
            // clipboard ops occasionally throw COMException on
            // first call when another process holds the
            // clipboard — non-fatal, swallow.
            CopyBtnGlyph.Text = ""; // CheckMark
            _ = System.Threading.Tasks.Task.Delay(1500).ContinueWith(
                _ => Dispatcher.Invoke(() => CopyBtnGlyph.Text = ""));
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"RadminAssistant.CopyNetwork: {ex.Message}");
        }
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
}
