using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// The room's win probability (design 55g/55h). The SERVER computes it with Glicko — mean rating
/// and combined uncertainty of each side — and sends it; the launcher never works it out, so this
/// class only reads the payload. Null means "not shown": a team is still empty, the second seat is
/// not taken, or the server said nothing.
/// </summary>
public static class WinOddsView
{
    /// <summary>A player's own chance in a 1v1 room.</summary>
    public static int? ForPlayer(RoomOdds? odds, string? userId)
        => odds?.Players != null && userId != null && odds.Players.TryGetValue(userId, out var p) ? p : null;

    /// <summary>Team 1's and Team 2's chances, or null while either team is empty.</summary>
    public static (int Team1, int Team2)? ForTeams(RoomOdds? odds)
        => odds?.Teams != null
           && odds.Teams.TryGetValue("1", out var a)
           && odds.Teams.TryGetValue("2", out var b)
            ? (a, b) : null;
}
