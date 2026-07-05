using System.Linq;
using WarsOfLibertyLauncher.Models;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="LauncherConfig.ApplyDeprecatedTranslationsFolderRepoMigration"/> —
/// the pure (no disk write) fold of the deprecated single-string
/// <c>translationsFolderRepo</c> into the multi-repo model. Exercised directly
/// (the caller <c>MigrateTranslationsFolderRepo</c> only adds the <c>Save()</c>).
/// </summary>
public class LauncherConfigMigrationTests
{
    [Fact]
    public void Migrate_None_DisablesCommunityTranslations()
    {
        var cfg = new LauncherConfig { TranslationsFolderRepo = "none" };

        var changed = cfg.ApplyDeprecatedTranslationsFolderRepoMigration();

        Assert.True(changed);
        Assert.True(cfg.CommunityTranslationsDisabled);
        Assert.Equal("", cfg.TranslationsFolderRepo);
        Assert.Empty(cfg.ExtraTranslationsFolderRepos);
    }

    [Fact]
    public void Migrate_CustomRepo_MovesIntoExtraList_AndClearsOldField()
    {
        var cfg = new LauncherConfig { TranslationsFolderRepo = "alice/es-pack" };

        var changed = cfg.ApplyDeprecatedTranslationsFolderRepoMigration();

        Assert.True(changed);
        Assert.False(cfg.CommunityTranslationsDisabled);
        Assert.Contains("alice/es-pack", cfg.ExtraTranslationsFolderRepos);
        Assert.Equal("", cfg.TranslationsFolderRepo);
    }

    [Fact]
    public void Migrate_Empty_IsNoOp()
    {
        var cfg = new LauncherConfig { TranslationsFolderRepo = "" };

        var changed = cfg.ApplyDeprecatedTranslationsFolderRepoMigration();

        Assert.False(changed);
        Assert.False(cfg.CommunityTranslationsDisabled);
        Assert.Empty(cfg.ExtraTranslationsFolderRepos);
    }

    [Fact]
    public void Migrate_CustomRepo_AlreadyInList_IsNotDuplicated()
    {
        var cfg = new LauncherConfig
        {
            TranslationsFolderRepo = "Alice/ES-Pack",           // differs only in case
            ExtraTranslationsFolderRepos = new[] { "alice/es-pack" },
        };

        cfg.ApplyDeprecatedTranslationsFolderRepoMigration();

        Assert.Single(cfg.ExtraTranslationsFolderRepos);
        Assert.Equal("", cfg.TranslationsFolderRepo);
    }

    [Fact]
    public void Migrate_IsIdempotent()
    {
        var cfg = new LauncherConfig { TranslationsFolderRepo = "alice/es-pack" };

        Assert.True(cfg.ApplyDeprecatedTranslationsFolderRepoMigration());
        // Second run: old field already cleared → nothing left to migrate.
        Assert.False(cfg.ApplyDeprecatedTranslationsFolderRepoMigration());
        Assert.Single(cfg.ExtraTranslationsFolderRepos);
    }
}
