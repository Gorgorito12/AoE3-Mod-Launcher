using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Other players' profiles, as last read: the standing and the history page, per player. At most
/// <see cref="Capacity"/> players (the least recently opened goes first); an answer is asked for
/// again once it is <see cref="StaleAfter"/> old, or <see cref="RetryAfterFailure"/> after a
/// failure, so going back and forth between two profiles costs nothing. Pure (the clock is a
/// parameter), pinned by <c>ProfileCacheTests</c>.
/// </summary>
public sealed class ProfileCache
{
    public const int Capacity = 12;
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromSeconds(10);

    public sealed class Entry
    {
        public EloSnapshot? Standing { get; set; }
        public List<MatchHistoryRow>? History { get; set; }
        public bool StandingFailed { get; set; }
        public string? HistoryError { get; set; }
        public bool Loading { get; set; }
        public DateTime AskedUtc { get; set; } = DateTime.MinValue;
        public DateTime UsedUtc { get; set; }
    }

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public int Count => _entries.Count;

    /// <summary>The entry for a player, made when there is none (the oldest dropped past capacity).</summary>
    public Entry Get(string userId, DateTime nowUtc)
    {
        if (!_entries.TryGetValue(userId, out var entry))
        {
            if (_entries.Count >= Capacity)
            {
                var oldest = _entries.OrderBy(kv => kv.Value.UsedUtc).First().Key;
                _entries.Remove(oldest);
            }
            entry = new Entry();
            _entries[userId] = entry;
        }
        entry.UsedUtc = nowUtc;
        return entry;
    }

    public bool Contains(string userId) => _entries.ContainsKey(userId);

    /// <summary>Whether the player's profile should be asked for now.</summary>
    public static bool NeedsFetch(Entry entry, DateTime nowUtc)
    {
        if (entry.Loading) return false;
        bool failed = entry.StandingFailed || entry.HistoryError != null;
        if (entry.Standing == null && entry.History == null && !failed) return true;
        return nowUtc - entry.AskedUtc >= (failed ? RetryAfterFailure : StaleAfter);
    }

    /// <summary>A match naming these players was stored: their next look asks again.</summary>
    public void MarkStale(IEnumerable<string>? userIds)
    {
        if (userIds == null) return;
        foreach (var id in userIds)
            if (_entries.TryGetValue(id, out var e)) e.AskedUtc = DateTime.MinValue;
    }

    public void Clear() => _entries.Clear();
}
