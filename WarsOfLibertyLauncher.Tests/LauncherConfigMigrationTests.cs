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
///
/// <para><see cref="LauncherConfig.ApplyShareDecksDefaultMigration"/>: the one-time switch-ON
/// of deck sharing. That one goes the other way and is the more delicate of the two - it
/// starts sending something, so the marker is what has to make "off" mean off.</para>
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
    /// THE ONE THAT MATTERS. Somebody who already has the launcher starts sharing.
    ///
    /// <para>Changing the property's default alone would have done NOTHING for them: the whole
    /// config is serialised on every save, so <c>shareDeckStats: false</c> is already written
    /// in every file that exists and deserialisation puts it straight back over the new
    /// default. Only a migration reaches them, which is the whole reason this one exists.</para>
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_AConfigThatNeverChoseStartsSharing()
    {
        // What an existing install looks like: the flag written false, no marker.
        var cfg = new LauncherConfig { ShareDeckStats = false };

        Assert.True(cfg.ApplyShareDecksDefaultMigration());
        Assert.True(cfg.ShareDeckStats);
        Assert.True(cfg.ShareDecksDefaultSeeded);
    }

    /// <summary>
    /// And having turned it off, it STAYS off - through this launch and every one after.
    ///
    /// <para>This is the half that makes the switch mean anything. Key the migration off "the
    /// flag is false" instead of off the marker and turning it off would be undone at the next
    /// start, which is not a setting, it is a countdown. It is also the "disableable" half of
    /// the code-signing terms this data collection is disclosed under.</para>
    /// </summary>
    [Fact]
    public void TurningItOffSurvivesEveryLaunch()
    {
        var cfg = new LauncherConfig { ShareDeckStats = false, ShareDecksDefaultSeeded = true };

        for (var launch = 0; launch < 3; launch++)
        {
            Assert.False(cfg.ApplyShareDecksDefaultMigration());
            Assert.False(cfg.ShareDeckStats);
        }
    }

    /// <summary>A fresh install shares from the start, with no migration involved.</summary>
    [Fact]
    public void AFreshConfigSharesFromTheStart()
    {
        Assert.True(new LauncherConfig().ShareDeckStats);
    }

    /// <summary>
    /// The marker is set even when nothing else changed, so the migration never looks twice.
    ///
    /// <para>Same shape as the developer-mode one: one config save on one launch buys never
    /// having to ask again.</para>
    /// </summary>
    [Fact]
    public void SomebodyAlreadySharingIsMarkedAndLeftAlone()
    {
        var cfg = new LauncherConfig { ShareDeckStats = true };

        Assert.True(cfg.ApplyShareDecksDefaultMigration());
        Assert.True(cfg.ShareDeckStats);
        Assert.True(cfg.ShareDecksDefaultSeeded);
    }

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

    // ---------------------------------------------------------------------
    // ApplyUpdateInfoUrlMigration — the two UpdateInfo overrides that earlier
    // builds shipped as non-empty DEFAULTS and stamped into every config.
    // ---------------------------------------------------------------------

    /// <summary>
    /// The whole point: UpdateService prefers a non-empty config value over the mod profile,
    /// so these two defaults shadowed the built-in profile's corrected URLs for every existing
    /// user. Clearing them hands resolution back to the profile.
    /// </summary>
    [Fact]
    public void UpdateInfoUrls_TheStaleDefaults_AreCleared()
    {
        var cfg = new LauncherConfig
        {
            UpdateInfoUrl = LauncherConfig.StaleUpdateInfoUrl,
            UpdateInfoUrlAlt = LauncherConfig.StaleUpdateInfoUrlAlt,
        };

        Assert.True(cfg.ApplyUpdateInfoUrlMigration());
        Assert.Equal("", cfg.UpdateInfoUrl);
        Assert.Equal("", cfg.UpdateInfoUrlAlt);
    }

    /// <summary>
    /// THE ONE THAT MATTERS. This migration heals a default nobody chose; it must never
    /// overwrite a mirror somebody deliberately configured. A config carrying custom URLs is
    /// reported as unchanged and left byte-for-byte alone.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_ACustomMirrorIsNeverTouched()
    {
        var cfg = new LauncherConfig
        {
            UpdateInfoUrl = "https://my-own-mirror.example/UpdateInfo.xml",
            UpdateInfoUrlAlt = "https://my-own-mirror.example/alt/UpdateInfo.xml",
        };

        Assert.False(cfg.ApplyUpdateInfoUrlMigration());
        Assert.Equal("https://my-own-mirror.example/UpdateInfo.xml", cfg.UpdateInfoUrl);
        Assert.Equal("https://my-own-mirror.example/alt/UpdateInfo.xml", cfg.UpdateInfoUrlAlt);
    }

    /// <summary>
    /// The match is the WHOLE value, not the host. The corrected HTTP fallback the built-in
    /// profile uses points at the same host as the stale primary, so a substring match here
    /// would clear a perfectly good override.
    /// </summary>
    [Fact]
    public void UpdateInfoUrls_SameHostButADifferentUrl_Survives()
    {
        var cfg = new LauncherConfig { UpdateInfoUrl = "https://aoe3wol.com/updates/UpdateInfo.xml" };

        Assert.False(cfg.ApplyUpdateInfoUrlMigration());
        Assert.Equal("https://aoe3wol.com/updates/UpdateInfo.xml", cfg.UpdateInfoUrl);
    }

    /// <summary>Each field is judged on its own — clearing one must not clear the other.</summary>
    [Fact]
    public void UpdateInfoUrls_OnlyTheStaleOneOfThePairIsCleared()
    {
        var cfg = new LauncherConfig
        {
            UpdateInfoUrl = LauncherConfig.StaleUpdateInfoUrl,
            UpdateInfoUrlAlt = "https://my-own-mirror.example/alt/UpdateInfo.xml",
        };

        Assert.True(cfg.ApplyUpdateInfoUrlMigration());
        Assert.Equal("", cfg.UpdateInfoUrl);
        Assert.Equal("https://my-own-mirror.example/alt/UpdateInfo.xml", cfg.UpdateInfoUrlAlt);
    }

    /// <summary>Idempotent: the second run has nothing left to clear.</summary>
    [Fact]
    public void UpdateInfoUrls_Migration_IsIdempotent()
    {
        var cfg = new LauncherConfig { UpdateInfoUrl = LauncherConfig.StaleUpdateInfoUrl };

        Assert.True(cfg.ApplyUpdateInfoUrlMigration());
        Assert.False(cfg.ApplyUpdateInfoUrlMigration());
    }

    /// <summary>
    /// A fresh config must ship these EMPTY. A non-empty default is the entire bug: it
    /// overrides the profile for everybody, and it is serialised on the first Save().
    /// </summary>
    [Fact]
    public void UpdateInfoUrls_ConfigDefaults_AreEmptySoTheProfileWins()
    {
        var cfg = new LauncherConfig();

        Assert.Equal("", cfg.UpdateInfoUrl);
        Assert.Equal("", cfg.UpdateInfoUrlAlt);
    }

    // -- Mod id rename (MigrateModId) -----------------------------------------
    //
    // A catalog rename moves a mod's folder AND its id. Everything this config keys
    // by id has to follow, or the user's install goes invisible while still sitting
    // on disk. These pin each surface separately, because the failure mode of missing
    // one is silent: the launcher just quietly forgets something.

    private const string OldId = "knights-and-barbarians";
    private const string NewId = "knights-and-barbarians-remastered";

    private static LauncherConfig ConfigWithOldMod(string installPath = @"C:\Games\KnB")
    {
        var cfg = new LauncherConfig
        {
            ActiveModId = OldId,
            UserModIds = { "improvement-mod", OldId },
            FavoriteModIds = { OldId },
            NotifiedCatalogModIds = { OldId },
        };
        cfg.Mods[OldId] = new ModState { InstallPath = installPath };
        cfg.NotifiedCatalogVersions[OldId] = "1.3.6c";
        cfg.Notifications.Add(new NotificationItem { ModId = OldId });
        return cfg;
    }

    [Fact]
    public void MigrateModId_MovesEverythingKeyedById()
    {
        var cfg = ConfigWithOldMod();

        Assert.True(cfg.MigrateModId(OldId, NewId));

        Assert.Equal(NewId, cfg.ActiveModId);
        Assert.False(cfg.Mods.ContainsKey(OldId));
        Assert.Equal(@"C:\Games\KnB", cfg.Mods[NewId].InstallPath);
        Assert.Equal(new[] { "improvement-mod", NewId }, cfg.UserModIds);
        Assert.Equal(new[] { NewId }, cfg.FavoriteModIds);
        Assert.Equal(new[] { NewId }, cfg.NotifiedCatalogModIds);
        Assert.Equal("1.3.6c", cfg.NotifiedCatalogVersions[NewId]);
        Assert.False(cfg.NotifiedCatalogVersions.ContainsKey(OldId));
        Assert.Equal(NewId, cfg.Notifications[0].ModId);
    }

    /// <summary>
    /// Position matters: UserModIds drives the Workshop ordering, so a rename must
    /// rewrite the entry in place rather than remove-and-append.
    /// </summary>
    [Fact]
    public void MigrateModId_KeepsCollectionOrdering()
    {
        var cfg = new LauncherConfig { UserModIds = { OldId, "improvement-mod", "wol" } };

        Assert.True(cfg.MigrateModId(OldId, NewId));

        Assert.Equal(new[] { NewId, "improvement-mod", "wol" }, cfg.UserModIds);
    }

    [Fact]
    public void MigrateModId_IsIdempotent()
    {
        var cfg = ConfigWithOldMod();

        Assert.True(cfg.MigrateModId(OldId, NewId));
        Assert.False(cfg.MigrateModId(OldId, NewId));
        Assert.Equal(new[] { "improvement-mod", NewId }, cfg.UserModIds);
    }

    /// <summary>
    /// GetState() auto-vivifies an empty record the moment anything asks about the new
    /// id. That placeholder must not shadow the real saved install path.
    /// </summary>
    [Fact]
    public void MigrateModId_EmptyDestination_TakesTheOldInstallPath()
    {
        var cfg = ConfigWithOldMod();
        cfg.Mods[NewId] = new ModState();

        Assert.True(cfg.MigrateModId(OldId, NewId));

        Assert.Equal(@"C:\Games\KnB", cfg.Mods[NewId].InstallPath);
        Assert.False(cfg.Mods.ContainsKey(OldId));
    }

    /// <summary>
    /// The other direction: a real install already recorded under the new id is newer
    /// than anything the old key remembers, and is never clobbered.
    /// </summary>
    [Fact]
    public void MigrateModId_LiveDestination_IsNeverClobbered()
    {
        var cfg = ConfigWithOldMod();
        cfg.Mods[NewId] = new ModState { InstallPath = @"D:\Games\Remastered" };

        Assert.True(cfg.MigrateModId(OldId, NewId));

        Assert.Equal(@"D:\Games\Remastered", cfg.Mods[NewId].InstallPath);
        Assert.False(cfg.Mods.ContainsKey(OldId));
    }

    [Fact]
    public void MigrateModId_UnknownOrDegenerateInput_ChangesNothing()
    {
        var cfg = ConfigWithOldMod();

        Assert.False(cfg.MigrateModId("never-published", NewId));
        Assert.False(cfg.MigrateModId(OldId, OldId));
        Assert.False(cfg.MigrateModId("", NewId));
        Assert.False(cfg.MigrateModId(OldId, "   "));
        Assert.Equal(OldId, cfg.ActiveModId);
        Assert.True(cfg.Mods.ContainsKey(OldId));
    }

    /// <summary>
    /// A pending settings import names its SOURCE mod, so it can sit under any other
    /// mod's record — every record is checked, not just the renamed one's.
    /// </summary>
    [Fact]
    public void MigrateModId_RewritesPendingSettingsImportOnAnyMod()
    {
        var cfg = ConfigWithOldMod();
        cfg.Mods["improvement-mod"] = new ModState { PendingSettingsImportFrom = OldId };

        Assert.True(cfg.MigrateModId(OldId, NewId));

        Assert.Equal(NewId, cfg.Mods["improvement-mod"].PendingSettingsImportFrom);
    }

    /// <summary>
    /// The common case by far: no mod declares a previousId, so a catalog refresh must
    /// not report a change — otherwise the config is rewritten on every single refresh.
    /// </summary>
    [Fact]
    public void ApplyModRenames_NoPreviousIds_IsANoOp()
    {
        var cfg = ConfigWithOldMod();
        var profiles = new[] { new ModProfile { Id = "improvement-mod" } };

        Assert.False(cfg.ApplyModRenames(profiles));
        Assert.False(cfg.ApplyModRenames(null));
        Assert.Equal(OldId, cfg.ActiveModId);
    }
}
