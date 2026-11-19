using System;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>"23 points to Imperial · 3 places", and how far up his own age the player is.</summary>
/// <param name="NextAge">The age one step up.</param>
/// <param name="Points">ELO still to gain, one more than the gap: level on ELO is not ahead.</param>
/// <param name="Places">Places between the player and the last place of that age.</param>
/// <param name="Fill">How far up through his CURRENT age the player stands, 0-1, from places only
/// (the bottom of the age is 1 / size of the age, its top place is 1). Never a guess at ratings.</param>
public sealed record BadgeProgress(RankAge NextAge, int Points, int Places, double Fill);

/// <summary>
/// Progress to the next badge (2026-11-18), from the server's <c>next_badge</c> (src/stats/standing.ts).
/// Ages are a share of the table, so the rating that earns the next one is only known to the server;
/// this decides whether to draw it and turns it into figures. Nothing is drawn while placing, with a
/// private rating, off the table, at the top age, or when the server's answer and the page disagree
/// (a place that moved a minute ago, an age the badge does not wear) — two different ages on one
/// screen would be worse than none. Pure; pinned by <c>BadgeProgressViewTests</c>.
/// </summary>
public static class BadgeProgressView
{
    public static BadgeProgress? For(NextBadge? next, int place, int ladderSize, int printedElo,
        bool ranked, bool privateRating)
    {
        if (next == null || !ranked || privateRating || place <= 0 || ladderSize <= 0) return null;
        // The server and the page must agree on where he stands and which age is above him.
        if (next.AtRank + next.Places != place) return null;
        var expected = RankAges.PlacesToNextAge(place, ladderSize);
        if (expected is not { } up || up.Places != next.Places || AgeOf(next.Age) != up.Age) return null;

        var points = Math.Max(1, (int)Math.Round(next.FloorRating) + 1 - printedElo);
        var bounds = RankAges.Bounds(ladderSize);
        // The player's own age runs from just under the next age's last place to its own last place.
        var top = next.AtRank + 1;
        var bottom = ladderSize;
        foreach (var b in bounds) if (b >= place) { bottom = b; break; }
        var width = Math.Max(1, bottom - top + 1);
        var fill = Math.Clamp((bottom - place + 1) / (double)width, 0, 1);
        return new BadgeProgress(up.Age, points, next.Places, fill);
    }

    /// <summary>The server's age name; Discovery for anything unknown, which never matches.</summary>
    public static RankAge AgeOf(string? name) => name switch
    {
        "sovereign" => RankAge.Sovereign,
        "imperial" => RankAge.Imperial,
        "industrial" => RankAge.Industrial,
        "fortress" => RankAge.Fortress,
        "colonial" => RankAge.Colonial,
        _ => RankAge.Discovery,
    };
}
