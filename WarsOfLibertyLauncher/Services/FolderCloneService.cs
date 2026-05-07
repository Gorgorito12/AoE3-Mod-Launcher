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

    /// <summary>Pause flag — same pattern as DownloadService.</summary>
    public bool Pause { get; set; }

    /// <summary>
    /// Clone <paramref name="sourceFolder"/> to <paramref name="destFolder"/>.
    /// </summary>
    public async Task CloneAsync(
        string sourceFolder,
        string destFolder,
        IProgress<CloneProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(sourceFolder))
            throw new DirectoryNotFoundException($"Source folder not found: {sourceFolder}");

        Directory.CreateDirectory(destFolder);

        DiagnosticLog.Write($"Cloning '{sourceFolder}' -> '{destFolder}'");

        // Step 1: enumerate everything to copy. We do this up-front so we can
        // show a real percentage instead of the indeterminate "files copied so
        // far" pattern.
        // Pass destFolder as an excluded subtree — when destination is inside
        // source (e.g. AoE3\Wars of Liberty\ is inside AoE3\), we must NOT
        // recurse into it or the copy will infinite-loop into itself.
        var files = await Task.Run(() => EnumerateFiles(sourceFolder, destFolder, ct), ct);
        long totalBytes = files.Sum(f => f.Length);
        DiagnosticLog.Write($"Files to copy: {files.Count} ({FormatBytes(totalBytes)})");

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
    /// Filters out files matching <see cref="SkipPatterns"/>.
    /// </summary>
    private static List<FileInfo> EnumerateFiles(string root, string? excludeSubtree, CancellationToken ct)
    {
        var result = new List<FileInfo>();
        var stack = new Stack<string>();
        stack.Push(root);

        // Normalize the excluded subtree path for case-insensitive prefix matching.
        // If the destination lives inside the source we must skip it entirely,
        // otherwise the clone recurses into the folder it's currently writing.
        string? excludeNormalized = null;
        if (!string.IsNullOrEmpty(excludeSubtree))
        {
            try { excludeNormalized = Path.GetFullPath(excludeSubtree).TrimEnd('\\', '/'); }
            catch { /* ignore */ }
        }

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = stack.Pop();

            // Skip the excluded subtree (and everything beneath it).
            if (excludeNormalized != null)
            {
                string currentFull;
                try { currentFull = Path.GetFullPath(current).TrimEnd('\\', '/'); }
                catch { continue; }

                if (string.Equals(currentFull, excludeNormalized, StringComparison.OrdinalIgnoreCase)
                    || currentFull.StartsWith(excludeNormalized + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || currentFull.StartsWith(excludeNormalized + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
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
                if (SkipPatterns.Any(pat => MatchesWildcard(name, pat)))
                    continue;

                try { result.Add(new FileInfo(file)); }
                catch { /* file disappeared; skip */ }
            }
        }

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
