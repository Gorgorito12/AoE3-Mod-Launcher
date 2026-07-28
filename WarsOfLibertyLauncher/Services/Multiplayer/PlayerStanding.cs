using System;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Turns a player's server-side tally into the number shown on the profile.
///
/// <para><b>The win rate is over DECIDED games, and that is the whole point of this class
/// existing.</b> A match is stored with <c>result = 0.5</c> whenever the outcome could not be
/// read — no recording, a team game, a skirmish, or any match reported before the launcher
/// could read one at all — and that is the majority of stored matches, not an edge case.
/// Dividing wins by games played would report <b>3&#160;%</b> for someone who won 3 of their 4
/// decided games: a number that is both false and demoralising.</para>
///
/// <para>Same rule as the History badge, which shows nothing rather than "Draw" for a 0.5.
/// Here it means: no decided games, no rate — never a 0&#160;%.</para>
/// </summary>
public static class PlayerStanding
{
    /// <summary>
    /// Percentage of decided games won, rounded, or <b>null</b> when none has been decided —
    /// which is also what an older backend that sends no tally at all looks like.
    /// </summary>
    public static int? WinPercent(int wins, int losses)
    {
        // Defensive: these arrive over the wire, and a negative would otherwise produce a
        // rate above 100 or a division by a negative denominator.
        if (wins < 0) wins = 0;
        if (losses < 0) losses = 0;

        var decided = wins + losses;
        if (decided <= 0) return null;

        return (int)Math.Round(wins * 100.0 / decided, MidpointRounding.AwayFromZero);
    }

    /// <summary>How many games the rate above is actually based on.</summary>
    public static int DecidedGames(int wins, int losses)
        => Math.Max(0, wins) + Math.Max(0, losses);
}
