using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The Library's list and the rules around the cover (design 68a). MainWindow itself is not built
/// here: it only runs on the real launcher, which the testbed opens.
/// </summary>
public class LibraryViewTests
{
    private static ModProfile Mod(string id, string? name = null) => new() { Id = id, DisplayName = name ?? id, Author = "Team" };

    private static void Run(Action action) => StaTestThread.Run(() =>
    {
        DialogXamlTests.EnsureResources();
        action();
    }, TimeSpan.FromSeconds(60));

    private static ModRail Rail(
        IReadOnlyList<LibraryGroup> groups,
        bool open = true,
        string selected = "a",
        RailOperation? operation = null,
        Func<string, bool>? favorite = null)
    {
        var rail = new ModRail();
        rail.Show(groups, m => new ModRowState { Status = ModRowStatus.Installed, CurrentVersion = "1.0" },
            favorite ?? (_ => false), selected, operation, open, LibraryMetrics.For(1920), "empty", "body", _ => null);
        rail.Measure(new Size(440, 800));
        rail.Arrange(new Rect(0, 0, 440, 800));
        rail.UpdateLayout();
        return rail;
    }

    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var deeper in Find<T>(child)) yield return deeper;
        }
    }

    private static Button Named(DependencyObject root, string id)
        => Find<Button>(root).Single(b => AutomationProperties.GetAutomationId(b) == id);

    private static LibraryGroup Group(LibraryGroupKind kind, params string[] ids)
        => new(kind, ids.Select(id => Mod(id)).ToList());

    [Fact]
    public void EveryRowAndStarCanBePressedByName() => Run(() =>
    {
        var rail = Rail(new[] { Group(LibraryGroupKind.Mine, "a"), Group(LibraryGroupKind.Catalog, "b") });
        ModProfile? selected = null, starred = null;
        rail.Selected += (_, m) => selected = m;
        rail.FavoriteToggled += (_, m) => starred = m;

        Named(rail, "ModRow_b").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Named(rail, "ModStar_a").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal("b", selected?.Id);
        Assert.Equal("a", starred?.Id);
    });

    [Fact]
    public void THE_ONE_THAT_MATTERS_TheStarDoesNotSelectTheRow() => Run(() =>
    {
        var rail = Rail(new[] { Group(LibraryGroupKind.Mine, "a") });
        var selected = 0;
        rail.Selected += (_, _) => selected++;
        Named(rail, "ModStar_a").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(0, selected);
    });

    [Fact]
    public void AProgressTickDoesNotRebuildTheList() => Run(() =>
    {
        var operation = new RailOperation("a", 10, false);
        var rail = Rail(new[] { Group(LibraryGroupKind.Mine, "a") }, operation: operation);
        var row = Named(rail, "ModRow_a");
        rail.UpdateOperation(operation with { Percent = 55 });
        rail.UpdateLayout();
        Assert.Same(row, Named(rail, "ModRow_a"));
        Assert.Contains(Find<TextBlock>(rail), t => t.Text == "55 %");
    });

    [Fact]
    public void TheSelectedRowTakesTheModsFill() => Run(() =>
    {
        var rail = Rail(new[] { Group(LibraryGroupKind.Mine, "a", "b") }, selected: "b");
        Assert.Same(Application.Current.FindResource("LibraryRowSelected"), Named(rail, "ModRow_b").Style);
        Assert.Same(Application.Current.FindResource("LibraryRow"), Named(rail, "ModRow_a").Style);
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void THE_ONE_THAT_MATTERS_ChoosingAnotherModMovesTheHighlightWithoutRebuilding(bool open) => Run(() =>
    {
        var rail = Rail(new[] { Group(LibraryGroupKind.Mine, "a", "b"), Group(LibraryGroupKind.Catalog, "c") }, open: open);
        var a = Named(rail, "ModRow_a");
        var c = Named(rail, "ModRow_c");

        rail.Select("c");
        rail.UpdateLayout();

        // The same buttons, not new ones: a rebuild is the cost a switch used to pay.
        Assert.Same(a, Named(rail, "ModRow_a"));
        Assert.Same(c, Named(rail, "ModRow_c"));
        Assert.Same(Application.Current.FindResource("LibraryRowSelected"), c.Style);
        Assert.Same(Application.Current.FindResource("LibraryRow"), a.Style);
        Assert.Same(Application.Current.FindResource("LibraryRow"), Named(rail, "ModRow_b").Style);
    });

    [Fact]
    public void FoldedRowsKeepTheirNameForTheScreenReader() => Run(() =>
    {
        var rail = Rail(new[] { Group(LibraryGroupKind.Favorites, "a") }, open: false, favorite: _ => true);
        var tile = Named(rail, "ModRow_a");
        Assert.Equal("a", AutomationProperties.GetName(tile));
        Assert.Equal(56, tile.Width);
        Assert.DoesNotContain(Find<Button>(rail), b => AutomationProperties.GetAutomationId(b) == "ModStar_a");
    });

    /// <summary>Folded, nothing told "my mods" from the catalog (reported): a rule parts every two
    /// groups now, with the group's name as its tooltip on an area big enough to hover, and the
    /// catalog's icons are dimmed except on the selected tile.</summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_FoldedTheCatalogIsSetApart() => Run(() =>
    {
        var rail = Rail(new[] { Group(LibraryGroupKind.Mine, "a"), Group(LibraryGroupKind.Catalog, "b", "c") },
            open: false, selected: "a");

        var divider = Find<Border>(rail).Single(b => b.Tag is LibraryGroupKind.Catalog);
        Assert.Equal(ModRail.FoldedDividerHeight, divider.ActualHeight);
        Assert.NotNull(divider.ToolTip);
        Assert.Single(Find<Border>(rail), b => b.Tag is LibraryGroupKind);

        static double IconOpacity(Button tile) => ((FrameworkElement)((Grid)tile.Content).Children[0]).Opacity;
        Assert.Equal(1, IconOpacity(Named(rail, "ModRow_a")));
        Assert.Equal(ModRail.CatalogTileOpacity, IconOpacity(Named(rail, "ModRow_b")));

        // Selecting a catalog mod gives its icon back in full; the other stays dim.
        rail.Select("b");
        Assert.Equal(1, IconOpacity(Named(rail, "ModRow_b")));
        Assert.Equal(ModRail.CatalogTileOpacity, IconOpacity(Named(rail, "ModRow_c")));
    });

    /// <summary>The fold arrow is a drawing of fixed size, centred in its button: as a character it
    /// was reported sitting high in the button now and then.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheFoldArrowIsDrawnAndCentred(bool open) => Run(() =>
    {
        var rail = Rail(new[] { Group(LibraryGroupKind.Mine, "a") }, open: open);
        var button = Named(rail, "LibraryRailFold");
        var arrow = Assert.IsType<System.Windows.Shapes.Path>(button.Content);
        var at = arrow.TransformToAncestor(button).Transform(new Point(0, 0));
        Assert.Equal((button.ActualHeight - arrow.ActualHeight) / 2, at.Y, 1);
        Assert.Equal((button.ActualWidth - arrow.ActualWidth) / 2, at.X, 1);
    });

    [Fact]
    public void AnEmptyListSaysSo() => Run(() =>
    {
        var rail = Rail(Array.Empty<LibraryGroup>());
        Assert.Contains(Find<TextBlock>(rail), t => t.Text == "empty");
    });

    [Fact]
    public void TheListGivesBackTheGlobalScrollPadding() => Run(() =>
    {
        // Chrome.xaml reserves 8 px on every ScrollViewer: left there, it crops a folded tile.
        var rail = Rail(new[] { Group(LibraryGroupKind.Mine, "a") }, open: false);
        Assert.Equal(new Thickness(0), Find<ScrollViewer>(rail).Single().Padding);
    });

    [Theory]
    [InlineData("LibraryPrimaryButton", 52.0)]
    [InlineData("LibraryGlassButton", 52.0)]
    [InlineData("LibraryCardButton", 32.0)]
    [InlineData("LibraryBarButton", 34.0)]
    [InlineData("LibraryPill", 30.0)]
    public void TheButtonsAreTheHandoffsHeights(string style, double height) => Run(() =>
    {
        var button = new Button { Style = (Style)Application.Current.FindResource(style) };
        Assert.Equal(height, button.Height);
    });

    [Theory]
    [InlineData(1920, 1080, 1920, 1080, 0, 0, 1, 1)]          // same shape: all of it
    [InlineData(2000, 1000, 1000, 1000, .25, 0, .5, 1)]       // wider image: centred
    [InlineData(1000, 2000, 1000, 1000, 0, .125, 1, .5)]      // taller image: 25 % down, not centred
    public void TheCoverIsCenter25Percent(double iw, double ih, double bw, double bh, double x, double y, double w, double h)
    {
        var box = HeroCover.Viewbox(iw, ih, bw, bh);
        Assert.Equal(x, box.X, 4);
        Assert.Equal(y, box.Y, 4);
        Assert.Equal(w, box.Width, 4);
        Assert.Equal(h, box.Height, 4);
    }

    [Fact]
    public void AnImageWithNoSizeYetShowsAllOfItself()
        => Assert.Equal(new Rect(0, 0, 1, 1), HeroCover.Viewbox(0, 0, 100, 100));

    [Theory]
    [InlineData("Up to date (v1.2.0e)", true)]
    [InlineData("Game closed.", true)]
    [InlineData("", true)]
    [InlineData(null, true)]
    [InlineData("Repaired: 3 files.", false)]
    [InlineData("Up to date with a twist", false)]
    public void OnlyAMessageWorthReadingGetsTheLine(string? message, bool routine)
        => Assert.Equal(routine, LibraryStatus.IsRoutine(message, new[] { "Up to date ({0})", "Game closed." }));
}
