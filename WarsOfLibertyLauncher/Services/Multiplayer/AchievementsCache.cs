using System;
using System.Collections.Generic;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// The medals the profile has read, per player and game (design 121). Pure bookkeeping: when a
/// read is due, what arrived, and whether the server has achievements at all.
///
/// <para><b>A 404 switches the section off for the session</b> (<see cref="Unsupported"/>): an older
/// server has no such route, and asking again on every repaint would be a request per frame that
/// can never succeed. Every other failure is asked again after <see cref="RetryAfterFailure"/>.</para>
/// </summary>
public sealed class AchievementsCache
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromSeconds(10);

    public sealed class Entry
    {
        public AchievementsResponse? Response { get; set; }
        public bool Loading { get; set; }
        public bool Failed { get; set; }
        public bool Stale { get; set; }
        public DateTime AskedUtc { get; set; }
    }

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The server answered 404: it has no achievements, and the section stays hidden.</summary>
    public bool Unsupported { get; set; }

    private static string Key(string userId, string modId) => userId + "|" + (modId ?? "").Trim().ToLowerInvariant();

    /// <summary>The entry for this player and game, made on first ask.</summary>
    public Entry Get(string userId, string modId)
    {
        var key = Key(userId, modId);
        if (!_entries.TryGetValue(key, out var e)) _entries[key] = e = new Entry();
        return e;
    }

    /// <summary>What is held, without making an entry.</summary>
    public AchievementsResponse? Peek(string userId, string modId)
        => _entries.TryGetValue(Key(userId, modId), out var e) ? e.Response : null;

    /// <summary>Whether to ask the server now.</summary>
    public bool NeedsFetch(Entry e, DateTime nowUtc)
    {
        if (Unsupported || e.Loading) return false;
        if (e.Failed) return nowUtc - e.AskedUtc >= RetryAfterFailure;
        if (e.Response == null || e.Stale) return true;
        return nowUtc - e.AskedUtc >= StaleAfter;
    }

    /// <summary>A match of this player changed, or he earned a medal: every game of his is read again.</summary>
    public void MarkStale(string userId)
    {
        foreach (var (key, entry) in _entries)
            if (key.StartsWith(userId + "|", StringComparison.OrdinalIgnoreCase)) entry.Stale = true;
    }

    /// <summary>Signing out or in as somebody else: nothing read for the old session stays.</summary>
    public void Clear()
    {
        _entries.Clear();
        Unsupported = false;
    }
}
