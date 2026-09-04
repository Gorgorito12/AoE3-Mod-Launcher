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
    private const int StepCount = 4;

    /// <summary>Segoe MDL2 "copy". A Private Use Area char - it prints as nothing in a
    /// terminal or a grep, which has made this pair look like a lost literal before.</summary>
    private const string CopyGlyph = "\ue8c8";

    /// <summary>Segoe MDL2 "accept", flashed for 1.5 s after a copy.</summary>
    private const string CopiedGlyph = "\ue73e";

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
    private TextBlock? _copyGlyph;

    /// <summary>
    /// True only when the launcher opened this itself (Radmin wasn't ready).
    /// Gates the auto-close in <see cref="Refresh"/> — see the comment there for
    /// why a window the USER asked for must never close on its own.
    /// </summary>
    private readonly bool _autoOpened;

    public RadminAssistantWindow(LauncherConfig config, bool autoOpened = false)
    {
        _config = config;
        _autoOpened = autoOpened;
        InitializeComponent();
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

        // Kick the first probe right away so the checklist isn't
        // visually empty for 3 seconds before the timer fires.
        Refresh();

        // The window measures itself now (SizeToContent="Height"), and the height it
        // settles on depends on the stage AND the language - so the anchor has to be
        // recomputed after every measure pass, not only at Loaded. Without this the
        // bottom-right corner drifts the moment a step folds.
        SizeChanged += (_, _) => AnchorBottomRight();

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
    /// Paint the whole window from one stage.
    ///
    /// <para><b>Two shapes, not four rows.</b> Below <see cref="RadminStage.InAoE3Network"/>
    /// this is a checklist with exactly ONE card open; at it, the checklist folds to a line
    /// and the window becomes the thing it is actually for — the network name, in full,
    /// with its copy button. The old version laid out four identical Grids in every state and
    /// hid none of them, so the only difference between "you have three steps to go" and
    /// "you are connected" was the colour of four badges.</para>
    ///
    /// <para><b>internal, for the tests.</b> On a machine where Radmin is already connected
    /// the probe only ever reports <see cref="RadminStage.InAoE3Network"/>, so three of the
    /// four states cannot be reached by opening the window at all — driving the stage is
    /// the only way to look at them.</para>
    /// </summary>
    internal void ApplyStage(RadminStage stage, RadminStatus status)
    {
        _status = status;
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
            ConnectedIp.Text = Strings.Format("RadAsstConnectedIp", status.AdapterIp ?? "26.?.?.?");
            StepsDoneText.Text = Strings.Get("RadAsstAllDone");
            ReopenRadminLink.Content = Strings.Get("RadAsstStep1BtnReopen");
            ShowStepsLink.Content = Strings.Get(_stepsExpanded ? "RadAsstHideSteps" : "RadAsstBannerShowSteps");
        }

        // The card has ONE definition. Which parent holds it is the only thing that changes,
        // and a WPF element cannot have two, so the old host is cleared first. Its label is
        // set HERE rather than at build time, because it is built once and the wording
        // depends on the stage - "the network name" while you are joining it, "the network
        // you are joined to" afterwards.
        var card = NetworkCard();
        _networkLabel!.Text = Strings.Get(connected
            ? "RadAsstNetworkLabelJoined"
            : "RadAsstNetworkLabel");
        var wantsCard = connected;
        if (wantsCard && !ReferenceEquals(ConnectedNetworkHost.Content, card))
        {
            Detach(card);
            ConnectedNetworkHost.Content = card;
        }
        else if (!wantsCard && ConnectedNetworkHost.Content != null)
        {
            ConnectedNetworkHost.Content = null;
        }

        RenderProgress(stage);
        RenderSteps(stage, status);
    }

    /// <summary>Four segments and a count, so the window says where you are before you read it.</summary>
    private void RenderProgress(RadminStage stage)
    {
        int done = DoneCount(stage);
        int current = Math.Min(done + 1, StepCount);
        ProgressLabel.Text = Strings.Format("RadAsstProgress", current, StepCount);

        ProgressSegments.Children.Clear();
        for (int i = 1; i <= StepCount; i++)
        {
            ProgressSegments.Children.Add(new Border
            {
                Height = 4,
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(0, 0, i == StepCount ? 0 : 3, 0),
                Background = i <= done ? Brush("MpOk")
                    : i == current ? Brush("MpAction")
                    : Brush("MpRimSoft"),
            });
        }
    }

    /// <summary>
    /// The list: finished steps on one line, the active one open, the rest dim.
    ///
    /// <para>Rebuilt rather than toggled because the three shapes share no layout — a
    /// folded row is a check, a sentence and a number, and the open one is a card with an
    /// action inside it. <see cref="Refresh"/> only calls this on a stage CHANGE, so this
    /// runs about four times in the window's life.</para>
    /// </summary>
    private void RenderSteps(RadminStage stage, RadminStatus status)
    {
        StepsList.Children.Clear();
        int done = DoneCount(stage);

        for (int n = 1; n <= StepCount; n++)
        {
            if (n <= done)
            {
                // Step 1 keeps its action here, shrunk to a link. It used to stay a
                // 160-px button in a finished step because ApplyStage set it Visible in
                // BOTH of its branches and nothing ever set it back.
                StepsList.Children.Add(FoldedStep(n, DoneCaption(n, status),
                    n == 1 ? Strings.Get("RadAsstStep1BtnReopen") : null));
            }
            else if (n == done + 1)
            {
                StepsList.Children.Add(OpenStep(n, stage));
            }
            else
            {
                StepsList.Children.Add(PendingStep(n));
            }
        }
    }

    private string DoneCaption(int n, RadminStatus status) => n switch
    {
        1 => Strings.Get("RadAsstStep1Done"),
        2 => Strings.Format("RadAsstStep2Done", status.AdapterIp ?? "26.?.?.?"),
        3 => Strings.Get("RadAsstStep3Done"),
        _ => Strings.Get("RadAsstStep4Done"),
    };

    /// <summary>How many steps are behind you. The stage enum is ordinal and stays the source.</summary>
    private static int DoneCount(RadminStage stage) => stage switch
    {
        RadminStage.NotInstalled => 0,
        RadminStage.InstalledNotRunning => 1,
        RadminStage.LoggedIn => 2,
        _ => StepCount,
    };

    // -- The three step shapes ------------------------------------------------

    private UIElement FoldedStep(int n, string caption, string? actionLabel)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var check = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            Background = Brush("MpOkBg"),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "\u2713",
                Foreground = Brush("MpOk"),
                FontSize = Size("MpSectionLabelSize"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetColumn(check, 0);
        grid.Children.Add(check);

        var text = new TextBlock
        {
            Text = caption,
            FontWeight = FontWeights.Medium,
            FontSize = Size("MpLabelSize"),
            Foreground = Brush("MpTextMuted"),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        if (actionLabel != null)
        {
            var link = new Button
            {
                Content = actionLabel,
                Style = (Style)FindResource("MpLinkButton"),
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            link.Click += Step1OpenBtn_Click;
            Grid.SetColumn(link, 2);
            grid.Children.Add(link);
        }

        var number = new TextBlock
        {
            Text = n.ToString(),
            FontFamily = (System.Windows.Media.FontFamily)FindResource("MonoFont"),
            FontSize = Size("MpPillSize"),
            Foreground = Brush("MpTextGhost"),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(number, 3);
        grid.Children.Add(number);

        return new Border
        {
            Padding = new Thickness(2, 8, 2, 8),
            BorderBrush = Brush("MpRimFaint"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid,
        };
    }

    private UIElement PendingStep(int n)
    {
        var grid = new Grid { Margin = new Thickness(2, 8, 2, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var ring = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            BorderBrush = Brush("MpRimSoft"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(ring, 0);
        grid.Children.Add(ring);

        var text = new TextBlock
        {
            Text = StepTitleText(n),
            FontWeight = FontWeights.Medium,
            FontSize = Size("MpLabelSize"),
            Foreground = Brush("MpTextGhost"),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var number = new TextBlock
        {
            Text = n.ToString(),
            FontFamily = (System.Windows.Media.FontFamily)FindResource("MonoFont"),
            FontSize = Size("MpPillSize"),
            Foreground = Brush("MpTextGhost"),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(number, 2);
        grid.Children.Add(number);

        return grid;
    }

    /// <summary>The one card that is open, with whatever that step needs you to do inside it.</summary>
    private UIElement OpenStep(int n, RadminStage stage)
    {
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var badge = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = Brush("MpAction"),
            Margin = new Thickness(0, 1, 11, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = n.ToString(),
                Foreground = System.Windows.Media.Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = Size("MpFigureSize"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetColumn(badge, 0);
        head.Children.Add(badge);

        var titles = new StackPanel();
        titles.Children.Add(new TextBlock
        {
            Text = StepTitleText(n),
            Style = (Style)FindResource("StepTitle"),
        });
        var body = new TextBlock { Style = (Style)FindResource("StepBody"), Text = BodyText(n, stage) };
        titles.Children.Add(body);
        Grid.SetColumn(titles, 1);
        head.Children.Add(titles);

        var stack = new StackPanel();
        stack.Children.Add(head);

        if (n == 1)
        {
            var open = new Button
            {
                Style = (Style)FindResource("PropertyActionButton"),
                HorizontalAlignment = HorizontalAlignment.Left,
                MinWidth = 160,
                Margin = new Thickness(33, 11, 0, 0),
                Content = Strings.Get(stage == RadminStage.NotInstalled
                    ? "RadAsstStep1BtnInstall"
                    : "RadAsstStep1BtnReopen"),
            };
            open.Click += Step1OpenBtn_Click;
            stack.Children.Add(open);
        }
        else if (n == 3)
        {
            var card = NetworkCard();
            Detach(card);
            var host = new ContentControl { Content = card, Margin = new Thickness(0, 11, 0, 0) };
            stack.Children.Add(host);
            stack.Children.Add(WaitingLine(Strings.Get("RadAsstWaitingJoin")));
        }
        else if (n == 4)
        {
            stack.Children.Add(WaitingLine(Strings.Get("RadAsstStep4BodyManual")));
        }

        return new Border
        {
            Margin = new Thickness(0, 4, 0, 4),
            Padding = new Thickness(13),
            CornerRadius = new CornerRadius(9),
            Background = Brush("MpRowHighlight"),
            BorderBrush = Brush("MpActionRim"),
            BorderThickness = new Thickness(1),
            Child = stack,
        };
    }

    /// <summary>A dot and a sentence: what the launcher is watching for while you do the step.</summary>
    private UIElement WaitingLine(string text)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 10, 0, 0),
        };
        row.Children.Add(new Border
        {
            Width = 6,
            Height = 6,
            CornerRadius = new CornerRadius(3),
            Background = Brush("MpAction"),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = Size("MpFigureSize"),
            Foreground = Brush("MpTextMuted"),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 330,
        });
        return row;
    }

    private static string StepTitleText(int n) => Strings.Get($"RadAsstStep{n}Title");

    private static string BodyText(int n, RadminStage stage) => n switch
    {
        1 => Strings.Get(stage == RadminStage.NotInstalled
            ? "RadAsstStep1BodyNotInstalled"
            : "RadAsstStep1BodyDone"),
        2 => Strings.Get("RadAsstStep2BodyWaiting"),
        3 => Strings.Get(stage >= RadminStage.LoggedIn
            ? "RadAsstStep3BodyActive"
            : "RadAsstStep3BodyPending"),
        _ => Strings.Get("RadAsstStep4BodyPending"),
    };

    // -- The network name card, built once ------------------------------------

    /// <summary>
    /// The card the whole window exists to hand over: the network name, IN FULL, and the
    /// button that copies it.
    ///
    /// <para>It used to be a <c>TextBlock</c> with <c>TextTrimming="CharacterEllipsis"</c>,
    /// no wrap and no tooltip, so "Age of Empires III: The Asian Dynasties" arrived as "Age
    /// of Empires III: The Asian D…" — unreadable and unrecoverable, in the one control
    /// the code itself calls the only reason to keep the window open. It wraps now.</para>
    ///
    /// <para>Built once and MOVED between hosts rather than built per host: two cards would
    /// be two copy buttons and two glyphs to flip back.</para>
    /// </summary>
    private Border NetworkCard()
    {
        if (_networkCard != null) return _networkCard;

        var stack = new StackPanel();
        _networkLabel = new TextBlock
        {
            Text = Strings.Get("RadAsstNetworkLabel"),
            FontWeight = FontWeights.SemiBold,
            FontSize = Size("MpSectionLabelSize"),
            Foreground = Brush("MpTextGhost"),
        };
        stack.Children.Add(_networkLabel);

        _networkName = new TextBlock
        {
            Text = RadminVpnService.AoE3TadNetworkName,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontWeight = FontWeights.Medium,
            FontSize = Size("MpMetaSize"),
            Foreground = Brush("MpTextSecondary"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 7, 0, 0),
        };
        stack.Children.Add(_networkName);

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 10, 0, 0),
        };
        _copyGlyph = new TextBlock
        {
            Text = CopyGlyph,
            FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
            FontSize = Size("MpLabelSize"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0),
        };
        var copyContent = new StackPanel { Orientation = Orientation.Horizontal };
        copyContent.Children.Add(_copyGlyph);
        copyContent.Children.Add(new TextBlock
        {
            Text = Strings.Get("RadAsstCopy"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        _copyBtn = new Button
        {
            Content = copyContent,
            Style = (Style)FindResource("PropertyActionButton"),
            ToolTip = Strings.Get("RadAsstCopyNetwork"),
        };
        _copyBtn.Click += CopyNetworkBtn_Click;
        row.Children.Add(_copyBtn);
        row.Children.Add(new TextBlock
        {
            Text = Strings.Get("RadAsstCopyDone"),
            FontSize = Size("MpPillSize"),
            Foreground = Brush("MpTextGhost"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(9, 0, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });
        stack.Children.Add(row);

        _networkCard = new Border
        {
            Padding = new Thickness(12, 11, 12, 11),
            CornerRadius = new CornerRadius(8),
            Background = Brush("MpAppBg"),
            BorderBrush = Brush("MpRimSoft"),
            BorderThickness = new Thickness(1),
            Child = stack,
        };
        return _networkCard;
    }

    /// <summary>Take the card off whatever is holding it — an element has one parent.</summary>
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

    /// <summary>
    /// Translate all static labels in one pass. Step bodies are
    /// re-translated on every Refresh (they vary by stage) — this
    /// is for the header, titles, button labels, footer.
    /// </summary>
    private void ApplyStrings()
    {
        Title = Strings.Get("RadAsstWindowTitle");
        // The product, not an instruction. "Conectate a la red AoE3" was fine as a heading
        // over a checklist and wrong the moment you were on the network - and with the
        // subtitle gone it is the only label the window has.
        TitleBarControl.Title = Strings.Get("RadAsstTitleBar");
        DontShowAgainCheck.Content = Strings.Get("RadAsstDontShowAgain");
        CloseBtn.Content = Strings.Get("RadAsstClose");
        SupportLinkHost.Content = Controls.SupportLink.Build();
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
            if (_copyGlyph != null)
            {
                _copyGlyph.Text = CopiedGlyph;
                _ = System.Threading.Tasks.Task.Delay(1500).ContinueWith(
                    _ => Dispatcher.Invoke(() =>
                    {
                        if (_copyGlyph != null) _copyGlyph.Text = CopyGlyph;
                    }));
            }
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
}
