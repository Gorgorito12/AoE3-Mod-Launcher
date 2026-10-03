using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// The INTERACTIVE tournament preview: the seven fabricated tournaments, played.
///
/// <para>It works by swapping the SERVER, not the buttons. Every tournament call the tab makes goes
/// through <see cref="TournamentApi"/>, which is the real client normally and
/// <see cref="TournamentSimulator"/> under <c>--demo-tournaments</c> — so in the preview the
/// confirmations, the error notices, the refresh after each action and the new-tournament dialog
/// are the production code, and what gets judged is the feature rather than a drawing of it.
/// Nothing is sent anywhere; the tournaments live in memory until "Reset the samples" or until the
/// launcher closes.</para>
///
/// <para>What the preview ADDS is only what a real server gets from other people: the amber strip
/// on top (who to look as, and "play the round" / "play to the end" / sign-ups for everybody
/// else), and an amber strip over the selected match (who won it, or the other side opening its
/// room). Both say they are the preview's, in the caution colours the preview has always used, so
/// neither can be mistaken for the interface being judged — and neither lives inside the bracket,
/// where <c>DialogXamlTests</c> forbids a button and the ⋯ menu is looked for.</para>
///
/// <para>"Play my match" opens a SAMPLE ROOM WINDOW (<see cref="ShowSampleRoom"/>):
/// the real room window, its buttons disconnected, holding the room the server would have made.
/// Never the real join, never the countdown — that path launches the game.</para>
/// </summary>
public partial class MultiplayerTab
{
    /// <summary>The preview's server, once the preview has been opened.</summary>
    private TournamentSimulator? _tournamentPreview;

    private PreviewTournamentApi? _tournamentPreviewApi;
    private LiveTournamentApi? _liveTournamentApi;

    /// <summary>The tournament the "view as" choice was made on. Selecting another one returns to
    /// that tournament's own viewer — the choice was about the bracket being looked at.</summary>
    private string? _previewChoiceFor;

    /// <summary>The bracket match whose sample room is open, so the window closes when the match
    /// stops having a room — the way a reported result closes the real one.</summary>
    private (string TournamentId, string MatchId)? _previewRoomFor;

    /// <summary>The room window when it is showing a SAMPLE room, from any preview.</summary>
    private LobbyWindow? _demoRoomWindow;

    /// <summary>Set while a sample room is opening, so <see cref="OpenLobbyWindow"/> can tell a
    /// sample opening from a real one.</summary>
    private bool _openingDemoRoom;

    /// <summary>Every preview control carries an automation id starting with this, so tests can
    /// find them and assert none of them strayed into the bracket.</summary>
    internal const string TournamentPreviewIdPrefix = "TournamentPreview:";

    /// <summary>The preview's server, created on first use. Kept for the session, so coming back
    /// to the preview finds the brackets where they were left.</summary>
    internal TournamentSimulator TournamentPreview => _tournamentPreview ??= new TournamentSimulator();

    /// <summary>
    /// The tournament server in use: the preview's while it is open, the session's otherwise, or
    /// null when there is neither.
    /// </summary>
    private ITournamentApi? TournamentApiOrNull
    {
        get
        {
            if (_demoTournaments)
            {
                return _tournamentPreviewApi ??= new PreviewTournamentApi(
                    TournamentPreview,
                    () => _selectedTournamentId,
                    // Through the REAL frame handler, so the toast is the real one.
                    notice => HandleTournamentUpdateFrame(JsonSerializer.Serialize(notice)));
            }

            var client = _session?.Api;
            if (client == null) return null;
            if (!ReferenceEquals(_liveTournamentApi?.Client, client))
                _liveTournamentApi = new LiveTournamentApi(client);
            return _liveTournamentApi;
        }
    }

    /// <summary>The server every tournament action talks to. Only reached from a click, where
    /// there always is one.</summary>
    private ITournamentApi TournamentApi => TournamentApiOrNull!;

    /// <summary>
    /// Who the tournament is being looked at AS: whoever is signed in, or in the preview whoever
    /// "view as" says — one person at a time, for the list and the bracket alike.
    /// </summary>
    private string? TournamentViewerId(TournamentSummary? context)
        => _demoTournaments
            ? _tournamentPreview?.ViewerOf(context?.Id) ?? TournamentDemoData.MeUserId
            : _session?.CurrentUser?.Id;

    /// <summary>
    /// Re-read the preview's store into the fields the renderer draws from. Runs at the top of
    /// every render in the preview, so the list and the open tournament always come from one
    /// state, in the current language.
    /// </summary>
    private void SyncTournamentPreview()
    {
        var sim = _tournamentPreview;
        if (!_demoTournaments || sim == null) return;

        if (!string.Equals(_previewChoiceFor, _selectedTournamentId, StringComparison.Ordinal))
        {
            sim.ViewerChoice = null;
            _previewChoiceFor = _selectedTournamentId;
        }
        if (_selectedTournamentId != null && !sim.Contains(_selectedTournamentId))
        {
            // A created tournament, gone after a reset.
            _selectedTournamentId = null;
        }

        _tournaments = sim.List(_selectedTournamentId);
        _tournamentDetail = sim.Detail(_selectedTournamentId);
        _tournamentsUnavailable = false;
        _tournamentsFetchedUtc = DateTime.UtcNow;
        CloseStalePreviewRoom();
    }

    /// <summary>
    /// The match worth having selected when a tournament opens in the preview: the person
    /// looking's own match when there is one to act on, else one being played, else the first that
    /// can be played. Null when the bracket has none of those.
    /// </summary>
    private string? PreviewTieOf(TournamentDetail? t)
    {
        var matches = t?.Matches;
        if (t == null || matches == null || matches.Count == 0) return null;
        var me = TournamentViewerId(t);

        var mine = matches
            .Where(m => Actionable(MatchCards.For(m, me, t.Entrants)))
            .OrderBy(m => m.Round).ThenBy(m => m.Position)
            .FirstOrDefault();
        if (mine != null) return mine.Id;

        var live = matches.FirstOrDefault(m => m.Lobby != null && m.Status == "pending");
        if (live != null) return live.Id;

        return matches
            .Where(TournamentRules.Playable)
            .OrderBy(m => m.Round).ThenBy(m => m.Position)
            .FirstOrDefault()?.Id;
    }

    // ---------------------------------------------------------------- the strips

    /// <summary>
    /// The amber strip at the top of the preview: what this is, who to look as, and what the rest
    /// of the tournament's people do.
    /// </summary>
    private UIElement BuildTournamentPreviewBanner(TournamentDetail t)
    {
        var box = new Border
        {
            Padding = new Thickness(12, 10, 12, 8),
            Margin = new Thickness(0, 0, 0, 13),
            BorderThickness = new Thickness(1),
        };
        AutomationProperties.SetAutomationId(box, TournamentPreviewIdPrefix + "Banner");
        box.SetResourceReference(Border.CornerRadiusProperty, "RadiusControl");
        box.SetResourceReference(Border.BackgroundProperty, "MpCautionBg");
        box.SetResourceReference(Border.BorderBrushProperty, "MpCautionRim");

        var stack = new StackPanel();
        var text = new TextBlock
        {
            Text = Strings.Get("MpTournamentDemoBanner"),
            TextWrapping = TextWrapping.Wrap,
        };
        text.SetResourceReference(TextBlock.FontSizeProperty, "MpLabelSize");
        text.SetResourceReference(TextBlock.ForegroundProperty, "MpCautionText");
        stack.Children.Add(text);

        var sim = _tournamentPreview;
        if (sim != null)
        {
            // WrapPanels throughout: a horizontal StackPanel measures at infinite width and draws
            // past the edge of the pane, and these captions are longest in Spanish.
            var view = new WrapPanel { Margin = new Thickness(0, 9, 0, 0) };
            view.Children.Add(PreviewLabel(Strings.Get("MpTournamentPreviewViewAs")));

            var role = sim.RoleOf(t.Id);
            foreach (var (viewer, key) in new[]
                     {
                         (TournamentPreviewViewer.Player, "MpTournamentPreviewAsPlayer"),
                         (TournamentPreviewViewer.Organiser, "MpTournamentPreviewAsOrganiser"),
                         (TournamentPreviewViewer.Spectator, "MpTournamentPreviewAsSpectator"),
                     })
            {
                var choice = viewer;
                var b = new Button
                {
                    Content = Strings.Get(key),
                    Padding = new Thickness(12, 5, 12, 5),
                    Margin = new Thickness(0, 0, 4, 4),
                    Tag = choice == role ? "active" : null,
                    ToolTip = TooltipHelper.Wrap(Strings.Get(key + "Tip")),
                };
                AutomationProperties.SetAutomationId(b, TournamentPreviewIdPrefix + "ViewAs:" + choice);
                b.SetResourceReference(FrameworkElement.StyleProperty, "MpSegment");
                b.Click += (_, _) => SetTournamentPreviewViewer(choice);
                view.Children.Add(b);
            }
            stack.Children.Add(view);

            var actions = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
            if (t.Status == "running")
            {
                actions.Children.Add(PreviewButton("MpTournamentPreviewPlayRound", "PlayRound",
                    () => RunPreviewConsoleAsync(s => s.PlayRound(t.Id))));
                actions.Children.Add(PreviewButton("MpTournamentPreviewPlayToEnd", "PlayToEnd",
                    () => RunPreviewConsoleAsync(s => s.PlayToEnd(t.Id))));
            }
            if (t.Status == "registration")
            {
                actions.Children.Add(PreviewButton("MpTournamentPreviewSignUp", "SignUp",
                    () => RunPreviewConsoleAsync(s => s.SignUp(t.Id))));
                actions.Children.Add(PreviewButton("MpTournamentPreviewFill", "Fill",
                    () => RunPreviewConsoleAsync(s => s.FillPlaces(t.Id))));
            }

            var me = TournamentViewerId(t);
            var mine = TournamentPermissions.MyEntrant(t, me);
            if (mine is { Status: "pending" or "waitlist" } && !TournamentPermissions.IsOwnerOrManager(t, me))
            {
                actions.Children.Add(PreviewButton("MpTournamentPreviewAcceptMe", "AcceptMe",
                    () => RunPreviewConsoleAsync(s => s.OrganiserAcceptsViewer(t.Id))));
            }

            actions.Children.Add(PreviewButton("MpTournamentPreviewReset", "Reset", () =>
            {
                ResetTournamentPreview();
                return Task.CompletedTask;
            }));
            stack.Children.Add(actions);
        }

        box.Child = stack;
        return box;
    }

    /// <summary>
    /// The amber strip over the selected match: what the two players' game would decide, or the
    /// other side opening its room — the things a real server hears from other people.
    /// </summary>
    private UIElement? BuildTournamentPreviewMatchStrip(TournamentDetail t)
    {
        if (_tournamentPreview == null || t.Status != "running") return null;
        var m = t.Matches?.FirstOrDefault(x => string.Equals(x.Id, _selectedMatchId, StringComparison.Ordinal));
        if (m == null || !TournamentRules.Playable(m)) return null;

        var box = new Border
        {
            Padding = new Thickness(12, 8, 12, 4),
            Margin = new Thickness(0, 0, 0, 8),
            BorderThickness = new Thickness(1),
        };
        AutomationProperties.SetAutomationId(box, TournamentPreviewIdPrefix + "MatchStrip");
        box.SetResourceReference(Border.CornerRadiusProperty, "RadiusControl");
        box.SetResourceReference(Border.BackgroundProperty, "MpCautionBg");
        box.SetResourceReference(Border.BorderBrushProperty, "MpCautionRim");

        var row = new WrapPanel();
        row.Children.Add(PreviewLabel(Strings.Get("MpTournamentPreviewSimulate")));

        var tid = t.Id;
        var mid = m.Id;
        foreach (var entrantId in new[] { m.Entrant1Id!, m.Entrant2Id! })
        {
            var winner = entrantId;
            row.Children.Add(PreviewButton(
                Strings.Format("MpTournamentPreviewWins", EntrantName(t, winner)),
                "Wins:" + winner,
                () => RunPreviewConsoleAsync(s => s.ReportPlayed(tid, mid, winner)),
                literal: true));
        }

        if (m.Lobby == null)
        {
            var opener = TournamentPreview.OpenerOf(t, m);
            row.Children.Add(PreviewButton(
                Strings.Format("MpTournamentPreviewOpensRoom", opener.DisplayName ?? ""),
                "OpensRoom",
                () => RunPreviewConsoleAsync(s => s.SomebodyOpensRoom(tid, mid)),
                literal: true));
        }
        else
        {
            row.Children.Add(PreviewButton("MpTournamentPreviewUnreadable", "Unreadable",
                () => RunPreviewConsoleAsync(s => s.ReportUnreadable(tid, mid))));
        }

        box.Child = row;
        return box;
    }

    private static TextBlock PreviewLabel(string text)
    {
        var label = new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 9, 4),
        };
        label.SetResourceReference(TextBlock.FontSizeProperty, "MpLabelSize");
        label.SetResourceReference(TextBlock.ForegroundProperty, "MpCautionText");
        return label;
    }

    /// <param name="caption">A string-table key, or the caption itself when
    /// <paramref name="literal"/> — the winner buttons carry a name.</param>
    private Button PreviewButton(string caption, string action, Func<Task> run, bool literal = false)
    {
        var b = new Button
        {
            Content = literal ? caption : Strings.Get(caption),
            Margin = new Thickness(0, 0, 6, 4),
        };
        AutomationProperties.SetAutomationId(b, TournamentPreviewIdPrefix + action);
        b.SetResourceReference(FrameworkElement.StyleProperty, "MpGhostCautionButton");
        b.Click += (_, _) => { _ = run(); };
        return b;
    }

    // ---------------------------------------------------------------- running the console

    /// <summary>
    /// One thing somebody else did, then the same refresh a real push leads to. A refusal is shown
    /// exactly as an action's would be; "nothing left to play" gets a sentence of its own.
    /// </summary>
    private async Task RunPreviewConsoleAsync(Func<TournamentSimulator, PreviewOutcome> operation)
    {
        if (TournamentApiOrNull is not PreviewTournamentApi api) return;

        PreviewOutcome outcome;
        try
        {
            outcome = api.Simulate(operation);
        }
        catch (LobbyApiException ex)
        {
            await MpAlertOverlay.NoticeAsync(
                TabRootGrid,
                Strings.Get("MpTournamentActionFailed"),
                TournamentErrorText(ex),
                Strings.Get("MpAlertOk"));
            return;
        }

        await RefreshTournamentsAsync(force: true);
        FollowPreviewSelection();

        if (outcome.Stuck)
        {
            await MpAlertOverlay.NoticeAsync(
                TabRootGrid,
                Strings.Get("MpTournamentDemoInertTitle"),
                Strings.Get("MpTournamentPreviewNothingToPlay"),
                Strings.Get("MpAlertOk"));
        }
    }

    /// <summary>
    /// When the selected match has just been decided, move the selection on to the next one worth
    /// looking at — usually the person looking's next match — so playing a bracket through is a
    /// matter of pressing the same strip again.
    /// </summary>
    private void FollowPreviewSelection()
    {
        var t = _tournamentDetail;
        if (t == null) return;
        var selected = t.Matches?.FirstOrDefault(
            m => string.Equals(m.Id, _selectedMatchId, StringComparison.Ordinal));
        if (selected != null && TournamentRules.Playable(selected)) return;

        var next = PreviewTieOf(t);
        if (next == _selectedMatchId) return;
        _selectedMatchId = next;
        RenderTournamentDetail();
    }

    private void SetTournamentPreviewViewer(TournamentPreviewViewer viewer)
    {
        var sim = _tournamentPreview;
        if (sim == null) return;
        sim.ViewerChoice = viewer;
        _previewChoiceFor = _selectedTournamentId;
        DiagnosticLog.Write($"Tournaments: preview now viewed as {viewer} ({sim.ViewerOf(_selectedTournamentId)}).");
        RenderTournamentsTab();
    }

    /// <summary>Every sample as it was written; whatever was created is gone.</summary>
    internal void ResetTournamentPreview()
    {
        TournamentPreview.Reset();
        _previewChoiceFor = null;
        if (_selectedTournamentId == null || !TournamentPreview.Contains(_selectedTournamentId))
            _selectedTournamentId = TournamentDemoData.RunningId;
        DiagnosticLog.Write("Tournaments: preview reset to its samples.");
        SyncTournamentPreview();
        _selectedMatchId = PreviewTieOf(_tournamentDetail);
        RenderTournamentsTab();
    }

    // ---------------------------------------------------------------- the sample room

    /// <summary>
    /// "Play my match", "Join the room" and "Back to the room" in the preview: the room on the
    /// preview's server, then the room window showing it. The real path — the mod check, the
    /// fingerprint, the join, and the countdown that launches the game — is never reached.
    /// </summary>
    private async Task OpenPreviewMatchAsync(TournamentDetail t, TournamentMatch m)
    {
        try
        {
            await TournamentApi.OpenTournamentMatchLobbyAsync(
                t.Id, m.Id, new TournamentLobbyRequest { ModCombinedHash = "preview" });
        }
        catch (LobbyApiException ex)
        {
            await MpAlertOverlay.NoticeAsync(
                TabRootGrid,
                Strings.Get("MpTournamentActionFailed"),
                TournamentErrorText(ex),
                Strings.Get("MpAlertOk"));
            return;
        }

        await RefreshTournamentsAsync(force: true);

        var sample = TournamentPreview.RoomSample(t.Id, m.Id);
        if (sample == null) return;
        if (RealRoomIsOpen())
        {
            // A sample room opened over a real one would disconnect the real one's buttons.
            await MpAlertOverlay.NoticeAsync(
                TabRootGrid,
                Strings.Get("MpTournamentDemoInertTitle"),
                Strings.Get("MpTournamentPreviewRealRoom"),
                Strings.Get("MpAlertOk"));
            return;
        }
        if (ShowSampleRoom(sample)) _previewRoomFor = (t.Id, m.Id);
    }

    /// <summary>Close the sample room once its match has no room any more — a result, a replay,
    /// a reset — the way the server closes the real one.</summary>
    private void CloseStalePreviewRoom()
    {
        if (_previewRoomFor is not { } room) return;
        if (_demoRoomWindow == null || !ReferenceEquals(_lobbyWindow, _demoRoomWindow))
        {
            _previewRoomFor = null;
            return;
        }

        var match = _tournamentPreview?.Detail(room.TournamentId)?.Matches?
            .FirstOrDefault(x => string.Equals(x.Id, room.MatchId, StringComparison.Ordinal));
        if (match?.Lobby != null) return;

        _previewRoomFor = null;
        CloseLobbyWindow();
    }

    /// <summary>Whether a REAL room is open: the session is in one, or the room window is showing
    /// something that is not a sample.</summary>
    private bool RealRoomIsOpen()
        => (_session != null && _session.Lobby != MultiplayerSession.LobbyStatus.Idle)
           || (_lobbyWindow != null && !ReferenceEquals(_lobbyWindow, _demoRoomWindow));

    // ---------------------------------------------------------------- doors for the tests
    //
    // A list card and a bracket cell are Borders with mouse handlers, which no synthetic click
    // reaches reliably from outside the process. These do exactly what those handlers do.

    /// <summary>Open a tournament, the way a click on its card in the list does.</summary>
    internal void OpenTournamentForPreview(string tournamentId) => _ = SelectTournamentAsync(tournamentId);

    /// <summary>Select a bracket cell and repaint, the way a click on it does.
    /// (<see cref="SelectBracketMatchForPreview"/> only sets the fields, for tests that build
    /// the bracket themselves.)</summary>
    internal void ClickBracketCellForPreview(string? matchId)
    {
        _selectedMatchId = matchId;
        RenderTournamentDetail();
    }

    /// <summary>The selected bracket cell.</summary>
    internal string? SelectedBracketMatchId => _selectedMatchId;
}
