using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// A community's worth of fabricated statistics, so the STATS tab can be looked at.
///
/// <para>Sibling of <see cref="TournamentDemoData"/> and it exists for a sharper version of the
/// same problem. A bracket at least becomes visible once sixteen people play fifteen games; the
/// full civilization table needs <b>hundreds of rated matches carrying a civilization</b>, and
/// this community has none at all yet — the launcher only started recording them. So the filled
/// state of this page could not be judged today by any amount of playing, and the redesign of
/// it would have shipped unseen.</para>
///
/// <para>Pure and WPF-free, like the tournament fixture, so its shape can be pinned by tests.
/// The map names deliberately carry pack prefixes, because turning
/// <c>ESOC_Fertile Crescent</c> into a name and a label is one of the things being looked
/// at.</para>
/// </summary>
internal static class StatsDemoData
{
    /// <summary>How long a window the fabricated totals claim to cover. Matches the server's
    /// own <c>ACTIVITY_WINDOW_DAYS</c>, so the header reads as it would in life.</summary>
    private const int WindowDays = 30;

    /// <summary>
    /// Map counts with a real SHAPE: one clear leader, a middle, and a long tail of maps
    /// played once.
    ///
    /// <para>The tail is the point. Eight maps with a single match took eight rows on the old
    /// page, saying nothing eight times, and the grouped row that replaces them cannot be
    /// judged against a list that has no tail.</para>
    /// </summary>
    private static readonly (string Map, int Matches)[] Maps =
    {
        ("ESOC_Fertile Crescent", 74),
        ("ESOC_Manchuria", 53),
        ("ESOC_Herald Island", 40),
        ("ESOC_High Plains", 29),
        ("ESOC_Tibet", 21),
        ("ESOC_Arizona", 18),
        ("ESOC_Thar Desert", 12),
        ("KOTH_Andes", 6),
        ("Great_Plains", 4),
        ("Painted_Desert", 3),
        ("Yucatan", 2),
        ("ESOC_Adirondacks", 1),
        ("ESOC_Baja California", 1),
        ("ESOC_Cascade Range", 1),
        ("ESOC_Indonesia", 1),
        ("ESOC_Yellow River", 1),
        ("WOL_Pampas Secas", 1),
        ("Bayou", 1),
    };

    /// <summary>
    /// Civilizations with a deliberate spread across the sample bar.
    ///
    /// <para>Six clear it, one sits just under it, and the rest are the long tail this mod will
    /// have for months. The one just under the bar is the row worth having: it is where the
    /// blank percentage and the empty grey bar have to be visibly different from a low
    /// percentage, rather than reading as a bug.</para>
    /// </summary>
    private static readonly (string Civ, int Played, int Wins, int Losses)[] Civs =
    {
        ("México", 64, 34, 27),
        ("Estados Unidos", 58, 26, 28),
        ("Argentina", 41, 20, 19),
        ("Brasil", 33, 13, 18),
        ("Gran Colombia", 12, 7, 5),
        ("Perú", 9, 4, 5),
        ("Haití", 4, 2, 2),          // under the bar: no percentage, empty channel
        ("Chile", 3, 2, 1),
        ("Bolivia", 3, 1, 1),
        ("Paraguay", 2, 1, 1),
        ("Uruguay", 2, 0, 1),
        ("Venezuela", 2, 1, 0),
        ("Ecuador", 1, 1, 0),
        ("Guatemala", 1, 0, 1),
        ("Cuba", 1, 1, 0),
    };

    /// <summary>
    /// A second mod with a handful of matches, and it is the point of the whole fixture.
    ///
    /// <para><b>One mod cannot demonstrate a per-mod filter.</b> With a single mod in the data
    /// a broken filter and a working one draw exactly the same page, so the thing this
    /// preview exists to check would be unverifiable. These rows are small on purpose: a
    /// second mod that looked like the first would hide an off-by-one just as well.</para>
    /// </summary>
    internal const string SecondModId = "improvement-mod";

    internal const string PrimaryModId = "wol";

    private static readonly (string Map, int Matches)[] SecondModMaps =
    {
        ("ESOC_Yucatan", 5),
        ("Great_Plains", 2),
        ("Bayou", 1),
    };

    private static readonly (string Civ, int Played, int Wins, int Losses)[] SecondModCivs =
    {
        ("Chinos", 6, 4, 2),
        ("Otomanos", 5, 2, 3),
        ("Rusos", 2, 1, 1),
    };

    /// <summary>
    /// The team ladder, and only for the first mod.
    ///
    /// <para><b>That asymmetry is the fixture.</b> The switch is supposed to disappear for a mod
    /// with no team games, and a preview where both mods had them could not show that happening.
    /// Picking the second mod with the switch on Teams also exercises the fallback, which is the
    /// one path that is otherwise invisible until a real community hits it.</para>
    /// </summary>
    internal static bool HasTeamData(string? modId)
        => !string.Equals(modId, SecondModId, System.StringComparison.OrdinalIgnoreCase);

    /// <summary>Fewer civilizations than the 1v1 table and different numbers, because a team
    /// table that mirrored the solo one would let a broken mode filter pass.</summary>
    private static readonly (string Civ, int Played, int Wins, int Losses)[] TeamCivs =
    {
        ("México", 41, 24, 17),
        ("Estados Unidos", 38, 19, 19),
        ("Brasil", 29, 16, 13),
        ("Argentina", 22, 9, 13),
        ("Gran Colombia", 11, 6, 5),
        ("Perú", 4, 3, 1),
    };

    /// <summary>How the matches of the window fall across its days. Deliberately uneven, and
    /// with a gap: a series that never dips reads as decoration rather than as data.</summary>
    private static readonly int[] PerDay =
        { 3, 5, 0, 8, 12, 6, 9, 4, 0, 7, 11, 14, 8, 5, 3, 6, 10, 12, 7, 4, 2, 0, 5, 9, 13, 8, 6, 4, 3, 5 };

    /// <summary>Rooms opened by hour, UTC. Two humps, because a community spread evenly over
    /// twenty-four hours is not a community anybody has.</summary>
    private static readonly int[] ByHour =
        { 1, 0, 0, 0, 0, 0, 0, 1, 2, 3, 5, 6, 4, 3, 4, 7, 11, 16, 22, 27, 24, 17, 9, 4 };

    internal static CommunityStats Community(string? modId = null, string? mode = null)
    {
        bool second = string.Equals(modId, SecondModId, System.StringComparison.OrdinalIgnoreCase);
        bool team = string.Equals(mode, "team", System.StringComparison.Ordinal);
        var maps = second ? SecondModMaps : Maps;
        int matches = maps.Sum(m => m.Matches) + (second ? 2 : 16);
        // The team ladder is a tenth of the community, which is the shape a small mod actually
        // has. It also has to DIFFER: with the same totals under both switches, a mode that
        // never reached /stats/community would draw an identical page and pass.
        if (team) matches = 34;


        return new CommunityStats
        {
            GeneratedAt = "",
            MinDecided = 5,
            Leaderboard = new List<LeaderboardRow>(),
            RecentMatches = new List<CommunityMatch>(),
            Mod = modId ?? PrimaryModId,
            Mode = team ? "team" : "default",
            Totals = new CommunityTotals
            {
                WindowDays = WindowDays,
                // Only 2v2 and 3v3, never 4v4: the server refuses to rate an eight-player
                // match as a team game, so it never carries the mode this page is scoped to.
                // A fixture with a 4v4 in it would be showing a row real data cannot produce.
                TeamFormats = team
                    ? new List<FormatCount>
                    {
                        new() { Players = 4, Matches = 21 },
                        new() { Players = 6, Matches = 9 },
                    }
                    : new List<FormatCount>(),
                // MORE than the maps add up to, and that gap is a real state worth drawing:
                // a match whose recording could not be read carries no map name at all, so
                // the two figures legitimately differ and the page has to explain it rather
                // than look broken.
                Matches = matches,
                // About four in five count. A fixture where nearly none did would put the
                // card in a state no healthy community is ever in, and one where all of them
                // did would leave it with nothing to say.
                Rated = second ? 7 : team ? 30 : 231,
                UnratedTopReason = "no_decided_result",
                UnratedTopReasonMatches = second ? 2 : team ? 3 : 31,
                MatchesPerDay = BuildPerDay(second),
                PlayersWindowDays = 7,
                Players = second ? 4 : team ? 19 : 37,
                // Team games are played on fewer maps, and the counts have to fit inside the
                // smaller total above rather than contradict it on the same screen.
                TopMaps = maps.Take(team ? 4 : maps.Length)
                    .Select((m, i) => new MapCount
                    {
                        Map = m.Map,
                        Matches = team ? System.Math.Max(1, m.Matches / 6) : m.Matches,
                    }).ToList(),
                TopMap = maps[0].Map,
                TopMapMatches = team ? System.Math.Max(1, maps[0].Matches / 6) : maps[0].Matches,
            },
            Activity = new ActivityBuckets
            {
                Source = "lobbies_created",
                WindowDays = WindowDays,
                Timezone = "UTC",
                Total = ByHour.Sum(),
                Hours = ByHour.Select((c, h) => new ActivityHour
                {
                    Hour = h,
                    Count = second ? c / 4 : c,
                }).ToList(),
            },
        };
    }

    private static List<DayCount> BuildPerDay(bool second)
    {
        var today = System.DateTime.UtcNow.Date;
        var days = new List<DayCount>();
        for (int i = 0; i < PerDay.Length; i++)
        {
            int n = second ? PerDay[i] / 4 : PerDay[i];
            // Days with nothing are simply absent, exactly as the server sends them.
            if (n <= 0) continue;
            days.Add(new DayCount
            {
                Day = today.AddDays(i - PerDay.Length + 1).ToString("yyyy-MM-dd"),
                Matches = n,
            });
        }
        return days;
    }

    /// <summary>The filled civilization table — the state 9b of the handoff exists to show.</summary>
    internal static CivStatsResponse CivStats(string? modId = null, string? mode = null)
    {
        bool second = string.Equals(modId, SecondModId, System.StringComparison.OrdinalIgnoreCase);
        bool team = string.Equals(mode, "team", System.StringComparison.Ordinal);
        var rows = second ? SecondModCivs : team ? TeamCivs : Civs;

        return new CivStatsResponse
        {
            GeneratedAt = "",
            Mod = modId ?? PrimaryModId,
            Mode = team ? "team" : "default",
            // COUNTED, not derived by halving rows: a civilization can fail to resolve for one
            // side of a match and not the other, which is the reason the server counts it
            // directly. Its denominator comes from the same query minus the civilization, so
            // the two are comparable by construction.
            RatedMatchesWithCiv = second ? 9 : team ? 30 : 236,
            RatedMatches = second ? 13 : team ? 34 : 291,
            Civs = rows.Select((c, i) => new CivStatEntry
            {
                ModId = modId ?? PrimaryModId,
                ModVersion = "demo",
                Civ = c.Civ,
                Played = c.Played,
                Wins = c.Wins,
                Losses = c.Losses,
                // Roughly a quarter of an hour, drifting a little, so the column has something
                // to show without pretending to precision it would not have.
                AvgSeconds = 780 + i * 37,
            }).ToList(),
        };
    }

    /// <summary>
    /// The empty civilization table, which is the state this community is ACTUALLY in.
    ///
    /// <para>9a of the handoff. Kept as a scenario of its own because the empty state is not a
    /// degenerate case here, it is today: the explanation it carries is the whole content of
    /// that half of the page for the next few months.</para>
    /// </summary>
    internal static CivStatsResponse NoCivStats(string? modId = null) => new()
    {
        GeneratedAt = "",
        Mod = modId ?? PrimaryModId,
        RatedMatchesWithCiv = 0,
        RatedMatches = 34,
        Civs = new List<CivStatEntry>(),
    };

    /// <summary>
    /// Civilization against civilization.
    ///
    /// <para>Deliberately thinner than the civ table: a matchup needs both sides resolved, so
    /// it always has less behind it than either civilization alone, and most pairs sit under
    /// the sample bar for far longer. A fixture where every pair had a percentage would
    /// flatter the table.</para>
    /// </summary>
    internal static MatchupsResponse Matchups(string? modId = null, string? mode = null)
    {
        bool second = string.Equals(modId, SecondModId, System.StringComparison.OrdinalIgnoreCase);
        bool team = string.Equals(mode, "team", System.StringComparison.Ordinal);

        // Who is played WITH. Only in team games, and deliberately a different set of pairs
        // from the rivals below: the two tables answer different questions, and a fixture that
        // repeated one in the other would hide a query joining on the wrong side.
        var together = team && !second
            ? new (string A, string B, int Played, int WinsA)[]
            {
                ("Estados Unidos", "México", 17, 10),
                ("Brasil", "México", 12, 5),
                ("Argentina", "Estados Unidos", 8, 5),
                ("Gran Colombia", "México", 3, 2),
            }
            : System.Array.Empty<(string A, string B, int Played, int WinsA)>();

        var pairs = second
            ? new (string A, string B, int Played, int WinsA)[]
            {
                ("Chinos", "Otomanos", 3, 2),
            }
            : team
            ? new (string A, string B, int Played, int WinsA)[]
            {
                ("Estados Unidos", "México", 24, 11),
                ("Brasil", "México", 18, 7),
                ("Argentina", "México", 13, 6),
                ("Argentina", "Brasil", 7, 4),
                ("Gran Colombia", "México", 2, 1),
            }
            : new (string A, string B, int Played, int WinsA)[]
            {
                ("Estados Unidos", "México", 19, 9),
                ("Argentina", "Brasil", 14, 8),
                ("Brasil", "México", 11, 4),
                ("Argentina", "México", 9, 5),
                ("Estados Unidos", "Gran Colombia", 4, 3),
            };

        MatchupEntry Pair((string A, string B, int Played, int WinsA) p) => new()
        {
            ModId = modId ?? PrimaryModId,
            ModVersion = "demo",
            CivA = p.A,
            CivB = p.B,
            Played = p.Played,
            WinsA = p.WinsA,
            LossesA = p.Played - p.WinsA,
        };

        return new MatchupsResponse
        {
            GeneratedAt = "",
            Mode = team ? "team" : "default",
            Allies = together.Select(Pair).ToList(),
            Matchups = pairs.Select(p => new MatchupEntry
            {
                ModId = modId ?? PrimaryModId,
                ModVersion = "demo",
                CivA = p.A,
                CivB = p.B,
                Played = p.Played,
                WinsA = p.WinsA,
                LossesA = p.Played - p.WinsA,
            }).ToList(),
        };
    }

    /// <summary>
    /// Which cards people BRING. Never "play": no recording carries the card that was played,
    /// and the launcher and the backend both say so in those words.
    /// </summary>
    internal static DeckStatsResponse Decks(string? modId = null)
    {
        bool second = string.Equals(modId, SecondModId, System.StringComparison.OrdinalIgnoreCase);
        var cards = second
            ? new (string Civ, string Card, int Players)[]
            {
                ("Chinos", "YPHCExpandedTradingPost", 2),
                ("Otomanos", "HCAdmirality", 1),
            }
            : new (string Civ, string Card, int Players)[]
            {
                ("México", "HCXPRefrigeration", 9),
                ("México", "HCCigarRollers", 7),
                ("Estados Unidos", "HCShipBalloons", 6),
                ("Estados Unidos", "HCAdmirality", 5),
                ("Argentina", "HCXPGauchos", 5),
                ("Brasil", "HCXPCoffeeTrade", 4),
                ("Gran Colombia", "HCXPLlaneros", 3),
            };

        return new DeckStatsResponse
        {
            GeneratedAt = "",
            Mod = modId ?? PrimaryModId,
            // Counted, never summed from the rows: one person carries many cards, so adding
            // the column up would report a multiple of the real headcount.
            Contributors = second ? 2 : 11,
            Cards = cards.Select(c => new DeckCardEntry
            {
                ModId = modId ?? PrimaryModId,
                Civ = c.Civ,
                Card = c.Card,
                Players = c.Players,
            }).ToList(),
        };
    }
}
