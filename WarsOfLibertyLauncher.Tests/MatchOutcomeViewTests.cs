using System;
using System.Collections.Generic;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The three refusals the end-of-match card is built on. Each of them is a case where
/// the honest answer is "nothing", and where showing a number instead would be a
/// statement about the match that only describes our ability to read it.
/// </summary>
public class MatchOutcomeViewTests
{
    [Fact]
    public void AHalfIsNoResult_NeverADraw()
    {
        // 0.5 is what the backend stores when the outcome could not be read — no
        // recording, a team game, a match reported before any of this existed. Those are
        // the MAJORITY of stored rows, so calling them draws would invent a fact about
        // almost every match in the database.
        Assert.Equal(MatchVerdict.NoResult, MatchOutcomeView.Classify(0.5));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.999)]
    public void AWinIsAWin(double result)
        => Assert.Equal(MatchVerdict.Win, MatchOutcomeView.Classify(result));

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.001)]
    public void ALossIsALoss(double result)
        => Assert.Equal(MatchVerdict.Loss, MatchOutcomeView.Classify(result));

    [Fact]
    public void AnUnknownRatingHasNoDelta_NotAZeroOne()
    {
        // "+0" says the match moved nothing. "Nothing" says we were not told what it
        // moved. An older backend sends neither value and must land on the second.
        Assert.Null(MatchOutcomeView.Delta(null, 1518));
        Assert.Null(MatchOutcomeView.Delta(1500, null));
        Assert.Null(MatchOutcomeView.Delta(null, null));
    }

    [Fact]
    public void ARealDeltaIsRounded()
    {
        Assert.Equal(18, MatchOutcomeView.Delta(1500.2, 1518.4));
        Assert.Equal(-14, MatchOutcomeView.Delta(1524.0, 1510.0));
    }

    [Fact]
    public void AnUndecidedPlayerHasNoWinRate_NotZeroPercent()
    {
        var view = Sample(wins: 0, losses: 0);
        Assert.Null(view.WinPercent);
        Assert.Equal(0, view.DecidedGames);
    }

    [Fact]
    public void TheWinRateDividesByDecidedGames()
    {
        var view = Sample(wins: 4, losses: 1);
        Assert.Equal(5, view.DecidedGames);
        Assert.Equal(80, view.WinPercent);
    }

    [Fact]
    public void AFreshRatingIsProvisional()
    {
        // New players start at rd 350 and settle after a handful of decided games; the
        // note exists so a big early swing is not read as a big result.
        Assert.True(MatchOutcomeView.IsProvisional(350));
        Assert.False(MatchOutcomeView.IsProvisional(60));
        // Not knowing the deviation is not a reason to warn about it.
        Assert.False(MatchOutcomeView.IsProvisional(null));
    }

    private static MatchOutcomeView Sample(int wins, int losses) => new(
        MatchVerdict.Win, "wol", "Texas", 1200, 2,
        RatingBefore: 1500, RatingAfter: 1518,
        RivalLogin: "someone", RivalRating: 1490,
        Wins: wins, Losses: losses, Rd: 60);
}

/// <summary>
/// Picking OUR match out of our own history — the only route a non-host has to the
/// result, since the room is closed with a bare socket close that carries nothing.
/// </summary>
public class MatchHistoryMatcherTests
{
    private static readonly DateTime Started = new(2026, 8, 13, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void PicksTheRowThatBracketsOurStart()
    {
        var rows = new List<MatchHistoryRow>
        {
            Row("older", "wol", Started.AddHours(-2)),
            Row("ours", "wol", Started.AddSeconds(9)),
        };
        Assert.Equal("ours", MatchHistoryMatcher.PickForMatch(rows, "wol", Started)?.Id);
    }

    [Fact]
    public void RefusesRatherThanTakingTheNewest()
    {
        // The whole point: with nothing close enough, "the most recent row" would be
        // somebody else's evening — reported late, or simply a different match of ours.
        var rows = new List<MatchHistoryRow> { Row("unrelated", "wol", Started.AddHours(-3)) };
        Assert.Null(MatchHistoryMatcher.PickForMatch(rows, "wol", Started));
    }

    [Fact]
    public void AnotherModIsNeverOurs()
    {
        var rows = new List<MatchHistoryRow> { Row("other-mod", "improvement-mod", Started) };
        Assert.Null(MatchHistoryMatcher.PickForMatch(rows, "wol", Started));
    }

    [Fact]
    public void TwoCandidatesResolveToTheCloser()
    {
        var rows = new List<MatchHistoryRow>
        {
            Row("further", "wol", Started.AddSeconds(90)),
            Row("closer", "wol", Started.AddSeconds(-4)),
        };
        Assert.Equal("closer", MatchHistoryMatcher.PickForMatch(rows, "wol", Started)?.Id);
    }

    [Fact]
    public void AZonelessTimestampIsReadAsUtc()
    {
        // SQLite's datetime('now') has no zone and .NET would read it as LOCAL, sliding
        // every row by the machine's offset — which would match nothing east or west of
        // Greenwich, and would do it silently.
        var parsed = MatchHistoryMatcher.ParseUtc("2026-08-13 20:00:00");
        Assert.NotNull(parsed);
        Assert.Equal(Started, parsed!.Value);
    }

    [Fact]
    public void NothingToSearchIsNotAMatch()
    {
        Assert.Null(MatchHistoryMatcher.PickForMatch(null, "wol", Started));
        Assert.Null(MatchHistoryMatcher.PickForMatch(new List<MatchHistoryRow>(), "wol", Started));
        Assert.Null(MatchHistoryMatcher.PickForMatch(
            new List<MatchHistoryRow> { Row("x", "wol", Started) }, null, Started));
    }

    private static MatchHistoryRow Row(string id, string modId, DateTime startedUtc) => new()
    {
        Id = id,
        ModId = modId,
        StartedAt = startedUtc.ToString("yyyy-MM-dd HH:mm:ss"),
        Result = 0.5,
    };
}

/// <summary>
/// The recording cell says what the LAUNCHER asked for, because what the GAME will do is
/// decided by a per-match checkbox nothing here can read.
/// </summary>
public class RecordingIndicatorTests
{
    [Fact]
    public void TurnedOffIsKnownWithCertainty()
    {
        Assert.Equal(RecordingState.Off, RecordingIndicator.Classify(false, null));
        Assert.Equal(RecordingState.Off, RecordingIndicator.Classify(false, true));
    }

    [Fact]
    public void WantedAndWrittenIsRequested()
        => Assert.Equal(RecordingState.Requested, RecordingIndicator.Classify(true, true));

    [Fact]
    public void WantedButNeverWrittenIsUnknown_NotRequested()
    {
        // A null marker means this mod's profile has not been reached yet — the game
        // creates it on its first run. Reporting that as "requested" would tell the
        // player the one thing they must not believe without ticking the box.
        Assert.Equal(RecordingState.Unknown, RecordingIndicator.Classify(true, null));
        Assert.Equal(RecordingState.Unknown, RecordingIndicator.Classify(true, false));
    }

    // ---------------- who gets to explain a match that did not count ----------------

    [Fact]
    public void ASpecificServerReasonWinsOverTheLocalOne()
    {
        // THE test. Both statements are true about a team game whose recording was also
        // unreadable — but only one of them is the thing the player would change, and
        // telling them their profile was unreadable would send them off to fix something
        // that was never going to make a 2v2 count.
        Assert.Equal(
            "MpResultUnratedTeam",
            MatchOutcomeView.UnratedNoteKey("not_1v1", LocalReadFailure.RecordingUnreadable));

        Assert.Equal(
            "MpResultUnratedMod",
            MatchOutcomeView.UnratedNoteKey("mod_not_ranked", LocalReadFailure.NoProfileName));
    }

    [Fact]
    public void TheGenericServerReasonDefersToTheLocalOne()
    {
        // "no_decided_result" says nobody won without saying why, and the why is the only
        // part the launcher knows.
        Assert.Equal(
            "MpResultUnratedNoProfile",
            MatchOutcomeView.UnratedNoteKey("no_decided_result", LocalReadFailure.NoProfileName));

        Assert.Equal(
            "MpResultUnratedUnreadable",
            MatchOutcomeView.UnratedNoteKey("no_decided_result", LocalReadFailure.RecordingUnreadable));

        Assert.Equal(
            "MpResultUnratedAmbiguous",
            MatchOutcomeView.UnratedNoteKey("no_decided_result", LocalReadFailure.RecordingAmbiguous));

        Assert.Equal(
            "MpResultUnratedNoRoster",
            MatchOutcomeView.UnratedNoteKey("no_decided_result", LocalReadFailure.RosterUnknown));
    }

    [Fact]
    public void AnOldBackendSaysNothingAndTheLocalReasonStillSpeaks()
    {
        // Null is what a backend that predates unrated_reason sends. It must not silence
        // the one explanation that does not need a server at all.
        Assert.Equal(
            "MpResultUnratedNoProfile",
            MatchOutcomeView.UnratedNoteKey(null, LocalReadFailure.NoProfileName));
    }

    [Fact]
    public void NothingWrongLocallyKeepsTheOriginalMessage()
    {
        // The regression guard: every match that reads correctly today must keep showing
        // exactly what it shows today. "No recording found" is the ordinary case, and its
        // advice — turn recording on — is the one piece of advice that is right.
        Assert.Equal(
            "MpResultNoneBody",
            MatchOutcomeView.UnratedNoteKey("no_decided_result", LocalReadFailure.NoRecordingFound));

        Assert.Equal(
            "MpResultNoneBody",
            MatchOutcomeView.UnratedNoteKey("no_decided_result", LocalReadFailure.None));

        Assert.Equal("MpResultNoneBody", MatchOutcomeView.UnratedNoteKey(null));
    }

    [Fact]
    public void AnUnknownServerReasonFallsBackRatherThanShowingNothing()
    {
        // A server newer than this launcher. Better the general message than a blank.
        Assert.Equal(
            "MpResultNoneBody",
            MatchOutcomeView.UnratedNoteKey("some_future_reason", LocalReadFailure.None));
    }

    [Fact]
    public void AnUnreadableProfileOutranksEverythingElse()
    {
        // It has to come first: with no readable profile name the match cannot produce a
        // result whatever the recording does, so leading with "recording is on" would be
        // reassuring the player about the wrong thing.
        Assert.Equal(
            RecordingState.ProfileUnreadable,
            RecordingIndicator.Classify(true, true, canIdentifyPlayer: false));

        Assert.Equal(
            RecordingState.ProfileUnreadable,
            RecordingIndicator.Classify(false, null, canIdentifyPlayer: false));
    }

    [Fact]
    public void AReadableProfileChangesNothingThatWorkedBefore()
    {
        // The regression guard for the default: every existing caller passes no third
        // argument and must keep getting exactly what it got.
        Assert.Equal(RecordingState.Requested, RecordingIndicator.Classify(true, true));
        Assert.Equal(RecordingState.Off, RecordingIndicator.Classify(false, true));
        Assert.Equal(RecordingState.Unknown, RecordingIndicator.Classify(true, null));

        Assert.Equal(
            RecordingState.Requested,
            RecordingIndicator.Classify(true, true, canIdentifyPlayer: true));
    }
}
