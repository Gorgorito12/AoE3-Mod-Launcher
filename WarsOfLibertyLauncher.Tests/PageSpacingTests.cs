using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The maintainer's rule for the Multiplayer tab: ONE outer margin M on the four sides of the
/// content area and ONE gap G between panels (across, down and between the community cards), both
/// 12 below 1600 px of width and 16 from there; the sub-bar uses the same M, so "Rooms" starts
/// where the Rooms panel does and "+ Create room" ends where the chat does; and every panel has
/// 16 px of padding on all four sides.
///
/// <para>It replaced a sum of margins that measured 27 / 24 / 8 / 20 around the panels, with
/// gaps of 17 and 13 and "Rooms" 15 px before the panels. The tests MEASURE the laid-out tab,
/// because the failure is exactly a value that is right in one container and added to by
/// another.</para>
/// </summary>
[Collection("wpf-and-language")]
public class PageSpacingTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData(0, 12)]
    [InlineData(1100, 12)]
    [InlineData(1366, 12)]
    [InlineData(1599.5, 12)]
    [InlineData(1600, 16)]
    [InlineData(2560, 16)]
    public void TheMarginIsTwelveBelow1600AndSixteenFromThere(double width, double expected)
        => Assert.Equal(expected, PageSpacing.For(width));

    [Fact]
    public void AWidthNotLaidOutYetIsTheNarrowValue()
    {
        Assert.Equal(PageSpacing.Narrow, PageSpacing.For(double.NaN));
        Assert.Equal(PageSpacing.Narrow, PageSpacing.For(double.PositiveInfinity));
        Assert.Equal(PageSpacing.Narrow, PageSpacing.For(-5));
    }

    /// <summary>
    /// THE ONE THAT MATTERS: on the real Rooms page at 1366 and 2560 px, the four margins, the
    /// two gaps between panels and the gaps between the community cards are ONE value, exactly;
    /// "Rooms" and "+ Create room" line up with the panels' edges; each panel's padding is 16.
    /// </summary>
    [Theory]
    [InlineData(1366, 768, 12)]
    [InlineData(2560, 1300, 16)]
    public void THE_ONE_THAT_MATTERS_TheMarginsAndGapsAreOneValue(double width, double height, double s)
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            // Laid out WITHOUT a window, at exactly the width asked for: a window wider than the
            // screen is never laid out at all (ActualWidth 0), and a window's frame would eat into
            // the width being tested.
            var tab = RoomsTab();
            void Layout()
            {
                tab.Measure(new Size(width, height));
                tab.Arrange(new Rect(0, 0, width, height));
                tab.UpdateLayout();
            }
            {
                for (var i = 0; i < 4; i++) { Layout(); tab.ApplyActivityLayout(); }
                Layout();

                var root = tab.TabRootGrid;
                Assert.Equal(s, tab.PageGap);
                Rect Box(FrameworkElement e) => new(e.TranslatePoint(new Point(0, 0), root), new Size(e.ActualWidth, e.ActualHeight));

                var rooms = Box(tab.RoomsBlock);
                var chat = Box(tab.ChatPanelCard);
                var block = Box(tab.ActivityBlock);
                // The Radmin banner is private to the tab and shown by the machine's Radmin state:
                // the content's top margin is measured from whichever sits right above it.
                var banner = (FrameworkElement)typeof(MultiplayerTab).GetField("RadminBanner", Private)!.GetValue(tab)!;
                var above = banner.Visibility == Visibility.Visible ? Box(banner) : Box(tab.SubBar);
                Assert.Equal(Visibility.Visible, tab.ActivityBlock.Visibility);
                Assert.Equal("Fixed", tab.ActivityMode.ToString());

                // The four margins.
                Assert.Equal(s, rooms.Left, 3);
                Assert.Equal(s, root.ActualWidth - chat.Right, 3);
                Assert.Equal(s, rooms.Top - above.Bottom, 3);
                Assert.Equal(s, chat.Top - above.Bottom, 3);
                Assert.Equal(s, root.ActualHeight - block.Bottom, 3);
                Assert.Equal(s, root.ActualHeight - chat.Bottom, 3);
                // The two gaps between panels, and the ones between the community cards.
                Assert.Equal(s, chat.Left - rooms.Right, 3);
                Assert.Equal(s, block.Top - rooms.Bottom, 3);
                var peak = Box(tab.ActivityPeakCard);
                var recent = Box(tab.ActivityRecentCard);
                var middle = Box(tab.ActivityMiddleCard);
                Assert.Equal(s, recent.Left - peak.Right, 3);
                Assert.Equal(s, middle.Left - recent.Right, 3);
                // The sub-bar on the same margin: "Rooms" over the Rooms panel, "Create room" over the chat.
                Assert.Equal(rooms.Left, Box(tab.SubtabRooms).Left, 3);
                Assert.Equal(chat.Right, Box(tab.CreateRoomButton).Right, 3);
                // Every panel's padding is 16 on all four sides.
                foreach (var panel in new[] { tab.RoomsBlock, tab.ActivityBlock, tab.ChatPanelCard })
                    Assert.Equal(new Thickness(PageSpacing.PanelPadding), panel.Padding);
            }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// The other subtabs take the same margin and gap: Ranking (and the gap beside its match
    /// list), Statistics, and Tournaments (its list pane, the gap, its detail pane).
    /// </summary>
    [Theory]
    [InlineData(1366, 12)]
    [InlineData(2560, 16)]
    public void TheOtherSubtabsUseTheSameMarginAndGap(double width, double s)
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            tab.ShowDemoElo("ranking");
            {
                for (var i = 0; i < 3; i++)
                {
                    tab.Measure(new Size(width, 900));
                    tab.Arrange(new Rect(0, 0, width, 900));
                    tab.UpdateLayout();
                }

                Assert.Equal(s, tab.PageGap);
                Assert.Equal(new Thickness(s, 0, s, 0), tab.SubBar.Padding);
                Assert.Equal(new Thickness(s), tab.RankingPage.Margin);
                Assert.Equal(s, tab.RankingPage.TranslatePoint(new Point(0, 0), tab.TabRootGrid).X, 3);
                if (tab.RankingHistoryCard.Visibility == Visibility.Visible)
                    Assert.Equal(s, Math.Max(tab.RankingHistoryCard.Margin.Left, tab.RankingHistoryCard.Margin.Top));
                Assert.Equal(new Thickness(s), tab.StatsPage.Margin);
                Assert.Equal(new Thickness(s, s, 0, s), tab.TournamentsListPane.Margin);
                Assert.Equal(s, tab.TournamentsGapColumn.ActualWidth > 0 ? tab.TournamentsGapColumn.ActualWidth : tab.TournamentsGapColumn.Width.Value);
                Assert.Equal(new Thickness(0, s, s, s), tab.TournamentsDetailPane.Margin);
            }
        });
        Assert.Null(error);
    }

    /// <summary>The Rooms page with community data and three rooms, as the snapshot harness builds it.</summary>
    private static MultiplayerTab RoomsTab()
    {
        var tab = new MultiplayerTab();
        typeof(MultiplayerTab).GetField("_config", Private)!.SetValue(tab, new LauncherConfig());
        tab.ShowDemoElo("highlights");
        var stats = (CommunityStats)typeof(MultiplayerTab).GetField("_communityStats", Private)!.GetValue(tab)!;
        stats.RecentMatches = Enumerable.Range(0, 6).Select(i => new CommunityMatch
        {
            Id = "m" + i, ModId = "wol", MapName = "ESOC_Florida", DurationSeconds = 1500, Competitive = true,
            ReportedAt = DateTime.UtcNow.AddHours(-(i + 1)).ToString("o"),
            Participants = new List<MatchHistoryParticipant>
            {
                new() { UserId = "a", DisplayName = "Kaiser", Result = 1 },
                new() { UserId = "b", DisplayName = "El Taita", Result = 0 },
            },
        }).ToList();
        typeof(MultiplayerTab).GetMethod("RenderActivityStrip", Private)!.Invoke(tab, null);
        var rooms = Enumerable.Range(0, 3).Select(i => new LobbySummary
        {
            Id = "ROOM000" + i, Title = "Sala " + i, ModId = "wol", Status = "open",
            MaxPlayers = 2, CurrentPlayers = 1, Competitive = true,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5).ToString("o"),
            Host = new LobbyHost { Id = "h" + i, DisplayName = "Host" + i, DiscordUsername = "host" + i },
        }).ToList();
        typeof(MultiplayerTab).GetField("_lastBrowserList", Private)!.SetValue(tab, rooms);
        typeof(MultiplayerTab).GetMethod("RenderRoomRows", Private)!.Invoke(tab, new object?[] { rooms });
        return tab;
    }
}
