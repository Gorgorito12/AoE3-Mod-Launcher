using System;
using System.IO;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// <see cref="LocalGames.ReadHumanMatches"/> — the reader behind BOTH screens that list the
/// matches recorded on this PC: the mod window's STATISTICS section and the multiplayer
/// Profile's MATCHES section.
///
/// <para>It had no test while it lived as a private method inside the mod window. The cases
/// that matter are the refusals: a skirmish against the AI is not a match against people, and
/// a recording sitting loose in the user-data root is not read when a <c>Savegame</c> folder
/// exists beside it.</para>
/// </summary>
public class LocalGamesTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "aoe3ml-localgames-" + Guid.NewGuid().ToString("N"));

    public LocalGamesTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static string Fixture(string name)
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    /// <summary>A real 2v2 between four people.</summary>
    private const string HumanMatch = "zv104-header.age3Yrec";

    /// <summary>Real skirmishes: one human against the AI.</summary>
    private static readonly string[] Skirmishes =
        { "wol-loss-arizona.age3Yrec", "wol-win-amazonia.age3Yrec" };

    private void Place(string folder, string fixture, string asName)
    {
        Directory.CreateDirectory(folder);
        File.Copy(Fixture(fixture), Path.Combine(folder, asName));
    }

    [Fact]
    public void AFolderThatDoesNotExistReadsAsNothing()
    {
        var rows = LocalGames.ReadHumanMatches(
            Path.Combine(_root, "not-here"), "Anybody", installPath: null, modId: "test-mod");

        Assert.Empty(rows);
    }

    [Fact]
    public void GamesAgainstTheAiAreNotMatchesAgainstPeople()
    {
        var saves = Path.Combine(_root, "Savegame");
        for (var i = 0; i < Skirmishes.Length; i++)
            Place(saves, Skirmishes[i], $"Record Game {i + 1}.age3Yrec");

        var rows = LocalGames.ReadHumanMatches(_root, "Anybody", installPath: null, modId: "test-mod");

        Assert.Empty(rows);
    }

    [Fact]
    public void AMatchBetweenPeopleIsReadWithItsPlayers()
    {
        Place(Path.Combine(_root, "Savegame"), HumanMatch, "Record Game 1.age3Yrec");

        var rows = LocalGames.ReadHumanMatches(_root, "Geaf_Argento", installPath: null, modId: "test-mod");

        var row = Assert.Single(rows);
        Assert.Equal("Record Game 1", row.FileName);
        Assert.Equal(4, row.Players.Count);

        // The viewer is found by the name in his own AoE3 profile, never by anything in the
        // outcome block — which names a loser, not a recorder.
        Assert.True(row.LocalSlot >= 0, "the viewer's own slot was not found by name");
    }

    /// <summary>
    /// The game writes its recordings into <c>Savegame</c>; the root is only read when that
    /// folder does not exist. A match lying loose in the root beside a real Savegame folder is
    /// somebody else's file, and listing it would put a stranger's game among the player's own.
    /// </summary>
    [Fact]
    public void TheSavegameFolderIsPreferredOverTheRoot()
    {
        Place(_root, HumanMatch, "copied here.age3Yrec");
        Place(Path.Combine(_root, "Savegame"), Skirmishes[0], "Record Game 1.age3Yrec");

        var rows = LocalGames.ReadHumanMatches(_root, "Anybody", installPath: null, modId: "test-mod");

        Assert.Empty(rows);
    }

    [Fact]
    public void WithNoSavegameFolderTheRootIsRead()
    {
        Place(_root, HumanMatch, "Record Game 1.age3Yrec");

        var rows = LocalGames.ReadHumanMatches(_root, "Anybody", installPath: null, modId: "test-mod");

        Assert.Single(rows);
    }

    /// <summary>A truncated recording costs its own row and nothing else.</summary>
    [Fact]
    public void AnUnreadableRecordingIsSkippedAndTheRestAreStillRead()
    {
        var saves = Path.Combine(_root, "Savegame");
        Place(saves, HumanMatch, "Record Game 1.age3Yrec");
        File.WriteAllBytes(Path.Combine(saves, "Record Game 2.age3Yrec"), new byte[] { 1, 2, 3 });

        var rows = LocalGames.ReadHumanMatches(_root, "Anybody", installPath: null, modId: "test-mod");

        Assert.Single(rows);
    }
}
