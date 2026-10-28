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
/// The Clasificación rows: the player's picture beside every name, the PEAK (with the day it was
/// reached) where the server sends one and a dash where it does not, and the pinned "YOU" row drawn
/// on exactly the same columns as the list — which lives inside a ScrollViewer that keeps a gutter
/// and a bar the pinned row does not have.
/// </summary>
[Collection("wpf-and-language")]
public class RankingRowsTests
{
    /// <summary>
    /// THE ONE THAT MATTERS: with the list scrolled so the viewer's own row is out of sight, the
    /// pinned copy at the foot puts every column — the ELO and the peak — at the same X as the real
    /// row. It used to sit to the right by the ScrollViewer's gutter and bar.
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

    /// <summary>
    /// The PEAK cell is the figure with the day it was first reached on hover — and a dash, never an
    /// empty cell, for a row the server sent no peak for. The W-L and the % that stood here moved
    /// to the Profile (2026-10-28); a row must not print them any more.
    /// </summary>
    [Fact]
    public void ThePeakIsTheFigureWithItsDay_AndADashWhenTheServerSentNone()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            Strings.SetLanguage(Strings.LangEs);
            try
            {
                var tab = new MultiplayerTab();
                const string at = "2026-08-12T20:00:00.000Z";
                var row = new LeaderboardRow
                {
                    Rank = 4, UserId = "u4", DisplayName = "Nuevo", Rating = 1540, Rd = 120,
                    GamesPlayed = 10, Wins = 6, Losses = 4, RatedWins = 6, RatedLosses = 4,
                    RatingPeak = 1612.4, RatingPeakAt = at,
                };
                var built = (DependencyObject)tab.BuildLeaderboardRow(row, 1630, isMe: false, RankingTableLayout.All, 5, team: false);
                var cell = Walk(built).OfType<TextBlock>().Single(t => Equals(t.Tag, MultiplayerTab.RankingPeakTag));
                Assert.Equal(PlacementView.RatingText(1612.4), cell.Text);
                var tip = Assert.IsAssignableFrom<TextBlock>(cell.ToolTip);
                Assert.Equal("Máximo alcanzado el " + ShortDate.Format(ShortDate.Parse(at)!.Value), tip.Text);
                // The record is the Profile's now: no "6-4" and no "60" anywhere in the row.
                Assert.DoesNotContain(Walk(built).OfType<TextBlock>(), t => t.Text == "60" || t.Text == "6-4");

                // No peak from the server for this row: a dash, and nothing to explain on hover.
                row.RatingPeak = null;
                row.RatingPeakAt = null;
                built = (DependencyObject)tab.BuildLeaderboardRow(row, 1630, isMe: false, RankingTableLayout.All, 5, team: false);
                cell = Walk(built).OfType<TextBlock>().Single(t => Equals(t.Tag, MultiplayerTab.RankingPeakTag));
                Assert.Equal(Strings.Get("MpDash"), cell.Text);
                Assert.Null(cell.ToolTip);
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// A server that sends no peak gets the wide table without the column — and still the WIDE
    /// table: the ELO bar on a ranked row, "Posicionamiento 6/10" on a placement row, a dash with
    /// its reason nowhere because there is no peak cell at all.
    /// </summary>
    [Fact]
    public void WithoutPeaksTheRowsKeepTheirBarAndTheLongPlacementText()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            Strings.SetLanguage(Strings.LangEs);
            try
            {
                var tab = new MultiplayerTab();
                var specs = RankingTableLayout.WithoutPeak;
                var ranked = (DependencyObject)tab.BuildLeaderboardRow(new LeaderboardRow
                {
                    Rank = 1, UserId = "u1", DisplayName = "Ana", Rating = 1600, Rd = 80, GamesPlayed = 12,
                }, 1600, isMe: false, specs, 5, team: false);
                Assert.Contains(Walk(ranked).OfType<Border>(), b => Equals(b.Tag, "RankingBar"));
                Assert.DoesNotContain(Walk(ranked).OfType<TextBlock>(), t => Equals(t.Tag, MultiplayerTab.RankingPeakTag));

                var placing = (DependencyObject)tab.BuildPlacementRow(new PlacementRow
                {
                    UserId = "u2", DisplayName = "Beto", Rating = 1500, PlacementPlayed = 6, PlacementRequired = 10,
                }, isMe: false, specs, ownResults: null);
                var plain = Walk(placing).OfType<TextBlock>().Select(WarsOfLibertyLauncher.RevealText.PlainTextOf);
                Assert.Contains(plain, t => t.Contains(Strings.Format("MpPlacementProgress", 6, 10)));
                Assert.DoesNotContain(Walk(placing).OfType<TextBlock>(), t => Equals(t.Tag, MultiplayerTab.RankingPeakTag));

                // With the column, a placement row's peak is a dash that says when it comes.
                placing = (DependencyObject)tab.BuildPlacementRow(new PlacementRow
                {
                    UserId = "u2", DisplayName = "Beto", Rating = 1500, PlacementPlayed = 6, PlacementRequired = 10,
                }, isMe: false, RankingTableLayout.All, ownResults: null);
                var dash = Walk(placing).OfType<TextBlock>().Single(t => Equals(t.Tag, MultiplayerTab.RankingPeakTag));
                Assert.Equal(Strings.Get("MpDash"), dash.Text);
                Assert.Equal(Strings.Get("MpRankPeakPlacementTip"), Assert.IsAssignableFrom<TextBlock>(dash.ToolTip).Text);
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
