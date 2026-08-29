using System;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// What a successful <see cref="GameLauncher.Launch"/> did, so the UI can react to
/// <em>how</em> the game started rather than only to whether it started.
/// </summary>
/// <param name="ExePath">The executable that was actually launched.</param>
/// <param name="NeededElevation">
/// True when the re-parented launch failed with <c>ERROR_ELEVATION_REQUIRED</c> and we
/// had to fall back to ShellExecute — i.e. Windows demanded admin for this exe and
/// showed a UAC prompt. On AoE3 that means a compatibility layer is applied to the exe
/// (see <see cref="AppCompatLayerService"/>), which the user can remove; it also costs
/// them the re-parenting that keeps the game alive when the launcher is force-closed.
/// </param>
/// <param name="ProcessId">
/// The process we actually started, or <c>-1</c> when the OS didn't hand one back (the
/// elevated ShellExecute path).
///
/// <para>Identifying the game by pid rather than by executable NAME matters because the
/// name is ambiguous: Wars of Liberty and the stock game both run <c>age3y.exe</c>, so
/// <c>GetProcessesByName("age3y")</c> can't tell them apart. Watching by name meant the
/// exit monitor stayed "still playing" while an unrelated AoE3 was open, and Stop killed
/// every copy on the machine instead of the one the launcher started.</para>
/// </param>
public readonly record struct GameLaunchResult(string ExePath, bool NeededElevation, int ProcessId);

/// <summary>
/// What a <see cref="GameLauncher.LaunchAndWatch"/> did — the multiplayer sibling of
/// <see cref="GameLaunchResult"/>.
///
/// <para><b>Why this replaced a bare <c>Process?</c>.</b> The elevated fallback returned a
/// perfectly ordinary-looking Process object with no exit watcher attached to it, so every
/// caller read it as full success and nothing downstream could tell the difference. That one
/// silent degradation switched off the entire post-match pipeline — the recording was never
/// read, the match never reported, <c>game_ended</c> never sent — and left the player with no
/// way to reach the one-click fix for its cause, because that offer hangs off the dashboard's
/// own exit handling. A type that has to be unpacked is what makes the degraded path
/// impossible to mistake for the good one.</para>
/// </summary>
/// <param name="Process">
/// A handle on the game when we have one. Null is not failure here: the game can be running
/// detached with the watcher attach having lost a race, which is why <paramref name="ProcessId"/>
/// is carried separately.
/// </param>
/// <param name="ProcessId">
/// The pid, or <c>-1</c> when the OS handed none back. This is what lets the exit be watched
/// by polling even when there is no Process object and no event — and, as
/// <see cref="GameLaunchResult.ProcessId"/> explains at length, why the pid rather than the
/// executable name: Wars of Liberty and the stock game both run <c>age3y.exe</c>.
/// </param>
/// <param name="ExePath">The executable that was actually launched.</param>
/// <param name="NeededElevation">
/// Windows demanded admin for this exe. On AoE3 that means a compatibility layer the player
/// can remove — see <see cref="AppCompatLayerService"/>. Surfaced here so the multiplayer
/// path can offer that fix too; before this it was reachable only from the dashboard, so a
/// player who only played multiplayer paid the UAC prompt on every single launch and was
/// never told there was anything to fix.
/// </param>
/// <param name="ExitWatcherAttached">
/// Whether <c>Process.Exited</c> is actually wired. False on the elevated path. The exit is
/// reported regardless — <see cref="GameExitWatcher"/> polls as well — so this is for
/// diagnosis, not for deciding whether to watch.
/// </param>
public readonly record struct WatchedLaunch(
    System.Diagnostics.Process? Process,
    int ProcessId,
    string ExePath,
    bool NeededElevation,
    bool ExitWatcherAttached)
{
    /// <summary>Nothing started at all — as opposed to "started, but degraded".</summary>
    public bool Failed => Process == null && ProcessId <= 0;
}

/// <summary>
/// The user declined the UAC prompt, so the game never started.
///
/// This exists to keep a DECISION from being reported as a failure: the raw
/// <see cref="System.ComponentModel.Win32Exception"/> (1223, ERROR_CANCELLED) used to
/// reach a generic handler that showed its framework message in a red error dialog —
/// telling the user something broke immediately after they chose that it shouldn't
/// happen. Same reasoning as <c>NsisExtractionException.DeclinedByUser</c>.
/// </summary>
public sealed class GameLaunchCancelledException : Exception
{
    public GameLaunchCancelledException(string exePath)
        : base($"The user declined the elevation prompt for '{exePath}'.")
    {
        ExePath = exePath;
    }

    public string ExePath { get; }
}
