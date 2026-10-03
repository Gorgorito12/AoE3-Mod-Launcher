using System.Collections.Generic;
using System.Linq;
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
/// The Clasificación rows: the player's picture beside every name, a dash (with the reason) where
/// a win rate would rest on too few matches, and the pinned "YOU" row drawn on exactly the same
/// columns as the list — which lives inside a ScrollViewer that keeps a gutter and a bar the pinned
/// row does not have.
/// </summary>
[Collection("wpf-and-language")]
public class RankingRowsTests
{
    /// <summary>
    /// THE ONE THAT MATTERS: with the list scrolled so the viewer's own row is out of sight, the
    /// pinned copy at the foot puts every column — ELO, W-L, % — at the same X as the real row.
    /// It used to sit to the right by the ScrollViewer's gutter and bar.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_ThePinnedRowUsesTheListsColumns()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            tab.ShowDemoElo("ranking");

            var host = (FrameworkElement)(VisualTreeHelper.GetParent(tab.RankingHeaderHost)
                                          ?? tab.RankingHeaderHost.Parent);
            void Layout()
            {
                host.Measure(new Size(1100, 420));
                host.Arrange(new Rect(0, 0, 1100, 420));
                host.UpdateLayout();
            }
            Layout();
            tab.RankingRowsScroll.ScrollToEnd();
            Layout();
            Layout();

            Assert.Equal(Visibility.Visible, tab.RankingPinnedRow.Visibility);

            var own = tab.RankingBody.Children.OfType<Border>()
                .Single(b => b.Tag is LeaderboardRow r && r.UserId == EloDemoData.ViewerId);
            var listRow = (Grid)own.Child;
            var pinnedRow = (Grid)((Border)((Border)tab.RankingPinnedRow.Children[0]).Child).Child;

            Assert.Equal(listRow.ColumnDefinitions.Count, pinnedRow.ColumnDefinitions.Count);
            double listOffset = 0, pinnedOffset = 0;
            for (var c = 0; c < listRow.ColumnDefinitions.Count; c++)
            {
                var listX = listRow.TranslatePoint(new Point(listOffset, 0), host).X;
                var pinnedX = pinnedRow.TranslatePoint(new Point(pinnedOffset, 0), host).X;
                Assert.Equal(listX, pinnedX, 0.5);
                Assert.Equal(listRow.ColumnDefinitions[c].ActualWidth, pinnedRow.ColumnDefinitions[c].ActualWidth, 0.5);
                listOffset += listRow.ColumnDefinitions[c].ActualWidth;
                pinnedOffset += pinnedRow.ColumnDefinitions[c].ActualWidth;
            }
        });
        Assert.Null(error);
    }

    /// <summary>Too few decided matches for a rate: "—", never an empty cell, with the bar on hover.</summary>
    [Fact]
    public void AHiddenPercentageIsADashThatSaysWhy()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            Strings.SetLanguage(Strings.LangEs);
            try
            {
                var tab = new MultiplayerTab();
                var row = new LeaderboardRow
                {
                    Rank = 4, UserId = "u4", DisplayName = "Nuevo", Rating = 1540, Rd = 120,
                    GamesPlayed = 2, Wins = 2, Losses = 0, RatedWins = 2, RatedLosses = 0,
                };
                var built = (DependencyObject)tab.BuildLeaderboardRow(row, 1630, isMe: false, RankingTableLayout.All, 5, team: false);
                var cell = Walk(built).OfType<TextBlock>()
                    .Single(t => Equals(t.Tag, MultiplayerTab.RankingPercentHiddenTag));
                Assert.Equal(Strings.Get("MpDash"), cell.Text);
                var tip = Assert.IsAssignableFrom<TextBlock>(cell.ToolTip);
                Assert.Equal($"Se muestra a partir de {PlayerStanding.MinDecidedForPercent} partidas", tip.Text);

                // Enough matches: the figure, and no dash.
                row.Wins = 6;
                row.RatedWins = 6;
                row.Losses = 4;
                row.RatedLosses = 4;
                built = (DependencyObject)tab.BuildLeaderboardRow(row, 1630, isMe: false, RankingTableLayout.All, 5, team: false);
                Assert.DoesNotContain(Walk(built).OfType<TextBlock>(), t => Equals(t.Tag, MultiplayerTab.RankingPercentHiddenTag));
                Assert.Contains(Walk(built).OfType<TextBlock>(), t => t.Text == "60");
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    /// <summary>Every name, ranked or placing, carries the player's picture.</summary>
    [Fact]
    public void EveryNameCarriesThePlayersAvatar()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            tab.ShowDemoElo("ranking");
            var rows = tab.RankingBody.Children.OfType<Border>()
                .Where(b => b.Tag is LeaderboardRow or PlacementRow).ToList();
            Assert.Contains(rows, b => b.Tag is LeaderboardRow);
            Assert.Contains(rows, b => b.Tag is PlacementRow);
            foreach (var row in rows)
                Assert.Single(Walk(row).OfType<FrameworkElement>(), e => Equals(e.Tag, MultiplayerTab.RankingAvatarTag));
        });
        Assert.Null(error);
    }

    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var d in Walk(child)) yield return d;
    }
}
