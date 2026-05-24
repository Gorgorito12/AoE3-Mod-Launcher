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

    private Subtab _activeSubtab = Subtab.Rooms;
    private bool _isRefreshingList;
    private bool _isRefreshingHistory;

    private System.Windows.Threading.DispatcherTimer? _quotaTimer;

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
        public bool Ready { get; set; }
    }

    // -------- Popup drag state --------------------------------------
    //
    // Floating-card drag: the user grabs the header strip and moves
    // the whole RoomPopupCard around within RoomPopupCanvas. Drag is
    // disabled while the room is in the Starting / InGame phase to
    // prevent accidental clicks during a live match.

    private bool _isDraggingPopup;
    private System.Windows.Point _dragStartCursorOnCanvas;
    private double _dragStartCardLeft;
    private double _dragStartCardTop;
    private bool _popupPositionInitialised;

    // -------- Match lifecycle state ---------------------------------
    //
    // Three logical phases:
    //   Lobby     — popup is fully interactive, drag enabled, X visible
    //   Starting  — countdown overlay shown, popup locked, no X
    //   InGame    — InGame overlay shown, popup locked, only Cancel/Leave
    //
    // We track the phase locally so the UI gates immediately without
    // waiting for a round-trip to the server. The Worker's
    // game_countdown / game_started / game_cancelled frames flip
    // this; the popup's UI responds to changes.

    private enum MatchPhase { Lobby, Starting, InGame }
    private MatchPhase _matchPhase = MatchPhase.Lobby;

    /// <summary>
    /// AoE3 process spawned when the countdown completed. Cached so
    /// <see cref="InGameCancelButton_Click"/> can <c>Kill()</c> it on
    /// cancel without re-walking the process table. Cleared when the
    /// process exits or we leave the room.
    /// </summary>
    private System.Diagnostics.Process? _aoe3Process;
    private DateTime _matchStartedAtUtc;
    private long _matchTimerStartTicks;

    /// <summary>Drives the breathing animation of the InGame "live" dot + match timer.</summary>
    private System.Windows.Threading.DispatcherTimer? _inGameTickTimer;

    /// <summary>Drives the per-frame countdown number 3 → 2 → 1.</summary>
    private System.Windows.Threading.DispatcherTimer? _countdownTickTimer;

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

    public MultiplayerTab()
    {
        InitializeComponent();
        ApplyStrings();
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
    // The user dismiss flag is intentionally NOT honoured anymore: the
    // new banner is informative (small, colour-coded) rather than
    // nagging, and a dismissed user who later forgets why their game
    // isn't connecting has no recourse otherwise.
    // LauncherConfig.Multiplayer.RadminBannerDismissed stays for
    // forward/backward config compat but is no longer read here.
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
            RadminPrimaryButton.Content = Strings.Get("MpRadminInstallButton");
            RadminPrimaryButton.Visibility = Visibility.Visible;
            RadminPrimaryButton.IsEnabled = true;
            // No actionable network info to show while Radmin isn't on yet.
            RadminNetworkNamePanel.Visibility = Visibility.Collapsed;
            RadminInstructionsText.Visibility = Visibility.Collapsed;
        }
        else if (!status.IsServiceRunning)
        {
            RadminBanner.Background = (Brush)new BrushConverter().ConvertFromString("#1f2c3d")!;
            RadminBanner.BorderBrush = (Brush)new BrushConverter().ConvertFromString("#3a5a8c")!;
            RadminStatusIcon.Background = (Brush)new BrushConverter().ConvertFromString("#3a5a8c")!;
            RadminStatusGlyph.Text = "i";
            RadminBannerTitle.Text = Strings.Get("MpRadminNotConnectedTitle");
            RadminBannerBody.Text = Strings.Get("MpRadminNotConnectedBody");
            RadminPrimaryButton.Content = Strings.Get("MpRadminOpenButton");
            RadminPrimaryButton.Visibility = Visibility.Visible;
            RadminPrimaryButton.IsEnabled = true;
            RadminNetworkNamePanel.Visibility = Visibility.Collapsed;
            RadminInstructionsText.Visibility = Visibility.Collapsed;
        }
        else
        {
            RadminBanner.Background = (Brush)new BrushConverter().ConvertFromString("#1f3d2a")!;
            RadminBanner.BorderBrush = (Brush)new BrushConverter().ConvertFromString("#3a8c5a")!;
            RadminStatusIcon.Background = (Brush)new BrushConverter().ConvertFromString("#3a8c5a")!;
            RadminStatusGlyph.Text = "✓";
            RadminBannerTitle.Text = Strings.Get("MpRadminConnectedTitle");
            RadminBannerBody.Text = string.Format(
                Strings.Get("MpRadminConnectedBody"),
                status.AdapterIp ?? "26.x.x.x");
            RadminPrimaryButton.Content = Strings.Get("MpRadminOpenButton");
            RadminPrimaryButton.Visibility = Visibility.Visible;
            RadminPrimaryButton.IsEnabled = true;

            // Show the network-name copier + numbered steps. The
            // TextBox is read-only and pre-filled with the canonical
            // network name so the user can verify visually that we're
            // pointing them at the right thing (no hidden clipboard
            // surprises) AND can select-and-copy with their own
            // keyboard shortcuts if they prefer that flow.
            RadminNetworkNameBox.Text = RadminVpnService.AoE3TadNetworkName;
            RadminCopyNameButton.Content = Strings.Get("MpRadminCopyNameButton");
            RadminInstructionsText.Text = Strings.Get("MpRadminInstructions");
            RadminNetworkNamePanel.Visibility = Visibility.Visible;
            RadminInstructionsText.Visibility = Visibility.Visible;
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
                    MessageBox.Show(
                        Strings.Get("MpRadminLaunchFailed"),
                        "Wars of Liberty Launcher",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
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
    }

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
        Func<ModProfile, bool>? switchActiveMod = null)
    {
        if (_session != null)
            _session.StateChanged -= OnSessionStateChanged;

        _session = session;
        _getActiveProfile = getActiveProfile;
        _computeModFingerprint = computeModFingerprint;
        _launchGame = launchGame;
        _switchActiveMod = switchActiveMod;
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
    }

    public void RefreshStrings() => ApplyStrings();

    private void ApplyStrings()
    {
        SubtabRooms.Content = Strings.Get("MpSubtabRooms");
        SubtabFriends.Content = Strings.Get("MpSubtabFriends");
        SubtabProfile.Content = Strings.Get("MpSubtabProfile");
        SubtabHistory.Content = Strings.Get("MpSubtabHistory");

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

        ReadyButton.Content = Strings.Get("MpRoomReady");
        StartButton.Content = Strings.Get("MpRoomStart");
        LeaveRoomButton.Content = Strings.Get("MpRoomLeave");
        ChatInputBox.Tag = Strings.Get("MpRoomChatPlaceholder");

        // Table column headers + empty-state copy. These have no
        // translation keys today; we use the wording from the
        // redesign brief directly. When localisation lands, route
        // them through Strings.Get like everything else.
        ColHeaderRoom.Text = "ROOM";
        ColHeaderHost.Text = "HOST";
        ColHeaderPlayers.Text = "PLAYERS";
        ColHeaderPing.Text = "PING";
        ColHeaderStatus.Text = "STATUS";
        ColHeaderAction.Text = "ACTION";
        EmptyTitleText.Text = "No rooms available right now";
        EmptyBodyText.Text = "Be the first to create one and start a game!";
        EmptyCreateButton.Content = "+  " + Strings.Get("MpRoomsCreate");

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
            (Color)ColorConverter.ConvertFromString("#2a2d34"));
        NatBadgeText.Foreground = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString("#cccccc"));
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
        });

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
            if (nextSocket == null) _currentLobbyModId = null;
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
            // Re-center the popup on next show so the user always
            // sees it at a sane position when they enter a new room.
            _popupPositionInitialised = false;
        }

        // Reset per-room UI state whenever we change rooms.
        if (socketChanged)
        {
            _roomMembers.Clear();
            _roomHostUserId = null;
            _isHostInCurrentRoom = false;
            ChatLogPanel.Children.Clear();
            RoomMembersPanel.Children.Clear();
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
                        StartCountdown(durationMs);
                        AppendChatSystem($"Game starting in {durationMs / 1000} seconds…");
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
                            AppendChatSystem("The game has started.");
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
                        AppendChatSystem(reason == "host_cancelled"
                            ? "Host cancelled the game. Returning to lobby."
                            : $"Game cancelled: {reason}.");
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
        if (ChatLogPanel == null) return;
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

        if (_roomMembers.TryGetValue(userId, out var existing))
        {
            existing.Login = login;
        }
        else
        {
            _roomMembers[userId] = new RoomMemberEntry { UserId = userId, Login = login };
        }
        AppendChatSystem($"{login} joined.");
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
            AppendChatSystem($"{entry.Login} left.");
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
    /// Rebuild the players sidebar from <see cref="_roomMembers"/>. Host
    /// rendered first with a small "host" tag; everyone else follows in
    /// join order (dictionary insertion order in .NET is preserved).
    /// </summary>
    private void RenderRoomMembers()
    {
        RoomMembersPanel.Children.Clear();

        // The "PLAYERS" section header lives in the XAML grid
        // above the members container now — no need to render
        // it from code. We just emit one player row per member.
        foreach (var m in _roomMembers.Values)
        {
            RoomMembersPanel.Children.Add(BuildMemberRow(m));
        }
    }

    /// <summary>
    /// One row in the players list. Layout:
    ///   [online dot] [avatar 32] [name + ping (small)] [Host badge] [Ready badge]
    /// Avatar uses the Discord avatar URL when we have one for the
    /// current user; for other members we don't have a URL yet,
    /// so we draw a coloured circle with their initial (cheap,
    /// stable, matches the redesign's "warm gold" placeholder).
    /// </summary>
    private FrameworkElement BuildMemberRow(RoomMemberEntry m)
    {
        var row = new Border
        {
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 7, 8, 7),
            Margin = new Thickness(0, 2, 0, 2),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // online dot
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // avatar
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });  // name
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // badges

        // Online dot.
        grid.Children.Add(WithColumn(new System.Windows.Shapes.Ellipse
        {
            Width = 8, Height = 8,
            Fill = (Brush)Application.Current.FindResource("MpStatusOnline"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        }, 0));

        // Avatar circle. We use the current user's Discord avatar
        // when this row IS the current user (only data we have
        // locally); otherwise an initial-on-coloured-disc tile.
        var avatarSize = 28.0;
        var avatarHost = new Border
        {
            Width = avatarSize, Height = avatarSize,
            CornerRadius = new CornerRadius(avatarSize / 2),
            Background = (Brush)Application.Current.FindResource("MpSurfaceAlt"),
            BorderBrush = (Brush)Application.Current.FindResource("BorderSubtle"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var me = _session?.CurrentUser;
        var isMe = me != null && string.Equals(m.UserId, me.Id, StringComparison.Ordinal);
        var initialText = !string.IsNullOrEmpty(m.Login)
            ? m.Login.Substring(0, 1).ToUpperInvariant()
            : "?";
        try
        {
            if (isMe && !string.IsNullOrEmpty(me?.AvatarUrl))
            {
                var img = new System.Windows.Media.ImageBrush
                {
                    ImageSource = new System.Windows.Media.Imaging.BitmapImage(
                        new Uri(me!.AvatarUrl!, UriKind.Absolute)),
                    Stretch = System.Windows.Media.Stretch.UniformToFill,
                };
                avatarHost.Background = img;
            }
            else
            {
                avatarHost.Child = new TextBlock
                {
                    Text = initialText,
                    Foreground = (Brush)Application.Current.FindResource("TextSecondary"),
                    FontSize = 12,
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
                Text = initialText,
                Foreground = (Brush)Application.Current.FindResource("TextSecondary"),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }
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
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        // Per-peer ping used to come from the in-launcher PeerMesh
        // (each PeerChannel ran its own STUN ping cadence). With n2n,
        // game traffic flows through the virtual NIC and we don't see
        // per-peer pings at this layer. The row stays without an RTT
        // line — could be filled in with a manual ICMP probe to the
        // peer's 10.99.0.X address later if the UI calls for it.
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
            badges.Children.Add(BuildBadge("Host  👑",
                (Brush)Application.Current.FindResource("MpBlueSubtle"),
                (Brush)Application.Current.FindResource("MpBlue")));
        }
        if (m.Ready)
        {
            badges.Children.Add(BuildBadge("Ready",
                Brushes.Transparent,
                (Brush)Application.Current.FindResource("MpPingGood")));
        }
        grid.Children.Add(WithColumn(badges, 3));

        row.Child = grid;
        return row;
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
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(7, 2, 7, 2),
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                Foreground = foreground,
                FontSize = 10,
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
        if (ChatLogPanel == null) return;

        var rowGrid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });   // timestamp
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });  // tag/avatar+name
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });  // body

        // Timestamp (column 0).
        rowGrid.Children.Add(WithColumn(new TextBlock
        {
            Text = timestamp.ToString("h:mm tt"),
            Foreground = (Brush)Application.Current.FindResource("TextSecondary"),
            FontSize = 11,
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
                FontSize = 12,
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
                        FontSize = 10,
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
                    FontSize = 10,
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
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
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
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        }, 2));

        ChatLogPanel.Children.Add(rowGrid);

        // Cap the in-memory log so a marathon session doesn't bloat
        // the visual tree. 500 rows ≈ 7 hours of moderate chat.
        while (ChatLogPanel.Children.Count > 500)
            ChatLogPanel.Children.RemoveAt(0);
        ChatScroll?.ScrollToBottom();
    }

    /// <summary>
    /// Legacy raw-append path. Kept so any caller still in
    /// transition to AppendChatRow doesn't break. Renders as a
    /// system info row with a "—" prefix to match the old look.
    /// </summary>
    private void AppendChatRaw(string text, Brush color)
    {
        ChatLogPanel.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = color,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 1, 0, 1),
        });
        while (ChatLogPanel.Children.Count > 500)
            ChatLogPanel.Children.RemoveAt(0);
        ChatScroll?.ScrollToBottom();
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
        ChatLogPanel?.Children.Clear();
    }

    /// <summary>
    /// Emoji button placeholder. A proper picker pulls in a UI
    /// library we don't need yet — for now this drops a smiley
    /// at the caret so the button is functional and visibly
    /// alive instead of a dead icon.
    /// </summary>
    private void ChatEmojiButton_Click(object sender, RoutedEventArgs e)
    {
        if (ChatInputBox == null) return;
        var caret = ChatInputBox.CaretIndex;
        ChatInputBox.Text = ChatInputBox.Text.Insert(caret, "🙂");
        ChatInputBox.CaretIndex = caret + 2; // emoji is a surrogate pair (length 2)
        ChatInputBox.Focus();
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
        if (ChatPlaceholderText == null || ChatInputBox == null) return;
        ChatPlaceholderText.Visibility = string.IsNullOrEmpty(ChatInputBox.Text)
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
            RoomPanel.Visibility = Visibility.Visible;
            RenderRoomPanel();
        }
        else
        {
            SignInPanel.Visibility = Visibility.Collapsed;
            BrowserPanel.Visibility = Visibility.Visible;
            RoomPanel.Visibility = Visibility.Collapsed;
            RenderBrowser();
        }
    }

    private void ShowSignInPanel(string? errorMessage)
    {
        SignInPanel.Visibility = Visibility.Visible;
        BrowserPanel.Visibility = Visibility.Collapsed;
        RoomPanel.Visibility = Visibility.Collapsed;
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

    private void RenderRoomPanel()
    {
        var s = _session!;
        RoomTitleText.Text = s.CurrentLobbyTitle ?? s.CurrentLobbyId ?? "";

        var status = s.Lobby switch
        {
            MultiplayerSession.LobbyStatus.Joining => "Joining…",
            MultiplayerSession.LobbyStatus.Leaving => "Leaving…",
            MultiplayerSession.LobbyStatus.InGame => "In game",
            _ => "In lobby",
        };

        // P2P readiness: with the hook-injector bridge, "ready to
        // play" just means the mesh is up AND the injector artefacts
        // are shipped — there's no per-machine driver install gate
        // anymore. For solo rooms (host alone) we still show
        // "P2P ready"; peers will join later.
        var p2pReady = s.IsP2pBridgeReady;
        var p2pStatus = p2pReady ? "P2P LAN ready" : "P2P starting…";

        // Build the meta line as inline runs so the P2P state can
        // wear its own (green) colour without having to maintain
        // two TextBlocks. Mirrors the reference: status in muted
        // text, P2P readiness highlighted.
        RoomMetaText.Inlines.Clear();
        RoomMetaText.Inlines.Add(new System.Windows.Documents.Run(status)
        {
            Foreground = (Brush)Application.Current.FindResource("TextSecondary"),
        });
        RoomMetaText.Inlines.Add(new System.Windows.Documents.Run("  ·  ")
        {
            Foreground = (Brush)Application.Current.FindResource("TextSecondary"),
        });
        RoomMetaText.Inlines.Add(new System.Windows.Documents.Run(p2pStatus)
        {
            Foreground = (Brush)Application.Current.FindResource(
                p2pReady ? "MpStatusOnline" : "MpStatusReconnect"),
            FontWeight = FontWeights.SemiBold,
        });

        // ---------- Stat blocks (HOST / PLAYERS / ROOM ID) ----------

        // Host display: pick the member entry that matches
        // _roomHostUserId, fall back to the user-id itself when
        // we haven't received the room_state yet (e.g. right
        // after EnterHostedLobbyAsync, before WS catches up).
        string hostLabel = "—";
        if (!string.IsNullOrEmpty(_roomHostUserId)
            && _roomMembers.TryGetValue(_roomHostUserId, out var hostEntry))
        {
            hostLabel = hostEntry.Login;
        }
        else if (!string.IsNullOrEmpty(_roomHostUserId))
        {
            hostLabel = _roomHostUserId;
        }
        RoomHostText.Text = hostLabel;

        // Players: live count from the roster vs. configured max.
        // We don't have MaxPlayers in the local snapshot today —
        // it's only on the lobby summary the browser fetches; for
        // v1 the count vs "?" is honest. If we later cache the
        // CreateLobbyResponse server-side, wire it through here.
        var playerCount = _roomMembers.Count;
        var maxPlayers = TryGetCurrentLobbyMaxPlayers(out var maxP) ? maxP.ToString() : "?";
        RoomPlayersText.Text = $"{playerCount} / {maxPlayers} players";

        // ROOM ID: short uppercase code if the worker assigns
        // one, otherwise the raw lobby id (truncated for sanity).
        var rid = s.CurrentLobbyId ?? "";
        if (rid.Length > 12) rid = rid.Substring(0, 12);
        RoomIdText.Text = rid.ToUpperInvariant();

        // ---------- Network info card ----------
        RoomConnectionText.Text = p2pReady ? "P2P LAN" : "P2P starting";
        RoomModText.Text = TryGetCurrentLobbyModName(out var modName) ? modName : "—";
        RoomMaxPlayersText.Text = maxPlayers;
        var hasPwd = TryGetCurrentLobbyHasPassword(out var hp) && hp;
        RoomPasswordText.Text = hasPwd ? "Required" : "None";

        // ---------- Action buttons ----------
        // Ready toggle visual: render with a check glyph + label
        // so the state ("Ready" / "Not ready") is obvious. The
        // actual roster-side ready flag lives in the room state
        // frame; the local user is found via session.CurrentUser.
        var me = s.CurrentUser;
        var iAmReady = me != null
            && _roomMembers.TryGetValue(me.Id, out var meEntry)
            && meEntry.Ready;
        ReadyButton.Content = iAmReady ? "✓  Ready" : "○  Mark as ready";
        ReadyButton.Tag = iAmReady ? "ready" : "";

        // The Start button only appears for the host; enabled once
        // the P2P bridge is ready so AoE3 launches into a working
        // hook-bridged network rather than discovering nothing.
        StartButton.Visibility = _isHostInCurrentRoom
            ? Visibility.Visible
            : Visibility.Collapsed;
        StartButton.IsEnabled = _isHostInCurrentRoom && s.IsP2pBridgeReady;
        StartButton.Content = "▶  " + Strings.Get("MpRoomStart");
        LeaveRoomButton.Content = "↩  " + Strings.Get("MpRoomLeave");
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
        if (string.IsNullOrEmpty(lobbyId) || _lastBrowserList == null) return false;
        foreach (var l in _lastBrowserList)
        {
            if (string.Equals(l.Id, lobbyId, StringComparison.Ordinal))
            {
                maxPlayers = l.MaxPlayers;
                return true;
            }
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
        if (string.IsNullOrEmpty(lobbyId) || _lastBrowserList == null) return false;
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

    private void SubtabRooms_Click(object sender, RoutedEventArgs e)
    {
        _activeSubtab = Subtab.Rooms;
        RefreshFromSession();
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
            FontSize = 13,
            Margin = new Thickness(0, 0, 8, 0),
        });
        header.Children.Add(new TextBlock
        {
            Text = $"{row.ModId} · {row.MapName ?? "—"}",
            Foreground = (Brush)Application.Current.FindResource("TextPrimary"),
            FontSize = 13,
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
            FontSize = 11,
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
            MessageBox.Show(
                Strings.Get("MpModNotInstalled"),
                "Multiplayer",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
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
            profile => _computeModFingerprint!(profile))
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
            await _session.EnterHostedLobbyAsync(dlg.CreatedLobby);
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
            MessageBox.Show(
                $"Could not enter the lobby:\n\n{ex.Message}",
                "Multiplayer error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
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
        return string.IsNullOrEmpty(saved) ? null : saved;
    }


    // ---------- Rooms list polling + rendering ----------

    private async Task RefreshRoomsListAsync()
    {
        if (_session == null || _isRefreshingList) return;
        _isRefreshingList = true;
        try
        {
            // Loading skeleton: a single dim line so the user knows
            // a fetch is in flight. The empty-state card and error
            // box are siblings (not children of RoomsListPanel) so
            // we hide both while loading and re-decide afterwards.
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

            var list = await _session.Api.ListLobbiesAsync();
            // Cache the snapshot so the room view (and any other
            // consumer that needs MaxPlayers / IsPrivate / ModId
            // for the current lobby) can read it without an extra
            // round-trip.
            _lastBrowserList = list.Lobbies as List<LobbySummary> ?? new List<LobbySummary>(list.Lobbies);
            RoomsListPanel.Children.Clear();

            if (list.Lobbies.Count == 0)
            {
                // Show the dedicated empty-state card (defined in
                // XAML with the crossed-flags illustration and the
                // outlined Create-room CTA). Better than dumping an
                // italic line in the table because the table header
                // strip stays visible above for context.
                RoomsEmptyState.Visibility = Visibility.Visible;
                return;
            }

            // Render alternating row backgrounds so the table reads
            // as a table, not a stack of cards. The header strip
            // above defines the column widths; BuildRoomRow mirrors
            // them so the columns line up.
            int idx = 0;
            foreach (var lobby in list.Lobbies)
                RoomsListPanel.Children.Add(BuildRoomRow(lobby, idx++));
        }
        catch (Exception ex)
        {
            // Network / API errors land in a dedicated banner so
            // they don't look like a row that "happens to be red".
            RoomsListPanel.Children.Clear();
            RoomsErrorText.Text = ex.Message;
            RoomsErrorBox.Visibility = Visibility.Visible;
        }
        finally
        {
            _isRefreshingList = false;
        }
    }

    private async Task RefreshQuotaAsync()
    {
        if (_session == null) return;
        try
        {
            var q = await _session.Api.GetQuotaAsync();
            // Render in the "12 players online · 4 active rooms"
            // style from the redesign reference. Drop the /max
            // counter on the visible label — it lives in the
            // tooltip instead so the header strip stays compact.
            QuotaText.Text = $"👥 {q.Players.Active} players online   ·   🏠 {q.Lobbies.Active} active rooms";
            QuotaText.ToolTip = Strings.Format("MpQuotaBar",
                q.Players.Active, q.Players.Max,
                q.Lobbies.Active, q.Lobbies.Max);
        }
        catch
        {
            QuotaText.Text = "";
        }
    }

    /// <summary>
    /// Build one row of the rooms table. Column widths match the
    /// header Grid declared in MultiplayerTab.xaml so the columns
    /// line up. The row uses alternating zebra-stripe backgrounds
    /// driven by <paramref name="rowIndex"/> so a long list stays
    /// scannable.
    /// </summary>
    private Border BuildRoomRow(LobbySummary lobby, int rowIndex)
    {
        // Is the lobby's mod actually installed on this PC? If not,
        // the user can't join (they wouldn't pass the fingerprint
        // check). Show the row dimmed with a "mod not installed"
        // note so it's obvious why Join is disabled.
        var modInstalled = IsModInstalledLocally(lobby.ModId);
        var inGame = lobby.Status == "in_game";
        var textPrimary = (Brush)Application.Current.FindResource("TextPrimary");
        var textSecondary = (Brush)Application.Current.FindResource("TextSecondary");
        var divider = (Brush)Application.Current.FindResource("MpDivider");

        // Zebra stripes — base = MpSurface, alt = transparent.
        // Even-index rows get the subtle tint so the first row
        // doesn't bleed into the header strip above.
        Brush rowBg = (rowIndex % 2 == 0)
            ? Brushes.Transparent
            : (Brush)Application.Current.FindResource("MpSurface");

        var row = new Border
        {
            Background = rowBg,
            BorderBrush = divider,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(24, 10, 24, 10),
            Opacity = modInstalled ? 1.0 : 0.55,
        };

        var grid = new Grid();
        // Mirror the header column widths exactly.
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.4, GridUnitType.Star), MinWidth = 160 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star), MinWidth = 110 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

        // -- Col 0: favorite star. Local-only flag (not persisted
        // for v1) — purely cosmetic, but keeps the row width
        // matching the reference and gives users a click target
        // for the future "starred lobbies" feature.
        var starBtn = new Button
        {
            Content = "☆",
            Style = (Style)Application.Current.FindResource("MpIconButton"),
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Star this room (local)",
            Tag = lobby,
        };
        Grid.SetColumn(starBtn, 0);
        grid.Children.Add(starBtn);

        // -- Col 1: Room (title + optional 🔒 / private chip).
        var roomCell = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        roomCell.Children.Add(new TextBlock
        {
            Text = lobby.Title,
            Foreground = textPrimary,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (lobby.IsPrivate)
        {
            roomCell.Children.Add(new TextBlock
            {
                Text = "  🔒",
                Foreground = textSecondary,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        if (!modInstalled)
        {
            roomCell.Children.Add(new TextBlock
            {
                Text = "  · mod not installed",
                Foreground = textSecondary,
                FontSize = 11,
                FontStyle = FontStyles.Italic,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        Grid.SetColumn(roomCell, 1);
        grid.Children.Add(roomCell);

        // -- Col 2: Host display name.
        var hostText = new TextBlock
        {
            Text = lobby.Host.DisplayName,
            Foreground = textSecondary,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(hostText, 2);
        grid.Children.Add(hostText);

        // -- Col 3: Players "X/Y".
        var playersText = new TextBlock
        {
            Text = $"{lobby.CurrentPlayers} / {lobby.MaxPlayers}",
            Foreground = textPrimary,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(playersText, 3);
        grid.Children.Add(playersText);

        // -- Col 4: Ping. The Worker's /lobbies payload doesn't
        // include a per-lobby ping; we don't have one to show
        // until the user joins. Render an em-dash with a muted
        // colour so the column doesn't look empty — the reference
        // does the same for unknown values.
        var pingCell = BuildPingCell(null);
        Grid.SetColumn(pingCell, 4);
        grid.Children.Add(pingCell);

        // -- Col 5: Status dot + label.
        var statusCell = BuildStatusCell(inGame ? "In game" : "Waiting", inGame);
        Grid.SetColumn(statusCell, 5);
        grid.Children.Add(statusCell);

        // -- Col 6: Join button. Hover state turns blue (matches
        // the MpRowJoinButton template). Disabled if mod isn't
        // installed, room is full, or game is in progress.
        var joinBtn = new Button
        {
            Content = Strings.Get("MpRoomJoin"),
            Style = (Style)Application.Current.FindResource("MpRowJoinButton"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsEnabled = modInstalled
                && !inGame
                && lobby.CurrentPlayers < lobby.MaxPlayers,
            Tag = lobby,
        };
        joinBtn.Click += JoinRoomButton_Click;
        Grid.SetColumn(joinBtn, 6);
        grid.Children.Add(joinBtn);

        row.Child = grid;
        return row;
    }

    /// <summary>
    /// Render the Ping column for a row. <paramref name="rttMs"/>
    /// null = no value yet (em-dash + muted); otherwise a small
    /// "signal bars" glyph coloured by RTT bucket plus the number.
    /// </summary>
    private FrameworkElement BuildPingCell(double? rttMs)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (rttMs is null)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "—",
                Foreground = (Brush)Application.Current.FindResource("TextSecondary"),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            });
            return panel;
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
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"{(int)rtt} ms",
            Foreground = (Brush)Application.Current.FindResource("TextPrimary"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return panel;
    }

    /// <summary>
    /// Status cell: coloured dot + label. <paramref name="inGame"/>
    /// switches the dot colour (green for in-game match in progress,
    /// blue for waiting in the lobby).
    /// </summary>
    private FrameworkElement BuildStatusCell(string label, bool inGame)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 8, Height = 8,
            Fill = (Brush)Application.Current.FindResource(
                inGame ? "MpStatusInGame" : "MpStatusWaiting"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0),
        });
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = (Brush)Application.Current.FindResource("TextPrimary"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return panel;
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
            return !string.IsNullOrEmpty(state.InstallPath);
        }
        catch
        {
            return false;
        }
    }

    private async void JoinRoomButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null || _getActiveProfile == null || _computeModFingerprint == null) return;
        if (sender is not Button btn || btn.Tag is not LobbySummary lobby) return;

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
                MessageBox.Show(
                    $"This room is for {displayName}, but you don't have that mod installed yet.\n\n" +
                    "Install it from the Mods tab and try again.",
                    "Mod not installed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
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
                MessageBox.Show(
                    $"This room uses an unknown mod ('{lobby.ModId}'). The launcher can't switch to it.",
                    "Unknown mod",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            // Ask MainWindow to switch the active profile. It runs
            // the same path the Play-tab tiles use (LoadModProfile),
            // including the busy-state pre-flight (in-progress
            // install / game running blocks the switch).
            if (_switchActiveMod == null || !_switchActiveMod(target))
            {
                MessageBox.Show(
                    $"Could not switch to {target.DisplayName}. Make sure no install / update is " +
                    "in progress, then try again.",
                    "Mod switch failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
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
            MessageBox.Show(ex.Message, "Cannot fingerprint mod", MessageBoxButton.OK, MessageBoxImage.Error);
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

        btn.IsEnabled = false;
        try
        {
            // Stamp the room's mod id so LaunchActiveModGame uses
            // the right profile when the host starts the game — see
            // the same step in the create-room path above.
            _currentLobbyModId = lobby.ModId;
            // Host vs joiner is decided by the WS room_state frame that
            // arrives once we connect — clearing it here is just for the
            // brief window before that frame lands.
            await _session.JoinLobbyAsync(lobby.Id, fingerprint, password);
            // Set the lobby title eagerly so the in-room header reads
            // something better than the short id until room_state lands.
            _session.GetType(); // appease the compiler
        }
        catch (LobbyApiException ex) when (ex.Code == "mod_mismatch")
        {
            MessageBox.Show(
                "Your local mod files don't match the host. Verify or update the mod before trying again.",
                "Mod version mismatch",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not join", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            btn.IsEnabled = true;
        }
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
            AppendChatSystem("Ready saved locally — will sync when the room reconnects.");
            return;
        }

        try { await _session.RoomSocket.SendReadyAsync(ready); }
        catch (Exception ex) { DiagnosticLog.Write($"MultiplayerTab.Ready: {ex.Message}"); }
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null) return;

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
        AppendChatSystem("Starting game…");

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
                        StartCountdown(3000);
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
        StartCountdown(3000);
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
        var text = ChatInputBox.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;
        ChatInputBox.Text = "";

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
            AppendChatSystem("Cannot launch — no active mod profile.");
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
                AppendChatSystem("Could not spawn the game process.");
                return null;
            }

            // n2n virtual-LAN flow: every peer's edge.exe presents the
            // room as a real LAN segment on 10.99.0.0/24, so both host
            // and joiner just walk through AoE3's stock LAN UI — no
            // virtual IPs to copy, no Direct IP textbox to paste into.
            AppendChatSystem("Partida lanzada. En AoE3: Multiplayer → LAN.");
            return process;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"MultiplayerTab.LaunchActiveModGame: {ex.Message}");
            AppendChatSystem($"Launch failed: {ex.Message}");
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
        var radmin = RadminVpnService.GetStatus();
        if (radmin.IsServiceRunning && !string.IsNullOrEmpty(radmin.AdapterIp))
        {
            sb.Append(" OverrideAddress=").Append(radmin.AdapterIp);
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
        AppendChatSystem("Game closed.");
        try
        {
            // The mod's user-data folder usually lives under Documents/My Games/<userDataFolder>.
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var modUserData = string.IsNullOrEmpty(profile.UserDataFolder)
                ? null
                : System.IO.Path.Combine(docs, "My Games", profile.UserDataFolder);

            if (string.IsNullOrEmpty(modUserData)) return;

            var replay = ReplayUploadService.FindLatestReplay(modUserData, gameStartedAtUtc);
            if (replay != null)
                AppendChatSystem($"Replay saved: {replay.Name} ({replay.Length / 1024} KB). Upload from History.");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"MultiplayerTab.OnGameExitedAsync: {ex.Message}");
        }
        await Task.CompletedTask;
    }

    // ==================================================================
    // Floating popup: drag handlers + position management
    // ==================================================================

    /// <summary>
    /// First time the popup canvas gets a real size, center the card
    /// inside it. Subsequent size changes (launcher window resize)
    /// re-clamp the popup so it never sits half-off-screen.
    /// </summary>
    private void RoomPopupCanvas_SizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
    {
        if (RoomPopupCard == null || RoomPopupCanvas == null) return;

        if (!_popupPositionInitialised)
        {
            CenterPopup();
            _popupPositionInitialised = true;
            return;
        }
        // Re-clamp to keep the popup inside the visible area when
        // the launcher window shrinks. We don't re-center — the
        // user-dragged position is preserved as long as it fits.
        ClampPopupPosition();
    }

    private void CenterPopup()
    {
        if (RoomPopupCard == null || RoomPopupCanvas == null) return;
        var canvasW = RoomPopupCanvas.ActualWidth;
        var canvasH = RoomPopupCanvas.ActualHeight;
        if (canvasW <= 0 || canvasH <= 0) return;
        var x = Math.Max(0, (canvasW - RoomPopupCard.Width) / 2.0);
        var y = Math.Max(0, (canvasH - RoomPopupCard.Height) / 2.0);
        System.Windows.Controls.Canvas.SetLeft(RoomPopupCard, x);
        System.Windows.Controls.Canvas.SetTop(RoomPopupCard, y);
    }

    private void ClampPopupPosition()
    {
        if (RoomPopupCard == null || RoomPopupCanvas == null) return;
        var canvasW = RoomPopupCanvas.ActualWidth;
        var canvasH = RoomPopupCanvas.ActualHeight;
        var left = System.Windows.Controls.Canvas.GetLeft(RoomPopupCard);
        var top = System.Windows.Controls.Canvas.GetTop(RoomPopupCard);
        // Always keep at least 80 px of the header visible on the
        // right edge / 40 px on the others so the user can always
        // grab the popup back even if they dragged it weird.
        var minLeft = -(RoomPopupCard.Width - 200);
        var maxLeft = canvasW - 80;
        var minTop = 0.0;
        var maxTop = canvasH - 60;
        left = Math.Max(minLeft, Math.Min(maxLeft, left));
        top = Math.Max(minTop, Math.Min(maxTop, top));
        System.Windows.Controls.Canvas.SetLeft(RoomPopupCard, left);
        System.Windows.Controls.Canvas.SetTop(RoomPopupCard, top);
    }

    private void RoomHeaderStrip_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Drag is forbidden while the game is starting or in
        // progress — the popup is "locked" so the user can't
        // accidentally drag it around mid-match. Clicks still go
        // through to the X button when in Lobby phase.
        if (_matchPhase != MatchPhase.Lobby) return;

        // Don't start drag if the user clicked on a child button
        // (close X). The original source is the actual hit element;
        // if it's a Button we let WPF handle it normally.
        if (e.OriginalSource is System.Windows.Controls.Button) return;

        if (RoomPopupCard == null || RoomPopupCanvas == null) return;
        _isDraggingPopup = true;
        _dragStartCursorOnCanvas = e.GetPosition(RoomPopupCanvas);
        _dragStartCardLeft = System.Windows.Controls.Canvas.GetLeft(RoomPopupCard);
        _dragStartCardTop = System.Windows.Controls.Canvas.GetTop(RoomPopupCard);
        if (double.IsNaN(_dragStartCardLeft)) _dragStartCardLeft = 0;
        if (double.IsNaN(_dragStartCardTop)) _dragStartCardTop = 0;
        ((System.Windows.UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    private void RoomHeaderStrip_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDraggingPopup || RoomPopupCard == null || RoomPopupCanvas == null) return;
        var current = e.GetPosition(RoomPopupCanvas);
        var dx = current.X - _dragStartCursorOnCanvas.X;
        var dy = current.Y - _dragStartCursorOnCanvas.Y;
        System.Windows.Controls.Canvas.SetLeft(RoomPopupCard, _dragStartCardLeft + dx);
        System.Windows.Controls.Canvas.SetTop(RoomPopupCard, _dragStartCardTop + dy);
        ClampPopupPosition();
    }

    private void RoomHeaderStrip_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_isDraggingPopup) return;
        _isDraggingPopup = false;
        ((System.Windows.UIElement)sender).ReleaseMouseCapture();
        e.Handled = true;
    }

    // ==================================================================
    // Match lifecycle: phases + countdown + in-game overlay
    // ==================================================================

    /// <summary>
    /// Apply visual state for the current <see cref="_matchPhase"/>:
    /// shows / hides the X button, drag cursor, overlays, and the
    /// Ready / Start / Leave buttons. Idempotent — safe to call on
    /// every state change.
    /// </summary>
    private void ApplyMatchPhaseUi()
    {
        // Header drag handle: only "SizeAll" cursor when draggable.
        if (RoomHeaderStrip != null)
        {
            RoomHeaderStrip.Cursor = _matchPhase == MatchPhase.Lobby
                ? System.Windows.Input.Cursors.SizeAll
                : System.Windows.Input.Cursors.Arrow;
        }

        // Close X: hidden during Starting / InGame so a stray click
        // can't accidentally abort the match. The same behaviour is
        // enforced on the OnClosing path of the main window.
        if (RoomCloseXButton != null)
        {
            RoomCloseXButton.Visibility = _matchPhase == MatchPhase.Lobby
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        // Overlays.
        if (CountdownOverlay != null)
            CountdownOverlay.Visibility = _matchPhase == MatchPhase.Starting
                ? Visibility.Visible : Visibility.Collapsed;
        if (InGameOverlay != null)
            InGameOverlay.Visibility = _matchPhase == MatchPhase.InGame
                ? Visibility.Visible : Visibility.Collapsed;

        // Cancel button caption + style differ for host vs joiner.
        if (InGameCancelButton != null)
        {
            InGameCancelButton.Content = _isHostInCurrentRoom
                ? "⚠  Cancel game (host)"
                : "↩  Leave game";
        }
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
        if (CountdownNumber == null) return;
        // Pure local timer — no server timestamp involved, so clock
        // skew between client and server can't shortcut the wait.
        var elapsedMs = Environment.TickCount64 - _countdownStartedAtTicks;
        var remainingMs = _countdownDurationMs - elapsedMs;
        if (remainingMs <= 0)
        {
            _countdownTickTimer?.Stop();
            CountdownNumber.Text = "Go";
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
        CountdownNumber.Text = seconds.ToString();
    }

    private void CancelLocalCountdownIfRunning()
    {
        _countdownTickTimer?.Stop();
        _countdownTickTimer = null;
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

        if (InGameRoomText != null)
            InGameRoomText.Text = _session?.CurrentLobbyTitle ?? _session?.CurrentLobbyId ?? "";

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
    /// Repaint the InGame status overlay from local data: match
    /// timer, the n2n connection badge, and a list of room members
    /// alongside us in the virtual LAN. Per-peer RTT used to come
    /// from PeerMesh's STUN ping cadence — n2n hides that detail
    /// (the supernode does its own keepalive), so RTT shows as "—"
    /// for remote players.
    /// </summary>
    private void RefreshInGamePanel()
    {
        // Match timer.
        if (InGameMatchTimer != null)
        {
            var elapsedMs = Environment.TickCount64 - _matchTimerStartTicks;
            var elapsed = TimeSpan.FromMilliseconds(Math.Max(0, elapsedMs));
            InGameMatchTimer.Text = elapsed.TotalHours >= 1
                ? elapsed.ToString(@"h\:mm\:ss")
                : elapsed.ToString(@"mm\:ss");
        }

        var bridgeReady = _session?.IsP2pBridgeReady ?? false;

        // Global traffic counter — n2n doesn't surface byte totals to
        // the launcher (the edge process keeps them internally). Show
        // a dash so the field doesn't look broken.
        if (InGameTrafficText != null)
        {
            InGameTrafficText.Text = "—";
        }

        // Mode badge.
        if (InGameModeText != null)
        {
            InGameModeText.Text = bridgeReady
                ? " — Connected via virtual LAN (n2n)"
                : " — Virtual LAN starting…";
            InGameModeText.Foreground = (Brush)Application.Current.FindResource(
                bridgeReady ? "MpStatusOnline" : "MpStatusReconnect");
        }

        // Peer list. We just enumerate room members minus ourselves
        // — every member that's in the lobby IS reachable on the
        // virtual LAN as long as their edge is connected.
        if (InGamePeersPanel != null)
        {
            InGamePeersPanel.Children.Clear();
            var me = _session?.CurrentUser;
            if (me != null)
            {
                InGamePeersPanel.Children.Add(BuildInGamePeerRow(
                    login: string.IsNullOrEmpty(me.DiscordUsername) ? me.DisplayName : me.DiscordUsername,
                    state: "you",
                    rttMs: 0,
                    bytesIn: 0,
                    bytesOut: 0,
                    isSelf: true));
            }
            int peerCount = 0;
            foreach (var member in _roomMembers.Values)
            {
                if (me != null && string.Equals(member.UserId, me.Id, StringComparison.Ordinal))
                    continue;
                peerCount++;
                InGamePeersPanel.Children.Add(BuildInGamePeerRow(
                    login: member.Login,
                    state: bridgeReady ? "Virtual LAN" : "Connecting…",
                    rttMs: 0,
                    bytesIn: 0,
                    bytesOut: 0,
                    isSelf: false));
            }

            if (peerCount == 0)
            {
                InGamePeersPanel.Children.Add(new TextBlock
                {
                    Text = "Waiting for peers — you're the only player in the room right now.\n" +
                           "P2P stack ready; another launcher needs to Join this room for game traffic to flow.",
                    Foreground = (Brush)Application.Current.FindResource("TextSecondary"),
                    FontSize = 11,
                    FontStyle = FontStyles.Italic,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 12, 0, 0),
                });
            }
        }

        // "Pulsing" dot — toggle opacity for a breathing effect.
        if (InGameLiveDot != null)
        {
            InGameLiveDot.Opacity = InGameLiveDot.Opacity > 0.6 ? 0.4 : 1.0;
        }
    }

    private FrameworkElement BuildInGamePeerRow(
        string login, string state, double rttMs, long bytesIn, long bytesOut, bool isSelf)
    {
        var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) }); // name
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) }); // state
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });  // rtt
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // bytes

        var nameTb = new TextBlock
        {
            Text = login,
            Foreground = (Brush)Application.Current.FindResource("TextPrimary"),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(nameTb, 0);
        row.Children.Add(nameTb);

        var stateBrush = state switch
        {
            "Direct P2P" or "Connected" or "you" =>
                (Brush)Application.Current.FindResource("MpStatusOnline"),
            "Relay" =>
                (Brush)Application.Current.FindResource("MpPingMedium"),
            "Lost" =>
                (Brush)Application.Current.FindResource("MpStatusOffline"),
            _ => (Brush)Application.Current.FindResource("TextSecondary"),
        };
        var stateTb = new TextBlock
        {
            Text = state,
            Foreground = stateBrush,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(stateTb, 1);
        row.Children.Add(stateTb);

        var rttTb = new TextBlock
        {
            Text = isSelf ? "—" : (rttMs > 0 ? $"{(int)rttMs} ms" : "…"),
            Foreground = (Brush)Application.Current.FindResource("TextPrimary"),
            FontFamily = new System.Windows.Media.FontFamily("Consolas, Courier New, monospace"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(rttTb, 2);
        row.Children.Add(rttTb);

        var bytesTb = new TextBlock
        {
            Text = $"↑ {FormatBytes(bytesOut)}   ↓ {FormatBytes(bytesIn)}",
            Foreground = (Brush)Application.Current.FindResource("TextSecondary"),
            FontFamily = new System.Windows.Media.FontFamily("Consolas, Courier New, monospace"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(bytesTb, 3);
        row.Children.Add(bytesTb);

        return row;
    }

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
        var verb = _isHostInCurrentRoom ? "Cancel the game for everyone?" : "Leave the game?";
        var detail = _isHostInCurrentRoom
            ? "All players will be disconnected and the room returns to the lobby."
            : "AoE3 will close. The room keeps playing for the other players.";
        var result = MessageBox.Show(
            $"{verb}\n\n{detail}",
            "Multiplayer",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        await EndMatchAsync(_isHostInCurrentRoom ? "host_cancelled" : "joiner_left");
    }

    /// <summary>
    /// Shared kill path: stops the local AoE3 process, exits the
    /// InGame phase locally, and if the user is the host, asks the
    /// Worker to broadcast game_cancelled. Idempotent — calling
    /// twice (e.g. host cancel + window-close confirm) is safe.
    /// </summary>
    private async Task EndMatchAsync(string reason)
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

        if (_isHostInCurrentRoom && _session?.RoomSocket != null)
        {
            try { await _session.RoomSocket.SendCancelGameAsync(reason); }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"MultiplayerTab.EndMatch: SendCancelGameAsync — {ex.Message}");
            }
        }
        AppendChatSystem(_isHostInCurrentRoom
            ? "You cancelled the game. Room returned to lobby."
            : "You left the game. Other players continue.");
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
        await EndMatchAsync("launcher_closed");
        return true;
    }

}
