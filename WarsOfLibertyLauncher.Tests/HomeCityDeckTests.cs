using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The decks a player has built, read from the game's own home city file.
///
/// <para>The fixture is a trimmed copy of a REAL <c>sp_Beijing_homecity.xml</c> — same element
/// names, same nesting, the same <c>dbid</c> attribute on a card whose text is its internal name,
/// and the same UTF-16 declaration — rather than a shape invented to match my reading of the
/// format, which would pass by construction and prove nothing.</para>
/// </summary>
public class HomeCityDeckTests
{
    private const string RealShape = """
    <?xml version="1.0" encoding="UTF-16"?>
    <savedhomecity version ="2">
      <defaultdirectoryid>0</defaultdirectoryid>
      <defaultfilename>homecitychinese.xml</defaultfilename>
      <civ>Chinese</civ>
      <hctype>HC</hctype>
      <name>Beijing</name>
      <heroname>Bai Yu Feng</heroname>
      <level>16</level>
      <xp>268787</xp>
      <decks>
        <deck>
          <name>Static Deck</name>
          <gameid>4</gameid>
          <cards>
            <card dbid ="4128">YPHCExpandedTradingPost</card>
            <card dbid ="2212">HCShipWoodCrates3</card>
            <card dbid ="2211">HCShipWoodCrates2</card>
            <card dbid ="52905">WOLHCShipTigermen2</card>
          </cards>
        </deck>
        <deck>
          <name>1</name>
          <gameid>4</gameid>
          <cards>
            <card dbid ="2573">HCShipWoodCrates1</card>
            <card dbid ="5038">YPHCRainbowTrickle</card>
          </cards>
        </deck>
      </decks>
    </savedhomecity>
    """;

    [Fact]
    public void ReadsTheCivilizationTheCityAndBothDecks()
    {
        var p = HomeCityDeckService.Parse("sp_Beijing_homecity", RealShape);

        Assert.NotNull(p);
        Assert.Equal("Chinese", p!.Civ);
        Assert.Equal("Beijing", p.CityName);
        Assert.Equal(16, p.Level);
        Assert.Equal(2, p.Decks.Count);
        Assert.Equal("Static Deck", p.Decks[0].Name);
        Assert.Equal(4, p.Decks[0].Cards.Count);
        Assert.Equal(2, p.Decks[1].Cards.Count);
    }

    /// <summary>
    /// <b>The slot is the file's ORDER, and that is the whole point of reading this file.</b> A
    /// deck is a sequence the player arranged; sorting it by name or by id would still print 25
    /// correct cards and silently destroy the only thing that says which one sits where.
    /// </summary>
    [Fact]
    public void TheSlotComesFromTheFilesOrderAndNeverFromASort()
    {
        var deck = HomeCityDeckService.Parse("sp_Beijing_homecity", RealShape)!.Decks[0];

        Assert.Equal(new[] { 0, 1, 2, 3 }, deck.Cards.Select(c => c.Slot));
        Assert.Equal("YPHCExpandedTradingPost", deck.Cards[0].InternalName);
        Assert.Equal("WOLHCShipTigermen2", deck.Cards[3].InternalName);

        // ...and the ids are NOT ascending, so a sort would have been visible here.
        Assert.Equal(new[] { 4128, 2212, 2211, 52905 }, deck.Cards.Select(c => c.Dbid));
    }

    /// <summary>
    /// The <c>dbid</c> is the same id <c>techtreey.xml</c> gives that tech — verified 50 of 50
    /// against a real home city — which is what lets <see cref="CardNameResolver"/> name the card.
    /// </summary>
    [Fact]
    public void KeepsTheCardIdAndTheInternalNameTogether()
    {
        var card = HomeCityDeckService.Parse("x", RealShape)!.Decks[0].Cards[1];

        Assert.Equal(2212, card.Dbid);
        Assert.Equal("HCShipWoodCrates3", card.InternalName);
    }

    [Fact]
    public void AFileThatIsNotAHomeCityIsRefusedRatherThanGuessedAt()
    {
        Assert.Null(HomeCityDeckService.Parse("x", "<ai><history /></ai>"));
        Assert.Null(HomeCityDeckService.Parse("x", "not xml at all <<<"));
        Assert.Null(HomeCityDeckService.Parse("x", ""));
    }

    [Fact]
    public void AHomeCityWithNoDecksIsStillARealProfile()
    {
        var p = HomeCityDeckService.Parse("x",
            "<savedhomecity><civ>Dutch</civ><name>Amsterdam</name></savedhomecity>");

        Assert.NotNull(p);
        Assert.Equal("Dutch", p!.Civ);
        Assert.Empty(p.Decks);
    }

    [Fact]
    public void AnEmptyCardEntryIsSkippedSoTheSlotsStayContiguous()
    {
        var p = HomeCityDeckService.Parse("x", """
        <savedhomecity><civ>Dutch</civ><name>Amsterdam</name><decks><deck>
          <name>d</name><cards>
            <card dbid ="1">HCOne</card>
            <card dbid ="2"></card>
            <card dbid ="3">HCThree</card>
          </cards>
        </deck></decks></savedhomecity>
        """);

        var cards = p!.Decks[0].Cards;
        Assert.Equal(2, cards.Count);
        Assert.Equal(new[] { 0, 1 }, cards.Select(c => c.Slot));
        Assert.Equal("HCThree", cards[1].InternalName);
    }

    // ------------------------------------------------------------------ the duplicate copy

    private static HomeCityProfile Profile(string file, string civ, string city)
        => new() { SourceFile = file, Civ = civ, CityName = city };

    /// <summary>
    /// <b>The rejection is the point.</b> The game keeps <c>LastHomeCityY.xml</c> as a
    /// byte-for-byte copy of whichever city was used last — on a real disk its MD5 matches
    /// <c>sp_Beijing_homecity.xml</c> exactly — so showing it would invent a second civilization
    /// the player does not have.
    /// </summary>
    [Fact]
    public void TheLastUsedCopyIsDroppedAndTheRealFileIsTheOneKept()
    {
        var kept = HomeCityDeckService.Deduplicate(new[]
        {
            Profile("LastHomeCityY", "Chinese", "Beijing"),
            Profile("sp_Beijing_homecity", "Chinese", "Beijing"),
        });

        var one = Assert.Single(kept);
        Assert.Equal("sp_Beijing_homecity", one.SourceFile);
    }

    [Fact]
    public void TwoRealCitiesOfDifferentCivilizationsBothSurvive()
    {
        var kept = HomeCityDeckService.Deduplicate(new[]
        {
            Profile("sp_Batavia_homecity", "Dutch", "Batavia"),
            Profile("sp_Delhi_homecity", "Indians", "Delhi"),
            Profile("sp_Solo_homecity", "Surakarta", "Solo"),
        });

        Assert.Equal(3, kept.Count);
    }

    /// <summary>
    /// Two cities of the SAME civilization are two real profiles — a player may rename one — so
    /// the duplicate rule keys on the pair and not on the civ alone.
    /// </summary>
    [Fact]
    public void TwoCitiesOfOneCivilizationAreNotEachOthersDuplicate()
    {
        var kept = HomeCityDeckService.Deduplicate(new[]
        {
            Profile("sp_Beijing_homecity", "Chinese", "Beijing"),
            Profile("sp_Nanjing_homecity", "Chinese", "Nanjing"),
        });

        Assert.Equal(2, kept.Count);
    }

    // ------------------------------------------------------------------ the mod's own tech layer

    [Fact]
    public void TheModsOwnTechLayerIsDerivedFromItsExecutable()
    {
        Assert.Contains("techtreen.xml", CardNameResolver.TechFilesFor("age3n.exe"));
        Assert.Contains("techtreem.xml", CardNameResolver.TechFilesFor("age3m.exe"));

        // age3y.exe IS the y layer, already in the base set — never added twice.
        Assert.Equal(3, CardNameResolver.TechFilesFor("age3y.exe").Count);
        Assert.Equal(3, CardNameResolver.TechFilesFor("age3.exe").Count);
        Assert.Equal(3, CardNameResolver.TechFilesFor(null).Count);
    }

    /// <summary>The base layers stay in the engine's override order: base, expansion, expansion 2.</summary>
    [Fact]
    public void TheTechLayersAreOrderedSoALaterOneWins()
    {
        Assert.Equal(
            new[] { "techtree.xml", "techtreex.xml", "techtreey.xml", "techtreen.xml" },
            CardNameResolver.TechFilesFor("age3n.exe"));
    }
}
