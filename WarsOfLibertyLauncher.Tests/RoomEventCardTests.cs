using System;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>The chat's "X opened a room" card follows its room instead of offering Join for ever.</summary>
public class RoomEventCardTests
{
    private static readonly DateTime Announced = new(2026, 11, 18, 20, 0, 0, DateTimeKind.Utc);

    private static RoomEventSnapshot Snap(bool competitive = false, int max = 2) =>
        new("abc123", "WoL · Casual", "wol", "u-host", "Pepedograu", max, competitive, Announced);

    [Fact]
    public void THE_ONE_THAT_MATTERS_ARoomMissingFromALaterListIsClosed()
    {
        Assert.Equal(RoomEventStatus.Closed, RoomEventCard.StatusOf(false, Announced.AddSeconds(2), Announced));
    }

    [Fact]
    public void AListOlderThanTheAnnouncementProvesNothing()
    {
        // The list was asked before the room existed: missing from it is not closed.
        Assert.Equal(RoomEventStatus.Announced, RoomEventCard.StatusOf(false, Announced.AddSeconds(-1), Announced));
        Assert.Equal(RoomEventStatus.Announced, RoomEventCard.StatusOf(false, null, Announced));
    }

    [Fact]
    public void InTheListItIsLiveWhateverTheTimes()
    {
        Assert.Equal(RoomEventStatus.Live, RoomEventCard.StatusOf(true, Announced.AddSeconds(-5), Announced));
        Assert.Equal(RoomEventStatus.Live, RoomEventCard.StatusOf(true, null, Announced));
    }

    [Fact]
    public void TheAnnouncementAloneIsAnOpenRoomWithItsHostInside()
    {
        var room = RoomEventCard.FromAnnouncement(Snap(competitive: true));
        Assert.Equal("abc123", room.Id);
        Assert.Equal(1, room.CurrentPlayers);
        Assert.Equal("open", room.Status);
        Assert.True(room.Competitive);
        Assert.False(room.IsPrivate);
        Assert.Equal("u-host", room.Host.Id);

        var seats = RoomFormats.SeatsOf(room.MaxPlayers, room.SpectatorSlots, room.CurrentPlayers, room.SpectatorsPresent);
        Assert.False(seats.PlayingFull);
    }

    [Fact]
    public void AFullRoomIsFullAndItsCardRepaints()
    {
        var open = RoomEventCard.FromAnnouncement(Snap());
        var full = RoomEventCard.FromAnnouncement(Snap());
        full.CurrentPlayers = 2;
        var seats = RoomFormats.SeatsOf(full.MaxPlayers, full.SpectatorSlots, full.CurrentPlayers, full.SpectatorsPresent);
        Assert.True(seats.PlayingFull);
        Assert.Equal(RoomOffer.Full, RoomFormats.OfferFor(seats, false));
        Assert.Equal(RoomAction.Full, RoomActionRule.Decide(new RoomRowFacts(false, false, RoomOffer.Full, true, false)));
        Assert.NotEqual(
            RoomEventCard.KeyOf(RoomEventStatus.Live, open, RoomAction.Join),
            RoomEventCard.KeyOf(RoomEventStatus.Live, full, RoomAction.Full));
    }

    [Fact]
    public void AnUnchangedRoomKeepsItsKey()
    {
        var a = RoomEventCard.FromAnnouncement(Snap());
        var b = RoomEventCard.FromAnnouncement(Snap());
        Assert.Equal(RoomEventCard.KeyOf(RoomEventStatus.Live, a, RoomAction.Join),
            RoomEventCard.KeyOf(RoomEventStatus.Live, b, RoomAction.Join));
    }

    [Fact]
    public void TheAnnouncementCarriesTheRulesAndTheKeyMovesWithThem()
    {
        var standard = RoomEventCard.FromAnnouncement(Snap());
        var koth = RoomEventCard.FromAnnouncement(Snap() with { GameRules = LanAutoStartArgs.KingOfTheHill });
        Assert.Null(standard.GameRules);
        Assert.Equal(LanAutoStartArgs.KingOfTheHill, koth.GameRules);
        Assert.NotEqual(RoomEventCard.KeyOf(RoomEventStatus.Announced, standard, RoomAction.Join),
            RoomEventCard.KeyOf(RoomEventStatus.Announced, koth, RoomAction.Join));
    }
}
