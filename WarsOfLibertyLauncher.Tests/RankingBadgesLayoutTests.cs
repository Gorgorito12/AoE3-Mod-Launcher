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
/// The Clasificación with its rank badges (docs/design_insignias_rango 45a, and rating v3's design
/// handoff 55a). What can go wrong silently is a row that does not line up with the header: a
/// wrapper that narrows its rows by a pixel misaligns every column, and nothing throws.
/// </summary>
// Serialised with the other WPF tests: RankBadge.AnimationsOverride is a STATIC, and tests
// that set it true and false in parallel read each other's value.
[Collection("wpf-and-language")]
public class RankingBadgesLayoutTests
{
    /// <summary>
    /// THE ONE THAT MATTERS. The header and every row — ranked and placement — start at the same X
    /// and have the same fixed column widths. That is what RankingTableLayout exists for.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_EveryRowLinesUpWithTheHeader()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = RenderedRanking();
            var header = (Grid)((Border)tab.RankingHeaderHost.Children[0]).Child;
            var rows = RowGrids(tab.RankingBody).ToList();
            Assert.True(rows.Count >= 12);

            var headerX = header.TranslatePoint(new Point(0, 0), tab.RankingHeaderHost).X;
            var specs = RankingTableLayout.All;
            foreach (var row in rows)
            {
                Assert.Equal(headerX, row.TranslatePoint(new Point(0, 0), tab.RankingHeaderHost).X, 1);
                // Every column of the table and a gap column between each two (design 59's gap).
                Assert.Equal(2 * specs.Count - 1, row.ColumnDefinitions.Count);
                Assert.Equal(header.ColumnDefinitions.Count, row.ColumnDefinitions.Count);
                // EVERY column, the flexible ones and the gaps included, and the right edge with
                // them. Comparing only the fixed widths and the left edge passed over a header that
                // sat 8 px to the right of its figures: the rows live inside the ScrollViewer,
                // which keeps a gutter (and its bar) the header outside it does not.
                for (var c = 0; c < row.ColumnDefinitions.Count; c++)
                    Assert.Equal(header.ColumnDefinitions[c].ActualWidth, row.ColumnDefinitions[c].ActualWidth, 1);
                Assert.Equal(header.ActualWidth, row.ActualWidth, 1);
            }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// 55a: the placement rows come AFTER every ranked row, in the same table, with no badge and no
    /// place — and the ranked ones in the server's order.
    /// </summary>
    [Fact]
    public void PlacementRowsFollowTheRankedOnes_WithNoBadge()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = RenderedRanking();
            var tags = tab.RankingBody.Children.OfType<Border>().Select(b => b.Tag).ToList();
            var lastRanked = tags.FindLastIndex(t => t is Models.Multiplayer.LeaderboardRow);
            var firstPlacing = tags.FindIndex(t => t is Models.Multiplayer.PlacementRow);
            Assert.True(lastRanked >= 0 && firstPlacing > lastRanked);
            Assert.Equal(
                StatsDemoData.Community().Leaderboard.Select(r => r.Rank),
                tags.OfType<Models.Multiplayer.LeaderboardRow>().Select(r => r.Rank));
            foreach (var b in tab.RankingBody.Children.OfType<Border>().Where(b => b.Tag is Models.Multiplayer.PlacementRow))
                Assert.Null(BadgeOf((Grid)b.Child));
        });
        Assert.Null(error);
    }

    /// <summary>
    /// The badge follows the PLACE, cut by the size of the table. Rating v3 draws the rows plain,
    /// as the handoff does: no age banner behind the full table (the strip keeps its own).
    /// </summary>
    [Fact]
    public void EachRankedRowWearsTheBadgeOfItsPlace()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = RenderedRanking();
            var rows = tab.RankingBody.Children.OfType<Border>()
                .Where(b => b.Tag is Models.Multiplayer.LeaderboardRow).ToList();
            for (var i = 0; i < rows.Count; i++)
            {
                Assert.Equal(RankAges.For(i + 1, rows.Count), (RankAge)BadgeOf((Grid)rows[i].Child)!.Tag);
                Assert.DoesNotContain(Walk(rows[i]).OfType<FrameworkElement>(), e => Equals(e.Tag, "RankBanner"));
            }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// 45e/47a/61, the community block's ranking card. Every row is the same fixed height with its
    /// banner; badges of two sizes sit in a slot of FIXED width, so the face and the name start at
    /// the same x on every row; the player's face is back (61 had dropped it; the maintainer asked
    /// for it as in the full table) at a size that never makes the row taller; and nothing on the
    /// way up from a badge clips — only the Sovereign's light does, and only itself. The banner's
    /// edge is the age's own glow colour, never the white bar.
    /// </summary>
    [Fact]
    public void THE_STRIP_ONE_BannerRowsKeepTheirHeightTheirFaceAndTheirHalo()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            RankBadge.AnimationsOverride = true;
            try
            {
                var tab = new MultiplayerTab();
                var stats = StatsDemoData.Community();
                typeof(MultiplayerTab).GetField("_communityStats", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(tab, stats);
                var build = typeof(MultiplayerTab).GetMethod("BuildStripLeaderboardRow", BindingFlags.Instance | BindingFlags.NonPublic)!;

                var host = new StackPanel { Width = 300 };
                foreach (var row in stats.Leaderboard.Take(5))
                    host.Children.Add((UIElement)build.Invoke(tab, new object[] { row, false })!);
                host.Measure(new Size(300, double.PositiveInfinity));
                host.Arrange(new Rect(0, 0, 300, host.DesiredSize.Height));
                host.UpdateLayout();

                double? avatarX = null;
                var avatarSize = RoomsActivityLayout.Fluid(0).RankAvatarSize;
                var n = CommunityStatsView.RankedPlayers(stats, team: false);
                for (var i = 0; i < host.Children.Count; i++)
                {
                    var layers = (Grid)host.Children[i];
                    Assert.Equal(MultiplayerTab.StripRowHeight, layers.ActualHeight, 1);

                    var content = layers.Children.OfType<Grid>().Last();
                    Assert.Equal(4, content.ColumnDefinitions.Count);   // rank, face, name, rating
                    Assert.Equal(MultiplayerTab.StripRankSlotWidth, content.ColumnDefinitions[0].ActualWidth, 1);

                    // One face per row, the table's own (same Tag), at the page's size, and starting
                    // at the same x on every row whatever badge sits before it.
                    var face = Assert.Single(Walk(content).OfType<FrameworkElement>(),
                        e => Equals(e.Tag, MultiplayerTab.RankingAvatarTag));
                    Assert.Equal(1, Grid.GetColumn(face));
                    Assert.Equal(avatarSize, face.ActualWidth, 1);
                    Assert.Equal(avatarSize, face.ActualHeight, 1);
                    var faceX = face.TranslatePoint(new Point(0, 0), content).X;
                    avatarX ??= faceX;
                    Assert.Equal(avatarX.Value, faceX, 1);

                    var age = RankAges.For(i + 1, n);
                    var badge = Assert.Single(Walk(content).OfType<FrameworkElement>(), e => e.Tag is RankAge);
                    Assert.Equal(age, badge.Tag);

                    // Nothing between the badge and the row clips.
                    for (DependencyObject? d = badge; d != null && !ReferenceEquals(d, host); d = LogicalTreeHelper.GetParent(d))
                        Assert.False(d is UIElement { ClipToBounds: true }, $"{d.GetType().Name} clips the badge.");

                    var all = Walk(layers).OfType<FrameworkElement>().ToList();
                    Assert.DoesNotContain(all, e => Equals(e.Tag, "RankFirstAccent"));
                    var edge = (System.Windows.Shapes.Rectangle)all.Single(e => Equals(e.Tag, "RankBannerEdge"));
                    var glow = ((SolidColorBrush)Application.Current.FindResource($"RankGlow{age}")).Color;
                    Assert.Equal(glow, ((SolidColorBrush)edge.Fill).Color);

                    var lights = all.Where(e => Equals(e.Tag, "RankBannerLight")).ToList();
                    Assert.Equal(age == RankAge.Sovereign ? 1 : 0, lights.Count);
                    foreach (var light in lights) Assert.True(light.ClipToBounds);
                    // The light is the ONLY layer allowed to clip.
                    Assert.Equal(lights.Count, all.Count(e => e.ClipToBounds));
                }
            }
            finally { RankBadge.AnimationsOverride = null; }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// The Sovereign's light fades out WITH its banner: the light layer carries an opacity mask
    /// that is fully transparent by the time the colour is, so it never reaches the banner's end
    /// and cannot stop in a hard vertical line (reported on screen as "a long rectangle"). The
    /// stripe itself is a bell, not a three-stop triangle.
    /// </summary>
    [Fact]
    public void TheLightFadesOutWithItsBannerAndHasNoHardEdge()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            RankBadge.AnimationsOverride = true;
            try
            {
                var banner = RankBadge.BuildRowBanner(RankAge.Sovereign);
                var light = (Border)Walk(banner).OfType<FrameworkElement>().Single(e => Equals(e.Tag, "RankBannerLight"));
                var mask = Assert.IsType<LinearGradientBrush>(light.OpacityMask);
                var last = mask.GradientStops.OrderBy(g => g.Offset).Last();
                Assert.Equal(0, last.Color.A);
                Assert.True(last.Offset <= RankBadge.BannerFadeEnd);

                var stripe = (System.Windows.Shapes.Rectangle)light.Child;
                Assert.True(((LinearGradientBrush)stripe.Fill).GradientStops.Count > 4);
            }
            finally { RankBadge.AnimationsOverride = null; }
        });
        Assert.Null(error);
    }

    /// <summary>With the system's animations off, the Sovereign's banner keeps its colour and
    /// carries no light at all.</summary>
    [Fact]
    public void WithAnimationsOffTheBannerHasNoLight()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            RankBadge.AnimationsOverride = false;
            try
            {
                var banner = RankBadge.BuildRowBanner(RankAge.Sovereign);
                var all = Walk(banner).OfType<FrameworkElement>().ToList();
                Assert.Contains(all, e => Equals(e.Tag, "RankBannerFill"));
                Assert.DoesNotContain(all, e => Equals(e.Tag, "RankBannerLight"));
                Assert.Empty(Walk(RankBadge.BuildRowBanner(RankAge.Discovery)).OfType<Border>());
            }
            finally { RankBadge.AnimationsOverride = null; }
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

    /// <summary>The Grid of every ranked and placement row under <paramref name="root"/>, in order.</summary>
    private static IEnumerable<Grid> RowGrids(Panel root)
    {
        foreach (UIElement child in root.Children)
            if (child is Border { Child: Grid g } b
                && b.Tag is Models.Multiplayer.LeaderboardRow or Models.Multiplayer.PlacementRow)
                yield return g;
    }

    private static FrameworkElement? BadgeOf(Grid row)
        => Walk(row).OfType<FrameworkElement>().FirstOrDefault(e => e.Tag is RankAge);
}
