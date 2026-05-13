using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WarsOfLibertyLauncher.Models.Multiplayer;

/// <summary>
/// What the launcher knows about the local ZeroTier install right now.
/// Drives which buttons the multiplayer tab can offer:
///   * NotInstalled         → show "Install ZeroTier" (UAC)
///   * InstalledServiceDown → show "Start ZeroTier service" (UAC)
///   * Running              → multiplayer is usable
///   * RunningNotAuthorized → connected to daemon but no API access yet
///                            (authtoken not readable, need a one-shot copy)
/// </summary>
public enum ZeroTierState
{
    Unknown,
    NotInstalled,
    InstalledServiceDown,
    RunningNotAuthorized,
    Running,
}

/// <summary>
/// Result of <c>GET /status</c> against the local daemon
/// (<c>http://127.0.0.1:9993/status</c>). Only the fields we use are
/// modelled — ZeroTier returns far more, but the launcher just needs the
/// node id, online flag, and version.
/// </summary>
public class ZeroTierNodeStatus
{
    /// <summary>10-hex-char ZeroTier address (the "node id" we send to the Worker).</summary>
    [JsonPropertyName("address")]
    public string Address { get; set; } = "";

    /// <summary>True when the daemon has reached the planet roots.</summary>
    [JsonPropertyName("online")]
    public bool Online { get; set; }

    /// <summary>e.g. "1.14.0".</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    /// <summary>True iff the daemon serves the JSON API at 127.0.0.1:9993.</summary>
    [JsonPropertyName("tcpFallbackActive")]
    public bool TcpFallbackActive { get; set; }
}

/// <summary>
/// Result of <c>GET /network/&lt;id&gt;</c>: a single membership record.
/// We surface enough fields for the join dialog to show why we are or
/// aren't on the LAN yet ("waiting for host to authorise…", "got IP
/// 10.147.20.42, ready to play").
/// </summary>
public class ZeroTierNetworkMembership
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>"OK", "ACCESS_DENIED", "REQUESTING_CONFIGURATION", "AUTHENTICATION_REQUIRED", "NOT_FOUND", etc.</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    /// <summary>IPv4/v6 addresses ZT assigned us in CIDR form.</summary>
    [JsonPropertyName("assignedAddresses")]
    public List<string> AssignedAddresses { get; set; } = new();

    /// <summary>OS-level interface ZT exposes for this network ("ztabcdef01").</summary>
    [JsonPropertyName("portDeviceName")]
    public string PortDeviceName { get; set; } = "";

    /// <summary>True when the local kernel routes are wired up.</summary>
    [JsonPropertyName("allowGlobal")]
    public bool AllowGlobal { get; set; }
}
