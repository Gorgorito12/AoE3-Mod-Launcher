using System;
using System.Collections.Generic;
using System.Linq;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Turns "the recording says the host won" into the score each player is reported with.
///
/// <para><b>This is the one place where a mistake moves rating points between two real people.</b>
/// Everything upstream only decides whether a recording can be trusted; this decides who gets
/// credited. It lived as a private method with no tests at all, which is why it is here: pure,
/// free of WPF, and taking a plain <c>double?</c> rather than the caller's own record so nothing
/// from the UI crosses the boundary.</para>
///
/// <para>Refusing is the safe answer and the common one. A result that cannot be established
/// beyond doubt is reported as 0.5 for everyone — <b>which means "not known", not "draw"</b> — so
/// a wrong guess never costs somebody a game they won. Sibling of
/// <see cref="PlayerStanding"/>, which is where that same 0.5 has to be excluded again when the
/// win rate is worked out.</para>
/// </summary>
public static class MatchResultResolver
{
    /// <summary>The score a player is reported with when nothing could be established.</summary>
    public const double Unknown = 0.5;

    /// <param name="Result">The host's score, or null when the match must go down as unknown.</param>
    /// <param name="Reason">
    /// A short English token naming why, logged by the caller. Returned rather than logged here so
    /// this stays pure — and so the reason itself can be tested, since "it refused" and "it
    /// refused for the right cause" are different claims.
    /// </param>
    public readonly record struct HostResultDecision(double? Result, string Reason);

    /// <summary>
    /// Whether the recording's verdict may be applied to this room.
    ///
    /// <para>Two gates beyond the recording itself. <b>Exactly two participants</b>, because the
    /// recording names one loser and the score of everyone else can only be inferred in a 1v1 —
    /// in a team game "the host lost" says nothing about the other three. And <b>the host must be
    /// among them</b>: the reporter is the player whose recording was read, so if they are not in
    /// the list being reported, the room's roster and the file disagree and nothing here can name
    /// the other player.</para>
    /// </summary>
    public static HostResultDecision ResolveHostResult(
        double? replayHostResult, IReadOnlyList<string>? participantIds, string? hostId)
    {
        if (replayHostResult == null)
            return new HostResultDecision(null, "the recording gave no result");

        if (participantIds == null || participantIds.Count != 2)
            return new HostResultDecision(
                null, $"the room had {participantIds?.Count ?? 0} players, not 2");

        if (string.IsNullOrEmpty(hostId))
            return new HostResultDecision(null, "no host id");

        if (!participantIds.Contains(hostId))
            return new HostResultDecision(null, "the host is not in the participant list");

        return new HostResultDecision(replayHostResult, "read from the recording");
    }

    /// <summary>
    /// One participant's score, given the host's.
    ///
    /// <para>The mirror image, because a 1v1 has exactly one winner: the backend validates that the
    /// scores sum to half the player count, and Glicko takes them at face value. Its own line
    /// because <c>1.0 - x</c> written inline is easy to read past and impossible to test.</para>
    /// </summary>
    public static double ParticipantResult(double hostResult, bool isHost)
        => isHost ? hostResult : 1.0 - hostResult;

    /// <summary>
    /// Every player's score in a TEAM match, from the one slot the recording names.
    ///
    /// <para><b>Why a 2v2 is readable at all when a free-for-all is not.</b> The trailer names
    /// exactly one loser, which on its own says nothing about the other three — that is the
    /// refusal <see cref="ResolveHostResult"/> makes and keeps making. What changes with two
    /// SIDES is that naming one loser names a whole side: the other side is what is left. That
    /// is the entire idea, and it needs no new bytes out of the file, only the team map the room
    /// already builds.</para>
    ///
    /// <para>Deliberately built on the same in-game names <see cref="MatchTeamMap"/> uses, and
    /// on the map it produced, so the slot the file names and the accounts the room knows are
    /// joined in exactly one place. Going from the loser's SLOT to their side any other way —
    /// re-reading raw team ids here — would be a second answer to a question that already has
    /// one, and the two could disagree.</para>
    ///
    /// <para>Every clause is a refusal, and null means "report 0.5 for everyone", which is what
    /// every team match did before this existed. That is the property to protect: a wrong side
    /// takes points from people with nothing on screen to explain it, while a refusal only
    /// leaves the match where it already was.</para>
    /// </summary>
    /// <param name="teams">userId to normalised side, from <see cref="MatchTeamMap.Resolve"/>.</param>
    /// <param name="inGameNames">userId to AoE3 profile name, frozen at match start.</param>
    /// <param name="players">The recording's own player list.</param>
    /// <param name="loserSlot">The slot the trailer named, or -1.</param>
    public static IReadOnlyDictionary<string, double>? ResolveTeamResults(
        IReadOnlyDictionary<string, int>? teams,
        IReadOnlyDictionary<string, string>? inGameNames,
        IReadOnlyList<ReplayParserService.ReplayPlayer>? players,
        int loserSlot)
    {
        if (teams == null || teams.Count == 0) return null;
        if (inGameNames == null || inGameNames.Count == 0) return null;
        if (players == null || players.Count == 0) return null;
        if (loserSlot < 0) return null;

        // A skirmish is not a match, whoever the trailer says lost it. ReadOutcome makes the
        // same refusal for a 1v1 and cannot make it here, because it returns the loser slot
        // before it ever looks at the players in a team-sized game.
        if (players.Any(p => !p.IsHuman)) return null;

        // Two sides, equal size. This mirrors the server's own shape check, and it is what
        // makes "the other side won" a complete answer: with three sides it is not.
        var sides = teams.Values.GroupBy(t => t).Select(g => g.Count()).ToList();
        if (sides.Count != 2) return null;
        if (sides[0] != sides[1]) return null;

        var loser = players.FirstOrDefault(p => p.Slot == loserSlot);
        if (loser == null || string.IsNullOrWhiteSpace(loser.Name)) return null;

        // Same comparison MatchTeamMap makes, so one machine's answer about the loser and the
        // map's answer about everybody can never disagree.
        string? loserId = null;
        foreach (var (userId, declared) in inGameNames)
        {
            if (!string.Equals(declared.Trim(), loser.Name.Trim(), StringComparison.OrdinalIgnoreCase))
                continue;
            // Two accounts claiming the same profile name: the map refuses that outright, so
            // reaching here means something changed underneath us.
            if (loserId != null) return null;
            loserId = userId;
        }
        if (loserId == null) return null;

        if (!teams.TryGetValue(loserId, out var losingSide)) return null;

        return teams.ToDictionary(
            kv => kv.Key,
            kv => kv.Value == losingSide ? 0.0 : 1.0,
            StringComparer.Ordinal);
    }
}
