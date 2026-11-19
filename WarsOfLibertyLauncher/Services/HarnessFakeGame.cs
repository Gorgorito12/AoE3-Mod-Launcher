using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// A game that "runs" for a set time and then leaves a REAL recording behind, so the testbed can
/// drive a whole multiplayer match with two accounts on one machine and no Age of Empires III.
///
/// <para>It exists because the post-match pipeline — reading the recording, the host's report,
/// the guest's confirmation, the civilizations, the result card — is the part of the launcher a
/// single player can never exercise, and it is where the bugs that cost people their rating
/// lived. Everything after the "exit" is the production code, untouched: only the process is
/// missing.</para>
///
/// <para><b>Reachable only after <see cref="HarnessMode.Start"/>-style blocking already
/// happened</b> (the caller is the harness branch of <c>GameLauncher.LaunchAndWatch</c>), and only
/// when the run has its own Documents folder. The recording is written inside that folder and
/// nowhere else; a path that would land outside it is refused. Pinned by
/// <c>HarnessFakeGameTests</c>, where the refusals are the point.</para>
/// </summary>
public static class HarnessFakeGame
{
    /// <summary>Names the JSON file describing the next simulated game.</summary>
    public const string Variable = "AOE3ML_FAKE_GAME";

    /// <summary>
    /// What the testbed writes: how long the game lasts and which real recording it leaves.
    /// The match must last at least three minutes to be reported, so the testbed asks for ~190 s.
    /// </summary>
    internal sealed record Spec(
        [property: JsonPropertyName("seconds")] int Seconds,
        [property: JsonPropertyName("recording")] string Recording);

    /// <summary>
    /// Null unless the harness is on, the run has its own Documents, and the file says something
    /// usable. Whether the recording exists is checked when the game starts, so this stays pure.
    /// </summary>
    internal static Spec? SpecFor(bool active, string? documentsRoot, string? json)
    {
        if (!active || string.IsNullOrWhiteSpace(documentsRoot) || string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var spec = JsonSerializer.Deserialize<Spec>(json);
            if (spec == null || spec.Seconds < 1 || string.IsNullOrWhiteSpace(spec.Recording)) return null;
            return spec;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Where the recording is dropped: <c>&lt;folder&gt;\Savegame\Record Game &lt;stamp&gt;.age3Yrec</c>,
    /// or null when that folder is not inside the run's own Documents (whole path segments, the
    /// same rule as the sandbox). The stamp keeps two matches of one run from overwriting each
    /// other, and the name is the one AoE3 itself uses, so the search treats it like any other.
    /// </summary>
    internal static string? DropPath(string? documentsRoot, string? userDataFolder, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(documentsRoot) || string.IsNullOrWhiteSpace(userDataFolder)) return null;
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(documentsRoot)) + Path.DirectorySeparatorChar;
            var folder = Path.GetFullPath(userDataFolder);
            if (!folder.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
            return Path.Combine(folder, "Savegame", $"Record Game {nowUtc:yyyyMMdd-HHmmss}.age3Yrec");
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Starts a simulated game for <paramref name="profile"/>, or returns null when the run did
    /// not ask for one (the caller then reports a blocked launch, as before).
    ///
    /// <para>The returned launch has no <see cref="System.Diagnostics.Process"/>, so nothing can
    /// ever be killed, and a pid that is not a multiple of four, so it can never be a real one.</para>
    /// </summary>
    public static WatchedLaunch? TryStart(LauncherConfig config, ModProfile profile, EventHandler onExited)
    {
        var documents = HarnessMode.DocumentsRoot;
        string? json = null;
        var specFile = Environment.GetEnvironmentVariable(Variable);
        try
        {
            // Re-read on every launch: the testbed points the next match at another recording.
            if (!string.IsNullOrWhiteSpace(specFile) && File.Exists(specFile)) json = File.ReadAllText(specFile);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"HARNESS fake game: could not read '{specFile}': {ex.Message}");
        }

        var spec = SpecFor(HarnessMode.Active, documents, json);
        if (spec == null) return null;
        if (!File.Exists(spec.Recording))
        {
            DiagnosticLog.Write($"HARNESS fake game: recording '{spec.Recording}' does not exist.");
            return null;
        }

        var folder = UserDataService.GetUserDataFolder(UserDataService.ResolveMatchFolderName(profile, config));
        var endsAt = DateTime.UtcNow.AddSeconds(spec.Seconds);
        DiagnosticLog.Write(
            $"HARNESS fake game: '{profile.Id}' runs {spec.Seconds} s, then leaves '{Path.GetFileName(spec.Recording)}' in '{folder}'.");

        var dropped = 0;
        var watcher = new GameExitWatcher(
            isAlive: () =>
            {
                if (DateTime.UtcNow < endsAt) return true;
                if (System.Threading.Interlocked.Exchange(ref dropped, 1) == 0)
                    Drop(spec.Recording, documents, folder);
                return false;
            },
            onExited: () => onExited(null, EventArgs.Empty));
        watcher.Start(TimeSpan.FromSeconds(1));

        var exe = profile.GameExecutable ?? "";
        return new WatchedLaunch(null, int.MaxValue, exe, NeededElevation: false, ExitWatcherAttached: false);
    }

    /// <summary>
    /// Copies the recording with a fresh write time: the search ignores anything older than the
    /// launch, and a copy keeps its source's date. Done once, by the probe that first sees the
    /// game "gone", so the file is there before the exit is reported.
    /// </summary>
    private static void Drop(string recording, string? documents, string? folder)
    {
        var target = DropPath(documents, folder, DateTime.UtcNow);
        if (target == null)
        {
            DiagnosticLog.Write("HARNESS fake game: refused to write a recording outside the run's Documents.");
            return;
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!File.Exists(target)) File.Copy(recording, target);
            File.SetLastWriteTimeUtc(target, DateTime.UtcNow);
            DiagnosticLog.Write($"HARNESS fake game: exited, recording at '{target}'.");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"HARNESS fake game: could not write the recording: {ex.Message}");
        }
    }
}
