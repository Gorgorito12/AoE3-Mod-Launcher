using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>One player of a side of a bracket match.</summary>
public sealed record TournamentRoomPlayer(string UserId, string Name, bool Captain);

/// <summary>One side of a bracket match.</summary>
public sealed record TournamentRoomSide(string EntrantId, string Name, int? Seed, IReadOnlyList<TournamentRoomPlayer> Players);

/// <summary>What the match leads to: the round after it and who waits there.</summary>
public sealed record TournamentRoomNext(string RoundPhraseKey, string? Rival, string? PairA, string? PairB);

/// <summary>A line of "what's at stake", with its dot.</summary>
public sealed record TournamentStakeLine(RoomText Text, TournamentStakeTone Tone);

public enum TournamentStakeTone { Mod, Quiet, Elo, Green }

/// <summary>
/// What a room needs to know about its bracket slot (design 85). A room carries only its
/// <c>tournament_match_id</c>; the rest is the tournament detail the Tournaments tab already loads,
/// read here into one plain record so that nothing in the window walks the bracket.
/// </summary>
public sealed record TournamentRoomContext(
    string TournamentId,
    string Name,
    string? OrganizerName,
    string Format,
    bool IsTeams,
    IReadOnlyList<string> RoundKeys,
    int CurrentRound,
    TournamentRoomSide Side1,
    TournamentRoomSide Side2,
    TournamentRoomNext? Next,
    string? PendingReport)
{
    public bool IsFinal => Next == null;

    public int PlayerCount => Side1.Players.Count + Side2.Players.Count;

    /// <summary>Which side the player is on: 1, 2, or 0 for somebody who is neither.</summary>
    public int SideOf(string? userId)
    {
        if (userId == null) return 0;
        if (Side1.Players.Any(p => p.UserId == userId)) return 1;
        return Side2.Players.Any(p => p.UserId == userId) ? 2 : 0;
    }

    /// <summary>The other side, seen from a player.</summary>
    public TournamentRoomSide RivalOf(string? userId) => SideOf(userId) == 2 ? Side1 : Side2;

    /// <summary>
    /// Reads the slot out of a tournament. Null when the match or one of its sides is not there:
    /// a room with a half-known bracket keeps the plain header instead of inventing a rival.
    /// </summary>
    public static TournamentRoomContext? From(TournamentDetail t, string? matchId)
    {
        var match = t.Matches?.FirstOrDefault(m => m.Id == matchId);
        if (match == null || t.Entrants == null) return null;
        var one = t.Entrants.FirstOrDefault(e => e.Id == match.Entrant1Id);
        var two = t.Entrants.FirstOrDefault(e => e.Id == match.Entrant2Id);
        if (one == null || two == null) return null;

        var total = t.RoundsTotal ?? t.Matches!.Max(m => m.Round);
        var rounds = Enumerable.Range(1, Math.Max(1, total))
            .Select(r => BracketLayout.RoundLabelKey(r, total)).ToList();
        return new TournamentRoomContext(
            t.Id, t.Name, OrganizerOf(t), t.Format ?? "1v1", IsTeamFormat(t.Format),
            rounds, match.Round, SideOf(one), SideOf(two), NextOf(t, match, total),
            match.PendingReport?.State);
    }

    private static string? OrganizerOf(TournamentSummary t)
        => string.IsNullOrWhiteSpace(t.OwnerName) ? null : t.OwnerName;

    private static bool IsTeamFormat(string? format) => format is "2v2" or "3v3";

    private static TournamentRoomSide SideOf(TournamentEntrant e)
    {
        var name = string.IsNullOrWhiteSpace(e.DisplayName) ? "?" : e.DisplayName!;
        List<TournamentRoomPlayer> players = e.Members is { Count: > 0 }
            ? e.Members.Select(m => new TournamentRoomPlayer(
                m.UserId, string.IsNullOrWhiteSpace(m.DisplayName) ? name : m.DisplayName!,
                m.UserId == e.CaptainUserId)).ToList()
            : (e.MemberIds ?? new List<string>()).Select(id =>
                new TournamentRoomPlayer(id, name, id == e.CaptainUserId)).ToList();
        return new TournamentRoomSide(e.Id, name, e.Seed, players);
    }

    private static TournamentRoomNext? NextOf(TournamentDetail t, TournamentMatch match, int total)
    {
        var next = t.Matches!.FirstOrDefault(m => m.Id == match.NextMatchId);
        if (next == null) return null;
        var phrase = BracketLayout.RoundPhraseKey(next.Round, total);

        // Who waits there is the other slot of that match, or the two who feed it.
        var otherId = match.NextSlot == 1 ? next.Entrant2Id : next.Entrant1Id;
        if (otherId != null) return new TournamentRoomNext(phrase, NameOf(t, otherId), null, null);
        var feeder = t.Matches!.FirstOrDefault(m => m.NextMatchId == next.Id && m.Id != match.Id);
        var a = NameOf(t, feeder?.Entrant1Id);
        var b = NameOf(t, feeder?.Entrant2Id);
        return a != null && b != null
            ? new TournamentRoomNext(phrase, null, a, b)
            : new TournamentRoomNext(phrase, null, null, null);
    }

    private static string? NameOf(TournamentDetail t, string? entrantId)
        => t.Entrants!.FirstOrDefault(e => e.Id == entrantId)?.DisplayName;

    /// <summary>"The winner plays the quarters against…", in the words the bracket allows.</summary>
    private RoomText WinnerLine() => Next switch
    {
        null => new(IsTeams ? "RtStakeFinalTeam" : "RtStakeFinal"),
        { Rival: { } rival } => new("RtStakeRival", new RoomKey(Next.RoundPhraseKey), rival),
        { PairA: { } a, PairB: { } b } => new("RtStakePair", new RoomKey(Next.RoundPhraseKey), a, b),
        _ => new("RtStakeRound", new RoomKey(Next.RoundPhraseKey)),
    };

    /// <summary>QUÉ ESTÁ EN JUEGO, before the match.</summary>
    public IReadOnlyList<TournamentStakeLine> StakeLines() => new[]
    {
        new TournamentStakeLine(WinnerLine(), TournamentStakeTone.Mod),
        new TournamentStakeLine(new(IsTeams ? "RtStakeConfirm" : "RtStakeOut"), TournamentStakeTone.Quiet),
        new TournamentStakeLine(IsTeams ? new("RtStakeEloAll", new RoomKey("RtCount" + PlayerCount)) : new("RoomFtCountsBoth"), TournamentStakeTone.Elo),
    };

    /// <summary>RESULTADO, once the bracket moved.</summary>
    public IReadOnlyList<TournamentStakeLine> ResultLines(bool won, string? userId)
    {
        var rival = RivalOf(userId).Name;
        var mine = SideOf(userId) == 2 ? Side2.Name : Side1.Name;
        if (won)
        {
            var through = Next == null
                ? new RoomText("RtWonTournament", Name)
                : new RoomText("RtThrough", new RoomKey(Next.RoundPhraseKey));
            return new[]
            {
                new TournamentStakeLine(through, TournamentStakeTone.Green),
                new TournamentStakeLine(new("RtRivalOut", rival), TournamentStakeTone.Quiet),
            };
        }
        return Next == null
            ? new[] { new TournamentStakeLine(new("RtYouOut"), TournamentStakeTone.Quiet) }
            : new[]
            {
                new TournamentStakeLine(new("RtYouOut"), TournamentStakeTone.Quiet),
                new TournamentStakeLine(new("RtRivalThrough", rival, new RoomKey(Next.RoundPhraseKey)), TournamentStakeTone.Quiet),
            };
    }

    /// <summary>The footer's line after a win: who is next, when the bracket knows.</summary>
    public RoomText? AfterWin() => Next switch
    {
        null => null,
        { Rival: { } rival } => new("RtNextRival", new RoomKey(Next.RoundPhraseKey), rival),
        { PairA: { } a, PairB: { } b } => new("RtNextPair", new RoomKey(Next.RoundPhraseKey), a, b),
        _ => new("RtNextRound", new RoomKey(Next.RoundPhraseKey)),
    };
}

/// <summary>An argument of a <see cref="RoomText"/> that is itself a string key.</summary>
public sealed record RoomKey(string Key);
