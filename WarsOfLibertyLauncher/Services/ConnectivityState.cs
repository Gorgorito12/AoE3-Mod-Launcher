using System;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// App-wide "are we online?" signal, derived by OBSERVATION rather than an active
/// probe. Network-touching code reports the outcome of the real calls it already
/// makes via <see cref="ReportSuccess"/> / <see cref="ReportFailure"/>, and the UI
/// reflects <see cref="IsOffline"/> (the offline chip + greying online-only
/// controls).
///
/// Why observed instead of a startup connectivity probe: a probe (HTTP HEAD / ping)
/// is unreliable in this launcher's real environments — a corporate proxy / TLS
/// inspection, a captive portal, or an active Radmin VPN adapter routinely produce
/// false negatives that would wrongly disable multiplayer/updates for an ONLINE
/// user. Reacting to the actual outcome of calls we already make never has that
/// failure mode: the flag only flips to offline after a real network call fails
/// with a network-type error, and any later success clears it. The existing
/// per-call HttpClient timeouts bound how long "offline" takes to observe
/// (sub-second on a clean offline; the call's timeout on a blackhole network).
/// Best-effort UX signal — last writer wins; the 5-min live-refresh timer and the
/// window <c>Activated</c> handler re-evaluate it, and reconnecting clears it on the
/// next successful call.
/// </summary>
public static class ConnectivityState
{
    private static readonly object _gate = new();
    private static bool _isOffline;

    /// <summary>
    /// True once a real network call has failed with a network-type error and none
    /// has since succeeded. Defaults to false (assume online until proven otherwise,
    /// so we never grey out features for a user we haven't actually failed to reach).
    /// </summary>
    public static bool IsOffline
    {
        get { lock (_gate) { return _isOffline; } }
    }

    /// <summary>
    /// Raised (on the reporting thread) whenever <see cref="IsOffline"/> flips. UI
    /// handlers MUST marshal to the dispatcher themselves.
    /// </summary>
    public static event Action? OfflineChanged;

    /// <summary>A real network call succeeded → we're online. Clears the flag.</summary>
    public static void ReportSuccess() => Set(false);

    /// <summary>
    /// A network call failed. Flips to offline ONLY for network-type exceptions
    /// (see <see cref="IsNetworkError"/>), so an unrelated logic bug surfaced from a
    /// network code path can't masquerade as "offline" and disable the UI.
    /// </summary>
    public static void ReportFailure(Exception? ex)
    {
        if (IsNetworkError(ex)) Set(true);
    }

    private static void Set(bool offline)
    {
        bool changed;
        lock (_gate)
        {
            changed = _isOffline != offline;
            _isOffline = offline;
        }
        if (!changed) return;
        // A throwing UI handler must never break the network code path that reported
        // the state change.
        try { OfflineChanged?.Invoke(); }
        catch { /* best-effort notification */ }
    }

    /// <summary>
    /// Whether an exception represents a connectivity failure (vs a logic bug). Walks
    /// the inner-exception chain because the launcher wraps transport errors — e.g.
    /// <c>UpdateInfoService.FetchAsync</c> throws
    /// <c>InvalidOperationException("ErrManifestUnreachable")</c> with the real
    /// <see cref="HttpRequestException"/> as its <c>InnerException</c>, so the wrapper
    /// type alone isn't a signal but its inner is.
    ///
    /// Note: <see cref="TaskCanceledException"/> is treated as a network error because
    /// an <see cref="HttpClient"/> TIMEOUT surfaces as one. Callers that also handle
    /// genuine user cancellation must filter that out FIRST (CheckAsync rethrows
    /// <see cref="OperationCanceledException"/> before reaching ReportFailure).
    /// </summary>
    internal static bool IsNetworkError(Exception? ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            switch (e)
            {
                case HttpRequestException:
                case SocketException:
                case TaskCanceledException:
                case TimeoutException:
                case IOException:
                    return true;
            }
        }
        return false;
    }
}
