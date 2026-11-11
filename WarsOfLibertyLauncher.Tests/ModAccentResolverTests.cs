using System;
using System.IO;
using System.Linq;
using System.Windows.Media;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The colour of a mod whose author declared none. It was one fixed blue for all of them; now it is
/// the icon's own colour when there is one, and otherwise a hue of its own. The refusals are the
/// point: a black-and-white icon gives no colour rather than a grey one, and a declared colour is
/// never replaced.
/// </summary>
public class ModAccentResolverTests
{
    [Fact]
    public void ARedIconGivesRed()
    {
        var color = ModAccentResolver.FromPixels(Fill(200, 30, 40, count: 900), 30, 30, 120);
        Assert.NotNull(color);
        Assert.True(HueDistance(Hue(color!.Value), 0) < 15, $"red came out as hue {Hue(color.Value)}");
    }

    /// <summary>The colour that WINS is the one with the most saturated weight, not a blend of
    /// everything: a mostly-green icon with a little blue is green.</summary>
    [Fact]
    public void TheDominantHueWinsOverAMinorOne()
    {
        var pixels = Fill(40, 170, 60, count: 700).Concat(Fill(40, 60, 200, count: 200)).ToArray();
        var color = ModAccentResolver.FromPixels(pixels, 30, 30, 120);
        Assert.NotNull(color);
        Assert.True(HueDistance(Hue(color!.Value), 130) < 20, $"came out as hue {Hue(color.Value)}");
    }

    [Theory]
    [InlineData(128, 128, 128)]   // grey
    [InlineData(5, 5, 5)]         // near black
    [InlineData(250, 250, 250)]   // near white
    public void AnIconWithNoColourGivesNothing(byte r, byte g, byte b)
        => Assert.Null(ModAccentResolver.FromPixels(Fill(r, g, b, count: 900), 30, 30, 120));

    [Fact]
    public void AFewColouredPixelsInABlackAndWhiteIconGiveNothing()
    {
        var pixels = Fill(128, 128, 128, count: 880).Concat(Fill(220, 20, 20, count: 20)).ToArray();
        Assert.Null(ModAccentResolver.FromPixels(pixels, 30, 30, 120));
    }

    [Fact]
    public void ATransparentIconGivesNothing()
    {
        var pixels = Fill(220, 20, 20, count: 900, alpha: 0);
        Assert.Null(ModAccentResolver.FromPixels(pixels, 30, 30, 120));
    }

    /// <summary>Fifteen mods without a colour (the whole catalog's count) still read as fifteen.</summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_FallbacksAreStableAndApart()
    {
        var hues = Enumerable.Range(0, 15).Select(i => Hue(ModAccentResolver.Fallback(i))).ToArray();
        Assert.Equal(hues, Enumerable.Range(0, 15).Select(i => Hue(ModAccentResolver.Fallback(i))).ToArray());
        var min = double.MaxValue;
        for (var a = 0; a < hues.Length; a++)
            for (var b = a + 1; b < hues.Length; b++)
                min = Math.Min(min, HueDistance(hues[a], hues[b]));
        Assert.True(min >= 8, $"two fallbacks are {min:0.0} degrees apart");
    }

    [Fact]
    public void ADeclaredColourIsNeverReplaced()
    {
        var declared = ModRegistry.ProjectToProfile(Entry("with-colour", "#3f9d5a"));
        var undeclared = ModRegistry.ProjectToProfile(Entry("without-colour", ""));
        Assert.True(declared.AccentDeclared);
        Assert.False(undeclared.AccentDeclared);

        ModRegistry.AssignFallbackAccents(new[] { declared, undeclared });
        Assert.Equal("#3f9d5a", declared.AccentColor);
        Assert.Equal(ModAccentResolver.Hex(ModAccentResolver.Fallback(0)), undeclared.AccentColor);
    }

    /// <summary>The fixed blue every such mod used to share is gone: two undeclared mods differ.</summary>
    [Fact]
    public void TwoUndeclaredModsDoNotShareAColour()
    {
        var a = ModRegistry.ProjectToProfile(Entry("alpha-mod", ""));
        var b = ModRegistry.ProjectToProfile(Entry("beta-mod", ""));
        ModRegistry.AssignFallbackAccents(new[] { b, a });
        Assert.NotEqual(a.AccentColor, b.AccentColor);
        // In id order, whatever order the catalog lists them in.
        Assert.Equal(ModAccentResolver.Hex(ModAccentResolver.Fallback(0)), a.AccentColor);
    }

    /// <summary>AoE3 icons are mostly gold, so the first colour of most of them is gold. When gold is
    /// already another mod's, the icon's next colour is taken instead; with nothing left, none.</summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_AColourAnotherModWearsIsPassedOver()
    {
        var gold = ModAccent.Normalize(Color.FromRgb(180, 140, 70));
        var teal = ModAccent.Normalize(Color.FromRgb(50, 145, 165));
        var pixels = Fill(180, 140, 70, count: 600).Concat(Fill(50, 145, 165, count: 300)).ToArray();
        var candidates = ModAccentResolver.Candidates(pixels, 30, 30, 120);

        Assert.True(candidates.Count >= 2);
        Assert.Equal(candidates[0], ModAccentResolver.Pick(candidates, Array.Empty<Color>()));
        var picked = ModAccentResolver.Pick(candidates, new[] { gold });
        Assert.NotNull(picked);
        Assert.True(ModAccentResolver.HueApart(picked!.Value, teal) < 15);
        Assert.Null(ModAccentResolver.Pick(candidates, new[] { gold, teal }));
    }

    [Fact]
    public void AFallbackNeverLandsOnADeclaredColour()
    {
        var declared = new[] { ModAccentResolver.Fallback(0), ModAccentResolver.Fallback(1) };
        for (var i = 0; i < 5; i++)
        {
            var hue = ModAccentResolver.FallbackAvoiding(i, declared);
            Assert.All(declared, d => Assert.True(ModAccentResolver.HueApart(hue, d) >= ModAccentResolver.MinHueApart / 2));
        }
    }

    [Fact]
    public void TheCacheKeepsALearnedColourForTheNextSession()
    {
        var file = Path.Combine(Path.GetTempPath(), "aoe3ml-accent-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            ModAccentCache.PathOverride = file;
            ModAccentCache.Forget();
            ModAccentCache.Put("https://example.org/icon.png", "#aa3344");
            ModAccentCache.Forget();
            Assert.Equal("#aa3344", ModAccentCache.Get("https://example.org/icon.png"));
            Assert.Null(ModAccentCache.Get("https://example.org/other.png"));
        }
        finally
        {
            ModAccentCache.PathOverride = null;
            ModAccentCache.Forget();
            try { File.Delete(file); } catch { }
        }
    }

    private static ModCatalogEntry Entry(string id, string accent) => new()
    {
        Manifest = new ModCatalogManifest
        {
            Id = id,
            DisplayName = id,
            AccentColor = accent,
            Install = new ModCatalogInstall { Type = "IsolatedFolder" },
            Update = new ModCatalogUpdate { Mechanism = "Manual" },
        },
    };

    private static byte[] Fill(byte r, byte g, byte b, int count, byte alpha = 255)
        => Enumerable.Range(0, count).SelectMany(_ => new[] { b, g, r, alpha }).ToArray();

    private static double Hue(Color c) => System.Drawing.Color.FromArgb(c.R, c.G, c.B).GetHue();

    private static double HueDistance(double a, double b)
    {
        var d = Math.Abs(a - b) % 360;
        return Math.Min(d, 360 - d);
    }
}
