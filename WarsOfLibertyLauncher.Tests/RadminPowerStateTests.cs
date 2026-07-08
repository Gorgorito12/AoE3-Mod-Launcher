using System.Collections.Generic;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Tests for the pure Radmin power-toggle classifier. It scans the log
/// lines newest→oldest and returns the first decisive event, so the LAST
/// relevant line wins: "Switched Off" ⇒ Off; "Switched On" or "Connected
/// to server" ⇒ On. Transient "Disconnected from server" and per-peer
/// "Connected to &lt;id&gt;/'name'" lines are ignored. This is the signal
/// that fixes the false-green banner when Radmin is powered off but its
/// adapter still lingers Up with the static 26.x IP.
/// </summary>
public class RadminPowerStateTests
{
    private static string Line(string msg) => $"2026.07.07 20:04:38.336\tINFO\t{msg}";

    [Fact]
    public void LastSwitchedOff_IsOff()
    {
        var lines = new List<string>
        {
            Line("Switched On"),
            Line("Connected to server (16)"),
            Line("Switched Off"),
            Line("Disconnected from server(code:4)"),
        };
        Assert.Equal(RadminPowerState.Off, RadminLogService.DeterminePowerState(lines));
    }

    [Fact]
    public void LastConnectedToServer_IsOn()
    {
        var lines = new List<string>
        {
            Line("Switched Off"),
            Line("Switched On"),
            Line("Connected to server (16)"),
        };
        Assert.Equal(RadminPowerState.On, RadminLogService.DeterminePowerState(lines));
    }

    [Fact]
    public void SwitchedOn_ThenSwitchedOff_IsOff()
    {
        var lines = new List<string>
        {
            Line("Switched On"),
            Line("Switched Off"),
        };
        Assert.Equal(RadminPowerState.Off, RadminLogService.DeterminePowerState(lines));
    }

    [Fact]
    public void SwitchedOff_ThenConnected_IsOn()
    {
        var lines = new List<string>
        {
            Line("Switched Off"),
            Line("Connected to server (16)"),
        };
        Assert.Equal(RadminPowerState.On, RadminLogService.DeterminePowerState(lines));
    }

    [Fact]
    public void TransientDisconnect_AfterConnected_IsStillOn()
    {
        // A network blip logs "Disconnected from server" but no "Switched
        // Off"; it must NOT flip us to Off (we ignore it and see the prior
        // "Connected to server").
        var lines = new List<string>
        {
            Line("Connected to server (16)"),
            Line("Node 105152551/'yungwilly' disconnected. Error: 0x200002745"),
            Line("Disconnected from server(code:4)"),
        };
        Assert.Equal(RadminPowerState.On, RadminLogService.DeterminePowerState(lines));
    }

    [Fact]
    public void OnlyPeerLines_AreIgnored_Unknown()
    {
        // "Connected to <id>/'name'" is a peer connection, NOT the literal
        // "Connected to server" — it must not be read as a power-on.
        var lines = new List<string>
        {
            Line("Connected to 164269969/'Jendersongessler' via TCP/outgoing"),
            Line("Connected to 82823768/'nacho' via TcpRelay/outgoing"),
        };
        Assert.Equal(RadminPowerState.Unknown, RadminLogService.DeterminePowerState(lines));
    }

    [Fact]
    public void Empty_IsUnknown()
    {
        Assert.Equal(RadminPowerState.Unknown, RadminLogService.DeterminePowerState(new List<string>()));
    }
}
