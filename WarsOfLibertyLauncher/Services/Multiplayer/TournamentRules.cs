using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// The tournament server's bracket and entrant rules, ported from
/// <c>wol-launcher-lobby-node/src/tournaments/bracket.ts</c> and <c>entrants.ts</c>.
///
/// <para><b>THE PREVIEW'S, AND ONLY THE PREVIEW'S.</b> On live data the launcher decides nothing
/// about a tournament: who plays whom, who advanced and who won all arrive from the server and are
/// only read (see the header of <see cref="BracketLayout"/>). This exists so the fabricated
/// tournaments behind <c>--demo-tournaments</c> can be PLAYED — a bracket that advances, a
/// champion at the end — without a server. Nothing here may ever be called on a tournament that
/// came from <see cref="LobbyApiClient"/>; <c>TournamentPreviewTests</c> scans for it.</para>
///
/// <para><b>Ported, not reinvented, and the names are the TypeScript ones on purpose</b>, so the
/// two can be read side by side. A preview that drew a bracket the server never would is worse
/// than none: somebody would take a design decision on a picture that cannot happen. Two server
/// behaviours that look like bugs are copied deliberately for the same reason and pinned by
/// tests — a disqualification considers only the entrant just thrown out, and its cascade never
/// finishes the tournament (that is the route's doing, in <see cref="TournamentSimulator"/>).</para>
///
/// <para>Unlike the server, which clones and returns update lists, these mutate the matches they
/// are handed — the preview's store IS the database — and every refusal is decided BEFORE the
/// first write, so a refused call leaves the bracket exactly as it was.</para>
/// </summary>
internal static class TournamentRules
{
    // ---------------------------------------------------------------- shape

    /// <summary>The smallest power of two that seats everyone, never below two.</summary>
    internal static int BracketSize(int entrantCount)
    {
        if (entrantCount < 2) return 2;
        int size = 2;
        while (size < entrantCount) size *= 2;
        return size;
    }

    /// <summary>How many rounds a bracket of this many entrants has. Two entrants is one round.</summary>
    internal static int RoundsFor(int entrantCount) => Log2(BracketSize(entrantCount));

    /// <summary>How many matches the given round of a bracket of <paramref name="size"/> holds.</summary>
    internal static int MatchesInRound(int size, int round) => size >> round;

    /// <summary>
    /// The standard seed placement, read two at a time: <c>SeedOrder(8)</c> is
    /// <c>[1,8,4,5,2,7,3,6]</c>. Built by repeated mirroring, which is what makes every
    /// first-round pair sum to <c>size + 1</c> and keeps the top two seeds apart until the final.
    /// </summary>
    internal static int[] SeedOrder(int size)
    {
        var order = new List<int> { 1 };
        while (order.Count < size)
        {
            int n = order.Count * 2;
            var next = new List<int>(n);
            foreach (int s in order)
            {
                next.Add(s);
                next.Add(n + 1 - s);
            }
            order = next;
        }
        return order.ToArray();
    }

    /// <summary>Where the winner of <c>(round, position)</c> goes, or null from the final.</summary>
    internal static (int Round, int Position, int Slot)? NextOf(int round, int position, int roundsTotal)
        => round >= roundsTotal ? null : (round + 1, position >> 1, (position & 1) + 1);

    /// <summary>Whether somebody could sit down and play this right now.</summary>
    internal static bool Playable(TournamentMatch m)
        => string.Equals(m.Status, "pending", StringComparison.Ordinal)
           && !string.IsNullOrEmpty(m.Entrant1Id)
           && !string.IsNullOrEmpty(m.Entrant2Id);

    /// <summary>
    /// Fill <c>NextMatchId</c> / <c>NextSlot</c> from the geometry, the way the server stores them.
    ///
    /// <para>The fabricated samples were written without the links — the renderer never reads them,
    /// it derives the same thing from round and position — but <see cref="Advance"/> walks them,
    /// exactly as the server's <c>settle</c> does, so a sample is linked before it is played.</para>
    /// </summary>
    internal static void EnsureLinks(IList<TournamentMatch> matches, int roundsTotal)
    {
        var at = matches.ToDictionary(m => (m.Round, m.Position));
        foreach (var m in matches)
        {
            var next = NextOf(m.Round, m.Position, roundsTotal);
            if (next is not { } n || !at.TryGetValue((n.Round, n.Position), out var target))
            {
                m.NextMatchId = null;
                m.NextSlot = null;
                continue;
            }
            m.NextMatchId = target.Id;
            m.NextSlot = n.Slot;
        }
    }

    /// <summary>
    /// Every match of a fresh bracket, with byes already decided and their winners seated.
    ///
    /// <para>Throws on malformed seeding, like the server: duplicate or missing seeds are a bug in
    /// the seeding step. The caller checks the same conditions first so a person gets a reason
    /// rather than an exception.</para>
    /// </summary>
    /// <param name="entrants">Entrant ids with seeds 1..N.</param>
    /// <param name="idFor">The id to give the match at <c>(round, position)</c>.</param>
    internal static List<TournamentMatch> Generate(
        IReadOnlyList<(string EntrantId, int Seed)> entrants, Func<int, int, string> idFor)
    {
        if (entrants.Count < 2) throw new InvalidOperationException("a bracket needs at least two entrants");

        var bySeed = new Dictionary<int, string>();
        foreach (var (entrantId, seed) in entrants)
        {
            if (bySeed.ContainsKey(seed)) throw new InvalidOperationException($"duplicate seed {seed}");
            bySeed[seed] = entrantId;
        }
        for (int s = 1; s <= entrants.Count; s++)
        {
            if (!bySeed.ContainsKey(s))
                throw new InvalidOperationException($"seed {s} is missing; seeds must be 1..N");
        }

        int size = BracketSize(entrants.Count);
        int rounds = Log2(size);

        var grid = new List<List<TournamentMatch>>();
        for (int r = 1; r <= rounds; r++)
        {
            var row = new List<TournamentMatch>();
            for (int p = 0; p < MatchesInRound(size, r); p++)
            {
                row.Add(new TournamentMatch
                {
                    Id = idFor(r, p),
                    Round = r,
                    Position = p,
                    Status = "pending",
                });
            }
            grid.Add(row);
        }

        for (int r = 1; r < rounds; r++)
        {
            foreach (var m in grid[r - 1])
            {
                var next = NextOf(m.Round, m.Position, rounds)!.Value;
                m.NextMatchId = grid[next.Round - 1][next.Position].Id;
                m.NextSlot = next.Slot;
            }
        }

        var order = SeedOrder(size);
        for (int p = 0; p < grid[0].Count; p++)
        {
            var m = grid[0][p];
            m.Entrant1Id = bySeed.TryGetValue(order[p * 2], out var a) ? a : null;
            m.Entrant2Id = bySeed.TryGetValue(order[p * 2 + 1], out var b) ? b : null;
        }

        var all = grid.SelectMany(r => r).ToList();
        var byId = all.ToDictionary(m => m.Id, StringComparer.Ordinal);
        foreach (var m in grid[0])
        {
            var present = new[] { m.Entrant1Id, m.Entrant2Id }.Where(x => !string.IsNullOrEmpty(x)).ToList();
            // Never 0: every first-round pair sums to size + 1, so one of the two is a real seed.
            if (present.Count != 1) continue;
            m.Status = "bye";
            m.Outcome = "bye";
            m.WinnerEntrantId = present[0];
            Seat(byId, m, present[0]!);
        }

        return all;
    }

    // ---------------------------------------------------------------- movement

    /// <summary>What <see cref="Advance"/> did, or why it refused.</summary>
    internal sealed record AdvanceResult(
        bool Ok,
        string? Refusal,
        bool TournamentDone,
        string? ChampionEntrantId,
        IReadOnlyList<string> Touched,
        IReadOnlyList<string> NewlyReady);

    /// <summary>
    /// Record a winner and move them on. Refusals, in the server's order and words:
    /// <c>match_not_found</c>, <c>is_bye</c>, <c>already_decided</c>, <c>winner_not_in_match</c>.
    ///
    /// <para><paramref name="disqualified"/> is consulted when a slot fills: a winner who arrives
    /// opposite somebody already thrown out wins that one too, by <c>dq</c>, as far as it goes.</para>
    /// </summary>
    internal static AdvanceResult Advance(
        IList<TournamentMatch> matches,
        string matchId,
        string winnerEntrantId,
        string outcome,
        IReadOnlySet<string>? disqualified = null)
    {
        var byId = matches.ToDictionary(m => m.Id, StringComparer.Ordinal);
        if (!byId.TryGetValue(matchId, out var target)) return Refuse("match_not_found");
        if (target.Status == "bye") return Refuse("is_bye");
        if (target.Status == "done") return Refuse("already_decided");
        if (!string.Equals(winnerEntrantId, target.Entrant1Id, StringComparison.Ordinal)
            && !string.Equals(winnerEntrantId, target.Entrant2Id, StringComparison.Ordinal))
        {
            return Refuse("winner_not_in_match");
        }

        var touched = new List<string>();
        var newlyReady = new List<string>();
        Settle(byId, target, winnerEntrantId, outcome,
               disqualified ?? new HashSet<string>(), touched, newlyReady);

        // The final is the match that feeds nothing — the server's own definition.
        var final = matches.FirstOrDefault(m => string.IsNullOrEmpty(m.NextMatchId));
        bool done = final != null && final.Status is "done" or "bye";
        return new AdvanceResult(true, null, done, final?.WinnerEntrantId, touched, newlyReady);
    }

    /// <summary>
    /// Throw an entrant out: every pending match of theirs whose opponent is already known goes to
    /// that opponent by <c>dq</c>, and the winner carries on. A match whose other slot is still
    /// empty is left alone — <see cref="Advance"/> resolves it when somebody arrives.
    ///
    /// <para>Only THIS entrant counts as out, exactly as the server's <c>disqualify()</c> does:
    /// somebody disqualified earlier who is met during the cascade is not treated as gone. A quirk,
    /// copied rather than fixed, because the preview's job is to show what the server would do.</para>
    /// </summary>
    /// <returns>The ids of every match that changed.</returns>
    internal static IReadOnlyList<string> Disqualify(IList<TournamentMatch> matches, string entrantId)
    {
        var byId = matches.ToDictionary(m => m.Id, StringComparer.Ordinal);
        var touched = new List<string>();
        var newlyReady = new List<string>();
        var outSet = new HashSet<string>(StringComparer.Ordinal) { entrantId };

        // The server walks the bracket as stored, which is ordered by round then position.
        foreach (var m in matches.OrderBy(x => x.Round).ThenBy(x => x.Position).ToList())
        {
            if (m.Status != "pending") continue;
            if (m.Entrant1Id != entrantId && m.Entrant2Id != entrantId) continue;
            var other = m.Entrant1Id == entrantId ? m.Entrant2Id : m.Entrant1Id;
            if (string.IsNullOrEmpty(other)) continue;
            if (outSet.Contains(other)) continue;
            Settle(byId, m, other, "dq", outSet, touched, newlyReady);
        }
        return touched;
    }

    private static void Settle(
        Dictionary<string, TournamentMatch> byId,
        TournamentMatch m,
        string winnerEntrantId,
        string outcome,
        IReadOnlySet<string> disqualified,
        List<string> touched,
        List<string> newlyReady)
    {
        m.Status = "done";
        m.Outcome = outcome;
        m.WinnerEntrantId = winnerEntrantId;
        Touch(touched, m.Id);

        if (string.IsNullOrEmpty(m.NextMatchId) || m.NextSlot is not int slot) return;
        if (!byId.TryGetValue(m.NextMatchId, out var next)) return;

        if (slot == 1) next.Entrant1Id = winnerEntrantId;
        else next.Entrant2Id = winnerEntrantId;
        Touch(touched, next.Id);

        if (string.IsNullOrEmpty(next.Entrant1Id) || string.IsNullOrEmpty(next.Entrant2Id)) return;
        if (next.Status != "pending") return;

        bool oneOut = disqualified.Contains(next.Entrant1Id);
        bool twoOut = disqualified.Contains(next.Entrant2Id);
        // Both gone is not a walkover for anybody: it needs a human, so it is left standing.
        if (oneOut != twoOut)
        {
            var survivor = oneOut ? next.Entrant2Id! : next.Entrant1Id!;
            Settle(byId, next, survivor, "dq", disqualified, touched, newlyReady);
            return;
        }
        newlyReady.Add(next.Id);
    }

    private static void Seat(Dictionary<string, TournamentMatch> byId, TournamentMatch from, string entrantId)
    {
        if (string.IsNullOrEmpty(from.NextMatchId) || from.NextSlot is not int slot) return;
        if (!byId.TryGetValue(from.NextMatchId, out var next)) return;
        if (slot == 1) next.Entrant1Id = entrantId;
        else next.Entrant2Id = entrantId;
    }

    private static void Touch(List<string> touched, string id)
    {
        if (!touched.Contains(id)) touched.Add(id);
    }

    private static AdvanceResult Refuse(string reason)
        => new(false, reason, false, null, Array.Empty<string>(), Array.Empty<string>());

    // ---------------------------------------------------------------- entrants

    /// <summary>How many players an entrant must field: one, two or three.</summary>
    internal static int RosterSizeFor(string? format) => format switch
    {
        "2v2" => 2,
        "3v3" => 3,
        _ => 1,
    };

    /// <summary>Whether this team-formation mode makes sense for this format.</summary>
    internal static bool TeamSourceAllowed(string? format, string? source)
        => format == "1v1" ? source == "solo" : source != "solo";

    /// <summary>
    /// Whether a line-up may enter, and if not why: <c>wrong_size</c>, <c>duplicate_member</c>
    /// or <c>already_entered</c>, in the server's order.
    /// </summary>
    internal static string? ValidateRoster(
        string? format, IReadOnlyList<string> memberIds, IReadOnlySet<string> alreadyEntered)
    {
        if (memberIds.Count != RosterSizeFor(format)) return "wrong_size";
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in memberIds)
        {
            if (string.IsNullOrEmpty(id)) return "wrong_size";
            if (!seen.Add(id)) return "duplicate_member";
            if (alreadyEntered.Contains(id)) return "already_entered";
        }
        return null;
    }

    /// <summary>What an unrated player counts as, the same 1500/350 the ladder uses.</summary>
    internal const double DefaultRating = 1500;
    internal const double DefaultRd = 350;

    /// <summary><c>rating - 2·rd</c>, the conservative estimate the public ladder orders by.</summary>
    internal static double ConservativeRating((double Rating, double Rd)? row)
        => (row?.Rating ?? DefaultRating) - 2 * (row?.Rd ?? DefaultRd);

    /// <summary>One entrant as seeding sees it.</summary>
    internal sealed record SeedableEntrant(string EntrantId, IReadOnlyList<string> MemberIds, long RegisteredAt);

    /// <summary>
    /// Strongest first, numbered 1..N. A team counts the MEAN of its members, so one strong player
    /// cannot carry two novices to the top seed; ties break on registration order, then on id.
    /// </summary>
    internal static IReadOnlyList<(string EntrantId, int Seed)> SeedByRating(
        IEnumerable<SeedableEntrant> entrants, Func<string, (double Rating, double Rd)?> ratingOf)
    {
        return entrants
            .Select(e => new
            {
                e.EntrantId,
                e.RegisteredAt,
                Strength = e.MemberIds.Count == 0
                    ? ConservativeRating(null)
                    : e.MemberIds.Average(id => ConservativeRating(ratingOf(id))),
            })
            .OrderByDescending(x => x.Strength)
            .ThenBy(x => x.RegisteredAt)
            .ThenBy(x => x.EntrantId, StringComparer.Ordinal)
            .Select((x, i) => (x.EntrantId, i + 1))
            .ToList();
    }

    /// <summary>
    /// Who moves up when places free, first come first served. Decides nothing about whether the
    /// place is really free; the caller claims it.
    /// </summary>
    internal static IReadOnlyList<string> PromoteFromWaitlist(
        IEnumerable<(string EntrantId, string? Status, long RegisteredAt)> candidates, int freeSlots)
    {
        if (freeSlots <= 0) return Array.Empty<string>();
        return candidates
            .Where(c => c.Status == "waitlist")
            .OrderBy(c => c.RegisteredAt)
            .ThenBy(c => c.EntrantId, StringComparer.Ordinal)
            .Take(freeSlots)
            .Select(c => c.EntrantId)
            .ToList();
    }

    /// <summary>
    /// The status a new registration lands in: approval mode always asks; open mode is in while
    /// there is room and waits after.
    /// </summary>
    internal static string EntryStatusFor(string? entryMode, bool seatClaimed)
        => entryMode == "approval" ? "pending" : seatClaimed ? "confirmed" : "waitlist";

    /// <summary>The statuses that hold a place and therefore block entering again.</summary>
    internal static bool OccupiesTournament(string? status)
        => status is "pending" or "confirmed" or "waitlist";

    /// <summary>The one status that gets a bracket slot.</summary>
    internal static bool PlaysInBracket(string? status) => status == "confirmed";

    // ---------------------------------------------------------------- plumbing

    /// <summary>
    /// FNV-1a, 32 bits, one step per UTF-16 unit — which for ASCII is the published byte vectors.
    ///
    /// <para>NOT <c>string.GetHashCode</c>, which .NET randomises per process: a simulated result
    /// that changed every launch would make a screenshot impossible to take twice.</para>
    /// </summary>
    internal static uint StableHash(string s)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char c in s)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return hash;
        }
    }

    private static int Log2(int size)
    {
        int r = 0;
        while ((1 << r) < size) r++;
        return r;
    }
}
