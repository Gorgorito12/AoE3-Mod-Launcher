using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WarsOfLibertyLauncher.Controls;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The community block's header line (design 61) shows only the facts that fit WHOLE and drops the
/// rest FROM THE END — never a second line. <see cref="FitRowPanel"/> is that rule.
/// </summary>
[Collection("wpf-and-language")]
public class FitRowPanelTests
{
    [Fact]
    public void AllThatFitAreShown()
        => Assert.Equal(3, FitRowPanel.CountThatFit(new double[] { 100, 120, 80 }, 300));

    [Fact]
    public void TheLastOnesGoFirst()
        => Assert.Equal(2, FitRowPanel.CountThatFit(new double[] { 100, 120, 80 }, 299));

    /// <summary>
    /// A prefix, always: a narrow fact after a wide one that does not fit is not shown in its place
    /// — the priority is the ORDER, so Players never jumps ahead of Matches.
    /// </summary>
    [Fact]
    public void AShortFactNeverJumpsAheadOfALongerOne()
        => Assert.Equal(1, FitRowPanel.CountThatFit(new double[] { 100, 300, 20 }, 150));

    [Fact]
    public void LayoutRoundingDoesNotCostAnExactFit()
        => Assert.Equal(2, FitRowPanel.CountThatFit(new double[] { 100.3, 99.9 }, 200));

    [Fact]
    public void ACollapsedChildIsSkippedAndNeverCounted()
        => Assert.Equal(2, FitRowPanel.CountThatFit(new double[] { 100, -1, 100 }, 200));

    /// <summary>On a real panel: the children past the edge get no width, and none wraps below.</summary>
    [Fact]
    public void ThePanelArrangesTheFittingPrefixOnOneLine()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var panel = new FitRowPanel();
            foreach (var w in new[] { 120.0, 140, 90, 200 })
                panel.Children.Add(new Border { Width = w, Height = 20 });
            panel.Measure(new Size(400, 28));
            panel.Arrange(new Rect(0, 0, 400, 28));

            Assert.Equal(3, panel.VisibleCount);
            var kids = panel.Children.OfType<Border>().ToList();
            static Rect Slot(UIElement e) => System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot((FrameworkElement)e);
            Assert.All(kids.Take(3), k => Assert.True(Slot(k).Width > 0));
            // The last one is arranged into an empty slot: clipped away, no width on the line.
            Assert.Equal(0, Slot(kids[3]).Width);
            // One line: the shown ones all sit at the same height.
            var y = kids[0].TranslatePoint(new Point(0, 0), panel).Y;
            Assert.All(kids.Take(3), k => Assert.Equal(y, k.TranslatePoint(new Point(0, 0), panel).Y, 1));
            Assert.Equal(260, kids[2].TranslatePoint(new Point(0, 0), panel).X, 1);
        });
        Assert.Null(error);
    }
}
