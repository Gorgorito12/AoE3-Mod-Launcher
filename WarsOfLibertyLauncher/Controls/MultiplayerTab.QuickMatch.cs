using System;
using System.Windows.Media.Animation;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// The quick match, phase 0 (docs/DISENO-PARTIDA-RAPIDA.md): one button that joins a competitive
/// 1v1 room of the ACTIVE mod waiting for a rival, or opens one and waits until somebody joins.
/// No server support: it is the rooms list and the same create / join every player already uses,
/// driven by <see cref="QuickMatchPlanner"/>.
///
/// <para><b>Waiting is not passive.</b> Two players who press the button at the same moment both
/// find nothing and both open a room; left alone, each would wait in his own for ever. So while
/// a search waits in its own room it looks again — about 3 s after opening it, every 20 s, and
/// whenever a room of its mod is announced — and moves into an OLDER compatible room when there is
/// one (<see cref="QuickMatchPlanner.PickToMoveTo"/>). Older is a total order, so of any two
/// waiters exactly one moves. A room changing between the list and the join is a lost race, never
/// a dialog (<c>quietOnRaceLoss</c>): the search simply tries the next room.</para>
///
/// <para><b>It reuses, it does not copy.</b> Joining is <see cref="JoinLobbyCoreAsync"/> — every
/// gate a hand-join passes — and opening a room ends in <see cref="EnterCreatedRoomAsync"/>, the
/// same steps "Create room" takes after its dialog. The title is a network value from an English
/// constant (<see cref="RoomTitleProposal.QuickMatch"/>), never a translated string.</para>
///
/// <para>The search ends when a rival joins, when the player cancels (the button, the pill's cross,
/// or simply leaving the room), or after <see cref="QuickMatchPlanner.GiveUpAfter"/> with nobody.
/// The interface is PROVISIONAL, built from existing styles and tokens: there is no design handoff
/// for it yet.</para>
/// </summary>
public partial class MultiplayerTab
{
    /// <summary>MainWindow's: show the window, on Multiplayer. For a card pressed on the desktop.</summary>
    private Action? _bringMultiplayerToFront;

    /// <summary>MainWindow's title-bar pill: its text and tooltip, or null to hide it.</summary>
    private Action<string?, string?>? _setQuickMatchPill;

    /// <summary>Set by <see cref="JoinLobbyCoreAsync"/>: its last false was a lost race.</summary>
    private bool _lastJoinLostRace;

    private enum QuickMatchStage
    {
        /// <summary>No search.</summary>
        Idle,
        /// <summary>Reading the list, joining a room or opening one.</summary>
        Looking,
        /// <summary>In its own room, waiting for a rival (and looking for an older room).</summary>
        Waiting,
    }

    private QuickMatchStage _qmStage = QuickMatchStage.Idle;
    private string? _qmLobbyId;
    private RoomDemoData.Sample? _demoQuickSearch;
    private bool _qmRadminNotReady;
    private bool _qmRoomWasAlone;
    private AnimationClock? _qmSeatDotClock;
    private string? _qmModId;
    private string? _qmModHash;
    private string? _qmModName;
    private DateTime _qmStartedUtc;
    private DispatcherTimer? _qmClock;
    private DispatcherTimer? _qmConvergeTimer;
    private bool _qmConverging;

    /// <summary>The title cap: the create dialog's own field (64), which the server enforces too.</summary>
    private const int QuickMatchTitleMax = 64;

    /// <summary>How many waiting rooms one look tries before opening its own.</summary>
    private const int QuickMatchJoinAttempts = 3;

    /// <summary>The first look after opening a room — two searchers who pressed together meet fast.</summary>
    private static readonly TimeSpan QuickMatchFirstLook = TimeSpan.FromSeconds(3);

    /// <summary>Every look after that. A room list is one cheap public request.</summary>
    private static readonly TimeSpan QuickMatchLookEvery = TimeSpan.FromSeconds(20);

    /// <summary>The look after a room of the same mod is announced (debounced).</summary>
    private static readonly TimeSpan QuickMatchLookSoon = TimeSpan.FromSeconds(1.5);

    /// <summary>Whether a search is running.</summary>
    internal bool IsQuickMatchSearching => _qmStage != QuickMatchStage.Idle;

    /// <summary>
    /// Whether a search is running in this mod. MainWindow asks it before raising a "new room"
    /// card: the search already weighs every room of its mod, and joining a newer one by hand would
    /// undo the rule that makes two searchers meet.
    /// </summary>
    public bool IsQuickMatchSearchingMod(string? modId)
        => IsQuickMatchSearching && !string.IsNullOrEmpty(modId)
           && string.Equals(_qmModId, modId, StringComparison.OrdinalIgnoreCase);

    private async void QuickMatchButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsQuickMatchSearching)
        {
            await CancelQuickMatchAsync(Strings.Get("QmCancelled"));
            return;
        }
        await StartQuickMatchAsync();
    }

    /// <summary>
    /// Starts a search for the ACTIVE mod: the cheap refusals, the room the player may already be
    /// in, the install's fingerprint — then join-or-create.
    /// </summary>
    internal async Task StartQuickMatchAsync()
    {
        if (IsQuickMatchSearching || _offlineMode || IsUpdateGated) return;
        if (_session == null || _session.Status != MultiplayerSession.SessionStatus.SignedIn
            || _getActiveProfile == null || _computeModFingerprint == null) return;

        var profile = _getActiveProfile();
        if (profile == null || string.IsNullOrEmpty(GetInstallPath(profile)))
        {
            await MpAlertOverlay.NoticeAsync(
                TabRootGrid, Strings.Get("QmFindMatch"),
                profile == null ? Strings.Get("QmModMissing")
                    : Strings.Format("QmModMissingNamed", profile.DisplayName),
                Strings.Get("MpAlertOk"));
            return;
        }

        // Whatever room the player is in, he leaves it first — or keeps it and does not search.
        if (!await EnsureNotInAnotherRoomAsync()) return;

        string hash;
        try
        {
            hash = await _computeModFingerprint(profile);
        }
        catch (Exception ex)
        {
            await MpAlertOverlay.NoticeAsync(
                TabRootGrid, Strings.Get("MpNoticeFingerprintTitle"), ex.Message, Strings.Get("MpAlertOk"));
            return;
        }
        if (IsQuickMatchSearching) return;   // a second click landed while hashing

        _qmStage = QuickMatchStage.Looking;
        _qmLobbyId = null;
        _qmModId = profile.Id;
        _qmModHash = hash;
        _qmModName = profile.DisplayName;
        _qmStartedUtc = DateTime.UtcNow;
        _qmRadminNotReady = false;
        StartQuickMatchClock();
        RefreshQuickMatchUi();
        DiagnosticLog.Write($"QuickMatch: searching a 1v1 in '{profile.Id}'.");

        // Informational, and never in the way: Radmin is needed to PLAY, not to find a rival.
        _ = WarnIfRadminNotReadyAsync();
        await JoinOrCreateAsync();
    }

    /// <summary>
    /// Join the best waiting room (the closest rating, then the oldest), or open one when nothing
    /// is waiting — or when every room tried was lost to a race.
    /// </summary>
    private async Task JoinOrCreateAsync()
    {
        if (_qmStage != QuickMatchStage.Looking || _session?.Api == null) return;

        List<LobbySummary> rooms;
        try
        {
            rooms = (await _session.Api.ListLobbiesAsync()).Lobbies ?? new List<LobbySummary>();
        }
        catch (Exception ex)
        {
            await FailQuickMatchAsync(ex.Message);
            return;
        }
        if (_qmStage != QuickMatchStage.Looking) return;

        var myId = _session.CurrentUser?.Id;
        var toJoin = QuickMatchPlanner.RoomsToJoin(rooms, myId, _qmModId!, _qmModHash!, _cachedStanding?.Rating);
        foreach (var room in toJoin.Take(QuickMatchJoinAttempts))
        {
            DiagnosticLog.Write($"QuickMatch: trying waiting room {room.Id}.");
            if (await JoinLobbyCoreAsync(room, quietOnRaceLoss: true))
            {
                EndQuickMatch($"joined waiting room {room.Id}");
                await SayInRoomAsync(room.Id, Strings.Get("QmJoinedRoom"));
                return;
            }
            if (_qmStage != QuickMatchStage.Looking) return;
            // Refused for a reason the join already put on screen (a mod switch that failed, the
            // fingerprint, a session about to expire the player chose to renew…): stop, never
            // open a room the player did not get to agree to.
            if (!_lastJoinLostRace)
            {
                EndQuickMatch("the join was refused");
                return;
            }
        }

        // Nothing to join. A player on another version learns why there was nothing for him.
        var otherVersions = QuickMatchPlanner.OtherVersionCount(rooms, myId, _qmModId!, _qmModHash!);
        if (otherVersions > 0)
        {
            _showAppToast?.Invoke(new AppToast.ToastOptions(
                "ℹ", Strings.Get("QmFindMatch"), Strings.Format("QmOtherVersion", otherVersions),
                Array.Empty<AppToast.ToastAction>(), AutoDismissMs: 12000, Cause: ToastCause.Answer));
        }
        await CreateQuickMatchRoomAsync();
    }

    /// <summary>
    /// Open the search's own room — competitive, two seats, the fixed title — and wait in it. A
    /// server that will not make it competitive (the mod has no ladder) gets the room closed again
    /// and the reason said: a casual room is not what was asked for.
    /// </summary>
    private async Task CreateQuickMatchRoomAsync()
    {
        if (_qmStage != QuickMatchStage.Looking || _session?.Api == null) return;
        // The same promise "Create room" asks for before it touches the server: a token that
        // would run out mid-match loses the report.
        if (!await EnsureSessionOutlastsMatchAsync())
        {
            EndQuickMatch("the session would not outlast a match");
            return;
        }
        if (_qmStage != QuickMatchStage.Looking) return;

        var title = RoomTitleProposal.QuickMatch(_qmModName ?? _qmModId ?? "", QuickMatchTitleMax);
        CreateLobbyResponse created;
        try
        {
            created = await _session.Api.CreateLobbyAsync(new CreateLobbyRequest
            {
                Title = title,
                ModId = _qmModId!,
                ModCombinedHash = _qmModHash!,
                MaxPlayers = 2,
                SpectatorSlots = 0,
                Password = null,
                Competitive = true,
            });
        }
        catch (LobbyApiException ex) when (CreateLobbyDialog.IsLauncherTooOld(ex))
        {
            EndQuickMatch("this launcher is too old for the server");
            await ShowLauncherTooOldAsync(ex);
            return;
        }
        catch (Exception ex)
        {
            await FailQuickMatchAsync(ex.Message);
            return;
        }

        if (_qmStage != QuickMatchStage.Looking)
        {
            // Stopped while the room was being made: it must not stay open with nobody waiting.
            DiagnosticLog.Write($"QuickMatch: stopped while creating — closing {created.Id}.");
            try { await _session.Api.LeaveLobbyAsync(created.Id); }
            catch (Exception ex) { DiagnosticLog.Write($"QuickMatch: closing {created.Id} failed — {ex.Message}"); }
            return;
        }

        try
        {
            await EnterCreatedRoomAsync(created, _qmModId!, title, maxPlayers: 2, isPrivate: false,
                isCompetitive: created.Competitive, spectatorSlots: created.SpectatorSlots,
                competitiveDowngraded: false);
        }
        catch (Exception ex)
        {
            _createdLobbyId = null;
            await FailQuickMatchAsync(ex.Message);
            return;
        }

        if (!created.Competitive)
        {
            EndQuickMatch("the server would not make the room competitive (no ladder for this mod)");
            await LeaveRoomQuietlyAsync("quick match: the mod has no ladder");
            await MpAlertOverlay.NoticeAsync(
                TabRootGrid, Strings.Get("QmFindMatch"), Strings.Get("QmModNotRanked"), Strings.Get("MpAlertOk"));
            return;
        }

        _qmLobbyId = created.Id;
        _qmStage = QuickMatchStage.Waiting;
        RefreshQuickMatchUi();
        StartQuickMatchLooks();
        DiagnosticLog.Write($"QuickMatch: waiting for a rival in own room {created.Id}.");
        await SayInRoomAsync(created.Id, Strings.Get("QmWaitingInRoom"));
    }

    /// <summary>
    /// While waiting in its own room: move into an OLDER compatible room if there is one. The
    /// other waiter's search makes the opposite decision, so the two meet in one room and the
    /// newer one closes. A lost race on the way looks again from the top.
    /// </summary>
    private async Task ConvergeAsync()
    {
        if (_qmStage != QuickMatchStage.Waiting || _qmConverging || _session?.Api == null || _qmLobbyId == null)
            return;
        if (RoomPlayerCount() >= 2) return;   // the rival is in; OnQuickMatchRivalJoined ends it

        _qmConverging = true;
        try
        {
            var rooms = (await _session.Api.ListLobbiesAsync()).Lobbies ?? new List<LobbySummary>();
            if (_qmStage != QuickMatchStage.Waiting || _qmLobbyId == null) return;

            var target = QuickMatchPlanner.PickToMoveTo(
                rooms, _qmLobbyId, _session.CurrentUser?.Id, _qmModId!, _qmModHash!);
            if (target == null || RoomPlayerCount() >= 2) return;

            DiagnosticLog.Write($"QuickMatch: moving from own room {_qmLobbyId} to older room {target.Id}.");
            _qmStage = QuickMatchStage.Looking;
            _qmLobbyId = null;
            _qmConvergeTimer?.Stop();
            await LeaveRoomQuietlyAsync("quick match: moving to an older room");
            if (_qmStage != QuickMatchStage.Looking) return;

            if (await JoinLobbyCoreAsync(target, quietOnRaceLoss: true))
            {
                EndQuickMatch($"moved into older room {target.Id}");
                await SayInRoomAsync(target.Id, Strings.Get("QmMovedToOlderRoom"));
                return;
            }
            if (_qmStage != QuickMatchStage.Looking) return;
            if (!_lastJoinLostRace)
            {
                EndQuickMatch("the join was refused");
                return;
            }
            // Lost the race (it filled, started or closed): look again from the top.
            await JoinOrCreateAsync();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"QuickMatch: looking for an older room failed — {ex.Message}");
        }
        finally
        {
            _qmConverging = false;
        }
    }

    /// <summary>The waiting room's looks: soon after opening it, then every 20 s.</summary>
    private void StartQuickMatchLooks()
    {
        _qmConvergeTimer?.Stop();
        _qmConvergeTimer = new DispatcherTimer { Interval = QuickMatchFirstLook };
        _qmConvergeTimer.Tick += async (_, _) =>
        {
            if (_qmConvergeTimer != null) _qmConvergeTimer.Interval = QuickMatchLookEvery;
            await ConvergeAsync();
        };
        _qmConvergeTimer.Start();
    }

    /// <summary>
    /// A room was announced over the global socket: if it is of the mod being searched (and not the
    /// player's own), look again shortly instead of waiting out the 20 s.
    /// </summary>
    private void OnQuickMatchRoomAnnounced(string modId, string hostUserId)
    {
        if (_qmStage != QuickMatchStage.Waiting || _qmConvergeTimer == null) return;
        if (!string.Equals(modId, _qmModId, StringComparison.OrdinalIgnoreCase)) return;
        if (string.Equals(hostUserId, _session?.CurrentUser?.Id, StringComparison.Ordinal)) return;
        _qmConvergeTimer.Stop();
        _qmConvergeTimer.Interval = QuickMatchLookSoon;
        _qmConvergeTimer.Start();
    }

    /// <summary>
    /// Somebody joined the room the search waits in: that is the rival. The search ends and the
    /// player is told — on the desktop when he is elsewhere, since he may have been waiting minutes.
    /// </summary>
    private void OnQuickMatchRivalJoined(string userId, string login)
    {
        if (_qmStage != QuickMatchStage.Waiting) return;
        if (string.Equals(userId, _session?.CurrentUser?.Id, StringComparison.Ordinal)) return;
        if (!string.Equals(_session?.CurrentLobbyId, _qmLobbyId, StringComparison.Ordinal)) return;

        EndQuickMatch($"rival '{login}' joined");
        _showAppToast?.Invoke(new AppToast.ToastOptions(
            "⚔",
            Strings.Get("QmFoundTitle"),
            Strings.Format("QmFoundBody", login),
            new[] { new AppToast.ToastAction(Strings.Get("QmFoundView"), true, ShowQuickMatchRoom) },
            AutoDismissMs: 20000, PreferDesktop: true, Cause: ToastCause.Request));
    }

    /// <summary>
    /// Stops the search. A search waiting alone in its own room closes it — nobody should find a
    /// room whose host has gone; one whose rival has already arrived is left alone.
    /// </summary>
    internal async Task CancelQuickMatchAsync(string? message, bool preferDesktop = false)
    {
        if (!IsQuickMatchSearching) return;
        var lobby = _qmLobbyId;
        var waiting = _qmStage == QuickMatchStage.Waiting;
        EndQuickMatch(message ?? "cancelled");
        if (waiting && lobby != null && _session != null
            && string.Equals(_session.CurrentLobbyId, lobby, StringComparison.Ordinal)
            && RoomPlayerCount() < 2)
            await LeaveRoomQuietlyAsync("quick match: stopped");
        if (!string.IsNullOrEmpty(message))
        {
            _showAppToast?.Invoke(new AppToast.ToastOptions(
                "⏹", Strings.Get("QmFindMatch"), message, Array.Empty<AppToast.ToastAction>(),
                AutoDismissMs: preferDesktop ? 15000 : 6000, PreferDesktop: preferDesktop, Cause: ToastCause.Answer));
        }
    }

    /// <summary>
    /// Whether the result card offers "Find another": only after a RANKED 1v1, the one kind of
    /// match the quick match looks for. The format is the one frozen at Start, so a casual
    /// two-seat room (format <c>Casual</c>, no label) is not offered a ranked search it never was.
    /// </summary>
    internal static bool OffersFindAnother(MatchOutcomeView model)
        => model.FormatLabelKey != null
           && string.Equals(model.FormatLabelKey, RoomFormats.LabelKey(RoomFormat.OneVOne), StringComparison.Ordinal);

    /// <summary>
    /// The result card's "Find another": out of the finished room first — one active room per
    /// player, so searching before the leave lands would be refused — then a new search.
    /// </summary>
    /// <summary>
    /// "Rematch" (87l): out of the finished room first, because the server allows one active lobby
    /// per player, then the same room again, then the invitation. Nothing here can leave the player
    /// in neither room with no word about it: each refusal says so and ends on the rooms list.
    /// </summary>
    private async Task RematchAsync()
    {
        if (_demoRoomSample != null || _session?.Api == null || _roomResult is not { } result) return;
        var rival = _roomMembers.Values.FirstOrDefault(
            m => string.Equals(m.Login, result.RivalLogin, StringComparison.Ordinal));
        var modId = _currentLobbyModId;
        var title = _roomTitle;
        var seats = RoomPlayingSeats();
        var competitive = _currentLobbyIsCompetitive;
        var rating = _roomResult.RivalRatingShown;
        if (rival == null || string.IsNullOrEmpty(modId) || ModRegistry.Find(modId) is not { } profile
            || _computeModFingerprint == null)
        {
            DiagnosticLog.Write("Rematch: nothing to rematch with - no rival or no mod.");
            return;
        }
        var rivalId = rival.UserId;
        var rivalLogin = rival.Login;
        var rivalAvatar = rival.AvatarUrl;

        ExitResultPhase();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (_session != null && _session.Lobby != MultiplayerSession.LobbyStatus.Idle && DateTime.UtcNow < deadline)
            await Task.Delay(200);
        _bringMultiplayerToFront?.Invoke();
        ShowRooms();
        if (_session?.Lobby != MultiplayerSession.LobbyStatus.Idle)
        {
            DiagnosticLog.Write("Rematch: gave up - the finished room was not left in 5 s.");
            ShowRematchFailed();
            return;
        }
        if (!await EnsureSessionOutlastsMatchAsync()) return;

        var newTitle = RematchPlan.Title(title, QuickMatchTitleMax);
        try
        {
            var hash = await _computeModFingerprint(profile);
            var created = await _session.Api.CreateLobbyAsync(new CreateLobbyRequest
            {
                Title = newTitle,
                ModId = modId,
                ModCombinedHash = hash,
                MaxPlayers = seats,
                SpectatorSlots = 0,
                Password = null,
                Competitive = competitive,
            });
            await EnterCreatedRoomAsync(created, modId, newTitle, seats, isPrivate: false,
                isCompetitive: created.Competitive, spectatorSlots: created.SpectatorSlots,
                competitiveDowngraded: false);
        }
        catch (Exception ex)
        {
            _createdLobbyId = null;
            DiagnosticLog.Write($"Rematch: the room could not be created - {ex.Message}");
            ShowRematchFailed();
            return;
        }

        // In the room, whatever the invitation does: if it is refused the room is an ordinary open
        // room on the list, and the footer goes back to saying so.
        _rematchInvited = new RematchInvite(rivalId, rivalLogin, rating, rivalAvatar);
        RenderRoomMembers();
        AppendChatSystem(Strings.Format("RoomChatInvited", rivalLogin));
        SendInvite(rivalId, rivalLogin);
    }

    private void ShowRematchFailed() => _showAppToast?.Invoke(new AppToast.ToastOptions(
        "\u26A0", Strings.Get("RoomRematchFailed"), null, Array.Empty<AppToast.ToastAction>(),
        AutoDismissMs: 6000, Cause: ToastCause.Answer));

    private async Task FindAnotherAfterResultAsync()
    {
        ExitResultPhase();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (_session != null && _session.Lobby != MultiplayerSession.LobbyStatus.Idle && DateTime.UtcNow < deadline)
            await Task.Delay(200);
        if (_session?.Lobby != MultiplayerSession.LobbyStatus.Idle)
        {
            DiagnosticLog.Write("QuickMatch: 'Find another' gave up — the finished room was not left in 5 s.");
            return;
        }
        _bringMultiplayerToFront?.Invoke();
        ShowRooms();
        await StartQuickMatchAsync();
    }

    /// <summary>The pill's click: the room the search waits in, in front.</summary>
    internal void ShowQuickMatchRoom()
    {
        _bringMultiplayerToFront?.Invoke();
        ShowRooms();
        if (_lobbyWindow == null) return;
        if (_lobbyWindow.WindowState == WindowState.Minimized) _lobbyWindow.WindowState = WindowState.Normal;
        _lobbyWindow.Activate();
    }

    /// <summary>The 1-s clock: the elapsed time on the button and the pill, the give-up, and the
    /// search's end when the player left its room some other way.</summary>
    private void StartQuickMatchClock()
    {
        _qmClock?.Stop();
        _qmClock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _qmClock.Tick += async (_, _) => await QuickMatchClockTickAsync();
        _qmClock.Start();
    }

    private async Task QuickMatchClockTickAsync()
    {
        if (!IsQuickMatchSearching)
        {
            _qmClock?.Stop();
            return;
        }
        if (_session?.Status != MultiplayerSession.SessionStatus.SignedIn)
        {
            EndQuickMatch("signed out");
            return;
        }
        // Left the room some other way — the Leave button, the window's cross, a kick, the room
        // closing: that IS stopping the search.
        if (_qmStage == QuickMatchStage.Waiting && !_qmConverging
            && !string.Equals(_session.CurrentLobbyId, _qmLobbyId, StringComparison.Ordinal))
        {
            EndQuickMatch("the player left the room");
            ShowSearchStopped();
            return;
        }
        if (_qmStage == QuickMatchStage.Waiting && DateTime.UtcNow - _qmStartedUtc >= QuickMatchPlanner.GiveUpAfter)
        {
            await CancelQuickMatchAsync(Strings.Get("QmTimedOut"), preferDesktop: true);
            return;
        }
        RefreshQuickMatchUi();
    }

    /// <summary>Ends the search's state and its clocks. Idempotent.</summary>
    private void EndQuickMatch(string reason)
    {
        if (_qmStage == QuickMatchStage.Idle) return;
        DiagnosticLog.Write(
            $"QuickMatch: search over — {reason} (after {QuickMatchPlanner.Elapsed(DateTime.UtcNow - _qmStartedUtc)}).");
        _qmStage = QuickMatchStage.Idle;
        _qmLobbyId = null;
        _qmClock?.Stop();
        _qmConvergeTimer?.Stop();
        RefreshQuickMatchUi();
    }

    /// <summary>Ends the search and says why, in the tab.</summary>
    private async Task FailQuickMatchAsync(string message)
    {
        EndQuickMatch("failed: " + message);
        await MpAlertOverlay.NoticeAsync(
            TabRootGrid, Strings.Get("QmFindMatch"), Strings.Format("QmFailed", message), Strings.Get("MpAlertOk"));
    }

    /// <summary>Leave the current room without asking (the search decided it, not the player).</summary>
    private async Task LeaveRoomQuietlyAsync(string why)
    {
        if (_session == null) return;
        _lobbyWindow?.SuppressLeaveConfirm();
        try { await _session.LeaveCurrentLobbyAsync(why: why); }
        catch (Exception ex) { DiagnosticLog.Write($"QuickMatch: leaving the room failed — {ex.Message}"); }
        _ = RefreshRoomsListAsync(quiet: true);
    }

    /// <summary>
    /// A system line in the room the player just entered — after the queued state pass that
    /// opens the room window, or it would be written to a window that does not exist yet (the
    /// same wait "Create room" makes for its downgrade line).
    /// </summary>
    private async Task SayInRoomAsync(string lobbyId, string text)
    {
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Normal);
        if (string.Equals(_session?.CurrentLobbyId, lobbyId, StringComparison.Ordinal))
            AppendChatSystem(text);
    }

    /// <summary>
    /// Says, without stopping anything, that Radmin is not ready: needed to play the match, not to
    /// find it. Read off the UI thread, from the base stage only (the same rule the assistant's
    /// own auto-open follows).
    /// </summary>
    private async Task WarnIfRadminNotReadyAsync()
    {
        try
        {
            var stage = await Task.Run(() => RadminAssistantService.StageOf(RadminVpnService.GetStatus()));
            if (stage >= RadminStage.LoggedIn || !IsQuickMatchSearching) return;
            _qmRadminNotReady = true;
            RefreshQuickRoom();
            _showAppToast?.Invoke(new AppToast.ToastOptions(
                "⚠",
                Strings.Get("QmRadminTitle"),
                Strings.Get("QmRadminNotice"),
                new[] { new AppToast.ToastAction(Strings.Get("QmRadminOpen"), true, OpenRadminAssistant) },
                AutoDismissMs: 15000, Cause: ToastCause.Answer));
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"QuickMatch: Radmin probe failed — {ex.Message}");
        }
    }

    /// <summary>
    /// The button's caption and tooltip, and the title-bar pill. Stopping is always possible;
    /// starting needs the backend, so offline it is disabled unless a search is running.
    /// </summary>
    private void RefreshQuickMatchUi()
    {
        var searching = IsQuickMatchSearching;
        var elapsed = QuickMatchPlanner.Elapsed(DateTime.UtcNow - _qmStartedUtc);
        if (QuickMatchButton != null)
        {
            QuickMatchButton.Content = searching ? Strings.Format("QmSearching", elapsed) : Strings.Get("QmFindMatch");
            QuickMatchButton.IsEnabled = searching || !_offlineMode;
            QuickMatchButton.ToolTip = !searching && _offlineMode
                ? _offlineNeedsInternet
                : TooltipHelper.Wrap(Strings.Get(searching ? "QmSearchingTip" : "QmFindMatchTip"));
        }
        _setQuickMatchPill?.Invoke(
            searching ? Strings.Format("QmPill", elapsed) : null,
            searching ? Strings.Format("QmPillTip", _qmModName ?? _qmModId ?? "") : null);
        RefreshQuickRoom();
    }

    /// <summary>The room is only a search so far: alone in the room the search waits in.</summary>
    internal bool QuickSearchAlone
    {
        get
        {
            var demo = _demoQuickSearch != null
                && (_openingDemoRoom || (_demoRoomWindow != null && ReferenceEquals(_lobbyWindow, _demoRoomWindow)));
            var searchingHere = demo || (_qmStage == QuickMatchStage.Waiting && _qmLobbyId != null
                && string.Equals(_session?.CurrentLobbyId, _qmLobbyId, StringComparison.Ordinal));
            return QuickMatchPlanner.WaitsAlone(searchingHere, RoomPlayerCount());
        }
    }

    private string QuickSearchModName
        => _qmModName ?? (_demoQuickSearch != null ? Strings.Get("MpTournamentDemoWatchMod") : _qmModId ?? "");

    /// <summary>
    /// The room window while it is only a search (design 96): the search box and the footer stand in
    /// for the actions, and the free seat says a rival is coming. Goes back on its own the moment
    /// somebody joins, because the same check answers both ways.
    /// </summary>
    private void RefreshQuickRoom()
    {
        var w = _lobbyWindow;
        if (w == null) return;
        var alone = QuickSearchAlone;
        var shown = alone ? Visibility.Visible : Visibility.Collapsed;
        w.QuickSearchCard.Visibility = shown;
        w.QuickSearchFooter.Visibility = shown;
        w.RoomFooter.Visibility = alone ? Visibility.Collapsed : Visibility.Visible;
        if (alone != _qmRoomWasAlone)
        {
            _qmRoomWasAlone = alone;
            StopQuickSeatDot();
            RenderRoomMembers();
        }
        if (!alone) return;

        var demo = _demoQuickSearch;
        var elapsed = demo?.QuickElapsed ?? QuickMatchPlanner.Elapsed(DateTime.UtcNow - _qmStartedUtc);
        var radmin = demo?.QuickRadminMissing ?? _qmRadminNotReady;
        w.QuickSearchLabel.Text = Strings.Get("QmRoomSearching");
        w.QuickSearchClock.Text = elapsed;
        w.QuickSearchSince.Text = Strings.Format("QmRoomSince",
            (demo != null ? DateTime.Now.AddMinutes(-2) : _qmStartedUtc.ToLocalTime()).ToString("HH:mm", Strings.Culture));
        w.QuickSearchBody.Text = Strings.Format("QmRoomBody", QuickSearchModName);
        w.QuickSearchNote.Text = Strings.Get("QmRoomNote");
        w.QuickFooterTitle.Text = radmin
            ? Strings.Get("QmRoomRadminTitle") : Strings.Format("QmRoomFooter", elapsed);
        w.QuickFooterTitle.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty,
            radmin ? "WindowNoticeTitle" : "WindowStrongText");
        w.QuickFooterNote.Text = radmin
            ? Strings.Format("QmRoomRadminBody", elapsed)
            : Strings.Format("QmRoomGiveUp", (int)QuickMatchPlanner.GiveUpAfter.TotalMinutes);
        w.QuickRadminButton.Content = Strings.Get("QmRadminOpen");
        w.QuickRadminButton.Visibility = radmin ? Visibility.Visible : Visibility.Collapsed;
        w.QuickStopButton.Content = Strings.Get("QmPillCancelTip");
    }

    /// <summary>"Stop searching": nothing to confirm, the room is empty.</summary>
    private void StopQuickMatchFromRoom()
    {
        if (_demoQuickSearch != null)
        {
            DiagnosticLog.Write("QuickMatch demo: stop pressed; nothing to stop.");
            ShowSearchStopped();
            return;
        }
        _ = CancelQuickMatchAsync(Strings.Get("QmCancelled"));
    }

    private void ShowSearchStopped() => _showAppToast?.Invoke(new AppToast.ToastOptions(
        "⏹", Strings.Get("QmFindMatch"), Strings.Get("QmCancelled"), Array.Empty<AppToast.ToastAction>(),
        AutoDismissMs: 6000, Cause: ToastCause.Answer));

    private System.Windows.Shapes.Ellipse QuickSeatDot()
    {
        StopQuickSeatDot();
        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 6,
            Height = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 2, 0),
            Fill = (Brush)Application.Current.FindResource("MpAction"),
        };
        if (RankBadge.ReducedEffects || !SystemParameters.ClientAreaAnimation) return dot;
        var pulse = new System.Windows.Media.Animation.DoubleAnimation(1, 0.35, TimeSpan.FromSeconds(0.8))
        {
            AutoReverse = true,
            RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
        };
        System.Windows.Media.Animation.Timeline.SetDesiredFrameRate(pulse, 30);
        _qmSeatDotClock = pulse.CreateClock();
        dot.ApplyAnimationClock(OpacityProperty, _qmSeatDotClock);
        return dot;
    }

    private void StopQuickSeatDot()
    {
        _qmSeatDotClock?.Controller?.Stop();
        _qmSeatDotClock = null;
    }
}
