using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// The quick match against the SERVER's queue (backend <c>src/matchmaking</c>,
/// docs/DISENO-PARTIDA-RAPIDA.md §7). The room-based search in <c>MultiplayerTab.QuickMatch.cs</c>
/// stays as the fallback for a server without the queue, and it is chosen per search:
/// <c>GET /matchmaking/status</c> answering 404 keeps the old path, and so does a queue that
/// never confirms the ticket.
///
/// <para><b>Hybrid:</b> nobody opens a room until two players are matched, but every searcher is
/// listed in the rooms list ("Looking for a 1v1") with a Play button that challenges him directly.
/// Matched, both get <see cref="MatchFoundDialog"/> with 20 s to accept; when both do, the server
/// opens a room only those two may enter, both launchers enter it on their own, mark themselves
/// ready, and the host's ordinary auto-start takes it from there.</para>
///
/// <para>All of it rides the global socket, which is already always on while signed in. A search
/// survives that socket dropping: the server keeps the ticket 15 s, and the launcher sends its
/// <c>mm_join</c> again when the socket comes back, which keeps the place in the queue.</para>
/// </summary>
public partial class MultiplayerTab
{
    /// <summary>Whether the server runs the queue: null not asked yet, then its answer.</summary>
    private bool? _mmServer;
    private DateTime _mmProbedUtc;
    private Task<bool>? _mmProbe;

    /// <summary>Everybody searching, as the server last said (<c>mm_searchers_changed</c>).</summary>
    private List<QueueSearcher> _mmSearchers = new();

    /// <summary>The first list after (re)connecting seeds the "seen" set silently.</summary>
    private bool _mmSearchersSeeded;
    private readonly HashSet<string> _mmSeenTickets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _mmNotifiedByUser = new(StringComparer.Ordinal);

    private DispatcherTimer? _mmPingTimer;
    private DispatcherTimer? _mmConfirmTimer;
    private MatchFoundDialog? _mmFoundDialog;
    private string? _mmFoundMatchId;
    private string? _mmFoundRival;
    private bool _mmFoundHosting;

    /// <summary>The server is asked again after this when it said it had no queue.</summary>
    private static readonly TimeSpan MatchmakingReprobe = TimeSpan.FromMinutes(10);

    /// <summary>A queue that has not confirmed the ticket by now is treated as absent.</summary>
    private static readonly TimeSpan MatchmakingConfirmWait = TimeSpan.FromSeconds(8);

    /// <summary>The server forgets a ticket that stops pinging for a minute.</summary>
    private static readonly TimeSpan MatchmakingPingEvery = TimeSpan.FromSeconds(15);

    /// <summary>Whether the room this player is in was opened by the queue for him.</summary>
    internal bool InMatchmakingRoom(LobbySummary lobby) => lobby.Matchmaking;

    // ---------- does the server have a queue? ----------------------------

    private Task<bool> ServerHasQueueAsync()
    {
        if (_mmServer == true) return Task.FromResult(true);
        if (_mmServer == false && DateTime.UtcNow - _mmProbedUtc < MatchmakingReprobe) return Task.FromResult(false);
        return _mmProbe ??= ProbeQueueAsync();
    }

    private async Task<bool> ProbeQueueAsync()
    {
        try
        {
            if (_session?.Api == null) return false;
            var status = await _session.Api.GetMatchmakingStatusAsync();
            _mmServer = status?.Enabled == true;
            DiagnosticLog.Write($"QuickMatch: the server {(_mmServer == true ? "runs" : "has no")} matchmaking queue.");
            if (_mmServer == true && _mmSearchers.Count == 0)
            {
                ApplySearchers(await _session.Api.GetQueueSearchersAsync(), fromSocket: false);
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"QuickMatch: asking for the queue failed — {ex.Message}");
            _mmServer = false;
        }
        finally
        {
            _mmProbedUtc = DateTime.UtcNow;
            _mmProbe = null;
        }
        return _mmServer == true;
    }

    /// <summary>The socket is there and the server said yes: this search goes through the queue.</summary>
    private async Task<bool> UseQueueAsync()
        => _globalChatSocket != null && await ServerHasQueueAsync();

    // ---------- the search ------------------------------------------------

    /// <summary>
    /// Enter the queue with the search already fixed by <c>StartQuickMatchAsync</c> (mod, version,
    /// kind). The search stays "Looking" until the server confirms with <c>mm_queued</c>; a queue that
    /// does not answer in <see cref="MatchmakingConfirmWait"/> falls back to the room-based search.
    /// </summary>
    private async Task JoinQueueAsync()
    {
        var radminReady = false;
        try { radminReady = await Task.Run(() => RadminAssistantService.StageOf(RadminVpnService.GetStatus()) >= RadminStage.LoggedIn); }
        catch { /* unknown is "not ready": the host is then the other player when he is */ }
        if (_qmStage != QuickMatchStage.Looking) return;

        if (!await SendQueueJoinAsync(radminReady))
        {
            await FallBackToRoomsAsync("the global socket refused the frame");
            return;
        }
        _mmConfirmTimer?.Stop();
        _mmConfirmTimer = new DispatcherTimer { Interval = MatchmakingConfirmWait };
        _mmConfirmTimer.Tick += async (_, _) =>
        {
            _mmConfirmTimer?.Stop();
            if (_qmStage == QuickMatchStage.Looking && _qmUsingQueue)
                await FallBackToRoomsAsync("the queue did not confirm the ticket");
        };
        _mmConfirmTimer.Start();
    }

    /// <summary>Set while the running search goes through the queue rather than rooms.</summary>
    private bool _qmUsingQueue;
    private bool _qmRadminReadySent;

    private async Task<bool> SendQueueJoinAsync(bool radminReady)
    {
        var sock = _globalChatSocket;
        if (sock == null || _qmModId == null || _qmModHash == null) return false;
        _qmUsingQueue = true;
        _qmRadminReadySent = radminReady;
        try
        {
            await sock.SendAsync(new
            {
                type = "mm_join",
                mod_id = _qmModId,
                mod_combined_hash = _qmModHash,
                kind = QueueSearchers.Wire(_qmKind),
                format = "1v1",
                radmin_ready = radminReady,
                host_ok = true,
            });
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"QuickMatch: mm_join failed — {ex.Message}");
            return false;
        }
    }

    private async Task FallBackToRoomsAsync(string why)
    {
        DiagnosticLog.Write($"QuickMatch: back to the room-based search — {why}.");
        _mmServer = false;
        _mmProbedUtc = DateTime.UtcNow;
        _qmUsingQueue = false;
        if (_qmStage != QuickMatchStage.Looking) return;
        await JoinOrCreateAsync();
    }

    private void SendQueueFrame(object frame)
    {
        var sock = _globalChatSocket;
        if (sock == null) return;
        _ = SendSafelyAsync(sock, frame);
    }

    private static async Task SendSafelyAsync(LobbyWebSocket sock, object frame)
    {
        try { await sock.SendAsync(frame); }
        catch (Exception ex) { DiagnosticLog.Write($"QuickMatch: sending a queue frame failed — {ex.Message}"); }
    }

    private void StartQueuePings()
    {
        _mmPingTimer?.Stop();
        _mmPingTimer = new DispatcherTimer { Interval = MatchmakingPingEvery };
        _mmPingTimer.Tick += (_, _) =>
        {
            if (_qmStage == QuickMatchStage.Queued) SendQueueFrame(new { type = "mm_ping" });
            else _mmPingTimer?.Stop();
        };
        _mmPingTimer.Start();
    }

    /// <summary>Called by <c>EndQuickMatch</c>: the queue's own clocks, and the server told.</summary>
    private void EndQueueSearch(bool tellServer)
    {
        _mmPingTimer?.Stop();
        _mmConfirmTimer?.Stop();
        if (tellServer && _qmUsingQueue) SendQueueFrame(new { type = "mm_leave" });
        _qmUsingQueue = false;
    }

    /// <summary>The global socket said hello again: a search in the queue re-enters it (same place).</summary>
    private void OnGlobalSocketReady()
    {
        _mmSearchersSeeded = false;
        if (_qmStage == QuickMatchStage.Queued && _qmUsingQueue)
        {
            DiagnosticLog.Write("QuickMatch: the socket is back — re-entering the queue.");
            _ = SendQueueJoinAsync(_qmRadminReadySent);
        }
        if (_mmServer != false) _ = ServerHasQueueAsync();
    }

    // ---------- frames ----------------------------------------------------

    /// <summary>Every <c>mm_*</c> frame of the global socket, on the UI thread.</summary>
    private void HandleMatchmakingFrame(string type, JsonElement json)
    {
        switch (type)
        {
            case "mm_queued":
                OnQueued(json);
                break;
            case "mm_searchers_changed":
                if (json.TryGetProperty("searchers", out var list))
                    ApplySearchers(JsonSerializer.Deserialize<List<QueueSearcher>>(list.GetRawText()) ?? new(), fromSocket: true);
                break;
            case "mm_found":
                OnMatchFound(JsonSerializer.Deserialize<MatchFoundFrame>(json.GetRawText()) ?? new MatchFoundFrame());
                break;
            case "mm_accept_progress":
                if (_mmFoundDialog != null && Str(json, "match_id") == _mmFoundMatchId)
                    _mmFoundDialog.SetProgress(Int(json, "accepted"), Math.Max(2, Int(json, "total")));
                break;
            case "mm_cancelled":
                OnMatchCancelled(json);
                break;
            case "mm_room_ready":
                _ = OnMatchRoomReadyAsync(json);
                break;
            case "mm_left":
                OnQueueLeft(Str(json, "reason"));
                break;
            case "mm_error":
                _ = OnQueueErrorAsync(Str(json, "code"), Str(json, "until"), Str(json, "mod_id"));
                break;
        }
    }

    private static string Str(JsonElement j, string name)
        => j.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static int Int(JsonElement j, string name)
        => j.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : 0;

    private void OnQueued(JsonElement json)
    {
        _mmServer = true;
        _mmConfirmTimer?.Stop();
        if (_qmStage is not (QuickMatchStage.Looking or QuickMatchStage.Queued) || !_qmUsingQueue) return;
        if (_qmStage == QuickMatchStage.Looking)
        {
            DiagnosticLog.Write($"QuickMatch: queued ({_qmKind}, '{_qmModId}').");
            // The server's time is the one the 30 minutes count from.
            if (RoomAgeFormat.ParseCreatedUtc(Str(json, "queued_at")) is DateTime at) _qmStartedUtc = at;
        }
        _qmStage = QuickMatchStage.Queued;
        StartQueuePings();
        RefreshQuickMatchUi();
    }

    private void OnQueueLeft(string reason)
    {
        if (_qmStage != QuickMatchStage.Queued || !_qmUsingQueue) return;
        switch (reason)
        {
            case "timeout":
                EndQuickMatch("the queue gave up after 30 minutes", tellServer: false);
                _showAppToast?.Invoke(new AppToast.ToastOptions(
                    AppToast.ToastTone.Neutral, AppToast.ToastIcon.Clock, Strings.Get("QmFindMatch"), Strings.Get("QmTimedOut"), Array.Empty<AppToast.ToastAction>(),
                    AutoDismissMs: 15000, PreferDesktop: true, Cause: ToastCause.Answer));
                break;
            case "stale":
            case "disconnected":
                // The server lost us (a long sleep, a dropped socket): we are still searching.
                DiagnosticLog.Write($"QuickMatch: the queue dropped the ticket ({reason}) — re-entering.");
                _ = SendQueueJoinAsync(_qmRadminReadySent);
                break;
            default:
                EndQuickMatch($"left the queue ({reason})", tellServer: false);
                break;
        }
    }

    private async Task OnQueueErrorAsync(string code, string until, string modId)
    {
        DiagnosticLog.Write($"QuickMatch: the queue answered {code}.");
        if (code == "not_supported")
        {
            if (_qmStage == QuickMatchStage.Looking) await FallBackToRoomsAsync("the server's queue is off");
            else _mmServer = false;
            return;
        }
        string message = code switch
        {
            "penalty" => RoomAgeFormat.ParseCreatedUtc(until) is DateTime t
                ? Strings.Format("MmPenaltyUntil", t.ToLocalTime().ToString("HH:mm", Strings.Culture))
                : Strings.Get("MmPenalty"),
            "in_room" => Strings.Get("MmErrInRoom"),
            "in_game" => Strings.Get("MmErrInGame"),
            "mod_not_ranked" => Strings.Get("QmModNotRanked"),
            "mod_mismatch" => Strings.Get("MmErrOtherVersion"),
            "not_searching" => Strings.Get("MmErrNotSearching"),
            "busy" => Strings.Get("MmErrBusy"),
            "rate_limited" => Strings.Get("MmErrRateLimited"),
            _ => Strings.Format("QmFailed", code),
        };
        if (code == "mod_not_ranked" && _qmModId is { } m) _qmUnrankedMods.Add(m);
        // A refused CHALLENGE leaves the player's own search, if any, as it was.
        var aboutMySearch = code is not ("mod_mismatch" or "not_searching" or "busy")
                            && _qmUsingQueue && _qmStage is QuickMatchStage.Looking or QuickMatchStage.Queued;
        if (aboutMySearch) EndQuickMatch($"the queue refused ({code})", tellServer: false);
        _showAppToast?.Invoke(new AppToast.ToastOptions(
            AppToast.ToastTone.Warn, AppToast.ToastIcon.Warn, Strings.Get("QmFindMatch"), message, Array.Empty<AppToast.ToastAction>(),
            AutoDismissMs: 9000, Cause: ToastCause.Answer));
    }

    // ---------- found, accepted, cancelled --------------------------------

    private void OnMatchFound(MatchFoundFrame f)
    {
        if (string.IsNullOrEmpty(f.MatchId)) return;
        CloseFoundDialog();
        _mmFoundMatchId = f.MatchId;
        _mmFoundRival = string.IsNullOrWhiteSpace(f.Opponent.DisplayName) ? "—" : f.Opponent.DisplayName;
        _mmFoundHosting = f.YouAreHost;
        DiagnosticLog.Write($"QuickMatch: match found {f.MatchId} against {f.Opponent.UserId} ({f.Kind}{(f.Challenge ? ", challenge" : "")}).");

        var mod = ModRegistry.Find(f.ModId);
        var kindLine = Strings.Format(f.Kind == "casual" ? "MmFoundKindCasual" : "MmFoundKindRanked",
            mod?.DisplayName ?? f.ModId);
        var hostLine = Strings.Format(f.YouAreHost ? "MmFoundYouHost" : "MmFoundTheyHost", _mmFoundRival);
        var dlg = new MatchFoundDialog(_mmFoundRival, BuildFoundRivalCard(f), kindLine, hostLine,
            f.AcceptDeadlineMs, iChallenged: f.Challenger, mod);
        var matchId = f.MatchId;
        dlg.Accepted += () => SendQueueFrame(new { type = "mm_accept", match_id = matchId });
        dlg.Declined += () => SendQueueFrame(new { type = "mm_decline", match_id = matchId });
        dlg.Closed += (_, _) => { if (ReferenceEquals(_mmFoundDialog, dlg)) _mmFoundDialog = null; };
        _mmFoundDialog = dlg;
        // A challenger has said yes already; the server needs to hear it.
        if (f.Challenger) SendQueueFrame(new { type = "mm_accept", match_id = matchId });

        PlaceFoundDialog(dlg);
        dlg.Show();
        dlg.Activate();
        WindowFlash.UntilForeground(dlg);
        SoundService.PlayMatchFound();
        RefreshQuickMatchUi();
    }

    /// <summary>
    /// Over the launcher when it is on screen, so the card appears where the player is looking and
    /// on the same monitor; with the launcher in the tray or minimised, centred on the screen that
    /// holds the pointer. Never owned: an owned window that closes minimises its owner.
    /// </summary>
    private void PlaceFoundDialog(Window dlg)
    {
        var main = Window.GetWindow(this);
        if (main is { IsVisible: true } && main.WindowState != WindowState.Minimized)
        {
            dlg.WindowStartupLocation = WindowStartupLocation.Manual;
            // A maximised window's restore rectangle sits on the same monitor it is maximised on.
            var b = main.WindowState == WindowState.Maximized
                ? main.RestoreBounds
                : new Rect(main.Left, main.Top, main.ActualWidth, main.ActualHeight);
            dlg.Left = b.Left + Math.Max(0, (b.Width - dlg.Width) / 2);
            dlg.Top = b.Top + Math.Max(0, b.Height / 2 - 220);
        }
        else
        {
            dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    /// <summary>
    /// <c>--preview-dialog=matchfound[:host|guest|placing|challenge|casual]</c>: the "rival found"
    /// window with a sample rival, wired to nothing — its buttons only close it. A real one needs
    /// two accounts and a server with the queue.
    /// </summary>
    internal void PreviewMatchFound(string variant)
    {
        var f = new MatchFoundFrame
        {
            MatchId = "preview",
            Kind = variant == "casual" ? "casual" : "ranked",
            ModId = "wol",
            Challenge = variant == "challenge",
            Challenger = variant == "challenge",
            AcceptDeadlineMs = 20000,
            YouAreHost = variant != "guest",
            Opponent = new MatchFoundOpponent
            {
                UserId = "preview-rival",
                DisplayName = "Kaiser",
                LadderRank = variant == "placing" ? null : 7,
                Rating = variant == "placing" ? null : 1642,
                GamesPlayed = variant == "placing" ? 3 : 41,
                InPlacement = variant == "placing",
            },
        };
        var mod = ModRegistry.Find(f.ModId);
        var dlg = new MatchFoundDialog(f.Opponent.DisplayName, BuildFoundRivalCard(f),
            Strings.Format(f.Kind == "casual" ? "MmFoundKindCasual" : "MmFoundKindRanked", mod?.DisplayName ?? f.ModId),
            Strings.Format(f.YouAreHost ? "MmFoundYouHost" : "MmFoundTheyHost", f.Opponent.DisplayName),
            f.AcceptDeadlineMs, iChallenged: f.Challenger, mod);
        dlg.SetProgress(f.Challenger ? 1 : 0, 2);
        dlg.Accepted += () => dlg.SetProgress(1, 2);
        PlaceFoundDialog(dlg);
        dlg.Show();
    }

    /// <summary>The rival's face, badge and rating, by the same rules every other list uses.</summary>
    private FrameworkElement BuildFoundRivalCard(MatchFoundFrame f)
    {
        var o = f.Opponent;
        var name = string.IsNullOrWhiteSpace(o.DisplayName) ? "—" : o.DisplayName;
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(BuildAvatarDisc(name, o.AvatarUrl, 44));
        var format = f.Kind == "casual" ? (RoomFormat?)null : RoomFormat.OneVOne;
        if (!o.InPlacement && BadgeOf(format, o.BadgeMode, o.LadderRank, null) is { } shown)
        {
            var badge = BuildShownBadge(shown, 28, "mm-found-" + o.UserId);
            badge.Margin = new Thickness(10, 0, 0, 0);
            badge.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(badge, 1);
            row.Children.Add(badge);
        }
        var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var nameText = new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        nameText.SetResourceReference(TextBlock.ForegroundProperty, "MpTextBody");
        nameText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSubtitle");
        text.Children.Add(nameText);
        var rating = new TextBlock { Margin = new Thickness(0, 3, 0, 0) };
        rating.SetResourceReference(TextBlock.ForegroundProperty, "MpTextMuted");
        rating.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeCaption");
        // A player still being placed shows his progress, never a number (placement privacy).
        rating.Text = o.InPlacement
            ? Strings.Format("MmFoundPlacing", Math.Min(o.GamesPlayed ?? 0, StatsDemoData.PlacementDefault), StatsDemoData.PlacementDefault)
            : o.Rating is double r ? $"{PlacementView.RatingText(r)} ELO" : "";
        if (!string.IsNullOrEmpty(rating.Text)) text.Children.Add(rating);
        Grid.SetColumn(text, 2);
        row.Children.Add(text);
        return row;
    }

    private void CloseFoundDialog()
    {
        var d = _mmFoundDialog;
        _mmFoundDialog = null;
        d?.CloseQuietly();
    }

    private void OnMatchCancelled(JsonElement json)
    {
        var matchId = Str(json, "match_id");
        if (!string.IsNullOrEmpty(_mmFoundMatchId) && !string.IsNullOrEmpty(matchId) && matchId != _mmFoundMatchId) return;
        CloseFoundDialog();
        _mmFoundMatchId = null;
        var reason = Str(json, "reason");
        var me = json.TryGetProperty("you", out var y) && y.ValueKind == JsonValueKind.True;
        var requeued = json.TryGetProperty("requeued", out var rq) && rq.ValueKind == JsonValueKind.True;
        var rival = _mmFoundRival ?? "—";
        DiagnosticLog.Write($"QuickMatch: match {matchId} cancelled ({reason}, me={me}, requeued={requeued}).");

        if (reason == "room_failed")
        {
            _ = RecoverFromFailedRoomAsync(me, rival);
            return;
        }

        string text;
        if (me)
        {
            if (_qmUsingQueue) EndQuickMatch($"I did not accept ({reason})", tellServer: false);
            var lockedUntil = RoomAgeFormat.ParseCreatedUtc(Str(json, "locked_until"));
            text = Strings.Get(reason == "timeout" ? "MmYouMissed" : "MmYouDeclined");
            if (lockedUntil is DateTime t)
                text += " " + Strings.Format("MmPenaltyUntil", t.ToLocalTime().ToString("HH:mm", Strings.Culture));
            else if (reason != "player_left")
                text += " " + Strings.Get("MmStrikeWarning");
        }
        else
        {
            text = requeued ? Strings.Format("MmRivalDidNotAcceptRequeued", rival) : Strings.Format("MmRivalDidNotAccept", rival);
        }
        _showAppToast?.Invoke(new AppToast.ToastOptions(
            me ? AppToast.ToastTone.Neutral : AppToast.ToastTone.Info,
            me ? AppToast.ToastIcon.Stop : AppToast.ToastIcon.Info, Strings.Get("QmFindMatch"), text, Array.Empty<AppToast.ToastAction>(),
            AutoDismissMs: 9000, PreferDesktop: true, Cause: ToastCause.Answer));
        RefreshQuickMatchUi();
    }

    /// <summary>The room was opened but somebody never came in. The one who did comes out and
    /// searches again; the one who did not is told he missed it.</summary>
    private async Task RecoverFromFailedRoomAsync(bool iWasAbsent, string rival)
    {
        if (_session?.CurrentLobbyId != null && _qmFoundLobbyId != null
            && string.Equals(_session.CurrentLobbyId, _qmFoundLobbyId, StringComparison.Ordinal))
            await LeaveRoomQuietlyAsync("quick match: the matched room was closed");
        if (iWasAbsent)
        {
            _showAppToast?.Invoke(new AppToast.ToastOptions(
                AppToast.ToastTone.Neutral, AppToast.ToastIcon.Stop, Strings.Get("QmFindMatch"), Strings.Get("MmYouNeverEntered"), Array.Empty<AppToast.ToastAction>(),
                AutoDismissMs: 9000, PreferDesktop: true, Cause: ToastCause.Answer));
            return;
        }
        _showAppToast?.Invoke(new AppToast.ToastOptions(
            AppToast.ToastTone.Info, AppToast.ToastIcon.Info, Strings.Get("QmFindMatch"), Strings.Format("MmRivalNeverEntered", rival), Array.Empty<AppToast.ToastAction>(),
            AutoDismissMs: 9000, PreferDesktop: true, Cause: ToastCause.Answer));
        await StartQuickMatchAsync();
    }

    /// <summary>
    /// Both accepted: the room exists and only we two may enter. Enter it the ordinary way — the
    /// host too, as a tournament room does — then mark ready so the host's auto-start can run.
    /// </summary>
    private async Task OnMatchRoomReadyAsync(JsonElement json)
    {
        CloseFoundDialog();
        var lobbyId = Str(json, "lobby_id");
        _mmFoundMatchId = null;
        if (string.IsNullOrEmpty(lobbyId)) return;
        RememberQuickMatchFound(lobbyId, _mmFoundRival ?? "—", _mmFoundHosting);
        if (IsQuickMatchSearching) EndQuickMatch($"matched — room {lobbyId}", tellServer: false);
        DiagnosticLog.Write($"QuickMatch: entering the matched room {lobbyId}{(_mmFoundHosting ? " as host" : "")}.");
        _bringMultiplayerToFront?.Invoke();
        await JoinByLobbyIdAsync(lobbyId);
        await MarkReadyWhenInAsync(lobbyId);
    }

    /// <summary>Ready, once the room's socket is up and the roster has us. Both answered "yes"
    /// already; asking them to press Ready too would be asking twice.</summary>
    private async Task MarkReadyWhenInAsync(string lobbyId)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var meId = _session?.CurrentUser?.Id;
            if (!string.Equals(_session?.CurrentLobbyId, lobbyId, StringComparison.Ordinal)) return;
            if (_session?.RoomSocket != null && meId != null && _roomMembers.TryGetValue(meId, out var me))
            {
                if (me.Ready) return;
                me.Ready = true;
                RenderRoomMembers();
                MaybeAutoStartOnAllReady();
                try { await _session.RoomSocket.SendReadyAsync(true); }
                catch (Exception ex) { DiagnosticLog.Write($"QuickMatch: auto-ready failed — {ex.Message}"); }
                AppendChatSystem(Strings.Get("MmReadyForYou"));
                return;
            }
            await Task.Delay(300);
        }
        DiagnosticLog.Write("QuickMatch: gave up marking ready — the room never came up in 20 s.");
    }

    // ---------- the public list ------------------------------------------

    private void ApplySearchers(List<QueueSearcher> list, bool fromSocket)
    {
        var before = _mmSearchers;
        _mmSearchers = list;
        if (fromSocket) MaybeNotifySearchers(list);
        // Only the rooms list and the bar show it: a change of who searches costs no request.
        var changed = before.Count != list.Count
                      || !before.Select(s => s.TicketId + s.Busy).SequenceEqual(list.Select(s => s.TicketId + s.Busy));
        if (!changed) return;
        _quickCardKey = null;
        RenderQuickCard();
        RerenderRoomsFromCache();
    }

    /// <summary>Raises the "X is looking for a 1v1" card for searchers that just appeared.</summary>
    private void MaybeNotifySearchers(List<QueueSearcher> list)
    {
        var now = DateTime.UtcNow;
        if (!_mmSearchersSeeded)
        {
            // Whoever was already searching when we connected is not news.
            foreach (var s in list) _mmSeenTickets.Add(s.TicketId);
            _mmSearchersSeeded = true;
            return;
        }
        var opted = Cfg().NotifyQuickMatchSearchers;
        var myId = _session?.CurrentUser?.Id;
        foreach (var s in list)
        {
            // My version of his mod not worked out yet: decide once it is (ReadModHashAsync asks
            // again), rather than mark him seen and lose the card.
            if (opted && !_mmSeenTickets.Contains(s.TicketId) && RoomModInstalled(s.ModId) && KnownModHash(s.ModId) == null
                && _computeModFingerprint != null)
                continue;
            var worth = QueueSearchers.WorthNotifying(s, opted, IsQuickMatchSearching, myId,
                modId => RoomModInstalled(modId) ? KnownModHash(modId) : null,
                _mmSeenTickets, _mmNotifiedByUser, now);
            _mmSeenTickets.Add(s.TicketId);
            if (!worth) continue;
            _mmNotifiedByUser[s.UserId] = now;
            var mod = ModRegistry.Find(s.ModId)?.DisplayName ?? s.ModId;
            var searcher = s;
            _showAppToast?.Invoke(new AppToast.ToastOptions(
                AppToast.ToastTone.Ok, AppToast.ToastIcon.Swords,
                Strings.Format("MmSomeoneSearchingTitle", string.IsNullOrWhiteSpace(s.DisplayName) ? "—" : s.DisplayName),
                Strings.Format(QueueSearchers.KindOf(s.Kind) == QuickMatchKind.Casual ? "MmSomeoneSearchingCasual" : "MmSomeoneSearchingRanked", mod),
                new[] { new AppToast.ToastAction(Strings.Get("MmPlay"), AppToast.ToastButton.Solid, () => { _bringMultiplayerToFront?.Invoke(); _ = ChallengeAsync(searcher); }) },
                AutoDismissMs: 15000, Cause: ToastCause.News, ModId: s.ModId));
        }
    }

    /// <summary>
    /// "Play" on a searcher's row (or on his card): challenge him directly. The same refusals as a
    /// search — signed in, not gated, out of any room — and my copy of his mod must be his version.
    /// The answer is the found card or a refusal from the server.
    /// </summary>
    private async Task ChallengeAsync(QueueSearcher s)
    {
        if (_offlineMode || IsUpdateGated || _session?.Status != MultiplayerSession.SessionStatus.SignedIn
            || _computeModFingerprint == null) return;
        if (_globalChatSocket == null) return;
        var profile = ModRegistry.Find(s.ModId);
        if (profile == null || string.IsNullOrEmpty(GetInstallPath(profile)))
        {
            if (profile != null) _openModInLibrary?.Invoke(profile);
            return;
        }
        if (!await EnsureNotInAnotherRoomAsync()) return;
        if (!await EnsureSessionOutlastsMatchAsync()) return;
        string hash;
        try { hash = await _computeModFingerprint(profile); }
        catch (Exception ex)
        {
            await MpAlertOverlay.NoticeAsync(TabRootGrid, Strings.Get("MpNoticeFingerprintTitle"), ex.Message, Strings.Get("MpAlertOk"));
            return;
        }
        if (!string.Equals(hash, s.ModCombinedHash, StringComparison.OrdinalIgnoreCase))
        {
            await MpAlertOverlay.NoticeAsync(TabRootGrid, Strings.Get("QmFindMatch"), Strings.Get("MmErrOtherVersion"), Strings.Get("MpAlertOk"));
            return;
        }
        _mmFoundRival = string.IsNullOrWhiteSpace(s.DisplayName) ? "—" : s.DisplayName;
        DiagnosticLog.Write($"QuickMatch: challenging {s.UserId} ({s.ModId}).");
        var radmin = false;
        try { radmin = await Task.Run(() => RadminAssistantService.StageOf(RadminVpnService.GetStatus()) >= RadminStage.LoggedIn); }
        catch { /* unknown */ }
        SendQueueFrame(new { type = "mm_challenge", ticket_id = s.TicketId, mod_combined_hash = hash, radmin_ready = radmin, host_ok = true });
    }

    /// <summary>The searchers shown as rows: free ones, in mods the list knows.</summary>
    private IEnumerable<LobbySummary> SearcherRows()
    {
        if (_demoRooms) return Array.Empty<LobbySummary>();
        return _mmSearchers.Where(s => !s.Busy).Select(s => (LobbySummary)new QueueSearcherRow(s));
    }

    /// <summary>How many search the bar's mod, version and kind right now (me excluded).</summary>
    private List<QueueSearcher> CompatibleSearchers(string modId, string? hash, QuickMatchKind kind)
        => QueueSearchers.Compatible(_mmSearchers, _session?.CurrentUser?.Id, modId, hash, kind);
}

/// <summary>
/// A searcher drawn as a row of the rooms list. It IS a <see cref="LobbySummary"/> so the list's
/// one decision (<see cref="RoomActionRule"/>), its grouping, its search box and its host cell
/// treat it exactly like a room of one waiting player — which is what a searcher is to somebody
/// looking for a game. Never sent anywhere: it lives only in the list.
/// </summary>
internal sealed class QueueSearcherRow : LobbySummary
{
    public QueueSearcher Searcher { get; }

    public QueueSearcherRow(QueueSearcher s)
    {
        Searcher = s;
        Id = "q:" + s.TicketId;
        ModId = s.ModId;
        ModCombinedHash = s.ModCombinedHash;
        MaxPlayers = 2;
        CurrentPlayers = 1;
        Status = "open";
        CreatedAt = s.QueuedAt;
        Competitive = QueueSearchers.KindOf(s.Kind) != QuickMatchKind.Casual;
        Title = s.DisplayName;
        Host = new LobbyHost
        {
            Id = s.UserId,
            DisplayName = s.DisplayName,
            AvatarUrl = s.AvatarUrl,
            Rating = s.Rating,
            LadderRank = s.LadderRank,
            BadgeMode = s.BadgeMode,
            GamesPlayed = s.GamesPlayed,
            InPlacement = s.InPlacement,
        };
    }
}
