using System;
using System.Linq;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The rooms preview (design 86, <c>--demo-rooms</c>) is worth something only while its seven rooms
/// reach every action and every heading. A fixture like this does not break, it decays: pin what
/// it is FOR.
/// </summary>
public class RoomListDemoDataTests
{
    private static RoomAction ActionOf(Models.Multiplayer.LobbySummary r, bool observers)
    {
        var seats = RoomFormats.SeatsOf(r.MaxPlayers, r.SpectatorSlots, r.CurrentPlayers, r.SpectatorsPresent);
        var installed = RoomListDemoData.IsInstalled(r.ModId);
        return RoomActionRule.Decide(new RoomRowFacts(false, r.Status == "in_game",
            RoomFormats.OfferFor(seats, observers), installed, installed && RoomListDemoData.VersionDiffers(r.Id)));
    }

    [Fact]
    public void TheSampleReachesEveryActionAViewerCanSee()
    {
        var rooms = RoomListDemoData.Rooms(DateTime.UtcNow);
        var withObservers = rooms.Select(r => ActionOf(r, observers: true)).ToHashSet();
        foreach (var a in Enum.GetValues<RoomAction>().Where(a => a != RoomAction.OpenMine))
            Assert.Contains(a, withObservers);
        // Without developer mode the observers' room is simply full: Watch never appears.
        Assert.DoesNotContain(RoomAction.Watch, rooms.Select(r => ActionOf(r, observers: false)));
    }

    [Fact]
    public void TheSampleFillsAllThreeHeadings()
    {
        var groups = RoomListDemoData.Rooms(DateTime.UtcNow).Select(r => RoomActionRule.GroupOf(ActionOf(r, false))).ToHashSet();
        Assert.Equal(Enum.GetValues<RoomGroup>().ToHashSet(), groups);
    }

    [Theory]
    [InlineData("86a", "Rooms")]
    [InlineData("86b", "Empty")]
    [InlineData("loading", "Loading")]
    [InlineData("86d", "Error")]
    [InlineData("OFFLINE", "Offline")]
    [InlineData(null, "Rooms")]
    [InlineData("nonsense", "Rooms")]
    public void ScenarioNamesParse(string? name, string expected)
        => Assert.Equal(expected, RoomListDemoData.Parse(name).ToString());
}
