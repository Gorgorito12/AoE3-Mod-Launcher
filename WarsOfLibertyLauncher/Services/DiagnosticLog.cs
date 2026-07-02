using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Simple file logger for diagnostics. Writes to launcher-debug.log in the
/// per-user data dir (%LocalAppData%\AoE3ModLauncher\ via AppPaths.LogFile).
/// On launcher startup the log is reset, so each session is self-contained.
///
/// Write() is non-blocking: callers from the UI thread (mod switch, CheckAsync,
/// progress reporters, …) just enqueue the message and return immediately.
/// A background drainer task serialises writes to disk, preserving order while
/// keeping the synchronous portion of hot UI paths free of disk I/O.
/// </summary>
public static class DiagnosticLog
{
    private static readonly string LogPath = AppPaths.LogFile;

    private static readonly object FileLock = new();
    private static readonly ConcurrentQueue<string> Queue = new();
    private static readonly SemaphoreSlim Signal = new(0);
    private static readonly Task DrainerTask = Task.Run(DrainerLoop);

    public static void Reset()
    {
        try
        {
            lock (FileLock)
            {
                File.WriteAllText(LogPath,
                    $"=== Wars of Liberty Launcher debug log ===\n" +
                    $"Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n\n");
            }
        }
        catch
        {
            // If we can't even create the log, oh well — don't crash the app.
        }
    }

    public static void Write(string message)
    {
        // Format the line on the caller's thread so the timestamp matches
        // the call site, not whenever the drainer happens to flush.
        Queue.Enqueue($"[{DateTime.Now:HH:mm:ss}] {message}\n");
        try { Signal.Release(); } catch { /* disposed during shutdown */ }
    }

    public static void WriteSection(string title)
    {
        Write("");
        Write("--- " + title + " ---");
    }

    /// <summary>Save raw text content (e.g. the UpdateInfo.xml) for inspection.</summary>
    public static void SaveSnapshot(string filename, string content)
    {
        try
        {
            var path = AppPaths.SnapshotFile(filename);
            File.WriteAllText(path, content);
            Write($"Snapshot guardado: {filename}");
        }
        catch
        {
        }
    }

    /// <summary>
    /// Synchronously flushes any queued messages. Call from shutdown hooks
    /// (AppDomain.ProcessExit, unhandled exception handlers, …) so the last
    /// few log lines aren't lost when the process dies before the drainer
    /// gets to them.
    /// </summary>
    public static void Flush()
    {
        WriteBatch();
    }

    /// <summary>
    /// Bundles the diagnostic files into a single <c>.zip</c> at
    /// <paramref name="destinationZipPath"/> so a user can attach ONE file when
    /// reporting a bug. Includes the top-level <c>*.log</c> (launcher-debug.log,
    /// multiplayer-events.log) and <c>*snapshot*</c> files from
    /// <paramref name="sourceDir"/> (defaults to <see cref="AppPaths.DataDir"/>).
    ///
    /// DELIBERATELY EXCLUDES <c>launcher-config.json</c>: it holds the cached
    /// Discord session token, which must not leave the user's machine in a shared
    /// bundle. Subfolders (e.g. <c>mod-assets\</c>) are not included.
    ///
    /// Files are copied to a temp staging folder first (not zipped from their live
    /// path) so an in-flight log write can't race the archive. Returns the zip
    /// path; throws on failure so the caller can surface it.
    /// </summary>
    public static string ExportBundle(string destinationZipPath, string? sourceDir = null)
    {
        Flush();
        var src = sourceDir ?? AppPaths.DataDir;
        var staging = Path.Combine(Path.GetTempPath(), "wol-diag-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(staging);

            if (Directory.Exists(src))
            {
                foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileName(file);
                    bool include =
                        name.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("snapshot", StringComparison.OrdinalIgnoreCase);
                    // Never ship the config — it carries the Discord session token.
                    if (name.Equals(AppPaths.ConfigFileName, StringComparison.OrdinalIgnoreCase))
                        include = false;
                    if (!include) continue;

                    try { File.Copy(file, Path.Combine(staging, name), overwrite: true); }
                    catch { /* skip a file we couldn't read; bundle the rest */ }
                }
            }

            var destDir = Path.GetDirectoryName(destinationZipPath);
            if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);
            if (File.Exists(destinationZipPath)) File.Delete(destinationZipPath);

            ZipFile.CreateFromDirectory(staging, destinationZipPath, CompressionLevel.Optimal,
                includeBaseDirectory: false);
            return destinationZipPath;
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }

    private static async Task DrainerLoop()
    {
        // Wait for at least one enqueued message, then drain everything
        // currently pending in one batched append. Coalescing keeps the
        // file handle churn low when many writes arrive in quick
        // succession (mod switches, CheckAsync progress streams, etc.).
        while (true)
        {
            try { await Signal.WaitAsync().ConfigureAwait(false); }
            catch { return; }

            WriteBatch();
        }
    }

    private static void WriteBatch()
    {
        if (Queue.IsEmpty) return;

        var sb = new System.Text.StringBuilder();
        while (Queue.TryDequeue(out var line))
            sb.Append(line);

        if (sb.Length == 0) return;

        lock (FileLock)
        {
            try { File.AppendAllText(LogPath, sb.ToString()); }
            catch { /* best-effort */ }
        }
    }
}
