using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Where the rank guide's two tabs come from (docs/design_guia_rangos_equipos, 53a/53b). Every
/// band is compared against <see cref="RankAges.BoundsFor"/> rather than written out, because the
/// handoff's 1st / 2nd / 3rd-4th is only an example over nine players and "no band by hand" is the
/// rule.
/// </summary>
public class RankGuideSourcesTests
{
    private const string Me = "me";

    private static LeaderboardRow Row(int rank, string id, string name)
        => new() { Rank = rank, UserId = id, DisplayName = name, DiscordUsername = name.ToLowerInvariant() };

    private static CommunityStats Stats(int teamPlayers = 9, bool withTeamLadder = true, int minDecided = 1)
    {
        var solo = Enumerable.Range(1, 14).Select(i => Row(i, "s" + i, "Solo" + i)).ToList();
        var team = Enumerable.Range(1, teamPlayers).Select(i => Row(i, i == 5 ? Me : "t" + i, "Team" + i)).ToList();
        return new CommunityStats
        {
            MinDecided = minDecided,
            Leaderboard = solo,
            RankedPlayers = 14,
            LeaderboardTeam = withTeamLadder ? team : null,
            RankedPlayersTeam = withTeamLadder ? teamPlayers : 0,
            // (Stats(teamPlayers: 0) is the deployed server today: an empty team table.)
        };
    }

    [Fact]
    public void TheTeamTabIsTheSameViewBuiltFromTheTeamList()
    {
        var standing = new EloSnapshot
        {
            Rating = 1388, LadderRank = 0, LadderSize = 14,
            LadderRankTeam = 5, LadderSizeTeam = 9, RatingTeam = 1455, RdTeam = 120, GamesPlayedTeam = 7,
        };
        var inputs = RankGuideSources.Gather(standing, Stats(), Me);

        Assert.NotNull(inputs.Team);
        var team = inputs.Team!.View;
        Assert.Equal(BadgeKind.Team, team.Ladder);
        Assert.Equal(BadgeKind.Solo, inputs.Solo.View.Ladder);
        Assert.Equal(5, team.MyPosition);
        Assert.Equal(9, team.LadderSize);
        Assert.Equal(RankAges.For(5, 9), team.MyAge);
        Assert.Equal(1455, inputs.Team.Rating);

        // Bands: exactly what the badges cut on a nine-player table, never a hand-written split.
        var bounds = RankAges.BoundsFor(9);
        var ladderAges = team.Ages.Where(a => a.Age != RankAge.Discovery).ToList();
        for (var i = 0; i < bounds.Length; i++)
            Assert.Equal(bounds[i], ladderAges[i].To);
        foreach (var a in ladderAges.Where(a => a.To >= a.From))
            for (var p = a.From; p <= a.To; p++)
                Assert.Equal(a.Age, RankAges.For(p, 9));

        // Names come from the TEAM list, not the 1v1 one.
        Assert.Contains("Team1", ladderAges[0].Holders);
        Assert.DoesNotContain(team.Ages.SelectMany(a => a.Holders), n => n.StartsWith("Solo"));
    }

    [Fact]
    public void TheStandingWinsAndTheTableIsOnlyAFallback()
    {
        // No standing at all: the viewer is found on the team page (5th), the size is the
        // server's count of the whole ladder, not the page length.
        var inputs = RankGuideSources.Gather(null, Stats(teamPlayers: 9), Me);
        Assert.Equal(5, inputs.Team!.View.MyPosition);
        Assert.Equal(9, inputs.Team.View.LadderSize);
        Assert.Null(inputs.Team.Rating);

        // The standing says 3rd of 12: that wins over the page.
        var standing = new EloSnapshot { LadderRankTeam = 3, LadderSizeTeam = 12 };
        var fromStanding = RankGuideSources.Gather(standing, Stats(teamPlayers: 9), Me);
        Assert.Equal(3, fromStanding.Team!.View.MyPosition);
        Assert.Equal(12, fromStanding.Team.View.LadderSize);
    }

    /// <summary>53b rule 2: no decided team match = the Discovery double shield, and NO row is
    /// lit — while the 1v1 guide keeps lighting Discovery as it always has.</summary>
    [Fact]
    public void ATeamDiscoveryLightsNoRow_WhileTheSoloOneStillDoes()
    {
        var standing = new EloSnapshot
        {
            LadderRank = 0, LadderSize = 14,
            LadderRankTeam = 0, LadderSizeTeam = 9, RatingTeam = 1500, RdTeam = 350, GamesPlayedTeam = 0,
        };
        var inputs = RankGuideSources.Gather(standing, Stats(), "nobody");

        var team = inputs.Team!.View;
        Assert.Equal(RankAge.Discovery, team.MyAge);
        Assert.Equal(RankGuideStepKind.PlayFirst, team.Next.Kind);
        Assert.DoesNotContain(team.Ages, a => a.IsMine);
        Assert.Null(inputs.Team.Rating);

        Assert.True(inputs.Solo.View.Ages.Single(a => a.Age == RankAge.Discovery).IsMine);
        Assert.Equal(1, inputs.EntryBar);
    }

    /// <summary>53b rule 3: a server with no team ladder (the team age is unknown) gives no Teams
    /// tab, and the 1v1 tab is what the guide always showed.</summary>
    [Fact]
    public void AnUnknownTeamAgeGivesNoTeamTab()
    {
        var oldServer = RankGuideSources.Gather(
            new EloSnapshot { Rating = 1400, LadderRank = 4, LadderSize = 14 }, Stats(withTeamLadder: false), Me);
        Assert.Null(oldServer.Team);
        Assert.Equal(4, oldServer.Solo.View.MyPosition);
        Assert.Equal(1400, oldServer.Solo.Rating);

        // Not on a PARTIAL team page and no standing to say otherwise: still unknown, never
        // Discovery — the viewer may be on the part of the table that was not sent.
        var partial = Stats();
        partial.RankedPlayersTeam = 60;
        Assert.Null(RankGuideSources.Gather(null, partial, "somebody-else").Team);

        // A count of 0 beside a non-empty list is a server that did not send the count.
        var noCount = Stats();
        noCount.RankedPlayersTeam = 0;
        Assert.Null(RankGuideSources.Gather(null, noCount, "somebody-else").Team);

        // Nobody signed in: there is no "me" to look for.
        Assert.Null(RankGuideSources.Gather(null, Stats(), null).Team);
    }

    /// <summary>
    /// A team page that holds the WHOLE team table proves the viewer is not on it: Discovery, not
    /// unknown. This is the deployed server's case today — <c>leaderboard_team: []</c>,
    /// <c>ranked_players_team: 0</c> and no team place in <c>/matches/elo</c> — and without it
    /// the Teams tab could not appear for anybody until the backend reports team places.
    /// </summary>
    [Fact]
    public void AWholeTeamTableWithoutTheViewerMeansDiscovery()
    {
        var empty = Stats(teamPlayers: 0);
        Assert.Empty(empty.LeaderboardTeam!);
        var fromEmpty = RankGuideSources.Gather(new EloSnapshot { LadderRank = 13, LadderSize = 16 }, empty, Me);
        Assert.NotNull(fromEmpty.Team);
        Assert.Equal(RankAge.Discovery, fromEmpty.Team!.View.MyAge);
        Assert.Equal(0, fromEmpty.Team.View.MyPosition);
        Assert.DoesNotContain(fromEmpty.Team.View.Ages, a => a.IsMine);
        Assert.Null(fromEmpty.Team.Rating);

        // A complete page of nine, counted nine by the server, without the viewer.
        var complete = RankGuideSources.Gather(null, Stats(teamPlayers: 9), "somebody-else");
        Assert.Equal(RankAge.Discovery, complete.Team!.View.MyAge);

        // The standing still wins when it speaks.
        var spoken = RankGuideSources.Gather(new EloSnapshot { LadderRankTeam = 2, LadderSizeTeam = 9 },
            Stats(teamPlayers: 0), Me);
        Assert.Equal(2, spoken.Team!.View.MyPosition);
    }

    [Fact]
    public void TheEntryBarIsOnlyTheServers()
    {
        Assert.Equal(3, RankGuideSources.Gather(null, Stats(minDecided: 3), Me).EntryBar);
        Assert.Null(RankGuideSources.Gather(null, Stats(minDecided: 0), Me).EntryBar);
        Assert.Null(RankGuideSources.Gather(null, null, Me).EntryBar);
    }
}
