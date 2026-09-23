using System.Linq;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="RankAges"/>: which age a ladder position earns. The badge is drawn on three
/// screens from this one rule, so a mistake here is a mistake everywhere a player's name appears.
/// </summary>
public class RankAgeTests
{
    [Theory]
    [InlineData(1, RankAge.Sovereign)]
    [InlineData(2, RankAge.Imperial)]
    [InlineData(3, RankAge.Industrial)]
    [InlineData(4, RankAge.Industrial)]
    [InlineData(5, RankAge.Fortress)]
    [InlineData(6, RankAge.Fortress)]
    [InlineData(7, RankAge.Colonial)]
    [InlineData(50, RankAge.Colonial)]
    public void EveryBoundaryLandsOnTheRightAge(int position, RankAge expected)
        => Assert.Equal(expected, RankAges.For(position));

    /// <summary>
    /// The server sends 0 for a player below the entry bar. That is Discovery, the one age that
    /// never appears in the Ranking table.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void OffTheLadderIsDiscovery(int position)
        => Assert.Equal(RankAge.Discovery, RankAges.For(position));

    /// <summary>
    /// A backend that predates <c>ladder_rank</c> sends nothing. Drawing that as Discovery would
    /// tell a player who IS on the table that they are not, so unknown stays unknown.
    /// </summary>
    [Fact]
    public void AnUnknownPositionIsNotDiscovery()
    {
        Assert.Null(RankAges.ForOptional(null));
        Assert.Equal(RankAge.Discovery, RankAges.ForOptional(0));
        Assert.Equal(RankAge.Sovereign, RankAges.ForOptional(1));
    }

    /// <summary>
    /// The live table the badges were designed against, in the order the server returned it.
    /// Note 1720 in fourth place: the table is ordered by <c>rating − 2·rd</c>, not by the
    /// printed rating.
    /// </summary>
    private static readonly (string Name, double Rating, int Position)[] AsTheServerSentThem =
    {
        ("Geaf_Argento", 1571, 1),
        ("Aluclown",     1519, 2),
        ("NathanR06",    1643, 3),
        ("AleReis",      1720, 4),
        ("Gommiustan",   1626, 5),
        ("alexari2040",  1662, 6),
        ("UnstoppableStreletsy", 1403, 7),
    };

    /// <summary>
    /// THE ONE THAT MATTERS. Going down the table, the age never rises — so the highest printed
    /// rating on the page wears the badge of its PLACE. If the age were read off the printed
    /// number, AleReis (1720, fourth) would outrank the three players above him and the badge
    /// would contradict the table in every row.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheAgeNeverRisesGoingDownTheTable()
    {
        var ages = AsTheServerSentThem.Select(p => RankAges.For(p.Position)).ToList();
        for (var i = 1; i < ages.Count; i++)
            Assert.True(ages[i] <= ages[i - 1],
                $"{AsTheServerSentThem[i].Name} ({ages[i]}) outranks {AsTheServerSentThem[i - 1].Name} ({ages[i - 1]}) above it.");

        var loudest = AsTheServerSentThem.OrderByDescending(p => p.Rating).First();
        Assert.Equal("AleReis", loudest.Name);
        Assert.Equal(RankAge.Industrial, RankAges.For(loudest.Position));
        Assert.Equal(RankAge.Sovereign, RankAges.For(AsTheServerSentThem[0].Position));
    }

    /// <summary>With the table this small, every age still has somebody in it.</summary>
    [Fact]
    public void SevenPlayersPopulateEveryAgeOnTheLadder()
    {
        var ages = AsTheServerSentThem.Select(p => RankAges.For(p.Position)).Distinct().ToList();
        foreach (var age in new[] { RankAge.Sovereign, RankAge.Imperial, RankAge.Industrial, RankAge.Fortress, RankAge.Colonial })
            Assert.Contains(age, ages);
    }

    /// <summary>
    /// THE SPLIT THE MAINTAINER CHOSE. With 18 players on the ladder: 1-2 Sovereign, 3-5
    /// Imperial, 6-9 Industrial, 10-13 Fortress, 14-18 Colonial. The fixed positions used to
    /// give exactly one red badge however many played.
    /// </summary>
    [Fact]
    public void EighteenPlayersSplitByShareOfTheTable()
    {
        Assert.Equal(new[] { 2, 5, 9, 13 }, RankAges.Bounds(18));
        var ages = Enumerable.Range(1, 18).Select(p => RankAges.For(p, 18)).ToList();
        Assert.Equal(2, ages.Count(a => a == RankAge.Sovereign));
        Assert.Equal(3, ages.Count(a => a == RankAge.Imperial));
        Assert.Equal(4, ages.Count(a => a == RankAge.Industrial));
        Assert.Equal(4, ages.Count(a => a == RankAge.Fortress));
        Assert.Equal(5, ages.Count(a => a == RankAge.Colonial));
    }

    /// <summary>A small table still gives every age somebody before the table runs out.</summary>
    [Theory]
    [InlineData(7, new[] { 1, 2, 4, 5 })]
    [InlineData(3, new[] { 1, 2, 3, 4 })]
    [InlineData(1, new[] { 1, 2, 3, 4 })]
    [InlineData(40, new[] { 4, 10, 18, 28 })]
    public void EveryCutIsAtLeastOnePlaceWide(int size, int[] expected)
        => Assert.Equal(expected, RankAges.Bounds(size));

    /// <summary>
    /// The bands grow with the community on their own: more players, more of each age, and the
    /// age still never rises going down the table.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(18)]
    [InlineData(100)]
    public void GoingDownAnySizeOfTableTheAgeNeverRises(int size)
    {
        var ages = Enumerable.Range(1, size).Select(p => RankAges.For(p, size)).ToList();
        Assert.Equal(RankAge.Sovereign, ages[0]);
        for (var i = 1; i < ages.Count; i++) Assert.True(ages[i] <= ages[i - 1]);
    }

    /// <summary>
    /// An unknown size — an older backend, or community stats not loaded yet — falls back to
    /// the fixed positions every launcher drew before. A position past a stale size is Colonial,
    /// and off the ladder is still Discovery.
    /// </summary>
    [Fact]
    public void AnUnknownSizeFallsBackAndEdgesHold()
    {
        foreach (var p in Enumerable.Range(1, 10))
        {
            Assert.Equal(RankAges.For(p), RankAges.For(p, null));
            Assert.Equal(RankAges.For(p), RankAges.For(p, 0));
        }
        Assert.Equal(RankAge.Colonial, RankAges.For(25, 18));
        Assert.Equal(RankAge.Discovery, RankAges.For(0, 18));
        Assert.Null(RankAges.ForOptional(null, 18));
    }

    /// <summary>Every age has a name, and the table carries both languages for it.</summary>
    [Fact]
    public void EveryAgeHasALocalizedName()
    {
        foreach (var age in System.Enum.GetValues<RankAge>())
        {
            var key = RankAges.NameKey(age);
            Assert.StartsWith("MpAge", key);
            // GetIn, never SetLanguage: the language is process-wide and tests run in parallel.
            var es = Strings.GetIn(Strings.LangEs, key);
            var en = Strings.GetIn(Strings.LangEn, key);
            Assert.NotEqual(key, es);
            Assert.NotEqual(key, en);
        }
    }
}
