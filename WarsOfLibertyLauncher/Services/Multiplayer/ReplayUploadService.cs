using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Locates a newly-written AoE3 replay and uploads it to the lobby
/// Worker. Called by <see cref="MultiplayerSession"/> after a match
/// completes (or by the host after <c>POST /matches</c> returns a
/// match id).
///
/// AoE3 (2007) writes replays into
/// <c>%USERPROFILE%\Documents\My Games\&lt;mod&gt;\Savegame\</c> with
/// the extension <c>.age3yrec</c> — same path as the game's regular
/// save dir. The convention is to take the newest file written after
/// the match started; we keep the trigger explicit (not a continuous
/// watcher) so the user can opt out of uploading a given replay by
/// simply deleting it before clicking "upload" in the post-game UI.
/// </summary>
public static class ReplayUploadService
{
    /// <summary>
    /// Find the most recently written replay inside the mod's user-data
    /// folder, filtered to only files created after <paramref name="afterUtc"/>.
    /// Returns null when no such file exists, e.g. the user aborted out
    /// before the engine flushed the recording.
    /// </summary>
    public static FileInfo? FindLatestReplay(string userDataDir, DateTime afterUtc)
    {
        try
        {
            if (string.IsNullOrEmpty(userDataDir) || !Directory.Exists(userDataDir))
                return null;

            // AoE3 stores replays under "Savegame" by convention. Some
            // mods (e.g. WoL) keep the same layout. If the folder is
            // missing, fall back to a recursive search — slower but
            // robust to mod-specific paths.
            var saveDir = Path.Combine(userDataDir, "Savegame");
            var searchRoot = Directory.Exists(saveDir) ? saveDir : userDataDir;

            var candidates = new DirectoryInfo(searchRoot)
                .EnumerateFiles("*.age3yrec", SearchOption.AllDirectories)
                .Where(f => f.LastWriteTimeUtc >= afterUtc)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(1)
                .ToList();

            return candidates.FirstOrDefault();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"ReplayUploadService.FindLatestReplay: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// How many READABLE recordings to judge before giving up. A folder can hold hundreds, and
    /// each candidate costs an inflate, so the walk is bounded — the right file is
    /// among the newest few or it is not there.
    ///
    /// <para>Counts candidates that actually parsed, not files opened. A recording still being
    /// flushed used to consume one of these, so a handful of half-written files could hide the
    /// real one behind a budget that had already been spent on nothing.</para>
    /// </summary>
    internal const int MaxCandidatesExamined = 5;

    /// <summary>
    /// Hard ceiling on files opened, so a folder full of unreadable ones cannot spin. Higher than
    /// <see cref="MaxCandidatesExamined"/> precisely so a few unreadable files no longer crowd out
    /// the readable ones.
    /// </summary>
    internal const int MaxCandidatesOpened = 12;

    /// <summary>What a candidate turned out to be. Three states, because the third is retryable.</summary>
    public enum CandidateVerdict
    {
        /// <summary>Ours — stop here.</summary>
        Match,
        /// <summary>Parsed fine and belongs to some other game. Waiting will not change it.</summary>
        NotOurs,
        /// <summary>Could not be read: still being written, locked, or corrupt. Worth another look.</summary>
        Unreadable,
    }

    /// <param name="File">The match's recording, or null when none qualified.</param>
    /// <param name="Parsed">Candidates that could be read and judged.</param>
    /// <param name="Unreadable">Candidates that could not — the only reason to try again.</param>
    public readonly record struct ReplaySearch(FileInfo? File, int Parsed, int Unreadable);

    /// <summary>
    /// Whether the search is worth repeating.
    ///
    /// <para><b>Two reasons to wait, and neither of them is "a file that isn't ours".</b> A
    /// recording that parsed cleanly and belongs to someone else will parse identically in three
    /// seconds, so retrying over it buys nothing. What IS worth waiting for is an <b>unreadable</b>
    /// candidate — very often the engine still flushing the file we want, since the search runs
    /// the instant the process dies — and <b>an empty folder</b>: nothing newer than the launch
    /// existed yet, which is what a recording written a moment after the process exits looks
    /// like.</para>
    ///
    /// <para><b>The empty case used to be excluded, and that was a real bug</b>: it required
    /// <c>Unreadable &gt; 0</c>, so a file that had not been CREATED yet got zero retries and the
    /// match reported all-draws at once. It was excluded for a good reason, though — waiting is
    /// pure latency for the majority of matches, which have no recording at all. That reason is
    /// gone now only because <b>the report no longer waits for this</b>: the caller reports on the
    /// first attempt and lets the remaining ones run behind it, so an extra pass costs nobody
    /// anything. If that ever changes, this branch has to go back.</para>
    /// </summary>
    public static bool ShouldRetry(ReplaySearch search, int attempt, int maxAttempts)
        => search.File == null
           && (search.Unreadable > 0 || search.Parsed == 0)
           && attempt < maxAttempts - 1;

    /// <summary>
    /// Finds the recording that belongs to the match that just ended, newest first,
    /// returning the first one <paramref name="belongsToMatch"/> accepts.
    ///
    /// <para><b>Why this is not just "the newest".</b> Replays other people send you live
    /// in the same <c>Savegame\</c> folder — that is where the game looks for them — and
    /// their timestamp is when they were copied, not when they were played. Two such
    /// files sat on the maintainer's disk eleven minutes newer than his own games, so a
    /// match played in between would have picked a stranger's recording and reported its
    /// result. Walking past the ones that fail the check turns that from a wrong answer
    /// into the right one.</para>
    ///
    /// <para>Null when nothing qualifies, which the caller must treat as "no result":
    /// having no replay is a normal outcome (a game killed before the engine flushed
    /// one) and is always safer than using a file that isn't ours.</para>
    /// </summary>
    /// <param name="preferBeforeUtc">
    /// Optional upper edge of the match's own window — normally the moment the game closed, plus
    /// a margin. Candidates written at or before it are judged FIRST, newest of them leading.
    ///
    /// <para><b>A preference, deliberately not a filter.</b> The window has only ever had a floor
    /// (newer than the launch), so a file written long afterwards still qualifies — and that is
    /// not theoretical: a player renaming his recordings to send them over Discord, minutes after
    /// the match, put a freshly-stamped file in <c>Savegame\</c> while the launcher was still
    /// looking. Ordering by the window puts the match's own recording first without ever
    /// REJECTING one, which matters because the recording is finished as the game closes and its
    /// timestamp keeps moving while the retries run: a ceiling that was even slightly tight would
    /// start discarding legitimate recordings, which is precisely the symptom this whole area
    /// exists to fix. Null keeps the previous newest-first order exactly.</para>
    /// </param>
    public static ReplaySearch FindMatchReplay(
        string userDataDir,
        DateTime afterUtc,
        Func<FileInfo, CandidateVerdict> examine,
        DateTime? preferBeforeUtc = null)
    {
        if (examine == null) throw new ArgumentNullException(nameof(examine));

        var parsed = 0;
        var unreadable = 0;

        try
        {
            if (string.IsNullOrEmpty(userDataDir) || !Directory.Exists(userDataDir))
                return new ReplaySearch(null, 0, 0);

            var saveDir = Path.Combine(userDataDir, "Savegame");
            var searchRoot = Directory.Exists(saveDir) ? saveDir : userDataDir;

            // Ordered newest-first and taken lazily: the two budgets below decide when to stop,
            // so an unreadable file no longer costs a slot that a readable one needed.
            //
            // The first key is the window (see preferBeforeUtc): true sorts before false under
            // OrderByDescending, so anything inside the match's own window is judged first and
            // anything outside it is still judged afterwards rather than dropped.
            var candidates = new DirectoryInfo(searchRoot)
                .EnumerateFiles("*.age3yrec", SearchOption.AllDirectories)
                .Where(f => f.LastWriteTimeUtc >= afterUtc)
                .OrderByDescending(f => preferBeforeUtc == null || f.LastWriteTimeUtc <= preferBeforeUtc.Value)
                .ThenByDescending(f => f.LastWriteTimeUtc)
                .Take(MaxCandidatesOpened);

            foreach (var candidate in candidates)
            {
                if (parsed >= MaxCandidatesExamined) break;

                CandidateVerdict verdict;
                // One unreadable candidate — still being written, locked, corrupt — must
                // not end the walk; the file we want may be the next one.
                try { verdict = examine(candidate); }
                catch (Exception ex)
                {
                    DiagnosticLog.Write($"Replay: '{candidate.Name}' could not be checked: {ex.Message}");
                    unreadable++;
                    continue;
                }

                switch (verdict)
                {
                    case CandidateVerdict.Match:
                        return new ReplaySearch(candidate, parsed + 1, unreadable);
                    case CandidateVerdict.Unreadable:
                        unreadable++;
                        DiagnosticLog.Write($"Replay: '{candidate.Name}' could not be read yet.");
                        break;
                    default:
                        parsed++;
                        DiagnosticLog.Write($"Replay: '{candidate.Name}' is not this match — skipping.");
                        break;
                }
            }

            if (parsed + unreadable > 0)
                DiagnosticLog.Write(
                    $"Replay: none of the recent recordings belong to this match " +
                    $"(readable={parsed} unreadable={unreadable}).");
            return new ReplaySearch(null, parsed, unreadable);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"ReplayUploadService.FindMatchReplay: {ex.Message}");
            return new ReplaySearch(null, parsed, unreadable);
        }
    }

    /// <summary>
    /// Upload a replay file to the Worker for the given match id.
    /// Enforces the size cap up front so a 500 MB recording from a
    /// 4-hour FFA isn't streamed across the network just to be
    /// rejected at the end. Returns the server-side object key on
    /// success, or null on error (already logged).
    /// </summary>
    public static async Task<string?> UploadAsync(
        LobbyApiClient api,
        string matchId,
        FileInfo replayFile,
        CancellationToken ct = default)
    {
        if (api == null) throw new ArgumentNullException(nameof(api));
        if (replayFile == null || !replayFile.Exists)
        {
            DiagnosticLog.Write("ReplayUploadService.UploadAsync: file missing");
            return null;
        }

        try
        {
            var handle = await api.RequestReplayUploadAsync(matchId, ct);
            if (replayFile.Length > handle.MaxBytes)
            {
                DiagnosticLog.Write(
                    $"ReplayUploadService: replay {replayFile.Length} > cap {handle.MaxBytes}, skipping");
                return null;
            }

            await using var stream = replayFile.OpenRead();
            await api.UploadReplayAsync(handle.UploadUrl, stream, replayFile.Length, ct);
            return handle.UploadUrl;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"ReplayUploadService.UploadAsync: {ex.Message}");
            return null;
        }
    }
}
