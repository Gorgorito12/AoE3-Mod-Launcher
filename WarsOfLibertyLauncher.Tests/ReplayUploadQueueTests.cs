using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;
using static WarsOfLibertyLauncher.Services.Multiplayer.ReplayUploadService;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// What the upload queue does with each answer. The refusals matter as much as the retries: a
/// refusal about the RECORDING retried for a week asks the same question a hundred times, and a
/// retry about the SERVER dropped loses a recording the server would have taken an hour later —
/// which is exactly what happened to the first match played on v1.0.16.
/// </summary>
public class ReplayUploadQueueClassifyTests
{
    private static ReplayUploadAttempt Ask(int status, string? code, ReplayUploadResult result = ReplayUploadResult.Refused)
        => new(result, ReplayUploadStage.Ask, status, code);

    [Fact]
    public void AnUploadedRecordingIsDone()
    {
        Assert.Equal(ReplayUploadVerdict.Done,
            ReplayUploadQueue.Classify(new(ReplayUploadResult.Uploaded, ReplayUploadStage.Done, 200)));
    }

    [Fact]
    public void AMatchThatAlreadyHasItsRecordingIsDone()
    {
        Assert.Equal(ReplayUploadVerdict.Done, ReplayUploadQueue.Classify(Ask(409, "already_uploaded")));
    }

    [Theory]
    [InlineData(403, "not_reporter")]
    [InlineData(409, "not_competitive")]
    [InlineData(409, "sha_mismatch")]
    [InlineData(404, "not_found")]
    [InlineData(400, "bad_request")]
    public void ARefusalAboutTheRecordingIsDropped(int status, string code)
    {
        Assert.Equal(ReplayUploadVerdict.Drop, ReplayUploadQueue.Classify(Ask(status, code)));
    }

    [Fact]
    public void TooLargeIsDroppedWhereverItWasNoticed()
    {
        Assert.Equal(ReplayUploadVerdict.Drop,
            ReplayUploadQueue.Classify(new(ReplayUploadResult.TooLarge, ReplayUploadStage.Read)));
        Assert.Equal(ReplayUploadVerdict.Drop,
            ReplayUploadQueue.Classify(Ask(413, "too_large", ReplayUploadResult.TooLarge)));
        Assert.Equal(ReplayUploadVerdict.Drop,
            ReplayUploadQueue.Classify(new(ReplayUploadResult.Failed, ReplayUploadStage.Confirm, 413, "too_large")));
    }

    [Fact]
    public void BytesThatCannotBeSentAreDropped()
    {
        Assert.Equal(ReplayUploadVerdict.Drop,
            ReplayUploadQueue.Classify(new(ReplayUploadResult.Failed, ReplayUploadStage.Read)));
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_AServerWithNoStorageIsAskedAgain()
    {
        // The first v1.0.16 match: the storage was not configured, and the recording was lost.
        Assert.Equal(ReplayUploadVerdict.Retry,
            ReplayUploadQueue.Classify(Ask(503, "replays_disabled", ReplayUploadResult.Disabled)));
    }

    [Theory]
    [InlineData(0, null)]                    // no answer at all
    [InlineData(401, "unauthorized")]        // the session expired
    [InlineData(429, "rate_limited")]
    [InlineData(500, "internal")]
    [InlineData(502, "storage_error")]
    [InlineData(404, "http_error")]          // a server too old to have the route
    [InlineData(200, NoStorageUrlCode)]      // a server that keeps recordings on its own disk
    public void AnAnswerAboutTheServerIsAskedAgain(int status, string? code)
    {
        Assert.Equal(ReplayUploadVerdict.Retry, ReplayUploadQueue.Classify(Ask(status, code)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(403)]
    [InlineData(500)]
    public void AFailedPutIsAskedAgain_WithANewSignature(int status)
    {
        Assert.Equal(ReplayUploadVerdict.Retry,
            ReplayUploadQueue.Classify(new(ReplayUploadResult.Failed, ReplayUploadStage.Put, status)));
    }

    [Theory]
    [InlineData(404, "not_uploaded")]
    [InlineData(502, "storage_error")]
    [InlineData(503, "replays_disabled")]
    public void AConfirmationTheServerCouldNotRecordIsAskedAgain(int status, string code)
    {
        Assert.Equal(ReplayUploadVerdict.Retry,
            ReplayUploadQueue.Classify(new(ReplayUploadResult.Failed, ReplayUploadStage.Confirm, status, code)));
    }

    [Fact]
    public void OnlyARetryAtTheFirstQuestionHoldsTheOthers()
    {
        Assert.True(ReplayUploadQueue.IsServerWide(Ask(503, "replays_disabled", ReplayUploadResult.Disabled)));
        Assert.True(ReplayUploadQueue.IsServerWide(Ask(0, null)));
        Assert.False(ReplayUploadQueue.IsServerWide(Ask(403, "not_reporter")));
        Assert.False(ReplayUploadQueue.IsServerWide(
            new(ReplayUploadResult.Failed, ReplayUploadStage.Confirm, 404, "not_uploaded")));
        Assert.False(ReplayUploadQueue.IsServerWide(new(ReplayUploadResult.Failed, ReplayUploadStage.Put)));
    }
}

/// <summary>The schedule rules, pure.</summary>
public class ReplayUploadQueueRuleTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 21, 0, 0, DateTimeKind.Utc);

    private static ReplayUploadQueue.Entry E(string id, string user = "u1", double ageHours = 0, double dueInMinutes = 0)
        => new()
        {
            MatchId = id,
            UserId = user,
            Sha256 = new string('a', 64),
            CreatedUtc = Now.AddHours(-ageHours),
            NextAttemptUtc = Now.AddMinutes(dueInMinutes),
        };

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 30)]
    [InlineData(2, 120)]
    [InlineData(3, 600)]
    [InlineData(4, 1800)]
    [InlineData(5, 7200)]
    [InlineData(40, 7200)]
    public void TheBackOffGrowsAndThenStays(int attempts, int seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), ReplayUploadQueue.NextDelay(attempts));
    }

    [Fact]
    public void ARecordingOlderThanAWeekIsGivenUp()
    {
        var kept = ReplayUploadQueue.Prune(new[] { E("old", ageHours: 24 * 7 + 1), E("new", ageHours: 24 * 6) }, Now);
        Assert.Equal(new[] { "new" }, kept.Select(e => e.MatchId));
    }

    [Fact]
    public void AtMostThirtyWait_TheNewestKept()
    {
        var all = Enumerable.Range(0, 40).Select(i => E("m" + i, ageHours: i)).ToList();
        var kept = ReplayUploadQueue.Prune(all, Now);
        Assert.Equal(ReplayUploadQueue.MaxEntries, kept.Count);
        Assert.Equal(all.Take(ReplayUploadQueue.MaxEntries).Select(e => e.MatchId), kept.Select(e => e.MatchId));
    }

    [Fact]
    public void OnlyTheReportersOwnEntriesAreDue_OldestFirst()
    {
        var due = ReplayUploadQueue.Due(
            new[] { E("young", ageHours: 1), E("other", user: "u2", ageHours: 3), E("old", ageHours: 2) }, "u1", Now);
        Assert.Equal(new[] { "old", "young" }, due.Select(e => e.MatchId));
    }

    [Fact]
    public void AnEntryWaitsForItsBackOff_UnlessTheSessionIsNew()
    {
        var entries = new[] { E("later", dueInMinutes: 10) };
        Assert.Empty(ReplayUploadQueue.Due(entries, "u1", Now));
        Assert.Single(ReplayUploadQueue.Due(entries, "u1", Now, ignoreSchedule: true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NobodySignedInMeansNothingIsDue(string? user)
    {
        Assert.Empty(ReplayUploadQueue.Due(new[] { E("m1") }, user, Now, ignoreSchedule: true));
    }
}

/// <summary>The queue on disk, with a fake upload and a clock the test moves.</summary>
public sealed class ReplayUploadQueueDiskTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rq-" + Guid.NewGuid().ToString("N"));
    private string QueueDir => Path.Combine(_root, "queue");
    private DateTime _now = new(2026, 10, 9, 21, 0, 0, DateTimeKind.Utc);

    public ReplayUploadQueueDiskTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private ReplayUploadQueue NewQueue() => new(QueueDir, () => _now);

    private FileInfo Recording(string name, int size = 1024)
    {
        var path = Path.Combine(_root, name);
        var bytes = new byte[size];
        new Random(name.GetHashCode()).NextBytes(bytes);
        File.WriteAllBytes(path, bytes);
        return new FileInfo(path);
    }

    private sealed class FakeServer
    {
        public readonly List<string> Asked = new();
        public Func<ReplayUploadQueue.Entry, ReplayUploadAttempt> Answer =
            _ => new(ReplayUploadResult.Uploaded, ReplayUploadStage.Done, 200);

        public Task<ReplayUploadAttempt> Upload(ReplayUploadQueue.Entry e, byte[] bytes, CancellationToken ct)
        {
            Asked.Add(e.MatchId);
            return Task.FromResult(Answer(e));
        }
    }

    private static readonly ReplayUploadAttempt Disabled =
        new(ReplayUploadResult.Disabled, ReplayUploadStage.Ask, 503, "replays_disabled");

    [Fact]
    public async Task THE_ONE_THAT_MATTERS_ARecordingTheServerCouldNotTakeIsSentWhenItCan()
    {
        var queue = NewQueue();
        Assert.True(queue.Enqueue("6433c705-53f2-4432-b06d-3a1c0a780e0d", "u1", Recording("Record Game 1.age3Yrec")));

        var server = new FakeServer { Answer = _ => Disabled };
        var first = await queue.DrainAsync(server.Upload, "u1", "always");
        Assert.Equal((1, 0, 0, 1), (first.Attempted, first.Uploaded, first.Dropped, first.Waiting));

        // The launcher closes; the storage is configured; the launcher opens again.
        _now = _now.AddHours(5);
        var reopened = NewQueue();
        server.Answer = _ => new(ReplayUploadResult.Uploaded, ReplayUploadStage.Done, 200);
        var second = await reopened.DrainAsync(server.Upload, "u1", "always", ignoreSchedule: true);

        Assert.Equal((1, 1, 0), (second.Attempted, second.Uploaded, second.Waiting));
        Assert.False(reopened.HasPendingFor("u1"));
        Assert.Empty(Directory.EnumerateFiles(QueueDir, "*.age3Yrec"));
    }

    [Fact]
    public async Task TheCopyIsWhatIsSent_EvenAfterTheOriginalIsRenumbered()
    {
        var queue = NewQueue();
        var original = Recording("Record Game 1.age3Yrec");
        var expected = File.ReadAllBytes(original.FullName);
        queue.Enqueue("m1", "u1", original);
        File.Delete(original.FullName);

        byte[]? sent = null;
        await queue.DrainAsync((e, bytes, ct) =>
        {
            sent = bytes;
            return Task.FromResult(new ReplayUploadAttempt(ReplayUploadResult.Uploaded, ReplayUploadStage.Done, 200));
        }, "u1", "always");

        Assert.Equal(expected, sent);
    }

    [Fact]
    public async Task ARetryWaitsForItsBackOff()
    {
        var queue = NewQueue();
        queue.Enqueue("m1", "u1", Recording("a.age3Yrec"));
        var server = new FakeServer { Answer = _ => Disabled };

        await queue.DrainAsync(server.Upload, "u1", "always");
        await queue.DrainAsync(server.Upload, "u1", "always");          // too soon
        Assert.Single(server.Asked);

        _now = _now.AddSeconds(31);
        await queue.DrainAsync(server.Upload, "u1", "always");
        Assert.Equal(2, server.Asked.Count);

        var entry = Assert.Single(queue.Snapshot());
        Assert.Equal(2, entry.Attempts);
        Assert.Equal(_now + TimeSpan.FromMinutes(2), entry.NextAttemptUtc);
    }

    [Fact]
    public async Task AServerWithNoStorageCostsOneRequestNotOnePerMatch()
    {
        var queue = NewQueue();
        queue.Enqueue("m1", "u1", Recording("a.age3Yrec"));
        _now = _now.AddSeconds(1);
        queue.Enqueue("m2", "u1", Recording("b.age3Yrec"));
        _now = _now.AddSeconds(1);
        queue.Enqueue("m3", "u1", Recording("c.age3Yrec"));

        var server = new FakeServer { Answer = _ => Disabled };
        await queue.DrainAsync(server.Upload, "u1", "always");

        Assert.Equal(new[] { "m1" }, server.Asked);
        var held = queue.Snapshot().Select(e => e.NextAttemptUtc).Distinct().ToList();
        Assert.Single(held);
    }

    [Fact]
    public async Task ARefusalAboutOneRecordingDoesNotHoldTheOthers()
    {
        var queue = NewQueue();
        queue.Enqueue("m1", "u1", Recording("a.age3Yrec"));
        _now = _now.AddSeconds(1);
        queue.Enqueue("m2", "u1", Recording("b.age3Yrec"));

        var server = new FakeServer
        {
            Answer = e => e.MatchId == "m1"
                ? new(ReplayUploadResult.Refused, ReplayUploadStage.Ask, 409, "sha_mismatch")
                : new(ReplayUploadResult.Uploaded, ReplayUploadStage.Done, 200),
        };
        var pass = await queue.DrainAsync(server.Upload, "u1", "always");

        Assert.Equal(new[] { "m1", "m2" }, server.Asked);
        Assert.Equal((1, 1, 0), (pass.Uploaded, pass.Dropped, pass.Waiting));
    }

    [Fact]
    public async Task APassTriesNoMoreThanItsShare()
    {
        var queue = NewQueue();
        for (var i = 0; i < 6; i++)
        {
            queue.Enqueue("m" + i, "u1", Recording($"r{i}.age3Yrec"));
            _now = _now.AddSeconds(1);
        }
        var server = new FakeServer();

        var pass = await queue.DrainAsync(server.Upload, "u1", "always");

        Assert.Equal(ReplayUploadQueue.MaxPerPass, pass.Attempted);
        Assert.Equal(2, pass.Waiting);
    }

    [Fact]
    public async Task AnotherAccountsRecordingWaitsForThatAccount()
    {
        var queue = NewQueue();
        queue.Enqueue("m1", "u2", Recording("a.age3Yrec"));
        var server = new FakeServer();

        await queue.DrainAsync(server.Upload, "u1", "always", ignoreSchedule: true);

        Assert.Empty(server.Asked);
        Assert.True(queue.HasPendingFor("u2"));
        Assert.False(queue.HasPendingFor("u1"));
    }

    [Fact]
    public async Task SwitchingSharingOffForgetsEverythingWaiting_AndSendsNothing()
    {
        var queue = NewQueue();
        queue.Enqueue("m1", "u1", Recording("a.age3Yrec"));
        queue.Enqueue("m2", "u2", Recording("b.age3Yrec"));
        var server = new FakeServer();

        var pass = await queue.DrainAsync(server.Upload, "u1", "never");

        Assert.Empty(server.Asked);
        Assert.Equal((2, 0), (pass.Dropped, pass.Waiting));
        Assert.Empty(Directory.EnumerateFiles(QueueDir, "*.age3Yrec"));
    }

    [Fact]
    public void SwitchingSharingOffInSettingsForgetsAtOnce_EvenSignedOut()
    {
        var queue = NewQueue();
        queue.Enqueue("m1", "u1", Recording("a.age3Yrec"));
        queue.Enqueue("m2", "u2", Recording("b.age3Yrec"));

        Assert.Equal(2, queue.Clear());

        Assert.Empty(queue.Snapshot());
        Assert.Empty(Directory.EnumerateFiles(QueueDir, "*.age3Yrec"));
        Assert.Empty(NewQueue().Snapshot());
    }

    [Fact]
    public async Task AWeekLaterTheRecordingIsGivenUp()
    {
        var queue = NewQueue();
        queue.Enqueue("m1", "u1", Recording("a.age3Yrec"));
        _now = _now.AddDays(7).AddMinutes(1);
        var server = new FakeServer();

        var pass = await queue.DrainAsync(server.Upload, "u1", "always", ignoreSchedule: true);

        Assert.Empty(server.Asked);
        Assert.Equal((1, 0), (pass.Dropped, pass.Waiting));
    }

    [Fact]
    public async Task ACopyThatChangedOnDiskIsNeverSent()
    {
        var queue = NewQueue();
        queue.Enqueue("m1", "u1", Recording("a.age3Yrec"));
        File.WriteAllBytes(Path.Combine(QueueDir, "m1.age3Yrec"), new byte[] { 1, 2, 3 });
        var server = new FakeServer();

        var pass = await queue.DrainAsync(server.Upload, "u1", "always");

        Assert.Empty(server.Asked);
        Assert.Equal(1, pass.Dropped);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("a/b")]
    [InlineData("")]
    [InlineData("C:\\x")]
    public void AMatchIdThatIsNotOneIsRefused(string matchId)
    {
        Assert.False(NewQueue().Enqueue(matchId, "u1", Recording("a.age3Yrec")));
        Assert.False(Directory.Exists(QueueDir) && Directory.EnumerateFileSystemEntries(QueueDir).Any());
    }

    [Fact]
    public void NothingIsQueuedWithoutAnAccountOrAFile()
    {
        var queue = NewQueue();
        Assert.False(queue.Enqueue("m1", "", Recording("a.age3Yrec")));
        Assert.False(queue.Enqueue("m1", "u1", new FileInfo(Path.Combine(_root, "missing.age3Yrec"))));
        Assert.False(queue.Enqueue("m1", "u1", Recording("empty.age3Yrec", size: 0)));
        Assert.Empty(queue.Snapshot());
    }

    [Fact]
    public void TheSameMatchIsQueuedOnce()
    {
        var queue = NewQueue();
        Assert.True(queue.Enqueue("m1", "u1", Recording("a.age3Yrec")));
        Assert.True(queue.Enqueue("m1", "u1", Recording("b.age3Yrec")));
        Assert.Single(queue.Snapshot());
    }

    [Fact]
    public void WhatACrashLeftBehindIsTidiedOnTheNextStart()
    {
        var queue = NewQueue();
        queue.Enqueue("m1", "u1", Recording("a.age3Yrec"));
        queue.Enqueue("m2", "u1", Recording("b.age3Yrec"));
        File.WriteAllBytes(Path.Combine(QueueDir, "stray.age3Yrec.tmp"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(QueueDir, "orphan.age3Yrec"), new byte[] { 1 });
        File.Delete(Path.Combine(QueueDir, "m2.age3Yrec"));

        var reopened = NewQueue();

        Assert.Equal(new[] { "m1" }, reopened.Snapshot().Select(e => e.MatchId));
        Assert.Equal(
            new[] { "m1.age3Yrec", "pending.json" },
            Directory.EnumerateFiles(QueueDir).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void AnUnreadableIndexIsAnEmptyQueue()
    {
        Assert.Empty(ReplayUploadQueue.Parse("{ not json"));
        Assert.Empty(ReplayUploadQueue.Parse(null));
        Assert.Empty(ReplayUploadQueue.Parse("[{\"match_id\":\"../x\",\"user_id\":\"u1\",\"sha256\":\"aa\"}]"));
    }
}
