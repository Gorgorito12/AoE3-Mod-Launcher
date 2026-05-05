using System;
using System.IO;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Simple file logger for diagnostics. Writes to launcher-debug.log next to the .exe.
/// On launcher startup the log is reset, so each session is self-contained.
/// </summary>
public static class DiagnosticLog
{
    private static readonly string LogPath =
        Path.Combine(AppContext.BaseDirectory, "launcher-debug.log");

    private static readonly object Lock = new();

    public static void Reset()
    {
        try
        {
            File.WriteAllText(LogPath,
                $"=== Wars of Liberty Launcher debug log ===\n" +
                $"Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n\n");
        }
        catch
        {
            // If we can't even create the log, oh well — don't crash the app.
        }
    }

    public static void Write(string message)
    {
        lock (Lock)
        {
            try
            {
                File.AppendAllText(LogPath,
                    $"[{DateTime.Now:HH:mm:ss}] {message}\n");
            }
            catch
            {
                // Best-effort; logging failures must never break the app.
            }
        }
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
}
