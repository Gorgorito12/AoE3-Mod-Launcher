using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The bracket geometry, and what each card offers.
///
/// <para><b>The negative cases are the point.</b> A layout that is slightly off looks
/// wrong and somebody fixes it; a card that offers "Play my match" to a spectator, or
/// hides it from the person whose match it is, looks fine and is discovered by a player
/// during a tournament.</para>
/// </summary>
public class BracketLayoutTests
{
    private static TournamentMatch M(
        int round, int position, string? e1 = null, string? e2 = null,
        string status = "pending", string? winner = null, TournamentMatchLobby? lobby = null)
        => new()
        {
            Id = $"m{round}-{position}",
            Round = round,
            Position = position,
            Entrant1Id = e1,
            Entrant2Id = e2,
            Status = status,
            WinnerEntrantId = winner,
            Lobby = lobby,
        };

    private static TournamentEntrant E(string id, string status = "confirmed", params string[] members)
        => new() { Id = id, Status = status, MemberIds = members.ToList(), CaptainUserId = members.FirstOrDefault() };

    // ---------------------------------------------------------------- geometry

    [Fact]
    public void AnEmptyBracketDrawsNothing()
    {
        var grid = BracketLayout.Build(null);
        Assert.Empty(grid.Columns);
        Assert.Equal(0, grid.RowCount);

        Assert.Empty(BracketLayout.Build(new List<TournamentMatch>()).Columns);
    }

    [Fact]
    public void OneColumnPerRound()
    {
        var grid = BracketLayout.Build(new List<TournamentMatch>
        {
            M(1, 0), M(1, 1), M(1, 2), M(1, 3), M(2, 0), M(2, 1), M(3, 0),
        });

        Assert.Equal(3, grid.Columns.Count);
        Assert.Equal(new[] { 1, 2, 3 }, grid.Columns.Select(c => c.Round));
        Assert.Equal(new[] { 4, 2, 1 }, grid.Columns.Select(c => c.Cells.Count));
        Assert.Equal(4, grid.RowCount);
    }

    [Fact]
    public void EachCardIsCentredOnTheTwoThatFeedIt()
    {
        var grid = BracketLayout.Build(new List<TournamentMatch>
        {
            M(1, 0), M(1, 1), M(1, 2), M(1, 3), M(2, 0), M(2, 1), M(3, 0),
        });

        // Round 1: one row each, in order.
        Assert.Equal(new[] { 0, 1, 2, 3 }, grid.Columns[0].Cells.Select(c => c.RowStart));
        Assert.All(grid.Columns[0].Cells, c => Assert.Equal(1, c.RowSpan));

        // Round 2: two rows each, so each sits across the pair below it.
        Assert.Equal(new[] { 0, 2 }, grid.Columns[1].Cells.Select(c => c.RowStart));
        Assert.All(grid.Columns[1].Cells, c => Assert.Equal(2, c.RowSpan));

        // The final spans the lot.
        Assert.Equal(0, grid.Columns[2].Cells[0].RowStart);
        Assert.Equal(4, grid.Columns[2].Cells[0].RowSpan);
    }

    [Fact]
    public void ABracketWithByesStillLaysOutByFirstRoundSlots()
    {
        // Five entrants: an eight-slot bracket, so four first-round matches, three of them byes.
        var grid = BracketLayout.Build(new List<TournamentMatch>
        {
            M(1, 0, "e1", null, status: "bye", winner: "e1"),
            M(1, 1, "e4", "e5"),
            M(1, 2, "e2", null, status: "bye", winner: "e2"),
            M(1, 3, "e3", null, status: "bye", winner: "e3"),
            M(2, 0), M(2, 1), M(3, 0),
        });

        Assert.Equal(4, grid.RowCount);
        Assert.Equal(3, grid.Columns.Count);
        // A bye is a card like any other; hiding it would leave a gap nobody can explain.
        Assert.Equal(4, grid.Columns[0].Cells.Count);
    }

    [Fact]
    public void RoundsAreNamedWhereANameExists()
    {
        Assert.Equal("MpTournamentRoundFinal", BracketLayout.RoundLabelKey(3, 3));
        Assert.Equal("MpTournamentRoundSemi", BracketLayout.RoundLabelKey(2, 3));
        Assert.Equal("MpTournamentRoundQuarter", BracketLayout.RoundLabelKey(1, 3));
        // Deeper than a quarter-final, or a bracket whose size is not known yet.
        Assert.Equal("MpTournamentRoundN", BracketLayout.RoundLabelKey(1, 5));
        Assert.Equal("MpTournamentRoundN", BracketLayout.RoundLabelKey(2, null));
    }

    // ---------------------------------------------------------------- card state

    private static readonly List<TournamentEntrant> Roster = new()
    {
        E("e1", "confirmed", "me"),
        E("e2", "confirmed", "rival"),
        E("e3", "confirmed", "stranger"),
    };

    [Fact]
    public void MyMatchWithBothSidesKnownAndNoRoomIsPlayable()
    {
        var s = MatchCards.For(M(1, 0, "e1", "e2"), "me", Roster);
        Assert.Equal(MatchCardState.Playable, s);
    }

    [Fact]
    public void MyMatchWithNoOpponentYetOffersNothingToPlay()
    {
        Assert.Equal(MatchCardState.WaitingOpponent,
            MatchCards.For(M(2, 0, "e1", null), "me", Roster));
    }

    [Fact]
    public void AnOpenRoomIsJoinedOrReturnedToDependingOnWhoseItIs()
    {
        var mine = new TournamentMatchLobby { Id = "L1", HostUserId = "me", Status = "open" };
        var theirs = new TournamentMatchLobby { Id = "L1", HostUserId = "rival", Status = "open" };

        Assert.Equal(MatchCardState.ReturnToRoom,
            MatchCards.For(M(1, 0, "e1", "e2", lobby: mine), "me", Roster));
        Assert.Equal(MatchCardState.JoinRoom,
            MatchCards.For(M(1, 0, "e1", "e2", lobby: theirs), "me", Roster));
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_SomebodyElsesMatchIsNeverJoinable()
    {
        var theirs = new TournamentMatchLobby { Id = "L1", HostUserId = "rival", Status = "open" };

        // A room on a match I am not in reads as "being played", never as something to enter.
        Assert.Equal(MatchCardState.InProgress,
            MatchCards.For(M(1, 0, "e2", "e3", lobby: theirs), "me", Roster));
        Assert.Equal(MatchCardState.NotMine,
            MatchCards.For(M(1, 0, "e2", "e3"), "me", Roster));
    }

    /// <summary>
    /// THE ONE THAT MATTERS for supervising: running the tournament is a way to LOOK, never a
    /// way in — and it must not touch what anybody else sees.
    ///
    /// <para>The same match, asked twice. A plain viewer keeps the answer the test above
    /// pins, <c>InProgress</c>: a room they cannot enter, described rather than offered. The
    /// person running it gets <c>SuperviseRoom</c>, which is a different request — watching
    /// is not a seat, and they are the one who may have to settle this match by hand
    /// afterwards.</para>
    ///
    /// <para>And with no room open there is nothing to watch, so supervising changes nothing:
    /// the card stays <c>NotMine</c>. A "watch" button on a match nobody has started is a
    /// button onto an empty room.</para>
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_RunningItLetsYouWatchAndChangesNothingElse()
    {
        var theirs = new TournamentMatchLobby { Id = "L1", HostUserId = "rival", Status = "in_game" };
        var live = M(1, 0, "e2", "e3", lobby: theirs);

        // Unchanged for everybody who does not run it.
        Assert.Equal(MatchCardState.InProgress, MatchCards.For(live, "me", Roster));
        Assert.Equal(MatchCardState.InProgress,
            MatchCards.For(live, "me", Roster, canSupervise: false));

        Assert.Equal(MatchCardState.SuperviseRoom,
            MatchCards.For(live, "me", Roster, canSupervise: true));

        // No room, nothing to watch.
        Assert.Equal(MatchCardState.NotMine,
            MatchCards.For(M(1, 0, "e2", "e3"), "me", Roster, canSupervise: true));
    }

    /// <summary>
    /// An organiser who also entered still plays their own match.
    ///
    /// <para>The case that would quietly cost somebody their game: if supervising were checked
    /// before ownership of the match, the person running the tournament would be offered a
    /// window onto their OWN match instead of the button that opens it. Every one of the four
    /// my-match answers has to survive the flag.</para>
    /// </summary>
    [Fact]
    public void SupervisingNeverTakesOverMyOwnMatch()
    {
        var mine = new TournamentMatchLobby { Id = "L1", HostUserId = "me", Status = "open" };
        var theirs = new TournamentMatchLobby { Id = "L1", HostUserId = "rival", Status = "open" };

        Assert.Equal(MatchCardState.Playable,
            MatchCards.For(M(1, 0, "e1", "e2"), "me", Roster, canSupervise: true));
        Assert.Equal(MatchCardState.ReturnToRoom,
            MatchCards.For(M(1, 0, "e1", "e2", lobby: mine), "me", Roster, canSupervise: true));
        Assert.Equal(MatchCardState.JoinRoom,
            MatchCards.For(M(1, 0, "e1", "e2", lobby: theirs), "me", Roster, canSupervise: true));
        Assert.Equal(MatchCardState.WaitingOpponent,
            MatchCards.For(M(2, 0, "e1", null), "me", Roster, canSupervise: true));
    }

    /// <summary>A settled match offers nothing to watch either — it is over.</summary>
    [Fact]
    public void ASettledMatchIsNotWatchableEitherWay()
    {
        var theirs = new TournamentMatchLobby { Id = "L1", HostUserId = "rival", Status = "open" };
        Assert.Equal(MatchCardState.Done,
            MatchCards.For(M(1, 0, "e2", "e3", status: "done", winner: "e2", lobby: theirs),
                           "me", Roster, canSupervise: true));
        Assert.Equal(MatchCardState.Bye,
            MatchCards.For(M(1, 0, "e2", status: "bye"), "me", Roster, canSupervise: true));
    }

    [Fact]
    public void SignedOutOrWithNoRosterNothingIsMine()
    {
        Assert.Equal(MatchCardState.NotMine, MatchCards.For(M(1, 0, "e1", "e2"), null, Roster));
        Assert.Equal(MatchCardState.NotMine, MatchCards.For(M(1, 0, "e1", "e2"), "me", null));
    }

    [Fact]
    public void ASettledMatchOffersNothingWhoeverIsLooking()
    {
        foreach (var who in new[] { "me", "rival", "stranger", null })
        {
            Assert.Equal(MatchCardState.Done,
                MatchCards.For(M(1, 0, "e1", "e2", status: "done", winner: "e1"), who, Roster));
            Assert.Equal(MatchCardState.Bye,
                MatchCards.For(M(1, 0, "e1", null, status: "bye", winner: "e1"), who, Roster));
        }
    }

    [Fact]
    public void MembershipIsReadFromTheFrozenRoster()
    {
        // A team entrant whose saved team has since changed still plays with the people it
        // registered. The launcher only ever sees member_ids, which is the frozen copy.
        var teams = new List<TournamentEntrant>
        {
            E("t1", "confirmed", "me", "mate"),
            E("t2", "confirmed", "rival", "theirMate"),
        };
        Assert.Equal(MatchCardState.Playable, MatchCards.For(M(1, 0, "t1", "t2"), "me", teams));
        Assert.Equal(MatchCardState.Playable, MatchCards.For(M(1, 0, "t1", "t2"), "mate", teams));
        Assert.Equal(MatchCardState.NotMine, MatchCards.For(M(1, 0, "t1", "t2"), "outsider", teams));
    }

    // ------------------------------------------------------- what a settled side shows

    /// <summary>
    /// A match somebody played shows 1 and 0, one on each side — the handoff's notation, and
    /// what makes a decided card readable at a glance instead of half filled in.
    /// </summary>
    [Fact]
    public void APlayedMatchPutsAFigureOnBothSides()
    {
        Assert.Equal(SideMarker.One,
            BracketLayout.MarkerFor(bye: false, decided: true, won: true, outcome: "played"));
        Assert.Equal(SideMarker.Zero,
            BracketLayout.LoserMarkerFor(decided: true, known: true, outcome: "played"));
    }

    /// <summary>
    /// THE ONE THAT MATTERS. Nobody played a walkover or a disqualification, so neither side
    /// gets a figure: the winner keeps its tag and the loser's edge stays empty.
    ///
    /// <para>"1 - 0" there would describe a game that never happened, on a launcher whose wire
    /// carries no score at all. This is the half of the reference that was deliberately NOT
    /// adopted, and it is the only place that says so in code rather than in a comment.</para>
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_NobodyPlayedSoNobodyGetsAFigure()
    {
        Assert.Equal(SideMarker.WalkoverTag,
            BracketLayout.MarkerFor(bye: false, decided: true, won: true, outcome: "walkover"));
        Assert.Equal(SideMarker.None,
            BracketLayout.LoserMarkerFor(decided: true, known: true, outcome: "walkover"));

        Assert.Equal(SideMarker.DqTag,
            BracketLayout.MarkerFor(bye: false, decided: true, won: true, outcome: "dq"));
        Assert.Equal(SideMarker.None,
            BracketLayout.LoserMarkerFor(decided: true, known: true, outcome: "dq"));
    }

    /// <summary>A bye draws one side and it says so; there is no second side to mark.</summary>
    [Fact]
    public void AByeIsATagAndNotAWin()
    {
        Assert.Equal(SideMarker.ByeTag,
            BracketLayout.MarkerFor(bye: true, decided: true, won: true, outcome: "bye"));
        Assert.Equal(SideMarker.ByeTag,
            BracketLayout.MarkerFor(bye: true, decided: false, won: false, outcome: null));
    }

    /// <summary>
    /// Nothing has been settled yet, or the slot has no occupant. Both sides stay blank — a 0
    /// on an undecided match would say it had been lost.
    /// </summary>
    [Fact]
    public void AnUndecidedOrEmptySideIsBlank()
    {
        Assert.Equal(SideMarker.None,
            BracketLayout.MarkerFor(bye: false, decided: false, won: false, outcome: null));
        Assert.Equal(SideMarker.None,
            BracketLayout.LoserMarkerFor(decided: false, known: true, outcome: "played"));
        // Decided, but this side of it was never filled in.
        Assert.Equal(SideMarker.None,
            BracketLayout.LoserMarkerFor(decided: true, known: false, outcome: "played"));
    }
}
