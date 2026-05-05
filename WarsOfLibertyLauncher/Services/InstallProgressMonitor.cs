using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Watches the Inno Setup installation log file in real time and reports
/// progress to the launcher UI.
///
/// Why this exists:
///   When Inno Setup runs in /VERYSILENT mode, no GUI is shown. The user has
///   no idea whether the install is making progress or hung. By tailing the
///   log file we can show a meaningful "Installing files: 42% (75,000 of
///   180,000)" indicator inside our own UI.
///
/// How it works:
///   Inno Setup writes one line per file as it copies, in formats like:
///     2026-05-03 17:30:15.123  -- File entry --
///     2026-05-03 17:30:15.123  Dest filename: C:\Wars of Liberty\art\foo.bar
///   We count the "Dest filename:" lines and divide by an estimated total to
///   produce a percentage.
/// </summary>
public class InstallProgressMonitor
{
    /// <summary>
    /// Estimated total file count for the WoL installer. Used as the
    /// denominator when computing percentage. The installer log doesn't
    /// announce the total upfront, so we use a known approximate value.
    ///
    /// If a future installer has a different file count, the percentage will
    /// just hit 100% earlier or stop at 90% — nothing breaks.
    /// </summary>
    public int EstimatedTotalFiles { get; set; } = 180_000;

    public record InstallProgress(int FilesCopied, double Percentage, string LastFile);

    /// <summary>
    /// Tails the log file until cancelled. Reports progress on each new
    /// "Dest filename" line found.
    /// </summary>
    public async Task MonitorAsync(
        string logPath,
        IProgress<InstallProgress>? progress,
        CancellationToken ct)
    {
        // The installer may take a moment to create the log file
        while (!File.Exists(logPath) && !ct.IsCancellationRequested)
        {
            await Task.Delay(200, ct);
        }
        if (ct.IsCancellationRequested) return;

        long lastReadPosition = 0;
        int filesCopied = 0;
        string lastFile = "";
        var lineBuffer = new System.Text.StringBuilder();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // FileShare.ReadWrite is essential — Inno Setup has the file
                // open for writing while we read it.
                using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read,
                                              FileShare.ReadWrite | FileShare.Delete);
                if (lastReadPosition > fs.Length)
                {
                    // Log was truncated/rotated — start over
                    lastReadPosition = 0;
                }

                fs.Seek(lastReadPosition, SeekOrigin.Begin);

                var buffer = new byte[8192];
                int read;
                while ((read = await fs.ReadAsync(buffer, ct)) > 0)
                {
                    var chunk = System.Text.Encoding.UTF8.GetString(buffer, 0, read);
                    lineBuffer.Append(chunk);
                }
                lastReadPosition = fs.Position;

                // Process complete lines
                var content = lineBuffer.ToString();
                int lastNewline = content.LastIndexOf('\n');
                if (lastNewline >= 0)
                {
                    var completeLines = content[..lastNewline];
                    lineBuffer.Remove(0, lastNewline + 1);

                    foreach (var line in completeLines.Split('\n'))
                    {
                        // Inno Setup writes destination paths with a "Dest filename:" prefix
                        var idx = line.IndexOf("Dest filename:", StringComparison.OrdinalIgnoreCase);
                        if (idx >= 0)
                        {
                            filesCopied++;
                            lastFile = line[(idx + "Dest filename:".Length)..].Trim();
                            double pct = Math.Min(99.0,
                                (double)filesCopied / EstimatedTotalFiles * 100.0);
                            progress?.Report(new InstallProgress(filesCopied, pct, lastFile));
                        }
                    }
                }
            }
            catch (IOException)
            {
                // File temporarily locked — try again on next tick
            }

            await Task.Delay(500, ct);
        }
    }
}
