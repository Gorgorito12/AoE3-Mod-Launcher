using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// What the room window shows besides its roster (design 87): the header, the panels of the left
/// column and the footer. Everything here is derived from the room's state and written in one
/// pass, <see cref="RefreshRoomView"/>, so no frame has to know which of them it affects.
/// </summary>
public partial class MultiplayerTab
{
    /// <summary>The mod whose picture and colour the window wears, so a repaint does not redo them.</summary>
    private string? _roomChromeModId;

    /// <summary>The room's own name; the header shows the result in its place when there is one.</summary>
    private string _roomTitle = "";

    /// <summary>Who left and made this player the host, until a match starts.</summary>
    private string? _roomHostLeftName;

    /// <summary>No 26.x adapter at the last look. Read on the room's 2.5-s tick, never on a paint.</summary>
    private bool _roomRadminMissing;

    /// <summary>The finished match, while the window shows it.</summary>
    private MatchOutcomeView? _roomResult;

    /// <summary>The sample on screen, when it is one.</summary>
    private RoomDemoData.Sample? _demoRoomSample;

    /// <summary>The host's name while it was known, for "X left the room".</summary>
    private string? _lastHostLogin;

    /// <summary>The rival a rematch invited, until they arrive.</summary>
    private RematchInvite? _rematchInvited;

    /// <summary>The footer's last answer, for the buttons that act on it.</summary>
    private RoomFooterView? _roomFooter;

    private sealed record RematchInvite(string UserId, string Login, double? Rating, string? AvatarUrl);

    /// <summary>Who the last 1v1 was against, so their invitation reads as the rematch it is (87m).</summary>
    private string? _lastRivalUserId;

    /// <summary>The server's answer to an invitation we sent as a rematch: the room stays, the wait ends.</summary>
    private void EndRematchWait()
    {
        if (_rematchInvited == null) return;
        _rematchInvited = null;
        RenderRoomMembers();
    }

    /// <summary>The room's stage as the footer sees it.</summary>
    private RoomStage CurrentRoomStage()
    {
        if (_demoRoomSample?.Stage is { } demo) return demo;
        if (_previewCountdown) return RoomStage.Countdown;
        if (_roomResult is { } settled && _roomTournament is { } bracket)
        {
            if (bracket.PendingReport is "awaiting_confirmation") return RoomStage.ResultAwaiting;
            if (settled.Verdict == MatchVerdict.NoResult) return RoomStage.ResultUndecided;
        }
        return _matchPhase switch
        {
            MatchPhase.Starting => RoomStage.Countdown,
            MatchPhase.InGame => RoomStage.InGame,
            MatchPhase.AwaitingResult or MatchPhase.Result => _roomResult is { } result
                ? ResultStageOf(result)
                : RoomStage.ResultWaiting,
            _ => _roomMatchLive ? RoomStage.GameClosed : RoomStage.Room,
        };
    }

    internal static RoomStage ResultStageOf(MatchOutcomeView model)
    {
        if (model.InPlacement) return RoomStage.ResultPlacement;
        return model.LooksUnrated ? RoomStage.ResultUnrated : RoomStage.ResultRated;
    }

    private bool RoomViewerIsHost() => _isHostInCurrentRoom || IsRoomViewerHost();

    /// <summary>Header, panels and footer, from the state as it is now.</summary>
    private void RefreshRoomView()
    {
        // The quick match card in the list follows who is in the room (key-cached: a repaint
        // with the same words rebuilds nothing).
        RenderQuickCard();
        if (_lobbyWindow == null) return;
        RefreshRoomChrome();
        RefreshRoomHeader();
        RefreshRoomPanels();
        RefreshRoomFooter();
        _lobbyWindow.QueueFrost();
    }

    private void RefreshRoomChrome()
    {
        var w = _lobbyWindow!;
        var id = _currentLobbyModId;
        if (string.IsNullOrEmpty(id) || string.Equals(id, _roomChromeModId, StringComparison.Ordinal)) return;
        if (ModRegistry.Find(id) is not { } mod) return;
        _roomChromeModId = id;
        ModDialogChrome.Config ??= Cfg();
        ModDialogChrome.AttachRoom(w, w.TitleBarControl, w.LobbyRootGrid, mod);
    }

    private void RefreshRoomHeader()
    {
        var w = _lobbyWindow!;
        var result = _roomResult;
        var modName = string.IsNullOrEmpty(_currentLobbyModId)
            ? "" : ResolveModDisplayName(_currentLobbyModId!);
        w.RoomKickerText.Text = result != null
            ? Strings.Format("RoomKickerOver", _roomTitle.ToUpper(Strings.Culture))
            : Strings.Format("RoomKicker", modName.ToUpper(Strings.Culture));
        w.RoomTitleText.Text = result is { } r ? ResultTitle(r) : _roomTitle;

        // The mode, the version and the code say what the room is. All three are facts about a
        // room still open, and a result replaces them with what happened.
        var format = CurrentRoomFormat();
        var seats = RoomPlayingSeats();
        var observers = _currentLobbySpectatorSlots;
        var labelKey = RoomFormats.LabelKey(format);
        w.RoomModeText.Text = _currentLobbyIsCompetitive
            ? labelKey == null ? Strings.Get("MpRoomCompetitiveBadge") : Strings.Format("RoomModeComp", Strings.Get(labelKey))
            : Strings.Format("RoomModeCasual", seats)
              + (observers == 1 ? Strings.Get("RoomModeObserver")
                  : observers > 1 ? Strings.Format("RoomModeObservers", observers) : "");
        w.RoomModeDot.Fill = Solid(_currentLobbyIsCompetitive ? "#E6C06A" : "#8C9CB1");

        var version = string.IsNullOrEmpty(_currentLobbyModId)
            ? "" : Cfg().GetState(_currentLobbyModId!).LastKnownVersion ?? "";
        w.RoomVersionName.Text = modName;
        w.RoomVersionNumber.Text = version;
        w.RoomVersionNumber.Visibility = version.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        w.RoomVersionChip.Visibility = modName.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        var hasPassword = TryGetCurrentLobbyHasPassword(out var has) && has;
        if (_demoRoomSample != null) hasPassword = _demoRoomSample.Password;
        w.RoomPasswordChip.Visibility = hasPassword ? Visibility.Visible : Visibility.Collapsed;
        w.RoomPasswordChipText.Text = Strings.Get("RoomPasswordChip");

        w.RoomCopyWord.Text = Strings.Get(_roomCodeJustCopied ? "RoomCopiedShort" : "RoomCopy");
        w.CopyRoomIdButton.Visibility = InTournamentRoom ? Visibility.Collapsed : Visibility.Visible;

        w.RenameRoomButton.Visibility = RoomViewerIsHost() && result == null && !InTournamentRoom && !QuickSearchAlone
            ? Visibility.Visible : Visibility.Collapsed;

        RefreshRoomTournamentHeader(result);

        w.InvitePlayersButton.Content = Strings.Get("MpRoomInvite");
        w.InvitePlayersButton.Visibility = result == null && !InTournamentRoom && !QuickSearchAlone
            ? Visibility.Visible : Visibility.Collapsed;

        RefreshRoomEloCards(result);
    }

    /// <summary>
    /// What a bracket match changes in the header (85): the tournament's name over who plays whom,
    /// the rounds, and the bracket instead of the code. Nothing here applies to any other room.
    /// </summary>
    private void RefreshRoomTournamentHeader(MatchOutcomeView? result)
    {
        var w = _lobbyWindow!;
        var bracket = _roomTournament;
        w.TournamentMode = bracket != null;
        w.RoomRoundsPath.Visibility = bracket != null ? Visibility.Visible : Visibility.Collapsed;
        // Once the footer's own button is "View bracket", a second one above it says nothing.
        var footerHasIt = CurrentRoomStage() is RoomStage.ResultUndecided or RoomStage.ResultAwaiting;
        w.RoomViewBracketButton.Visibility = bracket != null && result == null && !footerHasIt
            ? Visibility.Visible : Visibility.Collapsed;
        if (bracket == null) return;

        w.RoomKickerText.Text = bracket.Name.ToUpper(Strings.Culture);
        if (result == null) w.RoomTitleText.Text = Strings.Format("RtTitleVs", bracket.Side1.Name, bracket.Side2.Name);
        w.RoomModeText.Text = Strings.Format("RtChip", bracket.Format);
        w.RoomModeDot.Fill = (Brush)w.FindResource("ModAccentText");
        w.RoomViewBracketButton.Content = Strings.Get("RtViewBracket");
        var title = Strings.Format("RtWindowTitle", bracket.Name);
        w.Title = title;
        w.TitleBarControl.Title = title;

        var key = bracket.TournamentId + bracket.CurrentRound + Strings.Language;
        if (key == _roomPathDrawnFor) return;
        _roomPathDrawnFor = key;
        w.RoomRoundsPath.Children.Clear();
        for (var i = 0; i < bracket.RoundKeys.Count; i++)
        {
            var number = i + 1;
            var word = new TextBlock
            {
                Text = bracket.RoundKeys[i] == "MpTournamentRoundSemi"
                    ? Strings.Get("RtPathSemi")
                    : RoundWord(Strings.Format(bracket.RoundKeys[i], number)),
                FontSize = (double)Application.Current.FindResource("SetCaptionSize"),
                FontWeight = number == bracket.CurrentRound ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
            };
            if (number == bracket.CurrentRound) word.SetResourceReference(TextBlock.ForegroundProperty, "ModAccentText");
            else word.Foreground = Solid(number < bracket.CurrentRound ? "#8C9CB1" : "#61779A");
            w.RoomRoundsPath.Children.Add(word);
            if (number == bracket.RoundKeys.Count) continue;
            w.RoomRoundsPath.Children.Add(new TextBlock
            {
                Text = "\u203A",
                Margin = new Thickness(6, 0, 6, 0),
                FontSize = (double)Application.Current.FindResource("SetCaptionSize"),
                Foreground = Solid("#61779A"),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
    }

    /// <summary>The bracket's headers shout; a name in a line of them does not.</summary>
    private static string RoundWord(string label)
        => label.Length == 0 ? label : label[..1].ToUpper(Strings.Culture) + label[1..].ToLower(Strings.Culture);

    private string? _roomPathDrawnFor;

    /// <summary>The tournament of a room being opened from its card, until the join lands.</summary>
    private string? _pendingRoomTournamentId;

    /// <summary>The bracket slot of this room, once it is known.</summary>
    private TournamentRoomContext? _roomTournament;

    /// <summary>The tournament this room belongs to: from the lobby, or from the card that opened it.</summary>
    private string? _currentLobbyTournamentId;

    private bool _roomTournamentAsked;

    /// <summary>Reads the bracket the room belongs to, once per room. Without the id the room keeps
    /// the plain header: a server that predates <c>tournament_id</c>, reached by an invite link.</summary>
    private async void EnsureRoomTournament()
    {
        if (_roomTournament != null || _roomTournamentAsked || !InTournamentRoom || _demoRoomSample != null) return;
        var id = _currentLobbyTournamentId;
        if (string.IsNullOrEmpty(id)) return;
        _roomTournamentAsked = true;
        await RefreshRoomTournamentAsync(id);
    }

    private async System.Threading.Tasks.Task RefreshRoomTournamentAsync(string id)
    {
        try
        {
            var detail = await TournamentApi.GetTournamentAsync(id);
            if (_lobbyWindow == null || !InTournamentRoom) return;
            _roomTournament = TournamentRoomContext.From(detail, _currentLobbyTournamentMatchId);
            RenderRoomMembers();
        }
        catch (Exception ex) { DiagnosticLog.Write($"Room: the bracket could not be read - {ex.Message}"); }
    }

    private RoomFacts WithTournamentFacts(RoomFacts facts, MatchOutcomeView? result)
    {
        if (_roomTournament is not { } bracket) return facts;
        var me = RoomViewerId;
        var rivalSide = bracket.RivalOf(me);
        var present = rivalSide.Players.Where(p => _roomMembers.ContainsKey(p.UserId)).ToList();
        var absentAll = bracket.Side1.Players.Concat(bracket.Side2.Players)
            .FirstOrDefault(p => !_roomMembers.ContainsKey(p.UserId));
        var won = result?.Verdict == MatchVerdict.Win;
        var rivalMember = bracket.IsTeams || present.Count == 0 ? null : _roomMembers[present[0].UserId];
        return facts with
        {
            Tournament = true,
            Competitive = true,
            Absent = absentAll?.Name,
            Organizer = bracket.OrganizerName,
            MatchPlayers = bracket.PlayerCount,
            RivalName = rivalSide.Players.Count == 1 ? rivalSide.Players[0].Name : rivalSide.Name,
            RivalReady = rivalMember?.Ready ?? false,
            AfterResult = won ? bracket.AfterWin() : null,
            ResultWon = won,
        };
    }

    private System.Windows.Threading.DispatcherTimer? _roomClock;

    /// <summary>The figure beside the footer's title. The match clock has its own writer; the wait
    /// and the countdown are drawn here, and the timer lives only while one of them is on screen.</summary>
    private void RefreshRoomClock(RoomClock clock)
    {
        if (clock != RoomClock.Waiting)
        {
            _roomClock?.Stop();
            _roomClock = null;
        }
        var w = _lobbyWindow!;
        w.InGameMatchTimer.Foreground = clock == RoomClock.Countdown
            ? (Brush)w.FindResource("ModAccentText")
            : (Brush)Application.Current.FindResource(clock == RoomClock.Waiting ? "WindowMuted" : "UiHeadText");
        if (clock == RoomClock.Countdown) w.InGameMatchTimer.Text = w.CountdownNumber.Text;
        if (clock != RoomClock.Waiting) return;
        PaintRoomWait();
        if (_roomClock != null) return;
        _roomClock = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _roomClock.Tick += (_, _) => PaintRoomWait();
        _roomClock.Start();
    }

    private void PaintRoomWait()
    {
        if (_lobbyWindow == null) { _roomClock?.Stop(); _roomClock = null; return; }
        var waited = _currentLobbyCreatedUtc is { } opened ? DateTime.UtcNow - opened : TimeSpan.Zero;
        if (waited < TimeSpan.Zero) waited = TimeSpan.Zero;
        _lobbyWindow.InGameMatchTimer.Text = $"{(int)waited.TotalMinutes:00}:{waited.Seconds:00}";
    }

    private string AccentTextHex()
        => _lobbyWindow!.FindResource("ModAccentText") is SolidColorBrush { Color: var c }
            ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : "#55ACEE";

    private static string Say(RoomText text)
        => Strings.Format(text.Key, text.Args.Select(a => a is RoomKey key ? Strings.Get(key.Key) : a).ToArray());

    /// <summary>EL CRUCE: both sides of the bracket slot, in or out of the room.</summary>
    private void RenderTournamentRoster(TournamentRoomContext bracket)
    {
        var w = _lobbyWindow!;
        var host = w.RoomMembersPanel;
        void Side(TournamentRoomSide side, int number)
        {
            if (bracket.IsTeams)
            {
                var head = BuildRosterGroupHead(side.Name, side.Seed is { } s ? "#" + s : null, Strings.Get("RtSeedTip"), new TextBlock
                {
                    Text = Strings.Format("RtSide", number),
                    FontSize = (double)Application.Current.FindResource("SetCaptionSize"),
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Solid("#A8BCD2"),
                    VerticalAlignment = VerticalAlignment.Center,
                });
                // No "join this team" button here, so nothing asks for the extra height.
                head.MinHeight = 0;
                if (side.Seed != null && head.Children.Count > 1 && head.Children[1] is TextBlock seedText)
                    seedText.FontFamily = (FontFamily)Application.Current.FindResource("MonoFont");
                host.Children.Add(head);
            }
            foreach (var player in side.Players) host.Children.Add(BuildBracketRow(player, side, bracket));
        }
        Side(bracket.Side1, 1);
        Side(bracket.Side2, 2);
        w.RoomMoreFreeText.Visibility = Visibility.Visible;
        w.RoomMoreFreeText.Text = Strings.Get(bracket.IsTeams ? "RtNoteTeams" : "RtNote1v1");
    }

    /// <summary>The detail line with its figures in the mono face the bracket's numbers use.</summary>
    private static void AddDetailRuns(TextBlock line, string detail)
    {
        var last = 0;
        foreach (System.Text.RegularExpressions.Match figure in FigureInDetail.Matches(detail))
        {
            if (figure.Index > last) line.Inlines.Add(new System.Windows.Documents.Run(detail[last..figure.Index]));
            line.Inlines.Add(new System.Windows.Documents.Run(figure.Value)
            {
                FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"),
                Foreground = Solid("#C3D2E5"),
            });
            last = figure.Index + figure.Length;
        }
        if (last < detail.Length) line.Inlines.Add(new System.Windows.Documents.Run(detail[last..]));
    }

    private static readonly System.Text.RegularExpressions.Regex FigureInDetail = new(@"\d[\d/.,]*\d|\d");

    private FrameworkElement BuildBracketRow(TournamentRoomPlayer player, TournamentRoomSide side, TournamentRoomContext bracket)
    {
        var inRoom = _roomMembers.TryGetValue(player.UserId, out var member);
        var isMe = IsRoomViewer(player.UserId);
        var isHost = string.Equals(player.UserId, _roomHostUserId, StringComparison.Ordinal);

        var tags = new List<string>(3);
        if (isMe) tags.Add(Strings.Get("MpTeamsYou"));
        if (player.Captain && bracket.IsTeams) tags.Add(Strings.Get("MpTeamsCaptain"));
        if (isHost) tags.Add(Strings.Get("RoomTagHost"));
        var stack = RosterTwoLines(player.Name, "#F0F5FB", string.Join(" \u00B7 ", tags), "");

        var line = new TextBlock
        {
            Margin = new Thickness(0, 3, 0, 0),
            FontSize = (double)Application.Current.FindResource("SetControlSize"),
            Foreground = Solid("#A8BCD2"),
            TextWrapping = TextWrapping.Wrap,
        };
        if (!bracket.IsTeams && side.Seed is { } seed)
        {
            var mark = new System.Windows.Documents.Run("#" + seed)
            {
                FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"),
                Foreground = Solid("#8C9CB1"),
            };
            line.Inlines.Add(mark);
            line.ToolTip = Strings.Get("RtSeedTip");
            if (member != null) line.Inlines.Add(new System.Windows.Documents.Run(" \u00B7 "));
        }
        var detail = member != null ? MemberDetailLine(member) : "";
        if (detail.Length > 0) AddDetailRuns(line, detail);
        if (line.Inlines.Count > 0) stack.Children.Add(line);

        FrameworkElement avatar = inRoom ? BuildAvatarDisc(player.Name, member!.AvatarUrl, 32) : BuildEmptySeatDisc();
        var state = inRoom
            ? BracketState(member!, isMe, bracket, side, out var dim)
            : RosterState(Strings.Get("RtStAbsent"), "#8C9CB1", "#61779A", ring: true);
        var row = RosterRow(avatar, null, stack, state);
        if (inRoom && BracketDim(member!, isMe)) row.Opacity = .6;
        return row;
    }

    private bool BracketDim(RoomMemberEntry m, bool isMe)
    {
        MemberState(m, isMe, out var dim);
        return dim && CurrentRoomStage() == RoomStage.Room;
    }

    private StackPanel BracketState(RoomMemberEntry m, bool isMe, TournamentRoomContext bracket, TournamentRoomSide side, out bool dim)
    {
        dim = false;
        if (_roomResult is { } result && result.Verdict is MatchVerdict.Win or MatchVerdict.Loss)
        {
            var mine = bracket.SideOf(RoomViewerId) == (ReferenceEquals(side, bracket.Side1) ? 1 : 2);
            return mine == (result.Verdict == MatchVerdict.Win)
                ? RosterState(Strings.Get("RtStThrough"), "#7FE0A8", "#4FD68A")
                : RosterState(Strings.Get("RtStOut"), "#A8BCD2", "#61779A");
        }
        return MemberState(m, isMe, out dim);
    }

    /// <summary>The panels a bracket match words differently.</summary>
    private void RefreshRoomTournamentPanels()
    {
        var w = _lobbyWindow!;
        var bracket = _roomTournament;
        w.RoomStakeLines.Visibility = bracket != null ? Visibility.Visible : Visibility.Collapsed;
        w.RoomStakeText.Visibility = bracket == null ? Visibility.Visible : Visibility.Collapsed;
        w.RoomCountsPanel.ClearValue(Border.BorderBrushProperty);
        w.RoomCountsPanel.ClearValue(FrameworkElement.ToolTipProperty);
        if (bracket == null)
        {
            w.PlayersListHeader.Text = Strings.Get(RoomShowsTeams() ? "RoomPanelTeams" : "RoomPanelPlayers");
            w.RoomStakeLabel.Text = Strings.Get("RoomStakeTitle");
            w.RoomPlayersCount.Visibility = Visibility.Visible;
            return;
        }

        var result = _roomResult;
        var decided = result?.Verdict is MatchVerdict.Win or MatchVerdict.Loss;
        // The panel has no line for it here; that the recording is stored stays one hover away.
        w.PreflightReplayText.Visibility = Visibility.Collapsed;
        w.RoomCountsPanel.ToolTip = TooltipHelper.Wrap(Strings.Get("MpPreflightReplay"));
        w.PlayersListHeader.Text = Strings.Get("RtPanelMatch");
        w.RoomPlayersCount.Visibility = Visibility.Collapsed;
        w.RoomResultPanel.Visibility = Visibility.Collapsed;
        w.RoomStakeLabel.Text = Strings.Get(decided ? "RoomPanelResult" : "RtPanelStake");
        w.RoomStakePanel.Visibility = Visibility.Visible;
        w.RoomCountsPanel.Visibility = Visibility.Visible;
        w.RoomStakeNote.Visibility = Visibility.Collapsed;
        w.PreflightAbandonText.Visibility = Visibility.Collapsed;
        if (CurrentRoomStage() == RoomStage.ResultUndecided) w.RoomCountsPanel.BorderBrush = Solid("#99F4900C");
        SetCountsText(w, "RtCountsBody");

        var lines = decided
            ? bracket.ResultLines(result!.Verdict == MatchVerdict.Win, RoomViewerId)
            : bracket.StakeLines();
        w.RoomStakeLines.Children.Clear();
        foreach (var line in lines)
        {
            var row = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 7,
                Height = 7,
                Margin = new Thickness(0, 6, 10, 0),
                VerticalAlignment = VerticalAlignment.Top,
            };
            if (line.Tone == TournamentStakeTone.Mod) dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "ModAccentText");
            else dot.Fill = Solid(line.Tone switch
            {
                TournamentStakeTone.Green => "#4FD68A",
                TournamentStakeTone.Elo => "#55ACEE",
                _ => "#61779A",
            });
            row.Children.Add(dot);
            var text = new TextBlock
            {
                Text = Say(line.Text),
                FontSize = (double)Application.Current.FindResource("DialogBodySize"),
                Foreground = (Brush)Application.Current.FindResource("UiSoftText"),
                TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            w.RoomStakeLines.Children.Add(row);
        }
    }

    /// <summary>
    /// The rival came in while the player looked elsewhere (85j). The room can wait a long time for
    /// them, so this is the moment that needs a card; with the window in front it would only repeat
    /// what the roster just said.
    /// </summary>
    private void OnBracketRivalJoined(string userId, string login)
    {
        if (_roomTournament is not { } bracket || IsRoomViewer(userId) || _lobbyWindow is not { } w) return;
        if (w.IsActive) return;
        var round = Strings.Format(bracket.RoundKeys[Math.Clamp(bracket.CurrentRound - 1, 0, bracket.RoundKeys.Count - 1)], bracket.CurrentRound);
        _showAppToast?.Invoke(new AppToast.ToastOptions(
            "\u2694",
            Strings.Format("RtToastJoined", login),
            Strings.Format("RtToastBody", bracket.Name, RoundWord(round)),
            new[]
            {
                new AppToast.ToastAction(Strings.Get("QmFoundView"), true, () =>
                {
                    _bringMultiplayerToFront?.Invoke();
                    if (_lobbyWindow == null) return;
                    if (_lobbyWindow.WindowState == WindowState.Minimized) _lobbyWindow.WindowState = WindowState.Normal;
                    _lobbyWindow.Activate();
                }),
            },
            PreferDesktop: true,
            Cause: ToastCause.Request));
    }

    /// <summary>The way on after a result: the next game, or the bracket it fed.</summary>
    private void RoomResultPrimaryPressed()
    {
        if (_roomFooter?.Primary == RoomPrimary.ViewBracket) OpenRoomBracket();
        else _ = RematchAsync();
    }

    /// <summary>"View bracket": the Tournaments tab on this tournament, with the match in view.</summary>
    private void OpenRoomBracket()
    {
        if (_roomTournament is not { } bracket || _demoRoomSample != null) return;
        _bringMultiplayerToFront?.Invoke();
        SubtabTournaments_Click(this, new RoutedEventArgs());
        _ = SelectTournamentAsync(bracket.TournamentId);
    }

    private string ResultTitle(MatchOutcomeView model)
    {
        var rival = model.RivalLogin;
        return model.Verdict switch
        {
            MatchVerdict.Win => string.IsNullOrEmpty(rival)
                ? Strings.Get("MpResultWin") : Strings.Format("RoomResultWonVs", rival),
            MatchVerdict.Loss => string.IsNullOrEmpty(rival)
                ? Strings.Get("MpResultLoss") : Strings.Format("RoomResultLostVs", rival),
            _ => Strings.Get("MpResultNone"),
        };
    }

    private void RefreshRoomEloCards(MatchOutcomeView? model)
    {
        var w = _lobbyWindow!;
        var placing = model is { InPlacement: true };
        var rated = model is { RatingAfter: not null } && !placing;
        w.RoomEloCard.Visibility = rated ? Visibility.Visible : Visibility.Collapsed;
        w.RoomPlaceCard.Visibility = placing ? Visibility.Visible : Visibility.Collapsed;
        if (model == null) return;

        if (rated)
        {
            w.RoomEloLabel.Text = Strings.Get("RoomYourElo");
            w.RoomEloValue.Text = Math.Round(model.RatingAfter!.Value).ToString(CultureInfo.InvariantCulture);
            var delta = model.RatingDelta ?? 0;
            var moved = delta != 0 && !model.LooksUnrated;
            w.RoomEloDelta.Text = moved
                ? (delta > 0 ? "+" : "−") + Math.Abs(delta).ToString(CultureInfo.InvariantCulture)
                : "±0";
            var up = moved && delta > 0;
            w.RoomEloDeltaChip.Background = Solid(up ? "#244FD68A" : "#14FFFFFF");
            w.RoomEloDeltaChip.BorderBrush = Solid(up ? "#6B4FD68A" : "#29FFFFFF");
            w.RoomEloDelta.Foreground = Solid(up ? "#7FE0A8" : "#C3D2E5");
            w.RoomEloNote.Text = model.LooksUnrated
                ? Strings.Get("RoomEloNotRated")
                : model.RatingBefore is { } before
                    ? Strings.Format("RoomEloBefore", Math.Round(before).ToString(CultureInfo.InvariantCulture))
                    : "";

            // The age and the place, only when the server sent them. A place worked out here
            // would disagree with the ladder.
            var rank = model.EnteredRank;
            var showRank = rank is > 0 && !model.LooksUnrated;
            w.RoomEloRankBlock.Visibility = showRank ? Visibility.Visible : Visibility.Collapsed;
            if (showRank)
            {
                w.RoomEloRankHead.Children.Clear();
                var age = RankAges.For(rank!.Value, model.LadderSize);
                var shield = Solid("#9FC4D6");
                w.RoomEloRankHead.Children.Add(new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M1.1,0 L16.9,0 L18,3.1 L17.3,12.6 L9,21 L.7,12.6 L0,3.1 Z"),
                    Fill = shield,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                });
                w.RoomEloRankHead.Children.Add(new TextBlock
                {
                    Text = Strings.Get(RankAges.NameKey(age)),
                    FontSize = (double)Application.Current.FindResource("FontSizeBody"),
                    FontWeight = FontWeights.SemiBold,
                    Foreground = shield,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                w.RoomEloRankPlace.Text = model.LadderSize is { } size
                    ? Strings.Format("RoomRankPlace", rank.Value, size) : "";
                w.RoomEloRankPlace.Visibility = model.LadderSize != null ? Visibility.Visible : Visibility.Collapsed;
                var next = RankAges.PlacesToNextAge(rank.Value, model.LadderSize);
                w.RoomEloRankNext.Visibility = next != null ? Visibility.Visible : Visibility.Collapsed;
                if (next is { } nextAge)
                    w.RoomEloRankNext.Text = Strings.Format(nextAge.Places == 1 ? "RoomRankNextOne" : "RoomRankNext",
                        nextAge.Places, Strings.Get(RankAges.NameKey(nextAge.Age)));
            }
            return;
        }

        if (placing)
        {
            var played = model.PlacementPlayed ?? 0;
            var required = model.PlacementRequired ?? 0;
            var left = Math.Max(0, required - played);
            w.RoomPlaceLabel.Text = Strings.Get("RoomPlacementLabel");
            w.RoomPlaceValue.Text = played + "/" + required;
            w.RoomPlaceSub.Text = left == 1
                ? Strings.Get("RoomPlacementLeftOne") : Strings.Format("RoomPlacementLeft", left);
        }
    }

    private void RefreshRoomPanels()
    {
        var w = _lobbyWindow!;
        var stage = CurrentRoomStage();
        var hasResult = _roomResult != null;
        var live = stage is RoomStage.InGame or RoomStage.GameClosed;
        var competitive = _currentLobbyIsCompetitive;
        EnsureRoomTournament();
        var alone = !competitive && RoomPlayerCount() == 1 && !live && !hasResult && !QuickSearchAlone;

        w.RoomResultPanel.Visibility = hasResult ? Visibility.Visible : Visibility.Collapsed;
        w.RoomSoloPanel.Visibility = alone ? Visibility.Visible : Visibility.Collapsed;

        var observers = _roomMembers.Values.Any(m => string.Equals(m.Role, "spectator", StringComparison.Ordinal));
        w.RoomRecordPanel.Visibility = !competitive && !live && !hasResult && !observers
                                       && !QuickSearchAlone && !Cfg().RoomRecordNoticeHidden
            ? Visibility.Visible : Visibility.Collapsed;

        w.RoomCountsPanel.Visibility = competitive && !hasResult ? Visibility.Visible : Visibility.Collapsed;
        var stake = competitive && !hasResult && CurrentRoomFormat() == RoomFormat.OneVOne;
        w.RoomStakePanel.Visibility = stake ? Visibility.Visible : Visibility.Collapsed;
        if (stake)
        {
            var odds = WinOddsView.ForPlayer(_roomOdds, RoomViewerId);
            var hidden = WinOddsView.HiddenByPlacement(_roomOdds, AnyRoomPlayerPlacing(team: false));
            w.RoomStakeNote.Visibility = odds != null && !hidden ? Visibility.Visible : Visibility.Collapsed;
            if (odds is int pct && !hidden) w.RoomStakeNote.Text = Strings.Format("RoomStakeOdds", pct);
        }

        // The sentence about the abandonment rule stays where it always was: under what the
        // competitive room promises, only for the format it applies to.
        w.PreflightAbandonText.Visibility = stake && RoomFormats.AbandonmentApplies(CurrentRoomFormat())
            ? Visibility.Visible : Visibility.Collapsed;
        w.PreflightReplayText.Visibility = competitive ? Visibility.Visible : Visibility.Collapsed;

        if (hasResult) RenderRoomResultPanel(_roomResult!);
        RefreshRoomTournamentPanels();
    }

    private string? _roomResultDrawnFor;

    /// <summary>RESULTADO: the rival and the facts of the match. A case the facts cannot tell
    /// (no result, a repeated opponent) keeps its card, which is what explains it.</summary>
    private void RenderRoomResultPanel(MatchOutcomeView model)
    {
        var w = _lobbyWindow!;
        var key = model.GetHashCode().ToString(CultureInfo.InvariantCulture) + Strings.Language;
        if (key == _roomResultDrawnFor) return;
        _roomResultDrawnFor = key;

        var rows = w.RoomResultRows;
        rows.Children.Clear();
        if (!string.IsNullOrEmpty(model.RivalLogin)) rows.Children.Add(BuildResultRivalRow(model));

        void Fact(string key, string value)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(FactWord(Strings.Get(key), "#8C9CB1"));
            var text = FactWord(value, "#DCE7F5");
            text.FontSize = (double)Application.Current.FindResource("DialogBodySize");
            text.TextWrapping = TextWrapping.Wrap;
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            rows.Children.Add(new Border
            {
                Child = row,
                Padding = new Thickness(0, 7, 0, 7),
                BorderBrush = Solid("#0FFFFFFF"),
                BorderThickness = new Thickness(0, 0, 0, 1),
            });
        }

        if (!string.IsNullOrWhiteSpace(model.MyCiv))
            Fact("RoomFactCivs", string.IsNullOrWhiteSpace(model.RivalCiv)
                ? model.MyCiv!
                : Strings.Format("RoomFactVs", model.MyCiv!, model.RivalCiv!));
        if (model.DurationSeconds > 0)
            Fact("RoomFactDuration", Strings.Format("RoomFactMinutes", Math.Max(1, model.DurationSeconds / 60)));
        if (model.Case == ResultCase.Streak && model.StreakCurrent is { } streak)
            Fact("RoomFactStreak", Strings.Format("MpResultStreak", streak));
        if (model.LooksUnrated && !string.IsNullOrEmpty(model.UnratedReason))
            Fact("RoomFactElo", UnratedSentence(model));

        // The recording's file: the one place that points at it.
        if (!string.IsNullOrWhiteSpace(model.RecordingPath))
        {
            var path = model.RecordingPath!;
            var row = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(FactWord(Strings.Get("RoomFactRecording"), "#8C9CB1"));
            var saved = FactWord(Strings.Get("RoomFactSaved"), "#DCE7F5");
            saved.FontSize = (double)Application.Current.FindResource("DialogBodySize");
            Grid.SetColumn(saved, 1);
            row.Children.Add(saved);
            var reveal = new Button
            {
                Content = Strings.Get("RoomFactShowFile"),
                Style = (Style)Application.Current.FindResource("RoomSmallButton"),
                Height = 28,
                Padding = new Thickness(10, 0, 10, 0),
                FontSize = (double)Application.Current.FindResource("SetControlSize"),
                ToolTip = TooltipHelper.Wrap(Strings.Get("MpResultReplayReveal") + Environment.NewLine + path),
            };
            reveal.Click += (_, _) => FileReveal.Reveal(path);
            Grid.SetColumn(reveal, 2);
            row.Children.Add(reveal);
            rows.Children.Add(row);
        }

        // What the facts cannot say: no result, a repeated rival, a placement just finished.
        if (model.Case is not (ResultCase.Plain or ResultCase.Streak or ResultCase.Unrated or ResultCase.Placing))
        {
            var card = MatchResultCard.BuildCaseCard(model);
            ((FrameworkElement)card).Margin = new Thickness(0, 12, 0, 0);
            rows.Children.Add(card);
        }
    }

    private static TextBlock FactWord(string text, string hex) => new()
    {
        Text = text,
        FontSize = (double)Application.Current.FindResource("RoomChatSystemSize"),
        Foreground = Solid(hex),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static string UnratedSentence(MatchOutcomeView model) => model.UnratedReason ?? "";

    private FrameworkElement BuildResultRivalRow(MatchOutcomeView model)
    {
        var login = model.RivalLogin!;
        var entry = _roomMembers.Values.FirstOrDefault(m => string.Equals(m.Login, login, StringComparison.Ordinal));
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var avatar = BuildAvatarDisc(login, entry?.AvatarUrl, 32);
        avatar.Margin = new Thickness(0, 0, 11, 0);
        grid.Children.Add(avatar);
        var stack = RosterTwoLines(login, "#F0F5FB", "", "");
        if (model.RivalRatingShown is { } rating)
        {
            // Before it is the number the room carried; the report only sends the one after.
            var before = entry?.Rating;
            var line = new TextBlock
            {
                Margin = new Thickness(0, 3, 0, 0),
                FontSize = (double)Application.Current.FindResource("SetControlSize"),
                Foreground = Solid("#A8BCD2"),
            };
            line.Inlines.Add(new System.Windows.Documents.Run(Strings.Get("RoomRivalElo") + " "));
            var numbers = before is { } from
                ? $"{Math.Round(from).ToString(CultureInfo.InvariantCulture)} \u2192 {Math.Round(rating).ToString(CultureInfo.InvariantCulture)}"
                : Math.Round(rating).ToString(CultureInfo.InvariantCulture);
            line.Inlines.Add(new System.Windows.Documents.Run(numbers)
            {
                FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"),
                Foreground = Solid("#C3D2E5"),
            });
            stack.Children.Add(line);
            if (before is { } start)
            {
                var delta = (int)Math.Round(rating - start);
                var up = delta > 0;
                var chip = new Border
                {
                    Height = 24,
                    Padding = new Thickness(10, 0, 10, 0),
                    Margin = new Thickness(10, 0, 0, 0),
                    CornerRadius = new CornerRadius(12),
                    Background = Solid(up ? "#244FD68A" : "#14FFFFFF"),
                    BorderBrush = Solid(up ? "#6B4FD68A" : "#29FFFFFF"),
                    BorderThickness = new Thickness(1),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = delta == 0 ? "\u00B10" : (up ? "+" : "\u2212") + Math.Abs(delta).ToString(CultureInfo.InvariantCulture),
                        FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"),
                        FontSize = (double)Application.Current.FindResource("SetControlSize"),
                        FontWeight = FontWeights.SemiBold,
                        Foreground = Solid(up ? "#7FE0A8" : "#C3D2E5"),
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                };
                Grid.SetColumn(chip, 2);
                grid.Children.Add(chip);
            }
        }
        Grid.SetColumn(stack, 1);
        grid.Children.Add(stack);
        return new Border
        {
            Child = grid,
            Padding = new Thickness(0, 2, 0, 10),
            Margin = new Thickness(0, 10, 0, 0),
            BorderBrush = Solid("#0FFFFFFF"),
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
    }

    /// <summary>Paints the footer and decides every button in it.</summary>
    private void RefreshRoomFooter()
    {
        var w = _lobbyWindow!;
        var host = RoomViewerIsHost();
        var me = RoomViewerId;
        var players = RoomPlayers().ToList();
        var format = CurrentRoomFormat();
        var teams = RoomShowsTeams();
        var result = _roomResult;

        var rival = players.Count == 2 && RoomPlayingSeats() == 2
            ? players.FirstOrDefault(m => !string.Equals(m.UserId, me, StringComparison.Ordinal))
            : null;
        var hostEntry = players.FirstOrDefault(m => string.Equals(m.UserId, _roomHostUserId, StringComparison.Ordinal));

        // The host of a casual room starts it, so nobody waits for them. In a competitive room the
        // match starts itself, and the host's Ready is one of the ones it needs.
        var counted = players.Where(m => _currentLobbyIsCompetitive
                                         || !string.Equals(m.UserId, _roomHostUserId, StringComparison.Ordinal));
        var facts = new RoomFacts
        {
            Stage = CurrentRoomStage(),
            Competitive = _currentLobbyIsCompetitive,
            HasTeams = teams,
            IsHost = host,
            Players = players.Count,
            Seats = RoomPlayingSeats(),
            NotReady = counted.Count(m => !m.Ready),
            IAmReady = me != null && _roomMembers.TryGetValue(me, out var mine) && mine.Ready,
            Gate = CurrentStartGate(),
            HostName = hostEntry != null ? MemberName(hostEntry) : "",
            RivalName = rival != null ? MemberName(rival) : null,
            RivalReady = rival?.Ready ?? false,
            HostLeftName = _roomHostLeftName,
            VpnMissing = _roomRadminMissing,
            RematchWaitingFor = _rematchInvited?.Login,
            OffersRematch = result != null && OffersRematch(result) && !InTournamentRoom,
            OffersFindAnother = result != null && OffersFindAnother(result) && !InTournamentRoom,
            UnratedReason = result != null ? UnratedSentence(result) : null,
            OnTeams = players.Count(m => m.Team is 1 or 2),
            Watching = RoomViewerWatches(),
        };
        var view = RoomFooterState.Evaluate(WithTournamentFacts(facts, result));
        _roomFooter = view;

        w.RoomFooterDot.Fill = Solid(view.Dot switch
        {
            RoomDot.Green => "#4FD68A",
            RoomDot.Orange => "#F4900C",
            RoomDot.Blue => "#55ACEE",
            RoomDot.Accent => AccentTextHex(),
            _ => "#61779A",
        });
        w.RoomFooterTitle.Text = Say(view.Title);
        w.RoomFooterTitle.Foreground = view.WarmTitle
            ? Solid("#F5BD76") : (Brush)Application.Current.FindResource("UiHeadText");
        w.RoomFooterSub.Text = view.Sub is { } sub ? Say(sub) : "";
        w.RoomFooterSub.Visibility = view.Sub == null ? Visibility.Collapsed : Visibility.Visible;
        w.InGameMatchTimer.Visibility = view.Clock != RoomClock.None || view.Title.Key == "RoomFtInGame"
            ? Visibility.Visible : Visibility.Collapsed;
        RefreshRoomClock(view.Clock);

        PaintFooterButtons(view, host);

        w.RoomFooter.Visibility = QuickSearchAlone ? Visibility.Collapsed : Visibility.Visible;
    }

    private void PaintFooterButtons(RoomFooterView view, bool host)
    {
        var w = _lobbyWindow!;
        Visibility Show(bool on) => on ? Visibility.Visible : Visibility.Collapsed;

        var inTournamentResult = view.Primary == RoomPrimary.ViewBracket;
        // The way out. During a match it is the red one, and which button carries it matters:
        // InGameCancelButton asks about the match, LeaveRoomButton about the room.
        var inGame = CurrentRoomStage() == RoomStage.InGame;
        w.InGameCancelButton.Visibility = Show(inGame);
        w.LeaveRoomButton.Visibility = Show(!inGame && view.Leave != RoomLeave.None);
        // While AoE3 is open the words follow the abort window, because that is what the button
        // does. With it closed the room is what is left, and leaving it ends the host's match.
        var matchWord = Strings.Get((inGame ? WithinAbortWindow : host) ? "RoomBtnCancelMatch" : "RoomBtnLeaveMatch");
        if (view.Leave == RoomLeave.Abandon) matchWord = Strings.Get("RtBtnAbandon");
        w.InGameCancelButton.Content = matchWord;
        w.LeaveRoomButton.Content = view.Leave switch
        {
            RoomLeave.Match or RoomLeave.Abandon => matchWord,
            RoomLeave.Rooms => Strings.Get("RoomBtnBackToRooms"),
            _ => Strings.Get("RoomBtnLeave"),
        };
        w.LeaveRoomButton.Tag = view.Leave is RoomLeave.Match or RoomLeave.Abandon ? "danger" : null;

        var primaryStyle = (Style)Application.Current.FindResource("RoomFooterPrimary");
        var secondStyle = (Style)Application.Current.FindResource("RoomFooterButton");

        // Start doubles as the countdown's Cancel, and as Reopen for the host of a live room.
        var start = view.Primary is RoomPrimary.Start or RoomPrimary.CancelCountdown
                    || (view.Primary == RoomPrimary.Reopen && host);
        w.StartButton.Visibility = Show(start);
        if (start)
        {
            var cancel = view.Primary == RoomPrimary.CancelCountdown;
            w.StartButton.Style = cancel ? secondStyle : primaryStyle;
            w.StartButton.Tag = cancel ? "danger" : null;
            w.StartButton.Content = Strings.Get(cancel ? "MpCountdownCancel"
                : view.Primary == RoomPrimary.Reopen ? "MpRoomRejoinGame" : "RoomBtnStart");
            w.StartButton.IsEnabled = view.PrimaryEnabled;
            w.StartButton.ToolTip = view.PrimaryTipKey == null ? null : TooltipHelper.Wrap(Strings.Get(view.PrimaryTipKey));
        }

        var ready = view.Primary == RoomPrimary.Ready;
        var unready = view.Secondary == RoomSecondary.Unready;
        w.ReadyButton.Visibility = Show(ready || unready);
        if (ready || unready)
        {
            w.ReadyButton.Style = ready ? primaryStyle : secondStyle;
            w.ReadyButton.Content = Strings.Get(ready ? "RoomBtnReady" : "RoomBtnUnready");
            w.ReadyButton.IsEnabled = _session?.IsInLobby == true || _demoRoomSample != null;
        }

        var rejoin = view.Primary == RoomPrimary.Reopen && !host;
        w.RejoinGameButton.Visibility = Show(rejoin);
        if (rejoin)
        {
            w.RejoinGameButton.Content = Strings.Get("MpRoomRejoinGame");
            w.RejoinGameButton.ToolTip = TooltipHelper.Wrap(Strings.Get("MpRoomRejoinTooltip"));
        }

        // One button for the two ends of a match: the next game, or the bracket it fed.
        w.RematchButton.Visibility = Show(view.Primary == RoomPrimary.Rematch || inTournamentResult);
        w.RematchButton.Content = Strings.Get(inTournamentResult ? "RtViewBracket" : "RoomBtnRematch");

        var second = view.Secondary is RoomSecondary.Assistant or RoomSecondary.FindAnother;
        w.RoomSecondaryButton.Visibility = Show(second);
        if (second)
        {
            var find = view.Secondary == RoomSecondary.FindAnother;
            w.RoomSecondaryButton.Content = Strings.Get(find ? "QmFindAnother" : "RoomBtnAssistant");
            w.RoomSecondaryButton.ToolTip = TooltipHelper.Wrap(Strings.Get(find ? "RoomTipFindAnother" : "RoomBtnAssistantTip"));
        }
    }

    /// <summary>The footer's second button.</summary>
    private void RoomSecondaryPressed()
    {
        switch (_roomFooter?.Secondary)
        {
            case RoomSecondary.Assistant:
                OpenRadminAssistant();
                break;
            case RoomSecondary.FindAnother:
                if (_demoRoomSample == null) _ = FindAnotherAfterResultAsync();
                break;
        }
    }

    /// <summary>"Don't show this again", on the reminder of a casual room.</summary>
    private void MuteRecordNotice()
    {
        Cfg().RoomRecordNoticeHidden = true;
        try { Cfg().Save(); }
        catch (Exception ex) { DiagnosticLog.Write($"Room: saving the record notice: {ex.Message}"); }
        RefreshRoomView();
    }

    private bool _roomCodeJustCopied;

    /// <summary>The code chip in the header: copies and says so on the chip for a moment.</summary>
    private void CopyRoomCodeFromChip()
    {
        var code = CurrentRoomCode();
        if (string.IsNullOrEmpty(code) || _lobbyWindow == null) return;
        try { Clipboard.SetText(code); }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Copying the room code failed: {ex.Message}");
            return;
        }
        _roomCodeJustCopied = true;
        _lobbyWindow.RoomCopyWord.Text = Strings.Get("RoomCopiedShort");
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.4) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _roomCodeJustCopied = false;
            if (_lobbyWindow != null) _lobbyWindow.RoomCopyWord.Text = Strings.Get("RoomCopy");
        };
        timer.Start();
    }

    private static SolidColorBrush Solid(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    /// <summary>PARA QUE CUENTE. The link sits at the end of the sentence, so it is an inline.</summary>
    private void SetCountsText(LobbyWindow w, string key)
    {
        if (ReferenceEquals(w.RoomCountsText.Tag, key)) return;
        w.RoomCountsText.Tag = key;
        var where = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(Strings.Get("RoomRecordWhere")))
        {
            TextDecorations = null,
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        where.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "ModAccentText");
        where.Click += (_, _) => w.OnRecordHelp?.Invoke();
        w.RoomCountsText.Inlines.Clear();
        w.RoomCountsText.Inlines.Add(new System.Windows.Documents.Run(Strings.Get(key) + " "));
        w.RoomCountsText.Inlines.Add(where);
    }

    /// <summary>The static words of the panels; the rest of the window is in the view above.</summary>
    private void ApplyRoomViewLabels()
    {
        var w = _lobbyWindow!;
        w.RoomResultLabel.Text = Strings.Get("RoomPanelResult");
        w.InGameSoloTitle.Text = Strings.Get("RoomSoloTitle");
        w.InGameSoloBody.Text = Strings.Get("RoomSoloBody");
        w.InGameSoloCopyButton.Content = Strings.Get("MpInGameSoloCopy");
        w.InGameSoloAnnounceButton.Content = Strings.Get("RoomAnnounce");
        w.PreflightHeader.Text = Strings.Get("MpPreflightHeader");
        w.PreflightRecordText.Text = Strings.Get("RoomRecordNotice");
        w.PreflightRecordHelp.Content = Strings.Get("RoomRecordWhere");
        w.RoomRecordMute.Content = Strings.Get("RoomRecordMute");
        w.RoomCountsLabel.Text = Strings.Get("RoomCountsTitle");
        SetCountsText(w, "RoomCountsBody");
        w.PreflightAbandonText.Text = Strings.Get("MpPreflightAbandon");
        w.PreflightReplayText.Text = Strings.Get("MpPreflightReplay");
        w.PreflightReplayText.ToolTip = TooltipHelper.Wrap(Strings.Get("MpPreflightReplayTip"));
        w.RoomStakeLabel.Text = Strings.Get("RoomStakeTitle");
        w.RoomStakeText.Text = Strings.Get("RoomStakeBody");
        w.ChatHeaderText.Text = Strings.Get("RoomChatTitle");
        w.ClearChatButton.Content = Strings.Get("RoomChatClear");
    }

    /// <summary>A rematch needs a 1v1 with a rival somebody can invite.</summary>
    internal static bool OffersRematch(MatchOutcomeView model)
        => RematchPlan.Offered(OffersFindAnother(model), model.RivalLogin, model.Verdict);
}
