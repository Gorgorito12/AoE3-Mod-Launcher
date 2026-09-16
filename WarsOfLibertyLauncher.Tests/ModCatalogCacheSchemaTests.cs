using System.Text.Json;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The rule that decides whether an on-disk catalog cache may be used.
///
/// <para>Written after it went wrong in production. <c>previousIds</c> shipped without a
/// cache version, so users who updated to the build that was supposed to carry their
/// renamed mod's installation across were served a cache written hours earlier by the
/// previous build — one in which the field had never existed, because the cache is a
/// serialisation of the DTO and that DTO did not declare it yet. The new code read an
/// empty list, the install kept reporting as somebody else's, and the whole release did
/// nothing for a day. These tests exist so the next added field cannot repeat it.</para>
/// </summary>
public class ModCatalogCacheSchemaTests
{
    private const string Repo = "Gorgorito12/aoe3-mods-catalog";

    private static ModCatalogCache Cache(int schema, string repo = Repo) => new()
    {
        FetchedAt = System.DateTime.UtcNow,
        Repo = repo,
        Schema = schema,
    };

    [Fact]
    public void ACacheAtTheCurrentSchemaIsAccepted()
        => Assert.NotNull(ModCatalogService.AcceptCache(
            Cache(ModCatalogService.CacheSchemaVersion), Repo));

    /// <summary>
    /// The exact shape of the incident: no <c>schema</c> key at all, because the build
    /// that wrote it predated the field. Absent deserializes to 0, which must not match.
    /// </summary>
    [Fact]
    public void ACacheWrittenBeforeVersioningExistedIsDiscarded()
    {
        var legacy = JsonSerializer.Deserialize<ModCatalogCache>(
            $$"""{"fetchedAt":"2026-09-16T15:40:39Z","repo":"{{Repo}}","manifests":[]}""");

        Assert.Equal(0, legacy!.Schema);
        Assert.Null(ModCatalogService.AcceptCache(legacy, Repo));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public void ACacheFromADifferentBuildIsDiscarded(int schema)
    {
        if (schema == ModCatalogService.CacheSchemaVersion) return;   // vacuous after a bump
        Assert.Null(ModCatalogService.AcceptCache(Cache(schema), Repo));
    }

    /// <summary>The pre-existing rule still holds — a schema check must not replace it.</summary>
    [Fact]
    public void ACacheFromAnotherRepoIsStillDiscarded()
        => Assert.Null(ModCatalogService.AcceptCache(
            Cache(ModCatalogService.CacheSchemaVersion, "someone-else/fork"), Repo));

    [Fact]
    public void NullIsNull()
        => Assert.Null(ModCatalogService.AcceptCache(null, Repo));

    /// <summary>
    /// The regression that would have caught the incident on its own: a manifest field
    /// has to survive the write-then-read the cache performs. If this ever fails for a
    /// newly added field, that field is invisible to every cached session.
    /// </summary>
    [Fact]
    public void ManifestFieldsSurviveTheCacheRoundTrip()
    {
        var entry = new ModCatalogEntry
        {
            Manifest = new ModCatalogManifest
            {
                Id = "knights-and-barbarians-remastered",
                PreviousIds = new() { "knights-and-barbarians" },
                InstallProductGuid = "knights-and-barbarians_launcher",
            },
        };

        var json = JsonSerializer.Serialize(new ModCatalogCache
        {
            Repo = Repo,
            Schema = ModCatalogService.CacheSchemaVersion,
            Manifests = new() { entry },
        });

        var loaded = ModCatalogService.AcceptCache(
            JsonSerializer.Deserialize<ModCatalogCache>(json), Repo);

        Assert.NotNull(loaded);
        var manifest = Assert.Single(loaded!.Manifests).Manifest;
        Assert.Equal("knights-and-barbarians-remastered", manifest.Id);
        Assert.Equal(new[] { "knights-and-barbarians" }, manifest.PreviousIds);
        Assert.Equal("knights-and-barbarians_launcher", manifest.InstallProductGuid);
    }
}
