using System;
using System.Diagnostics;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Stops the game — one place for "kill, confirm, log", instead of the same try/catch
/// copied across five call sites.
///
/// <para><b>It asks nothing first, and that is a measured decision.</b> This class briefly
/// tried the polite path: <c>CloseMainWindow()</c>, wait up to 5 s, kill only if the game
/// refused — meaning to avoid <c>TerminateProcess</c> both for the sake of half-written
/// saves and because repeatedly terminating a legacy app is a suspected trigger for the
/// compatibility layer Windows pins on <c>age3y.exe</c> (see
/// <see cref="AppCompatLayerService"/>). The launcher log settled it: across six stops,
/// <c>"closed gracefully"</c> appeared <b>zero</b> times — four
/// <c>"ignored the close request after 5000 ms"</c> and two <c>"has no main window to
/// ask"</c>. AoE3 answers WM_CLOSE with its own in-game confirmation dialog, which nobody
/// can see while the launcher has focus, so the wait always expired.</para>
///
/// <para>That made it strictly worse than doing nothing: five seconds of frozen UI (the
/// Stop button calls this synchronously) <i>and</i> the same abrupt kill at the end, so the
/// PCA mitigation never actually applied once. Every mod runs the same 2007 engine, so no
/// profile will behave differently. <b>Don't reintroduce the polite attempt</b> — the real
/// safety net for the compat layer is detecting it and offering to remove it, which doesn't
/// rest on a hypothesis.</para>
///
/// <para>Best-effort throughout: a game that won't die must never surface as an exception
/// in a UI handler.</para>
/// </summary>
internal static class GameProcessCloser
{
    /// <summary>
    /// Terminates <paramref name="process"/> and waits briefly to confirm. Returns true when
    /// it is no longer running. <paramref name="killEntireTree"/> is used by the multiplayer
    /// paths, which have to take child processes with them.
    /// </summary>
    public static bool Stop(Process? process, bool killEntireTree = false)
    {
        if (process == null) return true;

        try
        {
            if (process.HasExited) return true;

            int pid = process.Id;
            if (killEntireTree) process.Kill(entireProcessTree: true);
            else process.Kill();

            // Returns in milliseconds in practice; the timeout is only so a wedged process
            // can't hang the caller.
            process.WaitForExit(5000);
            DiagnosticLog.Write($"Stopped game process (PID {pid}).");
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Could not stop the game: {ex.Message}");
            return false;
        }
    }
}
