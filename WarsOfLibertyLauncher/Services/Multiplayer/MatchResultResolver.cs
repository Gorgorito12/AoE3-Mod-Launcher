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
}
