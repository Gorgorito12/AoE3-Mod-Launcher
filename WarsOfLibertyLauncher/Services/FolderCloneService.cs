using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Reports progress while cloning a folder.
/// </summary>
public record CloneProgress(
    long BytesCopied,
    long BytesTotal,
    int FilesCopied,
    int FilesTotal,
    string CurrentFile,
    double BytesPerSecond);

/// <summary>
/// Copies the contents of a source folder to a destination folder, with
/// progress reporting and cancellation support.
///
/// Used for AoE3 → "Wars of Liberty" install root cloning. We skip files
/// that are specific to a storefront and shouldn't be copied (Steam manifest
/// files, GOG launcher metadata) so the resulting copy is a clean,
/// store-independent AoE3 install.
/// </summary>
public class FolderCloneService
{
    /// <summary>
    /// Filename patterns to skip during a clone. These belong to specific
    /// storefronts and would either fail to copy (file locks) or cause
    /// confusion in the destination (e.g. Steam thinking it owns the copy).
    /// </summary>
    private static readonly string[] SkipPatterns = new[]
    {
        // Steam metadata
        "appmanifest_*.acf",
        "*.vdf",
        "steam_appid.txt",
        "steam_api*.dll",
        "Steam.dll",
        // GOG metadata
        "goggame-*.info",
        "goggame-*.dll",
        "goggame-*.script",
        "goggame.dll",
        "*.tmp",
    };

    /// <summary>
    /// Top-level directory names (immediate children of the AoE3 root)
    /// to ALWAYS exclude when cloning, regardless of the auto-detection
    /// heuristics below. These fall into two buckets:
    ///
    ///   • Other mods that may live inside the user's AoE3 vanilla
    ///     install. Cloning them would drag thousands of files from
    ///     another mod into the new install root — multiplayer hashes
    ///     would mismatch against players who only have the target mod.
    ///     Improvement Mod is the canonical case; future community mods
    ///     would be added here as they become common.
    ///
    ///   • Side-loaded runtimes / installers that ship with AoE3 vanilla
    ///     but aren't part of the playable game (`directx\`, `msxml\`,
    ///     legacy `translations\` from previous launcher revisions).
    ///     Copying them bloats the install and contributes to multi-
    ///     player hash mismatches without adding anything useful.
    ///
    /// Auto-detection (the "*-manifest.json" probe a few lines down)
    /// would catch most mod-clone subfolders, but Improvement Mod and
    /// other non-launcher-installed mods don't ship that file — so we
    /// pair the heuristic with this hard list for defense-in-depth.
    /// Case-insensitive match on the directory's base name.
    /// </summary>
    private static readonly string[] AlwaysExcludeTopLevelDirs = new[]
    {
        // Other community mods
        "Improvement Mod",
        "Wars of Liberty",        // nested clone defensiveness
        "wol",                    // lowercase variant of WoL
        // Side-loaded runtimes / installers shipped with AoE3 vanilla
        "directx",
        "msxml",
        // Legacy launcher artifacts that ended up in some installs
        "translations",
    };

    /// <summary>Pause flag — same pattern as DownloadService.</summary>
    public bool Pause { get; set; }

    /// <summary>
    /// Clone <paramref name="sourceFolder"/> to <paramref name="destFolder"/>.
    /// <paramref name="extraExcludedSubtrees"/> lists additional folders that
    /// must NOT be copied even though they live inside the source. The
    /// canonical use is: when installing Improvement Mod, AoE3 lives at
    /// <c>...\Age Of Empires 3\</c> and the user's previous WoL install
    /// lives at <c>...\Age Of Empires 3\Wars of Liberty\</c>. Without the
    /// exclusion, cloning AoE3 → Improvement Mod destination would scoop
    /// up the entire WoL clone as a sub-folder, blowing up the install size
    /// and producing a confusingly nested layout.
    /// </summary>
    public async Task<int> CloneAsync(
        string sourceFolder,
        string destFolder,
        IProgress<CloneProgress>? progress = null,
        CancellationToken ct = default,
        IEnumerable<string>? extraExcludedSubtrees = null)
    {
        if (!Directory.Exists(sourceFolder))
            throw new DirectoryNotFoundException($"Source folder not found: {sourceFolder}");

        Directory.CreateDirectory(destFolder);

        DiagnosticLog.Write($"Cloning '{sourceFolder}' -> '{destFolder}'");

        // Assemble the full exclusion set (shared with the CountCloneableFiles
        // pre-flight so the dry-run count matches what we actually copy).
        var excludedSubtrees = BuildExcludedSubtrees(sourceFolder, destFolder, extraExcludedSubtrees);

        // Step 1: enumerate everything to copy. We do this up-front so we can
        // show a real percentage instead of the indeterminate "files copied so
        // far" pattern.
        var files = await Task.Run(() => EnumerateFiles(sourceFolder, excludedSubtrees, ct), ct);
        long totalBytes = files.Sum(f => f.Length);
        DiagnosticLog.Write($"Files to copy: {files.Count} ({FormatBytes(totalBytes)})");
        // Per-pattern skip tally so we can spot accidental overmatch
        // by the SkipPatterns list (e.g. a future pattern that ends
        // up catching game asset names by mistake).
        foreach (var kv in LastSkipCounts.Where(k => k.Value > 0))
        {
            DiagnosticLog.Write($"  SkipPattern '{kv.Key}' matched {kv.Value} file(s)");
        }

        // Step 2: copy with progress
        long bytesCopied = 0;
        int filesCopied = 0;
        var startTime = DateTime.UtcNow;

        foreach (var srcInfo in files)
        {
            ct.ThrowIfCancellationRequested();

            // Honor pause flag
            while (Pause && !ct.IsCancellationRequested)
                await Task.Delay(200, ct);

            var relativePath = Path.GetRelativePath(sourceFolder, srcInfo.FullName);
            var destPath = Path.Combine(destFolder, relativePath);

            // Ensure destination folder exists
            var destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir))
                Directory.CreateDirectory(destDir);

            // Copy the file in chunks so we can report mid-file progress on
            // very large files like AoE3's data archives.
            try
            {
                await CopyFileWithProgressAsync(
                    srcInfo.FullName, destPath, srcInfo.Length,
                    bytesCopied, totalBytes, filesCopied, files.Count,
                    relativePath, startTime, progress, ct);
            }
            catch (UnauthorizedAccessException ex)
            {
                // Some files (e.g. _CommonRedist on Steam) can be read-only
                // for the launcher even though they're readable. Skip them
                // and continue — they're not critical for the engine.
                DiagnosticLog.Write($"Skipping (access denied): {relativePath} — {ex.Message}");
            }
            catch (IOException ex) when (ex.HResult == unchecked((int)0x80070020))
            {
                // ERROR_SHARING_VIOLATION — file is locked by Steam/GOG client
                DiagnosticLog.Write($"Skipping (file locked): {relativePath}");
            }

            bytesCopied += srcInfo.Length;
            filesCopied++;
        }

        DiagnosticLog.Write($"Clone complete: {filesCopied}/{files.Count} files, " +
                            $"{FormatBytes(bytesCopied)}");
        return filesCopied;
    }

    /// <summary>
    /// Pre-flight count: how many files a <see cref="CloneAsync"/> with the SAME
    /// args would copy, WITHOUT copying anything. The install flow runs this
    /// BEFORE the (multi-GB) payload download, so a misconfigured clone — missing/
    /// empty AoE3 source, or one whose base content got fully excluded — fails
    /// fast instead of after a long download. Uses the exact same exclusion set
    /// + enumeration as the real clone, so the count is authoritative.
    /// </summary>
    public int CountCloneableFiles(
        string sourceFolder,
        string destFolder,
        IEnumerable<string>? extraExcludedSubtrees = null,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(sourceFolder)) return 0;
        var excluded = BuildExcludedSubtrees(sourceFolder, destFolder, extraExcludedSubtrees);
        return EnumerateFiles(sourceFolder, excluded, ct).Count;
    }

    /// <summary>
    /// Builds the clone exclusion set: destFolder (no self-recursion) + caller-
    /// supplied sibling subtrees + the hard AlwaysExclude list + auto-detected
    /// launcher-managed mod clones ("*-manifest.json" probe). Shared by
    /// <see cref="CloneAsync"/> and <see cref="CountCloneableFiles"/> so the
    /// pre-flight count and the real clone agree.
    /// </summary>
    private static List<string> BuildExcludedSubtrees(
        string sourceFolder, string destFolder, IEnumerable<string>? extraExcludedSubtrees)
    {
        var excludedSubtrees = new List<string> { destFolder };
        if (extraExcludedSubtrees != null)
        {
            foreach (var s in extraExcludedSubtrees)
                if (!string.IsNullOrEmpty(s))
                    excludedSubtrees.Add(s);
        }
        try
        {
            foreach (var sub in Directory.EnumerateDirectories(sourceFolder))
            {
                var subName = Path.GetFileName(sub);
                // Hard list — other mods + side-loaded runtimes we refuse to
                // clone into a fresh install root.
                if (AlwaysExcludeTopLevelDirs.Any(d =>
                        string.Equals(d, subName, StringComparison.OrdinalIgnoreCase)))
                {
                    DiagnosticLog.Write($"Clone: excluding well-known top-level dir '{subName}'");
                    excludedSubtrees.Add(sub);
                    continue;
                }
                // Heuristic — a subfolder with a "<x>-manifest.json" in its root
                // is a launcher-managed mod clone; skip it so AoE3 → ImprovementMod
                // doesn't scoop up a previously-installed WoL etc.
                bool looksLikeModClone = Directory.EnumerateFiles(sub, "*-manifest.json",
                    SearchOption.TopDirectoryOnly).Any();
                if (looksLikeModClone)
                {
                    DiagnosticLog.Write($"Clone: auto-excluding mod-clone subfolder '{sub}'");
                    excludedSubtrees.Add(sub);
                }
            }
        }
        catch (Exception ex)
        {
            // Don't fail just because the auto-detect probe can't enumerate one
            // of the source subfolders.
            DiagnosticLog.Write($"Clone: auto-exclude probe failed: {ex.Message}");
        }

        // #3 sanity guard: an exclusion that IS the clone source (or a PARENT of
        // it) would empty the entire clone — never legitimate (you're cloning the
        // source). Drop such entries + warn loudly instead of silently producing
        // a 0-file clone. (Excluding a SUBDIR like bin\ is a mod-specific call the
        // pre-flight count + post-clone gate + VerifyInstallation catch downstream
        // — not second-guessed here.)
        string srcFull;
        try { srcFull = Path.GetFullPath(sourceFolder).TrimEnd('\\', '/'); }
        catch { return excludedSubtrees; }
        excludedSubtrees.RemoveAll(s =>
        {
            string sFull;
            try { sFull = Path.GetFullPath(s).TrimEnd('\\', '/'); }
            catch { return false; }
            bool emptiesClone = string.Equals(sFull, srcFull, StringComparison.OrdinalIgnoreCase)
                || srcFull.StartsWith(sFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || srcFull.StartsWith(sFull + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            if (emptiesClone)
                DiagnosticLog.Write(
                    $"Clone: REFUSING exclusion '{s}' — it is (or contains) the clone " +
                    "source itself, which would empty the whole clone.");
            return emptiesClone;
        });

        return excludedSubtrees;
    }

    /// <summary>
    /// Copies a single file in 1 MB chunks, reporting progress on each chunk.
    /// Allows the UI to show smooth progress even for files several hundred MB
    /// in size (AoE3 has a few of those).
    /// </summary>
    private async Task CopyFileWithProgressAsync(
        string source, string dest, long fileSize,
        long startBytesCopied, long totalBytes,
        int filesCopiedBefore, int totalFiles,
        string relativePath, DateTime startTime,
        IProgress<CloneProgress>? progress,
        CancellationToken ct)
    {
        const int bufferSize = 1024 * 1024;
        await using var src = new FileStream(source, FileMode.Open, FileAccess.Read,
            FileShare.Read | FileShare.Delete, bufferSize, useAsync: true);
        await using var dst = new FileStream(dest, FileMode.Create, FileAccess.Write,
            FileShare.None, bufferSize, useAsync: true);

        var buffer = new byte[bufferSize];
        long fileBytes = 0;
        int read;
        while ((read = await src.ReadAsync(buffer, ct)) > 0)
        {
            while (Pause && !ct.IsCancellationRequested)
                await Task.Delay(200, ct);
            ct.ThrowIfCancellationRequested();

            await dst.WriteAsync(buffer.AsMemory(0, read), ct);
            fileBytes += read;

            // Report at most every ~250 ms or once per file to avoid spamming
            // the UI thread.
            if (fileBytes == fileSize || fileBytes % (16 * 1024 * 1024) < bufferSize)
            {
                long totalCopied = startBytesCopied + fileBytes;
                double seconds = (DateTime.UtcNow - startTime).TotalSeconds;
                double bps = seconds > 0 ? totalCopied / seconds : 0;

                progress?.Report(new CloneProgress(
                    BytesCopied: totalCopied,
                    BytesTotal: totalBytes,
                    FilesCopied: filesCopiedBefore,
                    FilesTotal: totalFiles,
                    CurrentFile: relativePath,
                    BytesPerSecond: bps));
            }
        }
    }

    /// <summary>
    /// Walks the source folder and returns every file that should be copied.
    /// Filters out files matching <see cref="SkipPatterns"/> and any path
    /// that lives inside one of <paramref name="excludeSubtrees"/>.
    /// </summary>
    /// <summary>
    /// Tally of files skipped per <see cref="SkipPatterns"/> entry
    /// during the most recent EnumerateFiles call. Diagnostic only —
    /// gives a quick view of "the clone dropped 6 .vdf files and
    /// 1 Steam.dll" so we can verify SkipPatterns isn't accidentally
    /// catching something it shouldn't (no game asset matches those
    /// patterns by design, but a future SkipPatterns addition could
    /// regress that). Reset on each EnumerateFiles invocation.
    /// </summary>
    public static IReadOnlyDictionary<string, int> LastSkipCounts { get; private set; } =
        new Dictionary<string, int>();

    private static List<FileInfo> EnumerateFiles(string root, IEnumerable<string> excludeSubtrees, CancellationToken ct)
    {
        var result = new List<FileInfo>();
        var stack = new Stack<string>();
        stack.Push(root);
        var skipCounts = SkipPatterns.ToDictionary(p => p, _ => 0);

        // Normalize each excluded subtree once so the per-directory check
        // in the loop is a cheap prefix string compare. Bad/missing paths
        // are silently dropped — they just don't contribute an exclusion.
        var excludeNormalized = new List<string>();
        foreach (var s in excludeSubtrees)
        {
            if (string.IsNullOrEmpty(s)) continue;
            try { excludeNormalized.Add(Path.GetFullPath(s).TrimEnd('\\', '/')); }
            catch { /* ignore malformed path */ }
        }

        bool IsUnderAnyExclusion(string currentFull)
        {
            foreach (var ex in excludeNormalized)
            {
                if (string.Equals(currentFull, ex, StringComparison.OrdinalIgnoreCase)
                    || currentFull.StartsWith(ex + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || currentFull.StartsWith(ex + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = stack.Pop();

            // Skip the current directory entirely if it sits under any
            // exclusion subtree. Same semantics as the old single-subtree
            // check, just generalised to a list.
            if (excludeNormalized.Count > 0)
            {
                string currentFull;
                try { currentFull = Path.GetFullPath(current).TrimEnd('\\', '/'); }
                catch { continue; }

                if (IsUnderAnyExclusion(currentFull))
                    continue;
            }

            IEnumerable<string> subdirs;
            IEnumerable<string> files;
            try
            {
                subdirs = Directory.EnumerateDirectories(current);
                files = Directory.EnumerateFiles(current);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var sub in subdirs)
                stack.Push(sub);

            foreach (var file in files)
            {
                var name = Path.GetFileName(file);
                string? matchedPattern = null;
                foreach (var pat in SkipPatterns)
                {
                    if (MatchesWildcard(name, pat))
                    {
                        matchedPattern = pat;
                        break;
                    }
                }
                if (matchedPattern != null)
                {
                    skipCounts[matchedPattern]++;
                    continue;
                }

                try { result.Add(new FileInfo(file)); }
                catch { /* file disappeared; skip */ }
            }
        }

        LastSkipCounts = skipCounts;
        return result;
    }

    private static bool MatchesWildcard(string fileName, string pattern)
    {
        // Convert "appmanifest_*.acf" to a regex-style match
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(
            fileName, regex,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:0.##} {units[unit]}";
    }
}
