using System;
using System.Linq;
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
using WarsOfLibertyLauncher.Services.Multiplayer.P2P;

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

    private Subtab _activeSubtab = Subtab.Rooms;
    private bool _isRefreshingList;
    private bool _isRefreshingHistory;
    private bool _isProbingVlan;

    /// <summary>
    /// State of the WinDivert-based P2P stack:
    ///   * <c>missing</c> — driver files not on disk, need download.
    ///   * <c>needs_elevation</c> — files present but the current
    ///     process can't open WinDivert (no admin).
    ///   * <c>ready</c> — IsAvailable() returns true.
    /// </summary>
    private string _vlanState = "unknown";

    private System.Windows.Threading.DispatcherTimer? _quotaTimer;

    /// <summary>
    /// Last NAT probe result. Cached so re-renders of the header don't
    /// re-probe; refreshed on demand by <see cref="ProbeNatTypeAsync"/>.
    /// </summary>
    private NatProbeResult? _natProbe;

    /// <summary>Currently-subscribed WS, so we can unsubscribe cleanly on room change.</summary>
    private LobbyWebSocket? _attachedSocket;

    /// <summary>Mesh we're listening to for peer-state transitions; cleared on room change.</summary>
    private PeerMesh? _attachedMesh;

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

    public MultiplayerTab()
    {
        InitializeComponent();
        ApplyStrings();
        // Initial state is the signed-out gate; once Attach() runs we
        // re-render against the real session.
        RefreshFromSession();
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
        Func<ModProfile, EventHandler, string?, System.Diagnostics.Process?>? launchGame = null)
    {
        if (_session != null)
            _session.StateChanged -= OnSessionStateChanged;

        _session = session;
        _getActiveProfile = getActiveProfile;
        _computeModFingerprint = computeModFingerprint;
        _launchGame = launchGame;
        session.StateChanged += OnSessionStateChanged;

        RefreshFromSession();

        // Fire-and-forget refreshes — UI-thread safe because each one
        // marshals back via Dispatcher.InvokeAsync in its own continuation.
        _ = RefreshQuotaAsync();
        _ = ProbeNatTypeAsync();
        _ = ProbeVlanAsync();
        if (session.Status == MultiplayerSession.SessionStatus.SignedIn)
            _ = RefreshRoomsListAsync();

        // Auto-refresh the quota bar every 60 s while this control is
        // alive. The bar surfaces server load + remaining daily budget;
        // refreshing keeps the "Server full" indicator accurate without
        // the user having to navigate away and back.
        _quotaTimer?.Stop();
        _quotaTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(60),
        };
        _quotaTimer.Tick += async (_, _) => await RefreshQuotaAsync();
        _quotaTimer.Start();
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
    /// Paint the NAT badge from <see cref="_natProbe"/>. Idempotent.
    /// Called on every language/state refresh so the badge keeps its
    /// tooltip + colour in sync.
    /// </summary>
    private void RenderNatBadge()
    {
        Brush bgPanelAlt = (Brush)Application.Current.FindResource("BgPanelAlt");
        Brush textSecondary = (Brush)Application.Current.FindResource("TextSecondary");

        if (_natProbe == null)
        {
            NatBadgeText.Text = Strings.Format("MpNatBadge", Strings.Get("MpNatProbing"));
            NatBadgeBorder.Background = bgPanelAlt;
            NatBadgeText.Foreground = textSecondary;
            NatBadgeBorder.ToolTip = null;
            return;
        }

        // Map enum to label + colour + tooltip. Colours pulled from a
        // simple "traffic light" palette so users grok at a glance:
        //   Open      → green
        //   Moderate  → green-ish
        //   Strict    → amber
        //   Symmetric → red
        //   Unknown   → grey
        var (labelKey, descKey, bg, fg) = _natProbe.Type switch
        {
            NatType.Open      => ("Open",     "MpNatOpen",      "#1f3d1f", "#9aff9a"),
            NatType.Moderate  => ("Moderate", "MpNatModerate",  "#1f3d2f", "#9affd1"),
            NatType.Strict    => ("Strict",   "MpNatStrict",    "#3d2f1f", "#ffcc88"),
            NatType.Symmetric => ("Symmetric","MpNatSymmetric", "#3d1f1f", "#ff9090"),
            _                 => ("Unknown",  "MpNatUnknown",   "#2a2d34", "#888888"),
        };

        NatBadgeText.Text = Strings.Format("MpNatBadge", labelKey);
        NatBadgeBorder.Background = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(bg));
        NatBadgeText.Foreground = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(fg));

        var tip = Strings.Get(descKey);
        if (_natProbe.PublicEndpoint != null)
            tip += $"\nPublic address: {_natProbe.PublicEndpoint}";
        if (!string.IsNullOrEmpty(_natProbe.ErrorMessage))
            tip += $"\n{_natProbe.ErrorMessage}";
        NatBadgeBorder.ToolTip = tip;
    }

    /// <summary>
    /// Run the NAT type probe in the background. Two STUN Binding
    /// Requests against different public servers. Result is cached
    /// in <see cref="_natProbe"/> and rendered via
    /// <see cref="RenderNatBadge"/>.
    /// </summary>
    private async Task ProbeNatTypeAsync()
    {
        try
        {
            _natProbe = await NatTypeDetector.DetectAsync();
            DiagnosticLog.Write(
                $"NAT probe: type={_natProbe.Type} public={_natProbe.PublicEndpoint} " +
                $"secondary={_natProbe.SecondaryEndpoint} err={_natProbe.ErrorMessage}");
            await Dispatcher.InvokeAsync(RenderNatBadge);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"MultiplayerTab.ProbeNatTypeAsync: {ex.Message}");
            _natProbe = new NatProbeResult(NatType.Unknown, null, null, ex.Message);
            await Dispatcher.InvokeAsync(RenderNatBadge);
        }
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
        var nextMesh = s?.Mesh;

        var socketChanged = !ReferenceEquals(_attachedSocket, nextSocket);
        var meshChanged = !ReferenceEquals(_attachedMesh, nextMesh);
        if (!socketChanged && !meshChanged) return;

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
            // status pill goes back to plain Connected.
            _isReconnecting = false;
            UpdateConnectionStatus();
        }

        if (meshChanged)
        {
            if (_attachedMesh != null)
                _attachedMesh.PeerStateChanged -= OnPeerStateChanged;
            _attachedMesh = nextMesh;
            if (_attachedMesh != null)
                _attachedMesh.PeerStateChanged += OnPeerStateChanged;
        }

        // Reset per-room UI state whenever we change rooms (we keep
        // the existing chat/members when only the mesh changed mid-
        // session, which shouldn't happen but doesn't hurt to guard).
        if (socketChanged)
        {
            _roomMembers.Clear();
            _roomHostUserId = null;
            _isHostInCurrentRoom = false;
            ChatLogPanel.Children.Clear();
            RoomMembersPanel.Children.Clear();

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
                    Login = string.IsNullOrEmpty(me.GithubLogin) ? me.DisplayName : me.GithubLogin,
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

    /// <summary>
    /// Render peer connection transitions as system chat lines. The
    /// mesh fires this from a background thread; marshal to UI.
    /// </summary>
    private PeerLinkState _lastLoggedState = (PeerLinkState)(-1);
    private string _lastLoggedUser = "";

    private void OnPeerStateChanged(object? sender, PeerChannel ch) =>
        Dispatcher.InvokeAsync(() =>
        {
            // Repaint the members panel so the RTT column reflects the
            // new state / latency sample. This fires on every pong
            // arrival (every 2 s while Connected) — cheap because the
            // panel only holds N members.
            RenderRoomMembers();

            // Avoid spamming the chat with one line per pong: only
            // emit a system line on actual state transitions.
            if (ch.State == _lastLoggedState && ch.UserId == _lastLoggedUser) return;
            _lastLoggedState = ch.State;
            _lastLoggedUser = ch.UserId;

            var label = ch.State switch
            {
                PeerLinkState.Discovering => "discovering",
                PeerLinkState.Punching => "punching",
                PeerLinkState.Connected => $"direct ({ch.ConfirmedEndpoint})",
                PeerLinkState.Lost => "lost — retrying",
                PeerLinkState.Failed => "direct failed (need relay)",
                _ => ch.State.ToString(),
            };
            AppendChatSystem($"P2P [{ch.Login}]: {label}");
        });

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
                    case "game_started":
                        AppendChatSystem("The game has started.");
                        RefreshFromSession();
                        LaunchActiveModGame();
                        break;
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
            _roomMembers[kv.Key] = new RoomMemberEntry
            {
                UserId = kv.Key,
                // Prefer the server-provided login; fall back to the
                // user id for legacy rooms that don't carry it yet.
                Login = string.IsNullOrEmpty(kv.Value.Login) ? kv.Key : kv.Value.Login,
                Ready = kv.Value.Ready,
            };
        }
        _roomHostUserId = state.HostUserId;
        _isHostInCurrentRoom = !string.IsNullOrEmpty(_session?.CurrentUser?.Id)
            && string.Equals(_roomHostUserId, _session!.CurrentUser!.Id, StringComparison.Ordinal);

        // Replay any chat history the DO buffered for us.
        ChatLogPanel.Children.Clear();
        foreach (var line in state.Chat) AppendChatLine(line);

        RenderRoomMembers();
        RenderRoomPanel();
    }

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
        var login = json.TryGetProperty("github_login", out var l) ? (l.GetString() ?? userId) : userId;

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

        var header = new TextBlock
        {
            Text = Strings.Get("MpRoomMembersHeader").ToUpperInvariant(),
            Foreground = (Brush)Application.Current.FindResource("MpTableHeader"),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        };
        RoomMembersPanel.Children.Add(header);

        foreach (var m in _roomMembers.Values)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 2, 0, 2),
            };

            // Ready indicator — green dot for ready, dim for not. Kept
            // small so the row stays scannable even with 8 players.
            row.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = m.Ready ? Brushes.LimeGreen : new SolidColorBrush(Color.FromRgb(0x55, 0x59, 0x5f)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            });

            var label = new TextBlock
            {
                Text = m.Login,
                Foreground = (Brush)Application.Current.FindResource("TextPrimary"),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            };
            if (string.Equals(m.UserId, _roomHostUserId, StringComparison.Ordinal))
                label.Text += "  ·  host";
            row.Children.Add(label);

            // Per-peer P2P quality: look up the mesh channel for this
            // member and render the RTT + a colour-coded dot. The mesh
            // is null when WinDivert isn't loaded (legacy ZeroTier path);
            // in that case we just skip the column rather than show
            // misleading "0 ms" values.
            var mesh = _session?.Mesh;
            if (mesh != null)
            {
                PeerChannel? ch = null;
                foreach (var candidate in mesh.Peers)
                {
                    if (string.Equals(candidate.UserId, m.UserId, StringComparison.Ordinal))
                    {
                        ch = candidate;
                        break;
                    }
                }
                if (ch != null)
                {
                    var (rttText, rttBrush) = ch.State switch
                    {
                        PeerLinkState.Connected when ch.RttMs >= 0 =>
                            ($"{(int)ch.RttMs} ms",
                                ch.RttMs < 80 ? Brushes.LimeGreen :
                                ch.RttMs < 200 ? Brushes.Goldenrod : Brushes.IndianRed),
                        PeerLinkState.Connected => ("…", (Brush)Application.Current.FindResource("TextSecondary")),
                        PeerLinkState.Punching => ("punching", Brushes.Goldenrod),
                        PeerLinkState.Lost => ("lost", Brushes.IndianRed),
                        PeerLinkState.Failed => ("relay", Brushes.IndianRed),
                        _ => ("…", (Brush)Application.Current.FindResource("TextSecondary")),
                    };

                    row.Children.Add(new TextBlock
                    {
                        Text = "  ·  " + rttText,
                        Foreground = rttBrush,
                        FontSize = 11,
                        VerticalAlignment = VerticalAlignment.Center,
                    });
                }
            }

            RoomMembersPanel.Children.Add(row);
        }
    }

    private void AppendChatLine(WsChatLine line)
    {
        var when = DateTimeOffset.FromUnixTimeMilliseconds(line.AtMs).LocalDateTime;
        AppendChatRaw($"{when:HH:mm}  {line.Login}: {line.Body}",
            (Brush)Application.Current.FindResource("TextPrimary"));
    }

    private void AppendChatSystem(string body) =>
        AppendChatRaw($"— {body}",
            (Brush)Application.Current.FindResource("TextSecondary"));

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
        // Cap the in-memory log so a marathon session doesn't bloat
        // the visual tree. 500 lines ≈ 7 hours of moderate chat.
        while (ChatLogPanel.Children.Count > 500)
            ChatLogPanel.Children.RemoveAt(0);
        // Auto-scroll on every append. Users who scroll up manually
        // lose the auto-follow until they scroll back to the bottom —
        // mirrors Discord / IRC behaviour without needing a position
        // tracker for v1.0.
        ChatScroll.ScrollToBottom();
    }

    /// <summary>
    /// Append a one-line entry to the bottom "global lobby chat"
    /// panel. Used for connection-state events (disconnect /
    /// reconnect) and any cross-room announcement we want to
    /// surface without polluting the in-room chat log.
    ///
    /// Rendered with a [System] tag and a blue tag colour to
    /// match the redesign reference. Capped at 200 lines.
    /// </summary>
    private void AppendGlobalSystemEvent(string body)
    {
        if (GlobalChatLogPanel == null) return;

        var line = new TextBlock
        {
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 1, 0, 1),
        };
        line.Inlines.Add(new System.Windows.Documents.Run("[System] ")
        {
            Foreground = (Brush)Application.Current.FindResource("MpStatusWaiting"),
            FontWeight = FontWeights.SemiBold,
        });
        line.Inlines.Add(new System.Windows.Documents.Run(body)
        {
            Foreground = (Brush)Application.Current.FindResource("TextSecondary"),
        });
        GlobalChatLogPanel.Children.Add(line);
        while (GlobalChatLogPanel.Children.Count > 200)
            GlobalChatLogPanel.Children.RemoveAt(0);
        GlobalChatScroll?.ScrollToBottom();
    }

    // ---------- Global chat bar interactions ----------

    /// <summary>
    /// Collapse / expand the bottom global lobby-chat panel. Only
    /// the header strip stays visible when collapsed so the room
    /// view gets the full vertical space back.
    /// </summary>
    private void GlobalChatToggle_Click(object sender, RoutedEventArgs e)
    {
        if (GlobalChatBody == null) return;
        var nowVisible = GlobalChatBody.Visibility != Visibility.Visible;
        GlobalChatBody.Visibility = nowVisible ? Visibility.Visible : Visibility.Collapsed;
        if (GlobalChatCaret != null)
            GlobalChatCaret.Text = nowVisible ? " ▲" : " ▼";
    }

    /// <summary>
    /// Direct-IP join shortcut placeholder. Future work — the
    /// connect-by-IP flow lives behind a small dialog; for v1.0
    /// of the redesign the button is wired but the dialog isn't
    /// implemented yet, so we just announce the gap rather than
    /// silently no-op.
    /// </summary>
    private void JoinWithIpButton_Click(object sender, RoutedEventArgs e)
    {
        AppendGlobalSystemEvent("Direct-IP join is not available yet — coming in a future build.");
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
        // WinDivert gate — without the P2P driver, AoE3 broadcasts
        // can't reach other players. We bias toward showing the
        // bootstrap card unless we KNOW the driver is ready; an
        // unknown state is still treated as "needs setup" so users
        // don't sign in only to fail at game launch.
        if (_vlanState == "missing" || _vlanState == "needs_elevation")
        {
            ShowVlanBootstrapPanel();
            return;
        }

        VlanBootstrapPanel.Visibility = Visibility.Collapsed;

        if (_session == null
            || _session.Status != MultiplayerSession.SessionStatus.SignedIn)
        {
            ShowSignInPanel(_session?.LastError);
            return;
        }

        // In a room? Hide the browser and let the room view take
        // the full multiplayer-tab body — the redesign brief
        // explicitly calls out that the room should feel like a
        // real lobby screen, not a small floating modal on top of
        // an unrelated background.
        if (_session.Lobby == MultiplayerSession.LobbyStatus.InLobby
            || _session.Lobby == MultiplayerSession.LobbyStatus.InGame
            || _session.Lobby == MultiplayerSession.LobbyStatus.Joining
            || _session.Lobby == MultiplayerSession.LobbyStatus.Leaving)
        {
            SignInPanel.Visibility = Visibility.Collapsed;
            BrowserPanel.Visibility = Visibility.Collapsed;
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

    /// <summary>
    /// Paint the WinDivert bootstrap card. Mirrors the ZT bootstrap
    /// flow but uses our own installer instead of an external MSI.
    /// </summary>
    private void ShowVlanBootstrapPanel()
    {
        VlanBootstrapPanel.Visibility = Visibility.Visible;
        SignInPanel.Visibility = Visibility.Collapsed;
        BrowserPanel.Visibility = Visibility.Collapsed;
        RoomPanel.Visibility = Visibility.Collapsed;
        VlanErrorText.Visibility = Visibility.Collapsed;
        VlanBootstrapProgress.Visibility = Visibility.Collapsed;

        VlanBootstrapTitle.Text = Strings.Get("MpVlanNotInstalledTitle");
        if (_vlanState == "needs_elevation")
        {
            VlanBootstrapBody.Text = Strings.Get("MpVlanElevateBody");
            VlanActionButton.Content = Strings.Get("MpVlanElevate");
        }
        else
        {
            VlanBootstrapBody.Text = Strings.Get("MpVlanNotInstalledBody");
            VlanActionButton.Content = Strings.Get("MpVlanInstall");
        }
    }

    private async Task ProbeVlanAsync()
    {
        if (_isProbingVlan) return;
        _isProbingVlan = true;
        try
        {
            // Three-state classifier:
            //   files missing → bootstrap card shows "Install"
            //   files present + IsAvailable false → "needs_elevation"
            //   files present + IsAvailable true  → ready
            string next;
            if (!WinDivertInstaller.IsInstalled())
                next = "missing";
            else if (!WinDivertNative.IsAvailable())
                next = "needs_elevation";
            else
                next = "ready";

            _vlanState = next;
            await Dispatcher.InvokeAsync(RefreshFromSession);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"MultiplayerTab.ProbeVlanAsync: {ex.Message}");
        }
        finally
        {
            _isProbingVlan = false;
        }
    }

    private async void VlanActionButton_Click(object sender, RoutedEventArgs e)
    {
        VlanErrorText.Visibility = Visibility.Collapsed;
        VlanActionButton.IsEnabled = false;
        VlanBootstrapProgress.Visibility = Visibility.Visible;
        try
        {
            if (_vlanState == "needs_elevation")
            {
                // Loading the WinDivert kernel driver needs admin. We
                // relaunch ourselves elevated; the elevated instance
                // re-runs the probe and sees ready=true.
                if (!WinDivertInstaller.RelaunchElevated())
                {
                    VlanErrorText.Text = "Admin rights are needed once to load the driver.";
                    VlanErrorText.Visibility = Visibility.Visible;
                    return;
                }
                // RelaunchElevated calls Application.Current.Shutdown()
                // on success; if we reach here, the new instance is
                // running and we'll exit shortly.
                return;
            }

            // Missing — download the release zip and unpack.
            VlanActionButton.Content = Strings.Get("MpVlanInstalling");
            var progress = new Progress<double>(p =>
            {
                VlanBootstrapProgress.IsIndeterminate = false;
                VlanBootstrapProgress.Maximum = 100;
                VlanBootstrapProgress.Value = p * 100;
            });
            VlanBootstrapProgress.IsIndeterminate = false;

            var result = await WinDivertInstaller.EnsureInstalledAsync(progress);
            if (result == WinDivertInstallResult.DownloadFailed
                || result == WinDivertInstallResult.InstallFailed)
            {
                VlanErrorText.Text = "Could not install the P2P driver. Check launcher-debug.log.";
                VlanErrorText.Visibility = Visibility.Visible;
                VlanActionButton.Content = Strings.Get("MpVlanInstall");
                return;
            }

            // Re-probe — typically transitions to needs_elevation now
            // (files on disk but driver not loaded yet).
            await ProbeVlanAsync();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"MultiplayerTab.VlanActionButton_Click: {ex.Message}");
            VlanErrorText.Text = ex.Message;
            VlanErrorText.Visibility = Visibility.Visible;
        }
        finally
        {
            VlanActionButton.IsEnabled = true;
            VlanBootstrapProgress.Visibility = Visibility.Collapsed;
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
            ? $"@{user.GithubLogin}"
            : "";

        // Fill the avatar circle either with the user's GitHub
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
                UserAvatarInitial.Text = !string.IsNullOrEmpty(user?.GithubLogin)
                    ? user.GithubLogin.Substring(0, 1).ToUpperInvariant()
                    : "?";
            }
        }
        catch
        {
            // BitmapImage throws on malformed URLs; fall back to
            // the initial so the toolbar still renders cleanly.
            UserAvatarBrush.ImageSource = null;
            UserAvatarInitial.Text = !string.IsNullOrEmpty(user?.GithubLogin)
                ? user.GithubLogin.Substring(0, 1).ToUpperInvariant()
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

        // P2P readiness: with the WinDivert virtual-LAN stack, the
        // "ready to play" condition is whether the local capture is
        // running AND at least one peer is Connected on the mesh.
        // For solo rooms (host alone) we still show "P2P ready" once
        // VirtualLan is up — peers will join later.
        var p2pReady = s.IsVirtualLanActive;
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
        // the virtual LAN is active so AoE3 launches into a working
        // network rather than discovering nothing.
        StartButton.Visibility = _isHostInCurrentRoom
            ? Visibility.Visible
            : Visibility.Collapsed;
        StartButton.IsEnabled = _isHostInCurrentRoom && s.IsVirtualLanActive;
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
        ProfileLoginText.Text = $"@{user.GithubLogin}";
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
            DiagnosticLog.Write($"CreateRoom: dialog returned lobby id {dlg.CreatedLobby.Id}, entering room");
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
        // The mod the user has active must match the lobby's mod; mod
        // browser switching is a separate UX flow.
        if (!string.Equals(profile.Id, lobby.ModId, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                $"This room is for {lobby.ModId}. Switch to that mod first.",
                "Wrong mod active",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
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

        // Host-side semantics: clicking Start ALWAYS launches AoE3 on
        // this PC. The WS `start` frame is just the "tell the other
        // peers to launch too" signal — best-effort. Previously we
        // waited for the DO's `game_started` echo before launching
        // locally, which meant a transient WS drop (quick-tunnel
        // idle disconnect) silently swallowed the launch.
        AppendChatSystem("Starting game…");
        LaunchActiveModGame();

        if (_session.RoomSocket != null)
        {
            try
            {
                await _session.RoomSocket.SendStartAsync();
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"MultiplayerTab.Start (notify peers): {ex.Message}");
            }
        }
        else
        {
            DiagnosticLog.Write("MultiplayerTab.Start: WS down — peers will pick up via room_state on reconnect");
        }
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

        var login = string.IsNullOrEmpty(_session.CurrentUser.GithubLogin)
            ? _session.CurrentUser.DisplayName
            : _session.CurrentUser.GithubLogin;

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
        // latency. The server will broadcast a `chat` frame including
        // OUR message back to us; AppendChatLine for that will draw a
        // second line. Accept that minor double-up rather than building
        // an id-based dedup table for the v1 release.
        AppendChatRaw(
            $"{DateTime.Now:HH:mm}  {login}: {text}",
            (Brush)Application.Current.FindResource("TextPrimary"));

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
    private void LaunchActiveModGame()
    {
        if (_launchGame == null || _getActiveProfile == null) return;

        var profile = _getActiveProfile();
        if (profile == null)
        {
            AppendChatSystem("Cannot launch — no active mod profile.");
            return;
        }

        try
        {
            var extraArgs = BuildMultiplayerLaunchArgs();
            DiagnosticLog.Write($"MultiplayerTab.LaunchActiveModGame: extraArgs='{extraArgs}'");

            var gameStartedAt = DateTime.UtcNow;
            var process = _launchGame(profile, async (_, _) =>
            {
                // Run on the UI thread so we can render chat messages
                // and access session state safely.
                await Dispatcher.InvokeAsync(async () => await OnGameExitedAsync(profile, gameStartedAt));
            }, extraArgs);

            if (process == null)
            {
                AppendChatSystem("Could not spawn the game process.");
                return;
            }

            AppendChatSystem(_isHostInCurrentRoom
                ? "Game launched. In AoE3: click Multiplayer → LAN → Host Game."
                : "Game launched. In AoE3: click Multiplayer → LAN → Join Game.");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"MultiplayerTab.LaunchActiveModGame: {ex.Message}");
            AppendChatSystem($"Launch failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Builds the AoE3 command-line tail for the current room context.
    /// All flag names here were verified against age3y.exe's string
    /// table (Wars of Liberty 1.2.0c2) — the previous attempt with
    /// <c>+nostartup +nodialog +mp</c> failed because none of those
    /// tokens exist in the binary. Confirmed flags (with the descriptive
    /// text the engine prints when it lists switches):
    ///   * <c>+noIntroCinematics</c> — "suppresses intro cinematics on app start"
    ///   * <c>+disableESOProfile</c> — "toggles the use of ESO for storing the player profile"
    ///   * <c>+dontDetectNAT</c>     — "Doth we not detect NAT addresses?"
    ///   * <c>+OverrideAddress &lt;ip&gt;</c> / <c>+OverridePort &lt;port&gt;</c>
    ///       — undocumented but listed; used by ESO's "address-grabbing
    ///         server" path so AoE3 advertises a chosen IP/port instead
    ///         of probing the local NIC. Voobly-style IP spoofing.
    ///
    /// AoE3 has NO command-line flag to auto-host or auto-join a LAN
    /// game (we searched for hostmpgame / joinIPaddr / joinmpgame /
    /// jumpTo etc. — none exist). The classic Voobly/GameRanger flow
    /// drove the menus via SendInput from outside the process; that's
    /// a separate Fase 2.5 if we want it. For now, the player still
    /// has to click "Multiplayer → LAN" once after the game opens, but
    /// the launcher has cut every other startup delay we can cut.
    /// </summary>
    private string BuildMultiplayerLaunchArgs()
    {
        // The intro / ESO / NAT skips are always safe to apply: they
        // just kill the splash + the long "connecting to ESO" wait.
        // Use a StringBuilder so we can append room-context flags
        // conditionally without paying for repeated string copies.
        var sb = new System.Text.StringBuilder("+noIntroCinematics +disableESOProfile +dontDetectNAT");

        // OverrideAddress is the Voobly-style fake-LAN trick. We only
        // do it when the WinDivert virtual LAN is up — otherwise the
        // address we'd advertise has nobody to route it. AllocateIpFor
        // is deterministic, so every peer in the room derives the same
        // 10.147.x.y mapping for the same user-id.
        var session = _session;
        var vlan = session?.VirtualLan;
        var myUserId = session?.CurrentUser?.Id;
        if (vlan != null && !string.IsNullOrEmpty(myUserId))
        {
            var myVip = vlan.AllocateIpFor(myUserId!);
            sb.Append(" +OverrideAddress ").Append(myVip.ToString());

            // Pin the LAN port too. AoE3 LAN games default to 2300
            // (DirectPlay); fixing it makes the host/join match
            // predictable across both ends.
            sb.Append(" +OverridePort ").Append(LanGamePort);
            sb.Append(" +hostPort ").Append(LanGamePort);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Fixed UDP port both ends advertise via <c>+OverridePort</c> /
    /// <c>+hostPort</c>. 2300 is the AoE3-era DirectPlay default and
    /// what the LAN browser scans first. WinDivert's capture filter
    /// (2200-2500) is already wider than this, so the virtual LAN
    /// service picks the host packets up regardless.
    /// </summary>
    private const int LanGamePort = 2300;

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
}
