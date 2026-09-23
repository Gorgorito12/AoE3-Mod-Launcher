using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The rank badge outside the Ranking table (docs/design_insignias_rango, 45b + 45c): the rooms
/// row puts it in the host's avatar slot, the room's roster puts it beside each avatar. The
/// refusal is the point in both: a <c>ladder_rank</c> the server did not send must draw NO
/// badge, never a Discovery one — that would tell a player on the ladder that they are not.
/// </summary>
public class RoomBadgesTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void THE_ONE_THAT_MATTERS_AnUnknownRankKeepsTheHostsAvatar()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var card = RoomCard(ladderRank: null);
            Assert.Empty(Badges(card));
        });
        Assert.Null(error);
    }

    [Fact]
    public void AKnownRankReplacesTheHostsAvatarWithItsAge()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var badge = Assert.Single(Badges(RoomCard(ladderRank: 3)));
            Assert.Equal(RankAges.For(3), badge.Tag);
            Assert.Equal(MultiplayerTab.RoomRowBadgeWidth, badge.Width);

            var discovery = Assert.Single(Badges(RoomCard(ladderRank: 0)));
            Assert.Equal(RankAge.Discovery, discovery.Tag);
        });
        Assert.Null(error);
    }

    [Fact]
    public void TheRosterRowWearsTheBadgeOnlyWhenTheServerSaidWhere()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            Assert.Empty(Badges(MemberRow(tab, ladderRank: null)));

            var badge = Assert.Single(Badges(MemberRow(tab, ladderRank: 7)));
            Assert.Equal(RankAge.Colonial, badge.Tag);
            Assert.Equal(MultiplayerTab.RosterBadgeWidth, badge.Width);
        });
        Assert.Null(error);
    }

    [Fact]
    public void TheDetailLineNamesTheAgeOnlyWhenItIsKnown()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            var colonial = Strings.Get(RankAges.NameKey(RankAge.Colonial));
            Assert.EndsWith(" · " + colonial, DetailLine(tab, ladderRank: 7));
            Assert.DoesNotContain(colonial, DetailLine(tab, ladderRank: null));
        });
        Assert.Null(error);
    }

    /// <summary>
    /// 45d, the global players panel. A badge only when the server sent the place — 0 draws
    /// Discovery, absent draws nothing — and a row carrying one is no taller than a row without.
    /// </summary>
    [Fact]
    public void ThePlayersPanelBadgeFollowsTheServerAndDoesNotGrowTheRow()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            var users = (System.Collections.IList)typeof(MultiplayerTab)
                .GetField("_globalOnlineUsers", Private)!.GetValue(tab)!;
            users.Add(("u1", "ranked", (string?)null, "idle", (double?)1500.0, (double?)80.0, (int?)3));
            users.Add(("u2", "newcomer", (string?)null, "idle", (double?)1500.0, (double?)350.0, (int?)0));
            users.Add(("u3", "unknown", (string?)null, "idle", (double?)1500.0, (double?)80.0, (int?)null));
            typeof(MultiplayerTab).GetMethod("RenderPlayersPanel", Private)!.Invoke(tab, null);

            var panel = tab.PlayersPanel;
            panel.Measure(new Size(260, double.PositiveInfinity));
            panel.Arrange(new Rect(0, 0, 260, panel.DesiredSize.Height));

            var rows = panel.Children.OfType<Grid>().ToList();
            FrameworkElement RowOf(string login) => rows.Single(r => Walk(r).OfType<TextBlock>().Any(t => t.Text == login));
            Assert.Equal(RankAges.For(3), Assert.Single(Badges(RowOf("ranked"))).Tag);
            Assert.Equal(RankAge.Discovery, Assert.Single(Badges(RowOf("newcomer"))).Tag);
            Assert.Empty(Badges(RowOf("unknown")));
            Assert.Equal(RowOf("unknown").ActualHeight, RowOf("ranked").ActualHeight, 1);
        });
        Assert.Null(error);
    }

    // ── helpers ──

    private static FrameworkElement RoomCard(int? ladderRank)
    {
        var tab = new MultiplayerTab();
        var lobby = new LobbySummary
        {
            Id = "abc123",
            Title = "Sala de prueba",
            ModId = "wol",
            MaxPlayers = 2,
            Status = "open",
            Host = new LobbyHost { Id = "u1", DiscordUsername = "host", LadderRank = ladderRank },
        };
        return (FrameworkElement)typeof(MultiplayerTab)
            .GetMethod("BuildRoomCard", Private)!
            .Invoke(tab, new object[] { lobby, 0 })!;
    }

    private static object Member(int? ladderRank)
    {
        var type = typeof(MultiplayerTab).GetNestedType("RoomMemberEntry", BindingFlags.NonPublic)!;
        var m = Activator.CreateInstance(type, nonPublic: true)!;
        type.GetProperty("UserId")!.SetValue(m, "u2");
        type.GetProperty("Login")!.SetValue(m, "rival");
        type.GetProperty("Rating")!.SetValue(m, 1383.0);
        type.GetProperty("Rd")!.SetValue(m, 80.0);
        type.GetProperty("LadderRank")!.SetValue(m, ladderRank);
        return m;
    }

    private static FrameworkElement MemberRow(MultiplayerTab tab, int? ladderRank)
        => (FrameworkElement)typeof(MultiplayerTab)
            .GetMethod("BuildMemberRow", Private)!
            .Invoke(tab, new[] { Member(ladderRank) })!;

    private static string DetailLine(MultiplayerTab tab, int? ladderRank)
        => (string)typeof(MultiplayerTab)
            .GetMethod("MemberDetailLine", Private)!
            .Invoke(tab, new[] { Member(ladderRank) })!;

    /// <summary>Every badge in the element's logical tree — a badge's root carries its age as Tag.</summary>
    private static FrameworkElement[] Badges(DependencyObject root)
        => Walk(root).OfType<FrameworkElement>().Where(e => e.Tag is RankAge).ToArray();

    private static System.Collections.Generic.IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var d in Walk(child)) yield return d;
    }
}
