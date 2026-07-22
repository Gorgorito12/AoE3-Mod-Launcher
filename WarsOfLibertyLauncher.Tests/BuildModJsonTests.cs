using System.Collections.Generic;
using System.Text.Json;
using WarsOfLibertyLauncher;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Tests for the pure mod.json builder behind the "Publish my mod" wizard
/// (<see cref="PublishModDialog.BuildModJson"/>). The load-bearing property is
/// the field→nesting map: the catalog schema uses additionalProperties:false,
/// so a field emitted at the wrong level silently makes the JSON invalid.
/// These assert each field lands at the right level and empties are omitted —
/// the only in-session verification (the wizard UI can't run headless).
/// </summary>
public class BuildModJsonTests
{
    private static JsonElement Build(PublishModDialog.ModJsonInput input)
    {
        var json = PublishModDialog.BuildModJson(input);
        return JsonDocument.Parse(json).RootElement;
    }

    private static bool Has(JsonElement obj, string prop) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(prop, out _);

    private static string Str(JsonElement obj, string prop) => obj.GetProperty(prop).GetString()!;

    [Fact]
    public void RequiredShape_AlwaysPresent()
    {
        var root = Build(new() { Id = "my-mod", DisplayName = "My Mod" });

        Assert.Equal("my-mod", Str(root, "id"));
        Assert.Equal("My Mod", Str(root, "displayName"));
        Assert.True(Has(root, "install"));
        Assert.Equal("IsolatedFolder", Str(root.GetProperty("install"), "type"));
        Assert.True(Has(root, "update"));
        Assert.Equal("WolPatcher", Str(root.GetProperty("update"), "mechanism"));
    }

    [Fact]
    public void EmptyOptionals_Omitted()
    {
        var root = Build(new() { Id = "m", DisplayName = "M" });

        Assert.False(Has(root, "subtitle"));
        Assert.False(Has(root, "author"));
        Assert.False(Has(root, "userDataFolder"));
        Assert.False(Has(root, "translations"));
        Assert.False(Has(root, "description"));
        Assert.False(Has(root.GetProperty("install"), "marker"));
        Assert.False(Has(root.GetProperty("install"), "payloadUrls"));
    }

    [Fact]
    public void TopLevelExtras_AtRoot_NotUnderInstall()
    {
        // The trap: userDataFolder reads like install data but is top-level.
        var root = Build(new()
        {
            Id = "m", DisplayName = "M",
            UserDataFolder = "My Mod",
            InstallProductGuid = "my-mod-guid",
        });

        Assert.Equal("My Mod", Str(root, "userDataFolder"));
        Assert.Equal("my-mod-guid", Str(root, "installProductGuid"));
        Assert.False(Has(root.GetProperty("install"), "userDataFolder"));
        Assert.False(Has(root.GetProperty("install"), "installProductGuid"));
    }

    [Fact]
    public void InstallFields_UnderInstall()
    {
        var root = Build(new()
        {
            Id = "m", DisplayName = "M",
            InstallType = "IsolatedFolder",
            Marker = @"art\unique",
            PayloadUrls = new[] { "https://a/x.zip.001", "https://a/x.zip.002" },
            PayloadSha256 = new[] { new string('a', 64) },
        });

        var install = root.GetProperty("install");
        Assert.Equal(@"art\unique", Str(install, "marker"));
        Assert.Equal(2, install.GetProperty("payloadUrls").GetArrayLength());
        Assert.Equal(1, install.GetProperty("payloadSha256").GetArrayLength());
        Assert.False(Has(root, "marker"));  // not at top level
    }

    [Fact]
    public void SetupPathRedirect_UnderInstall_OnlyWhenTrue()
    {
        // Off by default → omitted (JSON stays clean, like userDataRedirect).
        var off = Build(new() { Id = "m", DisplayName = "M" });
        Assert.False(Has(off.GetProperty("install"), "setupPathRedirect"));

        // On → install.setupPathRedirect: true (stock-exe replacement TC, §4).
        var on = Build(new()
        {
            Id = "m", DisplayName = "M",
            InstallType = "IsolatedFolder",
            SetupPathRedirect = true,
        });
        Assert.True(on.GetProperty("install").GetProperty("setupPathRedirect").GetBoolean());
        Assert.False(Has(on, "setupPathRedirect"));  // not at top level
    }

    [Fact]
    public void GitHub_ExternalCdn_UnderUpdateGithub_TagAtTopLevel()
    {
        var root = Build(new()
        {
            Id = "m", DisplayName = "M",
            Mechanism = "GitHubReleases",
            SourceRepo = "me/my-mod",
            ApprovedReleaseTag = "v1.0",
            GithubExternalAssetUrlTemplate = "https://cdn/{tag}.zip",
            GithubExternalAssetSha256 = new string('b', 64),
        });

        // sourceRepo + approvedReleaseTag are TOP-LEVEL.
        Assert.Equal("me/my-mod", Str(root, "sourceRepo"));
        Assert.Equal("v1.0", Str(root, "approvedReleaseTag"));

        // External CDN settings live under update.github.
        var github = root.GetProperty("update").GetProperty("github");
        Assert.Equal("https://cdn/{tag}.zip", Str(github, "externalAssetUrlTemplate"));
        Assert.Equal(new string('b', 64), Str(github, "externalAssetSha256"));
        Assert.False(Has(root.GetProperty("update"), "wol"));  // no cross-mechanism leak
    }

    [Fact]
    public void Wol_FieldsUnderUpdateWol()
    {
        var root = Build(new()
        {
            Id = "m", DisplayName = "M",
            Mechanism = "WolPatcher",
            WolUpdateInfoUrl = "https://s/UpdateInfo.xml",
            WolUpdateInfoUrlAlt = "https://mirror/UpdateInfo.xml",
            WolPayloadZipUrls = new[] { "https://s/p.zip.001" },
        });

        var wol = root.GetProperty("update").GetProperty("wol");
        Assert.Equal("https://s/UpdateInfo.xml", Str(wol, "updateInfoUrl"));
        Assert.Equal("https://mirror/UpdateInfo.xml", Str(wol, "updateInfoUrlAlt"));
        Assert.Equal(1, wol.GetProperty("payloadZipUrls").GetArrayLength());
        Assert.False(Has(root, "sourceRepo"));  // GH-only field absent for WoL
    }

    [Fact]
    public void Translations_TopLevelObject()
    {
        var root = Build(new()
        {
            Id = "m", DisplayName = "M",
            TranslationsRepo = "me/translations",
            TranslationsCoveredFiles = new[] { @"data\stringtable.xml", @"data\help.xml" },
        });

        var tr = root.GetProperty("translations");
        Assert.Equal("me/translations", Str(tr, "repo"));
        Assert.Equal(2, tr.GetProperty("coveredFiles").GetArrayLength());
    }

    [Fact]
    public void Description_PerLanguageObject()
    {
        var root = Build(new()
        {
            Id = "m", DisplayName = "M",
            DescriptionEn = "An English line.",
            DescriptionEs = "Una línea en español.",
        });

        var desc = root.GetProperty("description");
        Assert.Equal("An English line.", Str(desc, "en"));
        Assert.Equal("Una línea en español.", Str(desc, "es"));
    }

    [Fact]
    public void SplitLines_TrimsAndDropsBlanks()
    {
        var list = PublishModDialog.SplitLines("  a \n\n b \r\n   \nc");
        Assert.Equal(new[] { "a", "b", "c" }, list);
    }

    [Fact]
    public void Links_EmittedAsArrayOfTypedObjects()
    {
        var root = Build(new()
        {
            Id = "m", DisplayName = "M",
            LinkLines = new[]
            {
                "discord|https://discord.gg/my-mod",
                "https://www.moddb.com/mods/my-mod",   // no pipe → typed "other"
            },
        });

        var links = root.GetProperty("links");
        Assert.Equal(2, links.GetArrayLength());
        Assert.Equal("discord", Str(links[0], "type"));
        Assert.Equal("https://discord.gg/my-mod", Str(links[0], "url"));
        Assert.Equal("other", Str(links[1], "type"));
    }

    /// <summary>
    /// The wizard exists to produce a manifest that passes catalog CI first try,
    /// so it must not emit what the schema would reject: non-HTTPS urls, an
    /// unknown type outside the enum, or more entries than the cap.
    /// </summary>
    [Fact]
    public void Links_DropInvalid_NormaliseUnknownType_AndCap()
    {
        var root = Build(new()
        {
            Id = "m", DisplayName = "M",
            LinkLines = new[]
            {
                "website|http://insecure.example/",     // not HTTPS
                "website|file:///C:/x.exe",             // not a web url
                "telegram|https://example.com/a",       // type outside the enum
                "wiki|https://example.com/b",
                "forum|https://example.com/c",
                "video|https://example.com/d",
                "other|https://example.com/e",          // past the cap of 4
            },
        });

        var links = root.GetProperty("links");
        Assert.Equal(4, links.GetArrayLength());
        Assert.Equal("other", Str(links[0], "type"));   // telegram normalised
        Assert.Equal("https://example.com/a", Str(links[0], "url"));
    }

    [Fact]
    public void Links_OmittedWhenNoneValid()
    {
        var root = Build(new()
        {
            Id = "m", DisplayName = "M",
            LinkLines = new[] { "website|http://insecure.example/" },
        });

        Assert.False(Has(root, "links"));
    }
}
