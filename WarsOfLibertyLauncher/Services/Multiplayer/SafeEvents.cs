using System;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Raises an event so that one throwing subscriber can neither silence the next nor escape into
/// the code that raised it.
///
/// <para>The room socket raises from its own pump thread inside <c>RunLoopAsync</c>. An
/// unguarded <c>Disconnected?.Invoke</c> whose subscriber threw escaped the loop, faulted the
/// <c>Task.Run</c> task unobserved, and killed reconnection for good; a throwing
/// <c>FrameReceived</c> subscriber was reported as a "bad frame" and every subscriber after it lost
/// that frame. Latent today — no current subscriber throws — but a future handler that touches WPF
/// on the pump thread would trip it at once.</para>
/// </summary>
internal static class SafeEvents
{
    /// <summary>
    /// Calls every handler of <paramref name="handler"/> in subscription order, each in its own
    /// try/catch; <paramref name="onError"/> hears about each throw once. Nothing escapes —
    /// <paramref name="onError"/> included.
    /// </summary>
    internal static void Raise<T>(EventHandler<T>? handler, object sender, T arg, Action<Exception> onError)
    {
        if (handler == null) return;
        foreach (var d in handler.GetInvocationList())
        {
            try { ((EventHandler<T>)d)(sender, arg); }
            catch (Exception ex)
            {
                try { onError(ex); }
                catch { /* the error reporter must not be what breaks the loop */ }
            }
        }
    }
}
