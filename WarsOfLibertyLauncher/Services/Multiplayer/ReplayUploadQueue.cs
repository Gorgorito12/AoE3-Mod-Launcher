using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static WarsOfLibertyLauncher.Services.Multiplayer.ReplayUploadService;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>What the queue does with an upload after one attempt.</summary>
public enum ReplayUploadVerdict
{
    /// <summary>The match has its recording. Forget the entry.</summary>
    Done,
    /// <summary>Trying again would get the same refusal. Forget the entry.</summary>
    Drop,
    /// <summary>Nothing about this recording is wrong; the server or the network was. Keep it.</summary>
    Retry,
}

/// <summary>
/// The competitive recordings this launcher still owes the server, kept on disk until each one
/// is uploaded, refused for good, or a week old.
///
/// <para><b>Why a queue at all.</b> The upload used to be one attempt, fired as the match was
/// reported and forgotten whatever happened. The server accepts a recording at ANY later time —
/// its only condition is that the match has none yet — so a server whose storage was not
/// configured for an evening, or a connection that dropped as the game closed, lost that match's
/// recording for good. Measured: the first v1.0.16 match ever played was reported, rated, and
/// answered <c>503 replays_disabled</c> on the upload, and nothing ever asked again.</para>
///
/// <para><b>It keeps a COPY of the recording</b>, in <c>AppPaths.DataDir\replay-uploads\</c>,
/// never a path to the original: AoE3 calls every recording <c>Record Game N</c> and renumbers
/// them after each match, and <c>GameRecordingPurge</c> deletes the oldest automatic ones, so by
/// the time a retry runs the original may be gone or be a different game. The copy is hashed
/// when it is made and checked before it is sent.</para>
///
/// <para><b>Only the reporter can upload</b> (the server answers <c>403 not_reporter</c> to anyone
/// else), so an entry belongs to the account that reported the match and is tried only while that
/// account is signed in. <b>Switching sharing off drops every entry</b>, uploaded or not: the
/// opt-out covers what is still waiting.</para>
///
/// <para><b>The budget is the server's per-user limit</b> (10 requests a minute, 100 a day,
/// shared by both upload calls). A pass tries at most <see cref="MaxPerPass"/> entries, and an
/// answer that is about the SERVER rather than the recording (storage off, a 5xx, no network)
/// stops the pass and holds every other entry until the same time — so a server with no storage
/// costs one request per back-off step, not one per waiting match.</para>
/// </summary>
public sealed class ReplayUploadQueue
{
    /// <summary>The most recordings kept waiting; the oldest go first.</summary>
    public const int MaxEntries = 30;

    /// <summary>How long a recording waits before it is given up on.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

    /// <summary>The most uploads one pass tries, so a long queue cannot spend the server's
    /// per-minute budget in one go. Each upload is two requests.</summary>
    public const int MaxPerPass = 4;

    /// <summary>The wait after each failed attempt; the last one repeats.</summary>
    internal static readonly TimeSpan[] Delays =
    {
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2),
    };

    /// <summary>The same shape the server accepts for a match id. Anything else is refused,
    /// because the id names the copy on disk.</summary>
    private static readonly Regex MatchIdShape = new("^[A-Za-z0-9-]{1,64}$", RegexOptions.CultureInvariant);

    private const string IndexName = "pending.json";

    private readonly string _dir;
    private readonly Func<DateTime> _now;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _pass = new(1, 1);
    private List<Entry>? _entries;

    public ReplayUploadQueue(string dir, Func<DateTime>? utcNow = null)
    {
        _dir = dir;
        _now = utcNow ?? (() => DateTime.UtcNow);
    }

    /// <summary>The one the launcher uses.</summary>
    public static ReplayUploadQueue Default { get; } =
        new(Path.Combine(AppPaths.DataDir, "replay-uploads"));

    /// <summary>One recording waiting to be uploaded.</summary>
    public sealed class Entry
    {
        [JsonPropertyName("match_id")]
        public string MatchId { get; set; } = "";

        /// <summary>The account that reported the match — the only one the server accepts it from.</summary>
        [JsonPropertyName("user_id")]
        public string UserId { get; set; } = "";

        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; } = "";

        [JsonPropertyName("size_bytes")]
        public long SizeBytes { get; set; }

        /// <summary>The recording's own name when it was queued, for the log.</summary>
        [JsonPropertyName("source_name")]
        public string SourceName { get; set; } = "";

        [JsonPropertyName("created_utc")]
        public DateTime CreatedUtc { get; set; }

        [JsonPropertyName("attempts")]
        public int Attempts { get; set; }

        [JsonPropertyName("next_attempt_utc")]
        public DateTime NextAttemptUtc { get; set; }

        [JsonPropertyName("last_outcome")]
        public string? LastOutcome { get; set; }
    }

    /// <param name="Attempted">Uploads tried in this pass.</param>
    /// <param name="Uploaded">Of those, the ones the server now has.</param>
    /// <param name="Dropped">Entries forgotten: refused for good, unreadable, or opted out.</param>
    /// <param name="Waiting">Entries still waiting after the pass, for any account.</param>
    public readonly record struct PassSummary(int Attempted, int Uploaded, int Dropped, int Waiting);

    // ------------------------------------------------------------------ rules (pure)

    /// <summary>
    /// What one attempt means for the entry. <b>The Drop cases matter as much as the Retry ones</b>:
    /// retrying a refusal about the RECORDING (not the reporter, a casual room, the wrong file, too
    /// large) would ask the same question every back-off step for a week and get the same answer.
    /// Everything about the SERVER or the network is worth asking again.
    /// </summary>
    public static ReplayUploadVerdict Classify(ReplayUploadAttempt attempt)
    {
        if (attempt.Result == ReplayUploadResult.Uploaded) return ReplayUploadVerdict.Done;
        // Somebody already gave this match its recording — a second launcher of the same
        // account, or an earlier attempt whose confirmation was lost on the way back.
        if (attempt.Code == "already_uploaded") return ReplayUploadVerdict.Done;
        if (attempt.Result == ReplayUploadResult.TooLarge) return ReplayUploadVerdict.Drop;

        switch (attempt.Stage)
        {
            // The bytes themselves were unusable; they will be the same bytes next time.
            case ReplayUploadStage.Read: return ReplayUploadVerdict.Drop;
            // The bucket refused or the connection dropped mid-PUT. Each attempt signs a new URL,
            // so a stale signature or a clock problem is not repeated.
            case ReplayUploadStage.Put: return ReplayUploadVerdict.Retry;
        }

        // Asked the lobby server (or tried to).
        if (attempt.Status == 0) return ReplayUploadVerdict.Retry;          // no answer at all
        if (attempt.Status == 200) return ReplayUploadVerdict.Retry;        // an older server with no bucket
        if (attempt.Status == 401) return ReplayUploadVerdict.Retry;        // the session expired; signing in fixes it
        if (attempt.Status == 429 || attempt.Status >= 500) return ReplayUploadVerdict.Retry;
        if (attempt.Status == 404)
            // not_found: the match is gone. Anything else is "not uploaded yet" from the confirm,
            // or a server too old to have the route at all.
            return attempt.Code == "not_found" ? ReplayUploadVerdict.Drop : ReplayUploadVerdict.Retry;
        // 400, 403 not_reporter, 409 not_competitive / sha_mismatch: the answer will not change.
        return ReplayUploadVerdict.Drop;
    }

    /// <summary>
    /// Whether a retryable answer was about the SERVER rather than this recording — storage off,
    /// a 5xx, a rate limit, no network. Every other entry would get the same answer, so the pass
    /// stops and the rest wait with this one.
    /// </summary>
    internal static bool IsServerWide(ReplayUploadAttempt attempt)
        => attempt.Stage == ReplayUploadStage.Ask && Classify(attempt) == ReplayUploadVerdict.Retry;

    /// <summary>The wait after <paramref name="attemptsMade"/> failed attempts.</summary>
    public static TimeSpan NextDelay(int attemptsMade)
    {
        if (attemptsMade <= 0) return TimeSpan.Zero;
        return Delays[Math.Min(attemptsMade, Delays.Length) - 1];
    }

    /// <summary>
    /// The entries worth keeping: none older than <see cref="MaxAge"/>, and at most
    /// <see cref="MaxEntries"/> of the newest. The order of what is kept is the order given.
    /// </summary>
    public static List<Entry> Prune(IEnumerable<Entry> entries, DateTime nowUtc)
    {
        var fresh = entries.Where(e => nowUtc - e.CreatedUtc < MaxAge).ToList();
        if (fresh.Count <= MaxEntries) return fresh;
        var keep = fresh.OrderByDescending(e => e.CreatedUtc).Take(MaxEntries).ToHashSet();
        return fresh.Where(keep.Contains).ToList();
    }

    /// <summary>
    /// The entries this account may try now, oldest first — they run out of time first.
    /// <paramref name="ignoreSchedule"/> treats every one of them as due: a new session or a
    /// connection that came back is a reason to look again whatever the back-off said.
    /// </summary>
    public static List<Entry> Due(IEnumerable<Entry> entries, string? userId, DateTime nowUtc, bool ignoreSchedule = false)
    {
        if (string.IsNullOrEmpty(userId)) return new List<Entry>();
        return entries
            .Where(e => string.Equals(e.UserId, userId, StringComparison.Ordinal))
            .Where(e => ignoreSchedule || e.NextAttemptUtc <= nowUtc)
            .OrderBy(e => e.CreatedUtc)
            .ToList();
    }

    // ------------------------------------------------------------------ queue

    /// <summary>
    /// Keeps a copy of a match's recording until it is uploaded. Best-effort: a recording that
    /// cannot be kept is logged and skipped. Reads the file, so call it off the UI thread.
    /// </summary>
    /// <param name="reportedSha256">The fingerprint the match was reported with, when it had one —
    /// only to say in the log that the file has moved on since; the server is what decides.</param>
    /// <returns>True when the recording is waiting (or already was).</returns>
    public bool Enqueue(string matchId, string userId, FileInfo file, string? reportedSha256 = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (string.IsNullOrEmpty(matchId) || !MatchIdShape.IsMatch(matchId))
        {
            DiagnosticLog.Write($"ReplayUploadQueue: '{matchId}' is not a match id - not queuing.");
            return false;
        }
        if (string.IsNullOrEmpty(userId))
        {
            DiagnosticLog.Write($"ReplayUploadQueue: nobody is signed in - not queuing match {matchId}.");
            return false;
        }

        byte[] bytes;
        try
        {
            file.Refresh();
            if (!file.Exists)
            {
                DiagnosticLog.Write($"ReplayUploadQueue: '{file.Name}' is gone - nothing to queue for match {matchId}.");
                return false;
            }
            if (file.Length > MaxReplayBytes)
            {
                DiagnosticLog.Write($"ReplayUploadQueue: '{file.Name}' is {file.Length} bytes, over the {MaxReplayBytes} cap - not queuing.");
                return false;
            }
            bytes = File.ReadAllBytes(file.FullName);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"ReplayUploadQueue: could not read '{file.Name}' for match {matchId} - {ex.Message}");
            return false;
        }
        if (bytes.Length == 0)
        {
            DiagnosticLog.Write($"ReplayUploadQueue: '{file.Name}' is empty - not queuing match {matchId}.");
            return false;
        }

        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.IsNullOrEmpty(reportedSha256)
            && !string.Equals(sha, reportedSha256, StringComparison.OrdinalIgnoreCase))
        {
            DiagnosticLog.Write(
                $"ReplayUploadQueue: '{file.Name}' no longer hashes to what match {matchId} was reported with - "
                + "queuing it anyway; the server decides.");
        }

        lock (_gate)
        {
            var entries = Load();
            if (entries.Any(e => string.Equals(e.MatchId, matchId, StringComparison.Ordinal)))
            {
                DiagnosticLog.Write($"ReplayUploadQueue: match {matchId} is already waiting.");
                return true;
            }

            try
            {
                Directory.CreateDirectory(_dir);
                var copy = CopyPathOf(matchId);
                var tmp = copy + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllBytes(tmp, bytes);
                File.Move(tmp, copy, overwrite: true);
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"ReplayUploadQueue: could not keep a copy for match {matchId} - {ex.Message}");
                return false;
            }

            var now = _now();
            entries.Add(new Entry
            {
                MatchId = matchId,
                UserId = userId,
                Sha256 = sha,
                SizeBytes = bytes.LongLength,
                SourceName = file.Name,
                CreatedUtc = now,
                NextAttemptUtc = now,
            });
            Replace(Prune(entries, now));
            DiagnosticLog.Write(
                $"ReplayUploadQueue: queued '{file.Name}' ({bytes.Length} bytes) for match {matchId}; "
                + $"{_entries!.Count} waiting.");
            return true;
        }
    }

    /// <summary>Whether anything is waiting for this account.</summary>
    public bool HasPendingFor(string? userId)
    {
        if (string.IsNullOrEmpty(userId)) return false;
        lock (_gate) return Load().Any(e => string.Equals(e.UserId, userId, StringComparison.Ordinal));
    }

    /// <summary>
    /// Forgets every recording waiting, for every account, and deletes the copies. What switching
    /// sharing off does. Returns how many were forgotten.
    /// </summary>
    public int Clear()
    {
        lock (_gate)
        {
            var count = Load().Count;
            if (count > 0)
            {
                Replace(new List<Entry>());
                DiagnosticLog.Write($"ReplayUploadQueue: sharing was switched off - forgot {count} recording(s) waiting.");
            }
            return count;
        }
    }

    /// <summary>Everything waiting, for any account. A copy: the caller cannot change the queue through it.</summary>
    public IReadOnlyList<Entry> Snapshot()
    {
        lock (_gate)
            return Load().Select(CloneEntry).ToList();
    }

    /// <summary>
    /// Tries the entries of <paramref name="userId"/> that are due. Never throws except for
    /// cancellation, and never runs twice at once: a pass already running answers for this one.
    /// </summary>
    /// <param name="upload">Sends one recording. The launcher passes
    /// <see cref="ReplayUploadService.UploadBytesAsync"/>; tests pass a fake.</param>
    /// <param name="policy"><see cref="Models.LauncherConfig.ReplayUploadPolicy"/>. "never" drops
    /// everything waiting, for every account, without sending anything.</param>
    /// <param name="ignoreSchedule">Try every entry of this account now, back-off or not — once per
    /// session and when the network comes back.</param>
    public async Task<PassSummary> DrainAsync(
        Func<Entry, byte[], CancellationToken, Task<ReplayUploadAttempt>> upload,
        string? userId,
        string? policy,
        bool ignoreSchedule = false,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(upload);
        if (!await _pass.WaitAsync(0, ct).ConfigureAwait(false))
            return new PassSummary(0, 0, 0, Count());

        try
        {
            List<Entry> due;
            var dropped = 0;
            lock (_gate)
            {
                var entries = Load();
                if (!IsSharingEnabled(policy))
                {
                    if (entries.Count > 0)
                    {
                        DiagnosticLog.Write(
                            $"ReplayUploadQueue: sharing is off - dropping {entries.Count} recording(s) that were waiting.");
                        dropped = entries.Count;
                        Replace(new List<Entry>());
                    }
                    return new PassSummary(0, 0, dropped, 0);
                }

                var now = _now();
                var kept = Prune(entries, now);
                if (kept.Count != entries.Count)
                {
                    dropped += entries.Count - kept.Count;
                    DiagnosticLog.Write(
                        $"ReplayUploadQueue: gave up on {entries.Count - kept.Count} recording(s) older than {MaxAge.TotalDays:0} days.");
                    Replace(kept);
                }
                due = Due(_entries!, userId, now, ignoreSchedule).Take(MaxPerPass).ToList();
            }

            var attempted = 0;
            var uploaded = 0;
            foreach (var entry in due)
            {
                ct.ThrowIfCancellationRequested();

                var bytes = ReadCopy(entry);
                if (bytes == null)
                {
                    lock (_gate) Remove(entry.MatchId);
                    dropped++;
                    continue;
                }

                attempted++;
                ReplayUploadAttempt attempt;
                try { attempt = await upload(entry, bytes, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    // UploadBytesAsync never throws; anything else that does is not the
                    // recording's fault, so it is kept and tried again.
                    DiagnosticLog.Write($"ReplayUploadQueue: match {entry.MatchId} - the upload crashed: {ex.Message}");
                    attempt = new ReplayUploadAttempt(ReplayUploadResult.Failed, ReplayUploadStage.Put);
                }
                var verdict = Classify(attempt);
                var outcome = $"{attempt.Result} at {attempt.Stage}"
                              + (attempt.Status > 0 ? $" (HTTP {attempt.Status}{(attempt.Code != null ? " " + attempt.Code : "")})" : "");

                lock (_gate)
                {
                    if (verdict != ReplayUploadVerdict.Retry)
                    {
                        Remove(entry.MatchId);
                        if (verdict == ReplayUploadVerdict.Done) uploaded++;
                        else dropped++;
                        DiagnosticLog.Write(
                            $"ReplayUploadQueue: match {entry.MatchId} - {outcome} - "
                            + (verdict == ReplayUploadVerdict.Done ? "done." : "the server will not take it; forgotten."));
                        continue;
                    }

                    var live = Load().FirstOrDefault(e => string.Equals(e.MatchId, entry.MatchId, StringComparison.Ordinal));
                    if (live == null) continue;   // dropped meanwhile (sharing switched off)
                    var now = _now();
                    live.Attempts++;
                    live.NextAttemptUtc = now + NextDelay(live.Attempts);
                    live.LastOutcome = outcome;

                    var serverWide = IsServerWide(attempt);
                    if (serverWide)
                    {
                        // Every other entry of this account would get the same answer: hold them
                        // with this one rather than spend a request each to learn it.
                        foreach (var other in _entries!)
                            if (!ReferenceEquals(other, live)
                                && string.Equals(other.UserId, live.UserId, StringComparison.Ordinal)
                                && other.NextAttemptUtc < live.NextAttemptUtc)
                                other.NextAttemptUtc = live.NextAttemptUtc;
                    }
                    Save(_entries!);
                    DiagnosticLog.Write(
                        $"ReplayUploadQueue: match {entry.MatchId} - {outcome} - attempt {live.Attempts}, "
                        + $"trying again in {Describe(live.NextAttemptUtc - now)}"
                        + (serverWide ? "; the others wait with it." : "."));
                    if (serverWide) break;
                }
            }

            return new PassSummary(attempted, uploaded, dropped, Count());
        }
        finally
        {
            _pass.Release();
        }
    }

    // ------------------------------------------------------------------ disk

    private int Count()
    {
        lock (_gate) return Load().Count;
    }

    private string IndexPath => Path.Combine(_dir, IndexName);

    private string CopyPathOf(string matchId) => Path.Combine(_dir, matchId + ReplayExtension);

    /// <summary>The kept copy, or null when it is missing or no longer the bytes that were queued.</summary>
    private byte[]? ReadCopy(Entry entry)
    {
        try
        {
            var path = CopyPathOf(entry.MatchId);
            if (!File.Exists(path))
            {
                DiagnosticLog.Write($"ReplayUploadQueue: the copy for match {entry.MatchId} is gone - forgotten.");
                return null;
            }
            var bytes = File.ReadAllBytes(path);
            var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (!string.Equals(sha, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                DiagnosticLog.Write($"ReplayUploadQueue: the copy for match {entry.MatchId} changed on disk - forgotten.");
                return null;
            }
            return bytes;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"ReplayUploadQueue: could not read the copy for match {entry.MatchId} - {ex.Message}");
            return null;
        }
    }

    /// <summary>The entries in a saved index; an empty list for a missing or unreadable one.</summary>
    internal static List<Entry> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<Entry>();
        try
        {
            var list = JsonSerializer.Deserialize<List<Entry>>(json);
            return list?.Where(e => MatchIdShape.IsMatch(e.MatchId ?? "")
                                    && !string.IsNullOrEmpty(e.UserId)
                                    && !string.IsNullOrEmpty(e.Sha256))
                       .GroupBy(e => e.MatchId, StringComparer.Ordinal)
                       .Select(g => g.First())
                       .ToList()
                   ?? new List<Entry>();
        }
        catch (JsonException)
        {
            return new List<Entry>();
        }
    }

    /// <summary>
    /// The entries, read once. The first read also removes what a crash could have left behind: a
    /// copy no entry names, a half-written one, and an entry whose copy is gone.
    /// </summary>
    private List<Entry> Load()
    {
        if (_entries != null) return _entries;
        try
        {
            _entries = File.Exists(IndexPath) ? Parse(File.ReadAllText(IndexPath)) : new List<Entry>();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"ReplayUploadQueue: could not read '{IndexPath}' - {ex.Message}");
            _entries = new List<Entry>();
        }

        try
        {
            if (Directory.Exists(_dir))
            {
                var named = _entries.Select(e => CopyPathOf(e.MatchId)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var path in Directory.EnumerateFiles(_dir))
                {
                    if (string.Equals(Path.GetFileName(path), IndexName, StringComparison.OrdinalIgnoreCase)) continue;
                    if (named.Contains(path)) continue;
                    try { File.Delete(path); } catch (Exception) { /* next time */ }
                }
            }
            var missing = _entries.RemoveAll(e => !File.Exists(CopyPathOf(e.MatchId)));
            if (missing > 0)
            {
                DiagnosticLog.Write($"ReplayUploadQueue: {missing} waiting recording(s) had lost their copy - forgotten.");
                Save(_entries);
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"ReplayUploadQueue: could not tidy '{_dir}' - {ex.Message}");
        }

        if (_entries.Count > 0)
            DiagnosticLog.Write($"ReplayUploadQueue: {_entries.Count} recording(s) waiting to be uploaded.");
        return _entries;
    }

    /// <summary>Makes <paramref name="kept"/> the queue, deleting the copies of what is left out.</summary>
    private void Replace(List<Entry> kept)
    {
        var old = _entries ?? new List<Entry>();
        var keptIds = kept.Select(e => e.MatchId).ToHashSet(StringComparer.Ordinal);
        foreach (var gone in old.Where(e => !keptIds.Contains(e.MatchId)))
            DeleteCopy(gone.MatchId);
        _entries = kept;
        Save(kept);
    }

    private void Remove(string matchId)
    {
        var entries = Load();
        if (entries.RemoveAll(e => string.Equals(e.MatchId, matchId, StringComparison.Ordinal)) > 0)
        {
            DeleteCopy(matchId);
            Save(entries);
        }
    }

    private void DeleteCopy(string matchId)
    {
        try { File.Delete(CopyPathOf(matchId)); }
        catch (Exception ex) { DiagnosticLog.Write($"ReplayUploadQueue: could not delete the copy for match {matchId} - {ex.Message}"); }
    }

    private void Save(List<Entry> entries)
    {
        try
        {
            Directory.CreateDirectory(_dir);
            var tmp = IndexPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(entries));
            File.Move(tmp, IndexPath, overwrite: true);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"ReplayUploadQueue: could not save '{IndexPath}' - {ex.Message}");
        }
    }

    private static Entry CloneEntry(Entry e) => new()
    {
        MatchId = e.MatchId,
        UserId = e.UserId,
        Sha256 = e.Sha256,
        SizeBytes = e.SizeBytes,
        SourceName = e.SourceName,
        CreatedUtc = e.CreatedUtc,
        Attempts = e.Attempts,
        NextAttemptUtc = e.NextAttemptUtc,
        LastOutcome = e.LastOutcome,
    };

    private static string Describe(TimeSpan wait)
        => wait.TotalHours >= 1 ? $"{wait.TotalHours:0.#} h"
         : wait.TotalMinutes >= 1 ? $"{wait.TotalMinutes:0} min"
         : $"{Math.Max(0, wait.TotalSeconds):0} s";
}
