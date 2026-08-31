using System.Collections.Generic;
using System.Net.NetworkInformation;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Which network interface is Radmin's.
///
/// <para><b>This had no coverage at all, and it is the half that failed in the wild.</b> The
/// tested half was the power-state parser — the gate that PASSED on the reporting user's machine
/// — while the adapter walk, the gate that rejected him, called
/// <c>NetworkInterface.GetAllNetworkInterfaces()</c> straight and could not be reached from a
/// test. The selection is a pure function over a plain record now, for exactly that reason.</para>
///
/// <para>The rejections are the point: loosening the name is only safe because the ADDRESS is
/// still required, and these pin that.</para>
/// </summary>
public class RadminAdapterTests
{
    private static RadminVpnService.AdapterCandidate Nic(
        string name, string description, OperationalStatus status, params string[] ips) =>
        new(name + "-id", name, description, status, new List<string>(ips));

    /// <summary>
    /// <b>The reported machine, reduced.</b> Radmin on screen, online, 26.217.215.106 and fifteen
    /// peers — and a connection name Windows had generated, with no "Radmin" in it. The old walk
    /// threw the interface away on its first line and logged <c>adapter=none</c>.
    /// </summary>
    [Fact]
    public void AnInterfaceWindowsRenamedIsStillRadmins()
    {
        var chosen = RadminVpnService.SelectRadminAdapter(new[]
        {
            Nic("Ethernet", "Realtek Gaming 2.5GbE Family Controller", OperationalStatus.Up, "192.168.1.34"),
            Nic("Ethernet 3", "Radmin VPN Ethernet Adapter", OperationalStatus.Up, "26.217.215.106"),
        });

        Assert.NotNull(chosen);
        Assert.Equal("26.217.215.106", chosen!.Radmin26Ip);
    }

    /// <summary>
    /// Nothing in the name or the description, either — a driver string can be replaced too, and
    /// the address is what the game is going to bind to regardless.
    /// </summary>
    [Fact]
    public void NeitherNameNorDescriptionIsRequired()
    {
        var chosen = RadminVpnService.SelectRadminAdapter(new[]
        {
            Nic("Ethernet 5", "TAP-Windows Adapter V9", OperationalStatus.Up, "26.1.2.3"),
        });

        Assert.Equal("26.1.2.3", chosen?.Radmin26Ip);
    }

    /// <summary>
    /// Windows routes and pings over an interface without consulting `OperationalStatus`, and a
    /// virtual NIC with no physical media may legitimately report `Unknown` or `Dormant`. Being
    /// carried by such an interface does not make the address unusable.
    /// </summary>
    [Theory]
    [InlineData(OperationalStatus.Unknown)]
    [InlineData(OperationalStatus.Dormant)]
    [InlineData(OperationalStatus.Down)]
    public void AStatusOtherThanUpDoesNotDisqualifyIt(OperationalStatus status)
    {
        var chosen = RadminVpnService.SelectRadminAdapter(new[]
        {
            Nic("Radmin VPN", "Radmin VPN Ethernet Adapter", status, "26.5.5.5"),
        });

        Assert.Equal("26.5.5.5", chosen?.Radmin26Ip);
    }

    /// <summary>
    /// <b>The refusal that makes loosening the name safe.</b> The old filter demanded "Radmin" in
    /// the name so it would not read the wrong card; the address does that job better, and it has
    /// to keep doing it — an interface CALLED Radmin with no 26.x address is not the one.
    /// </summary>
    [Fact]
    public void ARadminNamedInterfaceWithoutA26AddressIsNotChosen()
    {
        var chosen = RadminVpnService.SelectRadminAdapter(new[]
        {
            Nic("Radmin VPN", "Radmin VPN Ethernet Adapter", OperationalStatus.Up, "192.168.56.1"),
        });

        Assert.Null(chosen);
    }

    /// <summary>Nothing carrying the address means nothing, never a consolation pick.</summary>
    [Fact]
    public void NoTwentySixAddressAnywhereMeansNull()
    {
        Assert.Null(RadminVpnService.SelectRadminAdapter(new[]
        {
            Nic("Ethernet", "Realtek", OperationalStatus.Up, "192.168.1.10"),
            Nic("Wi-Fi", "Intel AX201", OperationalStatus.Down, "169.254.7.7"),
        }));

        Assert.Null(RadminVpnService.SelectRadminAdapter(new List<RadminVpnService.AdapterCandidate>()));
    }

    /// <summary>
    /// With two interfaces carrying a 26.x — a leftover from a reinstall beside the live one —
    /// the one that is Up wins, whatever they are called.
    /// </summary>
    [Fact]
    public void AmongSeveralTheOneThatIsUpWins()
    {
        var chosen = RadminVpnService.SelectRadminAdapter(new[]
        {
            Nic("Radmin VPN", "Radmin VPN Ethernet Adapter", OperationalStatus.Down, "26.9.9.9"),
            Nic("Ethernet 7", "Radmin VPN Ethernet Adapter #2", OperationalStatus.Up, "26.8.8.8"),
        });

        Assert.Equal("26.8.8.8", chosen?.Radmin26Ip);
    }

    /// <summary>Same status: the one that says Radmin breaks the tie.</summary>
    [Fact]
    public void AmongEqualsTheNamedOneWins()
    {
        var chosen = RadminVpnService.SelectRadminAdapter(new[]
        {
            Nic("Ethernet 9", "Some Other Virtual Adapter", OperationalStatus.Up, "26.7.7.7"),
            Nic("Radmin VPN", "Radmin VPN Ethernet Adapter", OperationalStatus.Up, "26.6.6.6"),
        });

        Assert.Equal("26.6.6.6", chosen?.Radmin26Ip);
    }

    /// <summary>
    /// The prefix is a prefix of the OCTET, not of the string: 260.x cannot exist, but 2.6.x and
    /// 126.x must not be mistaken for it.
    /// </summary>
    [Theory]
    [InlineData("126.4.4.4")]
    [InlineData("2.6.4.4")]
    [InlineData("226.4.4.4")]
    public void AddressesThatMerelyContainTwentySixAreNotIt(string ip)
    {
        Assert.Null(RadminVpnService.SelectRadminAdapter(new[]
        {
            Nic("Radmin VPN", "Radmin VPN Ethernet Adapter", OperationalStatus.Up, ip),
        }));
    }
}
