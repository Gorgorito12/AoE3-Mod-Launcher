using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The Clasificación with its rank badges and the TOP 5 honour block (docs/design_insignias_rango,
/// 45a + 43g). The block is the part that can go wrong silently: a wrapper that narrows its rows
/// by a pixel misaligns every column under the header, and nothing throws.
/// </summary>
public class RankingBadgesLayoutTests
{
    /// <summary>
    /// THE ONE THAT MATTERS. The header and every row — inside the TOP 5 block and outside it —
    /// start at the same X and have the same column widths. That is what RankingTableLayout
    /// exists for, and a block with side margins or a real border would break it.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheTop5BlockDoesNotMoveAColumn()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = RenderedRanking();
            var header = (Grid)((Border)tab.RankingHeaderHost.Children[0]).Child;
            var rows = RowGrids(tab.RankingBody).ToList();
            Assert.True(rows.Count >= 7);

            // The reference is the first row OUTSIDE the block: the header lives outside the
            // scroller, so a scrollbar (when there is one) narrows every row's star columns
            // alike — that predates the block. What the block must not do is make ITS rows
            // differ from the rest, or move any of them off the header's left edge.
            var outside = rows[MultiplayerTab.RankingTop5Count];
            var headerX = header.TranslatePoint(new Point(0, 0), tab.RankingHeaderHost).X;
            var specs = RankingTableLayout.For(StatsDemoData.Community().Leaderboard);
            foreach (var row in rows)
            {
                var x = row.TranslatePoint(new Point(0, 0), tab.RankingHeaderHost).X;
                Assert.Equal(headerX, x, 1);
                Assert.Equal(outside.ColumnDefinitions.Count, row.ColumnDefinitions.Count);
                for (var c = 0; c < outside.ColumnDefinitions.Count; c++)
                {
                    Assert.Equal(outside.ColumnDefinitions[c].ActualWidth, row.ColumnDefinitions[c].ActualWidth, 1);
                    if (specs[c].FixedWidth is not null)
                        Assert.Equal(header.ColumnDefinitions[c].ActualWidth, row.ColumnDefinitions[c].ActualWidth, 1);
                }
            }
        });
        Assert.Null(error);
    }

    /// <summary>The block holds exactly the server's first five rows, and only them.</summary>
    [Fact]
    public void TheTop5BlockHoldsTheFirstFiveRows()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = RenderedRanking();
            var block = tab.RankingBody.Children.OfType<Grid>().Single(g => Equals(g.Tag, "RankingTop5"));
            Assert.Same(block, tab.RankingBody.Children[0]);
            var inside = RowGrids(block).ToList();
            Assert.Equal(MultiplayerTab.RankingTop5Count, inside.Count);
            // Eighteen on the demo ladder, so the share-of-the-table bands give two Sovereigns
            // and three Imperials — the TOP 5 is exactly the two highest ages.
            Assert.Equal(
                new[] { RankAge.Sovereign, RankAge.Sovereign, RankAge.Imperial, RankAge.Imperial, RankAge.Imperial },
                inside.Select(r => (RankAge)BadgeOf(r).Tag));
        });
        Assert.Null(error);
    }

    /// <summary>
    /// The badge follows the PLACE, never the printed rating: 1643 in third wears third's age,
    /// above players on less. Only first place carries the accent bar.
    /// </summary>
    [Fact]
    public void EachRowWearsTheBadgeOfItsPlace()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = RenderedRanking();
            var rows = RowGrids(tab.RankingBody).ToList();
            for (var i = 0; i < rows.Count; i++)
            {
                Assert.Equal(RankAges.For(i + 1, rows.Count), (RankAge)BadgeOf(rows[i]).Tag);
                var accents = rows[i].Children.OfType<FrameworkElement>().Count(e => Equals(e.Tag, "RankFirstAccent"));
                Assert.Equal(i == 0 ? 1 : 0, accents);
            }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// 45e, the community strip's ranking card. Badges of two sizes sit in a slot of FIXED width,
    /// so the avatar and the name start at the same x on every row; and no row grows by carrying
    /// one — the strip's height is paid for out of the rooms list under it.
    /// </summary>
    [Fact]
    public void THE_STRIP_ONE_TheBadgeNeitherShiftsTheFaceNorGrowsTheRow()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            typeof(MultiplayerTab).GetField("_communityStats", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(tab, StatsDemoData.Community());
            var build = typeof(MultiplayerTab).GetMethod("BuildStripLeaderboardRow", BindingFlags.Instance | BindingFlags.NonPublic)!;

            var host = new StackPanel { Width = 300 };
            foreach (var row in StatsDemoData.Community().Leaderboard.Take(5))
                host.Children.Add((UIElement)build.Invoke(tab, new object[] { row, false })!);
            host.Measure(new Size(300, double.PositiveInfinity));
            host.Arrange(new Rect(0, 0, 300, host.DesiredSize.Height));
            host.UpdateLayout();

            double? avatarX = null;
            foreach (Border row in host.Children)
            {
                // 18-px content + 4 + 4 padding + 3 bottom margin: what the row measured before
                // the badge existed.
                Assert.True(row.ActualHeight <= 18 + 8 + 0.5,
                    $"A strip row grew to {row.ActualHeight:0.0} px by carrying its badge.");
                var grid = (Grid)row.Child;
                Assert.Equal(MultiplayerTab.StripRankSlotWidth, grid.ColumnDefinitions[0].ActualWidth, 1);
                var x = grid.ColumnDefinitions[0].ActualWidth;
                avatarX ??= x;
                Assert.Equal(avatarX.Value, x, 1);
                Assert.Single(Walk(grid).OfType<FrameworkElement>(), e => e.Tag is RankAge);
            }
            var first = (Grid)((Border)host.Children[0]).Child;
            Assert.Contains(first.Children.OfType<FrameworkElement>(), e => Equals(e.Tag, "RankFirstAccent"));
        });
        Assert.Null(error);
    }

    // ── helpers ──

    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var d in Walk(child)) yield return d;
    }

    private static MultiplayerTab RenderedRanking()
    {
        var tab = new MultiplayerTab();
        typeof(MultiplayerTab)
            .GetField("_communityStats", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(tab, StatsDemoData.Community());
        typeof(MultiplayerTab)
            .GetMethod("RenderRanking", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(tab, null);

        // The ranking's own container: measured directly, so it does not matter which
        // subtab happens to be showing.
        var host = (FrameworkElement)VisualTreeHelper.GetParent(tab.RankingHeaderHost)
                   ?? (FrameworkElement)tab.RankingHeaderHost.Parent;
        host.Measure(new Size(1100, 700));
        host.Arrange(new Rect(0, 0, 1100, 700));
        host.UpdateLayout();
        return tab;
    }

    /// <summary>The Grid of every leaderboard row under <paramref name="root"/>, in order.</summary>
    private static IEnumerable<Grid> RowGrids(Panel root)
    {
        foreach (UIElement child in root.Children)
        {
            if (child is Border { Child: Grid g } && g.Children.OfType<FrameworkElement>().Any(e => e.Tag is RankAge))
                yield return g;
            else if (child is Panel p)
                foreach (var inner in RowGrids(p)) yield return inner;
        }
    }

    private static FrameworkElement BadgeOf(Grid row)
        => row.Children.OfType<FrameworkElement>().Single(e => e.Tag is RankAge);
}
