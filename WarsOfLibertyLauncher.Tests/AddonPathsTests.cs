using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins the wrapper-folder rule for addon archives.
///
/// Both directions are failures a player would experience as "the addon does
/// nothing", with no error to go on:
///   * NOT stripping a wrapper puts the gun-smoke addon's 197 files in
///     &lt;install&gt;\AO3\art\… , which the game never reads;
///   * stripping one that isn't there relocates every file of a flat archive.
/// The no-op cases matter as much as the stripping ones.
/// </summary>
public class AddonPathsTests
{
    /// <summary>The real gun-smoke archive: everything under a single "AO3/".</summary>
    [Fact]
    public void StripsASingleWrapperFolder()
    {
        var prefix = AddonPaths.StripCommonRoot(new[]
        {
            "AO3/art/effects/explosions/1exp_flash.particle.xmb",
            "AO3/data/playercolors.xml",
            "AO3/sound/musket.wav",
        });

        Assert.Equal("AO3/", prefix);
        Assert.Equal("data/playercolors.xml",
            AddonPaths.RemovePrefix("AO3/data/playercolors.xml", prefix));
    }

    /// <summary>The real building-rotator archive: files at the root, so nothing to strip.</summary>
    [Fact]
    public void FlatArchive_IsUntouched()
    {
        var prefix = AddonPaths.StripCommonRoot(new[]
        {
            "Building Rotator.exe",
            "EV Products ReadMe.pdf",
            "startup/gamey.con",
        });

        Assert.Equal("", prefix);
        Assert.Equal("startup/gamey.con", AddonPaths.RemovePrefix("startup/gamey.con", prefix));
    }

    /// <summary>
    /// A folder plus a loose file at the root is not a wrapper — stripping would
    /// silently drop the loose file.
    /// </summary>
    [Fact]
    public void MixedRoot_IsNotAWrapper()
        => Assert.Equal("", AddonPaths.StripCommonRoot(new[] { "AO3/art/x.ddt", "readme.txt" }));

    [Fact]
    public void TwoTopLevelFolders_AreNotAWrapper()
        => Assert.Equal("", AddonPaths.StripCommonRoot(new[] { "art/x.ddt", "sound/y.wav" }));

    /// <summary>
    /// The case that makes the rule hard, and that a naive "one shared root"
    /// check gets wrong: an addon shipping only art files has art/ as its single
    /// common root, and stripping it would scatter every file into the install
    /// root. A game folder is where files belong, not a wrapper around them.
    /// </summary>
    [Theory]
    [InlineData("art")]
    [InlineData("data")]
    [InlineData("sound")]
    [InlineData("startup")]
    [InlineData("UI")]
    public void AGameFolder_IsNeverTreatedAsAWrapper(string root)
        => Assert.Equal("", AddonPaths.StripCommonRoot(new[] { root + "/a.x", root + "/b/c.y" }));

    /// <summary>Only ONE level comes off — a second wrapper would need its own pass.</summary>
    [Fact]
    public void StripsOnlyTheOutermostLevel()
    {
        var prefix = AddonPaths.StripCommonRoot(new[] { "Pack/AO3/art/x.ddt", "Pack/AO3/y.wav" });

        Assert.Equal("Pack/", prefix);
        Assert.Equal("AO3/art/x.ddt", AddonPaths.RemovePrefix("Pack/AO3/art/x.ddt", prefix));
    }

    [Theory]
    [InlineData(@"AO3\art\x.ddt")]
    [InlineData("./AO3/art/x.ddt")]
    [InlineData("/AO3/art/x.ddt")]
    public void ToleratesSeparatorAndPrefixNoise(string entry)
        => Assert.Equal("AO3/", AddonPaths.StripCommonRoot(new[] { entry, "AO3/y.wav" }));

    [Fact]
    public void CaseInsensitive_SinceZipCasingIsNotASignal()
        => Assert.Equal("AO3/", AddonPaths.StripCommonRoot(new[] { "AO3/a.ddt", "ao3/b.ddt" }));

    [Fact]
    public void EmptyOrNull_IsNoOp()
    {
        Assert.Equal("", AddonPaths.StripCommonRoot(null));
        Assert.Equal("", AddonPaths.StripCommonRoot(new string[0]));
    }

    /// <summary>An entry outside the prefix yields "" so the caller can drop it.</summary>
    [Fact]
    public void RemovePrefix_DropsEntriesOutsideThePrefix()
        => Assert.Equal("", AddonPaths.RemovePrefix("other/x.ddt", "AO3/"));

    /// <summary>
    /// Forward slashes are the manifest's convention. Emitting backslashes makes
    /// RecaptureHashes fail to match OverlayFiles, which leaves verify calling
    /// every addon file corrupt.
    /// </summary>
    [Fact]
    public void Normalize_EmitsForwardSlashes()
        => Assert.Equal("art/ui/panel.ddt", AddonPaths.Normalize(@"art\ui\panel.ddt"));
}

/// <summary>
/// The offered-addon list. Each entry's shape was decided by reading the real
/// archive, so these pin the conclusions rather than the wording.
/// </summary>
public class AddonRegistryTests
{
    private static AddonEntry Get(string id) =>
        AddonRegistry.Find(id) ?? throw new Xunit.Sdk.XunitException($"missing addon {id}");

    /// <summary>
    /// The rotator's archive also carries an executable, a PDF and a screenshot.
    /// Declaring the .con files states exactly which game files it writes, so a
    /// reviewer sees it before any player runs it.
    /// </summary>
    [Fact]
    public void Rotator_DeclaresOnlyTheStartupConfigs()
    {
        var entry = Get("heaven-1932");

        Assert.NotNull(entry.IncludeOnly);
        Assert.All(entry.IncludeOnly!, p => Assert.StartsWith("startup/", p));
        Assert.Contains("startup/gamey.con", entry.IncludeOnly!);   // TAD — what WoL is built on
        Assert.False(entry.ExternalInstallerOnly);
    }

    /// <summary>
    /// Its archive holds exactly one file, an .exe, and no game files at all —
    /// so there is nothing to overlay and a checkbox would be a lie.
    /// </summary>
    [Fact]
    public void TransparentUi_IsInstallerOnly()
        => Assert.True(Get("heaven-1656").ExternalInstallerOnly);

    /// <summary>
    /// Everything ships under a wrapper folder that AddonPaths strips, and the
    /// automatic rules cover the rest, so no list is needed.
    /// </summary>
    [Fact]
    public void GunSmoke_UsesTheAutomaticRules()
    {
        var entry = Get("heaven-3730");

        Assert.Null(entry.IncludeOnly);
        Assert.False(entry.ExternalInstallerOnly);
    }

    [Fact]
    public void EveryEntry_HasWhatTheUiAndDownloaderNeed()
    {
        Assert.NotEmpty(AddonRegistry.All);
        foreach (var a in AddonRegistry.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(a.Id));
            Assert.False(string.IsNullOrWhiteSpace(a.HeavenFileId));
            Assert.False(string.IsNullOrWhiteSpace(a.Name));
            // Both languages: a blank one would render as an empty description.
            Assert.False(string.IsNullOrWhiteSpace(a.DescriptionFor("en")));
            Assert.False(string.IsNullOrWhiteSpace(a.DescriptionFor("es")));
            // The page link is shown to users, so it goes through the same gate
            // as any other mod-supplied url.
            Assert.True(SafeUrl.IsAllowed(a.SourceUrl));
        }
    }

    [Fact]
    public void Find_IsCaseInsensitive_AndNullSafe()
    {
        Assert.NotNull(AddonRegistry.Find("HEAVEN-1932"));
        Assert.Null(AddonRegistry.Find("nope"));
        Assert.Null(AddonRegistry.Find(null));
    }
}
