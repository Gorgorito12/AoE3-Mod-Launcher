using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Keeps the recordings the launcher caused from filling the disk, without ever deleting one the
/// player might want.
///
/// <para><b>Why this exists.</b> The launcher switches Age of Empires III's game recording on,
/// because the recording is the only place a match result is written. Measured on real files, each
/// game then costs 765&#160;KB – 2.1&#160;MB, forever, and the game never cleans up after
/// itself.</para>
///
/// <para><b>The rule: only files the GAME named.</b> Age of Empires III writes
/// <c>Record Game 1.age3Yrec</c> and counts up. Renaming one — "Code vs Nathan 2" — is the player
/// saying they care about it, so a renamed recording is never deleted and never counts against the
/// budget either. That mirrors how the launcher treats the rest of a player's files: it hides
/// stale install copies rather than removing them, and refuses to strip anything from an install
/// it did not add.</para>
///
/// <para>The decision is pure and tested; only <see cref="Run"/> touches the disk.</para>
/// </summary>
public static class GameRecordingPurge
{
    /// <summary>How many automatic recordings to keep. Roughly 14&#160;MB at the measured size.</summary>
    public const int KeepNewest = 10;

    /// <summary>The extension Age of Empires III writes, as it appears on disk.</summary>
    public const string Extension = ".age3Yrec";

    /// <summary>
    /// The name the game generates on its own — <c>cStringRecordGameFileName</c> ("Record Game")
    /// followed by a number.
    ///
    /// <para>Anchored at both ends, and the number is required: the game always numbers, so a bare
    /// "Record Game.age3Yrec" was made by a person. Deliberately narrow — a name we don't
    /// recognise is kept, which is the safe direction. Note the game's filename comes from a
    /// string table, so a localized install may write something else entirely; there the purge
    /// simply does nothing rather than guessing.</para>
    /// </summary>
    private static readonly Regex AutoName =
        new(@"^Record Game \d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>A recording as the selector sees it — no disk, no <c>FileInfo</c>.</summary>
    public readonly record struct RecordingFile(string Name, DateTime LastWriteUtc);

    /// <summary>Whether this is a recording the game named itself, and so one we may clean up.</summary>
    public static bool IsAutoNamed(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        if (!string.Equals(Path.GetExtension(fileName), Extension, StringComparison.OrdinalIgnoreCase))
            return false;

        return AutoName.IsMatch(Path.GetFileNameWithoutExtension(fileName));
    }

    /// <summary>
    /// The automatic recordings past the budget, newest kept.
    ///
    /// <para>Renamed recordings are neither deleted nor counted, so keeping twenty of them never
    /// pushes an automatic one out. Nothing written at or after <paramref name="protectAfterUtc"/>
    /// is ever returned — belt-and-braces so the match that just finished, which the launcher may
    /// still be reading to work out who won, cannot be swept up by its own cleanup.</para>
    ///
    /// <para>Ordered by write time and then by name, so the outcome is deterministic even when two
    /// files share a timestamp.</para>
    /// </summary>
    public static IReadOnlyList<string> SelectForDeletion(
        IReadOnlyList<RecordingFile> files, int keepNewest, DateTime protectAfterUtc)
    {
        if (files == null || files.Count == 0) return Array.Empty<string>();

        return files
            .Where(f => IsAutoNamed(f.Name))
            .Where(f => f.LastWriteUtc < protectAfterUtc)
            .OrderByDescending(f => f.LastWriteUtc)
            .ThenByDescending(f => f.Name, StringComparer.Ordinal)
            .Skip(Math.Max(0, keepNewest))
            .Select(f => f.Name)
            .ToList();
    }

    /// <summary>
    /// Deletes the surplus automatic recordings under a mod's user-data folder. Best-effort and
    /// silent: a purge that announces itself is a nag, and the behaviour is disclosed on the
    /// setting that causes it.
    ///
    /// <para>Top level of <c>Savegame\</c> only — a subfolder is something the player made, and the
    /// game does not create them. That deliberately differs from the recursive search used to FIND
    /// a match's recording: looking further is harmless, deleting further is not.</para>
    /// </summary>
    public static void Run(string userDataDir, DateTime protectAfterUtc)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(userDataDir)) return;

            var saves = Path.Combine(userDataDir, "Savegame");
            if (!Directory.Exists(saves)) return;

            var files = Directory
                .GetFiles(saves, "*" + Extension, SearchOption.TopDirectoryOnly)
                .Select(p => new FileInfo(p))
                .ToList();

            var doomed = SelectForDeletion(
                files.Select(f => new RecordingFile(f.Name, f.LastWriteTimeUtc)).ToList(),
                KeepNewest, protectAfterUtc);
            if (doomed.Count == 0) return;

            var bytes = 0L;
            var removed = 0;
            foreach (var name in doomed)
            {
                try
                {
                    var file = files.First(f => string.Equals(f.Name, name, StringComparison.Ordinal));
                    bytes += file.Length;
                    file.Delete();
                    removed++;
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Write($"GameRecording: could not delete '{name}': {ex.Message}");
                }
            }

            DiagnosticLog.Write(
                $"GameRecording: cleaned up {removed} automatic recording(s) ({bytes / 1024} KB) " +
                $"in {saves}, keeping the newest {KeepNewest} and every renamed one.");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"GameRecording: cleanup failed: {ex.Message}");
        }
    }
}
