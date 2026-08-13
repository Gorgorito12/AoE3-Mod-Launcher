using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="GameSettingsStore.PlanGameRecording"/> — the rule that decides whether the
/// launcher writes into a player's Age of Empires III profile.
///
/// <para>Two things make this worth testing on its own. First, <b>an opt-out has to be final</b>:
/// recording is on by default, so something writes it unasked, and a default that re-enables
/// itself every launch is not a default. Second, <b>the marker is per mod</b> — five installed
/// mods means five separate profiles, and a single launcher-wide "already seeded" flag would
/// enable recording for whichever mod was launched first and silently skip the other four.</para>
///
/// <para>Same shape and the same reasoning as <see cref="BackgroundStartupPlanTests"/>, which
/// guards the identical invariant for auto-start.</para>
/// </summary>
public class GameRecordingPlanTests
{
    [Fact]
    public void FreshMod_SeedsAndNotifies()
    {
        var plan = GameSettingsStore.PlanGameRecording(
            applied: null, wants: true, noticeAlreadyShown: false);

        Assert.True(plan.Write);
        Assert.True(plan.Value);
        Assert.True(plan.ShowNotice);
    }

    /// <summary>
    /// The case a single launcher-wide marker would have failed. The notice has already been
    /// shown — because another mod was launched first — but this mod's own profile has still
    /// never been written, so it must be seeded anyway. Quietly: one notice per launcher, not one
    /// per mod.
    /// </summary>
    [Fact]
    public void EveryModIsSeededSeparately_NotJustTheFirst()
    {
        var plan = GameSettingsStore.PlanGameRecording(
            applied: null, wants: true, noticeAlreadyShown: true);

        Assert.True(plan.Write);
        Assert.True(plan.Value);
        Assert.False(plan.ShowNotice);
    }

    /// <summary>
    /// Recording is switched off and we have never touched this mod's profile. There is nothing
    /// to undo, so the launcher must not edit a profile on behalf of a feature the player turned
    /// off. This branch is the reason the marker is nullable rather than a plain bool.
    /// </summary>
    [Fact]
    public void OptedOut_NeverWritesToAProfileWeNeverTouched()
    {
        var plan = GameSettingsStore.PlanGameRecording(
            applied: null, wants: false, noticeAlreadyShown: false);

        Assert.False(plan.Write);
        Assert.False(plan.ShowNotice);
    }

    /// <summary>
    /// THE ONE THAT MATTERS. The player unchecked the box after we had enabled recording: write
    /// <c>false</c> once so the action has a visible effect, and then never again. If the second
    /// half of this ever goes red, the launcher is re-writing a profile on every single launch,
    /// and — worse — would undo the player turning recording off inside the game itself.
    /// </summary>
    [Fact]
    public void OptedOut_WritesFalseOnceAndNeverReArms()
    {
        var first = GameSettingsStore.PlanGameRecording(
            applied: true, wants: false, noticeAlreadyShown: true);

        Assert.True(first.Write);
        Assert.False(first.Value);
        Assert.False(first.ShowNotice);

        // Next launch, with the marker now recording what we wrote.
        var second = GameSettingsStore.PlanGameRecording(
            applied: false, wants: false, noticeAlreadyShown: true);

        Assert.False(second.Write);
    }

    /// <summary>
    /// Seeded, still wanted: leave it alone. The game rewrites the whole profile when it exits, so
    /// the setting may well have been changed since — in the game's own options screen, by the
    /// player. That is their choice and the launcher does not sit on top of it.
    /// </summary>
    [Fact]
    public void SeededAndStillWanted_LeavesTheProfileAlone()
    {
        var plan = GameSettingsStore.PlanGameRecording(
            applied: true, wants: true, noticeAlreadyShown: true);

        Assert.False(plan.Write);
    }

    [Fact]
    public void OptedBackIn_WritesTrueWithoutRepeatingTheNotice()
    {
        var plan = GameSettingsStore.PlanGameRecording(
            applied: false, wants: true, noticeAlreadyShown: true);

        Assert.True(plan.Write);
        Assert.True(plan.Value);
        Assert.False(plan.ShowNotice);   // they just turned it on themselves; don't explain it
    }

    /// <summary>
    /// The defaults have to line up or the feature is inert: recording wanted, no mod marked yet,
    /// and no notice shown. A config that started out "already applied" would never seed anyone.
    /// </summary>
    [Fact]
    public void ConfigDefaults_RecordingIsOnAndNoModIsMarked()
    {
        var config = new LauncherConfig();
        var state = new ModState();

        Assert.True(config.EnableGameRecording);
        Assert.False(config.GameRecordingNoticeShown);
        Assert.False(config.GameRecordingNoticePending);
        Assert.Null(state.GameRecordingApplied);

        // The host reminder starts audible. AoE3's per-match "Record Game" box does not inherit
        // from the profile setting (measured), so it must be ticked by hand every match — and
        // only the player may decide they no longer need telling.
        Assert.False(config.GameRecordingReminderMuted);

        // And those defaults really do produce a seed.
        var plan = GameSettingsStore.PlanGameRecording(
            state.GameRecordingApplied, config.EnableGameRecording, config.GameRecordingNoticeShown);
        Assert.True(plan.Write);
        Assert.True(plan.ShowNotice);
    }
}
