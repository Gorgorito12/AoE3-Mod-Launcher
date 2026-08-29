using System;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="GameExitWatcher"/> — the thing that decides whether a finished match is
/// ever noticed.
///
/// <para><b>Why it needed its own type and its own tests.</b> <c>Process.Exited</c> used to be
/// the only trigger for the whole post-match pipeline, and it does not always fire: when AoE3
/// demands elevation the launcher can only start it through ShellExecute, and a medium
/// integrity process cannot hold a handle on a higher-integrity child. A real player's log
/// showed three launches and not one game-exit handler run — no recording read, no match
/// reported, no <c>game_ended</c> sent — with nothing on screen to suggest anything was
/// wrong.</para>
///
/// <para>The two rules worth reading are the ones that keep the fix from being worse than the
/// bug: it must not report an exit <em>before</em> the game has started (the UAC prompt makes
/// that a real window, not a hypothetical), and it must never report one twice when both
/// signals land.</para>
/// </summary>
public class GameExitWatcherTests
{
    /// <summary>A watcher whose liveness answer and clock the test drives directly.</summary>
    private static (GameExitWatcher Watcher, Func<int> Fired) Build(
        Func<bool> isAlive, Func<DateTime>? now = null)
    {
        var count = 0;
        var w = new GameExitWatcher(isAlive, () => count++, now);
        return (w, () => count);
    }

    [Fact]
    public void ItReportsTheExitOnceTheGameIsGone()
    {
        var alive = true;
        var (w, fired) = Build(() => alive);

        Assert.False(w.Poll());          // seen alive — nothing to report
        alive = false;
        Assert.True(w.Poll());

        Assert.Equal(1, fired());
        Assert.True(w.HasFired);
    }

    /// <summary>
    /// <b>The rule that keeps this from being worse than the silence it replaces.</b> On the
    /// elevated path the process does not exist while the UAC prompt is on screen, so the first
    /// ticks find nothing. Reporting an exit there would announce that the match had ended
    /// seconds before the game opened.
    /// </summary>
    [Fact]
    public void ItNeverReportsAnExitForAGameItHasNotSeenStart()
    {
        var alive = false;
        var (w, fired) = Build(() => alive);

        for (var i = 0; i < 20; i++) Assert.False(w.Poll());
        Assert.Equal(0, fired());

        // The prompt is answered and the game finally appears; only now can it end.
        alive = true;
        Assert.False(w.Poll());
        alive = false;
        Assert.True(w.Poll());
        Assert.Equal(1, fired());
    }

    /// <summary>
    /// Both signals run at once by design — the event when it works, the poll when it doesn't —
    /// so the once-only guard is what makes that safe. A second report would run the whole
    /// post-match flow twice, against a match context the first run had already cleared.
    /// </summary>
    [Fact]
    public void TheEventAndThePollTogetherStillReportExactlyOnce()
    {
        var alive = true;
        var (w, fired) = Build(() => alive);

        Assert.False(w.Poll());
        alive = false;

        Assert.True(w.SignalExited());   // the event got there first
        Assert.False(w.Poll());          // the tick that follows must stay quiet
        Assert.False(w.SignalExited());  // and so must a repeat of the event

        Assert.Equal(1, fired());
    }

    /// <summary>
    /// The event skips the seen-alive rule on purpose: it only exists because we were holding a
    /// handle on a real process, so it is proof rather than an inference.
    /// </summary>
    [Fact]
    public void TheEventIsTrustedEvenIfThePollNeverSawTheGame()
    {
        var (w, fired) = Build(() => false);

        Assert.True(w.SignalExited());
        Assert.Equal(1, fired());
    }

    /// <summary>
    /// A probe that throws says nothing about whether the game is running. Treating that as an
    /// exit would report a match while the player is still inside it — which is the one mistake
    /// here that moves somebody's rating.
    /// </summary>
    [Fact]
    public void AProbeThatThrowsIsNotAnExit()
    {
        var explode = false;
        var (w, fired) = Build(() => explode ? throw new InvalidOperationException("nope") : true);

        Assert.False(w.Poll());          // alive
        explode = true;
        for (var i = 0; i < 5; i++) Assert.False(w.Poll());

        Assert.Equal(0, fired());
        Assert.False(w.HasFired);
    }

    /// <summary>
    /// A launch that silently never happened must not leave a timer running for the rest of the
    /// session — but giving up is NOT an exit, and nothing is reported.
    /// </summary>
    [Fact]
    public void ItGivesUpQuietlyIfTheGameNeverAppears()
    {
        var clock = new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc);
        var (w, fired) = Build(() => false, () => clock);
        w.Start(TimeSpan.FromHours(1));   // a timer exists, so we can watch it stop

        Assert.False(w.Poll());
        Assert.True(w.IsPolling);

        clock += GameExitWatcher.ArmingTimeout + TimeSpan.FromSeconds(1);
        Assert.False(w.Poll());

        Assert.False(w.IsPolling);
        Assert.Equal(0, fired());
        Assert.False(w.HasFired);
    }

    /// <summary>
    /// Once the game HAS been seen, the arming timeout stops applying — a long match must not
    /// be mistaken for a launch that never happened.
    /// </summary>
    [Fact]
    public void ALongMatchIsNotMistakenForALaunchThatNeverHappened()
    {
        var clock = new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc);
        var alive = true;
        var (w, fired) = Build(() => alive, () => clock);

        Assert.False(w.Poll());
        clock += TimeSpan.FromHours(3);
        Assert.False(w.Poll());
        Assert.Equal(0, fired());

        alive = false;
        Assert.True(w.Poll());
        Assert.Equal(1, fired());
    }

    /// <summary>Disposing stops the poll and reports nothing — the room teardown path.</summary>
    [Fact]
    public void DisposingReportsNothing()
    {
        var (w, fired) = Build(() => false);
        w.Start(TimeSpan.FromHours(1));
        w.Dispose();

        Assert.False(w.IsPolling);
        Assert.Equal(0, fired());
    }

    /// <summary>
    /// A handler that throws must not escape: both signals can arrive on a thread-pool thread,
    /// where an unhandled exception is a background crash rather than a caught error.
    /// </summary>
    [Fact]
    public void AThrowingHandlerIsContained()
    {
        var w = new GameExitWatcher(() => false, () => throw new InvalidOperationException("boom"));

        Assert.True(w.SignalExited());   // returns normally
        Assert.True(w.HasFired);
    }
}
