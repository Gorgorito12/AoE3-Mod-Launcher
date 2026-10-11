using WarsOfLibertyLauncher.Models.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The summary the join flow builds when a room is reached by id and is not in the list.
///
/// <para>It used to be built inline with seven fields and left out the competitive flag, the
/// spectator seats and the bracket slot, so a player who reached a competitive or tournament
/// room that way was treated as being in a casual one: no Record Game check, a 0.5 report, and a
/// rename pencil in a room whose name the server composes.</para>
/// </summary>
public class LobbySummaryFromDetailTests
{
    [Fact]
    public void THE_ONE_THAT_MATTERS_EveryFlagOfTheRoomCrossesOver()
    {
        var detail = new LobbyDetail
        {
            Id = "abc123", Title = "Copa · Final · Ana · Luis", ModId = "wol",
            MaxPlayers = 6, SpectatorSlots = 2, CurrentPlayers = 3, IsPrivate = true,
            Competitive = true, TournamentMatchId = "tm1", Status = "open",
        };

        var s = LobbySummary.FromDetail(detail);

        Assert.Equal("abc123", s.Id);
        Assert.Equal("Copa · Final · Ana · Luis", s.Title);
        Assert.Equal("wol", s.ModId);
        Assert.Equal(6, s.MaxPlayers);
        Assert.Equal(2, s.SpectatorSlots);
        Assert.Equal(3, s.CurrentPlayers);
        Assert.True(s.IsPrivate);
        Assert.True(s.Competitive);
        Assert.Equal("tm1", s.TournamentMatchId);
        Assert.Equal("open", s.Status);
    }

    [Fact]
    public void AnOrdinaryRoomStaysOrdinary()
    {
        var s = LobbySummary.FromDetail(new LobbyDetail { Id = "x", Title = "Sala", Status = "open" });
        Assert.False(s.Competitive);
        Assert.Null(s.TournamentMatchId);
        Assert.Equal(0, s.SpectatorSlots);
    }
}
