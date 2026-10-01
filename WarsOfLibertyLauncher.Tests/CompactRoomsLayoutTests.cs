using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The Rooms page's compact layout and its room-code search (design handoff turn 36,
/// <c>docs/design_handoff_salas_laptop</c>, variant 36a).
///
/// <para>Most of what these pin is invisible when broken: a strip that stays inside the
/// scrolling page still renders, a poll that drops the search filter still shows rooms, a
/// join row that never appears is simply absent. Each test names the failure it exists for.</para>
/// </summary>
[Collection("wpf-and-language")]
public class CompactRoomsLayoutTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static IEnumerable<DependencyObject> Ancestors(DependencyObject d)
    {
        for (var p = LogicalTreeHelper.GetParent(d); p != null; p = LogicalTreeHelper.GetParent(p))
            yield return p;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }

    private static object? Call(MultiplayerTab tab, string method, params object?[] args)
        => typeof(MultiplayerTab).GetMethod(method, Private)!.Invoke(tab, args);

    private static void SetField(MultiplayerTab tab, string field, object? value)
        => typeof(MultiplayerTab).GetField(field, Private)!.SetValue(tab, value);

    /// <summary>
    /// THE ONE THAT MATTERS for the layout: in the compact layout the room list keeps its single
    /// scroller and the community block leaves it — and going back to wide restores exactly the
    /// page the existing wide tests pin.
    /// </summary>
    [Fact]
    public void TheCompactLayoutLiftsTheStripOutOfThePageAndTheWideOnePutsItBack()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            Assert.False(tab.IsCompactLayout);   // a tab built on its own is the wide layout

            tab.SetCompactLayout(true);
            Assert.True(tab.IsCompactLayout);

            // The list and its header still scroll with ONE viewport, and it is the page's.
            Assert.Same(tab.RoomsPageScroll, Ancestors(tab.RoomsListPanel).OfType<ScrollViewer>().Single());
            Assert.Same(tab.RoomsPageScroll, Ancestors(tab.RoomsHeaderStrip).OfType<ScrollViewer>().Single());

            // The full block moved into the overlay, out of every scroller.
            Assert.Same(tab.ActivityOverlayHost, LogicalTreeHelper.GetParent(tab.ActivityStrip));
            Assert.Empty(Ancestors(tab.ActivityStrip).OfType<ScrollViewer>());

            // The handoff's geometry: a fixed 300-px side panel, a 46-px sub-bar, and the column
            // header inset exactly over the rows' content (the row's 1 of border + 12 of padding).
            Assert.Equal(300, tab.RoomsSideColumn.Width.Value);
            Assert.True(tab.RoomsSideColumn.Width.IsAbsolute);
            Assert.Equal(46, tab.SubBar.Height);
            Assert.Equal(tab.RoomsListPanel.Margin.Left + 13, tab.RoomsHeaderStrip.Margin.Left);
            Assert.Equal(44, tab.ActivityBar.MinHeight);

            tab.SetCompactLayout(false);
            Assert.Same(tab.ActivityInlineHost, LogicalTreeHelper.GetParent(tab.ActivityStrip));
            Assert.Same(tab.RoomsPageScroll, Ancestors(tab.ActivityStrip).OfType<ScrollViewer>().Single());
            Assert.Equal(16, tab.RoomsListPanel.Margin.Left);
            Assert.Equal(270, tab.RoomsSideColumn.MaxWidth);
            Assert.Equal(48, tab.SubBar.Height);
            Assert.Equal(Visibility.Collapsed, tab.ActivityBar.Visibility);
        });
        Assert.Null(error);
    }

    /// <summary>
    /// The 44-px strip stays ONE line in Spanish with every segment present — a long match line
    /// must take the ellipsis, never a second line, or the strip grows into the list it exists
    /// to make room for.
    /// </summary>
    [Fact]
    public void TheActivityBarIsOneLineInSpanishWithEverySegment()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            try
            {
                Strings.SetLanguage("es");
                var tab = new MultiplayerTab();
                tab.SetCompactLayout(true);
                SetField(tab, "_communityStats", Stats());

                tab.ActivityStrip.Visibility = Visibility.Visible;
                Call(tab, "FillActivityBar");
                Call(tab, "PlaceActivityStrip");

                Assert.Equal(Visibility.Visible, tab.ActivityBar.Visibility);
                // Collapsed until the player asks for it: the list gets the height by default.
                Assert.Equal(Visibility.Collapsed, tab.ActivityOverlay.Visibility);
                // Peak, matches, the match line and #1, with three separators between them.
                Assert.Equal(7, tab.ActivityBarSegments.Children.Count);

                tab.ActivityBar.Measure(new Size(760, double.PositiveInfinity));
                Assert.True(tab.ActivityBar.DesiredSize.Height <= 44 + 10,
                    $"the activity strip measures {tab.ActivityBar.DesiredSize.Height:F0} px tall "
                    + "(44 + its 10 of margin allowed): something in it wrapped.");
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    [Fact]
    public void ACodeInTheSearchShowsAJoinRowEvenWithNoRooms()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();

            // Nothing fetched at all — the evening nobody else has a room open, or a private
            // room, which is never in the list. The row must appear anyway.
            tab.RoomSearchBox.Text = "sjmd9j6w";
            var row = Assert.IsType<Border>(tab.RoomsListPanel.Children[0]);
            Assert.Equal("JoinCodeRow", row.Tag);
            Assert.Contains(Descendants(row).OfType<TextBlock>(),
                t => t.Inlines.OfType<System.Windows.Documents.Run>().Any(r => r.Text == "SJMD9J6W"));

            // A listed room whose id IS the code is already on screen with its own Join: one door.
            SetField(tab, "_lastBrowserList", new List<LobbySummary> { Room("SJMD9J6W", competitive: true, max: 2, current: 1) });
            Call(tab, "RerenderRoomsFromCache");
            Assert.DoesNotContain(tab.RoomsListPanel.Children.OfType<Border>(), b => Equals(b.Tag, "JoinCodeRow"));
            Assert.Single(tab.RoomsListPanel.Children.OfType<Border>());

            // An ordinary search never grows one.
            tab.RoomSearchBox.Text = "kaiser";
            Assert.DoesNotContain(tab.RoomsListPanel.Children.OfType<Border>(), b => Equals(b.Tag, "JoinCodeRow"));
        });
        Assert.Null(error);
    }

    [Fact]
    public void ACasualRoomSaysSoAndDrawsOneBarPerSeat()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();

            var casual = BuildCard(tab, Room("CASUAL01", competitive: false, max: 6, current: 4));
            Assert.Contains(Descendants(casual).OfType<TextBlock>(), t => t.Text == Strings.Get("MpRoomCasualBadge"));
            var bars = Descendants(casual).OfType<StackPanel>().Single(p => Equals(p.Tag, "capacity"));
            Assert.Equal(6, bars.Children.Count);

            var ranked = BuildCard(tab, Room("RANKED01", competitive: true, max: 2, current: 1));
            Assert.DoesNotContain(Descendants(ranked).OfType<TextBlock>(), t => t.Text == Strings.Get("MpRoomCasualBadge"));
            Assert.Equal(2, Descendants(ranked).OfType<StackPanel>().Single(p => Equals(p.Tag, "capacity")).Children.Count);
        });
        Assert.Null(error);
    }

    [Fact]
    public void ACompactRowIsTheShortStyleWithAOneLineName()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            tab.SetCompactLayout(true);
            var card = BuildCard(tab, Room("COMPACT1", competitive: false, max: 4, current: 2,
                title: "A room name long enough that it would have wrapped to a second line"));

            Assert.Same(tab.FindResource("MpRoomCardCompact"), card.Style);
            var title = Descendants(card).OfType<TextBlock>()
                .First(t => t.Text.StartsWith("A room name", StringComparison.Ordinal));
            Assert.Equal(TextWrapping.NoWrap, title.TextWrapping);
            Assert.Equal(TextTrimming.CharacterEllipsis, title.TextTrimming);
        });
        Assert.Null(error);
    }

    /// <summary>
    /// The poll used to render rows WITHOUT the search filter, so whatever somebody had typed
    /// was thrown away the moment any room changed — and with a room code now typed into that
    /// same box, the join row would vanish under the cursor. One renderer, or it comes back.
    /// </summary>
    [Fact]
    public void ThePollGoesThroughTheSameRendererAsTheSearch()
    {
        var source = File.ReadAllText(RepoFile("Controls/MultiplayerTab.xaml.cs"));
        var start = source.IndexOf("private async Task RefreshRoomsListAsync(", StringComparison.Ordinal);
        Assert.True(start > 0);
        var end = source.IndexOf("private static string BuildRoomsSignature(", start, StringComparison.Ordinal);
        var body = source.Substring(start, end - start);

        Assert.Contains("RenderRoomRows(", body);
        Assert.DoesNotContain("BuildRoomCard(", body);
    }

    // ── fixtures ──

    private static LobbySummary Room(string id, bool competitive, int max, int current, string? title = null) => new()
    {
        Id = id,
        Title = title ?? "Sala " + id,
        ModId = "wol",
        MaxPlayers = max,
        CurrentPlayers = current,
        Competitive = competitive,
        Status = "open",
        Host = new LobbyHost { Id = "h-" + id, DiscordUsername = "host" },
    };

    private static Border BuildCard(MultiplayerTab tab, LobbySummary lobby)
        => (Border)typeof(MultiplayerTab).GetMethod("BuildRoomCard", Private)!
            .Invoke(tab, new object[] { lobby, 0 })!;

    private static CommunityStats Stats()
    {
        var hours = new List<ActivityHour>();
        for (var h = 0; h < 24; h++) hours.Add(new ActivityHour { Hour = h, Count = 2 + (h % 7) });
        return new CommunityStats
        {
            Activity = new ActivityBuckets { WindowDays = 30, Total = hours.Sum(x => x.Count), Hours = hours },
            Totals = new CommunityTotals { Matches = 111, WindowDays = 30 },
            Leaderboard = new List<LeaderboardRow>
            {
                new() { Rank = 1, UserId = "u1", DisplayName = "Aluclown", Rating = 1605, Rd = 80 },
            },
            RecentMatches = new List<CommunityMatch>
            {
                new()
                {
                    Id = "m1",
                    ModId = "wol",
                    ReportedAt = DateTime.UtcNow.AddMinutes(-36).ToString("o"),
                    Participants = new List<MatchHistoryParticipant>
                    {
                        new() { UserId = "a", DisplayName = "Kaiser con un nombre larguísimo de verdad", Result = 1, Team = 0 },
                        new() { UserId = "b", DisplayName = "El Taita también con un nombre muy largo", Result = 0, Team = 1 },
                    },
                },
            },
        };
    }

    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var project = Path.Combine(dir.FullName, "WarsOfLibertyLauncher");
            if (File.Exists(Path.Combine(project, "App.xaml")))
                return Path.GetFullPath(Path.Combine(project, relative));
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("WarsOfLibertyLauncher/App.xaml not found above the test output.");
    }
}
