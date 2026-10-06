using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Two things a player's lag bundle could not answer, or paid for without need: WHICH page was
/// on screen while every frame cost ~300 ms, and a Discord photo decoded at full resolution on
/// every rebuild of the players panel, to fill a disc 20 px wide.
/// </summary>
public class ScreenAndAvatarTests
{
    [Theory]
    [InlineData("Multiplayer", "Rooms", "Multiplayer/Rooms")]
    [InlineData("Multiplayer", "", "Multiplayer")]
    [InlineData("Play", "Rooms", "Play")] // the subtab means nothing outside Multiplayer
    [InlineData("Mods", null, "Mods")]
    public void TheLineNamesTheSubtabOnlyOnTheMultiplayerTab(string top, string? sub, string expected)
        => Assert.Equal(expected, ScreenTrace.Describe(top, sub));

    /// <summary>The subtab is re-applied on every session refresh; the log must not get a line
    /// each time.</summary>
    [Fact]
    public void ALineIsWrittenOnlyWhenTheScreenChanges()
    {
        Assert.Equal("TAB  Multiplayer/Rooms", ScreenTrace.Next(null, "Multiplayer/Rooms"));
        Assert.Null(ScreenTrace.Next("Multiplayer/Rooms", "Multiplayer/Rooms"));
        Assert.Equal("TAB  Play", ScreenTrace.Next("Multiplayer/Rooms", "Play"));
        Assert.Null(ScreenTrace.Next("Play", ""));
    }

    /// <summary>
    /// THE ONE THAT MATTERS for the avatars: the same face at the same size is ONE decoded
    /// photo, decoded at the size it is shown rather than the size it was uploaded.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_AnAvatarIsDecodedOnceAtTheSizeItIsShown()
    {
        var file = Path.Combine(Path.GetTempPath(), $"wol-avatar-{Guid.NewGuid():N}.png");
        try
        {
            WritePng(file, 512);
            var error = DialogXamlTests.RunOnStaThread(() =>
            {
                var url = new Uri(file).AbsoluteUri;
                var a = MultiplayerTab.AvatarBrush(url, 20);
                var b = MultiplayerTab.AvatarBrush(url, 20);
                Assert.NotNull(a);
                Assert.Same(a, b);

                var bmp = Assert.IsAssignableFrom<BitmapSource>(a!.ImageSource);
                Assert.Equal(20 * MultiplayerTab.AvatarDecodeScale, bmp.PixelWidth);

                // A larger disc is its own decode, never the small one drawn up.
                var big = MultiplayerTab.AvatarBrush(url, 56);
                Assert.NotSame(a, big);
                Assert.Equal(56 * MultiplayerTab.AvatarDecodeScale,
                    Assert.IsAssignableFrom<BitmapSource>(big!.ImageSource).PixelWidth);

                // An address that cannot be read leaves the monogram showing.
                Assert.Null(MultiplayerTab.AvatarBrush("not a url", 20));
            });
            Assert.Null(error);
        }
        finally { try { File.Delete(file); } catch { /* temp */ } }
    }

    private static void WritePng(string path, int size)
    {
        var pixels = new byte[size * size * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 200; pixels[i + 3] = 255; }
        var source = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
