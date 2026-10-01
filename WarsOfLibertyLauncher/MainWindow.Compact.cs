using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher;

/// <summary>
/// The launcher's COMPACT header (design handoff turn 36, <c>docs/design_handoff_salas_laptop</c>)
/// and the Connected ▾ dropdown that lives in it at every size.
///
/// <para><b>Two layouts, one switch.</b> Below <see cref="CompactLayout"/>'s thresholds the
/// title bar grows to 40 px and takes the three main tabs, the Connected capsule and the account
/// block out of the nav row, which collapses: one header row instead of two. The controls are
/// MOVED between chrome-less host Borders rather than duplicated, so every x:Name — and every
/// writer that addresses one — keeps working in both layouts.</para>
///
/// <para><b>Three couplings, all silent when broken.</b> (1) The bar's Height and
/// WindowChrome.CaptionHeight come from ONE key, set in ONE call
/// (<see cref="App.MainTitleBarHeightKey"/>); a caption taller than the bar drags the window
/// from whatever sits under it. (2) Every control that can enter the caption region carries
/// <c>IsHitTestVisibleInChrome</c> itself (in the XAML) — the flag does not inherit into a
/// ContentControl's Content, which is the "tabs are dead" bug. (3) A single row does not fit at
/// the launcher's 900-px minimum, so <see cref="CompactHeaderLayout"/> decides what leaves
/// first; nothing in that row would ellipsise on its own.</para>
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
    /// laptop user never sees the wide header flash and fold.
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
        else if (_compactChrome) QueueHeaderFit();
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
        QueueHeaderFit();
    }

    /// <summary>
    /// Fold the nav row into the title bar (compact) or unfold it (wide).
    /// </summary>
    private void ApplyCompactHeader(bool compact)
    {
        if (MainTitleBar == null || TitleBarContentGrid == null) return;

        // (1) Height and caption height TOGETHER, from the one key.
        var heightKey = App.MainTitleBarHeightKey(compact);
        MainTitleBar.SetResourceReference(HeightProperty, heightKey);
        TitleBarContentGrid.SetResourceReference(HeightProperty, heightKey);
        MainTitleBar.SetResourceReference(TitleBar.ButtonWidthProperty,
            compact ? "TitleBarButtonWidthMainCompact" : "TitleBarButtonWidthMain");
        App.SyncMainCaptionHeight(this, compact);

        // (2) Move the three pieces. x:Name fields survive re-parenting, ApplyTopTabOrder only
        // touches TopTabBar.Children, and the popups follow their PlacementTarget, so nothing
        // else needs to know where they are.
        MoveChild(compact ? NavTabsHost : TitleTabsHost, compact ? TitleTabsHost : NavTabsHost);
        MoveChild(compact ? NavConnectionHost : TitleConnectionHost, compact ? TitleConnectionHost : NavConnectionHost);
        MoveChild(compact ? NavAccountHost : TitleAccountHost, compact ? TitleAccountHost : NavAccountHost);

        // The seam belongs to the bar directly above the content: the nav row's own rule in the
        // wide layout, this one in the compact layout.
        MainNav.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        TitleBarSeam.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;

        var tabStyle = (Style)FindResource(compact ? "NavTabButtonCompact" : "NavTabButton");
        TopTabPlay.Style = tabStyle;
        TopTabMods.Style = tabStyle;
        TopTabMultiplayer.Style = tabStyle;
        // The new-room dot's ring is a cut-out of whatever bar the tab sits on.
        MultiplayerTabDot.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty,
            compact ? "ChromeTitleBg" : "ChromeNavBg");

        // The capsule: 26 high inside the 40-px bar, 30 in the 54-px nav row.
        ConnectionChip.SetResourceReference(HeightProperty,
            compact ? "ChromeCapsuleHeightCompact" : "ChromeCapsuleHeight");
        ConnectionChip.Tag = compact ? "compact" : null;
        ConnectionChip.Padding = compact ? new Thickness(10, 0, 10, 0) : new Thickness(12, 0, 12, 0);
        ConnectionChip.Margin = compact ? new Thickness(6, 0, 4, 0) : new Thickness(0, 0, 10, 0);

        // The account block: a 24-px avatar, the name, and the rating as a chip — the second
        // line has no room in a 40-px row.
        AccountAvatarHost.Width = compact ? 24 : 26;
        AccountAvatarHost.Height = compact ? 24 : 26;
        AccountAvatarHost.Margin = compact ? new Thickness(0, 0, 7, 0) : new Thickness(0, 0, 10, 0);
        // The badge stays in both layouts (every surface that draws a player wears it, rule
        // 45a-e); only its tuck under the avatar's margin follows the tighter compact gap.
        AccountRankHost.Margin = compact ? new Thickness(-2, 0, 7, 0) : new Thickness(-4, 0, 8, 0);
        AccountName.MaxWidth = compact ? 140 : 180;
        TitleAccountHost.Margin = compact ? new Thickness(6, 0, 8, 0) : new Thickness(0);
        RefreshAccountChipLayout();

        // The update pill matches the capsule's height in the compact bar.
        LauncherUpdatePill.Margin = compact ? new Thickness(0, 0, 6, 0) : new Thickness(0, 0, 10, 0);

        RefreshChromeDivider();
    }

    /// <summary>Move a host Border's single child into another host Border.</summary>
    private static void MoveChild(Border from, Border to)
    {
        if (from == null || to == null || ReferenceEquals(from, to)) return;
        var child = from.Child;
        if (child == null) return;
        from.Child = null;
        to.Child = child;
    }

    // ------------------------------------------------------------------------
    // Width fit of the single row
    // ------------------------------------------------------------------------

    private bool _headerFitQueued;

    /// <summary>
    /// The update pill's caption as last set by its writer, so a reduction can take it away
    /// and give it back. Null when the pill has never shown.
    /// </summary>
    private object? _launcherPillCaption;

    /// <summary>
    /// Re-decide what the compact row gives up. Coalesced at Background priority, so a burst of
    /// writers (sign-in paints the capsule, the account block and the pill within a frame) costs
    /// one measure. Every writer of a header element calls this.
    /// </summary>
    internal void QueueHeaderFit()
    {
        if (_headerFitQueued) return;
        _headerFitQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _headerFitQueued = false;
            ApplyHeaderFit();
        }));
    }

    private void ApplyHeaderFit()
    {
        RestoreHeaderReductions();
        if (!_compactChrome || TitleBarContentGrid == null) return;

        var available = TitleBarContentGrid.ActualWidth;
        if (!(available > 0)) return;

        // Measured at INFINITE width against a budget, the same trick
        // TheRoomsTopBarFitsAtTheNarrowestWindow uses: Measure clamps DesiredSize to the
        // constraint, so measuring at the real width would report an overflow as a fit.
        TitleBarContentGrid.Measure(new Size(double.PositiveInfinity, TitleBarContentGrid.ActualHeight));
        var required = TitleBarContentGrid.DesiredSize.Width;

        var savings = new List<double>();
        foreach (var r in CompactHeaderLayout.DropOrder)
            savings.Add(SavingOf(r));

        var k = CompactHeaderLayout.ReductionsNeeded(available, required, savings);
        for (var i = 0; i < k; i++) ApplyHeaderReduction(CompactHeaderLayout.DropOrder[i]);

        if (k > 0)
        {
            var saved = 0.0;
            for (var i = 0; i < k; i++) saved += savings[i];
            if (required - saved + CompactHeaderLayout.MinDragWidth > available)
                DiagnosticLog.Write(
                    $"Header: even with every reduction the row needs {required - saved:0} of {available:0} DIP; "
                    + "the right end is clipped.");
        }
        TitleBarContentGrid.InvalidateMeasure();
    }

    /// <summary>What one reduction gives back right now (0 for something not on screen).</summary>
    private double SavingOf(HeaderReduction r)
    {
        static double Width(FrameworkElement? e) =>
            e == null || e.Visibility != Visibility.Visible
                ? 0
                : e.DesiredSize.Width + e.Margin.Left + e.Margin.Right;

        switch (r)
        {
            case HeaderReduction.VersionChip:
                return Width(VersionChip);
            case HeaderReduction.BrandWordmark:
                return Width(BrandWordmark);
            case HeaderReduction.ConnectionWord:
                return ConnectionChip.Visibility == Visibility.Visible ? Width(ConnectionChipStatus) : 0;
            case HeaderReduction.AccountName:
                return AccountButton.Visibility == Visibility.Visible ? Width(AccountName) : 0;
            case HeaderReduction.AccountElo:
                return AccountButton.Visibility == Visibility.Visible ? Width(AccountEloChip) : 0;
            case HeaderReduction.UpdatePillCaption:
                if (LauncherUpdatePill.Visibility != Visibility.Visible || LauncherUpdatePill.Content == null)
                    return 0;
                // The caption's share is the pill with it minus the pill without it.
                var full = LauncherUpdatePill.DesiredSize.Width;
                var caption = LauncherUpdatePill.Content;
                LauncherUpdatePill.Content = null;
                LauncherUpdatePill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var bare = LauncherUpdatePill.DesiredSize.Width;
                LauncherUpdatePill.Content = caption;
                LauncherUpdatePill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                return Math.Max(0, full - bare);
            default:
                return 0;
        }
    }

    private void ApplyHeaderReduction(HeaderReduction r)
    {
        switch (r)
        {
            case HeaderReduction.VersionChip:
                VersionChip.Visibility = Visibility.Collapsed;
                // The brand button is hit-testable, so unlike the chip (part of the drag region)
                // it can carry a tooltip: the version is not lost, only moved.
                TitleBarBrandButton.ToolTip = LauncherUpdateService.CurrentInformationalTag;
                break;
            case HeaderReduction.BrandWordmark:
                BrandWordmark.Visibility = Visibility.Collapsed;
                break;
            case HeaderReduction.UpdatePillCaption:
                LauncherUpdatePill.Content = null;
                break;
            case HeaderReduction.ConnectionWord:
                ConnectionChipStatus.Visibility = Visibility.Collapsed;
                break;
            case HeaderReduction.AccountName:
                AccountName.Visibility = Visibility.Collapsed;
                break;
            case HeaderReduction.AccountElo:
                AccountEloChip.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private void RestoreHeaderReductions()
    {
        if (VersionChip == null) return;
        VersionChip.Visibility = Visibility.Visible;
        TitleBarBrandButton.ToolTip = null;
        BrandWordmark.Visibility = Visibility.Visible;
        if (_launcherPillCaption != null) LauncherUpdatePill.Content = _launcherPillCaption;
        ConnectionChipStatus.Visibility = Visibility.Visible;
        AccountName.Visibility = Visibility.Visible;
        RefreshAccountChipLayout();
    }

    // ------------------------------------------------------------------------
    // Account block
    // ------------------------------------------------------------------------

    /// <summary>The full rating line ("Colonial · 1383 ELO"), kept for the account menu.</summary>
    private string? _accountEloLine;

    /// <summary>The bare figure for the compact ELO chip ("1383"), or null.</summary>
    private string? _accountEloShort;

    /// <summary>
    /// Which of the account block's rating surfaces the current layout shows: the second line
    /// in the wide layout, the ELO chip in the compact one. The rank badge shows in both — the
    /// handoff drops the second LINE, not the badge — and the full "Colonial · 1383 ELO" is
    /// always in the account menu's header.
    /// </summary>
    private void RefreshAccountChipLayout()
    {
        if (AccountElo == null) return;
        var hasLine = !string.IsNullOrWhiteSpace(_accountEloLine);
        var hasShort = !string.IsNullOrWhiteSpace(_accountEloShort);

        AccountElo.Visibility = !_compactChrome && hasLine ? Visibility.Visible : Visibility.Collapsed;
        AccountEloChipText.Text = _accountEloShort ?? string.Empty;
        AccountEloChip.Visibility = _compactChrome && hasShort ? Visibility.Visible : Visibility.Collapsed;
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
