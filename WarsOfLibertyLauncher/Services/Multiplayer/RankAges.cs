namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// The visible rank a player wears, named after AoE3's ages. Ordered lowest to highest so a
/// comparison reads the way the ladder does.
/// </summary>
public enum RankAge
{
    /// <summary>Not on the ladder: fewer rated matches than the server's entry bar. No colour
    /// and no light on purpose — if the first step already glowed, climbing would not show.</summary>
    Discovery,
    Colonial,
    Fortress,
    Industrial,
    Imperial,
    /// <summary>First place on the ladder, and only first place. It changes hands the moment
    /// somebody overtakes it.</summary>
    Sovereign,
}

/// <summary>
/// Which age a ladder position earns. The one rule three screens share — the Ranking table,
/// the rooms row and the room's player panel — so it lives here, with no WPF, and nowhere else.
///
/// <para><b>The age comes from the POSITION, never from the rating that is printed.</b> The
/// ladder is ordered by the conservative rating (<c>rating − 2·rd</c>, see
/// <see cref="RankingTableLayout.ConservativeRating"/>), so the printed numbers do not descend
/// down the table: a player on 1720 with two matches sits fourth behind three players on less.
/// An age read off the printed number would put the highest badge on the page under three
/// lower ones, and the table and the badge would contradict each other in every row.</para>
///
/// <para><b>By a SHARE of the table, not by a fixed position</b>, so the ages grow with the
/// community on their own. It used to be 1 / 2 / 3-4 / 5-6 / rest, which left exactly one red
/// badge however many played; reported as "only one player has the red one". The cuts are
/// cumulative: Sovereign the top 10 %, Imperial the next 15 %, Industrial the next 20 %, Fortress
/// the next 25 %, Colonial the rest — each rounded UP and each at least one place wide, so a
/// small table still populates every age. With 18 players: 1-2 / 3-5 / 6-9 / 10-13 / 14-18.
/// Still never thresholds on the rating: see the paragraph above.</para>
///
/// <para><b>The size comes from the server</b> (<c>ranked_players</c>), counted with the same
/// WHERE as the list and as <c>ladder_rank</c>, so a position and its denominator cannot filter
/// differently. When it is unknown — an older backend, or community stats not loaded yet — the
/// old fixed positions are the fallback, which is exactly what every launcher drew before.</para>
///
/// <para><b>Discovery is "not on the ladder"</b> — the server's own entry rule
/// (<c>MIN_DECIDED</c>, sent as <c>min_decided</c>), never a number chosen here. The server
/// encodes it as a <c>ladder_rank</c> of 0; the Ranking table never shows it because nobody
/// below the bar is in that table.</para>
/// </summary>
public static class RankAges
{
    // The fixed positions below are the FALLBACK for an unknown ladder size only.

    /// <summary>Last position that is still Sovereign. First place, alone.</summary>
    public const int SovereignMaxPosition = 1;

    /// <summary>Last position that is still Imperial.</summary>
    public const int ImperialMaxPosition = 2;

    /// <summary>Last position that is still Industrial.</summary>
    public const int IndustrialMaxPosition = 4;

    /// <summary>Last position that is still Fortress. Everybody below it on the ladder is Colonial.</summary>
    public const int FortressMaxPosition = 6;

    /// <summary>Cumulative share of the table each age reaches, top down: Sovereign, Imperial,
    /// Industrial, Fortress. Everything past the last is Colonial.</summary>
    public static readonly double[] CumulativeShares = { 0.10, 0.25, 0.45, 0.70 };

    /// <summary>
    /// The last position of Sovereign, Imperial, Industrial and Fortress for a table of
    /// <paramref name="ladderSize"/> players. Each cut is rounded UP and at least one place past
    /// the previous one, so no age is empty until the table runs out.
    /// </summary>
    public static int[] Bounds(int ladderSize)
    {
        var bounds = new int[CumulativeShares.Length];
        var previous = 0;
        for (var i = 0; i < bounds.Length; i++)
        {
            var cut = (int)Math.Ceiling(ladderSize * CumulativeShares[i] - 1e-9);
            previous = bounds[i] = Math.Max(previous + 1, cut);
        }
        return bounds;
    }

    /// <summary>
    /// The age of a ladder position as the SERVER numbered it (1 = first), never renumbered.
    /// Anything below 1 means "not on the ladder" and is Discovery. <paramref name="ladderSize"/>
    /// is how many are on that ladder; unknown (null or 0) falls back to the fixed positions.
    /// </summary>
    public static RankAge For(int ladderPosition, int? ladderSize = null)
    {
        if (ladderPosition <= 0) return RankAge.Discovery;
        var b = ladderSize is int n && n > 0
            ? Bounds(n)
            : new[] { SovereignMaxPosition, ImperialMaxPosition, IndustrialMaxPosition, FortressMaxPosition };
        if (ladderPosition <= b[0]) return RankAge.Sovereign;
        if (ladderPosition <= b[1]) return RankAge.Imperial;
        if (ladderPosition <= b[2]) return RankAge.Industrial;
        if (ladderPosition <= b[3]) return RankAge.Fortress;
        return RankAge.Colonial;
    }

    /// <summary>
    /// The age for a position that may be unknown. Null in, null out: a backend that predates
    /// <c>ladder_rank</c> says nothing, and nothing must never be drawn as Discovery — that
    /// would tell a player on the table that they are not on it.
    /// </summary>
    public static RankAge? ForOptional(int? ladderPosition, int? ladderSize = null)
        => ladderPosition is { } p ? For(p, ladderSize) : null;

    /// <summary>The <c>Strings</c> key naming an age.</summary>
    public static string NameKey(RankAge age) => age switch
    {
        RankAge.Sovereign => "MpAgeSovereign",
        RankAge.Imperial => "MpAgeImperial",
        RankAge.Industrial => "MpAgeIndustrial",
        RankAge.Fortress => "MpAgeFortress",
        RankAge.Colonial => "MpAgeColonial",
        _ => "MpAgeDiscovery",
    };
}
