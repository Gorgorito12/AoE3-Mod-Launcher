using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The first paint reads the cover from disk. Before this it knew only the address, so every launch
/// downloaded a picture it already had and decoded it, then decoded the file twice more.
/// </summary>
public class ModAssetPrimeTests : IDisposable
{
    private const string Url = "https://example.com/mods/x/hero.jpg";
    private readonly string _id = "prime-" + Guid.NewGuid().ToString("N");
    private readonly List<string> _files = new();

    private string Cache(string role, string url, bool writeMeta = true, string? metaUrl = null)
    {
        Directory.CreateDirectory(ModAssetCacheService.CacheDir);
        var file = Path.Combine(ModAssetCacheService.CacheDir, $"{_id}-{role}{Path.GetExtension(url)}");
        File.WriteAllText(file, "x");
        _files.Add(file);
        if (writeMeta)
        {
            var meta = Path.Combine(ModAssetCacheService.CacheDir, $"{_id}-{role}.meta");
            File.WriteAllText(meta, JsonSerializer.Serialize(new { url = metaUrl ?? url, etag = (string?)null }));
            _files.Add(meta);
        }
        return file;
    }

    public void Dispose()
    {
        foreach (var file in _files) File.Delete(file);
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_AFileFromThatAddressIsUsedWithoutTheNetwork()
    {
        var file = Cache("hero", Url);
        var profile = new ModProfile { Id = _id, HeroImageUrl = Url };

        ModAssetCacheService.PrimeHeroPaths(profile);

        Assert.Equal(file, profile.LocalHeroImagePath);
        Assert.Equal(new[] { file }, profile.ResolveHeroSources());
    }

    [Fact]
    public void AFileFromAnotherAddressIsNotThisPicture()
    {
        Cache("hero", Url, metaUrl: "https://example.com/mods/x/old.jpg");
        var profile = new ModProfile { Id = _id, HeroImageUrl = Url };

        ModAssetCacheService.PrimeHeroPaths(profile);

        Assert.Null(profile.LocalHeroImagePath);
    }

    [Fact]
    public void AFileWithNoRecordOfWhereItCameFromProvesNothing()
    {
        Cache("hero", Url, writeMeta: false);
        var profile = new ModProfile { Id = _id, HeroImageUrl = Url };

        ModAssetCacheService.PrimeHeroPaths(profile);

        Assert.Null(profile.LocalHeroImagePath);
    }

    [Fact]
    public void ARotationIsTakenWholeOrNotAtAll()
    {
        const string second = "https://example.com/mods/x/hero2.jpg";
        Cache("hero-0", Url);
        var profile = new ModProfile { Id = _id, HeroImageUrls = new List<string> { Url, second } };

        ModAssetCacheService.PrimeHeroPaths(profile);
        Assert.Empty(profile.LocalHeroImagePaths);

        Cache("hero-1", second);
        ModAssetCacheService.PrimeHeroPaths(profile);
        Assert.Equal(2, profile.LocalHeroImagePaths.Count);
    }

    [Fact]
    public void APathAlreadyKnownIsLeftAlone()
    {
        Cache("hero", Url);
        var profile = new ModProfile { Id = _id, HeroImageUrl = Url, LocalHeroImagePath = "C:\\known.jpg" };

        ModAssetCacheService.PrimeHeroPaths(profile);

        Assert.Equal("C:\\known.jpg", profile.LocalHeroImagePath);
    }
}
