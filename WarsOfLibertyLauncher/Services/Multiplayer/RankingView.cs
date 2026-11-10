using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// The two colours an age paints with on the Clasificación page (design 93): the strong one for
/// fills and rims, the light one for text and highlights. The same hues the rank badge's plate uses
/// (Styles/Badges.xaml), kept here as numbers because the page mixes them at several opacities.
/// </summary>
public static class RankAgeColors
{
    public static (string Main, string Light) Of(RankAge age) => age switch
    {
        RankAge.Sovereign => ("#FF2F45", "#FF9AA6"),
        RankAge.Imperial => ("#A742FF", "#FFD76B"),
        RankAge.Industrial => ("#E06A12", "#FFC247"),
        RankAge.Fortress => ("#1E9FD4", "#A8ECFF"),
        RankAge.Colonial => ("#A85C1E", "#F0A75A"),
        _ => ("#5D6F8A", "#C3D2E5"),
    };
}

/// <summary>One group of the table: an age, and the places of the whole ladder that wear it.</summary>
/// <param name="FirstPlace">The first place of this age on the ladder (not only on the page).</param>
/// <param name="LastPlace">The last one.</param>
public readonly record struct RankingGroup(RankAge Age, int FirstPlace, int LastPlace, IReadOnlyList<LeaderboardRow> Rows);

/// <summary>
/// How the Clasificación page splits the ranked list (design 93): the first three on a podium, the
/// rest in the table under a heading per age. Pure: places and ages are the server's and
/// <see cref="RankAges"/>', nothing is renumbered or re-sorted here.
/// </summary>
public static class RankingGroups
{
    /// <summary>How many players stand on the podium.</summary>
    public const int PodiumPlaces = 3;

    /// <summary>The players of the podium, in the server's order (first place first).</summary>
    public static IReadOnlyList<LeaderboardRow> Podium(IReadOnlyList<LeaderboardRow> ranked)
        => ranked.Take(PodiumPlaces).ToList();

    /// <summary>
    /// The podium in the order it is DRAWN: second, first, third — first place in the middle. With
    /// fewer than three there is no middle to keep, so they stand in the server's order.
    /// </summary>
    public static IReadOnlyList<LeaderboardRow> PodiumDrawOrder(IReadOnlyList<LeaderboardRow> ranked)
    {
        var top = Podium(ranked);
        return top.Count == PodiumPlaces ? new[] { top[1], top[0], top[2] } : top;
    }

    /// <summary>
    /// The table's groups, from the fourth player on, one per age in the order the ladder has them.
    /// A group's range is the age's places on the WHOLE ladder — so "INDUSTRIAL · places 3–4" stays
    /// true when the third is on the podium, and when the server's page ends before the age does.
    /// </summary>
    public static IReadOnlyList<RankingGroup> Table(IReadOnlyList<LeaderboardRow> ranked, int? ladderSize)
    {
        var groups = new List<RankingGroup>();
        var size = ladderSize is int n && n > 0 ? n : (int?)null;
        foreach (var byAge in ranked.Skip(PodiumPlaces).GroupBy(r => RankAges.For(r.Rank, size)))
        {
            var rows = byAge.ToList();
            var (first, last) = PlacesOf(byAge.Key, size, ranked);
            groups.Add(new RankingGroup(byAge.Key, first, last, rows));
        }
        return groups;
    }

    /// <summary>The places an age covers: from the ladder's cuts when its size is known, otherwise
    /// from the rows on the page.</summary>
    internal static (int First, int Last) PlacesOf(RankAge age, int? ladderSize, IReadOnlyList<LeaderboardRow> ranked)
    {
        if (ladderSize is int n && n > 0)
        {
            var b = RankAges.BoundsFor(n);
            var (first, last) = age switch
            {
                RankAge.Sovereign => (1, b[0]),
                RankAge.Imperial => (b[0] + 1, b[1]),
                RankAge.Industrial => (b[1] + 1, b[2]),
                RankAge.Fortress => (b[2] + 1, b[3]),
                _ => (b[3] + 1, n),
            };
            return (first, Math.Min(last, n));
        }
        var places = ranked.Where(r => RankAges.For(r.Rank, null) == age).Select(r => r.Rank).ToList();
        return places.Count == 0 ? (0, 0) : (places.Min(), places.Max());
    }
}

/// <summary>One line of "Your position": the player above, the viewer, or the player below.</summary>
/// <param name="Gap">ELO from the viewer: positive above him, negative below, 0 on his own line.</param>
public readonly record struct StandingNeighbour(int Place, string Name, string? AvatarUrl, int Elo, int Gap, bool Self);

/// <summary>What "Your position" shows (design 93).</summary>
public sealed record YourStanding
{
    /// <summary>On the table: the place and everything around it. False while still being placed.</summary>
    public bool Ranked { get; init; }
    public int Place { get; init; }
    public int Of { get; init; }
    public RankAge Age { get; init; }

    /// <summary>The viewer's ELO; null when being placed and the launcher does not know it.</summary>
    public int? Elo { get; init; }
    public int Streak { get; init; }
    public IReadOnlyList<StandingNeighbour> Neighbours { get; init; } = Array.Empty<StandingNeighbour>();

    /// <summary>ELO needed to take the place above; 0 when first or when the neighbour is unknown.</summary>
    public int ToNextPlace { get; init; }
    public int NextPlace { get; init; }

    /// <summary>The age that place wears, when it is a higher one than the viewer's.</summary>
    public RankAge? NextAge { get; init; }
    public bool First => Ranked && Place == 1;

    public int PlacementPlayed { get; init; }
    public int PlacementRequired { get; init; }
    public int PlacementLeft => Math.Max(0, PlacementRequired - PlacementPlayed);
}

/// <summary>
/// "Your position", decided in one place (design 93). Everything comes from data the page already
/// has: the ranked list for the neighbours, the placement list and the viewer's own standing for the
/// rest. No figure is invented: a viewer the server's page does not reach gets his place and no
/// neighbours, and one being placed gets his progress.
/// </summary>
public static class YourStandingView
{
    /// <param name="ranked">The ranked list on the page, in the server's order.</param>
    /// <param name="placing">The players still being placed.</param>
    /// <param name="own">The viewer's own standing on this ladder, when the launcher has it.</param>
    /// <returns>Null when the viewer is neither on the table nor being placed.</returns>
    public static YourStanding? For(
        IReadOnlyList<LeaderboardRow> ranked, IReadOnlyList<PlacementRow> placing,
        string? viewerId, int? ladderSize, LadderStanding? own)
    {
        if (string.IsNullOrEmpty(viewerId)) return null;
        var size = ladderSize is int n && n > 0 ? n : ranked.Count;

        var index = -1;
        for (var i = 0; i < ranked.Count; i++)
            if (string.Equals(ranked[i].UserId, viewerId, StringComparison.Ordinal)) { index = i; break; }

        if (index >= 0)
        {
            var me = ranked[index];
            var myElo = Whole(me.Rating);
            var neighbours = new List<StandingNeighbour>();
            for (var j = index - 1; j <= index + 1; j++)
            {
                if (j < 0 || j >= ranked.Count) continue;
                var r = ranked[j];
                var elo = Whole(r.Rating);
                neighbours.Add(new StandingNeighbour(r.Rank, NameOf(r), r.AvatarUrl, elo, j == index ? 0 : elo - myElo, j == index));
            }
            var age = RankAges.For(me.Rank, size);
            var above = index > 0 ? ranked[index - 1] : null;
            var nextAge = above != null ? RankAges.For(above.Rank, size) : age;
            return new YourStanding
            {
                Ranked = true, Place = me.Rank, Of = size, Age = age, Elo = myElo, Streak = me.Streak,
                Neighbours = neighbours,
                // One more than the gap: level on ELO is not ahead.
                ToNextPlace = above != null ? Math.Max(1, Whole(above.Rating) - myElo + 1) : 0,
                NextPlace = above?.Rank ?? 0,
                NextAge = above != null && nextAge > age ? nextAge : null,
            };
        }

        var row = placing.FirstOrDefault(p => string.Equals(p.UserId, viewerId, StringComparison.Ordinal));
        if (row != null)
        {
            var rating = row.Rating ?? own?.Rating;
            return new YourStanding
            {
                Ranked = false, Age = RankAge.Discovery, Streak = row.Streak,
                Elo = rating is double d ? Whole(d) : null,
                PlacementPlayed = row.PlacementPlayed, PlacementRequired = row.PlacementRequired,
            };
        }

        // On the table but past the page the server sent: the place is his own standing's, and
        // there are no neighbours to name.
        if (own is { LadderRank: int place } && place > 0)
        {
            var total = own.LadderSize is int s && s > 0 ? s : size;
            return new YourStanding
            {
                Ranked = true, Place = place, Of = Math.Max(total, place), Age = RankAges.For(place, total),
                Elo = Whole(own.Rating), Streak = own.StreakCurrent,
            };
        }
        if (own != null && PlacementView.InPlacement(own.PlacementPlayed, own.PlacementRequired))
        {
            return new YourStanding
            {
                Ranked = false, Age = RankAge.Discovery, Streak = own.StreakCurrent, Elo = Whole(own.Rating),
                PlacementPlayed = own.PlacementPlayed, PlacementRequired = own.PlacementRequired,
            };
        }
        return null;
    }

    private static int Whole(double rating) => (int)Math.Round(rating);

    private static string NameOf(LeaderboardRow r)
        => string.IsNullOrEmpty(r.DisplayName) ? r.DiscordUsername : r.DisplayName;
}

/// <summary>
/// The podium's sizes that follow the page's width (design 93): CSS <c>clamp()</c> values against a
/// hundredth of the page, like <see cref="RankingTableLayout.Fluid"/>. Rounded, so a resize rebuilds
/// only when something visible changes.
/// </summary>
public readonly record struct RankingPodiumFluid(
    double Pedestal, double Number, double Padding, double Gap, double NameSize, double EloSize, double Avatar)
{
    /// <summary>The pedestal of a place: first is the tallest, then second, then third.</summary>
    public double PedestalOf(int place) => Math.Round(Pedestal * (place <= 1 ? 1 : place == 2 ? .86 : .76));

    /// <summary>The place number's size: first is the largest.</summary>
    public double NumberOf(int place) => Math.Round(Number * (place <= 1 ? 1 : .86));

    public static RankingPodiumFluid For(double pageWidth, double textFactor = 1.0)
    {
        var q = pageWidth > 0 && !double.IsNaN(pageWidth) && !double.IsInfinity(pageWidth) ? pageWidth / 100.0 : 0;
        var f = textFactor > 0 ? textFactor : 1.0;
        static double Half(double v) => Math.Round(v * 2, MidpointRounding.AwayFromZero) / 2;
        return new RankingPodiumFluid(
            Pedestal: Math.Round(Math.Clamp(q * 2.2, 30, 52)),
            Number: Math.Round(Math.Clamp(q * 1.15, 17, 28) * f),
            Padding: Math.Round(Math.Clamp(q * .7, 10, 18)),
            Gap: Math.Round(Math.Clamp(q * .6, 10, 16)),
            NameSize: Half(Math.Clamp(q * .7, 13.5, 18) * f),
            EloSize: Math.Round(Math.Clamp(q * 1.0, 17, 28) * f),
            Avatar: Math.Round(Math.Clamp(q * 2.6, 36, 64)));
    }
}
