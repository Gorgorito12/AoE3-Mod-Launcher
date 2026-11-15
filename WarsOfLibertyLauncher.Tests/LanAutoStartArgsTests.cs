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
}
