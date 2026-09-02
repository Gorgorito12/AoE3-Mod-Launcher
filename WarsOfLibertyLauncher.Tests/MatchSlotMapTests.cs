using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="MatchSlotMap"/> — which recording slot each Discord account played.
///
/// <para><b>The reason it exists at all is the first test below.</b> This join used to live
/// inside <see cref="MatchTeamMap"/>, which then refuses every slot whose team id is negative —
/// and a negative team id is what ALL FOURTEEN measured 1v1 recordings carry, because AoE3
/// writes "no team" when there are no teams. Right for teams, fatal for anything else: asking
/// the team map for the join would have left every 1v1 without a civilization, which is most of
/// the matches that rate.</para>
///
/// <para>The rest are refusals, for the same reason they are in <see cref="MatchTeamMapTests"/>:
/// a half-filled map attaches a real person's civilization to somebody else's slot in a stored
/// match, and nothing downstream could tell.</para>
/// </summary>
public class MatchSlotMapTests
{
    private static ReplayParserService.ReplayPlayer Human(int slot, string name, int team = -1)
        => new(slot, name, Civilization: 0, TeamId: team, SlotType: ReplayParserService.SlotTypeHuman);

    private static ReplayParserService.ReplayPlayer Ai(int slot, string name)
        => new(slot, name, Civilization: 0, TeamId: -1, SlotType: 1);

    /// <summary>The shape every real 1v1 has: two humans, both with AoE3's "no team".</summary>
    private static List<ReplayParserService.ReplayPlayer> OneVsOne() => new()
    {
        Human(1, "Gorgorito"),
        Human(2, "Alucard"),
    };

    private static Dictionary<string, string> TwoNames() => new()
    {
        ["u-gorgo"] = "Gorgorito",
        ["u-alu"] = "Alucard",
    };

    /// <summary>
    /// THE REGRESSION. Both must hold together: the slot map answers for a 1v1 and the team map
    /// still refuses it. Asserting only the first would pass with the old shared implementation
    /// and prove nothing about why this class was split out.
    /// </summary>
    [Fact]
    public void AOneVsOneResolvesHere_AndIsStillRefusedByTheTeamMap()
    {
        var slots = MatchSlotMap.Resolve(OneVsOne(), TwoNames());

        Assert.NotNull(slots);
        Assert.Equal(1, slots!["u-gorgo"].Slot);
        Assert.Equal(2, slots["u-alu"].Slot);

        Assert.Null(MatchTeamMap.Resolve(OneVsOne(), TwoNames()));
    }

    /// <summary>
    /// And the team map still reads a real 2v2 exactly as it did — it is built on this now, so
    /// the extraction has to be provably behaviour-preserving from the other side too.
    /// </summary>
    [Fact]
    public void TheTeamMapStillReadsTheRealTwoVsTwo()
    {
        var raw = File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "zv104-header.age3Yrec"));
        var header = ReplayParserService.TryParse(raw);
        Assert.NotNull(header);

        var names = new Dictionary<string, string>
        {
            ["u-metal"] = "69metal69",
            ["u-zelda"] = "ElZeldaVerde",
            ["u-geaf"] = "Geaf_Argento",
            ["u-alu"] = "Alucard",
        };

        var slots = MatchSlotMap.Resolve(header!.Players, names);
        var teams = MatchTeamMap.Resolve(header.Players, names);

        Assert.NotNull(slots);
        Assert.Equal(4, slots!.Count);
        Assert.NotNull(teams);
        Assert.Equal(2, teams!.Values.Distinct().Count());
    }

    // ------------------------------------------------------------------ refusals

    [Fact]
    public void NothingToJoinIsNotAnAnswer()
    {
        Assert.Null(MatchSlotMap.Resolve(null, TwoNames()));
        Assert.Null(MatchSlotMap.Resolve(OneVsOne(), null));
        Assert.Null(MatchSlotMap.Resolve(OneVsOne(), new Dictionary<string, string>()));
    }

    /// <summary>
    /// The recording has to be of THIS match. A head count that disagrees means the wrong file
    /// or somebody the room never saw, and a map built from it would be silently wrong rather
    /// than merely absent.
    /// </summary>
    [Fact]
    public void AHeadCountThatDisagreesRefusesEverything()
    {
        var names = TwoNames();
        names["u-third"] = "Someone";

        Assert.Null(MatchSlotMap.Resolve(OneVsOne(), names));
    }

    /// <summary>One unmatched name refuses the WHOLE map, never just that player.</summary>
    [Fact]
    public void OneNameNobodyPlayedRefusesTheWholeMap()
    {
        var names = new Dictionary<string, string>
        {
            ["u-gorgo"] = "Gorgorito",
            ["u-alu"] = "SomebodyElse",
        };

        Assert.Null(MatchSlotMap.Resolve(OneVsOne(), names));
    }

    /// <summary>
    /// Two players called the same thing cannot be told apart, and picking one would be
    /// inventing an answer. Checked on both sides, since either can carry the duplicate.
    /// </summary>
    [Fact]
    public void DuplicateNamesRefuse()
    {
        var sameInRecording = new List<ReplayParserService.ReplayPlayer>
        {
            Human(1, "Gorgorito"),
            Human(2, "Gorgorito"),
        };
        Assert.Null(MatchSlotMap.Resolve(sameInRecording, TwoNames()));

        var sameDeclared = new Dictionary<string, string>
        {
            ["u-a"] = "Gorgorito",
            ["u-b"] = "gorgorito",   // the comparison is case-insensitive, like FindPlayerSlot
        };
        Assert.Null(MatchSlotMap.Resolve(OneVsOne(), sameDeclared));
    }

    /// <summary>
    /// The same case-insensitive, trimmed comparison <c>FindPlayerSlot</c> uses for the local
    /// player — one machine's answer about itself and this method's answer about everyone must
    /// never disagree. The profile name is per MOD and its case is not stable.
    /// </summary>
    [Fact]
    public void TheNameComparisonIgnoresCaseAndSurroundingSpace()
    {
        var names = new Dictionary<string, string>
        {
            ["u-gorgo"] = "  gorgorito ",
            ["u-alu"] = "ALUCARD",
        };

        var slots = MatchSlotMap.Resolve(OneVsOne(), names);

        Assert.NotNull(slots);
        Assert.Equal(1, slots!["u-gorgo"].Slot);
        Assert.Equal(2, slots["u-alu"].Slot);
    }

    /// <summary>
    /// Only humans are joined. An AI occupies a slot and belongs to no account, so counting it
    /// would make the head count disagree with the room and refuse a perfectly good match.
    /// </summary>
    [Fact]
    public void TheAiIsNotAPlayerToJoin()
    {
        var withAi = new List<ReplayParserService.ReplayPlayer>
        {
            Human(1, "Gorgorito"),
            Ai(2, "Menelik II"),
        };

        var oneName = new Dictionary<string, string> { ["u-gorgo"] = "Gorgorito" };

        var slots = MatchSlotMap.Resolve(withAi, oneName);

        Assert.NotNull(slots);
        Assert.Single(slots!);
        Assert.Equal(1, slots["u-gorgo"].Slot);
    }

    [Fact]
    public void ABlankIdOrNameRefuses()
    {
        Assert.Null(MatchSlotMap.Resolve(OneVsOne(), new Dictionary<string, string>
        {
            ["u-gorgo"] = "Gorgorito",
            [" "] = "Alucard",
        }));

        Assert.Null(MatchSlotMap.Resolve(OneVsOne(), new Dictionary<string, string>
        {
            ["u-gorgo"] = "Gorgorito",
            ["u-alu"] = "   ",
        }));
    }
}
