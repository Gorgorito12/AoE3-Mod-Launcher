using System.Windows;
using WarsOfLibertyLauncher.Controls;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

public class PopupGlassTests
{
    private static readonly Size Layer = new(1000, 500);

    [Fact]
    public void TheWholeLayerIsTheWholeCover()
    {
        var cover = new Rect(.1, .2, .8, .6);
        Assert.Equal(cover, PopupGlass.ViewboxFor(cover, Layer, new Rect(0, 0, 1000, 500)));
    }

    /// <summary>The cover is cropped to fit the window, so a menu's piece is a fraction of that
    /// crop and not of the image: reading it off the image shows the wrong part of the picture.</summary>
    [Fact]
    public void AMenuTakesThePieceOfTheCropThatLiesUnderIt()
    {
        var piece = PopupGlass.ViewboxFor(new Rect(.1, .2, .8, .6), Layer, new Rect(500, 250, 100, 50));

        Assert.Equal(.5, piece.X, 6);
        Assert.Equal(.5, piece.Y, 6);
        Assert.Equal(.08, piece.Width, 6);
        Assert.Equal(.06, piece.Height, 6);
    }

    [Theory]
    [InlineData(0, 0, 1000, 500, true)]
    [InlineData(100, 100, 250, 200, true)]
    [InlineData(-.4, 0, 100, 100, true)] // a window edge is not a whole DIP
    [InlineData(-20, 0, 100, 100, false)]
    [InlineData(950, 0, 100, 100, false)] // hangs off the right edge
    [InlineData(0, 450, 100, 100, false)]
    [InlineData(0, 0, 0, 100, false)] // not laid out yet
    public void AMenuHangingOutOfTheWindowKeepsTheDenseFill(double x, double y, double w, double h, bool expected)
        => Assert.Equal(expected, PopupGlass.LiesWithin(Layer, new Rect(x, y, w, h)));
}
