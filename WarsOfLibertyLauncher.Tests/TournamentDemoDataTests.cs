using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// That the sample tournaments still show what they claim to show.
///
/// <para><b>This suite is not here because the fixture might break.</b> It is here because a
/// fixture decays: somebody tidies a scenario, and six months later the four samples are four
/// near-identical brackets that no longer cover the states they were built to cover — and
/// nothing says so, because everything still renders. The whole value of a preview is that
/// looking at it answers the question, and that stops being true silently.</para>
///
/// <para>So what is asserted here is the PURPOSE of each sample, not its contents.</para>
/// </summary>
public class TournamentDemoDataTests
{
    private const string Me = TournamentDemoData.MeUserId;

    private static TournamentEntrant? Entrant(TournamentDetail t, string? id)
        => t.Entrants?.FirstOrDefault(e => e.Id == id);

    // ---------------------------------------------------------------- the list

    [Fact]
    public void EveryScenarioIsOfferedAndNoneCollide()
    {
        var list = TournamentDemoData.List();
        Assert.NotNull(list.Tournaments);
        // The list and All() are two hand-written lists of the same samples, so one growing
        // without the other is the way a new scenario ends up unreachable from the UI while
        // every test that walks All() still passes.
        Assert.Equal(TournamentDemoData.All().Count, list.Tournaments!.Count);

        // The list IS the scenario picker, so a duplicate id would silently make one of them
        // unreachable — clicking it would open its twin.
        var ids = list.Tournaments.Select(t => t.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());

        // And every one of them must be reachable by the id the card carries.
        foreach (var id in ids) Assert.NotNull(TournamentDemoData.ById(id));
        Assert.Null(TournamentDemoData.ById("no-such-thing"));
    }

    /// <summary>
    /// THE ORGANISER SAMPLE HAS NO MATCH OF MINE, AND ONE OF SOMEBODY ELSE'S BEING PLAYED.
    ///
    /// <para>Both halves are the sample. It is the only one written from OUTSIDE the bracket,
    /// so the moment somebody "tidies" it by putting <see cref="TournamentDemoData.MeUserId"/>
    /// into an entrant, every card gains a my-match answer and the organiser's screen — six
    /// cards that offer nothing and one that is being played — becomes unreachable. And
    /// without the live room there is nothing to watch, which is the state the whole scenario
    /// exists to show.</para>
    /// </summary>
    [Fact]
    public void THE_ORGANISER_SAMPLE_IS_MINE_TO_RUN_AND_NONE_OF_IT_IS_MINE_TO_PLAY()
    {
        var t = TournamentDemoData.Organiser();

        Assert.Equal(Me, t.OwnerUserId);

        // Me appears exactly once in the whole fixture, and it is as the owner.
        Assert.All(t.Entrants!, e =>
        {
            Assert.NotEqual(Me, e.CaptainUserId);
            Assert.DoesNotContain(Me, e.MemberIds ?? new List<string>());
        });

        // Therefore no card is mine, whoever asks.
        Assert.All(t.Matches!, m =>
            Assert.False(MatchCards.IsMine(m, Me, t.Entrants),
                         $"match {m.Id} became mine; the organiser sample has no my-match"));

        // And exactly the state it exists for: a room, on somebody else's match, in game.
        var live = t.Matches!.Where(m => m.Lobby != null).ToList();
        Assert.Single(live);
        Assert.Equal("in_game", live[0].Lobby!.Status);
        Assert.Equal(MatchCardState.SuperviseRoom,
            MatchCards.For(live[0], Me, t.Entrants, canSupervise: true));

        // Beside it, cards that still offer nothing - or a screenshot cannot show that the
        // rest of the bracket was left alone.
        Assert.Contains(t.Matches!, m => m.Status == "done");
        Assert.Contains(t.Matches!, m =>
            m.Status == "pending" && m.Lobby == null
            && (string.IsNullOrEmpty(m.Entrant1Id) || string.IsNullOrEmpty(m.Entrant2Id)));
    }

    /// <summary>
    /// The watched room's contents carry no player names.
    ///
    /// <para>Who is in that room is the bracket's answer, and the window reads it from there.
    /// A second copy here could drift into a room whose occupants disagree with the slot —
    /// which is the one thing an organiser would open the window to check.</para>
    /// </summary>
    [Fact]
    public void TheWatchedRoomSampleDoesNotRestateTheRoster()
    {
        var t = TournamentDemoData.Organiser();
        var sample = TournamentDemoData.WatchSample();
        var live = t.Matches!.First(m => m.Lobby != null);

        var sides = new[] { live.Entrant1Id, live.Entrant2Id }
            .Select(id => Entrant(t, id)!.DisplayName)
            .ToList();

        // The chat is spoken BY the two players - that is the point of the sample - but the
        // room's own description never names them.
        Assert.DoesNotContain(sides, s => sample.RoomTitle.Contains(s, StringComparison.Ordinal));
        Assert.NotEmpty(sample.Chat);
        Assert.All(sample.Chat, line => Assert.Contains(line.Author, sides));
    }

    [Fact]
    public void THE_REGISTRATION_SAMPLE_HAS_A_CONFIRMED_ENTRANT_WITH_NO_SEED()
    {
        // The single row the whole 8b screen was redesigned around. CanStart refuses while any
        // confirmed entrant has no seed, and before the seed column existed the tournament
        // simply did not begin and the screen said nothing about why. If this sample ever gets
        // seeds handed out "to tidy it up", the amber row disappears and so does the only
        // place that state can be looked at.
        var t = TournamentDemoData.Registration();
        var confirmed = t.Entrants!.Where(e => e.Status == "confirmed").ToList();
        Assert.NotEmpty(confirmed);
        Assert.Contains(confirmed, e => !e.Seed.HasValue);
        Assert.False(TournamentPermissions.CanStart(t, Me));
    }

    [Fact]
    public void EveryScenarioNamesItselfAndKnowsHowManyRoundsItHas()
    {
        foreach (var t in TournamentDemoData.All())
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Name), $"{t.Id} has no name");

            // Without RoundsTotal the headers read "ROUND 4" instead of "FINAL", which is
            // exactly the kind of thing a preview exists to catch and would then be hiding.
            if (t.Matches is { Count: > 0 })
                Assert.True(t.RoundsTotal > 0, $"{t.Id} draws a bracket with no round count");
        }
    }

    [Fact]
    public void NoScenarioCarriesAModIdSoNothingCanTryToOpenARoom()
    {
        // A second lock after the demo guard itself: OpenTournamentMatchAsync early-returns
        // without a mod id, so even a missed guard cannot reach the network.
        Assert.All(TournamentDemoData.All(), t => Assert.True(string.IsNullOrEmpty(t.ModId)));
    }

    // ---------------------------------------------------------------- the big bracket

    [Fact]
    public void TheRunningBracketIsBigEnoughToBeWorthLookingAt()
    {
        var t = TournamentDemoData.Running();
        Assert.Equal("running", t.Status);
        Assert.Equal(15, t.Entrants!.Count);          // 15 in a 16-slot bracket, so one bye
        Assert.Equal(4, t.RoundsTotal);
        Assert.Equal(8, t.Matches!.Count(m => m.Round == 1));
        Assert.Equal(4, t.Matches!.Count(m => m.Round == 2));
        Assert.Single(t.Matches!, m => m.Round == 4);
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_TheSamplesBetweenThemShowEveryCardStateWorthSeeing()
    {
        // If a tidy-up ever leaves the samples without one of these, that card stops being
        // previewable and nobody finds out by looking — which is the failure this suite exists
        // to make loud.
        //
        // Asserted across ALL the samples rather than within one, and that is a fact about
        // single elimination rather than a convenience: one viewer cannot have a playable match
        // and a match waiting for an opponent at the same time, because waiting means you have
        // already won your last one. Forcing both into one bracket would mean a worse fixture,
        // so the running bracket supplies the busy states and the team one supplies the join.
        // Asked the way BuildBracketCard asks it, permission included. The card's answer
        // depends on who is looking as well as on the match, so a sweep that always asked as
        // a viewer with no powers could never reach SuperviseRoom whatever fixture existed.
        var states = TournamentDemoData.All()
            .SelectMany(t => (t.Matches ?? new List<TournamentMatch>())
                .Select(m => MatchCards.For(
                    m, Me, t.Entrants, TournamentPermissions.IsOwnerOrManager(t, Me))))
            .Distinct()
            .ToList();

        // ALL EIGHT, read off the enum rather than listed by hand: a ninth state added to
        // MatchCardState with no sample behind it fails here instead of quietly shipping a
        // card nobody has ever looked at.
        foreach (MatchCardState state in Enum.GetValues<MatchCardState>())
        {
            Assert.Contains(state, states);
        }
    }

    [Fact]
    public void THE_FOUR_EXCLUSIVE_STATES_EACH_HAVE_THEIR_OWN_SAMPLE()
    {
        // Playable, JoinRoom, ReturnToRoom and WaitingOpponent are four answers to one
        // question about ONE match: whether a room exists and who opened it. A person is a
        // single entrant in a tournament and an entrant has a single live match, so no
        // bracket can ever show two of them to the same viewer.
        //
        // That is why there are four brackets and not one, and why this asserts a sample per
        // state rather than a count: merging two of these samples to tidy up would look
        // harmless and would silently delete a card from the preview.
        var exclusive = new[]
        {
            MatchCardState.Playable, MatchCardState.JoinRoom,
            MatchCardState.ReturnToRoom, MatchCardState.WaitingOpponent,
        };

        foreach (var state in exclusive)
        {
            var carriers = TournamentDemoData.All()
                .Where(t => (t.Matches ?? new List<TournamentMatch>())
                    .Any(m => MatchCards.For(m, Me, t.Entrants) == state))
                .ToList();
            Assert.Single(carriers);
        }
    }

    [Fact]
    public void THE_TEAM_LINE_UPS_CARRY_NAMES_AND_NOT_ONLY_IDS()
    {
        // The pills on a team card draw Members; whether the match is MINE is decided from
        // MemberIds. A fixture where those two disagree renders a line-up the viewer can see
        // themselves in, on a card that says the match belongs to somebody else - so they are
        // built from one list and checked to still match here.
        var t = TournamentDemoData.Teams();
        foreach (var e in t.Entrants!)
        {
            Assert.NotNull(e.Members);
            Assert.Equal(e.MemberIds!.Count, e.Members!.Count);
            Assert.Equal(e.MemberIds, e.Members.Select(m => m.UserId).ToList());
            Assert.All(e.Members, m => Assert.False(string.IsNullOrWhiteSpace(m.DisplayName)));

            // Somebody has to wear the captain's mark, or the pill row says nothing that a
            // plain list of three names would not.
            Assert.Contains(e.Members, m => m.UserId == e.CaptainUserId);
        }
    }

    [Fact]
    public void ONLY_THE_APPROVAL_SCENARIO_REPORTS_APPLICATIONS_WAITING()
    {
        // The list row's "2 requests" reads PendingCount off the summary. It is derived from
        // the entrant list rather than written, so the two cannot disagree - and the count
        // being zero everywhere else is what makes the badge mean something where it appears.
        var list = TournamentDemoData.List();
        var withRequests = list.Tournaments!.Where(t => (t.PendingCount ?? 0) > 0).ToList();
        Assert.Single(withRequests);
        Assert.Equal(TournamentDemoData.RegistrationId, withRequests[0].Id);
        Assert.Equal(
            TournamentDemoData.Registration().Entrants!.Count(e => e.Status == "pending"),
            withRequests[0].PendingCount);
    }

    [Fact]
    public void TheRunningBracketAloneCarriesTheBusyStates()
    {
        // The one people will actually look at. Keeping this separate from the union above
        // means a change that empties it cannot hide behind another sample covering for it.
        var t = TournamentDemoData.Running();
        var states = t.Matches!.Select(m => MatchCards.For(m, Me, t.Entrants)).Distinct().ToList();

        Assert.Contains(MatchCardState.Playable, states);
        Assert.Contains(MatchCardState.Bye, states);
        Assert.Contains(MatchCardState.Done, states);
        Assert.Contains(MatchCardState.InProgress, states);
        Assert.Contains(MatchCardState.NotMine, states);
    }

    [Fact]
    public void ExactlyOneMatchIsMineAndReadyToPlay()
    {
        // More than one and the preview stops telling you what the common case looks like;
        // none and the most important card in the feature is not on screen at all.
        var t = TournamentDemoData.Running();
        var playable = t.Matches!
            .Where(m => MatchCards.For(m, Me, t.Entrants) == MatchCardState.Playable)
            .ToList();
        Assert.Single(playable);
        Assert.NotNull(playable[0].Entrant1Id);
        Assert.NotNull(playable[0].Entrant2Id);
    }

    [Fact]
    public void TheOpenRoomBelongsToSomebodyElse()
    {
        // If it were mine the card would say "back to the room" instead, and the state the
        // sample is meant to show — an opponent waiting for you — would be missing.
        var t = TournamentDemoData.Running();
        var withRoom = t.Matches!.Where(m => m.Lobby != null).ToList();
        Assert.Single(withRoom);
        Assert.NotEqual(Me, withRoom[0].Lobby!.HostUserId);
    }

    [Fact]
    public void TheByeIsARealOneAndAlreadyResolved()
    {
        var t = TournamentDemoData.Running();
        var bye = t.Matches!.Single(m => m.Status == "bye");
        Assert.Equal("bye", bye.Outcome);
        Assert.NotNull(bye.WinnerEntrantId);
        // A bye has exactly one side, or it is just a match somebody won.
        Assert.True(bye.Entrant1Id == null ^ bye.Entrant2Id == null);
    }

    // ---------------------------------------------------------------- teams

    [Fact]
    public void TheTeamScenarioIsActuallyATeamOne()
    {
        // The sides warning only renders for a non-1v1 format, and that sentence is the most
        // important thing on a team card: get the sides wrong in-game and the match does not
        // rate AND the bracket does not move.
        var t = TournamentDemoData.Teams();
        Assert.Equal("3v3", t.Format);
        Assert.All(t.Entrants!, e =>
        {
            Assert.Equal("team", e.Kind);
            Assert.Equal(3, e.MemberIds!.Count);
        });
    }

    [Fact]
    public void ONE_TEAM_IS_MINE_AND_ITS_CARD_CAN_BE_ACTED_ON()
    {
        // The sides warning renders only on a card you can act on, so if my team's match ever
        // stopped being one of those, the single most important sentence in a team tournament
        // would silently vanish from the preview.
        var t = TournamentDemoData.Teams();
        Assert.Single(t.Entrants!, e => e.MemberIds!.Contains(Me));

        var actionable = new[]
        {
            MatchCardState.Playable, MatchCardState.JoinRoom, MatchCardState.ReturnToRoom,
        };
        Assert.Contains(t.Matches!, m => actionable.Contains(MatchCards.For(m, Me, t.Entrants)));
    }

    [Fact]
    public void TeamNamesAreLongEnoughToTestTheTrimming()
    {
        // A 220 px card with three-letter names proves nothing. These are the widths a real
        // team name reaches.
        var t = TournamentDemoData.Teams();
        Assert.Contains(t.Entrants!, e => (e.DisplayName ?? "").Length >= 14);
    }

    // ---------------------------------------------------------------- registration

    [Fact]
    public void TheRegistrationScenarioHasNoBracketSoTheOtherHalfOfTheScreenIsShown()
    {
        // With no matches the detail pane falls through to the entrant list, which none of
        // the other three scenarios ever renders.
        var t = TournamentDemoData.Registration();
        Assert.Equal("registration", t.Status);
        Assert.Empty(t.Matches!);
        Assert.NotEmpty(t.Entrants!);
    }

    [Fact]
    public void TheRegistrationScenarioShowsEveryEntrantStatusThatMatters()
    {
        var t = TournamentDemoData.Registration();
        var statuses = t.Entrants!.Select(e => e.Status).Distinct().ToList();
        Assert.Contains("confirmed", statuses);
        Assert.Contains("waitlist", statuses);     // past capacity
        Assert.Contains("pending", statuses);      // approval mode: awaiting a decision
        Assert.Contains("withdrawn", statuses);    // dimmed by colour, not opacity
    }

    [Fact]
    public void IOwnTheRegistrationScenarioSoTheOwnerButtonsAreVisible()
    {
        // Owning it is what puts the owner strip on screen, which is the only place it can be
        // looked at. The buttons are made inert by the tab, not by hiding them here.
        var t = TournamentDemoData.Registration();
        Assert.True(TournamentPermissions.IsOwner(t, Me));
        Assert.True(TournamentPermissions.CanCloseRegistration(t, Me));
        Assert.True(TournamentPermissions.CanCancel(t, Me));

        // And a pending application must actually offer Accept / Reject.
        var pending = t.Entrants!.First(e => e.Status == "pending");
        Assert.True(TournamentPermissions.CanDecideEntrant(t, Me, pending));
    }

    [Fact]
    public void NoOtherScenarioIsOwnedByMe()
    {
        // The other three are meant to show an ENTRANT's view. If one of them became mine, its
        // owner strip would appear and the entrant's view would stop being previewable.
        foreach (var t in new[]
                 {
                     TournamentDemoData.Running(),
                     TournamentDemoData.Teams(),
                     TournamentDemoData.Finished(),
                 })
        {
            Assert.False(TournamentPermissions.IsOwner(t, Me), $"{t.Id} should not be mine");
        }
    }

    // ---------------------------------------------------------------- finished

    [Fact]
    public void TheFinishedScenarioCrownsSomebodyAndHasNothingLeftToPlay()
    {
        var t = TournamentDemoData.Finished();
        Assert.Equal("finished", t.Status);
        Assert.False(string.IsNullOrEmpty(t.WinnerEntrantId));
        Assert.NotNull(Entrant(t, t.WinnerEntrantId));
        Assert.DoesNotContain(t.Matches!, m => m.Status == "pending");
    }

    [Fact]
    public void TheFinishedScenarioShowsAWalkoverBesideAPlayedMatch()
    {
        // The outcome tag is the only thing that distinguishes "somebody won this" from
        // "nobody played this", so both have to be on screen at once to be comparable.
        var t = TournamentDemoData.Finished();
        var outcomes = t.Matches!.Select(m => m.Outcome).ToList();
        Assert.Contains("played", outcomes);
        Assert.Contains("walkover", outcomes);
    }

    [Fact]
    public void ILoseTheFinalOnPurpose()
    {
        // Winning it would show the bold, bright half of the card. Losing shows the DIMMED
        // half, which is the one that has to stay readable — and the one a mistake would make
        // unreadable without anybody noticing.
        var t = TournamentDemoData.Finished();
        var final = t.Matches!.Single(m => m.Round == t.RoundsTotal);
        var mine = t.Entrants!.Single(e => e.MemberIds!.Contains(Me));
        Assert.NotEqual(mine.Id, final.WinnerEntrantId);
        Assert.True(final.Entrant1Id == mine.Id || final.Entrant2Id == mine.Id);
    }

    // ---------------------------------------------------------------- coherence

    [Fact]
    public void EveryMatchPointsAtEntrantsThatExist()
    {
        // A dangling id renders as "TBD" and looks like a deliberate empty slot, so this is
        // exactly the kind of mistake a preview would hide rather than reveal.
        foreach (var t in TournamentDemoData.All())
        {
            var ids = new HashSet<string>(t.Entrants!.Select(e => e.Id));
            foreach (var m in t.Matches!)
            {
                foreach (var id in new[] { m.Entrant1Id, m.Entrant2Id, m.WinnerEntrantId })
                    if (!string.IsNullOrEmpty(id))
                        Assert.Contains(id!, ids);
            }
        }
    }

    [Fact]
    public void EveryDecidedMatchNamesAWinnerFromItsOwnTwoSides()
    {
        foreach (var t in TournamentDemoData.All())
        {
            foreach (var m in t.Matches!.Where(x => x.Status is "done" or "bye"))
            {
                Assert.False(string.IsNullOrEmpty(m.WinnerEntrantId),
                    $"{t.Id}/{m.Id} is settled but names no winner");
                Assert.True(m.WinnerEntrantId == m.Entrant1Id || m.WinnerEntrantId == m.Entrant2Id,
                    $"{t.Id}/{m.Id} was won by somebody who was not in it");
            }
        }
    }

    [Fact]
    public void EveryBracketLaysOutWithoutOverlappingItself()
    {
        // The layout is arithmetic, so a malformed fixture produces cards stacked on top of
        // each other rather than an exception.
        foreach (var t in TournamentDemoData.All().Where(x => x.Matches is { Count: > 0 }))
        {
            var grid = BracketLayout.Build(t.Matches);
            Assert.NotEmpty(grid.Columns);
            foreach (var col in grid.Columns)
            {
                var occupied = new HashSet<int>();
                foreach (var cell in col.Cells)
                {
                    for (int r = cell.RowStart; r < cell.RowStart + cell.RowSpan; r++)
                        Assert.True(occupied.Add(r), $"{t.Id} round {col.Round} overlaps at row {r}");
                }
            }
        }
    }
}
