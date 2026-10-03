using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The Profile's mode cards (design 55d-55f): which card each ladder gets, the header's status
/// line, the current-streak cell and the head-to-head's folding. Every fact is the server's; what
/// is pinned here is how the page reads it.
/// </summary>
[Collection("wpf-and-language")]
public class ProfileModeViewTests
{
    private static LadderStanding Ranked(int rank = 3, int size = 12) => new()
    {
        Rating = 1612, GamesPlayed = 24, LadderRank = rank, LadderSize = size,
        PlacementPlayed = 10, PlacementRequired = 10,
    };

    // ---------------------------------------------------------------- shapes

    [Fact]
    public void EachLadderGetsTheCardItsStateCallsFor()
    {
        Assert.Equal(ProfileModeShape.NoGames, ProfileModeView.ShapeOf(null));
        Assert.Equal(ProfileModeShape.NoGames, ProfileModeView.ShapeOf(new LadderStanding { PlacementRequired = 5 }));
        Assert.Equal(ProfileModeShape.Placement, ProfileModeView.ShapeOf(new LadderStanding
        {
            GamesPlayed = 2, PlacementPlayed = 2, PlacementRequired = 5, LadderRank = 0,
        }));
        Assert.Equal(ProfileModeShape.Ranked, ProfileModeView.ShapeOf(Ranked()));

        var idle = Ranked();
        idle.Inactive = true;
        Assert.Equal(ProfileModeShape.Inactive, ProfileModeView.ShapeOf(idle));
    }

    [Fact]
    public void APlaceOfZeroIsPlacement_EvenWhenTheCountsSaySomethingElse()
    {
        // The server's place is the authority on "on the table or not": 0 is "not yet".
        var odd = Ranked();
        odd.LadderRank = 0;
        Assert.Equal(ProfileModeShape.Placement, ProfileModeView.ShapeOf(odd));
    }

    [Fact]
    public void NoGamesAnywhere_OnlyWhenBothLaddersAreEmpty()
    {
        Assert.True(ProfileModeView.NoGamesAnywhere(null));
        Assert.True(ProfileModeView.NoGamesAnywhere(new LadderStandings()));
        Assert.False(ProfileModeView.NoGamesAnywhere(new LadderStandings { Default = Ranked() }));
    }

    // ---------------------------------------------------------------- the status line

    [Fact]
    public void TheStatusLineNamesEachModesState_AndCapitalisesItsStart()
    {
        WithLanguage(Strings.LangEs, () =>
        {
            // 5th of 12 is Industrial by the share cuts (2 / 3 / 6 / 9). The handoff draws 3rd as
            // Industrial, which the real cuts make Imperial; its numbers are illustrative.
            var solo = ProfileModeView.StatusSegment(Ranked(rank: 5, size: 12), "1v1", null);
            var team = ProfileModeView.StatusSegment(new LadderStanding
            {
                GamesPlayed = 2, PlacementRequired = 5, LadderRank = 0,
            }, "Equipos", null);
            Assert.Equal("Industrial en 1v1 · en posicionamiento en Equipos",
                ProfileModeView.StatusLine(new[] { solo, team }));

            // Only placing in 1v1: the line starts with a capital.
            var placing = ProfileModeView.StatusSegment(new LadderStanding
            {
                GamesPlayed = 6, PlacementRequired = 10, LadderRank = 0,
            }, "1v1", null);
            Assert.Equal("En posicionamiento en 1v1",
                ProfileModeView.StatusLine(new[] { placing, ProfileModeView.StatusSegment(null, "Equipos", null) }));
        });
    }

    [Fact]
    public void AnUnknownPlaceSaysNothingRatherThanGuessAnAge()
    {
        var unknown = Ranked();
        unknown.LadderRank = null;
        Assert.Null(ProfileModeView.StatusSegment(unknown, "1v1", 12));
        Assert.Null(ProfileModeView.StatusLine(new string?[] { null, " " }));
    }

    [Fact]
    public void TheAgeIsCutByTheLaddersOwnSize_FallingBackToTheCommunitysCount()
    {
        var noSize = Ranked(rank: 1, size: 0);
        noSize.LadderSize = null;
        Assert.Equal(RankAges.For(1, 40), ProfileModeView.AgeOf(noSize, 40));
        Assert.Equal(RankAges.For(3, 12), ProfileModeView.AgeOf(Ranked(rank: 3, size: 12), 40));
    }

    // ---------------------------------------------------------------- the streak cell

    [Fact]
    public void TheCurrentStreak_FlameFromThree_PlainBelow_DashAtNone()
    {
        WithLanguage(Strings.LangEs, () =>
        {
            var now = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
            var s = Ranked();

            s.StreakCurrent = 4;
            var flame = ProfileModeView.CurrentStreak(s, now, Strings.LangEs);
            Assert.Equal("🔥4", flame.Text);
            Assert.Equal("MpStreakText", flame.BrushKey);

            s.StreakCurrent = 2;
            Assert.Equal("2", ProfileModeView.CurrentStreak(s, now, Strings.LangEs).Text);

            s.StreakCurrent = 0;
            var none = ProfileModeView.CurrentStreak(s, now, Strings.LangEs);
            Assert.Equal(Strings.Get("MpDash"), none.Text);
            Assert.Null(none.Note);
        });
    }

    [Fact]
    public void AStreakThatRanOutForLackOfPlaySaysWhen()
    {
        WithLanguage(Strings.LangEs, () =>
        {
            var now = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
            var s = Ranked();
            s.StreakCurrent = 0;
            s.StreakEndedAt = "2026-09-18T12:00:00.000Z";
            Assert.Equal("Se cortó el 18 sep: 14 días sin jugar",
                ProfileModeView.CurrentStreak(s, now, Strings.LangEs).Note);
        });
    }

    // ---------------------------------------------------------------- head to head

    [Fact]
    public void TheHeadToHeadShowsFour_ThenEverythingTheServerSent()
    {
        var s = Ranked();
        s.HeadToHead = Enumerable.Range(1, 9).Select(i => new HeadToHeadEntry
        {
            UserId = "u" + i, DisplayName = "P" + i, Wins = i, Losses = 1,
        }).ToList();
        s.HeadToHeadTotal = 9;

        Assert.Equal(4, ProfileModeView.HeadToHeadRows(s, expanded: false).Count);
        Assert.Equal(9, ProfileModeView.HeadToHeadRows(s, expanded: true).Count);
        Assert.Equal(9, ProfileModeView.HeadToHeadTotal(s));
        // The server's order is kept: it already sorts by matches played.
        Assert.Equal("P1", ProfileModeView.HeadToHeadRows(s, false)[0].DisplayName);
    }

    [Fact]
    public void TheHeadToHeadOpensOnTeamsOnlyWhenOneVersusOneHasNobody()
    {
        var withSolo = new LadderStandings
        {
            Default = new LadderStanding { HeadToHead = { new HeadToHeadEntry { Wins = 1 } } },
            Team = new LadderStanding { HeadToHead = { new HeadToHeadEntry { Wins = 1 } } },
        };
        Assert.False(ProfileModeView.HeadToHeadOpensOnTeams(withSolo));

        var teamsOnly = new LadderStandings
        {
            Default = new LadderStanding(),
            Team = new LadderStanding { HeadToHead = { new HeadToHeadEntry { Wins = 1 } } },
        };
        Assert.True(ProfileModeView.HeadToHeadOpensOnTeams(teamsOnly));
    }

    [Fact]
    public void TheBalanceBarIsTheShareWon()
    {
        Assert.Equal(9.0 / 11, ProfileModeView.WinShare(new HeadToHeadEntry { Wins = 9, Losses = 2 }), 6);
        Assert.Equal(0, ProfileModeView.WinShare(new HeadToHeadEntry()));
    }

    // ---------------------------------------------------------------- relative days

    [Fact]
    public void RelativeDays_TodayYesterdayDaysWeeksMonthsYears()
    {
        WithLanguage(Strings.LangEs, () =>
        {
            var now = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
            Assert.Equal("hoy", RelativeDay.Format(now.AddHours(-1), now));
            Assert.Equal("ayer", RelativeDay.Format(now.AddDays(-1), now));
            Assert.Equal("hace 2 días", RelativeDay.Format(now.AddDays(-2), now));
            Assert.Equal("hace 1 semana", RelativeDay.Format(now.AddDays(-9), now));
            Assert.Equal("hace 3 semanas", RelativeDay.Format(now.AddDays(-21), now));
            Assert.Equal("hace 1 mes", RelativeDay.Format(now.AddDays(-40), now));
            Assert.Equal("hace 5 meses", RelativeDay.Format(now.AddDays(-150), now));
            Assert.Equal("hace 2 años", RelativeDay.Format(now.AddDays(-800), now));
            // A server clock ahead of ours is "today", never "-1 days ago".
            Assert.Equal("hoy", RelativeDay.Format(now.AddDays(2), now));
            Assert.Null(RelativeDay.Format((string?)null, now));
        });
        WithLanguage(Strings.LangEn, () =>
        {
            var now = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
            Assert.Equal("2 days ago", RelativeDay.Format(now.AddDays(-2), now));
            Assert.Equal("3 weeks ago", RelativeDay.Format(now.AddDays(-21), now));
        });
    }

    private static void WithLanguage(string language, Action body)
    {
        var previous = Strings.Language;
        Strings.SetLanguage(language);
        try { body(); }
        finally { Strings.SetLanguage(previous); }
    }
}
