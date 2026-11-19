using System.Linq;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The launch argument read by things-new's <c>lanAutoStart</c>. The refusals are the point: the
/// joiner's address comes from the server and goes onto a command line.
/// </summary>
public class LanAutoStartArgsTests
{
    [Theory]
    [InlineData(true, "lanAutoStart")]
    [InlineData(true, "  LANAUTOSTART host rules=1")]
    [InlineData(true, "lanAutoStart=host")]
    [InlineData(true, "lanAutoStart // with a comment")]
    [InlineData(false, "#lanAutoStart")]
    [InlineData(false, "# lanAutoStart")]
    [InlineData(false, "// lanAutoStart")]
    [InlineData(false, "lanAutoStartX")]
    [InlineData(false, "multipleInstances lanAutoStart")]
    [InlineData(false, "")]
    public void ACfgEnablesTheKeyOnlyByItsFirstWord(bool expected, string line)
    {
        Assert.Equal(expected, LanAutoStartArgs.CfgEnablesLanAutoStart(new[] { "ignoreRegistryPath", line }));
    }

    [Fact]
    public void AnInstallNamesTheCfgThatEnablesIt()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lanauto-" + System.Guid.NewGuid());
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, "Startup"));
            System.IO.File.WriteAllLines(System.IO.Path.Combine(root, "Startup", "wol.cfg"), new[] { "noAILimitV2" });
            Assert.Null(LanAutoStartArgs.CfgEnablingLanAutoStart(root));

            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, "engine-ext"));
            System.IO.File.WriteAllLines(System.IO.Path.Combine(root, "engine-ext", "engine-ext.cfg"), new[] { "lanAutoStart" });
            Assert.Equal(System.IO.Path.Combine("engine-ext", "engine-ext.cfg"), LanAutoStartArgs.CfgEnablingLanAutoStart(root));

            Assert.Null(LanAutoStartArgs.CfgEnablingLanAutoStart(null));
            Assert.Null(LanAutoStartArgs.CfgEnablingLanAutoStart(System.IO.Path.Combine(root, "missing")));
        }
        finally
        {
            System.IO.Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TheHostHostsWhateverAddressItKnows()
    {
        Assert.Equal(" +uhcLanHost", LanAutoStartArgs.For(true, null));
        Assert.Equal(" +uhcLanHost", LanAutoStartArgs.For(true, "26.1.2.3"));
    }

    [Fact]
    public void TheHostPassesTheRulesItChoseAndNothingForStandard()
    {
        Assert.Equal(" +uhcLanHost uhcLanRules=7", LanAutoStartArgs.For(true, null, LanAutoStartArgs.KingOfTheHill));
        Assert.Equal(" +uhcLanHost uhcLanRules=1", LanAutoStartArgs.For(true, null, LanAutoStartArgs.Classic));
        Assert.Equal(" +uhcLanHost", LanAutoStartArgs.For(true, null, LanAutoStartArgs.Standard));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void EveryTreatyIsPassed(int rules)
    {
        // Wars of Liberty labels them "Not supported"; the game hosts them all the same.
        Assert.Equal($" +uhcLanHost uhcLanRules={rules}", LanAutoStartArgs.For(true, null, rules));
        Assert.True(LanAutoStartArgs.IsTreaty(rules));
    }

    [Fact]
    public void TheDialogOffersTheGamesEightEntriesInItsOrder()
    {
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, LanAutoStartArgs.OfferedRules);
        Assert.Equal(8, LanAutoStartArgs.OfferedRules.Select(LanAutoStartArgs.RulesLabelKey).Distinct().Count());
    }

    [Fact]
    public void EveryOfferedRuleHasADescriptionAndEveryTreatyItsLength()
    {
        foreach (var rules in LanAutoStartArgs.OfferedRules)
        {
            var key = LanAutoStartArgs.RulesDescriptionKey(rules);
            Assert.NotEqual(key, WarsOfLibertyLauncher.Localization.Strings.Get(key));
            Assert.Equal(LanAutoStartArgs.IsTreaty(rules), LanAutoStartArgs.TreatyMinutes(rules) > 0);
        }
        Assert.Equal(new[] { 10, 20, 30, 40, 40 },
            new[] { 2, 3, 4, 5, 6 }.Select(LanAutoStartArgs.TreatyMinutes));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(8)]
    public void RulesNotOfferedAreNeverPassed(int rules)
    {
        Assert.Equal(" +uhcLanHost", LanAutoStartArgs.For(true, null, rules));
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_ACompetitiveRoomOffersStandardOnly()
    {
        Assert.Equal(new[] { LanAutoStartArgs.Standard }, LanAutoStartArgs.RulesOfferedFor(competitive: true, noTreaty: false));
        Assert.Equal(new[] { LanAutoStartArgs.Standard }, LanAutoStartArgs.RulesOfferedFor(competitive: true, noTreaty: true));
        Assert.Equal(LanAutoStartArgs.Standard, LanAutoStartArgs.EffectiveRules(LanAutoStartArgs.KingOfTheHill, true, false));
        Assert.Equal(LanAutoStartArgs.Standard, LanAutoStartArgs.EffectiveRules(LanAutoStartArgs.Classic, true, false));
    }

    [Fact]
    public void AModWithoutTreatiesOffersStandardClassicAndKingOfTheHill()
    {
        Assert.Equal(new[] { LanAutoStartArgs.Standard, LanAutoStartArgs.Classic, LanAutoStartArgs.KingOfTheHill },
            LanAutoStartArgs.RulesOfferedFor(competitive: false, noTreaty: true));
        Assert.Equal(LanAutoStartArgs.OfferedRules, LanAutoStartArgs.RulesOfferedFor(false, false));
    }

    [Fact]
    public void ASavedTreatyFallsBackToStandardWhereTreatiesAreNotOffered()
    {
        Assert.Equal(LanAutoStartArgs.Standard, LanAutoStartArgs.EffectiveRules(LanAutoStartArgs.Treaty20, false, true));
        Assert.Equal(LanAutoStartArgs.Treaty20, LanAutoStartArgs.EffectiveRules(LanAutoStartArgs.Treaty20, false, false));
        Assert.Equal(LanAutoStartArgs.Classic, LanAutoStartArgs.EffectiveRules(LanAutoStartArgs.Classic, false, true));
        Assert.Equal(LanAutoStartArgs.Standard, LanAutoStartArgs.EffectiveRules(42, false, false));
    }

    [Fact]
    public void ACompetitiveHostPassesStandardExplicitly()
    {
        // The game's dropdown opens on what the host last chose by hand; leaving the argument out
        // would not guarantee Standard.
        Assert.Equal(" +uhcLanHost uhcLanRules=0", LanAutoStartArgs.For(true, null, LanAutoStartArgs.Treaty10, forceStandard: true));
        Assert.Equal(" +uhcLanHost uhcLanRules=0", LanAutoStartArgs.For(true, null, LanAutoStartArgs.Standard, forceStandard: true));
        Assert.Equal(" uhcLanJoin=\"26.1.2.3\"", LanAutoStartArgs.For(false, "26.1.2.3", forceStandard: true));
    }

    [Fact]
    public void TheAutomaticEntrySwitchedOffPassesNothing()
    {
        Assert.Equal("", LanAutoStartArgs.For(true, null, LanAutoStartArgs.KingOfTheHill, enabled: false));
        Assert.Equal("", LanAutoStartArgs.For(true, null, forceStandard: true, enabled: false));
        Assert.Equal("", LanAutoStartArgs.For(false, "26.1.2.3", enabled: false));
    }

    [Fact]
    public void WarsOfLibertyDeclaresItsTreatiesUnsupported()
    {
        var wol = WarsOfLibertyLauncher.Services.ModRegistry.Find(WarsOfLibertyLauncher.Services.ModRegistry.WolId);
        Assert.NotNull(wol);
        Assert.True(wol!.NoTreatyRules);
    }

    [Fact]
    public void AJoinerNeverPassesRules()
    {
        Assert.Equal(" uhcLanJoin=\"26.1.2.3\"", LanAutoStartArgs.For(false, "26.1.2.3", LanAutoStartArgs.KingOfTheHill));
    }

    [Fact]
    public void AJoinerJoinsTheHostsAddressInQuotes()
    {
        Assert.Equal(" uhcLanJoin=\"26.162.244.170\"", LanAutoStartArgs.For(false, "26.162.244.170"));
        Assert.Equal(" uhcLanJoin=\"26.1.2.3\"", LanAutoStartArgs.For(false, "  26.1.2.3 "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("26.1.2")]                  // IPAddress.TryParse would read this as 26.1.0.2
    [InlineData("26.1.2.3.4")]
    [InlineData("26.1.2.300")]
    [InlineData("26.1.2.3\" +evil")]
    [InlineData("26.1.2.3 -x")]
    [InlineData("example.com")]
    [InlineData("fe80::1")]
    [InlineData("127.0.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("255.255.255.255")]
    public void AJoinerWithNoUsableHostAddressGetsNothing(string? ip)
    {
        Assert.Equal("", LanAutoStartArgs.For(false, ip));
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_APromotedHostPassesTheRoomsRules()
        // He never chose them and his own local value is Standard: the room's are what he launches.
        => Assert.Equal(LanAutoStartArgs.KingOfTheHill,
            LanAutoStartArgs.RoomRules(LanAutoStartArgs.KingOfTheHill, LanAutoStartArgs.Standard, competitive: false, noTreaty: false));

    [Fact]
    public void AnOlderServerKeepsTheHostsOwnChoice()
        => Assert.Equal(LanAutoStartArgs.Treaty30,
            LanAutoStartArgs.RoomRules(null, LanAutoStartArgs.Treaty30, competitive: false, noTreaty: false));

    [Fact]
    public void TheServersValueWinsOverTheDialog()
        => Assert.Equal(LanAutoStartArgs.Standard,
            LanAutoStartArgs.RoomRules(LanAutoStartArgs.Standard, LanAutoStartArgs.Treaty30, competitive: false, noTreaty: false));

    [Theory]
    [InlineData(true, false)]   // a ranked room is Standard whatever anybody says
    [InlineData(false, true)]   // a treaty on a mod whose treaties do not work is never launched
    public void ARoomNeverLaunchesRulesItMayNotUse(bool competitive, bool noTreaty)
        => Assert.Equal(LanAutoStartArgs.Standard,
            LanAutoStartArgs.RoomRules(LanAutoStartArgs.Treaty40, LanAutoStartArgs.Standard, competitive, noTreaty));

    [Theory]
    [InlineData(null, false)]   // an older server said nothing: no chip, never "Standard"
    [InlineData(0, false)]      // Standard on every room would say nothing
    [InlineData(99, false)]
    [InlineData(-1, false)]
    [InlineData(1, true)]
    [InlineData(5, true)]
    [InlineData(7, true)]
    public void ARoomWithNoStatedRulesShowsNoChip(int? rules, bool shown)
        => Assert.Equal(shown, LanAutoStartArgs.ShowsRulesChip(rules));
}
