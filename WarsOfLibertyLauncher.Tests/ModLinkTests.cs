using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
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

    // -- What the pill row shows -----------------------------------------------
    //
    // The "view mod page" button is gone, so this row is the only clickable route
    // to a mod's official website. That inverts the old rule: instead of skipping
    // a link that repeated the website, the website is folded in unless a link
    // already covers it.

    private static ModLink Link(ModLinkType type, string url) => new() { Type = type, Url = url };

    [Fact]
    public void OfficialWebsite_ComesFirst_AsAWebsitePill()
    {
        var result = ModLink.BuildDisplayList(
            "https://impmod.blogspot.com/",
            new[] { Link(ModLinkType.Discord, "https://discord.gg/x") });

        Assert.Equal(2, result.Count);
        Assert.Equal(ModLinkType.Website, result[0].Type);
        Assert.Equal("https://impmod.blogspot.com/", result[0].Url);
        Assert.Equal(ModLinkType.Discord, result[1].Type);
    }

    /// <summary>
    /// Wars of Liberty's site is http://aoe3wol.com/ — officialWebsite carries a
    /// deliberate legacy HTTP allowance that the HTTPS-only Sanitize does not.
    /// Running it through that would silently delete the pill.
    /// </summary>
    [Fact]
    public void HttpOfficialWebsite_Survives()
    {
        var result = ModLink.BuildDisplayList("http://aoe3wol.com/", new List<ModLink>());

        Assert.Single(result);
        Assert.Equal("http://aoe3wol.com/", result[0].Url);
    }

    /// <summary>Never twice: the old skip rule inverted, not deleted.</summary>
    [Theory]
    [InlineData("https://www.moddb.com/mods/x")]
    [InlineData("HTTPS://WWW.MODDB.COM/mods/x")]
    public void WebsiteAlreadyAmongLinks_IsNotDuplicated(string site)
    {
        var result = ModLink.BuildDisplayList(
            site, new[] { Link(ModLinkType.ModDb, "https://www.moddb.com/mods/x") });

        Assert.Single(result);
        Assert.Equal(ModLinkType.ModDb, result[0].Type);
    }

    /// <summary>
    /// The regression this change exists to avoid: a catalog mod that declares no
    /// `links` still has its site reachable, now that the button is gone.
    /// </summary>
    [Fact]
    public void ModWithoutLinks_StillGetsItsWebsite()
    {
        var result = ModLink.BuildDisplayList("https://example.com/", new List<ModLink>());

        Assert.Single(result);
        Assert.Equal(ModLinkType.Website, result[0].Type);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("file:///C:/x.exe")]
    [InlineData(@"C:\Windows\System32\cmd.exe")]
    [InlineData("https://user:pass@evil.example/")]
    public void UnusableWebsite_ProducesNoPill(string? site)
        => Assert.Empty(ModLink.BuildDisplayList(site, new List<ModLink>()));

    /// <summary>Nothing to show at all keeps the section collapsed.</summary>
    [Fact]
    public void NoWebsiteAndNoLinks_IsEmpty()
    {
        Assert.Empty(ModLink.BuildDisplayList("", new List<ModLink>()));
        Assert.Empty(ModLink.BuildDisplayList(null, null));
    }

    /// <summary>The author's order is preserved after the website.</summary>
    [Fact]
    public void CatalogLinksKeepTheirOrder()
    {
        var result = ModLink.BuildDisplayList("https://site/", new[]
        {
            Link(ModLinkType.Discord, "https://discord.gg/x"),
            Link(ModLinkType.ModDb, "https://moddb/x"),
            Link(ModLinkType.Video, "https://youtube/x"),
        });

        Assert.Equal(
            new[] { ModLinkType.Website, ModLinkType.Discord, ModLinkType.ModDb, ModLinkType.Video },
            result.Select(l => l.Type).ToArray());
    }

    // -- Pill icons ------------------------------------------------------------
    //
    // Generic system icons, never brand logos. The fallback is the part worth
    // pinning: a link type added later must still render an icon rather than an
    // empty gap, and that is a promise about code nobody has written yet.

    /// <summary>Every declared type has an icon — no gaps today.</summary>
    [Theory]
    [InlineData(ModLinkType.Website)]
    [InlineData(ModLinkType.Discord)]
    [InlineData(ModLinkType.ModDb)]
    [InlineData(ModLinkType.Forum)]
    [InlineData(ModLinkType.Wiki)]
    [InlineData(ModLinkType.Video)]
    [InlineData(ModLinkType.Other)]
    public void EveryLinkType_HasAGlyph(ModLinkType type)
        => Assert.False(string.IsNullOrEmpty(ModLink.GlyphFor(type)));

    /// <summary>
    /// Enumerating the enum rather than listing cases: a type added later is
    /// covered automatically, which is the whole point of the fallback.
    /// </summary>
    [Fact]
    public void NoLinkTypeIsEverIconless_IncludingOnesAddedLater()
    {
        foreach (ModLinkType type in Enum.GetValues<ModLinkType>())
            Assert.False(string.IsNullOrEmpty(ModLink.GlyphFor(type)),
                $"{type} has no glyph — add one to ModLink.GlyphFor.");

        // A value outside the enum is what an unmapped future type looks like
        // before someone updates the switch.
        Assert.Equal(ModLink.GenericLinkGlyph, ModLink.GlyphFor((ModLinkType)999));
    }

    /// <summary>
    /// Distinct icons are the point — the row should scan at a glance. If two
    /// types share one, the caption is doing all the work.
    /// </summary>
    [Fact]
    public void TheMainTypes_HaveDistinctGlyphs()
    {
        var glyphs = new[]
        {
            ModLinkType.Website, ModLinkType.Discord, ModLinkType.ModDb,
            ModLinkType.Forum, ModLinkType.Wiki, ModLinkType.Video,
        }.Select(ModLink.GlyphFor).ToList();

        Assert.Equal(glyphs.Count, glyphs.Distinct().Count());
    }

    /// <summary>
    /// Segoe MDL2 icons live in the Private Use Area. A glyph outside it would
    /// render as a literal character instead of an icon.
    /// </summary>
    [Fact]
    public void GlyphsArePrivateUseArea_SoTheyRenderAsIcons()
    {
        foreach (ModLinkType type in Enum.GetValues<ModLinkType>())
        {
            var g = ModLink.GlyphFor(type);
            Assert.Single(g);
            Assert.InRange(g[0], (char)0xE000, (char)0xF8FF);
        }

        Assert.Single(ModLink.ExternalGlyph);
        Assert.InRange(ModLink.ExternalGlyph[0], (char)0xE000, (char)0xF8FF);
    }

    // -- Built-in cosmetic overlay --------------------------------------------
    //
    // Built-ins never pass through ProjectToProfile, so the catalog entry that
    // shadows one is allowed to contribute its `links` and nothing else. These
    // pin that "and nothing else", plus the idempotence the overlay depends on.

    private static ModProfile Profile(string id) => new()
    {
        Id = id,
        DisplayName = "Built-in " + id,
        OfficialWebsite = "http://example.org/",
    };

    private static ModCatalogManifest Manifest(string id, params ModLinkManifest[] links)
        => new() { Id = id, Links = links.Length == 0 ? null : links.ToList() };

    [Fact]
    public void Overlay_AppliesLinksToTheMatchingProfile()
    {
        var profiles = new[] { Profile("wol"), Profile("aoe3-tad") };

        ModRegistry.ApplyCosmeticOverlay(
            profiles, Manifest("wol", Raw("discord", "https://discord.gg/example")));

        Assert.Single(profiles[0].Links);
        Assert.Equal(ModLinkType.Discord, profiles[0].Links[0].Type);
        Assert.Empty(profiles[1].Links);   // the other built-in is untouched
    }

    [Fact]
    public void Overlay_MatchesIdCaseInsensitively()
    {
        var profiles = new[] { Profile("wol") };

        ModRegistry.ApplyCosmeticOverlay(
            profiles, Manifest("WOL", Raw("moddb", "https://www.moddb.com/mods/example")));

        Assert.Single(profiles[0].Links);
    }

    /// <summary>
    /// The whole point of widening the shadow rule by exactly ONE field: a
    /// shadowing manifest must not be able to reach anything else on the
    /// built-in — an install path or a payload url above all.
    /// </summary>
    [Fact]
    public void Overlay_TouchesNothingButLinks()
    {
        var profile = Profile("wol");
        var manifest = Manifest("wol", Raw("discord", "https://discord.gg/example"));
        manifest.DisplayName = "Totally Different Mod";
        manifest.OfficialWebsite = "https://attacker.example/";

        ModRegistry.ApplyCosmeticOverlay(new[] { profile }, manifest);

        Assert.Equal("Built-in wol", profile.DisplayName);
        Assert.Equal("http://example.org/", profile.OfficialWebsite);
    }

    /// <summary>
    /// The overlay mutates the process-wide built-in singleton, so a manifest
    /// that drops its links must CLEAR them rather than leave the previous set
    /// alive until restart. Guarding the assignment on a non-null manifest list
    /// is exactly the bug this pins.
    /// </summary>
    [Fact]
    public void Overlay_ClearsStaleLinks_WhenTheManifestDropsThem()
    {
        var profiles = new[] { Profile("wol") };
        ModRegistry.ApplyCosmeticOverlay(
            profiles, Manifest("wol", Raw("discord", "https://discord.gg/example")));
        Assert.Single(profiles[0].Links);

        ModRegistry.ApplyCosmeticOverlay(profiles, Manifest("wol"));   // links now absent

        Assert.Empty(profiles[0].Links);
    }

    [Fact]
    public void Overlay_SanitisesJustLikeProjection()
    {
        var profiles = new[] { Profile("wol") };

        ModRegistry.ApplyCosmeticOverlay(profiles, Manifest(
            "wol",
            Raw("discord", "http://insecure.example/"),          // not HTTPS
            Raw("website", "https://user:pass@evil.example/"),   // embedded credentials
            Raw("wiki",    "https://example.com/wiki")));

        Assert.Single(profiles[0].Links);
        Assert.Equal(ModLinkType.Wiki, profiles[0].Links[0].Type);
    }

    [Fact]
    public void Overlay_IsANoOp_WhenNoProfileMatches()
    {
        var profiles = new[] { Profile("wol") };

        ModRegistry.ApplyCosmeticOverlay(
            profiles, Manifest("some-community-mod", Raw("discord", "https://discord.gg/x")));

        Assert.Empty(profiles[0].Links);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Overlay_IgnoresManifestWithoutAnId(string? id)
    {
        var profiles = new[] { Profile("wol") };
        profiles[0].Links.Add(new ModLink { Type = ModLinkType.Wiki, Url = "https://kept.example/" });

        ModRegistry.ApplyCosmeticOverlay(
            profiles, new ModCatalogManifest { Id = id!, Links = new List<ModLinkManifest>() });

        Assert.Single(profiles[0].Links);   // untouched, not cleared
    }
}
