using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>The scenes of the rating preview (design handoff 55): one per screen of the handoff.</summary>
internal enum EloPreviewScene
{
    /// <summary>55a: the ranking, ranked rows and placement rows, streaks, an inactive player.</summary>
    Ranking,

    /// <summary>55c: nobody has finished placement yet — the box, then the placement rows.</summary>
    Placement,

    /// <summary>55d-55f: a sample player's profile — one card per mode, head-to-head.</summary>
    Profile,

    /// <summary>55g: a 1v1 room with the server's win probability.</summary>
    Room1v1,

    /// <summary>55h: a team room — two columns, NO TEAM, the 1|2 pickers, the Start gate.</summary>
    RoomTeams,

    /// <summary>55i: the team countdown with the sides written out.</summary>
    Countdown,

    /// <summary>55j: the result card in each of its cases.</summary>
    Result,

    /// <summary>55k: the History with anti-farm, unrated and tournament rows.</summary>
    History,

    /// <summary>60: the community block under the rooms list — its data strip and the cards.</summary>
    Highlights,

    /// <summary>Ranking › Highlights: the month's top five of every highlight.</summary>
    RankingHighlights,

    /// <summary>55n: a points refund — the bell and the profile banner.</summary>
    Refund,
}

/// <summary>The sample player whose profile the preview opens, with everything the page reads.</summary>
internal sealed record EloProfileSample(
    LobbyUserSummary User, EloSnapshot Standing, IReadOnlyList<MatchHistoryRow> History);

/// <summary>
/// Fabricated rating data (rating v3), so every screen of design handoff 55 can be LOOKED AT
/// without a server and without the dozens of rated matches each state needs.
///
/// <para>Built on <see cref="StatsDemoData"/>'s ladder, never beside it: the profile's place, the
/// room's ratings and the ranking all read the same players, so the preview cannot contradict
/// itself between two screens.</para>
/// </summary>
internal static class EloDemoData
{
    /// <summary>Every scene, in the order the Settings list shows them.</summary>
    internal static IReadOnlyList<EloPreviewScene> Scenes { get; } =
        Enum.GetValues<EloPreviewScene>().ToList();

    /// <summary>The name <c>--demo-elo=&lt;name&gt;</c> and the Settings list use for a scene.</summary>
    internal static string NameOf(EloPreviewScene scene) => scene.ToString().ToLowerInvariant();

    /// <summary>The scene for a name; missing or unknown is the ranking.</summary>
    internal static EloPreviewScene SceneByName(string? name)
    {
        var want = (name ?? "").Trim();
        foreach (var scene in Scenes)
            if (string.Equals(NameOf(scene), want, StringComparison.OrdinalIgnoreCase)) return scene;
        return EloPreviewScene.Ranking;
    }

    /// <summary>The Settings label for a scene.</summary>
    internal static string LabelKeyOf(EloPreviewScene scene) => "SettingsDemoEloScene" + scene;

    /// <summary>The viewer the preview draws as "you": ranked in 1v1, placing in teams.</summary>
    internal const string ViewerId = StatsDemoData.ViewerId;

    /// <summary>
    /// The community payload for a scene. <paramref name="nobodyRanked"/> is 55c: the ranked
    /// tables empty, the placement rows still there — what the ladder looks like the day the new
    /// system starts.
    /// </summary>
    internal static CommunityStats Community(string? mod, string? mode, bool nobodyRanked)
    {
        var stats = StatsDemoData.Community(mod, mode);
        if (nobodyRanked)
        {
            stats.Leaderboard = new List<LeaderboardRow>();
            stats.LeaderboardTeam = new List<LeaderboardRow>();
            stats.RankedPlayers = 0;
            stats.RankedPlayersTeam = 0;
        }
        stats.MonthlyHighlights = Highlights();
        return stats;
    }

    /// <summary>
    /// 55l: this month so far (Pedro climbed most, in Teams; the long name played most; Geaf_Argento
    /// on 🔥9 in 1v1) and last month (Pedro +187 in 1v1, 58 matches, 🔥11). With
    /// <paramref name="justStarted"/> this month has only three rated matches, two days in — the
    /// empty state, with the link to last month.
    /// </summary>
    internal static MonthlyHighlights Highlights(bool justStarted = false)
    {
        var now = DateTime.UtcNow;
        var start = new DateTime(now.Year, now.Month, 1, 6, 0, 0, DateTimeKind.Utc);
        if (now < start) start = start.AddMonths(-1);
        string Iso(DateTime d) => d.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        HighlightPlayer P(string name, int? points = null, int? matches = null, int? wins = null, int? percent = null,
            int? from = null, int? to = null) => new()
        {
            UserId = name == "Gorgorito12" ? ViewerId : "demo-" + name,
            DisplayName = name,
            Points = points,
            Matches = matches,
            Wins = wins,
            Percent = percent,
            RatingFrom = from,
            RatingTo = to,
        };
        const string longName = "Comandante_Supremo_de_la_Gran_Armada_Libertadora";

        var previousStart = start.AddMonths(-1);
        var previous = new MonthHighlights
        {
            Month = previousStart.ToString("yyyy-MM"),
            StartsAt = Iso(previousStart),
            EndsAt = Iso(start),
            SoFar = false,
            TotalRated = 312,
            MinMatches = 5,
            BiggestClimb = new HighlightPerMode { Default = P("Pedro", points: 187, matches: 22), Team = P("Sara", points: 64, matches: 9) },
            MostMatches = P(longName, matches: 58),
            BestStreak = new HighlightPerMode { Default = P("Geaf_Argento", wins: 11), Team = P("Luis", wins: 5) },
            MinRateMatches = 10,
            MinCivPicks = 3,
        };
        // Last month's lists: fewer entries, and an empty team ladder — the climb card shows no
        // switch there.
        previous.Leaders = new HighlightLeaders
        {
            BiggestClimb = new HighlightPerModeList
            {
                Default = new() { P("Pedro", points: 187, matches: 22, from: 1490, to: 1677), P("Kaiser", points: 74, matches: 15, from: 1556, to: 1630) },
                Team = new(),
            },
            MostWins = new() { P(longName, wins: 33, matches: 58), P("Pedro", wins: 19, matches: 30) },
            MostMatches = new() { P(longName, matches: 58, wins: 33), P("Pedro", matches: 30, wins: 19) },
            BestStreak = new HighlightPerModeList { Default = new() { P("Geaf_Argento", wins: 11) }, Team = new() { P("Luis", wins: 5) } },
            BestWinRate = new() { P("Pedro", wins: 19, matches: 30, percent: 63) },
            TopCiv = new() { new HighlightCiv { ModId = "wol", Civ = "Dutch", Picks = 31, Wins = 17 } },
            BiggestUpset = new(),
        };
        var current = justStarted
            ? new MonthHighlights
            {
                Month = start.ToString("yyyy-MM"),
                // Two days ago, whatever today is: the empty state only says "just started"
                // during a month's first week, and the sample must not depend on the date.
                StartsAt = Iso(now.AddDays(-2)),
                EndsAt = Iso(start.AddMonths(1)),
                SoFar = true,
                TotalRated = 3,
                MinMatches = 5,
                MostMatches = P("Pedro", matches: 2),
                BestStreak = new HighlightPerMode { Default = P("Pedro", wins: 2) },
            }
            : new MonthHighlights
            {
                Month = start.ToString("yyyy-MM"),
                StartsAt = Iso(start),
                EndsAt = Iso(start.AddMonths(1)),
                SoFar = true,
                TotalRated = 147,
                MinMatches = 5,
                BiggestClimb = new HighlightPerMode { Default = P("Siux", points: 58, matches: 14), Team = P("Pedro", points: 96, matches: 12) },
                MostMatches = P(longName, matches: 41),
                BestStreak = new HighlightPerMode { Default = P("Geaf_Argento", wins: 9), Team = P("Luis", wins: 4) },
                // The four the strip gained: with them it has seven cells, more than a laptop
                // strip holds, so the preview also shows the "+N" button.
                MostWins = P("Aluclown", wins: 23, matches: 31),
                BestWinRate = P("Kaiser", wins: 14, matches: 17, percent: 82),
                BiggestUpset = new HighlightUpset
                {
                    Mode = "default",
                    MatchId = "demo-upset",
                    Gap = 238,
                    Winners = new() { P("Siux") },
                    Losers = new() { P("Geaf_Argento") },
                    WinnersRating = 1402,
                    LosersRating = 1640,
                },
                TopCiv = new HighlightCiv { ModId = "wol", Civ = "Germans", Picks = 27, Wins = 15 },
                MinRateMatches = 10,
                MinCivPicks = 3,
                // The top five of every highlight, for Ranking › Highlights. The firsts are the
                // singular fields above, as the server derives them; a tie (Siux and Kaiser, 12
                // wins) and the viewer (Gorgorito12) are in there on purpose.
                Leaders = new HighlightLeaders
                {
                    BiggestClimb = new HighlightPerModeList
                    {
                        Default = new()
                        {
                            P("Siux", points: 58, matches: 14, from: 1402, to: 1460),
                            P("Gorgorito12", points: 41, matches: 11, from: 1342, to: 1383),
                            P("Kaiser", points: 33, matches: 19, from: 1597, to: 1630),
                        },
                        Team = new()
                        {
                            P("Pedro", points: 96, matches: 12, from: 1511, to: 1607),
                            P("Sara", points: 40, matches: 9, from: 1480, to: 1520),
                        },
                    },
                    MostWins = new()
                    {
                        P("Aluclown", wins: 23, matches: 31),
                        P(longName, wins: 20, matches: 41),
                        P("Kaiser", wins: 14, matches: 17),
                        P("Siux", wins: 12, matches: 20),
                        P("Geaf_Argento", wins: 12, matches: 22),
                    },
                    MostMatches = new()
                    {
                        P(longName, matches: 41, wins: 20),
                        P("Aluclown", matches: 31, wins: 23),
                        P("Geaf_Argento", matches: 22, wins: 12),
                        P("Siux", matches: 20, wins: 12),
                        P("Kaiser", matches: 17, wins: 14),
                    },
                    BestStreak = new HighlightPerModeList
                    {
                        Default = new() { P("Geaf_Argento", wins: 9), P("Aluclown", wins: 7), P("Kaiser", wins: 5), P("Gorgorito12", wins: 3) },
                        Team = new() { P("Luis", wins: 4), P("Pedro", wins: 3) },
                    },
                    BestWinRate = new()
                    {
                        P("Kaiser", wins: 14, matches: 17, percent: 82),
                        P("Aluclown", wins: 23, matches: 31, percent: 74),
                        P("Siux", wins: 12, matches: 20, percent: 60),
                        P("Geaf_Argento", wins: 12, matches: 22, percent: 55),
                        P(longName, wins: 20, matches: 41, percent: 49),
                    },
                    TopCiv = new()
                    {
                        new HighlightCiv { ModId = "wol", Civ = "Germans", Picks = 27, Wins = 15 },
                        new HighlightCiv { ModId = "wol", Civ = "Ethiopians", Picks = 22, Wins = 12 },
                        new HighlightCiv { ModId = "wol", Civ = "Chinese", Picks = 18, Wins = 7 },
                        new HighlightCiv { ModId = "wol", Civ = "Dutch", Picks = 11, Wins = 6 },
                        new HighlightCiv { ModId = "wol", Civ = "British", Picks = 9, Wins = 3 },
                    },
                    BiggestUpset = new()
                    {
                        new HighlightUpset
                        {
                            Mode = "default", MatchId = "demo-upset", Gap = 238,
                            Winners = new() { P("Siux") }, Losers = new() { P("Geaf_Argento") },
                            WinnersRating = 1402, LosersRating = 1640,
                        },
                        new HighlightUpset
                        {
                            Mode = "team", MatchId = "demo-upset-team", Gap = 121,
                            Winners = new() { P("Pedro"), P("Sara") }, Losers = new() { P("Kaiser"), P("Luis") },
                            WinnersRating = 1496, LosersRating = 1617,
                        },
                        new HighlightUpset
                        {
                            Mode = "default", MatchId = "demo-upset-3", Gap = 64,
                            Winners = new() { P("Gorgorito12") }, Losers = new() { P("Kaiser") },
                            WinnersRating = 1366, LosersRating = 1430,
                        },
                    },
                },
            };
        return new MonthlyHighlights { Current = current, Previous = previous };
    }

    /// <summary>
    /// The sample profile: ranked 6th of 12 in 1v1 with a peak, a low and streaks; placing 2/5 in
    /// teams with one win and one loss; a head-to-head with six rivals.
    /// </summary>
    internal static EloProfileSample Profile()
    {
        var live = StatsDemoData.Community(StatsDemoData.PrimaryModId, null);
        var solo = live.Leaderboard.First(r => r.UserId == ViewerId);
        var now = DateTime.UtcNow;
        string Iso(DateTime d) => d.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        // The order the server sends: most matches first. Siux is the rival in the 1v1 room sample.
        var rivals = new (string Name, int W, int L, int DaysAgo)[]
        {
            ("Siux", 9, 2, 0),
            ("Geaf_Argento", 7, 4, 2),
            ("Aluclown", 5, 6, 5),
            ("Maluma", 3, 3, 12),
            ("NathanR06", 4, 1, 9),
            ("Alucard", 1, 2, 31),
        };

        var standing = new EloSnapshot
        {
            Rating = solo.Rating,
            Rd = solo.Rd,
            GamesPlayed = solo.GamesPlayed,
            Wins = solo.Wins,
            Losses = solo.Losses,
            LadderRank = solo.Rank,
            LadderSize = live.Leaderboard.Count,
            LadderRankTeam = 0,
            LadderSizeTeam = live.LeaderboardTeam?.Count,
            RatingTeam = 1534,
            RdTeam = 290,
            GamesPlayedTeam = 2,
            PlacementRequired = new PlacementRequirement
            {
                Default = StatsDemoData.PlacementDefault,
                Team = StatsDemoData.PlacementTeam,
            },
            Ladders = new LadderStandings
            {
                Default = new LadderStanding
                {
                    Rating = solo.Rating,
                    Rd = solo.Rd,
                    GamesPlayed = solo.GamesPlayed,
                    LadderRank = solo.Rank,
                    LadderSize = live.Leaderboard.Count,
                    PlacementPlayed = StatsDemoData.PlacementDefault,
                    PlacementRequired = StatsDemoData.PlacementDefault,
                    Inactive = false,
                    LastRatedAt = Iso(now.AddDays(-1)),
                    Wins = solo.Wins,
                    Losses = solo.Losses,
                    StreakCurrent = solo.Streak,
                    StreakBest = 7,
                    LossStreakBest = 4,
                    RatingPeak = 1720,
                    RatingPeakAt = Iso(new DateTime(now.Year, 8, 12, 20, 0, 0, DateTimeKind.Utc)),
                    RatingLow = 1488,
                    RatingLowAt = Iso(new DateTime(now.Year, 3, 3, 20, 0, 0, DateTimeKind.Utc)),
                    HeadToHead = rivals.Select(r => new HeadToHeadEntry
                    {
                        UserId = "demo-" + r.Name,
                        DisplayName = r.Name,
                        Wins = r.W,
                        Losses = r.L,
                        LastAt = Iso(now.AddDays(-r.DaysAgo)),
                    }).ToList(),
                    HeadToHeadTotal = rivals.Length,
                },
                Team = new LadderStanding
                {
                    Rating = 1534,
                    Rd = 290,
                    GamesPlayed = 2,
                    LadderRank = 0,
                    PlacementPlayed = 2,
                    PlacementRequired = StatsDemoData.PlacementTeam,
                    PlacementResults = new List<PlacementResultEntry>
                    {
                        new() { MatchId = "demo-t1", Result = 1, RatingAfter = 1578, At = Iso(now.AddDays(-3)) },
                        new() { MatchId = "demo-t2", Result = 0, RatingAfter = 1534, At = Iso(now.AddDays(-2)) },
                    },
                    LastRatedAt = Iso(now.AddDays(-2)),
                    Wins = 1,
                    Losses = 1,
                    StreakCurrent = 0,
                    HeadToHead = new List<HeadToHeadEntry>
                    {
                        new() { UserId = "demo-Kaiser", DisplayName = "Kaiser", Wins = 1, Losses = 1, LastAt = Iso(now.AddDays(-2)) },
                        new() { UserId = "demo-Siux", DisplayName = "Siux", Wins = 1, Losses = 1, LastAt = Iso(now.AddDays(-2)) },
                    },
                    HeadToHeadTotal = 2,
                },
            },
            Refunds = new List<RefundNotice>(),
        };

        var user = new LobbyUserSummary
        {
            Id = ViewerId,
            DiscordUsername = "gorgorito12",
            DisplayName = "Gorgorito12",
            CreatedAt = Iso(now.AddMonths(-7)),
        };
        return new EloProfileSample(user, standing, new List<MatchHistoryRow>());
    }

    /// <summary>
    /// 55e: placing 6/10 in 1v1 (won, lost, won, lost, won, won) and nothing played in teams — the
    /// rating carries its "?" and no peak or streak appears until the player is on the table.
    /// </summary>
    internal static EloProfileSample ProfilePlacing()
    {
        var sample = Profile();
        var now = DateTime.UtcNow;
        var results = new[] { 1, 0, 1, 0, 1, 1 };
        sample.Standing.Ladders = new LadderStandings
        {
            Default = new LadderStanding
            {
                Rating = 1490,
                Rd = 260,
                GamesPlayed = results.Length,
                LadderRank = 0,
                PlacementPlayed = results.Length,
                PlacementRequired = StatsDemoData.PlacementDefault,
                PlacementResults = results.Select((r, i) => new PlacementResultEntry
                {
                    MatchId = "demo-p" + i,
                    Result = r,
                    RatingAfter = 1500 + i * 5,
                    At = (now.AddDays(-results.Length + i)).ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                }).ToList(),
                Wins = results.Count(r => r == 1),
                Losses = results.Count(r => r == 0),
                HeadToHead = new List<HeadToHeadEntry>
                {
                    new() { UserId = "demo-Siux", DisplayName = "Siux", Wins = 2, Losses = 1, LastAt = now.AddDays(-1).ToString("yyyy-MM-ddTHH:mm:ss.fffZ") },
                },
                HeadToHeadTotal = 1,
            },
            Team = new LadderStanding { PlacementRequired = StatsDemoData.PlacementTeam },
        };
        return sample;
    }

    /// <summary>55f: no rated match anywhere — the header says so with a "—", and nobody to list.</summary>
    internal static EloProfileSample ProfileNoGames()
    {
        var sample = Profile();
        sample.Standing.Ladders = new LadderStandings
        {
            Default = new LadderStanding { PlacementRequired = StatsDemoData.PlacementDefault },
            Team = new LadderStanding { PlacementRequired = StatsDemoData.PlacementTeam },
        };
        return sample;
    }

    /// <summary>
    /// 55f's other two cases at once: inactive in 1v1 (the place kept, the amber notice), and in
    /// teams a streak that ran out for lack of play ("—" and the date it ended).
    /// </summary>
    internal static EloProfileSample ProfileLimits()
    {
        var sample = Profile();
        var now = DateTime.UtcNow;
        string Iso(DateTime d) => d.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var solo = sample.Standing.Ladders!.Default!;
        solo.Inactive = true;
        solo.Rating = 1502;
        solo.RatingPeak = 1583;
        solo.RatingPeakAt = Iso(now.AddDays(-120));
        solo.LastRatedAt = Iso(now.AddDays(-31));
        sample.Standing.Ladders.Team = new LadderStanding
        {
            Rating = 1580,
            Rd = 110,
            GamesPlayed = 22,
            LadderRank = 2,
            LadderSize = sample.Standing.LadderSizeTeam ?? 8,
            PlacementPlayed = StatsDemoData.PlacementTeam,
            PlacementRequired = StatsDemoData.PlacementTeam,
            Wins = 14,
            Losses = 8,
            StreakCurrent = 0,
            StreakEndedAt = Iso(now.AddDays(-15)),
            StreakBest = 9,
            LossStreakBest = 5,
            RatingPeak = 1611,
            RatingPeakAt = Iso(now.AddDays(-60)),
            RatingLow = 1502,
            RatingLowAt = Iso(now.AddDays(-200)),
            LastRatedAt = Iso(now.AddDays(-29)),
        };
        return sample;
    }

    /// <summary>
    /// 55g: a competitive 1v1 with both seats taken — the viewer (ranked 6th) against Siux, whom
    /// he leads 9–2, and the server's 62 % for him.
    /// </summary>
    internal static RoomDemoData.Sample Room1v1()
    {
        var ladder = StatsDemoData.DemoLadder();
        var me = ladder.First(r => r.UserId == ViewerId);
        var rival = ladder.First(r => r.UserId == "demo-Siux");
        return new RoomDemoData.Sample
        {
            Name = "elo1v1",
            Code = "R7KX2M9Q",
            RoomName = "Wars of Liberty · Ranked 1v1",
            Seats = 2,
            Competitive = true,
            ViewerId = ViewerId,
            ViewerStanding = Profile().Standing,
            Odds = new RoomOdds
            {
                Mode = "default",
                Players = new Dictionary<string, int> { [ViewerId] = 62, [rival.UserId] = 38 },
            },
            Players = new[]
            {
                new RoomDemoData.Seat
                {
                    UserId = ViewerId, Login = me.DisplayName, IsHost = true, Ready = true,
                    Rating = me.Rating, Rd = me.Rd, GamesPlayed = me.GamesPlayed, LadderRank = me.Rank,
                    BadgeMode = "1v1",
                },
                new RoomDemoData.Seat
                {
                    UserId = rival.UserId, Login = rival.DisplayName, Ready = true,
                    Rating = rival.Rating, Rd = rival.Rd, GamesPlayed = rival.GamesPlayed, LadderRank = rival.Rank,
                    BadgeMode = "1v1",
                },
            },
        };
    }

    /// <summary>
    /// 55h: a competitive 2v2, full, with one player still to pick a team — so the room shows both
    /// columns, the NO TEAM box, the odds the server sent, and why Start is locked. The viewer is
    /// the host (he may move everybody) and, like Bai Yu Feng, still being placed in teams ("?").
    /// </summary>
    internal static RoomDemoData.Sample RoomTeams(bool allPicked = false)
    {
        var ladder = StatsDemoData.DemoLadder(team: true);
        var placing = StatsDemoData.DemoPlacement(team: true);
        var geaf = ladder.First(r => r.DisplayName == "Geaf_Argento");
        var lincoln = ladder.First(r => r.DisplayName == "Lincoln");
        var bai = placing.First(r => r.DisplayName == "Bai Yu Feng");
        var standing = Profile().Standing;
        return new RoomDemoData.Sample
        {
            Name = "eloteams",
            Code = "T4WQ8N3E",
            RoomName = "Wars of Liberty · Ranked 2v2",
            Seats = 4,
            Competitive = true,
            ViewerId = ViewerId,
            ViewerStanding = standing,
            Odds = new RoomOdds
            {
                Mode = "team",
                Teams = new Dictionary<string, int> { ["1"] = 57, ["2"] = 43 },
            },
            Players = new[]
            {
                new RoomDemoData.Seat
                {
                    UserId = ViewerId, Login = "Gorgorito12", IsHost = true, Ready = true, Team = 1,
                    RatingTeam = standing.RatingTeam, GamesPlayedTeam = standing.GamesPlayedTeam,
                    LadderRankTeam = 0, BadgeMode = "team",
                },
                new RoomDemoData.Seat
                {
                    UserId = geaf.UserId, Login = geaf.DisplayName, Ready = true, Team = 1,
                    RatingTeam = geaf.Rating, GamesPlayedTeam = geaf.GamesPlayed, LadderRankTeam = geaf.Rank,
                    BadgeMode = "team",
                },
                new RoomDemoData.Seat
                {
                    UserId = lincoln.UserId, Login = lincoln.DisplayName, Ready = true, Team = 2,
                    RatingTeam = lincoln.Rating, GamesPlayedTeam = lincoln.GamesPlayed, LadderRankTeam = lincoln.Rank,
                    BadgeMode = "team",
                },
                new RoomDemoData.Seat
                {
                    UserId = bai.UserId, Login = bai.DisplayName,
                    Team = allPicked ? 2 : null, Ready = allPicked,
                    RatingTeam = bai.Rating, GamesPlayedTeam = bai.PlacementPlayed, LadderRankTeam = 0,
                    BadgeMode = "team",
                },
            },
        };
    }

    /// <summary>
    /// 55j's five cases, as the result card would get them from the server: a win on a streak,
    /// a win the anti-farm discounted to 40 %, a 2v2 whose in-game teams did not match the room's,
    /// a loss that did not count (new account, very short), and the win that finished placement.
    /// </summary>
    internal static IReadOnlyList<MatchOutcomeView> ResultCases()
    {
        MatchOutcomeView Win(double before, double after, string rival) => new(
            MatchVerdict.Win, StatsDemoData.PrimaryModId, "ESOC Fertile Crescent", 21 * 60, 2,
            before, after, rival, 1520, 15, 11, 90);
        return new[]
        {
            Win(1598, 1612, "Siux") with
            {
                RatingMode = "default", EloFactor = 1, StreakCurrent = 4, FormatLabelKey = "MpFormat1v1",
            },
            Win(1607, 1612, "Aluclown") with
            {
                RatingMode = "default", EloFactor = 0.4, FarmStreak = 8, StreakCurrent = 8,
                FormatLabelKey = "MpFormat1v1",
            },
            new MatchOutcomeView(MatchVerdict.Win, StatsDemoData.PrimaryModId, "ESOC Baja California",
                28 * 60, 4, null, null, null, null, 15, 11, 90, UnratedReason: "teams_mismatch")
            {
                RatingMode = "team",
                FormatLabelKey = "MpFormat2v2",
                OwnSide = new[] { "Gorgorito12", "Geaf_Argento" },
                OtherSide = new[] { "Lincoln", "Bai Yu Feng" },
                IngameTeamNames = new IReadOnlyList<string>[]
                {
                    new[] { "Gorgorito12", "Lincoln" },
                    new[] { "Geaf_Argento", "Bai Yu Feng" },
                },
            },
            new MatchOutcomeView(MatchVerdict.Loss, StatsDemoData.PrimaryModId, "Great Plains",
                4 * 60, 2, null, null, "Marta", null, 15, 11, 90, UnratedReason: "new_account_short")
            {
                RatingMode = "default", FormatLabelKey = "MpFormat1v1",
            },
            Win(1469, 1490, "Siux") with
            {
                RatingMode = "default", EloFactor = 1, PlacementPlayed = 10, PlacementRequired = 10,
                PlacementCompleted = true, EnteredRank = 7, LadderSize = 12, FormatLabelKey = "MpFormat1v1",
            },
        };
    }

    /// <summary>
    /// 55k's History, today and yesterday: a win the anti-farm rule discounted to 40 %, a plain
    /// win, a 2v2 whose in-game teams did not match the room's, a new-account loss, a semifinal
    /// of a tournament, and a match nobody could read.
    /// </summary>
    internal static IReadOnlyList<MatchHistoryRow> History()
    {
        var today = DateTime.Now.Date;
        string At(DateTime local) => local.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        MatchHistoryParticipant P(string name, double result, double? before = null, double? after = null, int team = 0)
            => new()
            {
                UserId = name == "Gorgorito12" ? ViewerId : "demo-" + name,
                DisplayName = name,
                DiscordUsername = name.ToLowerInvariant(),
                Result = result,
                RatingBefore = before,
                RatingAfter = after,
                Team = team,
            };

        MatchHistoryRow Row(string id, DateTime started, int minutes, string? map, double result,
            double? before, double? after, bool rated, params MatchHistoryParticipant[] players)
            => new()
            {
                Id = id,
                ModId = StatsDemoData.PrimaryModId,
                MapName = map,
                StartedAt = At(started),
                EndedAt = At(started.AddMinutes(minutes)),
                DurationSeconds = minutes * 60,
                PlayerCount = players.Length,
                Result = result,
                RatingBefore = before,
                RatingAfter = after,
                Rated = rated,
                Competitive = true,
                RatingMode = players.Length > 2 ? "team" : "default",
                EloFactor = rated ? 1 : null,
                Participants = players.ToList(),
            };

        var farmed = Row("demo-h1", today.AddHours(21).AddMinutes(40), 19, "Great Plains", 1, 1607, 1612, true,
            P("Gorgorito12", 1, 1607, 1612), P("Aluclown", 0, 1404, 1399));
        farmed.EloFactor = 0.4;
        farmed.FarmStreak = 8;

        var plain = Row("demo-h2", today.AddHours(20).AddMinutes(55), 24, "Yukon", 1, 1593, 1607, true,
            P("Gorgorito12", 1, 1593, 1607), P("Pedro", 0, 1550, 1536));

        var mismatch = Row("demo-h3", today.AddHours(20).AddMinutes(5), 31, "ESOC Baja California", 1, null, null, false,
            // The recording's teams, 0-based as the server stores them (the card prints them + 1).
            P("Gorgorito12", 1, team: 0), P("Luis", 1, team: 0), P("Pedro", 0, team: 1), P("Sara", 0, team: 1));
        mismatch.UnratedReason = "teams_mismatch";

        var fresh = Row("demo-h4", today.AddHours(19).AddMinutes(30), 4, "Great Plains", 0, null, null, false,
            P("Gorgorito12", 0), P("Marta", 1));
        fresh.UnratedReason = "new_account_short";

        var cup = Row("demo-h5", today.AddDays(-1).AddHours(18).AddMinutes(10), 27, "ESOC Arizona", 0, 1602, 1593, true,
            P("Gorgorito12", 0, 1602, 1593), P("Comandante_Supremo_de_la_Gran_Armada_Libertadora", 1, 1688, 1697));
        cup.Tournament = new MatchTournamentRef { Id = "demo-cup", Name = "Copa de septiembre", Round = 2, RoundsTotal = 3 };

        var unread = Row("demo-h6", today.AddDays(-1).AddHours(17).AddMinutes(20), 22, "Yukon", 0.5, null, null, false,
            P("Gorgorito12", 0.5), P("Pedro", 0.5));
        unread.UnratedReason = "no_decided_result";

        return new[] { farmed, plain, mismatch, fresh, cup, unread };
    }

    /// <summary>The refund scene's notice: 34 points back on the 1v1 ladder, two hours ago.</summary>
    internal static RefundNotice Refund() => new()
    {
        RefundId = "demo-refund-1",
        Mode = "default",
        Points = 34,
        Matches = 2,
        RatingBefore = 1578,
        RatingAfter = 1612,
        CreatedAt = DateTime.UtcNow.AddHours(-2).ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
        Seen = false,
    };
}
