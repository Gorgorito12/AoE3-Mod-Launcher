using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>How one civilization did, over some set of matches.</summary>
/// <param name="Civ">The civilization's name as the mod calls it.</param>
/// <param name="Played">How many matches it was played in — INCLUDING the ones nobody could read.</param>
/// <param name="Wins">Matches won.</param>
/// <param name="Losses">Matches lost.</param>
public readonly record struct CivStatRow(string Civ, int Played, int Wins, int Losses)
{
    /// <summary>How many of those matches actually ended in a result.</summary>
    public int Decided => Wins + Losses;
}

/// <summary>
/// Turns a player's match history into a record per civilization.
///
/// <para>Pure and WPF-free like <see cref="CommunityStatsView"/> beside it, because what is worth
/// getting right here is a decision rather than a layout: how few matches is too few to state a
/// percentage.</para>
/// </summary>
public static class CivStatsView
{
    /// <summary>
    /// How many DECIDED matches a civilization needs before a percentage is published for it.
    ///
    /// <para><b>This is the whole reason this class exists.</b> The Profile already stopped
    /// showing a win rate to somebody with one decided match, because it printed "0 % wins" for a
    /// player whose single game was a loss — the most discouraging number the launcher could have
    /// chosen, and not a rate. A civilization table repeats that mistake once per civilization,
    /// and Wars of Liberty ships 188 of them: for months almost every row will hold two or three
    /// matches. So the record is always shown and the percentage almost never is.</para>
    /// </summary>
    public const int MinDecidedForPercent = 5;

    /// <summary>
    /// One row per civilization the player used, most played first.
    ///
    /// <para><b>Ordered by matches PLAYED, never by a percentage.</b> Sorting by a rate computed
    /// from a handful puts whoever went 1-0 with something at the top of the table and calls it
    /// the best, which is the same lie as printing the rate.</para>
    ///
    /// <para>Ties break on the civilization's name so the list does not reshuffle itself between
    /// two visits to the tab.</para>
    /// </summary>
    /// <param name="rows">The history page, as received. Nulls and civ-less matches are skipped.</param>
    public static IReadOnlyList<CivStatRow> Rows(IReadOnlyList<MatchHistoryRow>? rows)
    {
        if (rows == null || rows.Count == 0) return Array.Empty<CivStatRow>();

        var byCiv = new Dictionary<string, (int Played, int Wins, int Losses)>(StringComparer.OrdinalIgnoreCase);
        var display = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            // A match reported before civilizations were sent, or one whose recording could not be
            // joined to the roster. Most stored matches are like this and always will be.
            if (row == null || string.IsNullOrWhiteSpace(row.Civ)) continue;

            var civ = row.Civ!.Trim();
            display.TryAdd(civ, civ);

            byCiv.TryGetValue(civ, out var tally);
            tally.Played++;
            // Through the same classifier the history badge and the roster lines use, so this
            // table and the rows above it can never answer the same number differently — and a
            // 0.5 stays what it is: the outcome could not be read, which is not a draw.
            switch (MatchOutcomeView.Classify(row.Result))
            {
                case MatchVerdict.Win: tally.Wins++; break;
                case MatchVerdict.Loss: tally.Losses++; break;
            }
            byCiv[civ] = tally;
        }

        return byCiv
            .Select(kv => new CivStatRow(display[kv.Key], kv.Value.Played, kv.Value.Wins, kv.Value.Losses))
            .OrderByDescending(r => r.Played)
            .ThenBy(r => r.Civ, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The win percentage, or <b>null</b> when there is not enough behind it to state one.
    ///
    /// <para>Null is the common answer and callers must draw nothing for it — not an em dash
    /// where a number would go, and never a 0.</para>
    /// </summary>
    public static int? WinPercent(CivStatRow row)
        => row.Decided >= MinDecidedForPercent
            ? (int)Math.Round(row.Wins * 100.0 / row.Decided)
            : null;
}
