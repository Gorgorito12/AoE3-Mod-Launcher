using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The community deck's decisions, away from the layout.
///
/// <para>What it replaced was a flat <c>Take(60)</c>: sixty rows every one of which said "1",
/// beside a civilization column repeating the same value twelve and twenty times. That is the
/// absence of a sample, printed. Every rule below is one the maps list or the civilization
/// balance already applies — brought here so a player does not meet three tables on one page
/// that disagree about what counts as evidence.</para>
///
/// <para>Then the per-civilization list capped at seven rows and folded the rest into a line
/// that said "seen once" — and the fold held cards in five of six decks. The bands are what
/// replaced that, and <see cref="THE_ONE_THAT_MATTERS_TheTailNeverHoldsACardTheBandsWouldName"/>
/// is the regression test for it.</para>
/// </summary>
public class DeckStatsViewTests
{
    private static DeckCardEntry Card(string civ, string card, int players) =>
        new() { ModId = "wol", Civ = civ, Card = card, Players = players };

    /// <summary>Identity resolvers: what a mod calls a card is not this class's business.</summary>
    private static IReadOnlyList<DeckCivGroup> Group(
        IEnumerable<DeckCardEntry> rows, ISet<string>? expanded = null)
        => DeckStatsView.Group(rows.ToList(), c => c, c => c, expanded);

    /// <summary>A civilization with enough decks behind it to publish a share.</summary>
    private static List<DeckCardEntry> Sampled(string civ, params int[] counts)
    {
        var rows = new List<DeckCardEntry>();
        // The generic shipment every deck of every civilization carries. It IS the denominator.
        rows.Add(Card(civ, "HCShipWood300", 10));
        for (int i = 0; i < counts.Length; i++) rows.Add(Card(civ, $"{civ}Card{i}", counts[i]));
        return rows;
    }

    // ---- the denominator -------------------------------------------------

    /// <summary>
    /// THE ONE THAT MATTERS. The share is of THAT CIVILIZATION's decks.
    ///
    /// <para>The payload's only headcount is <c>Contributors</c>: players who shared a deck for
    /// the MOD. Dividing a Mexican card by that counts everyone who never played Mexico. There
    /// is no per-civilization count on the wire, but every deck carries the generic shipments,
    /// so the largest count inside a civilization IS its deck count.</para>
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheDenominatorIsThisCivilizationsOwnDeckCount()
    {
        var groups = Group(Sampled("Aztecs", 5).Concat(Sampled("Zulu", 1)));

        Assert.Equal(10, groups.Single(g => g.Civ == "Aztecs").Decks);
        Assert.Equal(10, groups.Single(g => g.Civ == "Zulu").Decks);

        // 5 of 10 is half of the Aztec decks, whatever the rest of the mod did.
        var aztec = groups.Single(g => g.Civ == "Aztecs").Shown.Single(r => r.Players == 5);
        Assert.Equal(50, aztec.Percent);
    }

    /// <summary>
    /// A card that IS in somebody's deck is never reported as being in nobody's. One deck out
    /// of two hundred rounds to 1 %, not to 0 %.
    /// </summary>
    [Fact]
    public void ARareCardRoundsUpToOnePercentRatherThanDownToZero()
        => Assert.Equal(1, DeckStatsView.Percent(1, 200));

    [Fact]
    public void NoDecksIsNoPercentage() => Assert.Equal(0, DeckStatsView.Percent(3, 0));

    // ---- the sample minimum ----------------------------------------------

    /// <summary>
    /// THE STATE THE USER HAD ON DAY ONE. Every count is 1, so every civilization has one deck,
    /// so no percentage is publishable and every group is nothing but its summary line. That
    /// degenerate case has to look deliberate rather than broken.
    /// </summary>
    [Fact]
    public void WithOneDeckPerCivilization_NothingIsNamedButTheTail_AndNoPercentages()
    {
        var rows = Enumerable.Range(0, 18).Select(i => Card("Austrians", $"Card{i}", 1));

        var group = Assert.Single(Group(rows));

        Assert.Empty(group.Bands);
        Assert.Empty(group.Shown);
        Assert.Equal(18, group.Tail.Count);
        Assert.Equal(18, group.DistinctCards);
        Assert.All(group.Tail, r => Assert.Null(r.Percent));
    }

    /// <summary>
    /// One under the minimum names nothing at all; one at it publishes every share in the group.
    /// The threshold is the civilization balance's own — two thresholds on one page is a page
    /// that contradicts itself.
    ///
    /// <para>Below the minimum the answer is NOT "bands without percentages": it is no bands.
    /// A heading over four decks is the same claim the civilization bar refuses to print, and
    /// the README's own words are that those cards simply fall into the tail.</para>
    /// </summary>
    [Fact]
    public void FourDecksIsNotEnoughForAShare_FiveIs()
    {
        Assert.Equal(5, DeckStatsView.MinDecksForPercent);
        Assert.Equal(CivStatsView.MinDecidedForPercent, DeckStatsView.MinDecksForPercent);

        var four = Group(new[] { Card("Zulu", "Generic", 4), Card("Zulu", "Real", 3) });
        Assert.Empty(four[0].Bands);
        Assert.Equal(2, four[0].Tail.Count);

        var five = Group(new[] { Card("Zulu", "Generic", 5), Card("Zulu", "Real", 3) });
        Assert.All(five[0].Shown, r => Assert.NotNull(r.Percent));
        Assert.Equal(60, five[0].Shown.Single(r => r.Card == "Real").Percent);
        Assert.Equal(60, five[0].Bands.Single(b => b.Players == 3).Percent);
    }

    // ---- the bands ----------------------------------------------------------

    /// <summary>
    /// THE 25b REGRESSION. The tail is decided by how many decks carry a card and by NOTHING
    /// positional. The old rule capped each civilization at seven rows and folded the rest
    /// into a line that said "seen once" — so a civilization with twenty cards each in three of
    /// its six decks printed thirteen of them as examples of a card seen once. Now every one of
    /// the twenty is in the same band and the tail is empty.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheTailNeverHoldsACardTheBandsWouldName()
    {
        var rows = new List<DeckCardEntry> { Card("Aztecs", "Generic", 6) };
        rows.AddRange(Enumerable.Range(0, 20).Select(i => Card("Aztecs", $"Card{i}", 3)));

        var group = Assert.Single(Group(rows));

        Assert.Empty(group.Tail);
        Assert.Equal(21, group.Shown.Count);
        Assert.Equal(21, group.DistinctCards);
        var band = group.Bands.Single(b => b.Players == 3);
        Assert.Equal(20, band.Cards.Count);
        Assert.Equal(50, band.Percent);
    }

    /// <summary>
    /// One band per distinct count, most-carried first — the four headings of the handoff's
    /// German deck: 100 % · 83 % · 67 % · 50 %, each with the right number of cards under it.
    /// </summary>
    [Fact]
    public void ABandPerDistinctCount_MostCarriedFirst()
    {
        var rows = new List<DeckCardEntry>();
        void Add(string prefix, int n, int players)
        {
            for (int i = 0; i < n; i++) rows.Add(Card("Germans", $"{prefix}{i}", players));
        }
        Add("Every", 3, 6);
        Add("Five", 6, 5);
        Add("Four", 11, 4);
        Add("Three", 7, 3);
        Add("Two", 20, 2);
        Add("One", 11, 1);

        var group = Assert.Single(Group(rows));

        Assert.Equal(6, group.Decks);
        Assert.Equal(new[] { 6, 5, 4, 3 }, group.Bands.Select(b => b.Players));
        Assert.Equal(new[] { 100, 83, 67, 50 }, group.Bands.Select(b => b.Percent!.Value));
        Assert.Equal(new[] { 3, 6, 11, 7 }, group.Bands.Select(b => b.Cards.Count));
        Assert.Equal(31, group.Tail.Count);
        Assert.Equal(58, group.DistinctCards);
    }

    /// <summary>A card in one or two decks is counted, not named; three is where a band starts.</summary>
    [Fact]
    public void ACardInOneOrTwoDecksIsTheTail_AndThreeEarnsABand()
    {
        Assert.Equal(2, DeckStatsView.TailMaxPlayers);

        var group = Assert.Single(Group(new[]
        {
            Card("Chinese", "Generic", 6),
            Card("Chinese", "Thrice", 3),
            Card("Chinese", "Twice", 2),
            Card("Chinese", "Once", 1),
        }));

        Assert.Contains(group.Shown, r => r.Card == "Thrice");
        Assert.Contains(group.Tail, r => r.Card == "Twice");
        Assert.Contains(group.Tail, r => r.Card == "Once");
        Assert.DoesNotContain(group.Tail, r => r.Card == "Thrice");
    }

    /// <summary>
    /// Every card belongs to exactly one place. The count under the deck has to keep adding
    /// up, and a card in both a band and the tail would be drawn twice.
    /// </summary>
    [Fact]
    public void EveryCardIsInExactlyOneBandOrTheTail()
    {
        var group = Assert.Single(Group(Sampled("Aztecs", 9, 9, 4, 3, 2, 2, 1)));

        var inBands = group.Bands.SelectMany(b => b.Cards).Select(r => r.Card).ToList();
        var inTail = group.Tail.Select(r => r.Card).ToList();

        Assert.Equal(inBands.Count, inBands.Distinct().Count());
        Assert.Empty(inBands.Intersect(inTail));
        Assert.Equal(group.DistinctCards, inBands.Count + inTail.Count);
        Assert.Equal(inBands, group.Shown.Select(r => r.Card));
    }

    /// <summary>
    /// Opening a civilization names EVERYTHING — the tail becomes bands of its own and
    /// nothing is left to click again. Below the sample minimum too: the player asked, so
    /// the headings appear, but they say the count and never a percentage.
    /// </summary>
    [Fact]
    public void AnExpandedCivilizationShowsEverything()
    {
        var rows = Enumerable.Range(0, 20).Select(i => Card("Aztecs", $"Card{i}", 1)).ToList();

        var group = Assert.Single(Group(rows, new HashSet<string> { "Aztecs" }));

        Assert.Equal(20, group.Shown.Count);
        Assert.Empty(group.Tail);
        var band = Assert.Single(group.Bands);
        Assert.Equal(1, band.Players);
        Assert.Null(band.Percent);
    }

    // ---- ordering ---------------------------------------------------------

    /// <summary>
    /// By times seen, never by rarity. The other direction puts the card somebody brought once
    /// at the top and calls it notable — the lie the civilization rules already name.
    /// </summary>
    [Fact]
    public void RowsAreOrderedByTimesSeen()
    {
        var group = Assert.Single(Group(new[]
        {
            Card("Zulu", "Rare", 1),
            Card("Zulu", "Common", 9),
            Card("Zulu", "Middling", 4),
        }, new HashSet<string> { "Zulu" }));

        Assert.Equal(new[] { "Common", "Middling", "Rare" }, group.Shown.Select(r => r.Card));
    }

    /// <summary>Inside a band the order is the label, so the deck does not reshuffle itself
    /// between two visits to the tab.</summary>
    [Fact]
    public void InsideABandTheOrderIsTheLabel()
    {
        var group = Assert.Single(Group(new[]
        {
            Card("Zulu", "Generic", 6),
            Card("Zulu", "Zebra", 4),
            Card("Zulu", "Apple", 4),
            Card("Zulu", "mango", 4),
        }));

        Assert.Equal(
            new[] { "Apple", "mango", "Zebra" },
            group.Bands.Single(b => b.Players == 4).Cards.Select(r => r.Card));
    }

    /// <summary>Groups by how much each civilization has to say, and ties by name so the page
    /// does not reshuffle itself between two visits.</summary>
    [Fact]
    public void GroupsAreOrderedByHowMuchTheyHold_TiesByName()
    {
        var groups = Group(new[]
        {
            Card("Zulu", "A", 1),
            Card("Aztecs", "A", 1), Card("Aztecs", "B", 1), Card("Aztecs", "C", 1),
            Card("Berbers", "A", 1),
        });

        Assert.Equal(new[] { "Aztecs", "Berbers", "Zulu" }, groups.Select(g => g.Civ));
    }

    // ---- the ordinary defensive cases -------------------------------------

    [Fact]
    public void NoRowsIsNoGroups() => Assert.Empty(Group(System.Array.Empty<DeckCardEntry>()));

    [Fact]
    public void ARowWithNoCardIsSkipped()
        => Assert.Empty(Group(new[] { Card("Zulu", "", 3) }));

    /// <summary>The label is what the resolver returned, and the fold key stays the INTERNAL
    /// name — a group keyed by a display name would lose its place the moment the mod
    /// resolved.</summary>
    [Fact]
    public void TheFoldKeyIsTheInternalNameAndTheLabelIsTheResolvedOne()
    {
        var groups = DeckStatsView.Group(
            new List<DeckCardEntry> { Card("SPCXulu", "HCXPRefrigeration", 2) },
            c => "Refrigeration",
            c => "Zulu");

        Assert.Equal("SPCXulu", groups[0].Civ);
        Assert.Equal("Zulu", groups[0].CivLabel);
        Assert.Equal("Refrigeration", groups[0].Shown.Concat(groups[0].Tail).Single().Label);
    }

    // ------------------------------------------------------------ which deck opens

    [Fact]
    public void TheDeckThatOpensIsTheChosenOne_ThenTheOneIPlay_ThenTheBestSampled()
    {
        // Three civilizations: one with the most DISTINCT cards but few decks, one with the
        // most decks, and the player's own. Group() sorts by card count, so "the first group"
        // would be the one with the least agreement - which is exactly the wrong deck to
        // open on.
        var rows = new List<DeckCardEntry>();
        for (int i = 0; i < 12; i++) rows.Add(Card("Many", "HCMany" + i, 1));
        rows.Add(Card("Sampled", "HCGeneric", 7));
        rows.Add(Card("Sampled", "HCOther", 5));
        rows.Add(Card("Mine", "HCMineGeneric", 3));
        var groups = DeckStatsView.Group(rows, c => c, c => c == "Mine" ? "Peruvians" : c);

        // Nothing chosen, nothing played: the best-sampled deck, never the first group.
        Assert.Equal("Sampled", DeckStatsView.PickDefault(groups, null, null)!.Civ);

        // The player's own civilization, matched on the LABEL the server stored...
        Assert.Equal("Mine", DeckStatsView.PickDefault(groups, null, "peruvians")!.Civ);
        // ...or on the internal name, since the two tables live in different namespaces.
        Assert.Equal("Mine", DeckStatsView.PickDefault(groups, null, "MINE")!.Civ);
        // A civilization the player plays but nobody shared a deck for changes nothing.
        Assert.Equal("Sampled", DeckStatsView.PickDefault(groups, null, "Incas")!.Civ);

        // A choice wins over everything - and one that is no longer in the set is ignored
        // rather than leaving the page blank.
        Assert.Equal("Many", DeckStatsView.PickDefault(groups, "Many", "Peruvians")!.Civ);
        Assert.Equal("Mine", DeckStatsView.PickDefault(groups, "Gone", "Peruvians")!.Civ);

        Assert.Null(DeckStatsView.PickDefault(new List<DeckCivGroup>(), "Many", "Peruvians"));
    }

    [Fact]
    public void WithEqualDecksTheDeckWithMoreCardsOpens()
    {
        var rows = new List<DeckCardEntry>
        {
            Card("Few", "HCGeneric", 6),
            Card("Lots", "HCGeneric", 6), Card("Lots", "HCA", 5), Card("Lots", "HCB", 4),
        };
        var groups = DeckStatsView.Group(rows, c => c, c => c);
        Assert.Equal("Lots", DeckStatsView.PickDefault(groups, null, null)!.Civ);
    }

    // ------------------------------------------------------------ the civilization strip

    /// <summary>Ten civilizations, each with a deck count of its own so the order is decided.</summary>
    private static IReadOnlyList<DeckCivGroup> TenCivs()
    {
        var rows = new List<DeckCardEntry>();
        for (int i = 0; i < 10; i++)
            rows.Add(Card("Civ" + i, "HCGeneric", 10 - i));   // Civ0 has 10 decks, Civ9 has 1
        return DeckStatsView.Group(rows, c => c, c => c);
    }

    [Fact]
    public void ThePillsAreBestSampledFirst_SoTheFirstOneIsTheDeckThatOpens()
    {
        var groups = TenCivs();
        var (shown, hidden) = DeckStatsView.CivPills(groups, null);

        Assert.Equal(DeckStatsView.MaxCivPills, shown.Count);
        Assert.Equal(10 - DeckStatsView.MaxCivPills, hidden);
        Assert.Equal(
            new[] { "Civ0", "Civ1", "Civ2", "Civ3", "Civ4", "Civ5", "Civ6" },
            shown.Select(g => g.Civ));

        // The whole point of that order: the first pill IS the civilization the page opens on.
        Assert.Equal(shown[0].Civ, DeckStatsView.PickDefault(groups, null, null)!.Civ);
    }

    /// <summary>
    /// THE ONE THAT MATTERS: the open civilization is never one of the folded ones.
    ///
    /// <para>Otherwise the page draws a deck whose pill is off the row and NO pill lit, which
    /// is worse than having no row at all - the one thing the strip exists to say is whose
    /// cards these are. It takes the LAST shown slot, so choosing from the fold does not
    /// shuffle the pills in front of it.</para>
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheOpenCivilizationIsNeverFoldedAway()
    {
        var groups = TenCivs();
        var (shown, hidden) = DeckStatsView.CivPills(groups, "Civ9");

        Assert.Contains(shown, g => g.Civ == "Civ9");
        Assert.Equal("Civ9", shown[^1].Civ);
        // The pills in front of it did not move, and the count it is holding back did not change.
        Assert.Equal(
            new[] { "Civ0", "Civ1", "Civ2", "Civ3", "Civ4", "Civ5" },
            shown.Take(DeckStatsView.MaxCivPills - 1).Select(g => g.Civ));
        Assert.Equal(10 - DeckStatsView.MaxCivPills, hidden);
    }

    [Fact]
    public void UnfoldedShowsEveryCivilizationAndFoldsNothing()
    {
        var (shown, hidden) = DeckStatsView.CivPills(TenCivs(), "Civ9", max: 0);
        Assert.Equal(10, shown.Count);
        Assert.Equal(0, hidden);
    }

    [Fact]
    public void FewerCivilizationsThanTheCapNeverFold()
    {
        var rows = new List<DeckCardEntry> { Card("Only", "HCGeneric", 7) };
        var (shown, hidden) = DeckStatsView.CivPills(DeckStatsView.Group(rows, c => c, c => c), null);

        Assert.Equal("Only", Assert.Single(shown).Civ);
        Assert.Equal(0, hidden);

        // And nothing at all is not an error: an empty payload draws an empty strip.
        var (none, noneHidden) = DeckStatsView.CivPills(new List<DeckCivGroup>(), "Only");
        Assert.Empty(none);
        Assert.Equal(0, noneHidden);
    }

    /// <summary>A civilization the payload no longer carries cannot be shown, and must not
    /// cost a slot either - the same refusal <c>PickDefault</c> makes about a stale choice.</summary>
    [Fact]
    public void AChoiceThatIsGoneCostsNoSlot()
    {
        var (shown, _) = DeckStatsView.CivPills(TenCivs(), "Vanished");
        Assert.Equal(
            new[] { "Civ0", "Civ1", "Civ2", "Civ3", "Civ4", "Civ5", "Civ6" },
            shown.Select(g => g.Civ));
    }
}
