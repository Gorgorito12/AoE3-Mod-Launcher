using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>One player of a finished match, resolved down to what a history row draws.</summary>
/// <param name="UserId">Backend user id — the key the "you" marker is decided on.</param>
/// <param name="Name">Display name, already fallen back through the backend's own chain.</param>
/// <param name="AvatarUrl">Discord avatar, or null for the monogram fallback.</param>
/// <param name="IsMe">Whether this is the player looking at the list.</param>
/// <param name="Verdict">Won / lost / nobody knows — never "draw"; see <see cref="MatchOutcomeView.Classify"/>.</param>
/// <param name="RatingDelta">What the match did to their rating, or null when it cannot be stated.</param>
/// <param name="Team">
/// Which side they played on. <b>0 for everyone in a 1v1 and in every match reported before the
/// launcher could work teams out</b>, which is what makes the caller's grouping a no-op there —
/// see <c>HasTeams</c>.
/// </param>
public sealed record MatchParticipantLine(
    string UserId,
    string Name,
    string? AvatarUrl,
    bool IsMe,
    MatchVerdict Verdict,
    int? RatingDelta,
    int Team = 0);

/// <summary>
/// Turns a history row's participants into the lines under it — who played, and who won.
///
/// <para>Pure and WPF-free, like <see cref="MatchOutcomeView"/> and <see cref="PlayerStanding"/>
/// beside it, because the rules worth getting right here are decisions rather than layout: a
/// 0.5 must not become a winner, an unknown rating must not become "+0", and the order must
/// not depend on what the server happened to send.</para>
/// </summary>
public static class MatchParticipantsView
{
    /// <summary>
    /// The lines to draw, winner first. Empty when the backend sent no participants — which
    /// is every backend older than this feature, and is why the caller keeps its "N players"
    /// chip in that case instead of showing nothing at all.
    /// </summary>
    /// <param name="participants">The row's participants, as received.</param>
    /// <param name="myUserId">The signed-in player's id, or null when signed out.</param>
    public static IReadOnlyList<MatchParticipantLine> Build(
        IReadOnlyList<MatchHistoryParticipant>? participants,
        string? myUserId)
    {
        if (participants == null || participants.Count == 0)
            return System.Array.Empty<MatchParticipantLine>();

        // Ordered HERE and not merely trusted from the server. The backend sorts the same
        // way, but a client that depends on that silently reshuffles the moment the query
        // changes — and the winner sitting first is the whole point of the block.
        //
        // By score descending, so a won match reads winner-then-loser, and a match nobody
        // could read (every score 0.5) keeps the order it arrived in. OrderByDescending is
        // stable, which is what makes that second half true.
        return participants
            .OrderByDescending(p => p.Result)
            .Select(p => new MatchParticipantLine(
                p.UserId,
                NameOf(p),
                string.IsNullOrWhiteSpace(p.AvatarUrl) ? null : p.AvatarUrl,
                !string.IsNullOrEmpty(myUserId) && p.UserId == myUserId,
                MatchOutcomeView.Classify(p.Result),
                MatchOutcomeView.Delta(p.RatingBefore, p.RatingAfter),
                p.Team))
            .ToList();
    }

    /// <summary>
    /// Whether these lines describe a match with SIDES worth drawing separately.
    ///
    /// <para>Two or more distinct teams. A 1v1 reports 0 for both players, and so does every
    /// match stored before the launcher could work teams out at all — so this is false for all
    /// of them and the caller renders exactly what it rendered before. That equivalence is the
    /// property worth protecting: the common case must not change.</para>
    /// </summary>
    public static bool HasTeams(IReadOnlyList<MatchParticipantLine>? lines)
        => lines != null && lines.Select(l => l.Team).Distinct().Count() > 1;

    /// <summary>
    /// Display name, then Discord handle, then a placeholder.
    ///
    /// <para>The same chain the backend itself falls through when it names a room's host, so
    /// one person is not called two different things in two places.</para>
    /// </summary>
    private static string NameOf(MatchHistoryParticipant p)
        => !string.IsNullOrWhiteSpace(p.DisplayName) ? p.DisplayName
         : !string.IsNullOrWhiteSpace(p.DiscordUsername) ? p.DiscordUsername
         : "?";
}
