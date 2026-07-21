using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins the MODS switcher ordering: favourites first, then most-recently-played,
/// then alphabetical. Extracted from MainWindow precisely so it can be tested here
/// (MainWindow's static brush fields throw on a thread with no STA).
/// </summary>
public class ModOrderingTests
{
    private static ModProfile Mod(string id, string name) =>
        new() { Id = id, DisplayName = name };

    /// <summary>Convenience: order with no favourites and a lookup table of play times.</summary>
    private static List<string> Order(
        IEnumerable<ModProfile> mods,
        Dictionary<string, DateTime>? played = null,
        HashSet<string>? favorites = null)
    {
        return ModOrdering.OrderForSwitcher(
                mods,
                id => favorites != null && favorites.Contains(id),
                id => played != null && played.TryGetValue(id, out var t) ? t : (DateTime?)null)
            .Select(p => p.Id)
            .ToList();
    }

    private static readonly DateTime Base = new(2026, 7, 19, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void MostRecentlyPlayedComesFirst()
    {
        var mods = new[] { Mod("aoe3-tad", "Age of Empires III"), Mod("wol", "Wars of Liberty"), Mod("imp", "Improvement Mod") };
        var played = new Dictionary<string, DateTime>
        {
            ["wol"] = Base.AddDays(-3),
            ["imp"] = Base.AddHours(-2),   // newest
        };

        Assert.Equal(new[] { "imp", "wol", "aoe3-tad" }, Order(mods, played));
    }

    [Fact]
    public void NeverPlayedSortsLast_AlphabeticallyAmongThemselves()
    {
        var mods = new[] { Mod("zeta", "Zeta"), Mod("alpha", "Alpha"), Mod("played", "Played") };
        var played = new Dictionary<string, DateTime> { ["played"] = Base.AddMinutes(-5) };

        Assert.Equal(new[] { "played", "alpha", "zeta" }, Order(mods, played));
    }

    [Fact]
    public void FavoriteBeatsMoreRecentlyPlayed()
    {
        // The star is an explicit user pin — it must outrank recency, or starring a
        // mod would appear to do nothing the moment you play something else.
        var mods = new[] { Mod("fav", "Favourite"), Mod("recent", "Recent") };
        var played = new Dictionary<string, DateTime>
        {
            ["fav"] = Base.AddDays(-10),
            ["recent"] = Base.AddMinutes(-1),
        };

        Assert.Equal(new[] { "fav", "recent" }, Order(mods, played, new HashSet<string> { "fav" }));
    }

    [Fact]
    public void WithinFavorites_RecencyStillDecides()
    {
        var mods = new[] { Mod("f1", "First"), Mod("f2", "Second") };
        var played = new Dictionary<string, DateTime>
        {
            ["f1"] = Base.AddDays(-1),
            ["f2"] = Base.AddMinutes(-30),
        };

        Assert.Equal(new[] { "f2", "f1" },
            Order(mods, played, new HashSet<string> { "f1", "f2" }));
    }

    [Fact]
    public void NothingPlayed_IsPlainAlphabetical()
    {
        // A fresh install must look exactly like the old alphabetical list until the
        // user actually plays something.
        var mods = new[] { Mod("c", "Charlie"), Mod("a", "Alpha"), Mod("b", "Bravo") };

        Assert.Equal(new[] { "a", "b", "c" }, Order(mods));
    }

    [Fact]
    public void SamePlayTime_FallsBackToName()
    {
        var mods = new[] { Mod("z", "Zulu"), Mod("m", "Mike") };
        var played = new Dictionary<string, DateTime> { ["z"] = Base, ["m"] = Base };

        Assert.Equal(new[] { "m", "z" }, Order(mods, played));
    }

    [Fact]
    public void EmptyAndNullInputsAreSafe()
    {
        Assert.Empty(Order(Array.Empty<ModProfile>()));
        Assert.Empty(ModOrdering.OrderForSwitcher(null!, _ => false, _ => null));
    }
}
