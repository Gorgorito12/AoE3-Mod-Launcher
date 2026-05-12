using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Simple file logger for diagnostics. Writes to launcher-debug.log next to the .exe.
/// On launcher startup the log is reset, so each session is self-contained.
///
/// Write() is non-blocking: callers from the UI thread (mod switch, CheckAsync,
/// progress reporters, …) just enqueue the message and return immediately.
/// A background drainer task serialises writes to disk, preserving order while
/// keeping the synchronous portion of hot UI paths free of disk I/O.
/// </summary>
public static class DiagnosticLog
{
    private static readonly string LogPath =
        Path.Combine(AppContext.BaseDirectory, "launcher-debug.log");

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
            var path = Path.Combine(AppContext.BaseDirectory, filename);
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
