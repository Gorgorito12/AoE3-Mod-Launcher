using System.Collections.Generic;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins the rooms-browser search. The rule is a judgement call — which fields count,
/// and how forgiving the matching is — and a judgement call that only exists inside a
/// render method can't be pinned, which is why the filter is a pure service.
///
/// The accent case is the one that matters for this player base: most of it is
/// Spanish-speaking, and someone typing "rapida" for a room called "Partida rápida"
/// must find it. A search that silently returns nothing reads as "there are no rooms",
/// not as "your query didn't match".
/// </summary>
public class RoomSearchFilterTests
{
    private static LobbySummary Room(string title, string modId = "wol", string host = "someone")
        => new()
        {
            Id = title,
            Title = title,
            ModId = modId,
            Host = new LobbyHost { DisplayName = host, DiscordUsername = host },
        };

    private static readonly IReadOnlyList<LobbySummary> Rooms = new List<LobbySummary>
    {
        Room("Partida rápida", "wol", "gorgorito_12"),
        Room("Sin rush 10 min", "improvement-mod", "lennon047"),
        Room("Solo LatAm", "napoleonic-era", "92serd"),
    };

    // ---- The query is empty most of the time ----------------------------------

    [Fact]
    public void AnEmptyQueryReturnsTheListUntouched()
    {
        // Same instance, not a copy: this is the common case and it must not cost
        // anything or disturb the order the caller is about to sort.
        Assert.Same(Rooms, RoomSearchFilter.Apply(Rooms, ""));
        Assert.Same(Rooms, RoomSearchFilter.Apply(Rooms, null));
        Assert.Same(Rooms, RoomSearchFilter.Apply(Rooms, "   "));
    }

    // ---- Accents and case ------------------------------------------------------

    [Fact]
    public void AnUnaccentedQueryFindsAnAccentedRoom()
    {
        var hit = Assert.Single(RoomSearchFilter.Apply(Rooms, "rapida"));
        Assert.Equal("Partida rápida", hit.Title);
    }

    [Fact]
    public void AnAccentedQueryFindsItToo()
    {
        var hit = Assert.Single(RoomSearchFilter.Apply(Rooms, "rápida"));
        Assert.Equal("Partida rápida", hit.Title);
    }

    [Fact]
    public void MatchingIsCaseInsensitive()
    {
        Assert.Single(RoomSearchFilter.Apply(Rooms, "LATAM"));
        Assert.Single(RoomSearchFilter.Apply(Rooms, "latam"));
    }

    // ---- One box, three fields -------------------------------------------------

    [Fact]
    public void TheHostIsSearchable()
    {
        // Someone typing a player's name has no way to tell the box which column they
        // mean, so all three fields are searched at once.
        var hit = Assert.Single(RoomSearchFilter.Apply(Rooms, "lennon"));
        Assert.Equal("Sin rush 10 min", hit.Title);
    }

    [Fact]
    public void TheModIsSearchable()
    {
        var hit = Assert.Single(RoomSearchFilter.Apply(Rooms, "napoleonic"));
        Assert.Equal("Solo LatAm", hit.Title);
    }

    [Fact]
    public void MatchingIsSubstringNotPrefix()
    {
        // "rush" sits mid-title. Prefix-only matching would find nothing.
        Assert.Single(RoomSearchFilter.Apply(Rooms, "rush"));
    }

    // ---- Nothing matches -------------------------------------------------------

    [Fact]
    public void AQueryThatMatchesNothingReturnsEmpty_NotEverything()
    {
        // Falling back to the full list would be worse than showing none: the user
        // would think their search ran and these were the results.
        Assert.Empty(RoomSearchFilter.Apply(Rooms, "zzzz"));
    }

    [Fact]
    public void AnEmptyRoomListSurvivesAQuery()
    {
        Assert.Empty(RoomSearchFilter.Apply(new List<LobbySummary>(), "anything"));
    }

    [Fact]
    public void ARoomWithNoHostDoesNotThrow()
    {
        // LobbySummary.Host is nullable on the wire; a filter that crashed here would
        // take the whole rooms list down with it.
        var rooms = new List<LobbySummary> { new() { Id = "x", Title = "Sala", ModId = "wol" } };

        Assert.Single(RoomSearchFilter.Apply(rooms, "sala"));
        Assert.Empty(RoomSearchFilter.Apply(rooms, "nobody"));
    }
}
