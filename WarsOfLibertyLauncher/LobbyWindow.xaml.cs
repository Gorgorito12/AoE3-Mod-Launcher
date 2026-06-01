using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Non-modal lobby window. Replaces the in-tab Canvas overlay that
/// used to live inside <see cref="Controls.MultiplayerTab"/> (the
/// <c>RoomPanel</c> Grid + Canvas + floating-card Border).
///
/// Lifecycle:
///   • Created and <see cref="Window.Show()"/>n by MultiplayerTab when
///     the session enters a room (joined or created).
///   • Tracked in a single-instance field on MultiplayerTab; re-entering
///     a room with the window already open just <see cref="Window.Activate"/>s
///     it instead of stacking a duplicate.
///   • Closed (X / Esc / Alt+F4 / external Close) fires
///     <see cref="Window.Closed"/>; MultiplayerTab clears its single-instance
///     field and triggers a leave-room flow on the session if the user
///     dismissed mid-lobby.
///
/// Why a real Window: the previous in-tab popup looked modal because
/// of its floating-card chrome and dropshadow even though it was
/// technically non-modal. A real top-level Window gives the user:
///   • OS-native edge-drag resize (instead of a custom Thumb grip)
///   • Drag-to-move outside the launcher's bounds
///   • Alt-tab visibility (or not — controlled by ShowInTaskbar)
///   • Independent minimise from the main launcher window
/// — which is the menu/properties dialog pattern (see CLAUDE.md
/// under Runtime conventions).
///
/// Click forwarding: the lobby UI logic (rendering, chat send, match
/// phase transitions, etc.) lives in MultiplayerTab — it's tightly
/// coupled to <see cref="MultiplayerSession"/> events, the catalog,
/// telemetry, and the rest of the tab's state. Rather than move all
/// that across, this window exposes a set of <c>Action</c> callbacks
/// that MultiplayerTab populates on construction; the XAML click
/// handlers (<see cref="LeaveRoomButton_Click"/> etc.) are tiny
/// forwarders that invoke those callbacks. MultiplayerTab reads/writes
/// the UI elements directly via the field-modifier-internal x:Name
/// fields auto-generated for the Window (same assembly = accessible).
///
/// The window stores its <see cref="MultiplayerSession"/> reference but
/// deliberately does NOT subscribe to its events — that subscription
/// already exists on MultiplayerTab and continues to drive the UI.
/// Storing the session here is just so future callers / event handlers
/// that need it (chat send composing, etc.) can reach it without
/// passing it through every callback.
/// </summary>
public partial class LobbyWindow : Window
{
    /// <summary>
    /// Session this lobby window is rendering. Held so click handlers
    /// that need session data (e.g. "am I the host?") can reach it
    /// without round-tripping through a callback.
    /// </summary>
#pragma warning disable IDE0052 // Field is intentionally held for future direct callers.
    private readonly MultiplayerSession _session;
#pragma warning restore IDE0052

    // ------------------------------------------------------------------
    // Minimized-pill state. We never use the real WindowState.Minimized
    // (it drops a WindowStyle=None + ShowInTaskbar=False window to the
    // unstylable OS desktop stub whose click pops the system menu).
    // Instead EnterPill() shrinks the window to a small glowing pill and
    // RestoreFromPill() brings it back to the saved geometry/state.
    // ------------------------------------------------------------------
    private const double PillWidth = 188;
    private const double PillHeight = 52;
    private const double PillMargin = 16;   // gap from the work-area corner

    private bool _isPilled;
    private bool _suppressStateChange;      // re-entrancy guard for OnStateChanged
    private bool _hiddenByOwner;            // we hid WITH the launcher (owner) minimizing
    private Window? _launcherWindow;        // the launcher whose minimize we follow
    private bool _hasPillSavedBounds;
    private WindowState _restoreState;
    private double _restoreLeft, _restoreTop, _restoreWidth, _restoreHeight;
    private double _restoreMinWidth, _restoreMinHeight;
    private ResizeMode _restoreResizeMode;

    // ------------------------------------------------------------------
    // Click forwarder callbacks. MultiplayerTab populates these on
    // construction; the XAML click handlers below invoke whichever is
    // non-null. Defaulted nullable so a window opened without callbacks
    // (e.g. designer preview) doesn't NRE on every click.
    // ------------------------------------------------------------------

    /// <summary>"Leave room" button + title-bar close — same flow.</summary>
    public Action? OnLeaveRoom { get; set; }

    /// <summary>"Mark as ready" / "Ready" toggle button.</summary>
    public Action? OnReady { get; set; }

    /// <summary>"Start game" (host only).</summary>
    public Action? OnStart { get; set; }

    /// <summary>"Cancel game" / "Leave game" while a match is running.</summary>
    public Action? OnInGameCancel { get; set; }

    /// <summary>"Clear chat" — wipes the local chat log only.</summary>
    public Action? OnClearChat { get; set; }

    /// <summary>"Send" button on the chat input bar.</summary>
    public Action? OnSendChat { get; set; }

    /// <summary>Emoji icon next to the chat input.</summary>
    public Action? OnEmoji { get; set; }

    /// <summary>Chat input TextChanged — drives placeholder visibility.</summary>
    public Action? OnChatTextChanged { get; set; }

    /// <summary>Chat input KeyDown — Enter to send. Forwards the
    /// <see cref="KeyEventArgs"/> so the handler can read Key + check
    /// modifiers.</summary>
    public Action<KeyEventArgs>? OnChatKeyDown { get; set; }

    public LobbyWindow(MultiplayerSession session)
    {
        InitializeComponent();
        _session = session;
    }

    // ------------------------------------------------------------------
    // XAML click handlers. All tiny forwarders to the public callbacks.
    // Same-named as the originals in MultiplayerTab.xaml so the XAML
    // Click="…" wiring in LobbyWindow.xaml resolves cleanly here.
    // ------------------------------------------------------------------

    /// <summary>
    /// Title bar close. Closing the window through <see cref="Window.Close"/>
    /// lets the caller's <see cref="Window.Closed"/> handler run the
    /// leave-room flow — same exit path as Esc / Alt+F4. We deliberately
    /// do NOT invoke <see cref="OnLeaveRoom"/> here; the Closed event
    /// is the single rendezvous point for dismiss, so the behaviour
    /// stays identical regardless of which dismiss path the user took.
    /// </summary>
    private void CloseHeaderBtn_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Title-bar minimise. Shrinks to the in-window glowing pill instead
    /// of the OS minimize stub (see <see cref="EnterPill"/> for why).
    /// </summary>
    private void MinimizeBtn_Click(object sender, RoutedEventArgs e) => EnterPill();

    /// <summary>
    /// Title-bar maximise/restore. Toggles between
    /// <see cref="WindowState.Maximized"/> and <see cref="WindowState.Normal"/>;
    /// the App.OnStartup WM_GETMINMAXINFO hook (see CLAUDE.md → Runtime
    /// conventions → "Maximize-respects-taskbar") keeps the maximised
    /// rect from spilling over the Windows taskbar. The glyph + tooltip
    /// swap is wired in <see cref="OnStateChanged"/>.
    /// </summary>
    private void MaximizeBtn_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    /// <summary>
    /// Update the maximise-button glyph + tooltip when the window flips
    /// between Normal and Maximized. WPF fires this on every state
    /// transition (Normal ↔ Maximized ↔ Minimized) so we can keep the
    /// chrome reflecting the live state without polling. Guarded on
    /// <c>MaximizeBtn != null</c> because the base class can fire this
    /// during InitializeComponent before the named field is wired up.
    /// </summary>
    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        // Anything that minimises us through the real WindowState (Win+D,
        // "Show desktop", a programmatic minimise) would otherwise land on
        // the unstylable OS stub. Intercept it and convert to our pill.
        // The _suppressStateChange guard skips the WindowState writes we
        // make ourselves inside EnterPill/RestoreFromPill so this doesn't
        // recurse.
        if (!_suppressStateChange && WindowState == WindowState.Minimized && !_isPilled)
        {
            DiagnosticLog.Write($"LobbyWindow: real minimize hit; launcher={_launcherWindow?.WindowState.ToString() ?? "null"}, hiddenByOwner={_hiddenByOwner}");

            // We never allow a real OS-minimize (it shows the unstylable stub).
            // Snap back to Normal immediately so we don't get stuck minimized,
            // then DEFER the pill-vs-hide decision one dispatcher tick: if this
            // minimize was propagated from the launcher (owner) minimizing, the
            // launcher won't be flagged Minimized *yet* at this instant (event
            // ordering), so checking it now races. By the next Background tick
            // the launcher's own minimize has settled and we can tell which case
            // we're in — hide WITH the launcher, or (we alone) show the pill.
            _suppressStateChange = true;
            WindowState = WindowState.Normal;
            _suppressStateChange = false;

            Dispatcher.BeginInvoke(new Action(DecidePillOrHideWithOwner), DispatcherPriority.Background);
            return;
        }

        if (MaximizeBtn == null) return;

        bool isMax = WindowState == WindowState.Maximized;
        // 0xE923 = ChromeRestore, 0xE922 = ChromeMaximize. The two
        // glyphs are visually paired in Segoe MDL2 specifically for
        // this use; the FontFamily on the button is set to Segoe MDL2
        // Assets via the TitleBarChromeButton style, so the raw
        // codepoints render as the chrome icons. Using char-from-hex
        // instead of "\uXXXX" string literals because some source-file
        // round-trips mangle non-ASCII bytes.
        MaximizeBtn.Content = ((char)(isMax ? 0xE923 : 0xE922)).ToString();
        MaximizeBtn.ToolTip = isMax ? "Restore" : "Maximize";
    }

    /// <summary>
    /// Once the HWND exists, latch onto the launcher window and follow its
    /// minimize/restore so the lobby (or its pill) hides WITH the launcher
    /// instead of being left floating alone on the desktop (the reported
    /// "se quedó afuera" bug). We resolve the launcher as <see cref="Window.Owner"/>
    /// (set by OpenLobbyWindow) and fall back to
    /// <see cref="Application.MainWindow"/> in case the owner link didn't take.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _launcherWindow = Owner ?? Application.Current?.MainWindow;
        if (_launcherWindow != null && !ReferenceEquals(_launcherWindow, this))
            _launcherWindow.StateChanged += LauncherWindow_StateChanged;
        DiagnosticLog.Write($"LobbyWindow: tracking launcher = {(_launcherWindow == null ? "null" : _launcherWindow.GetType().Name)} (Owner={(Owner == null ? "null" : "set")})");
    }

    /// <summary>
    /// Launcher minimized → hide along with it (remembering we did, so the
    /// OnStateChanged pill logic stands down). Launcher restored → come back
    /// and re-focus. Hide()/Show() leave WindowState, size and position
    /// untouched, so a pilled lobby returns pilled and a full lobby returns
    /// full — no state is lost across the launcher minimise.
    /// </summary>
    private void LauncherWindow_StateChanged(object? sender, EventArgs e)
    {
        var launcher = _launcherWindow;
        if (launcher == null) return;
        DiagnosticLog.Write($"LobbyWindow: launcher StateChanged = {launcher.WindowState}, hiddenByOwner={_hiddenByOwner}, isPilled={_isPilled}, visible={IsVisible}");

        if (launcher.WindowState == WindowState.Minimized)
        {
            if (IsVisible)
            {
                _hiddenByOwner = true;
                Hide();
            }
        }
        else if (_hiddenByOwner)
        {
            _hiddenByOwner = false;
            Show();
            Activate();
        }
    }

    /// <summary>
    /// Deferred (one tick after a real minimize hit) so the launcher's own
    /// minimize has settled: if the launcher is now minimized this was a
    /// propagated minimize → hide with it; otherwise we alone were minimized
    /// → show the pill. Guards against double-handling when
    /// <see cref="LauncherWindow_StateChanged"/> already hid us.
    /// </summary>
    private void DecidePillOrHideWithOwner()
    {
        if (_isPilled || _hiddenByOwner) return;

        bool launcherMinimized = _launcherWindow is { WindowState: WindowState.Minimized };
        DiagnosticLog.Write($"LobbyWindow: deferred decide; launcherMinimized={launcherMinimized}");

        if (launcherMinimized)
        {
            if (IsVisible)
            {
                _hiddenByOwner = true;
                Hide();
            }
        }
        else
        {
            EnterPill();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_launcherWindow != null)
            _launcherWindow.StateChanged -= LauncherWindow_StateChanged;
        base.OnClosed(e);
    }

    // ------------------------------------------------------------------
    // Minimized pill: enter / restore + the blue neon glow.
    // ------------------------------------------------------------------

    /// <summary>
    /// Collapse the lobby into the small glowing pill docked at the
    /// bottom-left of the work area, in place of the OS minimize stub.
    /// We stay in <see cref="WindowState.Normal"/> the whole time (the
    /// opaque pill overlay covers the chrome + content), so Windows never
    /// draws its stub and a click can't summon the system menu. Idempotent.
    /// </summary>
    private void EnterPill()
    {
        if (_isPilled) return;
        _isPilled = true;
        DiagnosticLog.Write("LobbyWindow: EnterPill (showing floating pill)");

        // Remember where to come back to. Use RestoreBounds for the
        // non-Normal cases so restore returns to the pre-maximise rect (and
        // so a restore never lands on Minimized).
        _restoreState = WindowState == WindowState.Minimized ? WindowState.Normal : WindowState;
        var rb = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;
        _restoreLeft = rb.Left; _restoreTop = rb.Top;
        _restoreWidth = rb.Width; _restoreHeight = rb.Height;
        _restoreMinWidth = MinWidth; _restoreMinHeight = MinHeight;
        _restoreResizeMode = ResizeMode;
        _hasPillSavedBounds = true;

        // Force Normal (undo any in-flight minimise) without re-triggering
        // the OnStateChanged → EnterPill path.
        _suppressStateChange = true;
        WindowState = WindowState.Normal;
        _suppressStateChange = false;

        // Lower the size floor before shrinking (MinWidth/Height are 600/420)
        // and lock resize so the pill edges can't drag-resize it.
        ResizeMode = ResizeMode.NoResize;
        MinWidth = PillWidth; MinHeight = PillHeight;
        Width = PillWidth; Height = PillHeight;

        // Dock bottom-left of the primary work area (where the old OS stub
        // lived). Multi-monitor lands on the primary monitor — the same
        // documented primary-monitor assumption as the hero-scale code.
        var wa = SystemParameters.WorkArea;
        Left = wa.Left + PillMargin;
        Top = wa.Bottom - PillHeight - PillMargin;

        // Drop the window's 1 px grey edge so the blue neon stroke is the
        // outermost line (restored on un-pill).
        LobbyOuterFrame.BorderThickness = new Thickness(0);

        MinimizedPill.Visibility = Visibility.Visible;
        StartPillGlow();
    }

    /// <summary>
    /// Restore the lobby from the pill back to its saved geometry/state.
    /// Idempotent — a no-op if not currently pilled.
    /// </summary>
    private void RestoreFromPill()
    {
        if (!_isPilled) return;
        _isPilled = false;

        StopPillGlow();
        MinimizedPill.Visibility = Visibility.Collapsed;
        LobbyOuterFrame.BorderThickness = new Thickness(1);   // restore the 1 px edge

        if (_hasPillSavedBounds)
        {
            MinWidth = _restoreMinWidth;
            MinHeight = _restoreMinHeight;
            ResizeMode = _restoreResizeMode;
            // Set the Normal-state geometry before flipping state, so a
            // restore-to-Maximized still has a sane rect to un-maximise to.
            Left = _restoreLeft; Top = _restoreTop;
            Width = _restoreWidth; Height = _restoreHeight;

            _suppressStateChange = true;
            WindowState = _restoreState;
            _suppressStateChange = false;
        }

        Activate();
    }

    /// <summary>
    /// Bring the window back if it's sitting as the minimized pill; no-op
    /// otherwise. Called from MultiplayerTab when the user re-opens an
    /// already-open lobby — a bare Activate() wouldn't un-pill it.
    /// </summary>
    public void RestoreFromMinimized() => RestoreFromPill();

    /// <summary>Single click on the pill restores the lobby.</summary>
    private void MinimizedPill_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => RestoreFromPill();

    /// <summary>
    /// Start the blue "neon" glow — the pill's border + inward halo
    /// breathe via looping, auto-reversing animations: the
    /// DropShadowEffect's blur + opacity pulse, and the (local, mutable)
    /// BorderBrush colour shifts between a mid blue and a bright cyan.
    /// </summary>
    private void StartPillGlow()
    {
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        var period = new Duration(TimeSpan.FromSeconds(1.3));

        if (PillGlowBorder.Effect is DropShadowEffect fx)
        {
            fx.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(8, 18, period)
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = ease,
            });
            fx.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(0.5, 0.95, period)
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = ease,
            });
        }

        if (PillGlowBorder.BorderBrush is SolidColorBrush brush)
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(
                Color.FromRgb(0x4F, 0x8F, 0xD8),   // mid blue (idle)
                Color.FromRgb(0xA6, 0xDB, 0xFF),   // bright cyan highlight
                period)
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = ease,
            });
        }
    }

    /// <summary>Stop the glow animations; passing null reverts each
    /// property to its XAML base value (idle blue #4F8FD8, blur 14, opacity 0.7).</summary>
    private void StopPillGlow()
    {
        if (PillGlowBorder.Effect is DropShadowEffect fx)
        {
            fx.BeginAnimation(DropShadowEffect.BlurRadiusProperty, null);
            fx.BeginAnimation(DropShadowEffect.OpacityProperty, null);
        }
        if (PillGlowBorder.BorderBrush is SolidColorBrush brush)
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
    }

    /// <summary>
    /// Pulse the countdown bar's border + halo so an active timer is
    /// unmistakable. Same recipe as the pill glow: animate the (local,
    /// unfrozen) BorderBrush colour between mid-blue and bright cyan and
    /// the DropShadowEffect's blur + opacity, looping + auto-reversing.
    /// Public so <c>MultiplayerTab.ApplyMatchPhaseUi</c> can drive it
    /// alongside the bar's visibility. Idempotent — restarting just
    /// replaces the running clocks.
    /// </summary>
    public void StartCountdownGlow()
    {
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        var period = new Duration(TimeSpan.FromSeconds(1.1));

        if (CountdownOverlay.Effect is DropShadowEffect fx)
        {
            fx.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(8, 22, period)
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = ease,
            });
            fx.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(0.35, 0.9, period)
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = ease,
            });
        }

        if (CountdownOverlay.BorderBrush is SolidColorBrush brush)
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(
                Color.FromRgb(0x4F, 0x8F, 0xD8),   // mid blue (idle)
                Color.FromRgb(0xA6, 0xDB, 0xFF),   // bright cyan highlight
                period)
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = ease,
            });
        }
    }

    /// <summary>Stop the countdown-bar glow; null reverts each property to
    /// its XAML base value. Safe to call when no glow is running.</summary>
    public void StopCountdownGlow()
    {
        if (CountdownOverlay.Effect is DropShadowEffect fx)
        {
            fx.BeginAnimation(DropShadowEffect.BlurRadiusProperty, null);
            fx.BeginAnimation(DropShadowEffect.OpacityProperty, null);
        }
        if (CountdownOverlay.BorderBrush is SolidColorBrush brush)
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
    }

    private void LeaveRoomButton_Click(object sender, RoutedEventArgs e) => OnLeaveRoom?.Invoke();
    private void ReadyButton_Click(object sender, RoutedEventArgs e) => OnReady?.Invoke();
    private void StartButton_Click(object sender, RoutedEventArgs e) => OnStart?.Invoke();
    private void InGameCancelButton_Click(object sender, RoutedEventArgs e) => OnInGameCancel?.Invoke();
    private void ClearChatButton_Click(object sender, RoutedEventArgs e) => OnClearChat?.Invoke();
    private void ChatSendButton_Click(object sender, RoutedEventArgs e) => OnSendChat?.Invoke();
    private void ChatEmojiButton_Click(object sender, RoutedEventArgs e) => OnEmoji?.Invoke();
    private void ChatInputBox_TextChanged(object sender, TextChangedEventArgs e) => OnChatTextChanged?.Invoke();
    private void ChatInputBox_KeyDown(object sender, KeyEventArgs e) => OnChatKeyDown?.Invoke(e);

    /// <summary>
    /// Copy the room code to the clipboard, flashing a ✓ on the button
    /// for a moment as confirmation. Pure UI with no session coupling,
    /// so unlike the other handlers it does the work here directly
    /// instead of forwarding to a MultiplayerTab callback.
    /// </summary>
    private void CopyRoomIdButton_Click(object sender, RoutedEventArgs e)
    {
        var code = RoomIdText.Text;
        if (string.IsNullOrWhiteSpace(code)) return;
        try { Clipboard.SetText(code); }
        catch { return; } // clipboard can be momentarily locked by another app

        CopyRoomIdButton.Content = "✓";
        var revert = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.4) };
        revert.Tick += (_, _) =>
        {
            CopyRoomIdButton.Content = "📋";
            revert.Stop();
        };
        revert.Start();
    }
}
