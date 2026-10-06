using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// What a rank badge's light costs when nobody is looking at it.
///
/// <para>Two players reported the launcher "frozen" and at ~40 % CPU, and their logs showed the UI
/// thread redrawing back to back for hours — with the launcher in the tray. The cause: stopping a
/// badge used <c>BeginAnimation(property, null)</c>, which detaches an animation and leaves its
/// forever-repeating clock running until the garbage collector frees it, and an active clock makes
/// WPF draw a frame every refresh. Measured on a fast PC with the window in the tray: zero badges
/// running and 72 redraws a second still driven by their clocks. These tests read the clocks
/// themselves, because nothing on screen shows the difference.</para>
///
/// <para><b>Why the clocks are advanced by hand.</b> WPF only ticks animation clocks on a thread
/// that renders, and in this harness a window shown from the second test thread on never really
/// shows (loaded and visible both stay false — the same limitation <c>TrayStartParkingTests</c>
/// documents). So the time manager is ticked directly, by reflection, which is what a render
/// would have done.</para>
/// </summary>
// Serialised with the other WPF tests: AnimationsOverride and the foreground flag are STATICS.
[Collection("wpf-and-language")]
public class RankBadgeClockTests
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>Advances this thread's animation clocks as a render would.</summary>
    private static void Tick()
    {
        System.Threading.Thread.Sleep(30);
        var mcType = typeof(System.Windows.Media.Visual).Assembly.GetType("System.Windows.Media.MediaContext")!;
        var mc = mcType.GetMethod("From", Any, new[] { typeof(Dispatcher) })!.Invoke(null, new object[] { Dispatcher.CurrentDispatcher })!;
        var tm = mcType.GetProperty("TimeManager", Any)!.GetValue(mc)!;
        tm.GetType().GetMethod("Tick", Any, Type.EmptyTypes)!.Invoke(tm, null);
    }

    /// <summary>
    /// THE ONE THAT MATTERS. A stopped badge's clocks are STOPPED, not merely detached: with the
    /// old Stop every one of these was still Active afterwards, ticking until the next GC.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_StopStopsTheClocksAndDoesNotMerelyDetachThem()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            RankBadge.AnimationsOverride = true;
            try
            {
                var badge = RankBadge.Build(RankAge.Sovereign, "1", 28, "user-1");
                RankBadge.Start(badge);
                var clocks = RankBadge.ClocksOf(badge).ToList();
                Assert.Equal(RankBadge.AnimationCount(badge), clocks.Count);
                Tick();
                Assert.Contains(clocks, c => c.CurrentState == ClockState.Active);
                Assert.NotEqual("no active animation clocks", DiagnosticLog.DescribeActiveClocks(Dispatcher.CurrentDispatcher));

                RankBadge.Stop(badge);
                Tick();

                Assert.False(RankBadge.IsRunning(badge));
                Assert.All(clocks, c => Assert.Equal(ClockState.Stopped, c.CurrentState));
                // And WPF agrees there is nothing left to tick on this thread.
                Assert.Equal("no active animation clocks", DiagnosticLog.DescribeActiveClocks(Dispatcher.CurrentDispatcher));
            }
            finally { RankBadge.AnimationsOverride = null; }
        });
        Assert.Null(error);
    }

    /// <summary>The control: what the old Stop did. Detaching leaves the clock Active — the
    /// premise of the test above, kept here so that a WPF that ever stops detached clocks on its
    /// own is noticed rather than silently making the fix look necessary.</summary>
    [Fact]
    public void DetachingAForeverAnimationLeavesItsClockRunning()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var border = new Border();
            var clock = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever }
                .CreateClock();
            border.ApplyAnimationClock(UIElement.OpacityProperty, clock);
            Tick();
            Assert.Equal(ClockState.Active, clock.CurrentState);
            Assert.Contains("DoubleAnimation 1s Forever ×1", DiagnosticLog.DescribeActiveClocks(Dispatcher.CurrentDispatcher));

            border.ApplyAnimationClock(UIElement.OpacityProperty, null);
            Tick();
            Assert.Equal(ClockState.Active, clock.CurrentState); // still ticking, for nobody
            clock.Controller!.Stop();
        });
        Assert.Null(error);
    }

    /// <summary>Every light is drawn at <see cref="RankBadge.FrameRate"/>, not at the monitor's
    /// refresh rate — measured, 58 % of a core at 120 Hz against 21 % at 30.</summary>
    [Fact]
    public void EveryLightIsCappedAtTheBadgeFrameRate()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            RankBadge.AnimationsOverride = true;
            try
            {
                foreach (var badge in new FrameworkElement[]
                         {
                             RankBadge.Build(RankAge.Sovereign, "1", 28, "user-1"),
                             RankBadge.Build(RankAge.Colonial, "9", 24, "user-2"),
                             RankBadge.BuildRowBanner(RankAge.Sovereign),
                         })
                {
                    RankBadge.Start(badge);
                    var clocks = RankBadge.ClocksOf(badge);
                    Assert.NotEmpty(clocks);
                    Assert.All(clocks, c => Assert.Equal(RankBadge.FrameRate, Timeline.GetDesiredFrameRate(c.Timeline)));
                    RankBadge.Stop(badge);
                }
            }
            finally { RankBadge.AnimationsOverride = null; }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// The rule: a light runs only with the launcher in front, the badge on screen, and its window
    /// not minimized — a minimized window's content still reports itself visible, so the window
    /// state is the only thing that says so.
    /// </summary>
    [Theory]
    [InlineData(true, true, true, WindowState.Normal, true)]
    [InlineData(true, true, true, null, true)] // a badge in a popup has no window
    [InlineData(false, true, true, WindowState.Normal, false)] // another program in front
    [InlineData(true, true, true, WindowState.Minimized, false)]
    [InlineData(true, true, false, WindowState.Normal, false)] // hidden, e.g. in the tray
    [InlineData(true, false, true, WindowState.Normal, false)]
    public void ALightRunsOnlyWhileSomebodyCanSeeIt(bool active, bool loaded, bool visible, WindowState? state, bool runs)
        => Assert.Equal(runs, RankBadge.ShouldRun(active, loaded, visible, state));

    /// <summary>Going to the background stops what is running — the wiring of the rule above.</summary>
    [Fact]
    public void LosingTheForegroundStopsARunningLight()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            RankBadge.AnimationsOverride = true;
            try
            {
                var badge = RankBadge.Build(RankAge.Imperial, "2", 28, "user-1");
                RankBadge.Start(badge);
                Assert.True(RankBadge.IsRunning(badge));
                var clocks = RankBadge.ClocksOf(badge).ToList();

                RankBadge.SetAppActive(false);
                Tick();

                Assert.False(RankBadge.IsRunning(badge));
                Assert.All(clocks, c => Assert.Equal(ClockState.Stopped, c.CurrentState));
            }
            finally
            {
                RankBadge.SetAppActive(true);
                RankBadge.AnimationsOverride = null;
            }
        });
        Assert.Null(error);
    }
}
