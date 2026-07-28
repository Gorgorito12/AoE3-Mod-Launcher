using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Tests for the <c>.age3Yrec</c> header parser.
///
/// <para><b>The fixture is real.</b> <c>Fixtures/zv104-header.age3Yrec</c> is a genuine
/// 2v2 recorded game — its decompressed stream trimmed to the first 64 KB (the whole
/// settings dictionary fits comfortably) and repacked into a valid container, so the
/// repo carries 12 KB instead of 909 KB. Every byte the parser reads is a byte the game
/// wrote.</para>
///
/// <para>That matters more than usual here. A fixture built by encoding my own reading of
/// the format would pass by construction and prove nothing — the exact trap
/// <c>GameSettingsSyncTests</c> documents, where tests encoded a misreading and stayed
/// green. The multiplayer replay was chosen over a skirmish because only it exercises
/// real team ids; against the AI every slot reports team -1.</para>
///
/// <para>The <b>rejection</b> cases are the point, as in <c>SafeUrlTests</c>. This parser
/// reads a file produced by a 2007 engine that may have been killed mid-write, on the
/// way out of a match — it has to refuse cleanly, never throw and never invent.</para>
/// </summary>
public class ReplayParserTests
{
    private static byte[] Fixture()
        => File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "zv104-header.age3Yrec"));

    // ---------------- the real file ----------------

    [Fact]
    public void ParsesTheRealRecordedGame()
    {
        var header = ReplayParserService.TryParse(Fixture());

        Assert.NotNull(header);
        Assert.Equal(4, header!.PlayerCount);
        Assert.Contains("age3y.exe", header.GameVersion);
    }

    [Fact]
    public void MapNameIsTheMapPlayed_NotThePool()
    {
        // gamemapname holds the map POOL on a competitive game; gamefilename holds the
        // map. They agree on a plain skirmish (both "amazonia"), so taking the wrong one
        // looks correct right up until it reaches the games that matter.
        var header = ReplayParserService.TryParse(Fixture())!;

        Assert.Equal("ESOC_Baja California", header.MapName);
        Assert.Equal("ESOC Maps", header.MapPool);
    }

    [Fact]
    public void ReadsEveryHumanPlayerAndDropsTheEmptySlots()
    {
        var header = ReplayParserService.TryParse(Fixture())!;
        var humans = header.Players.Where(p => p.IsHuman).ToList();

        Assert.Equal(4, humans.Count);
        Assert.Equal(
            new[] { "69metal69", "ElZeldaVerde", "Geaf_Argento", "Alucard" },
            humans.OrderBy(p => p.Slot).Select(p => p.Name));
    }

    [Fact]
    public void ReadsTheTeamsThatMakeItA2v2()
    {
        // The reason the fixture is a multiplayer game: a skirmish against the AI
        // reports team -1 for everyone and would never catch a teams regression.
        var humans = ReplayParserService.TryParse(Fixture())!
            .Players.Where(p => p.IsHuman).OrderBy(p => p.Slot).ToList();

        Assert.Equal(new[] { 0, 1, 0, 1 }, humans.Select(p => p.TeamId));
    }

    [Fact]
    public void CivilizationIsTheRawIndex_NotAName()
    {
        // The index means different civilizations in different mods, so the parser
        // hands back the number and lets the caller resolve it against that mod's
        // civ list. Pinned so nobody "helpfully" maps it to base-game names.
        var humans = ReplayParserService.TryParse(Fixture())!
            .Players.Where(p => p.IsHuman).OrderBy(p => p.Slot).ToList();

        Assert.Equal(new[] { 17, 41, 35, 33 }, humans.Select(p => p.Civilization));
    }

    // ---------------- rejections: these are the point ----------------

    [Fact]
    public void RejectsAFileWithoutTheMagic()
    {
        var bytes = Fixture();
        bytes[0] = (byte)'x';
        Assert.Null(ReplayParserService.TryReadContainer(bytes));
    }

    [Fact]
    public void RejectsWhenTheDeclaredSizeDisagreesWithTheRealOne()
    {
        // The cheap integrity check: a truncated or corrupt recording is caught here
        // rather than by parsing offsets that can no longer be trusted.
        var bytes = Fixture();
        var wrong = BitConverter.GetBytes((uint)12345);
        Array.Copy(wrong, 0, bytes, 4, 4);

        Assert.Null(ReplayParserService.TryReadContainer(bytes));
    }

    [Fact]
    public void RejectsACorruptCompressedStream()
    {
        var bytes = Fixture();
        for (var i = 200; i < 400 && i < bytes.Length; i++) bytes[i] ^= 0xFF;

        Assert.Null(ReplayParserService.TryReadContainer(bytes));
    }

    [Fact]
    public void RejectsAFileCutInHalf()
    {
        var bytes = Fixture();
        Assert.Null(ReplayParserService.TryReadContainer(bytes.Take(bytes.Length / 2).ToArray()));
    }

    [Fact]
    public void RejectsAnAbsurdDeclaredSizeWithoutInflatingIt()
    {
        // Guards against a hostile or corrupt header asking us to allocate the world.
        var bytes = Fixture();
        var huge = BitConverter.GetBytes((uint)(ReplayParserService.MaxDecompressedBytes + 1));
        Array.Copy(huge, 0, bytes, 4, 4);

        Assert.Null(ReplayParserService.TryReadContainer(bytes));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(11)]
    public void RejectsFilesTooShortToBeAContainer(int length)
        => Assert.Null(ReplayParserService.TryReadContainer(new byte[length]));

    [Fact]
    public void RejectsNull()
    {
        Assert.Null(ReplayParserService.TryReadContainer(null!));
        Assert.Null(ReplayParserService.ParseHeader(null!));
    }

    [Fact]
    public void RejectsAValidContainerHoldingSomethingElse()
    {
        // Right wrapper, contents that are not a settings dictionary — the shape a
        // renamed file would have. It must come back null, not half-parsed.
        var payload = Encoding.UTF8.GetBytes(new string('x', 4096));
        Assert.Null(ReplayParserService.ParseHeader(payload));
        Assert.Null(ReplayParserService.TryParse(Pack(payload)));
    }

    [Fact]
    public void DoesNotThrowOnTruncatedHeaderData()
    {
        // Cutting the decompressed stream mid-key must end the walk, not crash and not
        // resynchronise into the command stream and report confident nonsense.
        var data = ReplayParserService.TryReadContainer(Fixture())!;
        for (var cut = 64; cut < 4096; cut += 331)
        {
            var ex = Record.Exception(() => ReplayParserService.ParseHeader(data.Take(cut).ToArray()));
            Assert.Null(ex);
        }
    }

    // ---------------- the outcome ----------------
    //
    // These two fixtures are the maintainer's own skirmishes, and their results are known
    // independently of anything the parser says: one was resigned, the other won. Each is
    // the real stream's first 64 KB (the settings dictionary) joined to its last 4 KB (the
    // outcome trailer) and repacked — a composite, stated plainly, and sound only because
    // the parser reads exactly those two regions and nothing between them.
    //
    // The reading was confirmed on five games in two contexts: three skirmishes and two
    // real 1v1s between humans, the latter predicted BEFORE the outcome was known and then
    // confirmed by the player who lost them.

    private static byte[] Loss() => Fx("wol-loss-arizona.age3Yrec");   // resigned  → slot 1 lost
    private static byte[] Win() => Fx("wol-win-amazonia.age3Yrec");    // won       → slot 2 lost

    private static byte[] Fx(string name)
        => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static ReplayParserService.ReplayOutcome OutcomeOf(byte[] raw)
    {
        var data = ReplayParserService.TryReadContainer(raw)!;
        return ReplayParserService.ReadOutcome(data, ReplayParserService.ParseHeader(data));
    }

    [Fact]
    public void ReadsTheLoserOfAGameThatWasResigned()
    {
        var o = OutcomeOf(Loss());

        Assert.Equal(ReplayParserService.ReplayOutcomeConfidence.Confident, o.Confidence);
        Assert.Equal(1, o.LoserSlot);   // the human resigned
        Assert.Equal(2, o.WinnerSlot);  // so the AI won
    }

    [Fact]
    public void ReadsTheLoserOfAGameThatWasWon()
    {
        // The case that kills every rival reading: the human WON here and holds slot 1,
        // so a field meaning "winner" or "whoever recorded this" would say 1. It says 2.
        var o = OutcomeOf(Win());

        Assert.Equal(ReplayParserService.ReplayOutcomeConfidence.Confident, o.Confidence);
        Assert.Equal(2, o.LoserSlot);
        Assert.Equal(1, o.WinnerSlot);
    }

    [Fact]
    public void ReportsWhichSlotRecordedTheFile()
    {
        // Both were recorded by the human in slot 1 — including the one he won, where the
        // loser field says 2. That difference is what makes these two fields distinct.
        Assert.Equal(1, OutcomeOf(Loss()).RecorderSlot);
        Assert.Equal(1, OutcomeOf(Win()).RecorderSlot);
    }

    [Fact]
    public void AGameWithNoTrailerIsAmbiguous_NotADraw()
    {
        // Two of seven real recordings end without the trailer, having finished
        // abnormally. They must come back Ambiguous so the caller reports a draw
        // deliberately rather than reading a missing answer as one.
        var data = ReplayParserService.TryReadContainer(Loss())!;
        var truncated = data.Take(data.Length - 64).ToArray();

        var o = ReplayParserService.ReadOutcome(truncated, ReplayParserService.ParseHeader(data));

        Assert.Equal(ReplayParserService.ReplayOutcomeConfidence.Ambiguous, o.Confidence);
    }

    [Fact]
    public void ALoserSlotThatIsNotInTheGameIsAmbiguous()
    {
        var data = (byte[])ReplayParserService.TryReadContainer(Loss())!.Clone();
        BitConverter.GetBytes((uint)9).CopyTo(data, data.Length - 12);

        var o = ReplayParserService.ReadOutcome(data, ReplayParserService.ParseHeader(data));

        Assert.Equal(ReplayParserService.ReplayOutcomeConfidence.Ambiguous, o.Confidence);
    }

    [Fact]
    public void MoreThanTwoPlayersIsAmbiguousEvenWithTheSignature()
    {
        // "X lost" names a winner only in a 1v1. With more players the others may have
        // lost too and nothing here says in what order, so the trailer is not enough.
        var data = ReplayParserService.TryReadContainer(Loss())!;
        var header = ReplayParserService.ParseHeader(data)!;
        var crowded = header with
        {
            Players = header.Players
                .Append(new ReplayParserService.ReplayPlayer(3, "Third", 1, 0, 0))
                .ToList(),
        };

        var o = ReplayParserService.ReadOutcome(data, crowded);

        Assert.Equal(ReplayParserService.ReplayOutcomeConfidence.Ambiguous, o.Confidence);
        Assert.Equal(1, o.LoserSlot);   // still reported, just not decisive on its own
    }

    [Fact]
    public void ReadOutcomeRejectsNullsAndStubs()
    {
        var header = ReplayParserService.ParseHeader(ReplayParserService.TryReadContainer(Loss())!);

        Assert.Equal(ReplayParserService.ReplayOutcomeConfidence.Ambiguous,
            ReplayParserService.ReadOutcome(null!, header).Confidence);
        Assert.Equal(ReplayParserService.ReplayOutcomeConfidence.Ambiguous,
            ReplayParserService.ReadOutcome(new byte[8], header).Confidence);
        Assert.Equal(ReplayParserService.ReplayOutcomeConfidence.Ambiguous,
            ReplayParserService.ReadOutcome(new byte[64], null).Confidence);
    }

    /// <summary>Wraps a payload the way the game does, so tests can build odd containers.</summary>
    private static byte[] Pack(byte[] payload)
    {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(payload, 0, payload.Length);

        var compressed = ms.ToArray();
        var result = new byte[8 + compressed.Length];
        Encoding.ASCII.GetBytes("l33t").CopyTo(result, 0);
        BitConverter.GetBytes((uint)payload.Length).CopyTo(result, 4);
        compressed.CopyTo(result, 8);
        return result;
    }
}
