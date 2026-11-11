using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>One pairing as the Statistics page shows it, every version of the mod together.</summary>
/// <param name="CivA">The civilization the record belongs to.</param>
internal sealed record MatchupLine(string CivA, string CivB, int Played, int WinsA, int LossesA)
{
    /// <summary>Back to the wire shape, so the row builder the page already has can draw it.</summary>
    internal MatchupEntry ToEntry() => new()
    {
        CivA = CivA,
        CivB = CivB,
        Played = Played,
        WinsA = WinsA,
        LossesA = LossesA,
    };
}

/// <summary>
/// What the matchups and allies tables of the Statistics page show (design 98, reworked).
///
/// <para>The server sends up to 600 pairs, one per pair AND per version of the mod, and the page
/// used to draw every one: about 25,000 px for a single card, and most of the second a switch to
/// the subtab cost. The table now shows the most played pairs, a way to see one civilization's
/// pairs, and the rest on request inside a box of its own.</para>
/// </summary>
internal static class MatchupListView
{
    /// <summary>The pairs shown before "see all".</summary>
    internal const int TopCount = 8;

    /// <summary>How many rows the box with the full list or a civilization's pairs shows before
    /// it scrolls on its own; the page never grows past it.</summary>
    internal const int BoxRows = 10;

    /// <summary>
    /// One line per pair of civilizations, every version summed, most played first and, among
    /// equals, in the order the server sent them. A pair that arrives reversed (B against A) is
    /// folded into the first spelling with its record turned round — unless <paramref name="sameSide"/>,
    /// the allies table, where the record is the PAIR's and reads the same either way.
    /// </summary>
    internal static IReadOnlyList<MatchupLine> Merge(IEnumerable<MatchupEntry>? rows, bool sameSide = false)
    {
        var lines = new List<(string A, string B, int Played, int Wins, int Losses)>();
        var index = new Dictionary<(string, string), int>();
        foreach (var row in rows ?? Enumerable.Empty<MatchupEntry>())
        {
            if (row == null || string.IsNullOrWhiteSpace(row.CivA) || string.IsNullOrWhiteSpace(row.CivB)) continue;
            if (index.TryGetValue((row.CivA, row.CivB), out var i))
            {
                var l = lines[i];
                lines[i] = (l.A, l.B, l.Played + row.Played, l.Wins + row.WinsA, l.Losses + row.LossesA);
            }
            else if (index.TryGetValue((row.CivB, row.CivA), out var j))
            {
                var l = lines[j];
                lines[j] = sameSide
                    ? (l.A, l.B, l.Played + row.Played, l.Wins + row.WinsA, l.Losses + row.LossesA)
                    : (l.A, l.B, l.Played + row.Played, l.Wins + row.LossesA, l.Losses + row.WinsA);
            }
            else
            {
                index[(row.CivA, row.CivB)] = lines.Count;
                lines.Add((row.CivA, row.CivB, row.Played, row.WinsA, row.LossesA));
            }
        }

        return lines
            .Select((l, order) => (Line: new MatchupLine(l.A, l.B, l.Played, l.Wins, l.Losses), Order: order))
            .OrderByDescending(x => x.Line.Played)
            .ThenBy(x => x.Order)
            .Select(x => x.Line)
            .ToList();
    }

    /// <summary>The first <paramref name="count"/> of a list already in order.</summary>
    internal static IReadOnlyList<MatchupLine> Top(IReadOnlyList<MatchupLine> lines, int count = TopCount)
        => lines.Take(Math.Max(0, count)).ToList();

    /// <summary>
    /// Every pair one civilization took part in, READ FROM ITS SIDE: when it is the second of the
    /// pair the record is turned round, so "Germans vs Dutch 7-3" always means the Germans won
    /// seven. Most played first, ties in the merged order. With <paramref name="sameSide"/> (allies)
    /// only the names swap: the record is the pair's.
    /// </summary>
    internal static IReadOnlyList<MatchupLine> For(IReadOnlyList<MatchupLine> lines, string civ, bool sameSide = false)
        => lines
            .Where(l => string.Equals(l.CivA, civ, StringComparison.Ordinal)
                        || string.Equals(l.CivB, civ, StringComparison.Ordinal))
            .Select(l => string.Equals(l.CivA, civ, StringComparison.Ordinal)
                ? l
                : sameSide
                    ? new MatchupLine(civ, l.CivA, l.Played, l.WinsA, l.LossesA)
                    : new MatchupLine(civ, l.CivA, l.Played, l.LossesA, l.WinsA))
            .ToList();

    /// <summary>The civilizations the pairs mention, each once, in no particular order.</summary>
    internal static IReadOnlyList<string> Civs(IReadOnlyList<MatchupLine> lines)
        => lines.SelectMany(l => new[] { l.CivA, l.CivB })
            .Distinct(StringComparer.Ordinal)
            .ToList();
}
