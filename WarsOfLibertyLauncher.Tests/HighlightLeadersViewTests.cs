using System;
using System.Globalization;
using System.Linq;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Ranking › Highlights, decided without WPF: which cards there are, in what order, and what
/// each row says. The SERVER picks the players; this only words them — and leaves out a card
/// with nobody in it, as the strip always did.
/// </summary>
[Collection("wpf-and-language")]
public class HighlightLeadersViewTests
{
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en");

    private static System.Collections.Generic.IReadOnlyList<LeaderCard> Cards(
        MonthHighlights? month, bool climbTeam = false, bool streakTeam = false)
        => HighlightLeadersView.Cards(month, climbTeam, streakTeam, En, (k, a) => Strings.Format(k, a));

    private static T InEnglish<T>(Func<T> body)
    {
        var previous = Strings.Language;
        Strings.SetLanguage(Strings.LangEn);
        try { return body(); }
        finally { Strings.SetLanguage(previous); }
    }

    /// <summary>
    /// THE ONE THAT MATTERS: all seven highlights, in the strip's priority order, each with at
    /// most five rows numbered from one.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_SevenCardsInPriorityOrderOfAtMostFive()
    {
        var cards = InEnglish(() => Cards(EloDemoData.Highlights().Current));
        Assert.Equal(new[]
        {
            HighlightCellKind.TopClimb, HighlightCellKind.MostWins, HighlightCellKind.MostMatches,
            HighlightCellKind.BestStreak, HighlightCellKind.BestWinRate, HighlightCellKind.BiggestUpset,
            HighlightCellKind.TopCiv,
        }, cards.Select(c => c.Kind));
        Assert.All(cards, c =>
        {
            Assert.InRange(c.Rows.Count, 1, 5);
            Assert.Equal(Enumerable.Range(1, c.Rows.Count), c.Rows.Select(r => r.Place));
        });
    }

    /// <summary>A month the server sent without its lists, or no month at all, gives no cards.</summary>
    [Fact]
    public void NoListsNoCards()
    {
        var month = EloDemoData.Highlights().Current!;
        month.Leaders = null;
        Assert.Empty(Cards(month));
        Assert.Empty(Cards(null));
    }

    /// <summary>
    /// A card with nobody in it is not drawn (56a). Last month's sample has no upset, and an empty
    /// team ladder, so the climb card shows 1v1 and offers no switch.
    /// </summary>
    [Fact]
    public void ACardWithNobodyIsLeftOutAndAnEmptyLadderOffersNoSwitch()
    {
        var cards = InEnglish(() => Cards(EloDemoData.Highlights().Previous, climbTeam: true));
        Assert.DoesNotContain(cards, c => c.Kind == HighlightCellKind.BiggestUpset);
        var climb = cards.Single(c => c.Kind == HighlightCellKind.TopClimb);
        Assert.False(climb.Team);           // asked for Teams, which is empty: 1v1 instead
        Assert.False(climb.HasBothLadders);

        // Entries with nothing behind them are nobody too.
        var month = EloDemoData.Highlights().Current!;
        month.Leaders!.MostWins = new() { new HighlightPlayer { UserId = "a", DisplayName = "Ana", Wins = 0, Matches = 4 } };
        month.Leaders.TopCiv = new() { new HighlightCiv { ModId = "wol", Civ = " ", Picks = 6 } };
        var trimmed = InEnglish(() => Cards(month));
        Assert.DoesNotContain(trimmed, c => c.Kind == HighlightCellKind.MostWins);
        Assert.DoesNotContain(trimmed, c => c.Kind == HighlightCellKind.TopCiv);
    }

    /// <summary>Climb and streak are per ladder: the switch picks one, and both being there offers it.</summary>
    [Fact]
    public void ClimbAndStreakFollowTheLadderAskedFor()
    {
        var month = EloDemoData.Highlights().Current;
        var solo = InEnglish(() => Cards(month)).Single(c => c.Kind == HighlightCellKind.TopClimb);
        Assert.False(solo.Team);
        Assert.True(solo.HasBothLadders);
        Assert.Equal("Siux", solo.Rows[0].Name);

        var team = InEnglish(() => Cards(month, climbTeam: true)).Single(c => c.Kind == HighlightCellKind.TopClimb);
        Assert.True(team.Team);
        Assert.Equal("Pedro", team.Rows[0].Name);

        var streak = InEnglish(() => Cards(month, streakTeam: true)).Single(c => c.Kind == HighlightCellKind.BestStreak);
        Assert.True(streak.Team);
        Assert.Equal(("Luis", 4), (streak.Rows[0].Name, streak.Rows[0].Streak));
        Assert.Null(streak.Rows[0].Figure);
    }

    /// <summary>
    /// What each row says, in English: the climb with its ratings (never grouped — the table
    /// prints 1460, so must this), wins out of matches with the share, the civilization with its
    /// wins, and the upset naming who was beaten.
    /// </summary>
    [Fact]
    public void EachRowSaysWhatItIsAbout()
    {
        var cards = InEnglish(() => Cards(EloDemoData.Highlights().Current)).ToDictionary(c => c.Kind);

        var climb = cards[HighlightCellKind.TopClimb].Rows[0];
        Assert.Equal(("+58", true, "1402 → 1460 · 14 matches"), (climb.Figure, climb.Positive, climb.Detail));

        var wins = cards[HighlightCellKind.MostWins].Rows[0];
        Assert.Equal(("Aluclown", "23", "out of 31 matches · 74 %"), (wins.Name, wins.Figure, wins.Detail));

        var matches = cards[HighlightCellKind.MostMatches].Rows[1];
        Assert.Equal(("31", "23 won"), (matches.Figure, matches.Detail));

        var rate = cards[HighlightCellKind.BestWinRate].Rows[0];
        Assert.Equal(("82 %", "14 of 17 matches"), (rate.Figure, rate.Detail));

        var civ = cards[HighlightCellKind.TopCiv].Rows[0];
        Assert.Equal(("Germans", "27", "15 won · 56 %", "wol"), (civ.Name, civ.Figure, civ.Detail, civ.CivModId));
        Assert.Empty(civ.UserIds);
        Assert.Null(civ.AvatarUrl);

        var upset = cards[HighlightCellKind.BiggestUpset].Rows[0];
        Assert.Equal(("Siux", "+238", "beat Geaf_Argento · 1402 vs 1640"), (upset.Name, upset.Figure, upset.Detail));
    }

    /// <summary>A side that won an upset is one row naming everybody on it, and marks all of them.</summary>
    [Fact]
    public void ATeamUpsetIsOneRowNamingTheWholeSide()
    {
        var cards = InEnglish(() => Cards(EloDemoData.Highlights().Current));
        var team = cards.Single(c => c.Kind == HighlightCellKind.BiggestUpset).Rows[1];
        Assert.Equal("Pedro and Sara", team.Name);
        Assert.Equal(2, team.UserIds.Count);
        Assert.Equal("beat Kaiser and Luis · 1496 vs 1617", team.Detail);
    }

    /// <summary>
    /// The rules under the titles quote the server's thresholds — and a threshold the server did
    /// not send is no rule at all, never an invented number.
    /// </summary>
    [Fact]
    public void TheRulesQuoteTheServersThresholdsOrSayNothing()
    {
        var month = EloDemoData.Highlights().Current!;
        var cards = InEnglish(() => Cards(month)).ToDictionary(c => c.Kind);
        Assert.Contains("10", cards[HighlightCellKind.BestWinRate].Rule);
        Assert.Contains("3", cards[HighlightCellKind.TopCiv].Rule);

        month.MinRateMatches = 0;
        month.MinCivPicks = 0;
        month.MinMatches = 0;
        var bare = InEnglish(() => Cards(month)).ToDictionary(c => c.Kind);
        Assert.Null(bare[HighlightCellKind.BestWinRate].Rule);
        Assert.Null(bare[HighlightCellKind.TopCiv].Rule);
        Assert.Null(bare[HighlightCellKind.TopClimb].Rule);
        Assert.NotNull(bare[HighlightCellKind.MostWins].Rule);   // a rule with no number in it stays
    }

    /// <summary>The server sends five; a sixth would never be drawn.</summary>
    [Fact]
    public void NeverMoreThanFiveRows()
    {
        var month = EloDemoData.Highlights().Current!;
        month.Leaders!.MostMatches = Enumerable.Range(1, 8)
            .Select(i => new HighlightPlayer { UserId = "u" + i, DisplayName = "P" + i, Matches = 40 - i, Wins = 10 })
            .ToList();
        var card = InEnglish(() => Cards(month)).Single(c => c.Kind == HighlightCellKind.MostMatches);
        Assert.Equal(5, card.Rows.Count);
        Assert.Equal("P5", card.Rows[^1].Name);
    }
}
