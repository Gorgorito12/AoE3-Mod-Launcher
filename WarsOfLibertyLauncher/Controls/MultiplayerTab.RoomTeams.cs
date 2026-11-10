using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// Teams chosen in the room (design handoff 55h). In a room of 4 or 6 seats each player picks
/// Team 1 or Team 2 — the host may move anybody — and the roster becomes two columns with the
/// players who have not picked in a NO TEAM box under them. A competitive room also shows the
/// SERVER's odds, the warning that the game's teams must match, and why Start is still locked.
///
/// <para>The server decides every part of it: who may move whom (<c>forbidden</c>,
/// <c>team_full</c>), when a competitive room may start (<c>start_*</c>, in the same order as
/// <see cref="StartGate"/>), and the odds (Glicko). The launcher only draws them and sends
/// <c>set_team</c> / <c>move_player</c>.</para>
/// </summary>
public partial class MultiplayerTab
{
    /// <summary>The <c>Tag</c>s of the team panel's parts, so the tests find them.</summary>
    internal const string TeamColumnTag = "RoomTeamColumn";
    internal const string NoTeamBoxTag = "RoomNoTeamBox";
    internal const string TeamOddsTag = "RoomTeamOdds";
    internal const string SidesLockedTag = "RoomSidesLocked";

    /// <summary>
    /// The playing seats, or 0 when the capacity is not known yet. The observers' seats come off,
    /// as on the server (<c>playingSeatsOf</c>): a room of four with two watching seats is a 1v1, and
    /// counting them made the roster show a free seat nobody could play in.
    /// </summary>
    private int RoomPlayingSeats()
        => TryGetCurrentLobbyMaxPlayers(out var max) ? Math.Max(0, max - Math.Max(0, _currentLobbySpectatorSlots)) : 0;

    /// <summary>Whether the viewer is in the room to watch, not to play.</summary>
    private bool RoomViewerWatches()
        => RoomViewerId is { } me && _roomMembers.TryGetValue(me, out var m)
           && string.Equals(m.Role, "spectator", StringComparison.Ordinal);

    /// <summary>Whether this room has teams: 4 or 6 seats, casual or competitive.</summary>
    internal bool RoomHasTeams() => StartGate.HasTeams(RoomPlayingSeats());

    /// <summary>Whether the roster is drawn by teams. Only a competitive room: there the sides decide
    /// the result, while a casual room of four is four people (design 87a).</summary>
    internal bool RoomShowsTeams() => RoomHasTeams() && _currentLobbyIsCompetitive && _roomTournament == null;

    /// <summary>The room's players, spectators left out — a spectator has no team and no seat.</summary>
    private IEnumerable<RoomMemberEntry> RoomPlayers()
        => _roomMembers.Values.Where(m => !string.Equals(m.Role, "spectator", StringComparison.Ordinal));

    private static string MemberName(RoomMemberEntry m)
        => string.IsNullOrEmpty(m.Login) ? m.UserId : m.Login;

    /// <summary>
    /// Why a competitive room may not start yet, in the server's order. Casual rooms never block.
    /// The server checks again when Start is pressed; this is the explanation, not the guard.
    /// </summary>
    internal StartGate.Verdict CurrentStartGate()
        => StartGate.Evaluate(
            _currentLobbyIsCompetitive,
            RoomPlayingSeats(),
            RoomPlayers().Select(m => new StartGate.Player(m.UserId, MemberName(m), m.Team)).ToList());

    /// <summary>Whether the host's Start may be pressed: the gate is open, or the match already
    /// started and Start reads "reopen the game".</summary>
    private bool StartGateAllows() => _roomMatchLive || CurrentStartGate().CanStart;

    /// <summary>The sentence under the teams: why Start is locked, or — in a team room — that
    /// everything is ready. Null when there is nothing to say.</summary>
    internal string? StartGateText(StartGate.Verdict verdict)
    {
        // Searching alone, the waiting box already says what is missing, and why it is fine.
        if (QuickSearchAlone) return null;
        string Blocked(string detail) => Strings.Get("MpStartBlockedPrefix") + " " + detail;
        return verdict.Reason switch
        {
            StartGate.Reason.MissingPlayers => Blocked(Strings.Format("MpStartBlockedPlayers", verdict.Have, verdict.Need)),
            StartGate.Reason.PlayerWithoutTeam => Blocked(Strings.Format(
                verdict.WithoutTeam!.Count == 1 ? "MpStartBlockedNoTeam" : "MpStartBlockedNoTeamPl",
                NameList.Join(verdict.WithoutTeam!))),
            StartGate.Reason.UnevenTeams => Blocked(Strings.Format("MpStartBlockedUneven", verdict.Team1, verdict.Team2)),
            _ => RoomHasTeams() && _currentLobbyIsCompetitive ? Strings.Get("MpStartReady") : null,
        };
    }

    // ------------------------------------------------------------------ the panel

    /// <summary>
    /// The players of a team room (55h): Team 1 and Team 2 side by side, NO TEAM under them while
    /// anybody has not picked, then — competitive only — the odds card, the warning and the
    /// sentence saying why Start is locked. Spectators keep their ordinary rows after it all.
    /// </summary>
    private void RenderTeamPanel(Panel host)
    {
        var players = RoomPlayers().ToList();
        var seats = StartGate.TeamSize(RoomPlayingSeats());
        host.Children.Add(BuildTeamColumn(1, players.Where(p => p.Team == 1).ToList(), seats));
        host.Children.Add(BuildTeamColumn(2, players.Where(p => p.Team == 2).ToList(), seats));

        var without = players.Where(p => p.Team is not (1 or 2)).ToList();
        if (without.Count > 0) host.Children.Add(BuildNoTeamBox(without));

        AddObserverGroup(host);

        // A tournament room: each team IS an entrant of the bracket, seated by the server.
        if (_roomTeamsLocked)
        {
            host.Children.Add(new TextBlock
            {
                Text = Strings.Get("MpTeamSidesLockedNote"),
                Tag = SidesLockedTag,
                Margin = new Thickness(0, 8, 0, 0),
                FontSize = (double)Application.Current.FindResource("MpMetaSize"),
                Foreground = (Brush)Application.Current.FindResource("MpRankMutedText"),
                TextWrapping = TextWrapping.Wrap,
            });
        }

        // Once both sides are whole: before that there is no match to give odds on.
        if (_currentLobbyIsCompetitive && players.Count(p => p.Team is 1 or 2) >= seats * 2)
        {
            host.Children.Add(BuildTeamOddsCard());
            host.Children.Add(BuildTeamsMismatchWarning());
        }
    }

    /// <summary>One team: its name and average ELO, "your team" or the way in, the players, and a
    /// row for each seat still free.</summary>
    private FrameworkElement BuildTeamColumn(int team, IReadOnlyList<RoomMemberEntry> members, int seats)
    {
        var stack = new StackPanel();
        var average = TeamAverage(members);
        // No average while a teammate's rating is withheld from this viewer: beside his partner's
        // visible number, the average gives the hidden one straight back.
        var averageHidden = members.Any(TeamRatingHiddenFromViewer);
        var figure = !averageHidden && average is int a ? Strings.Format("RoomTeamAvg", a) : null;

        FrameworkElement? right = null;
        var mine = members.Any(m => IsRoomViewer(m.UserId));
        var phaseOpen = _matchPhase == MatchPhase.Lobby && !_roomTeamsLocked;
        if (mine)
        {
            right = new TextBlock
            {
                Text = Strings.Get("RoomTeamMine"),
                FontSize = (double)Application.Current.FindResource("SetControlSize"),
                FontWeight = FontWeights.SemiBold,
                Foreground = Solid("#9FD0F5"),
                VerticalAlignment = VerticalAlignment.Center,
            };
        }
        else if (phaseOpen && RoomViewerId is { } viewer && _roomMembers.ContainsKey(viewer) && members.Count < Math.Max(1, seats))
        {
            var join = new Button
            {
                Content = Strings.Get("RoomTeamJoin"),
                Style = (Style)Application.Current.FindResource("RoomSmallButton"),
                Height = 28,
                FontSize = (double)Application.Current.FindResource("SetControlSize"),
                ToolTip = TooltipHelper.Wrap(Strings.Format("RoomTeamJoinTip", team)),
            };
            join.Click += (_, _) => _ = PickTeamAsync(viewer, team);
            right = join;
        }
        stack.Children.Add(BuildRosterGroupHead(Strings.Format("RoomTeamName", team), figure,
            averageHidden ? Strings.Get("MpTeamAvgHiddenPlacementTip") : null, right));

        foreach (var m in members) stack.Children.Add(BuildTeamRow(m, inline: false));
        for (var free = members.Count; free < seats; free++)
            stack.Children.Add(RosterRow(BuildEmptySeatDisc(), null,
                RosterTwoLines(Strings.Get("RoomFreeSeat"), "#A8BCD2", "", Strings.Get("RoomFreeTeam")),
                _roomTeamsLocked ? null : BuildCopyCodeButton()));

        return new Border { Child = stack, Tag = TeamColumnTag };
    }

    /// <summary>The players who have not picked a team (55h).</summary>
    private FrameworkElement BuildNoTeamBox(IReadOnlyList<RoomMemberEntry> members)
    {
        var stack = new StackPanel();
        stack.Children.Add(BuildRosterGroupHead(Strings.Get("RoomTeamNone"), null, null, null));
        foreach (var m in members) stack.Children.Add(BuildTeamRow(m, inline: true));
        return new Border { Child = stack, Tag = NoTeamBoxTag };
    }

    /// <summary>
    /// One player in a team room: the row of the roster with the team rating under the name. Your
    /// own team is chosen from the head of the group; the host keeps the 1 | 2 picker on the others,
    /// because moving somebody has no other door.
    /// </summary>
    private FrameworkElement BuildTeamRow(RoomMemberEntry m, bool inline)
    {
        var name = MemberName(m);
        var isMe = IsRoomViewer(m.UserId);
        var isHost = string.Equals(m.UserId, _roomHostUserId, StringComparison.Ordinal);
        var stack = RosterTwoLines(name, "#F0F5FB", MemberTags(isMe, isHost), "");
        stack.Children.Add(TeamRowDetail(m));

        var tail = new StackPanel { Orientation = Orientation.Horizontal };
        tail.Children.Add(MemberState(m, isMe, out var dim));
        if (IsRoomViewerHost() && !isMe && !_roomTeamsLocked && _matchPhase == MatchPhase.Lobby)
        {
            var picker = BuildTeamPicker(m);
            picker.Margin = new Thickness(10, 0, 0, 0);
            tail.Children.Add(picker);
        }
        if (BuildKickButton(m, isMe, isHost) is { } kick) tail.Children.Add(kick);

        var row = RosterRow(BuildAvatarDisc(name, m.AvatarUrl, 32), null, stack, tail, tag: m.UserId);
        if (dim) row.Opacity = .6;
        return row;
    }

    /// <summary>"1534? · you · host" in monospace: the team rating with its placement "?", then
    /// who this is. Nothing for a rating nobody sent — never a 1500 nobody earned — and, for somebody
    /// else still being placed on the team ladder, "In placement 2/5" where his number would be.</summary>
    private TextBlock TeamRowDetail(RoomMemberEntry m)
    {
        var tb = new TextBlock
        {
            FontSize = (double)Application.Current.FindResource("SetControlSize"),
            Foreground = Solid("#A8BCD2"),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 0),
        };
        var (rating, look, games, required) = TeamRatingOf(m);
        var lead = false;
        if (look == RatingDisplay.Look.HiddenPlacement)
        {
            tb.Inlines.Add(new Run(RatingDisplay.PlacementLabel(games, required, shortForm: false)));
            tb.Tag = HiddenPlacementTag;
            tb.ToolTip = TooltipHelper.Wrap(HiddenPlacementTip(games, required));
            lead = true;
        }
        else if (rating is double r)
        {
            tb.Inlines.Add(new Run(PlacementView.RatingText(r))
            {
                FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"),
                Foreground = Solid("#C3D2E5"),
            });
            if (look == RatingDisplay.Look.Provisional)
                tb.Inlines.Add(new Run("?") { Foreground = (Brush)Application.Current.FindResource("MpCaution") });
            lead = true;
        }
        if (lead && tb.Tag == null) tb.Inlines.Add(new Run(" ELO"));
        return tb;
    }

    /// <summary>
    /// The team rating a row shows and how it reads for this viewer (<see cref="RatingDisplay.LookFor"/>),
    /// with the counts the "In placement 2/5" label needs. The rating is null whenever there is no
    /// number to draw — including somebody else still being placed, whose number is not drawn even
    /// if an older server sent it.
    /// </summary>
    private (double? Rating, RatingDisplay.Look Look, int? Games, int Required) TeamRatingOf(RoomMemberEntry m)
    {
        double? rating = m.RatingTeam;
        double? rd = m.RdTeam;
        int? games = m.GamesPlayedTeam;
        var isViewer = IsRoomViewer(m.UserId);
        if (rating == null && isViewer && (_demoRoomStanding ?? _cachedStanding) is { } mine)
        {
            rating = mine.RatingTeam;
            rd ??= mine.RdTeam;
            games ??= mine.GamesPlayedTeam;
        }
        var required = PlacementRequiredShown(team: true);
        var look = RatingLookFor(isViewer, team: true, rd, games, m.InPlacementTeam);
        if (look == RatingDisplay.Look.HiddenPlacement) return (null, look, games, required);
        return RatingDisplay.ShouldShow(rating) ? (rating, look, games, required) : (null, look, games, required);
    }

    /// <summary>The mean of the shown team ratings, rounded; null when nobody's is known. The caller
    /// hides it while a teammate's rating is withheld from the viewer (see BuildTeamColumn).</summary>
    private int? TeamAverage(IReadOnlyList<RoomMemberEntry> members)
    {
        var known = members.Select(m => TeamRatingOf(m).Rating).Where(r => r.HasValue).Select(r => r!.Value).ToList();
        return known.Count == 0 ? null : (int)Math.Round(known.Average());
    }

    private bool IsRoomViewer(string userId)
        => RoomViewerId is { } me && string.Equals(userId, me, StringComparison.Ordinal);

    /// <summary>The 1 | 2 picker of one row.</summary>
    private FrameworkElement BuildTeamPicker(RoomMemberEntry m)
    {
        // Never in a tournament room: the server seated everybody on his entrant's side.
        var mayChange = _matchPhase == MatchPhase.Lobby
                        && !_roomTeamsLocked
                        && (IsRoomViewer(m.UserId) || IsRoomViewerHost());
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var team in new[] { 1, 2 })
        {
            var b = new Button
            {
                Content = team.ToString(),
                Style = (Style)Application.Current.FindResource("MpTeamPick"),
                Tag = m.Team == team ? (team == 1 ? "t1" : "t2") : null,
                IsEnabled = mayChange,
                Margin = new Thickness(team == 2 ? 2 : 0, 0, 0, 0),
                ToolTip = TooltipHelper.Wrap(_roomTeamsLocked
                    ? Strings.Get("MpTeamPickLockedTip")
                    : Strings.Format("MpTeamPickTip", team)),
            };
            var target = m.UserId;
            var chosen = team;
            b.Click += (_, _) => _ = PickTeamAsync(target, chosen);
            row.Children.Add(b);
        }
        return new Border
        {
            Child = row,
            Tag = "TeamPicker",
            Padding = new Thickness(2),
            CornerRadius = new CornerRadius(7),
            Background = (Brush)Application.Current.FindResource("MpGuideSegmentTray"),
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private bool IsRoomViewerHost()
        => RoomViewerId is { } me && string.Equals(me, _roomHostUserId, StringComparison.Ordinal);

    /// <summary>
    /// Asks the server to put a player on a team: <c>set_team</c> for yourself, <c>move_player</c>
    /// for somebody else (host only). The roster changes when the server's <c>member_team</c>
    /// arrives, never before — a refused move must not leave the room showing a team nobody has.
    /// A sample room has no server, so there the change is drawn at once.
    /// </summary>
    internal async System.Threading.Tasks.Task PickTeamAsync(string userId, int team)
    {
        if (!_roomMembers.TryGetValue(userId, out var member) || member.Team == team) return;

        if (_demoRoomWindow != null && ReferenceEquals(_lobbyWindow, _demoRoomWindow))
        {
            member.Team = team;
            RenderRoomMembers();
            return;
        }

        var socket = _session?.RoomSocket;
        if (socket == null) return;
        try
        {
            if (IsRoomViewer(userId)) await socket.SendSetTeamAsync(team);
            else await socket.SendMovePlayerAsync(userId, team);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"MultiplayerTab.PickTeam: {ex.Message}");
        }
    }

    /// <summary>The server moved a player (<c>member_team</c>); everybody, the mover included,
    /// hears it.</summary>
    private void HandleMemberTeam(JsonElement json)
    {
        if (!json.TryGetProperty("user_id", out var u) || u.GetString() is not { Length: > 0 } userId) return;
        int? team = json.TryGetProperty("team", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt32() : null;
        if (_roomMembers.TryGetValue(userId, out var entry)) entry.Team = team is 1 or 2 ? team : null;
        RenderRoomMembers();
        RenderStartButtonGate();
    }

    /// <summary>
    /// A refusal of a team change or of Start, in words. Returns false when the code is not one of
    /// these. A <c>start_*</c> refusal also stops the local 2-second fallback countdown that
    /// <see cref="BeginHostStart"/> arms — without that the match would start anyway.
    /// </summary>
    private bool HandleTeamOrStartError(string code, JsonElement json)
    {
        switch (code)
        {
            case "team_full":
                AppendChatSystem(Strings.Get("MpTeamErrTeamFull"));
                return true;
            case "forbidden":
                AppendChatSystem(Strings.Get("MpTeamErrForbidden"));
                return true;
            case "game_in_progress":
                AppendChatSystem(Strings.Get("MpTeamErrInGame"));
                return true;
            case "tournament_side_locked":
                AppendChatSystem(Strings.Get("MpTeamErrSideLocked"));
                return true;
            case "start_missing_players":
            case "start_player_without_team":
            case "start_uneven_teams":
                _startRefused = true;
                _autoStartInFlight = false;
                AppendChatSystem(StartRefusalText(code, json) ?? Strings.Get("MpStartBlockedPrefix"));
                RenderStartButtonGate();
                return true;
            default:
                return false;
        }
    }

    /// <summary>The server's start refusal, worded from ITS details (names looked up in the
    /// roster), so the chat says exactly what the server judged.</summary>
    private string? StartRefusalText(string code, JsonElement json)
    {
        var d = json.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Object
            ? details : default;
        int Num(string name) => d.ValueKind == JsonValueKind.Object && d.TryGetProperty(name, out var v)
                                 && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
        var verdict = code switch
        {
            "start_missing_players" => new StartGate.Verdict(StartGate.Reason.MissingPlayers, Have: Num("have"), Need: Num("need")),
            "start_uneven_teams" => new StartGate.Verdict(StartGate.Reason.UnevenTeams, Team1: Num("team1"), Team2: Num("team2")),
            _ => new StartGate.Verdict(StartGate.Reason.PlayerWithoutTeam, WithoutTeam: NamesOf(d)),
        };
        return StartGateText(verdict);

        List<string> NamesOf(JsonElement e)
        {
            var names = new List<string>();
            if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("user_ids", out var ids) && ids.ValueKind == JsonValueKind.Array)
                foreach (var id in ids.EnumerateArray())
                    if (id.GetString() is { } uid)
                        names.Add(_roomMembers.TryGetValue(uid, out var m) ? MemberName(m) : uid);
            return names;
        }
    }

    /// <summary>A Start the server refused; the fallback countdown checks it.</summary>
    private bool _startRefused;

    /// <summary>The host's Start: enabled only while the gate is open (Lobby phase only — the
    /// countdown owns the button as Cancel).</summary>
    private void RenderStartButtonGate()
    {
        if (_lobbyWindow == null || _matchPhase != MatchPhase.Lobby) return;
        RefreshRoomView();
    }

    // ------------------------------------------------------------------ the countdown (55i)

    /// <summary>The line-ups of the countdown: user id → team, as the server froze them at Start.</summary>
    private Dictionary<string, int>? _countdownTeams;

    /// <summary>The rating preview's countdown card: drawn as if counting, with no timer behind
    /// it — a real countdown ends by launching the game.</summary>
    private bool _previewCountdown;

    /// <summary>The rating preview's result cards (55j) are on screen in a sample room.</summary>
    private bool _previewResult;

    /// <summary>
    /// The <c>teams</c> of a <c>game_countdown</c> frame: <c>{"user id": 1 | 2}</c>, or null when
    /// the frame has none (a 1v1, a room whose teams were not chosen here, an older server).
    /// </summary>
    internal static Dictionary<string, int>? ParseCountdownTeams(JsonElement frame)
    {
        if (!frame.TryGetProperty("teams", out var teams) || teams.ValueKind != JsonValueKind.Object) return null;
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var p in teams.EnumerateObject())
            if (p.Value.ValueKind == JsonValueKind.Number && p.Value.GetInt32() is 1 or 2)
                map[p.Name] = p.Value.GetInt32();
        return map.Count > 0 ? map : null;
    }

    /// <summary>The two sides' names for the countdown card, in roster order; null unless both
    /// sides have somebody — a card naming one team would be no reminder at all.</summary>
    internal (IReadOnlyList<string> Team1, IReadOnlyList<string> Team2)? TeamCountdownSides()
    {
        if (_countdownTeams == null) return null;
        string NameOf(string id) => _roomMembers.TryGetValue(id, out var m) ? MemberName(m) : id;
        var ordered = _countdownTeams.Keys
            .OrderBy(id => _roomMembers.Keys.ToList().IndexOf(id) is var i && i >= 0 ? i : int.MaxValue)
            .ToList();
        var one = ordered.Where(id => _countdownTeams[id] == 1).Select(NameOf).ToList();
        var two = ordered.Where(id => _countdownTeams[id] == 2).Select(NameOf).ToList();
        return one.Count > 0 && two.Count > 0 ? (one, two) : null;
    }

    /// <summary>
    /// The card's sentence — "<b>Team 1:</b> Ana and Luis · <b>Team 2:</b> Pedro and Sara. Pick
    /// this in the game." — wrapping rather than trimming, so a 3v3 of long names stays whole —
    /// and, in a competitive room, the line saying a mismatch does not count.
    /// </summary>
    private void RenderTeamCountdown()
    {
        if (_lobbyWindow == null || TeamCountdownSides() is not { } sides) return;
        var teams = _lobbyWindow.TeamCountdownTeams;
        teams.Inlines.Clear();
        AddTemplateRuns(teams, Strings.Get("MpCountdownTeams"), i => i switch
        {
            0 => new Run(Strings.Get("MpCountdownTeam1"))
            {
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)Application.Current.FindResource("MpActionText"),
            },
            2 => new Run(Strings.Get("MpCountdownTeam2"))
            {
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)Application.Current.FindResource("MpTeam2Text"),
            },
            1 => new Run(NameList.Join(sides.Team1)),
            _ => new Run(NameList.Join(sides.Team2)),
        });
        _lobbyWindow.TeamCountdownFoot.Text = _currentLobbyIsCompetitive ? Strings.Get("MpCountdownFoot") : "";
    }

    // ------------------------------------------------------------------ odds and warning

    /// <summary>
    /// PROBABILITY (55h): the SERVER's chance of each team — never the launcher's own sum — as
    /// "Team 1: 58 % · Team 2: 42 %" and a two-coloured bar. Until both teams have somebody the
    /// figure is a dash and the note says when it will appear.
    /// </summary>
    internal FrameworkElement BuildTeamOddsCard()
    {
        var odds = WinOddsView.ForTeams(_roomOdds);
        // Withheld while anybody in the room is still being placed on the team ladder: the server
        // says so, or — against a server that still sends them — this launcher sees it, and then the
        // figures that arrived are not drawn. A dash, an empty bar and the sentence that says why.
        var hiddenForPlacement = WinOddsView.HiddenByPlacement(_roomOdds, odds != null && AnyRoomPlayerPlacing(team: true));
        if (hiddenForPlacement) odds = null;
        var stack = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.Children.Add(new TextBlock
        {
            Text = Strings.Get("MpWinProbLabel"),
            FontSize = (double)Application.Current.FindResource("MpSectionLabelSize"),
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.FindResource("MpTextLabel"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        var figure = new TextBlock
        {
            Text = odds is { } o ? Strings.Format("MpWinProbTeams", o.Team1, o.Team2) : Strings.Get("MpDash"),
            HorizontalAlignment = HorizontalAlignment.Right,
            FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"),
            FontSize = (double)Application.Current.FindResource("MpProfileH2HSize"),
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.FindResource("MpTextPrimary"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(figure, 1);
        head.Children.Add(figure);
        stack.Children.Add(head);

        var split = new Grid();
        var share1 = odds is { } s ? Math.Clamp(s.Team1 / 100.0, 0, 1) : 0;
        var share2 = odds is { } s2 ? Math.Clamp(s2.Team2 / 100.0, 0, 1) : 0;
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(share1, GridUnitType.Star) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(share2, GridUnitType.Star) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(odds == null ? 1 : 0, GridUnitType.Star) });
        split.Children.Add(new Border { Background = (Brush)Application.Current.FindResource("MpAction") });
        var second = new Border { Background = (Brush)Application.Current.FindResource("MpTeam2") };
        Grid.SetColumn(second, 1);
        split.Children.Add(second);
        // A Border with a radius clips its child, which is what rounds the two ends.
        stack.Children.Add(new Border
        {
            Height = 6,
            Margin = new Thickness(0, 9, 0, 0),
            CornerRadius = new CornerRadius(3),
            Background = (Brush)Application.Current.FindResource("MpSegPendingProfile"),
            Child = split,
        });
        stack.Children.Add(new TextBlock
        {
            Text = Strings.Get(hiddenForPlacement ? "MpOddsHiddenPlacement"
                : odds == null ? "MpWinProbTeamsEmpty" : "MpWinProbTeamsNote"),
            Tag = hiddenForPlacement ? OddsHiddenPlacementTag : null,
            Margin = new Thickness(0, 7, 0, 0),
            FontSize = (double)Application.Current.FindResource("MpRankSmallSize"),
            Foreground = (Brush)Application.Current.FindResource("MpTextFade"),
            TextWrapping = TextWrapping.Wrap,
        });

        return new Border
        {
            Child = stack,
            Tag = TeamOddsTag,
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(10),
            Background = Solid("#0DFFFFFF"),
            BorderBrush = Solid("#14FFFFFF"),
            BorderThickness = new Thickness(1),
        };
    }

    /// <summary>The amber line of a competitive team room (55h): the game's teams must be the
    /// room's, or the match does not count (<c>teams_mismatch</c>).</summary>
    private static FrameworkElement BuildTeamsMismatchWarning()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock
        {
            Text = "i",
            Margin = new Thickness(0, 0, 10, 0),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.FindResource("MpCaution"),
        });
        var text = new TextBlock
        {
            Text = Strings.Get("MpTeamsMismatchWarn"),
            FontSize = (double)Application.Current.FindResource("MpBodySize"),
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.FindResource("MpCautionTextAlt"),
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return new Border
        {
            Child = grid,
            Tag = "RoomTeamsWarning",
            Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(14, 10, 14, 10),
            CornerRadius = (CornerRadius)Application.Current.FindResource("RadiusPanel"),
            Background = (Brush)Application.Current.FindResource("MpCautionBg"),
            BorderBrush = (Brush)Application.Current.FindResource("MpInactiveNoticeRim"),
            BorderThickness = new Thickness(1),
        };
    }
}
