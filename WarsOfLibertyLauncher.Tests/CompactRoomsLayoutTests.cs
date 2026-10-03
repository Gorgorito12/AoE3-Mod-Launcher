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
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The Rooms page layout (the compact switch, the list/panel split) and its room-code search
/// (design handoff turns 36 and 38-39, <c>docs/design_handoff_salas_laptop</c>).
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
    /// THE ONE THAT MATTERS for the compact switch (design handoff turns 38-39): it changes
    /// GEOMETRY and nothing else. The room list keeps its own scroller, the community panel keeps
    /// its own row under it in both layouts — never lifted into an overlay, which is what turn 36
    /// did — and going back to wide restores exactly what the XAML says.
    /// </summary>
    [Fact]
    public void TheCompactLayoutKeepsEveryBlockAndOnlyChangesGeometry()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            Assert.False(tab.IsCompactLayout);   // a tab built on its own is the wide layout

            foreach (var compact in new[] { true, false, true })
            {
                tab.SetCompactLayout(compact);
                Assert.Equal(compact, tab.IsCompactLayout);

                // The rows scroll inside their own viewport, and nothing else does.
                Assert.Same(tab.RoomsListScroll, Ancestors(tab.RoomsListPanel).OfType<ScrollViewer>().Single());
                Assert.Empty(Ancestors(tab.RoomsHeaderStrip).OfType<ScrollViewer>());
                Assert.Empty(Ancestors(tab.ActivityStrip).OfType<ScrollViewer>());
                // The panel is in its own row of the column, under the rooms.
                Assert.Same(tab.ActivityHost, LogicalTreeHelper.GetParent(tab.ActivityStrip));
                Assert.Same(tab.ActivityHost, LogicalTreeHelper.GetParent(tab.ActivityBar));
                Assert.Equal(2, Grid.GetRow(tab.ActivityHost));
                Assert.Equal(1, Grid.GetRow(tab.HighlightsHost));
                Assert.Equal(0, Grid.GetRow(tab.RoomsBlock));
            }

            // The handoff's compact geometry: a fixed 300-px side panel, a 44-px sub-bar, and the
            // column header inset exactly over the rows' content (the row's 1 of border + 12).
            Assert.Equal(300, tab.RoomsSideColumn.Width.Value);
            Assert.True(tab.RoomsSideColumn.Width.IsAbsolute);
            Assert.Equal(44, tab.SubBar.Height);
            Assert.Equal(tab.RoomsListPanel.Margin.Left + 13, tab.RoomsHeaderStrip.Margin.Left);
            Assert.Equal(44, tab.ActivityBar.Height);

            tab.SetCompactLayout(false);
            Assert.Equal(16, tab.RoomsListPanel.Margin.Left);
            Assert.Equal(270, tab.RoomsSideColumn.MaxWidth);
            Assert.Equal(48, tab.SubBar.Height);
        });
        Assert.Null(error);
    }

    /// <summary>
    /// The split the handoff draws as 40a, 40b and 39a, measured on the real column: with zero,
    /// one or eight rooms the open panel is the same 248 px (Fixed) — with few rooms the spare
    /// height stays in the LIST, with eight the list scrolls — and folding gives the list all
    /// but the 44-px strip (Folded). In none of them does the panel reach up into the rooms.
    /// </summary>
    [Theory]
    [InlineData(0, null, "Fixed")]
    [InlineData(1, null, "Fixed")]
    [InlineData(8, null, "Fixed")]
    [InlineData(8, false, "Folded")]
    [InlineData(1, false, "Folded")]
    public void TheColumnSplitsByContentAndThePanelNeverCoversTheRooms(int rooms, bool? choice, string expected)
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            tab.SetCompactLayout(true);
            SetField(tab, "_config", new WarsOfLibertyLauncher.Models.LauncherConfig { RoomsActivityChoice = choice });
            SetField(tab, "_communityStats", Stats());
            Call(tab, "RenderActivityStrip");

            tab.RoomsListPanel.Children.Clear();
            for (var i = 0; i < rooms; i++)
                tab.RoomsListPanel.Children.Add(new Border { Height = 54, Margin = new Thickness(0, 0, 0, 6) });

            // Laid out DIRECTLY: on a bare tab nobody is signed in, so the sign-in gate would
            // collapse everything above this column. 716 is the column a 1380x860 window gives.
            void Layout()
            {
                tab.RoomsLeftColumn.Measure(new Size(1040, 716));
                tab.RoomsLeftColumn.Arrange(new Rect(0, 0, 1040, 716));
                tab.RoomsLeftColumn.UpdateLayout();
            }
            Layout();
            tab.ApplyActivityLayout();
            Layout();
            tab.ApplyActivityLayout();
            Layout();

            Assert.Equal(expected, tab.ActivityMode.ToString());

            var roomsBottom = tab.RoomsBlock.TranslatePoint(new Point(0, tab.RoomsBlock.ActualHeight), tab.RoomsLeftColumn).Y;
            var panelTop = tab.ActivityHost.TranslatePoint(new Point(0, 0), tab.RoomsLeftColumn).Y;
            Assert.True(panelTop >= roomsBottom - 0.5,
                $"the community panel starts at {panelTop:0} but the rooms end at {roomsBottom:0}: it covers them");

            switch (expected)
            {
                case "Fixed":
                    // 248 whatever the room count (turn 40), and the rooms keep the star row:
                    // everything above the panel and its 14-px gap is theirs, used or not.
                    Assert.Equal(248, tab.ActivityStrip.ActualHeight, 1);
                    Assert.True(tab.RoomsRow.Height.IsStar, "the rooms must keep the star row");
                    Assert.Equal(716 - 248 - RoomsActivityLayout.Gap, tab.RoomsBlock.ActualHeight, 1);
                    if (rooms >= 8)
                        Assert.True(tab.RoomsListScroll.ScrollableHeight > 0, "eight rooms should scroll inside the list");
                    else
                        Assert.Equal(0, tab.RoomsListScroll.ScrollableHeight);
                    break;
                case "Folded":
                    Assert.Equal(Visibility.Collapsed, tab.ActivityStrip.Visibility);
                    Assert.Equal(Visibility.Visible, tab.ActivityBar.Visibility);
                    Assert.Equal(44, tab.ActivityBar.ActualHeight, 1);
                    break;
            }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// 38b's own claim, as a number: at 248 px the panel holds FOUR whole matches and five
    /// ranking rows — and since turn 40 those are also the CAPS, so with eight of each on offer
    /// the cards are built from exactly four and five, and every one of them fits whole. It held three until the labels were given the mockup's line heights — WPF's
    /// default line box is a few pixels taller than CSS's <c>line-height: 1</c>, and four rows of
    /// that is the fourth match. A label put back on the default line box fails this, not a
    /// screenshot.
    /// </summary>
    [Fact]
    public void ThePanelAt248HoldsFourWholeMatchesAndFiveRanks()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            tab.SetCompactLayout(true);
            SetField(tab, "_config", new WarsOfLibertyLauncher.Models.LauncherConfig());
            var stats = Stats();
            stats.Leaderboard = Enumerable.Range(1, 8).Select(i => new LeaderboardRow
            {
                Rank = i, UserId = "u" + i, DisplayName = "Player" + i, Rating = 1700 - i * 20, Rd = 90,
            }).ToList();
            stats.RecentMatches = Enumerable.Range(0, 8).Select(i => new CommunityMatch
            {
                Id = "m" + i,
                ModId = "wol",
                MapName = "ESOC_Fertile Crescent",
                DurationSeconds = 1500,
                Competitive = i % 2 == 0,
                ReportedAt = DateTime.UtcNow.AddMinutes(-(36 + i * 40)).ToString("o"),
                Participants = new List<MatchHistoryParticipant>
                {
                    new() { UserId = "a", DisplayName = "Kaiser", Result = 1 },
                    new() { UserId = "b", DisplayName = "El Taita", Result = 0 },
                },
            }).ToList();
            SetField(tab, "_communityStats", stats);
            Call(tab, "RenderActivityStrip");

            tab.RoomsListPanel.Children.Clear();
            for (var i = 0; i < 8; i++)
                tab.RoomsListPanel.Children.Add(new Border { Height = 54, Margin = new Thickness(0, 0, 0, 6) });

            void Layout()
            {
                tab.RoomsLeftColumn.Measure(new Size(1040, 716));
                tab.RoomsLeftColumn.Arrange(new Rect(0, 0, 1040, 716));
                tab.RoomsLeftColumn.UpdateLayout();
            }
            for (var pass = 0; pass < 3; pass++) { Layout(); tab.ApplyActivityLayout(); }
            Layout();

            Assert.Equal("Fixed", tab.ActivityMode.ToString());
            Assert.Equal(4, tab.ActivityRecentList.Children.Count);
            Assert.Equal(5, tab.ActivityRankingList.Children.Count);
            Assert.Equal(4, tab.ActivityRecentList.VisibleCount);
            Assert.Equal(5, tab.ActivityRankingList.VisibleCount);
        });
        Assert.Null(error);
    }

    /// <summary>
    /// Turn 40: the PEAK HOURS sentences WRAP instead of trimming, and stop at two lines. They
    /// used to end in "…" in the 0.8* card, which on the peak line meant losing the hours — the
    /// answer. Laid out narrow, in Spanish (the wide language), each says everything it has in
    /// at most two lines, and neither has an ellipsis to fall back on.
    /// </summary>
    [Fact]
    public void ThePeakSentencesWrapToTwoLinesAndNeverTrim()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            try
            {
                Strings.SetLanguage("es");
                var tab = new MultiplayerTab();
                tab.SetCompactLayout(true);
                SetField(tab, "_config", new WarsOfLibertyLauncher.Models.LauncherConfig { RoomsActivityChoice = true });
                SetField(tab, "_communityStats", Stats());
                Call(tab, "RenderActivityStrip");

                void Layout()
                {
                    tab.RoomsLeftColumn.Measure(new Size(760, 716));
                    tab.RoomsLeftColumn.Arrange(new Rect(0, 0, 760, 716));
                    tab.RoomsLeftColumn.UpdateLayout();
                }
                for (var pass = 0; pass < 3; pass++) { Layout(); tab.ApplyActivityLayout(); }
                Layout();

                foreach (var line in new[] { tab.ActivityPeakLine, tab.ActivityPeakSubtitle })
                {
                    Assert.Equal(TextWrapping.Wrap, line.TextWrapping);
                    Assert.Equal(TextTrimming.None, line.TextTrimming);
                    var lineHeight = TextBlock.GetLineHeight(line);
                    Assert.True(lineHeight > 0, "the line height is what the two-line cap is made of");
                    Assert.Equal(2 * lineHeight, line.MaxHeight, 1);

                    // Unconstrained in height at the width it was given: two lines hold it all.
                    var width = line.ActualWidth;
                    Assert.True(width > 0);
                    var cap = line.MaxHeight;
                    line.MaxHeight = double.PositiveInfinity;
                    line.Measure(new Size(width + line.Margin.Left + line.Margin.Right, double.PositiveInfinity));
                    // DesiredSize includes the margin; the text's own height is what is capped.
                    var textHeight = line.DesiredSize.Height - line.Margin.Top - line.Margin.Bottom;
                    Assert.True(textHeight <= cap + 0.5,
                        $"'{RevealText.PlainTextOf(line)}' needs {textHeight:0.#} px at {width:0} wide, more than two lines ({cap:0.#})");
                    line.MaxHeight = cap;
                }
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// THE REPORT: at 110 % text the community card showed THREE matches over an empty band,
    /// because 248 px holds four rows only at the reference size. The panel now grows by what its
    /// capped rows need, so four whole matches and five ranks show at every text size - and the
    /// rooms keep the star row whatever the panel takes.
    /// </summary>
    [Theory]
    [InlineData(1.10)]
    [InlineData(1.25)]
    public void AtLargerTextTheCardStillShowsFourWholeMatches(double factor)
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            WarsOfLibertyLauncher.Services.TextScale.Apply(factor);
            try
            {
                var tab = new MultiplayerTab();
                tab.SetCompactLayout(true);
                SetField(tab, "_config", new WarsOfLibertyLauncher.Models.LauncherConfig());
                var stats = Stats();
                stats.Leaderboard = Enumerable.Range(1, 8).Select(i => new LeaderboardRow
                {
                    Rank = i, UserId = "u" + i, DisplayName = "Player" + i, Rating = 1700 - i * 20, Rd = 90,
                }).ToList();
                stats.RecentMatches = Enumerable.Range(0, 8).Select(i => new CommunityMatch
                {
                    Id = "m" + i,
                    ModId = "wol",
                    MapName = "ESOC_Fertile Crescent",
                    DurationSeconds = 1500,
                    Competitive = true,
                    ReportedAt = DateTime.UtcNow.AddMinutes(-(36 + i * 40)).ToString("o"),
                    Participants = new List<MatchHistoryParticipant>
                    {
                        new() { UserId = "a", DisplayName = "Kaiser", Result = 1 },
                        new() { UserId = "b", DisplayName = "El Taita", Result = 0 },
                    },
                }).ToList();
                SetField(tab, "_communityStats", stats);
                Call(tab, "RenderActivityStrip");

                tab.RoomsListPanel.Children.Clear();
                for (var i = 0; i < 8; i++)
                    tab.RoomsListPanel.Children.Add(new Border { Height = 54, Margin = new Thickness(0, 0, 0, 6) });

                void Layout()
                {
                    tab.RoomsLeftColumn.Measure(new Size(1040, 716));
                    tab.RoomsLeftColumn.Arrange(new Rect(0, 0, 1040, 716));
                    tab.RoomsLeftColumn.UpdateLayout();
                }
                // No dispatcher runs here, so the passes the real tab queues are run by hand.
                for (var pass = 0; pass < 4; pass++) { Layout(); tab.ApplyActivityLayout(); }
                Layout();

                Assert.Equal("Fixed", tab.ActivityMode.ToString());
                Assert.Equal(4, tab.ActivityRecentList.VisibleCount);
                Assert.Equal(5, tab.ActivityRankingList.VisibleCount);
                Assert.True(tab.ActivityStrip.ActualHeight > RoomsActivityLayout.ExpandedHeight,
                    $"at {factor:P0} the panel stayed at {tab.ActivityStrip.ActualHeight}");
                Assert.True(tab.RoomsRow.Height.IsStar, "the rooms must keep the star row");
            }
            finally
            {
                WarsOfLibertyLauncher.Services.TextScale.Apply(1.0);
            }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// A four-player match that does not fit cuts its NAMES and must take the flags with it:
    /// WPF went on drawing the flags past the ellipsis, so a cut name was followed by the next
    /// player's flag (reported). And the cut line still reveals in full, flags included.
    /// </summary>
    [Fact]
    public void ANarrowFourPlayerRowHidesTheFlagsPastTheCutAndStillReveals()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var flag = new System.Windows.Media.Imaging.WriteableBitmap(
                14, 10, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
            var icons = new Dictionary<string, System.Windows.Media.ImageSource>(StringComparer.Ordinal)
            {
                ["Peruvians"] = flag, ["Mexicans"] = flag, ["Germans"] = flag, ["Salvadorans"] = flag,
            };
            var vocab = new DeckCardNames.Vocabulary(
                new Dictionary<string, WarsOfLibertyLauncher.Services.CardDetail>(StringComparer.Ordinal),
                new Dictionary<string, System.Windows.Media.ImageSource>(StringComparer.Ordinal),
                new Dictionary<string, string>(StringComparer.Ordinal),
                CivIcons: icons);
            var match = new CommunityMatch
            {
                Id = "m", ModId = "wol", MapName = "ESOC_Manchac", DurationSeconds = 1560, Competitive = true,
                ReportedAt = DateTime.UtcNow.AddHours(-11).ToString("o"),
                Participants = new List<MatchHistoryParticipant>
                {
                    new() { UserId = "a", DisplayName = "El Taita", Result = 0.5, Civ = "Peruvians" },
                    new() { UserId = "b", DisplayName = "Geaf_Argento", Result = 0.5, Civ = "Mexicans" },
                    new() { UserId = "c", DisplayName = "Kaiser", Result = 0.5, Civ = "Germans" },
                    new() { UserId = "d", DisplayName = "UnstoppableStreletsy", Result = 0.5, Civ = "Salvadorans" },
                },
            };

            var row = (FrameworkElement)MultiplayerTab.BuildRankingMatchRow(match, vocab);
            row.Measure(new Size(260, double.PositiveInfinity));
            row.Arrange(new Rect(0, 0, 260, row.DesiredSize.Height));

            var who = Descendants<TextBlock>(row).First(t =>
                t.Inlines.OfType<System.Windows.Documents.InlineUIContainer>().Any());
            who.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

            var chips = who.Inlines.OfType<System.Windows.Documents.InlineUIContainer>()
                .Select(c => (FrameworkElement)c.Child).ToList();
            Assert.Equal(4, chips.Count);
            Assert.Equal(Visibility.Visible, chips[0].Visibility);
            // The last player's flag sits far past a 260-px row: it must not be drawn.
            Assert.Equal(Visibility.Collapsed, chips[3].Visibility);

            // And the whole line is on hover, every flag included.
            var tip = Assert.IsType<ToolTip>(who.ToolTip);
            var revealed = Assert.IsType<TextBlock>(tip.Content);
            Assert.Contains("UnstoppableStreletsy", RevealText.PlainTextOf(revealed));
            Assert.Equal(4, revealed.Inlines.OfType<System.Windows.Documents.InlineUIContainer>().Count());
        });
        Assert.Null(error);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T t) yield return t;
            foreach (var deeper in Descendants<T>(child)) yield return deeper;
        }
    }

    /// <summary>
    /// The folded strip never trims — the handoff forbids "E…" — and when it is short it drops
    /// WHOLE segments, the matches count first, never the peak. In Spanish, because that is the
    /// wide language.
    /// </summary>
    [Fact]
    public void TheFoldedStripNeverTrimsAndDropsWholeSegments()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            try
            {
                Strings.SetLanguage("es");
                var tab = new MultiplayerTab();
                SetField(tab, "_communityStats", Stats());
                Call(tab, "FillActivityBar");

                // Peak, the last match, the matches count — the handoff's three, in its order.
                Assert.Equal(3, tab.ActivityBarSegments.Children.Count);
                Assert.DoesNotContain(Descendants(tab.ActivityBarSegments).OfType<TextBlock>(),
                    t => t.TextTrimming != TextTrimming.None);

                tab.ActivityBar.Visibility = Visibility.Visible;
                tab.ActivityBar.Measure(new Size(560, 44));
                tab.ActivityBar.Arrange(new Rect(0, 0, 560, 44));
                tab.ActivityBar.UpdateLayout();
                Call(tab, "FitActivityBar");

                var segments = tab.ActivityBarSegments.Children.OfType<FrameworkElement>().ToList();
                Assert.Equal(Visibility.Visible, segments[0].Visibility);      // the peak stays
                Assert.Equal(Visibility.Collapsed, segments[2].Visibility);    // the count goes first
                var visible = segments.Where(s => s.Visibility == Visibility.Visible).ToList();
                var shown = visible.Sum(s =>
                {
                    s.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    return s.DesiredSize.Width;
                });
                var room = ((Grid)tab.ActivityBarSegments.Parent).ColumnDefinitions[1].ActualWidth;
                Assert.True(shown <= room + 0.5 || visible.Count == 1,
                    "the segments still shown do not fit the strip");
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
