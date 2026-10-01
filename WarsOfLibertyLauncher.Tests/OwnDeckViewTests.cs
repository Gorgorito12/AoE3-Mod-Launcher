using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// <see cref="OwnDeckView"/>: what the Profile's DECKS section does to the player's own decks
/// before drawing them. The refusals are the point — an empty civ list must hide nothing, and a
/// civilization whose display name did not resolve is still this mod's.
/// </summary>
public class OwnDeckViewTests
{
    private static HomeCityProfile Profile(string civ, string city, params string[] deckNames) => new()
    {
        Civ = civ,
        CityName = city,
        Decks = deckNames.Select(n => new HomeCityDeckEntry { Name = n }).ToList(),
    };

    /// <summary>
    /// THE ONE THAT MATTERS: Struggle of Indonesia's decks in Wars of Liberty's folder — measured
    /// on a real disk — are left out and COUNTED, while the player's own Bulgarians stay.
    /// </summary>
    [Fact]
    public void AnotherModsDecksAreLeftOutAndCounted()
    {
        var (own, foreign) = OwnDeckView.SplitForeign(
            new[]
            {
                Profile("Bulgarians", "Sofia", "Static Deck"),
                Profile("Toba", "Balige", "My Deck"),
                Profile("NetherlandsIndie", "Batavia", "My Deck", "Second"),
            },
            new[] { "Bulgarians", "Chinese" });

        Assert.Equal(new[] { "Bulgarians" }, own.Select(p => p.Civ));
        Assert.Equal(3, foreign);
    }

    /// <summary>An unreadable civ list says nothing about any deck: every deck stays.</summary>
    [Fact]
    public void AnEmptyCivListHidesNothing()
    {
        var (own, foreign) = OwnDeckView.SplitForeign(
            new[] { Profile("Toba", "Balige", "My Deck") }, new List<string>());

        Assert.Single(own);
        Assert.Equal(0, foreign);
    }

    [Fact]
    public void MembershipIgnoresCaseAndAFileWithNoCivStays()
    {
        var (own, foreign) = OwnDeckView.SplitForeign(
            new[] { Profile("bulgarians", "Sofia", "x"), Profile("", "Somewhere", "y") },
            new[] { "Bulgarians" });

        Assert.Equal(2, own.Count);
        Assert.Equal(0, foreign);
    }

    /// <summary>The game's default names say nothing and are dropped from the label.</summary>
    [Theory]
    [InlineData("My Deck")]
    [InlineData("static deck")]
    [InlineData("Default")]
    [InlineData("")]
    [InlineData(null)]
    public void DefaultDeckNamesAreGeneric(string? name)
        => Assert.True(OwnDeckView.IsGenericName(name));

    [Fact]
    public void ARealDeckNameIsKept()
    {
        Assert.False(OwnDeckView.IsGenericName("Fast Fortress"));
        var labels = OwnDeckView.Labels(new[] { ("Ethiopians", (string?)"Aksum", (string?)"Fast Fortress") });
        Assert.Equal("Ethiopians · Fast Fortress", labels.Single());
    }

    /// <summary>
    /// Two "Chinese · Static Deck" pills sat side by side on a real profile. They are two cities,
    /// and the city is what tells them apart — added only where it is needed.
    /// </summary>
    [Fact]
    public void TwinLabelsAreToldApartByTheirCity_AndOnlyTheTwins()
    {
        var labels = OwnDeckView.Labels(new[]
        {
            ("Chinese", (string?)"Beijing", (string?)"Static Deck"),
            ("Chinese", (string?)"Beijings", (string?)"Static Deck"),
            ("Bulgarians", (string?)"Sofia", (string?)"Static Deck"),
        });

        Assert.Equal(new[] { "Chinese · Beijing", "Chinese · Beijings", "Bulgarians" }, labels);
    }

    [Fact]
    public void SameCitySameNameFallsBackToANumber()
    {
        var labels = OwnDeckView.Labels(new[]
        {
            ("Aceh", (string?)"Banda", (string?)"My Deck"),
            ("Aceh", (string?)"Banda", (string?)"Default"),
        });

        Assert.Equal(new[] { "Aceh · Banda #1", "Aceh · Banda #2" }, labels);
    }

    [Theory]
    [InlineData(1, "I")]
    [InlineData(4, "IV")]
    [InlineData(5, "V")]
    public void AgesAreTheGamesNumerals(int age, string numeral)
        => Assert.Equal(numeral, OwnDeckView.AgeNumeral(age));

    /// <summary>An age the file could not mean is left out, never printed as "Age 0".</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(null)]
    public void AnAgeThatCannotBeNamedIsNull(int? age)
        => Assert.Null(OwnDeckView.AgeNumeral(age));
}
