using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The rank guide as the player meets it (46a/46b): a layer over the tab, built from the loaded
/// ladder, that every way out actually closes. The card is built in code, so a brush or string
/// key that does not exist throws only when somebody opens it — which is why it is built here.
/// </summary>
[Collection("wpf-and-language")]
public class RankGuideOverlayTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData(Strings.LangEn)]
    [InlineData(Strings.LangEs)]
    public void TheGuideOpensOverTheTabWithEveryAge(string language)
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var before = Strings.Language;
            Strings.SetLanguage(language);
            try
            {
                var tab = TabWithLadder();
                tab.ShowRankGuide();
                var overlay = Overlay(tab);
                Assert.NotNull(overlay);

                var all = Walk(overlay!).OfType<FrameworkElement>().ToList();
                var ages = all.Where(e => e.Tag is RankAge && e is Grid { MinHeight: > 0 }).Select(e => (RankAge)e.Tag).ToList();
                Assert.Equal(6, ages.Count);
                foreach (var text in all.OfType<TextBlock>().Select(t => t.Text))
                    Assert.DoesNotContain("MpGuide", text);   // a missing key renders as itself
                Assert.Contains(all, e => Equals(e.Tag, "RankGuideOpenRanking"));
            }
            finally { Strings.SetLanguage(before); }
        });
        Assert.Null(error);
    }

    [Fact]
    public void TheCloseButtonTheScrimAndEscapeAllClose()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = TabWithLadder();

            tab.ShowRankGuide();
            var x = (Button)Walk(Overlay(tab)!).OfType<FrameworkElement>().Single(e => Equals(e.Tag, "RankGuideClose"));
            x.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Null(Overlay(tab));

            tab.ShowRankGuide();
            var scrim = tab.TabRootGrid.Children[tab.TabRootGrid.Children.Count - 3];
            scrim.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = UIElement.MouseLeftButtonDownEvent,
            });
            Assert.Null(Overlay(tab));

            tab.ShowRankGuide();
            var outer = Overlay(tab)!;
            var source = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("t"));
            try
            {
                outer.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Escape)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent,
                });
            }
            finally { source.Dispose(); }
            Assert.Null(Overlay(tab));

            // One guide at a time: opening twice leaves exactly one layer.
            tab.ShowRankGuide();
            tab.ShowRankGuide();
            Assert.Single(tab.TabRootGrid.Children.OfType<FrameworkElement>(), e => Equals(e.Tag, "MpContentOverlay"));
        });
        Assert.Null(error);
    }

    /// <summary>
    /// 53b rule 1: the guide opens on the tab of the badge that was CLICKED — a double shield on
    /// Teams, a single one on 1v1 — through the real badge and its real click, since the call
    /// site handing the guide the wrong kind is the failure a player would see.
    /// </summary>
    [Fact]
    public void TheGuideOpensOnTheTabOfTheBadgeThatWasClicked()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = TabWithTeamLadder();

            Click(BuildShownBadge(tab, BadgeKind.Team));
            var team = Walk(Overlay(tab)!).OfType<FrameworkElement>().ToList();
            Assert.Equal("active", Segment(team, "RankGuideTabTeam").Tag);
            Assert.Contains(team, e => e.Tag is RankBadge.TeamBadgeTag);

            Click(BuildShownBadge(tab, BadgeKind.Solo));
            var solo = Walk(Overlay(tab)!).OfType<FrameworkElement>().ToList();
            Assert.Equal("active", Segment(solo, "RankGuideTab1v1").Tag);
            Assert.DoesNotContain(solo, e => e.Tag is RankBadge.TeamBadgeTag);
        });
        Assert.Null(error);
    }

    /// <summary>"Open team ranking" lands on the Clasificación's TEAMS tab, and the 1v1 tab's
    /// button on its 1v1 one — the guide never leaves the Ranking on the other ladder.</summary>
    [Fact]
    public void OpenTeamRankingLeavesTheRankingOnTeams()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = TabWithTeamLadder();

            tab.ShowRankGuide(initial: BadgeKind.Team);
            Open(tab).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Null(Overlay(tab));
            Assert.Equal(MultiplayerTab.Subtab.Ranking, Field(tab, "_activeSubtab"));
            Assert.Equal("Team", Field(tab, "_rankingMode")!.ToString());

            tab.ShowRankGuide();
            Open(tab).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal("Solo", Field(tab, "_rankingMode")!.ToString());
        });
        Assert.Null(error);
    }

    // ── helpers ──

    private static MultiplayerTab TabWithLadder()
    {
        var tab = new MultiplayerTab();
        typeof(MultiplayerTab).GetField("_communityStats", Private)!.SetValue(tab, StatsDemoData.Community());
        return tab;
    }

    /// <summary>The demo ladder plus a team table, and a standing that places the viewer 5th of 9
    /// on it — the shape the deployed backend will send once it reports team places.</summary>
    private static MultiplayerTab TabWithTeamLadder()
    {
        var tab = new MultiplayerTab();
        var stats = StatsDemoData.Community();
        stats.LeaderboardTeam = Enumerable.Range(1, 9)
            .Select(i => new Models.Multiplayer.LeaderboardRow { Rank = i, UserId = "t" + i, DisplayName = "Team" + i })
            .ToList();
        stats.RankedPlayersTeam = 9;
        typeof(MultiplayerTab).GetField("_communityStats", Private)!.SetValue(tab, stats);
        typeof(MultiplayerTab).GetField("_cachedStanding", Private)!.SetValue(tab, new Models.Multiplayer.EloSnapshot
        {
            Rating = 1388, LadderRank = 4, LadderSize = stats.RankedPlayers,
            LadderRankTeam = 5, LadderSizeTeam = 9, RatingTeam = 1455, RdTeam = 120, GamesPlayedTeam = 6,
        });
        return tab;
    }

    private static FrameworkElement BuildShownBadge(MultiplayerTab tab, BadgeKind kind)
        => (FrameworkElement)typeof(MultiplayerTab).GetMethod("BuildShownBadge", Private)!.Invoke(tab, new object?[]
        {
            new ShownBadge(kind, RankAges.For(5, 9), 5, null, 0), 24.0, "seed", false,
        })!;

    private static void Click(FrameworkElement badge)
        => badge.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonUpEvent,
        });

    private static Button Segment(IEnumerable<FrameworkElement> all, string name)
        => (Button)all.Single(e => e.Name == name);

    private static Button Open(MultiplayerTab tab)
        => (Button)Walk(Overlay(tab)!).OfType<FrameworkElement>().Single(e => Equals(e.Tag, "RankGuideOpenRanking"));

    private static object? Field(MultiplayerTab tab, string name)
        => typeof(MultiplayerTab).GetField(name, Private)!.GetValue(tab);

    private static FrameworkElement? Overlay(MultiplayerTab tab)
        => tab.TabRootGrid.Children.OfType<FrameworkElement>().SingleOrDefault(e => Equals(e.Tag, "MpContentOverlay"));

    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var d in Walk(child)) yield return d;
    }
}
