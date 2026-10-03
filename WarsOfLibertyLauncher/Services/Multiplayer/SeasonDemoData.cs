using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// The scenes of the season preview, in the order Settings offers them. Each is one surface the
/// rating seasons change, reachable today without waiting for a season to end.
/// </summary>
internal enum SeasonPreviewScene
{
    /// <summary>The ranking on the running season: the selector, the subtitle, the medals.</summary>
    Ranking,

    /// <summary>An ENDED season's final table — where the bell's click lands.</summary>
    Final,

    /// <summary>The night of the reset: the same calendar with the running tables empty.</summary>
    FirstDay,

    /// <summary>A sample player's profile: the SEASONS card, the season-scoped header and record.</summary>
    Profile,

    /// <summary>The room window with medals beside the names.</summary>
    Room,

    /// <summary>The rooms page with the connected-players panel wearing medals.</summary>
    Players,

    /// <summary>The bell, open on "Season 2 is over".</summary>
    Bell,
}

/// <summary>One connected player for the preview's Players panel. Plain data: the tab turns it
/// into its own row type, so nothing WPF crosses into this class.</summary>
internal sealed record SeasonPresenceSample(
    string UserId, string Login, string Status, double? Rating, double? Rd, int? LadderRank,
    int? LadderRankTeam, double? RatingTeam, SeasonTitleInfo? SeasonTitle);

/// <summary>The sample player whose profile the preview opens, with everything the page reads.</summary>
internal sealed record SeasonProfileSample(
    LobbyUserSummary User, EloSnapshot Standing, IReadOnlyList<MatchHistoryRow> History);

/// <summary>
/// Fabricated rating seasons, so the season UI can be LOOKED AT before the first season ends.
///
/// <para>Same reasoning as <see cref="StatsDemoData"/> and <see cref="RoomDemoData"/>, and more
/// so: the selector needs two ended seasons, a medal needs a top-3 finish in one, the SEASONS card
/// needs a player who finished some, and none of that exists until 1 December 2026 at the
/// earliest. Without this the art and the layout of every season surface would be decided blind.</para>
///
/// <para><b>Built ON TOP of <see cref="StatsDemoData"/>, never by editing it.</b> Its ladder, its
/// calendar and its totals are reused as they are — other previews and their tests read them —
/// and this class only adds what a season preview needs: explicit final tables, a team ladder,
/// and medals that agree with those tables.</para>
///
/// <para><b>Self-consistency is the point of the whole fixture.</b> The medal beside a name on
/// the running ladder is DERIVED from the ended seasons' tables, by the rule the server uses to
/// pick one (newest season, then the better place, then 1v1). A preview where somebody wore the
/// Season 2 gold while the Season 2 table named somebody else first would read as a bug in the
/// feature, and a design decision taken on it would be taken on a contradiction.</para>
/// </summary>
internal static class SeasonDemoData
{
    /// <summary>The season whose end the bell announces, and the one the "final" scene opens.</summary>
    internal const int EndedSeason = 2;

    /// <summary>The player whose profile and whose bell the preview shows: the Season 2 champion,
    /// so the profile carries the gold, a non-medal line and every kind of season row at once.</summary>
    internal const string ViewerName = "Geaf_Argento";

    internal static string IdOf(string name) => "demo-" + name;

    /// <summary>The name <c>--demo-seasons=&lt;name&gt;</c> and the Settings list use for a scene.</summary>
    internal static string NameOf(SeasonPreviewScene scene) => scene switch
    {
        SeasonPreviewScene.Final => "final",
        SeasonPreviewScene.FirstDay => "first-day",
        SeasonPreviewScene.Profile => "profile",
        SeasonPreviewScene.Room => "room",
        SeasonPreviewScene.Players => "players",
        SeasonPreviewScene.Bell => "bell",
        _ => "ranking",
    };

    /// <summary>The string key that labels a scene in Settings.</summary>
    internal static string LabelKeyOf(SeasonPreviewScene scene) => scene switch
    {
        SeasonPreviewScene.Final => "SettingsDemoSeasonsSceneFinal",
        SeasonPreviewScene.FirstDay => "SettingsDemoSeasonsSceneFirstDay",
        SeasonPreviewScene.Profile => "SettingsDemoSeasonsSceneProfile",
        SeasonPreviewScene.Room => "SettingsDemoSeasonsSceneRoom",
        SeasonPreviewScene.Players => "SettingsDemoSeasonsScenePlayers",
        SeasonPreviewScene.Bell => "SettingsDemoSeasonsSceneBell",
        _ => "SettingsDemoSeasonsSceneRanking",
    };

    /// <summary>Every scene, in the order Settings lists them.</summary>
    internal static IReadOnlyList<SeasonPreviewScene> Scenes { get; } =
        (SeasonPreviewScene[])Enum.GetValues(typeof(SeasonPreviewScene));

    /// <summary>
    /// Resolve <c>--demo-seasons=&lt;name&gt;</c> or a Settings choice. Missing or unknown falls
    /// back to the ranking, which is the surface the seasons change most.
    /// </summary>
    internal static SeasonPreviewScene SceneByName(string? name)
    {
        var wanted = (name ?? "").Trim();
        foreach (var scene in Scenes)
            if (string.Equals(NameOf(scene), wanted, StringComparison.OrdinalIgnoreCase))
                return scene;
        return SeasonPreviewScene.Ranking;
    }

    // ------------------------------------------------------------------ the ended seasons

    /// <summary>
    /// The final tables, in finishing order. Written out rather than generated so that who won
    /// what can be READ here — the medals on the running ladder follow from these and nothing
    /// else. Every name is a player on <see cref="StatsDemoData"/>'s ladder.
    /// </summary>
    private static readonly string[] S1Solo =
    {
        "Aluclown", ViewerName, "El Taita", "Maluma", "Kaiser", "Siux", "Alucard",
        "Jose Bareiro", "Bai Yu Feng", "NathanR06", "Gommiustan", "UnstoppableStreletsy",
        "Menelik", "Lincoln",
    };

    private static readonly string[] S1Team =
    {
        "Gommiustan", "Kaiser", "Aluclown", "Siux", "Maluma", ViewerName, "El Taita", "Alucard",
    };

    private static readonly string[] S2Solo =
    {
        ViewerName, "NathanR06", "UnstoppableStreletsy", "Aluclown", "Gommiustan", "Kaiser",
        "Siux", "El Taita", "Maluma", "Bai Yu Feng", "Alucard", "Jose Bareiro", "Menelik",
        "Lincoln", "Kanchay", "Jeops",
    };

    private static readonly string[] S2Team =
    {
        "Siux", ViewerName, "Gommiustan", "Kaiser", "Aluclown", "NathanR06", "Maluma",
        "Alucard", "El Taita", "Bai Yu Feng",
    };

    /// <summary>The running season's team ladder (2v2 and 3v3 share it), in order.</summary>
    private static readonly string[] S3Team =
    {
        "Gommiustan", "Siux", ViewerName, "Maluma", "Kaiser", "Aluclown", "Alucard",
        "NathanR06", "Jose Bareiro", "El Taita",
    };

    /// <summary>The top of each final table: what a season's champion finished on. Chosen so the
    /// ratings line up with the profile's history (the viewer finished Season 2 on 1702).</summary>
    private static double TopRating(int season, bool team) => (season, team) switch
    {
        (1, false) => 1632,
        (1, true) => 1595,
        (2, false) => 1702,
        _ => 1661,
    };

    private static string[] OrderOf(int season, bool team) => (season, team) switch
    {
        (1, false) => S1Solo,
        (1, true) => S1Team,
        (2, false) => S2Solo,
        (2, true) => S2Team,
        _ => Array.Empty<string>(),
    };

    /// <summary>
    /// An ended season's final tables. Places are the server's and never renumbered; the
    /// ratings fall in step with the place so the bars descend, and the records shrink down the
    /// table the way a real one does. No row carries a medal: the server's
    /// <c>/stats/season/:n</c> sends none either.
    /// </summary>
    internal static SeasonStandings SeasonTable(int season)
    {
        var entry = StatsDemoData.DemoSeason().List.FirstOrDefault(e => e.Number == season);
        var solo = FinalRows(season, team: false);
        var team = FinalRows(season, team: true);
        return new SeasonStandings
        {
            Season = season,
            StartsAt = entry?.StartsAt,
            EndsAt = entry?.EndsAt ?? "",
            MinDecided = 1,
            Leaderboard = solo,
            LeaderboardTeam = team,
            RankedPlayers = solo.Count,
            RankedPlayersTeam = team.Count,
        };
    }

    private static List<LeaderboardRow> FinalRows(int season, bool team)
    {
        var order = OrderOf(season, team);
        var top = TopRating(season, team);
        return order.Select((name, i) =>
        {
            var place = i + 1;
            var decided = Math.Max(3, (team ? 18 : 40) - 2 * i);
            var wins = (int)Math.Round(decided * Math.Max(0.25, 0.68 - 0.03 * i));
            return new LeaderboardRow
            {
                Rank = place,
                UserId = IdOf(name),
                DiscordUsername = name,
                DisplayName = name,
                Rating = top - 21 * i,
                Rd = (team ? 80 : 60) + 4 * place,
                GamesPlayed = decided,
                Wins = wins,
                Losses = decided - wins,
                SeasonWins = wins,
                SeasonLosses = decided - wins,
            };
        }).ToList();
    }

    // ------------------------------------------------------------------ the medals

    /// <summary>
    /// Every top-3 finish a player has, across both ended seasons and both ladders.
    /// </summary>
    internal static IReadOnlyList<SeasonTitleInfo> TitlesOf(string userId)
    {
        var titles = new List<SeasonTitleInfo>();
        foreach (var season in new[] { 1, 2 })
        {
            foreach (var team in new[] { false, true })
            {
                var order = OrderOf(season, team);
                for (var i = 0; i < Math.Min(3, order.Length); i++)
                {
                    if (IdOf(order[i]) != userId) continue;
                    titles.Add(new SeasonTitleInfo
                    {
                        Season = season,
                        Place = i + 1,
                        Mode = team ? "team" : "default",
                    });
                }
            }
        }
        return titles;
    }

    /// <summary>
    /// The ONE medal shown beside a name: the newest season, then the better place, then 1v1.
    ///
    /// <para>This is the server's rule restated, and it exists ONLY to keep this fixture honest
    /// with itself. Nothing outside the preview may call it: the launcher draws the medal the
    /// server chose, and a second opinion here is exactly the kind of copy that drifts.</para>
    /// </summary>
    internal static SeasonTitleInfo? MedalOf(string userId)
        => TitlesOf(userId)
            .OrderByDescending(t => t.Season)
            .ThenBy(t => t.Place)
            .ThenBy(t => t.IsTeam ? 1 : 0)
            .FirstOrDefault();

    // ------------------------------------------------------------------ the running season

    /// <summary>
    /// The community payload the preview draws: <see cref="StatsDemoData.Community"/> with the
    /// medals derived from the ended tables, a team ladder, and a few recent matches.
    /// </summary>
    /// <param name="firstDay">The night of the reset: the same calendar, both running tables
    /// empty — which is what every player sees at 06:00 UTC on the first day of a season.</param>
    internal static CommunityStats Community(string? modId, string? mode, bool firstDay)
    {
        var c = StatsDemoData.Community(modId, mode);
        if (firstDay)
        {
            c.Leaderboard = new List<LeaderboardRow>();
            c.LeaderboardTeam = new List<LeaderboardRow>();
            c.RankedPlayers = 0;
            c.RankedPlayersTeam = 0;
        }
        else
        {
            var team = TeamLadder(c.Leaderboard);
            foreach (var row in c.Leaderboard)
            {
                row.SeasonTitle = MedalOf(row.UserId);
                var teamPlace = team.FindIndex(t => t.UserId == row.UserId);
                row.LadderRankTeam = teamPlace >= 0 ? teamPlace + 1 : 0;
            }
            c.LeaderboardTeam = team;
            c.RankedPlayersTeam = team.Count;
        }
        if (c.RecentMatches.Count == 0) c.RecentMatches = RecentMatches();
        return c;
    }

    /// <summary>The running season's TEAM ladder. Each row carries the same medal as on the 1v1
    /// table — a player has one medal, whichever table he is standing on.</summary>
    private static List<LeaderboardRow> TeamLadder(IReadOnlyList<LeaderboardRow> solo)
    {
        return S3Team.Select((name, i) =>
        {
            var place = i + 1;
            var decided = Math.Max(4, 16 - i);
            var wins = (int)Math.Round(decided * Math.Max(0.3, 0.66 - 0.035 * i));
            var soloPlace = solo.FirstOrDefault(r => r.UserId == IdOf(name))?.Rank ?? 0;
            return new LeaderboardRow
            {
                Rank = place,
                UserId = IdOf(name),
                DiscordUsername = name,
                DisplayName = name,
                Rating = 1610 - 18 * i,
                Rd = 95 + 6 * place,
                GamesPlayed = decided,
                Wins = wins,
                Losses = decided - wins,
                LadderRank = soloPlace,
                SeasonTitle = MedalOf(IdOf(name)),
            };
        }).ToList();
    }

    /// <summary>
    /// A handful of recent community matches, so the rooms strip and the ranking's match list
    /// are not empty in the preview. Stamped relative to NOW, so "31 min ago" reads as it would.
    /// </summary>
    private static List<CommunityMatch> RecentMatches()
    {
        var now = DateTime.UtcNow;
        CommunityMatch Match(string id, int minutesAgo, string map, int minutes,
            (string Name, string Civ, double Result)[] players, bool? competitive = true, bool rated = true)
            => new()
            {
                Id = id,
                ModId = StatsDemoData.PrimaryModId,
                MapName = map,
                DurationSeconds = minutes * 60,
                ReportedAt = now.AddMinutes(-minutesAgo).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                Competitive = competitive,
                Rated = rated,
                UnratedReason = rated ? null : "no_decided_result",
                Participants = players.Select((p, i) => new MatchHistoryParticipant
                {
                    UserId = IdOf(p.Name),
                    DiscordUsername = p.Name,
                    DisplayName = p.Name,
                    // The first half of the list is one side, the second half the other.
                    Team = players.Length > 2 && i >= players.Length / 2 ? 1 : 0,
                    Civ = p.Civ,
                    Result = p.Result,
                }).ToList(),
            };

        return new List<CommunityMatch>
        {
            Match("demo-c1", 12, "ESOC_Fertile Crescent", 24,
                new[] { (ViewerName, "Germans", 1.0), ("NathanR06", "Canadians", 0.0) }),
            Match("demo-c2", 47, "ESOC_Manchuria", 31,
                new[] { ("Aluclown", "Chinese", 1.0), ("Kaiser", "British", 0.0) }),
            Match("demo-c3", 95, "ESOC_High Plains", 19,
                new[] { ("Siux", "Mexicans", 1.0), ("Maluma", "Colombians", 1.0),
                        ("Gommiustan", "Paraguayans", 0.0), ("Alucard", "Peruvians", 0.0) }),
            Match("demo-c4", 160, "ESOC_Tibet", 27,
                new[] { ("UnstoppableStreletsy", "Ethiopians", 0.5), ("El Taita", "Brazilians", 0.5) },
                rated: false),
            Match("demo-c5", 230, "ESOC_Herald Island", 22,
                new[] { ("Bai Yu Feng", "Chinese", 0.0), ("Jose Bareiro", "Paraguayans", 1.0) },
                competitive: false, rated: false),
        };
    }

    // ------------------------------------------------------------------ the profile

    /// <summary>
    /// The sample player's profile: the Season 2 champion three months into Season 3.
    ///
    /// <para>His finishes are READ from the final tables above rather than typed twice, so the
    /// SEASONS card, the medal in his header, the bell and the Season 2 table can never disagree
    /// about where he finished. <c>badge_mode</c> is left null on purpose: that hides the badge
    /// selector, so nothing in the preview can be sent to the server.</para>
    /// </summary>
    internal static SeasonProfileSample Profile()
    {
        var id = IdOf(ViewerName);
        var past = PastSeasonsOf(id);
        var titles = TitlesOf(id).ToList();

        var live = StatsDemoData.Community(StatsDemoData.PrimaryModId, null);
        var soloRow = live.Leaderboard.First(r => r.UserId == id);
        var teamLadder = TeamLadder(live.Leaderboard);
        var teamPlace = teamLadder.FindIndex(r => r.UserId == id);
        var teamRow = teamPlace >= 0 ? teamLadder[teamPlace] : null;

        var standing = new EloSnapshot
        {
            Rating = soloRow.Rating,
            Rd = soloRow.Rd,
            GamesPlayed = soloRow.Wins + soloRow.Losses,
            // All-time: this season plus every ended one.
            Wins = soloRow.Wins + past.Where(p => !p.IsTeam).Sum(p => p.Wins),
            Losses = soloRow.Losses + past.Where(p => !p.IsTeam).Sum(p => p.Losses),
            LadderRank = soloRow.Rank,
            LadderSize = live.Leaderboard.Count,
            LadderRankTeam = teamRow == null ? 0 : teamPlace + 1,
            LadderSizeTeam = teamLadder.Count,
            RatingTeam = teamRow?.Rating,
            RdTeam = teamRow?.Rd,
            GamesPlayedTeam = teamRow == null ? 0 : teamRow.Wins + teamRow.Losses,
            BadgeMode = null,
            Season = StatsDemoData.DemoSeason().Current,
            SeasonWins = soloRow.Wins,
            SeasonLosses = soloRow.Losses,
            SeasonWinsTeam = teamRow?.Wins,
            SeasonLossesTeam = teamRow?.Losses,
            PastSeasons = past.ToList(),
            SeasonTitles = titles,
            SeasonTitle = MedalOf(id),
        };

        var user = new LobbyUserSummary
        {
            Id = id,
            DiscordUsername = ViewerName,
            DisplayName = ViewerName,
            CreatedAt = "2026-08-14T19:22:00Z",
        };

        return new SeasonProfileSample(user, standing, History(id, soloRow.Rating));
    }

    /// <summary>Where a player finished every ended season, read off the final tables — newest
    /// season first, 1v1 before teams, as the server lists them.</summary>
    internal static IReadOnlyList<PastSeasonEntry> PastSeasonsOf(string userId)
    {
        var lines = new List<PastSeasonEntry>();
        foreach (var season in new[] { 2, 1 })
        {
            var table = SeasonTable(season);
            foreach (var (rows, size, mode) in new[]
                     {
                         (table.Leaderboard, table.RankedPlayers, "default"),
                         (table.LeaderboardTeam, table.RankedPlayersTeam, "team"),
                     })
            {
                var row = rows.FirstOrDefault(r => r.UserId == userId);
                if (row == null) continue;
                lines.Add(new PastSeasonEntry
                {
                    Season = season,
                    Mode = mode,
                    Place = row.Rank,
                    Size = size,
                    Rating = row.Rating,
                    Rd = row.Rd,
                    GamesPlayed = row.Wins + row.Losses,
                    Wins = row.Wins,
                    Losses = row.Losses,
                });
            }
        }
        return lines;
    }

    /// <summary>
    /// The sample player's recent matches. Eight from the running season — a continuous chain of
    /// ratings ending on today's, so the season's curve has a real shape — then three from the
    /// season before: two that moved his rating, and a team match decided AFTER that season ended,
    /// which keeps its result and moved nobody (<c>season_closed</c>).
    /// </summary>
    private static IReadOnlyList<MatchHistoryRow> History(string meId, double currentRating)
    {
        var rows = new List<MatchHistoryRow>();
        var rating = currentRating;

        // Newest first. Delta is the change THIS match made; null for one that moved nothing.
        var running = new (string When, string Opponent, string MyCiv, string TheirCiv, string Map,
                           int Minutes, double Result, int? Delta, bool Competitive, string? Unrated)[]
        {
            ("2027-05-28T21:40:00Z", "NathanR06", "Germans", "Canadians", "ESOC_Fertile Crescent", 24, 1.0, 15, true, null),
            ("2027-05-28T20:55:00Z", "Aluclown", "Germans", "Chinese", "ESOC_Manchuria", 31, 0.0, -18, true, null),
            ("2027-05-27T22:10:00Z", "Kaiser", "French", "British", "ESOC_High Plains", 27, 1.0, 13, true, null),
            ("2027-05-26T19:30:00Z", "Siux", "Germans", "Mexicans", "ESOC_Tibet", 18, 1.0, null, false, "not_competitive"),
            ("2027-05-25T23:05:00Z", "El Taita", "Germans", "Brazilians", "ESOC_Arizona", 22, 1.0, 16, true, null),
            ("2027-05-24T21:15:00Z", "Maluma", "French", "Colombians", "ESOC_Herald Island", 35, 0.5, null, true, "no_decided_result"),
            ("2027-05-22T20:00:00Z", "UnstoppableStreletsy", "Germans", "Ethiopians", "ESOC_Fertile Crescent", 29, 0.0, -21, true, null),
            ("2027-05-20T22:40:00Z", "Gommiustan", "Germans", "Paraguayans", "ESOC_Manchuria", 26, 1.0, 18, true, null),
        };

        var n = 0;
        foreach (var m in running)
        {
            double? after = null, before = null;
            if (m.Delta is int delta)
            {
                after = rating;
                before = rating - delta;
                rating = before.Value;
            }
            rows.Add(OneVOne(++n, meId, m.When, m.Opponent, m.MyCiv, m.TheirCiv, m.Map, m.Minutes,
                m.Result, before, after, m.Competitive, m.Unrated, season: 3));
        }

        // The season before: its own rating, which the reset halved towards 1500.
        rows.Add(new MatchHistoryRow
        {
            Id = "demo-h" + (++n),
            ModId = StatsDemoData.PrimaryModId,
            MapName = "ESOC_High Plains",
            DurationSeconds = 33 * 60,
            PlayerCount = 4,
            StartedAt = "2027-02-28T23:10:00Z",
            EndedAt = "2027-02-28T23:43:00Z",
            Team = 0,
            Civ = "Germans",
            Result = 1.0,
            Participants = new List<MatchHistoryParticipant>
            {
                Participant(meId, ViewerName, 0, "Germans", 1.0, null, null),
                Participant(IdOf("Siux"), "Siux", 0, "Mexicans", 1.0, null, null),
                Participant(IdOf("Gommiustan"), "Gommiustan", 1, "Paraguayans", 0.0, null, null),
                Participant(IdOf("Kaiser"), "Kaiser", 1, "British", 0.0, null, null),
            },
            Rated = false,
            UnratedReason = "season_closed",
            Competitive = true,
            Season = 2,
        });
        rows.Add(OneVOne(++n, meId, "2027-02-27T21:30:00Z", "NathanR06", "Germans", "Canadians",
            "ESOC_Tibet", 28, 1.0, 1690, 1702, true, null, season: 2));
        rows.Add(OneVOne(++n, meId, "2027-02-26T20:10:00Z", "Aluclown", "French", "Chinese",
            "ESOC_Fertile Crescent", 25, 0.0, 1708, 1690, true, null, season: 2));
        return rows;
    }

    private static MatchHistoryRow OneVOne(int n, string meId, string when, string opponent,
        string myCiv, string theirCiv, string map, int minutes, double result,
        double? before, double? after, bool competitive, string? unrated, int season)
    {
        var start = DateTime.Parse(when, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        bool rated = unrated == null;
        double? theirBefore = before == null ? null : 1500 + (n * 17 % 120);
        double? theirAfter = theirBefore == null || before == null || after == null
            ? null
            : theirBefore - (after - before);
        return new MatchHistoryRow
        {
            Id = "demo-h" + n,
            ModId = StatsDemoData.PrimaryModId,
            MapName = map,
            DurationSeconds = minutes * 60,
            PlayerCount = 2,
            StartedAt = start.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            EndedAt = start.AddMinutes(minutes).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            Team = 0,
            Civ = myCiv,
            Result = result,
            RatingBefore = before,
            RatingAfter = after,
            Participants = new List<MatchHistoryParticipant>
            {
                Participant(meId, ViewerName, 0, myCiv, result, before, after),
                Participant(IdOf(opponent), opponent, 0, theirCiv,
                    result == 0.5 ? 0.5 : 1.0 - result, theirBefore, theirAfter),
            },
            Rated = rated,
            UnratedReason = unrated,
            Competitive = competitive,
            Season = season,
        };
    }

    private static MatchHistoryParticipant Participant(string id, string name, int team, string civ,
        double result, double? before, double? after) => new()
        {
            UserId = id,
            DiscordUsername = name,
            DisplayName = name,
            Team = team,
            Civ = civ,
            Result = result,
            RatingBefore = before,
            RatingAfter = after,
        };

    // ------------------------------------------------------------------ the players panel

    /// <summary>
    /// The connected players: some in a game, some in a room, the rest in the launcher. Every
    /// medal, place and rating is the one this player has on the preview's ladders, and the panel
    /// mixes the three metals, a team medal, a name too long for its column with a medal after
    /// it, and players with none.
    /// </summary>
    internal static IReadOnlyList<SeasonPresenceSample> Players()
    {
        var live = StatsDemoData.Community(StatsDemoData.PrimaryModId, null);
        var team = TeamLadder(live.Leaderboard);

        SeasonPresenceSample Sample(string name, string status)
        {
            var id = IdOf(name);
            var solo = live.Leaderboard.FirstOrDefault(r => r.UserId == id);
            var teamIndex = team.FindIndex(r => r.UserId == id);
            return new SeasonPresenceSample(
                id, name, status, solo?.Rating, solo?.Rd, solo?.Rank ?? 0,
                teamIndex >= 0 ? teamIndex + 1 : 0,
                teamIndex >= 0 ? team[teamIndex].Rating : null,
                MedalOf(id));
        }

        return new[]
        {
            Sample("Aluclown", "in_game"),
            Sample("NathanR06", "in_game"),
            Sample("Bai Yu Feng", "in_game"),
            Sample("UnstoppableStreletsy", "in_room"),
            Sample("Siux", "in_room"),
            Sample("Kanchay", "in_room"),
            Sample(ViewerName, "idle"),
            Sample("Gommiustan", "idle"),
            Sample("Kaiser", "idle"),
            Sample("El Taita", "idle"),
            Sample("Jeops", "idle"),
            Sample("Nuevo", "idle"),
        };
    }

    // ------------------------------------------------------------------ the bell

    /// <summary>
    /// What the bell says when Season 2 ends, for the sample player: his two finishes in it, the
    /// way <see cref="SeasonNotice.Plan"/> would hand them over (1v1 first).
    /// </summary>
    internal static SeasonNoticePlan EndedNotice()
    {
        var places = PastSeasonsOf(IdOf(ViewerName))
            .Where(p => p.Season == EndedSeason)
            .OrderBy(p => p.IsTeam ? 1 : 0)
            .ToList();
        return new SeasonNoticePlan(SeasonNoticeStep.Ring, EndedSeason + 1, EndedSeason, places);
    }
}
