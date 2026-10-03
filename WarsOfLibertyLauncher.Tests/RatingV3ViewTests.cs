using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The pure halves of the rating screens (design handoff 55): placement, streaks, anti-farm
/// wording, ordinals, name lists, the Start gate, the odds, short dates and the strings behind
/// them. The server decides every one of these facts; what is pinned here is how the launcher
/// reads and words them.
/// </summary>
[Collection("wpf-and-language")]
public class RatingV3ViewTests
{
    // ---------------------------------------------------------------- placement

    [Fact]
    public void Placement_IsTheServersCount_NineOfTenIsPlacement_TenIsRanked()
    {
        Assert.True(PlacementView.InPlacement(9, 10));
        Assert.False(PlacementView.InPlacement(10, 10));
        Assert.True(PlacementView.InPlacement(4, 5));
        Assert.False(PlacementView.InPlacement(5, 5));
    }

    [Fact]
    public void Placement_NothingKnownMarksNothing()
    {
        // An older backend sends no counts: nobody gets a "?" on a guess.
        Assert.False(PlacementView.InPlacement(null, null));
        Assert.False(PlacementView.InPlacement(3, null));
        Assert.False(PlacementView.InPlacement(0, 10), "no rated match is not placement");
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_OthersSeeGreyProgress_OnlyTheOwnBarHasResults()
    {
        // Another player's results are never sent, so his bar can only be played/pending.
        var theirs = PlacementView.Segments(3, 5, ownResults: null);
        Assert.Equal(new[]
        {
            PlacementView.Segment.Played, PlacementView.Segment.Played, PlacementView.Segment.Played,
            PlacementView.Segment.Pending, PlacementView.Segment.Pending,
        }, theirs);

        var mine = PlacementView.Segments(3, 5, new List<PlacementResultEntry>
        {
            new() { Result = 1 }, new() { Result = 0 }, new() { Result = 1 },
        });
        Assert.Equal(new[]
        {
            PlacementView.Segment.Win, PlacementView.Segment.Loss, PlacementView.Segment.Win,
            PlacementView.Segment.Pending, PlacementView.Segment.Pending,
        }, mine);
    }

    [Fact]
    public void Placement_RemainingIsNeverNegative()
    {
        Assert.Equal(3, PlacementView.Remaining(7, 10));
        Assert.Equal(0, PlacementView.Remaining(12, 10));
    }

    // ---------------------------------------------------------------- streaks

    [Fact]
    public void Streak_FlameFromThree_PlainForOneOrTwo_NothingAtZero()
    {
        Assert.Equal(StreakView.Look.None, StreakView.LookOf(0));
        Assert.Equal(StreakView.Look.Plain, StreakView.LookOf(1));
        Assert.Equal(StreakView.Look.Plain, StreakView.LookOf(2));
        Assert.Equal(StreakView.Look.Flame, StreakView.LookOf(3));
        Assert.False(StreakView.ShowsPill(2));
        Assert.True(StreakView.ShowsPill(3));
    }

    [Fact]
    public void Streak_AnExpiredStreakCarriesItsEndDate()
    {
        Assert.NotNull(StreakView.ExpiredOn("2026-09-18T12:00:00Z"));
        Assert.Null(StreakView.ExpiredOn(null));
    }

    // ---------------------------------------------------------------- anti-farm

    [Fact]
    public void AntiFarm_PercentAndWhetherItWasDiscounted()
    {
        Assert.Equal(40, AntiFarmView.Percent(0.4));
        Assert.Equal(20, AntiFarmView.Percent(0.2));
        Assert.True(AntiFarmView.IsDiscounted(0.9));
        Assert.False(AntiFarmView.IsDiscounted(1.0));
        Assert.False(AntiFarmView.IsDiscounted(null), "not rated is not discounted");
    }

    [Fact]
    public void AntiFarm_TheTextsNeverNameAnyoneAsACheat()
    {
        var prev = Strings.Language;
        try
        {
            Strings.Language = Strings.LangEs;
            Assert.Equal(
                "+5 (40 %): 8.ª victoria seguida contra este rival. Vuelve a la normalidad si él gana una, o se recupera un 10 % por cada día sin jugar entre ustedes.",
                AntiFarmView.WinText("+5", 0.4, 8));
            Assert.Equal(
                "−4 (40 %): Pedro te ganó 8 veces seguidas. Vuelve a la normalidad si le ganas una, o se recupera un 10 % por cada día sin jugar entre ustedes.",
                AntiFarmView.LossText("−4", 0.4, "Pedro", 8));
            Strings.Language = Strings.LangEn;
            Assert.StartsWith("+5 (40%): 8th win in a row", AntiFarmView.WinText("+5", 0.4, 8));
            Assert.Equal("· 40%", AntiFarmView.HistorySuffix(0.4));
        }
        finally { Strings.Language = prev; }
    }

    [Fact]
    public void Ordinals_EnglishTeensAndSpanishFeminine()
    {
        Assert.Equal("1st", Ordinals.English(1));
        Assert.Equal("2nd", Ordinals.English(2));
        Assert.Equal("3rd", Ordinals.English(3));
        Assert.Equal("8th", Ordinals.English(8));
        Assert.Equal("11th", Ordinals.English(11));
        Assert.Equal("12th", Ordinals.English(12));
        Assert.Equal("13th", Ordinals.English(13));
        Assert.Equal("21st", Ordinals.English(21));
        Assert.Equal("112th", Ordinals.English(112));
        Assert.Equal("8.ª", Ordinals.Spanish(8));
    }

    // ---------------------------------------------------------------- names

    [Fact]
    public void NameList_TwoAndThreeInBothLanguages()
    {
        Assert.Equal("Ana y Luis", NameList.Join(new[] { "Ana", "Luis" }, Strings.LangEs));
        Assert.Equal("Ana, Luis y Marta", NameList.Join(new[] { "Ana", "Luis", "Marta" }, Strings.LangEs));
        Assert.Equal("Ana and Luis", NameList.Join(new[] { "Ana", "Luis" }, Strings.LangEn));
        Assert.Equal("Ana, Luis and Marta", NameList.Join(new[] { "Ana", "Luis", "Marta" }, Strings.LangEn));
        Assert.Equal("Ana", NameList.Join(new[] { "Ana" }, Strings.LangEs));
        Assert.Equal("", NameList.Join(Array.Empty<string>(), Strings.LangEs));
    }

    // ---------------------------------------------------------------- the Start gate

    private static StartGate.Player P(string name, int? team) => new(name.ToLowerInvariant(), name, team);

    [Fact]
    public void THE_ONE_THAT_MATTERS_StartGate_RefusesInTheServersOrder()
    {
        // Not full: refused for that, even though somebody also has no team.
        var notFull = StartGate.Evaluate(true, 4, new[] { P("Ana", 1), P("Luis", null), P("Pedro", 2) });
        Assert.Equal(StartGate.Reason.MissingPlayers, notFull.Reason);
        Assert.Equal(3, notFull.Have);
        Assert.Equal(4, notFull.Need);

        // Full, somebody without a team: that, even though the teams are also uneven.
        var noTeam = StartGate.Evaluate(true, 4, new[] { P("Ana", 1), P("Luis", 1), P("Pedro", 1), P("Sara", null) });
        Assert.Equal(StartGate.Reason.PlayerWithoutTeam, noTeam.Reason);
        Assert.Equal(new[] { "Sara" }, noTeam.WithoutTeam);

        var uneven = StartGate.Evaluate(true, 4, new[] { P("Ana", 1), P("Luis", 1), P("Pedro", 1), P("Sara", 2) });
        Assert.Equal(StartGate.Reason.UnevenTeams, uneven.Reason);
        Assert.Equal((3, 1), (uneven.Team1, uneven.Team2));

        Assert.True(StartGate.Evaluate(true, 4, new[] { P("Ana", 1), P("Luis", 2), P("Pedro", 1), P("Sara", 2) }).CanStart);
    }

    [Fact]
    public void StartGate_CasualRoomsAndOldLaunchersAreNeverBlockedOnTeams()
    {
        Assert.True(StartGate.Evaluate(false, 4, new[] { P("Ana", 1), P("Luis", 1) }).CanStart);
        // Somebody's launcher cannot pick a team: only the headcount applies.
        Assert.True(StartGate.Evaluate(true, 4,
            new[] { P("Ana", null), P("Luis", null), P("Pedro", null), P("Sara", null) },
            teamsSupported: false).CanStart);
        // A competitive 1v1 needs its second seat.
        Assert.Equal(StartGate.Reason.MissingPlayers, StartGate.Evaluate(true, 2, new[] { P("Ana", null) }).Reason);
    }

    // ---------------------------------------------------------------- odds

    [Fact]
    public void Odds_AreReadFromTheServer_NeverComputed()
    {
        var odds = JsonSerializer.Deserialize<RoomOdds>(
            """{"mode":"team","teams":{"1":58,"2":42},"players":null}""")!;
        Assert.Equal((58, 42), WinOddsView.ForTeams(odds));
        Assert.Null(WinOddsView.ForPlayer(odds, "ana"));

        var one = JsonSerializer.Deserialize<RoomOdds>(
            """{"mode":"default","teams":null,"players":{"ana":62,"luis":38}}""")!;
        Assert.Equal(62, WinOddsView.ForPlayer(one, "ana"));
        Assert.Null(WinOddsView.ForTeams(one));
        Assert.Null(WinOddsView.ForTeams(null));
    }

    // ---------------------------------------------------------------- dates

    [Fact]
    public void ShortDate_DayAndMonth_WithTheYearOnlyWhenItIsAnotherYear()
    {
        var now = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
        var aug = new DateTimeOffset(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);
        var lastYear = new DateTimeOffset(2025, 8, 12, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal("12 ago", ShortDate.Format(aug, now, Strings.LangEs));
        Assert.Equal("12 ago 2025", ShortDate.Format(lastYear, now, Strings.LangEs));
        Assert.Equal("Aug 12", ShortDate.Format(aug, now, Strings.LangEn));
        Assert.Equal("Aug 12, 2025", ShortDate.Format(lastYear, now, Strings.LangEn));
    }

    // ---------------------------------------------------------------- reasons

    [Fact]
    public void TheNewUnratedReasonsHaveTheirOwnWords()
    {
        Assert.Equal("MpResultUnratedTeamsMismatch", MatchOutcomeView.UnratedNoteKey("teams_mismatch"));
        Assert.Equal("MpResultNewAccount", MatchOutcomeView.UnratedNoteKey("new_account_short"));
    }

    // ---------------------------------------------------------------- strings

    [Fact]
    public void EveryRatingStringHasBothLanguages_AndTheTablesDoNotCollide()
    {
        // The rating table is merged into the main one at startup with Dictionary.Add, so a
        // duplicate key throws the type initializer — this line is what would explode.
        Assert.NotEqual("MpRankCountSummary", Strings.Get("MpRankCountSummary"));
        foreach (var key in new[]
        {
            "MpRankCountSummary", "MpPlacementProgress", "MpRankFootRule", "MpStreakTipTitle",
            "MpProfilePeak", "MpH2HSeeAll", "MpWinProb1v1", "MpWinProbTeams", "MpStartBlockedNoTeamPl",
            "MpCountdownTeams", "MpResultFarmWin", "MpHistReasonTeams", "MpHlEmpty", "MpRefundBody",
        })
        {
            Assert.NotEqual(key, Strings.GetIn(Strings.LangEn, key));
            Assert.NotEqual(key, Strings.GetIn(Strings.LangEs, key));
        }
    }

    [Fact]
    public void TheNewDtosReadTheServersRealFieldNames()
    {
        var c = JsonSerializer.Deserialize<CommunityStats>("""
            {"placement_required":{"default":10,"team":5},
             "leaderboard":[{"rank":1,"user_id":"a","rating":1700,"rd":80,"rated_wins":10,"rated_losses":2,"inactive":true,"last_rated_at":"2026-08-01T00:00:00Z"}],
             "leaderboard_placement":[{"user_id":"b","display_name":"Beto","rating":1500,"rd":300,"placement_played":6,"placement_required":10}],
             "placement_players":1,
             "monthly_highlights":{"current":{"month":"2026-10","so_far":true,"total_rated":3,"min_matches":5,
               "biggest_climb":{"default":null,"team":{"user_id":"a","display_name":"Ana","points":80,"matches":5}},
               "most_matches":{"user_id":"a","display_name":"Ana","matches":3},
               "best_streak":{"default":{"user_id":"a","display_name":"Ana","wins":3},"team":null}}}}
            """)!;
        Assert.Equal(10, c.PlacementRequired!.Default);
        Assert.True(c.Leaderboard[0].Inactive);
        Assert.Equal(10, c.Leaderboard[0].RecordWins);
        Assert.Equal(6, c.LeaderboardPlacement![0].PlacementPlayed);
        Assert.Equal(80, c.MonthlyHighlights!.Current!.BiggestClimb!.Team!.Points);

        var elo = JsonSerializer.Deserialize<EloSnapshot>("""
            {"rating":1700,"ladders":{"default":{"rating":1700,"games_played":4,"placement_played":4,"placement_required":10,
              "streak_current":3,"head_to_head":[{"user_id":"b","display_name":"Beto","wins":7,"losses":4,"last_at":"x"}],
              "head_to_head_total":1,"placement_results":[{"match_id":"m","result":1,"rating_after":1600,"at":"x"}]}},
             "refunds":[{"refund_id":"r","mode":"team","points":34,"matches":2,"seen":false}]}
            """)!;
        Assert.True(elo.Ladders!.Default!.InPlacement);
        Assert.Equal(11, elo.Ladders.Default.HeadToHead[0].Games);
        Assert.True(elo.Refunds![0].IsTeam);

        var row = JsonSerializer.Deserialize<MatchHistoryRow>("""
            {"id":"m","elo_factor":0.4,"farm_streak":8,"tournament":{"id":"t","name":"Copa","round":2,"rounds_total":3},
             "ingame_teams":[["a","b"],["c","d"]],"room_teams":[["a","c"],["b","d"]],"placement_index":3,"placement_completed":false}
            """)!;
        Assert.Equal(0.4, row.EloFactor);
        Assert.Equal("Copa", row.Tournament!.Name);
        Assert.Equal("c", row.RoomTeams![0][1]);

        var flags = JsonSerializer.Deserialize<WsRoomMemberFlags>("""{"team":2,"role":"player","inPlacementTeam":true}""")!;
        Assert.Equal(2, flags.Team);
        Assert.True(flags.InPlacementTeam);
    }
}
