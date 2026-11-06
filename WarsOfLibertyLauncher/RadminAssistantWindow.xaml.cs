using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WarsOfLibertyLauncher.Controls;
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
///     "next". The probe itself runs on the thread pool (it parses
///     every rotated Radmin log), one at a time.
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
///
/// The <c>autoOpened</c> ctor flag says who opened it, and gates the
/// auto-close: only a window the LAUNCHER opened may close itself once
/// the checklist goes green. One the user summoned stays until they
/// close it — see Refresh().
/// </summary>
public partial class RadminAssistantWindow : Window
{
    private readonly LauncherConfig _config;
    private DispatcherTimer? _pollTimer;
    private RadminStage _lastStage = (RadminStage)(-1);

    /// <summary>The four steps. One number, so the bar and the list cannot disagree.</summary>
    private const int StepCount = 3;

    /// <summary>Segoe MDL2 "copy". A Private Use Area char - it prints as nothing in a
    /// terminal or a grep, which has made this pair look like a lost literal before.</summary>

    /// <summary>Segoe MDL2 "accept", flashed for 1.5 s after a copy.</summary>

    /// <summary>Last probe, kept so a re-render does not have to wait for the next tick.</summary>
    private RadminStatus? _status;

    /// <summary>
    /// Whether the checklist is showing.
    ///
    /// <para>Starts FOLDED, and is forced open again the moment the stage drops below
    /// connected. A window that has nothing left to guide should not be showing four ticked
    /// boxes: the first cut defaulted this to true and the connected state rendered as the
    /// in-progress one with a confirmation stuck on top.</para>
    /// </summary>
    private bool _stepsExpanded;

    // The network card, built once by NetworkCard() and moved between hosts.
    private Border? _networkCard;
    private TextBlock? _networkLabel;
    private TextBlock? _networkName;
    private Button? _copyBtn;
    private Button? _openInCard;
    private bool _copied;

    // The waiting dot of the open step, which the poll beats.
    private Border? _waitDot;

    // Radmin installing from here: the percent, and 100 once msiexec has the file.
    private bool _installing;
    private int _installPercent;

    // A --preview-dialog state: this stage and nothing probed.
    private readonly RadminStage? _previewStage;

    /// <summary>
    /// True only when the launcher opened this itself (Radmin wasn't ready).
    /// Gates the auto-close in <see cref="Refresh"/> — see the comment there for
    /// why a window the USER asked for must never close on its own.
    /// </summary>
    private readonly bool _autoOpened;

    public RadminAssistantWindow(LauncherConfig config, bool autoOpened = false, RadminStage? previewStage = null)
    {
        _config = config;
        _autoOpened = autoOpened;
        _previewStage = previewStage;
        InitializeComponent();
        Controls.ModDialogChrome.AttachGlobal(this);
        ApplyStrings();
        DontShowAgainCheck.IsChecked = _config.RadminAssistantSkipped;
        // The network name is seeded by NetworkCard() from the same canonical constant the
        // banner used, so the two cannot drift. The string is bare ASCII, which is what
        // makes it copy cleanly to the Windows clipboard.
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
            AnchorBottomRight();
        }
        catch
        {
            // SystemParameters.WorkArea throws on machines with
            // certain RDP / non-standard display configurations;
            // fall back to the default WindowStartupLocation
            // (Manual at 0,0). Not pretty but never crashes.
        }

        if (_previewStage is { } stage)
        {
            ApplyStage(stage, Preview(stage));
            if (_previewOpened) { _stepsExpanded = true; Repaint(); }
            if (_previewInstalling) StartPreviewInstall();
            _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _pollTimer.Tick += (_, _) => PulseWaitDot();
            _pollTimer.Start();
            return;
        }

        // Kick the first probe right away so the checklist isn't
        // visually empty for 3 seconds before the timer fires.
        Refresh();

        // The window measures itself now (SizeToContent="Height"), and the height it
        // settles on depends on the stage AND the language - so the anchor has to be
        // recomputed after every measure pass, not only at Loaded. Without this the
        // bottom-right corner drifts the moment a step folds.
        SizeChanged += (_, _) => AnchorBottomRight();

        // 3-second polling — matches MultiplayerTab's existing Radmin banner timer. NOT cheap at
        // LoggedIn: the probe parses every rotated Radmin log, which is why it runs on the pool
        // and why a tick never starts a second probe while one is still running.
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _pollTimer.Tick += (_, _) =>
        {
            DiagnosticLog.Time("Radmin assistant refresh", Refresh);
            PulseWaitDot();
        };
        _pollTimer.Start();
    }

    /// <summary>A probe is running; a tick that lands meanwhile does nothing.</summary>
    private bool _refreshing;

    /// <summary>
    /// The window is closing or closed. Guards the paint after the probe's await and the 1.2-s
    /// auto-close — both can land after the user has already closed it.
    /// </summary>
    private bool _closed;

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _closed = true;
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
        if (_refreshing || _closed) return;
        _refreshing = true;
        try
        {
            // The await keeps this context, so everything after it — ApplyStage included — is
            // back on the UI thread; only the probe ran on the pool.
            var snap = await RadminAssistantService.ProbeAsync();
            if (_closed) return;
            var stage = snap.Stage;

            // Only rebuild when stage actually changed — keeps the
            // overlay quiet (no flicker) during the long stretches
            // where the user is staring at Radmin's window.
            if (stage == _lastStage) return;

            ApplyStage(stage, snap.Status);

            // Auto-close once we're confirmed in the network — but ONLY when we
            // opened ourselves. That path fires exclusively while Radmin is NOT
            // ready (MaybeAutoOpenAssistant bails at >= LoggedIn), so reaching
            // InAoE3Network means the tutorial did its job and can get out of the
            // way.
            //
            // A window the USER opened ("Show steps") must never do this: it can be
            // summoned at any stage, so with everything already green the very first
            // Refresh saw InAoE3Network and slammed it shut ~1.2s later — taking the
            // copy-network-name button with it, which is the ONLY thing the window
            // is good for once the checklist is green. Rule: they opened it, they
            // close it.
            if (stage == RadminStage.InAoE3Network && _autoOpened)
            {
                // Tiny delay so the user sees the final ✓ flash
                // before the window disappears — feels like a
                // celebration instead of "where did the window go?".
                _ = System.Threading.Tasks.Task.Delay(1200).ContinueWith(
                    _ => Dispatcher.Invoke(() => { if (!_closed) Close(); }));
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"RadminAssistant.Refresh: {ex.Message}");
        }
        finally
        {
            _refreshing = false;
        }
    }

    // -- Step rendering -------------------------------------------------------

    internal void ApplyStage(RadminStage stage, RadminStatus status)
    {
        _status = status;
        _lastStage = stage;
        bool connected = stage == RadminStage.InAoE3Network;

        // Reaching the end folds the list; opening it again is the user's call and sticks
        // until the stage moves, which is what _stepsExpanded remembers.
        if (!connected) _stepsExpanded = true;

        ConnectedBlock.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        StepsBlock.Visibility = connected && !_stepsExpanded
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (connected)
        {
            ConnectedTitle.Text = Strings.Get("RadAsstConnectedTitle");
            ConnectedIp.Inlines.Clear();
            Controls.ModDialogChrome.AddMarkedText(ConnectedIp,
                Strings.Format("RadAsstConnectedIp", status.AdapterIp ?? "26.?.?.?"));
            StepsDoneText.Text = Strings.Get("RadAsstAllDone");
            ReopenRadminLink.Content = Strings.Get("RadAsstOpenRadmin");
            ShowStepsLink.Content = Strings.Get(_stepsExpanded ? "RadAsstHideSteps" : "RadAsstBannerShowSteps");
        }

        // "Close" is the way out only once there is nothing left to do: until then the
        // primary button on screen is the step's own action.
        CloseBtn.Style = (Style)FindResource(connected ? "WindowPrimaryButton" : "WindowSecondaryButton");

        // The card has ONE definition. Which parent holds it is the only thing that changes,
        // and a WPF element cannot have two, so the old host is cleared first. Its label is
        // set HERE rather than at build time, because it is built once and the wording
        // depends on the stage - "the network name" while you are joining it, "the network
        // you are joined to" afterwards.
        var card = NetworkCard();
        _networkLabel!.Text = Strings.Get(connected
            ? "RadAsstNetworkLabelJoined"
            : "RadAsstNetworkLabel");
        _openInCard!.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        PaintCopyButton();
        if (connected && !ReferenceEquals(ConnectedNetworkHost.Content, card))
        {
            Detach(card);
            ConnectedNetworkHost.Content = card;
        }
        else if (!connected && ConnectedNetworkHost.Content != null)
        {
            ConnectedNetworkHost.Content = null;
        }

        RenderProgress(stage);
        RenderSteps(stage, status);
    }

    private void RenderProgress(RadminStage stage)
    {
        int done = DoneCount(stage);
        int current = Math.Min(done + 1, StepCount);
        ProgressLabel.Text = done == StepCount
            ? Strings.Get("RadAsstProgressDone")
            : Strings.Format("RadAsstProgress", current, StepCount);

        ProgressSegments.Children.Clear();
        for (int i = 1; i <= StepCount; i++)
        {
            var segment = new Border
            {
                Height = 4,
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(0, 0, i == StepCount ? 0 : 4, 0),
            };
            segment.SetResourceReference(Border.BackgroundProperty,
                i <= done ? "WindowSafeTick" : i == current ? "MpAction" : "WindowCardRim");
            ProgressSegments.Children.Add(segment);
        }
    }

    private void RenderSteps(RadminStage stage, RadminStatus status)
    {
        StepsList.Children.Clear();
        int done = DoneCount(stage);

        for (int n = 1; n <= StepCount; n++)
        {
            if (n <= done) StepsList.Children.Add(FoldedStep(n, DoneCaption(n, status)));
            else if (n == done + 1) StepsList.Children.Add(OpenStep(n));
            else StepsList.Children.Add(PendingStep(n));
        }
    }

    private static string DoneCaption(int n, RadminStatus status) => n switch
    {
        1 => Strings.Get("RadAsstStep1Done"),
        2 => Strings.Format("RadAsstStep2Done", status.AdapterIp ?? "26.?.?.?"),
        _ => Strings.Get("RadAsstStep3Done"),
    };

    /// <summary>Three steps: the launcher goes from "join the network" straight to all green, so
    /// a fourth "connection confirmed" step never opened. The waiting line does that job.</summary>
    internal static int DoneCount(RadminStage stage) => stage switch
    {
        RadminStage.NotInstalled => 0,
        RadminStage.InstalledNotRunning => 1,
        RadminStage.LoggedIn => 2,
        _ => StepCount,
    };

    // -- The three step shapes ------------------------------------------------

    private UIElement FoldedStep(int n, string caption)
    {
        var text = new TextBlock
        {
            FontSize = Size("SetRowDescSize"),
            Foreground = Brush("WindowSoftText"),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Controls.ModDialogChrome.AddMarkedText(text, caption);

        return StepRow(n, new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            Background = Brush("WindowTickFill"),
            Child = new TextBlock
            {
                Text = "✓",
                Foreground = Brush("WindowSafeTick"),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        }, text);
    }

    private UIElement PendingStep(int n) => StepRow(n, new Border
    {
        Width = 18,
        Height = 18,
        CornerRadius = new CornerRadius(9),
        BorderBrush = Brush("WindowFieldRim"),
        BorderThickness = new Thickness(1),
    }, new TextBlock
    {
        Text = StepTitleText(n),
        FontSize = Size("SetRowDescSize"),
        Foreground = Brush("WindowFineText"),
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center,
    });

    private UIElement StepRow(int n, FrameworkElement mark, TextBlock text)
    {
        var grid = new Grid { MinHeight = 30 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        mark.Margin = new Thickness(0, 0, 10, 0);
        mark.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(mark);

        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var number = new TextBlock
        {
            Text = n.ToString(),
            FontFamily = (FontFamily)FindResource("MonoFont"),
            FontSize = Size("MpPillSize"),
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x61, 0x77, 0x9A)),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(number, 2);
        grid.Children.Add(number);
        return grid;
    }

    private UIElement OpenStep(int n)
    {
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var badge = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Margin = new Thickness(0, 0, 11, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = n.ToString(),
                Foreground = Brushes.White,
                FontFamily = (FontFamily)FindResource("MonoFont"),
                FontWeight = FontWeights.SemiBold,
                FontSize = Size("SetCaptionSize"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        badge.SetResourceReference(Border.BackgroundProperty, "MpAction");
        head.Children.Add(badge);

        var titles = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };
        titles.Children.Add(new TextBlock
        {
            Text = StepTitleText(n),
            Style = (Style)FindResource("StepTitle"),
        });
        var body = new TextBlock { Style = (Style)FindResource("StepBody") };
        Controls.ModDialogChrome.AddMarkedText(body, Strings.Get($"RadAsstStep{n}Body"));
        Controls.ModDialogChrome.SetLineHeight(1.5, body);
        titles.Children.Add(body);
        Grid.SetColumn(titles, 1);
        head.Children.Add(titles);

        var stack = new StackPanel();
        stack.Children.Add(head);

        switch (n)
        {
            case 1 when _installing:
                stack.Children.Add(InstallProgress());
                stack.Children.Add(WaitingLine(Strings.Get("RadAsstWaitingInstall")));
                break;
            case 1:
                stack.Children.Add(InstallActions());
                stack.Children.Add(WaitingLine(Strings.Get("RadAsstWaitingInstall")));
                break;
            case 2:
                var open = new Button
                {
                    Style = (Style)FindResource("WindowPrimaryButton"),
                    MinWidth = 136,
                    Content = Strings.Get("RadAsstOpenRadmin"),
                };
                open.Click += OpenRadmin_Click;
                stack.Children.Add(new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(33, 11, 0, 0),
                    Children = { open },
                });
                stack.Children.Add(WaitingLine(Strings.Get("RadAsstWaitingLogin")));
                break;
            default:
                var card = NetworkCard();
                Detach(card);
                stack.Children.Add(new ContentControl { Content = card, Margin = new Thickness(0, 11, 0, 0) });
                stack.Children.Add(WaitingLine(Strings.Get("RadAsstWaitingJoin")));
                var hint = new TextBlock
                {
                    Text = Strings.Get("RadAsstStep3Hint"),
                    FontSize = Size("SetControlSize"),
                    Foreground = Brush("WindowFineText"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(33, 11, 0, 0),
                };
                Controls.ModDialogChrome.SetLineHeight(1.45, hint);
                stack.Children.Add(hint);
                break;
        }

        return new Border
        {
            Style = (Style)FindResource("WindowCard"),
            Margin = new Thickness(0, 4, 0, 4),
            Padding = new Thickness(14),
            Child = stack,
        };
    }

    private UIElement InstallActions()
    {
        var install = new Button
        {
            Style = (Style)FindResource("WindowPrimaryButton"),
            MinWidth = 136,
            Content = Strings.Get("RadAsstStep1BtnInstall"),
        };
        install.Click += InstallRadmin_Click;

        var web = new Button
        {
            Style = (Style)FindResource("WindowTextLink"),
            Content = Strings.Get("RadAsstStep1Web"),
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        web.Click += (_, _) => RadminVpnService.OpenDownloadPageInBrowser();

        return new WrapPanel
        {
            Margin = new Thickness(33, 11, 0, 0),
            Children = { install, web },
        };
    }

    /// <summary>The bar takes the place of the button. It stops counting once msiexec has the
    /// file, because from there Windows is waiting for the player to accept its prompt.</summary>
    private UIElement InstallProgress()
    {
        var setup = _installPercent >= 100;
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock
        {
            Text = Strings.Get(setup ? "RadAsstSettingUp" : "RadAsstDownloading"),
            FontSize = Size("SetRowDescSize"),
            Foreground = Brush("UiBodyText"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 8, 0),
        });
        if (!setup)
        {
            var pct = new TextBlock
            {
                Text = $"{_installPercent} %",
                FontFamily = (FontFamily)FindResource("MonoFont"),
                FontSize = Size("SetControlSize"),
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Bottom,
            };
            Grid.SetColumn(pct, 1);
            row.Children.Add(pct);
        }

        var bar = new ProgressBar
        {
            Height = 8,
            Margin = new Thickness(0, 7, 0, 0),
            Minimum = 0,
            Maximum = 100,
            Value = setup ? 100 : _installPercent,
            Template = (ControlTemplate)FindResource("WindowBar"),
            Opacity = setup ? .55 : 1,
        };
        bar.SetResourceReference(ForegroundProperty, "MpAction");

        return new StackPanel
        {
            Margin = new Thickness(33, 11, 0, 0),
            Children = { row, bar },
        };
    }

    private async void InstallRadmin_Click(object sender, RoutedEventArgs e)
    {
        _installing = true;
        _installPercent = 0;
        Repaint();

        if (_previewStage != null && !_previewRealDownload)
        {
            for (var p = 0; p < 100; p += 4)
            {
                _installPercent = p;
                Repaint();
                await System.Threading.Tasks.Task.Delay(90);
                if (_closed) return;
            }
            _installPercent = 100;
            Repaint();
            await System.Threading.Tasks.Task.Delay(1600);
            if (_closed) return;
            _installing = false;
            ApplyStage(RadminStage.InstalledNotRunning, Preview(RadminStage.InstalledNotRunning));
            return;
        }

        var progress = new Progress<int>(p =>
        {
            _installPercent = p;
            if (!_closed) Repaint();
        });

        bool ok;
        try
        {
            ok = await RadminVpnService.InstallSilentAsync(
                progress, System.Threading.CancellationToken.None, confirmSpace: ConfirmSpace);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"RadminAssistant.Install: {ex.Message}");
            ok = false;
        }
        if (_closed) return;

        _installing = false;
        Repaint();
        if (!ok) RadminVpnService.OpenDownloadPageInBrowser();
    }

    /// <summary>The same room check the multiplayer banner's installer makes.</summary>
    private bool ConfirmSpace(long msiBytes)
    {
        var required = Math.Max(0, msiBytes) + DiskSpaceService.RadminInstallAllowanceBytes;
        var shortfall = DiskSpaceService.Check(
            System.IO.Path.GetTempPath(), required, tempPath: null, tempRequired: 0);
        return WarsOfLibertyLauncher.DiskSpacePrompt.ConfirmOrCancel(this, shortfall, "DiskSpaceConfirmDownloadBody");
    }

    private void Repaint()
    {
        if (_status != null) ApplyStage(_lastStage, _status);
    }

    private UIElement WaitingLine(string text)
    {
        var scale = new ScaleTransform(1, 1, 3, 3);
        _waitDot = new Border
        {
            Width = 6,
            Height = 6,
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = scale,
        };
        _waitDot.SetResourceReference(Border.BackgroundProperty, "MpAction");

        var row = new Grid { Margin = new Thickness(33, 11, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(_waitDot);
        var label = new TextBlock
        {
            Text = text,
            FontSize = Size("SetControlSize"),
            Foreground = Brush("WindowMuted"),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(label, 1);
        row.Children.Add(label);
        return row;
    }

    /// <summary>One beat per probe, so the dot says what the launcher is really doing. A finite
    /// animation started by the poll, and not a forever clock: there is nothing to stop, and the
    /// window asks nothing of WPF between beats.</summary>
    private void PulseWaitDot()
    {
        if (_waitDot == null || RankBadge.ReducedEffects || !SystemParameters.ClientAreaAnimation) return;

        DoubleAnimationUsingKeyFrames Beat(double low)
        {
            var beat = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            beat.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            beat.KeyFrames.Add(new EasingDoubleKeyFrame(low, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.5))));
            beat.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3))));
            return beat;
        }

        _waitDot.BeginAnimation(OpacityProperty, Beat(.3));
        if (_waitDot.RenderTransform is ScaleTransform scale)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, Beat(.6));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, Beat(.6));
        }
    }

    private static string StepTitleText(int n) => Strings.Get($"RadAsstStep{n}Title");

    // -- The network name card, built once ------------------------------------

    private Border NetworkCard()
    {
        if (_networkCard != null) return _networkCard;

        var stack = new StackPanel();
        _networkLabel = new TextBlock
        {
            Text = Strings.Get("RadAsstNetworkLabel"),
            FontWeight = FontWeights.SemiBold,
            FontSize = Size("MpPillSize"),
            Foreground = Brush("WindowMuted"),
        };
        stack.Children.Add(_networkLabel);

        _networkName = new TextBlock
        {
            Text = RadminVpnService.AoE3TadNetworkName,
            FontFamily = (FontFamily)FindResource("MonoFont"),
            FontWeight = FontWeights.Medium,
            FontSize = Size("DialogBodySize"),
            Foreground = Brush("WindowStrongText"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        };
        stack.Children.Add(_networkName);

        _copyBtn = new Button { MinWidth = 104, ToolTip = Strings.Get("RadAsstCopyNetwork") };
        _copyBtn.Click += CopyNetworkBtn_Click;

        _openInCard = new Button
        {
            Style = (Style)FindResource("WindowSecondaryButton"),
            MinWidth = 112,
            Margin = new Thickness(8, 0, 0, 0),
            Content = Strings.Get("RadAsstOpenRadmin"),
        };
        _openInCard.Click += OpenRadmin_Click;

        stack.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 10, 0, 0),
            Children = { _copyBtn, _openInCard },
        });

        _networkCard = new Border
        {
            Style = (Style)FindResource("WindowField"),
            Padding = new Thickness(12, 11, 12, 12),
            Child = stack,
        };
        PaintCopyButton();
        return _networkCard;
    }

    /// <summary>"Copy" is the step's primary action until there is nothing left to do, and says it
    /// worked only after it did: the window never touches the clipboard by itself.</summary>
    private void PaintCopyButton()
    {
        if (_copyBtn == null) return;
        var connected = _lastStage == RadminStage.InAoE3Network;
        _copyBtn.Style = (Style)FindResource(connected || _copied ? "WindowSecondaryButton" : "WindowPrimaryButton");
        _copyBtn.Content = Strings.Get(_copied ? "RadAsstCopyDone" : "RadAsstCopy");
        if (_copied)
        {
            _copyBtn.Background = Brush("WindowSafeFill");
            _copyBtn.BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0x4F, 0xD6, 0x8A));
            _copyBtn.Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0xE0, 0xB0));
        }
        else
        {
            _copyBtn.ClearValue(BackgroundProperty);
            _copyBtn.ClearValue(BorderBrushProperty);
            _copyBtn.ClearValue(ForegroundProperty);
        }
    }

    private static void Detach(UIElement child)
    {
        switch (LogicalTreeHelper.GetParent(child))
        {
            case ContentControl host: host.Content = null; break;
            case Panel panel: panel.Children.Remove(child); break;
            case Border border: border.Child = null; break;
        }
    }

    private Brush Brush(string key) => (Brush)FindResource(key);
    private double Size(string key) => (double)FindResource(key);

    // -- Strings --------------------------------------------------------------

    private void ApplyStrings()
    {
        Title = Strings.Get("RadAsstWindowTitle");
        TitleBarControl.Title = Strings.Get("RadAsstWindowTitle");
        DontShowAgainCheck.Content = new TextBlock
        {
            Text = Strings.Get("RadAsstDontShowAgain"),
            FontSize = Size("SetRowDescSize"),
            Foreground = Brush("WindowSoftText"),
        };
        CloseBtn.Content = Strings.Get("RadAsstClose");
        SupportLinkHost.Content = Controls.SupportLink.BuildOutline("SupportDiscordHelpLabel");
    }

    // -- Handlers -------------------------------------------------------------

    private void OpenRadmin_Click(object sender, RoutedEventArgs e)
    {
        if (_previewStage != null)
        {
            DiagnosticLog.Write("Radmin assistant preview: Open Radmin not run.");
            return;
        }
        var status = RadminVpnService.GetStatus();
        if (!string.IsNullOrEmpty(status.ExePath)) RadminVpnService.LaunchGui(status.ExePath);
    }

    private void CopyNetworkBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Clipboard calls occasionally throw COMException when another process holds the
            // clipboard. Non-fatal, and then it does not say it copied.
            if (_previewStage == null) Clipboard.SetText(RadminVpnService.AoE3TadNetworkName);
            _copied = true;
            PaintCopyButton();
            _ = System.Threading.Tasks.Task.Delay(1500).ContinueWith(_ => Dispatcher.Invoke(() =>
            {
                _copied = false;
                if (!_closed) PaintCopyButton();
            }));
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"RadminAssistant.CopyNetwork: {ex.Message}");
        }
    }

    /// <summary>
    /// Unfold the checklist in the connected state, and fold it again.
    ///
    /// <para>The steps are not deleted when they are done, only put away. Somebody who wants
    /// to see what the launcher actually checked - or to re-run one of them - can, and the
    /// window shrinks back to a confirmation when they have finished looking.</para>
    /// </summary>
    private void ShowStepsLink_Click(object sender, RoutedEventArgs e)
    {
        _stepsExpanded = !_stepsExpanded;
        if (_status != null) ApplyStage(_lastStage, _status);
    }

    /// <summary>
    /// Bottom-right of the work area, recomputed from the CURRENT size.
    ///
    /// <para>It has to be recomputed rather than set once: the window is
    /// <c>SizeToContent="Height"</c> now, so its height changes when a step folds and again
    /// when the language does. Anchoring only at <c>Loaded</c> would leave the corner
    /// drifting away from the screen edge every time the content moved.</para>
    ///
    /// <para><c>SystemParameters.WorkArea</c> throws on some RDP and non-standard display
    /// configurations. The fallback is the default manual position at 0,0 - not pretty, but
    /// never a crash.</para>
    /// </summary>
    private void AnchorBottomRight()
    {
        try
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth - 20;
            Top = area.Bottom - ActualHeight - 20;
        }
        catch
        {
            // See above: a display configuration we cannot read is not worth a crash.
        }
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

    private static RadminStatus Preview(RadminStage stage) => new(
        stage == RadminStage.NotInstalled ? RadminInstallState.NotInstalled : RadminInstallState.Installed,
        ExePath: null,
        Version: null,
        IsServiceRunning: stage > RadminStage.InstalledNotRunning,
        AdapterIp: stage >= RadminStage.LoggedIn ? "26.162.244.170" : null);

    private bool _previewOpened;
    private bool _previewInstalling;

    /// <summary>The two preview states a stage alone does not say: the steps unfolded (78e) and
    /// the install under way (78f).</summary>
    internal void ConfigurePreview(bool stepsOpen, bool installing, bool realDownload = false)
    {
        _previewOpened = stepsOpen;
        _previewInstalling = installing;
        _previewRealDownload = realDownload;
    }

    /// <summary>Downloads the real installer; the harness still stops msiexec from running.</summary>
    private bool _previewRealDownload;

    private void StartPreviewInstall()
    {
        _installing = true;
        _installPercent = 64;
        Repaint();
    }
}
