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
    public void ReadsTheMatchFingerprintFromTheRealFile()
    {
        // Measured out of this exact fixture, not chosen: seed 21427, host clock 1310758.
        // Asserting numbers read from a real recording is the whole reason the fixture is
        // a genuine file — values invented to match my reading of the format would pass
        // by construction and prove nothing.
        //
        // These two are what identify the MATCH rather than the players. Both machines in
        // one game must generate the same map, so the seed is shared by construction; two
        // different games are not. That is what lets the server tell whether the host and
        // their opponent read the same match, without comparing a single name — profile
        // names are frequently nothing like the Discord account and were rejected for it.
        var header = ReplayParserService.TryParse(Fixture());

        Assert.NotNull(header);
        Assert.Equal(21427u, header!.RandomSeed);
        Assert.Equal(1310758u, header.HostTime);
    }


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

    private static ReplayParserService.ReplayOutcome OutcomeOf(
        byte[] raw,
        Func<ReplayParserService.ReplayHeader, ReplayParserService.ReplayHeader>? adjust = null)
    {
        var data = ReplayParserService.TryReadContainer(raw)!;
        var header = ReplayParserService.ParseHeader(data)!;
        return ReplayParserService.ReadOutcome(data, adjust?.Invoke(header) ?? header);
    }

    [Fact]
    public void ASkirmishAgainstTheAiNeverProducesAResult()
    {
        // Both fixtures are skirmishes, so neither may yield a verdict however clean the
        // trailer is. Reporting already cannot reach one — it needs a lobby, and an AI is
        // never a room member — but the replay is picked as "newest file after the match
        // started", so a stray skirmish recording can arrive here, and a result derived
        // from one would look exactly as trustworthy as a real one.
        Assert.Equal(ReplayParserService.ReplayOutcomeConfidence.Ambiguous, OutcomeOf(Loss()).Confidence);
        Assert.Equal(ReplayParserService.ReplayOutcomeConfidence.Ambiguous, OutcomeOf(Win()).Confidence);
    }

    [Fact]
    public void TheLoserIsStillReadCorrectlyInARefusedSkirmish()
    {
        // Refusing to rule on it is not the same as failing to read it: the slot stays
        // available for a diagnostic bundle, it just carries no authority.
        Assert.Equal(1, OutcomeOf(Loss()).LoserSlot);   // the human resigned
        Assert.Equal(2, OutcomeOf(Win()).LoserSlot);    // the AI was beaten
    }

    [Fact]
    public void ReadsTheLoserOfAGameThatWasResigned()
    {
        // Same real trailer, with the opponent labelled human — which is what a 1v1
        // between people looks like. Only the label is simulated; the bytes under test
        // are the ones the game wrote. Real human 1v1s are checked outside the suite so
        // no third party's handle ends up in the repo.
        var o = OutcomeOf(Loss(), AllHuman);

        Assert.Equal(ReplayParserService.ReplayOutcomeConfidence.Confident, o.Confidence);
        Assert.Equal(1, o.LoserSlot);
        Assert.Equal(2, o.WinnerSlot);
    }

    [Fact]
    public void ReadsTheLoserOfAGameThatWasWon()
    {
        // The case that kills every rival reading: the recorder WON here and holds slot 1,
        // so a field meaning "winner" or "whoever recorded this" would say 1. It says 2.
        var o = OutcomeOf(Win(), AllHuman);

        Assert.Equal(ReplayParserService.ReplayOutcomeConfidence.Confident, o.Confidence);
        Assert.Equal(2, o.LoserSlot);
        Assert.Equal(1, o.WinnerSlot);
    }

    /// <summary>Relabels every slot as human, leaving the replay bytes untouched.</summary>
    private static ReplayParserService.ReplayHeader AllHuman(ReplayParserService.ReplayHeader h)
        => h with
        {
            Players = h.Players
                .Select(p => p with { SlotType = ReplayParserService.SlotTypeHuman })
                .ToList(),
        };

    /// <summary>
    /// Reports the trailer's second field WITHOUT claiming to know what it is.
    ///
    /// <para>This test used to be called <c>ReportsWhichSlotRecordedTheFile</c> and read
    /// the same two values as proof that B names the recorder. It does not. Both fixtures
    /// are singleplayer games from the same human on the same machine, and with one player
    /// "the one who recorded it" and "the one who ended it" are the same person — so the
    /// evidence could never separate them. Three multiplayer matches captured from BOTH
    /// players did: the two copies of one match are byte-identical for the last 64 bytes,
    /// so B cannot name the machine that wrote one, and in all six copies B is the loser.
    /// Use <see cref="ReplayParserService.FindPlayerSlot"/> for identity; this value is
    /// carried only so a diagnostic bundle can show it.</para>
    /// </summary>
    [Fact]
    public void ReportsTheTrailersSecondFieldWithoutInterpretingIt()
    {
        Assert.Equal(1, OutcomeOf(Loss()).TrailerSecondSlot);
        Assert.Equal(1, OutcomeOf(Win()).TrailerSecondSlot);
    }

    [Fact]
    public void SeparatesAMissingTrailerFromAnUnusableOne()
    {
        // Two different things to tell a player — "the game never wrote its ending" versus
        // "it wrote one we cannot use" — so they are two fields, not one sentinel. Both
        // fixtures are skirmishes, hence Ambiguous, but their trailers are right there.
        Assert.True(OutcomeOf(Win()).SignaturePresent);
        Assert.True(OutcomeOf(Loss()).SignaturePresent);

        // Break the first of the twelve zero bytes, which is exactly how the real
        // abnormally-ended recording fails: the signature check dies on byte one.
        var data = (byte[])ReplayParserService.TryReadContainer(Win())!.Clone();
        var header = ReplayParserService.ParseHeader(data)!;
        Assert.True(ReplayParserService.ReadOutcome(data, header).SignaturePresent);

        data[^32] = 0x02;
        Assert.False(ReplayParserService.ReadOutcome(data, header).SignaturePresent);
    }

    [Fact]
    public void AGameWithNoTrailerIsAmbiguous_NotADraw()
    {
        // Two of seven real recordings end without the trailer, having finished
        // abnormally. They must come back Ambiguous so the caller reports a draw
        // deliberately rather than reading a missing answer as one.
        //
        // Relabelled human so the missing trailer is the ONLY reason it fails.
        var data = ReplayParserService.TryReadContainer(Loss())!;
        var header = AllHuman(ReplayParserService.ParseHeader(data)!);
        var truncated = data.Take(data.Length - 64).ToArray();

        var o = ReplayParserService.ReadOutcome(truncated, header);

        Assert.Equal(ReplayParserService.ReplayOutcomeConfidence.Ambiguous, o.Confidence);
    }

    [Fact]
    public void ALoserSlotThatIsNotInTheGameIsAmbiguous()
    {
        var data = (byte[])ReplayParserService.TryReadContainer(Loss())!.Clone();
        var header = AllHuman(ReplayParserService.ParseHeader(data)!);
        BitConverter.GetBytes((uint)9).CopyTo(data, data.Length - 12);

        var o = ReplayParserService.ReadOutcome(data, header);

        Assert.Equal(ReplayParserService.ReplayOutcomeConfidence.Ambiguous, o.Confidence);
    }

    [Fact]
    public void MoreThanTwoPlayersIsAmbiguousEvenWithTheSignature()
    {
        // "X lost" names a winner only in a 1v1. With more players the others may have
        // lost too and nothing here says in what order, so the trailer is not enough.
        //
        // Everyone is relabelled human first, so this fails on the player COUNT alone —
        // otherwise the AI rule would also reject it and the test would pass without
        // exercising what it claims to.
        var data = ReplayParserService.TryReadContainer(Loss())!;
        var header = AllHuman(ReplayParserService.ParseHeader(data)!);
        var crowded = header with
        {
            Players = header.Players
                .Append(new ReplayParserService.ReplayPlayer(
                    3, "Third", 1, 0, ReplayParserService.SlotTypeHuman))
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

    // ---------------- outcome → the host's score ----------------
    //
    // The one line where a mistake silently moves rating points between two real people,
    // so it is pure and pinned here rather than living in the WPF caller.

    [Fact]
    public void TheHostWhoWonScoresOne()
    {
        // The game he actually won: he recorded it from slot 1 and the trailer blames 2.
        var o = OutcomeOf(Win(), AllHuman);

        Assert.Equal(1.0, ReplayParserService.HostResultFrom(o, hostSlot: 1));
    }

    [Fact]
    public void TheHostWhoLostScoresZero()
    {
        var o = OutcomeOf(Loss(), AllHuman);   // he resigned from slot 1

        Assert.Equal(0.0, ReplayParserService.HostResultFrom(o, hostSlot: 1));
        Assert.Equal(1.0, ReplayParserService.HostResultFrom(o, hostSlot: 2));
    }

    [Fact]
    public void AnAmbiguousOutcomeScoresNothing()
    {
        // The rule the whole feature rests on: no answer is null, which the caller turns
        // into the 0.5 draw reporting used before any of this existed. Never a winner.
        var skirmish = OutcomeOf(Win());   // real trailer, refused for being against AI

        Assert.Null(ReplayParserService.HostResultFrom(skirmish, hostSlot: 1));
        Assert.Null(ReplayParserService.HostResultFrom(null, hostSlot: 1));
    }

    /// <summary>
    /// <b>The invariant the whole rating rests on:</b> the two players' copies of one match
    /// must score OPPOSITELY, from the same bytes.
    ///
    /// <para>They never did. The local slot was taken from the trailer's B field, which is
    /// the loser and is identical in both copies — so <c>HostResultFrom</c> answered 0.0 on
    /// both machines and could not return a win at all. Across two players' complete logs,
    /// every confident reading ever recorded was 0.0; not one was 1.0. Resolving the slot
    /// by name is what makes these two assertions able to differ.</para>
    /// </summary>
    [Fact]
    public void EachPlayerScoresTheOppositeFromTheSameBytes()
    {
        var data = ReplayParserService.TryReadContainer(Win())!;

        // The real trailer of a game slot 1 won, over a roster naming both people — which
        // is what a human 1v1 looks like, and what neither fixture can supply on its own.
        var header = ReplayParserService.ParseHeader(data)! with
        {
            Players = new[]
            {
                new ReplayParserService.ReplayPlayer(
                    1, "Winner", 1, 0, ReplayParserService.SlotTypeHuman),
                new ReplayParserService.ReplayPlayer(
                    2, "Loser", 1, 0, ReplayParserService.SlotTypeHuman),
            },
        };

        var outcome = ReplayParserService.ReadOutcome(data, header);

        var won = ReplayParserService.HostResultFrom(
            outcome, ReplayParserService.FindPlayerSlot(header, "Winner"));
        var lost = ReplayParserService.HostResultFrom(
            outcome, ReplayParserService.FindPlayerSlot(header, "Loser"));

        Assert.Equal(1.0, won);
        Assert.Equal(0.0, lost);
        Assert.Equal(1.0, won!.Value + lost!.Value);
    }

    [Fact]
    public void AHostWhoIsNeitherPlayerScoresNothing()
    {
        // Either the wrong recording was picked or the slots do not mean what we think.
        // Both are reasons to say nothing rather than to pick one of the two at random.
        var o = OutcomeOf(Win(), AllHuman);

        Assert.Null(ReplayParserService.HostResultFrom(o, hostSlot: 5));
        Assert.Null(ReplayParserService.HostResultFrom(o, hostSlot: -1));
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
