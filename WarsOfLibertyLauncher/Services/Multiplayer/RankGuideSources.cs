using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>One tab of the rank guide: the view, and the rating its header prints (null = none).</summary>
public sealed record RankGuideSide(RankGuideView View, double? Rating);

/// <param name="Solo">The 1v1 tab — always present, exactly what the guide showed before 53.</param>
/// <param name="Team">The Teams tab, or null when the viewer's team age is unknown (a server
/// with no team ladder, or one that did not say): then there is no selector and the guide stays
/// as it was (docs/design_guia_rangos_equipos, 53b rule 3).</param>
/// <param name="EntryBar">The ladder's entry bar as the server states it, or null when it did
/// not — the Teams notice quotes it, and never a number of its own.</param>
public sealed record RankGuideInputs(RankGuideSide Solo, RankGuideSide? Team, int? EntryBar);

/// <summary>
/// Everything the rank guide states, gathered from the two payloads the launcher already holds
/// — the viewer's standing (<c>GET /matches/elo</c>) and the community stats
/// (<c>GET /stats/community</c>) — with no WPF in it, so it is tested rather than trusted.
///
/// <para><b>Nothing is invented.</b> The team place, the team ladder's size and the names by
/// place are the same fields the account chip and the Ranking's TEAMS tab already read
/// (<c>ladder_rank_team</c>, <c>ladder_size_team</c> / <c>ranked_players_team</c>,
/// <c>leaderboard_team</c>). The standing wins over the table, which is only a page of it and
/// may not contain the viewer at all.</para>
///
/// <para><b>An unknown team age means no Teams tab</b> — the rule <see cref="RankBadgeChoice"/>
/// follows: not knowing is not Discovery, and a tab that guessed would tell a player on the
/// team table that they are not on it.</para>
/// </summary>
public static class RankGuideSources
{
    public static RankGuideInputs Gather(EloSnapshot? standing, CommunityStats? stats, string? myUserId)
    {
        // ── 1v1: unchanged from what ShowRankGuide did before 53 ──
        var soloRows = CommunityStatsView.Rows(stats);
        int? soloRank = standing?.LadderRank;
        if (soloRank == null && PlaceOf(soloRows, myUserId) is > 0 and var fromTable) soloRank = fromTable;
        var soloSize = standing?.LadderSize is > 0 and var s ? s : CommunityStatsView.RankedPlayers(stats, team: false);
        var soloView = RankGuideView.Build(soloRank, soloSize, NamesByPlace(soloRows));
        double? soloRating = RatingDisplay.ShouldShow(standing?.Rating) ? standing!.Rating : null;
        var solo = new RankGuideSide(soloView, soloRating);

        // ── Teams ──
        var teamList = CommunityStatsView.TeamRows(stats);
        var teamRows = teamList ?? new List<LeaderboardRow>();
        var teamCount = CommunityStatsView.RankedPlayers(stats, team: true);
        int? teamRank = standing?.LadderRankTeam;
        if (teamRank == null && PlaceOf(teamRows, myUserId) is > 0 and var fromTeamTable) teamRank = fromTeamTable;
        else if (teamRank == null && !string.IsNullOrEmpty(myUserId) && HoldsTheWholeLadder(teamList, teamCount))
            teamRank = 0;   // the whole team table is here and the viewer is not on it: Discovery
        var teamSize = standing?.LadderSizeTeam is > 0 and var ts ? ts : teamCount;
        var teamAge = RankAges.ForOptional(teamRank, teamSize > 0 ? teamSize : null);

        RankGuideSide? team = null;
        if (teamAge is { } age)
        {
            var teamView = RankGuideView.Build(teamRank, teamSize, NamesByPlace(teamRows), BadgeKind.Team);
            // The team rating only once there is one to speak of: a viewer with no decided team
            // match is Discovery, and the 1500 the server would hand back is a placeholder.
            double? teamRating = age != RankAge.Discovery
                                 && RatingDisplay.ShouldShow(standing?.RatingTeam)
                                 && !RatingDisplay.IsUnrated(standing?.RdTeam, standing?.GamesPlayedTeam)
                ? standing!.RatingTeam
                : null;
            team = new RankGuideSide(teamView, teamRating);
        }

        return new RankGuideInputs(solo, team, CommunityStatsView.RequiredDecided(stats));
    }

    /// <summary>
    /// Whether a ladder page IS the whole ladder, so that somebody missing from it is provably
    /// not on it. Two cases, and neither is a guess: a list the server sent EMPTY (a ladder with
    /// anybody on it fills at least one row of any page), and a page holding as many rows as the
    /// server counts on that ladder. A null list is an older server — it says nothing — and a
    /// count of 0 beside a non-empty list is a server that did not send the count.
    ///
    /// <para>This is what lets the Teams tab exist against a server that reports the team table
    /// but not, yet, the viewer's own team place: on the deployed backend the table is empty, so
    /// every viewer is in Discovery there — the very case 53b rule 2 describes.</para>
    /// </summary>
    private static bool HoldsTheWholeLadder(IReadOnlyList<LeaderboardRow>? list, int countOnLadder)
        => list != null && (list.Count == 0 || (countOnLadder > 0 && list.Count >= countOnLadder));

    /// <summary>The viewer's place in a ladder page, or 0 when they are not on it. The rank is
    /// the SERVER's — this only looks the player up, it never counts rows.</summary>
    private static int PlaceOf(IReadOnlyList<LeaderboardRow> rows, string? myUserId)
    {
        if (string.IsNullOrEmpty(myUserId)) return 0;
        foreach (var row in rows)
            if (string.Equals(row.UserId, myUserId, StringComparison.Ordinal)) return row.Rank;
        return 0;
    }

    private static IReadOnlyDictionary<int, string> NamesByPlace(IReadOnlyList<LeaderboardRow> rows)
        => rows
            .GroupBy(r => r.Rank)
            .ToDictionary(g => g.Key, g =>
            {
                var r = g.First();
                return string.IsNullOrEmpty(r.DisplayName) ? r.DiscordUsername : r.DisplayName;
            });
}
