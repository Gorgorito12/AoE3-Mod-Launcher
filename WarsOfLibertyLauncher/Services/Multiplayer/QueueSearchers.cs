using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// The rules about who is searching in the server's quick-match queue, as the launcher uses them:
/// which searchers a search of mine would meet, which earn a row's "Play" button, and which earn
/// the "somebody is searching" card. Pure, so the refusals can be pinned (QueueSearchersTests).
/// </summary>
internal static class QueueSearchers
{
    internal static QuickMatchKind KindOf(string? wire) => wire switch
    {
        "casual" => QuickMatchKind.Casual,
        "either" => QuickMatchKind.Either,
        _ => QuickMatchKind.Ranked,
    };

    internal static string Wire(QuickMatchKind kind) => kind switch
    {
        QuickMatchKind.Casual => "casual",
        QuickMatchKind.Either => "either",
        _ => "ranked",
    };

    /// <summary>The server's rule: rated and casual never meet; Either meets both.</summary>
    internal static bool KindsMeet(QuickMatchKind a, QuickMatchKind b)
        => a == QuickMatchKind.Either || b == QuickMatchKind.Either || a == b;

    /// <summary>Not me, not being matched right now, the same mod and the same version.</summary>
    internal static bool SameGame(QueueSearcher s, string? myId, string modId, string? myHash)
        => !string.Equals(s.UserId, myId, StringComparison.Ordinal)
           && !s.Busy
           && string.Equals(s.ModId, modId, StringComparison.OrdinalIgnoreCase)
           && !string.IsNullOrEmpty(myHash)
           && string.Equals(s.ModCombinedHash, myHash, StringComparison.OrdinalIgnoreCase);

    /// <summary>Who a search of mine (this mod, this version, this kind) could be matched with.</summary>
    internal static List<QueueSearcher> Compatible(
        IEnumerable<QueueSearcher> all, string? myId, string modId, string? myHash, QuickMatchKind kind)
        => all.Where(s => SameGame(s, myId, modId, myHash) && KindsMeet(KindOf(s.Kind), kind))
              .OrderBy(s => s.QueuedAt, StringComparer.Ordinal)
              .ToList();

    /// <summary>
    /// Whether a searcher who just appeared earns the "X is looking for a 1v1" card: the player
    /// asked for it, he is not searching himself (his own search will meet them), the searcher is
    /// somebody else, free, in a mod installed here and of a version KNOWN to be mine — a card
    /// whose Play button would answer "other version" is noise. Never twice for one ticket, and
    /// never more than once in <see cref="PerPersonCooldown"/> for one person.
    /// </summary>
    internal static bool WorthNotifying(
        QueueSearcher s, bool optedIn, bool searchingMyself, string? myId,
        Func<string, string?> myHashOf, IReadOnlySet<string> seenTickets,
        IReadOnlyDictionary<string, DateTime> lastByUser, DateTime nowUtc)
    {
        if (!optedIn || searchingMyself) return false;
        if (seenTickets.Contains(s.TicketId)) return false;
        if (string.Equals(s.UserId, myId, StringComparison.Ordinal) || s.Busy) return false;
        var mine = myHashOf(s.ModId);
        if (mine == null || !string.Equals(mine, s.ModCombinedHash, StringComparison.OrdinalIgnoreCase)) return false;
        if (lastByUser.TryGetValue(s.UserId, out var last) && nowUtc - last < PerPersonCooldown) return false;
        return true;
    }

    /// <summary>The same person searching again soon is not news.</summary>
    internal static readonly TimeSpan PerPersonCooldown = TimeSpan.FromMinutes(10);

    /// <summary>Minutes since the searcher entered the queue, at least 1; null when unknown.</summary>
    internal static int? MinutesWaiting(QueueSearcher s, DateTime nowUtc)
        => RoomAgeFormat.ParseCreatedUtc(s.QueuedAt) is DateTime at
            ? Math.Max(1, (int)(nowUtc - at).TotalMinutes)
            : null;
}
