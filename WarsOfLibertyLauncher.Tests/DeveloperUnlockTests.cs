using System;
using WarsOfLibertyLauncher;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The tap run that opens the developer block.
///
/// <para><b>What this does not claim.</b> Developer mode is not protected and cannot be:
/// it is a boolean in the player's own <c>launcher-config.json</c>, and what it gates is
/// author tooling that no server treats differently — the lobby has no role column at all,
/// deliberately. The switch was removed from Settings → General so the tools stop
/// advertising themselves to every player, not so they become unreachable.</para>
///
/// <para>So the property worth pinning is the one that would make the door open by
/// ACCIDENT.</para>
/// </summary>
public class DeveloperUnlockTests
{
    private static readonly DateTime T0 = new(2026, 9, 3, 14, 0, 0, DateTimeKind.Utc);

    private static bool RunOf(int count, Func<int, TimeSpan> gap)
    {
        int taps = 0;
        var last = DateTime.MinValue;
        for (int i = 0; i < count; i++)
        {
            var now = T0 + gap(i);
            var tap = LauncherSettingsDialog.CountUnlockTap(taps, last, now);
            taps = tap.Taps;
            last = now;
            if (tap.Unlocked) return true;
        }
        return false;
    }

    /// <summary>Six is not seven, and the seventh is the one that opens it.</summary>
    [Theory]
    [InlineData(6, false)]
    [InlineData(7, true)]
    public void ItTakesSevenInARow(int taps, bool expected)
    {
        Assert.Equal(expected, RunOf(taps, i => TimeSpan.FromMilliseconds(200 * i)));
    }

    /// <summary>
    /// THE ONE THAT MATTERS. A run that pauses is a run that ended.
    ///
    /// <para>Without the window the count would accumulate for the life of the dialog, so
    /// somebody who clicks the version line idly while reading it — a few taps now, a few
    /// next week — would eventually open a panel they never asked for, with nothing on
    /// screen to explain where it came from. Twenty slow taps must open nothing.</para>
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_ASlowRunNeverGetsThere()
    {
        Assert.False(RunOf(20, i => TimeSpan.FromSeconds(30 * i)));
    }

    /// <summary>
    /// And the pause resets to zero rather than decrementing: six taps, a pause, then six
    /// more is still not seven in a row.
    /// </summary>
    [Fact]
    public void APauseStartsTheCountOver()
    {
        int taps = 0;
        var last = DateTime.MinValue;

        for (int i = 0; i < 6; i++)
        {
            var now = T0 + TimeSpan.FromMilliseconds(200 * i);
            var tap = LauncherSettingsDialog.CountUnlockTap(taps, last, now);
            taps = tap.Taps;
            last = now;
            Assert.False(tap.Unlocked);
        }
        Assert.Equal(6, taps);

        var afterPause = T0 + TimeSpan.FromMinutes(5);
        var resumed = LauncherSettingsDialog.CountUnlockTap(taps, last, afterPause);
        Assert.False(resumed.Unlocked);
        Assert.Equal(1, resumed.Taps);
    }

    /// <summary>
    /// The very first tap of a session, with no previous one to compare against, counts as
    /// one and not as a completion. <c>DateTime.MinValue</c> is the real starting state.
    /// </summary>
    [Fact]
    public void TheFirstTapEverIsJustTheFirstTap()
    {
        var tap = LauncherSettingsDialog.CountUnlockTap(0, DateTime.MinValue, T0);

        Assert.False(tap.Unlocked);
        Assert.Equal(1, tap.Taps);
    }
}
