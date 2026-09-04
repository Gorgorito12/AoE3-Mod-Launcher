using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="StartupRegistrationService.PlanStartup"/> and
/// <see cref="StartupRegistrationService.PlanAnswer"/> — the decision behind "run in
/// background", which is the only thing this launcher does that changes what the machine
/// does at logon.
///
/// Three facts make this worth testing rather than eyeballing:
///
/// (1) The Settings checkbox reads the REGISTRY, not the config, and only
///     StartupRegistrationService.Apply writes the Run key. So changing the config alone
///     changes NOTHING the user can see — it becomes real only when something writes.
///
/// (2) That write must be keyed off the seed MARKER, never off "the Run key is
///     missing". Keyed off the key, unchecking the toggle (which deletes it) would
///     silently re-enable auto-start at the next launch. A default that refuses to
///     stay off is malware behaviour — <see cref="OptedOut_NeverReArms"/> is the test
///     that exists to catch that, and it is the reason this class exists.
///
/// (3) And the newer one: an unseeded config is ASKED, not written. The Run key used to
///     be seeded on the first launch and announced afterwards by a tray balloon. A notice
///     after a registry write is not consent — it cannot be answered and is easy to miss —
///     so the unseeded case now writes nothing at all and returns
///     <c>AskFirst</c>. <see cref="NeverAsked_WritesNothingAndAsks"/> is what keeps that
///     true, and it is as load-bearing as (2).
/// </summary>
public class BackgroundStartupPlanTests
{
    /// <summary>
    /// THE ONE THAT MATTERS FOR CONSENT. Brand-new config, no Run key: the launcher must
    /// ask, and must touch nothing in the meantime.
    ///
    /// <para>Register is false AND SeedNow is false, which together mean the caller does not
    /// call Apply at all. Both halves matter: Apply(true) is the old silent registration,
    /// and Apply(false) would DELETE a key it has no business deleting — a launcher sharing
    /// a machine with an older copy of itself could clear that copy's registration before
    /// anybody was asked anything.</para>
    /// </summary>
    [Fact]
    public void NeverAsked_WritesNothingAndAsks()
    {
        var plan = StartupRegistrationService.PlanStartup(
            alreadySeeded: false, startWithWindows: true, alreadyRegistered: false);

        Assert.True(plan.AskFirst);
        Assert.False(plan.SeedNow);
        Assert.False(plan.Register);
    }

    /// <summary>
    /// An EXISTING config from before any of this: it carries a persisted
    /// startWithWindows=false, which means "never chose" (the toggle used to default off),
    /// not "declined". It is asked too — the flag must not be read as an answer, or a user
    /// who never chose would be silently opted out of a question they were never put.
    /// </summary>
    [Fact]
    public void ExistingConfig_WithFlagOff_IsStillAsked()
    {
        var plan = StartupRegistrationService.PlanStartup(
            alreadySeeded: false, startWithWindows: false, alreadyRegistered: false);

        Assert.True(plan.AskFirst);
        Assert.False(plan.Register);   // the flag is not consulted, in either direction
    }

    /// <summary>
    /// Someone who had already switched auto-start on by hand: nothing would change, so
    /// there is nothing to ask. Seed quietly and keep their key.
    /// </summary>
    [Fact]
    public void AlreadyRegisteredByHand_SeedsQuietly()
    {
        var plan = StartupRegistrationService.PlanStartup(
            alreadySeeded: false, startWithWindows: true, alreadyRegistered: true);

        Assert.False(plan.AskFirst);
        Assert.True(plan.SeedNow);
        Assert.True(plan.Register);
    }

    /// <summary>
    /// THE ONE THAT MATTERS. The user said no, or unchecked the toggle later: seeded, flag
    /// off, no Run key. The next launch must not bring it back — no seed, no registration,
    /// and above all no second asking. If this ever goes red, the launcher is re-arming
    /// auto-start against the user's explicit choice.
    /// </summary>
    [Fact]
    public void OptedOut_NeverReArms()
    {
        var plan = StartupRegistrationService.PlanStartup(
            alreadySeeded: true, startWithWindows: false, alreadyRegistered: false);

        Assert.False(plan.AskFirst);
        Assert.False(plan.SeedNow);
        Assert.False(plan.Register);
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
    /// Steady state for a user who said yes: no re-seed, no second question, and the key is
    /// re-applied each launch (that's what self-heals the path when the portable exe moves).
    /// </summary>
    [Fact]
    public void SeededAndOn_ReAppliesWithoutAsking()
    {
        var plan = StartupRegistrationService.PlanStartup(
            alreadySeeded: true, startWithWindows: true, alreadyRegistered: true);

        Assert.False(plan.AskFirst);
        Assert.False(plan.SeedNow);
        Assert.True(plan.Register);
    }

    /// <summary>
    /// BOTH answers seed. A "no" that left the config unseeded would put the same question
    /// again on the next launch, and a question that keeps coming back until it gets the
    /// answer it wants is not a question. This is also what makes
    /// <see cref="OptedOut_NeverReArms"/> reachable from the very first launch rather than
    /// only after a forced write.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnAnswerIsRecordedWhicheverWayItWent(bool accepted)
    {
        var plan = StartupRegistrationService.PlanAnswer(accepted);

        Assert.True(plan.SeedNow);
        Assert.False(plan.AskFirst);
        Assert.Equal(accepted, plan.Register);
    }

    /// <summary>
    /// The whole round trip, which is the property the two functions only have together:
    /// decline, then relaunch, and the launcher neither asks again nor registers anything.
    /// </summary>
    [Fact]
    public void DecliningOnceIsTheEndOfIt()
    {
        var answer = StartupRegistrationService.PlanAnswer(accepted: false);

        // What the caller writes to the config after that answer.
        var next = StartupRegistrationService.PlanStartup(
            alreadySeeded: answer.SeedNow,
            startWithWindows: answer.Register,
            alreadyRegistered: false);

        Assert.False(next.AskFirst);
        Assert.False(next.Register);
        Assert.False(next.SeedNow);
    }

    /// <summary>
    /// The config defaults themselves. These are what a fresh install deserialises to, and
    /// what the answer overwrites — the three move together because they are one choice
    /// behind one switch, and a config where they disagree is the silent divergence this
    /// whole area exists to avoid.
    /// </summary>
    [Fact]
    public void ConfigDefaults_RunInBackgroundIsOn_AndUnseeded()
    {
        var cfg = new LauncherConfig();

        Assert.True(cfg.StartWithWindows);
        Assert.True(cfg.MinimizeToTray);
        Assert.True(cfg.StartMinimized);
        // Must start unseeded, or a fresh config would skip the question entirely and the
        // config would claim an answer nobody gave.
        Assert.False(cfg.BackgroundDefaultSeeded);
    }
}
