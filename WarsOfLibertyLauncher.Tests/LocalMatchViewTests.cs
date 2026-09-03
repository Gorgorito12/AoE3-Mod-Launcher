using System.Collections.Generic;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Which recordings on disk are games against people, and how one is named on screen.
///
/// <para>The rules are small on purpose. Everything a recording can honestly say about a match
/// with no AI in it is who played and which slot lost — there are no statistics to compute,
/// because the game writes none for such a match.</para>
/// </summary>
public class LocalMatchViewTests
{
    private static ReplayParserService.ReplayPlayer Player(int slot, string name, bool human) =>
        new(slot, name, Civilization: 1, TeamId: -1,
            SlotType: human ? ReplayParserService.SlotTypeHuman : 1u);

    private static ReplayParserService.ReplayHeader Header(
        params ReplayParserService.ReplayPlayer[] players) =>
        new("age3y.exe", "room", "map", "pool", players.Length, players);

    // ------------------------------------------------------------------ who counts

    /// <summary>
    /// One human and an AI is a game against the AI, which has its own section reading its own
    /// file. Listing it here would show the same match twice under two different promises.
    /// </summary>
    [Fact]
    public void OneHumanAgainstAnAiIsNotAMatchAgainstPeople()
    {
        var header = Header(Player(1, "Gorgorito", human: true), Player(2, "wolShaka", human: false));

        Assert.False(LocalMatchView.IsHumanMatch(header));
        Assert.Equal(1, LocalMatchView.HumanCount(header));
    }

    [Fact]
    public void TwoPeopleIsAMatchAgainstPeople() =>
        Assert.True(LocalMatchView.IsHumanMatch(
            Header(Player(1, "Gorgorito", true), Player(2, "Taita", true))));

    /// <summary>
    /// An AI sitting in a game two people are playing does not stop it being a game against a
    /// person — the launcher just cannot say who won it.
    /// </summary>
    [Fact]
    public void AnAiAlongsideTwoPeopleStillCounts() =>
        Assert.True(LocalMatchView.IsHumanMatch(Header(
            Player(1, "Gorgorito", true), Player(2, "Taita", true), Player(3, "wolShaka", false))));

    [Fact]
    public void NothingIsNotAMatch()
    {
        Assert.False(LocalMatchView.IsHumanMatch(null));
        Assert.False(LocalMatchView.IsHumanMatch(Header()));
        Assert.Equal(0, LocalMatchView.HumanCount(null));
    }

    // ------------------------------------------------------------------ naming

    [Theory]
    [InlineData("ESOC_Baja California", "ESOC Baja California")]
    [InlineData("Great_Plains", "Great Plains")]
    [InlineData("Amazonia", "Amazonia")]
    public void TheMapFileNameIsMadeReadable(string raw, string expected) =>
        Assert.Equal(expected, LocalMatchView.PrettyMap(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AMapWithNoNameStaysEmptyRatherThanInventingOne(string? raw) =>
        Assert.Equal("", LocalMatchView.PrettyMap(raw));

    /// <summary>Real values, straight from the recording committed to this repository.</summary>
    [Theory]
    [InlineData("sp_Berlin_homecity.xml", "Berlin")]
    [InlineData("sp_Ciudad de México_homecity.xml", "Ciudad de México")]
    [InlineData("sp_Beijing_homecity", "Beijing")]
    public void TheHomeCityIsTakenOutOfItsFileName(string file, string expected) =>
        Assert.Equal(expected, LocalMatchView.HomeCityFrom(file));

    /// <summary>
    /// <b>The refusals matter more than the successes.</b> A mod may name these files however it
    /// likes, and half a trimmed word presented as a city is worse than no city at all — the
    /// caller simply leaves the fact out.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("LastHomeCityY.xml")]
    [InlineData("Beijing.xml")]
    [InlineData("sp_Beijing.xml")]
    [InlineData("Beijing_homecity.xml")]
    public void AnythingElseYieldsNoCity(string? file) =>
        Assert.Equal("", LocalMatchView.HomeCityFrom(file));
}
