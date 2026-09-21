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
    /// How many DECIDED matches a rate needs behind it before the launcher will publish one.
    ///
    /// <para><b>One number for every surface.</b> The rule was invented for the civilization
    /// table (<see cref="CivStatsView.MinDecidedForPercent"/>, which now points here) after the
    /// Profile printed "0 % wins" to a player whose single decided match was a loss — the most
    /// discouraging number the launcher could have chosen, and not a rate. The ladder kept
    /// publishing it anyway: on the live table three players sat at "0 %" and one at "100 %",
    /// all off a single match.</para>
    ///
    /// <para><b>The Profile's own guard had quietly stopped working</b>, which is why this had
    /// to become a shared constant rather than a second copy. It keyed off the SERVER's
    /// <c>min_decided</c> — the ladder ENTRY bar — and that bar dropped from 5 to 1, so it went
    /// from hiding the rate below five matches to hiding it only from somebody with none. A
    /// threshold borrowed from another question stops protecting the moment that question's
    /// answer moves; "are you on the ladder" and "is this rate worth stating" are not the same
    /// question, and <see cref="ProfileSummaryView.IsProvisional"/> keeps the first one.</para>
    /// </summary>
    public const int MinDecidedForPercent = 5;

    /// <summary>
    /// The win rate, or <b>null</b> when there is not enough behind it to state one.
    ///
    /// <para>This is what a SURFACE should call. Null is the common answer and callers must
    /// draw nothing for it — not an em dash where a number would go, and never a 0. The record
    /// and the decided count are always shown, so nothing is hidden about the sample; only the
    /// rate computed from too little of it.</para>
    /// </summary>
    public static int? PublishableWinPercent(int wins, int losses)
        => DecidedGames(wins, losses) >= MinDecidedForPercent ? WinPercent(wins, losses) : null;

    /// <summary>
    /// Percentage of decided games won, rounded, or <b>null</b> when none has been decided —
    /// which is also what an older backend that sends no tally at all looks like.
    ///
    /// <para><b>Ungated on purpose.</b> This is the arithmetic; the question of whether a rate
    /// is worth showing belongs to <see cref="PublishableWinPercent"/>. Anything that renders a
    /// percentage to a player should call that one instead.</para>
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
