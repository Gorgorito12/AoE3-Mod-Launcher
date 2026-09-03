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
}
