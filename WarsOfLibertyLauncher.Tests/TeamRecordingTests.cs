using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;
using Xunit.Abstractions;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins how a TEAM match is read out of its recording: the sides from the map-setup string, the
/// losers from the resign records, and the checks that keep either reading from being wrong.
///
/// <para><b>The first competitive 2v2s scored nothing, for two reasons this file exists to keep
/// fixed.</b> The sides were read from <c>gameplayer{N}teamid</c>, the lobby's dropdown, which
/// was -1 for all four players; and the winner was read from the outcome block, which is only the
/// LAST resignation — one casualty out of two. Both answers are elsewhere in the same file.</para>
///
/// <para><b>Every fixture is genuine bytes.</b> The <c>team2v2-*</c> files are real four-player
/// recordings, SPLICED to stay small: the first 65,536 inflated bytes (the settings dictionary and
/// the setup string, which measured between 19,912 and 48,023 bytes in) followed by the last N
/// bytes (every resign record and the outcome block), repacked as <c>l33t</c> + declared size +
/// zlib so <see cref="ReplayParserService.TryReadContainer"/> accepts them. Offsets from the END
/// are preserved, which is what the outcome block and the self-check depend on. N is 16 KB for
/// Malaysia, Kamchatka and Baja, 4 KB for the ESOC_Iowa file, and 266 KB for ZV 104, whose first
/// resignation sits 270,802 bytes before its end. Inventing bytes to match a reading of the
/// format would pass by construction and prove nothing.</para>
/// </summary>
public class TeamRecordingTests
{
    private readonly ITestOutputHelper _out;

    public TeamRecordingTests(ITestOutputHelper output) => _out = output;

    private sealed record Fixture(byte[] Data, ReplayParserService.ReplayHeader Header);

    private static Fixture Load(string name)
    {
        var raw = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
        var data = ReplayParserService.TryReadContainer(raw);
        Assert.NotNull(data);
        var header = ReplayParserService.ParseHeader(data!);
        Assert.NotNull(header);
        return new Fixture(data!, header!);
    }

    // ---------------------------------------------------------------- the sides

    [Theory]
    [InlineData("team2v2-malaysia.age3Yrec", new[] { 0, 0, 1, 1 })]
    [InlineData("team2v2-kamchatka.age3Yrec", new[] { 0, 1, 1, 0 })]
    [InlineData("team2v2-baja.age3Yrec", new[] { 1, 0, 1, 0 })]
    [InlineData("team2v2-zv104.age3Yrec", new[] { 0, 1, 0, 1 })]
    [InlineData("team2v2-iowa-removals.age3Yrec", new[] { 0, 1, 0, 1 })]
    [InlineData("zv104-header.age3Yrec", new[] { 0, 1, 0, 1 })]
    [InlineData("wol-loss-arizona.age3Yrec", new[] { 0, 1 })]
    [InlineData("wol-win-amazonia.age3Yrec", new[] { 0, 1 })]
    public void EveryRecordingCarriesTheSidesTheGameAssigned(string fixture, int[] expected)
    {
        var f = Load(fixture);
        var players = f.Header.Players.OrderBy(p => p.Slot).ToList();

        Assert.Equal(expected, players.Select(p => p.GameTeam));
        Assert.Equal(expected, players.Select(p => p.Team));
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_TheSidesDoNotDependOnTheLobbyDropdown()
    {
        // The exact shape of the first 2v2s: nobody touched the Team dropdown, so every teamid is
        // -1 — and the game still knows the sides, because it assigned them.
        var f = Load("team2v2-malaysia.age3Yrec");

        Assert.All(f.Header.Players, p => Assert.Equal(-1, p.TeamId));
        Assert.Equal(new[] { 0, 0, 1, 1 }, f.Header.Players.OrderBy(p => p.Slot).Select(p => p.Team));
    }

    [Fact]
    public void WhereTheLobbyDidSetATeam_ItAgreesWithTheGame()
    {
        // ESOC_Iowa: the dropdown was half filled, [0,-1,-1,1]. Where it says something it says
        // what the game says — measured on every recording, and the reason a disagreement refuses.
        var f = Load("team2v2-iowa-removals.age3Yrec");
        var players = f.Header.Players.OrderBy(p => p.Slot).ToList();

        Assert.Equal(new[] { 0, -1, -1, 1 }, players.Select(p => p.TeamId));
        Assert.Equal(new[] { 0, 1, 0, 1 }, players.Select(p => p.Team));
    }

    [Theory]
    [InlineData(-1, -1, -1)]   // nothing known
    [InlineData(0, -1, 0)]     // only the lobby — every recording that already worked keeps reading the same
    [InlineData(-1, 1, 1)]     // only the game
    [InlineData(1, 1, 1)]      // both, agreeing
    [InlineData(0, 1, -1)]     // both, DISAGREEING: not a recording this reading understands
    public void TheSideRule(int lobby, int game, int expected)
        => Assert.Equal(expected, ReplayParserService.ResolveTeam(lobby, game));

    // ---- the setup string's refusals, on synthetic bytes so each one is isolated ----

    private static List<ReplayParserService.ReplayPlayer> Players(params int[] civs)
        => civs.Select((civ, i) => new ReplayParserService.ReplayPlayer(
            i + 1, $"P{i + 1}", civ, -1, ReplayParserService.SlotTypeHuman)).ToList();

    /// <summary>Filler, then each string length-prefixed. The odd filler puts the first string
    /// at an ODD offset, which is where some real copies sit.</summary>
    private static byte[] WithStrings(params string[] strings)
    {
        var ms = new MemoryStream();
        ms.Write(new byte[37]);
        foreach (var s in strings)
        {
            ms.Write(BitConverter.GetBytes((uint)s.Length));
            ms.Write(Encoding.Unicode.GetBytes(s));
            ms.Write(new byte[11]);
        }
        return ms.ToArray();
    }

    private static IReadOnlyDictionary<int, int>? Read(byte[] data, IReadOnlyList<ReplayParserService.ReplayPlayer> players, uint seed = 123)
        => ReplayParserService.ReadSetupTeams(data, "ESOC_Test Map", players.Count, seed, players, out _);

    [Fact]
    public void ASetupStringAtAnOddOffsetIsRead()
    {
        var teams = Read(WithStrings("ESOC_Test Map/4/123/0/1/10/0/11/1/12/0/13"), Players(10, 11, 12, 13));

        Assert.NotNull(teams);
        Assert.Equal(new[] { 1, 0, 1, 0 }, new[] { teams![1], teams[2], teams[3], teams[4] });
    }

    [Fact]
    public void EverySetupStringThatDoesNotFitIsRefused()
    {
        var four = Players(10, 11, 12, 13);

        // A civilization that is not the header's: the string is not describing these players.
        Assert.Null(Read(WithStrings("ESOC_Test Map/4/123/0/1/10/0/11/1/99/0/13"), four));
        // Another game's seed: no string of THIS match.
        Assert.Null(Read(WithStrings("ESOC_Test Map/4/999/0/1/10/0/11/1/12/0/13"), four));
        // A pair missing, or one too many.
        Assert.Null(Read(WithStrings("ESOC_Test Map/4/123/0/1/10/0/11/1/12"), four));
        Assert.Null(Read(WithStrings("ESOC_Test Map/4/123/0/1/10/0/11/1/12/0/13/1/14"), four));
        // A negative or non-numeric side.
        Assert.Null(Read(WithStrings("ESOC_Test Map/4/123/0/-1/10/0/11/1/12/0/13"), four));
        Assert.Null(Read(WithStrings("ESOC_Test Map/4/123/0/a/10/0/11/1/12/0/13"), four));
        // No string at all.
        Assert.Null(Read(WithStrings("something else entirely"), four));
        // The header describes a player the string does not.
        var five = Players(10, 11, 12, 13, 14);
        Assert.Null(ReplayParserService.ReadSetupTeams(
            WithStrings("ESOC_Test Map/4/123/0/1/10/0/11/1/12/0/13"), "ESOC_Test Map", 4, 123, five, out _));
    }

    [Fact]
    public void TwoCopies_OrACopyThatDoesNotValidate_RefusesTheWholeThing()
    {
        var four = Players(10, 11, 12, 13);
        const string good = "ESOC_Test Map/4/123/0/1/10/0/11/1/12/0/13";

        // The engine writes it exactly once (90 of 90 measured). Two means the reading is not of
        // what was measured, and choosing one would be a guess.
        Assert.Null(Read(WithStrings(good, good), four));
        // A second, length-prefixed occurrence that does not validate is not ignored either.
        Assert.Null(Read(WithStrings(good, "ESOC_Test Map/4/123/0/1/99/0/11/1/12/0/13"), four));
    }

    [Fact]
    public void TheSameTextInsideSomethingElseIsNotACandidate()
    {
        // Somebody types the setup line into a chat message. Its four preceding bytes are text,
        // not a plausible length, so it is not a copy at all — and the real one is still read.
        var four = Players(10, 11, 12, 13);
        var data = WithStrings(
            "gg ESOC_Test Map/4/123/0/0/10/1/11/0/12/1/13",
            "ESOC_Test Map/4/123/0/1/10/0/11/1/12/0/13");

        var teams = Read(data, four);
        Assert.NotNull(teams);
        Assert.Equal(1, teams![1]);
    }

    // ---------------------------------------------------------------- the resignations

    // sender>target, in the order they were given.
    [Theory]
    [InlineData("team2v2-malaysia.age3Yrec", "1>1 2>2")]
    [InlineData("team2v2-kamchatka.age3Yrec", "4>4 1>1")]
    [InlineData("team2v2-baja.age3Yrec", "2>2 4>4")]
    [InlineData("team2v2-zv104.age3Yrec", "4>4 2>2")]
    // Slot 4 removed both dropped opponents: the sender is not the player who is out.
    [InlineData("team2v2-iowa-removals.age3Yrec", "4>3 4>1")]
    // A skirmish the human WON: his machine resigned the defeated AI.
    [InlineData("wol-win-amazonia.age3Yrec", "1>2")]
    // One he lost: he resigned himself.
    [InlineData("wol-loss-arizona.age3Yrec", "1>1")]
    public void EveryResignationIsRead_SenderAndTarget(string fixture, string expected)
    {
        var f = Load(fixture);
        var records = ReplayParserService.ReadResignations(f.Data, f.Header);

        Assert.Equal(expected, string.Join(" ", records.Select(r => $"{r.Sender}>{r.Target}")));
    }

    [Theory]
    [InlineData("team2v2-malaysia.age3Yrec")]
    [InlineData("team2v2-kamchatka.age3Yrec")]
    [InlineData("team2v2-baja.age3Yrec")]
    [InlineData("team2v2-zv104.age3Yrec")]
    [InlineData("team2v2-iowa-removals.age3Yrec")]
    [InlineData("wol-win-amazonia.age3Yrec")]
    [InlineData("wol-loss-arizona.age3Yrec")]
    public void TheLastResignationIsTheOutcomeBlock(string fixture)
    {
        // The self-check that makes reading command 0x10 safe for a mod nobody has measured: the
        // block the 1v1 path already trusts is the tail of the last record, byte for byte.
        var f = Load(fixture);
        var outcome = ReplayParserService.ReadOutcome(f.Data, f.Header);
        var records = ReplayParserService.ReadResignations(f.Data, f.Header);

        Assert.True(outcome.BlockOffset >= 0);
        Assert.Equal(outcome.BlockOffset, records[^1].Offset + ReplayParserService.ResignBlockOffsetInRecord);
        Assert.True(ReplayParserService.ResignationsAgreeWithOutcome(records, outcome));
    }

    [Fact]
    public void AnEarlierResignationSitsFarBeyondAnyTailWindow()
    {
        // Why the whole stream is scanned. ZV 104's first resignation is 270,802 bytes before the
        // end: a window sized for the outcome block would decide this match from its LAST
        // casualty alone, which is one player out of two.
        var f = Load("team2v2-zv104.age3Yrec");
        var first = ReplayParserService.ReadResignations(f.Data, f.Header)[0];

        Assert.Equal(270_802, f.Data.Length - first.Offset);
    }

    [Fact]
    public void ARecordWithAnyByteOutOfPlaceIsNotARecord()
    {
        var f = Load("team2v2-malaysia.age3Yrec");
        var last = ReplayParserService.ReadResignations(f.Data, f.Header)[^1];

        // Each of the fixed fields, broken one at a time.
        foreach (var at in new[] { 1, 10, 18, 22, 30, 34, 42, 46, 50, 61 })
        {
            var broken = (byte[])f.Data.Clone();
            broken[last.Offset + at] ^= 0x5A;
            var records = ReplayParserService.ReadResignations(broken, f.Header);
            Assert.DoesNotContain(records, r => r.Offset == last.Offset);
        }

        // The sender written at +73 must be the one at +6.
        var mismatched = (byte[])f.Data.Clone();
        mismatched[last.Offset + 73] = 3;
        Assert.DoesNotContain(ReplayParserService.ReadResignations(mismatched, f.Header), r => r.Offset == last.Offset);
    }

    [Fact]
    public void ARecordNamingASlotTheGameDoesNotHaveIsNotARecord()
    {
        var f = Load("team2v2-malaysia.age3Yrec");
        var last = ReplayParserService.ReadResignations(f.Data, f.Header)[^1];

        var stranger = (byte[])f.Data.Clone();
        BitConverter.GetBytes(9u).CopyTo(stranger, last.Offset + 69);   // target slot 9

        Assert.DoesNotContain(ReplayParserService.ReadResignations(stranger, f.Header), r => r.Offset == last.Offset);
    }

    [Fact]
    public void ARecordCutByTheEndOfTheFileIsNotARecord()
    {
        var f = Load("team2v2-malaysia.age3Yrec");
        var last = ReplayParserService.ReadResignations(f.Data, f.Header)[^1];

        var cut = f.Data[..(last.Offset + 60)];
        Assert.DoesNotContain(ReplayParserService.ReadResignations(cut, f.Header), r => r.Offset == last.Offset);
    }

    [Fact]
    public void RecordsThatDisagreeWithTheOutcomeBlockAreNotTrusted()
    {
        var f = Load("team2v2-malaysia.age3Yrec");
        var outcome = ReplayParserService.ReadOutcome(f.Data, f.Header);
        var records = ReplayParserService.ReadResignations(f.Data, f.Header);

        // A block naming somebody else, or sitting where no record put it.
        Assert.False(ReplayParserService.ResignationsAgreeWithOutcome(records, outcome with { LoserSlot = 3 }));
        Assert.False(ReplayParserService.ResignationsAgreeWithOutcome(records, outcome with { BlockOffset = outcome.BlockOffset - 1 }));
        // A block that NO record wrote.
        Assert.False(ReplayParserService.ResignationsAgreeWithOutcome(
            Array.Empty<ReplayParserService.ResignRecord>(), outcome));
        // No block at all: nothing to contradict, the records stand on their layout.
        Assert.True(ReplayParserService.ResignationsAgreeWithOutcome(records, outcome with { BlockOffset = -1 }));
    }

    [Fact]
    public void AnUnreadableRecordingReadsNoResignations()
    {
        Assert.Empty(ReplayParserService.ReadResignations(Array.Empty<byte>(), null));
        var f = Load("zv104-header.age3Yrec");
        // The header-only fixture holds no command stream at all.
        Assert.Empty(ReplayParserService.ReadResignations(f.Data, f.Header));
    }

    // ---------------------------------------------------------------- the whole reading

    // fixture, losing slots — read from the recordings and agreed with the players.
    [Theory]
    [InlineData("team2v2-malaysia.age3Yrec", new[] { 1, 2 })]    // El Taita + Kaiser
    [InlineData("team2v2-kamchatka.age3Yrec", new[] { 1, 4 })]   // Kaiser + El Taita
    [InlineData("team2v2-baja.age3Yrec", new[] { 2, 4 })]        // Streletsy + Kaiser
    [InlineData("team2v2-zv104.age3Yrec", new[] { 2, 4 })]       // ElZeldaVerde + Alucard
    [InlineData("team2v2-iowa-removals.age3Yrec", new[] { 1, 3 })]
    public void EveryRealTwoVersusTwoIsDecided_FromTheFileAlone(string fixture, int[] losers)
    {
        var f = Load(fixture);
        var outcome = ReplayParserService.ReadOutcome(f.Data, f.Header);
        var records = ReplayParserService.ReadResignations(f.Data, f.Header);
        Assert.True(ReplayParserService.ResignationsAgreeWithOutcome(records, outcome));

        var d = MatchResultResolver.ResolveTeamResultsBySlot(
            f.Header.Players, records, outcome.LoserSlot, perSide: 2);

        Assert.NotNull(d.ScoresBySlot);
        foreach (var p in f.Header.Players)
            Assert.Equal(losers.Contains(p.Slot) ? 0.0 : 1.0, d.ScoresBySlot![p.Slot]);
    }

    [Theory]
    [InlineData("team2v2-malaysia.age3Yrec", new[] { 1, 2 })]    // El Taita + Kaiser
    [InlineData("team2v2-kamchatka.age3Yrec", new[] { 1, 4 })]   // Kaiser + El Taita
    [InlineData("team2v2-baja.age3Yrec", new[] { 2, 4 })]        // Streletsy + Kaiser
    [InlineData("team2v2-zv104.age3Yrec", new[] { 2, 4 })]       // ElZeldaVerde + Alucard
    [InlineData("team2v2-iowa-removals.age3Yrec", new[] { 1, 3 })]
    public void AndTheAccountsAreCredited_ThroughThePublishedNames(string fixture, int[] losers)
    {
        var f = Load(fixture);
        var outcome = ReplayParserService.ReadOutcome(f.Data, f.Header);
        var records = ReplayParserService.ReadResignations(f.Data, f.Header);
        // Every account published exactly the name its slot carries, as a working room does.
        var names = f.Header.Players.ToDictionary(p => $"user-{p.Slot}", p => p.Name);

        var teams = MatchTeamMap.Resolve(f.Header.Players, names);
        Assert.NotNull(teams);
        Assert.True(RoomFormats.TeamsAgreeWithFormat(RoomFormat.TwoVTwo, teams));

        var scores = MatchResultResolver.ResolveTeamResults(names, f.Header.Players, records, outcome.LoserSlot, perSide: 2);
        Assert.NotNull(scores);
        foreach (var p in f.Header.Players)
            Assert.Equal(losers.Contains(p.Slot) ? 0.0 : 1.0, scores![$"user-{p.Slot}"]);
    }

    [Fact]
    public void ARemovalIsTraceable_ByWhoSentIt()
    {
        var f = Load("team2v2-iowa-removals.age3Yrec");
        var records = ReplayParserService.ReadResignations(f.Data, f.Header);

        Assert.Equal(
            "3:Jeops removed by 4:Geaf_Argento, 1:Gommiustan removed by 4:Geaf_Argento",
            ReplayParserService.DescribeResignations(records, f.Header.Players));
    }

    [Fact]
    public void AOneVersusOneStillHasNoTeams()
    {
        // The setup string gives a 1v1 a side per player now. Read as teams, a 1v1 room would log
        // a format mismatch and a casual 1v1 would start storing teams it never had — so the map
        // refuses sides of one, and a 1v1 reports exactly what it always did.
        var f = Load("wol-loss-arizona.age3Yrec");
        var duel = f.Header.Players
            .Select(p => p with { SlotType = ReplayParserService.SlotTypeHuman }).ToList();
        var names = duel.ToDictionary(p => $"user-{p.Slot}", p => p.Name);

        var teams = MatchTeamMap.Resolve(duel, names);

        Assert.Null(teams);
        Assert.True(RoomFormats.TeamsAgreeWithFormat(RoomFormat.OneVOne, teams));
    }

    // ---------------------------------------------------------------- the corpus

    /// <summary>
    /// Every recording under <c>AOE3ML_REPLAY_CORPUS</c> (folders separated by <c>;</c>), checked
    /// against the facts this reading rests on. Returns at once when the variable is unset, so the
    /// ordinary suite never depends on anybody's disk.
    ///
    /// <para><b>This is how a new mod gets MEASURED instead of assumed.</b> Record a short game of
    /// it, resign, point the variable at the folder and run this test: the setup string must be
    /// found, the lobby and the game must agree where both are set, and the last resign record
    /// must be the outcome block. A team recording must be decided.</para>
    /// </summary>
    [Fact]
    public void TheWholeCorpus_WhenOneIsGiven()
    {
        var roots = Environment.GetEnvironmentVariable("AOE3ML_REPLAY_CORPUS");
        if (string.IsNullOrWhiteSpace(roots)) return;

        var seen = new HashSet<string>();
        var problems = new List<string>();
        int readable = 0, setup = 0, agree = 0, teamFiles = 0, teamDecided = 0, noEnding = 0;

        foreach (var root in roots.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        foreach (var path in Directory.EnumerateFiles(root, "*.age3Yrec", SearchOption.AllDirectories))
        {
            var raw = File.ReadAllBytes(path);
            if (!seen.Add(Convert.ToHexString(SHA256.HashData(raw)))) continue;

            var data = ReplayParserService.TryReadContainer(raw);
            var header = data == null ? null : ReplayParserService.ParseHeader(data);
            if (data == null || header == null || header.Players.Count == 0) continue;
            readable++;

            var name = Path.GetFileName(path);
            if (header.Players.All(p => p.GameTeam >= 0)) setup++;
            else problems.Add($"{name}: no setup string ({header.GameVersion})");

            if (header.Players.Any(p => p.Team < 0 && p.GameTeam >= 0))
                problems.Add($"{name}: lobby and game disagree about a side");

            var outcome = ReplayParserService.ReadOutcome(data, header);
            var records = ReplayParserService.ReadResignations(data, header);
            if (ReplayParserService.ResignationsAgreeWithOutcome(records, outcome)) agree++;
            else problems.Add($"{name}: the last resign record is not the outcome block");

            var humans = header.Players.Where(p => p.IsHuman).ToList();
            if (humans.Count >= 4 && humans.Count == header.Players.Count)
            {
                teamFiles++;
                // A file with neither a resignation nor an outcome block has no ending to decide
                // from — a game abandoned by everybody, or a header cut down for a fixture. The
                // refusal there is correct, so it is counted, not reported.
                if (records.Count == 0 && outcome.BlockOffset < 0) { noEnding++; continue; }
                var d = MatchResultResolver.ResolveTeamResultsBySlot(header.Players, records, outcome.LoserSlot);
                if (d.ScoresBySlot != null) teamDecided++;
                else problems.Add($"{name}: team recording not decided — {d.Reason}");
            }
        }

        _out.WriteLine(
            $"corpus: {readable} readable, setup string {setup}, resignations agree {agree}, " +
            $"team files {teamFiles} of which decided {teamDecided} and {noEnding} with no ending to read");
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }
}
