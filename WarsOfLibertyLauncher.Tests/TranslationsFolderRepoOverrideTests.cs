using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins the new global "translations source" override
/// (<see cref="LauncherConfig.TranslationsFolderRepo"/>, surfaced by the
/// Settings → TRANSLATIONS tab) as it flows through
/// <see cref="UpdateService.EffectiveTranslationsFolderRepo"/> and
/// <see cref="UpdateService.EffectiveTranslationsRepo"/>. The contract:
/// <list type="bullet">
///   <item><c>""</c> → the active profile's own folder repo (default); the
///     legacy releases path keeps its existing behaviour.</item>
///   <item><c>"none"</c> → both folder AND releases suppressed (no community
///     packs).</item>
///   <item><c>"owner/repo"</c> → that folder repo wins; releases suppressed so
///     the chosen repo is the single source.</item>
/// </list>
/// The participation gate is preserved: a profile with no Translations block
/// never receives packs, whatever the override says — so the override can't
/// inject foreign strings into a mod that opted out.
/// </summary>
public class TranslationsFolderRepoOverrideTests
{
    private static ModProfile ParticipatingProfile() => new()
    {
        Id = "wol",
        Translations = new TranslationsSettings
        {
            Repo = "papillo12/translations",
            FolderRepo = "Gorgorito12/translations",
        },
    };

    private static ModProfile NonParticipatingProfile() => new()
    {
        Id = "aoe3-tad",
        Translations = null,
    };

    [Fact]
    public void FolderRepo_DefaultOverride_UsesProfileFolderRepo()
    {
        var cfg = new LauncherConfig { TranslationsFolderRepo = "" };
        var svc = new UpdateService(cfg, ParticipatingProfile());

        Assert.Equal("Gorgorito12/translations", svc.EffectiveTranslationsFolderRepo());
    }

    [Fact]
    public void FolderRepo_CustomOverride_WinsOverProfile()
    {
        var cfg = new LauncherConfig { TranslationsFolderRepo = "someone/es-translations" };
        var svc = new UpdateService(cfg, ParticipatingProfile());

        Assert.Equal("someone/es-translations", svc.EffectiveTranslationsFolderRepo());
    }

    [Fact]
    public void FolderRepo_Disabled_ReturnsEmpty()
    {
        var cfg = new LauncherConfig { TranslationsFolderRepo = "none" };
        var svc = new UpdateService(cfg, ParticipatingProfile());

        Assert.Equal("", svc.EffectiveTranslationsFolderRepo());
    }

    [Theory]
    [InlineData("")]
    [InlineData("none")]
    [InlineData("someone/es-translations")]
    public void FolderRepo_NonParticipatingProfile_AlwaysEmpty(string override_)
    {
        // The gate wins over every override value: no Translations block → no packs.
        var cfg = new LauncherConfig { TranslationsFolderRepo = override_ };
        var svc = new UpdateService(cfg, NonParticipatingProfile());

        Assert.Equal("", svc.EffectiveTranslationsFolderRepo());
    }

    [Fact]
    public void ReleasesRepo_DefaultOverride_KeepsExistingBehaviour()
    {
        // "" folder override → releases path unchanged: the global releases repo
        // (non-empty default) still wins for a participating profile.
        var cfg = new LauncherConfig { TranslationsFolderRepo = "" };
        var svc = new UpdateService(cfg, ParticipatingProfile());

        Assert.Equal("papillo12/translations", svc.EffectiveTranslationsRepo());
    }

    [Theory]
    [InlineData("none")]
    [InlineData("someone/es-translations")]
    public void ReleasesRepo_SuppressedWhenFolderCustomOrDisabled(string folderOverride)
    {
        // Custom or disabled folder source → legacy releases suppressed so the
        // folder repo is the single source of truth.
        var cfg = new LauncherConfig { TranslationsFolderRepo = folderOverride };
        var svc = new UpdateService(cfg, ParticipatingProfile());

        Assert.Equal("", svc.EffectiveTranslationsRepo());
    }

    [Fact]
    public void ReleasesRepo_NonParticipatingProfile_Empty()
    {
        var cfg = new LauncherConfig { TranslationsFolderRepo = "" };
        var svc = new UpdateService(cfg, NonParticipatingProfile());

        Assert.Equal("", svc.EffectiveTranslationsRepo());
    }
}
