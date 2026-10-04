using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
                    "BIGGEST CLIMB", "MOST WINS", "MOST MATCHES", "BEST STREAK",
                    "BEST WIN RATE", "BIGGEST UPSET", "CIVILIZATION OF THE MONTH",
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
            var own = Application.Current.FindResource("MpRankOwnRow");
            var rows = CardsOf(tab).SelectMany(RowsOf).ToList();
            var marked = rows.Where(r => ReferenceEquals(r.Background, own)).ToList();
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
