using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// The Achievements section's sample data (design 121a/121c), for <c>--demo-elo=profile:achievements</c>
/// and <c>profile:achievements-tad</c>: the design's own values and dates, generated from its lists.
/// Nobody's real medals: the profile preview says so above the page.
/// </summary>
public static class AchievementsDemoData
{
    /// <summary>The design's 121a sample: wol, 14 of 40 medals.</summary>
    public static AchievementsResponse Wol() => new()
    {
        Mod = "wol",
        Total = 40,
        Earned = 14,
        Mods = new()
        {
            new() { Mod = "wol", Earned = 14, Total = 40 },
            new() { Mod = "aoe3-tad", Earned = 7, Total = 41 },
        },
        Latest = new()
        {
            new() { Id = "team_debut", Tier = 0, Metal = "b", Icon = "people", Enamel = 3, Name = "Team Debut", Condition = "Play your first competitive team match", EarnedAt = "2026-11-02T18:00:00Z" },
            new() { Id = "organiser", Tier = 0, Metal = "s", Icon = "bracket", Enamel = 6, Name = "Organiser", Condition = "Organise a tournament that reaches the end", EarnedAt = "2026-10-27T18:00:00Z" },
            new() { Id = "endless_siege", Tier = 0, Metal = "b", Icon = "clock", Enamel = 5, Name = "Endless Siege", Condition = "Win a match longer than 60 minutes", EarnedAt = "2026-10-08T18:00:00Z" }
        },
        Groups = new()
        {
            new() { Id = "first_steps", Enamel = 0, Items = new()
            {
                new() { Id = "baptism_of_fire", Icon = "sword", Metal = "b", Name = "Baptism of Fire", Condition = "Play your first multiplayer match", EarnedAt = "2026-03-02T18:00:00Z" },
                new() { Id = "first_blood", Icon = "check", Metal = "b", Name = "First Blood", Condition = "Win your first match", EarnedAt = "2026-03-03T18:00:00Z" },
                new() { Id = "veteran", Icon = "shield", Metal = "s", Name = "Veteran", Condition = "Play 100 matches", Goal = 100, Value = 26 },
                new() { Id = "conqueror", Icon = "trophy", Metal = "s", Name = "Conqueror", Condition = "Win 50 matches", Goal = 50, Value = 15 }
            } },
            new() { Id = "streaks_ranking", Enamel = 1, Items = new()
            {
                new() { Id = "unstoppable", Icon = "flame", Metal = "s", Name = "Unstoppable", Condition = "Win 5 matches in a row", EarnedAt = "2026-08-12T18:00:00Z" },
                new() { Id = "phoenix", Icon = "phoenix", Metal = "b", Name = "Phoenix", Condition = "Win after losing 3 in a row" },
                new() { Id = "giant_slayer", Icon = "climb", Metal = "g", Name = "Giant Slayer", Condition = "Beat someone with 200 more ELO" },
                new() { Id = "graduate", Icon = "cap", Metal = "b", Name = "Graduate", Condition = "Finish your placement matches", EarnedAt = "2026-03-09T18:00:00Z" },
                new() { Id = "promotion", Icon = "promotion", Metal = "s", Name = "Promotion", Condition = "Reach an age of the ranking: one medal per age", Tiers = new() { new() { Tier = 1, Key = "colonial", EarnedAt = "2026-08-13T18:00:00Z" }, new() { Tier = 2, Key = "fortress", EarnedAt = "2026-08-13T18:00:00Z" }, new() { Tier = 3, Key = "industrial", EarnedAt = "2026-08-13T18:00:00Z" }, new() { Tier = 4, Key = "imperial", EarnedAt = null }, new() { Tier = 5, Key = "sovereign", EarnedAt = null } }, TierReached = 3 },
                new() { Id = "on_top", Icon = "crown", Metal = "g", Name = "On Top", Condition = "Enter the podium (top 3)" }
            } },
            new() { Id = "competitive_1v1", Enamel = 2, Items = new()
            {
                new() { Id = "ranked_debut", Icon = "target", Metal = "b", Name = "Ranked Debut", Condition = "Play your first competitive 1v1", EarnedAt = "2026-03-04T18:00:00Z" },
                new() { Id = "ladder_regular", Icon = "shield", Metal = "s", Name = "Ladder Regular", Condition = "Play 25 competitive 1v1 matches", Goal = 25, Value = 26, EarnedAt = "2026-09-20T18:00:00Z" },
                new() { Id = "duelist", Icon = "sword", Metal = "b", Name = "Duelist", Condition = "Win 10 competitive 1v1 matches", Goal = 10, Value = 15, EarnedAt = "2026-06-21T18:00:00Z" },
                new() { Id = "gladiator", Icon = "trophy", Metal = "g", Name = "Gladiator", Condition = "Win 50 competitive 1v1 matches", Goal = 50, Value = 15 },
                new() { Id = "contender", Icon = "climb", Metal = "b", Name = "Contender", Condition = "Reach 1600 ELO in 1v1", Ladder = "default", Goal = 1600, Value = 1612, EarnedAt = "2026-07-30T18:00:00Z" },
                new() { Id = "elite", Icon = "climb", Metal = "s", Name = "Elite", Condition = "Reach 1700 ELO in 1v1", Ladder = "default", Goal = 1700, Value = 1612 },
                new() { Id = "legend", Icon = "climb", Metal = "g", Name = "Legend", Condition = "Reach 1800 ELO in 1v1", Ladder = "default", Goal = 1800, Value = 1612 }
            } },
            new() { Id = "competitive_teams", Enamel = 3, Items = new()
            {
                new() { Id = "team_debut", Icon = "people", Metal = "b", Name = "Team Debut", Condition = "Play your first competitive team match", EarnedAt = "2026-11-02T18:00:00Z" },
                new() { Id = "team_graduate", Icon = "cap", Metal = "b", Name = "Team Graduate", Condition = "Finish your placement matches in Teams" },
                new() { Id = "squadmate", Icon = "shield", Metal = "s", Name = "Squadmate", Condition = "Play 25 competitive team matches", Goal = 25, Value = 2 },
                new() { Id = "coalition", Icon = "flag", Metal = "s", Name = "Coalition", Condition = "Win 10 competitive team matches", Goal = 10, Value = 1 },
                new() { Id = "grand_alliance", Icon = "crown", Metal = "g", Name = "Grand Alliance", Condition = "Win 50 competitive team matches", Goal = 50, Value = 1 },
                new() { Id = "vanguard", Icon = "climb", Metal = "b", Name = "Vanguard", Condition = "Reach 1600 ELO in Teams", Ladder = "team", Goal = 1600, LockedReason = "placement" },
                new() { Id = "old_guard", Icon = "climb", Metal = "s", Name = "Old Guard", Condition = "Reach 1700 ELO in Teams", Ladder = "team", Goal = 1700, LockedReason = "placement" },
                new() { Id = "high_command", Icon = "climb", Metal = "g", Name = "High Command", Condition = "Reach 1800 ELO in Teams", Ladder = "team", Goal = 1800, LockedReason = "placement" }
            } },
            new() { Id = "civs_maps", Enamel = 4, Items = new()
            {
                new() { Id = "ambassador", Icon = "flag", Metal = "s", Name = "Ambassador", Condition = "Win with 10 different civilizations", Goal = 10, Value = 4 },
                new() { Id = "citizen_of_the_world", Icon = "globe", Metal = "g", Name = "Citizen of the World", Condition = "Win with every civilization of the mod" },
                new() { Id = "specialist", Icon = "star", Metal = "s", Name = "Specialist", Condition = "Win 25 matches with the same civilization", Goal = 25, Value = 9 },
                new() { Id = "cartographer", Icon = "compass", Metal = "s", Name = "Cartographer", Condition = "Play on 20 different maps", Goal = 20, Value = 11 }
            } },
            new() { Id = "formats", Enamel = 5, Items = new()
            {
                new() { Id = "brothers_in_arms", Icon = "people", Metal = "b", Name = "Brothers in Arms", Condition = "Win 10 team matches", Goal = 10, Value = 1 },
                new() { Id = "lightning_war", Icon = "bolt", Metal = "s", Name = "Lightning War", Condition = "Win a match shorter than 15 minutes" },
                new() { Id = "endless_siege", Icon = "clock", Metal = "b", Name = "Endless Siege", Condition = "Win a match longer than 60 minutes", EarnedAt = "2026-10-08T18:00:00Z" }
            } },
            new() { Id = "community", Enamel = 6, Items = new()
            {
                new() { Id = "diplomat", Icon = "people", Metal = "b", Name = "Diplomat", Condition = "Play against 25 different players", Goal = 25, Value = 12 },
                new() { Id = "host", Icon = "host", Metal = "s", Name = "Host", Condition = "Create 20 rooms that get played", Goal = 20, Value = 7 },
                new() { Id = "champion", Icon = "trophy", Metal = "g", Name = "Champion", Condition = "Win a tournament" },
                new() { Id = "organiser", Icon = "bracket", Metal = "s", Name = "Organiser", Condition = "Organise a tournament that reaches the end", EarnedAt = "2026-10-27T18:00:00Z" }
            } }
        },
    };

    /// <summary>The design's 121c sample: aoe3-tad, 7 of 41 medals.</summary>
    public static AchievementsResponse Tad() => new()
    {
        Mod = "aoe3-tad",
        Total = 41,
        Earned = 7,
        Mods = new()
        {
            new() { Mod = "aoe3-tad", Earned = 7, Total = 41 },
            new() { Mod = "wol", Earned = 14, Total = 40 },
        },
        Latest = new()
        {
            new() { Id = "promotion", Tier = 1, Metal = "s", Icon = "promotion", Enamel = 6, Name = "Promotion", Condition = "Reach an age of the ranking: one medal per age", EarnedAt = "2026-11-16T18:00:00Z" },
            new() { Id = "clash_of_civilizations", Tier = 0, Metal = "b", Icon = "sword", Enamel = 1, Name = "Clash of Civilizations", Condition = "Win with an Asian civilization against a European one", EarnedAt = "2026-11-15T18:00:00Z" },
            new() { Id = "non_aggression_pact", Tier = 0, Metal = "b", Icon = "check", Enamel = 5, Name = "Non-Aggression Pact", Condition = "Win a match with treaty rules", EarnedAt = "2026-11-12T18:00:00Z" }
        },
        Groups = new()
        {
            new() { Id = "expansions", Enamel = 0, Items = new()
            {
                new() { Id = "old_world", Icon = "crown", Metal = "s", Name = "Old World", Condition = "Win with the 8 European civilizations", Goal = 8, Value = 3 },
                new() { Id = "war_chiefs", Icon = "feather", Metal = "b", Name = "War Chiefs", Condition = "Win with the 3 native civilizations", Goal = 3 },
                new() { Id = "asian_dynasties", Icon = "star", Metal = "b", Name = "Asian Dynasties", Condition = "Win with the 3 Asian civilizations", Goal = 3, Value = 1 },
                new() { Id = "complete_empire", Icon = "globe", Metal = "g", Name = "The Complete Empire", Condition = "Win with all 14 civilizations", Goal = 14, Value = 4 }
            } },
            new() { Id = "rivalries", Enamel = 1, Items = new()
            {
                new() { Id = "clash_of_civilizations", Icon = "sword", Metal = "b", Name = "Clash of Civilizations", Condition = "Win with an Asian civilization against a European one", EarnedAt = "2026-11-15T18:00:00Z" },
                new() { Id = "native_resistance", Icon = "feather", Metal = "b", Name = "Native Resistance", Condition = "Win with a native civilization against a European one" },
                new() { Id = "duel_of_dynasties", Icon = "flag", Metal = "s", Name = "Duel of Dynasties", Condition = "Win with Chinese against Japanese, or the other way round" },
                new() { Id = "mirror", Icon = "people", Metal = "b", Name = "Mirror", Condition = "Win with the same civilization as your rival", EarnedAt = "2026-11-09T18:00:00Z" }
            } },
            new() { Id = "maps", Enamel = 4, Items = new()
            {
                new() { Id = "silk_road", Icon = "compass", Metal = "g", Name = "Silk Road", Condition = "Win on every Asian map of the expansion" },
                new() { Id = "new_world", Icon = "flag", Metal = "s", Name = "New World", Condition = "Win on 10 maps of the Americas", Goal = 10, Value = 2 },
                new() { Id = "globetrotter", Icon = "globe", Metal = "s", Name = "Globetrotter", Condition = "Play on 20 different maps", Goal = 20, Value = 6 }
            } },
            new() { Id = "room_rules", Enamel = 5, Items = new()
            {
                new() { Id = "non_aggression_pact", Icon = "check", Metal = "b", Name = "Non-Aggression Pact", Condition = "Win a match with treaty rules", EarnedAt = "2026-11-12T18:00:00Z" },
                new() { Id = "long_treaty", Icon = "clock", Metal = "s", Name = "Long Treaty", Condition = "Win a 40-minute treaty match" },
                new() { Id = "king_of_the_hill", Icon = "crown", Metal = "s", Name = "King of the Hill", Condition = "Win a match with that rule" },
                new() { Id = "classic", Icon = "shield", Metal = "b", Name = "Classic", Condition = "Win a match with classic rules", EarnedAt = "2026-11-08T18:00:00Z" }
            } },
            new() { Id = "competitive_1v1", Enamel = 2, Items = new()
            {
                new() { Id = "ranked_debut", Icon = "target", Metal = "b", Name = "Ranked Debut", Condition = "Play your first competitive 1v1", EarnedAt = "2026-11-06T18:00:00Z" },
                new() { Id = "ladder_regular", Icon = "shield", Metal = "s", Name = "Ladder Regular", Condition = "Play 25 competitive 1v1 matches", Goal = 25, Value = 12 },
                new() { Id = "duelist", Icon = "sword", Metal = "b", Name = "Duelist", Condition = "Win 10 competitive 1v1 matches", Goal = 10, Value = 8 },
                new() { Id = "gladiator", Icon = "trophy", Metal = "g", Name = "Gladiator", Condition = "Win 50 competitive 1v1 matches", Goal = 50, Value = 8 },
                new() { Id = "contender", Icon = "climb", Metal = "b", Name = "Contender", Condition = "Reach 1600 ELO in 1v1", Ladder = "default", Goal = 1600, Value = 1548 },
                new() { Id = "elite", Icon = "climb", Metal = "s", Name = "Elite", Condition = "Reach 1700 ELO in 1v1", Ladder = "default", Goal = 1700, Value = 1548 },
                new() { Id = "legend", Icon = "climb", Metal = "g", Name = "Legend", Condition = "Reach 1800 ELO in 1v1", Ladder = "default", Goal = 1800, Value = 1548 }
            } },
            new() { Id = "competitive_teams", Enamel = 3, Items = new()
            {
                new() { Id = "team_debut", Icon = "people", Metal = "b", Name = "Team Debut", Condition = "Play your first competitive team match" },
                new() { Id = "team_graduate", Icon = "cap", Metal = "b", Name = "Team Graduate", Condition = "Finish your placement matches in Teams" },
                new() { Id = "squadmate", Icon = "shield", Metal = "s", Name = "Squadmate", Condition = "Play 25 competitive team matches", Goal = 25 },
                new() { Id = "coalition", Icon = "flag", Metal = "s", Name = "Coalition", Condition = "Win 10 competitive team matches", Goal = 10 },
                new() { Id = "grand_alliance", Icon = "crown", Metal = "g", Name = "Grand Alliance", Condition = "Win 50 competitive team matches", Goal = 50 },
                new() { Id = "vanguard", Icon = "climb", Metal = "b", Name = "Vanguard", Condition = "Reach 1600 ELO in Teams", Ladder = "team", Goal = 1600, LockedReason = "placement" },
                new() { Id = "old_guard", Icon = "climb", Metal = "s", Name = "Old Guard", Condition = "Reach 1700 ELO in Teams", Ladder = "team", Goal = 1700, LockedReason = "placement" },
                new() { Id = "high_command", Icon = "climb", Metal = "g", Name = "High Command", Condition = "Reach 1800 ELO in Teams", Ladder = "team", Goal = 1800, LockedReason = "placement" }
            } },
            new() { Id = "general", Enamel = 6, Items = new()
            {
                new() { Id = "first_blood", Icon = "check", Metal = "b", Name = "First Blood", Condition = "Win your first match", EarnedAt = "2026-11-07T18:00:00Z" },
                new() { Id = "conqueror", Icon = "trophy", Metal = "s", Name = "Conqueror", Condition = "Win 50 matches", Goal = 50, Value = 8 },
                new() { Id = "unstoppable", Icon = "flame", Metal = "s", Name = "Unstoppable", Condition = "Win 5 matches in a row" },
                new() { Id = "giant_slayer", Icon = "climb", Metal = "g", Name = "Giant Slayer", Condition = "Beat someone with 200 more ELO" },
                new() { Id = "promotion", Icon = "promotion", Metal = "s", Name = "Promotion", Condition = "Reach an age of the ranking: one medal per age", Tiers = new() { new() { Tier = 1, Key = "colonial", EarnedAt = "2026-11-16T18:00:00Z" }, new() { Tier = 2, Key = "fortress", EarnedAt = null }, new() { Tier = 3, Key = "industrial", EarnedAt = null }, new() { Tier = 4, Key = "imperial", EarnedAt = null }, new() { Tier = 5, Key = "sovereign", EarnedAt = null } }, TierReached = 1 },
                new() { Id = "diplomat", Icon = "people", Metal = "b", Name = "Diplomat", Condition = "Play against 25 different players", Goal = 25, Value = 5 },
                new() { Id = "champion", Icon = "trophy", Metal = "g", Name = "Champion", Condition = "Win a tournament" }
            } }
        },
    };

    /// <summary>
    /// The catalogue as the server has it since 2026-11-26 (<c>--demo-elo=profile:achievements-more</c>):
    /// design 121's Wars of Liberty sample with the medals added after it, in the server's groups and
    /// order. The layout and the new items are printed from the server's <c>catalog.ts</c>, so the ids,
    /// icons and metals are the ones a player will receive.
    /// </summary>
    public static AchievementsResponse WolExtended()
    {
        var known = Wol().Groups!.SelectMany(g => g.Items).Concat(AddedItems).ToDictionary(i => i.Id);
        var groups = ExtendedLayout.Select(g => new AchievementGroup
        {
            Id = g.Id, Enamel = g.Enamel, Name = g.Name, Items = g.Items.Select(id => known[id]).ToList(),
        }).ToList();
        int total = groups.Sum(g => g.Items.Sum(i => i.Tiers is { Count: > 0 } t ? t.Count : 1));
        int earned = groups.Sum(g => g.Items.Sum(i => i.Tiers is { Count: > 0 } t
            ? t.Count(x => x.EarnedAt != null)
            : i.EarnedAt != null ? 1 : 0));
        var r = Wol();
        r.Total = total;
        r.Earned = earned;
        r.Groups = groups;
        r.Mods = new()
        {
            new() { Mod = "wol", Earned = earned, Total = total },
            new() { Mod = "aoe3-tad", Earned = 7, Total = 61 },
        };
        return r;
    }

    /// <summary>The server's Wars of Liberty groups, in order, and the medal ids in each.</summary>
    private static readonly (string Id, int Enamel, string Name, string[] Items)[] ExtendedLayout =
    {
            ("first_steps", 0, "First steps", new[] { "baptism_of_fire", "first_blood", "veteran", "conqueror", "field_marshal", "steadfast" }),
            ("streaks_ranking", 1, "Streaks and ranking", new[] { "unstoppable", "invincible", "phoenix", "revenge", "giant_slayer", "graduate", "promotion", "on_top" }),
            ("competitive_1v1", 2, "Competitive 1v1", new[] { "ranked_debut", "ladder_regular", "duelist", "gladiator", "contender", "elite", "legend" }),
            ("competitive_teams", 3, "Competitive teams", new[] { "team_debut", "team_graduate", "squadmate", "coalition", "grand_alliance", "vanguard", "old_guard", "high_command" }),
            ("civs_maps", 4, "Civilizations and maps", new[] { "ambassador", "africa_oceania", "eastern_empires", "old_continent", "the_americas", "citizen_of_the_world", "specialist", "cartographer" }),
            ("historical_wars", 1, "Historical wars", new[] { "war_triple_alliance", "war_pacific", "war_independence", "war_crimean", "war_mexican_american", "war_franco_prussian", "war_anglo_zulu", "war_russo_japanese" }),
            ("formats", 5, "Formats", new[] { "brothers_in_arms", "all_rounder", "lightning_war", "endless_siege", "ready_for_battle", "skirmisher", "minuteman" }),
            ("community", 6, "Community", new[] { "diplomat", "cosmopolitan", "host", "in_the_bracket", "finalist", "champion", "organiser" }),
            ("feats", 7, "Feats", new[] { "grandmaster", "number_one", "iron_streak", "centurion", "legion", "emperor", "virtuoso", "triple_crown", "living_legend" }),
    };

    /// <summary>The medals design 121 does not draw, with sample progress.</summary>
    private static readonly AchievementItem[] AddedItems =
    {
            new() { Id = "field_marshal", Icon = "star", Metal = "g", Name = "Field Marshal", Condition = "Play 500 matches", Goal = 500, Value = 26 },
            new() { Id = "steadfast", Icon = "clock", Metal = "s", Name = "Steadfast", Condition = "Play on 30 different days", Goal = 30, Value = 19 },
            new() { Id = "invincible", Icon = "flame", Metal = "g", Name = "Invincible", Condition = "Win 10 matches in a row", Goal = 10, Value = 6 },
            new() { Id = "revenge", Icon = "sword", Metal = "b", Name = "Revenge", Condition = "Win a 1v1 against the player who beat you the last time you met", EarnedAt = "2026-09-14T18:00:00Z" },
            new() { Id = "africa_oceania", Icon = "compass", Metal = "b", Name = "Africa and Oceania", Condition = "Win with each civilization of Africa and Oceania", Goal = 5, Value = 2 },
            new() { Id = "eastern_empires", Icon = "star", Metal = "s", Name = "Eastern Empires", Condition = "Win with each civilization of Asia", Goal = 8, Value = 1 },
            new() { Id = "old_continent", Icon = "crown", Metal = "s", Name = "Old Continent", Condition = "Win with each civilization of Europe", Goal = 17, Value = 3 },
            new() { Id = "the_americas", Icon = "flag", Metal = "s", Name = "The Americas", Condition = "Win with each civilization of the Americas", Goal = 18, Value = 1 },
            new() { Id = "war_triple_alliance", Icon = "people", Metal = "b", Name = "Triple Alliance", Condition = "Win Paraguayans vs Argentines, Brazilians or Uruguayans, either side", EarnedAt = "2026-07-04T18:00:00Z" },
            new() { Id = "war_pacific", Icon = "compass", Metal = "b", Name = "War of the Pacific", Condition = "Win Chileans vs Peruvians or Bolivians, either side" },
            new() { Id = "war_independence", Icon = "flag", Metal = "b", Name = "Independence", Condition = "Win a former Spanish colony vs Spain, either side" },
            new() { Id = "war_crimean", Icon = "shield", Metal = "b", Name = "Crimean War", Condition = "Win Russians vs Ottomans, British or French, either side" },
            new() { Id = "war_mexican_american", Icon = "target", Metal = "s", Name = "Mexican-American War", Condition = "Win Mexicans vs the United States, either side" },
            new() { Id = "war_franco_prussian", Icon = "crown", Metal = "s", Name = "Franco-Prussian War", Condition = "Win French vs Germans, either side", EarnedAt = "2026-08-02T18:00:00Z" },
            new() { Id = "war_anglo_zulu", Icon = "sword", Metal = "s", Name = "Anglo-Zulu War", Condition = "Win British vs Zulu, either side" },
            new() { Id = "war_russo_japanese", Icon = "star", Metal = "s", Name = "Russo-Japanese War", Condition = "Win Russians vs Japanese, either side" },
            new() { Id = "all_rounder", Icon = "star", Metal = "s", Name = "All-Rounder", Condition = "Win a 1v1, a 2v2 and a 3v3", Goal = 3, Value = 2 },
            new() { Id = "ready_for_battle", Icon = "bolt", Metal = "b", Name = "Ready for Battle", Condition = "Win a quick match", EarnedAt = "2026-11-12T18:00:00Z" },
            new() { Id = "skirmisher", Icon = "bolt", Metal = "s", Name = "Skirmisher", Condition = "Win 10 quick matches", Goal = 10, Value = 3 },
            new() { Id = "minuteman", Icon = "target", Metal = "g", Name = "Minuteman", Condition = "Win 50 quick matches", Goal = 50, Value = 3 },
            new() { Id = "cosmopolitan", Icon = "globe", Metal = "g", Name = "Cosmopolitan", Condition = "Play against 100 different players", Goal = 100, Value = 12 },
            new() { Id = "in_the_bracket", Icon = "bracket", Metal = "b", Name = "In the Bracket", Condition = "Play a tournament match", EarnedAt = "2026-10-20T18:00:00Z" },
            new() { Id = "finalist", Icon = "trophy", Metal = "s", Name = "Finalist", Condition = "Play the final of a tournament" },
            new() { Id = "grandmaster", Icon = "climb", Metal = "p", Name = "Grandmaster", Condition = "Reach 1900 ELO in 1v1", Ladder = "default", Goal = 1900, Value = 1612 },
            new() { Id = "number_one", Icon = "crown", Metal = "p", Name = "Number One", Condition = "Be first on a ranking of 10 or more" },
            new() { Id = "iron_streak", Icon = "flame", Metal = "p", Name = "Iron Streak", Condition = "Win 10 competitive 1v1 matches in a row", Goal = 10, Value = 10, EarnedAt = "2026-11-20T18:00:00Z" },
            new() { Id = "centurion", Icon = "sword", Metal = "p", Name = "Centurion", Condition = "Win 100 competitive 1v1 matches", Goal = 100, Value = 15 },
            new() { Id = "legion", Icon = "people", Metal = "p", Name = "Legion", Condition = "Win 100 competitive team matches", Goal = 100, Value = 1 },
            new() { Id = "emperor", Icon = "shield", Metal = "p", Name = "Emperor", Condition = "Win 250 matches", Goal = 250, Value = 15 },
            new() { Id = "virtuoso", Icon = "flag", Metal = "p", Name = "Virtuoso", Condition = "Win 100 matches with the same civilization", Goal = 100, Value = 9 },
            new() { Id = "triple_crown", Icon = "trophy", Metal = "p", Name = "Triple Crown", Condition = "Win 3 tournaments", Goal = 3, Value = 0 },
            new() { Id = "living_legend", Icon = "star", Metal = "p", Name = "Living Legend", Condition = "Earn three in four of this game's medals", Goal = 54, Value = 20 },
    };
}
