using System;
using System.Diagnostics;
using System.IO;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Show one file to the user in Explorer, <b>selected</b> — not merely somewhere on screen.
///
/// <para><b>Why selecting matters here, and why opening the folder is not the same thing.</b>
/// AoE3 names every recording <c>Record Game N.age3Yrec</c> and RENUMBERS after each match, so
/// the newest is always number 1. Measured on a real diagnostic bundle: three matches in one
/// evening were each read from a file called <c>Record Game 1.age3Yrec</c>, and by the time the
/// player went looking, that name belonged to a different game. Opening the folder shows ten
/// files with interchangeable names; selecting one answers the question.</para>
///
/// <para><b>Never throws.</b> Revealing a file is a convenience hanging off a result card, and
/// a shell that will not start must not take the card down with it.</para>
/// </summary>
public static class FileReveal
{
    /// <summary>
    /// Reveal <paramref name="path"/> in Explorer, falling back to its folder when the file
    /// itself is gone. Returns whether anything was opened.
    ///
    /// <para><b>The fallback is not politeness — the file genuinely moves.</b> A stored path
    /// goes stale two ways: the renumbering above renames it, and
    /// <see cref="GameRecordingPurge"/> deletes automatic recordings past the newest ten. So a
    /// path captured minutes ago may name nothing, and dropping the click in silence would look
    /// exactly like a broken button.</para>
    /// </summary>
    public static bool Reveal(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        try
        {
            if (File.Exists(path))
            {
                // /select wants ONE quoted absolute path after the comma. A Windows path
                // cannot contain a quote, so there is nothing here to escape.
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{Path.GetFullPath(path)}\"",
                    UseShellExecute = true,
                });
                return true;
            }

            var folder = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return false;

            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"FileReveal.Reveal('{path}'): {ex.Message}");
            return false;
        }
    }
}
