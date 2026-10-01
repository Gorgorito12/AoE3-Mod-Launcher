using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Which badge a player wears WHERE (docs/design_insignia_equipos, 51b), checked on the real rows
/// rather than on <see cref="RankBadgeChoice"/> alone: the pure rule can be perfectly right and a
/// call site can still hand it the wrong room or the wrong preference, which is the failure a
/// player would actually see.
///
/// <para>The cases that matter are the ones where the room OVERRULES the player: a 2v2 room shows
/// the team badge to somebody who chose 1v1, and a 1v1 room the 1v1 badge to somebody who chose
/// Teams. Only a room that says nothing about its format — a casual one — defers to the choice.</para>
/// </summary>
[Collection("wpf-and-language")]
public class RankBadgeContextTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void THE_ONE_THAT_MATTERS_ATeamRoomShowsTheHostsTeamBadgeEvenWhenTheyChose1v1()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var card = RoomCard(competitive: true, maxPlayers: 4, solo: 5, team: 2, mode: "1v1");
            var team = Assert.Single(TeamBadges(card));
            Assert.Equal(RankAges.ForOptional(2, 0), ((RankBadge.TeamBadgeTag)team.Tag).Age);
        });
        Assert.Null(error);
    }

    [Fact]
    public void A1v1RoomIgnoresATeamPreference()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var card = RoomCard(competitive: true, maxPlayers: 2, solo: 5, team: 2, mode: "team");
            Assert.Empty(TeamBadges(card));
            Assert.Equal(RankAges.ForOptional(5, 0), Assert.Single(Badges(card)).Tag);
        });
        Assert.Null(error);
    }

    [Fact]
    public void ACasualRoomFollowsTheHostsChoice()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            Assert.Single(TeamBadges(RoomCard(competitive: false, maxPlayers: 8, solo: 5, team: 2, mode: "team")));
            Assert.Empty(TeamBadges(RoomCard(competitive: false, maxPlayers: 8, solo: 5, team: 2, mode: "1v1")));
        });
        Assert.Null(error);
    }

    /// <summary>
    /// A team room with somebody who never played a team match still shows the TEAM badge — the
    /// double Discovery shield — and never quietly the 1v1 one, which would tell the room their
    /// team record is their 1v1 record.
    /// </summary>
    [Fact]
    public void ATeamRoomShowsTheDiscoveryDoubleShieldToANewcomer()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var card = RoomCard(competitive: true, maxPlayers: 4, solo: 3, team: 0, mode: "highest");
            Assert.Equal(RankAge.Discovery, ((RankBadge.TeamBadgeTag)Assert.Single(TeamBadges(card)).Tag).Age);
        });
        Assert.Null(error);
    }

    [Fact]
    public void InA2v2TheRosterWearsTheTeamBadgeAndItsLineCarriesTheTeamRating()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            typeof(MultiplayerTab).GetField("_currentLobbyIsCompetitive", Private)!.SetValue(tab, true);
            typeof(MultiplayerTab).GetField("_currentLobbyMaxPlayers", Private)!.SetValue(tab, 4);

            var member = Member(solo: 5, team: 2, ratingTeam: 1612, mode: "1v1");
            var row = (FrameworkElement)typeof(MultiplayerTab)
                .GetMethod("BuildMemberRow", Private)!.Invoke(tab, new[] { member })!;
            Assert.Single(TeamBadges(row));

            var line = (string)typeof(MultiplayerTab)
                .GetMethod("MemberDetailLine", Private)!.Invoke(tab, new[] { member })!;
            Assert.Contains("1612", line);
            Assert.Contains(Strings.Get("MpModeTeams"), line);
            Assert.DoesNotContain("1383", line);
        });
        Assert.Null(error);
    }

    // ── helpers ──

    private static FrameworkElement RoomCard(bool competitive, int maxPlayers, int? solo, int? team, string mode)
    {
        var tab = new MultiplayerTab();
        var lobby = new LobbySummary
        {
            Id = "abc123",
            Title = "Sala de prueba",
            ModId = "wol",
            MaxPlayers = maxPlayers,
            Competitive = competitive,
            Status = "open",
            Host = new LobbyHost
            {
                Id = "u1",
                DiscordUsername = "host",
                LadderRank = solo,
                LadderRankTeam = team,
                BadgeMode = mode,
            },
        };
        return (FrameworkElement)typeof(MultiplayerTab)
            .GetMethod("BuildRoomCard", Private)!
            .Invoke(tab, new object[] { lobby, 0 })!;
    }

    private static object Member(int? solo, int? team, double ratingTeam, string mode)
    {
        var type = typeof(MultiplayerTab).GetNestedType("RoomMemberEntry", BindingFlags.NonPublic)!;
        var m = Activator.CreateInstance(type, nonPublic: true)!;
        type.GetProperty("UserId")!.SetValue(m, "u2");
        type.GetProperty("Login")!.SetValue(m, "rival");
        type.GetProperty("Rating")!.SetValue(m, 1383.0);
        type.GetProperty("Rd")!.SetValue(m, 80.0);
        type.GetProperty("LadderRank")!.SetValue(m, solo);
        type.GetProperty("LadderRankTeam")!.SetValue(m, team);
        type.GetProperty("RatingTeam")!.SetValue(m, (double?)ratingTeam);
        type.GetProperty("RdTeam")!.SetValue(m, (double?)80.0);
        type.GetProperty("BadgeMode")!.SetValue(m, mode);
        return m;
    }

    /// <summary>Every single-shield badge: a badge's front carries its age as Tag.</summary>
    private static FrameworkElement[] Badges(DependencyObject root)
        => Walk(root).OfType<FrameworkElement>().Where(e => e.Tag is RankAge).ToArray();

    /// <summary>Every two-shield badge: its root carries a <see cref="RankBadge.TeamBadgeTag"/>.</summary>
    private static FrameworkElement[] TeamBadges(DependencyObject root)
        => Walk(root).OfType<FrameworkElement>().Where(e => e.Tag is RankBadge.TeamBadgeTag).ToArray();

    private static System.Collections.Generic.IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var d in Walk(child)) yield return d;
    }
}
