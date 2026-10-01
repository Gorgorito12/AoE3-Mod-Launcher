using System.Collections.Generic;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="MatchResultResolver"/> — who gets credited with winning.
///
/// <para>This is the only code in the launcher where being wrong takes rating points off one real
/// person and gives them to another, and until now it had no tests at all. The refusals are half
/// the point: a result that cannot be established is reported as 0.5 for everyone, which the
/// backend and <see cref="PlayerStanding"/> both read as "not known" rather than "draw".</para>
/// </summary>
public class MatchResultResolverTests
{
    private const string Host = "host-user-id";
    private const string Rival = "rival-user-id";

    private static List<string> Duel() => new() { Host, Rival };

    [Fact]
    public void AClean1v1WithTheHostPresent_CarriesTheResult()
    {
        Assert.Equal(1.0, MatchResultResolver.ResolveHostResult(1.0, Duel(), Host).Result);
        Assert.Equal(0.0, MatchResultResolver.ResolveHostResult(0.0, Duel(), Host).Result);
    }

    [Fact]
    public void NoResultInTheRecording_IsNotKnown()
        => Assert.Null(MatchResultResolver.ResolveHostResult(null, Duel(), Host).Result);

    /// <summary>
    /// The recording names one loser. In a team game that says nothing about the other three
    /// players, so the whole match has to go down as unknown rather than have three scores
    /// invented around one real one.
    /// </summary>
    [Fact]
    public void MoreThanTwoParticipants_IsNotKnown()
    {
        var teamGame = new List<string> { Host, Rival, "third", "fourth" };

        Assert.Null(MatchResultResolver.ResolveHostResult(1.0, teamGame, Host).Result);
    }

    [Fact]
    public void FewerThanTwoParticipants_IsNotKnown()
    {
        Assert.Null(MatchResultResolver.ResolveHostResult(1.0, new List<string> { Host }, Host).Result);
        Assert.Null(MatchResultResolver.ResolveHostResult(1.0, new List<string>(), Host).Result);
        Assert.Null(MatchResultResolver.ResolveHostResult(1.0, null, Host).Result);
    }

    /// <summary>
    /// The reporter is the player whose recording was read. If they are not among the people being
    /// reported, the room's roster and the file disagree — so nothing here can be trusted to name
    /// the other player either.
    /// </summary>
    [Fact]
    public void HostMissingFromTheParticipants_IsNotKnown()
    {
        var strangers = new List<string> { "someone", Rival };

        Assert.Null(MatchResultResolver.ResolveHostResult(1.0, strangers, Host).Result);
    }

    [Fact]
    public void NoHostId_IsNotKnown()
    {
        Assert.Null(MatchResultResolver.ResolveHostResult(1.0, Duel(), null).Result);
        Assert.Null(MatchResultResolver.ResolveHostResult(1.0, Duel(), "").Result);
    }

    /// <summary>
    /// Every refusal has to name its cause. Before this, a match that silently went down as a draw
    /// looked identical whether it was skipped, refused or had simply never recorded — which made
    /// "my game didn't count" undiagnosable from a log.
    /// </summary>
    [Fact]
    public void EveryRefusalNamesItsReason()
    {
        Assert.Contains("recording", MatchResultResolver.ResolveHostResult(null, Duel(), Host).Reason);
        Assert.Contains("not 2", MatchResultResolver
            .ResolveHostResult(1.0, new List<string> { Host }, Host).Reason);
        Assert.Contains("host", MatchResultResolver
            .ResolveHostResult(1.0, new List<string> { "someone", Rival }, Host).Reason);
        Assert.NotEmpty(MatchResultResolver.ResolveHostResult(1.0, Duel(), Host).Reason);
    }

    [Fact]
    public void TheOpponentGetsTheMirrorImage()
    {
        Assert.Equal(0.0, MatchResultResolver.ParticipantResult(1.0, isHost: false));
        Assert.Equal(1.0, MatchResultResolver.ParticipantResult(0.0, isHost: false));
        Assert.Equal(0.5, MatchResultResolver.ParticipantResult(0.5, isHost: false));
        Assert.Equal(1.0, MatchResultResolver.ParticipantResult(1.0, isHost: true));
    }

    /// <summary>
    /// THE ONE THAT MATTERS. The backend rejects a report whose scores don't sum to half the
    /// player count, and Glicko takes them at face value — so if this ever goes red, either every
    /// match is refused or two players are credited for the same win.
    /// </summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(0.0)]
    [InlineData(0.5)]
    public void TheTwoScoresAlwaysSumToOne(double hostResult)
    {
        var host = MatchResultResolver.ParticipantResult(hostResult, isHost: true);
        var rival = MatchResultResolver.ParticipantResult(hostResult, isHost: false);

        Assert.Equal(1.0, host + rival, precision: 10);
    }

    // -----------------------------------------------------------------------
    // Team matches: the side every member of which resigned
    // -----------------------------------------------------------------------
    //
    // The refusals are the point again, and more so here: a wrong side takes points from
    // three or five people at once, and null only leaves the match where every team match
    // already was — reported 0.5 across the board.

    private static ReplayParserService.ReplayPlayer P(int slot, string name, int team, bool human = true)
        => new(slot, name, 0, team, human ? ReplayParserService.SlotTypeHuman : 4u);

    /// <summary>A resign record. Sent by its own target unless told otherwise.</summary>
    private static ReplayParserService.ResignRecord R(int target, int? sender = null)
        => new(0, sender ?? target, target);

    /// <summary>A 2v2: slots 1+2 against 3+4, with the names each account published.</summary>
    private static (Dictionary<string, string> Names, List<ReplayParserService.ReplayPlayer> Players) TwoVTwo()
        => (
            new Dictionary<string, string> { ["a1"] = "Ana", ["a2"] = "Abel", ["b1"] = "Bea", ["b2"] = "Beto" },
            new List<ReplayParserService.ReplayPlayer>
            {
                P(1, "Ana", 1), P(2, "Abel", 1), P(3, "Bea", 2), P(4, "Beto", 2),
            });

    [Fact]
    public void TheSideEveryMemberOfWhichResignedLoses()
    {
        var (names, players) = TwoVTwo();

        // Bea resigned first, Beto last. The outcome block is only Beto's record: on its own it
        // names one casualty, and it is the side that makes it a result.
        var r = MatchResultResolver.ResolveTeamResults(names, players, new[] { R(3), R(4) }, loserSlot: 4);

        Assert.NotNull(r);
        Assert.Equal(1.0, r!["a1"]);
        Assert.Equal(1.0, r["a2"]);
        Assert.Equal(0.0, r["b1"]);
        Assert.Equal(0.0, r["b2"]);
    }

    [Fact]
    public void AWinnerWhoFellFirstStillWins_WhenHisPartnerFinishedIt()
    {
        // The case a "last casualty wins" or "all casualties on one side" rule gets wrong: Ana
        // went down first and Abel carried the game. Her side is not complete, theirs is.
        var (names, players) = TwoVTwo();

        var r = MatchResultResolver.ResolveTeamResults(
            names, players, new[] { R(1), R(3), R(4) }, loserSlot: 4)!;

        Assert.Equal(1.0, r["a1"]);
        Assert.Equal(1.0, r["a2"]);
        Assert.Equal(0.0, r["b1"]);
        Assert.Equal(0.0, r["b2"]);
    }

    [Fact]
    public void ARemovalCountsAgainstItsTarget_WhoeverSentIt()
    {
        // The shape of the measured ESOC_Iowa file: slot 4 removed BOTH dropped opponents, so
        // neither record was sent by the player it put out. The target loses, exactly as the 1v1
        // trailer has always treated a drop.
        var players = new List<ReplayParserService.ReplayPlayer>
        {
            P(1, "Gommiustan", 0), P(2, "Kanchay", 1), P(3, "Jeops", 0), P(4, "Geaf_Argento", 1),
        };

        var d = MatchResultResolver.ResolveTeamResultsBySlot(
            players, new[] { R(3, sender: 4), R(1, sender: 4) }, trailerLoserSlot: 1);

        Assert.NotNull(d.ScoresBySlot);
        Assert.Equal(0.0, d.ScoresBySlot![1]);
        Assert.Equal(0.0, d.ScoresBySlot[3]);
        Assert.Equal(1.0, d.ScoresBySlot[2]);
        Assert.Equal(1.0, d.ScoresBySlot[4]);
    }

    [Fact]
    public void TheHostsOwnTeammateIsNeverMarkedALoser()
    {
        // The bug this exists to prevent. ParticipantResult mirrors the host's score onto
        // everyone else (1.0 - x), which is right for a 1v1 and puts the host's PARTNER on
        // the losing side of a 2v2 the host won.
        var (names, players) = TwoVTwo();
        var r = MatchResultResolver.ResolveTeamResults(names, players, new[] { R(3), R(4) }, loserSlot: 4)!;

        Assert.Equal(r["a1"], r["a2"]);
        Assert.Equal(r["b1"], r["b2"]);
    }

    [Fact]
    public void TheScoresSumToHalfThePlayerCount()
    {
        // Exactly what the backend validates (`sum <= N/2`), and the team generalisation of
        // TheTwoScoresAlwaysSumToOne — for either side losing.
        var (names, players) = TwoVTwo();
        foreach (var losers in new[] { new[] { R(1), R(2) }, new[] { R(3), R(4) } })
        {
            var r = MatchResultResolver.ResolveTeamResults(names, players, losers, losers[^1].Target)!;
            var sum = 0.0;
            foreach (var v in r.Values) sum += v;
            Assert.Equal(2.0, sum);
        }
    }

    [Fact]
    public void AThreeVThreeReadsTheSameWay()
    {
        var names = new Dictionary<string, string>
        { ["a1"] = "A1", ["a2"] = "A2", ["a3"] = "A3", ["b1"] = "B1", ["b2"] = "B2", ["b3"] = "B3" };
        var players = new List<ReplayParserService.ReplayPlayer>
        {
            P(1, "A1", 1), P(2, "A2", 1), P(3, "A3", 1),
            P(4, "B1", 2), P(5, "B2", 2), P(6, "B3", 2),
        };

        var r = MatchResultResolver.ResolveTeamResults(
            names, players, new[] { R(2), R(1), R(3) }, loserSlot: 3, perSide: 3)!;
        Assert.Equal(0.0, r["a1"]);
        Assert.Equal(0.0, r["a3"]);
        Assert.Equal(1.0, r["b2"]);
    }

    [Fact]
    public void APartialCopyFallsBackToTheOutcomeBlocksSide()
    {
        // The copy of a player who closed his game right after resigning holds his own record
        // and nobody else's. No side is complete, so the decision is the rule team matches used
        // before resign records were read: the side the outcome block names lost.
        var (_, players) = TwoVTwo();

        var d = MatchResultResolver.ResolveTeamResultsBySlot(players, new[] { R(3) }, trailerLoserSlot: 3);

        Assert.NotNull(d.ScoresBySlot);
        Assert.Equal(0.0, d.ScoresBySlot![3]);
        Assert.Equal(0.0, d.ScoresBySlot[4]);
        Assert.Equal(1.0, d.ScoresBySlot[1]);
        Assert.Equal(1.0, d.ScoresBySlot[2]);
    }

    [Fact]
    public void TheSlotDecisionNeedsNoNamesAtAll()
    {
        // What a CONFIRMATION sends: our own score from our own slot. It must not depend on
        // anybody else's published name — the room loses those in exactly the matches that need
        // confirming, which is how the first 2v2s confirmed nothing but 0.5.
        var (_, players) = TwoVTwo();

        var d = MatchResultResolver.ResolveTeamResultsBySlot(players, new[] { R(3), R(4) }, trailerLoserSlot: 4);

        Assert.Equal(1.0, d.ScoresBySlot![1]);
        Assert.Equal(0.0, d.ScoresBySlot[4]);
    }

    // ---------------- the refusals ----------------

    [Fact]
    public void BothSidesComplete_IsRefused()
    {
        // No game that ended can say this, so the file is not one this reading understands.
        var (_, players) = TwoVTwo();
        var d = MatchResultResolver.ResolveTeamResultsBySlot(
            players, new[] { R(1), R(2), R(3), R(4) }, trailerLoserSlot: 4);
        Assert.Null(d.ScoresBySlot);
    }

    [Fact]
    public void NoSideCompleteAndNoOutcomeBlock_IsRefused()
    {
        var (_, players) = TwoVTwo();
        Assert.Null(MatchResultResolver.ResolveTeamResultsBySlot(players, new[] { R(3) }, -1).ScoresBySlot);
        Assert.Null(MatchResultResolver.ResolveTeamResultsBySlot(players, null, -1).ScoresBySlot);
    }

    [Fact]
    public void AnOutcomeBlockNamingTheOtherSide_IsRefused()
    {
        // The file's two records of the ending disagree: side 2 resigned whole, and the block
        // says slot 1 was the last casualty. Neither may be believed over the other.
        var (_, players) = TwoVTwo();
        var d = MatchResultResolver.ResolveTeamResultsBySlot(players, new[] { R(3), R(4) }, trailerLoserSlot: 1);
        Assert.Null(d.ScoresBySlot);
    }

    [Fact]
    public void EveryUnestablishedSideIsRefused()
    {
        var all = new[] { R(3), R(4) };

        // Three sides: naming one loser leaves two possible winners.
        Assert.Null(MatchResultResolver.ResolveTeamResultsBySlot(
            new[] { P(1, "A", 0), P(2, "B", 1), P(3, "C", 2), P(4, "D", 2) }, all, 4).ScoresBySlot);
        // Two sides of unequal size — a room that promised 2v2 and was played 1v3.
        Assert.Null(MatchResultResolver.ResolveTeamResultsBySlot(
            new[] { P(1, "A", 0), P(2, "B", 1), P(3, "C", 1), P(4, "D", 1) }, all, 4).ScoresBySlot);
        // Sides of one: a 1v1, which ResolveHostResult decides and this must not.
        Assert.Null(MatchResultResolver.ResolveTeamResultsBySlot(
            new[] { P(1, "A", 0), P(2, "B", 1) }, new[] { R(2) }, 2).ScoresBySlot);
        // A side nobody knows.
        Assert.Null(MatchResultResolver.ResolveTeamResultsBySlot(
            new[] { P(1, "A", 0), P(2, "B", -1), P(3, "C", 1), P(4, "D", 1) }, all, 4).ScoresBySlot);
        // Sides of the wrong size for what the room declared.
        var (_, players) = TwoVTwo();
        Assert.Null(MatchResultResolver.ResolveTeamResultsBySlot(players, all, 4, perSide: 3).ScoresBySlot);
        // An outcome block naming a slot the game does not have.
        Assert.Null(MatchResultResolver.ResolveTeamResultsBySlot(players, new[] { R(3) }, 7).ScoresBySlot);
        // Nothing to read.
        Assert.Null(MatchResultResolver.ResolveTeamResultsBySlot(null, all, 4).ScoresBySlot);
    }

    [Fact]
    public void ASkirmishIsNotAMatch_WhoeverResigned()
    {
        var withAi = new List<ReplayParserService.ReplayPlayer>
        {
            P(1, "Ana", 1), P(2, "Abel", 1), P(3, "Bea", 2), P(4, "Beto", 2, human: false),
        };
        Assert.Null(MatchResultResolver.ResolveTeamResultsBySlot(withAi, new[] { R(3), R(4) }, 4).ScoresBySlot);
    }

    [Fact]
    public void TheAccountJoinIsAllOrNothing()
    {
        // The file decides — and still nobody's score is reported when one account cannot be
        // placed in a slot, because a result written against the wrong account takes points
        // from somebody who was not there.
        var (names, players) = TwoVTwo();
        names.Remove("b2");

        Assert.Null(MatchResultResolver.ResolveTeamResults(names, players, new[] { R(3), R(4) }, 4));
    }

    [Fact]
    public void ALoserNobodyInTheRoomClaimsIsRefused()
    {
        // The recording is of some other game, or of these people under names they never
        // published. Either way there is no account to credit.
        var (_, players) = TwoVTwo();
        var strangers = new Dictionary<string, string>
        { ["a1"] = "Zoe", ["a2"] = "Yago", ["b1"] = "Xime", ["b2"] = "Wal" };

        Assert.Null(MatchResultResolver.ResolveTeamResults(strangers, players, new[] { R(3), R(4) }, 4));
    }
}
