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

    /// <param name="ScoresBySlot">Recording slot to score, or null when the sides cannot be established.</param>
    /// <param name="Reason">A short English token naming why, logged by the caller — see <see cref="HostResultDecision"/>.</param>
    public readonly record struct TeamSlotDecision(
        IReadOnlyDictionary<int, double>? ScoresBySlot, string Reason);

    /// <summary>
    /// Every SLOT's score in a team match, read from the recording alone — no room, no names.
    ///
    /// <para><b>The losing side is the one side every member of which was a resign TARGET.</b>
    /// A team game ends when the last member of a side is out, and every resignation is a record
    /// in the file (<see cref="ReplayParserService.ReadResignations"/>). Over the five measured
    /// four-player recordings exactly one side is complete in each — the earlier resignations sit
    /// 12 KB to 271 KB before the end, which is why the 1v1 trailer alone could never decide a
    /// team game: it is only the LAST record, one casualty out of two or three.</para>
    ///
    /// <para><b>A removal counts against its target whoever sent it</b>, exactly as the 1v1 trailer
    /// has always treated a drop: in one measured 2v2 a player removed both dropped opponents, and
    /// they lost. The caller logs which ones were removals, so a disputed drop can be traced.</para>
    ///
    /// <para><b>Every clause is a refusal</b>, and null leaves the match at 0.5 for everyone —
    /// where every team match already was:</para>
    /// <list type="bullet">
    ///   <item>an AI among the players (a skirmish is not a match);</item>
    ///   <item>a player whose side is not known (see <see cref="ReplayParserService.ReplayPlayer.Team"/>);</item>
    ///   <item>not exactly two sides of equal size, or sides of one (a 1v1 is
    ///         <see cref="ResolveHostResult"/>'s question), or — when <paramref name="perSide"/> is
    ///         given — sides of any other size than the room declared;</item>
    ///   <item>BOTH sides complete, which no ended game can be;</item>
    ///   <item>the outcome block naming somebody on the OTHER side, which means the file's two
    ///         records of the ending disagree.</item>
    /// </list>
    ///
    /// <para><b>When no side is complete it falls back to the outcome block's side</b> — the rule
    /// team matches used before this. That is the copy of a player who closed his game right
    /// after resigning: it holds his record and nobody else's. It is the one case where a
    /// teammate who played on alone and turned it round would be read as a loss; the server only
    /// rates a team match when a reading from the OTHER side agrees, which is what catches it.</para>
    /// </summary>
    /// <param name="players">The recording's slots; their <see cref="ReplayParserService.ReplayPlayer.Team"/> is the side.</param>
    /// <param name="resignations">Every resign record in the file. Null or empty is allowed.</param>
    /// <param name="trailerLoserSlot">The slot the outcome block named, or -1.</param>
    /// <param name="perSide">Players per side the room declared (2 for 2v2, 3 for 3v3), or 0 for "any".</param>
    public static TeamSlotDecision ResolveTeamResultsBySlot(
        IReadOnlyList<ReplayParserService.ReplayPlayer>? players,
        IReadOnlyList<ReplayParserService.ResignRecord>? resignations,
        int trailerLoserSlot,
        int perSide = 0)
    {
        if (players == null || players.Count == 0)
            return new TeamSlotDecision(null, "no players");

        // A skirmish is not a match, whoever resigned in it.
        if (players.Any(p => !p.IsHuman))
            return new TeamSlotDecision(null, "an AI played");

        if (players.Any(p => p.Team < 0))
            return new TeamSlotDecision(null, "the sides are not known");

        var sides = players.GroupBy(p => p.Team).ToList();
        if (sides.Count != 2)
            return new TeamSlotDecision(null, $"{sides.Count} sides");
        if (sides[0].Count() != sides[1].Count())
            return new TeamSlotDecision(null, $"uneven sides {sides[0].Count()}/{sides[1].Count()}");
        if (sides[0].Count() < 2)
            return new TeamSlotDecision(null, "sides of one — a 1v1");
        if (perSide > 0 && sides[0].Count() != perSide)
            return new TeamSlotDecision(null, $"sides of {sides[0].Count()}, the room declared {perSide}");

        var targets = new HashSet<int>((resignations ?? Array.Empty<ReplayParserService.ResignRecord>())
            .Select(r => r.Target));
        var complete = sides.Where(side => side.All(p => targets.Contains(p.Slot))).ToList();

        int losingTeam;
        string how;
        if (complete.Count == 2)
            return new TeamSlotDecision(null, "both sides resigned");

        var trailerTeam = -1;
        if (trailerLoserSlot >= 0)
        {
            var named = players.FirstOrDefault(p => p.Slot == trailerLoserSlot);
            if (named == null)
                return new TeamSlotDecision(null, $"the outcome block names slot {trailerLoserSlot}, who is not in the game");
            trailerTeam = named.Team;
        }

        if (complete.Count == 1)
        {
            losingTeam = complete[0].Key;
            if (trailerTeam >= 0 && trailerTeam != losingTeam)
                return new TeamSlotDecision(null, "the outcome block names the other side");
            how = "every member of the losing side resigned or was removed";
        }
        else if (trailerTeam >= 0)
        {
            losingTeam = trailerTeam;
            how = "no side resigned whole — decided by the outcome block's side";
        }
        else
        {
            return new TeamSlotDecision(null, "no side resigned whole and there is no outcome block");
        }

        var scores = players.ToDictionary(p => p.Slot, p => p.Team == losingTeam ? 0.0 : 1.0);
        return new TeamSlotDecision(scores, how);
    }

    /// <summary>
    /// Every ACCOUNT's score in a team match: <see cref="ResolveTeamResultsBySlot"/> joined to the
    /// room's accounts through the in-game names each player published.
    ///
    /// <para><b>Only a join, on purpose.</b> Who won is decided from the file alone; the names
    /// only say which account sat in which slot. That is what lets a player CONFIRM a match with
    /// his own score from his own slot without anybody else's name, while the REPORT, which has
    /// to name every account, still needs all of them.</para>
    ///
    /// <para><b>All-or-nothing, through the strict <c>MatchSlotMap.Resolve</c>.</b>
    /// One missing or ambiguous name refuses everybody's score, because a result written against
    /// the wrong account takes points from somebody who was not there.</para>
    /// </summary>
    public static IReadOnlyDictionary<string, double>? ResolveTeamResults(
        IReadOnlyDictionary<string, string>? inGameNames,
        IReadOnlyList<ReplayParserService.ReplayPlayer>? players,
        IReadOnlyList<ReplayParserService.ResignRecord>? resignations,
        int loserSlot,
        int perSide = 0)
    {
        var bySlot = ResolveTeamResultsBySlot(players, resignations, loserSlot, perSide).ScoresBySlot;
        if (bySlot == null) return null;

        var accounts = MatchSlotMap.Resolve(players, inGameNames);
        if (accounts == null) return null;

        var scores = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (userId, player) in accounts)
        {
            if (!bySlot.TryGetValue(player.Slot, out var score)) return null;
            scores[userId] = score;
        }
        return scores;
    }
}
