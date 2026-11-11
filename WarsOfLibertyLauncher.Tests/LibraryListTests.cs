using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>Which mods the Library lists, and where. Design 68a.</summary>
public class LibraryListTests
{
    private static ModProfile Mod(string id, string? name = null, string author = "")
        => new() { Id = id, DisplayName = name ?? id, Author = author };

    private static readonly DateTime Monday = new(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc);

    private static IReadOnlyList<LibraryGroup> Build(
        IEnumerable<ModProfile> mods,
        Dictionary<string, ModRowState> states,
        string[]? favorites = null,
        Dictionary<string, DateTime>? played = null,
        LibraryFilter filter = LibraryFilter.All,
        string? query = null)
        => LibraryList.Build(
            mods,
            p => states.TryGetValue(p.Id, out var s) ? s : new ModRowState(),
            id => favorites?.Contains(id) == true,
            id => played != null && played.TryGetValue(id, out var t) ? t : null,
            filter,
            query);

    [Fact]
    public void AFavouriteLeadsAndAppearsOnce()
    {
        var states = new Dictionary<string, ModRowState>
        {
            ["a"] = new() { Status = ModRowStatus.Installed },
            ["b"] = new() { Status = ModRowStatus.Installed },
        };
        var groups = Build(new[] { Mod("a"), Mod("b"), Mod("c") }, states, favorites: new[] { "b" });

        Assert.Equal(new[] { LibraryGroupKind.Favorites, LibraryGroupKind.Mine, LibraryGroupKind.Catalog }, groups.Select(g => g.Kind));
        Assert.Equal(new[] { "b" }, groups[0].Mods.Select(m => m.Id));
        Assert.Equal(1, groups.SelectMany(g => g.Mods).Count(m => m.Id == "b"));
    }

    [Fact]
    public void TheMostRecentlyPlayedComesFirst()
    {
        var states = new Dictionary<string, ModRowState>
        {
            ["old"] = new() { Status = ModRowStatus.Installed },
            ["new"] = new() { Status = ModRowStatus.Installed },
            ["never"] = new() { Status = ModRowStatus.Installed },
        };
        var groups = Build(new[] { Mod("never"), Mod("old"), Mod("new") }, states,
            played: new() { ["old"] = Monday, ["new"] = Monday.AddDays(3) });

        Assert.Equal(new[] { "new", "old", "never" }, groups.Single().Mods.Select(m => m.Id));
    }

    [Fact]
    public void AnAddedModThatIsNotInstalledIsStillMine()
    {
        var states = new Dictionary<string, ModRowState> { ["a"] = new() { IsInUserCollection = true } };
        var groups = Build(new[] { Mod("a") }, states);
        Assert.Equal(LibraryGroupKind.Mine, groups.Single().Kind);
    }

    [Fact]
    public void AnEmptyGroupIsNotDrawn()
    {
        var groups = Build(new[] { Mod("a") }, new Dictionary<string, ModRowState>());
        Assert.Equal(LibraryGroupKind.Catalog, groups.Single().Kind);
    }

    [Theory]
    [InlineData(LibraryFilter.All, 3)]
    [InlineData(LibraryFilter.Installed, 2)]
    [InlineData(LibraryFilter.NotInstalled, 1)]
    [InlineData(LibraryFilter.Updates, 1)]
    public void TheFiltersCount(LibraryFilter filter, int expected)
    {
        var states = new Dictionary<string, ModRowState>
        {
            ["a"] = new() { Status = ModRowStatus.Installed },
            ["b"] = new() { Status = ModRowStatus.UpdateAvailable },
        };
        var groups = Build(new[] { Mod("a"), Mod("b"), Mod("c") }, states, filter: filter);
        Assert.Equal(expected, groups.SelectMany(g => g.Mods).Count());
    }

    [Theory]
    [InlineData("liberty", 1)]
    [InlineData("TEAM", 1)]
    [InlineData("  ", 2)]
    [InlineData("nothing", 0)]
    public void TheSearchReadsNameAndAuthor(string query, int expected)
    {
        var mods = new[] { Mod("a", "Wars of Liberty", "The Team"), Mod("b", "Hundred Days") };
        Assert.Equal(expected, Build(mods, new Dictionary<string, ModRowState>(), query: query).SelectMany(g => g.Mods).Count());
    }

    [Theory]
    [InlineData(1366, 320, 56, 36.882)]
    [InlineData(1920, 441.6, 59.52, 51.84)]
    [InlineData(2560, 460, 68, 64)]
    public void TheMetricsAreThePrototypesClamps(double width, double rail, double row, double title)
    {
        var metrics = LibraryMetrics.For(width);
        Assert.Equal(rail, metrics.RailWidth, 2);
        Assert.Equal(row, metrics.RowHeight, 2);
        Assert.Equal(title, metrics.TitleSize, 2);
    }

    /// <summary>
    /// The text-size setting scales the four type sizes and nothing that is geometry: the list,
    /// the gaps, the rows and the thumbnails keep the prototype's clamps at any setting.
    /// </summary>
    [Fact]
    public void TheTextSizeScalesTheTypeAndNothingElse()
    {
        var plain = LibraryMetrics.For(1920);
        var larger = LibraryMetrics.For(1920, 1.1);
        Assert.Equal(plain.TitleSize * 1.1, larger.TitleSize, 3);
        Assert.Equal(plain.DescriptionSize * 1.1, larger.DescriptionSize, 3);
        Assert.Equal(plain.AuthorSize * 1.1, larger.AuthorSize, 3);
        Assert.Equal(plain.RowNameSize * 1.1, larger.RowNameSize, 3);
        Assert.Equal(plain.RailWidth, larger.RailWidth);
        Assert.Equal(plain.RowHeight, larger.RowHeight);
        Assert.Equal(plain.ShotWidth, larger.ShotWidth);
        Assert.Equal(plain.ColumnGap, larger.ColumnGap);
        Assert.Equal(plain, LibraryMetrics.For(1920, 0));
    }

    [Theory]
    [InlineData("auto", 1366, false)]
    [InlineData("auto", 1920, true)]
    [InlineData(null, 1399, false)]
    [InlineData("open", 1000, true)]
    [InlineData("closed", 2560, false)]
    public void ThePlayersChoiceBeatsTheWidth(string? mode, double width, bool open)
        => Assert.Equal(open, LibraryMetrics.RailOpen(mode, width));
}
