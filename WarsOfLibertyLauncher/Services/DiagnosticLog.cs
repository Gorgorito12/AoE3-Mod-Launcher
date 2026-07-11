using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
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

    /// <summary>Previous session's rotated log — see <see cref="Reset"/>.</summary>
    private static readonly string PrevLogPath =
        Path.Combine(AppPaths.DataDir, "launcher-debug.prev.log");

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
                // Preserve the previous session's log as launcher-debug.prev.log
                // BEFORE truncating, so a crash that killed the last run (even one
                // that didn't trip the in-app crash net, e.g. a native/hard kill)
                // still leaves its full log behind for the next diagnostic bundle.
                // One generation only — overwritten each launch. ExportBundle picks
                // it up automatically (it ends in .log).
                try
                {
                    if (File.Exists(LogPath))
                    {
                        if (File.Exists(PrevLogPath)) File.Delete(PrevLogPath);
                        File.Move(LogPath, PrevLogPath);
                    }
                }
                catch { /* if rotation fails we still truncate below */ }

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

    /// <summary>
    /// Persists an unhandled exception to a timestamped <c>crash-&lt;…&gt;.log</c> in
    /// the data dir. Unlike <see cref="LogPath"/> (which <see cref="Reset"/> rotates
    /// each launch), a crash log SURVIVES the next launch so the reporter's
    /// diagnostic bundle actually contains the crash. Also mirrors a one-line marker
    /// into the debug log and flushes. Called from the global exception hooks in
    /// <c>App</c>. Best-effort — must never throw (it runs while the app may be dying).
    /// </summary>
    public static void WriteCrash(string source, Exception? ex)
    {
        try
        {
            var now = DateTime.Now;
            var body =
                "=== Wars of Liberty Launcher CRASH ===\n" +
                $"Time:    {now:yyyy-MM-dd HH:mm:ss}\n" +
                $"Source:  {source}\n" +
                $"Version: {VersionLine()}\n" +
                $"OS:      {Environment.OSVersion} / .NET {Environment.Version}\n\n" +
                (ex?.ToString() ?? "(no exception object)") + "\n";

            var path = Path.Combine(AppPaths.DataDir, $"crash-{now:yyyyMMdd-HHmmss}.log");
            lock (FileLock)
            {
                try { File.WriteAllText(path, body); } catch { /* best-effort */ }
            }

            Write($"UNHANDLED EXCEPTION ({source}): " +
                  $"{ex?.GetType().Name}: {ex?.Message} -> {Path.GetFileName(path)}");
            Flush();
            PruneOldCrashLogs();
        }
        catch { /* never let crash logging itself crash */ }
    }

    private static string VersionLine()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            return string.IsNullOrWhiteSpace(info)
                ? asm.GetName().Version?.ToString() ?? "?"
                : info;
        }
        catch { return "?"; }
    }

    /// <summary>Keep only the newest <paramref name="keep"/> crash logs.</summary>
    private static void PruneOldCrashLogs(int keep = 5)
    {
        try
        {
            var files = Directory.GetFiles(AppPaths.DataDir, "crash-*.log");
            if (files.Length <= keep) return;
            Array.Sort(files,
                (a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
            for (int i = keep; i < files.Length; i++)
            {
                try { File.Delete(files[i]); } catch { /* best-effort */ }
            }
        }
        catch { /* best-effort */ }
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
    /// When <paramref name="gameUserDataDir"/> is given (the mod's
    /// <c>My Games\&lt;folder&gt;</c> resolved by
    /// <see cref="UserDataService.GetUserDataFolder(string)"/>), the bundle ALSO
    /// carries — under a <c>game-userdata/</c> subfolder — the small OOS / sync /
    /// text-log artifacts AoE3 writes there (matched by
    /// <see cref="ShouldIncludeGameFile"/>, size- and count-capped so recorded
    /// games / savegames are never swept), plus a <c>game-userdata-listing.txt</c>
    /// snapshot of that folder. This is what makes an in-game OUT-OF-SYNC report
    /// diagnosable: a sim desync is written by the GAME, not the launcher log, so
    /// without these files the bundle can't show the cause. It is READ-ONLY — files
    /// are copied to staging, the game folder is never modified.
    ///
    /// DELIBERATELY EXCLUDES <c>launcher-config.json</c>: it holds the cached
    /// Discord session token, which must not leave the user's machine in a shared
    /// bundle. Subfolders (e.g. <c>mod-assets\</c>) are not included.
    ///
    /// Files are copied to a temp staging folder first (not zipped from their live
    /// path) so an in-flight log write can't race the archive. Returns the zip
    /// path; throws on failure so the caller can surface it.
    /// </summary>
    public static string ExportBundle(
        string destinationZipPath, string? sourceDir = null, string? gameUserDataDir = null)
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

            // Game user-data OOS/sync artifacts (best-effort, read-only).
            if (!string.IsNullOrEmpty(gameUserDataDir))
                StageGameUserData(gameUserDataDir!, staging);

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

    /// <summary>Cap on a single game-folder file we'll copy (bytes). Above this we
    /// skip it — a recorded game (<c>.age3Yrec</c>) / savegame is far larger and
    /// isn't a small OOS/sync/log dump.</summary>
    internal const long GameFileMaxBytes = 2L * 1024 * 1024;

    /// <summary>Safety cap on how many game-folder files we copy into the bundle.</summary>
    internal const int GameFileMaxCount = 40;

    /// <summary>
    /// Pure include/exclude rule for a top-level file in the game's user-data
    /// folder (kept static + parameterised so it's unit-testable). We take only the
    /// small OOS / sync / plain-text-log artifacts — matched by name pattern
    /// (<c>*oos*</c>, <c>*sync*</c>, <c>*.txt</c>, <c>*.log</c>, case-insensitive) —
    /// and reject anything over <see cref="GameFileMaxBytes"/>, so recorded games
    /// (<c>.age3Yrec</c>), savegames (<c>.age3Ysav</c>), configs and other binaries
    /// never enter the shared bundle.
    /// </summary>
    internal static bool ShouldIncludeGameFile(string name, long sizeBytes)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (sizeBytes < 0 || sizeBytes > GameFileMaxBytes) return false;

        bool nameMatch =
            name.Contains("oos", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("sync", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".log", StringComparison.OrdinalIgnoreCase);
        return nameMatch;
    }

    /// <summary>
    /// Copy the game user-data folder's OOS/sync/log artifacts into a
    /// <c>game-userdata/</c> subfolder of the staging area, and always write a
    /// <c>game-userdata-listing.txt</c> snapshot of the folder's top level — even
    /// when nothing matched — so a reporter (and we) can see exactly what AoE3 left
    /// there and learn the real dump names. Whole-operation best-effort: a failure
    /// here must never abort the bundle.
    /// </summary>
    private static void StageGameUserData(string gameUserDataDir, string staging)
    {
        try
        {
            if (!Directory.Exists(gameUserDataDir)) return;

            var outDir = Path.Combine(staging, "game-userdata");
            Directory.CreateDirectory(outDir);

            var listing = new System.Text.StringBuilder();
            listing.Append("Game user-data folder: ").AppendLine(gameUserDataDir);
            listing.AppendLine("Top-level entries (name | bytes | last write UTC):");
            listing.AppendLine();

            int copied = 0;
            foreach (var file in Directory.EnumerateFiles(gameUserDataDir, "*", SearchOption.TopDirectoryOnly))
            {
                long size;
                DateTime mtimeUtc;
                var name = Path.GetFileName(file);
                try
                {
                    var info = new FileInfo(file);
                    size = info.Length;
                    mtimeUtc = info.LastWriteTimeUtc;
                }
                catch { continue; }

                listing.Append(name).Append(" | ").Append(size).Append(" | ")
                       .AppendLine(mtimeUtc.ToString("u"));

                if (copied < GameFileMaxCount && ShouldIncludeGameFile(name, size))
                {
                    try { File.Copy(file, Path.Combine(outDir, name), overwrite: true); copied++; }
                    catch { /* skip a file we couldn't read; bundle the rest */ }
                }
            }

            listing.AppendLine().Append("Files copied into bundle: ").Append(copied)
                   .Append(" (cap ").Append(GameFileMaxCount).AppendLine(").");

            try { File.WriteAllText(Path.Combine(outDir, "game-userdata-listing.txt"), listing.ToString()); }
            catch { /* listing is a nice-to-have */ }
        }
        catch (Exception ex)
        {
            Write($"ExportBundle: could not stage game user-data '{gameUserDataDir}': {ex.Message}");
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
