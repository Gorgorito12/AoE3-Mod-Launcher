using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="InGameNamePublishState"/> — when our AoE3 profile name goes out to the room.
///
/// <para>Every way the first competitive 2v2s lost a name was the launcher counting a name as
/// delivered because it had been WRITTEN: to a socket not yet open, before the server had
/// handled our hello, or just before a reconnect rebuilt our member without it. The cases below
/// are those three, plus the ones that keep the resend from running for ever.</para>
/// </summary>
public class InGameNamePublishStateTests
{
    [Fact]
    public void NothingGoesOutBeforeTheServerHasAnsweredOurHello()
    {
        // A frame that lands before the hello is handled is read as unauthenticated. The
        // room_state is the server's answer to the hello, so it is what opens the gate.
        var s = new InGameNamePublishState();
        Assert.False(s.ShouldSend("Gorgorito"));

        s.RoomState(null);
        Assert.True(s.ShouldSend("Gorgorito"));
    }

    [Fact]
    public void ItKeepsGoingOutUntilTheServerConfirmsIt()
    {
        // Sending is not delivering. Until the server says it holds the name, every tick sends
        // it again — the server ignores an unchanged name, so a resend costs one small frame.
        var s = new InGameNamePublishState();
        s.RoomState(null);

        Assert.True(s.ShouldSend("Gorgorito"));
        Assert.True(s.ShouldSend("Gorgorito"));

        s.Echo("Gorgorito");
        Assert.False(s.ShouldSend("Gorgorito"));
    }

    [Fact]
    public void AReconnectMakesItGoOutAgain()
    {
        // THE ONE THAT MATTERS. The server deletes our member on close and rebuilds it without
        // the name. The old guard still said "already sent" and the name was gone for the match.
        var s = new InGameNamePublishState();
        s.RoomState("Gorgorito");
        Assert.False(s.ShouldSend("Gorgorito"));

        s.ConnectionLost();
        Assert.False(s.ShouldSend("Gorgorito"));    // not until the next hello is answered

        s.RoomState(null);                          // ...and that answer no longer has it
        Assert.True(s.ShouldSend("Gorgorito"));
    }

    [Fact]
    public void ARoomStateThatAlreadyHasItIsTheConfirmation()
    {
        var s = new InGameNamePublishState();
        s.RoomState("Gorgorito");
        Assert.False(s.ShouldSend("Gorgorito"));
    }

    [Fact]
    public void AChangedNameGoesOut()
    {
        // The player switched AoE3 profile between rooms or games.
        var s = new InGameNamePublishState();
        s.RoomState("Gorgorito");
        Assert.True(s.ShouldSend("gorgorito2"));
    }

    [Fact]
    public void TheServersTrimIsMatched_SoAStraySpaceDoesNotResendForEver()
    {
        // The server stores frame.name.trim(); comparing an untrimmed name against it would
        // never match and the resend would never stop.
        var s = new InGameNamePublishState();
        s.RoomState("Gorgorito");
        Assert.False(s.ShouldSend("  Gorgorito "));
    }

    [Fact]
    public void ANewRoomStartsFromNothing()
    {
        var s = new InGameNamePublishState();
        s.RoomState("Gorgorito");

        s.Reset();

        Assert.False(s.Ready);
        Assert.Null(s.Confirmed);
        Assert.False(s.ShouldSend("Gorgorito"));
    }

    [Fact]
    public void ABlankNameIsNeverSent_AndABlankEchoConfirmsNothing()
    {
        var s = new InGameNamePublishState();
        s.RoomState(null);

        Assert.False(s.ShouldSend(null));
        Assert.False(s.ShouldSend("   "));

        s.Echo("  ");
        Assert.True(s.ShouldSend("Gorgorito"));
    }
}
