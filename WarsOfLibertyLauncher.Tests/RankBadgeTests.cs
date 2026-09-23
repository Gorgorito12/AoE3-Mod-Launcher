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
/// The rank badge is built in code, so nothing about it is checked at compile time: a brush key
/// that does not resolve throws at BUILD time — and it is only built once a ranking, a room or a
/// roster is on screen. These build every age at every size the three screens use.
/// </summary>
public class RankBadgeTests
{
    [Theory]
    [InlineData(RankAge.Discovery)]
    [InlineData(RankAge.Colonial)]
    [InlineData(RankAge.Fortress)]
    [InlineData(RankAge.Industrial)]
    [InlineData(RankAge.Imperial)]
    [InlineData(RankAge.Sovereign)]
    public void EveryAgeBuildsAtEverySizeTheScreensUse(RankAge age)
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            foreach (var size in new[] { 17.0, 24.0, 28.0 })
            {
                var badge = RankBadge.Build(age, age == RankAge.Discovery ? null : "14", size, "user-1", "tip");
                badge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                badge.Arrange(new Rect(badge.DesiredSize));

                // It takes exactly its nominal room: the edge paints PAST the box, it does not grow it.
                Assert.Equal(size, badge.DesiredSize.Width, 1);
                Assert.Equal(age, badge.Tag);
            }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// The handoff: one shield, declared once, shared by every layer. A layer carrying its own
    /// copy of the outline is how a 1-px seam appears along the edge.
    /// </summary>
    [Fact]
    public void EveryLayerDrawsTheOneSharedShield()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var shared = Application.Current.FindResource("RankShieldGeometry");
            var upper = (CombinedGeometry)Application.Current.FindResource("RankShieldFacetUpper");
            var lower = (CombinedGeometry)Application.Current.FindResource("RankShieldFacetLower");
            // The facets are that same shield cut in two, not outlines of their own.
            Assert.Same(shared, upper.Geometry1);
            Assert.Same(shared, lower.Geometry1);

            var badge = (Panel)RankBadge.Build(RankAge.Sovereign, "1", 28, "user-1");
            var paths = badge.Children.OfType<Path>().ToList();
            Assert.NotEmpty(paths);
            Assert.All(paths, p => Assert.True(
                ReferenceEquals(shared, p.Data) || ReferenceEquals(upper, p.Data) || ReferenceEquals(lower, p.Data),
                "a layer drew an outline that is not the shared shield"));
        });
        Assert.Null(error);
    }

    /// <summary>
    /// The halo reaches past the shield on the higher ages, so nothing may clip the badge.
    /// </summary>
    [Fact]
    public void NothingClipsTheBadge()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var badge = (Panel)RankBadge.Build(RankAge.Sovereign, "1", 28, "user-1");
            Assert.False(badge.ClipToBounds);
            Assert.Null(badge.Clip);
        });
        Assert.Null(error);
    }

    /// <summary>
    /// The numeral is monospaced (aligned figures), never Georgia/Cambria — the ranking's old
    /// position column used the display serif, whose old-style figures dance in height.
    /// </summary>
    [Fact]
    public void TheNumeralIsMonospaced()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var mono = (FontFamily)Application.Current.FindResource("MonoFont");
            var badge = (Panel)RankBadge.Build(RankAge.Industrial, "4", 24, "user-1");
            var numerals = badge.Children.OfType<TextBlock>().ToList();
            Assert.NotEmpty(numerals);
            Assert.All(numerals, t => Assert.Same(mono, t.FontFamily));
            Assert.All(numerals, t => Assert.Equal("4", t.Text));
        });
        Assert.Null(error);
    }

    /// <summary>Discovery carries no light and no colour: no edge, no glow.</summary>
    [Fact]
    public void DiscoveryHasNoLight()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var badge = (Panel)RankBadge.Build(RankAge.Discovery, null, 24, "user-1");
            Assert.Empty(badge.Children.OfType<TextBlock>());
            Assert.DoesNotContain(badge.Children.OfType<UIElement>(), e => e.Effect != null);
            // Plate + flat veil; no edge outside it.
            Assert.All(badge.Children.OfType<Path>(), p => Assert.True(p.Margin.Left >= 0));
        });
        Assert.Null(error);
    }

    /// <summary>
    /// The light is built but does not run until the badge is on screen — a page being built,
    /// or a test, must not start forty clocks nobody can see. Start/Stop are idempotent.
    /// </summary>
    [Fact]
    public void TheLightRunsOnlyWhileShown()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            RankBadge.AnimationsOverride = true;
            try
            {
                var badge = RankBadge.Build(RankAge.Sovereign, "1", 28, "user-1");
                Assert.True(RankBadge.AnimationCount(badge) > 0);
                Assert.False(RankBadge.IsRunning(badge));

                RankBadge.Start(badge);
                RankBadge.Start(badge);
                Assert.True(RankBadge.IsRunning(badge));
                RankBadge.Stop(badge);
                Assert.False(RankBadge.IsRunning(badge));
            }
            finally { RankBadge.AnimationsOverride = null; }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// With Windows' animations switched off the badge keeps its colours and stays still: no
    /// light layers at all, exactly the static badge.
    /// </summary>
    [Theory]
    [InlineData(RankAge.Colonial)]
    [InlineData(RankAge.Industrial)]
    [InlineData(RankAge.Sovereign)]
    public void WithAnimationsOffTheBadgeIsStill(RankAge age)
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            RankBadge.AnimationsOverride = false;
            try
            {
                var still = (Panel)RankBadge.Build(age, "3", 24, "user-1");
                Assert.Equal(0, RankBadge.AnimationCount(still));
                Assert.Empty(still.Children.OfType<Canvas>());
            }
            finally { RankBadge.AnimationsOverride = null; }
        });
        Assert.Null(error);
    }

    /// <summary>Discovery carries no light even with animations on.</summary>
    [Fact]
    public void DiscoveryHasNoAnimationsEvenWhenTheyAreOn()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            RankBadge.AnimationsOverride = true;
            try
            {
                Assert.Equal(0, RankBadge.AnimationCount(RankBadge.Build(RankAge.Discovery, null, 24, "user-1")));
                Assert.True(RankBadge.AnimationCount(RankBadge.Build(RankAge.Colonial, "9", 24, "user-1")) > 0);
            }
            finally { RankBadge.AnimationsOverride = null; }
        });
        Assert.Null(error);
    }
}
