using System;
using System.Collections.Generic;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Finds the match we just played inside our own history.
///
/// <para>Only the HOST reports a match, and the backend answers that POST with every
/// participant's rating change. A guest gets nothing: the room is closed with a bare
/// WebSocket close and no frame carries the result. So a guest who wants to see how the
/// match went has to go looking for it in <c>GET /matches/history</c> — and "the newest
/// row" is not good enough, for the same reason "the newest recording" is not: the row
/// that lands first may be an older match reported late, and someone who plays two games
/// in an evening would be shown the wrong one.</para>
///
/// <para>Pure, so the matching rule can be tested rather than trusted. It refuses rather
/// than guesses: a null answer shows "not there yet, check History", which is honest,
/// while a wrong row would tell someone they lost a match they won.</para>
/// </summary>
public static class MatchHistoryMatcher
{
    /// <summary>
    /// How far a stored start time may sit from ours and still be the same match.
    ///
    /// <para>Wide enough to absorb clock skew between this machine and the server (the
    /// backend stamps <c>started_at</c> itself), narrow enough that two matches of the
    /// same mod cannot both fall inside it — the shortest reportable match is three
    /// minutes.</para>
    /// </summary>
    public static readonly TimeSpan DefaultTolerance = TimeSpan.FromSeconds(120);

    /// <summary>
    /// The row for the match that started at <paramref name="startedUtc"/> in
    /// <paramref name="modId"/>, or null when no row is close enough to be sure.
    ///
    /// <para>When several rows qualify — which should not happen, but a retried report or
    /// a clock jump could produce it — the CLOSEST in time wins, and only it. Taking the
    /// first would depend on the server's ordering.</para>
    /// </summary>
    public static MatchHistoryRow? PickForMatch(
        IReadOnlyList<MatchHistoryRow>? rows,
        string? modId,
        DateTime startedUtc,
        TimeSpan? tolerance = null)
    {
        if (rows == null || rows.Count == 0 || string.IsNullOrWhiteSpace(modId)) return null;
        var window = tolerance ?? DefaultTolerance;

        MatchHistoryRow? best = null;
        var bestGap = TimeSpan.MaxValue;

        foreach (var row in rows)
        {
            if (!string.Equals(row.ModId, modId, StringComparison.OrdinalIgnoreCase)) continue;
            var started = ParseUtc(row.StartedAt);
            if (started == null) continue;

            var gap = (started.Value - startedUtc).Duration();
            if (gap > window) continue;
            if (gap >= bestGap) continue;

            best = row;
            bestGap = gap;
        }

        return best;
    }

    /// <summary>
    /// Read a stored timestamp as UTC.
    ///
    /// <para>SQLite's <c>datetime('now')</c> yields <c>'YYYY-MM-DD HH:MM:SS'</c> with no
    /// zone, which .NET reads as LOCAL time — so a machine east or west of UTC would slide
    /// the whole history by its offset and match nothing. Anything without an explicit zone
    /// is therefore assumed UTC, which is what the backend actually writes.</para>
    /// </summary>
    public static DateTime? ParseUtc(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!DateTime.TryParse(
                value,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal
                    | System.Globalization.DateTimeStyles.AssumeUniversal,
                out var parsed))
            return null;
        return parsed;
    }
}
