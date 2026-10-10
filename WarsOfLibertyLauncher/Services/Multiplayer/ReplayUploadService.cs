using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Finds the recording AoE3 wrote for a match, and uploads the recording of a COMPETITIVE
/// match so any player can download it later.
///
/// AoE3 (2007) writes recordings into
/// <c>%USERPROFILE%\Documents\My Games\&lt;mod&gt;\Savegame\</c> with the extension
/// <c>.age3Yrec</c>. Finding the one that belongs to a match is <see cref="FindMatchReplay"/>.
///
/// <para><b>The upload never passes through the lobby server.</b> The server answers
/// <c>POST /replays/upload-url</c> with a presigned PUT on an S3-compatible bucket (Oracle
/// Object Storage) and the bytes go straight there, on a client of their own that never
/// carries the lobby's bearer token — an Authorization header on a presigned URL is a second
/// signature and the storage refuses the request. A RELATIVE upload URL is what the old
/// backend answered (it stored recordings on its own disk), and is refused rather than
/// followed. Only the player who reported the match uploads, only for a competitive room, and
/// only while <see cref="LauncherConfig.ReplayUploadPolicy"/> is not <c>"never"</c>
/// (<see cref="Decide"/>). It runs in the background and never delays the report, and a
/// failed upload is kept and tried again by <see cref="ReplayUploadQueue"/>.</para>
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

    /// <summary>
    /// The same ceiling for a COMPETITIVE match, where the trade-off is reversed.
    ///
    /// <para>The ordinary limits are tuned for the common case: almost no match is recorded, so
    /// opening files is usually work spent learning nothing. A competitive room inverts that by
    /// construction — the host confirmed Record Game before the countdown, so a recording SHOULD
    /// exist — and there the seconds are worth spending, because what is at stake is somebody's
    /// rating rather than a line in a history nobody reads.</para>
    /// </summary>
    internal const int MaxCandidatesOpenedCompetitive = 24;

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
        DateTime? preferBeforeUtc = null,
        bool thorough = false)
    {
        if (examine == null) throw new ArgumentNullException(nameof(examine));

        // Only the ceiling on files OPENED moves. MaxCandidatesExamined stays where it is: it
        // counts recordings that actually parsed, and judging more than five real ones would not
        // find a sixth answer — the right file is among the newest few or it is not there.
        var maxOpened = thorough ? MaxCandidatesOpenedCompetitive : MaxCandidatesOpened;

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
                .Take(maxOpened);

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

    // ---------------------------------------------------------------- upload

    /// <summary>
    /// The largest recording this launcher will try to send. Mirrors the server's default
    /// <c>REPLAY_MAX_BYTES</c>; the server's own answer (<see cref="ReplayUploadHandle.MaxBytes"/>)
    /// is what decides. Checked BEFORE the file is read into memory. Real recordings are
    /// 0.8-2 MB.
    /// </summary>
    public const long MaxReplayBytes = 20L * 1024 * 1024;

    /// <summary>The extension AoE3 gives a recording, and the one a download is saved with.</summary>
    public const string ReplayExtension = ".age3Yrec";

    /// <summary>Why a recording is, or is not, uploaded after a match.</summary>
    public enum ReplayUploadDecision { Upload, NotCompetitive, OptedOut, NoFile, NotVerified, NoMatchId }

    /// <summary>How an upload ended. Never an exception: an upload must not break the match flow.</summary>
    public enum ReplayUploadResult { Uploaded, Disabled, Refused, TooLarge, Failed }

    /// <summary>The step an upload reached. Read by <see cref="ReplayUploadQueue.Classify"/>.</summary>
    public enum ReplayUploadStage
    {
        /// <summary>Checking the bytes in hand, before anything was asked.</summary>
        Read,
        /// <summary><c>POST /replays/upload-url</c>.</summary>
        Ask,
        /// <summary>The PUT to the storage bucket.</summary>
        Put,
        /// <summary><c>POST /replays/confirm</c>.</summary>
        Confirm,
        /// <summary>Uploaded and recorded on the match.</summary>
        Done,
    }

    /// <summary>
    /// The code this launcher gives an answer that offered no presigned storage URL: a server
    /// older than the bucket, which kept recordings on its own disk. Not a code the server sends.
    /// </summary>
    public const string NoStorageUrlCode = "no_storage_url";

    /// <summary>
    /// How one upload attempt ended, with what the server said. <see cref="Status"/> is 0 when no
    /// HTTP answer arrived (a network failure, a timeout); <see cref="Code"/> is the lobby
    /// server's error code when it gave one. The queue decides from these whether to try again.
    /// </summary>
    public readonly record struct ReplayUploadAttempt(
        ReplayUploadResult Result, ReplayUploadStage Stage, int Status = 0, string? Code = null);

    /// <summary>
    /// Whether the player shares their competitive recordings. On unless they chose
    /// <c>"never"</c>: the older <c>"ask"</c> default reads as on, which is the implicit consent
    /// decided for this feature (a competitive room states it before the match).
    /// </summary>
    public static bool IsSharingEnabled(string? policy) =>
        !string.Equals(policy?.Trim(), "never", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The upload gate, in the order the reasons are logged. Pure, so the refusals — a casual
    /// room, a player who opted out, no file, a file not proved to be this match's, no match
    /// id — are pinned by tests.
    /// </summary>
    /// <param name="verified">
    /// Whether the recording was checked against the match (the player's own name in it and
    /// the room's head count). The launcher also keeps an UNCHECKED file — the newest one, when
    /// it did not know the player's name or the room's roster — to name it in the chat. That one
    /// is never uploaded: it can be a recording somebody sent the player, and it would be stored
    /// on the match as if it were this game.
    /// </param>
    public static ReplayUploadDecision Decide(
        bool isCompetitive, string? policy, bool fileExists, bool verified, string? matchId)
    {
        if (!isCompetitive) return ReplayUploadDecision.NotCompetitive;
        if (!IsSharingEnabled(policy)) return ReplayUploadDecision.OptedOut;
        if (!fileExists) return ReplayUploadDecision.NoFile;
        if (!verified) return ReplayUploadDecision.NotVerified;
        if (string.IsNullOrWhiteSpace(matchId)) return ReplayUploadDecision.NoMatchId;
        return ReplayUploadDecision.Upload;
    }

    /// <summary>
    /// A storage URL this launcher will send a file to, or fetch one from: absolute, https, and
    /// one <see cref="SafeUrl"/> allows. The old backend's relative <c>/replays/upload/…</c>
    /// fails this on purpose — the file must never be streamed through the lobby server.
    /// </summary>
    public static bool IsAcceptableStorageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
        return SafeUrl.IsAllowed(url);
    }

    private static readonly string[] ReservedWindowsNames =
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// The name a download is offered under. The server suggests one, and it is still not a
    /// path this launcher follows: only its last segment is kept, characters Windows refuses
    /// become <c>_</c>, a device name is prefixed, the length is capped, and the extension is
    /// always <see cref="ReplayExtension"/> — AoE3's "Load recorded game" lists nothing else.
    /// </summary>
    public static string SafeReplayFileName(string? serverName, string? matchId)
    {
        var name = (serverName ?? "").Replace('\\', '/');
        var slash = name.LastIndexOf('/');
        if (slash >= 0) name = name[(slash + 1)..];

        name = Clean(name);
        if (name.EndsWith(ReplayExtension, StringComparison.OrdinalIgnoreCase))
            name = name[..^ReplayExtension.Length];
        name = name.Trim().Trim('.', ' ');

        if (name.Length == 0) name = Clean(matchId ?? "").Trim('.', ' ');
        if (name.Length == 0) name = "replay";
        if (name.Length > 120) name = name[..120].TrimEnd('.', ' ');
        if (ReservedWindowsNames.Contains(name, StringComparer.OrdinalIgnoreCase)) name = "_" + name;
        return name + ReplayExtension;

        static string Clean(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(value.Length);
            foreach (var c in value)
                sb.Append(char.IsControl(c) || Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return sb.ToString().Trim();
        }
    }

    /// <summary>
    /// The HTTP client for the storage bucket. Separate from <see cref="LobbyApiClient"/>'s on
    /// purpose: that one carries a bearer token and an X-Launcher-Version header by default,
    /// and neither may reach a presigned URL. Five minutes, because a slow upload of a 2 MB
    /// file is still a successful one.
    /// </summary>
    private static readonly HttpClient s_storageHttp = new() { Timeout = TimeSpan.FromMinutes(5) };

    /// <summary>
    /// Uploads a competitive match's recording: ask the lobby server for a presigned PUT, send
    /// the bytes straight to the bucket, then confirm so the server records it on the match.
    /// Never throws except for cancellation; every outcome is logged and returned with what
    /// the server said, so <see cref="ReplayUploadQueue"/> can decide whether to try again.
    /// </summary>
    /// <param name="bytes">The recording, as hashed. The queue keeps its own copy: AoE3
    /// renumbers its recordings after every match, so the file on disk may have moved on.</param>
    /// <param name="sha256">The SHA-256 of <paramref name="bytes"/>, lower-case hex.</param>
    /// <param name="label">What to call the recording in the log.</param>
    public static async Task<ReplayUploadAttempt> UploadBytesAsync(
        LobbyApiClient api,
        string matchId,
        byte[] bytes,
        string sha256,
        string label,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(bytes);
        var stage = ReplayUploadStage.Read;
        try
        {
            if (bytes.Length == 0)
            {
                DiagnosticLog.Write($"ReplayUploadService: '{label}' is empty - not uploading match {matchId}.");
                return new ReplayUploadAttempt(ReplayUploadResult.Failed, stage);
            }
            if (bytes.LongLength > MaxReplayBytes)
            {
                DiagnosticLog.Write($"ReplayUploadService: '{label}' is {bytes.Length} bytes, over the {MaxReplayBytes} cap - not uploading.");
                return new ReplayUploadAttempt(ReplayUploadResult.TooLarge, stage);
            }

            stage = ReplayUploadStage.Ask;
            ReplayUploadHandle handle;
            try
            {
                handle = await api.RequestReplayUploadAsync(matchId, bytes.LongLength, sha256, ct).ConfigureAwait(false);
            }
            catch (LobbyApiException ex)
            {
                return new ReplayUploadAttempt(
                    Classify(ex, matchId, "asking for an upload URL"), stage, ex.Status, ex.Code);
            }

            if (handle == null || !IsAcceptableStorageUrl(handle.UploadUrl))
            {
                DiagnosticLog.Write(
                    $"ReplayUploadService: the server offered no presigned storage URL for match {matchId} "
                    + $"('{Shorten(handle?.UploadUrl)}') - an older server that keeps recordings itself. Not uploading.");
                return new ReplayUploadAttempt(ReplayUploadResult.Disabled, stage, 200, NoStorageUrlCode);
            }
            if (!string.Equals(handle.Method, "PUT", StringComparison.OrdinalIgnoreCase))
            {
                DiagnosticLog.Write($"ReplayUploadService: the server asked for '{handle.Method}', not PUT - not uploading.");
                return new ReplayUploadAttempt(ReplayUploadResult.Disabled, stage, 200, NoStorageUrlCode);
            }
            if (handle.MaxBytes > 0 && bytes.LongLength > handle.MaxBytes)
            {
                DiagnosticLog.Write($"ReplayUploadService: {bytes.Length} bytes is over the server's {handle.MaxBytes} - not uploading.");
                return new ReplayUploadAttempt(ReplayUploadResult.TooLarge, stage, 413, "too_large");
            }

            stage = ReplayUploadStage.Put;
            var put = await PutToStorageAsync(s_storageHttp, new Uri(handle.UploadUrl), bytes, ct).ConfigureAwait(false);
            if (!put.Success)
            {
                DiagnosticLog.Write(
                    $"ReplayUploadService: the storage refused the upload for match {matchId} - HTTP {put.Status} {Shorten(put.Body, 300)}");
                return new ReplayUploadAttempt(ReplayUploadResult.Failed, stage, put.Status);
            }

            stage = ReplayUploadStage.Confirm;
            try
            {
                await api.ConfirmReplayUploadAsync(matchId, ct).ConfigureAwait(false);
            }
            catch (LobbyApiException ex)
            {
                DiagnosticLog.Write(
                    $"ReplayUploadService: uploaded match {matchId} but the server did not record it - HTTP {ex.Status} {ex.Code}: {ex.Message}");
                return new ReplayUploadAttempt(
                    ex.Status == 503 ? ReplayUploadResult.Disabled : ReplayUploadResult.Failed,
                    stage, ex.Status, ex.Code);
            }

            MultiplayerTelemetry.Bump(MultiplayerTelemetry.ReplayUploaded);
            DiagnosticLog.Write($"ReplayUploadService: uploaded '{label}' ({bytes.Length} bytes) for match {matchId}.");
            return new ReplayUploadAttempt(ReplayUploadResult.Uploaded, ReplayUploadStage.Done, 200);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // No HTTP answer at all: a dropped connection, a timeout, a DNS failure.
            DiagnosticLog.Write(
                $"ReplayUploadService: upload for match {matchId} failed at {stage} - {ex.GetType().Name}: {ex.Message}");
            return new ReplayUploadAttempt(ReplayUploadResult.Failed, stage);
        }
    }

    /// <summary>
    /// What a refusal from the lobby server means for this upload. 503 <c>replays_disabled</c>
    /// and 404 (a server without the route) are "this server keeps no recordings", not a
    /// failure; 413 is the size; anything else is the server saying no for a reason it names.
    /// </summary>
    private static ReplayUploadResult Classify(LobbyApiException ex, string matchId, string step)
    {
        var result = ex.Status switch
        {
            503 or 404 => ReplayUploadResult.Disabled,
            413 => ReplayUploadResult.TooLarge,
            _ => ReplayUploadResult.Refused,
        };
        DiagnosticLog.Write(
            $"ReplayUploadService: {step} for match {matchId} - HTTP {ex.Status} {ex.Code}: {ex.Message} ({result}).");
        return result;
    }

    internal readonly record struct StoragePutOutcome(bool Success, int Status, string? Body);

    /// <summary>
    /// PUTs the bytes to a presigned storage URL, exactly as signed: octet-stream, the exact
    /// Content-Length, and NO Authorization header. One retry, only for a transient network
    /// failure (<see cref="DownloadService.IsTransientDownloadFailure"/>) — never for an answer
    /// from the storage, which would only repeat itself. Internal for the tests.
    /// </summary>
    internal static async Task<StoragePutOutcome> PutToStorageAsync(
        HttpClient http, Uri url, byte[] bytes, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Put, url);
                var content = new ByteArrayContent(bytes);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                content.Headers.ContentLength = bytes.LongLength;
                req.Content = content;

                using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode) return new StoragePutOutcome(true, (int)resp.StatusCode, null);

                string? body = null;
                try { body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false); }
                catch (Exception) when (!ct.IsCancellationRequested) { /* the status is enough */ }
                return new StoragePutOutcome(false, (int)resp.StatusCode, body);
            }
            catch (Exception ex) when (attempt == 1
                                       && DownloadService.IsTransientDownloadFailure(ex, ct.IsCancellationRequested))
            {
                DiagnosticLog.Write($"ReplayUploadService: upload interrupted ({ex.Message}) - retrying once.");
                await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
            }
        }
    }

    private static string Shorten(string? text, int max = 120)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var flat = text.Replace('\r', ' ').Replace('\n', ' ');
        return flat.Length <= max ? flat : flat[..max] + "...";
    }
}
