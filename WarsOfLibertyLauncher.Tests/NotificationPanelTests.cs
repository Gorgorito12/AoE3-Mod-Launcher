using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The bell's panel (design 120): as tall as its notices, the launcher's notices with the
/// launcher's icon, and every mark drawn rather than typed.
/// </summary>
[Collection("wpf-and-language")]
public class NotificationPanelTests
{
    private static NotificationItem Item(NotificationKind kind, int n, bool read = false, string? target = null) => new()
    {
        Kind = kind,
        ModId = "wol",
        Title = "Notice number " + n,
        Body = "A body long enough to say something about notice number " + n + ".",
        CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5 * n),
        Read = read,
        TargetId = target,
    };

    private static List<NotificationItem> Notices(int count) =>
        Enumerable.Range(1, count).Select(n => Item(NotificationKind.UpdateAvailable, n)).ToList();

    private static IEnumerable<DependencyObject> Tree(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var below in Tree(child))
                yield return below;
    }

    private static List<FrameworkElement> Tagged(DependencyObject root, string tag) =>
        Tree(root).OfType<FrameworkElement>().Where(e => Equals(e.Tag, tag)).ToList();

    /// <summary>The panel laid out as the window lays it: top-aligned, in a host of this height.</summary>
    private static double Laid(NotificationPanel panel, double hostHeight)
    {
        panel.VerticalAlignment = VerticalAlignment.Top;
        panel.MaxHeight = NotificationPlacement.MaxHeight(hostHeight);
        if (panel.Parent is Grid old) old.Children.Remove(panel);
        var host = new Grid { Width = 900, Height = hostHeight };
        host.Children.Add(panel);
        host.Measure(new Size(900, hostHeight));
        host.Arrange(new Rect(0, 0, 900, hostHeight));
        host.UpdateLayout();
        return panel.ActualHeight;
    }

    /// <summary>
    /// THE ONE THAT MATTERS: the panel used to stretch to the bottom of the window with two
    /// notices in it. It is as tall as what it holds; only when the notices do not fit does it
    /// stop at the cap, and then the list scrolls and nothing else moves.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_ThePanelIsAsTallAsItsNotices()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();

            var two = new NotificationPanel();
            two.Show(Notices(2));
            double twoHigh = Laid(two, 900);
            Assert.InRange(twoHigh, 200, 500);
            Assert.Equal(0, two.ListForTests.ScrollableHeight, 1);

            var none = new NotificationPanel();
            none.Show(new List<NotificationItem>());
            double noneHigh = Laid(none, 900);
            Assert.True(noneHigh < twoHigh, $"empty {noneHigh} is not shorter than two notices {twoHigh}");
            Assert.Single(Tagged(none, NotificationPanel.EmptyTag));

            var many = new NotificationPanel();
            many.Show(Notices(12));
            double manyHigh = Laid(many, 600);
            Assert.Equal(NotificationPlacement.MaxHeight(600), manyHigh, 1);
            Assert.True(many.ListForTests.ScrollableHeight > 50, "twelve notices under a cap did not scroll");

            // The same twelve with room for them all: taller than the cap was, and no scrolling.
            double roomy = Laid(many, 4000);
            Assert.True(roomy > manyHigh);
            Assert.Equal(0, many.ListForTests.ScrollableHeight, 1);
        });
        Assert.Null(error);
    }

    /// <summary>The launcher's notices wear the icon the window hands them, with the mark on its
    /// corner; a notice with no icon keeps the disc.</summary>
    [Fact]
    public void ALauncherNoticeWearsTheLaunchersIcon()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();
            var launcherIcon = new SolidColorBrush(Colors.Teal);
            var panel = new NotificationPanel
            {
                IconFor = item => NotificationView.IsAboutTheLauncher(item.Kind) ? launcherIcon : null,
            };
            panel.Show(new List<NotificationItem>
            {
                Item(NotificationKind.LauncherUpdate, 1),
                Item(NotificationKind.Announcement, 2),
                Item(NotificationKind.Connectivity, 3, target: "offline"),
                Item(NotificationKind.MonthlyHighlights, 4),
            });

            var tiles = Tagged(panel, NotificationPanel.IconTileTag);
            Assert.Equal(2, tiles.Count);
            Assert.All(tiles, t => Assert.Same(launcherIcon, ((Border)t).Background));
            // One mark per notice: on the icon's corner for two, the disc itself for the others.
            Assert.Equal(4, Tagged(panel, NotificationPanel.MarkTag).Count);
        });
        Assert.Null(error);
    }

    /// <summary>A mark typed as a character (an arrow, a star, a gear) is drawn by whichever symbol
    /// font Windows picks and never sat in the middle of its disc. Every one is a path.</summary>
    [Fact]
    public void NoMarkAndNoGearIsACharacter()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();
            var panel = new NotificationPanel { IconFor = item => item.Kind == NotificationKind.NewMod ? Brushes.Teal : null };
            var kinds = Enum.GetValues<NotificationKind>().Where(NotificationView.BelongsInTheBell).ToList();
            panel.Show(kinds.Select((k, i) => Item(k, i + 1, target: k == NotificationKind.Connectivity ? "offline" : null)).ToList());

            var marks = Tagged(panel, NotificationPanel.MarkTag);
            Assert.Equal(kinds.Count, marks.Count);
            foreach (var mark in marks)
            {
                Assert.Empty(Tree(mark).OfType<TextBlock>());
                Assert.Single(Tree(mark).OfType<Path>());
            }

            var drawn = Assert.IsType<Viewbox>(panel.GearForTests.Content);
            Assert.Single(Tree(drawn).OfType<Path>());
        });
        Assert.Null(error);
    }

    [Fact]
    public void EveryPictogramParses()
    {
        Assert.All(NotificationPanel.AllPaths, data =>
        {
            var bounds = Geometry.Parse(data).Bounds;
            Assert.False(bounds.IsEmpty);
            Assert.InRange(bounds.Right, 1, 20);
            Assert.InRange(bounds.Bottom, 1, 20);
        });
    }

    [Theory]
    [InlineData("en", "Notification settings")]
    [InlineData("es", "Configuración de notificaciones")]
    public void TheGearSaysWhatItOpens(string language, string expected)
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();
            var before = Strings.Language;
            try
            {
                Strings.SetLanguage(language);
                var panel = new NotificationPanel();
                panel.Show(Notices(1));
                Assert.Equal(expected, Assert.IsType<TextBlock>(panel.GearForTests.ToolTip).Text);
            }
            finally
            {
                Strings.SetLanguage(before);
            }
        });
        Assert.Null(error);
    }
}
