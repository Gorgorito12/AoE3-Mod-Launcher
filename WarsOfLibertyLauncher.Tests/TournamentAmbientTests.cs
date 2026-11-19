using System;
using System.Windows.Media;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>The tournament's mod colour on the page's chrome. States keep their own colours.</summary>
public class TournamentAmbientTests
{
    private static readonly Color WolRed = ModAccent.Normalize((Color)ColorConverter.ConvertFromString("#c8102e"));

    private static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    private static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_TheActiveTabIsTheSelectionFillOfTheMod()
    {
        var a = TournamentAmbient.For(WolRed);
        Assert.Equal(ModAccent.SelectionFill(WolRed), a.ActiveFill);
        Assert.Equal(WolRed.R, a.ActiveRim.R);
        Assert.Equal(WolRed.G, a.ActiveRim.G);
        Assert.Equal(WolRed.B, a.ActiveRim.B);
    }

    [Fact]
    public void TheTextOnTheTintedPiecesStaysReadable()
    {
        foreach (var hex in new[] { "#c8102e", "#2f7fe0", "#e6c06a", "#3a8cd9", "#5c9e31" })
        {
            var accent = ModAccent.Normalize((Color)ColorConverter.ConvertFromString(hex));
            var a = TournamentAmbient.For(accent);
            Assert.True(Contrast(Colors.White, a.ActiveFill) >= 4.5, $"{hex}: white on the active tab");
            Assert.True(Contrast(a.ChipText, a.ChipFill) >= 4.5, $"{hex}: the mod chip's name");
        }
    }

    [Fact]
    public void ARedModTouchesNoStateColour()
    {
        // The ambient carries no state: champion gold, live blue and won green come from StateColors.
        var a = TournamentAmbient.For(WolRed);
        foreach (var state in new[] { StateColors.Gold, StateColors.Blue, StateColors.Green })
        {
            Assert.NotEqual(state, Opaque(a.ActiveRim));
            Assert.NotEqual(state, Opaque(a.Line));
            Assert.NotEqual(state, Opaque(a.PathRim));
        }
    }

    [Fact]
    public void WithoutTheModsColourTheChromeIsTheLauncherBlue()
    {
        var blue = ModTheme.AccentOf("#c8102e", useModColor: false);
        var a = TournamentAmbient.For(blue);
        Assert.Equal(ModAccent.SelectionFill(blue), a.ActiveFill);
        Assert.Equal(blue.B, a.ActiveRim.B);
    }

    private static Color Opaque(Color c) => Color.FromRgb(c.R, c.G, c.B);
}
