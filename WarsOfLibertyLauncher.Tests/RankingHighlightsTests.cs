using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Ranking › Highlights on the real page: the third option beside 1v1 and Teams, which takes the
/// whole page — the table and the match list step aside — and draws one card per highlight with
/// its top five. Each test names the failure it is for; most of them would be invisible in a
/// build and look fine in a screenshot of the wrong mode.
/// </summary>
[Collection("wpf-and-language")]
public class RankingHighlightsTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static void Click(Button b)
        => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var d in Walk(child)) yield return d;
    }

    private static List<Border> CardsOf(MultiplayerTab tab)
        => Walk(tab.RankingHighlightsBody).OfType<Border>().Where(b => Equals(b.Tag, MultiplayerTab.LeaderCardTag)).ToList();

    private static List<Border> RowsOf(DependencyObject card)
        => Walk(card).OfType<Border>().Where(b => Equals(b.Tag, MultiplayerTab.LeaderRowTag)).ToList();

    private static List<string> TextOf(DependencyObject root)
        => Walk(root).OfType<TextBlock>().Select(RevealText.PlainTextOf).ToList();

    /// <summary>
    /// THE ONE THAT MATTERS: the Highlights button shows the view and puts the table and the
    /// match list away — and 1v1 brings them back. With both on screen the page would be the
    /// table with a second page drawn over it.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheHighlightsButtonSwapsThePage()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            tab.ShowDemoElo("ranking");
            Assert.Equal(Visibility.Visible, tab.RankingTableCard.Visibility);
            Assert.Equal(Visibility.Collapsed, tab.RankingHighlightsView.Visibility);

            Click(tab.RankingModeHighlights);
            Assert.Equal("active", tab.RankingModeHighlights.Tag);
            Assert.Null(tab.RankingModeSolo.Tag);
            Assert.Equal(Visibility.Visible, tab.RankingHighlightsView.Visibility);
            Assert.Equal(Visibility.Collapsed, tab.RankingTableCard.Visibility);
            Assert.Equal(Visibility.Collapsed, tab.RankingHistoryCard.Visibility);
            Assert.Equal(Visibility.Visible, tab.RankingMonthCapsule.Visibility);
            Assert.NotEmpty(CardsOf(tab));

            Click(tab.RankingModeSolo);
            Assert.Equal(Visibility.Visible, tab.RankingTableCard.Visibility);
            Assert.Equal(Visibility.Collapsed, tab.RankingHighlightsView.Visibility);
            Assert.Equal(Visibility.Collapsed, tab.RankingMonthCapsule.Visibility);
        });
        Assert.Null(error);
    }

    /// <summary>Seven cards, in the strip's priority order, of at most five rows each.</summary>
    [Fact]
    public void TheCardsComeInOrderWithAtMostFiveRows()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            Strings.SetLanguage(Strings.LangEn);
            try
            {
                var tab = new MultiplayerTab();
                tab.ShowDemoElo("rankinghighlights");
                var cards = CardsOf(tab);
                Assert.Equal(7, cards.Count);
                var titles = cards.Select(c => TextOf(c)[0]).ToList();
                Assert.Equal(new[]
                {
                    "Biggest climb", "Most wins", "Most matches", "Best streak",
                    "Best win rate", "Biggest upset", "Civilization of the month",
                }, titles);
                Assert.All(cards, c => Assert.InRange(RowsOf(c).Count, 1, 5));
                // The subtitle says how many matches stand behind the month.
                Assert.Contains(EloDemoData.Highlights().Current!.TotalRated.ToString("N0", Strings.Culture),
                    tab.RankingSubtitleText.Text);
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// The viewer's own rows are marked the way the ladder marks them — its tint and "YOU" — and
    /// nobody else's is. The demo puts the viewer in the 1v1 climb, the 1v1 streak and an upset.
    /// </summary>
    [Fact]
    public void TheViewersOwnRowsAreMarked()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            tab.ShowDemoElo("rankinghighlights");
            // Design 93f: the viewer's own row is blue with a rim; the first of a card is soft gold.
            var own = Color.FromArgb(0x14, 0x55, 0xAC, 0xEE);
            var rows = CardsOf(tab).SelectMany(RowsOf).ToList();
            var marked = rows.Where(r => r.Background is SolidColorBrush b && b.Color == own).ToList();
            Assert.Equal(3, marked.Count);
            Assert.All(marked, r => Assert.Contains(Strings.Get("MpRankYouTag"), TextOf(r)));
            Assert.All(rows.Except(marked), r => Assert.DoesNotContain(Strings.Get("MpRankYouTag"), TextOf(r)));
        });
        Assert.Null(error);
    }

    /// <summary>
    /// The climb and streak cards offer "1v1 · Teams" only when both ladders have someone, and
    /// the switch changes the rows. Last month's team climb is empty, so its card has no switch.
    /// </summary>
    [Fact]
    public void TheLadderSwitchShowsOnlyWithBothLaddersAndSwapsTheRows()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            tab.ShowDemoElo("rankinghighlights");

            var climb = CardsOf(tab)[0];
            var switches = Walk(climb).OfType<Button>().ToList();
            Assert.Equal(2, switches.Count);
            Assert.Contains("Siux", TextOf(RowsOf(climb)[0]));

            Click(switches[1]);   // Teams
            var teamClimb = CardsOf(tab)[0];
            Assert.Contains("Pedro", TextOf(RowsOf(teamClimb)[0]));

            Click(tab.RankingMonthPrevious);
            Assert.Equal("active", tab.RankingMonthPrevious.Tag);
            var lastClimb = CardsOf(tab)[0];
            Assert.Empty(Walk(lastClimb).OfType<Button>());
            Assert.Contains("+187", TextOf(lastClimb));
        });
        Assert.Null(error);
    }

    /// <summary>
    /// Design 119: the cards are a mosaic. Laid out at a wide page they take four columns and the
    /// civilization's card two of them; at a laptop's width two columns, the civilization's card
    /// the whole row; and no card is stretched to its neighbour's height.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheCardsAreAMosaic()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            tab.ShowDemoElo("rankinghighlights");
            var mosaic = tab.RankingLeadersMosaic!;
            var cards = CardsOf(tab);
            Assert.Equal(7, mosaic.Children.Count);
            var civ = cards[6];
            Assert.Equal(2, BentoPanel.GetSpan(civ));
            Assert.All(cards.Take(6), c => Assert.Equal(1, BentoPanel.GetSpan(c)));

            void Lay(double width)
            {
                mosaic.Measure(new Size(width, double.PositiveInfinity));
                mosaic.Arrange(new Rect(0, 0, width, mosaic.DesiredSize.Height));
            }
            Rect Slot(Border card) => LayoutInformation.GetLayoutSlot(card);

            Lay(1880);
            Assert.Equal(4, mosaic.Columns);
            var column = (1880 - 3 * 14) / 4.0;
            Assert.Equal(column, Slot(cards[0]).Width, 1);
            Assert.Equal(2 * column + 14, Slot(civ).Width, 1);
            // The first four share a row and the next three the one under it.
            Assert.All(cards.Take(4), c => Assert.Equal(0, Slot(c).Top, 1));
            Assert.All(cards.Skip(4), c => Assert.Equal(Slot(cards[4]).Top, Slot(c).Top, 1));
            Assert.True(Slot(cards[4]).Top >= cards.Take(4).Max(c => Slot(c).Bottom) + 14 - 1);
            // Each card is as tall as its own content, not as its row.
            Assert.All(cards, c => Assert.Equal(c.DesiredSize.Height, Slot(c).Height, 1));
            Assert.True(cards.Take(4).Select(c => Math.Round(Slot(c).Height)).Distinct().Count() > 1);

            Lay(1340);
            Assert.Equal(2, mosaic.Columns);
            Assert.Equal(1340, Slot(civ).Width, 1);
            Assert.Equal(0, Slot(civ).Left, 1);
            Assert.Equal(Slot(cards[0]).Top, Slot(cards[1]).Top, 1);
            Assert.True(Slot(cards[2]).Top > Slot(cards[0]).Top);
        });
        Assert.Null(error);
    }

    /// <summary>
    /// Every place about one player keeps the room for his rank badge, filled or not, so the
    /// photos of a card line up; and nobody off the sample ladder is given a badge.
    /// </summary>
    [Fact]
    public void APlaceWithNoBadgeKeepsItsRoom()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            tab.ShowDemoElo("rankinghighlights");
            var wins = CardsOf(tab)[1];
            var slots = Walk(wins).OfType<Grid>().Where(g => Equals(g.Tag, MultiplayerTab.LeaderBadgeSlotTag)).ToList();
            Assert.Equal(RowsOf(wins).Count, slots.Count);
            Assert.Single(slots.Select(s => s.Width).Distinct());
            Assert.Contains(slots, s => s.Children.Count == 1);
            // The long-named sample player is on nobody's ladder: his slot is there and empty.
            Assert.Contains(slots, s => s.Children.Count == 0);
            // A switch is still the only thing in a card that is a button.
            Assert.Empty(Walk(wins).OfType<Button>());
        });
        Assert.Null(error);
    }

    /// <summary>The seven pictograms and the flame are drawings WPF can read.</summary>
    [Fact]
    public void EveryPictogramParses()
    {
        Assert.Equal(7, MultiplayerTab.LeaderIcons.Count);
        foreach (var kind in Enum.GetValues<HighlightCellKind>())
            Assert.False(Geometry.Parse(MultiplayerTab.LeaderIcons[kind]).Bounds.IsEmpty, kind.ToString());
        Assert.False(Geometry.Parse(MultiplayerTab.LeaderFlame).Bounds.IsEmpty);
    }

    /// <summary>Design 118e: the month selector is the mode capsule's twin, button for button.</summary>
    [Fact]
    public void TheMonthSelectorIsBuiltLikeTheModeCapsule()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            Assert.Same(tab.RankingModeHighlights.Style, tab.RankingMonthCurrent.Style);
            Assert.Same(tab.RankingModeHighlights.Style, tab.RankingMonthPrevious.Style);
            var mode = (Border)((FrameworkElement)tab.RankingModeSolo.Parent).Parent;
            Assert.Equal(mode.CornerRadius, tab.RankingMonthCapsule.CornerRadius);
            Assert.Equal(mode.Padding, tab.RankingMonthCapsule.Padding);
        });
        Assert.Null(error);
    }

    /// <summary>
    /// An older server answers 404 to <c>/stats/highlights</c>: the view SAYS so, and draws no
    /// cards — an empty page would read as a community with nothing to highlight.
    /// </summary>
    [Theory]
    [InlineData(404, "MpHlLeadersUnavailable")]
    [InlineData(500, "MpHlLeadersFailed")]
    [InlineData(null, "MpHlLeadersLoading")]
    public void WithNoAnswerTheViewSaysWhy(int? failure, string key)
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            typeof(MultiplayerTab).GetField("_rankingLeadersFailure", Private)!.SetValue(tab, failure);
            typeof(MultiplayerTab).GetMethod("RenderRankingHighlights", Private)!.Invoke(tab, null);

            Assert.Empty(CardsOf(tab));
            Assert.Contains(Strings.Get(key), TextOf(tab.RankingHighlightsBody));
            Assert.Equal(Visibility.Collapsed, tab.RankingMonthCapsule.Visibility);
        });
        Assert.Null(error);
    }
}
