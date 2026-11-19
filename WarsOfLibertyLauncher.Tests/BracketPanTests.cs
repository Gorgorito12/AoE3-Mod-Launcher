using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>Dragging the bracket: a short press still picks the card, a drag never does, and a
/// sheet that fits never arms.</summary>
public class BracketPanTests
{
    [Fact]
    public void AFourPixelMoveIsStillAClick()
    {
        Assert.False(BracketPan.Starts(4, 0));
        Assert.False(BracketPan.Starts(-3, 4));
        Assert.True(BracketPan.Starts(5, 0));
        Assert.True(BracketPan.Starts(0, -6));
    }

    [Fact]
    public void ASheetThatFitsNeverArms_NorAPressOnAScrollBar()
    {
        Assert.False(BracketPan.MayPan(800, 600, 800, 600, onScrollBar: false));
        Assert.True(BracketPan.MayPan(1600, 600, 800, 600, onScrollBar: false));
        Assert.False(BracketPan.MayPan(1600, 600, 800, 600, onScrollBar: true));
    }

    [Fact]
    public void TheSheetMovesWithTheHandAndStopsAtItsEdges()
    {
        Assert.Equal((80.0, 40.0), BracketPan.Offsets(100, 50, 20, 10, 500, 500));
        Assert.Equal((0.0, 0.0), BracketPan.Offsets(10, 10, 50, 50, 500, 500));
        Assert.Equal((500.0, 300.0), BracketPan.Offsets(480, 280, -100, -100, 500, 300));
    }
}

/// <summary>Only the OWNER changes a tournament's visibility (before the draw) or renews its code,
/// and only on a server that says it can.</summary>
public class TournamentVisibilityPermissionTests
{
    private static TournamentSummary T(string status, bool isPrivate) => new()
    {
        Id = "t", OwnerUserId = "owner", Status = status, Visibility = isPrivate ? "private" : "public",
    };

    [Fact]
    public void THE_ONE_THAT_MATTERS_ACoOrganiserOrAnOlderServerGetsNoControl()
    {
        Assert.True(TournamentPermissions.CanChangeVisibility(T("registration", false), "owner", serverSupports: true));
        Assert.False(TournamentPermissions.CanChangeVisibility(T("registration", false), "helper", serverSupports: true));
        Assert.False(TournamentPermissions.CanChangeVisibility(T("registration", false), "owner", serverSupports: false));
        Assert.False(TournamentPermissions.CanChangeVisibility(T("running", false), "owner", serverSupports: true));
    }

    [Fact]
    public void OnlyAPrivateTournamentHasACodeToRenew()
    {
        Assert.True(TournamentPermissions.CanRenewCode(T("finished", true), "owner", serverSupports: true));
        Assert.False(TournamentPermissions.CanRenewCode(T("registration", false), "owner", serverSupports: true));
        Assert.False(TournamentPermissions.CanRenewCode(T("registration", true), "helper", serverSupports: true));
    }
}
