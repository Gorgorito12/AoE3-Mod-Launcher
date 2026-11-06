using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>The game's pictures stay on screen, and their absence leaves no empty square.</summary>
[Collection("wpf-and-language")]
public class CardArtTests
{
    private static ImageSource Pixel()
    {
        var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static int Images(DependencyObject root)
    {
        var count = root is Image ? 1 : 0;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            count += Images(child);
        return count;
    }

    private static void Run(System.Action body)
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();
            body();
        });
        Assert.Null(error);
    }

    [Fact]
    public void TheBalanceTableCarriesTheFlagOnlyWhenThereIsOne()
    {
        Run(() =>
        {
            Assert.Equal(1, Images(MultiplayerTab.BuildCivRow("Mexicans", 9, 5, 4, 780, flag: Pixel())));
            Assert.Equal(0, Images(MultiplayerTab.BuildCivRow("Mexicans", 9, 5, 4, 780)));
        });
    }

    [Fact]
    public void TheProfileTableCarriesTheFlagOnlyWhenThereIsOne()
    {
        Run(() =>
        {
            var row = new CivStatRow("Mexicans", 9, 5, 4);
            Assert.Equal(1, Images(MultiplayerTab.BuildProfileCivRow(row, Pixel())));
            Assert.Equal(0, Images(MultiplayerTab.BuildProfileCivRow(row)));
        });
    }

    /// <summary>The chip grows with the picture, or the picture would be cut by its height.</summary>
    [Fact]
    public void AProfileCardShowsItsArtAndMakesRoomForIt()
    {
        Run(() =>
        {
            var withArt = (Border)MultiplayerTab.BuildDeckChip("Cards", Pixel(), 42, false, "tip");
            var without = (Border)MultiplayerTab.BuildDeckChip("Cards", null, 42, false, "tip");
            Assert.Equal(1, Images(withArt));
            Assert.Equal(0, Images(without));
            Assert.True(withArt.Height > without.Height);
        });
    }
}

public class ModFingerprintCheckTests
{
    private static Dictionary<string, string> Hashes(string proto, string tech = "b", string str = "c") => new()
    {
        ["data/protoy.xml"] = proto,
        ["data/techtreey.xml"] = tech,
        ["data/stringtabley.xml"] = str,
    };

    [Fact]
    public void MatchingFilesAreOk()
        => Assert.Equal(ModFingerprintState.Ok, ModFingerprintCheck.Compare(Hashes("a"), Hashes("A")));

    [Fact]
    public void AFileTouchedByHandIsDifferent()
        => Assert.Equal(ModFingerprintState.Different, ModFingerprintCheck.Compare(Hashes("a"), Hashes("z")));

    /// <summary>The refusals are the point: no baseline or an unread file is no verdict at all.</summary>
    [Fact]
    public void NothingToCompareSaysNothing()
    {
        Assert.Equal(ModFingerprintState.Unknown, ModFingerprintCheck.Compare(null, Hashes("a")));
        Assert.Equal(ModFingerprintState.Unknown, ModFingerprintCheck.Compare(new Dictionary<string, string>(), Hashes("a")));
        Assert.Equal(ModFingerprintState.Unknown, ModFingerprintCheck.Compare(Hashes("a"), null));
        Assert.Equal(ModFingerprintState.Unknown, ModFingerprintCheck.Compare(
            Hashes("a"), new Dictionary<string, string> { ["data/protoy.xml"] = "a" }));
    }
}

public class ProfileRatingStateTests
{
    [Theory]
    [InlineData(true, false, false, ProfileRatingState.Ready)]
    [InlineData(true, true, true, ProfileRatingState.Ready)]
    [InlineData(false, false, false, ProfileRatingState.Loading)]
    [InlineData(false, true, false, ProfileRatingState.Failed)]
    [InlineData(false, false, true, ProfileRatingState.Failed)]
    internal void AStandingInHandAlwaysWins(bool has, bool failed, bool offline, ProfileRatingState expected)
        => Assert.Equal(expected, ProfileRatingView.StateOf(has, failed, offline));
}
