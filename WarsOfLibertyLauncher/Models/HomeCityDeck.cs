using System.Collections.Generic;

namespace WarsOfLibertyLauncher.Models;

/// <summary>
/// One card in a deck, in the position the player put it.
///
/// <para><see cref="Dbid"/> is the SAME id <c>techtreey.xml</c> gives that tech — verified 50 of 50
/// against a real home city — so a card resolves to its on-screen name through the chain
/// <see cref="Services.CardNameResolver"/> already owns. <see cref="InternalName"/> is kept beside
/// it because it is what a modder recognises and what survives when the string table cannot
/// name the card.</para>
/// </summary>
public class HomeCityCard
{
    /// <summary>Zero-based position in the deck — the slot the player sees.</summary>
    public int Slot { get; set; }

    public int Dbid { get; set; }

    /// <summary><c>HCShipWoodCrates3</c>, <c>YPHCExpandedTradingPost</c>.</summary>
    public string InternalName { get; set; } = "";
}

/// <summary>One named deck. A player keeps several per home city and picks one per match.</summary>
public class HomeCityDeckEntry
{
    public string Name { get; set; } = "";

    /// <summary>
    /// The game mode the deck is filed under, as the file states it. Recorded rather than
    /// interpreted: nothing here knows what the numbers mean, and inventing a meaning for one
    /// would put a claim in front of the player that no measurement backs.
    /// </summary>
    public int GameId { get; set; }

    public List<HomeCityCard> Cards { get; set; } = new();
}

/// <summary>
/// A player's home city for one civilization, as the game stores it in
/// <c>My Games\&lt;mod&gt;\Savegame\sp_&lt;City&gt;_homecity.xml</c>.
///
/// <para><b>This is what the player BRINGS, not what they played.</b> Every surface that shows it
/// has to say so: a deck holds 25 cards and a match may use five of them, so reading this as "cards
/// played" would overstate it by a factor nobody could see. What the recording carries about cards
/// actually sent is nothing at all — measured, see the card section in
/// <c>.claude/rules/multiplayer.md</c> — which is precisely why this file is worth reading.</para>
/// </summary>
public class HomeCityProfile
{
    /// <summary>The file's own name without extension, e.g. <c>sp_Beijing_homecity</c>.</summary>
    public string SourceFile { get; set; } = "";

    /// <summary>Internal civ name as the file states it (<c>Chinese</c>), not a display name.</summary>
    public string Civ { get; set; } = "";

    /// <summary>The city the player named, e.g. <c>Beijing</c>.</summary>
    public string CityName { get; set; } = "";

    public int Level { get; set; }

    public List<HomeCityDeckEntry> Decks { get; set; } = new();
}
