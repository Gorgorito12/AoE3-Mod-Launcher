using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The rooms list's PING column (design 86): the round trip to each host over Radmin. Every case
/// is about not showing a number that is not this host's, and not showing a column a server
/// cannot fill.
/// </summary>
[Collection("wpf-and-language")]
public class HostPingTests
{
    private static HostPingKind K(bool mine = false, bool radmin = true, bool published = true,
            bool hostIp = true, int? last = null, int fails = 0)
        => HostPing.Classify(new HostPingFacts(mine, radmin, published, hostIp, last, fails));

    [Fact]
    public void AnAnsweredProbeShowsTheFigure()
    {
        Assert.Equal(HostPingKind.Answered, K(last: 38));
        Assert.Equal("38 ms", HostPing.Text(HostPingKind.Answered, 38));
    }

    [Fact]
    public void MyOwnRoomShowsNothing()
        => Assert.Equal(HostPingKind.None, K(mine: true, last: 12));

    [Fact]
    public void WithoutMyRadminNothingIsMeasured()
        => Assert.Equal(HostPingKind.NoMyRadmin, K(radmin: false, last: 40));

    /// <summary>A password or tournament room's host is never published; it must not read as a host
    /// who is "not on Radmin", which would be a guess.</summary>
    [Fact]
    public void ARoomWhoseHostIsNotPublishedSaysSo()
        => Assert.Equal(HostPingKind.NotPublished, K(published: false, hostIp: false));

    [Fact]
    public void AHostWithoutAnAddressIsNotOnRadmin()
        => Assert.Equal(HostPingKind.HostNotOnRadmin, K(hostIp: false));

    /// <summary>One dropped packet is not a firewall: "no answer" needs two in a row.</summary>
    [Fact]
    public void OneMissedProbeIsStillMeasuring_TwoAreNoAnswer()
    {
        Assert.Equal(HostPingKind.Measuring, K(fails: 1));
        Assert.Equal(HostPingKind.NoAnswer, K(fails: HostPing.FailsBeforeNoAnswer));
        Assert.Equal("—", HostPing.Text(HostPingKind.NoAnswer, null));
        Assert.Equal("…", HostPing.Text(HostPingKind.Measuring, null));
    }

    [Fact]
    public void EveryStateThatShowsADashExplainsItself()
    {
        foreach (var kind in Enum.GetValues<HostPingKind>())
        {
            if (kind == HostPingKind.None) { Assert.Null(HostPing.TipKey(kind)); continue; }
            var key = HostPing.TipKey(kind);
            Assert.NotNull(key);
            Assert.NotEqual(key, WarsOfLibertyLauncher.Localization.Strings.Get(key!));
        }
    }

    /// <summary>
    /// A server without <c>GET /lobbies/radmin</c> leaves the list exactly as it was: the base set
    /// has no PING column at any width, and the row adds its cell only behind the same flag.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_NoPingColumnWithoutTheServer()
    {
        for (var w = 0; w <= 2400; w += 40)
            Assert.DoesNotContain(RoomsTableLayout.Resolve(w), c => c.Column == RoomColumn.Ping);
        Assert.Contains(RoomsTableLayout.Resolve(2400, includePing: true), c => c.Column == RoomColumn.Ping);

        var rows = File.ReadAllText(UiThreadAttributionTests.LauncherFile("Controls/MultiplayerTab.RoomRows.cs"));
        Assert.Contains("if (HostPingColumnShown)\n            PlaceRoomCell(grid, RoomColumn.Ping", rows.Replace("\r\n", "\n"));
    }

    /// <summary>An older server answers /lobbies/radmin as the room called "radmin": a 404, which
    /// means "not supported", never an error that would leave a column of dashes.</summary>
    [Fact]
    public async Task AnOldServersNotFoundMeansNoColumn()
    {
        var (url, server, stop, requests) = SessionRefreshTests.Serve(_ => (404, "Not Found",
            "{\"error\":\"not_found\",\"message\":\"lobby not found\"}"));
        try
        {
            var config = SessionRefreshTests.SignedIn(url, "token", DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600);
            var session = new MultiplayerSession(config);
            Assert.Null(await session.Api.GetHostRadminIpsAsync());
            Assert.True(requests.TryDequeue(out var head));
            Assert.StartsWith("GET /lobbies/radmin ", head);
            Assert.Contains("Bearer token", head);
        }
        finally { await SessionRefreshTests.Finish(stop, server); }
    }

    [Fact]
    public async Task ANewServersAnswerIsReadByLobbyId()
    {
        var (url, server, stop, _) = SessionRefreshTests.Serve(_ => (200, "OK",
            "{\"hosts\":{\"ABC123\":\"26.1.2.3\"}}"));
        try
        {
            var config = SessionRefreshTests.SignedIn(url, "token", DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600);
            var session = new MultiplayerSession(config);
            var hosts = await session.Api.GetHostRadminIpsAsync();
            Assert.NotNull(hosts);
            Assert.Equal("26.1.2.3", hosts!["ABC123"]);
        }
        finally { await SessionRefreshTests.Finish(stop, server); }
    }
}
