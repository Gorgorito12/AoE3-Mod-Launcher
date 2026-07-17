using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="StartupRegistrationService.PlanStartup"/> — the decision behind the
/// ON-by-default "run in background" preference.
///
/// Two facts make this worth testing rather than eyeballing:
///
/// (1) The Settings checkbox reads the REGISTRY, not the config, and only
///     StartupRegistrationService.Apply writes the Run key. So flipping the config
///     default alone changes NOTHING the user can see — the default only becomes real
///     because an unseeded config triggers a one-time write.
///
/// (2) That write must be keyed off the seed MARKER, never off "the Run key is
///     missing". Keyed off the key, unchecking the toggle (which deletes it) would
///     silently re-enable auto-start at the next launch. A default that refuses to
///     stay off is malware behaviour — <see cref="OptedOut_NeverReArms"/> is the test
///     that exists to catch that, and it is the reason this class exists.
/// </summary>
public class BackgroundStartupPlanTests
{
    /// <summary>
    /// Brand-new config: nothing has ever been seeded, so the default is applied —
    /// register the Run key and tell the user we did.
    /// </summary>
    [Fact]
    public void FreshConfig_SeedsRegistersAndNotifies()
    {
        var plan = StartupRegistrationService.PlanStartup(
            alreadySeeded: false, startWithWindows: true, alreadyRegistered: false);

        Assert.True(plan.SeedNow);
        Assert.True(plan.Register);
        Assert.True(plan.ShowNotice);
    }

    /// <summary>
    /// An EXISTING config from before this default: it carries a persisted
    /// startWithWindows=false, which means "never chose" (the toggle used to default
    /// off), not "declined". It must still be seeded — so the plan must NOT read that
    /// flag to decide, or the new default would never reach current users.
    /// </summary>
    [Fact]
    public void ExistingConfig_WithFlagOff_StillSeeds()
    {
        var plan = StartupRegistrationService.PlanStartup(
            alreadySeeded: false, startWithWindows: false, alreadyRegistered: false);

        Assert.True(plan.SeedNow);
        Assert.True(plan.Register);   // seeding FORCES it on; the flag is not consulted
        Assert.True(plan.ShowNotice);
    }

    /// <summary>
    /// THE ONE THAT MATTERS. The user unchecked the toggle: seeded, flag off, Run key
    /// already gone. The next launch must not bring it back — no seed, no registration.
    /// If this ever goes red, the launcher is silently re-arming auto-start against the
    /// user's explicit choice.
    /// </summary>
    [Fact]
    public void OptedOut_NeverReArms()
    {
        var plan = StartupRegistrationService.PlanStartup(
            alreadySeeded: true, startWithWindows: false, alreadyRegistered: false);

        Assert.False(plan.SeedNow);
        Assert.False(plan.Register);
        Assert.False(plan.ShowNotice);
    }

    /// <summary>
    /// Opting out is final even if the Run key somehow still exists (a failed delete, a
    /// stale key written by an older build). Register=false clears it — the flag is the
    /// user's answer and the registry follows it, never the other way round.
    /// </summary>
    [Fact]
    public void OptedOut_WithStaleKeyPresent_ClearsItAndStaysOff()
    {
        var plan = StartupRegistrationService.PlanStartup(
            alreadySeeded: true, startWithWindows: false, alreadyRegistered: true);

        Assert.False(plan.SeedNow);
        Assert.False(plan.Register);
    }

    /// <summary>
    /// Steady state for a user who kept the default: no re-seed, and the key is
    /// re-applied each launch (that's what self-heals the path when the portable exe
    /// moves). The notice already fired once and must not nag.
    /// </summary>
    [Fact]
    public void SeededAndOn_ReAppliesWithoutNotice()
    {
        var plan = StartupRegistrationService.PlanStartup(
            alreadySeeded: true, startWithWindows: true, alreadyRegistered: true);

        Assert.False(plan.SeedNow);
        Assert.True(plan.Register);
        Assert.False(plan.ShowNotice);
    }

    /// <summary>
    /// Someone who had already switched auto-start on by hand before this default
    /// shipped: seeding is a no-op for them, so announcing it would be noise.
    /// </summary>
    [Fact]
    public void AlreadyRegisteredByHand_SeedsQuietly()
    {
        var plan = StartupRegistrationService.PlanStartup(
            alreadySeeded: false, startWithWindows: true, alreadyRegistered: true);

        Assert.True(plan.SeedNow);
        Assert.True(plan.Register);
        Assert.False(plan.ShowNotice);   // nothing actually changed for them
    }

    /// <summary>
    /// The config defaults themselves. These are what a fresh install deserialises to,
    /// and what the seed forces onto an old config — if any of them regress to false,
    /// "run in background" silently stops being the default.
    /// </summary>
    [Fact]
    public void ConfigDefaults_RunInBackgroundIsOn_AndUnseeded()
    {
        var cfg = new LauncherConfig();

        Assert.True(cfg.StartWithWindows);
        Assert.True(cfg.MinimizeToTray);
        Assert.True(cfg.StartMinimized);
        // Must start unseeded, or a fresh config would skip the Run-key write and the
        // default would be inert — config says "on", registry (and checkbox) say off.
        Assert.False(cfg.BackgroundDefaultSeeded);
    }
}
