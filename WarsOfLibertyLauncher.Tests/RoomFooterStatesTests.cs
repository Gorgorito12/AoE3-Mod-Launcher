using System.Linq;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The footer of the room window (design 87): one state line and one primary button. The rule used
/// to live in six methods; these pin that every state of the prototype lands on the right one, and
/// that the buttons a state forbids are really gone.
/// </summary>
public class RoomFooterStatesTests
{
    private static RoomFacts Casual(int players, int notReady, bool host = true) => new()
    {
        Players = players, Seats = 4, NotReady = notReady, IsHost = host, HostName = "Gorgo",
    };

    [Fact]
    public void ACasualHostAloneCannotStartAndIsToldWhy()
    {
        var view = RoomFooterState.Evaluate(Casual(1, 0));
        Assert.Equal("RoomFtWaiting", view.Title.Key);
        Assert.Equal(RoomPrimary.Start, view.Primary);
        Assert.False(view.PrimaryEnabled);
        Assert.Equal("RoomTipNeedsPlayer", view.PrimaryTipKey);
    }

    [Theory]
    [InlineData(1, "RoomFtOneMissing")]
    [InlineData(2, "RoomFtManyMissing")]
    public void ACasualHostWaitsForEveryoneElse(int notReady, string sub)
    {
        var view = RoomFooterState.Evaluate(Casual(3, notReady));
        Assert.Equal("RoomFtCount", view.Title.Key);
        Assert.Equal(sub, view.Sub!.Key);
        Assert.False(view.PrimaryEnabled);
        Assert.Equal("RoomTipNeedsReady", view.PrimaryTipKey);
    }

    [Fact]
    public void ACasualHostStartsWhenAllAreReady()
    {
        var view = RoomFooterState.Evaluate(Casual(3, 0));
        Assert.Equal(RoomDot.Green, view.Dot);
        Assert.Equal(RoomPrimary.Start, view.Primary);
        Assert.True(view.PrimaryEnabled);
        Assert.Null(view.PrimaryTipKey);
    }

    [Fact]
    public void AGuestIsOfferedReadyUntilPressedAndThenTheWayBack()
    {
        var waiting = RoomFooterState.Evaluate(Casual(3, 1, host: false));
        Assert.Equal(RoomPrimary.Ready, waiting.Primary);
        Assert.Equal(RoomSecondary.None, waiting.Secondary);

        var ready = RoomFooterState.Evaluate(Casual(3, 1, host: false) with { IAmReady = true });
        Assert.Equal(RoomPrimary.None, ready.Primary);
        Assert.Equal(RoomSecondary.Unready, ready.Secondary);
    }

    private static RoomFacts OneVOne(bool host = false) => new()
    {
        Competitive = true, Players = 2, Seats = 2, IsHost = host, HostName = "Kaiser", RivalName = "Kaiser",
        Gate = new StartGate.Verdict(StartGate.Reason.Ready),
    };

    [Fact]
    public void ACompetitiveRoomStartsItselfSoEvenTheHostMarksReady()
    {
        var view = RoomFooterState.Evaluate(OneVOne(host: true) with { NotReady = 2 });
        Assert.Equal(RoomPrimary.Ready, view.Primary);
        Assert.Equal("RoomFtRivalWait", view.Title.Key);
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_AHostIsNeverLeftWithNoWayToStart()
    {
        // The automatic start fires once. A cancelled Record Game confirmation leaves everybody
        // ready and the room still here: without Start there is nothing left to press.
        var view = RoomFooterState.Evaluate(OneVOne(host: true) with { IAmReady = true, NotReady = 0, RivalReady = true });
        Assert.Equal(RoomPrimary.Start, view.Primary);
        Assert.True(view.PrimaryEnabled);
    }

    [Fact]
    public void TheGuestOfAReadyRivalIsToldWhoHosts()
    {
        var view = RoomFooterState.Evaluate(OneVOne() with { RivalReady = true, NotReady = 1 });
        Assert.Equal(RoomDot.Green, view.Dot);
        Assert.Equal("RoomFtRivalReady", view.Title.Key);
        Assert.Equal("RoomFtAutoTwoGuest", view.Sub!.Key);
        Assert.Equal(RoomPrimary.Ready, view.Primary);
    }

    private static RoomFacts Teams(StartGate.Verdict gate) => new()
    {
        Competitive = true, HasTeams = true, Players = 4, Seats = 4, IsHost = true, HostName = "Gorgo",
        OnTeams = 3, NotReady = 1, Gate = gate,
    };

    [Fact]
    public void TheTeamReasonsComeFromTheGate()
    {
        var names = RoomFooterState.Evaluate(Teams(new StartGate.Verdict(
            StartGate.Reason.PlayerWithoutTeam, WithoutTeam: new[] { "Malinche" })));
        Assert.Equal(RoomDot.Orange, names.Dot);
        Assert.Equal("RoomFtInTeams", names.Title.Key);
        Assert.Equal("RoomFtNoTeam", names.Sub!.Key);

        var uneven = RoomFooterState.Evaluate(Teams(new StartGate.Verdict(StartGate.Reason.UnevenTeams, Team1: 3, Team2: 1)));
        Assert.Equal("RoomFtUneven", uneven.Title.Key);
        Assert.Equal(new object[] { 3, 1 }, uneven.Sub!.Args);

        var whole = RoomFooterState.Evaluate(Teams(new StartGate.Verdict(StartGate.Reason.Ready)));
        Assert.Equal(RoomDot.Green, whole.Dot);
        Assert.Equal("RoomFtTeamsFull", whole.Title.Key);
    }

    [Fact]
    public void InAMatchThereIsNoPrimaryAndTheWayOutIsTheRedOne()
    {
        var view = RoomFooterState.Evaluate(Casual(3, 0) with { Stage = RoomStage.InGame });
        Assert.Equal(RoomPrimary.None, view.Primary);
        Assert.Equal(RoomLeave.Match, view.Leave);
    }

    [Theory]
    [InlineData(true, "RoomFtClosedHostSub")]
    [InlineData(false, "RoomFtClosedSub")]
    public void WithAoE3ClosedTheFooterOffersTheGameBack(bool host, string sub)
    {
        var view = RoomFooterState.Evaluate(Casual(3, 0, host) with { Stage = RoomStage.GameClosed });
        Assert.Equal(RoomPrimary.Reopen, view.Primary);
        Assert.True(view.WarmTitle);
        Assert.Equal(sub, view.Sub!.Key);
        Assert.Equal(RoomLeave.Match, view.Leave);
    }

    [Fact]
    public void AMissingVpnOutranksEveryRoomStateAndBringsTheAssistant()
    {
        var view = RoomFooterState.Evaluate(Casual(3, 0) with { VpnMissing = true, HostLeftName = "Kaiser" });
        Assert.Equal("RoomFtVpn", view.Title.Key);
        Assert.Equal(RoomSecondary.Assistant, view.Secondary);
        Assert.Equal(RoomPrimary.Start, view.Primary);
    }

    [Fact]
    public void AHostWhoInheritedTheRoomIsToldWhoLeft()
    {
        var view = RoomFooterState.Evaluate(Casual(3, 2) with { HostLeftName = "Kaiser" });
        Assert.Equal("RoomFtNowHost", view.Title.Key);
        Assert.Equal(new object[] { "Kaiser" }, view.Sub!.Args);
        Assert.False(view.PrimaryEnabled);
    }

    [Fact]
    public void WaitingForARematchSaysWhoAndKeepsReady()
    {
        var view = RoomFooterState.Evaluate(OneVOne(host: true) with { Players = 1, RivalName = null, RematchWaitingFor = "Kaiser" });
        Assert.Equal(RoomDot.Blue, view.Dot);
        Assert.Equal("RoomFtRivalWait", view.Title.Key);
        Assert.Equal("RoomFtRematchSub", view.Sub!.Key);
        Assert.Equal(RoomPrimary.Ready, view.Primary);
    }

    [Theory]
    [InlineData(RoomStage.ResultRated, "RoomFtRegistered", "RoomFtCountsBoth")]
    [InlineData(RoomStage.ResultPlacement, "RoomFtRegistered", "RoomFtPlacementSub")]
    public void AResultOffersTheNextThing(RoomStage stage, string title, string sub)
    {
        var view = RoomFooterState.Evaluate(OneVOne() with { Stage = stage, OffersRematch = true, OffersFindAnother = true });
        Assert.Equal(title, view.Title.Key);
        Assert.Equal(sub, view.Sub!.Key);
        Assert.Equal(RoomPrimary.Rematch, view.Primary);
        Assert.Equal(RoomSecondary.FindAnother, view.Secondary);
        Assert.Equal(RoomLeave.Rooms, view.Leave);
    }

    [Fact]
    public void AnUnratedResultSaysWhyInItsOwnWords()
    {
        var view = RoomFooterState.Evaluate(OneVOne() with { Stage = RoomStage.ResultUnrated, UnratedReason = "short" });
        Assert.Equal("RoomFtNoElo", view.Title.Key);
        Assert.Equal(new object[] { "short" }, view.Sub!.Args);
        Assert.Equal(RoomDot.Neutral, view.Dot);
    }

    [Fact]
    public void NoResultNoRematch()
    {
        var view = RoomFooterState.Evaluate(OneVOne() with { Stage = RoomStage.ResultRated });
        Assert.Equal(RoomPrimary.None, view.Primary);
        Assert.Equal(RoomSecondary.None, view.Secondary);
    }

    [Fact]
    public void EveryFooterStateHasAStringInBothLanguages()
    {
        foreach (var stage in System.Enum.GetValues<RoomStage>())
        {
            var view = RoomFooterState.Evaluate(Casual(2, 1) with { Stage = stage, UnratedReason = "x" });
            Assert.False(string.IsNullOrEmpty(WarsOfLibertyLauncher.Localization.Strings.GetIn(WarsOfLibertyLauncher.Localization.Strings.LangEs, view.Title.Key)));
            Assert.NotEqual(view.Title.Key, WarsOfLibertyLauncher.Localization.Strings.GetIn(WarsOfLibertyLauncher.Localization.Strings.LangEn, view.Title.Key));
        }
    }

    private static RoomFacts Match(bool host = true) => new()
    {
        Tournament = true, Competitive = true, Players = 2, Seats = 2, MatchPlayers = 2, IsHost = host,
        HostName = "Cuzco", RivalName = "Cuzco", Organizer = "Kaiser",
    };

    [Fact]
    public void AloneInABracketRoomTheClockRunsAndTheFooterSaysWhoSettlesIt()
    {
        var view = RoomFooterState.Evaluate(Match() with { Players = 1, Absent = "Cuzco" });
        Assert.Equal("RoomFtRivalWait", view.Title.Key);
        Assert.Equal(RoomClock.Waiting, view.Clock);
        Assert.Equal("RtFtSoloSub", view.Sub!.Key);
        Assert.Equal(new object[] { "Cuzco", "Kaiser" }, view.Sub.Args);
        Assert.Equal(RoomPrimary.Ready, view.Primary);
    }

    [Fact]
    public void WithoutAnOrganizersNameTheSentenceDoesNotInventOne()
    {
        var view = RoomFooterState.Evaluate(Match() with { Players = 1, Absent = "Cuzco", Organizer = null });
        Assert.Equal("RtFtSoloSubNoName", view.Sub!.Key);
    }

    [Fact]
    public void ARoomOfABracketNamesWhoIsReadyAndWhoIsNot()
    {
        Assert.Equal("RtFtRivalJoined", RoomFooterState.Evaluate(Match()).Title.Key);
        Assert.Equal("RtFtInRivalRoom", RoomFooterState.Evaluate(Match(host: false)).Title.Key);
        Assert.Equal("RoomFtRivalReady", RoomFooterState.Evaluate(Match() with { RivalReady = true }).Title.Key);
        Assert.Equal("RtFtReadyWaiting", RoomFooterState.Evaluate(Match() with { IAmReady = true }).Title.Key);
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_AGameThatDoesNotCountOffersTheBracketNotARepeat()
    {
        // The server closes every room after a report, whatever it says, so "I'm ready" here
        // would be a button on a room that no longer exists.
        var view = RoomFooterState.Evaluate(Match() with { Stage = RoomStage.ResultUndecided });
        Assert.Equal("RtFtNotYet", view.Title.Key);
        Assert.Equal(RoomPrimary.ViewBracket, view.Primary);
    }

    [Theory]
    [InlineData(RoomStage.ResultRated)]
    [InlineData(RoomStage.ResultAwaiting)]
    public void AfterABracketMatchThereIsNoRematchAndNoSearch(RoomStage stage)
    {
        var view = RoomFooterState.Evaluate(Match() with { Stage = stage, OffersRematch = true, OffersFindAnother = true });
        Assert.Equal(RoomPrimary.ViewBracket, view.Primary);
        Assert.Equal(RoomSecondary.None, view.Secondary);
    }

    [Theory]
    [InlineData(true, RoomDot.Green)]
    [InlineData(false, RoomDot.Neutral)]
    public void OnlyAWinEndsTheRoomInGreen(bool won, RoomDot dot)
    {
        var view = RoomFooterState.Evaluate(Match() with { Stage = RoomStage.ResultRated, ResultWon = won });
        Assert.Equal(dot, view.Dot);
        Assert.Equal("RtFtMoved", view.Title.Key);
    }

    [Fact]
    public void WinningTheFinalIsStillAWin_ThoughNothingComesAfterIt()
    {
        var view = RoomFooterState.Evaluate(Match() with { Stage = RoomStage.ResultRated, ResultWon = true, AfterResult = null });
        Assert.Equal(RoomDot.Green, view.Dot);
        Assert.Equal("RtFtFollow", view.Sub!.Key);
    }

    [Theory]
    [InlineData(true, "RtFtCountdownHost")]
    [InlineData(false, "RtFtCountdownGuest")]
    public void TheCountdownTellsEachSideWhatToDoInTheGame(bool host, string sub)
    {
        var view = RoomFooterState.Evaluate(Match(host) with { Stage = RoomStage.Countdown });
        Assert.Equal(sub, view.Sub!.Key);
        Assert.Equal(RoomClock.Countdown, view.Clock);
        Assert.Equal(RoomLeave.None, view.Leave);
    }

    [Fact]
    public void InAGameTheWayOutIsAbandoning()
    {
        var view = RoomFooterState.Evaluate(Match() with { Stage = RoomStage.InGame });
        Assert.Equal(RoomLeave.Abandon, view.Leave);
        Assert.Equal(RoomClock.Match, view.Clock);
    }

    private static Models.Multiplayer.TournamentDetail Bracket() => new()
    {
        Id = "t", Name = "Copa", Format = "1v1", OwnerName = "Kaiser", RoundsTotal = 3,
        Entrants = new()
        {
            new() { Id = "a", DisplayName = "Gorgo", Seed = 1, MemberIds = new() { "u1" } },
            new() { Id = "b", DisplayName = "Cuzco", Seed = 8, MemberIds = new() { "u2" } },
            new() { Id = "c", DisplayName = "Mariscal", Seed = 4, MemberIds = new() { "u3" } },
            new() { Id = "d", DisplayName = "Almirante", Seed = 5, MemberIds = new() { "u4" } },
        },
        Matches = new()
        {
            new() { Id = "m1", Round = 1, Position = 0, Entrant1Id = "a", Entrant2Id = "b", NextMatchId = "s1", NextSlot = 1 },
            new() { Id = "m2", Round = 1, Position = 1, Entrant1Id = "c", Entrant2Id = "d", NextMatchId = "s1", NextSlot = 2 },
            new() { Id = "s1", Round = 2, Position = 0, NextMatchId = "f", NextSlot = 1 },
            new() { Id = "f", Round = 3, Position = 0 },
        },
    };

    [Fact]
    public void TheContextReadsTheSlotOutOfTheBracket()
    {
        var c = TournamentRoomContext.From(Bracket(), "m1")!;
        Assert.Equal("Kaiser", c.OrganizerName);
        Assert.Equal("Gorgo", c.Side1.Name);
        Assert.Equal(1, c.SideOf("u1"));
        Assert.Equal("Cuzco", c.RivalOf("u1").Name);
        Assert.Equal("Mariscal", c.Next!.PairA);
        Assert.Equal("RtRoundTheSemi", c.Next.RoundPhraseKey);
    }

    [Fact]
    public void ARivalAlreadyThroughIsNamedInsteadOfThePair()
    {
        var t = Bracket();
        t.Matches!.First(m => m.Id == "s1").Entrant2Id = "c";
        Assert.Equal("Mariscal", TournamentRoomContext.From(t, "m1")!.Next!.Rival);
    }

    [Fact]
    public void AnUnknownSlotGivesNoContextRatherThanAGuess()
    {
        Assert.Null(TournamentRoomContext.From(Bracket(), "nope"));
        Assert.Null(TournamentRoomContext.From(Bracket(), "s1"));
    }

    [Fact]
    public void TheFinalHasNoNextRound()
    {
        var t = Bracket();
        var f = t.Matches!.First(m => m.Id == "f");
        f.Entrant1Id = "a";
        f.Entrant2Id = "c";
        var c = TournamentRoomContext.From(t, "f")!;
        Assert.True(c.IsFinal);
        Assert.Equal("RtStakeFinal", c.StakeLines()[0].Text.Key);
    }

    [Theory]
    [InlineData(1, 5, "MpTournamentRoundSixteenth")]
    [InlineData(2, 5, "MpTournamentRoundEighth")]
    [InlineData(3, 5, "MpTournamentRoundQuarter")]
    [InlineData(1, 6, "MpTournamentRoundThirtySecond")]
    [InlineData(1, 7, "MpTournamentRoundN")]
    public void TheEarlyRoundsHaveNamesUpToTheRoundOf64(int round, int total, string key)
        => Assert.Equal(key, BracketLayout.RoundLabelKey(round, total));

    [Fact]
    public void EveryDesign85SampleIsABracketRoom()
    {
        foreach (var id in "abcdefghi".Select(c => "85" + c))
        {
            var sample = RoomDemoData.ByName(id);
            Assert.Equal(id, sample.Name);
            Assert.NotNull(sample.Tournament);
        }
    }

    [Theory]
    [InlineData("WoL 1v1", "WoL 1v1 (rematch)")]
    [InlineData("WoL 1v1 (rematch)", "WoL 1v1 (rematch)")]
    public void ARematchRoomIsMarkedOnce(string title, string expected)
        => Assert.Equal(expected, RematchPlan.Title(title, 64));

    [Fact]
    public void TheMarkAlwaysFitsTheServersLimit()
    {
        var title = RematchPlan.Title(new string('x', 64), 64);
        Assert.Equal(64, title.Length);
        Assert.EndsWith(RematchPlan.Suffix, title);
    }

    [Theory]
    [InlineData(true, "Kaiser", MatchVerdict.Win, true)]
    [InlineData(true, "Kaiser", MatchVerdict.Loss, true)]
    [InlineData(false, "Kaiser", MatchVerdict.Win, false)]
    [InlineData(true, null, MatchVerdict.Win, false)]
    [InlineData(true, "Kaiser", MatchVerdict.NoResult, false)]
    public void ARematchNeedsADecided1v1AndSomebodyToInvite(bool oneVOne, string? rival, MatchVerdict verdict, bool offered)
        => Assert.Equal(offered, RematchPlan.Offered(oneVOne, rival, verdict));

    [Fact]
    public void ThePlacesToTheNextAgeCountFromItsLastPosition()
    {
        var bounds = RankAges.Bounds(9);
        var next = RankAges.PlacesToNextAge(bounds[2] + 1, 9);
        Assert.NotNull(next);
        Assert.Equal(1, next!.Value.Places);
        Assert.Equal(RankAge.Industrial, next.Value.Age);
        Assert.Null(RankAges.PlacesToNextAge(1, 9));
        Assert.Null(RankAges.PlacesToNextAge(0, 9));
    }

    [Fact]
    public void EveryDesign87SampleOpensByName()
    {
        foreach (var id in "abcdefghijkl".Select(c => "87" + c))
            Assert.Equal(id, RoomDemoData.ByName(id).Name);
    }
}
