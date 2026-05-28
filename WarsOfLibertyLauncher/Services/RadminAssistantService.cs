using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Stage in the Radmin VPN connection journey, ordered from "nothing
/// done yet" up to "fully verified inside the AoE3 TAD network".
/// </summary>
///
/// The assistant overlay renders one checklist step per stage and
/// auto-advances when <see cref="RadminAssistantService.ProbeAsync"/>
/// reports a higher stage. The user never has to click "next" — the
/// 3-second polling loop just keeps re-probing and the UI reacts.
public enum RadminStage
{
    /// <summary>Radmin MSI isn't installed on this machine yet.</summary>
    NotInstalled = 0,

    /// <summary>
    /// Installed, but the user isn't signed in: no Radmin NIC is up
    /// with a 26.x.x.x identity IP. They need to either open Radmin
    /// or finish their first-time Famatech account creation flow.
    /// </summary>
    InstalledNotRunning = 1,

    /// <summary>
    /// User has an active 26.x.x.x adapter IP (i.e. Radmin is running
    /// and signed in to Famatech), but we can't independently verify
    /// they're joined to the specific AoE3 TAD network — Radmin's
    /// per-network membership lives server-side and isn't exposed.
    ///
    /// This is the "ambiguous" state: they might be in the right
    /// network, or in some other network, or in zero networks. The
    /// overlay tells them to verify in the Radmin window.
    /// </summary>
    LoggedIn = 2,

    /// <summary>
    /// Confirmed in the AoE3 TAD network. Promoted from
    /// <see cref="LoggedIn"/> by <see cref="RadminAssistantService.ProbeAsync"/>
    /// when either:
    ///   • <see cref="RadminLogService.GetActiveNetworkMemberships"/>
    ///     reports the canonical AoE3 TAD network name as currently
    ///     joined (preferred — direct evidence from Radmin's own log);
    ///   • or, as a fallback when the log can't be read, a ping to a
    ///     known seed peer inside that network answered.
    /// The overlay marks the final checkbox green and auto-closes.
    /// </summary>
    InAoE3Network = 3,
}

/// <summary>
/// Higher-level wrapper on top of <see cref="RadminVpnService"/> that
/// the Multiplayer assistant overlay binds to. Adds:
///
///   • A 4-stage classification (<see cref="RadminStage"/>) instead
///     of the simpler boolean-pair RadminStatus exposes — the
///     overlay's checklist needs ordinal stages to know which step
///     to highlight as "in progress" vs "complete".
///   • An async <see cref="ProbeAsync"/> API that resolves network
///     membership by parsing Radmin's <c>service.log</c> via
///     <see cref="RadminLogService"/>, with a seed-peer ping as a
///     fallback when the log isn't accessible. The log is the
///     authoritative signal: it records the user's actual join/
///     leave events verbatim.
///   • One place to extend later with backend-supplied peer lists,
///     other network-membership probes, etc.
///
/// Stateless — every call hits the registry + NIC list + (if
/// LoggedIn) the log tail and/or seed-ping. Safe to call from a
/// 3-second timer; per-call cost when LoggedIn is ~5-10 ms for the
/// log read (or up to ~600 ms when it falls back to ping).
/// </summary>
public static class RadminAssistantService
{
    /// <summary>
    /// Fallback seed peers in the AoE3 TAD Radmin network. Only used
    /// when <see cref="RadminLogService.GetActiveNetworkMemberships"/>
    /// returns null — i.e. the service log is missing, locked
    /// exclusively, or otherwise unreadable. When the log IS readable
    /// it's strictly more accurate (direct membership evidence vs.
    /// "some peer answered") so the ping never runs.
    ///
    /// Probed in parallel; if ANY peer responds, the user is treated
    /// as confirmed inside the network. Multi-peer lists tolerate any
    /// one regular going offline without flipping the assistant for
    /// everyone else.
    ///
    /// Add new IPs here as you collect them from regulars who are
    /// almost always online.
    /// </summary>
    private static readonly string[] SeedPeerIps = new[]
    {
        "26.120.13.21",   // Alucard
    };

    /// <summary>
    /// Per-ping timeout, in ms. Short enough that a fully-failed
    /// probe doesn't visibly stall the 3 s polling loop (a Radmin
    /// IP that's offline always times out — Radmin peers don't
    /// answer ICMP unless they're online in the same network as
    /// you). 600 ms is a sweet spot: enough margin for high-latency
    /// connections (mobile, cross-continent) but short enough that
    /// the user notices the overlay reacting promptly.
    /// </summary>
    private const int SeedPingTimeoutMs = 600;

    /// <summary>
    /// Single snapshot of Radmin's stage plus the raw underlying
    /// status. UIs typically read Stage for branching and other
    /// fields for cosmetic display (e.g. show the IP next to the
    /// "Logged in" pill).
    /// </summary>
    public sealed record AssistantSnapshot(
        RadminStage Stage,
        RadminStatus Status);

    /// <summary>
    /// Probe Radmin's state once. Returns the highest stage we can
    /// confidently report:
    ///
    ///   NotInstalled        → registry has no Radmin uninstall entry
    ///   InstalledNotRunning → installed but no 26.x.x.x adapter
    ///   LoggedIn            → 26.x.x.x adapter is up
    ///   InAoE3Network       → service.log says we joined the canonical
    ///                         AoE3 TAD network (or, fallback, a seed-
    ///                         peer ping answered when the log isn't
    ///                         readable)
    ///
    /// Membership detection only runs when we're already at LoggedIn —
    /// no point reading the log or pinging if we don't even have a
    /// Radmin identity. The log read is preferred because it's exact
    /// (it sees the user's actual join/leave events) and tolerant of
    /// firewall or peer-offline failure modes; the ping is the safety
    /// net for the rare case where ProgramData isn't readable.
    ///
    /// Cancellation aborts the ping but never throws — callers
    /// should treat OperationCanceledException-during-ping as
    /// "stay on the previous stage" (the snapshot from
    /// GetStatus is still useful).
    /// </summary>
    public static async Task<AssistantSnapshot> ProbeAsync(CancellationToken ct = default)
    {
        var status = RadminVpnService.GetStatus();
        var baseStage = MapToStage(status);

        // Don't bother with membership detection unless the user has
        // a 26.x identity — without one, no Radmin network can be
        // joined anyway, and reading the log would just confirm
        // historical events that are no longer current.
        if (baseStage != RadminStage.LoggedIn)
            return new AssistantSnapshot(baseStage, status);

        // Preferred signal: Radmin's service.log records the user's
        // actual "You joined gaming network 'X'" / "You left network
        // 'X'" events verbatim. No dependence on seed peers, no ICMP,
        // no false negatives from a peer going offline.
        var joined = RadminLogService.GetActiveNetworkMemberships();
        if (joined != null)
        {
            bool inAoE3 = false;
            foreach (var name in joined)
            {
                if (string.Equals(name, RadminVpnService.AoE3TadNetworkName, StringComparison.Ordinal))
                {
                    inAoE3 = true;
                    break;
                }
            }
            var stage = inAoE3 ? RadminStage.InAoE3Network : RadminStage.LoggedIn;
            return new AssistantSnapshot(stage, status);
        }

        // Fallback: log unreadable (deleted, ACL'd, sandboxed account).
        // Ping a known seed peer — less precise, but the only signal
        // available when we can't see the log. Behaves identically to
        // the pre-log probe.
        if (SeedPeerIps.Length == 0)
            return new AssistantSnapshot(baseStage, status);

        bool seedAnswered = await PingAnySeedAsync(ct).ConfigureAwait(false);
        var fallbackStage = seedAnswered ? RadminStage.InAoE3Network : RadminStage.LoggedIn;
        return new AssistantSnapshot(fallbackStage, status);
    }

    /// <summary>
    /// Translate the boolean-pair <see cref="RadminStatus"/> into the
    /// ordinal <see cref="RadminStage"/> the overlay binds to. Kept
    /// as a pure function so the seed-peer ping (above) can post-
    /// process the result and bump LoggedIn → InAoE3Network without
    /// touching this mapping.
    /// </summary>
    private static RadminStage MapToStage(RadminStatus status)
    {
        if (status.InstallState == RadminInstallState.NotInstalled)
            return RadminStage.NotInstalled;
        if (!status.IsServiceRunning)
            return RadminStage.InstalledNotRunning;
        return RadminStage.LoggedIn;
    }

    /// <summary>
    /// Ping every IP in <see cref="SeedPeerIps"/> in parallel and
    /// return true if ANY answers within the timeout. Defensive
    /// against single-peer outages — as long as one regular is
    /// online in the AoE3 TAD network, the assistant correctly
    /// confirms membership.
    ///
    /// Each ping uses its own <see cref="Ping"/> instance because
    /// the class isn't thread-safe (concurrent calls on the same
    /// instance throw <c>InvalidOperationException</c>). Cheap —
    /// Ping construction is allocation-only, no native handle until
    /// SendPingAsync runs.
    ///
    /// Cancellation is honoured: the moment the caller's token
    /// trips we stop awaiting and return false. Mid-flight pings
    /// fire-and-forget until the underlying socket times out, but
    /// that's harmless background work.
    /// </summary>
    private static async Task<bool> PingAnySeedAsync(CancellationToken ct)
    {
        try
        {
            var probes = SeedPeerIps.Select(async ip =>
            {
                try
                {
                    using var ping = new Ping();
                    // Ping.SendPingAsync has no native cancellation
                    // token overload on .NET 8 — we honour the token
                    // by checking before/after instead. Worst case
                    // we spin one extra ping per cancellation.
                    if (ct.IsCancellationRequested) return false;
                    var reply = await ping.SendPingAsync(ip, SeedPingTimeoutMs).ConfigureAwait(false);
                    return reply.Status == IPStatus.Success;
                }
                catch
                {
                    // PingException (host unreachable), socket errors,
                    // anything else — treat as "did not answer". A
                    // failing ping shouldn't crash the polling loop.
                    return false;
                }
            }).ToArray();

            var results = await Task.WhenAll(probes).ConfigureAwait(false);
            return results.Any(ok => ok);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"RadminAssistantService.PingAnySeedAsync: {ex.Message}");
            return false;
        }
    }
}
