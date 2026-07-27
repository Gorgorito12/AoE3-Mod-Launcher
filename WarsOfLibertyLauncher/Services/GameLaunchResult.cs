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
