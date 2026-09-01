using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="MatchHistoryView"/> — everything the History tab works out for itself.
///
/// <para>Most of these are REFUSALS, and that is the point: a 0.5 is never counted as a draw,
/// a timestamp that cannot be read never loses somebody their match, and a summary is never
/// computed over the filtered view.</para>
/// </summary>
public class MatchHistoryViewTests
{
    private static MatchHistoryRow Row(
        double result = 0.5,
        bool? rated = null,
        string? reason = null,
        string map = "ESOC Fertile Crescent",
        string started = "2026-08-29T17:50:00Z",
        double? before = null,
        double? after = null)
        => new()
        {
            Id = Guid.NewGuid().ToString(),
            ModId = "wol",
            MapName = map,
            StartedAt = started,
            EndedAt = started,
            Result = result,
            Rated = rated,
            UnratedReason = reason,
            RatingBefore = before,
            RatingAfter = after,
        };

    // -------------------------------------------------------------------- rated

    /// <summary>
    /// The SERVER's answer wins. It knows things the launcher cannot see — whether the room
    /// was competitive, whether the mod has a ladder, whether the recording had already been
    /// reported — and a match can have a perfectly readable result and still not have counted.
    /// </summary>
    [Fact]
    public void TheServerDecidesWhetherAMatchCounted()
    {
        // A clean win the server refused to rate: a friendly room.
        Assert.False(MatchHistoryView.IsRated(Row(result: 1.0, rated: false, reason: "not_competitive")));

        // And the reverse — the server says it counted, whatever the score looks like.
        Assert.True(MatchHistoryView.IsRated(Row(result: 0.5, rated: true)));
    }

    /// <summary>
    /// An older backend sends neither field, and the score is then the only thing to go on.
    /// This is the degradation that keeps every stored row rendering as it always did.
    /// </summary>
    [Theory]
    [InlineData(1.0, true)]
    [InlineData(0.0, true)]
    [InlineData(0.5, false)]
    public void WithNoServerAnswerTheScoreDecides(double result, bool expected)
    {
        Assert.Equal(expected, MatchHistoryView.IsRated(Row(result: result)));
    }

    // ------------------------------------------------------------------- filter

    [Fact]
    public void TheFilterSplitsOnWhatCounted()
    {
        var rows = new List<MatchHistoryRow>
        {
            Row(result: 1.0, rated: true),
            Row(result: 0.5, rated: false),
            Row(result: 0.0, rated: true),
        };

        Assert.Equal(3, MatchHistoryView.Filter(rows, HistoryFilter.All).Count);
        Assert.Equal(2, MatchHistoryView.Filter(rows, HistoryFilter.Rated).Count);
        Assert.Single(MatchHistoryView.Filter(rows, HistoryFilter.Unrated));
    }

    [Fact]
    public void NoHistoryIsNotACrash()
    {
        Assert.Empty(MatchHistoryView.Filter(null, HistoryFilter.All));
        Assert.Empty(MatchHistoryView.GroupByDay(null));
        Assert.Equal((null, 0), MatchHistoryView.TopMap(null));
    }

    // ---------------------------------------------------------------- grouping

    /// <summary>
    /// Newest day first, and newest match first inside it — so the page reads top-down in the
    /// order things happened, most recent first.
    /// </summary>
    [Fact]
    public void DaysAndMatchesAreNewestFirst()
    {
        var rows = new List<MatchHistoryRow>
        {
            Row(started: "2026-08-27T12:00:00Z", map: "old"),
            Row(started: "2026-08-29T19:00:00Z", map: "late"),
            Row(started: "2026-08-29T11:00:00Z", map: "early"),
        };

        var days = MatchHistoryView.GroupByDay(rows);
        Assert.Equal(2, days.Count);
        Assert.True(days[0].LocalDate > days[1].LocalDate);
        Assert.Equal("late", days[0].Matches[0].MapName);
        Assert.Equal("early", days[0].Matches[1].MapName);
    }

    /// <summary>
    /// THE ONE THAT MATTERS. A match with an unreadable timestamp is still LISTED — losing
    /// somebody's game out of their own history because of a malformed date would be worse
    /// than filing it oddly, and it files last, under a heading that admits what happened.
    /// </summary>
    [Fact]
    public void AMatchWithAnUnreadableDateIsStillShown()
    {
        var rows = new List<MatchHistoryRow>
        {
            Row(started: "not a date", map: "orphan"),
            Row(started: "2026-08-29T11:00:00Z", map: "real"),
        };

        var days = MatchHistoryView.GroupByDay(rows);
        var all = days.SelectMany(d => d.Matches).Select(m => m.MapName).ToList();

        Assert.Contains("orphan", all);
        Assert.Equal(2, all.Count);
        // Last, so it cannot be mistaken for the most recent thing that happened.
        Assert.Equal(DateTime.MinValue.Date, days[^1].LocalDate);
    }

    /// <summary>
    /// A timestamp with no zone marker is UTC — SQLite's <c>datetime('now')</c> writes them
    /// that way. Read as local time it would be hours out, which for an evening game means the
    /// wrong DAY.
    /// </summary>
    [Fact]
    public void ATimestampWithNoZoneIsReadAsUtc()
    {
        var bare = MatchHistoryView.ParseLocal("2026-08-29 17:50:00");
        var marked = MatchHistoryView.ParseLocal("2026-08-29T17:50:00Z");
        Assert.Equal(marked, bare);
    }

    /// <summary>An offset that IS stated must not be applied a second time.</summary>
    [Fact]
    public void AnExplicitOffsetIsNotAppliedTwice()
    {
        var withOffset = MatchHistoryView.ParseLocal("2026-08-29T12:50:00-05:00");
        var asUtc = MatchHistoryView.ParseLocal("2026-08-29T17:50:00Z");
        Assert.Equal(asUtc, withOffset);
    }

    [Fact]
    public void NothingIsNotATime()
    {
        Assert.Null(MatchHistoryView.ParseLocal(null));
        Assert.Null(MatchHistoryView.ParseLocal(""));
        Assert.Null(MatchHistoryView.ParseLocal("   "));
        Assert.Null(MatchHistoryView.ParseLocal("yesterday"));
    }

    // ----------------------------------------------------------------- top map

    [Fact]
    public void TheMostPlayedMapIsTheOnePlayedMost()
    {
        var rows = new List<MatchHistoryRow>
        {
            Row(map: "Fertile Crescent"),
            Row(map: "Malaysia"),
            Row(map: "Fertile Crescent"),
        };
        Assert.Equal(("Fertile Crescent", 2), MatchHistoryView.TopMap(rows));
    }

    /// <summary>
    /// A tie resolves the same way every time it is drawn. Without the tiebreak the card would
    /// name a different favourite map on each repaint, which is worse than naming none.
    /// </summary>
    [Fact]
    public void ATieIsBrokenStably()
    {
        var rows = new List<MatchHistoryRow> { Row(map: "Zanzibar"), Row(map: "Andes") };
        var first = MatchHistoryView.TopMap(rows);
        var reversed = MatchHistoryView.TopMap(rows.AsEnumerable().Reverse().ToList());
        Assert.Equal(first, reversed);
    }

    [Fact]
    public void MatchesWithNoMapNameDoNotBecomeAMap()
    {
        var rows = new List<MatchHistoryRow> { Row(map: ""), Row(map: null!) };
        Assert.Equal((null, 0), MatchHistoryView.TopMap(rows));
    }

    // ----------------------------------------------------------------- summary

    /// <summary>
    /// A 0.5 is counted as NEITHER a win nor a loss — the same refusal the badge makes, applied
    /// to the tally. Counting them as draws would report most of a player's history as drawn
    /// games that never happened.
    /// </summary>
    [Fact]
    public void TheTallyIgnoresMatchesNobodyCouldRead()
    {
        var rows = new List<MatchHistoryRow>
        {
            Row(result: 1.0, rated: true),
            Row(result: 0.0, rated: true),
            Row(result: 0.5, rated: false),
            Row(result: 0.5, rated: false),
        };

        var s = MatchHistoryView.Summarise(rows, null);
        Assert.Equal(1, s.Wins);
        Assert.Equal(1, s.Losses);
        Assert.Equal(2, s.Unrated);
    }

    /// <summary>
    /// The delta is the most recent match that MOVED the rating, not the most recent match.
    /// With most rows unrated, taking the newest row's would show an em dash to somebody who
    /// had just won.
    /// </summary>
    [Fact]
    public void TheDeltaComesFromTheLastMatchThatCounted()
    {
        var rows = new List<MatchHistoryRow>
        {
            Row(result: 0.5, rated: false),
            Row(result: 0.0, rated: true, before: 1500, after: 1383),
        };

        Assert.Equal(-117, MatchHistoryView.Summarise(rows, null).Delta);
    }

    /// <summary>
    /// No standing means no rating shown. The server hands every new player 1500 and printing
    /// that as though it were earned is the lie the whole rating surface refuses to tell.
    /// </summary>
    [Fact]
    public void AnUnknownStandingShowsNoRating()
    {
        Assert.Null(MatchHistoryView.Summarise(new List<MatchHistoryRow> { Row() }, null).Rating);
    }

    // ---------------------------------------------------------------- opponent

    [Fact]
    public void ADuelNamesTheOtherPlayer()
    {
        var row = Row(result: 0.0);
        row.Participants = new List<MatchHistoryParticipant>
        {
            new() { UserId = "me", DisplayName = "Gorgorito12" },
            new() { UserId = "them", DisplayName = "Aluclown" },
        };
        Assert.Equal("Aluclown", MatchHistoryView.SoleOpponent(row, "me"));
    }

    /// <summary>
    /// Past two players there is no "the opponent", so the card says nothing rather than
    /// naming one person out of several — the roster underneath lists them all.
    /// </summary>
    [Fact]
    public void MoreThanTwoPlayersHaveNoSoleOpponent()
    {
        var row = Row();
        row.Participants = new List<MatchHistoryParticipant>
        {
            new() { UserId = "me", DisplayName = "Gorgorito12" },
            new() { UserId = "a", DisplayName = "Aluclown" },
            new() { UserId = "b", DisplayName = "Kaiser" },
        };
        Assert.Null(MatchHistoryView.SoleOpponent(row, "me"));
    }

    [Fact]
    public void ARosterWithoutUsNamesNobody()
    {
        var row = Row();
        row.Participants = new List<MatchHistoryParticipant>
        {
            new() { UserId = "a", DisplayName = "Aluclown" },
            new() { UserId = "b", DisplayName = "Kaiser" },
        };
        // Two rows, neither of them ours: there is no "against", because we are not in it.
        Assert.Null(MatchHistoryView.SoleOpponent(row, "me"));
    }

    [Fact]
    public void AnEmptyRosterNamesNobody()
    {
        Assert.Null(MatchHistoryView.SoleOpponent(Row(), "me"));
    }
}
