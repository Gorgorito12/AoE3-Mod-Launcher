using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Hash of a single file in a mod install, used to compare with a peer.
/// </summary>
public record ModFileHash(string RelativePath, string Sha256, long SizeBytes);

/// <summary>
/// A reproducible fingerprint of a mod install on disk. Two players whose
/// fingerprints' <see cref="CombinedHash"/> match are guaranteed to have
/// the same bytes for every critical file, which is what AoE3's sync
/// checker cares about — a single byte off in <c>protoy.xml</c> drops the
/// game out-of-sync within seconds.
///
/// The combined hash is what the launcher actually compares before
/// allowing a join; the per-file list exists so the join dialog can show
/// exactly which files differ when the check fails.
///
/// The fingerprint is LOCALIZATION-INVARIANT: files covered by a community
/// translation (e.g. <c>data\stringtabley.xml</c>) are hashed from the canonical
/// English snapshot in <c>translations\_originals\</c>, not the live (possibly
/// translated) file. String tables don't affect the simulation, so a translated
/// and an English install on the same build still match — see the comment in
/// <see cref="ModHashService.FingerprintAsync(Models.ModProfile,string,System.Collections.Generic.IEnumerable{string},System.Threading.CancellationToken)"/>.
/// </summary>
public record ModFingerprint(
    string ModId,
    string InstallRoot,
    IReadOnlyList<ModFileHash> Files,
    string CombinedHash)
{
    /// <summary>True when every probed file existed and contributed a non-empty hash.</summary>
    public bool IsComplete => Files.All(f => !string.IsNullOrEmpty(f.Sha256));
}

/// <summary>
/// Builds a deterministic, peer-comparable hash of a mod install for the
/// v1.0 multiplayer flow.
///
/// Before joining a host's room, the launcher fingerprints the local mod
/// install and sends its <see cref="ModFingerprint.CombinedHash"/> to the
/// matchmaking backend; the backend already has the host's combined hash
/// (computed when the host opened the room) and rejects the join with a
/// "version mismatch" error if they differ. The join dialog can then
/// inspect the per-file list to point the user at the exact file(s) that
/// need updating before falling back to "auto-repair via the existing
/// patcher" or "ask the host to repair".
///
/// Performance: the default file list (3 small XMLs) hashes in a few
/// milliseconds. The service is also used by larger probes (mod manifests
/// with dozens of files), so all I/O is async and a cancellation token
/// short-circuits long scans.
/// </summary>
public static class ModHashService
{
    /// <summary>
    /// Files the existing WoL-style updater already uses to identify the
    /// installed mod version (MD5 in the legacy pipeline; SHA-256 here for
    /// the multiplayer fingerprint). These three files together encode
    /// game logic + civ data + UI strings — a difference in any of them is
    /// the canonical AoE3 out-of-sync trigger, so they're the right
    /// minimum probe for "are we playing the same build?".
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultProbeFiles = new[]
    {
        Path.Combine("data", "protoy.xml"),
        Path.Combine("data", "techtreey.xml"),
        Path.Combine("data", "stringtabley.xml"),
    };

    /// <summary>
    /// The probe files that identify a mod's version for the multiplayer join
    /// check: the profile's own <see cref="ModProfile.MultiplayerProbeFiles"/>
    /// when it declares them, else <see cref="DefaultProbeFiles"/>.
    ///
    /// This lives HERE, not in the caller, so every call site is correct at once
    /// — the one in <c>MainWindow</c> today and any added later. A mod like
    /// Napoleonic Era ships its own data files (<c>data\proton.xml</c>,
    /// <c>data\techtreen.xml</c> — the <c>n</c> suffix) and NONE of the default
    /// <c>y</c> files; without this the fingerprint would hash the base game's
    /// <c>y</c> files, which the AoE3 clone makes identical for every player, so
    /// the room's version check would be INERT and two versions could share a
    /// match and desync.
    /// </summary>
    public static IReadOnlyList<string> ProbeFilesFor(ModProfile profile)
    {
        var declared = profile?.MultiplayerProbeFiles;
        return declared is { Count: > 0 } ? declared : DefaultProbeFiles;
    }

    /// <summary>
    /// Fingerprint a mod install using the probe files the profile resolves to
    /// (its own, or the defaults). Convenience overload for the join check.
    /// </summary>
    public static Task<ModFingerprint> FingerprintAsync(
        ModProfile profile,
        string installRoot,
        CancellationToken ct = default)
        => FingerprintAsync(profile, installRoot, ProbeFilesFor(profile), ct);

    /// <summary>
    /// Fingerprint a mod install over an explicit set of relative paths.
    /// Missing files contribute an empty-hash entry instead of throwing,
    /// so the caller can decide whether to treat the install as broken or
    /// just incomplete.
    /// </summary>
    public static async Task<ModFingerprint> FingerprintAsync(
        ModProfile profile,
        string installRoot,
        IEnumerable<string> relativePaths,
        CancellationToken ct = default)
    {
        if (profile == null) throw new ArgumentNullException(nameof(profile));
        if (string.IsNullOrEmpty(installRoot)) throw new ArgumentException("Install root required.", nameof(installRoot));

        // Deduplicate and normalise so two identical lists in different
        // order always produce the same combined hash.
        var normalised = (relativePaths ?? Array.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(NormaliseRelative)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Localization must NOT change the fingerprint. Applying a community
        // translation overwrites a covered file (e.g. data\stringtabley.xml), which
        // is one of the probed files — so two players on the same build but
        // different languages would otherwise produce different CombinedHashes and
        // be wrongly rejected at the lobby gate. String tables don't affect the
        // simulation, so that's a FALSE mismatch. We hash the canonical English
        // snapshot (translations\_originals\) for covered files instead of the live
        // file, exactly like UpdateService.DetectCurrentVersionAsync does for
        // version detection. No-op for English (snapshot == live); protoy/techtree
        // have no snapshot so they keep hashing the live file (a real OOS still
        // mismatches); falls back to the live file when no snapshot exists; and host
        // and joiner compute it the same way, so the comparison stays symmetric.
        var translations = new TranslationService(installRoot, profile.Translations?.CoveredFiles);

        var results = new List<ModFileHash>(normalised.Count);
        foreach (var rel in normalised)
        {
            ct.ThrowIfCancellationRequested();
            var absolute = translations.ResolveHashableFile(rel);
            var (sha, size) = await HashFileAsync(absolute, ct);
            results.Add(new ModFileHash(rel, sha, size));
        }

        var combined = ComputeCombinedHash(profile.Id, results);
        return new ModFingerprint(profile.Id, installRoot, results, combined);
    }

    /// <summary>
    /// Diff two fingerprints: returns the relative paths that differ.
    /// Files missing on one side but present on the other count as
    /// differences too. Used by the join dialog to enumerate the files
    /// the user needs to repair.
    /// </summary>
    public static IReadOnlyList<string> Diff(ModFingerprint local, ModFingerprint remote)
    {
        if (local == null) throw new ArgumentNullException(nameof(local));
        if (remote == null) throw new ArgumentNullException(nameof(remote));

        var byPathLocal = local.Files.ToDictionary(f => f.RelativePath, StringComparer.OrdinalIgnoreCase);
        var byPathRemote = remote.Files.ToDictionary(f => f.RelativePath, StringComparer.OrdinalIgnoreCase);

        var diffs = new List<string>();
        var allPaths = byPathLocal.Keys
            .Concat(byPathRemote.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var path in allPaths)
        {
            var hereOk = byPathLocal.TryGetValue(path, out var here);
            var thereOk = byPathRemote.TryGetValue(path, out var there);
            if (!hereOk || !thereOk || !string.Equals(here!.Sha256, there!.Sha256, StringComparison.OrdinalIgnoreCase))
                diffs.Add(path);
        }

        diffs.Sort(StringComparer.OrdinalIgnoreCase);
        return diffs;
    }

    private static async Task<(string Sha, long Size)> HashFileAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
            return (string.Empty, 0L);

        try
        {
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 1024 * 1024, useAsync: true);

            using var sha = SHA256.Create();
            var hash = await sha.ComputeHashAsync(stream, ct);
            return (Convert.ToHexString(hash).ToLowerInvariant(), stream.Length);
        }
        catch (Exception ex)
        {
            // A locked or unreadable file looks the same as a mismatch to
            // peers — we don't want a transient read error to brick a
            // join, but we also don't want to claim success silently.
            DiagnosticLog.Write($"ModHashService: failed to hash '{path}': {ex.Message}");
            return (string.Empty, 0L);
        }
    }

    private static string ComputeCombinedHash(string modId, IReadOnlyList<ModFileHash> files)
    {
        // Canonical form: "<modId>\n<relPath>:<sha>:<size>\n..." with
        // relative paths normalised to forward slashes (so two players on
        // different working directories or with different path separators
        // still hash to the same value).
        var sb = new StringBuilder();
        sb.Append(modId).Append('\n');
        foreach (var f in files)
        {
            sb.Append(f.RelativePath.Replace('\\', '/'))
              .Append(':')
              .Append(f.Sha256)
              .Append(':')
              .Append(f.SizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture))
              .Append('\n');
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string NormaliseRelative(string relative)
    {
        // Strip any leading separators and unify direction so the same
        // logical path always normalises to the same string.
        var cleaned = relative.Trim().TrimStart('/', '\\');
        return cleaned.Replace('/', Path.DirectorySeparatorChar);
    }
}
