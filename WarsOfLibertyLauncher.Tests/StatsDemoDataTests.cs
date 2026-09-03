using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// What the Statistics preview has to keep demonstrating. Run: <c>dotnet test</c>.
///
/// <para>Same purpose as <see cref="TournamentDemoDataTests"/> and the same failure mode: a
/// fixture like this does not break, it decays. Somebody tidies the numbers, every row lands
/// on the same side of the sample bar, and the page still paints beautifully while showing
/// none of the states it was built to show.</para>
///
/// <para>It matters more here than it does for the bracket, because the filled page CANNOT be
/// reached by playing: the civilization table needs hundreds of rated matches carrying a
/// civilization and this community has none. If this fixture stops covering a case, that case
/// has no other way of being seen at all.</para>
/// </summary>
public class StatsDemoDataTests
{
    private const string Wol = StatsDemoData.PrimaryModId;
    private const string Other = StatsDemoData.SecondModId;

    // ---------------------------------------------------------------- the ladder scope

    [Fact]
    public void THE_ONE_THAT_MATTERS_TheTwoLaddersAnswerDifferently()
    {
        // Same argument as the two mods above: a mode filter cannot be verified against a
        // fixture that answers the same thing under both switches. If the launcher ever stopped
        // sending ?mode=, or the server stopped reading it, the page would look identical and
        // this is the only place that would notice.
        var solo = StatsDemoData.CivStats(Wol);
        var team = StatsDemoData.CivStats(Wol, "team");

        Assert.NotEqual(solo.Civs.Count, team.Civs.Count);
        Assert.NotEqual(solo.RatedMatches, team.RatedMatches);

        var soloTotals = StatsDemoData.Community(Wol).Totals!;
        var teamTotals = StatsDemoData.Community(Wol, "team").Totals!;
        Assert.NotEqual(soloTotals.Matches, teamTotals.Matches);
        Assert.NotEqual(soloTotals.Rated, teamTotals.Rated);
    }

    [Fact]
    public void AlliesExistOnlyInTeamGames()
    {
        // In a 1v1 nobody has an ally, so an allies table there would be a heading over an
        // empty box - and worse, would suggest the query ran and found nothing.
        Assert.Empty(StatsDemoData.Matchups(Wol).Allies ?? new List<MatchupEntry>());

        var team = StatsDemoData.Matchups(Wol, "team");
        Assert.NotEmpty(team.Allies!);

        // And they are not the rivals over again: a fixture that repeated one list in the other
        // would pass a query joined on the wrong side of the match.
        Assert.NotEqual(
            team.Matchups.Select(m => $"{m.CivA}|{m.CivB}|{m.Played}").ToList(),
            team.Allies!.Select(m => $"{m.CivA}|{m.CivB}|{m.Played}").ToList());
    }

    [Fact]
    public void TheFormatSplitIsTeamOnlyAndNeverHoldsAFourVersusFour()
    {
        Assert.Empty(StatsDemoData.Community(Wol).Totals!.TeamFormats!);

        var formats = StatsDemoData.Community(Wol, "team").Totals!.TeamFormats!;
        Assert.NotEmpty(formats);

        // `matchShape` accepts 2, 4 or 6 participants and nothing else, so an eight-player
        // match never carries rating_mode 'team' and can never reach this card. A fixture with
        // one in it would be drawing a row the real data cannot produce.
        Assert.All(formats, f => Assert.True(f.Players == 4 || f.Players == 6));
        Assert.All(formats, f => Assert.True(f.Matches > 0));
    }

    [Fact]
    public void TheSecondModHasNoTeamData_WhichIsWhatHidesTheSwitch()
    {
        // The asymmetry IS the fixture. The switch is meant to disappear for a mod with no team
        // games, and a preview where both mods had them could not show that happening.
        Assert.True(StatsDemoData.HasTeamData(Wol));
        Assert.False(StatsDemoData.HasTeamData(Other));
    }

    [Fact]
    public void DeckCardsAreRealInternalNames()
    {
        // They have to be internal names and not display names: the whole point of the deck
        // table's resolver is to turn one into the other, and a fixture already holding the
        // answer would let a resolver that does nothing look like it worked.
        var cards = StatsDemoData.Decks(Wol).Cards;
        Assert.NotEmpty(cards);
        Assert.All(cards, c => Assert.StartsWith("HC", c.Card, System.StringComparison.Ordinal));
        Assert.All(cards, c => Assert.DoesNotContain(" ", c.Card));
    }

    // ---------------------------------------------------------------- the mod scope

    [Fact]
    public void THE_ONE_THAT_MATTERS_TwoModsAnswerDifferently()
    {
        // A per-mod filter cannot be verified against a fixture holding one mod: a broken
        // filter and a working one draw exactly the same page. Every payload the screen reads
        // has to differ between the two, or the block built from it is unchecked.
        Assert.NotEqual(
            StatsDemoData.Community(Wol).Totals!.Matches,
            StatsDemoData.Community(Other).Totals!.Matches);
        Assert.NotEqual(
            StatsDemoData.CivStats(Wol).Civs.Count,
            StatsDemoData.CivStats(Other).Civs.Count);
        Assert.NotEqual(
            StatsDemoData.Matchups(Wol).Matchups.Count,
            StatsDemoData.Matchups(Other).Matchups.Count);
        Assert.NotEqual(
            StatsDemoData.Decks(Wol).Cards.Count,
            StatsDemoData.Decks(Other).Cards.Count);
    }

    [Fact]
    public void EveryRowIsStampedWithTheModItWasAskedFor()
    {
        // The rows carry `mod_id`, and drawing rows from one mod under another mod's name is
        // precisely the bug the scope was added to end — so the fixture must not do it either.
        foreach (var mod in new[] { Wol, Other })
        {
            Assert.All(StatsDemoData.CivStats(mod).Civs, c => Assert.Equal(mod, c.ModId));
            Assert.All(StatsDemoData.Matchups(mod).Matchups, m => Assert.Equal(mod, m.ModId));
            Assert.All(StatsDemoData.Decks(mod).Cards, d => Assert.Equal(mod, d.ModId));
            Assert.Equal(mod, StatsDemoData.Community(mod).Mod);
        }
    }

    // ---------------------------------------------------------------- the civ table

    [Fact]
    public void THE_CIV_ROWS_STRADDLE_THE_SAMPLE_BAR()
    {
        // Rows above the bar, rows below it, and — the important one — a row JUST below it.
        // That last row is where a withheld percentage and an empty grey bar have to look
        // deliberate rather than broken, and it is the only row that tests the difference.
        int bar = CivStatsView.MinDecidedForPercent;
        var civs = StatsDemoData.CivStats().Civs;

        Assert.Contains(civs, c => c.Wins + c.Losses >= bar);
        Assert.Contains(civs, c => c.Wins + c.Losses < bar);
        Assert.Contains(civs, c => c.Wins + c.Losses == bar - 1);
    }

    [Fact]
    public void TheCivTailIsLongEnoughToBeWorthGrouping()
    {
        int bar = CivStatsView.MinDecidedForPercent;
        var tail = StatsDemoData.CivStats().Civs.Where(c => c.Wins + c.Losses < bar).ToList();
        Assert.True(tail.Count >= 5, $"only {tail.Count} civilizations below the sample bar");
    }

    [Fact]
    public void NoCivRowClaimsMoreDecidedMatchesThanItPlayed()
    {
        // Wins plus losses above played is impossible, and a fixture holding one would make
        // the percentage arithmetic produce numbers over 100.
        Assert.All(StatsDemoData.CivStats().Civs,
            c => Assert.True(c.Wins + c.Losses <= c.Played, c.Civ));
    }

    [Fact]
    public void THE_CIV_COUNT_AND_ITS_DENOMINATOR_COME_FROM_THE_SAME_QUESTION()
    {
        // The card reads "N of M matches carry a civilization". Both figures now come from
        // /stats/civs on identical terms; the version before this paired the numerator with a
        // THIRTY-DAY total from another endpoint, so the sentence held two different windows.
        var civs = StatsDemoData.CivStats();
        Assert.NotNull(civs.RatedMatches);
        Assert.True(civs.RatedMatchesWithCiv <= civs.RatedMatches);

        var empty = StatsDemoData.NoCivStats();
        Assert.Equal(0, empty.RatedMatchesWithCiv);
        Assert.True(empty.RatedMatches > 0, "the empty state still needs a real denominator");
    }

    // ---------------------------------------------------------------- maps

    [Fact]
    public void THE_MAP_LIST_HAS_A_TAIL_OF_SINGLE_MATCH_MAPS()
    {
        // Eight maps with one match each took eight rows on the page this replaces. The
        // grouped row is the fix, and it needs a tail to group.
        var maps = StatsDemoData.Community().Totals!.TopMaps!;
        Assert.True(maps.Count(m => m.Matches == 1) >= 5);
        // And a clear leader, or the proportional bars all come out the same length and the
        // column stops comparing anything.
        var top = maps.OrderByDescending(m => m.Matches).First();
        Assert.True(top.Matches >= maps.Sum(m => m.Matches) / 6);
    }

    [Fact]
    public void THE_MAP_NAMES_EXERCISE_BOTH_HALVES_OF_THE_SPLITTER()
    {
        // The page's job here is to stop printing "ESOC_Fertile Crescent" at a player. That is
        // only visible if the sample carries BOTH shapes: names with a pack tag, and names
        // with an underscore that is not one. A fixture of only prefixed names would let a
        // splitter that eats every first word look correct.
        var maps = StatsDemoData.Community().Totals!.TopMaps!;
        Assert.Contains(maps, m => LocalMatchView.MapLabel(m.Map).Pack != null);
        Assert.Contains(maps, m => m.Map.Contains('_') && LocalMatchView.MapLabel(m.Map).Pack == null);
        Assert.Contains(maps, m => !m.Map.Contains('_'));
    }

    [Fact]
    public void MORE_MATCHES_THAN_THE_MAPS_ACCOUNT_FOR()
    {
        // A match whose recording could not be read carries no map name, so the map counts
        // legitimately add up to less than the total. The page has to explain that gap rather
        // than look broken, and it cannot be seen at all in a fixture where they agree.
        var totals = StatsDemoData.Community().Totals!;
        Assert.True(totals.Matches > totals.TopMaps!.Sum(m => m.Matches));
    }

    // ---------------------------------------------------------------- the new blocks

    [Fact]
    public void SOME_MATCHES_COUNTED_AND_SOME_DID_NOT_AND_THERE_IS_A_REASON()
    {
        // The health card is built from two columns that existed since the rating rules were
        // written and that no endpoint read. A fixture where everything counted would draw a
        // card with nothing to say.
        var totals = StatsDemoData.Community().Totals!;
        Assert.NotNull(totals.Rated);
        Assert.True(totals.Rated < totals.Matches, "nothing failed to rate: no card to look at");
        Assert.False(string.IsNullOrWhiteSpace(totals.UnratedTopReason));
        Assert.True(totals.UnratedTopReasonMatches > 0);
    }

    [Fact]
    public void THE_ACTIVITY_HOURS_HAVE_A_SHAPE()
    {
        // All twenty-four buckets, and a real peak. A flat histogram is indistinguishable
        // from a broken one, and a missing hour would read as absent data rather than as a
        // quiet hour.
        var hours = StatsDemoData.Community().Activity!.Hours;
        Assert.Equal(24, hours.Count);
        Assert.Equal(Enumerable.Range(0, 24), hours.Select(h => h.Hour));
        Assert.True(hours.Max(h => h.Count) >= 3 * (hours.Sum(h => h.Count) / 24));
        Assert.Contains(hours, h => h.Count == 0);
    }

    [Fact]
    public void THE_PER_DAY_SERIES_HAS_A_QUIET_DAY()
    {
        // Days with nothing are absent from the payload, exactly as the server sends them —
        // so the series is shorter than the window, and anything drawing it has to zero-fill
        // rather than assume one row per day.
        var totals = StatsDemoData.Community().Totals!;
        Assert.NotNull(totals.MatchesPerDay);
        Assert.True(totals.MatchesPerDay!.Count < totals.WindowDays);
        Assert.All(totals.MatchesPerDay, d => Assert.True(d.Matches > 0));
    }

    [Fact]
    public void THE_MATCHUPS_ARE_THINNER_THAN_THE_CIVILIZATIONS()
    {
        // A matchup needs BOTH sides resolved, so it always has less behind it than either
        // civilization alone. A fixture where every pair cleared the sample bar would flatter
        // the table and hide the state it will actually be in for months.
        var civs = StatsDemoData.CivStats().Civs;
        var pairs = StatsDemoData.Matchups().Matchups;
        Assert.True(pairs.Count < civs.Count);
        Assert.Contains(pairs, p => p.Played < CivStatsView.MinDecidedForPercent);
        Assert.All(pairs, p => Assert.Equal(p.Played, p.WinsA + p.LossesA));
    }

    [Fact]
    public void THE_DECK_CONTRIBUTORS_ARE_NOT_THE_SUM_OF_THE_ROWS()
    {
        // One person carries many cards, so adding the column up reports a multiple of the
        // real headcount. The figure is counted server-side for exactly that reason, and a
        // fixture where the two happened to match would let a client-side sum look correct.
        var decks = StatsDemoData.Decks();
        Assert.True(decks.Contributors > 0);
        Assert.NotEqual(decks.Cards.Sum(c => c.Players), decks.Contributors);
    }
}
