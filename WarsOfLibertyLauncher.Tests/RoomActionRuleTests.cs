using System;
using System.Linq;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>Design 86: one decision gives a room row its button and its heading.</summary>
public class RoomActionRuleTests
{
    static RoomRowFacts Open(RoomOffer offer = RoomOffer.Join, bool installed = true, bool differs = false)
        => new(false, false, offer, installed, differs);

    [Fact]
    public void MyRoomWinsOverEverything()
    {
        var f = new RoomRowFacts(true, true, RoomOffer.Full, false, true);
        Assert.Equal(RoomAction.OpenMine, RoomActionRule.Decide(f));
    }

    [Fact]
    public void AStartedMatchIsSeenNotJoined_WhateverItsSeatsSay()
    {
        Assert.Equal(RoomAction.SeeMatch, RoomActionRule.Decide(new RoomRowFacts(false, true, RoomOffer.Join, true, false)));
        Assert.Equal(RoomAction.SeeMatch, RoomActionRule.Decide(new RoomRowFacts(false, true, RoomOffer.Full, false, false)));
    }

    [Fact]
    public void AFullRoomSaysFullEvenWithoutTheMod()
        => Assert.Equal(RoomAction.Full, RoomActionRule.Decide(Open(RoomOffer.Full, installed: false)));

    [Fact]
    public void TheModComesBeforeItsVersion()
        => Assert.Equal(RoomAction.InstallMod, RoomActionRule.Decide(Open(installed: false, differs: true)));

    [Fact]
    public void AnotherVersionAsksToCheckTheCopy()
        => Assert.Equal(RoomAction.CheckMyCopy, RoomActionRule.Decide(Open(differs: true)));

    [Fact]
    public void WatchOnlyWhenTheSeatsOfferIt()
    {
        Assert.Equal(RoomAction.Watch, RoomActionRule.Decide(Open(RoomOffer.Watch)));
        Assert.Equal(RoomAction.Join, RoomActionRule.Decide(Open()));
    }

    [Fact]
    public void WithoutObserversAFullGameIsFull()
    {
        var seats = RoomFormats.SeatsOf(4, 1, 3, 0);
        Assert.Equal(RoomAction.Full, RoomActionRule.Decide(Open(RoomFormats.OfferFor(seats, observersEnabled: false))));
        Assert.Equal(RoomAction.Watch, RoomActionRule.Decide(Open(RoomFormats.OfferFor(seats, observersEnabled: true))));
    }

    [Fact]
    public void TheGroupsFollowTheDesignsOrder()
    {
        Assert.Equal(RoomGroup.CanEnter, RoomActionRule.GroupOf(RoomAction.OpenMine));
        Assert.Equal(RoomGroup.CanEnter, RoomActionRule.GroupOf(RoomAction.Join));
        Assert.Equal(RoomGroup.CanEnter, RoomActionRule.GroupOf(RoomAction.InstallMod));
        Assert.Equal(RoomGroup.CanEnter, RoomActionRule.GroupOf(RoomAction.Watch));
        Assert.Equal(RoomGroup.InGame, RoomActionRule.GroupOf(RoomAction.SeeMatch));
        Assert.Equal(RoomGroup.Unavailable, RoomActionRule.GroupOf(RoomAction.CheckMyCopy));
        Assert.Equal(RoomGroup.Unavailable, RoomActionRule.GroupOf(RoomAction.Full));
    }

    [Fact]
    public void RankNeverDisagreesWithTheGroup()
    {
        var ordered = Enum.GetValues<RoomAction>().OrderBy(RoomActionRule.Rank).Select(RoomActionRule.GroupOf).ToList();
        for (var i = 1; i < ordered.Count; i++)
            Assert.True(ordered[i] >= ordered[i - 1]);
    }

    [Theory]
    [InlineData(null, "RmPingUnknown")]
    [InlineData(-1, "RmPingUnknown")]
    [InlineData(42, "RmPingGood")]
    [InlineData(99, "RmPingGood")]
    [InlineData(100, "RmPingPlain")]
    [InlineData(150, "RmPingPlain")]
    [InlineData(151, "RmPingSlow")]
    public void PingBands(int? ms, string key) => Assert.Equal(key, RoomActionRule.PingBrushKey(ms));

    [Fact]
    public void EveryCaptionAndHeadingIsTranslated()
    {
        foreach (var a in Enum.GetValues<RoomAction>())
            Assert.NotEqual(RoomActionRule.CaptionKey(a), Strings.Get(RoomActionRule.CaptionKey(a)));
        foreach (var g in Enum.GetValues<RoomGroup>())
            Assert.NotEqual(RoomActionRule.GroupKey(g), Strings.Get(RoomActionRule.GroupKey(g)));
    }
}
