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
/// <para>The rooms LIST no longer wears one (design 86, the maintainer's call: the host's name and
/// rating, as the prototype draws them); the badge lives in the room, the profile and the ranking.
/// The room still decides which one shows there.</para>
/// </summary>
[Collection("wpf-and-language")]
public class RankBadgeContextTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>Design 86: whatever the host's places, the rooms row draws no badge.</summary>
    [Fact]
    public void TheRoomsRowWearsNoBadgeAnyMore()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            foreach (var (competitive, max, mode) in new[] { (true, 4, "1v1"), (true, 2, "team"), (false, 8, "team") })
            {
                var card = RoomCard(competitive, max, solo: 5, team: 2, mode);
                Assert.Empty(TeamBadges(card));
                Assert.Empty(Badges(card));
            }
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
