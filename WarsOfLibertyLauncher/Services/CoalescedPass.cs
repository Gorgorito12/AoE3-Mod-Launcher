using System.Threading;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// At most one queued run of a pass: the first request queues it, every request until it starts
/// rides on it, and a request that arrives once it has started queues the next.
///
/// <para>Thread-safe — a session state change can be raised from a socket's pump thread as well
/// as the UI thread. Same idea as the launcher's other "queue once" passes (the mod cards, the
/// activity layout), made a type so it can be pinned.</para>
/// </summary>
internal sealed class CoalescedPass
{
    private int _queued;

    /// <summary>True when the caller must queue the run; false when one is already queued.</summary>
    public bool TryQueue() => Interlocked.Exchange(ref _queued, 1) == 0;

    /// <summary>Called FIRST inside the run, so a request arriving during it queues another.</summary>
    public void BeginRun() => Interlocked.Exchange(ref _queued, 0);
}
