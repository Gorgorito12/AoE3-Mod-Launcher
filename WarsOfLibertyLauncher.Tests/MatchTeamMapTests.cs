using System.Collections.Generic;
using System.IO;
using System.Linq;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="MatchTeamMap"/> — which Discord account played on which side.
///
/// <para><b>The refusals are the whole file.</b> A wrong answer here puts a real person on the
/// wrong team of a real match in somebody else's history, permanently and with nothing on screen
/// to contradict it; a refusal costs only the teams of one match, which is what already happens
/// for every match reported so far.</para>
///
/// <para>The positive case runs against <c>Fixtures/zv104-header.age3Yrec</c>, a genuine 2v2 —
/// the same file <see cref="ReplayParserTests"/> uses. Inventing a header to match my reading of
/// the format would pass by construction and prove nothing.</para>
/// </summary>
public class MatchTeamMapTests
{
    private static IReadOnlyList<ReplayParserService.ReplayPlayer> RealTwoVsTwo()
    {
        var raw = File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "zv104-header.age3Yrec"));
        var header = ReplayParserService.TryParse(raw);
        Assert.NotNull(header);
        return header!.Players;
    }

    /// <summary>Names as the real recording carries them, mapped to invented Discord ids.</summary>
    private static Dictionary<string, string> RealNames() => new()
    {
        ["u-metal"] = "69metal69",
        ["u-zelda"] = "ElZeldaVerde",
        ["u-geaf"] = "Geaf_Argento",
        ["u-alu"] = "Alucard",
    };

    [Fact]
    public void ReadsTheTwoSidesOutOfTheRealRecording()
    {
        var map = MatchTeamMap.Resolve(RealTwoVsTwo(), RealNames());

        Assert.NotNull(map);
        // The file's own team ids are 0,1,0,1 across slots 1..4, so these two pairs are read
        // from the game's bytes rather than chosen here.
        Assert.Equal(map!["u-metal"], map["u-geaf"]);
        Assert.Equal(map["u-zelda"], map["u-alu"]);
        Assert.NotEqual(map["u-metal"], map["u-zelda"]);

        // Normalised to 0 and 1 — not the raw ids — so the number means the same thing on both
        // machines that could report this match.
        Assert.Equal(new[] { 0, 1 }, map.Values.Distinct().OrderBy(v => v).ToArray());
    }

    [Fact]
    public void NamesAreMatchedIgnoringCaseAndSurroundingSpace()
    {
        // The same rule FindPlayerSlot uses for the local player. A profile name typed with a
        // stray space must not cost the whole match its teams.
        var names = new Dictionary<string, string>
        {
            ["u-metal"] = " 69METAL69 ",
            ["u-zelda"] = "elzeldaverde",
            ["u-geaf"] = "Geaf_Argento",
            ["u-alu"] = "alucard",
        };

        Assert.NotNull(MatchTeamMap.Resolve(RealTwoVsTwo(), names));
    }

    // ---------------- the refusals ----------------

    [Fact]
    public void OneNameThatDoesNotMatchRefusesTheWholeMap()
    {
        // THE ONE THAT MATTERS. Three players would still map cleanly, and reporting those three
        // with a team while the fourth got a default would look right and be wrong.
        var names = RealNames();
        names["u-alu"] = "somebody else";

        Assert.Null(MatchTeamMap.Resolve(RealTwoVsTwo(), names));
    }

    [Fact]
    public void TwoPlayersWithTheSameProfileNameRefuse()
    {
        var names = RealNames();
        names["u-alu"] = "Geaf_Argento";   // now two accounts claim one slot

        Assert.Null(MatchTeamMap.Resolve(RealTwoVsTwo(), names));
    }

    [Fact]
    public void AHeadCountThatDisagreesWithTheRoomRefuses()
    {
        var names = RealNames();
        names.Remove("u-alu");             // three in the room, four in the recording

        Assert.Null(MatchTeamMap.Resolve(RealTwoVsTwo(), names));
    }

    [Fact]
    public void NoTeamsAtAllIsNotATeamGame()
    {
        // What every one of fourteen real 1v1s carries, and what a free-for-all carries: team
        // id -1 on every slot. There is nothing to record, and inventing a split would be worse.
        var players = new List<ReplayParserService.ReplayPlayer>
        {
            new(1, "Ana", 0, -1, ReplayParserService.SlotTypeHuman),
            new(2, "Beto", 0, -1, ReplayParserService.SlotTypeHuman),
        };
        var names = new Dictionary<string, string> { ["a"] = "Ana", ["b"] = "Beto" };

        Assert.Null(MatchTeamMap.Resolve(players, names));
    }

    [Fact]
    public void EveryoneOnOneSideIsNotATeamGameEither()
    {
        var players = new List<ReplayParserService.ReplayPlayer>
        {
            new(1, "Ana", 0, 0, ReplayParserService.SlotTypeHuman),
            new(2, "Beto", 0, 0, ReplayParserService.SlotTypeHuman),
        };
        var names = new Dictionary<string, string> { ["a"] = "Ana", ["b"] = "Beto" };

        Assert.Null(MatchTeamMap.Resolve(players, names));
    }

    [Fact]
    public void AMixOfRealTeamsAndNoTeamRefuses()
    {
        var players = new List<ReplayParserService.ReplayPlayer>
        {
            new(1, "Ana", 0, 0, ReplayParserService.SlotTypeHuman),
            new(2, "Beto", 0, 1, ReplayParserService.SlotTypeHuman),
            new(3, "Caro", 0, -1, ReplayParserService.SlotTypeHuman),
        };
        var names = new Dictionary<string, string>
        {
            ["a"] = "Ana", ["b"] = "Beto", ["c"] = "Caro",
        };

        Assert.Null(MatchTeamMap.Resolve(players, names));
    }

    [Fact]
    public void NothingToWorkFromIsRefusedRatherThanThrown()
    {
        Assert.Null(MatchTeamMap.Resolve(null, RealNames()));
        Assert.Null(MatchTeamMap.Resolve(RealTwoVsTwo(), null));
        Assert.Null(MatchTeamMap.Resolve(RealTwoVsTwo(), new Dictionary<string, string>()));
        Assert.Null(MatchTeamMap.Resolve(
            RealTwoVsTwo(),
            new Dictionary<string, string> { ["a"] = "", ["b"] = "x", ["c"] = "y", ["d"] = "z" }));
    }
}
