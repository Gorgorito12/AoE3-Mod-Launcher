using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>The room's "Automatic entry" card. A switch is offered only where it can do something.</summary>
public class RoomLanEntryViewTests
{
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheSwitchIsOfferedOnlyWhenTheGameCanEnterByItself()
    {
        Assert.True(RoomLanEntryView.For(RoomLanAvailability.Available, true, true).ShowSwitch);
        Assert.True(RoomLanEntryView.For(RoomLanAvailability.Available, false, true).ShowSwitch);
        Assert.False(RoomLanEntryView.For(RoomLanAvailability.NotSetUp, true, true).ShowSwitch);
        Assert.False(RoomLanEntryView.For(RoomLanAvailability.CannotSetUp, true, true).ShowSwitch);
        Assert.Equal(RoomLanCardKind.Hidden, RoomLanEntryView.For(RoomLanAvailability.Unknown, true, true).Kind);
    }

    [Fact]
    public void OnAndOffSayDifferentThings()
    {
        var on = RoomLanEntryView.For(RoomLanAvailability.Available, true, true);
        var off = RoomLanEntryView.For(RoomLanAvailability.Available, false, true);
        Assert.Equal(RoomLanCardKind.On, on.Kind);
        Assert.Equal(RoomLanCardKind.Off, off.Kind);
        Assert.NotEqual(on.BodyKey, off.BodyKey);
        Assert.Null(on.ActionKey);
    }

    [Fact]
    public void TheSwitchIsFrozenOnceTheMatchIsUnderWay()
    {
        // The arguments are built when the game launches; flipping it later would change nothing.
        Assert.True(RoomLanEntryView.For(RoomLanAvailability.Available, true, inLobbyPhase: true).SwitchEnabled);
        Assert.False(RoomLanEntryView.For(RoomLanAvailability.Available, true, inLobbyPhase: false).SwitchEnabled);
        Assert.Null(RoomLanEntryView.For(RoomLanAvailability.NotSetUp, true, inLobbyPhase: false).ActionKey);
    }

    [Fact]
    public void WhatCannotEnterOffersTheWayToFixItOrTheReason()
    {
        Assert.Equal("LanCardSetUp", RoomLanEntryView.For(RoomLanAvailability.NotSetUp, true, true).ActionKey);
        Assert.Equal("LanCardWhy", RoomLanEntryView.For(RoomLanAvailability.CannotSetUp, true, true).ActionKey);
    }

    [Fact]
    public void TheRoomIsToldTheTruthAboutThisPlayer()
    {
        Assert.True(RoomLanEntryView.EntersByItself(RoomLanAvailability.Available, true));
        Assert.False(RoomLanEntryView.EntersByItself(RoomLanAvailability.Available, false));
        Assert.False(RoomLanEntryView.EntersByItself(RoomLanAvailability.NotSetUp, true));
        Assert.Null(RoomLanEntryView.EntersByItself(RoomLanAvailability.Unknown, true));
    }

    [Fact]
    public void EveryKeyTheCardUsesExists()
    {
        foreach (var a in new[] { RoomLanAvailability.Available, RoomLanAvailability.NotSetUp, RoomLanAvailability.CannotSetUp })
        foreach (var on in new[] { true, false })
        {
            var card = RoomLanEntryView.For(a, on, true);
            foreach (var key in new[] { card.BodyKey, card.ActionKey })
                if (key != null) Assert.NotEqual(key, Strings.Get(key));
        }
        foreach (var key in new[] { "LanCardTitle", "LanCardSwitchOffTip", "LanCardSwitchOnTip" })
            Assert.NotEqual(key, Strings.Get(key));
    }
}
