using System;
using System.Collections.Generic;
using System.IO;
using WarsOfLibertyLauncher.Models;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins the image-source fallback chain behind the "disk cache only for
/// installed + active mods" policy: cached local file → remote catalog URL
/// (gated by allowRemote, the offline switch) → packed pack:// resource →
/// null. Every UI surface (Workshop grid, dashboard hero, MP room discs,
/// dialogs) paints through these, so a regression here silently breaks icons
/// launcher-wide. The static cores take allowRemote explicitly so the tests
/// never depend on the observed ConnectivityState.
/// </summary>
public class ModProfileResolverTests : IDisposable
{
    private readonly string _dir;

    public ModProfileResolverTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "wol-resolver-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string MakeFile(string name)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, "x");
        return p;
    }

    private const string Url = "https://raw.githubusercontent.com/x/y/main/mods/m/icon.png";
    private const string Pack = "pack://application:,,,/WoL.ico";

    // ---- single-source chain ------------------------------------------------

    [Fact]
    public void ExistingLocalFile_WinsOverRemote()
    {
        var local = MakeFile("icon.png");
        Assert.Equal(local, ModProfile.ResolveImageSource(local, Url, Pack, allowRemote: true));
    }

    [Fact]
    public void MissingLocalFile_FallsToRemote()
    {
        var gone = Path.Combine(_dir, "deleted.png");
        Assert.Equal(Url, ModProfile.ResolveImageSource(gone, Url, Pack, allowRemote: true));
    }

    [Fact]
    public void NoRemote_FallsToPacked()
        => Assert.Equal(Pack, ModProfile.ResolveImageSource(null, null, Pack, allowRemote: true));

    [Fact]
    public void AllNull_ReturnsNull()
        => Assert.Null(ModProfile.ResolveImageSource(null, null, null, allowRemote: true));

    [Fact]
    public void Offline_SkipsRemote_FallsToPacked()
        => Assert.Equal(Pack, ModProfile.ResolveImageSource(null, Url, Pack, allowRemote: false));

    [Fact]
    public void Offline_SkipsRemote_NoPacked_ReturnsNull()
        => Assert.Null(ModProfile.ResolveImageSource(null, Url, null, allowRemote: false));

    [Fact]
    public void WhitespaceEntries_AreIgnored()
        => Assert.Null(ModProfile.ResolveImageSource("  ", " ", "", allowRemote: true));

    // ---- list chain (heroes / screenshots) ----------------------------------

    [Fact]
    public void ExistingLocalList_WinsAsASet_DroppingMissingEntries()
    {
        var a = MakeFile("hero-0.jpg");
        var missing = Path.Combine(_dir, "hero-1.jpg");
        var result = ModProfile.ResolveImageSources(
            new List<string> { a, missing }, new List<string> { Url }, allowRemote: true);
        Assert.Equal(new[] { a }, result);
    }

    [Fact]
    public void NoLocalFilesOnDisk_FallsToRemoteList()
    {
        var missing = Path.Combine(_dir, "hero-0.jpg");
        var result = ModProfile.ResolveImageSources(
            new List<string> { missing }, new List<string> { Url, Url + "2" }, allowRemote: true);
        Assert.Equal(new[] { Url, Url + "2" }, result);
    }

    [Fact]
    public void Offline_EmptyLocals_ReturnsEmpty_NotRemote()
    {
        var result = ModProfile.ResolveImageSources(
            new List<string>(), new List<string> { Url }, allowRemote: false);
        Assert.Empty(result);
    }

    [Fact]
    public void BothEmpty_ReturnsEmpty()
        => Assert.Empty(ModProfile.ResolveImageSources(null, null, allowRemote: true));

    // ---- instance wrappers (chain order across roles) ------------------------

    [Fact]
    public void ResolveHeroSources_PrefersRotatingLocals_OverEverything()
    {
        var h0 = MakeFile("hero-0.jpg");
        var p = new ModProfile
        {
            Id = "m",
            LocalHeroImagePaths = new List<string> { h0 },
            HeroImageUrls = new List<string> { Url },
            LocalBannerPath = MakeFile("banner.png"),
        };
        Assert.Equal(new[] { h0 }, p.ResolveHeroSources());
    }

    [Fact]
    public void ResolveHeroSources_FallsThrough_SingleHero_ThenBanner()
    {
        // No rotating heroes; single hero not on disk but has a URL → the URL.
        var p = new ModProfile { Id = "m", HeroImageUrl = Url, BannerUrl = Url + "-banner" };
        Assert.Equal(new[] { Url }, p.ResolveHeroSources());

        // No hero at all → banner URL. Empty when the mod ships nothing.
        var q = new ModProfile { Id = "m", BannerUrl = Url + "-banner" };
        Assert.Equal(new[] { Url + "-banner" }, q.ResolveHeroSources());
        Assert.Empty(new ModProfile { Id = "m" }.ResolveHeroSources());
    }

    [Fact]
    public void ResolveBannerSource_HasNoPackedFallback()
    {
        // A 256px .ico stretched to a 1200×300 banner looks broken — the
        // packed icon must never leak into the banner surface.
        var p = new ModProfile { Id = "m", BannerImage = Pack };
        Assert.Null(p.ResolveBannerSource());
    }

    [Fact]
    public void ResolveIconSource_PackedFallback_WhenNothingElse()
    {
        var p = new ModProfile { Id = "m", BannerImage = Pack };
        Assert.Equal(Pack, p.ResolveIconSource());
    }
    // ---------------------------------------------------------------------------------------
    // The Library's gallery (design 68a): screenshots when declared, else cover + banner.

    [Fact]
    public void THE_ONE_THAT_MATTERS_AModWithACoverAndABannerHasAGallery()
    {
        // No mod in the catalog declares screenshots, so this is the shape that makes the strip
        // appear at all — the prototype shows exactly these two pictures.
        var gallery = ModProfile.GalleryFrom(Array.Empty<string>(), new[] { "hero.jpg" }, "banner.jpg");
        Assert.Equal(new[] { "hero.jpg", "banner.jpg" }, gallery);
    }

    [Fact]
    public void DeclaredScreenshotsWinAndTheCoverIsNotAdded()
    {
        var gallery = ModProfile.GalleryFrom(new[] { "s1.png", "s2.gif" }, new[] { "hero.jpg" }, "banner.jpg");
        Assert.Equal(new[] { "s1.png", "s2.gif" }, gallery);
    }

    [Fact]
    public void OnePictureIsNoGallery()
    {
        // Wars of Liberty has a cover and no banner: one thumbnail would only repeat the background.
        Assert.Empty(ModProfile.GalleryFrom(Array.Empty<string>(), new[] { "hero.jpg" }, null));
        Assert.Empty(ModProfile.GalleryFrom(new[] { "only.png" }, Array.Empty<string>(), null));
        Assert.Empty(ModProfile.GalleryFrom(Array.Empty<string>(), Array.Empty<string>(), null));
    }

    [Fact]
    public void TheSamePictureIsNotShownTwiceAndTheStripIsCapped()
    {
        // A mod whose banner IS its cover has one picture, not two.
        Assert.Empty(ModProfile.GalleryFrom(Array.Empty<string>(), new[] { "Hero.jpg" }, "hero.jpg"));
        var many = new List<string>();
        for (var i = 0; i < 12; i++) many.Add($"s{i}.png");
        Assert.Equal(ModProfile.MaxGalleryImages, ModProfile.GalleryFrom(many, Array.Empty<string>(), null).Count);
    }
}
