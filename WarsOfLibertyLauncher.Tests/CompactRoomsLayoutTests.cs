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
    /// GEOMETRY and nothing else. The room list keeps its own scroller, the community block (60)
    /// keeps its own row under it in both layouts, and going back to wide restores exactly what the
    /// XAML says. The chat column is NOT the switch's any more: it follows the page's width (57).
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
                // Two rows (61): the rooms, then the ONE community block — its header line, with the
                // facts on it, and its cards. No highlights row, no folded bar, no strip of facts.
                Assert.Equal(2, tab.RoomsLeftColumn.RowDefinitions.Count);
                Assert.Same(tab.ActivityHost, LogicalTreeHelper.GetParent(tab.ActivityBlock));
                Assert.Same(tab.ActivityBlockGrid, LogicalTreeHelper.GetParent(tab.ActivityStrip));
                Assert.Same(tab.ActivityHeader, LogicalTreeHelper.GetParent(tab.ActivityFacts));
                Assert.Same(tab.ActivityHeader, LogicalTreeHelper.GetParent(tab.ActivityFactsMonthLink));
                Assert.Equal(1, Grid.GetRow(tab.ActivityHost));
                Assert.Equal(0, Grid.GetRow(tab.RoomsBlock));
            }

            // The handoff's compact geometry: a 44-px sub-bar and the column header inset exactly
            // over the rows' content (the row's 1 of border + 12). The chat column is 280 px on a
            // page that has not been measured (and under 1600 px).
            Assert.Equal(280, tab.RoomsSideColumn.Width.Value);
            Assert.True(tab.RoomsSideColumn.Width.IsAbsolute);
            Assert.Equal(44, tab.SubBar.Height);
            Assert.Equal(tab.RoomsListPanel.Margin.Left + 13, tab.RoomsHeaderStrip.Margin.Left);

            tab.SetCompactLayout(false);
            // The list is a card in both layouts (57a) and the rows sit on its 16-px padding, so
            // their inset does not change with the switch — nor do the tab's margins and gaps,
            // which follow the width alone (PageSpacing).
            Assert.Equal(0, tab.RoomsListPanel.Margin.Left);
            Assert.Equal(new Thickness(16), tab.RoomsBlock.Padding);
            Assert.Equal(280, tab.RoomsSideColumn.Width.Value);
            Assert.Equal(48, tab.SubBar.Height);
        });
        Assert.Null(error);
    }

    /// <summary>
    /// THE ONE THAT MATTERS for design 61 and the maintainer's "always at the bottom": the
    /// community block's bottom IS the column's bottom — open or folded, with no rooms, one or
    /// eight — and the rooms take everything above it. Measured on the real layout.
    /// </summary>
    [Theory]
    [InlineData(0, null, "Fixed")]
    [InlineData(1, null, "Fixed")]
    [InlineData(8, null, "Fixed")]
    [InlineData(8, false, "Folded")]
    [InlineData(0, false, "Folded")]
    public void THE_ONE_THAT_MATTERS_TheBlockSitsOnTheColumnsBottom(int rooms, bool? choice, string expected)
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = LaidOut(rooms, choice, width: 1040, height: 900, stats: Stats(), empty: rooms == 0);

            Assert.Equal(expected, tab.ActivityMode.ToString());
            Assert.True(tab.RoomsRow.Height.IsStar, "the rooms row is always the star");

            var column = tab.RoomsLeftColumn.ActualHeight;
            var blockBottom = tab.ActivityHost.TranslatePoint(new Point(0, tab.ActivityHost.ActualHeight), tab.RoomsLeftColumn).Y;
            Assert.Equal(column, blockBottom, 1);

            var roomsBottom = tab.RoomsBlock.TranslatePoint(new Point(0, tab.RoomsBlock.ActualHeight), tab.RoomsLeftColumn).Y;
            var blockTop = tab.ActivityHost.TranslatePoint(new Point(0, 0), tab.RoomsLeftColumn).Y;
            Assert.Equal(blockTop - RoomsActivityLayout.Gap, roomsBottom, 1);
            Assert.Equal(0, tab.RoomsBlock.TranslatePoint(new Point(0, 0), tab.RoomsLeftColumn).Y, 1);

            if (expected == "Fixed")
            {
                Assert.Equal(tab.ActivityCardsHeight, tab.ActivityStrip.ActualHeight, 1);
                // The cards end at the fifth player — or, on a ladder of fewer, at the peak card's
                // floor — and nothing in the peak card is cut by that height.
                var peak = tab.ActivityPeakCard;
                var last = tab.ActivityPeakSubtitle;
                var lastBottom = last.TranslatePoint(new Point(0, last.ActualHeight), peak).Y;
                Assert.True(lastBottom <= peak.ActualHeight - peak.Padding.Bottom - peak.BorderThickness.Bottom + 0.5,
                    $"the peak card's last line ends at {lastBottom:0.#} of {peak.ActualHeight:0.#}: the cards ({tab.ActivityCardsHeight}) cut it");
            }
            else
            {
                Assert.Equal(Visibility.Collapsed, tab.ActivityStrip.Visibility);
                Assert.True(tab.ActivityHost.ActualHeight <= RoomsActivityLayout.FoldedHeight + 14,
                    $"the folded block is {tab.ActivityHost.ActualHeight:0} px: more than its header line");
            }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// 61b: with no rooms, the rooms card still fills everything above the block and its notice
    /// sits CENTRED in it — the gap that 60 left under the block is gone.
    /// </summary>
    [Fact]
    public void TheEmptyNoticeIsCentredInTheRoomsCard()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = LaidOut(0, null, width: 1040, height: 900, stats: Stats(), empty: true);

            Assert.Equal(Visibility.Visible, tab.RoomsEmptyState.Visibility);
            var headerBottom = tab.RoomsSectionHeader.TranslatePoint(
                new Point(0, tab.RoomsSectionHeader.ActualHeight + tab.RoomsSectionHeader.Margin.Bottom), tab.RoomsBlock).Y;
            // The space is the panel's CONTENT: inside its 16-px padding and its rim, the same
            // inset the title has above.
            var cardBottom = tab.RoomsBlock.ActualHeight - tab.RoomsBlock.Padding.Bottom - tab.RoomsBlock.BorderThickness.Bottom;
            var noticeTop = tab.RoomsEmptyState.TranslatePoint(new Point(0, 0), tab.RoomsBlock).Y;
            var noticeMiddle = noticeTop + tab.RoomsEmptyState.ActualHeight / 2;
            Assert.True(Math.Abs((headerBottom + cardBottom) / 2 - noticeMiddle) <= 2,
                $"the notice's middle is at {noticeMiddle:0.#}, the space's at {(headerBottom + cardBottom) / 2:0.#}");
            Assert.True(cardBottom > 500, $"the empty rooms card is {cardBottom:0} px: it should fill the column");
        });
        Assert.Null(error);
    }

    /// <summary>
    /// Design 61: the facts live on the block's HEADER LINE, beside its title — open or folded —
    /// and nowhere else; the line is one line, never two.
    /// </summary>
    [Theory]
    [InlineData(true, null)]
    [InlineData(false, null)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void TheFactsLiveOnTheHeaderLine(bool compact, bool? choice)
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var stats = Stats();
            stats.MonthlyHighlights = EloDemoData.Highlights();
            var tab = LaidOut(3, choice, width: 1040, height: 900, stats: stats, compact: compact);

            Assert.Equal(choice == false ? "Folded" : "Fixed", tab.ActivityMode.ToString());
            var facts = tab.ActivityFacts.Children.OfType<Border>().ToList();
            Assert.NotEmpty(facts);
            Assert.All(facts, f => Assert.Equal(MultiplayerTab.ActivityFactTag, f.Tag));
            Assert.Equal(facts.Count, Descendants(tab).OfType<Border>().Count(b => Equals(b.Tag, MultiplayerTab.ActivityFactTag)));
            Assert.Equal(28, tab.ActivityHeader.ActualHeight, 1);

            var headerTop = tab.ActivityHeader.TranslatePoint(new Point(0, 0), tab.RoomsLeftColumn).Y;
            foreach (var f in facts.Where(f => System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(f).Width > 0))
                Assert.Equal(headerTop, f.TranslatePoint(new Point(0, 0), tab.RoomsLeftColumn).Y, 1);
        });
        Assert.Null(error);
    }

    /// <summary>
    /// THE ONE THAT MATTERS for 57a's "Show activity opens the cards on top of the list". On a
    /// laptop column the rooms keep their four rows and the cards do not fit under them: shown,
    /// they lie OVER the list's bottom, opaque, with the block still in its row — folded to its
    /// one line (60b) and offering to hide them — and hiding puts them back in the block and folds it.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_AShownPanelThatDoesNotFitLiesOverTheListAndTheBlockStays()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            try
            {
                Strings.SetLanguage("en");
                var tab = new MultiplayerTab();
                tab.SetCompactLayout(true);
                var config = new WarsOfLibertyLauncher.Models.LauncherConfig { RoomsActivityChoice = true };
                SetField(tab, "_config", config);
                SetField(tab, "_communityStats", RichStats());
                Call(tab, "RenderActivityStrip");

                tab.RoomsListPanel.Children.Clear();
                for (var i = 0; i < 8; i++)
                    tab.RoomsListPanel.Children.Add(new Border { Height = 54, Margin = new Thickness(0, 0, 0, 6) });

                void Layout()
                {
                    tab.RoomsLeftColumn.Measure(new Size(1040, 520));
                    tab.RoomsLeftColumn.Arrange(new Rect(0, 0, 1040, 520));
                    tab.RoomsLeftColumn.UpdateLayout();
                }
                for (var pass = 0; pass < 3; pass++) { Layout(); tab.ApplyActivityLayout(); }
                Layout();

                Assert.Equal("Overlay", tab.ActivityMode.ToString());
                Assert.Same(tab.ActivityOverlayCard, LogicalTreeHelper.GetParent(tab.ActivityStrip));
                Assert.Equal(Visibility.Visible, tab.ActivityOverlayHost.Visibility);
                // The block stays in its row, folded to its header line.
                Assert.Equal(Visibility.Visible, tab.ActivityHost.Visibility);
                Assert.True(tab.ActivityHost.ActualHeight <= RoomsActivityLayout.FoldedHeight + 14);
                Assert.True(tab.RoomsRow.Height.IsStar, "the rooms keep their row; the cards lie over it");
                // The cards cover the list's bottom, not the block under it.
                var overlayBottom = tab.ActivityOverlayHost.TranslatePoint(
                    new Point(0, tab.ActivityOverlayHost.ActualHeight), tab.RoomsLeftColumn).Y;
                var blockTop = tab.ActivityHost.TranslatePoint(new Point(0, 0), tab.RoomsLeftColumn).Y;
                Assert.True(overlayBottom <= blockTop, $"the cards end at {overlayBottom:0}, under the block at {blockTop:0}");
                Assert.Equal("Hide ▾", tab.ActivityToggle.Content);
                // Laid over the list the cards are at their measured minimum, which holds the top five whole.
                Assert.Equal(5, tab.ActivityRankingList.VisibleCount);

                tab.ActivityToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.False(config.RoomsActivityChoice);
                Assert.Equal("Folded", tab.ActivityMode.ToString());
                Assert.Same(tab.ActivityBlockGrid, LogicalTreeHelper.GetParent(tab.ActivityStrip));
                Assert.Equal(Visibility.Collapsed, tab.ActivityStrip.Visibility);
                Assert.Equal(Visibility.Collapsed, tab.ActivityOverlayHost.Visibility);
                Assert.Equal("Show ▴", tab.ActivityToggle.Content);
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// THE ONE THAT MATTERS for the rooms' priority (57b): with eight rooms on a laptop column, at
    /// least four whole rows are on screen — and the community block is folded to its one line.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_OnALaptopFourRoomRowsStayOnScreen()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            tab.SetCompactLayout(true);
            SetField(tab, "_config", new WarsOfLibertyLauncher.Models.LauncherConfig());
            SetField(tab, "_communityStats", Stats());
            Call(tab, "RenderActivityStrip");

            tab.RoomsListPanel.Children.Clear();
            for (var i = 0; i < 8; i++)
                tab.RoomsListPanel.Children.Add(new Border { Height = 54, Margin = new Thickness(0, 0, 0, 6) });

            void Layout()
            {
                tab.RoomsLeftColumn.Measure(new Size(1040, 520));
                tab.RoomsLeftColumn.Arrange(new Rect(0, 0, 1040, 520));
                tab.RoomsLeftColumn.UpdateLayout();
            }
            for (var pass = 0; pass < 3; pass++) { Layout(); tab.ApplyActivityLayout(); }
            Layout();

            Assert.Equal("Folded", tab.ActivityMode.ToString());
            Assert.True(tab.RoomsListScroll.ViewportHeight >= 4 * 60 - 6,
                $"the list shows {tab.RoomsListScroll.ViewportHeight:0} px: fewer than four rows");
        });
        Assert.Null(error);
    }

    /// <summary>
    /// 57c: "»" folds the chat to a 44-px rail and the launcher remembers it; the rail's players
    /// count unfolds it on the Players tab and its chat icon on the chat.
    /// </summary>
    [Fact]
    public void TheChatFoldsToARailThatIsRememberedAndUnfoldsOnTheTabYouPick()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            var config = new WarsOfLibertyLauncher.Models.LauncherConfig();
            SetField(tab, "_config", config);

            tab.ChatFoldButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.True(config.RoomsChatFolded);
            Assert.Equal(Visibility.Visible, tab.ChatRail.Visibility);
            Assert.Equal(Visibility.Collapsed, tab.ChatPanelCard.Visibility);
            Assert.Equal(RoomsActivityLayout.ChatRail, tab.RoomsSideColumn.Width.Value);

            tab.ChatRailPlayersButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.False(config.RoomsChatFolded);
            Assert.Equal(Visibility.Visible, tab.ChatPanelCard.Visibility);
            Assert.Equal(Visibility.Collapsed, tab.ChatRail.Visibility);
            Assert.Equal(Visibility.Visible, tab.PlayersScroll.Visibility);
            Assert.Equal(RoomsActivityLayout.ChatNarrow, tab.RoomsSideColumn.Width.Value);

            tab.ChatFoldButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            tab.ChatRailChatButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal(Visibility.Visible, tab.PanelChatBody.Visibility);
            Assert.Equal(Visibility.Collapsed, tab.PlayersScroll.Visibility);

            // A fresh tab with the remembered fold starts folded.
            config.RoomsChatFolded = true;
            var again = new MultiplayerTab();
            SetField(again, "_config", config);
            Call(again, "ApplyChatFold");
            Assert.Equal(Visibility.Visible, again.ChatRail.Visibility);
        });
        Assert.Null(error);
    }

    /// <summary>
    /// The rail counts only what was MISSED: live messages from somebody else while folded. It
    /// starts at zero, ignores everything while unfolded, and unfolding clears it.
    /// </summary>
    [Fact]
    public void TheRailCountsOnlyMessagesMissedWhileFolded()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            var config = new WarsOfLibertyLauncher.Models.LauncherConfig();
            SetField(tab, "_config", config);

            Call(tab, "CountUnreadChat");
            Assert.Equal(0, tab.ChatUnread);

            tab.SetChatFolded(true);
            Call(tab, "CountUnreadChat");
            Call(tab, "CountUnreadChat");
            Assert.Equal(2, tab.ChatUnread);
            Assert.Equal(Visibility.Visible, tab.ChatRailUnreadBadge.Visibility);
            Assert.Equal("2", tab.ChatRailUnreadText.Text);

            tab.SetChatFolded(false);
            Assert.Equal(0, tab.ChatUnread);
            Assert.Equal(Visibility.Collapsed, tab.ChatRailUnreadBadge.Visibility);
        });
        Assert.Null(error);
    }

    /// <summary>
    /// Where the rail counts matters as much as how: once, in the LIVE frame handler, inside the
    /// branch that already skips our own messages — never in the history replay, or rejoining the
    /// channel would report its whole backlog as unread.
    /// </summary>
    [Fact]
    public void TheUnreadCountIsTakenOnlyFromLiveMessagesOfOthers()
    {
        var source = File.ReadAllText(RepoFile("Controls/MultiplayerTab.xaml.cs"));
        var calls = System.Text.RegularExpressions.Regex.Matches(source, @"CountUnreadChat\(\)");
        Assert.Single(calls);
        var before = source[..calls[0].Index];
        var guard = before.LastIndexOf("_session?.CurrentUser?.Id", StringComparison.Ordinal);
        var frame = before.LastIndexOf("case \"chat\":", StringComparison.Ordinal);
        Assert.True(guard > frame && frame > 0, "CountUnreadChat must sit in the live chat frame, past the own-message guard");
    }

    /// <summary>
    /// 61a, the laptop frame (1300 × 800: a 682-px column 984 wide): the three cards stay, at
    /// their minimum — 44-px-and-up bars, two-line matches, the top five — and the rooms keep three
    /// rows above the block. Every match and every rank shown is WHOLE.
    /// </summary>
    [Fact]
    public void OnTheLaptopFrameTheCardsAreCompactAndTheRoomsKeepTheirRows()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = LaidOut(3, null, width: 984, height: 682, stats: RichStats(), pageWidth: 1300);

            Assert.Equal("Fixed", tab.ActivityMode.ToString());
            Assert.Equal(5, tab.ActivityRankingList.VisibleCount);
            Assert.InRange(tab.ActivityRecentList.VisibleCount, 2, 5);
            AssertTheCardsEndAtTheFifthPlayer(tab);
            // Two-line rows (the correction to 61), each with 6 px above it on a laptop.
            Assert.All(tab.ActivityRecentList.Children.OfType<Border>(), b => Assert.Equal(6, b.Padding.Top));
            Assert.True(tab.RoomsListScroll.ViewportHeight >= 3 * 60 - 6,
                $"the rooms show {tab.RoomsListScroll.ViewportHeight:0} px: fewer than three rows");
            Assert.True(tab.ActivityHost.ActualHeight < 0.4 * 682,
                $"the block is {tab.ActivityHost.ActualHeight:0} px of a 682-px column");
        });
        Assert.Null(error);
    }

    /// <summary>
    /// THE ONE THAT MATTERS for the maintainer's "the limit should be the 5th best player": on the
    /// big frame (2560 × 1392: a 1274-px column) the cards end right under the Ranking card's fifth
    /// row — no band of nothing below it, as a third of the column left — and the rooms take the
    /// rest. The matches card shows the whole rows that fit.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheBlockEndsAtTheFifthPlayer()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = LaidOut(0, null, width: 2204, height: 1274, stats: RichStats(), pageWidth: 2560, empty: true);

            Assert.Equal("Fixed", tab.ActivityMode.ToString());
            Assert.Equal(5, tab.ActivityRankingList.VisibleCount);
            AssertTheCardsEndAtTheFifthPlayer(tab);
            Assert.True(tab.ActivityRecentList.VisibleCount >= 2, $"{tab.ActivityRecentList.VisibleCount} matches");
            Assert.True(tab.ActivityHost.ActualHeight < 0.25 * 1274,
                $"the block is {tab.ActivityHost.ActualHeight:0} px of a 1274-px column");
            Assert.All(tab.ActivityRecentList.Children.OfType<Border>(), b => Assert.Equal(9, b.Padding.Top));
        });
        Assert.Null(error);
    }

    /// <summary>
    /// The Ranking card's fifth row is the last thing in the cards: its bottom plus the card's own
    /// padding and rim is the card's bottom, within a pixel, and that card is as tall as the cards.
    /// </summary>
    private static void AssertTheCardsEndAtTheFifthPlayer(MultiplayerTab tab)
    {
        var card = tab.ActivityMiddleCard;
        var rows = tab.ActivityRankingList.Children.OfType<FrameworkElement>().Take(5).ToList();
        Assert.Equal(5, rows.Count);
        var last = rows[^1];
        var lastBottom = last.TranslatePoint(new Point(0, last.ActualHeight), card).Y;
        var inside = card.ActualHeight - card.Padding.Bottom - card.BorderThickness.Bottom;
        Assert.True(Math.Abs(inside - lastBottom) <= 1.5,
            $"{inside - lastBottom:0.#} px of nothing under the fifth player");
        Assert.Equal(tab.ActivityStrip.ActualHeight, card.ActualHeight, 1);
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
    /// At a larger text size the rows grow, and the block's MEASURED height grows with them: the top
    /// five still show whole and the cards still end at the fifth, the matches card shows the whole
    /// rows that fit — and the rooms keep the star row.
    /// </summary>
    [Theory]
    [InlineData(1.10)]
    [InlineData(1.25)]
    public void AtLargerTextEveryRankStillShowsWholeAndTheCardsEndThere(double factor)
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            WarsOfLibertyLauncher.Services.TextScale.Apply(factor);
            try
            {
                var tab = LaidOut(3, null, width: 1040, height: 900, stats: RichStats());
                Assert.Equal("Fixed", tab.ActivityMode.ToString());
                Assert.Equal(5, tab.ActivityRankingList.VisibleCount);
                AssertTheCardsEndAtTheFifthPlayer(tab);
                Assert.True(tab.ActivityRecentList.VisibleCount >= 2, $"{tab.ActivityRecentList.VisibleCount} matches at {factor:P0}");
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

    /// <summary>
    /// THE ONE THAT MATTERS for design 58b: the flags are 18 × 12 with a thin rim, 6 px before the
    /// name — in the rooms panel and in the Ranking list alike — and the second line starts 24 px
    /// in, under the first NAME rather than under its flag. With no flag to step over, it does not
    /// step in.
    /// </summary>
    [Theory]
    [InlineData(false)]   // the rooms panel's row
    [InlineData(true)]    // the Ranking list's row (design 59's sizes)
    public void THE_ONE_THAT_MATTERS_TheFlagsAre18By12AndLineTwoStartsUnderTheFirstName(bool ranking)
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var flag = new System.Windows.Media.Imaging.WriteableBitmap(
                18, 12, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
            var icons = new Dictionary<string, System.Windows.Media.ImageSource>(StringComparer.Ordinal)
            {
                ["Peruvians"] = flag, ["Mexicans"] = flag,
            };
            var vocab = new DeckCardNames.Vocabulary(
                new Dictionary<string, WarsOfLibertyLauncher.Services.CardDetail>(StringComparer.Ordinal),
                new Dictionary<string, System.Windows.Media.ImageSource>(StringComparer.Ordinal),
                new Dictionary<string, string>(StringComparer.Ordinal),
                CivIcons: icons);
            var match = new CommunityMatch
            {
                Id = "m", ModId = "wol", MapName = "ESOC_Indonesia", DurationSeconds = 1140, Competitive = true,
                ReportedAt = DateTime.UtcNow.AddDays(-1).ToString("o"),
                Participants = new List<MatchHistoryParticipant>
                {
                    new() { UserId = "a", DisplayName = "Aluclown", Result = 1, Civ = "Peruvians" },
                    new() { UserId = "b", DisplayName = "Geaf_Argento", Result = 0, Civ = "Mexicans" },
                },
            };

            (TextBlock Who, TextBlock Sub, FrameworkElement Row) Build(DeckCardNames.Vocabulary? v)
            {
                var row = (FrameworkElement)MultiplayerTab.BuildRankingMatchRow(
                    match, v, look: ranking ? MultiplayerTab.MatchRowLook.Ranking(15) : null);
                row.Measure(new Size(460, double.PositiveInfinity));
                row.Arrange(new Rect(0, 0, 460, row.DesiredSize.Height));
                row.UpdateLayout();
                var blocks = Descendants<TextBlock>(row).ToList();
                return (blocks.First(t => Grid.GetRow(t) == 0 && Grid.GetColumn(t) == 1),
                        blocks.Single(t => Grid.GetRow(t) == 1), row);
            }

            var (who, sub, built) = Build(vocab);
            var chip = Assert.IsType<Border>(who.Inlines.OfType<System.Windows.Documents.InlineUIContainer>().First().Child);
            Assert.Equal(MultiplayerTab.MatchFlagWidth, chip.Width);
            Assert.Equal(18, chip.Width);
            Assert.Equal(12, chip.Height);
            Assert.Equal(new CornerRadius(2), chip.CornerRadius);
            Assert.Equal(new Thickness(1), chip.BorderThickness);
            Assert.Equal(System.Windows.Media.Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF),
                Assert.IsType<System.Windows.Media.SolidColorBrush>(chip.BorderBrush).Color);
            Assert.Equal(6, chip.Margin.Right);

            // Line 2 starts where the first NAME does: the flag and its gap further in.
            var whoX = who.TranslatePoint(new Point(0, 0), built).X;
            var subX = sub.TranslatePoint(new Point(0, 0), built).X;
            Assert.Equal(whoX + 24, subX, 0.5);

            // No flag: the leading player still takes a flag's room — an EMPTY slot, the
            // maintainer's request — so line 2 steps in the same 24 px and lines up with the
            // flagged rows around it. It draws and says nothing.
            var (plainWho, plainSub, plainRow) = Build(null);
            Assert.Equal(subX, plainSub.TranslatePoint(new Point(0, 0), plainRow).X, 0.5);
            Assert.Equal(plainWho.TranslatePoint(new Point(0, 0), plainRow).X + 24,
                plainSub.TranslatePoint(new Point(0, 0), plainRow).X, 0.5);
            var slot = Assert.IsType<Border>(Assert.IsType<System.Windows.Documents.InlineUIContainer>(
                plainWho.Inlines.FirstInline).Child);
            Assert.Equal(MultiplayerTab.EmptyFlagSlotTag, slot.Tag);
            Assert.Equal(18, slot.Width);
            Assert.Equal(12, slot.Height);
            Assert.Equal(6, slot.Margin.Right);
            Assert.Null(slot.Background);
            Assert.Null(slot.BorderBrush);
            Assert.Null(slot.ToolTip);
        });
        Assert.Null(error);
    }

    /// <summary>
    /// THE ONE THAT MATTERS for the maintainer's "make it symmetric": every match row lines up —
    /// line 2 at the same x whether the first player has a flag or not, decided or not, in the
    /// Rooms card and in the Ranking list. Only the LEADING player ever gets the empty slot; a
    /// later player with no flag gets none, or "A beat B" would grow a hole mid-sentence.
    /// </summary>
    [Theory]
    [InlineData(false)]   // the Rooms card
    [InlineData(true)]    // the Ranking list
    public void THE_ONE_THAT_MATTERS_EveryRowLinesUpWhetherOrNotItHasAFlag(bool ranking)
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var flag = new System.Windows.Media.Imaging.WriteableBitmap(
                18, 12, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
            var vocab = new DeckCardNames.Vocabulary(
                new Dictionary<string, WarsOfLibertyLauncher.Services.CardDetail>(StringComparer.Ordinal),
                new Dictionary<string, System.Windows.Media.ImageSource>(StringComparer.Ordinal),
                new Dictionary<string, string>(StringComparer.Ordinal),
                CivIcons: new Dictionary<string, System.Windows.Media.ImageSource>(StringComparer.Ordinal)
                {
                    ["Germans"] = flag, ["Russians"] = flag,
                });
            var look = ranking
                ? MultiplayerTab.MatchRowLook.Ranking(15)
                : MultiplayerTab.MatchRowLook.Rooms(RoomsActivityLayout.Fluid(1300));
            CommunityMatch M(double a, string? civA, double b, string? civB) => new()
            {
                Id = "m", ModId = "wol", MapName = "ESOC_Florida", DurationSeconds = 24 * 60, Competitive = true,
                ReportedAt = DateTime.UtcNow.AddDays(-1).ToString("o"),
                Participants = new List<MatchHistoryParticipant>
                {
                    new() { UserId = "a", DisplayName = "Kaiser", Result = a, Civ = civA },
                    new() { UserId = "b", DisplayName = "UnstoppableStreletsy", Result = b, Civ = civB },
                },
            };
            (double SubX, TextBlock Who) Build(CommunityMatch m)
            {
                var row = (FrameworkElement)MultiplayerTab.BuildRankingMatchRow(m, vocab, look: look);
                row.Measure(new Size(460, double.PositiveInfinity));
                row.Arrange(new Rect(0, 0, 460, row.DesiredSize.Height));
                row.UpdateLayout();
                var blocks = Descendants<TextBlock>(row).ToList();
                var sub = blocks.Single(t => Grid.GetRow(t) == 1);
                return (sub.TranslatePoint(new Point(0, 0), row).X,
                        blocks.First(t => Grid.GetRow(t) == 0 && Grid.GetColumn(t) == 1));
            }

            var flagged = Build(M(1, "Germans", 0, "Russians"));
            var flaglessUndecided = Build(M(0.5, null, 0.5, null));
            var flaglessDecided = Build(M(1, null, 0, null));
            var secondOnly = Build(M(1, null, 0, "Russians"));

            foreach (var other in new[] { flaglessUndecided, flaglessDecided, secondOnly })
                Assert.Equal(flagged.SubX, other.SubX, 0.5);

            // One slot, leading the line — never a second one for the flagless loser.
            int Slots(TextBlock who) => who.Inlines.OfType<System.Windows.Documents.InlineUIContainer>()
                .Count(c => Equals((c.Child as FrameworkElement)?.Tag, MultiplayerTab.EmptyFlagSlotTag));
            Assert.Equal(1, Slots(flaglessUndecided.Who));
            Assert.Equal(1, Slots(flaglessDecided.Who));
            Assert.Equal(0, Slots(flagged.Who));
            Assert.Equal(1, Slots(secondOnly.Who));
            Assert.Equal(2, secondOnly.Who.Inlines.OfType<System.Windows.Documents.InlineUIContainer>().Count());
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
    /// THE ONE THAT MATTERS for 61's header line: when the facts do not fit they never wrap — whole
    /// facts are dropped FROM THE END (Most played first, then Players), and the ones shown fit.
    /// In Spanish, the wide language, on a narrow column.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheFactsStayOnOneLineAndDropFromTheEnd()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            try
            {
                Strings.SetLanguage("es");
                var stats = Stats();
                stats.MonthlyHighlights = EloDemoData.Highlights();
                stats.Totals!.Players = 18;
                stats.Totals.PlayersWindowDays = 7;
                stats.Totals.TopMap = "ESOC_Fertile Crescent";
                var tab = LaidOut(3, false, width: 760, height: 716, stats: stats);

                var facts = tab.ActivityFacts.Children.OfType<Border>().ToList();
                Assert.Equal(6, facts.Count);
                var shown = tab.ActivityFacts.VisibleCount;
                Assert.InRange(shown, 1, 5);
                // A prefix: the first `shown` have a slot on the line, every later one an empty one
                // (which draws nothing and takes no hits).
                for (var i = 0; i < facts.Count; i++)
                    Assert.Equal(i < shown, System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(facts[i]).Width > 0);
                // One line: the header did not grow.
                Assert.Equal(28, tab.ActivityHeader.ActualHeight, 1);
                var used = facts.Take(shown).Sum(f => System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(f).Width);
                Assert.True(used <= tab.ActivityFacts.ActualWidth + 0.5);
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// THE ONE THAT MATTERS for the maintainer's correction to 61: the community card's matches are
    /// the Ranking's «Latest matches» row — TWO lines. Line 1 the players and the age; line 2, 24 px
    /// in under the first name, the kind of room in bold and its own colour, then map and length,
    /// trimmed at the end and never right-aligned. The padding follows the page: 6 on a laptop, 9
    /// on a big screen (one less below, the hairline is inside it).
    /// </summary>
    [Theory]
    [InlineData(1300, 6)]
    [InlineData(2560, 9)]
    public void THE_ONE_THAT_MATTERS_ACommunityMatchIsTwoLinesLikeLatestMatches(double pageWidth, double padding)
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            try
            {
                Strings.SetLanguage("en");
                var flag = new System.Windows.Media.Imaging.WriteableBitmap(
                    18, 12, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
                var vocab = new DeckCardNames.Vocabulary(
                    new Dictionary<string, WarsOfLibertyLauncher.Services.CardDetail>(StringComparer.Ordinal),
                    new Dictionary<string, System.Windows.Media.ImageSource>(StringComparer.Ordinal),
                    new Dictionary<string, string>(StringComparer.Ordinal),
                    CivIcons: new Dictionary<string, System.Windows.Media.ImageSource>(StringComparer.Ordinal)
                    {
                        ["Germans"] = flag, ["Russians"] = flag,
                    });
                var look = MultiplayerTab.MatchRowLook.Rooms(RoomsActivityLayout.Fluid(pageWidth));
                var ages = new List<(TextBlock, DateTime)>();
                CommunityMatch M(double a, double b, bool? competitive) => new()
                {
                    Id = "m", ModId = "wol", MapName = "ESOC_Florida", DurationSeconds = 24 * 60, Competitive = competitive,
                    ReportedAt = DateTime.UtcNow.AddDays(-1).ToString("o"),
                    Participants = new List<MatchHistoryParticipant>
                    {
                        new() { UserId = "a", DisplayName = "Kaiser", Result = a, Civ = "Germans" },
                        new() { UserId = "b", DisplayName = "UnstoppableStreletsy", Result = b, Civ = "Russians" },
                    },
                };
                (Border Row, TextBlock Who, TextBlock Sub) Build(CommunityMatch m)
                {
                    var built = (Border)MultiplayerTab.BuildRankingMatchRow(m, vocab, ages, look);
                    built.Measure(new Size(460, double.PositiveInfinity));
                    built.Arrange(new Rect(0, 0, 460, built.DesiredSize.Height));
                    built.UpdateLayout();
                    var blocks = Descendants(built).OfType<TextBlock>().ToList();
                    return (built, blocks.First(t => Grid.GetRow(t) == 0 && Grid.GetColumn(t) == 1),
                            blocks.Single(t => Grid.GetRow(t) == 1));
                }

                var (row, who, sub) = Build(M(1, 0, true));
                Assert.Equal(padding, row.Padding.Top);
                Assert.Equal(padding - 1, row.Padding.Bottom);
                // Two lines: line 2 is UNDER line 1, not beside it, and 24 px in, under the first name.
                Assert.True(sub.TranslatePoint(new Point(0, 0), row).Y >= who.TranslatePoint(new Point(0, who.ActualHeight), row).Y - 0.5);
                Assert.Equal(who.TranslatePoint(new Point(0, 0), row).X + 24, sub.TranslatePoint(new Point(0, 0), row).X, 0.5);
                // Line 2 is one run of text: the label first, bold, in the competitive gold; then map and length.
                var label = sub.Inlines.OfType<System.Windows.Documents.Run>().First();
                Assert.Equal("COMPETITIVE 1v1", label.Text);
                Assert.Equal(FontWeights.Bold, label.FontWeight);
                Assert.Same(Application.Current.FindResource("MpMatchLabelCompetitive"), label.Foreground);
                Assert.Equal("COMPETITIVE 1v1 · ESOC Florida · 24 min", RevealText.PlainTextOf(sub));
                Assert.Equal(TextWrapping.NoWrap, sub.TextWrapping);
                Assert.Equal(TextTrimming.CharacterEllipsis, sub.TextTrimming);
                Assert.NotEqual(TextAlignment.Right, sub.TextAlignment);
                Assert.Equal(HorizontalAlignment.Stretch, sub.HorizontalAlignment);
                // The winner in SemiBold; the age handed back so it ticks.
                Assert.Contains(who.Inlines.OfType<System.Windows.Documents.Run>(),
                    r => r.Text == "Kaiser" && r.FontWeight == FontWeights.SemiBold);
                Assert.Single(ages);

                // Casual: the label in its own grey-blue, still bold.
                var casual = Build(M(1, 0, false)).Sub.Inlines.OfType<System.Windows.Documents.Run>().First();
                Assert.Same(Application.Current.FindResource("MpMatchLabelCasual"), casual.Foreground);
                Assert.Equal(FontWeights.Bold, casual.FontWeight);

                // Nobody won: "no result", and the map stays.
                Assert.Equal("COMPETITIVE 1v1 · no result · ESOC Florida · 24 min",
                    RevealText.PlainTextOf(Build(M(0.5, 0.5, true)).Sub));
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

    /// <summary>
    /// A tab whose Rooms column is laid out directly at <paramref name="width"/> × <paramref name="height"/>
    /// (on a bare tab nobody is signed in, so the sign-in gate would collapse everything above it),
    /// with <paramref name="rooms"/> rows or the empty notice, the community block rendered from
    /// <paramref name="stats"/>, and the layout passes the real tab queues run by hand.
    /// </summary>
    private static MultiplayerTab LaidOut(int rooms, bool? choice, double width, double height,
        CommunityStats stats, bool compact = true, double? pageWidth = null, bool empty = false)
    {
        var tab = new MultiplayerTab();
        tab.SetCompactLayout(compact);
        tab.ActivityPageWidthOverride = pageWidth ?? width + 300;
        SetField(tab, "_config", new WarsOfLibertyLauncher.Models.LauncherConfig { RoomsActivityChoice = choice });
        SetField(tab, "_communityStats", stats);
        if (empty)
            typeof(MultiplayerTab).GetMethod("RenderRoomRows", Private)!
                .Invoke(tab, new object?[] { new List<LobbySummary>() });
        Call(tab, "RenderActivityStrip");

        if (!empty)
        {
            tab.RoomsListPanel.Children.Clear();
            for (var i = 0; i < rooms; i++)
                tab.RoomsListPanel.Children.Add(new Border { Height = 54, Margin = new Thickness(0, 0, 0, 6) });
        }

        void Layout()
        {
            tab.RoomsLeftColumn.Measure(new Size(width, height));
            tab.RoomsLeftColumn.Arrange(new Rect(0, 0, width, height));
            tab.RoomsLeftColumn.UpdateLayout();
        }
        for (var pass = 0; pass < 4; pass++) { Layout(); tab.ApplyActivityLayout(); }
        Layout();
        return tab;
    }

    /// <summary>A community with plenty: twelve matches and eight ranked players.</summary>
    private static CommunityStats RichStats()
    {
        var stats = Stats();
        stats.Leaderboard = Enumerable.Range(1, 8).Select(i => new LeaderboardRow
        {
            Rank = i, UserId = "u" + i, DisplayName = "Player" + i, Rating = 1700 - i * 20, Rd = 90,
        }).ToList();
        stats.RecentMatches = Enumerable.Range(0, 12).Select(i => new CommunityMatch
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
        return stats;
    }

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
