using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// "You vs them" at the top of another player's profile: how the viewer has done against him,
/// read from the subject's own history page (the matches both played are in it).
///
/// <para>The page holds at most <see cref="HistoryPageSize"/> matches, so "never played" is said
/// only when the page is shorter than that: a full page may have dropped the meetings that
/// happened before it. Pure, pinned by <c>VersusViewTests</c>.</para>
/// </summary>
public static class VersusView
{
    /// <summary>The server's history page (<c>GET /matches/history/:id</c>, LIMIT 50).</summary>
    public const int HistoryPageSize = 50;

    public const int RecentShown = 3;

    public sealed record Meeting(MatchHistoryRow Match, bool? ViewerWon);

    /// <param name="Wins">Decided meetings the viewer won.</param>
    /// <param name="Losses">Decided meetings the viewer lost.</param>
    /// <param name="Played">Every meeting on the page, decided or not.</param>
    /// <param name="Complete">The page held the whole history, so a zero means "never".</param>
    public sealed record Summary(int Wins, int Losses, int Played, bool Complete, IReadOnlyList<Meeting> Recent)
    {
        public bool NeverPlayed => Played == 0 && Complete;
    }

    /// <summary>
    /// The meetings on <paramref name="subjectHistory"/> between <paramref name="viewerId"/> and
    /// <paramref name="subjectId"/>. A meeting is a match that names both; the viewer won it when
    /// his result is a win and lost it when a loss. A team game where they played TOGETHER counts
    /// as played, never as a win or a loss against him. Null when there is nothing to say: no
    /// viewer, the viewer's own profile, or no page yet.
    /// </summary>
    public static Summary? For(IReadOnlyList<MatchHistoryRow>? subjectHistory, string? viewerId, string? subjectId)
    {
        if (subjectHistory == null || string.IsNullOrEmpty(viewerId) || string.IsNullOrEmpty(subjectId)) return null;
        if (string.Equals(viewerId, subjectId, StringComparison.Ordinal)) return null;

        var meetings = new List<Meeting>();
        int wins = 0, losses = 0;
        foreach (var row in subjectHistory)
        {
            var parts = row.Participants ?? new List<MatchHistoryParticipant>();
            var me = parts.FirstOrDefault(p => string.Equals(p.UserId, viewerId, StringComparison.Ordinal));
            var them = parts.FirstOrDefault(p => string.Equals(p.UserId, subjectId, StringComparison.Ordinal));
            if (me == null || them == null) continue;

            bool together = me.Team != 0 && me.Team == them.Team && parts.Count > 2;
            bool? won = together ? null
                : me.Result >= 0.999 ? true
                : me.Result <= 0.001 ? false
                : null;
            if (won == true) wins++;
            else if (won == false) losses++;
            meetings.Add(new Meeting(row, won));
        }

        return new Summary(wins, losses, meetings.Count, subjectHistory.Count < HistoryPageSize,
            meetings.Take(RecentShown).ToList());
    }
}
