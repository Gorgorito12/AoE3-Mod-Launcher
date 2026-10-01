using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The TEAM badge (design handoff 51a): two shields of the same age. Built in code, so a brush
/// key that does not resolve throws only once a team room or the team ladder is on screen —
/// these build it at every size and every age.
/// </summary>
[Collection("wpf-and-language")]
public class RankBadgeTeamTests
{
    private static IEnumerable<FrameworkElement> Tree(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe) yield return fe;
            foreach (var d in Tree(child)) yield return d;
        }
    }

    private static FrameworkElement Built(RankAge age, double width)
    {
        var badge = RankBadge.BuildTeam(age, age == RankAge.Discovery ? null : "2", width, "user-1", "tip");
        badge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        badge.Arrange(new Rect(badge.DesiredSize));
        badge.UpdateLayout();
        return badge;
    }

    /// <summary>
    /// THE ONE THAT MATTERS: a team badge is still ONE badge to everybody looking for one — a
    /// single element tagged with an age, at the nominal width — and its footprint is the
    /// front's width plus the back shield's offset.
    /// </summary>
    [Theory]
    [InlineData(RankAge.Discovery)]
    [InlineData(RankAge.Colonial)]
    [InlineData(RankAge.Industrial)]
    [InlineData(RankAge.Sovereign)]
    public void THE_ONE_THAT_MATTERS_ATeamBadgeIsOneBadgeWithAWiderFootprint(RankAge age)
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            foreach (var w in new[] { 17.0, 19.0, 24.0, 34.0, 48.0 })
            {
                var badge = Built(age, w);
                Assert.Equal(new RankBadge.TeamBadgeTag(age), badge.Tag);
                Assert.Equal(RankBadge.FootprintWidth(w, BadgeKind.Team), badge.DesiredSize.Width, 1);

                var fronts = Tree(badge).Where(e => e.Tag is RankAge).ToList();
                var front = Assert.Single(fronts);
                Assert.Equal(age, front.Tag);
                Assert.Equal(w, front.Width, 1);
            }
        });
        Assert.Null(error);
    }

    /// <summary>The handoff's measurements: 31 x 28 around a 24-px front, 62 x 56 around 48.</summary>
    [Fact]
    public void TheHandoffsBoxes()
    {
        Assert.Equal(31, RankBadge.FootprintWidth(24, BadgeKind.Team), 0);
        Assert.Equal(62, RankBadge.FootprintWidth(48, BadgeKind.Team), 0);
        Assert.Equal(24, RankBadge.FootprintWidth(24, BadgeKind.Solo));

        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var badge = (Panel)Built(RankAge.Imperial, 24);
            Assert.Equal(28, badge.DesiredSize.Height, 0);
            var back = (FrameworkElement)badge.Children[0];
            var front = (FrameworkElement)badge.Children[1];
            Assert.Equal(21, back.Width, 1);
            Assert.Equal(24, back.Height, 1);
            Assert.Equal(3, back.Margin.Top, 1);
            Assert.Equal(7, front.Margin.Left, 0);
        });
        Assert.Null(error);
    }

    /// <summary>
    /// The back shield never animates — a team badge costs what a 1v1 badge does — and the
    /// light stays on the front.
    /// </summary>
    [Fact]
    public void OnlyTheFrontShieldCarriesLight()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            RankBadge.AnimationsOverride = true;
            try
            {
                var badge = (Panel)Built(RankAge.Colonial, 24);
                Assert.Equal(0, RankBadge.AnimationCount(badge.Children[0]));
                Assert.Equal(0, RankBadge.AnimationCount(badge));
                Assert.True(RankBadge.AnimationCount(badge.Children[1]) > 0);

                var newcomer = (Panel)Built(RankAge.Discovery, 24);
                Assert.Equal(0, RankBadge.AnimationCount(newcomer.Children[1]));
            }
            finally { RankBadge.AnimationsOverride = null; }
        });
        Assert.Null(error);
    }

    /// <summary>The back shield draws the ONE shared outline, never a copy of its own.</summary>
    [Fact]
    public void TheBackShieldReusesTheSharedGeometry()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var shared = Application.Current.FindResource("RankShieldGeometry");
            var back = (Panel)((Panel)Built(RankAge.Industrial, 24)).Children[0];
            var paths = back.Children.OfType<Path>().ToList();
            Assert.NotEmpty(paths);
            Assert.All(paths, p => Assert.Same(shared, p.Data));
        });
        Assert.Null(error);
    }

    /// <summary>
    /// Only the back layer clips (the cut-out between the shields). The root and the front never
    /// do — the front's light paints past its box — and nothing that holds text is faded.
    /// </summary>
    [Fact]
    public void OnlyTheBackLayerClipsOrFades()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var badge = (Panel)Built(RankAge.Sovereign, 24);
            var back = (FrameworkElement)badge.Children[0];
            var front = (FrameworkElement)badge.Children[1];

            Assert.NotNull(back.Clip);
            Assert.Null(badge.Clip);
            Assert.False(badge.ClipToBounds);
            Assert.Null(front.Clip);
            Assert.False(front.ClipToBounds);
            Assert.Equal(1.0, badge.Opacity);
            Assert.Equal(1.0, front.Opacity);
            Assert.Empty(Tree(back).OfType<TextBlock>());
        });
        Assert.Null(error);
    }

    /// <summary>BuildFor draws one shield for a 1v1 badge and two for a team one.</summary>
    [Fact]
    public void BuildForPicksTheShape()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var solo = RankBadge.BuildFor(new ShownBadge(BadgeKind.Solo, RankAge.Fortress, 5, null, 0), 24, "u");
            var team = RankBadge.BuildFor(new ShownBadge(BadgeKind.Team, RankAge.Fortress, 5, null, 0), 24, "u");
            Assert.Equal(RankAge.Fortress, solo.Tag);
            Assert.IsType<RankBadge.TeamBadgeTag>(team.Tag);
        });
        Assert.Null(error);
    }
}
