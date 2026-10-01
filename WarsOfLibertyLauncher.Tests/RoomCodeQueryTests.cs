using System.Collections.Generic;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The rooms search box doubles as the room-code field (design handoff turn 36). A code is
/// the backend's <c>shortId(8)</c>: eight Crockford base32 characters, looked up
/// case-SENSITIVELY — so what is accepted has to be exact, and what is sent uppercased.
///
/// <para>The REJECTIONS are the point: an ordinary search ("kaiser", "1v1", a map name) must
/// never grow a "Join room" row, and a near-miss must not be quietly "fixed" into a code that
/// names somebody else's room.</para>
/// </summary>
public class RoomCodeQueryTests
{
    [Theory]
    [InlineData("SJMD9J6W", "SJMD9J6W")]
    [InlineData("sjmd9j6w", "SJMD9J6W")]      // typed in lowercase — the server would miss it as typed
    [InlineData("  SJMD9J6W \t", "SJMD9J6W")]  // pasted with whitespace around it
    [InlineData("3K7N9P2X", "3K7N9P2X")]
    [InlineData("ZZZZZZZZ", "ZZZZZZZZ")]      // no digit at all: about one real id in twenty
    [InlineData("wol-launcher://join/NHHXP1NR", "NHHXP1NR")]   // the whole Discord link
    public void ACodeIsRecognisedAndUppercased(string typed, string code)
        => Assert.Equal(code, RoomCodeQuery.TryParse(typed));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("SJMD9J6")]       // seven
    [InlineData("SJMD9J6WX")]     // nine
    [InlineData("SJMD 9J6W")]     // a space inside
    [InlineData("SJMD9J6I")]      // I is not in the alphabet — never remapped to 1
    [InlineData("SJMD9J6L")]      // L
    [InlineData("SJMD9J6O")]      // O — never remapped to 0
    [InlineData("SJMD9J6U")]      // U
    [InlineData("kaiser")]
    [InlineData("1v1")]
    [InlineData("Treaty 40 min")]
    [InlineData("SJMD-9J6W")]
    [InlineData("wol-launcher://join/NHHXP1NRX")]   // a link whose id is not a code
    [InlineData("https://wol-lobby.duckdns.org/j/NHHXP1NR")]
    public void AnythingElseIsAnOrdinarySearch(string? typed)
        => Assert.Null(RoomCodeQuery.TryParse(typed));

    [Fact]
    public void APastedCodeFindsItsOwnRoomInTheList()
    {
        var rooms = new List<LobbySummary>
        {
            new() { Id = "SJMD9J6W", Title = "Treaty", ModId = "wol", Host = new LobbyHost { DisplayName = "a" } },
            new() { Id = "3K7N9P2X", Title = "Ranked", ModId = "wol", Host = new LobbyHost { DisplayName = "b" } },
        };

        var hit = Assert.Single(RoomSearchFilter.Apply(rooms, "sjmd9j6w"));
        Assert.Equal("SJMD9J6W", hit.Id);
    }

    [Fact]
    public void APartOfAnIdIsNotAMatch()
    {
        // Ids are random: a substring match would put unrelated rooms under every short query.
        var rooms = new List<LobbySummary>
        {
            new() { Id = "SJMD9J6W", Title = "Treaty", ModId = "wol", Host = new LobbyHost { DisplayName = "a" } },
        };
        Assert.Empty(RoomSearchFilter.Apply(rooms, "9J6"));
    }
}
