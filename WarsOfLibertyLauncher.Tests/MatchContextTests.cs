using System;
using System.Collections.Generic;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="MatchContext"/> — the facts of a match, captured when it starts.
///
/// <para>It exists because of one real incident: the host closed the room while everyone was
/// still playing, and the teardown that follows wiped the roster, killed the game and cleared
/// "we are the host" — all before the game-exit handler that the kill itself triggered. The
/// report then asked the room what had just happened, got nothing back, and logged
/// <c>skipped — not host of this room</c>. The match never reached anyone's history.</para>
///
/// <para>So the test that matters most here is <see cref="AClosedRoomCannotChangeTheAnswer"/>:
/// the claim is not that <see cref="MatchContext.CanReport"/> computes the right thing, it is
/// that nothing outside the instance can make it compute a different one.</para>
/// </summary>
public class MatchContextTests
{
    private const string Me = "me-user-id";
    private const string Rival = "rival-user-id";
    private const int MinSeconds = 180;

    private static readonly DateTime Started = new(2026, 7, 30, 18, 0, 0, DateTimeKind.Utc);

    private static MatchContext Match(
        IEnumerable<string?>? members = null,
        string? lobbyId = "lobby-1",
        string? modId = "wol",
        string? me = Me,
        bool isHost = true)
        => MatchContext.Capture(
            members ?? new[] { Me, Rival }, lobbyId, modId, me, isHost, Started);

    // ---------- Capture ----------

    [Fact]
    public void Capture_DropsBlanksAndDuplicates_AndOrdersTheRest()
    {
        var ctx = Match(new[] { Rival, "", Me, null, "   ", Rival });

        Assert.Equal(new[] { Me, Rival }, ctx.Participants);
        Assert.Equal(2, ctx.ExpectedHumans);
    }

    /// <summary>
    /// The replay search counts humans and the report counts participants. They used to be two
    /// separate expressions over the same list — one raw, one filtered — so a member with a blank
    /// id would have had the recording searched for a head count the report would never send.
    /// </summary>
    [Fact]
    public void TheHeadCountAndTheParticipantCountAreTheSameNumber()
    {
        var ctx = Match(new[] { Me, "", Rival });

        Assert.Equal(ctx.Participants.Count, ctx.ExpectedHumans);
    }

    [Fact]
    public void Capture_TreatsBlankIdsAsAbsent()
    {
        var ctx = Match(lobbyId: "   ", modId: "", me: null);

        Assert.Null(ctx.LobbyId);
        Assert.Null(ctx.ModId);
        Assert.Null(ctx.ReporterUserId);
    }

    // ---------- CanReport: the accept ----------

    [Fact]
    public void AHostedTwoPlayerMatchOfARealLength_Reports()
    {
        var (ok, _) = Match().CanReport(Started.AddMinutes(20), MinSeconds);

        Assert.True(ok);
    }

    // ---------- CanReport: every refusal, with its reason ----------

    /// <summary>
    /// The regression test for the incident. A guest must never report — the exit handler runs on
    /// every player's client and the POST writes a row per participant, so a second reporter puts
    /// the same match into everyone's history twice.
    /// </summary>
    [Fact]
    public void NotTheHost_DoesNotReport()
    {
        var (ok, reason) = Match(isHost: false).CanReport(Started.AddMinutes(20), MinSeconds);

        Assert.False(ok);
        Assert.Equal("not host of this room", reason);
    }

    [Fact]
    public void NoReporterId_DoesNotReport()
    {
        var (ok, reason) = Match(me: null).CanReport(Started.AddMinutes(20), MinSeconds);

        Assert.False(ok);
        Assert.Equal("no reporter id", reason);
    }

    [Fact]
    public void NoLobbyOrMod_DoesNotReport()
    {
        var (ok, reason) = Match(lobbyId: null).CanReport(Started.AddMinutes(20), MinSeconds);

        Assert.False(ok);
        Assert.Contains("lobbyId=", reason);
    }

    [Fact]
    public void ASoloLaunch_DoesNotReport()
    {
        var (ok, reason) = Match(new[] { Me }).CanReport(Started.AddMinutes(20), MinSeconds);

        Assert.False(ok);
        Assert.Equal(
            "only 1 participant(s), need >= 2 (multiplayer match, not a solo launch)", reason);
    }

    /// <summary>
    /// The boundary itself, because "under three minutes" is the rule that throws away a session
    /// where somebody opened AoE3 and closed it again — and one second either side decides whether
    /// a real game counts.
    /// </summary>
    [Theory]
    [InlineData(179, false)]
    [InlineData(180, true)]
    public void TheThreeMinuteFloorIsInclusive(int seconds, bool expected)
    {
        var (ok, _) = Match().CanReport(Started.AddSeconds(seconds), MinSeconds);

        Assert.Equal(expected, ok);
    }

    [Fact]
    public void TooShort_SaysHowShort()
    {
        var (_, reason) = Match().CanReport(Started.AddSeconds(104), MinSeconds);

        Assert.Equal("duration 104s < 180s", reason);
    }

    /// <summary>A clock that went backwards is a zero-length match, never a negative one.</summary>
    [Fact]
    public void AnEndBeforeTheStart_IsZeroSeconds()
        => Assert.Equal(0, Match().DurationSeconds(Started.AddMinutes(-5)));

    // ---------- The property the whole type exists for ----------

    /// <summary>
    /// The incident, expressed as a test. Everything the room could do between the launch and the
    /// exit — close, empty out, hand the host role away, drop the socket — happens to state this
    /// object does not have, so the same instance answers identically before and after.
    ///
    /// <para>It reads as trivial precisely because it is: the fix was making the answer
    /// unreachable from the outside rather than making it cleverer. If someone ever gives this
    /// type a reference to the session or the roster, this test is what stops them.</para>
    /// </summary>
    [Fact]
    public void AClosedRoomCannotChangeTheAnswer()
    {
        var members = new List<string> { Me, Rival };
        var ctx = MatchContext.Capture(members, "lobby-1", "wol", Me, isHost: true, Started);

        var before = ctx.CanReport(Started.AddMinutes(20), MinSeconds);

        // Whatever the teardown does to the room, it cannot reach in here.
        members.Clear();

        var after = ctx.CanReport(Started.AddMinutes(20), MinSeconds);

        Assert.Equal(before, after);
        Assert.True(after.Ok);
    }

    // ---------- WithHostLost ----------

    [Fact]
    public void WithHostLost_StopsReportingAndKeepsEverythingElse()
    {
        var ctx = Match();
        var demoted = ctx.WithHostLost();

        Assert.False(demoted.IsHost);
        Assert.Equal("not host of this room",
            demoted.CanReport(Started.AddMinutes(20), MinSeconds).Reason);

        Assert.Equal(ctx.Participants, demoted.Participants);
        Assert.Equal(ctx.LobbyId, demoted.LobbyId);
        Assert.Equal(ctx.ModId, demoted.ModId);
        Assert.Equal(ctx.ReporterUserId, demoted.ReporterUserId);
        Assert.Equal(ctx.StartedAtUtc, demoted.StartedAtUtc);
    }

    /// <summary>The original is untouched — it is a record, and the caller replaces its own field.</summary>
    [Fact]
    public void WithHostLost_LeavesTheOriginalAlone()
    {
        var ctx = Match();
        ctx.WithHostLost();

        Assert.True(ctx.IsHost);
    }

    // ---------- Composition with the resolver ----------

    /// <summary>
    /// The two pure types have to keep fitting together: a captured duel is exactly the shape
    /// <see cref="MatchResultResolver"/> will accept, and the reporter is one of the two players.
    /// A change to <see cref="MatchContext.Capture"/>'s normalisation that broke that — dropping
    /// the host, say — would leave every match unreportable with nothing to point at.
    /// </summary>
    [Fact]
    public void ACapturedDuelStillResolvesAResult()
    {
        var ctx = Match();

        var decision = MatchResultResolver.ResolveHostResult(1.0, ctx.Participants, ctx.ReporterUserId);

        Assert.Equal(1.0, decision.Result);
        Assert.Equal(0.0, MatchResultResolver.ParticipantResult(decision.Result!.Value, isHost: false));
    }
}
