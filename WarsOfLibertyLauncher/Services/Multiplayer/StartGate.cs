using System.Collections.Generic;
using System.Linq;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Why a competitive room may not start yet (design 55h), checked in the SAME order the server
/// checks it (src/lobbies/roomTeams.ts, <c>startRefusal</c>): a room that is not full, then a
/// player with no team, then uneven teams. The launcher shows the reason before Start is pressed;
/// the server refuses anyway — this is the explanation, not the guard.
/// </summary>
public static class StartGate
{
    public enum Reason
    {
        Ready,
        MissingPlayers,
        PlayerWithoutTeam,
        UnevenTeams,
    }

    public sealed record Verdict(Reason Reason, int Have = 0, int Need = 0,
        IReadOnlyList<string>? WithoutTeam = null, int Team1 = 0, int Team2 = 0)
    {
        public bool CanStart => Reason == Reason.Ready;
    }

    public sealed record Player(string UserId, string Name, int? Team);

    /// <summary>
    /// The verdict for a room. <paramref name="playingSeats"/> is max players minus spectator seats;
    /// teams exist only with 4 or 6. A casual room is never blocked; with
    /// <paramref name="teamsSupported"/> false (somebody's launcher cannot pick a team) only the
    /// headcount applies, exactly as the server does.
    /// </summary>
    public static Verdict Evaluate(
        bool competitive,
        int playingSeats,
        IReadOnlyList<Player> players,
        bool teamsSupported = true)
    {
        if (!competitive) return new Verdict(Reason.Ready);
        if (players.Count < playingSeats)
            return new Verdict(Reason.MissingPlayers, Have: players.Count, Need: playingSeats);
        if (!HasTeams(playingSeats) || !teamsSupported) return new Verdict(Reason.Ready);
        var without = players.Where(p => p.Team is not (1 or 2)).Select(p => p.Name).ToList();
        if (without.Count > 0) return new Verdict(Reason.PlayerWithoutTeam, WithoutTeam: without);
        var t1 = players.Count(p => p.Team == 1);
        var t2 = players.Count(p => p.Team == 2);
        if (t1 != t2) return new Verdict(Reason.UnevenTeams, Team1: t1, Team2: t2);
        return new Verdict(Reason.Ready);
    }

    /// <summary>Only 2v2 and 3v3 rooms have teams.</summary>
    public static bool HasTeams(int playingSeats) => playingSeats is 4 or 6;

    /// <summary>Seats per team.</summary>
    public static int TeamSize(int playingSeats) => playingSeats switch { 4 => 2, 6 => 3, _ => 0 };
}
