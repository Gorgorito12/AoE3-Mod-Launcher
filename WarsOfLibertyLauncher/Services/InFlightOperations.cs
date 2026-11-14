using System;
using System.Collections.Generic;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// The dispatcher operations that have started and not yet completed, for the "UI OP" timing in
/// <see cref="DiagnosticLog.StartUiStallWatch"/>.
///
/// <para><b>Why a stack and not a <c>ConditionalWeakTable</c>, which is what this replaced
/// (2026-11-13).</b> The table took one add and one remove per operation — a few hundred a second
/// while anything animates — and a weak table does not reuse a removed slot: it fills, resizes into
/// a new internal container and leaves the old one for the finalizer. Measured on the testbed, ten
/// minutes of tab switching left 90,000 of those containers alive and the launcher's private memory
/// climbed ~235 MB an hour, from v1.0.14h on, for every player with the window open.</para>
///
/// <para>Operations on one dispatcher never interleave: one may NEST others (a modal dialog runs a
/// message loop inside an operation), but an inner one always finishes first. So the in-flight set
/// is a stack. An operation that never reports completion (it threw past the hook) is dropped when
/// an operation below it completes, and <see cref="Capacity"/> bounds the worst case, so nothing
/// here can grow without limit. Single-threaded: the dispatcher's own thread is the only caller.</para>
/// </summary>
internal sealed class InFlightOperations<TKey, TValue> where TKey : class
{
    /// <summary>Deeper than any real nesting; past it the oldest entry is forgotten.</summary>
    internal const int Capacity = 64;

    private readonly List<(TKey Key, TValue Value)> _stack = new();

    internal int Count => _stack.Count;

    internal void Start(TKey key, TValue value)
    {
        if (_stack.Count >= Capacity) _stack.RemoveAt(0);
        _stack.Add((key, value));
    }

    /// <summary>
    /// The value <paramref name="key"/> started with, and false for an operation this never saw.
    /// Anything started after it and still listed can no longer complete normally, and goes too.
    /// </summary>
    internal bool TryComplete(TKey key, out TValue value)
    {
        for (int i = _stack.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(_stack[i].Key, key)) continue;
            value = _stack[i].Value;
            _stack.RemoveRange(i, _stack.Count - i);
            return true;
        }
        value = default!;
        return false;
    }
}
