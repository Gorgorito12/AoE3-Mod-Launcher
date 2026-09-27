using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Per-file integrity verification of a mod install against the size+SHA-256
/// fingerprints recorded in <see cref="InstallManifest.FileHashes"/> (mod
/// overlay) and <see cref="InstallManifest.EngineFileHashes"/> (base engine).
///
/// This is the testable core behind the launcher's "Verify files" / "Repair"
/// tools: it produces the EXACT set of damaged (corrupt) and missing files.
///
/// Correctness rules carried over from the rest of the codebase:
///   * <b>size-first</b> — compare <see cref="FileInfo.Length"/> before hashing.
///     Truncation is the most common corruption and is free to detect.
///   * <b>localization-invariant for covered files</b> — a community translation
///     legitimately overwrites a covered file (e.g. <c>data/stringtabley.xml</c>),
///     so hashing the live file against the canonical (English) manifest hash
///     would wrongly flag it. Covered files are verified against the
///     <c>translations\_originals\</c> snapshot instead (same approach as
///     <see cref="Multiplayer.ModHashService.FingerprintAsync"/>). A covered file
///     with no snapshot is skipped (can't compare to the English hash).
///   * <b>overlay vs engine separation</b> — overlay files (from the mod payload)
///     are granularly repairable; engine files (from the cloned base game) are
///     NOT, so they live in a separate map and a damaged engine file is reported
///     as "reinstall the base game", never routed into the payload re-copy.
/// </summary>
public static class VerifyService
{
    /// <summary>Result of a per-file verification pass.</summary>
    public sealed record VerifyResult(
        List<string> MissingItems,
        List<string> CorruptItems,
        int TotalFilesChecked);

    /// <summary>
    /// Progress of a verification pass. <see cref="CurrentFile"/> is the
    /// most-recently-started file (a hint under parallelism, not strictly the
    /// in-flight one). <see cref="BytesTotal"/> is the sum of expected sizes
    /// (no disk access).
    /// </summary>
    public readonly record struct VerifyProgress(
        int Done, int Total, string CurrentFile, long BytesDone, long BytesTotal);

    /// <summary>
    /// Curated base-engine files to fingerprint, install-relative. Hash-if-present
    /// (a profile that doesn't ship one just skips it). The first three are the
    /// version-key data files; the rest are AoE3 engine DLLs that, if corrupt,
    /// make the game fail to launch. After <c>FlattenBinSubfolder</c> the DLLs
    /// live at the install ROOT (not <c>bin\</c>). Conservative start —
    /// RockallDLL.dll is confirmed shipped; the others are hashed only if found.
    /// </summary>
    public static readonly string[] EngineCandidates =
    {
        "data/protoy.xml",
        "data/techtreey.xml",
        "data/stringtabley.xml",
        "RockallDLL.dll",
        "binkw32.dll",
        "granny2.dll",
        "deformerdlly.dll",
    };

    private const int MaxParallelism = 4;

    /// <summary>True when the manifest carries per-file overlay hashes.</summary>
    public static bool HasFileHashes(InstallManifest? manifest) =>
        manifest != null && manifest.FileHashes.Count > 0;

    /// <summary>True when the manifest carries base-engine hashes.</summary>
    public static bool HasEngineHashes(InstallManifest? manifest) =>
        manifest != null && manifest.EngineFileHashes.Count > 0;

    // ------------------------------------------------------------------------
    // Shared helpers (used by verify AND by NativeInstallService.RecaptureHashes)
    // ------------------------------------------------------------------------

    /// <summary>Builds a forward-slash, case-insensitive set of covered files.</summary>
    public static HashSet<string> BuildCoveredSet(IReadOnlyList<string>? coveredFiles)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (coveredFiles != null)
            foreach (var c in coveredFiles)
                if (!string.IsNullOrWhiteSpace(c))
                    set.Add(c.Replace('\\', '/'));
        return set;
    }

    /// <summary>The <c>translations\_originals\</c> folder for an install.</summary>
    public static string OriginalsFolderOf(string installPath) => Path.Combine(
        installPath,
        TranslationService.TranslationsFolderName,
        TranslationService.OriginalsFolderName);

    /// <summary>
    /// Resolves the absolute path whose bytes represent the canonical (English)
    /// content of <paramref name="rel"/> — the snapshot for a covered file,
    /// otherwise the live file. Returns null when the file is covered but has no
    /// snapshot (can't be compared localization-invariantly → skip).
    /// </summary>
    public static string? ResolveHashTarget(
        string installPath, string rel, HashSet<string> coveredSet, string originalsFolder)
    {
        if (coveredSet.Contains(rel))
        {
            var snapshot = Path.Combine(originalsFolder, Path.GetFileName(rel));
            return File.Exists(snapshot) ? snapshot : null;
        }
        return Path.Combine(installPath, rel.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>Size + lowercase-hex SHA-256 of a file (thread-safe; static hasher).</summary>
    public static FileFingerprint ComputeFingerprintOf(string absolutePath)
    {
        using var fs = new FileStream(absolutePath, FileMode.Open, FileAccess.Read,
            FileShare.Read, bufferSize: 1024 * 1024, useAsync: false);
        var digest = SHA256.HashData(fs);
        return new FileFingerprint(fs.Length, Convert.ToHexString(digest).ToLowerInvariant());
    }

    // ------------------------------------------------------------------------
    // Overlay verification (parallel)
    // ------------------------------------------------------------------------

    /// <summary>What a single file check found. Healthy files are not reported.</summary>
    public enum FileProblem
    {
        Missing,
        SizeMismatch,
        HashMismatch,
        /// <summary>Another process holds it (sharing/lock violation) — the file may be fine.</summary>
        Unreadable,
        /// <summary>An antivirus refused the read (ERROR_VIRUS_INFECTED / ERROR_VIRUS_DELETED).</summary>
        Blocked,
    }

    /// <summary>Per-file result of <see cref="InspectOverlay"/> / <see cref="InspectEngine"/>.</summary>
    public sealed record Inspection(
        IReadOnlyList<(string Path, FileProblem Problem)> Problems,
        int FilesChecked);

    private const int ErrorSharingViolation = unchecked((int)0x80070020);
    private const int ErrorLockViolation = unchecked((int)0x80070021);
    private const int ErrorVirusInfected = unchecked((int)0x800700E1);
    private const int ErrorVirusDeleted = unchecked((int)0x800700E2);

    /// <summary>
    /// What a failed read says about the file. A LOCKED file is not a damaged one: counting it
    /// as corrupt made a repair re-download the whole payload because the game, or an antivirus
    /// scan, happened to hold one file open — and the re-lay then failed on that same file.
    /// Anything unrecognised stays damage, as before.
    /// </summary>
    internal static FileProblem ClassifyReadFailure(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is IOException io)
            {
                if (io.HResult is ErrorSharingViolation or ErrorLockViolation) return FileProblem.Unreadable;
                if (io.HResult is ErrorVirusInfected or ErrorVirusDeleted) return FileProblem.Blocked;
            }
        }
        return FileProblem.HashMismatch;
    }

    /// <summary>
    /// Verifies every entry in <see cref="InstallManifest.FileHashes"/> against
    /// the files on disk. Returns the exact missing/corrupt sets (relative paths,
    /// forward slashes — the manifest's own keys, sorted for determinism).
    /// Hashing runs in parallel; covered files verify against the snapshot.
    /// A locked or antivirus-blocked file counts as corrupt here; callers that need
    /// the difference use <see cref="InspectOverlay"/>.
    /// </summary>
    public static VerifyResult VerifyAgainstManifest(
        string installPath,
        InstallManifest manifest,
        IReadOnlyList<string>? coveredFiles,
        IProgress<VerifyProgress>? progress = null,
        CancellationToken ct = default)
    {
        var inspection = InspectOverlay(installPath, manifest.FileHashes, coveredFiles,
            hashOnly: null, progress, ct, retryDelayMs: 0);
        var missing = inspection.Problems.Where(p => p.Problem == FileProblem.Missing)
            .Select(p => p.Path).ToList();
        var corrupt = inspection.Problems.Where(p => p.Problem != FileProblem.Missing)
            .Select(p => p.Path).ToList();
        return new VerifyResult(missing, corrupt, inspection.FilesChecked);
    }

    /// <summary>
    /// Checks <paramref name="entries"/> against the files on disk, classifying each problem.
    /// Size first, then SHA-256. With <paramref name="hashOnly"/> set, every entry still gets the
    /// existence + size check (a FileInfo, no read) and only the listed ones are hashed — the
    /// recheck after a re-lay, which must not re-read a multi-GB install to prove what it just
    /// wrote. Files that were only locked are retried once after a short pause.
    /// </summary>
    public static Inspection InspectOverlay(
        string installPath,
        IReadOnlyDictionary<string, FileFingerprint> entries,
        IReadOnlyList<string>? coveredFiles,
        IReadOnlyCollection<string>? hashOnly = null,
        IProgress<VerifyProgress>? progress = null,
        CancellationToken ct = default,
        int retryDelayMs = 1500)
    {
        var coveredSet = BuildCoveredSet(coveredFiles);
        var originalsFolder = OriginalsFolderOf(installPath);
        var hashSet = hashOnly == null
            ? null
            : new HashSet<string>(hashOnly.Select(p => p.Replace('\\', '/')), StringComparer.OrdinalIgnoreCase);

        var problems = new ConcurrentDictionary<string, FileProblem>(StringComparer.Ordinal);
        int verified = 0;
        int done = 0;
        long bytesDone = 0;

        var list = entries.ToList();
        int total = list.Count;
        long bytesTotal = list.Sum(e => e.Value.Size);

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, MaxParallelism),
            CancellationToken = ct,
        };

        Parallel.ForEach(list, options, entry =>
        {
            int myDone = Interlocked.Increment(ref done);
            bool hash = hashSet == null || hashSet.Contains(entry.Key);
            var (problem, counted, bytes) = CheckOne(installPath, entry.Key, entry.Value, hash, coveredSet, originalsFolder);
            if (problem != null) problems[entry.Key] = problem.Value;
            if (counted) Interlocked.Increment(ref verified);
            ReportTick(progress, myDone, total, entry.Key, ref bytesDone, bytesTotal, bytes);
        });

        // A file another process held may just have been mid-scan: one sequential retry.
        var locked = problems.Where(p => p.Value == FileProblem.Unreadable).Select(p => p.Key).ToList();
        if (locked.Count > 0)
        {
            if (retryDelayMs > 0) ct.WaitHandle.WaitOne(retryDelayMs);
            ct.ThrowIfCancellationRequested();
            foreach (var rel in locked)
            {
                bool hash = hashSet == null || hashSet.Contains(rel);
                var (problem, _, _) = CheckOne(installPath, rel, entries[rel], hash, coveredSet, originalsFolder);
                if (problem == null) problems.TryRemove(rel, out _);
                else problems[rel] = problem.Value;
            }
        }

        // Deterministic output (parallelism scrambles insertion order).
        var sorted = problems.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => (p.Key, p.Value)).ToList();
        return new Inspection(sorted, verified);
    }

    /// <summary>
    /// One entry: (problem or null, whether it counted as checked, bytes for the progress bar).
    /// A covered file with no snapshot is confirmed to exist and not counted, as before.
    /// </summary>
    private static (FileProblem? Problem, bool Counted, long Bytes) CheckOne(
        string installPath, string rel, FileFingerprint fp, bool hash,
        HashSet<string> coveredSet, string originalsFolder)
    {
        var live = Path.Combine(installPath, rel.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(live)) return (FileProblem.Missing, false, 0);

        var target = ResolveHashTarget(installPath, rel, coveredSet, originalsFolder);
        if (target == null) return (null, false, 0);

        long len;
        try { len = new FileInfo(target).Length; }
        catch { len = -1; }
        if (len != fp.Size) return (FileProblem.SizeMismatch, true, Math.Max(0, len));
        if (!hash) return (null, true, 0);

        try
        {
            var actual = ComputeFingerprintOf(target);
            return string.Equals(actual.Sha256, fp.Sha256, StringComparison.OrdinalIgnoreCase)
                ? (null, true, fp.Size)
                : (FileProblem.HashMismatch, true, fp.Size);
        }
        catch (Exception ex)
        {
            return (ClassifyReadFailure(ex), true, fp.Size);
        }
    }

    private static void ReportTick(
        IProgress<VerifyProgress>? progress, int done, int total, string currentFile,
        ref long bytesDone, long bytesTotal, long fileBytes)
    {
        long nowDone = Interlocked.Add(ref bytesDone, fileBytes);
        // Coalesce so a multi-thousand-file install doesn't flood the UI.
        if (progress != null && (done == 1 || done % 64 == 0 || done == total))
            progress.Report(new VerifyProgress(done, total, currentFile, nowDone, bytesTotal));
    }

    // ------------------------------------------------------------------------
    // Engine verification (SEPARATE — never feeds the granular repair path)
    // ------------------------------------------------------------------------

    /// <summary>
    /// Verifies the curated base-engine files against
    /// <see cref="InstallManifest.EngineFileHashes"/>. Returns the relative paths
    /// of damaged/missing engine files. These are NOT repairable from the mod
    /// payload, so the caller surfaces them as "reinstall the base game" and must
    /// NOT route them into the granular repair set.
    /// </summary>
    public static List<string> VerifyEngineFiles(
        string installPath,
        InstallManifest manifest,
        IReadOnlyList<string>? coveredFiles,
        CancellationToken ct = default)
        => InspectEngine(installPath, manifest, coveredFiles, ct).Problems.Select(p => p.Path).ToList();

    /// <summary>
    /// The engine map, classified like <see cref="InspectOverlay"/>. Sequential — it is at most
    /// seven files.
    /// </summary>
    public static Inspection InspectEngine(
        string installPath,
        InstallManifest manifest,
        IReadOnlyList<string>? coveredFiles,
        CancellationToken ct = default)
    {
        var problems = new List<(string, FileProblem)>();
        if (manifest.EngineFileHashes.Count == 0) return new Inspection(problems, 0);

        var coveredSet = BuildCoveredSet(coveredFiles);
        var originalsFolder = OriginalsFolderOf(installPath);
        int checkedCount = 0;

        foreach (var (rel, fp) in manifest.EngineFileHashes.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var (problem, counted, _) = CheckOne(installPath, rel, fp, hash: true, coveredSet, originalsFolder);
            if (counted) checkedCount++;
            if (problem != null) problems.Add((rel, problem.Value));
        }
        return new Inspection(problems, checkedCount);
    }

    // ------------------------------------------------------------------------
    // Unexpected / leftover files (DIAGNOSTIC ONLY — log, never UI)
    // ------------------------------------------------------------------------

    /// <summary>
    /// Returns install-relative paths present on disk that the manifest does NOT
    /// account for (not in <see cref="InstallManifest.Files"/> /
    /// <see cref="InstallManifest.FileHashes"/> / <see cref="InstallManifest.EngineFileHashes"/>),
    /// excluding the launcher's own bookkeeping artifacts. This is DIAGNOSTIC: a
    /// patched install legitimately gains files the manifest never recorded, so it
    /// is logged (not surfaced as "corrupt") and the caller caps the output.
    /// </summary>
    public static IReadOnlyList<string> FindUnexpectedFiles(string installPath, InstallManifest manifest)
    {
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in manifest.Files) expected.Add(f);
        foreach (var k in manifest.FileHashes.Keys) expected.Add(k);
        foreach (var k in manifest.EngineFileHashes.Keys) expected.Add(k);

        var extras = new List<string>();
        if (!Directory.Exists(installPath)) return extras;

        foreach (var full in Directory.EnumerateFiles(installPath, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(installPath, full).Replace('\\', '/');
            if (expected.Contains(rel)) continue;
            if (IsLauncherArtifact(rel)) continue;
            extras.Add(rel);
        }
        extras.Sort(StringComparer.Ordinal);
        return extras;
    }

    /// <summary>Launcher-created bookkeeping that legitimately isn't an overlay/base file.</summary>
    private static bool IsLauncherArtifact(string rel)
    {
        if (rel.Equals(InstallManifest.FileName, StringComparison.OrdinalIgnoreCase)) return true;
        if (rel.Equals(InstallManifest.LegacyFileName, StringComparison.OrdinalIgnoreCase)) return true;
        if (InstallManifest.IsSaveScratch(rel)) return true;
        if (rel.EndsWith(Repair.EngineRestore.StagingSuffix, StringComparison.OrdinalIgnoreCase)) return true;
        if (rel.StartsWith("translations/", StringComparison.OrdinalIgnoreCase)) return true;
        if (rel.StartsWith("etc/", StringComparison.OrdinalIgnoreCase)
            && rel.EndsWith("_delete.lst", StringComparison.OrdinalIgnoreCase)) return true;
        if (rel.EndsWith("-shortcut.ico", StringComparison.OrdinalIgnoreCase)) return true;
        // Patch scratch dirs (upd_backup_*, _upd_delete_backup) left mid-failure.
        if (rel.StartsWith("upd_backup_", StringComparison.OrdinalIgnoreCase)) return true;
        if (rel.StartsWith("_upd_delete_backup/", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
