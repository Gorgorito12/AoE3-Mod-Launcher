using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace WarsOfLibertyLauncher.Services.Multiplayer.P2P;

/// <summary>
/// What the user's home/office NAT does to their UDP packets, as far
/// as P2P matchmaking cares. The labels are deliberately user-facing
/// rather than RFC-precise — the launcher shows them as a badge in
/// the Multiplayer tab, so they need to make sense to a non-engineer.
/// </summary>
public enum NatType
{
    /// <summary>Probe failed (offline, firewall blocking UDP, etc.).</summary>
    Unknown,

    /// <summary>No NAT, or full-cone NAT. Hole-punching works trivially.</summary>
    Open,

    /// <summary>Same public port from every destination — easy hole-punch.</summary>
    Moderate,

    /// <summary>
    /// Restricted-cone NAT. Same port but only inbound from peers we
    /// already sent to. Hole-punch works with coordinated send.
    /// </summary>
    Strict,

    /// <summary>
    /// Symmetric NAT — different public port per destination. Direct
    /// hole-punch fails; need a TURN relay.
    /// </summary>
    Symmetric,
}

/// <summary>
/// Snapshot of a NAT probe. <see cref="PublicEndpoint"/> is what other
/// peers would see as our address; the launcher publishes it via the
/// lobby's signaling WS so peers can hole-punch us.
/// </summary>
public sealed record NatProbeResult(
    NatType Type,
    IPEndPoint? PublicEndpoint,
    IPEndPoint? SecondaryEndpoint,
    string? ErrorMessage);

/// <summary>
/// Two-server NAT type probe.
///
/// Approach (a simplified take on RFC 5780):
///   1. Bind a single local UDP socket.
///   2. Run a STUN Binding Request against server A — record mapped IP:port.
///   3. Run another Binding Request against server B FROM THE SAME SOCKET
///      — record mapped IP:port.
///   4. If both mapped ports match, NAT is cone-style (we expose the
///      same port regardless of destination → hole-punch works).
///      If they differ, NAT is symmetric and we need a TURN relay.
///   5. Compare the local socket address against the mapped address
///      to detect "no NAT" (true public IP / port-forwarded).
///
/// We don't bother distinguishing Restricted-cone from Port-Restricted
/// because the hole-punch strategy is the same for both: peers send
/// each other UDP packets simultaneously to open their respective
/// inbound paths.
///
/// Free, no-registration STUN servers we cycle through:
///   * stun.l.google.com:19302
///   * stun.cloudflare.com:3478
///   * stun.nextcloud.com:443
/// </summary>
public static class NatTypeDetector
{
    /// <summary>
    /// Default list of public STUN servers. Ordered by perceived
    /// reliability; the probe tries them in order until one answers.
    /// </summary>
    public static readonly (string Host, int Port)[] DefaultServers = new[]
    {
        ("stun.l.google.com", 19302),
        ("stun.cloudflare.com", 3478),
        ("stun.nextcloud.com", 443),
    };

    /// <summary>
    /// Detect the local NAT type. Total wall time is bounded to a few
    /// seconds — both STUN probes time out at 2 s each, with a single
    /// retry across the server list.
    /// </summary>
    public static async Task<NatProbeResult> DetectAsync(CancellationToken ct = default)
    {
        // One socket for both probes — that's the whole point. A new
        // socket would get a different ephemeral port and we'd compare
        // apples to oranges.
        using var udp = new UdpClient(0);          // bind to any local port

        IPEndPoint? mappedA = null;
        IPEndPoint? mappedB = null;
        IPEndPoint? localSource = null;
        string? lastError = null;

        // First server pass: find any one that answers.
        foreach (var (host, port) in DefaultServers)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var srv = await ResolveAsync(host, port, ct);
                if (srv == null) continue;
                var r = await StunClient.BindingRequestAsync(udp, srv, TimeSpan.FromSeconds(2), ct);
                mappedA = r.Mapped;
                localSource = r.LocalSource;
                break;
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                DiagnosticLog.Write($"NAT probe: {host}:{port} failed: {ex.Message}");
            }
        }

        if (mappedA == null || localSource == null)
        {
            return new NatProbeResult(NatType.Unknown, null, null,
                lastError ?? "No STUN server reachable.");
        }

        // Second probe — different server. If the mapped port matches,
        // the NAT is cone-style (no per-destination remapping).
        foreach (var (host, port) in DefaultServers)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var srv = await ResolveAsync(host, port, ct);
                if (srv == null) continue;
                // Skip the same server we used for probe A. (mappedA is
                // guaranteed non-null here by the early return above,
                // but the compiler can't see across loop iterations.)
                if (srv.Address.Equals(mappedA!.Address)) continue;
                var r = await StunClient.BindingRequestAsync(udp, srv, TimeSpan.FromSeconds(2), ct);
                mappedB = r.Mapped;
                break;
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                DiagnosticLog.Write($"NAT probe B: {host}:{port} failed: {ex.Message}");
            }
        }

        var type = ClassifyNat(localSource, mappedA, mappedB);
        return new NatProbeResult(type, mappedA, mappedB, null);
    }

    private static NatType ClassifyNat(
        IPEndPoint localSource,
        IPEndPoint mappedA,
        IPEndPoint? mappedB)
    {
        // No NAT — the address we bound locally is the same one the
        // STUN server saw. Lucky users with public IPs or port forwards.
        if (localSource.Address.Equals(mappedA.Address) && localSource.Port == mappedA.Port)
            return NatType.Open;

        if (mappedB == null)
        {
            // Only one probe succeeded. We can't distinguish cone from
            // symmetric — call it Strict (worst-case for hole-punching
            // that's still recoverable). The UI nudges the user to
            // retry the probe later.
            return NatType.Strict;
        }

        if (mappedA.Port == mappedB.Port && mappedA.Address.Equals(mappedB.Address))
        {
            // Same public port from two different destinations →
            // cone-style. We collapse full-cone and restricted-cone
            // into "Moderate" because hole-punching is the same for
            // both from our perspective.
            return NatType.Moderate;
        }

        // Different public port per destination → symmetric. Direct
        // hole-punch won't work; TURN relay required.
        return NatType.Symmetric;
    }

    private static async Task<IPEndPoint?> ResolveAsync(string host, int port, CancellationToken ct)
    {
        try
        {
            var ips = await Dns.GetHostAddressesAsync(host, ct);
            var v4 = ips.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            if (v4 == null) return null;
            return new IPEndPoint(v4, port);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"NAT probe: DNS for {host} failed: {ex.Message}");
            return null;
        }
    }
}
