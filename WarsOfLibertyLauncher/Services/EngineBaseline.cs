using System.IO;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Keeps <see cref="InstallManifest.EngineFileHashes"/> a record of the engine AS IT WAS LAID, not
/// of whatever is on disk at the end of the latest operation.
///
/// <para><b>The bug it closes.</b> Every manifest writer recomputed the engine map from the live
/// files: a repair, a WoL patch, an addon apply. So a damaged <c>RockallDLL.dll</c> that "Verify
/// files" had just reported was fingerprinted AS DAMAGED by the next repair — which does not touch
/// engine files — and from then on verified as healthy. The one signal that the mod's copy of the
/// game was broken was erased by the tool the player used to fix it.</para>
///
/// <para><b>The rule, per engine key.</b> Overlay-owned now → dropped (a file is never in both
/// maps). Written by this operation → the fresh fingerprint (the previous one is kept only when the
/// file exists but could not be read). Not written, previous fingerprint known → kept, unless this
/// operation removed the file. No previous fingerprint → the fresh one, which is how a legacy
/// manifest gains its first baseline.</para>
///
/// <para><b>Only where the launcher owns the engine bytes</b> (<see cref="Applies"/>): an
/// IsolatedFolder clone. An in-place overlay sits in the player's own AoE3, which Steam or GOG may
/// legitimately update underneath it, so it keeps the recompute.</para>
/// </summary>
internal static class EngineBaseline
{
    internal static bool Applies(ModProfile profile) => profile.InstallType == ModInstallType.IsolatedFolder;

    internal static Dictionary<string, FileFingerprint> Merge(
        IReadOnlyDictionary<string, FileFingerprint>? previous,
        IReadOnlyDictionary<string, FileFingerprint> recomputed,
        IEnumerable<string> touched,
        IEnumerable<string> overlayKeysNow,
        Func<string, bool> existsNow,
        Func<string, bool>? existedBefore = null)
    {
        var overlay = new HashSet<string>(overlayKeysNow.Select(Normalize), StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, FileFingerprint>(StringComparer.OrdinalIgnoreCase);

        if (previous == null || previous.Count == 0)
        {
            foreach (var (k, v) in recomputed)
                if (!overlay.Contains(Normalize(k))) result[Normalize(k)] = v;
            return result;
        }

        var touchedSet = new HashSet<string>(touched.Select(Normalize), StringComparer.OrdinalIgnoreCase);
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var k in VerifyService.EngineCandidates) keys.Add(Normalize(k));
        foreach (var k in previous.Keys) keys.Add(Normalize(k));
        foreach (var k in recomputed.Keys) keys.Add(Normalize(k));

        foreach (var key in keys)
        {
            if (overlay.Contains(key)) continue;
            var prev = Lookup(previous, key);
            var now = Lookup(recomputed, key);

            if (touchedSet.Contains(key))
            {
                if (now != null) result[key] = now;
                else if (prev != null && existsNow(key)) result[key] = prev;
                continue;
            }
            if (prev != null)
            {
                if (existedBefore != null && existedBefore(key) && !existsNow(key)) continue;
                result[key] = prev;
                continue;
            }
            if (now != null) result[key] = now;
        }
        return result;
    }

    /// <summary>Which engine candidates exist under <paramref name="installPath"/> right now.</summary>
    internal static HashSet<string> SnapshotExisting(string installPath)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rel in VerifyService.EngineCandidates)
            if (File.Exists(LivePath(installPath, rel))) set.Add(rel);
        return set;
    }

    internal static Func<string, bool> ExistsUnder(string installPath)
        => rel => File.Exists(LivePath(installPath, rel));

    private static string LivePath(string installPath, string rel)
        => Path.Combine(installPath, rel.Replace('/', Path.DirectorySeparatorChar));

    private static string Normalize(string rel) => rel.Replace('\\', '/');

    private static FileFingerprint? Lookup(IReadOnlyDictionary<string, FileFingerprint> map, string key)
    {
        if (map.TryGetValue(key, out var v)) return v;
        foreach (var (k, value) in map)
            if (string.Equals(Normalize(k), key, StringComparison.OrdinalIgnoreCase)) return value;
        return null;
    }
}
