using System.Collections.Generic;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Tests for the pure host-address resolver.
///
/// <para><b>The REFUSALS are the point.</b> Only one case here produces an address; the
/// rest exist to stop the launcher handing a player a confident wrong one. A missing
/// address shows a grey "waiting" line the player can read and act on; a wrong address
/// sends them into a different game and, in a competitive room, lands a real result on
/// the wrong people — and nothing on screen would look broken while it happened.</para>
/// </summary>
public class HostJoinAddressTests
{
    private static IReadOnlyDictionary<string, string?> Ips(params (string Id, string? Ip)[] entries)
    {
        var d = new Dictionary<string, string?>();
        foreach (var (id, ip) in entries) d[id] = ip;
        return d;
    }

    [Fact]
    public void AHostWithARadminAddressIsTheOnlyCaseThatProducesOne()
    {
        var r = HostJoinAddress.Resolve("host", "me", Ips(("host", "26.58.19.45")));

        Assert.Equal(HostAddressState.Ready, r.State);
        Assert.Equal("26.58.19.45", r.Ip);
        Assert.True(r.HasAddress);
    }

    [Fact]
    public void NoRoomAndNoHostBothMeanThereIsNothingToShow()
    {
        Assert.Equal(HostAddressState.NoRoom, HostJoinAddress.Resolve(null, "me", Ips()).State);
        Assert.Equal(HostAddressState.NoRoom, HostJoinAddress.Resolve("", "me", Ips()).State);
        Assert.Equal(HostAddressState.NoRoom, HostJoinAddress.Resolve("   ", "me", Ips()).State);
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_ANonRadminAddressIsRefusedRatherThanOffered()
    {
        // A 192.168.x from the host's PHYSICAL LAN resolves on the JOINER's own network,
        // so it does not fail — it quietly reaches a different machine. The backend
        // validates 26.x server-side, but an older backend does not, which is exactly
        // why this side refuses too instead of trusting the wire.
        foreach (var bogus in new[] { "192.168.1.20", "10.0.0.5", "127.0.0.1", "not-an-ip", "126.1.2.3" })
        {
            var r = HostJoinAddress.Resolve("host", "me", Ips(("host", bogus)));
            Assert.Equal(HostAddressState.WaitingForHost, r.State);
            Assert.Null(r.Ip);
            Assert.False(r.HasAddress);
        }
    }

    [Fact]
    public void TheHostIsNeverOfferedHisOwnAddress()
    {
        // He creates the game; everyone else comes to him. Handing him his own IP to
        // paste into his own game is a no-op the player would spend time on.
        var r = HostJoinAddress.Resolve("me", "me", Ips(("me", "26.1.2.3")));

        Assert.Equal(HostAddressState.YouAreTheHost, r.State);
        Assert.Null(r.Ip);
    }

    [Fact]
    public void AHostWhoHasNotReportedYetIsWaiting_NotAbsent()
    {
        // Normal for the first seconds of a room: set_radmin_ip is sent on entry and
        // again at launch. Distinct from NoRoom because here there IS someone to wait for.
        Assert.Equal(HostAddressState.WaitingForHost,
            HostJoinAddress.Resolve("host", "me", Ips()).State);
        Assert.Equal(HostAddressState.WaitingForHost,
            HostJoinAddress.Resolve("host", "me", Ips(("host", null))).State);
        Assert.Equal(HostAddressState.WaitingForHost,
            HostJoinAddress.Resolve("host", "me", Ips(("host", "  "))).State);
    }

    [Fact]
    public void UserIdsCompareOrdinally_SoACaseDifferenceIsADifferentAccount()
    {
        // Every other user-id comparison on this path is Ordinal. If this one were not,
        // a member whose id merely differed in case would be read as "you are the host"
        // and the real host's address would never be offered.
        var r = HostJoinAddress.Resolve("HOST", "host", Ips(("HOST", "26.9.9.9")));

        Assert.Equal(HostAddressState.Ready, r.State);
        Assert.Equal("26.9.9.9", r.Ip);
    }

    [Fact]
    public void SurroundingWhitespaceIsTrimmedRatherThanRefused()
    {
        var r = HostJoinAddress.Resolve("host", "me", Ips(("host", "  26.58.19.45  ")));

        Assert.Equal(HostAddressState.Ready, r.State);
        Assert.Equal("26.58.19.45", r.Ip);
    }

    [Fact]
    public void ASignedOutViewerStillGetsTheAddress()
    {
        // myUserId is only ever used to recognise ourselves as the host. Not knowing it
        // must not suppress the address — that would be a refusal with no safety value.
        var r = HostJoinAddress.Resolve("host", null, Ips(("host", "26.4.4.4")));

        Assert.Equal(HostAddressState.Ready, r.State);
    }
}
