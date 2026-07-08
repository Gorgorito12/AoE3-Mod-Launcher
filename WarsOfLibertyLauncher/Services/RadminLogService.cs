using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Current Radmin VPN power-toggle state, derived from the log's
/// <c>Switched On</c>/<c>Switched Off</c>/<c>Connected to server</c>
/// events. <see cref="Unknown"/> means no readable/decisive log — the
/// caller should NOT treat it as "off" (fall back to its other signals).
/// </summary>
public enum RadminPowerState
{
    Unknown,
    On,
    Off,
}

/// <summary>
/// Reads Radmin VPN's <c>service.log</c> (plus any rotated backups) to
/// determine which networks the user is currently joined to.
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
/// -- Log rotation handling -----------------------------------------
///
/// Radmin VPN rotates <c>service.log</c> when it grows past ~1 MB:
/// the existing file is renamed to <c>service (1).log</c> (and an
/// older <c>service (1).log</c> shifts to <c>service (2).log</c>, etc.)
/// and a fresh empty <c>service.log</c> is started. The rotation
/// happens silently and the current file may contain ZERO join/leave
/// events even though the user is still in a network — because the
/// "You joined X" event lives in a rotated backup, not the live log.
/// Our first implementation only read the current file and incorrectly
/// reported the user as "not in any network" right after a rotation.
///
/// Fix: enumerate every <c>service.log</c> + <c>service (N).log</c>
/// in the directory, sort by <see cref="FileInfo.LastWriteTimeUtc"/>
/// ascending (oldest rotation first, current file last), and parse
/// them all in chronological order into the same latest-event dict.
/// Join/leave events from newer files naturally overwrite older ones
/// in the dict, so the final state matches what Radmin actually sees.
///
/// -- Cost --------------------------------------------------------
///
/// At typical multiplayer-active rates the current log grows ~250 KB/
/// day and Radmin keeps maybe 2-3 rotated backups around (~1 MB each),
/// so we scan ~4 MB total per call — sub-100 ms on any SSD. See
/// <see cref="MaxBytesScannedPerFile"/> for the per-file cap that
/// bounds pathologically huge logs.
///
/// Stateless: every call re-reads the files. Cheap enough for the
/// 3-second polling loop the assistant uses; the polling pauses
/// entirely when the user navigates away from the Multiplayer tab,
/// so there's no constant disk pressure.
/// </summary>
public static class RadminLogService
{
    /// <summary>
    /// Canonical directory of Radmin's logs. Famatech's installer pins
    /// this to <c>%PROGRAMDATA%\Famatech\Radmin VPN\</c> regardless of
    /// the install path the user picks for the binaries. We resolve
    /// <c>%PROGRAMDATA%</c> via
    /// <see cref="Environment.SpecialFolder.CommonApplicationData"/> so
    /// the rare enterprise machine with a relocated ProgramData still
    /// finds it.
    /// </summary>
    private static string LogDir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Famatech",
            "Radmin VPN");

    /// <summary>
    /// Hard cap on bytes scanned per individual log file. With rotation
    /// handling we may end up reading several files in one call, but
    /// each one independently is capped so a single corrupted/giant
    /// file can't stall the UI thread.
    ///
    /// 32 MB caps at ~200 ms on a slow HDD; the cost of being wrong
    /// (a years-old join event we miss in one rotated file) is small
    /// because newer rotations / the current log usually have a
    /// "you joined / you left" event that overwrites the missed one
    /// anyway. If everything falls through (pathologically rotated
    /// logs with no recent events), the caller's seed-peer ping
    /// fallback still works.
    /// </summary>
    private const long MaxBytesScannedPerFile = 32L * 1024 * 1024;

    /// <summary>
    /// Matches the current and rotated log filenames Famatech uses:
    /// <c>service.log</c> (current) and <c>service (N).log</c> (rotated
    /// backups, N=1,2,3…). The <c>^</c>/<c>$</c> anchors guarantee we
    /// don't accidentally pick up unrelated files like
    /// <c>service-debug.log</c> or <c>RadminVpn_setupapi_…log</c>.
    /// </summary>
    private static readonly Regex LogFilePattern = new(
        @"^service(?: \(\d+\))?\.log$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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
    /// or <c>null</c> when no readable log file exists. Caller treats
    /// <c>null</c> as "no signal" and falls back to whatever it had
    /// before this service existed (seed-peer ping, manual verification).
    ///
    /// Possible reasons for <c>null</c>:
    ///   • log directory doesn't exist (Radmin uninstalled);
    ///   • directory exists but no <c>service*.log</c> file is in it
    ///     (fresh install hasn't written its first event yet);
    ///   • permission denied (corrupted ACLs, sandboxed account);
    ///   • generic IO error.
    ///
    /// Empty list is a valid non-null result: it means we read at
    /// least one log successfully and the user genuinely isn't in any
    /// network — distinct from "we don't know" so the UI can render
    /// "signed in, no network joined" differently from "log missing".
    /// </summary>
    public static IReadOnlyList<string>? GetActiveNetworkMemberships()
    {
        try
        {
            if (!Directory.Exists(LogDir)) return null;

            // Find every service.log / service (N).log, sort oldest-
            // first by last-write time. The "oldest" is the most-
            // rotated backup (the one Radmin wrote first), and the
            // "newest" is the live service.log Radmin is appending to
            // right now. Processing in that order means later events
            // overwrite earlier ones in the dict and the final state
            // matches what Radmin currently believes.
            var files = Directory.EnumerateFiles(LogDir, "service*.log")
                .Select(p => new FileInfo(p))
                .Where(f => LogFilePattern.IsMatch(f.Name))
                .OrderBy(f => f.LastWriteTimeUtc)
                .ToList();

            if (files.Count == 0) return null;

            // Single accumulator shared across all rotated logs.
            // Ordinal comparison because Radmin emits names byte-for-
            // byte as the user typed them — case-folding would mash
            // "Age of Empires III" and "AGE OF EMPIRES III" together,
            // which are technically distinct Famatech networks.
            var latest = new Dictionary<string, bool>(StringComparer.Ordinal);

            bool anyReadOk = false;
            foreach (var f in files)
            {
                if (ScanOneLog(f.FullName, latest))
                    anyReadOk = true;
            }

            // If we couldn't open ANY of the discovered files (all
            // locked, ACL'd, etc.), treat as "no signal" so the caller
            // falls back to its ping. Empty-but-readable is fine.
            if (!anyReadOk) return null;

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

    /// <summary>
    /// Scan a single log file forward, merging every recognised join/
    /// leave event into the shared <paramref name="latest"/> dict.
    /// Returns true on a successful open + read (regardless of whether
    /// any events were actually found — empty files are fine), false
    /// when the file couldn't be opened at all.
    /// </summary>
    private static bool ScanOneLog(string path, Dictionary<string, bool> latest)
    {
        try
        {
            // FileShare.ReadWrite|Delete is mandatory: the Radmin VPN
            // Control Service keeps the current log open with an
            // exclusive write lock at runtime. Without read-share,
            // Windows refuses to open the file even though we only
            // want to read it. Adding Delete tolerates log rotation
            // racing with us — if Radmin renames the file while we're
            // mid-read, the rename succeeds and our handle keeps
            // serving the old contents until we Dispose.
            using var fs = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            // Default: scan from byte 0 to EOF. Only seek into the
            // middle of the file when the log is bigger than our hard
            // per-file cap — in which case we accept losing the very
            // oldest events (a tradeoff documented on
            // MaxBytesScannedPerFile) and drop the first partial line
            // so the regex never matches a fragment.
            long start = 0;
            if (fs.Length > MaxBytesScannedPerFile)
            {
                start = fs.Length - MaxBytesScannedPerFile;
                fs.Seek(start, SeekOrigin.Begin);
            }

            using var reader = new StreamReader(fs, Encoding.UTF8);
            if (start > 0) reader.ReadLine();

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
            return true;
        }
        catch (Exception ex)
        {
            // Best-effort: one corrupted / locked rotated file shouldn't
            // poison the whole scan. Log it and move on so the other
            // files still contribute their events to the dict.
            DiagnosticLog.Write($"RadminLogService.ScanOneLog '{path}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Current Radmin VPN power state, read from the log's toggle events.
    /// This is the ONLY reliable "is the VPN actually connected" signal:
    /// the virtual adapter stays Up with its static 26.x identity IP even
    /// when Radmin is powered off ("Desconectado"), and the network
    /// membership parser (<see cref="GetActiveNetworkMemberships"/>) goes
    /// stale after a power-off because Radmin emits <c>Switched Off</c>
    /// but NOT a <c>You left network</c> line. The toggle IS logged, so we
    /// read it directly — local + deterministic, unlike an ICMP seed ping
    /// (which false-negatives whenever no peer happens to be online).
    ///
    /// Reads the newest few <c>service*.log</c> files (the toggle event is
    /// always near the tail of the live/most-recent log) and returns the
    /// first decisive result. <see cref="RadminPowerState.Unknown"/> when
    /// no readable/decisive log exists — callers must NOT treat that as
    /// "off".
    /// </summary>
    public static RadminPowerState GetPowerState()
    {
        try
        {
            if (!Directory.Exists(LogDir)) return RadminPowerState.Unknown;

            // Newest-first: the current power state lives in the most
            // recently written log. We only need a handful — right after a
            // rotation the live file can be tiny, so fall through to the
            // previous one(s), but never scan the whole rotated history.
            var files = Directory.EnumerateFiles(LogDir, "service*.log")
                .Select(p => new FileInfo(p))
                .Where(f => LogFilePattern.IsMatch(f.Name))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(3);

            foreach (var f in files)
            {
                var lines = ReadLogLines(f.FullName);
                if (lines == null) continue; // unreadable file — try the next
                var state = DeterminePowerState(lines);
                if (state != RadminPowerState.Unknown) return state;
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"RadminLogService.GetPowerState: {ex.Message}");
        }
        return RadminPowerState.Unknown;
    }

    /// <summary>
    /// Pure classifier (unit-tested): scan lines newest→oldest and return
    /// the first decisive power event. <c>Switched Off</c> ⇒ Off;
    /// <c>Switched On</c> or <c>Connected to server</c> ⇒ On. Everything
    /// else is ignored — crucially the transient <c>Disconnected from
    /// server</c> (a network blip that's always followed by a reconnect or
    /// a real <c>Switched Off</c>), and the per-peer <c>Connected to
    /// &lt;id&gt;/'name'</c> lines (which never contain the literal
    /// "Connected to server"). No decisive line ⇒ Unknown.
    /// </summary>
    internal static RadminPowerState DeterminePowerState(IReadOnlyList<string> lines)
    {
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            var l = lines[i];
            if (l.Contains("Switched Off", StringComparison.Ordinal))
                return RadminPowerState.Off;
            if (l.Contains("Switched On", StringComparison.Ordinal)
                || l.Contains("Connected to server", StringComparison.Ordinal))
                return RadminPowerState.On;
        }
        return RadminPowerState.Unknown;
    }

    /// <summary>
    /// Read one log file's lines with the same share/encoding/size-cap
    /// rules <see cref="ScanOneLog"/> uses (the Radmin service holds the
    /// live log with an exclusive write lock, so read-share + Delete is
    /// mandatory; BOM auto-detect handles Radmin's UTF-16LE files). Returns
    /// null when the file can't be opened at all.
    /// </summary>
    private static IReadOnlyList<string>? ReadLogLines(string path)
    {
        try
        {
            using var fs = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            long start = 0;
            if (fs.Length > MaxBytesScannedPerFile)
            {
                start = fs.Length - MaxBytesScannedPerFile;
                fs.Seek(start, SeekOrigin.Begin);
            }

            using var reader = new StreamReader(fs, Encoding.UTF8);
            if (start > 0) reader.ReadLine(); // drop the partial first line

            var lines = new List<string>();
            string? line;
            while ((line = reader.ReadLine()) != null)
                lines.Add(line);
            return lines;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"RadminLogService.ReadLogLines '{path}': {ex.Message}");
            return null;
        }
    }
}
