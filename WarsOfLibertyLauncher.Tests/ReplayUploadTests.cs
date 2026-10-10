using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;
using static WarsOfLibertyLauncher.Services.Multiplayer.ReplayUploadService;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The gate that decides whether a match's recording is uploaded at all. The refusals are the
/// point: a casual room's recording, or the recording of somebody who switched sharing off,
/// leaving this machine would be a privacy failure nobody could see.
/// </summary>
public class ReplayUploadGateTests
{
    [Fact]
    public void ACasualRoomIsNeverUploaded()
    {
        Assert.Equal(ReplayUploadDecision.NotCompetitive, Decide(false, "always", true, true, "m1"));
    }

    [Theory]
    [InlineData("never")]
    [InlineData("NEVER")]
    [InlineData(" Never ")]
    public void APlayerWhoOptedOutIsNeverUploaded(string policy)
    {
        Assert.Equal(ReplayUploadDecision.OptedOut, Decide(true, policy, true, true, "m1"));
        Assert.False(IsSharingEnabled(policy));
    }

    [Fact]
    public void NoFileMeansNothingToUpload()
    {
        Assert.Equal(ReplayUploadDecision.NoFile, Decide(true, "always", false, true, "m1"));
    }

    // The announce-only path keeps the newest recording without checking it is this match's:
    // it can be a file somebody sent the player, and uploading it would store a stranger's
    // game on the match.
    [Fact]
    public void ARecordingNeverCheckedAgainstTheMatchIsNeverUploaded()
    {
        Assert.Equal(ReplayUploadDecision.NotVerified, Decide(true, "always", true, false, "m1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoMatchIdMeansNothingToAttachItTo(string? matchId)
    {
        Assert.Equal(ReplayUploadDecision.NoMatchId, Decide(true, "always", true, true, matchId));
    }

    // The old three-way setting defaulted to "ask"; that reads as ON — the implicit consent
    // decided for this feature, stated in every competitive room.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ask")]
    [InlineData("always")]
    public void EverythingButNeverShares(string? policy)
    {
        Assert.True(IsSharingEnabled(policy));
        Assert.Equal(ReplayUploadDecision.Upload, Decide(true, policy, true, true, "m1"));
    }
}

/// <summary>
/// The only URLs a recording is sent to or fetched from. The old backend answered a RELATIVE
/// path on itself, which would stream the file through the lobby server — refused on purpose.
/// </summary>
public class ReplayStorageUrlTests
{
    [Theory]
    [InlineData("/replays/upload/abc")]
    [InlineData("replays/upload/abc")]
    [InlineData("http://ns.compat.objectstorage.us-ashburn-1.oraclecloud.com/wol-replays/k")]
    [InlineData("https://user:pass@ns.compat.objectstorage.us-ashburn-1.oraclecloud.com/k")]
    [InlineData("file:///C:/Windows/notepad.exe")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not a url")]
    public void Refused(string? url)
    {
        Assert.False(IsAcceptableStorageUrl(url));
    }

    [Fact]
    public void APresignedHttpsUrlIsAccepted()
    {
        Assert.True(IsAcceptableStorageUrl(
            "https://idjvr9jpxae6.compat.objectstorage.us-ashburn-1.oraclecloud.com/wol-replays/replays/wol/2026/m1.age3Yrec"
            + "?X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Signature=abc"));
    }
}

/// <summary>
/// The name a download is offered under. It comes from the server and is still not a path
/// this launcher follows.
/// </summary>
public class ReplayFileNameTests
{
    [Fact]
    public void TheServersNameIsKept()
    {
        Assert.Equal("2026-10-09_Ana-vs-Luis_Texas.age3Yrec",
            SafeReplayFileName("2026-10-09_Ana-vs-Luis_Texas.age3Yrec", "m1"));
    }

    [Theory]
    [InlineData(@"..\..\Windows\evil.age3Yrec", "evil.age3Yrec")]
    [InlineData("../../etc/evil.age3Yrec", "evil.age3Yrec")]
    [InlineData(@"C:\Users\x\evil.age3Yrec", "evil.age3Yrec")]
    public void OnlyTheLastSegmentSurvives(string server, string expected)
    {
        Assert.Equal(expected, SafeReplayFileName(server, "m1"));
    }

    [Fact]
    public void CharactersWindowsRefusesBecomeUnderscores()
    {
        var name = SafeReplayFileName("a:b*c?\"d<e>f|g.age3Yrec", "m1");
        Assert.Equal("a_b_c__d_e_f_g.age3Yrec", name);
    }

    [Theory]
    [InlineData("recording.zip", "recording.zip.age3Yrec")]
    [InlineData("recording", "recording.age3Yrec")]
    [InlineData("recording.AGE3YREC", "recording.age3Yrec")]
    public void TheExtensionIsAlwaysTheGamesOwn(string server, string expected)
    {
        Assert.Equal(expected, SafeReplayFileName(server, "m1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("...")]
    [InlineData(".age3Yrec")]
    public void NothingUsableFallsBackToTheMatchId(string? server)
    {
        Assert.Equal("m1.age3Yrec", SafeReplayFileName(server, "m1"));
    }

    [Fact]
    public void ADeviceNameIsNeverUsedAsIs()
    {
        Assert.Equal("_CON.age3Yrec", SafeReplayFileName("CON.age3Yrec", "m1"));
    }

    [Fact]
    public void AVeryLongNameIsCapped()
    {
        var name = SafeReplayFileName(new string('x', 500), "m1");
        Assert.True(name.Length <= 120 + ReplayExtension.Length, name.Length.ToString());
        Assert.EndsWith(ReplayExtension, name);
    }
}

/// <summary>
/// The PUT to a presigned storage URL. The property that matters most is negative: NO
/// Authorization header. A bearer token on a presigned URL is a second signature, and the
/// storage refuses the request — every upload would fail, silently, in the background.
/// </summary>
public class ReplayStoragePutTests
{
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _answers = new();
        public List<(HttpMethod Method, Uri? Uri, bool HadAuthorization, long? ContentLength, string? ContentType, byte[] Body)> Seen { get; } = new();

        public RecordingHandler Then(Func<HttpRequestMessage, HttpResponseMessage> answer)
        {
            _answers.Enqueue(answer);
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content == null ? Array.Empty<byte>() : await request.Content.ReadAsByteArrayAsync(ct);
            Seen.Add((
                request.Method,
                request.RequestUri,
                request.Headers.Authorization != null || request.Headers.Contains("Authorization"),
                request.Content?.Headers.ContentLength,
                request.Content?.Headers.ContentType?.MediaType,
                body));
            return _answers.Dequeue()(request);
        }
    }

    private static readonly Uri Url = new(
        "https://ns.compat.objectstorage.us-ashburn-1.oraclecloud.com/wol-replays/replays/wol/2026/m1.age3Yrec?X-Amz-Signature=abc");

    [Fact]
    public async Task THE_ONE_THAT_MATTERS_TheUploadCarriesNoBearerAndExactlyTheSignedLength()
    {
        var handler = new RecordingHandler().Then(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var http = new HttpClient(handler);
        var bytes = Encoding.UTF8.GetBytes("l33t recording bytes");

        var outcome = await PutToStorageAsync(http, Url, bytes, CancellationToken.None);

        Assert.True(outcome.Success);
        var put = Assert.Single(handler.Seen);
        Assert.Equal(HttpMethod.Put, put.Method);
        Assert.Equal(Url, put.Uri);
        Assert.False(put.HadAuthorization);
        Assert.Equal(bytes.Length, put.ContentLength);
        Assert.Equal("application/octet-stream", put.ContentType);
        Assert.Equal(bytes, put.Body);
    }

    [Fact]
    public async Task ADroppedConnectionIsRetriedOnce()
    {
        var handler = new RecordingHandler()
            .Then(_ => throw new HttpRequestException("connection reset"))
            .Then(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var http = new HttpClient(handler);

        var outcome = await PutToStorageAsync(http, Url, new byte[] { 1, 2, 3 }, CancellationToken.None);

        Assert.True(outcome.Success);
        Assert.Equal(2, handler.Seen.Count);
    }

    [Fact]
    public async Task AnAnswerFromTheStorageIsNeverRetried()
    {
        var handler = new RecordingHandler()
            .Then(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("<Error><Code>SignatureDoesNotMatch</Code></Error>"),
            });
        using var http = new HttpClient(handler);

        var outcome = await PutToStorageAsync(http, Url, new byte[] { 1 }, CancellationToken.None);

        Assert.False(outcome.Success);
        Assert.Equal(403, outcome.Status);
        Assert.Contains("SignatureDoesNotMatch", outcome.Body);
        Assert.Single(handler.Seen);
    }

    [Fact]
    public async Task ASecondDroppedConnectionGivesUp()
    {
        var handler = new RecordingHandler()
            .Then(_ => throw new HttpRequestException("reset"))
            .Then(_ => throw new HttpRequestException("reset again"));
        using var http = new HttpClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => PutToStorageAsync(http, Url, new byte[] { 1 }, CancellationToken.None));
        Assert.Equal(2, handler.Seen.Count);
    }
}
