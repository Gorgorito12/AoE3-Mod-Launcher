using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>What design 98 promises about the Statistics page, on the real renderer.</summary>
[Collection("wpf-and-language")]
public class StatsPageTests
{
    private static IEnumerable<T> Under<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T found) yield return found;
            foreach (var deeper in Under<T>(child)) yield return deeper;
        }
    }

    private static string AllText(DependencyObject root)
        => string.Join(" | ", Under<TextBlock>(root).Select(RevealText.PlainTextOf));

    private static void WithPage(string scenario, Action<MultiplayerTab> check)
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            try
            {
                Strings.SetLanguage("es");
                var tab = new MultiplayerTab();
                tab.ShowDemoStats(scenario);
                check(tab);
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// The page takes the colour of the mod being looked at, and only the page: the override
    /// lives in <c>StatsView.Resources</c>, so the tabs above it stay the launcher's blue.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheModsColourStaysInsideThePage()
        => WithPage("full", tab =>
        {
            Assert.IsType<SolidColorBrush>(tab.StatsView.Resources["StatsAccentFill"]);
            Assert.True(tab.StatsView.Resources.Contains("MpAction"));
            Assert.False(tab.Resources.Contains("MpAction"));
        });

    /// <summary>
    /// The server sends up to 600 pairs and the page drew every one, ~25,000 px for one card.
    /// Now it draws the eight most played; the rest open in a box that never grows past ten rows,
    /// and the civilization picker narrows the table to one civilization's pairs.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_SixHundredPairsDrawEightAndTheRestInABox()
        => WithPage("big", tab =>
        {
            int total = MatchupListView.Merge(StatsDemoData.BigMatchups().Matchups).Count;
            Assert.True(total > MatchupListView.BoxRows);
            string text = AllText(tab.StatsLeftColumn);
            Assert.Contains(Strings.Format("MpStatsPairsTopShown", MatchupListView.TopCount, total), text);
            Assert.DoesNotContain(Under<ScrollViewer>(tab.StatsLeftColumn),
                s => System.Windows.Automation.AutomationProperties.GetAutomationId(s) == "StatsPairsBox");

            Under<Button>(tab.StatsLeftColumn)
                .First(b => Equals(b.Content, Strings.Format("MpStatsPairsSeeAll", total)))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            var box = Under<ScrollViewer>(tab.StatsLeftColumn)
                .Single(s => System.Windows.Automation.AutomationProperties.GetAutomationId(s) == "StatsPairsBox");
            Assert.True(box.MaxHeight <= MatchupListView.BoxRows * 45);

            var picker = Under<ComboBox>(tab.StatsLeftColumn)
                .First(c => System.Windows.Automation.AutomationProperties.GetAutomationId(c) == "StatsPairsCiv");
            picker.SelectedIndex = 1;
            Assert.DoesNotContain(Under<Button>(tab.StatsLeftColumn),
                b => Equals(b.Content, Strings.Get("MpStatsSeeLess")));
        });

    /// <summary>
    /// The page's scroll bar sat in the last 6 px of the window, which Windows keeps for
    /// resizing it, so its thumb could not be dragged. It has to stand clear of the edge.
    /// </summary>
    [Fact]
    public void TheStatisticsScrollBarStandsClearOfTheWindowsEdge()
        => WithPage("big", tab =>
        {
            tab.Measure(new Size(1200, 800));
            tab.Arrange(new Rect(0, 0, 1200, 800));
            tab.UpdateLayout();
            var right = tab.StatsScroll.TranslatePoint(new Point(tab.StatsScroll.ActualWidth, 0), tab).X;
            Assert.True(tab.ActualWidth - right >= 8, $"the bar ends {tab.ActualWidth - right} px from the edge");
        });

    /// <summary>
    /// Going back to the subtab rebuilt every card each time (974 ms in a player's log). With
    /// nothing moved it now draws nothing; the language is one of the things that does move it.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_GoingBackToStatisticsDrawsNothingWhenNothingMoved()
        => WithPage("full", tab =>
        {
            var first = tab.StatsLeftColumn.Children[0];
            Assert.False(tab.RenderStatsTabIfChanged());
            Assert.Same(first, tab.StatsLeftColumn.Children[0]);

            Strings.SetLanguage("en");
            Assert.True(tab.RenderStatsTabIfChanged());
            Assert.NotSame(first, tab.StatsLeftColumn.Children[0]);
        });

    /// <summary>The civilizations with three matches or fewer fold, and the fold opens.</summary>
    [Fact]
    public void TheThinCivilizationsFoldAndTheFoldOpens()
        => WithPage("full", tab =>
        {
            Assert.DoesNotContain("Haitians |", AllText(tab.StatsLeftColumn).Replace("Haitians | 4", ""));
            var fold = Under<Button>(tab.StatsLeftColumn)
                .First(b => Equals(b.Content, Strings.Get("MpStatsSeeAllCivs")));
            string before = AllText(tab.StatsLeftColumn);

            fold.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            string after = AllText(tab.StatsLeftColumn);
            Assert.NotEqual(before, after);
            Assert.Contains(Under<Button>(tab.StatsLeftColumn),
                b => Equals(b.Content, Strings.Get("MpStatsSeeLess")));
        });

    /// <summary>
    /// 98d: nothing is ranked yet, so there is no pair to draw and the balance says why. A card
    /// with an empty table would read as a fault.
    /// </summary>
    [Fact]
    public void WithNoCivilizationsTheBalanceExplainsItself()
        => WithPage("empty", tab =>
        {
            string text = AllText(tab.StatsLeftColumn);
            Assert.Contains(Strings.Get("MpStatsCivsNoData"), text);
            Assert.DoesNotContain(Strings.Get("MpStatsMatchupsScope"), text);
        });

    /// <summary>98e and 98f: neither state may claim a figure it does not have.</summary>
    [Fact]
    public void LoadingShowsNoFigureAndAFailureOffersARetry()
    {
        WithPage("loading", tab =>
        {
            Assert.Equal(Strings.Get("MpStatsLoadingShort"), tab.StatsCountsText.Text);
            Assert.Equal(Visibility.Collapsed, tab.StatsErrorBox.Visibility);
            Assert.NotEmpty(tab.StatsLeftColumn.Children);
        });

        WithPage("error", tab =>
        {
            Assert.Equal(Visibility.Visible, tab.StatsErrorBox.Visibility);
            Assert.Equal(Visibility.Collapsed, tab.StatsColumns.Visibility);

            tab.StatsRetryButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(Visibility.Collapsed, tab.StatsErrorBox.Visibility);
            Assert.Equal(Visibility.Visible, tab.StatsColumns.Visibility);
        });
    }

    /// <summary>
    /// The heading counts RATED matches. The total carries no rated predicate, and the card of
    /// the two figures side by side only reads if the first really is the rated one.
    /// </summary>
    [Fact]
    public void TheHeadCountsTheRatedMatchesAndNamesTheMod()
        => WithPage("full", tab =>
        {
            Assert.Contains("231", tab.StatsCountsText.Text);
            Assert.DoesNotContain("285", tab.StatsCountsText.Text);
            Assert.Contains(Strings.Get("MpStatsModeSolo"), tab.StatsCountsText.Text);
        });
}
