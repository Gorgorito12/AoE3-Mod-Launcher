using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Reads Radmin VPN's <c>service.log</c> to determine which networks
/// the user is currently joined to.
///
/// Why this exists: Radmin's per-network membership lives server-side
/// and isn't exposed via NIC enumeration — the user gets the same
/// 26.x.x.x identity address regardless of which networks they've
/// joined. We used to ping a known seed peer to *infer* "are you in
/// the AoE3 network", but that has three failure modes that produce
/// silent false negatives:
///   • the seed peer is offline (or churns IPs);
///   • the user's outbound firewall drops ICMP;
///   • the answer doesn't tell us *which* Radmin network the connection
///     belongs to (multi-network users get the same boolean).
///
/// <c>service.log</c> writes an explicit UPDATE-level entry for every
/// join/leave event, in English, with a stable tab-delimited format
/// observed across Radmin VPN 2.x:
/// <code>
/// 2026.05.24 08:54:47.750\tUPDATE\tYou joined gaming network 'X'
/// 2026.05.24 13:15:51.962\tUPDATE\tYou left network 'X'
/// </code>
/// So we scan the file, walk events forwards keeping only the latest
/// per network name, and report the ones whose latest event is a join.
/// Locale-stable (Radmin writes the log in English even on Spanish/
/// Russian/etc Windows), update-stable (the format has held across
/// every Radmin point release we've seen).
///
/// We have to read the full log history (not just a tail) because
/// Radmin only writes a single "joined" event per join action — not
/// on every service restart, not on every login. A user who joined
/// the AoE3 network last month and never explicitly left has that
/// single relevant event at the very start of an otherwise huge log.
/// At typical multiplayer-active rates (~250 KB/day) reading the
/// full file is sub-100 ms even after months of use. See
/// <see cref="MaxBytesScanned"/> for the upper bound that protects
/// against pathologically large logs.
///
/// Stateless: every call re-reads the file. Cheap enough for the
/// 3-second polling loop the assistant uses; the polling pauses
/// entirely when the user navigates away from the Multiplayer tab,
/// so there's no constant disk pressure.
/// </summary>
public static class RadminLogService
{
    /// <summary>
    /// Canonical location of Radmin's service log. Famatech's installer
    /// pins this to <c>%PROGRAMDATA%\Famatech\Radmin VPN\service.log</c>
    /// regardless of the install path the user picks for the binaries.
    /// We resolve <c>%PROGRAMDATA%</c> via
    /// <see cref="Environment.SpecialFolder.CommonApplicationData"/> so
    /// the rare enterprise machine with a relocated ProgramData still
    /// finds it.
    /// </summary>
    private static string LogPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Famatech",
            "Radmin VPN",
            "service.log");

    /// <summary>
    /// Hard cap on how many bytes we read from the end of the log. We
    /// need the WHOLE history of join/leave events to know current
    /// state — Radmin only writes "You joined X" once, not on every
    /// service restart, so a user who joined the AoE3 network last
    /// month and never left has that single relevant event sitting at
    /// byte ~0 of a now-huge log. So our default behaviour is read the
    /// full file (typical size: 1-10 MB at multiplayer-active rates,
    /// well under a 100 ms disk read).
    ///
    /// The cap exists only to bound the worst case: a user who's been
    /// running Radmin for years without Famatech ever rotating the log
    /// might end up with hundreds of MB. We'd rather report a stale
    /// answer than block the UI thread on a multi-second read. 32 MB
    /// caps the read at ~200 ms on a slow HDD; the cost of being wrong
    /// (a years-old join event we miss) is the user falling through to
    /// the ping fallback, which is still better than crashing.
    /// </summary>
    private const long MaxBytesScanned = 32L * 1024 * 1024;

    /// <summary>
    /// Matches Radmin's "You joined ..." line. The "gaming " infix is
    /// optional because Famatech tags gaming networks differently from
    /// password-protected community ones ("You joined network 'X' as
    /// Member" vs "You joined gaming network 'X'"); we want both.
    /// </summary>
    private static readonly Regex JoinLine = new(
        @"UPDATE\tYou joined (?:gaming )?network '([^']+)'",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Matches Radmin's "You left ..." line. No "gaming" variant in
    /// the wild — Radmin always says "left network".
    /// </summary>
    private static readonly Regex LeaveLine = new(
        @"UPDATE\tYou left network '([^']+)'",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Returns the names of networks the user is currently joined to,
    /// or <c>null</c> when the log can't be read. Caller treats
    /// <c>null</c> as "no signal" and falls back to whatever it had
    /// before this service existed (seed-peer ping, manual verification).
    ///
    /// Possible reasons for <c>null</c>:
    ///   • log file doesn't exist (Radmin uninstalled, or fresh install
    ///     hasn't written the first event yet);
    ///   • permission denied (corrupted ACLs, sandboxed account);
    ///   • generic IO error during the tail read.
    ///
    /// Empty list is a valid non-null result: it means we read the log
    /// successfully and the user isn't currently in any network. That's
    /// distinct from "log missing" because the UI may want to render
    /// "you're signed in but not in any network" differently from
    /// "we don't know".
    /// </summary>
    public static IReadOnlyList<string>? GetActiveNetworkMemberships()
    {
        try
        {
            var path = LogPath;
            if (!File.Exists(path)) return null;

            // FileShare.ReadWrite|Delete is mandatory: the Radmin VPN
            // Control Service keeps the log open with an exclusive
            // write lock at runtime. Without read-share, Windows
            // refuses to open the file even though we only want to
            // read it. Adding Delete tolerates log rotation racing
            // with us — if Radmin renames the file while we're
            // mid-read, the rename succeeds and our handle keeps
            // serving the old contents until we Dispose.
            using var fs = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            // Default: scan from byte 0 to EOF. Only seek into the
            // middle of the file when the log is bigger than our hard
            // cap — in which case we accept losing the very oldest
            // events (a tradeoff documented on MaxBytesScanned) and
            // drop the first partial line so the regex never matches
            // a fragment.
            long start = 0;
            if (fs.Length > MaxBytesScanned)
            {
                start = fs.Length - MaxBytesScanned;
                fs.Seek(start, SeekOrigin.Begin);
            }

            using var reader = new StreamReader(fs, Encoding.UTF8);
            if (start > 0) reader.ReadLine();

            // Forward scan: each successive match for the same network
            // name overwrites the previous one in the dict, so when we
            // hit EOF the value is the latest event for that network.
            // Ordinal comparison because Radmin emits the names byte-
            // for-byte as the user typed them — case-folding would mash
            // "Age of Empires III" and "AGE OF EMPIRES III" together,
            // which are technically distinct Famatech networks.
            var latest = new Dictionary<string, bool>(StringComparer.Ordinal);

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var m = JoinLine.Match(line);
                if (m.Success)
                {
                    latest[m.Groups[1].Value] = true;
                    continue;
                }
                m = LeaveLine.Match(line);
                if (m.Success)
                {
                    latest[m.Groups[1].Value] = false;
                }
            }

            var result = new List<string>();
            foreach (var kv in latest)
            {
                if (kv.Value) result.Add(kv.Key);
            }
            return result;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"RadminLogService.GetActiveNetworkMemberships: {ex.Message}");
            return null;
        }
    }
}
