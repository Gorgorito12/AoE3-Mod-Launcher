using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins the multi-repo translation source resolution: the active profile's own
/// folder repo (default) FIRST, then the user's extra repos (de-duplicated),
/// all fetched and merged. The participation gate and the master disable switch
/// both yield an empty list. See <see cref="UpdateService.EffectiveTranslationsFolderRepos"/>.
/// </summary>
public class MultiTranslationRepoTests
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
    public void FolderRepos_DefaultOnly_ReturnsProfileRepo()
    {
        var cfg = new LauncherConfig();
        var svc = new UpdateService(cfg, ParticipatingProfile());

        Assert.Equal(new[] { "Gorgorito12/translations" }, svc.EffectiveTranslationsFolderRepos());
    }

    [Fact]
    public void FolderRepos_WithExtras_DefaultFirstThenExtrasInOrder()
    {
        var cfg = new LauncherConfig
        {
            ExtraTranslationsFolderRepos = new[] { "alice/es-pack", "bob/fr-pack" },
        };
        var svc = new UpdateService(cfg, ParticipatingProfile());

        Assert.Equal(
            new[] { "Gorgorito12/translations", "alice/es-pack", "bob/fr-pack" },
            svc.EffectiveTranslationsFolderRepos());
    }

    [Fact]
    public void FolderRepos_ExtraEqualToDefault_IsDeduped()
    {
        var cfg = new LauncherConfig
        {
            ExtraTranslationsFolderRepos = new[] { "Gorgorito12/translations", "alice/es-pack" },
        };
        var svc = new UpdateService(cfg, ParticipatingProfile());

        Assert.Equal(
            new[] { "Gorgorito12/translations", "alice/es-pack" },
            svc.EffectiveTranslationsFolderRepos());
    }

    [Fact]
    public void FolderRepos_InvalidExtrasDropped()
    {
        var cfg = new LauncherConfig
        {
            ExtraTranslationsFolderRepos = new[] { "not-a-repo", "", "alice/es-pack" },
        };
        var svc = new UpdateService(cfg, ParticipatingProfile());

        Assert.Equal(
            new[] { "Gorgorito12/translations", "alice/es-pack" },
            svc.EffectiveTranslationsFolderRepos());
    }

    [Fact]
    public void FolderRepos_Disabled_ReturnsEmpty()
    {
        var cfg = new LauncherConfig
        {
            CommunityTranslationsDisabled = true,
            ExtraTranslationsFolderRepos = new[] { "alice/es-pack" },
        };
        var svc = new UpdateService(cfg, ParticipatingProfile());

        Assert.Empty(svc.EffectiveTranslationsFolderRepos());
    }

    [Fact]
    public void FolderRepos_NonParticipatingProfile_Empty()
    {
        var cfg = new LauncherConfig { ExtraTranslationsFolderRepos = new[] { "alice/es-pack" } };
        var svc = new UpdateService(cfg, NonParticipatingProfile());

        Assert.Empty(svc.EffectiveTranslationsFolderRepos());
    }

    [Fact]
    public void ReleasesRepo_DefaultWhenParticipatingAndEnabled()
    {
        var svc = new UpdateService(new LauncherConfig(), ParticipatingProfile());
        Assert.Equal("papillo12/translations", svc.EffectiveTranslationsRepo());
    }

    [Fact]
    public void ReleasesRepo_EmptyWhenDisabled()
    {
        var cfg = new LauncherConfig { CommunityTranslationsDisabled = true };
        var svc = new UpdateService(cfg, ParticipatingProfile());
        Assert.Equal("", svc.EffectiveTranslationsRepo());
    }

    [Fact]
    public void ReleasesRepo_EmptyWhenNonParticipating()
    {
        var svc = new UpdateService(new LauncherConfig(), NonParticipatingProfile());
        Assert.Equal("", svc.EffectiveTranslationsRepo());
    }

    // ------ EffectiveTranslationSources: one list for every kind of source

    private const string JuanIndex = "https://traducciones.example.com/translations-index.json";

    [Fact]
    public void Sources_OfficialFirstThenAddedRepoThenAddedLinks()
    {
        var cfg = new LauncherConfig
        {
            ExtraTranslationsFolderRepos = new[] { "alice/es-pack" },
            ExtraTranslationIndexUrls = new[] { JuanIndex },
        };
        var all = new UpdateService(cfg, ParticipatingProfile()).EffectiveTranslationSources().All;

        Assert.Equal(new[] { "gh:gorgorito12/translations", "ghr:papillo12/translations", "gh:alice/es-pack", "url:" + JuanIndex },
            all.Select(s => s.Source.Key));
        Assert.Equal(new[] { true, true, false, false }, all.Select(s => s.IsOfficial));
    }

    /// <summary>
    /// Official is decided by IDENTITY: the player re-adding the mod's own repo doesn't make a
    /// second, unofficial copy of it.
    /// </summary>
    [Fact]
    public void Sources_TheModsOwnRepoAddedAgain_StaysOneOfficialSource()
    {
        var cfg = new LauncherConfig { ExtraTranslationsFolderRepos = new[] { "gorgorito12/TRANSLATIONS" } };
        var all = new UpdateService(cfg, ParticipatingProfile()).EffectiveTranslationSources().All;

        var own = Assert.Single(all, s => s.Source.Kind == TranslationSourceKind.GitHubFolder);
        Assert.True(own.IsOfficial);
    }

    /// <summary>THE GATE: a source the player added can't inject packs into a mod that takes none.</summary>
    [Fact]
    public void Sources_NonParticipatingProfile_Empty()
    {
        var cfg = new LauncherConfig
        {
            ExtraTranslationsFolderRepos = new[] { "alice/es-pack" },
            ExtraTranslationIndexUrls = new[] { JuanIndex },
        };
        Assert.True(new UpdateService(cfg, NonParticipatingProfile()).EffectiveTranslationSources().IsEmpty);
    }

    [Fact]
    public void Sources_Disabled_Empty()
    {
        var cfg = new LauncherConfig { CommunityTranslationsDisabled = true, ExtraTranslationIndexUrls = new[] { JuanIndex } };
        Assert.True(new UpdateService(cfg, ParticipatingProfile()).EffectiveTranslationSources().IsEmpty);
    }

    [Fact]
    public void Sources_OnlyUnofficial_KeepsWhatThePlayerAdded()
    {
        var cfg = new LauncherConfig { ExtraTranslationIndexUrls = new[] { JuanIndex } };
        var added = new UpdateService(cfg, ParticipatingProfile()).EffectiveTranslationSources().OnlyUnofficial();
        Assert.Equal("url:" + JuanIndex, Assert.Single(added.All).Source.Key);
    }
}
