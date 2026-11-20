using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using WarsOfLibertyLauncher.Controls;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The mosaic panel of Ranking › Highlights (design 119): equal columns, cards row by row, each
/// at its own height from the top of its row, and one card that can lie over two columns.
/// </summary>
public class BentoPanelTests
{
    private static (BentoPanel Panel, Border[] Cards) Laid(double width, params (double Height, int Span)[] cards)
    {
        var panel = new BentoPanel { Gap = 14 };
        var built = cards.Select(c =>
        {
            var card = new Border { Height = c.Height };
            BentoPanel.SetSpan(card, c.Span);
            panel.Children.Add(card);
            return card;
        }).ToArray();
        panel.Measure(new Size(width, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, width, panel.DesiredSize.Height));
        return (panel, built);
    }

    private static Rect Slot(UIElement card) => LayoutInformation.GetLayoutSlot((FrameworkElement)card);

    /// <summary>
    /// THE ONE THAT MATTERS: a short card beside a tall one keeps its own height. Stretched to
    /// its row it is the half-empty card the redesign exists to remove.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_ACardIsAsTallAsItsContentAndNoTaller()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var (panel, cards) = Laid(1880, (120, 1), (300, 1), (80, 1), (200, 1), (90, 1));
            Assert.Equal(4, panel.Columns);
            Assert.Equal(new[] { 120.0, 300, 80, 200, 90 }, cards.Select(c => Slot(c).Height));
            Assert.All(cards.Take(4), c => Assert.Equal(0, Slot(c).Top));
            // The next row starts under the tallest card of the one above, a gap away.
            Assert.Equal(314, Slot(cards[4]).Top);
            Assert.Equal(314 + 90, panel.DesiredSize.Height);
        });
        Assert.Null(error);
    }

    [Fact]
    public void ColumnsAreEqualAndAGapApart()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var (_, cards) = Laid(1880, (100, 1), (100, 1), (100, 1), (100, 1));
            var width = (1880 - 3 * 14) / 4.0;
            for (var i = 0; i < 4; i++)
            {
                Assert.Equal(width, Slot(cards[i]).Width, 3);
                Assert.Equal(i * (width + 14), Slot(cards[i]).Left, 3);
            }
        });
        Assert.Null(error);
    }

    /// <summary>A card over two columns goes to the next row when only one column is left.</summary>
    [Fact]
    public void AWideCardWrapsWhenItDoesNotFitItsRow()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var (_, cards) = Laid(1880, (100, 1), (100, 1), (100, 1), (100, 2), (100, 1));
            var width = (1880 - 3 * 14) / 4.0;
            Assert.Equal(114, Slot(cards[3]).Top);
            Assert.Equal(0, Slot(cards[3]).Left);
            Assert.Equal(2 * width + 14, Slot(cards[3]).Width, 3);
            // And the card after it sits beside it, in the third column.
            Assert.Equal(114, Slot(cards[4]).Top);
            Assert.Equal(2 * (width + 14), Slot(cards[4]).Left, 3);
        });
        Assert.Null(error);
    }

    /// <summary>On a laptop two columns; narrower still, one, where a wide card is simply a card.</summary>
    [Fact]
    public void ANarrowPageHasTwoColumnsAndThenOne()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var (two, cards) = Laid(1340, (100, 1), (100, 1), (100, 2));
            Assert.Equal(2, two.Columns);
            Assert.Equal(1340, Slot(cards[2]).Width, 3);

            var (one, stacked) = Laid(600, (100, 1), (100, 2));
            Assert.Equal(1, one.Columns);
            Assert.Equal(600, Slot(stacked[1]).Width, 3);
            Assert.Equal(114, Slot(stacked[1]).Top);
        });
        Assert.Null(error);
    }

    /// <summary>A card that is collapsed takes no cell and leaves no gap behind.</summary>
    [Fact]
    public void ACollapsedCardTakesNoRoom()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var panel = new BentoPanel { Gap = 14 };
            var hidden = new Border { Height = 500, Visibility = Visibility.Collapsed };
            var shown = new Border { Height = 100 };
            panel.Children.Add(hidden);
            panel.Children.Add(shown);
            panel.Measure(new Size(1880, double.PositiveInfinity));
            panel.Arrange(new Rect(0, 0, 1880, panel.DesiredSize.Height));
            Assert.Equal(100, panel.DesiredSize.Height);
            Assert.Equal(0, Slot(shown).Left);
        });
        Assert.Null(error);
    }
}
