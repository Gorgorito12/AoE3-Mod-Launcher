using System;
using System.Collections.Generic;
using System.Linq;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Works out the CHEAPEST way to get an install from whatever version it has to the version it
/// wants, given every release a mod has published and what each one carries.
///
/// <para>This is what lets a modder stop re-uploading the whole mod on every release. The full
/// <c>.zip</c> goes up once (a "baseline"); later releases carry only small patches, and the
/// launcher works out the route: one cumulative patch from the baseline, one incremental patch
/// from the previous version, a short chain of them, or — when that is cheaper or safer — the
/// full download it would have done anyway.</para>
///
/// <para><b>Pure: no network, no disk, no clock.</b> Its whole input is a snapshot of the ONE
/// <c>GET /repos/{repo}/releases?per_page=100</c> the launcher already makes, which returns every
/// release WITH its assets, their sizes and their URLs. That is what makes this affordable — the
/// entire patch graph and every cost is known for one request against a 60/hour budget.</para>
///
/// <para><b>It is <c>internal</c> so it can be unit-tested.</b> Its opposite number in the WoL
/// pipeline, <c>UpdateService.ComputePendingDownloads</c>, is <c>private</c> and consequently has
/// no test at all despite being the rule that decides what every WoL user downloads.</para>
/// </summary>
internal static class DeltaChainPlanner
{
    // ---------------------------------------------------------------- input

    /// <summary>One asset on a release, as the listing reports it.</summary>
    internal sealed record ReleaseAssetSnapshot(string Name, long Size, string Url);

    /// <summary>One release and everything attached to it.</summary>
    internal sealed record ReleaseSnapshot(
        string Tag, bool Prerelease, IReadOnlyList<ReleaseAssetSnapshot> Assets);

    // ---------------------------------------------------------------- output

    internal enum StepKind
    {
        /// <summary>Download a release's full overlay zip — the start of a route.</summary>
        FullBaseline,
        /// <summary>Apply one patch on top of what is already installed.</summary>
        Patch,
    }

    /// <summary>
    /// One step of a route. A <see cref="StepKind.Patch"/> step carries its host release's whole
    /// asset list so that preparing it later costs <b>no</b> further API call.
    /// </summary>
    internal sealed record PlanStep(
        StepKind Kind,
        string FromTag,
        string ToTag,
        long Bytes,
        string AssetName,
        string AssetUrl,
        string? DescriptorName,
        string? DescriptorUrl,
        IReadOnlyList<ReleaseAssetSnapshot> HostReleaseAssets);

    internal sealed record Plan(IReadOnlyList<PlanStep> Steps, long TotalBytes)
    {
        /// <summary>
        /// True when the route starts from the install we already have, i.e. it is pure patching
        /// and downloads no full overlay. An update takes this path or falls back to the full
        /// re-overlay it would have done anyway.
        /// </summary>
        internal bool StartsFromInstalled
            => Steps.Count == 0 || Steps[0].Kind == StepKind.Patch;

        internal IReadOnlyList<PlanStep> PatchSteps
            => Steps.Where(s => s.Kind == StepKind.Patch).ToList();
    }

    /// <summary>Which releases a route may START from.</summary>
    internal enum BaselinePolicy
    {
        /// <summary>Any release shipping a full zip — a fresh install, which has nothing yet.</summary>
        Any,
        /// <summary>
        /// Only the target's own zip. Used for an update, so the comparison is exactly "a patch
        /// route versus the full download we would do today" and never "let us silently reinstall
        /// the mod from an older release".
        /// </summary>
        TargetOnly,
        /// <summary>No full download at all — patch or nothing.</summary>
        None,
    }

    // ---------------------------------------------------------------- dials

    /// <summary>
    /// What one extra hop costs, expressed in bytes so the whole comparison stays a single
    /// arithmetic rule.
    ///
    /// <para><b>A hop is not free, and counting only download bytes gets this badly wrong.</b>
    /// Every hop runs a complete <c>ApplyGitHubDeltaAsync</c> finalize: the manifest write
    /// enumerates the entire install and re-hashes the key and engine files, the overlay is
    /// re-classified, and the translation snapshot is refreshed. On a multi-gigabyte overlay that
    /// is minutes of disk work per hop even when the patch itself is two megabytes. Without this
    /// penalty the planner happily picks six tiny hops over one slightly larger patch and the
    /// update takes far longer.</para>
    /// </summary>
    internal const long HopPenaltyBytes = 64L * 1024 * 1024;

    /// <summary>
    /// Longest chain worth walking. Kept small for the same reason as
    /// <see cref="HopPenaltyBytes"/>, and it doubles as the termination guard — see
    /// <see cref="Build"/>.
    /// </summary>
    internal const int MaxHops = 4;

    // ---------------------------------------------------------------- edges

    private sealed record Edge(
        string From, string To, long Bytes,
        string PayloadName, string PayloadUrl,
        string DescriptorName, string DescriptorUrl,
        IReadOnlyList<ReleaseAssetSnapshot> HostAssets);

    /// <summary>
    /// Every hop the releases actually offer. Each rejection here is deliberate and has a test:
    /// a name that cannot be resolved to real tags, a descriptor with no payload beside it (its
    /// cost would be unknown), a hop whose target is not the release hosting it (an old release
    /// must not be able to inject an edge into a newer one), a self-loop, and prereleases as
    /// intermediate stops.
    /// </summary>
    private static List<Edge> BuildEdges(
        IReadOnlyList<ReleaseSnapshot> releases, string targetTag)
    {
        var knownTags = releases.Select(r => r.Tag).ToList();
        var edges = new List<Edge>();

        foreach (var release in releases)
        {
            // A prerelease is only ever a destination the user explicitly asked for; it must
            // never be a stop along the way, or a chain that dies midway strands somebody on a
            // build the modder had not published yet.
            if (release.Prerelease
                && !string.Equals(release.Tag, targetTag, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var asset in release.Assets)
            {
                if (!DeltaPatchService.PatchAssetNaming.IsDescriptor(asset.Name)) continue;

                var hop = DeltaPatchService.PatchAssetNaming.ProposeHop(asset.Name, knownTags);
                if (hop == null) continue;
                if (string.Equals(hop.Value.From, hop.Value.To, StringComparison.OrdinalIgnoreCase))
                    continue;

                // The hop must land on the release that hosts it.
                if (!string.Equals(hop.Value.To, release.Tag, StringComparison.OrdinalIgnoreCase))
                    continue;

                var payloadName = DeltaPatchService.PatchAssetNaming.DescriptorToPayloadName(asset.Name);
                var payload = release.Assets.FirstOrDefault(a =>
                    string.Equals(a.Name, payloadName, StringComparison.OrdinalIgnoreCase));
                if (payload == null || payload.Size <= 0) continue;

                edges.Add(new Edge(
                    hop.Value.From, hop.Value.To, payload.Size + Math.Max(asset.Size, 0),
                    payload.Name, payload.Url, asset.Name, asset.Url, release.Assets));
            }
        }

        // Two descriptors describing the same hop: keep the cheaper, then the ordinally smaller
        // name, so the result cannot depend on the order GitHub happened to list assets in.
        return edges
            .GroupBy(e => (e.From.ToLowerInvariant(), e.To.ToLowerInvariant()))
            .Select(g => g.OrderBy(e => e.Bytes)
                          .ThenBy(e => e.DescriptorName, StringComparer.Ordinal)
                          .First())
            .ToList();
    }

    /// <summary>The full overlay zip on a release, or null when it ships none.</summary>
    private static ReleaseAssetSnapshot? FullZipOf(ReleaseSnapshot release)
    {
        var names = release.Assets.Select(a => a.Name).ToList();
        var i = GitHubReleaseDownloader.PickAssetIndex(names, null);
        if (i == null) return null;
        var asset = release.Assets[i.Value];
        return asset.Size > 0 ? asset : null;
    }

    // ---------------------------------------------------------------- the rule

    /// <summary>
    /// Cheapest route from <paramref name="installedTag"/> (null for a fresh install) to
    /// <paramref name="targetTag"/>, or null when there is none and the caller should do whatever
    /// it would have done without patches.
    ///
    /// <para><b>Bounded-hop dynamic programming, not a shortest-path search.</b> GitHub tags have
    /// no total order — nothing here compares or sorts them — so this is a walk over a graph whose
    /// nodes are tags. Indexing the table by hop count makes the hop cap double as the termination
    /// guard: a table indexed by <c>k</c> cannot revisit, so a <c>fromTag</c>/<c>toTag</c> cycle
    /// written by a modder is structurally harmless and needs no visited set.</para>
    ///
    /// <para><b>Ties go to the full download.</b> Only a strictly cheaper patch route wins, because
    /// a full re-overlay also repairs an install that has quietly diverged and a patch does not.
    /// After cost, fewer hops wins, then the ordinally smaller asset name — so shuffling the input
    /// cannot change the answer.</para>
    /// </summary>
    internal static Plan? Build(
        IReadOnlyList<ReleaseSnapshot> releases,
        string? installedTag,
        string targetTag,
        BaselinePolicy baseline,
        int maxHops = MaxHops,
        long hopPenaltyBytes = HopPenaltyBytes)
    {
        if (releases == null || releases.Count == 0) return null;
        if (string.IsNullOrWhiteSpace(targetTag)) return null;
        if (maxHops < 0) return null;

        var target = releases.FirstOrDefault(r =>
            string.Equals(r.Tag, targetTag, StringComparison.OrdinalIgnoreCase));
        if (target == null) return null;

        // Already there: a real, empty plan — not "no route".
        if (!string.IsNullOrWhiteSpace(installedTag)
            && string.Equals(installedTag, targetTag, StringComparison.OrdinalIgnoreCase))
            return new Plan(Array.Empty<PlanStep>(), 0);

        var edges = BuildEdges(releases, targetTag);
        var byFrom = edges.GroupBy(e => e.From, StringComparer.OrdinalIgnoreCase)
                          .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        // A route's start: cost 0 for what we already have, or the price of a full zip.
        var starts = new Dictionary<string, (long Cost, PlanStep? Step)>(StringComparer.OrdinalIgnoreCase);

        bool installedIsKnown = !string.IsNullOrWhiteSpace(installedTag)
            && releases.Any(r => string.Equals(r.Tag, installedTag, StringComparison.OrdinalIgnoreCase));
        if (installedIsKnown) starts[installedTag!] = (0, null);

        if (baseline != BaselinePolicy.None)
        {
            foreach (var r in releases)
            {
                if (baseline == BaselinePolicy.TargetOnly
                    && !string.Equals(r.Tag, targetTag, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (r.Prerelease
                    && !string.Equals(r.Tag, targetTag, StringComparison.OrdinalIgnoreCase))
                    continue;

                var zip = FullZipOf(r);
                if (zip == null) continue;
                if (starts.TryGetValue(r.Tag, out var existing) && existing.Cost <= zip.Size) continue;

                starts[r.Tag] = (zip.Size, new PlanStep(
                    StepKind.FullBaseline, "", r.Tag, zip.Size,
                    zip.Name, zip.Url, null, null, r.Assets));
            }
        }
        if (starts.Count == 0) return null;

        // route[tag] = the best way found so far to be AT tag.
        var best = new Dictionary<string, (long Cost, List<PlanStep> Steps)>(StringComparer.OrdinalIgnoreCase);
        var frontier = new Dictionary<string, (long Cost, List<PlanStep> Steps)>(StringComparer.OrdinalIgnoreCase);

        foreach (var (tag, start) in starts)
        {
            var steps = start.Step == null ? new List<PlanStep>() : new List<PlanStep> { start.Step };
            frontier[tag] = (start.Cost, steps);
            best[tag] = (start.Cost, steps);
        }

        for (int hop = 0; hop < maxHops && frontier.Count > 0; hop++)
        {
            var next = new Dictionary<string, (long Cost, List<PlanStep> Steps)>(StringComparer.OrdinalIgnoreCase);

            foreach (var (fromTag, reached) in frontier)
            {
                if (!byFrom.TryGetValue(fromTag, out var outgoing)) continue;

                foreach (var e in outgoing)
                {
                    var cost = reached.Cost + e.Bytes + hopPenaltyBytes;
                    var steps = new List<PlanStep>(reached.Steps)
                    {
                        new PlanStep(StepKind.Patch, e.From, e.To, e.Bytes,
                            e.PayloadName, e.PayloadUrl, e.DescriptorName, e.DescriptorUrl, e.HostAssets),
                    };

                    if (IsBetter(cost, steps, next)) next[e.To] = (cost, steps);
                    if (IsBetter(cost, steps, best)) best[e.To] = (cost, steps);
                }
            }

            frontier = next;
        }

        if (!best.TryGetValue(targetTag, out var winner)) return null;
        return new Plan(winner.Steps, winner.Cost);

        // Strictly cheaper wins; then fewer hops; then the ordinally smaller first asset name.
        static bool IsBetter(long cost, List<PlanStep> steps,
                             Dictionary<string, (long Cost, List<PlanStep> Steps)> table)
        {
            var tag = steps[^1].ToTag;
            if (!table.TryGetValue(tag, out var cur)) return true;
            if (cost != cur.Cost) return cost < cur.Cost;
            if (steps.Count != cur.Steps.Count) return steps.Count < cur.Steps.Count;
            return string.CompareOrdinal(steps[0].AssetName, cur.Steps[0].AssetName) < 0;
        }
    }
}
