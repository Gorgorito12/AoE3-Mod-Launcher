using System;
using System.Globalization;
using System.Linq;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The community block's data strip, decided without WPF (design 60): which facts there are, in
/// what order, and what their labels say. The rule's whole point is the refusals — a fact with
/// nothing behind it is left out, never a dash — and that each window comes from the payload.
/// </summary>
public class ActivityFactsViewTests
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es");
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en");

    /// <summary>The key and its arguments, so the label's inputs are visible to the assertions.</summary>
    private static string Label(string key, object[] args) => args.Length == 0 ? key : key + ":" + string.Join(",", args);

    private static HighlightPlayer P(string name, int? points = null, int? matches = null, int? wins = null)
        => new() { UserId = name, DisplayName = name, Points = points, Matches = matches, Wins = wins };

    private static MonthHighlights Month(int total = 40) => new()
    {
        Month = "2026-10",
        StartsAt = "2026-10-01T06:00:00.000Z",
        TotalRated = total,
        BiggestClimb = new HighlightPerMode { Default = P("Siux", points: 58), Team = P("Pedro", points: 96) },
        MostMatches = P("Aluclown", matches: 41, wins: 23),
        BestStreak = new HighlightPerMode { Default = P("Geaf_Argento", wins: 9) },
        // The four highlights that live in Ranking › Highlights: present, and never a fact.
        MostWins = P("Aluclown", wins: 23, matches: 31),
        BestWinRate = new HighlightPlayer { UserId = "k", DisplayName = "Kaiser", Wins = 14, Matches = 17, Percent = 82 },
        TopCiv = new HighlightCiv { ModId = "wol", Civ = "Germans", Picks = 27, Wins = 15 },
        BiggestUpset = new HighlightUpset { Gap = 238, Winners = new() { P("Siux") }, Losers = new() { P("Geaf") } },
    };

    private static CommunityTotals Totals() => new()
    {
        WindowDays = 30,
        Matches = 147,
        PlayersWindowDays = 7,
        Players = 18,
        TopMap = "ESOC_Fertile_Crescent",
    };

    /// <summary>
    /// THE ONE THAT MATTERS: the mockup's facts in its order — the climb first when the month has
    /// one — and none of the four highlights that live in Ranking › Highlights.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheMockupsFactsInItsOrderAndNothingElse()
    {
        var facts = ActivityFactsView.Build(Month(), Totals(), En, Label);
        Assert.Equal(new[]
        {
            ActivityFactKind.TopClimb, ActivityFactKind.MostMatches, ActivityFactKind.BestStreak,
            ActivityFactKind.Matches, ActivityFactKind.Players, ActivityFactKind.MostPlayed,
        }, facts.Select(f => f.Kind));
        Assert.Equal(Enum.GetValues<ActivityFactKind>().Length, facts.Count);
    }

    /// <summary>Each fact's value: the bigger climb of the two ladders, the streak as a pill, the map without underscores.</summary>
    [Fact]
    public void EachFactSaysWhatItIsAbout()
    {
        var facts = ActivityFactsView.Build(Month(), Totals(), En, Label).ToDictionary(f => f.Kind);

        Assert.Equal(("Pedro", "+96"), (facts[ActivityFactKind.TopClimb].Name, facts[ActivityFactKind.TopClimb].Figure));
        Assert.Equal(("Aluclown", "41"), (facts[ActivityFactKind.MostMatches].Name, facts[ActivityFactKind.MostMatches].Figure));
        var streak = facts[ActivityFactKind.BestStreak];
        Assert.Equal(("Geaf_Argento", 9), (streak.Name, streak.Streak));
        Assert.Null(streak.Figure);
        Assert.Equal("147", facts[ActivityFactKind.Matches].Figure);
        Assert.Null(facts[ActivityFactKind.Matches].Name);
        Assert.Equal("18", facts[ActivityFactKind.Players].Figure);
        Assert.Equal("ESOC Fertile Crescent", facts[ActivityFactKind.MostPlayed].Name);
    }

    /// <summary>The month's labels carry its abbreviation; the windows are the payload's own.</summary>
    [Fact]
    public void TheLabelsCarryTheMonthAndThePayloadsWindows()
    {
        var totals = Totals();
        totals.WindowDays = 14;
        totals.PlayersWindowDays = 3;
        var facts = ActivityFactsView.Build(Month(), totals, En, Label).ToDictionary(f => f.Kind);

        Assert.Equal("MpFactTopGain:Oct", facts[ActivityFactKind.TopClimb].Label);
        Assert.Equal("MpFactMostMatches:Oct", facts[ActivityFactKind.MostMatches].Label);
        Assert.Equal("MpFactBestStreak:Oct", facts[ActivityFactKind.BestStreak].Label);
        Assert.Equal("MpFactMatches:14", facts[ActivityFactKind.Matches].Label);
        Assert.Equal("MpFactPlayers:3", facts[ActivityFactKind.Players].Label);
        Assert.Equal("MpFactMostPlayed", facts[ActivityFactKind.MostPlayed].Label);
    }

    /// <summary>
    /// The abbreviation is the launcher's language without the full stop some cultures add — in a
    /// label in capitals "OCT." reads as the end of a sentence.
    /// </summary>
    [Fact]
    public void TheMonthIsAbbreviatedWithoutAFullStop()
    {
        Assert.Equal("oct", ActivityFactsView.MonthAbbreviation("2026-10", Es));
        Assert.Equal("Oct", ActivityFactsView.MonthAbbreviation("2026-10", En));
        Assert.DoesNotContain(".", ActivityFactsView.MonthAbbreviation("2026-09", Es));
        Assert.Null(ActivityFactsView.MonthAbbreviation("not-a-month", En));
        Assert.Null(ActivityFactsView.MonthAbbreviation(null, En));
    }

    /// <summary>A month with too little behind it gives no month facts; the community's figures remain.</summary>
    [Fact]
    public void AThinMonthLeavesOnlyTheCommunitysFigures()
    {
        var facts = ActivityFactsView.Build(Month(HighlightsView.MinMonthMatches - 1), Totals(), En, Label);
        Assert.Equal(new[] { ActivityFactKind.Matches, ActivityFactKind.Players, ActivityFactKind.MostPlayed },
            facts.Select(f => f.Kind));

        Assert.Equal(3, ActivityFactsView.Build(null, Totals(), En, Label).Count);
    }

    /// <summary>
    /// The refusals: no climb (or a climb of nothing), no streak, a window of zero, no map, no
    /// totals at all — each fact simply leaves, and nothing stands in for it.
    /// </summary>
    [Fact]
    public void AFactWithNothingBehindItIsLeftOut()
    {
        var month = Month();
        month.BiggestClimb = new HighlightPerMode { Default = P("Siux", points: 0) };
        month.BestStreak = null;
        var totals = Totals();
        totals.PlayersWindowDays = 0;
        totals.TopMap = "  ";

        var facts = ActivityFactsView.Build(month, totals, En, Label);
        Assert.Equal(new[] { ActivityFactKind.MostMatches, ActivityFactKind.Matches }, facts.Select(f => f.Kind));

        totals.WindowDays = 0;
        Assert.Equal(new[] { ActivityFactKind.MostMatches },
            ActivityFactsView.Build(month, totals, En, Label).Select(f => f.Kind));
        Assert.Equal(new[] { ActivityFactKind.MostMatches },
            ActivityFactsView.Build(month, null, En, Label).Select(f => f.Kind));
        Assert.Empty(ActivityFactsView.Build(null, null, En, Label));
    }

    /// <summary>A player with no name is "?", never an empty cell.</summary>
    [Fact]
    public void ANamelessPlayerReadsAsAQuestionMark()
    {
        var month = Month();
        month.MostMatches = new HighlightPlayer { UserId = "x", DisplayName = " ", Matches = 12 };
        var fact = ActivityFactsView.Build(month, null, En, Label).Single(f => f.Kind == ActivityFactKind.MostMatches);
        Assert.Equal("?", fact.Name);
    }
}
