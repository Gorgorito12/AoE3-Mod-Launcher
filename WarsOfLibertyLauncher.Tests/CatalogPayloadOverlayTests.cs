using System;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The catalog may set WoL's payload — the second and last field a shadowing entry can give a
/// built-in — but only with a SHA-256 per part, which the download verifies. The REFUSALS are
/// the point: a payload the launcher accepts is gigabytes on every player's disk.
/// </summary>
public class CatalogPayloadOverlayTests
{
    private static readonly string[] Compiled =
    {
        "https://github.com/papillo12/Updater/releases/download/1.2.0e/WolPayload.zip.001",
        "https://github.com/papillo12/Updater/releases/download/1.2.0e/WolPayload.zip.002",
    };

    private static readonly string[] NewUrls =
    {
        "https://github.com/papillo12/Updater/releases/download/1.2.0f/WolPayload.zip.001",
        "https://github.com/papillo12/Updater/releases/download/1.2.0f/WolPayload.zip.002",
    };

    private static readonly string[] NewSha =
    {
        new string('a', 64),
        new string('B', 64),
    };

    private static ModProfile Wol() => new()
    {
        Id = "wol",
        UpdateMechanism = ModUpdateMechanism.WolPatcher,
        Wol = new WolPatcherSettings { PayloadZipUrls = (string[])Compiled.Clone(), PayloadVersion = "1.2.0e" },
    };

    private static ModCatalogManifest Manifest(string[]? urls, string[]? sha, string? version = null) => new()
    {
        Id = "wol",
        Update = new ModCatalogUpdate
        {
            Wol = new ModCatalogWolSettings { PayloadZipUrls = urls, PayloadSha256 = sha, PayloadVersion = version },
        },
    };

    [Fact]
    public void APinnedCatalogPayloadIsTaken()
    {
        var p = Wol();
        ModRegistry.ApplyCosmeticOverlay(new[] { p }, Manifest(NewUrls, NewSha));

        Assert.Equal(NewUrls, p.Wol!.PayloadZipUrls);
        Assert.Equal(new[] { new string('a', 64), new string('b', 64) }, p.Wol.PayloadSha256);
    }

    public static TheoryData<string[]?, string[]?> Refused => new()
    {
        { NewUrls, null },                                              // no pins at all
        { NewUrls, new[] { new string('a', 64) } },                     // one pin short
        { NewUrls, new[] { new string('a', 64), "not-a-hash" } },       // bad hex
        { new[] { "http://github.com/x.zip.001" }, new[] { new string('a', 64) } },   // plain http
        { new[] { "file:///C:/evil.zip" }, new[] { new string('a', 64) } },           // local file
        { new[] { "https://user@evil.example/x.zip" }, new[] { new string('a', 64) } }, // userinfo trick
        { Array.Empty<string>(), Array.Empty<string>() },               // empty
    };

    /// <summary>THE ONE THAT MATTERS. Anything short of a fully pinned https payload is ignored whole.</summary>
    [Theory]
    [MemberData(nameof(Refused))]
    public void AnythingElseIsRefusedAndTheBuiltInPayloadStays(string[]? urls, string[]? sha)
    {
        var p = Wol();
        ModRegistry.ApplyCosmeticOverlay(new[] { p }, Manifest(urls, sha));

        Assert.Equal(Compiled, p.Wol!.PayloadZipUrls);
        Assert.Empty(p.Wol.PayloadSha256);
    }

    /// <summary>
    /// The overlay mutates the built-in singleton, so withdrawing (or breaking) the override in the
    /// catalog must put the compiled payload back rather than leave the old override alive.
    /// </summary>
    [Fact]
    public void WithdrawingTheOverrideRestoresTheCompiledPayload()
    {
        var p = Wol();
        ModRegistry.ApplyCosmeticOverlay(new[] { p }, Manifest(NewUrls, NewSha));
        ModRegistry.ApplyCosmeticOverlay(new[] { p }, Manifest(null, null));

        Assert.Equal(Compiled, p.Wol!.PayloadZipUrls);
        Assert.Empty(p.Wol.PayloadSha256);
    }

    /// <summary>
    /// The version a payload lays down travels with the payload and never with a different one:
    /// a new snapshot with no declared version must NOT inherit the compiled "1.2.0e", or every
    /// install of it records a version its bytes are not.
    /// </summary>
    [Fact]
    public void ANewPayloadWithoutADeclaredVersionIsUnknown_NotTheCompiledOne()
    {
        var p = Wol();
        ModRegistry.ApplyCosmeticOverlay(new[] { p }, Manifest(NewUrls, NewSha));
        Assert.Equal("", p.Wol!.PayloadVersion);
    }

    [Fact]
    public void ADeclaredVersionIsTaken_AndWithdrawingPutsTheCompiledOneBack()
    {
        var p = Wol();
        ModRegistry.ApplyCosmeticOverlay(new[] { p }, Manifest(NewUrls, NewSha, " 1.2.0f "));
        Assert.Equal("1.2.0f", p.Wol!.PayloadVersion);

        ModRegistry.ApplyCosmeticOverlay(new[] { p }, Manifest(null, null));
        Assert.Equal("1.2.0e", p.Wol.PayloadVersion);
    }

    [Fact]
    public void RePinningTheCompiledUrlsKeepsTheCompiledVersion()
    {
        var p = Wol();
        ModRegistry.ApplyCosmeticOverlay(new[] { p }, Manifest((string[])Compiled.Clone(), NewSha));
        Assert.Equal("1.2.0e", p.Wol!.PayloadVersion);
    }

    [Theory]
    [InlineData("1.2.0e", "1.2.0e")]
    [InlineData("  v2.1.7b ", "v2.1.7b")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("1.2 0e", "")]            // free text
    [InlineData("-1", "")]                // must start with a letter or digit
    [InlineData("<script>", "")]
    [InlineData("123456789012345678901234567890123", "")]   // 33 chars
    public void OnlyAVersionLookingValueIsAccepted(string? raw, string expected)
        => Assert.Equal(expected, ModRegistry.NormalizePayloadVersion(raw));

    /// <summary>
    /// The compiled WoL payload names its release in its urls, so the version it declares must be
    /// that release — the two are edited together or the launcher records the wrong version for
    /// every install and repair, silently.
    /// </summary>
    [Fact]
    public void TheWolBuiltInDeclaresTheVersionItsUrlsDownload()
    {
        var wol = ModRegistry.Find("wol")!.Wol!;
        Assert.False(string.IsNullOrEmpty(wol.PayloadVersion));
        Assert.All(wol.PayloadZipUrls,
            u => Assert.Contains($"/releases/download/{wol.PayloadVersion}/", u, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A built-in that is not WolPatcher (the stock game) is never given a payload.</summary>
    [Fact]
    public void ANonWolPatcherBuiltInIsUntouched()
    {
        var stock = new ModProfile { Id = "wol", UpdateMechanism = ModUpdateMechanism.Manual };
        ModRegistry.ApplyCosmeticOverlay(new[] { stock }, Manifest(NewUrls, NewSha));

        Assert.Null(stock.Wol);
    }
}
