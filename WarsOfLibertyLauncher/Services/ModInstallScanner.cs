using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Bounded, depth-limited search for an existing install of a mod, purely by
/// CONTENT. The per-folder decision is delegated to
/// <see cref="ModInstallProbe.LooksLikeModInstall"/>, so this NEVER widens WHAT
/// counts as an install (the anti-vanilla marker guard — e.g. WoL's
/// <c>art\zulushield</c> — is preserved); it only widens WHERE we look. Shared by
/// the automatic fallback scan (<see cref="UpdateService"/>) and the manual
/// folder picker / "search for my install" button (<c>MainWindow</c>).
///
/// Everything is bounded so it can never become a full-disk crawl:
///   * a maximum recursion depth per root,
///   * a skip-list of huge / irrelevant / permission-heavy system directories,
///   * per-directory IO/ACL swallowing (one unreadable folder can't abort a scan),
///   * lazy <c>yield</c> so callers can stop at the first hit,
///   * a hard cap on total directories visited, and
///   * a <see cref="CancellationToken"/>.
/// </summary>
public static class ModInstallScanner
{
    /// <summary>Hard ceiling on directories visited in a single broad/deep scan.</summary>
    private const int MaxDirsScanned = 20_000;

    /// <summary>
    /// Directory leaf names we never descend into — enormous, irrelevant, or
    /// access-denied trees that would only waste time and never hold a game mod.
    /// </summary>
    private static readonly HashSet<string> SkipDirNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "windows", "$recycle.bin", "system volume information", "recovery",
        "appdata", "programdata", "node_modules", ".git", ".svn",
        "$windows.~bt", "$windows.~ws", "windowsapps", "packagecache",
        "package cache", "temp", "tmp", "config.msi", "msocache",
        "perflogs", "intel", "amd", "nvidia",
    };

    /// <summary>
    /// Yield every directory at or under <paramref name="root"/> (up to
    /// <paramref name="maxDepth"/> levels deep, where 0 = only <paramref name="root"/>
    /// itself) that looks like a real install of <paramref name="profile"/>. Lazy:
    /// the caller can take just the first. Pass a shared <paramref name="visited"/>
    /// set across several roots (e.g. a drive root and its Program Files subfolder)
    /// so overlapping trees are walked at most once.
    /// </summary>
    public static IEnumerable<string> FindDeep(
        string root,
        ModProfile profile,
        int maxDepth,
        CancellationToken ct = default,
        HashSet<string>? visited = null,
        int maxDirs = MaxDirsScanned)
        => FindDeep(root, dir => ModInstallProbe.LooksLikeModInstall(dir, profile),
                    maxDepth, ct, visited, maxDirs);

    /// <summary>
    /// Same bounded BFS as the <see cref="ModProfile"/> overload, but the
    /// per-folder decision is an arbitrary <paramref name="match"/> predicate
    /// instead of the mod-install content rule. Lets other content-searches
    /// (e.g. finding a clean AoE3 base by <c>age3y.exe</c>) reuse the exact
    /// same skip-list / depth / visited-set / cap / cancellation machinery.
    /// The predicate MUST NOT throw — it's called per directory inside the walk.
    /// </summary>
    public static IEnumerable<string> FindDeep(
        string root,
        Func<string, bool> match,
        int maxDepth,
        CancellationToken ct = default,
        HashSet<string>? visited = null,
        int maxDirs = MaxDirsScanned)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            yield break;

        var seen = visited ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Breadth-first with an explicit queue: bounds depth cleanly and can't
        // blow the stack on a pathologically deep tree.
        var queue = new Queue<(string Dir, int Depth)>();
        queue.Enqueue((root, 0));
        int scanned = 0;

        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (dir, depth) = queue.Dequeue();

            if (!seen.Add(NormalizeDir(dir))) continue;
            if (++scanned > maxDirs) yield break;

            if (match(dir))
                yield return dir;

            if (depth >= maxDepth) continue;

            foreach (var sub in SafeEnumerateDirectories(dir))
            {
                var leaf = Path.GetFileName(
                    sub.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrEmpty(leaf) || SkipDirNames.Contains(leaf)) continue;
                queue.Enqueue((sub, depth + 1));
            }
        }
    }

    /// <summary>
    /// Broad fallback scan across a curated set of likely game-install roots.
    /// Lazy and early-exit friendly (take the first, or enumerate all to offer a
    /// chooser). Shares one visited-set so overlapping roots (a drive root and its
    /// Program Files) aren't walked twice.
    /// </summary>
    public static IEnumerable<string> FindBroad(
        ModProfile profile,
        int maxDepth,
        CancellationToken ct = default,
        bool includeDriveRoots = true,
        int maxDirs = MaxDirsScanned)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in EnumerateLikelyRoots(includeDriveRoots))
        {
            ct.ThrowIfCancellationRequested();
            foreach (var hit in FindDeep(root, profile, maxDepth, ct, visited, maxDirs))
                yield return hit;
        }
    }

    /// <summary>
    /// A curated, deduplicated set of roots likely to hold a game install, for
    /// the broad fallback scan. Reuses <see cref="AoE3Detector"/>'s drive + Steam
    /// enumeration. Cheap to build (no deep IO); the actual walking is the
    /// caller's bounded <see cref="FindDeep"/>.
    /// </summary>
    public static IEnumerable<string> EnumerateLikelyRoots(bool includeDriveRoots = true)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        bool Fresh(string? p)
            => !string.IsNullOrWhiteSpace(p) && seen.Add(NormalizeDir(p!)) && SafeExists(p!);

        // 1) In and around every detected AoE3 install — the mod usually lives
        //    here (sibling of AoE3, or nested under it).
        foreach (var inst in SafeList(() => AoE3Detector.FindAll()))
        {
            if (Fresh(inst.ModRoot)) yield return inst.ModRoot;
            if (Fresh(inst.GameFolder)) yield return inst.GameFolder;
            var parent = SafeParent(inst.ModRoot);
            if (Fresh(parent)) yield return parent!;
        }

        // 2) Steam libraries' common dir (games live under steamapps\common\<game>).
        foreach (var lib in SafeList(() => AoE3Detector.EnumerateSteamLibraryRoots().ToList()))
        {
            var common = SafeCombine(lib, "steamapps", "common");
            if (Fresh(common)) yield return common!;
        }

        // 3) Per fixed drive: Program Files (x86/64) and — only when
        //    includeDriveRoots — the bare drive root itself. Enumerating whole
        //    drive roots (C:\, D:\…) is a ransomware-adjacent behavioural signal
        //    for antivirus heuristics, so the AUTOMATIC/passive scan opts out
        //    (includeDriveRoots:false) and covers only AoE3-adjacent + Steam +
        //    Program Files; the MANUAL, user-initiated search keeps drive roots
        //    (a broad scan is expected when the user clicks "find my install").
        foreach (var probeRoot in SafeList(() => AoE3Detector.EnumerateProbeRoots().ToList()))
        {
            if (!includeDriveRoots && IsBareDriveRoot(probeRoot)) continue;
            if (Fresh(probeRoot)) yield return probeRoot;
        }
    }

    // ---------- helpers ----------

    /// <summary>
    /// True if <paramref name="p"/> is a bare drive root (e.g. <c>C:\</c>), as
    /// opposed to a subfolder like <c>C:\Program Files\</c>. Used to keep the
    /// passive scan off whole-drive enumeration.
    /// </summary>
    internal static bool IsBareDriveRoot(string p)
    {
        if (string.IsNullOrWhiteSpace(p)) return false;
        try
        {
            var full = Path.GetFullPath(p);
            var root = Path.GetPathRoot(full);
            return !string.IsNullOrEmpty(root)
                && string.Equals(
                    Path.TrimEndingDirectorySeparator(full),
                    Path.TrimEndingDirectorySeparator(root),
                    StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string dir)
    {
        try { return Directory.EnumerateDirectories(dir); }
        catch { return Array.Empty<string>(); }
    }

    private static string NormalizeDir(string dir)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir));
        }
        catch
        {
            return dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private static bool SafeExists(string dir)
    {
        try { return Directory.Exists(dir); }
        catch { return false; }
    }

    private static string? SafeParent(string dir)
    {
        try { return Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)); }
        catch { return null; }
    }

    private static string? SafeCombine(params string[] parts)
    {
        try { return Path.Combine(parts); }
        catch { return null; }
    }

    private static List<T> SafeList<T>(Func<List<T>> f)
    {
        try { return f() ?? new List<T>(); }
        catch { return new List<T>(); }
    }
}
