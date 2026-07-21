using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins the sanitisation that community links go through on projection. This
/// duplicates rules the catalog CI already enforces on purpose: built-in
/// profiles never pass through that CI and the on-disk catalog cache is
/// user-writable, so this is the guarantee and the schema is the early warning.
/// </summary>
public class ModLinkTests
{
    private static ModLinkManifest Raw(string type, string url, string? label = null)
        => new() { Type = type, Url = url, Label = label };

    [Fact]
    public void Null_YieldsEmptyList()
        => Assert.Empty(ModLink.Sanitize(null));

    [Fact]
    public void KeepsHttpsLinks_InAuthorOrder()
    {
        var result = ModLink.Sanitize(new[]
        {
            Raw("discord", "https://discord.gg/example"),
            Raw("moddb",   "https://www.moddb.com/mods/example"),
        });

        Assert.Equal(2, result.Count);
        Assert.Equal(ModLinkType.Discord, result[0].Type);
        Assert.Equal("https://www.moddb.com/mods/example", result[1].Url);
    }

    /// <summary>
    /// Stricter than <c>officialWebsite</c>, whose HTTP allowance is a legacy
    /// concession to aoe3wol.com. A NEW field has no such history.
    /// </summary>
    [Theory]
    [InlineData("http://example.com/")]
    [InlineData("file:///C:/x.exe")]
    [InlineData(@"\\attacker\share\payload.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:pass@evil.example/")]
    [InlineData("")]
    public void DropsAnythingThatIsNotHttps(string url)
        => Assert.Empty(ModLink.Sanitize(new[] { Raw("website", url) }));

    [Fact]
    public void DedupesByUrl_CaseInsensitively()
    {
        var result = ModLink.Sanitize(new[]
        {
            Raw("discord", "https://discord.gg/example"),
            Raw("other",   "HTTPS://DISCORD.GG/example"),
        });

        Assert.Single(result);
    }

    [Fact]
    public void CapsAtMaxLinks()
    {
        var many = Enumerable.Range(0, ModLink.MaxLinks + 3)
            .Select(i => Raw("other", $"https://example.com/{i}"))
            .ToList();

        Assert.Equal(ModLink.MaxLinks, ModLink.Sanitize(many).Count);
    }

    [Fact]
    public void UnknownType_DegradesToOther_WithoutDroppingTheLink()
    {
        var result = ModLink.Sanitize(new[] { Raw("telegram", "https://example.com/") });

        Assert.Single(result);
        Assert.Equal(ModLinkType.Other, result[0].Type);
    }

    [Fact]
    public void Label_IsTrimmedAndLengthCapped()
    {
        var result = ModLink.Sanitize(new[]
        {
            Raw("discord", "https://discord.gg/example", new string('x', ModLink.MaxLabelLength + 20)),
        });

        Assert.Equal(ModLink.MaxLabelLength, result[0].Label.Length);
    }

    /// <summary>
    /// Control characters are stripped BEFORE the length cap, so padding a label
    /// with them can't smuggle extra visible text past the limit — and newlines
    /// can't break the pill's single-line layout.
    /// </summary>
    [Fact]
    public void Label_StripsControlCharacters()
    {
        var label = "  Our\r\n\tDiscord";
        var result = ModLink.Sanitize(new[] { Raw("discord", "https://discord.gg/example", label) });

        Assert.Equal("OurDiscord", result[0].Label);
    }

    [Fact]
    public void MissingLabel_IsEmpty_SoTheUiCanFallBackToTheTypeName()
    {
        var result = ModLink.Sanitize(new[] { Raw("wiki", "https://example.com/wiki") });

        Assert.Equal("", result[0].Label);
        Assert.Equal(ModLinkType.Wiki, result[0].Type);
    }

    [Fact]
    public void NullEntriesAreSkipped()
    {
        var raw = new List<ModLinkManifest?> { null, Raw("forum", "https://example.com/forum") };

        Assert.Single(ModLink.Sanitize(raw!));
    }
}
