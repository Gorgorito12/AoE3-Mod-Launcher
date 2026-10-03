using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The tournament preview's server. What is pinned here is that it behaves like the real one —
/// same transitions, same refusals in the same words, same notices to the same people — because a
/// preview that answered differently would show a feature that does not exist.
///
/// <para>Two server behaviours that look like bugs are pinned on purpose
/// (<see cref="THE_SERVERS_QUIRK_ADisqualificationNeverCrownsNorFreesASeat"/>): the preview's job
/// is to show what WOULD happen, and fixing them here alone would hide them.</para>
/// </summary>
public class TournamentSimulatorTests
{
    private const string Me = TournamentDemoData.MeUserId;

    private static TournamentMatch Match(TournamentSimulator s, string tid, string mid)
        => s.Detail(tid)!.Matches!.Single(m => m.Id == mid);

    private static TournamentDetail D(TournamentSimulator s, string tid) => s.Detail(tid)!;

    private static string CreateDraft(
        TournamentSimulator s, string format = "1v1", string source = "solo",
        string mode = "open", int capacity = 8, string name = "Copa de prueba")
    {
        var body = JsonSerializer.SerializeToElement(new
        {
            name,
            mod_id = "wol",
            format,
            team_source = source,
            entry_mode = mode,
            capacity,
        });
        return s.Create(body).Id;
    }

    // ---------------------------------------------------------------- the store

    [Fact]
    public void ResetRestoresEverySample_AndDropsCreatedOnes()
    {
        var s = new TournamentSimulator();
        var created = CreateDraft(s);
        s.ViewerChoice = TournamentPreviewViewer.Spectator;
        s.PlayToEnd(TournamentDemoData.RunningId);
        Assert.Equal("finished", D(s, TournamentDemoData.RunningId).Status);

        s.Reset();

        Assert.False(s.Contains(created));
        Assert.Null(s.ViewerChoice);
        Assert.Equal("running", D(s, TournamentDemoData.RunningId).Status);
        foreach (var sample in TournamentDemoData.All())
        {
            Assert.True(s.Contains(sample.Id));
            Assert.Equal(sample.Status, D(s, sample.Id).Status);
        }
    }

    [Fact]
    public void ADetailIsACopy_SoAHandlerCannotChangeTheServer()
    {
        var s = new TournamentSimulator();
        var first = D(s, TournamentDemoData.RunningId);
        first.Status = "cancelled";
        first.Matches!.First().WinnerEntrantId = "nobody";
        first.Entrants!.First().Status = "withdrawn";

        var again = D(s, TournamentDemoData.RunningId);
        Assert.Equal("running", again.Status);
        Assert.NotEqual("nobody", again.Matches!.First().WinnerEntrantId);
        Assert.Equal("confirmed", again.Entrants!.First().Status);
    }

    [Fact]
    public void TheFixturesAreNeverTouched()
    {
        var before = JsonSerializer.Serialize(TournamentDemoData.All());
        var s = new TournamentSimulator();
        foreach (var t in TournamentDemoData.All())
        {
            s.ViewerChoice = TournamentPreviewViewer.Spectator;
            if (t.Status == "running") s.PlayToEnd(t.Id);
        }
        Assert.Equal(before, JsonSerializer.Serialize(TournamentDemoData.All()));
    }

    [Fact]
    public void TheListAndTheDetailAgree()
    {
        var s = new TournamentSimulator();
        var list = s.List(null);
        foreach (var summary in list.Tournaments!)
        {
            var detail = D(s, summary.Id);
            Assert.Equal(detail.Status, summary.Status);
            Assert.Equal(detail.ConfirmedCount, summary.ConfirmedCount);
            Assert.Equal(detail.Entrants!.Count(e => e.Status == "pending"), summary.PendingCount);
            Assert.Equal(detail.Name, summary.Name);
        }
    }

    [Fact]
    public void ADraftIsListedOnlyForItsOwner()
    {
        var s = new TournamentSimulator();
        var id = CreateDraft(s);

        Assert.Contains(s.List(id).Drafts!, t => t.Id == id);
        Assert.DoesNotContain(s.List(id).Tournaments!, t => t.Id == id);

        s.ViewerChoice = TournamentPreviewViewer.Spectator;
        Assert.DoesNotContain(s.List(id).Drafts!, t => t.Id == id);
    }

    // ---------------------------------------------------------------- the lifecycle

    [Fact]
    public void ACreatedTournamentWalksFromDraftToAChampion()
    {
        var s = new TournamentSimulator();
        var id = CreateDraft(s, capacity: 6);
        Assert.Equal("draft", D(s, id).Status);
        Assert.Equal(TournamentPreviewViewer.Organiser, s.RoleOf(id));

        s.OpenRegistration(id);
        s.FillPlaces(id);
        Assert.Equal(6, D(s, id).Entrants!.Count(e => e.Status == "confirmed"));

        s.CloseRegistration(id);
        s.Seed(id);
        Assert.Equal(Enumerable.Range(1, 6), D(s, id).Entrants!.Select(e => e.Seed!.Value).OrderBy(x => x));

        s.Start(id);
        var t = D(s, id);
        Assert.Equal("running", t.Status);
        Assert.Equal(8, t.BracketSize);
        Assert.Equal(3, t.RoundsTotal);
        Assert.Equal(2, t.Matches!.Count(m => m.Status == "bye"));

        var outcome = s.PlayToEnd(id);
        Assert.False(outcome.Stuck);
        t = D(s, id);
        Assert.Equal("finished", t.Status);
        Assert.Contains(t.Entrants!, e => e.Id == t.WinnerEntrantId);
    }

    [Fact]
    public void ACreatedTeamTournamentPlaysToAChampionToo()
    {
        var s = new TournamentSimulator();
        var id = CreateDraft(s, format: "2v2", source: "adhoc", capacity: 4);
        s.OpenRegistration(id);
        s.FillPlaces(id);
        var entrants = D(s, id).Entrants!;
        Assert.Equal(4, entrants.Count);
        Assert.All(entrants, e =>
        {
            Assert.Equal("team", e.Kind);
            Assert.Equal(2, e.MemberIds!.Count);
            Assert.Equal(2, e.Members!.Count);
        });
        // Nobody appears twice: a duplicate would make "is this my match" unanswerable.
        Assert.Equal(8, entrants.SelectMany(e => e.MemberIds!).Distinct().Count());

        s.CloseRegistration(id);
        s.Seed(id);
        s.Start(id);
        s.PlayToEnd(id);
        Assert.Equal("finished", D(s, id).Status);
    }

    [Fact]
    public void StartDrawsTheServersBracket()
    {
        var s = new TournamentSimulator();
        var id = CreateDraft(s, capacity: 5);
        s.OpenRegistration(id);
        s.FillPlaces(id);
        s.CloseRegistration(id);
        s.Seed(id);
        s.Start(id);

        var t = D(s, id);
        var expected = TournamentRules.Generate(
            t.Entrants!.Where(e => e.Status == "confirmed").Select(e => (e.Id, e.Seed!.Value)).ToList(),
            (r, p) => $"{id}-r{r}m{p}");
        foreach (var m in expected)
        {
            var actual = t.Matches!.Single(x => x.Id == m.Id);
            Assert.Equal(m.Entrant1Id, actual.Entrant1Id);
            Assert.Equal(m.Entrant2Id, actual.Entrant2Id);
            Assert.Equal(m.Status, actual.Status);
            Assert.Equal(m.NextMatchId, actual.NextMatchId);
        }
    }

    [Fact]
    public void StartRefusesInTheServersWords()
    {
        // The registration sample has a confirmed entrant with no seed — the one row that holds
        // the tournament up — so starting before seeding is refused exactly as the server would.
        var s = new TournamentSimulator();
        var id = TournamentDemoData.RegistrationId;
        s.CloseRegistration(id);

        var ex = Assert.Throws<LobbyApiException>(() => s.Start(id));
        Assert.Equal(409, ex.Status);
        Assert.Equal("conflict", ex.Code);
        Assert.Equal("Seed the entrants first.", ex.Message);

        s.Seed(id);
        s.Start(id);
        var twice = Assert.Throws<LobbyApiException>(() => s.Start(id));
        Assert.Equal("Close registration before starting.", twice.Message);
    }

    [Fact]
    public void EveryRefusalIsALobbyApiExceptionWithTheServersCode()
    {
        var s = new TournamentSimulator();
        var running = TournamentDemoData.RunningId;

        // A player cannot run the tournament.
        Assert.Equal("forbidden", Assert.Throws<LobbyApiException>(() => s.Cancel(running)).Code);
        // Registration is shut.
        Assert.Equal("tournament_closed", Assert.Throws<LobbyApiException>(() => s.Enter(running)).Code);
        // An unknown tournament is a 404, before anything else is asked.
        var missing = Assert.Throws<LobbyApiException>(() => s.OpenRegistration("nope"));
        Assert.Equal(404, missing.Status);
        Assert.Equal("not_found", missing.Code);

        s.ViewerChoice = TournamentPreviewViewer.Organiser;
        Assert.Equal("Registration cannot be opened from this state.",
            Assert.Throws<LobbyApiException>(() => s.OpenRegistration(running)).Message);
    }

    // ---------------------------------------------------------------- places

    [Fact]
    public void OpenEntryConfirmsWhileThereIsRoom_ThenWaitlists()
    {
        var s = new TournamentSimulator();
        var id = CreateDraft(s, capacity: 2);
        s.OpenRegistration(id);
        s.SignUp(id, 3);
        var statuses = D(s, id).Entrants!.Select(e => e.Status).ToList();
        Assert.Equal(2, statuses.Count(x => x == "confirmed"));
        Assert.Equal(1, statuses.Count(x => x == "waitlist"));
        Assert.Equal(2, D(s, id).ConfirmedCount);
    }

    [Fact]
    public void ApprovalModeMakesEveryoneAsk()
    {
        var s = new TournamentSimulator();
        var id = CreateDraft(s, mode: "approval", capacity: 2);
        s.OpenRegistration(id);
        s.SignUp(id, 3);
        Assert.All(D(s, id).Entrants!, e => Assert.Equal("pending", e.Status));
        Assert.Equal(0, D(s, id).ConfirmedCount);
    }

    [Fact]
    public void AcceptingTakesASeat_AndOutsideRegistrationLandsOnTheWaitingList()
    {
        var s = new TournamentSimulator();
        var id = TournamentDemoData.RegistrationId;

        s.Accept(id, "g8");
        Assert.Equal("confirmed", D(s, id).Entrants!.Single(e => e.Id == "g8").Status);

        // A seat can only be claimed while registration is open — the server's conditional UPDATE.
        s.CloseRegistration(id);
        s.Accept(id, "g9");
        Assert.Equal("waitlist", D(s, id).Entrants!.Single(e => e.Id == "g9").Status);

        var ex = Assert.Throws<LobbyApiException>(() => s.Accept(id, "g1"));
        Assert.Equal("That entrant is not awaiting a decision.", ex.Message);
    }

    [Fact]
    public void TheOrganiserAcceptingMe_TellsMe()
    {
        var s = new TournamentSimulator();
        var id = CreateDraft(s, mode: "approval");
        s.OpenRegistration(id);

        s.ViewerChoice = TournamentPreviewViewer.Player;
        var entry = s.Enter(id);
        Assert.Equal("pending", entry.Status);

        var outcome = s.OrganiserAcceptsViewer(id);
        var notice = Assert.Single(outcome.ToViewer);
        Assert.Equal("entry_accepted", notice.Kind);
        Assert.Equal(id, notice.TournamentId);
    }

    [Fact]
    public void WithdrawingGivesThePlaceToWhoeverWaitedLongest_Silently()
    {
        var s = new TournamentSimulator();
        var id = CreateDraft(s, capacity: 2);
        s.OpenRegistration(id);
        s.SignUp(id, 4);
        var before = D(s, id).Entrants!;
        var firstWaiting = before.First(e => e.Status == "waitlist").Id;

        // The player's view of a tournament I organise is its first entrant.
        s.ViewerChoice = TournamentPreviewViewer.Player;
        var mine = before.First(e => e.CaptainUserId == s.ViewerOf(id));
        Assert.Equal("confirmed", mine.Status);

        var outcome = s.Withdraw(id, mine.Id);
        Assert.Empty(outcome.ToViewer);
        var after = D(s, id).Entrants!;
        Assert.Equal("withdrawn", after.Single(e => e.Id == mine.Id).Status);
        Assert.Equal("confirmed", after.Single(e => e.Id == firstWaiting).Status);
        Assert.Equal(2, D(s, id).ConfirmedCount);
    }

    [Fact]
    public void OnlyTheCaptainWithdraws_AndNotOnceTheBracketIsDrawn()
    {
        var s = new TournamentSimulator();
        var running = TournamentDemoData.RunningId;
        var mine = D(s, running).Entrants!.Single(e => e.MemberIds!.Contains(Me));
        Assert.Equal("The bracket has already been drawn.",
            Assert.Throws<LobbyApiException>(() => s.Withdraw(running, mine.Id)).Message);

        var notMine = D(s, running).Entrants!.First(e => !e.MemberIds!.Contains(Me));
        Assert.Equal("forbidden", Assert.Throws<LobbyApiException>(() => s.Withdraw(running, notMine.Id)).Code);
    }

    // ---------------------------------------------------------------- playing

    [Fact]
    public void PlayingMyMatchMovesMeOn_AndTellsMeIWon()
    {
        var s = new TournamentSimulator();
        var id = TournamentDemoData.RunningId;
        var mine = D(s, id).Entrants!.Single(e => e.MemberIds!.Contains(Me)).Id;

        var outcome = s.ReportPlayed(id, "r2m1", mine);

        var played = Match(s, id, "r2m1");
        Assert.Equal("done", played.Status);
        Assert.Equal("played", played.Outcome);
        Assert.Null(played.Lobby);
        // Position 1 feeds slot 2.
        Assert.Equal(mine, Match(s, id, "r3m0").Entrant2Id);

        var notice = Assert.Single(outcome.ToViewer);
        Assert.Equal("match_done", notice.Kind);
        Assert.True(notice.YouWon);
        Assert.Equal("r2m1", notice.TournamentMatchId);
    }

    [Fact]
    public void LosingMyMatchTellsMeSo()
    {
        var s = new TournamentSimulator();
        var id = TournamentDemoData.RunningId;
        var r2m1 = Match(s, id, "r2m1");
        var rival = r2m1.Entrant1Id == "r4" ? r2m1.Entrant2Id! : r2m1.Entrant1Id!;

        var notice = Assert.Single(s.ReportPlayed(id, "r2m1", rival).ToViewer);
        Assert.False(notice.YouWon);
    }

    [Fact]
    public void NoticesReachOnlyThePersonLooking()
    {
        var s = new TournamentSimulator();
        s.ViewerChoice = TournamentPreviewViewer.Spectator;
        var outcome = s.ReportPlayed(TournamentDemoData.RunningId, "r2m1", "r4");
        Assert.Empty(outcome.ToViewer);
    }

    [Fact]
    public void TheFinalCrownsAChampion()
    {
        var s = new TournamentSimulator();
        var id = TournamentDemoData.MyRoomId;
        s.ReportPlayed(id, "om0", "o1");
        var final = Match(s, id, "om2");
        Assert.True(TournamentRules.Playable(final));

        var outcome = s.ReportPlayed(id, "om2", "o1");
        var t = D(s, id);
        Assert.Equal("finished", t.Status);
        Assert.Equal("o1", t.WinnerEntrantId);
        Assert.True(Assert.Single(outcome.ToViewer).YouWon);
    }

    [Fact]
    public void AWalkoverAdvancesSilently()
    {
        var s = new TournamentSimulator();
        var id = TournamentDemoData.OrganiserId;
        var outcome = s.Walkover(id, "gm5", "g2");
        Assert.Empty(outcome.ToViewer);
        var m = Match(s, id, "gm5");
        Assert.Equal("walkover", m.Outcome);
        Assert.Equal("g2", Match(s, id, "gm6").Entrant2Id);

        Assert.Equal("Cannot award this match: already_decided",
            Assert.Throws<LobbyApiException>(() => s.Walkover(id, "gm5", "g3")).Message);
    }

    [Fact]
    public void AReplayClosesTheRoom_KeepsTheTie_AndTellsBothSidesIncludingTheOrganiser()
    {
        var s = new TournamentSimulator();
        // MyRoom: I organise it AND play in om0, whose room I opened.
        var id = TournamentDemoData.MyRoomId;
        Assert.NotNull(Match(s, id, "om0").Lobby);

        var outcome = s.Replay(id, "om0");
        var m = Match(s, id, "om0");
        Assert.Null(m.Lobby);
        Assert.Equal("pending", m.Status);
        Assert.Equal("match_replay", Assert.Single(outcome.ToViewer).Kind);
    }

    [Fact]
    public void ARivalOpeningTheRoomTellsMe_AndTheCardBecomesJoin()
    {
        var s = new TournamentSimulator();
        var id = TournamentDemoData.RunningId;
        var outcome = s.SomebodyOpensRoom(id, "r2m1");

        var m = Match(s, id, "r2m1");
        Assert.NotNull(m.Lobby);
        Assert.NotEqual(Me, m.Lobby!.HostUserId);
        Assert.Equal(MatchCardState.JoinRoom, MatchCards.For(m, Me, D(s, id).Entrants));
        var notice = Assert.Single(outcome.ToViewer);
        Assert.Equal("room_opened", notice.Kind);
        Assert.Equal(m.Lobby.Id, notice.LobbyId);
    }

    [Fact]
    public void OpeningMyRoomTellsMeNothing_AndOpeningItAgainIsTheSameRoom()
    {
        var s = new TournamentSimulator();
        var id = TournamentDemoData.RunningId;

        var (first, outcome) = s.OpenRoom(id, "r2m1");
        Assert.False(first.Existing);
        Assert.Equal(Me, first.HostUserId);
        Assert.Empty(outcome.ToViewer);
        Assert.Equal(MatchCardState.ReturnToRoom,
            MatchCards.For(Match(s, id, "r2m1"), Me, D(s, id).Entrants));

        var (second, _) = s.OpenRoom(id, "r2m1");
        Assert.True(second.Existing);
        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public void AnOutsiderCannotOpenTheRoom()
    {
        var s = new TournamentSimulator();
        s.ViewerChoice = TournamentPreviewViewer.Spectator;
        var ex = Assert.Throws<LobbyApiException>(() => s.OpenRoom(TournamentDemoData.RunningId, "r2m1"));
        Assert.Equal(403, ex.Status);
        Assert.Equal("tournament_not_participant", ex.Code);
    }

    [Fact]
    public void AGameWithNoReadableResultClosesTheRoomAndLeavesTheTieOpen()
    {
        var s = new TournamentSimulator();
        var id = TournamentDemoData.RunningId;
        s.ReportUnreadable(id, "r2m2");
        var m = Match(s, id, "r2m2");
        Assert.Null(m.Lobby);
        Assert.Equal("pending", m.Status);
        Assert.True(TournamentRules.Playable(m));
    }

    [Fact]
    public void ADisqualificationCascades()
    {
        var s = new TournamentSimulator();
        var id = TournamentDemoData.OrganiserId;
        s.Disqualify(id, "g2");
        var gm5 = Match(s, id, "gm5");
        Assert.Equal("dq", gm5.Outcome);
        Assert.Equal("g3", gm5.WinnerEntrantId);
        Assert.Equal("g3", Match(s, id, "gm6").Entrant2Id);
        Assert.Equal("disqualified", D(s, id).Entrants!.Single(e => e.Id == "g2").Status);
    }

    /// <summary>
    /// THE SERVER'S QUIRK, copied on purpose: the disqualify route never finishes the tournament,
    /// even when its cascade decides the final, and never gives the seat back. A preview that
    /// crowned somebody here would show something the real server does not do.
    /// </summary>
    [Fact]
    public void THE_SERVERS_QUIRK_ADisqualificationNeverCrownsNorFreesASeat()
    {
        var s = new TournamentSimulator();
        var id = TournamentDemoData.MyRoomId;
        int seats = D(s, id).ConfirmedCount!.Value;

        s.Disqualify(id, "o4");   // o1 takes om0 and reaches the final against o2
        s.Disqualify(id, "o2");   // o1 takes the final too — by the cascade

        var t = D(s, id);
        var final = t.Matches!.Single(m => m.Round == 2);
        Assert.Equal("done", final.Status);
        Assert.Equal("o1", final.WinnerEntrantId);
        Assert.Equal("running", t.Status);
        Assert.Null(t.WinnerEntrantId);
        Assert.Equal(seats, t.ConfirmedCount);
    }

    [Fact]
    public void OnlyTheOwnerCancelsOrAppoints_AndTheOwnerIsNeverACoOrganiser()
    {
        var s = new TournamentSimulator();
        var id = TournamentDemoData.RunningId;
        Assert.Equal("forbidden", Assert.Throws<LobbyApiException>(() => s.AddManager(id, "u5")).Code);

        s.ViewerChoice = TournamentPreviewViewer.Organiser;
        var owner = s.ViewerOf(id);
        Assert.Equal("The owner already runs it.",
            Assert.Throws<LobbyApiException>(() => s.AddManager(id, owner)).Message);

        s.AddManager(id, "u5");
        Assert.Contains("u5", D(s, id).ManagerUserIds!);
        Assert.Contains(D(s, id).Managers!, m => m.UserId == "u5" && !string.IsNullOrEmpty(m.DisplayName));

        s.RemoveManager(id, "u5");
        Assert.DoesNotContain("u5", D(s, id).ManagerUserIds!);
        Assert.Equal("not_found", Assert.Throws<LobbyApiException>(() => s.RemoveManager(id, "u5")).Code);

        s.Cancel(id);
        Assert.Equal("cancelled", D(s, id).Status);
    }

    [Fact]
    public void PlayingTheRoundLeavesMyOwnMatchToMe()
    {
        var s = new TournamentSimulator();
        var id = TournamentDemoData.RunningId;

        // Round two: the one other undecided tie, and not mine.
        Assert.False(s.PlayRound(id).Stuck);
        Assert.Equal("done", Match(s, id, "r2m2").Status);
        Assert.Equal("pending", Match(s, id, "r2m1").Status);

        // That filled the other semifinal, which is next.
        Assert.False(s.PlayRound(id).Stuck);
        Assert.Equal("done", Match(s, id, "r3m1").Status);

        // Now everything left waits on my match, and the round button says so instead of
        // playing it for me.
        Assert.True(s.PlayRound(id).Stuck);
        Assert.Equal("pending", Match(s, id, "r2m1").Status);
    }

    [Fact]
    public void PlayingToTheEndReachesAChampion_WithMyMatchesIncluded()
    {
        var s = new TournamentSimulator();
        var id = TournamentDemoData.RunningId;
        var outcome = s.PlayToEnd(id);
        Assert.False(outcome.Stuck);
        Assert.Equal("finished", D(s, id).Status);
        // I played at least one more match, so I was told about it.
        Assert.Contains(outcome.ToViewer, n => n.Kind == "match_done");
    }

    [Fact]
    public void SimulatedResultsAreTheSameEveryRun()
    {
        string Champion()
        {
            var s = new TournamentSimulator();
            s.PlayToEnd(TournamentDemoData.RunningId);
            return D(s, TournamentDemoData.RunningId).WinnerEntrantId!;
        }
        Assert.Equal(Champion(), Champion());
    }

    // ---------------------------------------------------------------- who is looking

    [Fact]
    public void EverySampleOpensAsTheViewerItWasWrittenFor()
    {
        var s = new TournamentSimulator();
        foreach (var t in TournamentDemoData.All())
        {
            Assert.Equal(Me, s.ViewerOf(t.Id));
        }
        Assert.Equal(TournamentPreviewViewer.Player, s.RoleOf(TournamentDemoData.RunningId));
        Assert.Equal(TournamentPreviewViewer.Organiser, s.RoleOf(TournamentDemoData.OrganiserId));
        Assert.Equal(TournamentPreviewViewer.Organiser, s.RoleOf(TournamentDemoData.RegistrationId));
    }

    [Fact]
    public void TheViewerIsWhoeverTheChosenRoleSays()
    {
        var s = new TournamentSimulator();

        s.ViewerChoice = TournamentPreviewViewer.Organiser;
        Assert.Equal("u1", s.ViewerOf(TournamentDemoData.RunningId));

        s.ViewerChoice = TournamentPreviewViewer.Spectator;
        Assert.Equal(TournamentSimulator.SpectatorUserId, s.ViewerOf(TournamentDemoData.RunningId));

        // A PLAYER never runs the tournament: in the two samples I organise, the player's view is
        // the first entrant who does not.
        s.ViewerChoice = TournamentPreviewViewer.Player;
        Assert.Equal(Me, s.ViewerOf(TournamentDemoData.RunningId));
        Assert.Equal("u-g2", s.ViewerOf(TournamentDemoData.RegistrationId));
        Assert.Equal("u-g1", s.ViewerOf(TournamentDemoData.OrganiserId));
    }

    // ---------------------------------------------------------------- the room

    [Fact]
    public void TheRoomTitleIsComposedLikeTheServers()
    {
        var s = new TournamentSimulator();
        var id = TournamentDemoData.RunningId;
        s.OpenRoom(id, "r2m1");
        var sample = s.RoomSample(id, "r2m1")!;
        var t = D(s, id);
        var m = t.Matches!.Single(x => x.Id == "r2m1");
        string Name(string? e) => t.Entrants!.Single(x => x.Id == e).DisplayName!;
        var expected = $"{t.Name} · R2 · {Name(m.Entrant1Id)} · {Name(m.Entrant2Id)}";
        Assert.Equal(expected.Length > 80 ? expected[..80] : expected, sample.RoomName);
        Assert.True(sample.RoomName.Length <= 80);

        // The last round is called Final, untranslated, as the server writes it.
        s.ViewerChoice = TournamentPreviewViewer.Spectator;
        s.PlayRound(TournamentDemoData.MyRoomId);
        Assert.Contains(" · Final · ",
            s.RoomTitle(D(s, TournamentDemoData.MyRoomId), D(s, TournamentDemoData.MyRoomId).Matches!.Single(x => x.Round == 2)));
    }

    [Fact]
    public void TheRoomHoldsTheHostsSide_AndThePersonJoiningIt()
    {
        var s = new TournamentSimulator();

        // My own room, just opened: only me in it, one seat free for the rival.
        s.OpenRoom(TournamentDemoData.RunningId, "r2m1");
        var mine = s.RoomSample(TournamentDemoData.RunningId, "r2m1")!;
        Assert.Equal(2, mine.Seats);
        Assert.True(mine.Competitive);
        var seat = Assert.Single(mine.Players);
        Assert.Equal(Me, seat.UserId);
        Assert.True(seat.IsHost);

        // The team sample: the other side opened it, so their three are in, plus me.
        var theirs = s.RoomSample(TournamentDemoData.TeamsId, "tm0")!;
        Assert.Equal(6, theirs.Seats);
        Assert.Equal(4, theirs.Players.Count);
        Assert.Contains(theirs.Players, p => p.UserId == Me && !p.IsHost);
        Assert.Single(theirs.Players, p => p.IsHost);
    }

    [Fact]
    public void ATeamTournamentRefusesAnEmptyEntryLikeTheServer()
    {
        // The launcher has no team picker, so "Enter" on a registered-teams tournament sends no
        // team — and the server refuses. The preview shows that gap rather than hiding it.
        var s = new TournamentSimulator();
        var id = CreateDraft(s, format: "3v3", source: "registered");
        s.OpenRegistration(id);
        s.ViewerChoice = TournamentPreviewViewer.Player;
        var ex = Assert.Throws<LobbyApiException>(() => s.Enter(id));
        Assert.Equal("bad_request", ex.Code);
        Assert.Equal("team_id required for this tournament", ex.Message);
    }

    // ---------------------------------------------------------------- the adapter

    [Fact]
    public void TheAdapterAnswersWithTheServersShapes_AndPushesWhatTheViewerWouldGet()
    {
        var pushed = new List<TournamentUpdateNotice>();
        var s = new TournamentSimulator();
        var api = new PreviewTournamentApi(s, () => TournamentDemoData.RunningId, pushed.Add);

        var list = api.ListTournamentsAsync().Result;
        Assert.Equal(TournamentDemoData.All().Count, list.Tournaments!.Count);

        api.Simulate(sim => sim.SomebodyOpensRoom(TournamentDemoData.RunningId, "r2m1"));
        Assert.Equal("room_opened", Assert.Single(pushed).Kind);

        var refused = api.StartTournamentAsync(TournamentDemoData.RunningId);
        Assert.True(refused.IsFaulted);
        Assert.IsType<LobbyApiException>(refused.Exception!.InnerException);
    }

    private sealed class Loop
    {
        public Loop? Self { get; set; }
    }

    [Fact]
    public void TheAdapterTurnsItsOwnBugsIntoARefusalTheTabWillShow()
    {
        var api = new PreviewTournamentApi(new TournamentSimulator(), () => null, null);
        var loop = new Loop();
        loop.Self = loop;   // cannot be serialised: a stand-in for any bug inside the simulator

        var task = api.CreateTournamentAsync(loop);
        Assert.True(task.IsFaulted);
        var ex = Assert.IsType<LobbyApiException>(task.Exception!.InnerException);
        Assert.Equal("preview_error", ex.Code);
    }
}
