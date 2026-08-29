using System;
using System.Threading;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Tells the caller the game has closed — exactly once — from whichever of two signals
/// arrives first: the <c>Process.Exited</c> event, or a poll that finds the process gone.
///
/// <para><b>Why this exists.</b> <c>Process.Exited</c> was the only trigger for the whole
/// post-match pipeline, and it does not always fire. When AoE3 demands elevation (a Windows
/// compatibility layer pinned on <c>age3y.exe</c> is enough to cause it),
/// <see cref="GameLauncher.LaunchAndWatch"/> falls back to a ShellExecute launch — a medium
/// integrity launcher cannot open a handle on a higher-integrity child, so no event ever
/// comes. A real player's diagnostic log showed three launches and <b>zero</b> game-exit
/// handling: no recording read, no match reported, no <c>game_ended</c> sent, and the match
/// context leaking into the next game. Nothing warned him, because the failed launch still
/// returned a Process object that read as success.</para>
///
/// <para><b>The polling is not limited to that path, on purpose.</b> One way for the event to
/// go missing has now been found; assuming it is the only one is how this lasted as long as it
/// did. A two-second tick costs nothing next to a match, and the once-only guard is what makes
/// running both signals together safe.</para>
///
/// <para>The timing and the decision are deliberately separate: <see cref="Poll"/> is a single
/// tick a test can call directly, so none of the rules below need a clock to check.</para>
/// </summary>
public sealed class GameExitWatcher : IDisposable
{
    /// <summary>
    /// How often to ask whether the game is still there.
    ///
    /// <para>The same cadence the dashboard's own monitor has used all along. It bounds how
    /// late the post-match work can start, and that work is already allowed to take tens of
    /// seconds hunting for the recording, so a finer tick would buy nothing.</para>
    /// </summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long to wait for the game to appear before giving up — quietly, never as an exit.
    ///
    /// <para>Generous because the thing being waited on is a human: on the elevated path a UAC
    /// prompt stands between the launch and a process existing at all. Long enough that nobody
    /// reasonable is cut off, short enough that a launch which silently never happened does not
    /// leave a timer ticking for the rest of the session.</para>
    /// </summary>
    public static readonly TimeSpan ArmingTimeout = TimeSpan.FromMinutes(2);

    private readonly Func<bool> _isAlive;
    private readonly Action _onExited;
    private readonly Func<DateTime> _now;
    private readonly DateTime _startedAt;

    /// <summary>0 until something has reported the exit; set with Interlocked, never reset.</summary>
    private int _fired;

    /// <summary>
    /// Whether the game has ever been observed running.
    ///
    /// <para><b>This is the guard that keeps the poll from reporting an exit before the start.</b>
    /// On the elevated path the process does not exist while the UAC prompt is on screen, so the
    /// first tick finds nothing — and without this it would announce that the match had ended
    /// seconds before the game opened, which is worse than the silence it replaces.</para>
    /// </summary>
    private bool _seenAlive;

    private Timer? _timer;

    public GameExitWatcher(Func<bool> isAlive, Action onExited, Func<DateTime>? now = null)
    {
        _isAlive = isAlive ?? throw new ArgumentNullException(nameof(isAlive));
        _onExited = onExited ?? throw new ArgumentNullException(nameof(onExited));
        _now = now ?? (() => DateTime.UtcNow);
        _startedAt = _now();
    }

    /// <summary>Whether the exit has already been reported.</summary>
    public bool HasFired => Volatile.Read(ref _fired) != 0;

    /// <summary>Whether the game has been seen running at least once.</summary>
    public bool SeenAlive => _seenAlive;

    /// <summary>Whether the poll is still running.</summary>
    public bool IsPolling => Volatile.Read(ref _timer) != null;

    /// <summary>Begin polling. Safe to call when the event is wired too — see the class note.</summary>
    public void Start() => Start(DefaultInterval);

    public void Start(TimeSpan interval)
    {
        if (_timer != null) return;
        _timer = new Timer(_ => Poll(), null, interval, interval);
    }

    /// <summary>
    /// Report the exit from outside — this is what <c>Process.Exited</c> calls.
    ///
    /// <para>It deliberately skips the seen-alive rule: the event only exists because we held a
    /// handle on a real process, so it is proof rather than an inference.</para>
    /// </summary>
    public bool SignalExited() => Fire();

    /// <summary>
    /// One poll tick. Returns true if this tick was the one that reported the exit.
    ///
    /// <para><b>A probe that throws is "don't know", never "it exited".</b> Any failure to
    /// inspect the process says nothing about whether the game is running, and treating it as
    /// an exit would report a match while the player is still in it.</para>
    /// </summary>
    public bool Poll()
    {
        if (HasFired) return false;

        bool alive;
        try { alive = _isAlive(); }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"GameExitWatcher: liveness probe failed (ignored): {ex.Message}");
            return false;
        }

        if (alive)
        {
            _seenAlive = true;
            return false;
        }

        // Never seen it: the game has not appeared YET (a UAC prompt is up), or it never will.
        // Either way this is not an exit. Stop eventually so a launch that silently failed does
        // not leave a timer running for the session.
        if (!_seenAlive)
        {
            if (_now() - _startedAt >= ArmingTimeout)
            {
                DiagnosticLog.Write(
                    "GameExitWatcher: the game never appeared; giving up on the exit poll " +
                    "(no exit reported).");
                StopTimer();
            }
            return false;
        }

        return Fire();
    }

    private bool Fire()
    {
        if (Interlocked.Exchange(ref _fired, 1) != 0) return false;

        // Stop polling BEFORE handing control to the callback: the post-match work runs for
        // tens of seconds, and there is nothing left for a tick to discover.
        StopTimer();

        try { _onExited(); }
        catch (Exception ex)
        {
            // Same guard the Exited handler already had: this can run on a thread-pool thread,
            // where an escaping throw is a background crash.
            DiagnosticLog.Write($"GameExitWatcher: exit handler failed (ignored): {ex.Message}");
        }
        return true;
    }

    private void StopTimer()
    {
        var t = Interlocked.Exchange(ref _timer, null);
        try { t?.Dispose(); } catch { /* best-effort */ }
    }

    public void Dispose() => StopTimer();
}
