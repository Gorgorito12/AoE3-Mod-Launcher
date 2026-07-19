using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Identifies WHICH release of a <c>GitHubReleases</c> mod is actually on disk,
/// by CRC-matching a few local files against the release zips' central
/// directories (read remotely via <see cref="RemoteZipIndex"/>, no download).
///
/// Why this exists: a GitHubReleases mod's version is just a string the launcher
/// stamps when IT installs. A mod detected on disk, updated by hand, or migrated
/// to another repo ends up with an empty or bogus version, and the launcher can't
/// compare anything — so it can never offer an update. WoL doesn't have this
/// problem because UpdateInfo.xml publishes per-version MD5s; this is the
/// equivalent for GitHubReleases, and it needs nothing from the modder.
///
/// Best-effort by construction: returns null whenever it can't be confident, and
/// the caller keeps its previous behaviour.
/// </summary>
public static class ModVersionFingerprint
{
    /// <summary>A release we can try to match against.</summary>
    public sealed record Candidate(string Tag, string AssetUrl, long AssetSize);

    /// <summary>
    /// Newest releases to index. Each costs ~250 KB of range reads, so keep it
    /// small — a user is realistically within a few versions of current.
    /// </summary>
    public const int MaxCandidates = 4;

    /// <summary>How many discriminating files to CRC locally.</summary>
    private const int MaxProbeFiles = 12;

    /// <summary>
    /// Cap on bytes hashed locally. The discriminating files are taken smallest
    /// first (in the real Improvement Mod case the best one is 10 KB), so this
    /// ceiling is rarely approached — it exists so a pathological payload can't
    /// turn a version check into a multi-GB read.
    /// </summary>
    private const long MaxProbeBytes = 25L * 1024 * 1024;

    /// <summary>
    /// Fraction of probed files that must agree for an identification to count.
    /// </summary>
    private const double MinConfidence = 0.60;

    /// <summary>
    /// Try to name the installed release. Returns null when it can't tell —
    /// "unknown" is always preferable to a wrong version.
    /// </summary>
    public static async Task<string?> IdentifyAsync(
        string installPath, IReadOnlyList<Candidate> candidates, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath)) return null;
        if (candidates == null || candidates.Count < 1) return null;

        var indexes = new Dictionary<string, Dictionary<string, RemoteZipIndex.ZipEntryInfo>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var c in candidates.Take(MaxCandidates))
        {
            ct.ThrowIfCancellationRequested();
            var idx = await RemoteZipIndex.TryReadAsync(c.AssetUrl, c.AssetSize, ct);
            if (idx != null) indexes[c.Tag] = idx;
        }
        if (indexes.Count == 0) return null;

        // With a single indexable release there is nothing to DISCRIMINATE
        // against, so a "match" would only prove the files came from some
        // version of this mod — not which one. Refuse rather than guess.
        if (indexes.Count == 1) return null;

        var probes = SelectDiscriminating(indexes);
        if (probes.Count == 0) return null;

        var hits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in indexes.Keys) hits[tag] = 0;

        int probed = 0;
        foreach (var rel in probes)
        {
            ct.ThrowIfCancellationRequested();
            var full = Path.Combine(installPath, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full)) continue;

            var crcHex = await HashService.ComputeCrc32Async(full, ct);
            if (string.IsNullOrEmpty(crcHex)) continue;
            if (!uint.TryParse(crcHex, System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out var crc))
                continue;

            probed++;
            foreach (var (tag, idx) in indexes)
                if (idx.TryGetValue(rel, out var e) && e.Crc32 == crc) hits[tag]++;
        }

        var winner = Decide(hits, probed);
        DiagnosticLog.Write(
            $"ModVersionFingerprint: probed {probed} file(s) across {indexes.Count} release(s) " +
            $"→ {(winner ?? "no confident match")}.");
        return winner;
    }

    /// <summary>
    /// Pick the files worth hashing: present in at least two indexed releases
    /// with DIFFERING CRCs (a file identical everywhere proves nothing), taken
    /// smallest-first and bounded by <see cref="MaxProbeBytes"/>.
    /// </summary>
    internal static List<string> SelectDiscriminating(
        Dictionary<string, Dictionary<string, RemoteZipIndex.ZipEntryInfo>> indexes)
    {
        var names = new Dictionary<string, (long Size, uint First, bool Differs, int Seen)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var idx in indexes.Values)
        {
            foreach (var (name, info) in idx)
            {
                if (names.TryGetValue(name, out var acc))
                {
                    names[name] = (Math.Min(acc.Size, info.Size), acc.First,
                        acc.Differs || info.Crc32 != acc.First, acc.Seen + 1);
                }
                else
                {
                    names[name] = (info.Size, info.Crc32, false, 1);
                }
            }
        }

        var picked = new List<string>();
        long budget = MaxProbeBytes;
        foreach (var (name, acc) in names
                     .Where(k => k.Value.Seen >= 2 && k.Value.Differs && k.Value.Size > 0)
                     .OrderBy(k => k.Value.Size))
        {
            if (picked.Count >= MaxProbeFiles) break;
            if (acc.Size > budget) continue;
            picked.Add(name);
            budget -= acc.Size;
        }
        return picked;
    }

    /// <summary>
    /// Turn per-release hit counts into a verdict. Requires a clear winner:
    /// at least <see cref="MinConfidence"/> of the probed files AND strictly
    /// more than the runner-up — two near-identical releases must yield
    /// "unknown", never a coin flip.
    /// </summary>
    internal static string? Decide(IReadOnlyDictionary<string, int> hits, int probed)
    {
        if (hits == null || hits.Count == 0 || probed <= 0) return null;

        var ranked = hits.OrderByDescending(k => k.Value).ToList();
        var best = ranked[0];
        if (best.Value <= 0) return null;
        if (best.Value < probed * MinConfidence) return null;
        if (ranked.Count > 1 && ranked[1].Value >= best.Value) return null;

        return best.Key;
    }
}
