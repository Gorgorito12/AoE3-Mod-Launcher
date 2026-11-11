using System;
using System.Windows.Media;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>The design base's numbers, which every window reads through these two classes.</summary>
public class GlassThemeTests
{
    [Theory]
    [InlineData(null, GlassTheme.Medium)]
    [InlineData("", GlassTheme.Medium)]
    [InlineData("nonsense", GlassTheme.Medium)]
    [InlineData(GlassTheme.Solid, GlassTheme.Solid)]
    [InlineData(GlassTheme.Maximum, GlassTheme.Maximum)]
    public void AnUnknownLevelReadsAsTheDefault(string? value, string expected)
        => Assert.Equal(expected, GlassTheme.Normalize(value));

    [Theory]
    [InlineData(GlassTheme.Solid, 219, 1.0)]
    [InlineData(GlassTheme.Medium, 128, .42)]
    [InlineData(GlassTheme.Maximum, 71, .3)]
    public void EachLevelCarriesTheHandoffsAlpha(string level, byte panelAlpha, double barAlpha)
    {
        var spec = GlassTheme.For(level);
        Assert.Equal(panelAlpha, spec.Panel.A);
        Assert.Equal(barAlpha, spec.BarAlpha);
    }

    /// <summary>
    /// The 68b palette, as the catalog now declares it, lands where the prototype's own
    /// normalisation puts it ("igualada"): the five mods that used to share one gold now read as
    /// five different colours.
    /// </summary>
    [Theory]
    [InlineData("#1fa3a3", "#08a4a4")]   // Age of Dynasties (Remake)
    [InlineData("#3b5bd6", "#4c6cd1")]   // AoE1 HD Edition
    [InlineData("#8f9a2e", "#8f9a2e")]   // Hundred Days
    [InlineData("#8b4fc9", "#8957c0")]   // King's Return
    [InlineData("#3f9d5a", "#3f9d5a")]   // The Ancient Age
    public void THE_ONE_THAT_MATTERS_The68bPaletteNormalisesAsThePrototypeDoes(string declared, string expected)
    {
        Assert.True(ModAccent.TryParse(declared, out var color));
        Assert.True(ModAccent.TryParse(expected, out var want));
        var got = ModAccent.Normalize(color);
        Assert.True(Math.Abs(got.R - want.R) <= 2 && Math.Abs(got.G - want.G) <= 2 && Math.Abs(got.B - want.B) <= 2,
            $"{declared} normalised to #{got.R:x2}{got.G:x2}{got.B:x2}, the prototype gives {expected}");
    }

    [Theory]
    [InlineData("#c8102e", "#d8434a")]   // Wars of Liberty: red too dark and too saturated
    [InlineData("#3a8cd9", "#3a8cd9")]   // Improvement Mod: already inside the band keeps its hue
    public void TheAccentKeepsItsHueInsideTheBand(string input, string _)
    {
        Assert.True(ModAccent.TryParse(input, out var color));
        var normal = ModAccent.Normalize(color);
        Assert.True(HueDistance(color, normal) < 12, $"{input} moved its hue to {normal}");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("#12")]
    [InlineData("red")]
    public void ABadAccentIsRefused(string? hex) => Assert.False(ModAccent.TryParse(hex, out _));

    [Fact]
    public void TextInTheModsColourReachesFiveToOne()
    {
        foreach (var hex in new[] { "#c8102e", "#3a8cd9", "#8f9a2e", "#1fa3a3" })
        {
            Assert.True(ModAccent.TryParse(hex, out var color));
            Assert.True(ModAccent.Contrast(ModAccent.TextOn(ModAccent.Normalize(color)), ModAccent.Navy) >= 5, hex);
        }
    }

    [Fact]
    public void TheSelectionFillIsOpaque()
    {
        Assert.True(ModAccent.TryParse("#c8102e", out var color));
        Assert.Equal(255, ModAccent.SelectionFill(color).A);
    }

    private static double HueDistance(Color a, Color b)
    {
        static double Hue(Color c)
        {
            var d = System.Drawing.Color.FromArgb(c.R, c.G, c.B);
            return d.GetHue();
        }
        var delta = System.Math.Abs(Hue(a) - Hue(b));
        return System.Math.Min(delta, 360 - delta);
    }
}
