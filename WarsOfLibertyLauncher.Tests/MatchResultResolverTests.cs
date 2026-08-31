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
    // Team matches: naming one loser names a whole SIDE
    // -----------------------------------------------------------------------
    //
    // The refusals are the point again, and more so here: a wrong side takes points from
    // three or five people at once, and null only leaves the match where every team match
    // already was — reported 0.5 across the board.

    private static ReplayParserService.ReplayPlayer P(int slot, string name, int team, bool human = true)
        => new(slot, name, 0, team, human ? ReplayParserService.SlotTypeHuman : 4u);

    /// <summary>A 2v2: slots 1+2 against 3+4, joined to accounts by profile name.</summary>
    private static (Dictionary<string, int> Teams,
                    Dictionary<string, string> Names,
                    List<ReplayParserService.ReplayPlayer> Players) TwoVTwo()
        => (
            new Dictionary<string, int> { ["a1"] = 0, ["a2"] = 0, ["b1"] = 1, ["b2"] = 1 },
            new Dictionary<string, string> { ["a1"] = "Ana", ["a2"] = "Abel", ["b1"] = "Bea", ["b2"] = "Beto" },
            new List<ReplayParserService.ReplayPlayer>
            {
                P(1, "Ana", 1), P(2, "Abel", 1), P(3, "Bea", 2), P(4, "Beto", 2),
            });

    [Fact]
    public void TheLosersWholeSideLoses_AndTheOtherSideWins()
    {
        var (teams, names, players) = TwoVTwo();

        // The trailer named slot 3 — Bea. That is not just Bea's defeat: it is her side's.
        var r = MatchResultResolver.ResolveTeamResults(teams, names, players, loserSlot: 3);

        Assert.NotNull(r);
        Assert.Equal(1.0, r!["a1"]);
        Assert.Equal(1.0, r["a2"]);
        Assert.Equal(0.0, r["b1"]);
        Assert.Equal(0.0, r["b2"]);
    }

    [Fact]
    public void TheHostsOwnTeammateIsNeverMarkedALoser()
    {
        // The bug this exists to prevent. ParticipantResult mirrors the host's score onto
        // everyone else (1.0 - x), which is right for a 1v1 and puts the host's PARTNER on
        // the losing side of a 2v2 the host won.
        var (teams, names, players) = TwoVTwo();
        var r = MatchResultResolver.ResolveTeamResults(teams, names, players, loserSlot: 3)!;

        Assert.Equal(r["a1"], r["a2"]);
        Assert.Equal(r["b1"], r["b2"]);
    }

    [Fact]
    public void TheScoresSumToHalfThePlayerCount()
    {
        // Exactly what the backend validates (`sum <= N/2`), and the team generalisation of
        // TheTwoScoresAlwaysSumToOne.
        foreach (var loser in new[] { 1, 2, 3, 4 })
        {
            var (teams, names, players) = TwoVTwo();
            var r = MatchResultResolver.ResolveTeamResults(teams, names, players, loser)!;
            var sum = 0.0;
            foreach (var v in r.Values) sum += v;
            Assert.Equal(2.0, sum);
        }
    }

    [Fact]
    public void AThreeVThreeReadsTheSameWay()
    {
        var teams = new Dictionary<string, int>
        { ["a1"] = 0, ["a2"] = 0, ["a3"] = 0, ["b1"] = 1, ["b2"] = 1, ["b3"] = 1 };
        var names = new Dictionary<string, string>
        { ["a1"] = "A1", ["a2"] = "A2", ["a3"] = "A3", ["b1"] = "B1", ["b2"] = "B2", ["b3"] = "B3" };
        var players = new List<ReplayParserService.ReplayPlayer>
        {
            P(1, "A1", 1), P(2, "A2", 1), P(3, "A3", 1),
            P(4, "B1", 2), P(5, "B2", 2), P(6, "B3", 2),
        };

        var r = MatchResultResolver.ResolveTeamResults(teams, names, players, loserSlot: 1)!;
        Assert.Equal(0.0, r["a1"]);
        Assert.Equal(0.0, r["a3"]);
        Assert.Equal(1.0, r["b2"]);
    }

    [Fact]
    public void EveryUnestablishedSideIsRefused()
    {
        var (teams, names, players) = TwoVTwo();

        // No trailer named anybody.
        Assert.Null(MatchResultResolver.ResolveTeamResults(teams, names, players, -1));
        // A slot the recording does not contain.
        Assert.Null(MatchResultResolver.ResolveTeamResults(teams, names, players, 7));
        // Nothing to join the file to the accounts with.
        Assert.Null(MatchResultResolver.ResolveTeamResults(teams, null, players, 3));
        Assert.Null(MatchResultResolver.ResolveTeamResults(null, names, players, 3));
        Assert.Null(MatchResultResolver.ResolveTeamResults(teams, names, null, 3));

        // Three sides: naming one loser leaves two possible winners, so it names nothing.
        var ffa = new Dictionary<string, int> { ["a1"] = 0, ["a2"] = 1, ["b1"] = 2, ["b2"] = 2 };
        Assert.Null(MatchResultResolver.ResolveTeamResults(ffa, names, players, 3));

        // Two sides of unequal size — a room that promised 2v2 and was played 1v3.
        var lopsided = new Dictionary<string, int> { ["a1"] = 0, ["a2"] = 1, ["b1"] = 1, ["b2"] = 1 };
        Assert.Null(MatchResultResolver.ResolveTeamResults(lopsided, names, players, 3));
    }

    [Fact]
    public void ASkirmishIsNotAMatch_WhoeverTheTrailerSaysLost()
    {
        // ReadOutcome makes this refusal for a 1v1 and CANNOT make it here: it hands back
        // the loser slot before it ever looks at who is human, once there are more than two.
        var (teams, names, _) = TwoVTwo();
        var withAi = new List<ReplayParserService.ReplayPlayer>
        {
            P(1, "Ana", 1), P(2, "Abel", 1), P(3, "Bea", 2), P(4, "Beto", 2, human: false),
        };

        Assert.Null(MatchResultResolver.ResolveTeamResults(teams, names, withAi, loserSlot: 3));
    }

    [Fact]
    public void ALoserNobodyInTheRoomClaimsIsRefused()
    {
        // The recording is of some other game, or of these people under names they never
        // published. Either way there is no side to lose.
        var (teams, _, players) = TwoVTwo();
        var strangers = new Dictionary<string, string>
        { ["a1"] = "Zoe", ["a2"] = "Yago", ["b1"] = "Xime", ["b2"] = "Wal" };

        Assert.Null(MatchResultResolver.ResolveTeamResults(teams, strangers, players, loserSlot: 3));
    }
}
