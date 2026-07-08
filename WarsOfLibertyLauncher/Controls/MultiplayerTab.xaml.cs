using System;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// Top-level multiplayer UI. Owns no state itself — everything sits in
/// <see cref="MultiplayerSession"/> and we re-render whenever it raises
/// <c>StateChanged</c>. The host (MainWindow) injects the session via
/// <see cref="Attach"/> after construction, mirroring how the rest of
/// the controls get their services.
///
/// Subtab navigation is purely visual; the same session backs every
/// view, so switching subtab is just toggling which Grid is visible.
/// </summary>
public partial class MultiplayerTab : UserControl
{
    private enum Subtab { Rooms, Friends, Profile, History }

    private MultiplayerSession? _session;
    private Func<ModProfile?>? _getActiveProfile;
    private Func<ModProfile, Task<string>>? _computeModFingerprint;
    /// <summary>
    /// MainWindow-provided launch hook. Returns the spawned process so
    /// the multiplayer flow can subscribe to its Exited event (replay
    /// upload, match reporting). Null when the host has no active mod
    /// install — in that case the multiplayer tab declines to launch
    /// rather than guessing.
    ///
    /// 3rd param is the room-aware extra args string built by
    /// <see cref="BuildMultiplayerLaunchArgs"/> — keeps the
    /// multiplayer-specific flag knowledge local to this control so
    /// MainWindow stays a dumb plumber.
    /// </summary>
    private Func<ModProfile, EventHandler, string?, System.Diagnostics.Process?>? _launchGame;

    /// <summary>
    /// MainWindow-provided callback to switch the launcher's active
    /// mod profile in place (same path the Play-tab tiles use). The
    /// multiplayer-join flow asks for the switch when the user
    /// clicks Join on a room hosted by a different mod than the
    /// one currently active — instead of forcing them to navigate
    /// to Play, click the tile, then come back. Returns true when
    /// the switch succeeded (or the target was already active).
    /// </summary>
    private Func<ModProfile, bool>? _switchActiveMod;

    /// <summary>
    /// MainWindow-provided callback to rotate the ACTIVE install copy of the
    /// active mod (wraps <c>SwitchActiveInstallAsync</c>). Used by the create-room
    /// copy picker: multiplayer always launches / fingerprints the active copy, so
    /// choosing a copy there switches the active copy (single source of truth).
    /// </summary>
    private Func<string, Task>? _switchActiveCopy;

    private Subtab _activeSubtab = Subtab.Rooms;
    private bool _isRefreshingList;
    private bool _isRefreshingHistory;

    private System.Windows.Threading.DispatcherTimer? _quotaTimer;

    /// <summary>
    /// Polls the overall connection ping and refreshes the rooms-browser
    /// PING cells in place while the Multiplayer tab is visible. Tied to the
    /// same visibility gate as <see cref="_quotaTimer"/>.
    /// </summary>
    private System.Windows.Threading.DispatcherTimer? _roomsPingTimer;

    /// <summary>
    /// Auto-refreshes the rooms-browser LIST (a quiet, diff-based render
    /// that only repaints when the set of rooms actually changed) while the
    /// Multiplayer tab is visible AND the Rooms subtab is active, so newly
    /// created rooms appear without the user pressing Actualizar. Separate
    /// from <see cref="_roomsPingTimer"/> (which owns the PING column) and
    /// tied to the same visibility gate as <see cref="_quotaTimer"/>.
    /// </summary>
    private System.Windows.Threading.DispatcherTimer? _roomsListTimer;

    /// <summary>
    /// Signature of the rooms list as last rendered, used by the quiet
    /// auto-refresh to skip a full re-render (and the Join-button rebuild it
    /// would cause) when nothing visible changed. Built by
    /// <see cref="BuildRoomsSignature"/> from the payload fields only — ping
    /// is excluded because <see cref="_roomsPingTimer"/> refreshes it in place.
    /// </summary>
    private string _lastRenderedRoomsSignature = "\0uninitialized";

    /// <summary>
    /// The PING cells of the currently-rendered rooms rows, so the ping can
    /// be refreshed in place (no row rebuild — that would disrupt the Join
    /// buttons). Rebuilt each time the rooms list re-renders.
    /// </summary>
    private readonly System.Collections.Generic.List<StackPanel> _roomPingCells = new();

    // Radmin banner state. The timer polls the install/connection
    // status every 3 s while the tab is visible so the user gets
    // immediate feedback when they finish installing or starting
    // Radmin from its own window. _lastRadminStatus is kept so the
    // primary button's click handler knows which branch (install vs
    // launch) to take without re-querying.
    private System.Windows.Threading.DispatcherTimer? _radminTimer;
    private RadminStatus? _lastRadminStatus;

    // (Pre-Radmin: there used to be n2n bootstrap status here for the
    //  header badge. With the n2n stack removed and Radmin as the
    //  user-managed VPN, the header badge just shows a static label.)

    /// <summary>Currently-subscribed WS, so we can unsubscribe cleanly on room change.</summary>
    private LobbyWebSocket? _attachedSocket;

    /// <summary>
    /// The persistent GLOBAL chat socket (the /global/ws room), owned here
    /// because its lifetime is gated on this tab's visibility + sign-in, not
    /// on being in a lobby. Reuses the generic <see cref="LobbyWebSocket"/>
    /// (SessionToken hello). Opened by <see cref="SyncGlobalChat"/> and torn
    /// down when the tab hides / the user signs out. Separate from
    /// <see cref="MultiplayerSession.RoomSocket"/> — a user can be in the
    /// global chat and a lobby at the same time.
    /// </summary>
    private LobbyWebSocket? _globalChatSocket;

    /// <summary>True once a <c>global_state</c> frame has populated the panel,
    /// so the empty-hint can say "connecting…" before then and "no messages"
    /// after.</summary>
    private bool _globalChatRendered;

    // (Pre-n2n: a per-room PeerMesh lived here so the tab could repaint
    //  when peer RTT/state changed. With n2n the local edge is owned by
    //  the session, peer-by-peer ping is no longer something we can
    //  observe at this layer, and connection state is just N2n.State on
    //  the session. The whole subscription dance is gone.)

    /// <summary>Live state of the current room, rebuilt as WS frames arrive.</summary>
    private readonly System.Collections.Generic.Dictionary<string, RoomMemberEntry> _roomMembers = new();
    private string? _roomHostUserId;
    private bool _isHostInCurrentRoom;

    private sealed class RoomMemberEntry
    {
        public required string UserId { get; init; }
        public string Login { get; set; } = "";
        /// <summary>Discord avatar URL from the room roster (room_state/member_joined),
        /// null for legacy rooms that don't send it yet → falls back to a monogram.</summary>
        public string? AvatarUrl { get; set; }
        public bool Ready { get; set; }
        /// <summary>Peer's Radmin VPN IP (26.x), from room_state / member_net.
        /// Null until they report it (at match launch). Used to ICMP-ping them
        /// for the in-game per-player ping column.</summary>
        public string? RadminIp { get; set; }
        /// <summary>Last measured ICMP RTT to <see cref="RadminIp"/> in ms;
        /// -1 = unknown / no answer / no IP yet.</summary>
        public int PingMs { get; set; } = -1;
        /// <summary>Consecutive non-answering probes — feeds
        /// <see cref="PeerNetHealth.Classify"/> so a single dropped packet doesn't
        /// flip the peer to "Lost". Reset to 0 on any answer.</summary>
        public int ConsecutiveFails { get; set; }
        /// <summary>Consecutive answering probes — used to debounce the
        /// "reconnected" chat edge. Reset to 0 on any failure.</summary>
        public int ConsecutiveOks { get; set; }
        /// <summary>Last link state we ANNOUNCED in chat, so RefreshInGamePanel only
        /// posts on the Online↔Lost edge (not every tick). Init WaitingVpn.</summary>
        public PeerLinkState LastLinkState { get; set; } = PeerLinkState.WaitingVpn;
    }

    // -------- Lobby window (replaces the old in-tab popup) ----------
    //
    // The lobby UI used to be a Canvas overlay inside this tab
    // (RoomPanel Grid + floating-card Border). We extracted it to a
    // real top-level Window so the user can drag/resize/move it freely
    // via OS chrome. Single-instance: opening a room with the window
    // already open just .Activate()s it. Closing it (✕/Esc/Alt+F4)
    // fires Closed which clears this field AND triggers leave-room on
    // the session if we're still in one (see HandleLobbyWindowClosed).
    //
    // Render* and Apply* methods below guard on _lobbyWindow == null
    // and return early — they're invoked from session events that may
    // fire after the window was already closed (e.g. host disconnect
    // race) and we shouldn't crash on a null reference. When non-null,
    // they read/write the window's UI elements directly through the
    // field-modifier-internal x:Name fields (same assembly).
    private LobbyWindow? _lobbyWindow;

    // -------- Match lifecycle state ---------------------------------
    //
    // Three logical phases:
    //   Lobby     — popup is fully interactive, X visible
    //   Starting  — countdown overlay shown, no X
    //   InGame    — InGame overlay shown, only Cancel/Leave
    //
    // We track the phase locally so the UI gates immediately without
    // waiting for a round-trip to the server. The Worker's
    // game_countdown / game_started / game_cancelled frames flip
    // this; the popup's UI responds to changes.

    private enum MatchPhase { Lobby, Starting, InGame }
    private MatchPhase _matchPhase = MatchPhase.Lobby;

    /// <summary>
    /// Abort grace window (ms) AFTER AoE3 launches. Within it, ANY member can
    /// abort the match for everyone (covers a bad/desynced start while the map
    /// loads). After it, "leave" only removes yourself. Must stay ≤ the backend's
    /// own window (COUNTDOWN_MS + 60s) — the server is authoritative; this just
    /// flips the button UX so the user isn't offered an abort the server rejects.
    /// </summary>
    private const long AbortWindowMs = 60_000;

    /// <summary>
    /// True while aborting the match for everyone is still allowed: the whole
    /// countdown (Starting), plus the first <see cref="AbortWindowMs"/> after
    /// launch (InGame). Measured off the local launch tick — the same moment the
    /// server measures from once the synchronised countdown fires.
    /// </summary>
    private bool WithinAbortWindow =>
        _matchPhase == MatchPhase.Starting
        || (_matchPhase == MatchPhase.InGame
            && (Environment.TickCount64 - _matchTimerStartTicks) < AbortWindowMs);

    /// <summary>
    /// AoE3 process spawned when the countdown completed. Cached so
    /// <see cref="InGameCancelButton_Click"/> can <c>Kill()</c> it on
    /// cancel without re-walking the process table. Cleared when the
    /// process exits or we leave the room.
    /// </summary>
    private System.Diagnostics.Process? _aoe3Process;
    private DateTime _matchStartedAtUtc;
    private long _matchTimerStartTicks;

    /// <summary>
    /// Radmin-adapter total-byte counter captured when the match started,
    /// so the InGame TRAFFIC stat can show bytes moved during THIS match
    /// (the OS counter is cumulative since the adapter came up). -1 = the
    /// adapter wasn't found at match start, so we show "—".
    /// </summary>
    private long _matchBaselineBytes = -1;

    /// <summary>
    /// Last measured internet latency (ICMP RTT to a public host — see
    /// <see cref="PingInternetRttMsAsync"/>), in ms; -1 = unknown/no answer.
    /// Refreshed by a fire-and-forget probe, guarded by
    /// <see cref="_connectionPingInFlight"/>.
    /// </summary>
    private int _connectionPingMs = -1;
    private bool _connectionPingInFlight;

    /// <summary>Drives the breathing animation of the InGame "live" dot + match timer.</summary>
    private System.Windows.Threading.DispatcherTimer? _inGameTickTimer;

    /// <summary>Drives the per-frame countdown number 3 → 2 → 1.</summary>
    private System.Windows.Threading.DispatcherTimer? _countdownTickTimer;

    /// <summary>
    /// Polls the overall connection ping (seed-peer RTT) while the lobby
    /// window is open, so the room header's CONNECTION stat stays live
    /// even before a match starts. Stopped when the window closes.
    /// </summary>
    private System.Windows.Threading.DispatcherTimer? _lobbyPingTimer;

    /// <summary>
    /// Local tick count when the countdown started + the total
    /// duration in ms. We use a purely-local timer (not the
    /// server's `starts_at_ms` absolute timestamp) because client
    /// and server clocks can drift by seconds, which made the
    /// countdown either skip entirely (client ahead → remaining
    /// is negative the moment the frame arrives) or run too long
    /// (client behind). Per-peer drift is bounded by WS latency
    /// variance (~50 ms), which is fine for AoE3 LAN host setup.
    /// </summary>
    private long _countdownStartedAtTicks;
    private int _countdownDurationMs = 3000;

    /// <summary>
    /// Launcher config reference, injected via <see cref="Attach"/>.
    /// Read by the Radmin assistant auto-open logic to honour the
    /// user's mode preference (Auto / OnRequest / Never) and to
    /// flip <c>RadminAssistantSkipped</c> when the user ticks the
    /// "Don't show again" checkbox inside the overlay.
    /// </summary>
    private LauncherConfig? _config;

    /// <summary>
    /// Tracks whether we've already attempted to auto-open the
    /// Radmin assistant during the current launcher session. Without
    /// this guard the assistant would re-open every time the user
    /// switched tabs back to Multiplayer (because StartRadminPolling
    /// re-runs from the IsVisibleChanged hook). One auto-open per
    /// session is enough — if they closed it they can reopen via the
    /// banner's "Show steps" button.
    /// </summary>
    private bool _radminAssistantAutoOpenedThisSession;

    /// <summary>
    /// The currently-open assistant window, if any. Kept so a
    /// second "Show steps" click brings the existing window to
    /// front instead of opening a duplicate.
    /// </summary>
    private RadminAssistantWindow? _radminAssistantWindow;

    public MultiplayerTab()
    {
        InitializeComponent();
        ApplyStrings();

        // Window-size scaling for the whole Multiplayer surface (Controls/UiScale.cs).
        // sizeSource = this UserControl (host-sized, transform-independent → no
        // feedback). A LayoutTransform makes the scaled root still fill its slot,
        // so the MpAlertOverlay scrim injected into the root keeps covering the
        // full tab.
        if (Content is FrameworkElement mpRoot)
            UiScale.Attach(mpRoot, this, 1100, 604);
        // Keep the rooms header aligned with the rows as the vertical scrollbar
        // comes and goes (see SyncHeaderScrollbarGutter).
        RoomsListScroll.ScrollChanged += (_, _) => SyncHeaderScrollbarGutter();
        // Initial Radmin banner render (state poll + paint). The timer
        // starts ticking only once IsVisible flips to true via the
        // OnVisibleChangedTabGate hook installed by Attach().
        RefreshRadminBanner();
        // Initial state is the signed-out gate; once Attach() runs we
        // re-render against the real session.
        RefreshFromSession();
    }

    // ------------------------------------------------------------------
    // Radmin VPN banner — reactive 3-state UI driven by RadminVpnService.
    //
    // The banner sits at the top of the rooms browser. We re-render it
    // every 3 s while the tab is visible so manual state changes the
    // user makes in Radmin's own window (connect, disconnect, install
    // mid-session) are reflected without them having to navigate away
    // and back.
    //
    // The user dismiss flag (previously RadminBannerDismissed) was
    // removed in this iteration: the new banner is informative (small,
    // colour-coded) rather than nagging, and a dismissed user who
    // later forgets why their game isn't connecting has no recourse
    // otherwise. The config field has also been deleted.
    // ------------------------------------------------------------------

    /// <summary>
    /// Query Radmin's current state and update the banner's icon,
    /// title, body and primary-action button to match. Cheap (sub-ms
    /// registry + NIC enumeration), safe to call on the UI thread.
    /// </summary>
    private void RefreshRadminBanner()
    {
        if (RadminBanner == null) return;

        var status = RadminVpnService.GetStatus();
        _lastRadminStatus = status;

        // Three-way switch driven by (InstallState, IsServiceRunning):
        //   * NotInstalled              → red    "Install"
        //   * Installed, service off    → blue   "Open Radmin"
        //   * Service on (any state)    → green  "Radmin running — copy/paste the AoE3 network name"
        //
        // We DON'T try to distinguish "in the AoE3 network" from "in
        // some other Radmin network" or "in no network at all". Radmin
        // keeps per-network membership inside its own process — the OS
        // only learns about specific peers when there's actual IP
        // traffic with them (typically 1-2 entries even for a 20+
        // member active network), so any peer-count heuristic produces
        // misleading false negatives. We report the honest signal
        // ("Radmin is on"), put the network name + a Copy button
        // directly in the banner, and number the manual steps. That's
        // as low-friction as the GUI-only manual flow can be made.
        if (status.InstallState == RadminInstallState.NotInstalled)
        {
            RadminBanner.Background = (Brush)new BrushConverter().ConvertFromString("#3d1f1f")!;
            RadminBanner.BorderBrush = (Brush)new BrushConverter().ConvertFromString("#8c3a3a")!;
            RadminStatusIcon.Background = (Brush)new BrushConverter().ConvertFromString("#8c3a3a")!;
            RadminStatusGlyph.Text = "!";
            RadminBannerTitle.Text = Strings.Get("MpRadminNotInstalledTitle");
            RadminBannerBody.Text = Strings.Get("MpRadminNotInstalledBody");
            RadminBannerBody.Visibility = Visibility.Visible;
            RadminPrimaryButton.Content = Strings.Get("MpRadminInstallButton");
            RadminPrimaryButton.Visibility = Visibility.Visible;
            RadminPrimaryButton.IsEnabled = true;
            // No actionable network info to show while Radmin isn't on yet.
            RadminNetworkNamePanel.Visibility = Visibility.Collapsed;
            RadminInstructionsText.Visibility = Visibility.Collapsed;
        }
        else if (!status.IsServiceRunning)
        {
            // RED warning (same palette as NotInstalled): "not ready" —
            // Radmin is installed but closed OR powered off ("Desconectado").
            // A blue "info" tone under-sold it; this is an action-required
            // state, so it reads as a warning like the traffic-light green.
            RadminBanner.Background = (Brush)new BrushConverter().ConvertFromString("#3d1f1f")!;
            RadminBanner.BorderBrush = (Brush)new BrushConverter().ConvertFromString("#8c3a3a")!;
            RadminStatusIcon.Background = (Brush)new BrushConverter().ConvertFromString("#8c3a3a")!;
            RadminStatusGlyph.Text = "!";
            RadminBannerTitle.Text = Strings.Get("MpRadminNotConnectedTitle");
            RadminBannerBody.Text = Strings.Get("MpRadminNotConnectedBody");
            RadminBannerBody.Visibility = Visibility.Visible;
            RadminPrimaryButton.Content = Strings.Get("MpRadminOpenButton");
            RadminPrimaryButton.Visibility = Visibility.Visible;
            RadminPrimaryButton.IsEnabled = true;
            RadminNetworkNamePanel.Visibility = Visibility.Collapsed;
            RadminInstructionsText.Visibility = Visibility.Collapsed;
        }
        else
        {
            RadminBanner.Background = (Brush)new BrushConverter().ConvertFromString("#123C2B")!;
            RadminBanner.BorderBrush = (Brush)new BrushConverter().ConvertFromString("#732E7D4F")!;
            RadminStatusIcon.Background = (Brush)new BrushConverter().ConvertFromString("#22C55E")!;
            RadminStatusGlyph.Text = "✓";
            // Compact one-line layout for the running state: the title
            // carries both the status and the IP, body/copier/steps are
            // hidden because the RadminAssistantWindow (reachable via
            // "Show steps") is now the place where the user verifies
            // the network membership and copies the join name.
            RadminBannerTitle.Text = Strings.Format(
                "MpRadminConnectedTitleCompact",
                status.AdapterIp ?? "26.x.x.x");
            RadminBannerBody.Text = string.Empty;
            RadminBannerBody.Visibility = Visibility.Collapsed;
            RadminPrimaryButton.Content = Strings.Get("MpRadminOpenButton");
            RadminPrimaryButton.Visibility = Visibility.Visible;
            RadminPrimaryButton.IsEnabled = true;
            RadminNetworkNamePanel.Visibility = Visibility.Collapsed;
            RadminInstructionsText.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Dedicated "copy the network name" button — separate from the
    /// auto-copy that happens when "Open Radmin" is clicked, so the
    /// user can grab a fresh copy without re-launching the GUI (handy
    /// when they accidentally overwrote the clipboard with something
    /// else before pasting into Radmin's Join dialog).
    /// </summary>
    private void RadminCopyNameButton_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(RadminVpnService.AoE3TadNetworkName); }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"RadminCopyNameButton_Click: clipboard: {ex.Message}");
            return;
        }
        FlashCopiedToast(RadminCopyNameButton);
    }

    /// <summary>
    /// Briefly swap a button's label to "Copied!" so the click feels
    /// acknowledged, then restore the original text after a short
    /// delay. Pure UI candy — no behavioural consequence beyond the
    /// visual feedback.
    /// </summary>
    private void FlashCopiedToast(System.Windows.Controls.Button button)
    {
        var original = button.Content;
        button.Content = Strings.Get("MpRadminCopiedToast");
        button.IsEnabled = false;
        var revert = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1500),
        };
        revert.Tick += (_, _) =>
        {
            revert.Stop();
            button.Content = original;
            button.IsEnabled = true;
        };
        revert.Start();
    }

    /// <summary>
    /// State-aware click handler for the banner's primary button. Routes
    /// to install / launch based on the last polled status — that's
    /// what was on screen when the user clicked, so it's always the
    /// right action to perform even if the world changed mid-frame.
    /// </summary>
    /// <summary>
    /// "Show steps" button on the Radmin banner. Opens (or focuses)
    /// the assistant overlay window — same window the auto-open path
    /// uses. Independent of the assistant mode so a power user who
    /// turned auto-open off can still summon the overlay when they
    /// genuinely need the tutorial.
    /// </summary>
    private void RadminShowStepsButton_Click(object sender, RoutedEventArgs e)
    {
        ShowRadminAssistant();
    }

    private async void RadminPrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        var status = _lastRadminStatus ?? RadminVpnService.GetStatus();

        if (status.InstallState == RadminInstallState.NotInstalled)
        {
            await RunRadminAutoInstallAsync();
        }
        else
        {
            // Pre-load the AoE3 TAD network name into the clipboard so
            // the user only has to Ctrl+V into Radmin's "Join network"
            // dialog instead of typing 38 characters of mixed case.
            // Clipboard access can fail under restricted RDP / locked
            // workstation sessions; swallow + log so the launcher still
            // lifts the GUI window.
            try { Clipboard.SetText(RadminVpnService.AoE3TadNetworkName); }
            catch (Exception ex) { DiagnosticLog.Write($"RadminPrimaryButton_Click: clipboard: {ex.Message}"); }

            if (!string.IsNullOrEmpty(status.ExePath))
            {
                var launched = RadminVpnService.LaunchGui(status.ExePath);
                if (!launched)
                {
                    await MpAlertOverlay.NoticeAsync(
                        TabRootGrid,
                        Strings.Get("MpNoticeRadminLaunchTitle"),
                        Strings.Get("MpRadminLaunchFailed"),
                        Strings.Get("MpAlertOk"));
                }
            }
            // Immediate refresh — the new connection state will only
            // show once the user actually clicks Join inside Radmin,
            // but if Radmin was already connected and the launcher
            // just opened the window, the banner should still tick
            // visibly so the click feels responsive.
            RefreshRadminBanner();
        }
    }

    /// <summary>
    /// Download Famatech's MSI and run a silent install, with a
    /// progress label in the banner body. UAC fires once because the
    /// MSI installs a system service + TAP driver. On any failure
    /// we degrade gracefully to opening the download page in the
    /// browser so the user still has a path forward.
    /// </summary>
    private async Task RunRadminAutoInstallAsync()
    {
        if (RadminBanner == null) return;
        RadminPrimaryButton.IsEnabled = false;

        var progress = new Progress<int>(p =>
        {
            RadminBannerBody.Text = string.Format(Strings.Get("MpRadminInstalling"), p);
        });

        bool ok;
        try
        {
            ok = await RadminVpnService.InstallSilentAsync(progress, CancellationToken.None);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"MultiplayerTab.RunRadminAutoInstallAsync: {ex.Message}");
            ok = false;
        }

        if (!ok)
        {
            RadminBannerBody.Text = Strings.Get("MpRadminInstallFailed");
            RadminVpnService.OpenDownloadPageInBrowser();
        }

        // One immediate refresh to flip the banner to "installed but
        // not connected" if msiexec succeeded. The 3 s timer will keep
        // it honest if the user proceeds to join a network manually.
        RefreshRadminBanner();
    }

    /// <summary>
    /// Start the 3-second poll. Called from the IsVisible hook so we
    /// don't burn CPU enumerating NICs while the user is on another
    /// tab. Idempotent — calling twice is safe.
    /// </summary>
    private void StartRadminPolling()
    {
        if (_radminTimer == null)
        {
            _radminTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3),
            };
            _radminTimer.Tick += (_, _) => RefreshRadminBanner();
        }
        RefreshRadminBanner();   // one-shot so the user sees fresh data immediately
        _radminTimer.Start();

        // First-time visit to the Multiplayer tab in this session →
        // maybe pop the Radmin assistant overlay. Gated by config so
        // the user can opt out (Mode=Never), one-shot dismiss
        // (RadminAssistantSkipped), or already-connected detection
        // (we don't pop the overlay when they're past LoggedIn —
        // that means everything is working).
        MaybeAutoOpenAssistant();
    }

    /// <summary>
    /// Decide whether to auto-open the Radmin assistant overlay this
    /// session. Three gates:
    ///   1. Mode != "Never" — user explicitly disabled the assistant
    ///   2. !RadminAssistantSkipped — user previously ticked "don't
    ///      show again"
    ///   3. Stage &lt; LoggedIn — already signed in to Radmin? skip
    ///      auto-open since the user clearly knows what they're
    ///      doing (the "Show steps" button stays available if they
    ///      want it).
    /// Also guarded by _radminAssistantAutoOpenedThisSession so
    /// repeated tab switches don't keep re-opening the overlay —
    /// once auto-opened (or once we decided to skip), we stay quiet
    /// for the rest of the session.
    /// </summary>
    private async void MaybeAutoOpenAssistant()
    {
        if (_radminAssistantAutoOpenedThisSession) return;
        _radminAssistantAutoOpenedThisSession = true;

        if (_config == null) return;
        if (string.Equals(_config.RadminAssistantMode, "Never", StringComparison.OrdinalIgnoreCase)) return;
        if (string.Equals(_config.RadminAssistantMode, "OnRequest", StringComparison.OrdinalIgnoreCase)) return;
        if (_config.RadminAssistantSkipped) return;

        try
        {
            var snap = await RadminAssistantService.ProbeAsync();
            // Skip auto-open if the user is already past LoggedIn —
            // they don't need a tutorial for something that's working.
            // Future: when seed-peer ping ships, this also catches
            // InAoE3Network → nothing to teach.
            if (snap.Stage >= RadminStage.LoggedIn) return;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"MultiplayerTab.MaybeAutoOpenAssistant: probe failed: {ex.Message}");
            // Probe failure → don't auto-open. Better to stay quiet
            // than to flash a confused overlay.
            return;
        }

        ShowRadminAssistant();
    }

    /// <summary>
    /// Open (or focus) the Radmin assistant overlay. Single-instance:
    /// a second click brings the existing window to front instead of
    /// opening a duplicate that would race the first one on the 3s
    /// poll timer.
    /// </summary>
    private void ShowRadminAssistant()
    {
        if (_config == null) return;
        if (_radminAssistantWindow != null)
        {
            try
            {
                _radminAssistantWindow.Activate();
                if (_radminAssistantWindow.WindowState == WindowState.Minimized)
                    _radminAssistantWindow.WindowState = WindowState.Normal;
                return;
            }
            catch
            {
                // Previous window was disposed without our Closed
                // hook firing (rare — usually means the dispatcher
                // queue ate the event). Fall through to recreate.
                _radminAssistantWindow = null;
            }
        }

        var win = new RadminAssistantWindow(_config);
        win.Closed += (_, _) =>
        {
            if (ReferenceEquals(_radminAssistantWindow, win))
                _radminAssistantWindow = null;
        };
        _radminAssistantWindow = win;
        // Owner = the main window so the overlay sits above it but
        // stops appearing in the taskbar (ShowInTaskbar=false in
        // XAML), and so closing the main launcher also closes the
        // overlay. Wrapped in try because Window.GetWindow returns
        // null in some unit-test paths.
        try
        {
            var owner = Window.GetWindow(this);
            if (owner != null) win.Owner = owner;
        }
        catch { /* fall through to ownerless */ }
        win.Show();
    }

    /// <summary>
    /// Wired to the new "Show steps" button on the Radmin banner.
    /// Exposed publicly so MainWindow could trigger it from a global
    /// shortcut in the future without re-implementing the same logic.
    /// </summary>
    public void OpenRadminAssistantWindow() => ShowRadminAssistant();

    /// <summary>
    /// Wires the control to its dependencies. Called once from
    /// MainWindow after the session is constructed. The
    /// <paramref name="computeModFingerprint"/> callback hashes the
    /// currently-installed mod files using <see cref="ModHashService"/>
    /// and returns the combined hash — kept as a callback so the heavy
    /// I/O lives on the host's thread pool instead of behind this
    /// UserControl.
    /// </summary>
    public void Attach(
        MultiplayerSession session,
        Func<ModProfile?> getActiveProfile,
        Func<ModProfile, Task<string>> computeModFingerprint,
        Func<ModProfile, EventHandler, string?, System.Diagnostics.Process?>? launchGame = null,
        Func<ModProfile, bool>? switchActiveMod = null,
        LauncherConfig? config = null,
        Func<string, Task>? switchActiveCopy = null)
    {
        if (_session != null)
        {
            _session.StateChanged -= OnSessionStateChanged;
            // Drop the old session's global chat socket before rebinding.
            CloseGlobalChat();
        }

        _session = session;
        _getActiveProfile = getActiveProfile;
        _computeModFingerprint = computeModFingerprint;
        _launchGame = launchGame;
        _switchActiveMod = switchActiveMod;
        _switchActiveCopy = switchActiveCopy;
        // Optional so old callers (and the parameterless ctor path
        // used by XAML preview) still work — null _config just means
        // the Radmin assistant features stay dormant.
        _config = config;
        session.StateChanged += OnSessionStateChanged;

        RefreshFromSession();

        // Fire-and-forget probes that don't hit the Worker (cheap, no
        // budget cost). The expensive ones — RefreshQuotaAsync /
        // RefreshRoomsListAsync — are gated below so they only run
        // when the user is actually looking at the Multiplayer tab.
        // No async bootstrap to fire anymore — the game-network layer
        // (Radmin VPN) is user-managed; the launcher just paints the
        // static badge once via RenderNatBadge() further down.

        // Auto-refresh the quota bar every 60 s. Only ticks while
        // the control is *visible* — when the user switches to
        // Play / Mods / News / Settings, the IsVisibleChanged hook
        // stops the timer so we don't burn ~60 Worker requests/hour
        // on the launcher just being open in another tab. Resumes
        // when they come back to Multiplayer.
        _quotaTimer?.Stop();
        _quotaTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(60),
        };
        _quotaTimer.Tick += async (_, _) => await RefreshQuotaAsync();

        // Subscribe once. Multiple calls to Attach are guarded by
        // the unsubscribe step on the previous session above, but
        // IsVisibleChanged on this control is process-lifetime, so
        // we unsubscribe-then-resubscribe to keep the count at 1.
        IsVisibleChanged -= OnVisibleChangedTabGate;
        IsVisibleChanged += OnVisibleChangedTabGate;

        // Initial state: kick off the cheap fetches + the timer only
        // when we're already the visible tab (e.g. user launched the
        // app with Multiplayer as last-active-tab). Otherwise the
        // IsVisibleChanged handler will pick it up when the user
        // navigates here.
        if (IsVisible)
        {
            StartQuotaPolling();
            StartRadminPolling();
        }
    }

    /// <summary>
    /// Toggles the quota timer + initial fetches when this control's
    /// Visibility flips. Switching to the Multiplayer tab → fetch a
    /// fresh quota + lobbies snapshot and start the 60 s poll.
    /// Switching away → stop the poll so the launcher stops burning
    /// Worker requests while the user is reading the news or
    /// fiddling with settings.
    /// </summary>
    private void OnVisibleChangedTabGate(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            StartQuotaPolling();
            StartRadminPolling();
        }
        else
        {
            _quotaTimer?.Stop();
            _radminTimer?.Stop();
            _roomsPingTimer?.Stop();
            _roomsListTimer?.Stop();
            CloseGlobalChat();
        }
    }

    private void StartQuotaPolling()
    {
        // One-shot fetch on activation so the user sees fresh data
        // immediately (otherwise they'd wait up to 60 s for the
        // first timer tick after switching to this tab).
        _ = RefreshQuotaAsync();
        if (_session?.Status == MultiplayerSession.SessionStatus.SignedIn)
            _ = RefreshRoomsListAsync();
        _quotaTimer?.Start();

        // Keep the rooms-browser PING (your connection latency) fresh every
        // ~3 s while the tab is visible, updating the cells in place.
        _roomsPingTimer?.Stop();
        _roomsPingTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3),
        };
        _roomsPingTimer.Tick += (_, _) => { KickConnectionPing(); RefreshRoomPingCells(); UpdateRoomsUpdatedLabel(); };
        _roomsPingTimer.Start();
        KickConnectionPing();

        // Auto-refresh the rooms LIST every ~5 s so newly-created rooms
        // appear quickly without the user pressing Actualizar. The fetch is a
        // quiet, diff-based render (no "loading" skeleton, no row/Join-button
        // rebuild when nothing changed — see RefreshRoomsListAsync(quiet:true))
        // and only fires while signed in AND on the Rooms subtab, so it costs
        // at most one cheap GET /lobbies every 5 s while the user is actually
        // browsing: 12 req/min (under the 60/min per-IP cap) and ~720/h — the
        // daily 2000/IP cap is only approached after hours of continuous
        // browsing, which is acceptable for a fresher list.
        _roomsListTimer?.Stop();
        _roomsListTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5),
        };
        _roomsListTimer.Tick += (_, _) =>
        {
            if (_session?.Status == MultiplayerSession.SessionStatus.SignedIn
                && _activeSubtab == Subtab.Rooms)
                _ = RefreshRoomsListAsync(quiet: true);
        };
        _roomsListTimer.Start();

        // Connect the persistent global chat while the tab is up + signed in.
        SyncGlobalChat();
    }

    public void RefreshStrings() => ApplyStrings();

    private void ApplyStrings()
    {
        SubtabRooms.Content = Strings.Get("MpSubtabRooms");
        SubtabFriends.Content = Strings.Get("MpSubtabFriends");
        SubtabProfile.Content = Strings.Get("MpSubtabProfile");
        SubtabHistory.Content = Strings.Get("MpSubtabHistory");

        // Radmin assistant "Show steps" button. Hidden when the
        // user disabled the assistant entirely via Settings
        // (Mode=Never) — the legacy Open Radmin / Install button
        // (RadminPrimaryButton) stays for them.
        RadminShowStepsButton.Content = Strings.Get("RadAsstBannerShowSteps");
        var mode = _config?.RadminAssistantMode;
        RadminShowStepsButton.Visibility =
            string.Equals(mode, "Never", StringComparison.OrdinalIgnoreCase)
                ? Visibility.Collapsed
                : Visibility.Visible;

        SignInTitleText.Text = Strings.Get("MpSignInTitle");
        SignInBodyText.Text = Strings.Get("MpSignInBody");
        SignInButton.Content = Strings.Get("MpSignInButton");

        SignOutLink.Content = Strings.Get("MpSignOutButton");
        // Compose icon + label using inline runs so the look stays
        // close to the reference (small glyph + word). Plain content
        // strings would be fine too — we keep them simple to avoid
        // pulling icon fonts.
        RefreshButton.Content = "↻  " + Strings.Get("MpRoomsRefresh");
        CreateRoomButton.Content = "+  " + Strings.Get("MpRoomsCreate");

        // Active-rooms section title + global chat panel labels.
        RoomsSectionTitle.Text = Strings.Get("MpRoomsSectionTitle");
        GlobalChatHeaderText.Text = Strings.Get("MpGlobalChatTitle");
        GlobalChatPlaceholder.Text = Strings.Get("MpGlobalChatPlaceholder");
        // Send is an icon button now — the localized caption lives on its ToolTip.
        GlobalChatSendButton.ToolTip = Strings.Get("MpGlobalChatSend");
        UpdateGlobalChatEmptyHint();

        // Lobby window labels — only updated if it's currently open.
        // Static labels go through ApplyLobbyStaticLabels(); the dynamic,
        // state-driven ones (status line, player count, ready toggle, …)
        // are refreshed by re-running RenderRoomPanel, so a mid-room
        // language switch re-localises the whole window at once.
        if (_lobbyWindow != null)
        {
            ApplyLobbyStaticLabels();
            RenderRoomPanel();
            // Re-localise the match-phase overlays too, so switching
            // language mid-countdown / mid-match refreshes the
            // cancel/leave button and in-game mode badge — not just
            // the lobby body.
            ApplyMatchPhaseUi();
            if (_matchPhase == MatchPhase.InGame)
                RefreshInGamePanel();
        }

        // Room-list column headers (localized) + empty-state copy.
        ColHeaderRoom.Text = Strings.Get("MpColRoom");
        ColHeaderMod.Text = Strings.Get("MpColMod");
        ColHeaderHost.Text = Strings.Get("MpColHost");
        ColHeaderPlayers.Text = Strings.Get("MpColPlayers");
        ColHeaderPing.Text = Strings.Get("MpColPing");
        ColHeaderStatus.Text = Strings.Get("MpColStatus");
        ColHeaderAction.Text = Strings.Get("MpColAction");
        UpdateSortArrows();
        EmptyTitleText.Text = Strings.Get("MpRoomsEmptyTitle");
        EmptyBodyText.Text = Strings.Get("MpRoomsEmptyBody");
        EmptyCreateButton.Content = "+  " + Strings.Get("MpRoomsCreate");
        UpdateRoomsUpdatedLabel();

        UpdateSubtabHighlights();
        RenderNatBadge();
        UpdateConnectionStatus();
    }

    /// <summary>
    /// Paint the header badge. Used to flip between NAT-type colours
    /// and later between n2n bootstrap states; now that the actual
    /// game network is Radmin VPN (managed by the user outside the
    /// launcher), the badge is purely informational — it reminds the
    /// user that Radmin is the connectivity layer and points at the
    /// banner above the rooms list for setup instructions.
    /// </summary>
    private void RenderNatBadge()
    {
        if (NatBadgeText == null || NatBadgeBorder == null) return;
        NatBadgeText.Text = "Radmin VPN";
        NatBadgeBorder.Background = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString("#182740"));
        NatBadgeText.Foreground = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString("#94A3B8"));
        NatBadgeBorder.ToolTip =
            "Multiplayer rooms and chat run through this launcher, but the actual game-to-game " +
            "connection is established over Radmin VPN. Make sure Radmin is installed and you " +
            "have joined the community network before starting a game.";
    }

    private void OnSessionStateChanged(object? sender, EventArgs e) =>
        Dispatcher.InvokeAsync(() =>
        {
            SyncRoomSocketSubscription();
            RefreshFromSession();
            // Sign-in / sign-out flips whether the global chat should be
            // connected; entering/leaving a room is harmless (idempotent).
            SyncGlobalChat();
        });

    // ------------------------------------------------------------------------
    // Offline mode (driven by MainWindow from the app-wide ConnectivityState)
    // ------------------------------------------------------------------------

    private bool _offlineMode;
    private string _offlineNeedsInternet = "";
    private string _offlineNotice = "";

    /// <summary>
    /// Greys the multiplayer actions that need the network (sign-in, create room,
    /// refresh rooms) while the app is offline, and restores them on reconnect.
    /// Strings are passed in by MainWindow (from the localized keys). Multiplayer is
    /// inherently online, so this is the whole tab's "you can't do this offline" gate;
    /// the title-bar chip carries the global signal.
    /// </summary>
    public void SetOfflineMode(bool offline, string needsInternetTooltip, string offlineNotice)
    {
        _offlineMode = offline;
        _offlineNeedsInternet = needsInternetTooltip;
        _offlineNotice = offlineNotice;

        if (offline)
        {
            ApplyOfflineDisable();
        }
        else
        {
            // Back online: drop our tooltips and let the session logic recompute the
            // correct enabled states (don't force IsEnabled=true on a button that
            // should stay disabled for another reason, e.g. CreateRoom while signed
            // out). RefreshFromSession is the single source of those states.
            if (SignInButton != null) SignInButton.ToolTip = null;
            if (RefreshButton != null) { RefreshButton.IsEnabled = true; RefreshButton.ToolTip = null; }
            if (CreateRoomButton != null) CreateRoomButton.ToolTip = null;
            RefreshFromSession();
        }
    }

    /// <summary>
    /// Force-disables the online action buttons and explains why. Re-applied at the
    /// end of <see cref="RefreshFromSession"/> so a session refresh can't silently
    /// re-enable them while we're still offline.
    /// </summary>
    private void ApplyOfflineDisable()
    {
        if (SignInButton != null) { SignInButton.IsEnabled = false; SignInButton.ToolTip = _offlineNotice; }
        if (RefreshButton != null) { RefreshButton.IsEnabled = false; RefreshButton.ToolTip = _offlineNeedsInternet; }
        if (CreateRoomButton != null) { CreateRoomButton.IsEnabled = false; CreateRoomButton.ToolTip = _offlineNeedsInternet; }
    }

    /// <summary>
    /// Compares <c>_attachedSocket</c> with the session's current
    /// <see cref="MultiplayerSession.RoomSocket"/> and (un)subscribes
    /// to match. Called every time the session state changes — joining
    /// a lobby sets a new socket; leaving sets it to null. Idempotent.
    /// </summary>
    private void SyncRoomSocketSubscription()
    {
        var s = _session;
        var nextSocket = s?.RoomSocket;

        var socketChanged = !ReferenceEquals(_attachedSocket, nextSocket);
        if (!socketChanged) return;

        if (socketChanged)
        {
            if (_attachedSocket != null)
            {
                _attachedSocket.FrameReceived -= OnRoomFrame;
                _attachedSocket.Disconnected -= OnRoomDisconnected;
                _attachedSocket.Reconnecting -= OnRoomReconnecting;
            }
            _attachedSocket = nextSocket;
            // Detaching a socket always means "we're no longer in
            // an active room" — reset the reconnect flag so the
            // status pill goes back to plain Connected, and clear
            // the room-mod cache so a stale value doesn't drive a
            // future LaunchActiveModGame.
            _isReconnecting = false;
            if (nextSocket == null)
            {
                _currentLobbyModId = null;
                _currentLobbyMaxPlayers = 0;
            }
            UpdateConnectionStatus();
            // Also reset the match-phase machinery. If we somehow
            // exit a room with an active game (forced disconnect,
            // host left), tear down the local AoE3 process and
            // unlock the popup chrome.
            try
            {
                var p = _aoe3Process;
                if (p != null && !p.HasExited) p.Kill(entireProcessTree: true);
            }
            catch { /* best-effort cleanup */ }
            ExitInGamePhase();
            // Lobby window position used to need re-centering here for
            // the in-tab popup. The real Window we use now remembers
            // its own position between opens via OS chrome, so there's
            // nothing to reset.
        }

        // Reset per-room UI state whenever we change rooms.
        if (socketChanged)
        {
            _roomMembers.Clear();
            _roomHostUserId = null;
            _isHostInCurrentRoom = false;
            if (_lobbyWindow != null)
            {
                _lobbyWindow.ChatLogPanel.Children.Clear();
                _lobbyWindow.RoomMembersPanel.Children.Clear();
                UpdateChatEmptyState();
            }
            // Fresh room → fresh chat replay cursor. Otherwise the
            // first room_state of the new room would skip lines whose
            // atMs happens to be smaller than the last one we saw in
            // the previous room.
            _highestSeenChatAtMs = 0;

            // Seed the members map with the local user before any
            // server frame arrives. Without this, a brief delay or
            // a tunnel-side WS hiccup leaves the Players panel
            // completely empty — confusing because the user clearly
            // IS in a room. The real room_state frame from the DO
            // will overwrite this with the authoritative list as
            // soon as it lands.
            var me = _session?.CurrentUser;
            if (me != null && nextSocket != null)
            {
                _roomMembers[me.Id] = new RoomMemberEntry
                {
                    UserId = me.Id,
                    Login = string.IsNullOrEmpty(me.DiscordUsername) ? me.DisplayName : me.DiscordUsername,
                    Ready = false,
                };
                RenderRoomMembers();
            }
        }

        if (socketChanged && nextSocket != null)
        {
            nextSocket.FrameReceived += OnRoomFrame;
            nextSocket.Disconnected += OnRoomDisconnected;
            nextSocket.Reconnecting += OnRoomReconnecting;
        }
    }

    private void OnRoomDisconnected(object? sender, string reason) =>
        Dispatcher.InvokeAsync(() =>
        {
            // Connection-state events used to spam the room chat
            // log, which the redesign brief explicitly calls out as
            // wrong — they're now routed to the global chat bar at
            // the bottom AND drive the status pill at the top.
            _isReconnecting = true;
            UpdateConnectionStatus();
            AppendGlobalSystemEvent($"Disconnected: {reason}. Reconnecting…");
        });

    private void OnRoomReconnecting(object? sender, string nextAttempt) =>
        Dispatcher.InvokeAsync(() =>
        {
            _isReconnecting = true;
            UpdateConnectionStatus();
            AppendGlobalSystemEvent($"Reconnecting… ({nextAttempt})");
        });

    /// <summary>
    /// Frame router. Every type from the Worker's LobbyRoom DO arrives
    /// here; we deserialise the slice we care about and update local
    /// state + UI. Marshals back to the UI thread because
    /// <see cref="LobbyWebSocket.FrameReceived"/> fires from the
    /// background receive loop.
    /// </summary>
    private void OnRoomFrame(object? sender, LobbyWebSocket.FrameReceivedEventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            try
            {
                switch (e.Type)
                {
                    case "room_state":
                        HandleRoomState(e.Json);
                        break;
                    case "chat":
                        HandleChat(e.Json);
                        break;
                    case "member_joined":
                        HandleMemberJoined(e.Json);
                        break;
                    case "member_left":
                        HandleMemberLeft(e.Json);
                        break;
                    case "member_ready":
                        HandleMemberReady(e.Json);
                        break;
                    case "host_changed":
                        HandleHostChanged(e.Json);
                        break;
                    case "member_net":
                        HandleMemberNet(e.Json);
                        break;
                    case "kicked":
                        HandleKicked();
                        break;
                    case "game_countdown":
                    {
                        // Host pressed Start — server broadcasts the
                        // canonical countdown duration. Switch popup
                        // into Starting phase and run a purely-local
                        // countdown timer (no dependence on absolute
                        // server timestamps, which would let clock
                        // skew skip the wait entirely on a host with
                        // a fast-running clock).
                        var durationMs = e.Json.TryGetProperty("duration_ms", out var dm)
                            && dm.ValueKind == System.Text.Json.JsonValueKind.Number
                                ? dm.GetInt32()
                                : 3000;
                        // Floor at 10 s so the countdown is always long enough
                        // to read and to cancel, whatever the server sends.
                        durationMs = Math.Max(10000, durationMs);
                        StartCountdown(durationMs);
                        AppendChatSystem(Strings.Format("MpChatGameStartingIn", durationMs / 1000));
                        break;
                    }
                    case "game_started":
                        // Legacy-compat path: this frame is broadcast
                        // alongside `game_countdown` for clients that
                        // don't know about the countdown protocol. The
                        // CURRENT launcher routes the actual launch
                        // through the countdown timer's expiry (see
                        // UpdateCountdownTick) so we only honour
                        // game_started when we're still in the bare
                        // Lobby phase — meaning the host pressed Start
                        // on a server old enough not to emit the
                        // countdown frame. In Starting / InGame phase
                        // we ignore game_started (the countdown handles
                        // the launch, or we're already running).
                        if (_matchPhase == MatchPhase.Lobby)
                        {
                            AppendChatSystem(Strings.Get("MpChatGameStarted"));
                            var process = LaunchActiveModGame();
                            EnterInGamePhase(process);
                        }
                        RefreshFromSession();
                        break;
                    case "game_cancelled":
                    {
                        var reason = e.Json.TryGetProperty("reason", out var r)
                            ? (r.GetString() ?? "host_cancelled")
                            : "host_cancelled";
                        AppendChatSystem(reason is "host_cancelled" or "aborted"
                            ? Strings.Get("MpChatGameAborted")
                            : Strings.Format("MpChatGameCancelledReason", reason));
                        // Kill local AoE3 if running and exit the
                        // InGame phase. We don't send a follow-up
                        // frame back — the server already cleared
                        // the lobby state in its UPDATE.
                        try
                        {
                            var p = _aoe3Process;
                            if (p != null && !p.HasExited)
                                p.Kill(entireProcessTree: true);
                        }
                        catch (Exception ex)
                        {
                            DiagnosticLog.Write($"MultiplayerTab.game_cancelled: kill — {ex.Message}");
                        }
                        ExitInGamePhase();
                        RefreshFromSession();
                        break;
                    }
                    case "error":
                        var code = e.Json.TryGetProperty("code", out var c) ? c.GetString() : "";
                        var msg = e.Json.TryGetProperty("message", out var m) ? m.GetString() : "";
                        // The abort grace window closed — surface a friendly,
                        // localized note instead of the raw English server text.
                        if (code == "grace_window_closed")
                            AppendChatSystem(Strings.Get("MpChatAbortWindowClosed"));
                        else
                            AppendChatSystem($"[{code}] {msg}");
                        break;
                    // "pong" intentionally swallowed.
                }
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"MultiplayerTab.OnRoomFrame ({e.Type}): {ex.Message}");
            }
        });
    }

    private void HandleRoomState(JsonElement json)
    {
        // Receiving a room_state means the socket is alive — clear
        // the "Reconnecting…" pill if it was set. Cheap to do here
        // and keeps the status indicator in sync with reality
        // without polling the WS object directly.
        if (_isReconnecting)
        {
            _isReconnecting = false;
            UpdateConnectionStatus();
            AppendGlobalSystemEvent("Reconnected to multiplayer server.");
        }

        var state = JsonSerializer.Deserialize<WsRoomState>(json.GetRawText());
        if (state == null) return;

        _roomMembers.Clear();
        foreach (var kv in state.Members)
        {
            var memberLogin = string.IsNullOrEmpty(kv.Value.Login) ? kv.Key : kv.Value.Login;
            _roomMembers[kv.Key] = new RoomMemberEntry
            {
                UserId = kv.Key,
                // Prefer the server-provided login; fall back to the
                // user id for legacy rooms that don't carry it yet.
                Login = memberLogin,
                Ready = kv.Value.Ready,
                RadminIp = kv.Value.RadminIp,
                AvatarUrl = kv.Value.AvatarUrl,
            };
        }

        // The n2n edge bring-up is owned by MultiplayerSession.OnFrame —
        // it sees the same room_state snapshot we do and uses the
        // sorted member list to derive each peer's slot index + virtual
        // IP deterministically. No extra signaling from this tab.
        _roomHostUserId = state.HostUserId;
        _isHostInCurrentRoom = !string.IsNullOrEmpty(_session?.CurrentUser?.Id)
            && string.Equals(_roomHostUserId, _session!.CurrentUser!.Id, StringComparison.Ordinal);

        // Replay the server-buffered chat WITHOUT wiping local lines.
        // Why: room_state fires on every WS reconnect (auto-reconnect
        // backoff). If the user typed a message right before a brief
        // tunnel hiccup, the server's chatRing might not contain that
        // message yet (the send arrived after the reconnect snapshot),
        // and the old "clear + replay" path would erase the message
        // the user JUST saw appear as a local echo. The bug we shipped
        // looked exactly like "I type, the message flashes, then it
        // disappears".
        //
        // New behaviour: only append lines whose atMs is newer than the
        // newest one we already have rendered. The local echo uses
        // DateTime.Now so its effective atMs is "now" — server lines
        // for that same message will carry a slightly different atMs
        // and may produce one duplicate, which is an acceptable cost
        // (the alternative was making messages vanish). For the
        // initial connect (chat panel empty) this still replays the
        // entire ring exactly once.
        ReplayChatRing(state.Chat);

        RenderRoomMembers();
        RenderRoomPanel();
    }

    /// <summary>
    /// Append any chat lines from the server's ring buffer that we
    /// haven't shown yet, in chronological order. Idempotent across
    /// repeated calls — re-running with the same ring is a no-op.
    /// </summary>
    private void ReplayChatRing(System.Collections.Generic.IEnumerable<WsChatLine> ring)
    {
        if (_lobbyWindow == null) return;
        foreach (var line in ring)
        {
            if (line == null) continue;
            if (line.AtMs <= _highestSeenChatAtMs) continue;
            AppendChatLine(line);
        }
    }

    /// <summary>
    /// Cursor for the chat-replay dedup. AppendChatLine bumps this
    /// every time it processes a server-sourced line. Local echoes
    /// don't touch it (they're rendered out-of-band with DateTime.Now).
    /// </summary>
    private long _highestSeenChatAtMs;

    /// <summary>
    /// Bodies of messages we just sent locally that haven't been
    /// "echoed" back by the server yet. Used to skip the duplicate
    /// render when the broadcast `chat` frame for our own message
    /// lands a few hundred ms after the optimistic local echo.
    /// Bounded by a 5 s TTL so a stale entry can't shadow a genuine
    /// later duplicate (e.g. user repeats themselves after a delay).
    /// </summary>
    private readonly System.Collections.Generic.List<(string Body, long SentTicks)> _recentLocalEchoes = new();
    private const int LocalEchoMatchWindowMs = 5000;

    private void HandleChat(JsonElement json)
    {
        if (!json.TryGetProperty("line", out var lineJson)) return;
        var line = JsonSerializer.Deserialize<WsChatLine>(lineJson.GetRawText());
        if (line == null) return;
        AppendChatLine(line);
    }

    private void HandleMemberJoined(JsonElement json)
    {
        if (!json.TryGetProperty("user_id", out var u)) return;
        var userId = u.GetString();
        if (string.IsNullOrEmpty(userId)) return;
        var login = json.TryGetProperty("discord_username", out var l) ? (l.GetString() ?? userId) : userId;
        var avatar = json.TryGetProperty("avatar_url", out var av) ? av.GetString() : null;

        if (_roomMembers.TryGetValue(userId, out var existing))
        {
            existing.Login = login;
            if (!string.IsNullOrEmpty(avatar)) existing.AvatarUrl = avatar;
        }
        else
        {
            _roomMembers[userId] = new RoomMemberEntry { UserId = userId, Login = login, AvatarUrl = avatar };
        }
        AppendChatSystem(Strings.Format("MpChatMemberJoined", login));
        RenderRoomMembers();

        // n2n discovery is supernode-mediated: edges find each other
        // by community, not by per-room signaling. The session's
        // OnFrame handler watches member_joined frames too and may
        // re-derive our slot index when the roster changes; nothing
        // else for this tab to do.
    }

    private void HandleMemberLeft(JsonElement json)
    {
        if (!json.TryGetProperty("user_id", out var u)) return;
        var userId = u.GetString();
        if (string.IsNullOrEmpty(userId)) return;
        if (_roomMembers.Remove(userId, out var entry))
            AppendChatSystem(Strings.Format("MpChatMemberLeft", entry.Login));
        RenderRoomMembers();
    }

    private void HandleMemberReady(JsonElement json)
    {
        if (!json.TryGetProperty("user_id", out var u)) return;
        var userId = u.GetString();
        if (string.IsNullOrEmpty(userId)) return;
        var ready = json.TryGetProperty("ready", out var r) && r.GetBoolean();
        if (_roomMembers.TryGetValue(userId, out var entry))
            entry.Ready = ready;
        RenderRoomMembers();
    }

    /// <summary>
    /// The host left and the server handed the lobby to the next member
    /// (GameRanger-style migration). Update who we think the host is, re-evaluate
    /// our own host-only controls, and re-render the roster's HOST badge.
    /// </summary>
    private void HandleHostChanged(JsonElement json)
    {
        var newHost = json.TryGetProperty("new_host_user_id", out var h) ? h.GetString() : null;
        if (string.IsNullOrEmpty(newHost)) return;
        var newLogin = json.TryGetProperty("new_host_login", out var l) ? (l.GetString() ?? "") : "";

        _roomHostUserId = newHost;
        _isHostInCurrentRoom = !string.IsNullOrEmpty(_session?.CurrentUser?.Id)
            && string.Equals(_roomHostUserId, _session!.CurrentUser!.Id, StringComparison.Ordinal);

        if (string.IsNullOrEmpty(newLogin) && _roomMembers.TryGetValue(newHost, out var e))
            newLogin = e.Login;
        AppendChatSystem(Strings.Format("MpChatHostChanged",
            string.IsNullOrEmpty(newLogin) ? newHost : newLogin));

        RenderRoomMembers();   // host-first order + HOST badge
        RenderRoomPanel();     // re-evaluates host-only controls (Start, etc.)
    }

    /// <summary>
    /// The host kicked us. Close the lobby window (which fires the normal
    /// leave-room cleanup + disposes the socket, so there's no reconnect loop
    /// after the server closes ours) and show a notice on the tab.
    /// </summary>
    private void HandleKicked()
    {
        CloseLobbyWindow();
        _ = MpAlertOverlay.NoticeAsync(
            TabRootGrid,
            Strings.Get("MpKickedTitle"),
            Strings.Get("MpKickedBody"),
            Strings.Get("MpAlertOk"));
    }

    /// <summary>A peer reported (or changed) its Radmin IP — store it so the
    /// in-game ping prober can ICMP them.</summary>
    private void HandleMemberNet(JsonElement json)
    {
        var userId = json.TryGetProperty("user_id", out var u) ? u.GetString() : null;
        if (string.IsNullOrEmpty(userId)) return;
        var ip = json.TryGetProperty("radmin_ip", out var r) ? r.GetString() : null;
        if (_roomMembers.TryGetValue(userId, out var entry))
            entry.RadminIp = ip;
    }

    /// <summary>
    /// Rebuild the players sidebar from <see cref="_roomMembers"/>. Host
    /// rendered first with a small "host" tag; everyone else follows in
    /// join order (dictionary insertion order in .NET is preserved).
    /// </summary>
    private void RenderRoomMembers()
    {
        if (_lobbyWindow == null) return;
        _lobbyWindow!.RoomMembersPanel.Children.Clear();

        // Host first. The doc-comment always promised this, but raw
        // dictionary order only happens to put the host first in the
        // host's OWN room — a joiner sees room_state replay order.
        // OrderByDescending is stable, so non-host members keep their
        // join order.
        var ordered = _roomMembers.Values
            .OrderByDescending(m => string.Equals(m.UserId, _roomHostUserId, StringComparison.Ordinal));
        foreach (var m in ordered)
        {
            _lobbyWindow!.RoomMembersPanel.Children.Add(BuildMemberRow(m));
        }

        // Open-slot placeholders up to the room capacity, so the list
        // shows at a glance how many players can still join. Only when
        // the max is known (see TryGetCurrentLobbyMaxPlayers).
        if (TryGetCurrentLobbyMaxPlayers(out var max) && max > _roomMembers.Count)
        {
            for (var i = _roomMembers.Count; i < max; i++)
                _lobbyWindow!.RoomMembersPanel.Children.Add(BuildOpenSlotRow());
        }
    }

    /// <summary>
    /// A dimmed "open slot" row, one per unfilled player slot up to the
    /// room capacity. Mirrors <see cref="BuildMemberRow"/>'s left metrics
    /// (an avatar-sized disc + a label) so the rows line up, but muted and
    /// with an empty outlined circle instead of an avatar.
    /// </summary>
    private FrameworkElement BuildOpenSlotRow()
    {
        var row = new Border
        {
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 7, 8, 7),
            Margin = new Thickness(0, 2, 0, 2),
            Opacity = 0.5,
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        const double avatarSize = 28.0;
        // 16 px left margin lines the disc up with the member rows'
        // avatar (8 px online dot + 8 px gap precede it there).
        panel.Children.Add(new Border
        {
            Width = avatarSize, Height = avatarSize,
            CornerRadius = new CornerRadius(avatarSize / 2),
            Background = Brushes.Transparent,
            BorderBrush = (Brush)Application.Current.FindResource("MpDivider"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(16, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(new TextBlock
        {
            Text = Strings.Get("MpRoomSlotOpen"),
            Foreground = (Brush)Application.Current.FindResource("TextSecondary"),
            FontSize = (double)Application.Current.FindResource("FontSizeBody"),
            FontStyle = FontStyles.Italic,
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Child = panel;
        return row;
    }

    /// <summary>
    /// One row in the players list. Layout:
    ///   [online dot] [avatar 32] [name + ping (small)] [Host badge] [Ready badge]
    /// Avatar uses the Discord avatar URL when we have one for the
    /// current user; for other members we don't have a URL yet,
    /// so we draw a coloured circle with their initial (cheap,
    /// stable, matches the redesign's "warm gold" placeholder).
    /// </summary>
    /// <summary>
    /// Recolour the roster's per-member health dots in place from the latest ICMP
    /// probe, without rebuilding the rows (which would re-fetch avatars and flicker).
    /// Each dot is Tagged with its member userId in <see cref="BuildMemberRow"/>.
    /// Called on the lobby tick; open-slot rows carry no tagged dot and are skipped.
    /// </summary>
    private void RefreshRosterHealthDots()
    {
        var panel = _lobbyWindow?.RoomMembersPanel;
        if (panel == null) return;
        var myId = _session?.CurrentUser?.Id;
        foreach (var child in panel.Children)
        {
            if (child is not Border b || b.Child is not Grid g) continue;
            System.Windows.Shapes.Ellipse? dot = null;
            foreach (var el in g.Children)
                if (el is System.Windows.Shapes.Ellipse e && e.Tag is string) { dot = e; break; }
            if (dot?.Tag is not string uid || !_roomMembers.TryGetValue(uid, out var m)) continue;
            dot.Fill = (myId != null && string.Equals(uid, myId, StringComparison.Ordinal))
                ? (Brush)Application.Current.FindResource("MpStatusOnline")
                : PeerDotBrush(PeerNetHealth.Classify(
                    !string.IsNullOrEmpty(m.RadminIp), m.PingMs, m.ConsecutiveFails));
        }
    }

    private FrameworkElement BuildMemberRow(RoomMemberEntry m)
    {
        var row = new Border
        {
            // Subtle green wash on rows whose player has readied up, so
            // ready state reads at a glance beyond the small pill.
            Background = m.Ready
                ? new SolidColorBrush(Color.FromArgb(0x22, 0x3F, 0xB9, 0x50))
                : Brushes.Transparent,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 7, 8, 7),
            Margin = new Thickness(0, 2, 0, 2),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // online dot
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // avatar
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });  // name
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // badges

        // Health dot. Coloured by the peer's derived link state and Tagged with
        // the userId so RefreshRosterHealthDots can recolour it live (per lobby
        // tick) without rebuilding the row. Your own row is always green.
        var isSelfDot = _session?.CurrentUser != null
            && string.Equals(m.UserId, _session.CurrentUser!.Id, StringComparison.Ordinal);
        grid.Children.Add(WithColumn(new System.Windows.Shapes.Ellipse
        {
            Width = 8, Height = 8,
            Fill = isSelfDot
                ? (Brush)Application.Current.FindResource("MpStatusOnline")
                : PeerDotBrush(PeerNetHealth.Classify(
                    !string.IsNullOrEmpty(m.RadminIp), m.PingMs, m.ConsecutiveFails)),
            Tag = m.UserId,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        }, 0));

        // Avatar circle: the member's real Discord photo from the room roster
        // (room_state/member_joined), with a coloured-initial fallback. "Me" also
        // has its avatar locally as a backstop for legacy rooms.
        var me = _session?.CurrentUser;
        var isMe = me != null && string.Equals(m.UserId, me.Id, StringComparison.Ordinal);
        var memberAvatar = !string.IsNullOrEmpty(m.AvatarUrl)
            ? m.AvatarUrl
            : (isMe ? me?.AvatarUrl : null);
        var avatarHost = BuildAvatarDisc(m.Login, memberAvatar, 28);
        avatarHost.Margin = new Thickness(0, 0, 10, 0);
        grid.Children.Add(WithColumn(avatarHost, 1));

        // Name + (optional) RTT.
        var nameStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
        };
        nameStack.Children.Add(new TextBlock
        {
            Text = m.Login,
            Foreground = (Brush)Application.Current.FindResource("TextPrimary"),
            FontSize = (double)Application.Current.FindResource("FontSizeBody"),
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        // Per-peer link quality is shown via the leading health dot (recoloured
        // live by RefreshRosterHealthDots from the ICMP probe), so the name cell
        // stays text-only here; the numeric RTT lives in the in-game panel.
        grid.Children.Add(WithColumn(nameStack, 2));

        // Badges (Host / Ready). Compact pills so multiple badges
        // can sit side-by-side without overflowing the 340-wide
        // left column.
        var badges = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var isHost = string.Equals(m.UserId, _roomHostUserId, StringComparison.Ordinal);
        if (isHost)
        {
            badges.Children.Add(BuildBadge(Strings.Get("MpRoomBadgeHost") + "  👑",
                (Brush)Application.Current.FindResource("MpBlueSubtle"),
                (Brush)Application.Current.FindResource("MpBlue")));
        }
        if (m.Ready)
        {
            badges.Children.Add(BuildBadge(Strings.Get("MpRoomReady"),
                Brushes.Transparent,
                (Brush)Application.Current.FindResource("MpPingGood")));
        }

        // Kick button — host-only, never on the host's OWN row. The button
        // tracks _isHostInCurrentRoom, which host migration keeps current, so it
        // appears for whoever currently holds the room.
        if (_isHostInCurrentRoom && !isMe && !isHost)
        {
            var kickBtn = new Button
            {
                Content = "✕",
                Width = 24,
                Height = 24,
                Padding = new Thickness(0),
                Margin = new Thickness(6, 0, 0, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                BorderBrush = (Brush)Application.Current.FindResource("MpStatusOffline"),
                Foreground = (Brush)Application.Current.FindResource("MpStatusOffline"),
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = Strings.Format("MpConfirmKickBody", m.Login),
            };
            var targetId = m.UserId;
            var targetLogin = m.Login;
            kickBtn.Click += async (_, _) => await KickMemberAsync(targetId, targetLogin);
            badges.Children.Add(kickBtn);
        }
        grid.Children.Add(WithColumn(badges, 3));

        row.Child = grid;
        return row;
    }

    /// <summary>
    /// Host action: confirm, then ask the server to kick a member. The roster
    /// re-renders on its own when the resulting member_left frame arrives.
    /// </summary>
    private async Task KickMemberAsync(string userId, string login)
    {
        if (_lobbyWindow == null || _session?.RoomSocket == null) return;
        bool confirmed = await MpAlertOverlay.ConfirmAsync(
            _lobbyWindow.LobbyRootGrid,
            Strings.Get("MpConfirmKickTitle"),
            Strings.Format("MpConfirmKickBody", login),
            Strings.Get("MpConfirmKickYes"),
            Strings.Get("MpAlertCancel"),
            danger: true);
        if (!confirmed) return;
        try { await _session.RoomSocket.SendKickAsync(userId); }
        catch (Exception ex) { DiagnosticLog.Write($"MultiplayerTab.KickMember: {ex.Message}"); }
    }

    /// <summary>Helper: assigns a Grid.Column without verbosity at call sites.</summary>
    private static T WithColumn<T>(T element, int col) where T : FrameworkElement
    {
        Grid.SetColumn(element, col);
        return element;
    }

    /// <summary>
    /// Compact rounded pill ("Host", "Ready"). Background +
    /// foreground passed in so the caller controls the colour.
    /// </summary>
    private static Border BuildBadge(string text, Brush background, Brush foreground)
    {
        return new Border
        {
            Background = background,
            BorderBrush = foreground,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(7, 2, 7, 2),
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                Foreground = foreground,
                FontSize = (double)Application.Current.FindResource("FontSizeCaption"),
                FontWeight = FontWeights.SemiBold,
            },
        };
    }

    private void AppendChatLine(WsChatLine line)
    {
        // Dedup the local optimistic echo. When the server broadcasts
        // OUR message back to us, it carries our user_id and the same
        // body we typed; we already drew that line as a local echo on
        // send, so re-rendering would produce a visible duplicate.
        // We match by (userId, body, within the 5 s send window) and
        // consume one entry per matched echo so a second identical
        // message from us later still renders.
        var me = _session?.CurrentUser;
        if (me != null
            && string.Equals(line.UserId, me.Id, StringComparison.Ordinal))
        {
            var nowTicks = Environment.TickCount64;
            // GC stale local-echo records first so a hours-old entry
            // can't accidentally swallow a brand-new server line.
            _recentLocalEchoes.RemoveAll(x =>
                nowTicks - x.SentTicks > LocalEchoMatchWindowMs);

            for (int i = 0; i < _recentLocalEchoes.Count; i++)
            {
                if (string.Equals(_recentLocalEchoes[i].Body, line.Body, StringComparison.Ordinal))
                {
                    _recentLocalEchoes.RemoveAt(i);
                    if (line.AtMs > _highestSeenChatAtMs)
                        _highestSeenChatAtMs = line.AtMs;
                    return;
                }
            }
        }

        var when = DateTimeOffset.FromUnixTimeMilliseconds(line.AtMs).LocalDateTime;
        AppendChatRow(
            timestamp: when,
            isSystem: false,
            authorLogin: line.Login,
            authorUserId: line.UserId,
            body: line.Body,
            severity: ChatSeverity.Info);
        // Track the newest server-sourced timestamp so a later
        // room_state replay can skip lines we already rendered.
        if (line.AtMs > _highestSeenChatAtMs)
            _highestSeenChatAtMs = line.AtMs;
    }

    private void AppendChatSystem(string body) =>
        AppendChatRow(
            timestamp: DateTime.Now,
            isSystem: true,
            authorLogin: null,
            authorUserId: null,
            body: body,
            severity: ChatSeverity.Info);

    /// <summary>Severity bucket for a chat row's body colour.</summary>
    private enum ChatSeverity { Info, Warning, Error }

    /// <summary>
    /// Render one chat row in the new format:
    ///   [12:34 PM]  [System | name]  body
    /// System rows use the blue "[System]" tag; user rows use a
    /// small avatar circle + the user's blue-coloured login. The
    /// body wraps and stays selectable.
    /// </summary>
    private void AppendChatRow(
        DateTime timestamp,
        bool isSystem,
        string? authorLogin,
        string? authorUserId,
        string body,
        ChatSeverity severity)
    {
        if (_lobbyWindow == null) return;

        var rowGrid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });   // timestamp
        // Auto (was a fixed 140 px) so the name column hugs the login and
        // the message sits right after it instead of across a wide gap.
        // The name TextBlock carries a MaxWidth so a very long login still
        // truncates rather than shoving the body off-screen.
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });       // tag/avatar+name
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });  // body

        // Timestamp (column 0).
        rowGrid.Children.Add(WithColumn(new TextBlock
        {
            Text = timestamp.ToString("h:mm tt"),
            Foreground = (Brush)Application.Current.FindResource("TextSecondary"),
            FontSize = (double)Application.Current.FindResource("FontSizeCaption"),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.75,
        }, 0));

        // Tag column (column 1).
        if (isSystem)
        {
            rowGrid.Children.Add(WithColumn(new TextBlock
            {
                Text = "[System]",
                Foreground = (Brush)Application.Current.FindResource("MpBlue"),
                FontSize = (double)Application.Current.FindResource("FontSizeBody"),
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            }, 1));
        }
        else
        {
            // Tiny avatar + login. The avatar is the same kind of
            // 22 px circle the player list uses but smaller.
            var stack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var avatarSize = 22.0;
            var avatarHost = new Border
            {
                Width = avatarSize, Height = avatarSize,
                CornerRadius = new CornerRadius(avatarSize / 2),
                Background = (Brush)Application.Current.FindResource("MpSurfaceAlt"),
                BorderBrush = (Brush)Application.Current.FindResource("BorderSubtle"),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var me = _session?.CurrentUser;
            var isMe = !string.IsNullOrEmpty(authorUserId)
                && me != null
                && string.Equals(authorUserId, me.Id, StringComparison.Ordinal);
            try
            {
                if (isMe && !string.IsNullOrEmpty(me?.AvatarUrl))
                {
                    avatarHost.Background = new System.Windows.Media.ImageBrush
                    {
                        ImageSource = new System.Windows.Media.Imaging.BitmapImage(
                            new Uri(me!.AvatarUrl!, UriKind.Absolute)),
                        Stretch = System.Windows.Media.Stretch.UniformToFill,
                    };
                }
                else
                {
                    avatarHost.Child = new TextBlock
                    {
                        Text = !string.IsNullOrEmpty(authorLogin)
                            ? authorLogin.Substring(0, 1).ToUpperInvariant()
                            : "?",
                        Foreground = (Brush)Application.Current.FindResource("TextSecondary"),
                        FontSize = (double)Application.Current.FindResource("FontSizeCaption"),
                        FontWeight = FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                }
            }
            catch
            {
                avatarHost.Child = new TextBlock
                {
                    Text = !string.IsNullOrEmpty(authorLogin)
                        ? authorLogin.Substring(0, 1).ToUpperInvariant()
                        : "?",
                    Foreground = (Brush)Application.Current.FindResource("TextSecondary"),
                    FontSize = (double)Application.Current.FindResource("FontSizeCaption"),
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
            }
            stack.Children.Add(avatarHost);
            stack.Children.Add(new TextBlock
            {
                Text = (authorLogin ?? "?") + ":",
                Foreground = (Brush)Application.Current.FindResource("MpBlue"),
                FontSize = (double)Application.Current.FindResource("FontSizeBody"),
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 150,
            });
            rowGrid.Children.Add(WithColumn(stack, 1));
        }

        // Body (column 2). Wraps. Colour by severity for system
        // events so warnings / errors stand out without needing a
        // separate panel.
        var bodyBrush = severity switch
        {
            ChatSeverity.Warning => (Brush)Application.Current.FindResource("WarningBrush"),
            ChatSeverity.Error => (Brush)Application.Current.FindResource("MpStatusOffline"),
            _ => (Brush)Application.Current.FindResource("TextPrimary"),
        };
        rowGrid.Children.Add(WithColumn(new TextBlock
        {
            Text = body,
            Foreground = bodyBrush,
            FontSize = (double)Application.Current.FindResource("FontSizeBody"),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0),   // small gap after the name
        }, 2));

        _lobbyWindow!.ChatLogPanel.Children.Add(rowGrid);

        // Cap the in-memory log so a marathon session doesn't bloat
        // the visual tree. 500 rows ≈ 7 hours of moderate chat.
        while (_lobbyWindow!.ChatLogPanel.Children.Count > 500)
            _lobbyWindow!.ChatLogPanel.Children.RemoveAt(0);
        UpdateChatEmptyState();
        _lobbyWindow?.ChatScroll.ScrollToBottom();
    }

    /// <summary>
    /// Legacy raw-append path. Kept so any caller still in
    /// transition to AppendChatRow doesn't break. Renders as a
    /// system info row with a "—" prefix to match the old look.
    /// </summary>
    private void AppendChatRaw(string text, Brush color)
    {
        if (_lobbyWindow == null) return;
        _lobbyWindow!.ChatLogPanel.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = color,
            FontSize = (double)Application.Current.FindResource("FontSizeBody"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 1, 0, 1),
        });
        while (_lobbyWindow!.ChatLogPanel.Children.Count > 500)
            _lobbyWindow!.ChatLogPanel.Children.RemoveAt(0);
        UpdateChatEmptyState();
        _lobbyWindow?.ChatScroll.ScrollToBottom();
    }

    /// <summary>
    /// Show or hide the "no messages yet" hint based on whether the
    /// chat log has any rows. Called after every append and every
    /// clear so the hint tracks the live state.
    /// </summary>
    private void UpdateChatEmptyState()
    {
        if (_lobbyWindow == null) return;
        _lobbyWindow.ChatEmptyHint.Visibility =
            _lobbyWindow.ChatLogPanel.Children.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    /// <summary>
    /// Forward a connection-state event to the diagnostic log.
    /// The old design routed these into a dedicated "global lobby
    /// chat" strip at the bottom of the tab; the redesign removed
    /// that strip entirely, so the user-visible signal is now
    /// just the connection-status pill at the top-right (driven
    /// by UpdateConnectionStatus). We keep the log line so
    /// developers can still debug WS hiccups from the trace file.
    /// </summary>
    private void AppendGlobalSystemEvent(string body)
    {
        DiagnosticLog.Write($"Multiplayer event: {body}");
    }

    /// <summary>
    /// (Removed) The old layout had a Lobby Chat strip at the
    /// bottom with a collapse toggle and a "Join with IP" button.
    /// We deleted both per the redesign; this comment is the
    /// only thing left so future readers don't wonder where
    /// they went. Handler stubs are intentionally absent.
    /// </summary>

    /// <summary>
    /// "Clear chat" header button: wipes the visible log without
    /// touching the server side. Useful when the chat got noisy
    /// during reconnects and the user wants a clean view. We do
    /// NOT re-emit the room_state replay — only the user's local
    /// view is cleared.
    /// </summary>
    private void ClearChatButton_Click(object sender, RoutedEventArgs e)
    {
        _lobbyWindow?.ChatLogPanel.Children.Clear();
        UpdateChatEmptyState();
    }

    /// <summary>
    /// Emoji button placeholder. A proper picker pulls in a UI
    /// library we don't need yet — for now this drops a smiley
    /// at the caret so the button is functional and visibly
    /// alive instead of a dead icon.
    /// </summary>
    private void ChatEmojiButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lobbyWindow == null) return;
        var caret = _lobbyWindow!.ChatInputBox.CaretIndex;
        _lobbyWindow!.ChatInputBox.Text = _lobbyWindow!.ChatInputBox.Text.Insert(caret, "🙂");
        _lobbyWindow!.ChatInputBox.CaretIndex = caret + 2; // emoji is a surrogate pair (length 2)
        _lobbyWindow!.ChatInputBox.Focus();
    }

    /// <summary>
    /// Toggle the faux placeholder TextBlock over the chat input.
    /// WPF TextBox has no native placeholder support so we draw
    /// our own and hide it as soon as the user types. Cheap to
    /// run on every TextChanged because it's just a Visibility
    /// flip.
    /// </summary>
    private void ChatInputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_lobbyWindow == null) return;
        _lobbyWindow!.ChatPlaceholderText.Visibility = string.IsNullOrEmpty(_lobbyWindow!.ChatInputBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void RefreshFromSession()
    {
        // Refresh the top-right connection pill on every state
        // pass so signing in / out / reconnecting always flow
        // through to the UI without extra plumbing.
        UpdateConnectionStatus();

        if (_session == null)
        {
            ShowSignInPanel(null);
            return;
        }

        switch (_activeSubtab)
        {
            case Subtab.Rooms:
                RoomsView.Visibility = Visibility.Visible;
                FriendsView.Visibility = Visibility.Collapsed;
                ProfileView.Visibility = Visibility.Collapsed;
                HistoryView.Visibility = Visibility.Collapsed;
                RenderRoomsTab();
                break;
            case Subtab.Friends:
                RoomsView.Visibility = Visibility.Collapsed;
                FriendsView.Visibility = Visibility.Visible;
                ProfileView.Visibility = Visibility.Collapsed;
                HistoryView.Visibility = Visibility.Collapsed;
                break;
            case Subtab.Profile:
                RoomsView.Visibility = Visibility.Collapsed;
                FriendsView.Visibility = Visibility.Collapsed;
                ProfileView.Visibility = Visibility.Visible;
                HistoryView.Visibility = Visibility.Collapsed;
                RenderProfileTab();
                break;
            case Subtab.History:
                RoomsView.Visibility = Visibility.Collapsed;
                FriendsView.Visibility = Visibility.Collapsed;
                ProfileView.Visibility = Visibility.Collapsed;
                HistoryView.Visibility = Visibility.Visible;
                break;
        }

        UpdateSubtabHighlights();

        // Keep the online actions greyed while offline — RenderRoomsTab /
        // ShowSignInPanel above may have re-enabled them based on session state.
        if (_offlineMode) ApplyOfflineDisable();
    }

    private void RenderRoomsTab()
    {
        // The legacy WinDivert bootstrap gate is gone — the hook
        // injector ships next to the .exe and needs no per-user
        // setup. Fall straight through to sign-in / browser rendering.

        if (_session == null
            || _session.Status != MultiplayerSession.SessionStatus.SignedIn)
        {
            ShowSignInPanel(_session?.LastError);
            return;
        }

        // In a room? Show the room as a centered popup over the
        // browser. BrowserPanel stays Visible underneath (the
        // RoomPanel's own backdrop rectangle dims it) so the user
        // doesn't lose context. Leaving / X closes the popup and
        // the browser becomes interactive again without any
        // extra state plumbing.
        if (_session.Lobby == MultiplayerSession.LobbyStatus.InLobby
            || _session.Lobby == MultiplayerSession.LobbyStatus.InGame
            || _session.Lobby == MultiplayerSession.LobbyStatus.Joining
            || _session.Lobby == MultiplayerSession.LobbyStatus.Leaving)
        {
            SignInPanel.Visibility = Visibility.Collapsed;
            BrowserPanel.Visibility = Visibility.Visible;
            RenderBrowser();
            OpenLobbyWindow();
            RenderRoomPanel();
        }
        else
        {
            SignInPanel.Visibility = Visibility.Collapsed;
            BrowserPanel.Visibility = Visibility.Visible;
            CloseLobbyWindow();
            RenderBrowser();
        }
    }

    private void ShowSignInPanel(string? errorMessage)
    {
        SignInPanel.Visibility = Visibility.Visible;
        BrowserPanel.Visibility = Visibility.Collapsed;
        CloseLobbyWindow();
        SignInErrorText.Visibility = string.IsNullOrEmpty(errorMessage)
            ? Visibility.Collapsed
            : Visibility.Visible;
        SignInErrorText.Text = errorMessage ?? "";
    }

    private void RenderBrowser()
    {
        var user = _session?.CurrentUser;
        SignedInAsText.Text = user != null
            ? $"@{user.DiscordUsername}"
            : "";

        // Fill the avatar circle either with the user's Discord
        // avatar (cached_user.avatar_url) or, when we have no URL
        // / it fails to load, with the uppercase first letter of
        // the login as a placeholder. Both cases keep the circle
        // the same physical size so the toolbar layout doesn't
        // shift when the network is slow.
        try
        {
            if (user != null && !string.IsNullOrEmpty(user.AvatarUrl))
            {
                UserAvatarBrush.ImageSource = new System.Windows.Media.Imaging.BitmapImage(
                    new Uri(user.AvatarUrl, UriKind.Absolute));
                UserAvatarInitial.Text = "";
            }
            else
            {
                UserAvatarBrush.ImageSource = null;
                UserAvatarInitial.Text = !string.IsNullOrEmpty(user?.DiscordUsername)
                    ? user.DiscordUsername.Substring(0, 1).ToUpperInvariant()
                    : "?";
            }
        }
        catch
        {
            // BitmapImage throws on malformed URLs; fall back to
            // the initial so the toolbar still renders cleanly.
            UserAvatarBrush.ImageSource = null;
            UserAvatarInitial.Text = !string.IsNullOrEmpty(user?.DiscordUsername)
                ? user.DiscordUsername.Substring(0, 1).ToUpperInvariant()
                : "?";
        }
    }

    /// <summary>
    /// Push the lobby window's STATIC labels (section headers, field
    /// labels, button captions, placeholder, copy tooltip) through the
    /// localisation table. Called when the window opens and again on a
    /// mid-room language switch. The dynamic, state-driven text (status
    /// line, player count, ready toggle, …) is owned by
    /// <see cref="RenderRoomPanel"/> instead.
    /// </summary>
    private void ApplyLobbyStaticLabels()
    {
        if (_lobbyWindow == null) return;
        _lobbyWindow.PlayersStatHeader.Text = Strings.Get("MpRoomPlayersHeader");
        _lobbyWindow.RoomIdStatHeader.Text = Strings.Get("MpRoomIdHeader");
        _lobbyWindow.RoomConnHeader.Text = Strings.Get("MpInGameConnectionHeader");
        _lobbyWindow.CopyRoomIdButton.ToolTip = Strings.Get("MpRoomCopyCode");
        _lobbyWindow.PlayersListHeader.Text = Strings.Get("MpRoomPlayersHeader");
        _lobbyWindow.RoomInfoHeaderText.Text = Strings.Get("MpRoomInfoHeader");
        _lobbyWindow.RoomModLabel.Text = Strings.Get("MpRoomFieldMod");
        _lobbyWindow.RoomPasswordLabel.Text = Strings.Get("MpRoomFieldPassword");
        _lobbyWindow.RoomCopyLabel.Text = Strings.Get("MpRoomFieldCopy");
        _lobbyWindow.ChatHeaderText.Text = Strings.Get("MpRoomChatHeader");
        _lobbyWindow.ClearChatButton.Content = "🗑  " + Strings.Get("MpRoomChatClear");
        _lobbyWindow.ChatSendButton.Content = Strings.Get("MpRoomChatSend");
        _lobbyWindow.ChatPlaceholderText.Text = Strings.Get("MpRoomChatPlaceholder");
        _lobbyWindow.ChatEmptyHint.Text = Strings.Get("MpRoomChatEmpty");

        // Match-phase static labels (countdown chat-line / InGameOverlay).
        // The dynamic captions — countdown "Go", the in-game mode badge, the
        // in-game cancel/leave button, AND the Start-button-as-Cancel during
        // the countdown — are owned by UpdateCountdownTick /
        // RefreshInGamePanel / ApplyMatchPhaseUi. The countdown now lives as
        // a single live line INSIDE the chat (⏱ label + number, no hint and
        // no button of its own — the left-column Start button doubles as
        // Cancel), so there's no CountdownHint / CountdownCancelButton
        // caption to set here.
        _lobbyWindow.CountdownLabel.Text = Strings.Get("MpCountdownLabel");
        _lobbyWindow.InGameTitleText.Text = Strings.Get("MpInGameTitle");
        _lobbyWindow.InGameMatchTimeHeader.Text = Strings.Get("MpInGameMatchTimeHeader");
        _lobbyWindow.InGameTrafficHeader.Text = Strings.Get("MpInGameTrafficHeader");
        _lobbyWindow.InGameConnectionHeader.Text = Strings.Get("MpInGameConnectionHeader");
        _lobbyWindow.InGameRoomHeader.Text = Strings.Get("MpInGameRoomHeader");
        _lobbyWindow.InGameModeText.Text = Strings.Get("MpInGameModeConnected");
    }

    private void RenderRoomPanel()
    {
        // Lobby window closed → nothing to render. Fires from session
        // events that may arrive after we've left the room and the
        // window has already been disposed.
        if (_lobbyWindow == null) return;

        var s = _session!;

        var status = s.Lobby switch
        {
            MultiplayerSession.LobbyStatus.Joining => Strings.Get("MpRoomStatusJoining"),
            MultiplayerSession.LobbyStatus.Leaving => Strings.Get("MpRoomStatusLeaving"),
            MultiplayerSession.LobbyStatus.InGame => Strings.Get("MpRoomStatusInGame"),
            _ => Strings.Get("MpRoomStatusInLobby"),
        };

        // P2P readiness: with the hook-injector bridge, "ready to
        // play" just means the mesh is up AND the injector artefacts
        // are shipped — there's no per-machine driver install gate
        // anymore. For solo rooms (host alone) we still show
        // "P2P ready"; peers will join later.
        var p2pReady = s.IsInLobby;
        var p2pStatus = p2pReady ? Strings.Get("MpRoomP2pReady") : Strings.Get("MpRoomP2pStarting");

        // Build the meta line as inline runs so the P2P state can wear
        // its own (green) colour without two TextBlocks: status in muted
        // text, P2P readiness highlighted. This line is now the single
        // home for the P2P status — the old "Connection" info-card cell
        // repeated it and was removed.
        _lobbyWindow!.RoomMetaText.Inlines.Clear();
        _lobbyWindow!.RoomMetaText.Inlines.Add(new System.Windows.Documents.Run(status)
        {
            Foreground = (Brush)Application.Current.FindResource("TextSecondary"),
        });
        _lobbyWindow!.RoomMetaText.Inlines.Add(new System.Windows.Documents.Run("  ·  ")
        {
            Foreground = (Brush)Application.Current.FindResource("TextSecondary"),
        });
        _lobbyWindow!.RoomMetaText.Inlines.Add(new System.Windows.Documents.Run(p2pStatus)
        {
            Foreground = (Brush)Application.Current.FindResource(
                p2pReady ? "MpStatusOnline" : "MpStatusReconnect"),
            FontWeight = FontWeights.SemiBold,
        });

        // ---------- Host (drives the title fallback only) ----------
        // The roster below marks the host with a badge, so there's no
        // separate HOST stat to fill anymore; we still resolve the name
        // to build a friendly title when the room is unnamed.
        string hostLabel = "";
        if (!string.IsNullOrEmpty(_roomHostUserId)
            && _roomMembers.TryGetValue(_roomHostUserId, out var hostEntry))
        {
            hostLabel = hostEntry.Login;
        }
        else if (!string.IsNullOrEmpty(_roomHostUserId))
        {
            hostLabel = _roomHostUserId;
        }

        // ---------- Title ----------
        // Prefer the room's own name. When unnamed, the title used to
        // fall back to the raw lobby id — exactly what the ROOM ID stat
        // already shows, so it read as a duplicate. Use "<host>'s room"
        // instead (or a generic label until the host is known).
        var title = s.CurrentLobbyTitle;
        if (string.IsNullOrWhiteSpace(title)
            || string.Equals(title, s.CurrentLobbyId, StringComparison.Ordinal))
        {
            title = !string.IsNullOrEmpty(hostLabel)
                ? Strings.Format("MpRoomTitleFallback", hostLabel)
                : Strings.Get("MpRoomTitleGeneric");
        }
        _lobbyWindow!.RoomTitleText.Text = title;

        // ---------- Players ----------
        // Live count vs configured max. The trailing "players" word is
        // gone (the PLAYERS header says it) and we drop the "/ ?" when
        // the max is unknown rather than printing a question mark.
        // (MaxPlayers only arrives on the browser's lobby summary; see
        // TryGetCurrentLobbyMaxPlayers.)
        var playerCount = _roomMembers.Count;
        _lobbyWindow!.RoomPlayersText.Text = TryGetCurrentLobbyMaxPlayers(out var maxP)
            ? $"{playerCount} / {maxP}"
            : playerCount.ToString();

        // ---------- ROOM ID ----------
        // Short uppercase code if the worker assigns one, otherwise the
        // raw lobby id (truncated for sanity).
        var rid = s.CurrentLobbyId ?? "";
        if (rid.Length > 12) rid = rid.Substring(0, 12);
        _lobbyWindow!.RoomIdText.Text = rid.ToUpperInvariant();

        // ---------- Room info card (Mod + Password) ----------
        // Slimmed from four cells to two: "Connection" duplicated the
        // P2P meta line and "Max players" duplicated the PLAYERS stat.
        // The whole card collapses when neither remaining field has data.
        var modKnown = TryGetCurrentLobbyModName(out var modName);
        _lobbyWindow!.RoomModText.Text = modKnown ? modName : "—";
        var hasPwd = TryGetCurrentLobbyHasPassword(out var hp) && hp;
        _lobbyWindow!.RoomPasswordText.Text = hasPwd
            ? Strings.Get("MpRoomPasswordYes")
            : Strings.Get("MpRoomPasswordNo");

        // Which install copy the room uses — only when the room's mod has 2+
        // copies (multiplayer always uses the ACTIVE copy of that mod). Tells
        // host + joiners which folder they entered with.
        string? copyLeaf = null;
        if (!string.IsNullOrEmpty(_currentLobbyModId))
        {
            var st = WarsOfLibertyLauncher.Models.LauncherConfig.Load().GetState(_currentLobbyModId);
            if (st.HasMultipleInstalls && !string.IsNullOrWhiteSpace(st.InstallPath))
                copyLeaf = CopyLeaf(st.InstallPath);
        }
        var hasCopy = !string.IsNullOrEmpty(copyLeaf);
        _lobbyWindow!.RoomCopyRow.Visibility = hasCopy ? Visibility.Visible : Visibility.Collapsed;
        if (hasCopy) _lobbyWindow!.RoomCopyText.Text = copyLeaf;

        _lobbyWindow!.RoomInfoCard.Visibility = (modKnown || hasPwd || hasCopy)
            ? Visibility.Visible
            : Visibility.Collapsed;

        // ---------- Action buttons ----------
        // Ready toggle visual: glyph + label so the state is obvious.
        // The roster-side ready flag lives in the room-state frame; the
        // local user is found via session.CurrentUser.
        var me = s.CurrentUser;
        var iAmReady = me != null
            && _roomMembers.TryGetValue(me.Id, out var meEntry)
            && meEntry.Ready;
        _lobbyWindow!.ReadyButton.Content = iAmReady
            ? "✓  " + Strings.Get("MpRoomReady")
            : "○  " + Strings.Get("MpRoomReadyMark");
        _lobbyWindow!.ReadyButton.Tag = iAmReady ? "ready" : "";

        // The Start button only appears for the host; enabled once the
        // P2P bridge is ready so AoE3 launches into a working network.
        // GUARD: only own the Start button while we're in the Lobby phase.
        // During the countdown (Starting) ApplyMatchPhaseUi repurposes this
        // same button as the red "Cancel" for everyone, so a room_state
        // refresh mid-countdown must NOT stomp it back to "Start game".
        if (_matchPhase == MatchPhase.Lobby)
        {
            _lobbyWindow!.StartButton.Visibility = _isHostInCurrentRoom
                ? Visibility.Visible
                : Visibility.Collapsed;
            _lobbyWindow!.StartButton.IsEnabled = _isHostInCurrentRoom && s.IsInLobby;
            _lobbyWindow!.StartButton.Content = "▶  " + Strings.Get("MpRoomStart");
        }
        _lobbyWindow!.LeaveRoomButton.Content = "↩  " + Strings.Get("MpRoomLeave");
    }

    /// <summary>
    /// Try to look up MaxPlayers for the current lobby. The session
    /// keeps the ID/title but not the full LobbySummary, so we walk
    /// the cached browser list (last /lobbies fetch) for a match.
    /// Cheap because the list is bounded at ~8 active rooms.
    /// </summary>
    private bool TryGetCurrentLobbyMaxPlayers(out int maxPlayers)
    {
        maxPlayers = 0;
        var lobbyId = _session?.CurrentLobbyId;
        if (!string.IsNullOrEmpty(lobbyId) && _lastBrowserList != null)
        {
            foreach (var l in _lastBrowserList)
            {
                if (string.Equals(l.Id, lobbyId, StringComparison.Ordinal))
                {
                    maxPlayers = l.MaxPlayers;
                    return true;
                }
            }
        }
        // Fallback for the host (absent from the browser snapshot of
        // joinable rooms): capacity is stashed on create/join.
        if (_currentLobbyMaxPlayers > 0)
        {
            maxPlayers = _currentLobbyMaxPlayers;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Try to resolve the human-readable mod name for the current
    /// lobby. Same approach as MaxPlayers — walks the cached
    /// browser list, then falls back to <see cref="ModRegistry"/>
    /// to translate the mod id into a display name.
    /// </summary>
    private bool TryGetCurrentLobbyModName(out string modName)
    {
        modName = "";
        var lobbyId = _session?.CurrentLobbyId;
        if (!string.IsNullOrEmpty(lobbyId) && _lastBrowserList != null)
        {
            foreach (var l in _lastBrowserList)
            {
                if (string.Equals(l.Id, lobbyId, StringComparison.Ordinal))
                {
                    // Look the id up in the registry for the friendly
                    // name; fall back to the raw id if not registered.
                    foreach (var p in ModRegistry.All)
                    {
                        if (string.Equals(p.Id, l.ModId, StringComparison.OrdinalIgnoreCase))
                        {
                            modName = p.DisplayName;
                            return true;
                        }
                    }
                    modName = l.ModId;
                    return true;
                }
            }
        }
        // Fallback: the host (and anyone whose browser snapshot is stale
        // or was never fetched) isn't in _lastBrowserList, but the
        // current room's mod id is cached on create/join. Resolve that
        // so the info card shows the mod name instead of an em-dash.
        if (!string.IsNullOrEmpty(_currentLobbyModId))
        {
            foreach (var p in ModRegistry.All)
            {
                if (string.Equals(p.Id, _currentLobbyModId, StringComparison.OrdinalIgnoreCase))
                {
                    modName = p.DisplayName;
                    return true;
                }
            }
            modName = _currentLobbyModId;
            return true;
        }
        return false;
    }

    private bool TryGetCurrentLobbyHasPassword(out bool hasPwd)
    {
        hasPwd = false;
        var lobbyId = _session?.CurrentLobbyId;
        if (string.IsNullOrEmpty(lobbyId) || _lastBrowserList == null) return false;
        foreach (var l in _lastBrowserList)
        {
            if (string.Equals(l.Id, lobbyId, StringComparison.Ordinal))
            {
                hasPwd = l.IsPrivate;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Most recent /lobbies snapshot. Cached so the room view can
    /// read MaxPlayers / IsPrivate / ModId without re-fetching. We
    /// don't expire it aggressively — the data is mostly static
    /// for the duration of a single match and worst case the user
    /// sees "?" until the next refresh tick.
    /// </summary>
    private List<LobbySummary>? _lastBrowserList;

    /// <summary>
    /// The mod id of the CURRENT room — set when the user creates
    /// or joins a lobby, cleared on leave. <see cref="LaunchActiveModGame"/>
    /// uses this to pick the right profile (NOT the Play tab's
    /// active profile, which can disagree with the room's mod
    /// when the user was browsing other mods between sessions).
    /// </summary>
    private string? _currentLobbyModId;

    /// <summary>
    /// Max players for the CURRENT room — set on create/join, cleared on
    /// leave. The host isn't in the browser snapshot (`_lastBrowserList`)
    /// so this is the only reliable source of room capacity for the
    /// PLAYERS stat and the players-list open-slot rows. 0 = unknown.
    /// </summary>
    private int _currentLobbyMaxPlayers;

    private void RenderProfileTab()
    {
        var user = _session?.CurrentUser;
        if (user == null)
        {
            ProfileNameText.Text = "—";
            ProfileLoginText.Text = "";
            ProfileEloText.Text = "";
            ProfileGamesText.Text = "";
            return;
        }
        ProfileNameText.Text = user.DisplayName;
        ProfileLoginText.Text = $"@{user.DiscordUsername}";
        // ELO is fetched lazily; cached value would live in extended
        // cachedUser later. For now leave the line empty so we don't
        // lie about an unknown rating.
        ProfileEloText.Text = "";
        ProfileGamesText.Text = "";
    }

    private void UpdateSubtabHighlights()
    {
        // Multiplayer subtabs use the blue accent instead of the
        // per-mod red — the redesign brief makes blue the section's
        // own identity colour. The underline is the only visual
        // indicator (no background pill), matching the reference.
        var accent = (Brush)Application.Current.FindResource("MpBlue");
        var transparent = Brushes.Transparent;
        var dim = (Brush)Application.Current.FindResource("TextSecondary");
        var bright = (Brush)Application.Current.FindResource("TextPrimary");

        void Paint(Button b, bool active)
        {
            b.Foreground = active ? bright : dim;
            b.BorderBrush = active ? accent : transparent;
        }

        Paint(SubtabRooms, _activeSubtab == Subtab.Rooms);
        Paint(SubtabFriends, _activeSubtab == Subtab.Friends);
        Paint(SubtabProfile, _activeSubtab == Subtab.Profile);
        Paint(SubtabHistory, _activeSubtab == Subtab.History);

        // Viewing the Rooms subtab clears the "new room created" dot.
        if (_activeSubtab == Subtab.Rooms) SetNewRoomIndicator(false);
    }

    /// <summary>True while an unseen "new room created" signal is pending.</summary>
    private bool _hasNewRoomSignal;

    /// <summary>
    /// Shows/hides the small red "new room created" dot on the Rooms subtab.
    /// Room notifications no longer add a bell item — MainWindow's lobby poll
    /// calls this (plus a Windows toast + the MULTIPLAYER nav-tab dot). The dot
    /// only shows while the user is NOT already on the Rooms subtab, and is
    /// cleared when they open it (see <see cref="UpdateSubtabHighlights"/>).
    /// </summary>
    public void SetNewRoomIndicator(bool on)
    {
        _hasNewRoomSignal = on;
        if (RoomsSubtabDot != null)
            RoomsSubtabDot.Visibility =
                on && _activeSubtab != Subtab.Rooms ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Reconnection state tracked from WS events. The LobbyWebSocket
    /// raises Disconnected / Reconnecting; we flip this flag and the
    /// status pill picks it up on the next UpdateConnectionStatus
    /// pass. <c>true</c> means "we lost the room socket and the
    /// retry loop is running"; cleared back to <c>false</c> on the
    /// next successful room_state frame or when the socket is
    /// detached entirely (left room).
    /// </summary>
    private bool _isReconnecting;

    /// <summary>
    /// Repaint the connection-status pill at the top-right of the
    /// header. Three states: Connected (green dot), Reconnecting
    /// (amber), Offline (red). Idempotent — safe to call on every
    /// state change or on a poll.
    /// </summary>
    private void UpdateConnectionStatus()
    {
        // Default to "signed out" appearance when there's no session
        // — keeps the pill from claiming "Connected" before sign-in.
        if (_session == null
            || _session.Status != MultiplayerSession.SessionStatus.SignedIn)
        {
            ConnDot.Fill = (Brush)Application.Current.FindResource("MpStatusOffline");
            ConnStatusText.Text = "Offline";
            return;
        }

        if (_isReconnecting)
        {
            ConnDot.Fill = (Brush)Application.Current.FindResource("MpStatusReconnect");
            ConnStatusText.Text = "Reconnecting…";
            return;
        }

        ConnDot.Fill = (Brush)Application.Current.FindResource("MpStatusOnline");
        ConnStatusText.Text = "Connected";
    }

    // ---------- Subtab clicks ----------

    /// <summary>
    /// Public entry point (used by MainWindow when a "new room" notification is
    /// clicked) to force the Rooms subtab and freshen the list. Mirrors
    /// <see cref="SubtabRooms_Click"/>.
    /// </summary>
    public void ShowRooms()
    {
        _activeSubtab = Subtab.Rooms;
        UpdateSubtabHighlights();
        RefreshFromSession();
        if (_session?.Status == MultiplayerSession.SessionStatus.SignedIn)
            _ = RefreshRoomsListAsync(quiet: true);
    }

    private void SubtabRooms_Click(object sender, RoutedEventArgs e)
    {
        _activeSubtab = Subtab.Rooms;
        SetNewRoomIndicator(false); // opening Rooms clears the "new room" dot
        RefreshFromSession();
        // Coming (back) to the Rooms subtab: quietly freshen the list so
        // rooms created while the user was on another subtab show up at
        // once, without the skeleton flash a full refresh would cause. The
        // 5 s _roomsListTimer keeps it current from here on.
        if (_session?.Status == MultiplayerSession.SessionStatus.SignedIn)
            _ = RefreshRoomsListAsync(quiet: true);
    }
    private void SubtabFriends_Click(object sender, RoutedEventArgs e)
    {
        _activeSubtab = Subtab.Friends;
        RefreshFromSession();
    }
    private void SubtabProfile_Click(object sender, RoutedEventArgs e)
    {
        _activeSubtab = Subtab.Profile;
        RefreshFromSession();
    }
    private void SubtabHistory_Click(object sender, RoutedEventArgs e)
    {
        _activeSubtab = Subtab.History;
        RefreshFromSession();
        _ = RefreshHistoryAsync();
    }

    /// <summary>
    /// Fetch the signed-in user's last 50 matches and render them as
    /// stacked rows. Idempotent; runs whenever the History subtab is
    /// opened. Anonymous viewers (not signed in) see an empty list.
    /// </summary>
    private async Task RefreshHistoryAsync()
    {
        if (_session?.CurrentUser == null || _isRefreshingHistory) return;
        _isRefreshingHistory = true;
        try
        {
            HistoryView.Children.Clear();
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            var stack = new StackPanel { Margin = new Thickness(24, 18, 8, 24) };
            scroll.Content = stack;
            HistoryView.Children.Add(scroll);

            stack.Children.Add(new TextBlock
            {
                Text = "Loading…",
                Foreground = (Brush)Application.Current.FindResource("TextSecondary"),
                FontStyle = FontStyles.Italic,
            });

            var resp = await _session.Api.GetHistoryAsync(_session.CurrentUser.Id);
            stack.Children.Clear();

            if (resp.Matches.Count == 0)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = "No matches yet — your first game will appear here.",
                    Foreground = (Brush)Application.Current.FindResource("TextSecondary"),
                    FontStyle = FontStyles.Italic,
                });
                return;
            }

            foreach (var row in resp.Matches)
                stack.Children.Add(BuildHistoryRow(row));
        }
        catch (Exception ex)
        {
            HistoryView.Children.Clear();
            HistoryView.Children.Add(new TextBlock
            {
                Text = ex.Message,
                Foreground = Brushes.Salmon,
                Margin = new Thickness(24, 18, 24, 0),
                TextWrapping = TextWrapping.Wrap,
            });
        }
        finally
        {
            _isRefreshingHistory = false;
        }
    }

    private Border BuildHistoryRow(MatchHistoryRow row)
    {
        var card = new Border
        {
            Background = (Brush)Application.Current.FindResource("BgPanel"),
            BorderBrush = (Brush)Application.Current.FindResource("BorderSubtle"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 16, 8),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel();
        var label = row.Result switch
        {
            >= 0.99 => "Win",
            <= 0.01 => "Loss",
            _ => "Draw",
        };
        var color = row.Result switch
        {
            >= 0.99 => Brushes.LimeGreen,
            <= 0.01 => Brushes.IndianRed,
            _ => (Brush)Application.Current.FindResource("TextSecondary"),
        };

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = color,
            FontWeight = FontWeights.Bold,
            FontSize = (double)Application.Current.FindResource("FontSizeBody"),
            Margin = new Thickness(0, 0, 8, 0),
        });
        header.Children.Add(new TextBlock
        {
            Text = $"{row.ModId} · {row.MapName ?? "—"}",
            Foreground = (Brush)Application.Current.FindResource("TextPrimary"),
            FontSize = (double)Application.Current.FindResource("FontSizeBody"),
        });
        left.Children.Add(header);

        var meta = $"{row.StartedAt} · {TimeSpan.FromSeconds(row.DurationSeconds):mm\\:ss}";
        if (row.RatingBefore.HasValue && row.RatingAfter.HasValue)
        {
            var delta = row.RatingAfter.Value - row.RatingBefore.Value;
            var sign = delta >= 0 ? "+" : "";
            meta += $" · ELO {row.RatingBefore.Value:0}→{row.RatingAfter.Value:0} ({sign}{delta:0})";
        }
        left.Children.Add(new TextBlock
        {
            Text = meta,
            Foreground = (Brush)Application.Current.FindResource("TextSecondary"),
            FontSize = (double)Application.Current.FindResource("FontSizeCaption"),
            Margin = new Thickness(0, 2, 0, 0),
        });

        Grid.SetColumn(left, 0);
        grid.Children.Add(left);

        if (!string.IsNullOrEmpty(row.ReplayObjectKey))
        {
            var dl = new Button
            {
                Content = "Replay",
                Style = (Style)Application.Current.FindResource("SidebarPrimaryButton"),
                Padding = new Thickness(12, 4, 12, 4),
                MinWidth = 90,
                Background = new SolidColorBrush(Color.FromRgb(0x3a, 0x3d, 0x44)),
                VerticalAlignment = VerticalAlignment.Center,
                Tag = row.Id,
            };
            dl.Click += async (_, _) =>
            {
                try
                {
                    // Open in the user's browser — the Worker streams the
                    // replay back with a Content-Disposition: attachment
                    // header so the browser saves rather than renders.
                    var uri = new Uri(_session!.Api.BaseUri, $"replays/{row.Id}");
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = uri.AbsoluteUri,
                        UseShellExecute = true,
                    });
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Write($"MultiplayerTab: replay open: {ex.Message}");
                }
            };
            Grid.SetColumn(dl, 1);
            grid.Children.Add(dl);
        }

        card.Child = grid;
        return card;
    }

    // ---------- Sign in / out ----------

    private async void SignInButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null) return;
        SignInButton.IsEnabled = false;
        SignInErrorText.Visibility = Visibility.Collapsed;
        try
        {
            var dlg = new GitHubLoginDialog(_session)
            {
                Owner = Window.GetWindow(this),
            };
            var ok = dlg.ShowDialog();
            if (ok == true)
            {
                await RefreshRoomsListAsync();
                await RefreshQuotaAsync();
            }
        }
        catch (Exception ex)
        {
            SignInErrorText.Text = ex.Message;
            SignInErrorText.Visibility = Visibility.Visible;
        }
        finally
        {
            SignInButton.IsEnabled = true;
        }
    }

    private void SignOutLink_Click(object sender, RoutedEventArgs e) =>
        _session?.SignOut();

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshRoomsListAsync();
        await RefreshQuotaAsync();
    }

    private async void CreateRoomButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null || _getActiveProfile == null || _computeModFingerprint == null) return;

        // Build the list of mods the host can pick from. We restrict
        // the dropdown to mods that are actually installed on this PC
        // — picking an uninstalled mod would just fail at fingerprint
        // time. The active profile (from the Play tab) is highlighted
        // as the default but the host can change it.
        var allProfiles = ModRegistry.All;
        var installedProfiles = new List<ModProfile>();
        foreach (var p in allProfiles)
        {
            var installPath = _session.Api != null
                ? GetInstallPath(p)
                : null;
            if (!string.IsNullOrEmpty(installPath))
                installedProfiles.Add(p);
        }

        if (installedProfiles.Count == 0)
        {
            await MpAlertOverlay.NoticeAsync(
                TabRootGrid,
                Strings.Get("MpNoticeModNotInstalledTitle"),
                Strings.Get("MpNoticeModNotInstalledBody"),
                Strings.Get("MpAlertOk"));
            return;
        }

        var initiallySelected = _getActiveProfile() ?? installedProfiles[0];

        var dlg = new CreateLobbyDialog(
            _session,
            installedProfiles,
            initiallySelected,
            // The dialog hands us each picked profile and we return
            // its on-disk fingerprint. Bridge through the same
            // callback the tab already received from MainWindow.
            profile => _computeModFingerprint!(profile),
            // Copy-awareness: which installed copies the mod has + which is
            // active. Lets the host SEE and (for the active mod) CHOOSE the copy.
            BuildCopyInfo,
            // Choosing a copy rotates the active copy (single source of truth);
            // multiplayer launches / fingerprints the active copy.
            installId => _switchActiveCopy != null ? _switchActiveCopy(installId) : Task.CompletedTask)
        {
            Owner = Window.GetWindow(this),
        };
        if (dlg.ShowDialog() != true || dlg.CreatedLobby == null) return;

        try
        {
            var createdModId = dlg.CreatedLobbyProfile?.Id ?? "";
            DiagnosticLog.Write($"CreateRoom: dialog returned lobby id {dlg.CreatedLobby.Id} (mod={createdModId}), entering room");
            // Stamp the current room's mod id so LaunchActiveModGame
            // picks the right profile, even if the user later
            // switches the Play tab to a different mod while still
            // inside the room. CreateLobbyResponse doesn't carry
            // the mod id back (it only has id + status), so we read
            // it from the dialog's selected profile.
            _currentLobbyModId = createdModId;
            _currentLobbyMaxPlayers = dlg.CreatedLobbyMaxPlayers;
            await _session.EnterHostedLobbyAsync(dlg.CreatedLobby, dlg.CreatedLobbyTitle);
            // Optimistic host flag — we created the room, so we ARE the
            // host. The WS room_state frame will reaffirm this when it
            // arrives. Setting it here means the Start button shows up
            // immediately even if the WS hiccups (e.g. a tunnel idle
            // drop) before room_state lands.
            _isHostInCurrentRoom = true;
            RenderRoomPanel();
            DiagnosticLog.Write($"CreateRoom: EnterHostedLobbyAsync completed for {dlg.CreatedLobby.Id}");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"CreateRoom: EnterHostedLobbyAsync THREW: {ex.GetType().Name}: {ex.Message}");
            await MpAlertOverlay.NoticeAsync(
                TabRootGrid,
                Strings.Get("MpNoticeCreateFailedTitle"),
                ex.Message,
                Strings.Get("MpAlertOk"));
            SignInErrorText.Text = ex.Message;
            SignInErrorText.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// Look up the user's install path for a given mod profile via
    /// LauncherConfig — without that, we'd be hammering the disk
    /// inside the dialog for every selection change.
    /// </summary>
    private string? GetInstallPath(ModProfile profile)
    {
        // The launcher config is owned by MainWindow; we don't have a
        // direct reference here. Probe heuristically: try the saved
        // path via the same registry the rest of the launcher uses,
        // then fall back to "any non-empty install probe file under
        // the default folder".
        var saved = WarsOfLibertyLauncher.Models.LauncherConfig
            .Load().GetState(profile.Id).InstallPath;
        if (!string.IsNullOrEmpty(saved)) return saved;

        // The stock Age of Empires III profile is never "installed" through
        // the launcher, so it has no saved path. Resolve it from the detected
        // AoE3 install on disk so it still shows up as host-able / join-able
        // and can be fingerprinted for the version-parity check.
        if (profile.IsStockGame)
            return AoE3Detector.FindInstallRoot();

        return null;
    }

    /// <summary>
    /// Copy-awareness for the create-room dialog: does this mod have multiple
    /// installed copies, which is active, and can we switch it from here (only
    /// when it's the active dashboard mod, since the switch rotates the active
    /// copy). Labels are disambiguated so two copies sharing a folder name are
    /// still distinguishable. Read-only for the stock game / single-install mods.
    /// </summary>
    private Models.ModCopyInfo BuildCopyInfo(ModProfile profile)
    {
        var st = WarsOfLibertyLauncher.Models.LauncherConfig.Load().GetState(profile.Id);
        if (profile.IsStockGame || !st.HasMultipleInstalls)
            return new Models.ModCopyInfo(false, false, System.Array.Empty<Models.ModCopyChoice>());

        var raw = new List<(string Id, string Label, string Path, bool Active)>
        {
            (st.ActiveInstallId, CopyLeaf(st.InstallPath), st.InstallPath, true),
        };
        foreach (var o in st.OtherInstalls)
            raw.Add((o.Id, CopyLeaf(o.InstallPath), o.InstallPath, false));

        var labels = Services.PathDisplay.DisambiguateLabels(
            raw.Select(r => (r.Label, r.Path)).ToList());

        var choices = new List<Models.ModCopyChoice>();
        for (int i = 0; i < raw.Count; i++)
            choices.Add(new Models.ModCopyChoice(raw[i].Id, labels[i], raw[i].Active));

        var active = _getActiveProfile?.Invoke();
        bool canSwitch = active != null
            && string.Equals(active.Id, profile.Id, StringComparison.OrdinalIgnoreCase);
        return new Models.ModCopyInfo(true, canSwitch, choices);
    }

    /// <summary>Folder-leaf label of an install path (the copy's own folder name).</summary>
    private static string CopyLeaf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        var leaf = System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
        return string.IsNullOrEmpty(leaf) ? path! : leaf;
    }


    // ---------- Rooms list polling + rendering ----------

    /// <summary>
    /// Fetch <c>GET /lobbies</c> and render the rooms browser. Called both
    /// as an explicit refresh and as a 10 s background auto-refresh — the
    /// <paramref name="quiet"/> flag is what separates the two.
    /// </summary>
    /// <param name="quiet">
    /// When true this is a background auto-refresh (the 10 s
    /// <see cref="_roomsListTimer"/> tick): skip the "loading" skeleton,
    /// don't repaint when the result matches what's already rendered, and
    /// swallow transient errors instead of wiping the list. A false
    /// (default) call is an explicit refresh — manual Actualizar button,
    /// sign-in, tab activation, leave-room — and always re-renders with the
    /// skeleton + error banner.
    /// </param>
    private async Task RefreshRoomsListAsync(bool quiet = false)
    {
        if (_session == null || _isRefreshingList) return;
        _isRefreshingList = true;
        try
        {
            // Loading skeleton: a single dim line so the user knows
            // a fetch is in flight. The empty-state card and error
            // box are siblings (not children of RoomsListPanel) so
            // we hide both while loading and re-decide afterwards.
            // Skipped on a quiet auto-refresh — flashing "loading…"
            // every 10 s would be worse than swapping the (usually
            // unchanged) rows once the result lands.
            if (!quiet)
            {
                RoomsListPanel.Children.Clear();
                RoomsEmptyState.Visibility = Visibility.Collapsed;
                RoomsErrorBox.Visibility = Visibility.Collapsed;
                RoomsListPanel.Children.Add(new TextBlock
                {
                    Text = Strings.Get("MpRoomsLoading"),
                    Foreground = (Brush)Application.Current.FindResource("TextSecondary"),
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(24, 12, 24, 0),
                });
            }

            var list = await _session.Api.ListLobbiesAsync();
            // Cache the snapshot so the room view (and any other
            // consumer that needs MaxPlayers / IsPrivate / ModId
            // for the current lobby) can read it without an extra
            // round-trip.
            _lastBrowserList = list.Lobbies as List<LobbySummary> ?? new List<LobbySummary>(list.Lobbies);

            // Stamp the "last updated" label on every successful fetch — even a
            // quiet poll that skips the re-render still confirmed the list is
            // current, so the header should reflect it.
            _lastRoomsRenderedAt = DateTime.Now;
            UpdateRoomsUpdatedLabel();

            // Quiet auto-refresh: bail out without touching the visual
            // tree when the rooms are exactly what we already rendered.
            // That keeps Join buttons, hover and scroll position intact
            // (a rebuild would reset them) and leaves the PING column to
            // _roomsPingTimer, which updates it in place. A full/manual
            // refresh always re-renders.
            var signature = BuildRoomsSignature(list.Lobbies);
            if (quiet && signature == _lastRenderedRoomsSignature)
                return;

            RoomsListPanel.Children.Clear();
            RoomsErrorBox.Visibility = Visibility.Collapsed;
            _roomPingCells.Clear();

            if (list.Lobbies.Count == 0)
            {
                // Show the dedicated empty-state card (defined in
                // XAML with the crossed-flags illustration and the
                // outlined Create-room CTA). Better than dumping an
                // italic line in the table because the table header
                // strip stays visible above for context.
                RoomsEmptyState.Visibility = Visibility.Visible;
                UpdateRoomsShowingCount(0);
                _lastRenderedRoomsSignature = signature;
                return;
            }
            RoomsEmptyState.Visibility = Visibility.Collapsed;

            // Render each room as a table row, in the user's chosen sort order
            // (server order by default). The signature above is built from the
            // server order so the quiet diff stays stable regardless of sort.
            var ordered = ApplyRoomSort(list.Lobbies);
            int idx = 0;
            foreach (var lobby in ordered)
                RoomsListPanel.Children.Add(BuildRoomCard(lobby, idx++));
            _lastRenderedRoomsSignature = signature;
            UpdateRoomsShowingCount(ordered.Count);
            _ = Dispatcher.BeginInvoke(new Action(SyncHeaderScrollbarGutter),
                System.Windows.Threading.DispatcherPriority.Render);
        }
        catch (Exception ex)
        {
            // A quiet background poll must not wipe the list the user
            // is looking at over a transient network blip — keep the
            // last good render and just log. Manual / activation
            // refreshes still surface the error banner.
            if (quiet)
            {
                DiagnosticLog.Write($"RefreshRoomsList (quiet) failed: {ex.Message}");
            }
            else
            {
                RoomsListPanel.Children.Clear();
                RoomsErrorText.Text = ex.Message;
                RoomsErrorBox.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            _isRefreshingList = false;
        }
    }

    /// <summary>
    /// Compact signature of the rooms list as the server returned it,
    /// covering every field <see cref="BuildRoomCard"/> renders (in order)
    /// so the quiet auto-refresh can tell "nothing changed" from "repaint
    /// needed" with a single string compare. Ping is deliberately excluded
    /// — it's your own latency, identical across rows, and owned by
    /// <see cref="RefreshRoomPingCells"/>.
    /// </summary>
    private static string BuildRoomsSignature(IReadOnlyList<LobbySummary> lobbies)
    {
        var sb = new System.Text.StringBuilder(lobbies.Count * 48);
        foreach (var l in lobbies)
        {
            sb.Append(l.Id).Append('|')
              .Append(l.Status).Append('|')
              .Append(l.CurrentPlayers).Append('/').Append(l.MaxPlayers).Append('|')
              .Append(l.IsPrivate ? '1' : '0').Append('|')
              .Append(l.Title).Append('|')
              .Append(l.ModId).Append('|')
              .Append(l.Host.DisplayName).Append('|')
              .Append(l.Host.DiscordUsername).Append('\n');
        }
        return sb.ToString();
    }

    // ---------- Rooms table sorting / footer / header alignment ----------

    /// <summary>Which column the rooms table is sorted by (None = server order).</summary>
    private enum RoomSort { None, Room, Mod, Host, Players, Ping, Status }
    private RoomSort _roomsSort = RoomSort.None;
    private bool _roomsSortAsc = true;

    /// <summary>
    /// A column header was clicked: same column toggles asc/desc, a new column
    /// selects it ascending. Re-renders from the cached list (no network).
    /// </summary>
    private void RoomHeader_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string tag) return;
        if (!Enum.TryParse<RoomSort>(tag, out var col)) return;
        if (_roomsSort == col) _roomsSortAsc = !_roomsSortAsc;
        else { _roomsSort = col; _roomsSortAsc = true; }
        UpdateSortArrows();
        RerenderRoomsFromCache();
    }

    /// <summary>Paint each header's sort arrow: ⇅ idle, ↑/↓ on the active column.</summary>
    private void UpdateSortArrows()
    {
        var active = (Brush)Application.Current.FindResource("TextPrimary");
        var idle = (Brush)Application.Current.FindResource("MpTableHeader");
        void Set(TextBlock? arrow, RoomSort col)
        {
            if (arrow == null) return;
            if (_roomsSort == col) { arrow.Text = _roomsSortAsc ? "↑" : "↓"; arrow.Foreground = active; }
            else { arrow.Text = "⇅"; arrow.Foreground = idle; }
        }
        Set(SortArrowRoom, RoomSort.Room);
        Set(SortArrowMod, RoomSort.Mod);
        Set(SortArrowHost, RoomSort.Host);
        Set(SortArrowPlayers, RoomSort.Players);
        Set(SortArrowPing, RoomSort.Ping);
        Set(SortArrowStatus, RoomSort.Status);
    }

    /// <summary>Mod display name for sorting (falls back to the raw id).</summary>
    private static string ModSortName(LobbySummary l)
    {
        var n = ModRegistry.Find(l.ModId)?.DisplayName;
        return string.IsNullOrWhiteSpace(n) ? l.ModId : n!;
    }

    /// <summary>Host name for sorting (display → Discord username → em-dash).</summary>
    private static string HostSortName(LobbySummary l)
    {
        var n = l.Host.DisplayName;
        if (string.IsNullOrWhiteSpace(n) || n == "-") n = l.Host.DiscordUsername;
        return string.IsNullOrWhiteSpace(n) || n == "-" ? "" : n;
    }

    /// <summary>Rank for status sorting: Waiting &lt; Full &lt; In Game.</summary>
    private static int StatusRank(LobbySummary l)
        => l.Status == "in_game" ? 2 : (l.CurrentPlayers >= l.MaxPlayers ? 1 : 0);

    /// <summary>
    /// Return a copy of the rooms ordered by the active sort. Server order when
    /// <see cref="_roomsSort"/> is None. Stable (OrderBy) so equal keys keep
    /// their relative order; a descending sort reverses the ascending result.
    /// </summary>
    private List<LobbySummary> ApplyRoomSort(IEnumerable<LobbySummary> src)
    {
        var listCopy = src.ToList();
        if (_roomsSort == RoomSort.None) return listCopy;
        IEnumerable<LobbySummary> ordered = _roomsSort switch
        {
            RoomSort.Room => listCopy.OrderBy(l => l.Title, StringComparer.OrdinalIgnoreCase),
            RoomSort.Mod => listCopy.OrderBy(ModSortName, StringComparer.OrdinalIgnoreCase),
            RoomSort.Host => listCopy.OrderBy(HostSortName, StringComparer.OrdinalIgnoreCase),
            RoomSort.Players => listCopy.OrderBy(l => l.CurrentPlayers).ThenBy(l => l.MaxPlayers),
            RoomSort.Ping => listCopy, // your latency is identical across rows — no-op
            RoomSort.Status => listCopy.OrderBy(StatusRank),
            _ => listCopy,
        };
        var result = ordered.ToList();
        if (!_roomsSortAsc) result.Reverse();
        return result;
    }

    /// <summary>
    /// Re-render the rooms list from the cached snapshot applying the current
    /// sort — used by a header click (no network). Leaves the render signature
    /// untouched so a following quiet poll with unchanged data still skips.
    /// </summary>
    private void RerenderRoomsFromCache()
    {
        if (_lastBrowserList == null || _lastBrowserList.Count == 0) return;
        RoomsEmptyState.Visibility = Visibility.Collapsed;
        RoomsListPanel.Children.Clear();
        _roomPingCells.Clear();
        var ordered = ApplyRoomSort(_lastBrowserList);
        int idx = 0;
        foreach (var lobby in ordered)
            RoomsListPanel.Children.Add(BuildRoomCard(lobby, idx++));
        UpdateRoomsShowingCount(ordered.Count);
        Dispatcher.BeginInvoke(new Action(SyncHeaderScrollbarGutter),
            System.Windows.Threading.DispatcherPriority.Render);
    }

    /// <summary>Set the "Showing N rooms" footer count.</summary>
    private void UpdateRoomsShowingCount(int n)
    {
        if (RoomsShowingCount == null) return;
        RoomsShowingCount.Text = Strings.Format("MpRoomsShowingCount", n);
    }

    /// <summary>
    /// Keep the header strip's right edge aligned with the rows when the Auto
    /// vertical scrollbar appears/disappears (the header lives outside the
    /// ScrollViewer, so the scrollbar only steals width from the rows).
    /// </summary>
    private void SyncHeaderScrollbarGutter()
    {
        if (RoomsHeaderStrip == null || RoomsListScroll == null) return;
        bool bar = RoomsListScroll.ComputedVerticalScrollBarVisibility == Visibility.Visible;
        double right = bar ? 30 + SystemParameters.VerticalScrollBarWidth : 30;
        var m = RoomsHeaderStrip.Margin;
        if (Math.Abs(m.Right - right) > 0.5)
            RoomsHeaderStrip.Margin = new Thickness(m.Left, m.Top, right, m.Bottom);
    }

    // ---------- Global chat ----------

    /// <summary>
    /// Open the global chat socket when it should be live (tab visible +
    /// signed in) and close it otherwise. Idempotent — safe to call from
    /// the visibility gate, session-state changes and tab activation.
    /// </summary>
    private void SyncGlobalChat()
    {
        var shouldConnect = IsVisible
            && _session?.Status == MultiplayerSession.SessionStatus.SignedIn;
        if (shouldConnect) OpenGlobalChat();
        else CloseGlobalChat();
    }

    private void OpenGlobalChat()
    {
        if (_globalChatSocket != null) return;             // already connected
        var token = _session?.SessionToken;
        if (_session == null || string.IsNullOrEmpty(token)) return;
        try
        {
            var uri = LobbyWebSocket.BuildWsUri(_session.Api.BaseUri, "global/ws");
            var sock = new LobbyWebSocket(uri, LobbyWebSocket.HelloMode.SessionToken, token);
            sock.FrameReceived += OnGlobalChatFrame;
            _globalChatSocket = sock;
            _globalChatRendered = false;
            sock.Start();
            UpdateGlobalChatEmptyHint();   // shows "connecting…" until global_state lands
            DiagnosticLog.Write($"Global chat: connecting to {uri}");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Global chat: open failed: {ex.Message}");
        }
    }

    private void CloseGlobalChat()
    {
        var sock = _globalChatSocket;
        if (sock == null) return;
        _globalChatSocket = null;
        sock.FrameReceived -= OnGlobalChatFrame;
        // DisposeAsync aborts the socket synchronously (no polite close
        // frame) so the fire-and-forget never actually blocks.
        _ = sock.DisposeAsync();
        _globalChatRendered = false;
        GlobalChatPanel.Children.Clear();
        _lastGlobalChatAuthor = null;
        GlobalChatPresenceText.Text = "";
        GlobalChatNotice.Visibility = Visibility.Collapsed;
        UpdateGlobalChatEmptyHint();
    }

    /// <summary>
    /// WS frame handler for the global room. Fires on a background thread;
    /// we marshal to the dispatcher and ignore frames from a socket we've
    /// since replaced/closed (a close can race the last receive).
    /// </summary>
    private void OnGlobalChatFrame(object? sender, LobbyWebSocket.FrameReceivedEventArgs e) =>
        Dispatcher.InvokeAsync(() =>
        {
            if (!ReferenceEquals(sender, _globalChatSocket)) return;
            try
            {
                switch (e.Type)
                {
                    case "global_state":
                        RenderGlobalChatState(e.Json);
                        break;
                    case "chat":
                        if (e.Json.TryGetProperty("line", out var line))
                            AppendGlobalChatLine(line, scroll: true);
                        break;
                    case "presence":
                        ParseOnlineUsers(e.Json);
                        if (e.Json.TryGetProperty("online", out var on) && on.TryGetInt32(out var n))
                            UpdateGlobalPresence(n);
                        break;
                    case "error":
                        var code = e.Json.TryGetProperty("code", out var c) ? (c.GetString() ?? "") : "";
                        DiagnosticLog.Write($"Global chat error frame: {code}");
                        ShowGlobalChatNoticeFor(code);
                        break;
                }
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"Global chat frame handling failed: {ex.Message}");
            }
        });

    private void RenderGlobalChatState(JsonElement json)
    {
        GlobalChatPanel.Children.Clear();
        _lastGlobalChatAuthor = null;
        _globalChatRendered = true;
        if (json.TryGetProperty("history", out var hist) && hist.ValueKind == JsonValueKind.Array)
        {
            foreach (var line in hist.EnumerateArray())
                AppendGlobalChatLine(line, scroll: false);
        }
        ParseOnlineUsers(json);
        if (json.TryGetProperty("online", out var on) && on.TryGetInt32(out var n))
            UpdateGlobalPresence(n);
        UpdateGlobalChatEmptyHint();
        ScrollGlobalChatToEnd();
    }

    /// <summary>
    /// Cache the global-chat "who's online" list (with each player's live status)
    /// from a presence / global_state frame's optional <c>onlineUsers</c> array,
    /// then (re)render the live players panel. Only replaces the cache when the
    /// frame carries the array — an old backend that sends only the count leaves
    /// the list untouched (back-compat; the panel then shows empty). Each entry's
    /// <c>status</c> is <c>in_game</c> / <c>in_room</c> / <c>idle</c> (missing →
    /// idle, so an old backend puts everyone "in launcher").
    /// </summary>
    private void ParseOnlineUsers(JsonElement frame)
    {
        if (frame.TryGetProperty("onlineUsers", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            _globalOnlineUsers.Clear();
            foreach (var u in arr.EnumerateArray())
            {
                var userId = u.TryGetProperty("userId", out var idEl) ? (idEl.GetString() ?? "") : "";
                var login = u.TryGetProperty("login", out var lEl) ? (lEl.GetString() ?? "") : "";
                var avatarUrl = u.TryGetProperty("avatarUrl", out var avEl) ? avEl.GetString() : null;
                var status = u.TryGetProperty("status", out var stEl) ? (stEl.GetString() ?? "idle") : "idle";
                _globalOnlineUsers.Add((userId, login, avatarUrl, status));
            }
        }
        RenderPlayersPanel();
    }

    private void AppendGlobalChatLine(JsonElement line, bool scroll)
    {
        var login = line.TryGetProperty("login", out var l) ? (l.GetString() ?? "") : "";
        var body = line.TryGetProperty("body", out var b) ? (b.GetString() ?? "") : "";
        long at = line.TryGetProperty("at", out var a) && a.TryGetInt64(out var ms) ? ms : 0;
        var avatarUrl = line.TryGetProperty("avatarUrl", out var av) ? av.GetString() : null;
        if (string.IsNullOrEmpty(body)) return;
        AppendGlobalChatRow(login, body, at, avatarUrl);
        UpdateGlobalChatEmptyHint();
        if (scroll) ScrollGlobalChatToEnd();
    }

    /// <summary>Author of the last appended global-chat row, so consecutive
    /// messages from the same person render as continuations (no repeated
    /// avatar/name). Reset to null whenever the panel is cleared.</summary>
    private string? _lastGlobalChatAuthor;

    /// <summary>
    /// Build one chat row as a subtle left-aligned bubble: avatar (real Discord
    /// photo, monogram fallback) + a name/time header line + the message body in
    /// a rounded bubble. Consecutive messages from the SAME author render as
    /// continuations — no repeated avatar/name, just the bubble aligned under
    /// the first one — to cut the visual repetition.
    /// </summary>
    private void AppendGlobalChatRow(string login, string body, long atMs, string? avatarUrl)
    {
        var textPrimary = (Brush)Application.Current.FindResource("TextPrimary");
        var textSecondary = (Brush)Application.Current.FindResource("TextSecondary");
        var bubbleBg = (Brush)Application.Current.FindResource("MpSurfaceAlt");

        bool sameAuthor = !string.IsNullOrEmpty(login)
            && string.Equals(login, _lastGlobalChatAuthor, StringComparison.Ordinal);

        // The message body, rendered as a rounded bubble.
        var bubble = new Border
        {
            Background = bubbleBg,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 6, 10, 7),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, sameAuthor ? 0 : 3, 0, 0),
            Child = new TextBlock
            {
                Text = body,
                Foreground = textPrimary,
                FontSize = (double)Application.Current.FindResource("FontSizeBody"),
                TextWrapping = TextWrapping.Wrap,
            },
        };

        var grid = new Grid
        {
            // Tight gap for a continuation, a clearer gap when the author changes.
            Margin = new Thickness(0, sameAuthor ? 2 : 10, 0, 0),
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        if (sameAuthor)
        {
            // Continuation: just the bubble, aligned under the first message.
            Grid.SetColumn(bubble, 1);
            grid.Children.Add(bubble);
        }
        else
        {
            // Avatar: monogram fallback with the real Discord photo painted on
            // top when we have a URL (if the image fails to load, the monogram
            // underneath stays visible).
            var avatarInner = new Grid();
            avatarInner.Children.Add(new TextBlock
            {
                Text = Monogram(login),
                Foreground = textSecondary,
                FontWeight = FontWeights.Bold,
                FontSize = (double)Application.Current.FindResource("FontSizeBody"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
            if (!string.IsNullOrEmpty(avatarUrl))
            {
                var photo = new System.Windows.Shapes.Ellipse { Width = 30, Height = 30 };
                try
                {
                    photo.Fill = new ImageBrush(
                        new System.Windows.Media.Imaging.BitmapImage(new Uri(avatarUrl, UriKind.Absolute)))
                    {
                        Stretch = Stretch.UniformToFill,
                    };
                }
                catch { /* malformed URL → leave the monogram visible */ }
                avatarInner.Children.Add(photo);
            }
            var avatar = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                Background = bubbleBg,
                VerticalAlignment = VerticalAlignment.Top,
                Child = avatarInner,
            };
            Grid.SetColumn(avatar, 0);
            grid.Children.Add(avatar);

            var stack = new StackPanel();
            Grid.SetColumn(stack, 1);

            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(login) ? "—" : login,
                Foreground = textPrimary,
                FontWeight = FontWeights.SemiBold,
                FontSize = (double)Application.Current.FindResource("FontSizeBody"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            if (atMs > 0)
            {
                header.Children.Add(new TextBlock
                {
                    Text = FormatChatTime(atMs),
                    Foreground = textSecondary,
                    FontSize = (double)Application.Current.FindResource("FontSizeCaption"),
                    Margin = new Thickness(8, 1, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }
            stack.Children.Add(header);
            stack.Children.Add(bubble);
            grid.Children.Add(stack);
        }

        GlobalChatPanel.Children.Add(grid);
        _lastGlobalChatAuthor = login;
    }

    private void UpdateGlobalPresence(int online)
    {
        // The presence dot lives in the merged header now, so just the count text.
        GlobalChatPresenceText.Text = Strings.Format("MpGlobalChatPresence", online);
        // This live presence is also the top-bar "players online" source now, so
        // both read the same real connected-user count.
        _lastGlobalOnline = online;
        UpdateTopBarCounts();
    }

    /// <summary>
    /// Toggle the centered hint shown when the message list is empty:
    /// "connecting…" before the first <c>global_state</c>, "no messages yet"
    /// after.
    /// </summary>
    private void UpdateGlobalChatEmptyHint()
    {
        if (GlobalChatPanel.Children.Count > 0)
        {
            GlobalChatEmptyHint.Visibility = Visibility.Collapsed;
            return;
        }
        GlobalChatEmptyHint.Text = _globalChatRendered
            ? Strings.Get("MpGlobalChatEmpty")
            : Strings.Get("MpGlobalChatConnecting");
        GlobalChatEmptyHint.Visibility = Visibility.Visible;
    }

    private void ScrollGlobalChatToEnd() => GlobalChatScroll.ScrollToEnd();

    /// <summary>
    /// Surface a localized hint above the composer when the server drops a
    /// message (slow-mode / rate-limit / auto-timeout / too-long). Unknown or
    /// transport-level error codes aren't user-facing — they only get logged.
    /// </summary>
    private void ShowGlobalChatNoticeFor(string code)
    {
        var key = code switch
        {
            "chat_slow_mode" => "MpGlobalChatSlowMode",
            "chat_rate_limited" => "MpGlobalChatRateLimited",
            "chat_muted" => "MpGlobalChatMuted",
            "chat_timeout" => "MpGlobalChatTimedOut",
            "chat_too_long" => "MpGlobalChatTooLong",
            _ => null,
        };
        if (key == null) return;
        GlobalChatNotice.Text = Strings.Get(key);
        GlobalChatNotice.Visibility = Visibility.Visible;
    }

    private void SendGlobalChat()
    {
        var sock = _globalChatSocket;
        var body = GlobalChatInput.Text?.Trim() ?? "";
        if (sock == null || string.IsNullOrEmpty(body)) return;
        GlobalChatInput.Clear();   // the server echoes the message back to us
        _ = sock.SendChatAsync(body);
    }

    private void GlobalChatSendButton_Click(object sender, RoutedEventArgs e) => SendGlobalChat();

    private void GlobalChatInput_KeyDown(object sender, KeyEventArgs e)
    {
        // Enter sends; Shift+Enter is reserved for a future multiline box.
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            SendGlobalChat();
        }
    }

    private void GlobalChatInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        GlobalChatPlaceholder.Visibility = string.IsNullOrEmpty(GlobalChatInput.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        // Typing again dismisses any throttle hint.
        if (GlobalChatNotice.Visibility == Visibility.Visible)
            GlobalChatNotice.Visibility = Visibility.Collapsed;
    }

    private static string Monogram(string login) =>
        string.IsNullOrWhiteSpace(login) ? "?" : login.Substring(0, 1).ToUpperInvariant();

    /// <summary>
    /// A circular avatar disc: the user's Discord photo when we have a URL, with a
    /// coloured-hash monogram underneath (visible when there's no photo or it fails
    /// to load). Reused by the roster, the rooms-list host cell and the room-peek
    /// popup so Discord avatars render consistently everywhere — matching the webhook.
    /// </summary>
    private static FrameworkElement BuildAvatarDisc(string name, string? avatarUrl, double size)
    {
        var disc = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        disc.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(size / 2),
            Background = HostMonogramBrush(name),
            Child = new TextBlock
            {
                Text = Monogram(name),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = (double)Application.Current.FindResource("FontSizeCaption"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        });
        if (!string.IsNullOrEmpty(avatarUrl))
        {
            try
            {
                disc.Children.Add(new System.Windows.Shapes.Ellipse
                {
                    Width = size,
                    Height = size,
                    Fill = new ImageBrush(
                        new System.Windows.Media.Imaging.BitmapImage(new Uri(avatarUrl, UriKind.Absolute)))
                    {
                        Stretch = Stretch.UniformToFill,
                    },
                });
            }
            catch { /* malformed URL → monogram stays visible */ }
        }
        return disc;
    }

    private static string FormatChatTime(long atMs) =>
        DateTimeOffset.FromUnixTimeMilliseconds(atMs).LocalDateTime.ToString("HH:mm");

    // Top-bar count sources. "players online" prefers the LIVE global-chat
    // presence (the same number the chat shows as "N connected" — the users
    // actually connected right now), which is why it now matches the chat and
    // no longer shows the /quota in-lobby count. `_lastGlobalOnline` stays null
    // until the first presence frame arrives, so until then we fall back to the
    // /quota active-players count. "active rooms" stays from /quota.
    private int? _lastGlobalOnline;
    private int _lastQuotaPlayers;
    private int _lastActiveRooms;

    // The connected global-chat users + each one's live status, cached from the
    // presence / global_state frames' onlineUsers array (see ParseOnlineUsers).
    // Status: "in_game" / "in_room" / "idle". Rendered by RenderPlayersPanel.
    private readonly List<(string userId, string login, string? avatarUrl, string status)> _globalOnlineUsers = new();

    private void UpdateTopBarCounts()
    {
        int players = _lastGlobalOnline ?? _lastQuotaPlayers;
        // The 👥 icon is static in the XAML chip; here we set just the label.
        // "active rooms" is its own static chip. The detailed list lives in the
        // players panel in the right column (RenderPlayersPanel).
        OnlinePlayersText.Text = $"{players} players online";
        ActiveRoomsText.Text = $"🏠 {_lastActiveRooms} active rooms";
    }

    /// <summary>
    /// (Re)render the live players panel (right column, bottom half) from the
    /// cached presence list, grouped by status into three sections —
    /// 🟢 In game / 🟡 In a room / ⚪ In launcher — each with a count. One row per
    /// player: <see cref="BuildAvatarDisc"/> + name, own row tagged "· you".
    /// Called on every presence / global_state frame (cheap, ≤~60 rows). Empty
    /// (old backend / no presence yet) → a neutral hint.
    /// </summary>
    private void RenderPlayersPanel()
    {
        if (PlayersPanel == null) return;
        PlayersPanel.Children.Clear();
        PlayersPanelTitle.Text = Strings.Format("MpPlayersPanelTitle", _globalOnlineUsers.Count);

        Brush R(string k) => (Brush)Application.Current.FindResource(k);
        double F(string k) => (double)Application.Current.FindResource(k);

        if (_globalOnlineUsers.Count == 0)
        {
            PlayersPanel.Children.Add(new TextBlock
            {
                Text = Strings.Get("MpOnlinePlayersEmpty"),
                Foreground = R("TextSecondary"),
                FontSize = F("FontSizeBody"),
                Margin = new Thickness(4, 6, 0, 0),
            });
            return;
        }

        var me = _session?.CurrentUser;
        bool IsMe((string userId, string login, string? avatarUrl, string status) u) =>
            me != null && (
                (!string.IsNullOrEmpty(u.userId) && string.Equals(u.userId, me.Id, StringComparison.Ordinal))
                || (!string.IsNullOrEmpty(u.login)
                    && string.Equals(u.login, me.DiscordUsername, StringComparison.OrdinalIgnoreCase)));

        void Section(string statusKey, string headerKey, string dotBrushKey)
        {
            var members = _globalOnlineUsers.Where(u => u.status == statusKey).ToList();
            // Category header: a status dot + "<label> · N" (always shown).
            var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 4) };
            headerRow.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 8, Height = 8, Fill = R(dotBrushKey),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0),
            });
            headerRow.Children.Add(new TextBlock
            {
                Text = Strings.Format(headerKey, members.Count),
                Foreground = R("TextSecondary"),
                FontSize = F("FontSizeCaption"),
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            });
            PlayersPanel.Children.Add(headerRow);

            foreach (var u in members)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 2, 0, 2) };
                var disc = BuildAvatarDisc(u.login, u.avatarUrl, 24);
                disc.Margin = new Thickness(0, 0, 8, 0);
                row.Children.Add(disc);
                row.Children.Add(new TextBlock
                {
                    Text = u.login,
                    Foreground = R("TextPrimary"),
                    FontSize = F("FontSizeBody"),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 200,
                });
                if (IsMe(u))
                    row.Children.Add(new TextBlock
                    {
                        Text = "  · " + Strings.Get("MpOnlinePlayersYou"),
                        Foreground = R("TextSecondary"),
                        FontSize = F("FontSizeCaption"),
                        VerticalAlignment = VerticalAlignment.Center,
                    });
                PlayersPanel.Children.Add(row);
            }
        }

        // Ordered: playing → waiting in a room → idle in the launcher.
        Section("in_game", "MpPlayersInGame", "MpStatusInGame");
        Section("in_room", "MpPlayersInRoom", "MpStatusFull");
        Section("idle", "MpPlayersInLauncher", "TextSecondary");
    }

    // The room-roster "peek" popup (see who's in a room without joining). Single
    // instance; cleared DEFERRED on Closed so a re-click on the same cell toggles
    // it off instead of reopening (the StaysOpen=false auto-dismiss + click race).
    private System.Windows.Controls.Primitives.Popup? _peekPopup;

    private async void PlayersPeek_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement anchor || anchor.Tag is not LobbySummary lobby || _session == null)
            return;
        // Toggle: a re-click on the same cell closes the open peek.
        if (_peekPopup != null) { _peekPopup.IsOpen = false; return; }

        Brush R(string k) => (Brush)Application.Current.FindResource(k);
        double F(string k) => (double)Application.Current.FindResource(k);

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(lobby.Title) ? Strings.Get("MpRoomPeekTitle") : lobby.Title,
            Foreground = R("TextPrimary"),
            FontWeight = FontWeights.Bold,
            FontSize = F("FontSizeBodyStrong"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 260,
            Margin = new Thickness(0, 0, 0, 8),
        });
        var loading = new TextBlock
        {
            Text = Strings.Get("MpRoomPeekLoading"),
            Foreground = R("TextSecondary"),
            FontSize = F("FontSizeBody"),
        };
        panel.Children.Add(loading);

        var card = new Border
        {
            Background = R("MpSurface"),
            BorderBrush = R("MpCardBorder"),
            BorderThickness = new Thickness(1),
            CornerRadius = (CornerRadius)Application.Current.FindResource("RadiusLg"),
            Padding = new Thickness(14),
            MinWidth = 220,
            MaxWidth = 300,
            Child = panel,
        };
        var popup = new System.Windows.Controls.Primitives.Popup
        {
            Child = card,
            PlacementTarget = anchor,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
        };
        ChromePopups.Track(popup, anchor);
        popup.Closed += (_, _) => Dispatcher.BeginInvoke(
            new Action(() => { if (ReferenceEquals(_peekPopup, popup)) _peekPopup = null; }),
            System.Windows.Threading.DispatcherPriority.Background);
        _peekPopup = popup;
        popup.IsOpen = true;

        try
        {
            var detail = await _session.Api.GetLobbyByIdAsync(lobby.Id);
            if (!ReferenceEquals(_peekPopup, popup)) return; // closed while loading
            panel.Children.Remove(loading);
            panel.Children.Add(new TextBlock
            {
                Text = $"👤 {detail.CurrentPlayers} / {detail.MaxPlayers}",
                Foreground = R("TextSecondary"),
                FontSize = F("FontSizeCaption"),
                Margin = new Thickness(0, 0, 0, 8),
            });
            if (detail.Members.Count == 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = Strings.Get("MpRoomPeekEmpty"),
                    Foreground = R("TextSecondary"),
                    FontSize = F("FontSizeBody"),
                });
            }
            foreach (var m in detail.Members)
            {
                var display = !string.IsNullOrWhiteSpace(m.DisplayName) ? m.DisplayName : m.DiscordUsername;
                bool isHost = string.Equals(m.Id, detail.HostUserId, StringComparison.Ordinal);
                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 3, 0, 3),
                };
                var disc = BuildAvatarDisc(display, m.AvatarUrl, 24);
                disc.Margin = new Thickness(0, 0, 8, 0);
                row.Children.Add(disc);
                row.Children.Add(new TextBlock
                {
                    Text = display,
                    Foreground = isHost ? R("AccentBrush") : R("TextPrimary"),
                    FontSize = F("FontSizeBody"),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 190,
                });
                if (isHost)
                    row.Children.Add(new TextBlock
                    {
                        Text = "  · " + Strings.Get("MpRoomBadgeHost"),
                        Foreground = R("AccentBrush"),
                        FontSize = F("FontSizeCaption"),
                        VerticalAlignment = VerticalAlignment.Center,
                    });
                else if (m.IsReady)
                    row.Children.Add(new TextBlock
                    {
                        Text = "  ✓ " + Strings.Get("MpRoomReady"),
                        Foreground = R("MpStatusInGame"),
                        FontSize = F("FontSizeCaption"),
                        VerticalAlignment = VerticalAlignment.Center,
                    });
                panel.Children.Add(row);
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Room peek failed: {ex.Message}");
            if (!ReferenceEquals(_peekPopup, popup)) return;
            panel.Children.Remove(loading);
            panel.Children.Add(new TextBlock
            {
                Text = Strings.Get("MpRoomPeekError"),
                Foreground = R("ErrorBrush"),
                FontSize = F("FontSizeBody"),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 260,
            });
        }
    }

    private async Task RefreshQuotaAsync()
    {
        if (_session == null) return;
        try
        {
            var q = await _session.Api.GetQuotaAsync();
            // Render in the "12 players online · 4 active rooms" style. The /max
            // counter lives in the tooltip so the header strip stays compact.
            _lastQuotaPlayers = q.Players.Active;
            _lastActiveRooms = q.Lobbies.Active;
            // The /max breakdown lives on the active-rooms chip's tooltip; the
            // players pill keeps its own "see who's online" tooltip.
            ActiveRoomsChip.ToolTip = Strings.Format("MpQuotaBar",
                q.Players.Active, q.Players.Max,
                q.Lobbies.Active, q.Lobbies.Max);
            UpdateTopBarCounts();
        }
        catch
        {
            // Quota fetch failed: blank only the quota-derived rooms label; the
            // players pill is presence-driven and keeps its last value.
            ActiveRoomsText.Text = "";
        }
    }

    /// <summary>
    /// Build one room as a full-width CARD styled like a table row: SALA
    /// (mod icon disc — ★ fallback — + title + mod/private chips), ANFITRIÓN,
    /// JUGADORES, PING, ESTADO, ACCIÓN. The six column widths mirror the header Grid in
    /// MultiplayerTab.xaml (and its 31px side margin) so the columns line up
    /// under the labels. Hover lift comes from the MpRoomCard style.
    /// </summary>
    private Border BuildRoomCard(LobbySummary lobby, int rowIndex)
    {
        // Is the lobby's mod actually installed on this PC? If not, the user
        // can't join (they wouldn't pass the fingerprint check). The card is
        // dimmed with a "mod not installed" note so it's obvious why Join is off.
        var modInstalled = IsModInstalledLocally(lobby.ModId);
        var inGame = lobby.Status == "in_game";
        var isFull = lobby.CurrentPlayers >= lobby.MaxPlayers;
        var me = _session?.CurrentUser;
        var textPrimary = (Brush)Application.Current.FindResource("TextPrimary");
        var textSecondary = (Brush)Application.Current.FindResource("TextSecondary");

        var card = new Border
        {
            // MpRoomCard is a LOCAL UserControl resource (not app-global like
            // the brushes), so resolve it via this control's FindResource, not
            // Application.Current.FindResource (which only sees merged app dicts).
            Style = (Style)FindResource("MpRoomCard"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0),
            Opacity = modInstalled ? 1.0 : 0.6,
            Tag = lobby,
        };

        // (The card's illumination — a STATIC, subtle BLUE rim + faint blue
        // glow — lives in the MpRoomCard style now; no per-card animation.)

        // Seven columns mirroring the header Grid (MultiplayerTab.xaml): ROOM,
        // MOD, HOST, PLAYERS, PING, STATUS, ACTION. STAR-sized with Min/Max (NOT
        // fixed px) — fixed widths overflowed a narrow window (the rooms list
        // shares its row with the flexible chat, and the ScrollViewer has
        // horizontal scroll disabled), clipping the right-most ACTION column off
        // screen. Stars always divide the available width so the row never
        // overflows; ROOM has NO MaxWidth so it absorbs slack (no empty band);
        // MaxWidth caps the others; MinWidth (esp. ACTION) keeps the button fully
        // visible when space is tight. Keep these in lockstep with the header
        // definitions (RoomsHeaderStrip in MultiplayerTab.xaml).
        // Proportional, NO MaxWidth (except none): columns grow together on a
        // wide window so the air distributes evenly (symmetric) instead of ROOM
        // eating all slack. Low MinWidths so the 7 fit a small window with the
        // ACTION button visible. IDENTICAL to the header (lockstep).
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.3, GridUnitType.Star), MinWidth = 120 });  // ROOM
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star), MinWidth = 58 }); // MOD
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star), MinWidth = 66 }); // HOST
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.62, GridUnitType.Star), MinWidth = 46 }); // PLAYERS
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.62, GridUnitType.Star), MinWidth = 48 }); // PING
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star), MinWidth = 60 });  // STATUS
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.95, GridUnitType.Star), MinWidth = 100 }); // ACTION

        // === Col 0: ROOM — mod icon disc (★ fallback) + title (wraps to 2
        // lines, never hard-cut) over an optional sub-line (🔒 private / "not
        // installed"). A Grid{Auto,*} — NOT a horizontal StackPanel — so the
        // title is width-constrained and wrapping/ellipsis actually engage. ===
        var modProfile = ModRegistry.Find(lobby.ModId);
        var modName = modProfile?.DisplayName;
        if (string.IsNullOrWhiteSpace(modName)) modName = lobby.ModId;

        var salaCell = new Grid { VerticalAlignment = VerticalAlignment.Center };
        salaCell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        salaCell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var modIconBrush = ResolveRoomModIcon(modProfile);
        FrameworkElement disc;
        if (modIconBrush != null)
        {
            // Border background is clipped to CornerRadius, so the
            // UniformToFill brush renders as a centre-cropped circle.
            disc = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                Background = modIconBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
            };
        }
        else
        {
            disc = new TextBlock
            {
                Text = "★",
                Foreground = (Brush)Application.Current.FindResource("AccentBrush"),
                // Mod-icon fallback glyph — sized to the icon slot, not a type token.
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
            };
        }
        Grid.SetColumn(disc, 0);
        salaCell.Children.Add(disc);

        var salaText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        salaText.Children.Add(new TextBlock
        {
            Text = lobby.Title,
            Foreground = textPrimary,
            FontSize = (double)Application.Current.FindResource("FontSizeBodyStrong"),
            FontWeight = FontWeights.SemiBold,
            // Single line, ellipsized (compact rows). A private room is conveyed
            // by the purple "Private" dot in the STATUS column, not a lock here.
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        if (!modInstalled)
        {
            salaText.Children.Add(new TextBlock
            {
                Text = Strings.Get("MpRoomModNotInstalled"),
                Foreground = textSecondary,
                FontSize = (double)Application.Current.FindResource("FontSizeCaption"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 2, 0, 0),
            });
        }
        Grid.SetColumn(salaText, 1);
        salaCell.Children.Add(salaText);
        Grid.SetColumn(salaCell, 0);
        grid.Children.Add(salaCell);

        // === Col 1: MOD — the mod's name as a blue chip (its own column now,
        // moved out of the ROOM cell). ===
        var modCell = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        modCell.Children.Add(BuildRoomChip(
            modName!,
            (Brush)Application.Current.FindResource("MpModBadgeBg"),
            (Brush)Application.Current.FindResource("FgHoverBlue")));
        Grid.SetColumn(modCell, 1);
        grid.Children.Add(modCell);

        // === Col 2: HOST — colored initial circle + name (Grid{Auto,*} so the
        // name ellipsizes instead of overflowing). Same host-name resolution as
        // the table (display → Discord username → me → em-dash). ===
        var hostName = lobby.Host.DisplayName;
        if (string.IsNullOrWhiteSpace(hostName) || hostName == "-")
            hostName = lobby.Host.DiscordUsername;
        if (string.IsNullOrWhiteSpace(hostName) || hostName == "-")
        {
            var hostIsMe = me != null
                && _isHostInCurrentRoom
                && string.Equals(lobby.Id, _session?.CurrentLobbyId, StringComparison.Ordinal);
            if (hostIsMe)
                hostName = string.IsNullOrEmpty(me!.DiscordUsername) ? me.DisplayName : me.DiscordUsername;
        }
        if (string.IsNullOrWhiteSpace(hostName) || hostName == "-")
            hostName = "—";

        var hostCell = new Grid { VerticalAlignment = VerticalAlignment.Center };
        hostCell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hostCell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var hostDisc = BuildAvatarDisc(hostName, lobby.Host?.AvatarUrl, 24);
        hostDisc.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(hostDisc, 0);
        hostCell.Children.Add(hostDisc);
        var hostNameText = new TextBlock
        {
            Text = hostName,
            Foreground = textPrimary,
            FontSize = (double)Application.Current.FindResource("FontSizeBody"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(hostNameText, 1);
        hostCell.Children.Add(hostNameText);
        Grid.SetColumn(hostCell, 2);
        grid.Children.Add(hostCell);

        // === Col 3: PLAYERS — icon + X/Y, clickable to PEEK the roster without joining. ===
        var playersCell = new TextBlock
        {
            Text = $"👤 {lobby.CurrentPlayers} / {lobby.MaxPlayers}",
            Foreground = textPrimary,
            FontSize = (double)Application.Current.FindResource("FontSizeBody"),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = System.Windows.Input.Cursors.Hand,
            Tag = lobby,
            ToolTip = Strings.Get("MpRoomPeekTooltip"),
            TextDecorations = null,
        };
        // Underline on hover so it reads as clickable (link affordance).
        playersCell.MouseEnter += (_, _) => playersCell.TextDecorations = TextDecorations.Underline;
        playersCell.MouseLeave += (_, _) => playersCell.TextDecorations = null;
        playersCell.MouseLeftButtonUp += PlayersPeek_Click;
        Grid.SetColumn(playersCell, 3);
        grid.Children.Add(playersCell);

        // === Col 4: PING — registered so RefreshRoomPingCells() updates it in
        // place (no rebuild). It's YOUR internet latency (same for every row;
        // /lobbies has no per-host IP). ===
        var pingCell = BuildPingCell(_connectionPingMs >= 0 ? _connectionPingMs : (double?)null);
        pingCell.ToolTip = Strings.Get("MpRoomPingTooltip");
        _roomPingCells.Add(pingCell);
        Grid.SetColumn(pingCell, 4);
        grid.Children.Add(pingCell);

        // === Col 5: STATUS — colored dot + label. Waiting (blue) / In Game
        // (green) / Full (amber). ===
        // Priority: In Game > Full > Private > Waiting. Private is a purple dot
        // (mockup look), but the room is still JOINABLE — the ACTION below stays
        // an enabled Join (password prompt on click), it is NOT a hard "Locked".
        var statusKind = inGame ? RoomStatusKind.InGame
            : (isFull ? RoomStatusKind.Full
            : (lobby.IsPrivate ? RoomStatusKind.Locked : RoomStatusKind.Waiting));
        var statusLabel = inGame ? Strings.Get("MpRoomStatusInGame")
            : (isFull ? Strings.Get("MpRoomFull")
            : (lobby.IsPrivate ? Strings.Get("MpRoomStatusLocked") : Strings.Get("MpRoomStatusWaiting")));
        var statusCell = BuildStatusCell(statusLabel, statusKind);
        Grid.SetColumn(statusCell, 5);
        grid.Children.Add(statusCell);

        // === Col 6: ACTION — gold-outline button. SAME priority logic: in this
        // room → Re-enter; our own room → "Your room" (disabled); in game →
        // disabled; full → disabled; mod not installed → disabled Join; else →
        // enabled Join. Enabled Join / Re-enter are the gold outline; disabled
        // states fall back to the neutral secondary style. ===
        var iAmInThisRoom = string.Equals(lobby.Id, _session?.CurrentLobbyId, StringComparison.Ordinal);
        var iAmHost = me != null && (
            (!string.IsNullOrEmpty(lobby.Host.Id)
                && string.Equals(lobby.Host.Id, me.Id, StringComparison.Ordinal))
            || (!string.IsNullOrEmpty(lobby.Host.DiscordUsername)
                && string.Equals(lobby.Host.DiscordUsername, me.DiscordUsername, StringComparison.OrdinalIgnoreCase)));

        var actionBtn = new Button
        {
            // Compact + centred so the button stays button-sized (not stretched)
            // now that the ACTION column has no MaxWidth and can grow wide.
            HorizontalAlignment = HorizontalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 96,
            MaxWidth = 130,
            Padding = new Thickness(10, 4, 10, 4),
            Tag = lobby,
        };
        var outline = (Style)Application.Current.FindResource("MpOutlineBlueButton");
        var secondary = (Style)Application.Current.FindResource("MpSecondaryButton");
        if (iAmInThisRoom)
        {
            actionBtn.Style = outline;
            actionBtn.Content = Strings.Get("MpRoomReenter");
            actionBtn.Click += (_, _) => OpenLobbyWindow();
        }
        else if (iAmHost)
        {
            actionBtn.Style = secondary;
            actionBtn.Content = Strings.Get("MpRoomYours");
            actionBtn.IsEnabled = false;
        }
        else if (inGame)
        {
            actionBtn.Style = secondary;
            actionBtn.Content = Strings.Get("MpRoomStatusInGame");
            actionBtn.IsEnabled = false;
        }
        else if (isFull)
        {
            actionBtn.Style = secondary;
            actionBtn.Content = Strings.Get("MpRoomFull");
            actionBtn.IsEnabled = false;
        }
        else
        {
            actionBtn.Style = outline;
            actionBtn.Content = Strings.Get("MpRoomJoin");
            actionBtn.IsEnabled = modInstalled;
            actionBtn.Click += JoinRoomButton_Click;
        }
        Grid.SetColumn(actionBtn, 6);
        grid.Children.Add(actionBtn);

        card.Child = grid;
        return card;
    }

    /// <summary>Small rounded badge for a room row (the MOD name). Bordered so
    /// it stays legible over the row's hover fill.</summary>
    private Border BuildRoomChip(string text, Brush bg, Brush fg) => new Border
    {
        Background = bg,
        BorderBrush = (Brush)Application.Current.FindResource("MpModBadgeBorder"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(8, 2, 8, 2),
        Margin = new Thickness(0, 0, 6, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock
        {
            Text = text,
            Foreground = fg,
            FontSize = (double)Application.Current.FindResource("FontSizeCaption"),
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        },
    };

    /// <summary>
    /// Per-mod-id cache of the resolved rooms-browser icon brush, so a quiet
    /// list refresh (every 10 s) doesn't re-decode the same icon each tick.
    /// Only successful brushes are cached — a mod whose catalog icon hasn't
    /// been fetched yet (LocalIconPath still null) is retried on the next
    /// render so a late-arriving icon still shows.
    /// </summary>
    private readonly Dictionary<string, ImageBrush> _roomModIconCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolve a room's mod icon (cached catalog icon.png → live remote URL →
    /// built-in packed icon, via <see cref="ModProfile.ResolveIconSource"/>)
    /// to a UniformToFill brush for the card's leading disc, or null when the
    /// mod ships no resolvable icon (caller falls back to ★). A room for a mod
    /// the user hasn't installed paints its icon straight from the catalog URL
    /// — nothing is written to the mod-asset disk cache for it.
    /// Mirrors <c>CreateLobbyDialog.LoadIconBrush</c>.
    /// </summary>
    private ImageBrush? ResolveRoomModIcon(ModProfile? profile)
    {
        if (profile == null) return null;
        if (_roomModIconCache.TryGetValue(profile.Id, out var cached)) return cached;

        string? uri = profile.ResolveIconSource();
        if (string.IsNullOrEmpty(uri)) return null;

        try
        {
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 48; // disc is 24 logical px; cap the decoded copy
            bmp.UriSource = new Uri(uri, UriKind.Absolute);
            bmp.EndInit();
            // A remote icon is still downloading here: it can't be frozen yet
            // (unconditional Freeze throws) and, left unfrozen, the brush
            // repaints itself when the download completes. Evict the memo on a
            // failed download so the next quiet refresh retries.
            if (bmp.IsDownloading)
            {
                var modId = profile.Id;
                bmp.DownloadFailed += (_, _) => _roomModIconCache.Remove(modId);
            }
            if (bmp.CanFreeze) bmp.Freeze();
            var brush = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
            if (brush.CanFreeze) brush.Freeze();
            _roomModIconCache[profile.Id] = brush;
            return brush;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Refresh the "last updated" label in the rooms header from
    /// <see cref="_lastRoomsRenderedAt"/>. Called after each successful
    /// /lobbies fetch and ticked by the rooms ping timer so the relative
    /// time stays current ("hace 10 s").
    /// </summary>
    private DateTime _lastRoomsRenderedAt = DateTime.MinValue;

    private void UpdateRoomsUpdatedLabel()
    {
        if (RoomsUpdatedText == null) return;
        if (_lastRoomsRenderedAt == DateTime.MinValue)
        {
            RoomsUpdatedText.Text = "";
            return;
        }
        var secs = (int)(DateTime.Now - _lastRoomsRenderedAt).TotalSeconds;
        RoomsUpdatedText.Text = secs < 5
            ? Strings.Get("MpRoomsUpdatedNow")
            : secs < 60
                ? Strings.Format("MpRoomsUpdatedSecs", secs)
                : Strings.Format("MpRoomsUpdatedMins", secs / 60);
    }

    /// <summary>
    /// Render the Ping column for a row. <paramref name="rttMs"/>
    /// null = no value yet (em-dash + muted); otherwise a small
    /// "signal bars" glyph coloured by RTT bucket plus the number.
    /// </summary>
    private StackPanel BuildPingCell(double? rttMs)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        FillPingCell(panel, rttMs);
        return panel;
    }

    /// <summary>
    /// (Re)populate a ping cell. Split out from <see cref="BuildPingCell"/>
    /// so <see cref="RefreshRoomPingCells"/> can refresh the rooms-browser
    /// cells in place without rebuilding rows (a rebuild would disrupt the
    /// Join button mid-hover/click).
    /// </summary>
    private void FillPingCell(StackPanel panel, double? rttMs)
    {
        panel.Children.Clear();
        if (rttMs is null)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "—",
                Foreground = (Brush)Application.Current.FindResource("TextSecondary"),
                FontSize = (double)Application.Current.FindResource("FontSizeBody"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            return;
        }

        var rtt = rttMs.Value;
        var brush = rtt < 80
            ? (Brush)Application.Current.FindResource("MpPingGood")
            : rtt < 200
                ? (Brush)Application.Current.FindResource("MpPingMedium")
                : (Brush)Application.Current.FindResource("MpPingBad");

        panel.Children.Add(new TextBlock
        {
            Text = "▂▄▆ ",
            Foreground = brush,
            FontSize = (double)Application.Current.FindResource("FontSizeCaption"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"{(int)rtt} ms",
            Foreground = (Brush)Application.Current.FindResource("TextPrimary"),
            FontSize = (double)Application.Current.FindResource("FontSizeBody"),
            VerticalAlignment = VerticalAlignment.Center,
        });
    }

    /// <summary>
    /// Refresh the rooms-browser PING cells in place from the cached
    /// <see cref="_connectionPingMs"/> — your internet latency, the same
    /// value for every row because the launcher can't ping each host
    /// individually (no per-host Radmin IP).
    /// </summary>
    private void RefreshRoomPingCells()
    {
        double? p = _connectionPingMs >= 0 ? _connectionPingMs : (double?)null;
        foreach (var cell in _roomPingCells)
            FillPingCell(cell, p);
    }

    /// <summary>Which status a room row is showing (drives the dot colour).</summary>
    private enum RoomStatusKind { Waiting, InGame, Full, Locked }

    /// <summary>
    /// Status cell: coloured dot + label. Waiting = blue dot, In Game = green
    /// dot (bold green label), Full = amber dot.
    /// </summary>
    private FrameworkElement BuildStatusCell(string label, RoomStatusKind kind)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var dotKey = kind switch
        {
            RoomStatusKind.InGame => "MpStatusInGame",
            RoomStatusKind.Full => "MpStatusFull",
            RoomStatusKind.Locked => "MpStatusLocked",
            _ => "MpStatusWaiting",
        };
        panel.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 8, Height = 8,
            Fill = (Brush)Application.Current.FindResource(dotKey),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0),
        });
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = (Brush)Application.Current.FindResource(
                kind == RoomStatusKind.InGame ? "MpStatusInGame" : "TextPrimary"),
            FontSize = (double)Application.Current.FindResource("FontSizeBody"),
            FontWeight = kind == RoomStatusKind.InGame ? FontWeights.SemiBold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        return panel;
    }

    /// <summary>
    /// A stable, name-derived colour for a host's monogram disc (like the
    /// reference mockup's coloured initials). Same name → same colour.
    /// </summary>
    private static readonly string[] _hostMonogramColors =
    {
        "#3F6FAE", "#4FA66A", "#B5794A", "#8E6FB5",
        "#B85A6A", "#4A9DB5", "#B59A4A", "#6A8E5A",
    };

    private static Brush HostMonogramBrush(string name)
    {
        int h = 0;
        foreach (char c in name ?? "") h = unchecked(h * 31 + c) & 0x7fffffff;
        var hex = _hostMonogramColors[h % _hostMonogramColors.Length];
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        if (brush.CanFreeze) brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Quick local check: do we have a saved install path for the
    /// given mod id? Used to grey out rooms whose mod isn't installed
    /// on this PC. We don't probe the actual files — the saved path
    /// in LauncherConfig is already gated by an on-disk probe at
    /// install time, so it's a safe proxy.
    /// </summary>
    private bool IsModInstalledLocally(string modId)
    {
        try
        {
            var cfg = WarsOfLibertyLauncher.Models.LauncherConfig.Load();
            var state = cfg.GetState(modId);
            if (!string.IsNullOrEmpty(state.InstallPath)) return true;

            // The stock Age of Empires III profile is never installed through
            // the launcher, so it has no saved path. Detect the base game on
            // disk instead so stock rooms aren't greyed out or blocked at join.
            var profile = ModRegistry.Find(modId);
            if (profile is { IsStockGame: true })
                return !string.IsNullOrEmpty(AoE3Detector.FindInstallRoot());

            return false;
        }
        catch
        {
            return false;
        }
    }

    private async void JoinRoomButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LobbySummary lobby) return;
        btn.IsEnabled = false;
        try { await JoinLobbyCoreAsync(lobby); }
        finally { btn.IsEnabled = true; }
    }

    /// <summary>
    /// Runs the full join flow for an already-resolved <paramref name="lobby"/>:
    /// mod-match / install gate (auto-switching the active mod when the room's mod
    /// is installed locally), fingerprint, password prompt for private rooms, then
    /// the session join. Shared by the Join button and the deep-link auto-join
    /// (<see cref="JoinByLobbyIdAsync"/>). Assumes the caller already ensured
    /// sign-in; all failures surface as in-tab notices.
    /// </summary>
    private async Task JoinLobbyCoreAsync(LobbySummary lobby)
    {
        if (_session == null || _getActiveProfile == null || _computeModFingerprint == null) return;

        var profile = _getActiveProfile();
        if (profile == null)
        {
            SignInErrorText.Text = Strings.Get("MpModNotInstalled");
            SignInErrorText.Visibility = Visibility.Visible;
            return;
        }

        // Profile-vs-room mod resolution. Three cases:
        //   1. Active profile == lobby.ModId      → proceed.
        //   2. Active profile != lobby.ModId but
        //      the room's mod IS installed locally → auto-switch
        //      to it (silently, no popup) and proceed.
        //   3. Active profile != lobby.ModId AND
        //      the room's mod is NOT installed    → tell the user
        //      they need to install it first.
        //
        // Path #2 replaces the older "Wrong mod active" popup that
        // told the user to manually go switch the mod — a
        // frustrating UX since the launcher knows the right mod
        // already.
        if (!string.Equals(profile.Id, lobby.ModId, StringComparison.OrdinalIgnoreCase))
        {
            if (!IsModInstalledLocally(lobby.ModId))
            {
                // Resolve a friendly display name for the message.
                string displayName = lobby.ModId;
                foreach (var p in ModRegistry.All)
                {
                    if (string.Equals(p.Id, lobby.ModId, StringComparison.OrdinalIgnoreCase))
                    {
                        displayName = p.DisplayName;
                        break;
                    }
                }
                await MpAlertOverlay.NoticeAsync(
                    TabRootGrid,
                    Strings.Get("MpNoticeRoomModMissingTitle"),
                    Strings.Format("MpNoticeRoomModMissingBody", displayName),
                    Strings.Get("MpAlertOk"));
                return;
            }

            // Find the target profile in the registry.
            ModProfile? target = null;
            foreach (var p in ModRegistry.All)
            {
                if (string.Equals(p.Id, lobby.ModId, StringComparison.OrdinalIgnoreCase))
                {
                    target = p;
                    break;
                }
            }
            if (target == null)
            {
                await MpAlertOverlay.NoticeAsync(
                    TabRootGrid,
                    Strings.Get("MpNoticeUnknownModTitle"),
                    Strings.Format("MpNoticeUnknownModBody", lobby.ModId),
                    Strings.Get("MpAlertOk"));
                return;
            }

            // Ask MainWindow to switch the active profile. It runs
            // the same path the Play-tab tiles use (LoadModProfile),
            // including the busy-state pre-flight (in-progress
            // install / game running blocks the switch).
            if (_switchActiveMod == null || !_switchActiveMod(target))
            {
                await MpAlertOverlay.NoticeAsync(
                    TabRootGrid,
                    Strings.Get("MpNoticeSwitchFailedTitle"),
                    Strings.Format("MpNoticeSwitchFailedBody", target.DisplayName),
                    Strings.Get("MpAlertOk"));
                return;
            }
            // Use the new profile from here on. The active-profile
            // getter would also return it now, but reading the
            // local variable is faster and avoids an extra ref-eq.
            profile = target;
            DiagnosticLog.Write($"JoinRoom: auto-switched active mod to '{target.Id}' to match lobby '{lobby.Id}'");
        }

        string fingerprint;
        try
        {
            fingerprint = await _computeModFingerprint(profile);
        }
        catch (Exception ex)
        {
            await MpAlertOverlay.NoticeAsync(
                TabRootGrid,
                Strings.Get("MpNoticeFingerprintTitle"),
                ex.Message,
                Strings.Get("MpAlertOk"));
            return;
        }

        string? password = null;
        if (lobby.IsPrivate)
        {
            var prompt = new PasswordPromptDialog("This room is password-protected. Enter the password:")
            {
                Owner = Window.GetWindow(this),
            };
            if (prompt.ShowDialog() != true || string.IsNullOrEmpty(prompt.EnteredPassword)) return;
            password = prompt.EnteredPassword;
        }

        try
        {
            // Stamp the room's mod id so LaunchActiveModGame uses
            // the right profile when the host starts the game — see
            // the same step in the create-room path above.
            _currentLobbyModId = lobby.ModId;
            _currentLobbyMaxPlayers = lobby.MaxPlayers;
            // Host vs joiner is decided by the WS room_state frame that
            // arrives once we connect — clearing it here is just for the
            // brief window before that frame lands.
            // Pass the title from the browser summary so the in-room
            // header reads the real room name immediately, not the id.
            await _session.JoinLobbyAsync(lobby.Id, fingerprint, password, lobby.Title);
        }
        catch (LobbyApiException ex) when (ex.Code == "mod_mismatch")
        {
            await MpAlertOverlay.NoticeAsync(
                TabRootGrid,
                Strings.Get("MpNoticeMismatchTitle"),
                Strings.Get("MpNoticeMismatchBody"),
                Strings.Get("MpAlertOk"));
        }
        catch (Exception ex)
        {
            await MpAlertOverlay.NoticeAsync(
                TabRootGrid,
                Strings.Get("MpNoticeJoinFailedTitle"),
                ex.Message,
                Strings.Get("MpAlertOk"));
        }
    }

    /// <summary>
    /// Deep-link auto-join: join a room by its id (from a Discord "Join" link).
    /// Ensures the user is signed in (opening the sign-in dialog if needed),
    /// resolves the <see cref="LobbySummary"/> from the live list (there is no
    /// get-by-id API), navigates to Rooms, and runs the shared join flow. If we're
    /// already in that room it just re-opens the lobby window. All failures surface
    /// as in-tab notices.
    /// </summary>
    public async Task JoinByLobbyIdAsync(string lobbyId)
    {
        if (_session == null || string.IsNullOrWhiteSpace(lobbyId)) return;
        DiagnosticLog.Write($"DeepLink: auto-join requested for lobby '{lobbyId}'.");

        // 1. Ensure signed in (reuse the same modal the Sign in button opens).
        if (_session.Status != MultiplayerSession.SessionStatus.SignedIn)
        {
            try
            {
                var dlg = new GitHubLoginDialog(_session) { Owner = Window.GetWindow(this) };
                dlg.ShowDialog();
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"DeepLink: sign-in dialog failed: {ex.Message}");
            }
            if (_session.Status != MultiplayerSession.SessionStatus.SignedIn)
            {
                await MpAlertOverlay.NoticeAsync(
                    TabRootGrid,
                    Strings.Get("MpDeepLinkSignInTitle"),
                    Strings.Get("MpDeepLinkSignInBody"),
                    Strings.Get("MpAlertOk"));
                return;
            }
        }

        ShowRooms();

        // 2. Already in this room? Just bring its window up.
        if (string.Equals(_session.CurrentLobbyId, lobbyId, StringComparison.OrdinalIgnoreCase))
        {
            OpenLobbyWindow();
            return;
        }

        // 3. Resolve the LobbySummary from the live list (no get-by-id endpoint).
        LobbySummary? lobby = null;
        try
        {
            var list = await _session.Api.ListLobbiesAsync();
            foreach (var l in list.Lobbies)
            {
                if (string.Equals(l.Id, lobbyId, StringComparison.OrdinalIgnoreCase)) { lobby = l; break; }
            }
        }
        catch (Exception ex)
        {
            await MpAlertOverlay.NoticeAsync(
                TabRootGrid,
                Strings.Get("MpDeepLinkFailedTitle"),
                ex.Message,
                Strings.Get("MpAlertOk"));
            return;
        }

        if (lobby == null)
        {
            await MpAlertOverlay.NoticeAsync(
                TabRootGrid,
                Strings.Get("MpDeepLinkNotFoundTitle"),
                Strings.Get("MpDeepLinkNotFoundBody"),
                Strings.Get("MpAlertOk"));
            return;
        }

        await JoinLobbyCoreAsync(lobby);
    }

    // ---------- In-room actions ----------

    private async void ReadyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session?.CurrentUser == null) return;

        // Toggle locally first so the UI gives instant feedback even
        // if the WS is mid-reconnect. The server-side member_ready
        // frame from the next room_state will reconcile if anything
        // drifted. Without this the button felt dead during the
        // brief WS hiccups caused by quick-tunnel idle disconnects.
        var meId = _session.CurrentUser.Id;
        var ready = !(_roomMembers.TryGetValue(meId, out var prev) && prev.Ready);
        if (_roomMembers.TryGetValue(meId, out var entry))
            entry.Ready = ready;
        RenderRoomMembers();

        if (_session.RoomSocket == null)
        {
            AppendChatSystem(Strings.Get("MpChatReadySavedLocally"));
            return;
        }

        try { await _session.RoomSocket.SendReadyAsync(ready); }
        catch (Exception ex) { DiagnosticLog.Write($"MultiplayerTab.Ready: {ex.Message}"); }
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null) return;

        // The same button is the red "Cancel" during the countdown (for
        // host AND joiner — see ApplyMatchPhaseUi). Route the click to the
        // abort path instead of (re)starting a game.
        if (_matchPhase == MatchPhase.Starting)
        {
            CancelCountdownByUser();
            return;
        }

        // Host-side semantics:
        //   1. Tell the Worker to start the game. The Worker will
        //      broadcast `game_countdown` back to every member,
        //      including us. The countdown handler in OnRoomFrame
        //      runs the local 3-second timer and launches AoE3 at
        //      the end — same path for host AND joiners, so the
        //      pre-game UX is symmetric.
        //   2. If the WS is dead (rare — tunnel idle drop, network
        //      blink) we won't get a server echo back. After a
        //      short grace window we start the countdown locally so
        //      the host can still launch a solo session for testing.
        //
        // This replaces the older "launch AoE3 immediately and just
        // signal peers" path, which made the host bypass the
        // 3-second countdown entirely (countdown overlay never
        // showed, AoE3 spawned on Start press instantly).
        AppendChatSystem(Strings.Get("MpChatStartingGame"));

        if (_session.RoomSocket != null)
        {
            try
            {
                await _session.RoomSocket.SendStartAsync();
                // Grace fallback: if the server's game_countdown
                // hasn't landed in 2 s (e.g. WS dropped right after
                // our send), kick off a local countdown so the host
                // isn't left frozen with nothing happening. The
                // countdown handler is idempotent — if the server
                // frame still arrives later, the duplicate
                // StartCountdown is a no-op (phase already Starting).
                _ = Dispatcher.InvokeAsync(async () =>
                {
                    await Task.Delay(2000);
                    if (_matchPhase == MatchPhase.Lobby)
                    {
                        DiagnosticLog.Write("MultiplayerTab.Start: server didn't echo countdown in 2s, " +
                            "starting local fallback countdown");
                        StartCountdown(10000);
                    }
                });
                return;
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"MultiplayerTab.Start (notify peers): {ex.Message}");
                // Fall through to the offline-host path below.
            }
        }
        else
        {
            DiagnosticLog.Write("MultiplayerTab.Start: WS down — peers will pick up via room_state on reconnect");
        }
        // WS unavailable / SendStart threw: still kick off the
        // local countdown so the host can launch solo. Peers won't
        // hear about it but a single-player test session works.
        StartCountdown(10000);
    }

    private async void LeaveRoomButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null) return;
        try { await _session.LeaveCurrentLobbyAsync(); }
        catch (Exception ex) { DiagnosticLog.Write($"MultiplayerTab.Leave: {ex.Message}"); }
        finally
        {
            await RefreshRoomsListAsync();
        }
    }

    private async void ChatSendButton_Click(object sender, RoutedEventArgs e) =>
        await SendChatAsync();

    private async void ChatInputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await SendChatAsync();
    }

    private async Task SendChatAsync()
    {
        if (_session?.CurrentUser == null) return;
        if (_lobbyWindow == null) return;
        var text = _lobbyWindow!.ChatInputBox.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;
        _lobbyWindow!.ChatInputBox.Text = "";

        var login = string.IsNullOrEmpty(_session.CurrentUser.DiscordUsername)
            ? _session.CurrentUser.DisplayName
            : _session.CurrentUser.DiscordUsername;

        if (_session.RoomSocket == null)
        {
            // Offline echo so the user still gets visual feedback. The
            // line is local-only — the server never sees it. Marker
            // makes that obvious.
            AppendChatLine(new WsPeerlessChatLine($"{login} (pending): {text}"));
            return;
        }

        // Optimistic local echo first so the message appears the very
        // moment the user presses Enter — independent of WS round-trip
        // latency. The matching server broadcast that lands a few
        // hundred ms later is suppressed by AppendChatLine via the
        // _recentLocalEchoes registry below, so no double-up.
        AppendChatRow(
            timestamp: DateTime.Now,
            isSystem: false,
            authorLogin: login,
            authorUserId: _session.CurrentUser.Id,
            body: text,
            severity: ChatSeverity.Info);
        _recentLocalEchoes.Add((text, Environment.TickCount64));

        try { await _session.RoomSocket.SendChatAsync(text); }
        catch (Exception ex) { DiagnosticLog.Write($"MultiplayerTab.Chat: {ex.Message}"); }
    }

    /// <summary>Tiny wrapper to render a non-server chat line via the
    /// same path AppendChatLine uses for real ones.</summary>
    private sealed class WsPeerlessChatLine : WsChatLine
    {
        public WsPeerlessChatLine(string body)
        {
            Login = "system";
            Body = body;
            AtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }

    /// <summary>
    /// Launch the active mod's executable when a <c>game_started</c>
    /// frame arrives. Every member of the room sees the same frame and
    /// fires this — that's intentional: each player launches AoE3 on
    /// their own machine, then AoE3's own LAN code (broadcasting on
    /// the ZeroTier network) discovers the other peers.
    ///
    /// We use the launch callback MainWindow injected via Attach so
    /// this control doesn't need direct access to LauncherConfig. The
    /// callback returns the started Process; we subscribe to Exited so
    /// the post-game flow (replay upload, match reporting) can run
    /// without spinning up a watcher thread of our own.
    /// </summary>
    private System.Diagnostics.Process? LaunchActiveModGame()
    {
        if (_launchGame == null || _getActiveProfile == null) return null;

        // Pick the profile to launch from the ROOM, not the Play
        // tab's currently-active mod. The room carries its own
        // mod_id (chosen by the host at create time); launching
        // whatever happens to be selected on the Play tab is wrong
        // — it'd open AoE3 from a different mod's folder and the
        // peer's mod fingerprint check would reject the session.
        //
        // Source of the mod id, in priority order:
        //   1. _currentLobbyModId — stamped at create / join time,
        //      so it works even for brand-new rooms that aren't in
        //      the browser snapshot yet.
        //   2. The cached browser snapshot (_lastBrowserList) —
        //      backup for cases where the user pre-existed the
        //      current session somehow.
        //   3. The active profile from the Play tab — last-resort
        //      defensive fallback so the launcher never throws.
        ModProfile? profile = null;
        var lobbyId = _session?.CurrentLobbyId;
        if (!string.IsNullOrEmpty(_currentLobbyModId))
        {
            foreach (var candidate in ModRegistry.All)
            {
                if (string.Equals(candidate.Id, _currentLobbyModId, StringComparison.OrdinalIgnoreCase))
                {
                    profile = candidate;
                    break;
                }
            }
        }
        if (profile == null && !string.IsNullOrEmpty(lobbyId) && _lastBrowserList != null)
        {
            foreach (var l in _lastBrowserList)
            {
                if (!string.Equals(l.Id, lobbyId, StringComparison.Ordinal)) continue;
                foreach (var candidate in ModRegistry.All)
                {
                    if (string.Equals(candidate.Id, l.ModId, StringComparison.OrdinalIgnoreCase))
                    {
                        profile = candidate;
                        break;
                    }
                }
                break;
            }
        }
        if (profile == null)
        {
            profile = _getActiveProfile();
        }
        if (profile == null)
        {
            AppendChatSystem(Strings.Get("MpChatCannotLaunchNoProfile"));
            return null;
        }
        DiagnosticLog.Write($"MultiplayerTab.LaunchActiveModGame: launching profile '{profile.Id}' ({profile.DisplayName}) for lobby '{lobbyId}'");

        try
        {
            var extraArgs = BuildMultiplayerLaunchArgs();
            DiagnosticLog.Write($"MultiplayerTab.LaunchActiveModGame: extraArgs='{extraArgs}'");

            // With n2n in place, this method used to bring up a Detours-
            // based hook DLL inside age3y.exe and a named-pipe bridge
            // that forwarded each WinSock send/recv across PeerMesh.
            // None of that exists anymore — every peer in the room is
            // already on the same 10.99.0.0/24 virtual LAN via the
            // edge.exe process the session spun up at join time, so
            // AoE3's stock LAN multiplayer code just works.

            var gameStartedAt = DateTime.UtcNow;
            var process = _launchGame(profile, async (_, _) =>
            {
                // Run on the UI thread so we can render chat messages
                // and access session state safely.
                await Dispatcher.InvokeAsync(async () =>
                {
                    // The OS-side "game closed" path: exit InGame and
                    // run the post-match flow. If the user cancels
                    // via the popup, ExitInGamePhase has already
                    // fired — calling it again is a no-op.
                    if (_matchPhase == MatchPhase.InGame) ExitInGamePhase();
                    await OnGameExitedAsync(profile, gameStartedAt);
                });
            }, extraArgs);


            if (process == null)
            {
                AppendChatSystem(Strings.Get("MpChatCouldNotSpawn"));
                return null;
            }

            // n2n virtual-LAN flow: every peer's edge.exe presents the
            // room as a real LAN segment on 10.99.0.0/24, so both host
            // and joiner just walk through AoE3's stock LAN UI — no
            // virtual IPs to copy, no Direct IP textbox to paste into.
            AppendChatSystem(Strings.Get("MpChatGameLaunched"));
            return process;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"MultiplayerTab.LaunchActiveModGame: {ex.Message}");
            AppendChatSystem(Strings.Format("MpChatLaunchFailed", ex.Message));
        }
        return null;
    }

    /// <summary>
    /// Builds the AoE3 command-line tail for the current room context.
    /// All flag names here were verified against age3y.exe's string
    /// table (Wars of Liberty 1.2.0c2). Confirmed flags (with the
    /// descriptive text the engine prints when it lists switches):
    ///   * <c>+noIntroCinematics</c> — "suppresses intro cinematics on app start"
    ///   * <c>+disableESOProfile</c> — "toggles the use of ESO for storing the player profile"
    ///   * <c>+dontDetectNAT</c>     — "Doth we not detect NAT addresses?"
    ///
    /// AoE3 has NO command-line flag to auto-host or auto-join a LAN
    /// game (we searched for hostmpgame / joinIPaddr / joinmpgame /
    /// jumpTo etc. — none exist), so the player still has to click
    /// "Multiplayer → LAN" once after the game opens. The launcher
    /// cuts every other startup delay it can.
    /// </summary>
    private string BuildMultiplayerLaunchArgs()
    {
        // The intro / ESO / NAT skips are always safe to apply: they
        // just kill the splash + the long "connecting to ESO" wait.
        var sb = new System.Text.StringBuilder();
        sb.Append("+noIntroCinematics +disableESOProfile +dontDetectNAT");

        // Bind AoE3's DirectPlay LAN discovery to the Radmin VPN
        // adapter when it's up. Without this, AoE3 broadcasts on the
        // physical wifi NIC and peers on different networks can't see
        // each other's lobbies — works for two PCs on the same wifi,
        // breaks the moment one switches to mobile data or another
        // network. The 26.x.x.x address belongs to Radmin's virtual
        // /8 overlay and is reachable from every Radmin peer
        // regardless of physical location.
        //
        // The community tutorial that goes around the AoE3 forums
        // tells users to add `OverrideAddress="<your radmin ip>"` to
        // the launch line by hand; this just does it automatically.
        // When Radmin isn't running, we omit the flag entirely so
        // local-LAN play (e.g. two laptops on the same router with no
        // Radmin) keeps working unmodified.
        //
        // Syntax — DO NOT "fix" this back to `+OverrideAddress`. This has
        // flip-flopped twice already and the `+` form is the BROKEN one,
        // confirmed by a real in-game capture (the lobby's "Dirección IP"
        // showed 192.168.56.1 — a VirtualBox host-only adapter — instead of
        // the 26.x Radmin IP). The form age3y.exe actually honours is the
        // community tutorial's literal `OverrideAddress="<ip>"`: NO `+`
        // prefix, an `=` assignment, and the IP in double quotes. The
        // string reaches the game's command line verbatim (single Arguments
        // string, UseShellExecute=false), exactly like the shortcut Target.
        //   * `+OverrideAddress <ip>` is silently ignored — OverrideAddress
        //     is NOT a `+`-prefixed console cvar (those are a different
        //     mechanism), so the engine drops it and auto-picks whatever
        //     adapter IP it finds first (the VirtualBox NIC, here).
        //   * The skip-intro switches above (`+noIntroCinematics`, etc.)
        //     legitimately ARE `+` cvars and stay prefixed — they work.
        var radmin = RadminVpnService.GetStatus();
        if (radmin.IsServiceRunning && !string.IsNullOrEmpty(radmin.AdapterIp))
        {
            sb.Append(" OverrideAddress=\"").Append(radmin.AdapterIp).Append('"');
        }

        return sb.ToString();
    }

    /// <summary>
    /// Called when the AoE3 process spawned by <see cref="LaunchActiveModGame"/>
    /// exits. Best-effort post-game flow: find the freshest
    /// <c>.age3yrec</c> in the mod's user-data folder and surface it
    /// in the chat. Full upload + match reporting requires per-player
    /// result data that we don't auto-extract for v1.0 — that polish
    /// pass comes once AoE3 result parsing is in scope.
    /// </summary>
    private async Task OnGameExitedAsync(ModProfile profile, DateTime gameStartedAtUtc)
    {
        AppendChatSystem(Strings.Get("MpChatGameClosed"));
        try
        {
            // The mod's user-data folder lives under Documents/My Games/<userDataFolder>.
            // Resolved by the central helper so it honours the dual-root rule
            // (redirected OneDrive Documents vs the physical folder).
            var modUserData = UserDataService.GetUserDataFolder(profile.UserDataFolder);

            if (string.IsNullOrEmpty(modUserData)) return;

            var replay = ReplayUploadService.FindLatestReplay(modUserData, gameStartedAtUtc);
            if (replay != null)
                AppendChatSystem(Strings.Format("MpChatReplaySaved", replay.Name, replay.Length / 1024));
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"MultiplayerTab.OnGameExitedAsync: {ex.Message}");
        }
        await Task.CompletedTask;
    }

    // ==================================================================
    // Lobby window lifecycle (single-instance open/close)
    // ==================================================================
    //
    // The whole "floating popup drag + resize + Canvas position" block
    // that used to live here is gone — the lobby is now a real
    // top-level Window (LobbyWindow.xaml) with native OS chrome that
    // handles drag, resize and edge clamping for free. What's left is
    // just the open/close lifecycle:
    //
    //   • OpenLobbyWindow() is idempotent. If a window already exists,
    //     Activate()s it (so a duplicate Create/Join click brings the
    //     existing one to front instead of spawning a second). The
    //     callbacks point each click handler back to the methods that
    //     used to be wired via XAML Click="…" — the logic itself
    //     stayed in this class for now (close coupling with
    //     MultiplayerSession state, telemetry, etc.); the Window is
    //     a thin forwarder.
    //   • CloseLobbyWindow() is idempotent. The Closed event handler
    //     fires HandleLobbyWindowClosed which nulls the field and (if
    //     we're still in a session-tracked room) triggers the
    //     leave-room flow — same single rendezvous point regardless
    //     of how the user dismissed (✕ / Esc / Alt+F4 / our own Close).

    private void OpenLobbyWindow()
    {
        if (_lobbyWindow != null)
        {
            _lobbyWindow.Activate();
            return;
        }
        if (_session == null) return;

        var w = new LobbyWindow(_session)
        {
            // No Owner: the lobby is an INDEPENDENT top-level window with its
            // own Windows taskbar button (ShowInTaskbar=True). Minimizing the
            // launcher doesn't hide it, and it isn't pinned above the launcher
            // — the user can alt-tab / move it to another monitor freely.

            // Click forwarders. The handler bodies stayed in this
            // class (where the Multiplayer state lives); LobbyWindow's
            // XAML buttons fire Action callbacks instead of using
            // XAML Click="…" wires.
            OnLeaveRoom = () => LeaveRoomButton_Click(this, new RoutedEventArgs()),
            OnReady = () => ReadyButton_Click(this, new RoutedEventArgs()),
            OnStart = () => StartButton_Click(this, new RoutedEventArgs()),
            OnInGameCancel = () => InGameCancelButton_Click(this, new RoutedEventArgs()),
            OnClearChat = () => ClearChatButton_Click(this, new RoutedEventArgs()),
            OnSendChat = () => ChatSendButton_Click(this, new RoutedEventArgs()),
            OnEmoji = () => ChatEmojiButton_Click(this, new RoutedEventArgs()),
            // The existing TextChanged / KeyDown handlers take WPF
            // routed event args we don't construct here — call them
            // through with a synthetic args object (the args aren't
            // read by the handler bodies, only Key on KeyDown).
            OnChatTextChanged = () => ChatInputBox_TextChanged(this, null!),
            OnChatKeyDown = e => ChatInputBox_KeyDown(this, e),
        };

        _lobbyWindow = w;

        // Localise the static labels and paint the current room state
        // before Show() so there's no English/empty flash on open.
        ApplyLobbyStaticLabels();
        RenderRoomPanel();
        UpdateChatEmptyState();

        // Poll the connection ping while the lobby is open so the header's
        // CONNECTION stat stays live even before a match starts. ~2.5 s
        // cadence; KickConnectionPing guards against overlapping probes.
        _lobbyPingTimer?.Stop();
        _lobbyPingTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(2500),
        };
        _lobbyPingTimer.Tick += (_, _) =>
        {
            KickConnectionPing();
            UpdateLobbyPing();
            // Also probe peers pre-match so the roster health dots are live before
            // anyone hits Start (needs our own IP reported first).
            MaybeReportRadminIp();
            KickPeerPings();
            RefreshRosterHealthDots();
        };
        _lobbyPingTimer.Start();
        KickConnectionPing();
        UpdateLobbyPing();

        // Race-safe field clear in Closed: a follow-up OpenLobbyWindow
        // call between Close() and Closed firing must not clobber the
        // new instance, so we only null the field if it still points
        // at THIS window.
        w.Closed += (_, _) =>
        {
            if (ReferenceEquals(_lobbyWindow, w))
                _lobbyWindow = null;
            HandleLobbyWindowClosed();
        };

        w.Show();
    }

    private void CloseLobbyWindow()
    {
        if (_lobbyWindow == null) return;
        var stale = _lobbyWindow;
        // Null the field FIRST so any in-flight render that races
        // with Close() sees "no window" instead of a half-disposed
        // one. The Closed handler's ReferenceEquals guard makes the
        // null-then-Close ordering safe.
        _lobbyWindow = null;
        stale.Close();
    }

    /// <summary>
    /// Single rendezvous point for "lobby window dismissed". Runs on
    /// the Closed event for the ✕, Esc, Alt+F4, OS chrome close, AND
    /// our own <see cref="CloseLobbyWindow"/> path. If we still appear
    /// to be in a room (session state hasn't already moved past
    /// InLobby/InGame), trigger the leave-room flow so the server
    /// doesn't keep us as a ghost member.
    /// </summary>
    private void HandleLobbyWindowClosed()
    {
        _lobbyPingTimer?.Stop();
        _lobbyPingTimer = null;

        var s = _session;
        if (s == null) return;

        // If we're already past lobby (RoomLeft drove the close) the
        // leave-room call is a no-op / errors; skip.
        if (s.Lobby != MultiplayerSession.LobbyStatus.InLobby
            && s.Lobby != MultiplayerSession.LobbyStatus.InGame)
            return;

        // Fire-and-forget the leave; failures are user-visible via
        // the standard error banner path inside MultiplayerSession.
        _ = s.LeaveCurrentLobbyAsync();
    }

    // ==================================================================
    // Match lifecycle: phases + countdown + in-game overlay
    // ==================================================================

    /// <summary>
    /// Apply visual state for the current <see cref="_matchPhase"/>:
    /// shows / hides the overlays and updates the Cancel/Leave button
    /// caption. Idempotent — safe to call on every state change.
    ///
    /// Pre-Window refactor, this method also locked the popup's
    /// header-drag cursor and hid a custom close-X / resize-thumb
    /// during Starting / InGame. Those concerns are gone because the
    /// OS chrome handles drag/resize natively and the title bar's
    /// close X is independent of the lobby — match-phase locking now
    /// only needs to flip the two overlays and the cancel button.
    /// </summary>
    private void ApplyMatchPhaseUi()
    {
        // No window open → nothing to render. Fires when phase changes
        // arrive from session events after we've already left the room.
        if (_lobbyWindow == null) return;

        var starting = _matchPhase == MatchPhase.Starting;

        // Overlays — Visibility set via the prefixed accessors (the
        // null-forgiving '!' is safe because of the guard above).
        _lobbyWindow!.CountdownOverlay.Visibility = starting
            ? Visibility.Visible : Visibility.Collapsed;
        _lobbyWindow!.InGameOverlay.Visibility = _matchPhase == MatchPhase.InGame
            ? Visibility.Visible : Visibility.Collapsed;

        // NO glow call here — load-bearing. The countdown is now a live
        // line INSIDE the chat, whose CountdownOverlay Border uses a shared,
        // frozen DynamicResource (MpBlue) BorderBrush and has no Effect.
        // Calling StartCountdownGlow() on it threw InvalidOperationException
        // (a frozen Freezable can't be animated), and because that throw
        // happened RIGHT AFTER the Visibility line above but BEFORE the
        // button-swap below — and before StartCountdown reached
        // UpdateCountdownTick — the symptom was: the bar appeared but froze
        // at the XAML-default number, the Start button never became Cancel,
        // and the "starting in N" chat line never posted. Don't re-add a
        // glow call unless the chat-line Border is given a LOCAL unfrozen
        // SolidColorBrush + a DropShadowEffect first (see CLAUDE.md).

        // The big left-column Start button DOUBLES as the countdown's
        // Cancel. During Starting it turns red, reads "Cancel", and is
        // shown + enabled for EVERYONE (host and joiner) so anyone can
        // abort the launch; StartButton_Click routes to CancelCountdownByUser
        // while in this phase. Outside the countdown, ownership of the
        // button returns to RenderRoomPanel (blue "Start game", host-only)
        // — we mirror that block here so the restore is immediate even
        // before the next room_state refresh lands.
        if (starting)
        {
            _lobbyWindow!.StartButton.Style = (Style)Application.Current.FindResource("MpDangerButton");
            _lobbyWindow!.StartButton.Visibility = Visibility.Visible;
            _lobbyWindow!.StartButton.IsEnabled = true;
            _lobbyWindow!.StartButton.Content = "✕  " + Strings.Get("MpCountdownCancel");
        }
        else
        {
            _lobbyWindow!.StartButton.Style = (Style)Application.Current.FindResource("MpPrimaryButton");
            _lobbyWindow!.StartButton.Visibility = _isHostInCurrentRoom
                ? Visibility.Visible : Visibility.Collapsed;
            _lobbyWindow!.StartButton.IsEnabled = _isHostInCurrentRoom && (_session?.IsInLobby ?? false);
            _lobbyWindow!.StartButton.Content = "▶  " + Strings.Get("MpRoomStart");
        }

        // In-game cancel caption: "Abort match" (for everyone) while the grace
        // window is open, else "Leave" (just you). RefreshInGamePanel re-applies
        // this each tick so it flips when the window elapses mid-match.
        _lobbyWindow!.InGameCancelButton.Content = WithinAbortWindow
            ? Strings.Get("MpInGameAbort")
            : Strings.Get("MpInGameLeave");
    }

    /// <summary>
    /// Begin the local 3-second countdown after receiving
    /// <c>game_countdown</c> from the Worker. <paramref name="startsAtMsUnix"/>
    /// is the server-issued epoch time at which AoE3 should launch;
    /// every client uses the same value so the countdown stays in
    /// sync across peers regardless of WS latency.
    /// </summary>
    private void StartCountdown(int durationMs)
    {
        _matchPhase = MatchPhase.Starting;
        _countdownStartedAtTicks = Environment.TickCount64;
        _countdownDurationMs = Math.Max(500, durationMs);   // sanity floor
        DiagnosticLog.Write($"MultiplayerTab.StartCountdown: duration={_countdownDurationMs}ms, phase=Starting");
        ApplyMatchPhaseUi();

        _countdownTickTimer?.Stop();
        _countdownTickTimer = new System.Windows.Threading.DispatcherTimer
        {
            // 100 ms tick keeps the number animation crisp without
            // flickering — UI only repaints when the displayed digit
            // changes (see UpdateCountdownTick).
            Interval = TimeSpan.FromMilliseconds(100),
        };
        _countdownTickTimer.Tick += (_, _) => UpdateCountdownTick();
        _countdownTickTimer.Start();
        UpdateCountdownTick();
    }

    private void UpdateCountdownTick()
    {
        if (_lobbyWindow == null) return;
        // Pure local timer — no server timestamp involved, so clock
        // skew between client and server can't shortcut the wait.
        var elapsedMs = Environment.TickCount64 - _countdownStartedAtTicks;
        var remainingMs = _countdownDurationMs - elapsedMs;
        if (remainingMs <= 0)
        {
            _countdownTickTimer?.Stop();
            _lobbyWindow!.CountdownNumber.Text = Strings.Get("MpCountdownGo");
            DiagnosticLog.Write("MultiplayerTab.UpdateCountdownTick: countdown expired, launching AoE3");
            // This is the *only* path that launches AoE3 in the
            // happy case. If for any reason we're already in InGame
            // (defensive), don't re-launch.
            if (_matchPhase != MatchPhase.InGame)
            {
                var process = LaunchActiveModGame();
                EnterInGamePhase(process);
            }
            return;
        }
        var seconds = Math.Max(1, (int)Math.Ceiling(remainingMs / 1000.0));
        _lobbyWindow!.CountdownNumber.Text = seconds.ToString();
    }

    private void CancelLocalCountdownIfRunning()
    {
        _countdownTickTimer?.Stop();
        _countdownTickTimer = null;
    }

    /// <summary>
    /// User pressed Cancel on the pre-launch countdown overlay. No AoE3
    /// process exists yet during the countdown, so this routes through
    /// <see cref="EndMatchAsync"/> only to stop the local timer + return
    /// to the lobby (via ExitInGamePhase) and — for the host — broadcast
    /// game_cancelled so every peer's countdown stops too.
    /// </summary>
    private async void CancelCountdownByUser()
    {
        if (_matchPhase != MatchPhase.Starting) return;
        // During the countdown ANY member may abort for everyone (we're inside
        // the grace window). The server validates and broadcasts game_cancelled.
        await EndMatchAsync("aborted", sendCancel: true);
    }

    /// <summary>
    /// Enter the InGame phase: lock the popup, start the match
    /// timer + the 1-Hz refresh of the P2P status panel. Caches
    /// the spawned AoE3 process so Cancel can kill it.
    /// </summary>
    private void EnterInGamePhase(System.Diagnostics.Process? gameProcess)
    {
        _matchPhase = MatchPhase.InGame;
        _aoe3Process = gameProcess;
        _matchStartedAtUtc = DateTime.UtcNow;
        _matchTimerStartTicks = Environment.TickCount64;

        // Snapshot the Radmin adapter's byte counter so the TRAFFIC stat
        // can show bytes moved during THIS match (delta), and reset the
        // connection-ping readout for the new match.
        var baseline = RadminVpnService.GetAdapterBytes();
        _matchBaselineBytes = baseline.HasValue ? baseline.Value.sent + baseline.Value.received : -1;
        _connectionPingMs = -1;

        // Report our Radmin IP so peers can ping us (the per-player ping column).
        // Done at LAUNCH, not join: at join time the user often isn't on the VPN
        // yet (AdapterIp null). Re-checked each tick in case they connect later.
        _lastReportedRadminIp = null;
        MaybeReportRadminIp();

        if (_lobbyWindow != null)
            _lobbyWindow!.InGameRoomText.Text = _session?.CurrentLobbyTitle ?? _session?.CurrentLobbyId ?? "";

        CancelLocalCountdownIfRunning();
        ApplyMatchPhaseUi();

        _inGameTickTimer?.Stop();
        _inGameTickTimer = new System.Windows.Threading.DispatcherTimer
        {
            // 1 s is plenty — RTT / bytes counters drift slowly. The
            // pulsing "live" dot animates via XAML opacity ticks
            // independent of this timer to stay smooth.
            Interval = TimeSpan.FromSeconds(1),
        };
        _inGameTickTimer.Tick += (_, _) => RefreshInGamePanel();
        _inGameTickTimer.Start();
        RefreshInGamePanel();
    }

    private void ExitInGamePhase()
    {
        _matchPhase = MatchPhase.Lobby;
        _aoe3Process = null;
        _inGameTickTimer?.Stop();
        _inGameTickTimer = null;
        CancelLocalCountdownIfRunning();
        ApplyMatchPhaseUi();
    }

    /// <summary>
    /// Repaint the InGame status overlay from local data: match timer, traffic,
    /// your internet CONNECTION, and a per-peer row list with each peer's derived
    /// <see cref="PeerLinkState"/> (health dot + real ICMP RTT to their Radmin IP,
    /// or "Esperando VPN" / "Sin conexión"). Also posts a chat line on the
    /// Online↔Lost edge. The ICMP "Lost" is INDICATIVE only (Radmin/Windows may
    /// block inbound echo); member_left is the authoritative "left" signal.
    /// </summary>
    private void RefreshInGamePanel()
    {
        // Lobby window closed → nothing to refresh. The 1-s timer that
        // drives this method might still tick once after the window
        // closed (Closed/RoomLeft race); the guard makes that harmless.
        if (_lobbyWindow == null) return;

        // Match timer.
        var elapsedMs = Environment.TickCount64 - _matchTimerStartTicks;
        var elapsed = TimeSpan.FromMilliseconds(Math.Max(0, elapsedMs));
        _lobbyWindow!.InGameMatchTimer.Text = elapsed.TotalHours >= 1
            ? elapsed.ToString(@"h\:mm\:ss")
            : elapsed.ToString(@"mm\:ss");

        var bridgeReady = _session?.IsInLobby ?? false;

        // Traffic: delta of the Radmin adapter's byte counters since the
        // match started (the OS counter is cumulative + whole-adapter, so
        // we show the per-match delta). "—" when the adapter wasn't found.
        var bytesNow = RadminVpnService.GetAdapterBytes();
        if (bytesNow.HasValue && _matchBaselineBytes >= 0)
        {
            var moved = Math.Max(0, (bytesNow.Value.sent + bytesNow.Value.received) - _matchBaselineBytes);
            _lobbyWindow!.InGameTrafficText.Text = FormatBytes(moved);
        }
        else
        {
            _lobbyWindow!.InGameTrafficText.Text = "—";
        }

        // Connection latency: show the cached internet RTT (your link
        // quality — NOT a per-rival ping) colour-coded by health, and kick
        // a fresh probe for the next tick.
        _lobbyWindow!.InGameConnectionText.Text = _connectionPingMs >= 0 ? $"{_connectionPingMs} ms" : "…";
        _lobbyWindow!.InGameConnectionText.Foreground = (Brush)Application.Current.FindResource(
            _connectionPingMs < 0 ? "TextSecondary"
            : _connectionPingMs < 80 ? "MpStatusOnline"
            : _connectionPingMs < 200 ? "MpPingMedium"
            : "MpStatusOffline");
        KickConnectionPing();
        // Keep reporting our Radmin IP (user may have joined the VPN after launch)
        // and refresh the per-peer pings for the rows below.
        MaybeReportRadminIp();
        KickPeerPings();
        // Flip the cancel button from "Abort match" to "Leave" the moment the
        // grace window elapses mid-match (no phase transition fires for that).
        _lobbyWindow!.InGameCancelButton.Content = WithinAbortWindow
            ? Strings.Get("MpInGameAbort")
            : Strings.Get("MpInGameLeave");

        // Mode badge.
        _lobbyWindow!.InGameModeText.Text = Strings.Get(bridgeReady
            ? "MpInGameModeInLobby"
            : "MpInGameModeWaitingLobby");
        _lobbyWindow!.InGameModeText.Foreground = (Brush)Application.Current.FindResource(
            bridgeReady ? "MpStatusOnline" : "MpStatusReconnect");

        // Peer list. We just enumerate room members minus ourselves
        // — every member that's in the lobby IS reachable on the
        // virtual LAN as long as their edge is connected.
        _lobbyWindow!.InGamePeersPanel.Children.Clear();
        var me = _session?.CurrentUser;
        if (me != null)
        {
            _lobbyWindow!.InGamePeersPanel.Children.Add(BuildInGamePeerRow(
                login: string.IsNullOrEmpty(me.DiscordUsername) ? me.DisplayName : me.DiscordUsername,
                state: PeerLinkState.Online,   // your own row is always "you" / green
                rttMs: 0,
                isSelf: true));
        }
        int peerCount = 0;
        foreach (var member in _roomMembers.Values)
        {
            if (me != null && string.Equals(member.UserId, me.Id, StringComparison.Ordinal))
                continue;
            peerCount++;
            var state = PeerNetHealth.Classify(
                !string.IsNullOrEmpty(member.RadminIp), member.PingMs, member.ConsecutiveFails);
            // Chat notice only on the Online↔Lost edge (debounced by the fail
            // threshold), never on the transient Unstable/WaitingVpn steps.
            if (state == PeerLinkState.Lost && member.LastLinkState != PeerLinkState.Lost)
                AppendChatSystem(Strings.Format("MpChatPeerLost", member.Login));
            else if (state == PeerLinkState.Online && member.LastLinkState == PeerLinkState.Lost)
                AppendChatSystem(Strings.Format("MpChatPeerReconnected", member.Login));
            member.LastLinkState = state;
            _lobbyWindow!.InGamePeersPanel.Children.Add(BuildInGamePeerRow(
                login: member.Login,
                state: state,
                rttMs: member.PingMs,
                isSelf: false));
        }

        if (peerCount == 0)
        {
            _lobbyWindow!.InGamePeersPanel.Children.Add(new TextBlock
            {
                Text = Strings.Get("MpInGameWaitingPeers"),
                Foreground = (Brush)Application.Current.FindResource("TextSecondary"),
                FontSize = (double)Application.Current.FindResource("FontSizeCaption"),
                FontStyle = FontStyles.Italic,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 12, 0, 0),
            });
        }

        // "Pulsing" dot — toggle opacity for a breathing effect.
        _lobbyWindow!.InGameLiveDot.Opacity = _lobbyWindow!.InGameLiveDot.Opacity > 0.6 ? 0.4 : 1.0;
    }

    /// <summary>
    /// Fire-and-forget refresh of <see cref="_connectionPingMs"/> via an
    /// internet ICMP probe (see <see cref="PingInternetRttMsAsync"/>).
    /// Guarded so a fast tick can call it repeatedly without stacking
    /// overlapping pings (each probe can take up to its timeout to fail).
    /// </summary>
    private async void KickConnectionPing()
    {
        if (_connectionPingInFlight) return;
        _connectionPingInFlight = true;
        try
        {
            _connectionPingMs = await PingInternetRttMsAsync();
        }
        finally
        {
            _connectionPingInFlight = false;
        }
    }

    /// <summary>
    /// Ping a reliable public anycast resolver (Cloudflare 1.1.1.1, then
    /// Google 8.8.8.8 as fallback) and return the round-trip time in ms, or
    /// -1 if neither answered. This is the user's general INTERNET latency,
    /// shown everywhere a "ping" appears (in-game overlay, lobby header,
    /// rooms browser). Chosen over a Radmin seed-peer ping because it always
    /// resolves to a number — the seed depended on one peer being online AND
    /// you already being on the VPN, so it usually showed "—".
    /// </summary>
    private static async Task<int> PingInternetRttMsAsync()
    {
        foreach (var host in new[] { "1.1.1.1", "8.8.8.8" })
        {
            try
            {
                using var ping = new System.Net.NetworkInformation.Ping();
                var reply = await ping.SendPingAsync(host, 1000).ConfigureAwait(false);
                if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                    return (int)reply.RoundtripTime;
            }
            catch
            {
                // Try the next host; return -1 if all fail.
            }
        }
        return -1;
    }

    // -------- Per-peer ping (in-game) -------------------------------

    /// <summary>Our last-reported Radmin IP, so we don't re-send an unchanged one.</summary>
    private string? _lastReportedRadminIp;
    private bool _peerPingInFlight;

    /// <summary>
    /// Report our current Radmin VPN IP (26.x) to the room so peers can ping us,
    /// but only when it's known AND changed since last time. Cheap no-op when the
    /// user isn't on Radmin yet (AdapterIp null) or hasn't changed.
    /// </summary>
    private void MaybeReportRadminIp()
    {
        var ip = RadminVpnService.GetStatus().AdapterIp;
        if (string.IsNullOrEmpty(ip) || string.Equals(ip, _lastReportedRadminIp, StringComparison.Ordinal))
            return;
        var sock = _session?.RoomSocket;
        if (sock == null) return;
        _lastReportedRadminIp = ip;
        _ = sock.SendSetRadminIpAsync(ip);
    }

    /// <summary>
    /// Fire-and-forget refresh of every peer's ICMP RTT to their Radmin IP, in
    /// parallel (so N timeouts don't serialise into N seconds). Stores the result
    /// on each <see cref="RoomMemberEntry.PingMs"/>; the next RefreshInGamePanel
    /// paints it. Guarded so a fast tick can't stack overlapping probe rounds.
    /// </summary>
    private async void KickPeerPings()
    {
        if (_peerPingInFlight) return;
        _peerPingInFlight = true;
        try
        {
            var myId = _session?.CurrentUser?.Id;
            var targets = _roomMembers.Values
                .Where(m => !string.IsNullOrEmpty(m.RadminIp)
                            && (myId == null || !string.Equals(m.UserId, myId, StringComparison.Ordinal)))
                .ToList();
            await Task.WhenAll(targets.Select(async m =>
            {
                var rtt = await PingPeerAsync(m.RadminIp!);
                m.PingMs = rtt;
                // Track the short failure/success history the health classifier reads.
                // A single answered probe clears the fail streak (and vice-versa) so
                // "Lost" needs sustained silence, not one dropped packet.
                if (rtt >= 0) { m.ConsecutiveOks++; m.ConsecutiveFails = 0; }
                else { m.ConsecutiveFails++; m.ConsecutiveOks = 0; }
            }));
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"MultiplayerTab.KickPeerPings: {ex.Message}");
        }
        finally
        {
            _peerPingInFlight = false;
        }
    }

    /// <summary>ICMP RTT (ms) to a peer's Radmin IP, or -1 on timeout/error.
    /// Same shape as <see cref="PingInternetRttMsAsync"/> but a single host.</summary>
    private static async Task<int> PingPeerAsync(string ip)
    {
        try
        {
            using var ping = new System.Net.NetworkInformation.Ping();
            var reply = await ping.SendPingAsync(ip, 1000).ConfigureAwait(false);
            if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                return (int)reply.RoundtripTime;
        }
        catch { /* unreachable / not on VPN yet → -1 */ }
        return -1;
    }

    /// <summary>
    /// Repaint the lobby header's CONNECTION stat from the cached
    /// <see cref="_connectionPingMs"/> (your internet latency, not a
    /// per-rival ping). Same colour thresholds as the in-game CONNECTION
    /// stat. No-op when the lobby window is gone.
    /// </summary>
    private void UpdateLobbyPing()
    {
        if (_lobbyWindow == null) return;
        _lobbyWindow.RoomConnText.Text = _connectionPingMs >= 0 ? $"{_connectionPingMs} ms" : "…";
        _lobbyWindow.RoomConnText.Foreground = (Brush)Application.Current.FindResource(
            _connectionPingMs < 0 ? "TextSecondary"
            : _connectionPingMs < 80 ? "MpStatusOnline"
            : _connectionPingMs < 200 ? "MpPingMedium"
            : "MpStatusOffline");
    }

    /// <summary>
    /// One peer row for the in-game panel: [health dot] [name — star, ellipsis]
    /// [ping-or-status — right aligned]. Star-sizing the name is load-bearing —
    /// the old fixed 180+110+80 columns overflowed the ~284 px panel and clipped
    /// the ping off-screen (the "no ping shows" bug). Self renders as "you" / "—".
    /// </summary>
    private FrameworkElement BuildInGamePeerRow(
        string login, PeerLinkState state, double rttMs, bool isSelf)
    {
        var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });               // dot
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // name
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });               // ping/status

        var dotBrush = isSelf
            ? (Brush)Application.Current.FindResource("MpStatusOnline")
            : PeerDotBrush(state);
        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 8, Height = 8,
            Fill = dotBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        Grid.SetColumn(dot, 0);
        row.Children.Add(dot);

        var nameTb = new TextBlock
        {
            Text = login,
            Foreground = (Brush)Application.Current.FindResource("TextPrimary"),
            FontSize = (double)Application.Current.FindResource("FontSizeBody"),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(nameTb, 1);
        row.Children.Add(nameTb);

        // Right cell: your own row shows "you"; a peer shows its real RTT
        // ("NN ms", coloured by health) when Online, else a localized status
        // ("Esperando VPN" / "…" / "Sin conexión").
        string statusText;
        Brush statusBrush;
        if (isSelf)
        {
            statusText = Strings.Get("MpPeerYou");
            statusBrush = (Brush)Application.Current.FindResource("MpStatusOnline");
        }
        else if (state == PeerLinkState.Online && rttMs >= 0)
        {
            statusText = $"{(int)rttMs} ms";
            statusBrush = (Brush)Application.Current.FindResource(
                rttMs < 80 ? "MpStatusOnline" : rttMs < 200 ? "MpPingMedium" : "MpStatusOffline");
        }
        else
        {
            statusText = state switch
            {
                PeerLinkState.WaitingVpn => Strings.Get("MpPeerWaitingVpn"),
                PeerLinkState.Lost => Strings.Get("MpPeerLost"),
                _ => "…",
            };
            statusBrush = PeerDotBrush(state);
        }
        var statusTb = new TextBlock
        {
            Text = statusText,
            Foreground = statusBrush,
            FontFamily = new System.Windows.Media.FontFamily("Consolas, Courier New, monospace"),
            FontSize = (double)Application.Current.FindResource("FontSizeCaption"),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        Grid.SetColumn(statusTb, 2);
        row.Children.Add(statusTb);

        return row;
    }

    /// <summary>Health-dot / status brush for a derived <see cref="PeerLinkState"/>:
    /// grey (waiting) / green (online) / amber (unstable) / red (lost).</summary>
    private static Brush PeerDotBrush(PeerLinkState state) => (Brush)Application.Current.FindResource(
        state switch
        {
            PeerLinkState.Online => "MpStatusOnline",
            PeerLinkState.Unstable => "MpPingMedium",
            PeerLinkState.Lost => "MpStatusOffline",
            _ => "TextSecondary",
        });

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    /// <summary>
    /// Cancel / Leave game button click.
    /// Host → asks the Worker to broadcast game_cancelled so every
    /// peer kills its AoE3 and the room returns to "open" status.
    /// Non-host → just kills the local AoE3 process and leaves the
    /// room; the other players keep playing.
    /// </summary>
    private async void InGameCancelButton_Click(object sender, RoutedEventArgs e)
    {
        // Themed in-lobby confirm (replaces the OS MessageBox). Host =
        // "cancel for everyone" (danger, broadcasts game_cancelled); joiner
        // = "leave the game" (only this player drops, room plays on). Needs
        // the lobby window open to host the overlay — it always is here
        // (this button lives in that window), but guard anyway.
        if (_lobbyWindow == null) return;
        // Within the grace window → "abort for everyone" (any member); after it →
        // "leave" (just you). The server is the final authority on the window.
        bool canAbort = WithinAbortWindow;
        bool confirmed = await MpAlertOverlay.ConfirmAsync(
            _lobbyWindow.LobbyRootGrid,
            canAbort ? Strings.Get("MpConfirmAbortTitle") : Strings.Get("MpConfirmLeaveTitle"),
            canAbort ? Strings.Get("MpConfirmAbortBody") : Strings.Get("MpConfirmLeaveBody"),
            canAbort ? Strings.Get("MpConfirmAbortYes") : Strings.Get("MpConfirmLeaveYes"),
            Strings.Get("MpAlertCancel"),
            danger: true);
        if (!confirmed) return;

        await EndMatchAsync(canAbort ? "aborted" : "left", sendCancel: canAbort);
    }

    /// <summary>
    /// Shared kill path: stops the local AoE3 process, exits the
    /// InGame phase locally, and if the user is the host, asks the
    /// Worker to broadcast game_cancelled. Idempotent — calling
    /// twice (e.g. host cancel + window-close confirm) is safe.
    /// </summary>
    private async Task EndMatchAsync(string reason, bool sendCancel)
    {
        try
        {
            var p = _aoe3Process;
            if (p != null)
            {
                try
                {
                    if (!p.HasExited) p.Kill(entireProcessTree: true);
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Write($"MultiplayerTab.EndMatch: kill AoE3 failed — {ex.Message}");
                }
            }
        }
        finally
        {
            ExitInGamePhase();
        }

        // sendCancel = "abort for everyone" (within the grace window). The server
        // re-checks the window and broadcasts game_cancelled, or replies
        // grace_window_closed if we raced past it — either way we've already left
        // locally, so a late race just means the others keep playing.
        if (sendCancel && _session?.RoomSocket != null)
        {
            try { await _session.RoomSocket.SendCancelGameAsync(reason); }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"MultiplayerTab.EndMatch: SendCancelGameAsync — {ex.Message}");
            }
        }
        AppendChatSystem(sendCancel
            ? Strings.Get("MpChatYouCancelled")
            : Strings.Get("MpChatYouLeftGame"));
    }

    /// <summary>
    /// True while a game is actively running locally. Used by
    /// MainWindow.OnClosing to confirm with the user before
    /// terminating, since closing the launcher mid-match would
    /// kill AoE3 without giving the host the chance to cancel
    /// cleanly first.
    /// </summary>
    public bool IsMatchActive => _matchPhase == MatchPhase.InGame || _matchPhase == MatchPhase.Starting;

    /// <summary>
    /// Called from MainWindow.OnClosing when the user attempts to
    /// close the launcher with an active game. Confirms and (on
    /// yes) cancels cleanly. Returns false if the user said "no"
    /// so the close can be aborted.
    /// </summary>
    public async Task<bool> ConfirmCloseDuringMatchAsync()
    {
        var msg = _isHostInCurrentRoom
            ? "A game is in progress. Cancelling now will disconnect every player. Continue?"
            : "AoE3 is running. Closing the launcher will terminate it. Continue?";
        var r = MessageBox.Show(msg, "Multiplayer", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) return false;
        // Closing within the grace window aborts for everyone; after it, only we drop.
        await EndMatchAsync("launcher_closed", sendCancel: WithinAbortWindow);
        return true;
    }

}
