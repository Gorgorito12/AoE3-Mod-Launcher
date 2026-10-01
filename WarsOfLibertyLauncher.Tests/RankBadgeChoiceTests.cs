using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Which rank badge a player wears (design handoff 51b), and the words that go with it (51a).
///
/// <para><b>The order of authority is what these pin</b>: a room whose format is known decides
/// for everybody in it; otherwise the player's stored preference decides; Highest compares the
/// AGE and a tie goes to 1v1. Each rule is one line of code and each one, wrong, silently puts
/// the wrong badge beside somebody's name in front of everyone.</para>
/// </summary>
[Collection("wpf-and-language")]
public class RankBadgeChoiceTests
{
    // ---------------------------------------------------------------- the room decides

    [Fact]
    public void A1v1RoomWearsThe1v1Badge_EvenForAPlayerWhoChoseTeams()
        => Assert.Equal(BadgeKind.Solo,
            RankBadgeChoice.Decide(RoomFormat.OneVOne, BadgeMode.Team, RankAge.Colonial, RankAge.Sovereign));

    [Theory]
    [InlineData(RoomFormat.TwoVTwo)]
    [InlineData(RoomFormat.ThreeVThree)]
    public void ATeamRoomWearsTheTeamBadge_EvenForAPlayerWhoChose1v1(RoomFormat room)
        => Assert.Equal(BadgeKind.Team,
            RankBadgeChoice.Decide(room, BadgeMode.Solo, RankAge.Sovereign, RankAge.Colonial));

    /// <summary>
    /// No decided team match yet: in a team room that player still wears the team badge, as the
    /// Discovery double shield — the room decides for everybody.
    /// </summary>
    [Fact]
    public void ATeamRoomGivesANewcomerTheDiscoveryDoubleShield()
        => Assert.Equal(BadgeKind.Team,
            RankBadgeChoice.Decide(RoomFormat.TwoVTwo, BadgeMode.Highest, RankAge.Imperial, RankAge.Discovery));

    /// <summary>A server that sends no team rank: the team room falls back to what it drew before.</summary>
    [Fact]
    public void ATeamRoomWithAnOlderServerFallsBackTo1v1()
        => Assert.Equal(BadgeKind.Solo,
            RankBadgeChoice.Decide(RoomFormat.TwoVTwo, BadgeMode.Team, RankAge.Imperial, null));

    // ---------------------------------------------------------------- the preference decides

    /// <summary>
    /// A casual room, a room whose format cannot be read, and no room at all are all "not known"
    /// — the preference decides. A casual room's seat count is NOT read as a format.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData(RoomFormat.Casual)]
    [InlineData(RoomFormat.Unknown)]
    public void AnUnknownModeFollowsThePreference(RoomFormat? room)
    {
        Assert.Equal(BadgeKind.Team, RankBadgeChoice.Decide(room, BadgeMode.Team, RankAge.Sovereign, RankAge.Colonial));
        Assert.Equal(BadgeKind.Solo, RankBadgeChoice.Decide(room, BadgeMode.Solo, RankAge.Colonial, RankAge.Sovereign));
    }

    [Fact]
    public void HighestTakesTheHigherAge_InEitherDirection()
    {
        Assert.Equal(BadgeKind.Team, RankBadgeChoice.Decide(null, BadgeMode.Highest, RankAge.Industrial, RankAge.Imperial));
        Assert.Equal(BadgeKind.Solo, RankBadgeChoice.Decide(null, BadgeMode.Highest, RankAge.Imperial, RankAge.Industrial));
    }

    /// <summary>THE ONE THAT MATTERS for Highest: a tie goes to 1v1.</summary>
    [Theory]
    [InlineData(RankAge.Colonial)]
    [InlineData(RankAge.Industrial)]
    [InlineData(RankAge.Sovereign)]
    public void THE_ONE_THAT_MATTERS_HighestBreaksATieTowards1v1(RankAge age)
        => Assert.Equal(BadgeKind.Solo, RankBadgeChoice.Decide(null, BadgeMode.Highest, age, age));

    /// <summary>
    /// Highest compares AGES, never positions: first of a tiny team ladder and fourth of a big
    /// 1v1 ladder can be the same age, and then it is a tie — 1v1.
    /// </summary>
    [Fact]
    public void HighestComparesTheAgeNotThePosition()
    {
        var shown = RankBadgeChoice.Resolve(null, BadgeMode.Highest,
            soloPosition: 4, soloLadderSize: 40, teamPosition: 1, teamLadderSize: 1);
        Assert.Equal(RankAges.For(4, 40), RankAges.For(1, 1));
        Assert.Equal(BadgeKind.Solo, shown!.Value.Kind);
    }

    /// <summary>Teams with no decided team match — Discovery or unknown — shows the 1v1 badge.</summary>
    [Theory]
    [InlineData(BadgeMode.Team)]
    [InlineData(BadgeMode.Highest)]
    public void TeamsWithNoTeamMatchesShows1v1(BadgeMode mode)
    {
        Assert.Equal(BadgeKind.Solo, RankBadgeChoice.Decide(null, mode, RankAge.Colonial, RankAge.Discovery));
        Assert.Equal(BadgeKind.Solo, RankBadgeChoice.Decide(null, mode, RankAge.Colonial, null));
        Assert.False(RankBadgeChoice.HasTeamBadge(RankAge.Discovery));
        Assert.False(RankBadgeChoice.HasTeamBadge(null));
        Assert.True(RankBadgeChoice.HasTeamBadge(RankAge.Colonial));
    }

    /// <summary>A known team badge is never hidden behind an unknown 1v1 one.</summary>
    [Fact]
    public void HighestWithNo1v1RankShowsTheTeamBadge()
        => Assert.Equal(BadgeKind.Team, RankBadgeChoice.Decide(null, BadgeMode.Highest, null, RankAge.Fortress));

    /// <summary>An unknown position for the chosen ladder draws no badge, never a guessed one.</summary>
    [Fact]
    public void AnUnknownPositionDrawsNothing()
        => Assert.Null(RankBadgeChoice.Resolve(null, BadgeMode.Solo, null, 10, 3, 10));

    [Fact]
    public void ResolveCarriesTheOtherLadderForTheTooltip()
    {
        var b = RankBadgeChoice.Resolve(RoomFormat.TwoVTwo, BadgeMode.Highest, 5, 18, 2, 18)!.Value;
        Assert.Equal(BadgeKind.Team, b.Kind);
        Assert.Equal(2, b.Position);
        Assert.Equal(RankAges.For(5, 18), b.OtherAge);
        Assert.Equal(5, b.OtherPosition);
    }

    [Theory]
    [InlineData("highest", BadgeMode.Highest)]
    [InlineData("1v1", BadgeMode.Solo)]
    [InlineData("team", BadgeMode.Team)]
    public void TheWireSpellingRoundTrips(string wire, BadgeMode mode)
    {
        Assert.Equal(mode, BadgeModes.Parse(wire));
        Assert.Equal(wire, BadgeModes.ToWire(mode));
    }

    /// <summary>Anything the launcher does not know — or nothing at all — is Highest.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("default")]
    [InlineData("Team")]
    public void AnythingElseParsesAsHighest(string? wire)
        => Assert.Equal(BadgeMode.Highest, BadgeModes.Parse(wire));

    // ---------------------------------------------------------------- the words

    private static void In(string lang, System.Action body)
    {
        var was = Strings.Language;
        try { Strings.SetLanguage(lang); body(); }
        finally { Strings.SetLanguage(was); }
    }

    /// <summary>The handoff's own example, word for word, in both languages.</summary>
    [Fact]
    public void TheTooltipNamesBothBadges()
    {
        var b = new ShownBadge(BadgeKind.Team, RankAge.Imperial, 2, RankAge.Industrial, 5);
        In("en", () => Assert.Equal("Teams rank · Imperial\n#2 in the teams ladder · 1v1: Industrial #5",
            RankBadgeTips.Text(b, 1)));
        In("es", () => Assert.Equal("Rango Equipos · Imperial\n#2 en la clasificación de equipos · 1v1: Industrial #5",
            RankBadgeTips.Text(b, 1)));
    }

    /// <summary>An unknown other ladder is not mentioned; a Discovery one carries no "#0".</summary>
    [Fact]
    public void TheTooltipNeverPrintsHashZero()
    {
        In("en", () =>
        {
            var unknown = new ShownBadge(BadgeKind.Solo, RankAge.Industrial, 5, null, 0);
            Assert.Equal("1v1 rank · Industrial\n#5 in the 1v1 ladder", RankBadgeTips.Text(unknown, 1));

            var unplaced = new ShownBadge(BadgeKind.Solo, RankAge.Industrial, 5, RankAge.Discovery, 0);
            var text = RankBadgeTips.Text(unplaced, 1);
            Assert.EndsWith("Teams: Discovery", text);
            Assert.DoesNotContain("#0", text);

            var newcomer = new ShownBadge(BadgeKind.Team, RankAge.Discovery, 0, RankAge.Colonial, 9);
            text = RankBadgeTips.Text(newcomer, 1);
            Assert.Contains(Strings.Get("MpBadgeModeTeamsLocked"), text);
            Assert.DoesNotContain("#0", text);
        });
    }

    [Fact]
    public void TheDetailLineAddsTheMode()
    {
        In("en", () =>
        {
            var b = new ShownBadge(BadgeKind.Team, RankAge.Imperial, 2, null, 0);
            Assert.Equal("1612 ELO · you · Teams · Imperial", RankBadgeTips.DetailLine("1612 ELO", "you", b));
            Assert.Equal("1612 ELO · Teams · Imperial", RankBadgeTips.DetailLine("1612 ELO", null, b));
            Assert.Equal("1612 ELO · you", RankBadgeTips.DetailLine("1612 ELO", "you", null));
            Assert.Equal("Teams · Imperial", RankBadgeTips.ModeAndAge(b));
        });
    }
}
