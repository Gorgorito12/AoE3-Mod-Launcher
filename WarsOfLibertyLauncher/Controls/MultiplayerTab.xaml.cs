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

    private Subtab _activeSubtab = Subtab.Rooms;
    private bool _isRefreshingList;
    private bool _isRefreshingHistory;
    private bool _isProbingZt;

    /// <summary>
    /// Last known state of the local ZeroTier install. Refreshed on
    /// every <c>StateChanged</c> pass; drives whether the ZT bootstrap
    /// gate or the sign-in/browser are visible.
    /// </summary>
    private ZeroTierState _ztState = ZeroTierState.Unknown;

    private System.Windows.Threading.DispatcherTimer? _quotaTimer;

    /// <summary>Currently-subscribed WS, so we can unsubscribe cleanly on room change.</summary>
    private LobbyWebSocket? _attachedSocket;

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
        Func<ModProfile, Task<string>> computeModFingerprint)
    {
        if (_session != null)
            _session.StateChanged -= OnSessionStateChanged;

        _session = session;
        _getActiveProfile = getActiveProfile;
        _computeModFingerprint = computeModFingerprint;
        session.StateChanged += OnSessionStateChanged;

        RefreshFromSession();

        // Fire-and-forget refreshes — UI-thread safe because each one
        // marshals back via Dispatcher.InvokeAsync in its own continuation.
        _ = RefreshQuotaAsync();
        _ = ProbeZeroTierAsync();
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
        RefreshButton.Content = Strings.Get("MpRoomsRefresh");
        CreateRoomButton.Content = Strings.Get("MpRoomsCreate");

        ReadyButton.Content = Strings.Get("MpRoomReady");
        StartButton.Content = Strings.Get("MpRoomStart");
        LeaveRoomButton.Content = Strings.Get("MpRoomLeave");
        ChatInputBox.Tag = Strings.Get("MpRoomChatPlaceholder");

        UpdateSubtabHighlights();
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
        if (ReferenceEquals(_attachedSocket, nextSocket)) return;

        if (_attachedSocket != null)
        {
            _attachedSocket.FrameReceived -= OnRoomFrame;
            _attachedSocket.Disconnected -= OnRoomDisconnected;
        }

        _attachedSocket = nextSocket;

        // Reset per-room UI state whenever the underlying socket
        // changes — entering a new room, or leaving one.
        _roomMembers.Clear();
        _roomHostUserId = null;
        _isHostInCurrentRoom = false;
        ChatLogPanel.Children.Clear();
        RoomMembersPanel.Children.Clear();

        if (nextSocket != null)
        {
            nextSocket.FrameReceived += OnRoomFrame;
            nextSocket.Disconnected += OnRoomDisconnected;
        }
    }

    private void OnRoomDisconnected(object? sender, string reason) =>
        Dispatcher.InvokeAsync(() =>
        {
            AppendChatSystem($"Disconnected: {reason}. Reconnecting…");
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
        var state = JsonSerializer.Deserialize<WsRoomState>(json.GetRawText());
        if (state == null) return;

        _roomMembers.Clear();
        foreach (var kv in state.Members)
        {
            _roomMembers[kv.Key] = new RoomMemberEntry
            {
                UserId = kv.Key,
                Login = kv.Key,        // login filled in by member_joined frames
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
            Text = Strings.Get("MpRoomMembersHeader"),
            Foreground = (Brush)Application.Current.FindResource("TextSecondary"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6),
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

    private void RefreshFromSession()
    {
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
        // ZeroTier gate first — without a working daemon the rest of
        // the flow can't succeed. The probe runs async; until it
        // returns _ztState stays Unknown which we treat as "OK so
        // far" so the UI doesn't flash a transient install card.
        if (_ztState != ZeroTierState.Running && _ztState != ZeroTierState.Unknown)
        {
            ShowZtBootstrapPanel();
            return;
        }

        ZtBootstrapPanel.Visibility = Visibility.Collapsed;

        if (_session == null
            || _session.Status != MultiplayerSession.SessionStatus.SignedIn)
        {
            ShowSignInPanel(_session?.LastError);
            return;
        }

        // In a room? Show the room panel; otherwise the browser.
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
    /// Paint the bootstrap card with text/button tailored to the current
    /// ZeroTier state. The button click handler picks which action to
    /// fire by inspecting <see cref="_ztState"/> again, so this method
    /// stays purely presentational.
    /// </summary>
    private void ShowZtBootstrapPanel()
    {
        ZtBootstrapPanel.Visibility = Visibility.Visible;
        SignInPanel.Visibility = Visibility.Collapsed;
        BrowserPanel.Visibility = Visibility.Collapsed;
        RoomPanel.Visibility = Visibility.Collapsed;
        ZtErrorText.Visibility = Visibility.Collapsed;
        ZtBootstrapProgress.Visibility = Visibility.Collapsed;

        ZtBootstrapTitle.Text = Strings.Get("MpZtNotInstalledTitle");
        switch (_ztState)
        {
            case ZeroTierState.NotInstalled:
                ZtBootstrapBody.Text = Strings.Get("MpZtNotInstalledBody");
                ZtActionButton.Content = Strings.Get("MpZtInstall");
                break;
            case ZeroTierState.InstalledServiceDown:
                ZtBootstrapBody.Text = Strings.Get("MpZtStarting");
                ZtActionButton.Content = Strings.Get("MpZtStarting");
                break;
            case ZeroTierState.RunningNotAuthorized:
                ZtBootstrapBody.Text = Strings.Get("MpZtAuthorizeBody");
                ZtActionButton.Content = Strings.Get("MpZtAuthorize");
                break;
            default:
                ZtBootstrapBody.Text = Strings.Get("MpZtNotInstalledBody");
                ZtActionButton.Content = Strings.Get("MpZtInstall");
                break;
        }
    }

    private async Task ProbeZeroTierAsync()
    {
        if (_isProbingZt) return;
        _isProbingZt = true;
        try
        {
            _ztState = await ZeroTierService.DetectAsync();
            RefreshFromSession();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"MultiplayerTab.ProbeZeroTierAsync: {ex.Message}");
        }
        finally
        {
            _isProbingZt = false;
        }
    }

    private async void ZtActionButton_Click(object sender, RoutedEventArgs e)
    {
        ZtErrorText.Visibility = Visibility.Collapsed;
        ZtActionButton.IsEnabled = false;
        ZtBootstrapProgress.Visibility = Visibility.Visible;
        try
        {
            switch (_ztState)
            {
                case ZeroTierState.NotInstalled:
                    var progress = new Progress<double>(p =>
                    {
                        ZtBootstrapProgress.IsIndeterminate = false;
                        ZtBootstrapProgress.Value = p * 100.0;
                        ZtBootstrapProgress.Maximum = 100.0;
                    });
                    ZtBootstrapProgress.IsIndeterminate = false;
                    var installResult = await ZeroTierService.InstallAsync(progress);
                    if (installResult == ZeroTierInstallResult.UserDeclinedElevation)
                    {
                        ShowZtError("Installation needs admin rights. Try again when ready.");
                        return;
                    }
                    if (installResult == ZeroTierInstallResult.DownloadFailed
                        || installResult == ZeroTierInstallResult.InstallFailed)
                    {
                        ShowZtError("ZeroTier install failed — see launcher-debug.log.");
                        return;
                    }
                    break;

                case ZeroTierState.InstalledServiceDown:
                    ZtBootstrapProgress.IsIndeterminate = true;
                    if (!await ZeroTierService.StartServiceAsync())
                    {
                        ShowZtError("Could not start the ZeroTier service. Try again with admin rights.");
                        return;
                    }
                    break;

                case ZeroTierState.RunningNotAuthorized:
                    ZtBootstrapProgress.IsIndeterminate = true;
                    if (!await ZeroTierService.EnsureUserAuthTokenAsync())
                    {
                        ShowZtError("Could not read the local API token. Try again with admin rights.");
                        return;
                    }
                    break;
            }

            // Give the daemon a moment to come up after install/start.
            await Task.Delay(TimeSpan.FromSeconds(2));
            await ProbeZeroTierAsync();
        }
        catch (Exception ex)
        {
            ShowZtError(ex.Message);
        }
        finally
        {
            ZtActionButton.IsEnabled = true;
            ZtBootstrapProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowZtError(string message)
    {
        ZtErrorText.Text = message;
        ZtErrorText.Visibility = Visibility.Visible;
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
        RoomMetaText.Text = $"{status} · ZT {s.CurrentZtNetworkId ?? "-"}";
        // The Start button only appears for the host — we approximate
        // "host" by the absence of a join token (the host enters via
        // SessionToken). A real isHost flag is read off room_state
        // when it arrives.
        StartButton.Visibility = _isHostInCurrentRoom
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

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
        var accent = (Brush)Application.Current.FindResource("AccentBrush");
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

        var profile = _getActiveProfile();
        if (profile == null)
        {
            SignInErrorText.Text = Strings.Get("MpModNotInstalled");
            SignInErrorText.Visibility = Visibility.Visible;
            return;
        }

        // Compute the mod fingerprint up front so we don't block the
        // dialog with file I/O after it opens.
        string fingerprint;
        try
        {
            fingerprint = await _computeModFingerprint(profile);
        }
        catch (Exception ex)
        {
            SignInErrorText.Text = ex.Message;
            SignInErrorText.Visibility = Visibility.Visible;
            return;
        }

        var dlg = new CreateLobbyDialog(_session, profile, fingerprint)
        {
            Owner = Window.GetWindow(this),
        };
        if (dlg.ShowDialog() != true || dlg.CreatedLobby == null) return;

        try
        {
            await _session.EnterHostedLobbyAsync(dlg.CreatedLobby);
            // _isHostInCurrentRoom is set authoritatively when the
            // WS room_state frame arrives — it carries host_user_id,
            // which is the canonical source of truth.
        }
        catch (Exception ex)
        {
            SignInErrorText.Text = ex.Message;
            SignInErrorText.Visibility = Visibility.Visible;
        }
    }

    // ---------- Rooms list polling + rendering ----------

    private async Task RefreshRoomsListAsync()
    {
        if (_session == null || _isRefreshingList) return;
        _isRefreshingList = true;
        try
        {
            RoomsListPanel.Children.Clear();
            RoomsListPanel.Children.Add(new TextBlock
            {
                Text = Strings.Get("MpRoomsLoading"),
                Foreground = (Brush)Application.Current.FindResource("TextSecondary"),
                FontStyle = FontStyles.Italic,
            });

            var list = await _session.Api.ListLobbiesAsync();
            RoomsListPanel.Children.Clear();

            if (list.Lobbies.Count == 0)
            {
                RoomsListPanel.Children.Add(new TextBlock
                {
                    Text = Strings.Get("MpRoomsEmpty"),
                    Foreground = (Brush)Application.Current.FindResource("TextSecondary"),
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 20, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center,
                });
                return;
            }

            foreach (var lobby in list.Lobbies)
                RoomsListPanel.Children.Add(BuildRoomRow(lobby));
        }
        catch (Exception ex)
        {
            RoomsListPanel.Children.Clear();
            RoomsListPanel.Children.Add(new TextBlock
            {
                Text = ex.Message,
                Foreground = Brushes.Salmon,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 20, 0, 0),
            });
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
            QuotaText.Text = Strings.Format("MpQuotaBar",
                q.Players.Active, q.Players.Max,
                q.Lobbies.Active, q.Lobbies.Max);
        }
        catch
        {
            QuotaText.Text = "";
        }
    }

    private Border BuildRoomRow(LobbySummary lobby)
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
        left.Children.Add(new TextBlock
        {
            Text = lobby.Title,
            Foreground = (Brush)Application.Current.FindResource("TextPrimary"),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
        });
        left.Children.Add(new TextBlock
        {
            Text = $"{lobby.Host.DisplayName} · {lobby.ModId} · {lobby.CurrentPlayers}/{lobby.MaxPlayers}"
                   + (lobby.IsPrivate ? " · 🔒" : "")
                   + (lobby.Status == "in_game" ? " · in game" : ""),
            Foreground = (Brush)Application.Current.FindResource("TextSecondary"),
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 0),
        });

        Grid.SetColumn(left, 0);
        grid.Children.Add(left);

        var joinBtn = new Button
        {
            Content = Strings.Get("MpRoomJoin"),
            Style = (Style)Application.Current.FindResource("SidebarPrimaryButton"),
            MinWidth = 110,
            Padding = new Thickness(14, 6, 14, 6),
            VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = lobby.Status != "in_game" && lobby.CurrentPlayers < lobby.MaxPlayers,
            Tag = lobby,
        };
        joinBtn.Click += JoinRoomButton_Click;
        Grid.SetColumn(joinBtn, 1);
        grid.Children.Add(joinBtn);

        card.Child = grid;
        return card;
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
        if (_session?.RoomSocket == null) return;
        try { await _session.RoomSocket.SendReadyAsync(true); }
        catch (Exception ex) { DiagnosticLog.Write($"MultiplayerTab.Ready: {ex.Message}"); }
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session?.RoomSocket == null) return;
        try { await _session.RoomSocket.SendStartAsync(); }
        catch (Exception ex) { DiagnosticLog.Write($"MultiplayerTab.Start: {ex.Message}"); }
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
        if (_session?.RoomSocket == null) return;
        var text = ChatInputBox.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;
        ChatInputBox.Text = "";
        try { await _session.RoomSocket.SendChatAsync(text); }
        catch (Exception ex) { DiagnosticLog.Write($"MultiplayerTab.Chat: {ex.Message}"); }
    }
}
