using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Services;
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

    /// <summary>
    /// The right-click menu read lighter and warmer than the list it opened over (reported): it was
    /// the blurred cover and its own tint, without the side shade, the gradients and the panel's tint
    /// that lie between the cover and the list. Rendered here over the same warm picture, the menu
    /// must come out the panel's own hue, only darker.
    /// </summary>
    [Theory]
    [InlineData(GlassTheme.Solid)]
    [InlineData(GlassTheme.Medium)]
    [InlineData(GlassTheme.Maximum)]
    public void THE_ONE_THAT_MATTERS_AMenuIsThePanelsGlassOnlyDenser(string level)
    {
        Color panel = default, menu = default;
        var error = StaTestThread.Run(() =>
        {
            var picture = new SolidColorBrush(Color.FromRgb(167, 127, 100));
            var shade = new SolidColorBrush(Color.FromArgb(219, 15, 28, 46));
            var tint = GlassTheme.For(level).Panel;
            var size = new Size(100, 100);
            var surface = new Rect(10, 10, 40, 40);

            panel = Render(new DrawingGroup
            {
                Children =
                {
                    new GeometryDrawing(picture, null, new RectangleGeometry(new Rect(0, 0, 1, 1))),
                    new GeometryDrawing(shade, null, new RectangleGeometry(new Rect(0, 0, 1, 1))),
                    new GeometryDrawing(new SolidColorBrush(tint), null, new RectangleGeometry(new Rect(0, 0, 1, 1))),
                },
            });
            menu = Render(PopupGlass.Layers(picture, new Brush[] { shade }, size, surface, tint));
        }, TimeSpan.FromSeconds(30));
        Assert.Null(error);

        Assert.True(Luma(menu) < Luma(panel), $"{level}: menu {menu} is not darker than the panel {panel}");
        Assert.True(Math.Abs(Hue(menu) - Hue(panel)) < 12, $"{level}: menu {menu} and panel {panel} are different hues");
    }

    private static Color Render(Drawing drawing)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
            context.DrawRectangle(new DrawingBrush(drawing) { Stretch = Stretch.Fill }, null, new Rect(0, 0, 8, 8));
        var bitmap = new RenderTargetBitmap(8, 8, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[8 * 8 * 4];
        bitmap.CopyPixels(pixels, 32, 0);
        int at = (4 * 8 + 4) * 4;
        return Color.FromRgb(pixels[at + 2], pixels[at + 1], pixels[at]);
    }

    private static double Luma(Color c) => .2126 * c.R + .7152 * c.G + .0722 * c.B;

    private static double Hue(Color c) => System.Drawing.Color.FromArgb(c.R, c.G, c.B).GetHue();
}
