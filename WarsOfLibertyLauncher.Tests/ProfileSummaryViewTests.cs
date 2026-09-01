using System;
using System.Collections.Generic;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="ProfileSummaryView"/> — the numbers the Profile tab computes for itself.
///
/// <para>The two that matter most are refusals: a single match does not draw a curve, and the
/// player's own teammate is never reported as their rival.</para>
/// </summary>
public class ProfileSummaryViewTests
{
    private static MatchHistoryRow Row(
        double result = 0.5,
        bool? rated = null,
        double? before = null,
        double? after = null,
        string map = "ESOC Fertile Crescent")
        => new()
        {
            Id = Guid.NewGuid().ToString(),
            ModId = "wol",
            MapName = map,
            StartedAt = "2026-08-29T17:50:00Z",
            EndedAt = "2026-08-29T19:09:00Z",
            Result = result,
            Rated = rated,
            RatingBefore = before,
            RatingAfter = after,
        };

    private static MatchHistoryParticipant P(string id, string name, int team, double result)
        => new() { UserId = id, DisplayName = name, Team = team, Result = result };

    // ------------------------------------------------------------------- curve

    /// <summary>
    /// THE ONE THAT MATTERS. One match gives two points — where you started and where you are
    /// — so the card can draw a line rather than a dot, and the "1500 start" label under it is
    /// true. Fewer than two and the caller says so instead of drawing a flat stroke, which
    /// would be a claim about a rating that has held steady.
    /// </summary>
    [Fact]
    public void OneRatedMatchIsAStartAndAnEnd()
    {
        var rows = new List<MatchHistoryRow>
        {
            Row(result: 0.0, rated: true, before: 1500, after: 1383),
        };

        Assert.Equal(new[] { 1500.0, 1383.0 }, ProfileSummaryView.RatingCurve(rows));
    }

    [Fact]
    public void NothingRatedIsNoCurveAtAll()
    {
        var rows = new List<MatchHistoryRow>
        {
            Row(result: 0.5, rated: false),
            Row(result: 0.5, rated: false),
        };
        Assert.Empty(ProfileSummaryView.RatingCurve(rows));
        Assert.Empty(ProfileSummaryView.RatingCurve(null));
    }

    /// <summary>
    /// The server sends newest first and a curve reads oldest first. Getting this backwards
    /// draws every player's history in reverse, which looks entirely plausible.
    /// </summary>
    [Fact]
    public void TheCurveRunsOldestToNewest()
    {
        var rows = new List<MatchHistoryRow>
        {
            Row(result: 1.0, rated: true, before: 1400, after: 1450),   // newest
            Row(result: 0.0, rated: true, before: 1500, after: 1400),   // oldest
        };

        Assert.Equal(new[] { 1500.0, 1400.0, 1450.0 }, ProfileSummaryView.RatingCurve(rows));
    }

    /// <summary>
    /// Unrated matches are not points on the line. Including them would draw a flat step for
    /// every match that moved nothing — which is most of them — and the shape of the line is
    /// the entire content of that card.
    /// </summary>
    [Fact]
    public void MatchesThatDidNotCountAreNotPoints()
    {
        var rows = new List<MatchHistoryRow>
        {
            Row(result: 0.5, rated: false, before: 1383, after: 1383),
            Row(result: 0.0, rated: true, before: 1500, after: 1383),
        };
        Assert.Equal(2, ProfileSummaryView.RatingCurve(rows).Count);
    }

    // ------------------------------------------------------------------ totals

    /// <summary>
    /// All three numbers, because the first alone misleads: "3 matches" beside a record of 0-1
    /// reads as a contradiction until the other two explain it.
    /// </summary>
    [Fact]
    public void TotalsSplitPlayedFromDecided()
    {
        var rows = new List<MatchHistoryRow>
        {
            Row(result: 0.0, rated: true),
            Row(result: 0.5, rated: false),
            Row(result: 0.5, rated: false),
        };

        var t = ProfileSummaryView.Totals(rows);
        Assert.Equal(3, t.Played);
        Assert.Equal(1, t.Decided);
        Assert.Equal(2, t.Unrated);
    }

    [Fact]
    public void NoHistoryIsThreeZeroes()
    {
        var t = ProfileSummaryView.Totals(null);
        Assert.Equal(0, t.Played);
        Assert.Equal(0, t.Decided);
        Assert.Equal(0, t.Unrated);
    }

    // ---------------------------------------------------------------- opponent

    [Fact]
    public void TheUsualOpponentIsTheOneFacedMost()
    {
        var a = Row(result: 0.0, rated: true);
        a.Participants = new List<MatchHistoryParticipant> { P("me", "Me", 0, 0.0), P("alu", "Aluclown", 0, 1.0) };
        var b = Row(result: 1.0, rated: true);
        b.Participants = new List<MatchHistoryParticipant> { P("me", "Me", 0, 1.0), P("alu", "Aluclown", 0, 0.0) };
        var c = Row(result: 0.0, rated: true);
        c.Participants = new List<MatchHistoryParticipant> { P("me", "Me", 0, 0.0), P("kai", "Kaiser", 0, 1.0) };

        var rival = ProfileSummaryView.FrequentOpponent(new[] { a, b, c }, "me");

        Assert.NotNull(rival);
        Assert.Equal("Aluclown", rival!.Name);
        Assert.Equal(2, rival.Matches);
        Assert.Equal(1, rival.Wins);
        Assert.Equal(1, rival.Losses);
    }

    /// <summary>
    /// THE SECOND ONE THAT MATTERS. A team match lists everybody, so counting the roster
    /// wholesale would eventually report the player's own partner as their rival. In a 2v2 the
    /// partner has to be skipped, and only the other side counted.
    /// </summary>
    [Fact]
    public void YourOwnTeammateIsNeverYourRival()
    {
        var match = Row(result: 1.0, rated: true);
        match.Participants = new List<MatchHistoryParticipant>
        {
            P("me", "Me", 0, 1.0),
            P("mate", "Partner", 0, 1.0),
            P("foe1", "Aluclown", 1, 0.0),
            P("foe2", "Kaiser", 1, 0.0),
        };

        var rival = ProfileSummaryView.FrequentOpponent(new[] { match }, "me");
        Assert.NotNull(rival);
        Assert.NotEqual("Partner", rival!.Name);
    }

    /// <summary>
    /// A 1v1 stores BOTH players as team 0 — every one of them, and every pre-team row ever
    /// stored. Skipping same-team players without this exception would exclude the opponent of
    /// every duel ever played, i.e. the card would be permanently empty for a 1v1 player.
    /// </summary>
    [Fact]
    public void ADuelStoredAsOneTeamStillHasAnOpponent()
    {
        var match = Row(result: 0.0, rated: true);
        match.Participants = new List<MatchHistoryParticipant>
        {
            P("me", "Me", 0, 0.0),
            P("alu", "Aluclown", 0, 1.0),
        };

        Assert.Equal("Aluclown", ProfileSummaryView.FrequentOpponent(new[] { match }, "me")?.Name);
    }

    [Fact]
    public void ATieNamesTheSameRivalEveryTime()
    {
        var a = Row(result: 0.0, rated: true);
        a.Participants = new List<MatchHistoryParticipant> { P("me", "Me", 0, 0.0), P("z", "Zulu", 0, 1.0) };
        var b = Row(result: 0.0, rated: true);
        b.Participants = new List<MatchHistoryParticipant> { P("me", "Me", 0, 0.0), P("a", "Andes", 0, 1.0) };

        var first = ProfileSummaryView.FrequentOpponent(new[] { a, b }, "me")?.Name;
        var reversed = ProfileSummaryView.FrequentOpponent(new[] { b, a }, "me")?.Name;
        Assert.Equal(first, reversed);
    }

    [Fact]
    public void NoRosterAndNoSelfMeanNoRival()
    {
        Assert.Null(ProfileSummaryView.FrequentOpponent(new[] { Row() }, "me"));
        Assert.Null(ProfileSummaryView.FrequentOpponent(null, "me"));
        Assert.Null(ProfileSummaryView.FrequentOpponent(new[] { Row() }, null));
    }

    // --------------------------------------------------------------- the ladder

    [Theory]
    [InlineData(5, 0, 5)]
    [InlineData(5, 1, 4)]
    [InlineData(5, 5, 0)]
    [InlineData(5, 9, 0)]
    public void TheDistanceToTheLadderCountsDown(int bar, int played, int expected)
    {
        Assert.Equal(expected, ProfileSummaryView.MatchesToLadder(bar, played));
    }

    /// <summary>
    /// A server that did not state the bar states no distance either. Guessing the rule is how
    /// the launcher would end up telling a player the wrong number of matches to play.
    /// </summary>
    [Fact]
    public void NoThresholdMeansNoClaim()
    {
        Assert.Equal(0, ProfileSummaryView.MatchesToLadder(0, 0));
        Assert.False(ProfileSummaryView.IsProvisional(0, 0));
    }

    [Fact]
    public void ProvisionalMeansNotOnTheLadderYet()
    {
        Assert.True(ProfileSummaryView.IsProvisional(5, 1));
        Assert.False(ProfileSummaryView.IsProvisional(5, 5));
    }

    /// <summary>Negative counts arrive over the wire and must not produce a negative distance.</summary>
    [Fact]
    public void NonsenseFromTheWireDoesNotProduceNonsense()
    {
        Assert.Equal(5, ProfileSummaryView.MatchesToLadder(5, -3));
        Assert.Equal(0, ProfileSummaryView.MatchesToLadder(-5, 0));
    }
}
