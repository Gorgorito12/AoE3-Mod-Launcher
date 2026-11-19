using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// A player with no home city launches WITHOUT the automatic LAN entry that time (the game asks
/// for a city and the DLL does not wait for that screen). The refusals are the point: a wrong
/// "has a city" launches him into the screen this exists to keep him out of, and a wrong "no city"
/// costs everybody else the automatic entry — so only a save folder that was READ and holds no city
/// counts as none.
/// </summary>
public class HomeCityPresenceTests
{
    [Theory]
    [InlineData("sp_Beijing_homecity.xml", true)]
    [InlineData("SP_Londres_HomeCity.XML", true)]
    [InlineData("sp_Asunción_homecity.xml", true)]
    [InlineData("LastHomeCityY.xml", false)]          // a copy of the last city used, never a city alone
    [InlineData("homecityamericans.xml", false)]      // a stray data file a modder left in Savegame
    [InlineData("sp__homecity.xml", false)]
    [InlineData("Record Game 1.age3Yrec", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyTheGamesOwnCityFilesCount(string? name, bool city)
        => Assert.Equal(city, HomeCityPresence.IsCityFile(name));

    [Fact]
    public void THE_ONE_THAT_MATTERS_AFolderNotReadIsUnknownNeverNone()
        => Assert.Equal(HomeCityState.Unknown, HomeCityPresence.From(null));

    [Fact]
    public void AReadFolderWithoutACityIsNone_EvenWithTheLastUsedCopy()
        => Assert.Equal(HomeCityState.None,
            HomeCityPresence.From(new[] { "LastHomeCityY.xml", "homecityamericans.xml", "Record Game 1.age3Yrec" }));

    [Fact]
    public void OneCityIsEnough()
        => Assert.Equal(HomeCityState.Present,
            HomeCityPresence.From(new[] { "LastHomeCityY.xml", "sp_Beijing_homecity.xml" }));

    [Fact]
    public void NoProfileIsUnknown() => Assert.Equal(HomeCityState.Unknown, HomeCityPresence.For(null, null));

    // ---- the room's card ----

    [Fact]
    public void THE_ONE_THAT_MATTERS_NoCityOutranksOnAndTheLaunchGoesByHand()
    {
        var card = RoomLanEntryView.For(RoomLanAvailability.Available, true, true, HomeCityState.None);
        Assert.Equal(RoomLanCardKind.NeedsHomeCity, card.Kind);
        Assert.Equal("LanCardNoCityBody", card.BodyKey);
        Assert.False(card.ShowSwitch);
        Assert.False(RoomLanEntryView.MayAutoEnter(true, HomeCityState.None));
        Assert.False(RoomLanEntryView.EntersByItself(RoomLanAvailability.Available, true, HomeCityState.None));
    }

    [Fact]
    public void UnknownChangesNothing()
    {
        Assert.Equal(RoomLanCardKind.On,
            RoomLanEntryView.For(RoomLanAvailability.Available, true, true, HomeCityState.Unknown).Kind);
        Assert.True(RoomLanEntryView.MayAutoEnter(true, HomeCityState.Unknown));
        Assert.True(RoomLanEntryView.EntersByItself(RoomLanAvailability.Available, true, HomeCityState.Unknown));
    }

    [Fact]
    public void ACityChangesNothing()
        => Assert.Equal(RoomLanCardKind.On,
            RoomLanEntryView.For(RoomLanAvailability.Available, true, true, HomeCityState.Present).Kind);

    [Fact]
    public void TheSwitchOffStaysOff_AndTheOtherStatesKeepTheirOwnCard()
    {
        Assert.Equal(RoomLanCardKind.Off, RoomLanEntryView.For(RoomLanAvailability.Available, false, true, HomeCityState.None).Kind);
        Assert.Equal(RoomLanCardKind.NotSetUp, RoomLanEntryView.For(RoomLanAvailability.NotSetUp, true, true, HomeCityState.None).Kind);
        Assert.Equal(RoomLanCardKind.CannotSetUp, RoomLanEntryView.For(RoomLanAvailability.CannotSetUp, true, true, HomeCityState.None).Kind);
        Assert.False(RoomLanEntryView.MayAutoEnter(false, HomeCityState.Present));
    }
}
