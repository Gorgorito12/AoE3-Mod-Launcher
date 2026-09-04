using System.Linq;
using WarsOfLibertyLauncher.Models;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins the pure (no disk write) config migrations that <see cref="LauncherConfig.Load"/>
/// runs over an old config. Exercised directly — each caller only adds the <c>Save()</c>.
///
/// <para><see cref="LauncherConfig.ApplyDeprecatedTranslationsFolderRepoMigration"/>: the
/// fold of the deprecated single-string <c>translationsFolderRepo</c> into the multi-repo
/// model.</para>
///
/// <para><see cref="LauncherConfig.ApplyDeveloperModeResetMigration"/>: the one-time
/// switch-off of developer mode for the people who had turned it on while its switch was
/// still in plain sight in GENERAL.</para>
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

    // ------------------------------------------------ retiring an already-on developer mode

    /// <summary>
    /// Somebody who had switched developer mode on back when its switch was a visible row at
    /// the bottom of GENERAL. Hiding the block did nothing for them — a persisted
    /// <c>developerMode: true</c> kept the whole thing on screen — so it is switched off
    /// once.
    /// </summary>
    [Fact]
    public void DeveloperMode_IsRetiredOnceForSomebodyWhoHadIt()
    {
        var cfg = new LauncherConfig { DeveloperMode = true };

        Assert.True(cfg.ApplyDeveloperModeResetMigration());
        Assert.False(cfg.DeveloperMode);
        Assert.True(cfg.DeveloperModeRetired);
    }

    /// <summary>
    /// THE ONE THAT MATTERS. Somebody unlocked it again with the seven-tap gesture; the next
    /// launch must leave it alone.
    ///
    /// <para>This is why the migration is keyed off the MARKER and never off "the flag is
    /// true". Read from the flag it would run every launch, and the gesture would buy exactly
    /// one session before the block closed again with nothing to explain it. It is the mirror
    /// of <c>BackgroundStartupPlanTests.OptedOut_NeverReArms</c> — there a default that
    /// refuses to stay off, here a setting that refuses to stay on — and both are the same
    /// bug: the launcher overriding a choice the user made on purpose.</para>
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_ReUnlockingSurvivesEveryLaunch()
    {
        var cfg = new LauncherConfig { DeveloperMode = true, DeveloperModeRetired = true };

        Assert.False(cfg.ApplyDeveloperModeResetMigration());
        Assert.True(cfg.DeveloperMode);
    }

    /// <summary>
    /// The marker is set even for somebody who never had it on, so the migration never has to
    /// look again. Costs one config save on one launch, and it is what makes the case above
    /// reachable: without it the flag would be the only state there is.
    /// </summary>
    [Fact]
    public void DeveloperMode_TheMarkerIsSetEvenWhenItWasAlreadyOff()
    {
        var cfg = new LauncherConfig { DeveloperMode = false };

        Assert.True(cfg.ApplyDeveloperModeResetMigration());
        Assert.True(cfg.DeveloperModeRetired);
        Assert.False(cfg.DeveloperMode);
    }

    /// <summary>
    /// It takes the TOOLS away and not the content. A mod added from a local <c>mod.json</c>
    /// can be installed, so forgetting its path would orphan a real install with no active
    /// mod to return to and no way to uninstall it from the UI.
    /// </summary>
    [Fact]
    public void DeveloperMode_RetiringItLeavesTheLocalModsAlone()
    {
        var cfg = new LauncherConfig
        {
            DeveloperMode = true,
            LocalCatalogModPaths = new System.Collections.Generic.List<string>
            {
                @"C:\mods\struggle-of-indonesia\mod.json",
            },
        };

        Assert.True(cfg.ApplyDeveloperModeResetMigration());
        Assert.Single(cfg.LocalCatalogModPaths);
    }

    [Fact]
    public void DeveloperMode_RetireIsIdempotent()
    {
        var cfg = new LauncherConfig { DeveloperMode = true };

        Assert.True(cfg.ApplyDeveloperModeResetMigration());
        Assert.False(cfg.ApplyDeveloperModeResetMigration());
    }

    /// <summary>
    /// A fresh config starts unretired, or a new install would skip the migration and carry
    /// the marker without it ever having run.
    /// </summary>
    [Fact]
    public void DeveloperMode_ConfigDefaults_StartUnretiredAndOff()
    {
        var cfg = new LauncherConfig();

        Assert.False(cfg.DeveloperModeRetired);
        Assert.False(cfg.DeveloperMode);
    }
}
