using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher;

/// <summary>
/// The launcher's COMPACT header (design handoff turns 38-39, <c>docs/design_handoff_salas_laptop</c>)
/// and the Connected ▾ dropdown, which lives in the nav row at every size.
///
/// <para><b>The same three bars, only lower.</b> Below <see cref="CompactLayout"/>'s thresholds
/// the title bar drops to 34 px and the nav row to 42; nothing moves between them. Turn 36 folded
/// the tabs, the capsule and the account block into the title bar instead, and the handoff that
/// replaced it says why that was wrong: a small window has to show the SAME blocks as a big one,
/// and the folded row put WORKSHOP under the Update pill.</para>
///
/// <para><b>One coupling, silent when broken.</b> The bar's Height and
/// WindowChrome.CaptionHeight come from ONE key, set in ONE call
/// (<see cref="App.MainTitleBarHeightKey"/>); a caption taller than the bar drags the window
/// from whatever sits under it — here, the top of the nav row.</para>
/// </summary>
public partial class MainWindow
{
    /// <summary>Whether the window currently draws the compact layout.</summary>
    private bool _compactChrome;

    /// <summary>
    /// Read by <c>App.ApplyWindowChrome</c>, which runs from a Loaded class handler and has to
    /// pick the caption height of the layout the window will actually show.
    /// </summary>
    internal bool IsCompactChrome => _compactChrome;

    /// <summary>
    /// Decide the layout from the RESTORED size, before the first frame, and listen for every
    /// later change. Called from the constructor right after <c>RestoreWindowState</c>, so a
    /// laptop user never sees the wide header flash and shrink.
    /// </summary>
    private void InitCompactLayout()
    {
        double w, h;
        if (WindowState == WindowState.Maximized)
        {
            w = SystemParameters.WorkArea.Width;
            h = SystemParameters.WorkArea.Height;
        }
        else
        {
            w = double.IsNaN(Width) ? 1100 : Width;
            h = double.IsNaN(Height) ? 700 : Height;
        }

        ApplyCompactLayout(CompactLayout.IsCompact(w, h, wasCompact: false), force: true);

        // Permanent, unlike the bell popup's SizeChanged hook: a resize, a maximise, a snap and a
        // move to a monitor with another DPI all arrive here.
        SizeChanged += (_, _) => EvaluateCompactLayout();
        StateChanged += (_, _) => EvaluateCompactLayout();
    }

    private void EvaluateCompactLayout()
    {
        var compact = CompactLayout.IsCompact(ActualWidth, ActualHeight, _compactChrome);
        if (compact != _compactChrome) ApplyCompactLayout(compact);
    }

    /// <summary>Switch the whole window between its two layouts. No-op when unchanged.</summary>
    private void ApplyCompactLayout(bool compact, bool force = false)
    {
        if (!force && compact == _compactChrome) return;
        _compactChrome = compact;
        DiagnosticLog.Write(
            $"Layout: {(compact ? "compact" : "wide")} (window {ActualWidth:0}x{ActualHeight:0} DIP, "
            + $"thresholds {CompactLayout.MaxCompactWidth:0}x{CompactLayout.MaxCompactHeight:0}).");

        ApplyCompactHeader(compact);
        MultiplayerView?.SetCompactLayout(compact);
    }

    /// <summary>
    /// Lower the title bar and the nav row (compact) or give them back their wide heights.
    /// Every value set here has its wide twin in the XAML, which is what the wide branch restores.
    /// </summary>
    private void ApplyCompactHeader(bool compact)
    {
        if (MainTitleBar == null || TitleBarContentGrid == null) return;

        // Height and caption height TOGETHER, from the one key.
        var heightKey = App.MainTitleBarHeightKey(compact);
        MainTitleBar.SetResourceReference(HeightProperty, heightKey);
        TitleBarContentGrid.SetResourceReference(HeightProperty, heightKey);
        MainTitleBar.SetResourceReference(TitleBar.ButtonWidthProperty,
            compact ? "TitleBarButtonWidthMainCompact" : "TitleBarButtonWidthMain");
        App.SyncMainCaptionHeight(this, compact);

        // The nav row: 42 high, the tabs flush left with their own 18-px padding.
        MainNav.SetResourceReference(HeightProperty, compact ? "MainNavHeightCompact" : "MainNavHeight");
        if (compact) MainNavGrid.Margin = new Thickness(0, 0, 12, 0);
        else MainNavGrid.SetResourceReference(MarginProperty, "MainNavPadding");

        var tabStyle = (Style)FindResource(compact ? "NavTabButtonCompact" : "NavTabButton");
        TopTabPlay.Style = tabStyle;
        TopTabMods.Style = tabStyle;
        TopTabMultiplayer.Style = tabStyle;

        // The capsule: 26 high in the 42-px row, 30 in the 54-px one.
        ConnectionChip.SetResourceReference(HeightProperty,
            compact ? "ChromeCapsuleHeightCompact" : "ChromeCapsuleHeight");
        ConnectionChip.Tag = compact ? "compact" : null;
        ConnectionChip.Padding = compact ? new Thickness(10, 0, 10, 0) : new Thickness(12, 0, 12, 0);
    }

    // ------------------------------------------------------------------------
    // Account block
    // ------------------------------------------------------------------------

    /// <summary>The full rating line ("Colonial · 1383 ELO"), kept for the account menu.</summary>
    private string? _accountEloLine;

    /// <summary>
    /// The rating line under the name and the rank badge beside the avatar, each shown only when
    /// there is something to show. The account menu reads <see cref="_accountEloLine"/> rather
    /// than this line's visibility.
    /// </summary>
    private void RefreshAccountChipLayout()
    {
        if (AccountElo == null) return;
        AccountElo.Text = _accountEloLine ?? string.Empty;
        AccountElo.Visibility = string.IsNullOrWhiteSpace(_accountEloLine) ? Visibility.Collapsed : Visibility.Visible;
        if (AccountRankHost != null)
            AccountRankHost.Visibility = AccountRankHost.Content != null
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    // ------------------------------------------------------------------------
    // The Connected ▾ dropdown (every size)
    // ------------------------------------------------------------------------

    /// <summary>The Radmin VPN address the capsule was last painted with, for the dropdown.</summary>
    private string? _connectionIp;

    /// <summary>The open dropdown, or null. The field-based toggle model, like the account menu.</summary>
    private System.Windows.Controls.Primitives.Popup? _connectionPopup;

    /// <summary>
    /// The Connected ▾ menu: the Radmin IP (click to copy) and "Help connecting", which opens
    /// the Radmin assistant. Since design handoff turn 36 this is the ONLY manual door to the
    /// assistant — and it follows the same "Never" gate the banner's "Show steps" does.
    ///
    /// <para>Toggle by FIELD, never ChromePopups.ConsumeToggleOff: a StaysOpen=false popup
    /// auto-dismisses on the mouse-down that re-clicks its opener, and the 300-ms guess that
    /// method makes has already failed once. The Closed handler clears the field DEFERRED at
    /// Background priority so a re-click still sees it.</para>
    /// </summary>
    private void ConnectionChip_Click(object sender, RoutedEventArgs e)
    {
        if (_connectionPopup != null)
        {
            _connectionPopup.IsOpen = false;
            return;
        }

        var popup = new System.Windows.Controls.Primitives.Popup
        {
            PlacementTarget = ConnectionChip,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            VerticalOffset = 4,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = System.Windows.Controls.Primitives.PopupAnimation.Fade,
        };

        // The header's menu recipe: ChromePopupBg inside a two-tone rim, shadow on the OUTER band.
        var border = new Border
        {
            Background = (Brush)FindResource("ChromePopupBg"),
            BorderBrush = (Brush)FindResource("MenuBorder"),
            BorderThickness = new Thickness(2),
            CornerRadius = (CornerRadius)FindResource("RadiusPopupInner"),
            Padding = new Thickness(10),
            MinWidth = 240,
            MaxWidth = 320,
        };
        var rim = new Border
        {
            Background = (Brush)FindResource("MenuBorderOuter"),
            CornerRadius = (CornerRadius)FindResource("RadiusPopupOuter"),
            Padding = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 20,
                ShadowDepth = 4,
                Color = Colors.Black,
                Opacity = 0.6,
            },
        };
        var content = new StackPanel();
        border.Child = content;
        rim.Child = border;
        ApplyPopupScale(rim);
        popup.Child = rim;

        // Right-aligned under the capsule, which sits near the right edge of whichever bar it
        // is in. Computed in Opened because the card is content-sized.
        popup.Opened += (_, _) =>
            popup.HorizontalOffset = ConnectionChip.ActualWidth - rim.ActualWidth * UiScale.Current;

        // The status, as the capsule says it — a menu that opens without restating what it is
        // about reads as a list of unrelated commands.
        var status = new TextBlock
        {
            Text = ConnectionChipStatus.Text,
            Foreground = (Brush)FindResource("ChromeConnectedText"),
            FontFamily = (FontFamily)FindResource("BodyFont"),
            FontSize = (double)FindResource("FontSizeCaption"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(8, 4, 8, 2),
        };
        content.Children.Add(status);
        content.Children.Add(BuildSettingsDivider());

        if (!string.IsNullOrWhiteSpace(_connectionIp))
        {
            var ip = _connectionIp!;
            FrameworkElement? ipRow = null;
            ipRow = BuildSettingsRow(
                glyph: "",   // Copy
                label: Strings.Format("MpChipVpnDetail", ip),
                subtitle: Strings.Get("MpChipMenuCopyIp"),
                click: () =>
                {
                    try
                    {
                        Clipboard.SetText(ip);
                        SetRowSubtitle(ipRow, Strings.Get("MpRadminCopiedToast"));
                    }
                    catch (Exception ex)
                    {
                        // The clipboard can be held by another process; that is not worth a
                        // dialog, and the address is right there on the row to read.
                        DiagnosticLog.Write($"Connected menu: copying the IP failed: {ex.Message}");
                    }
                });
            content.Children.Add(ipRow);
        }
        else
        {
            content.Children.Add(new TextBlock
            {
                Text = Strings.Get("MpChipMenuNoIp"),
                Foreground = (Brush)FindResource("ChromeTextDim"),
                FontFamily = (FontFamily)FindResource("BodyFont"),
                FontSize = (double)FindResource("FontSizeCaption"),
                Margin = new Thickness(8, 6, 8, 6),
            });
        }

        // "Never" means never: the setting's own hint says the assistant is off, and a visible
        // way in would make it a lie.
        if (MultiplayerView?.IsRadminAssistantAvailable == true)
        {
            content.Children.Add(BuildSettingsDivider());
            content.Children.Add(BuildSettingsRow(
                glyph: "",   // Help
                label: Strings.Get("MpRoomsRadminHelp"),
                subtitle: Strings.Get("MpRoomsRadminHelpTooltip"),
                click: () =>
                {
                    popup.IsOpen = false;
                    MultiplayerView?.OpenRadminAssistant();
                }));
        }

        _connectionPopup = popup;
        ConnectionChipChevron.Text = "▴";
        popup.Closed += (_, _) =>
        {
            ConnectionChipChevron.Text = "▾";
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (ReferenceEquals(_connectionPopup, popup)) _connectionPopup = null;
            }));
        };

        // Single-open invariant across the header's menus + close-on-dialog-open.
        ChromePopups.Track(popup, ConnectionChip);
        popup.IsOpen = true;
    }

    /// <summary>
    /// Rewrite the subtitle of a row built by <c>BuildSettingsRow</c> — the "copied" feedback
    /// has to appear where the click was, because the clipboard write itself is invisible.
    /// </summary>
    private static void SetRowSubtitle(FrameworkElement? row, string text)
    {
        if (row == null) return;
        var stack = FindDescendant<StackPanel>(row);
        if (stack != null && stack.Children.Count > 1 && stack.Children[1] is TextBlock sub)
            sub.Text = text;
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is T hit) return hit;
            if (child is DependencyObject d && FindDescendant<T>(d) is { } deeper) return deeper;
        }
        return null;
    }
}
