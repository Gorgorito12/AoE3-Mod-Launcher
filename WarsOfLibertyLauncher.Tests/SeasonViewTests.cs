using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// How the launcher DRAWS rating seasons. As everywhere in this corner of the code the refusals
/// are the point: a backend older than seasons sends no calendar, and every answer must then be
/// what the launcher drew before — never an invented "season 1".
/// </summary>
public class SeasonViewTests
{
    private static SeasonInfo Calendar(int current, params int[] numbers) => new()
    {
        Current = current,
        EndsAt = "2027-03-01T06:00:00.000Z",
        List = numbers.Select(n => new SeasonListEntry
        {
            Number = n,
            EndsAt = "2027-03-01T06:00:00.000Z",
            Closed = n < current,
        }).ToList(),
    };

    [Fact]
    public void NoCalendarOffersNothing()
    {
        Assert.Empty(SeasonView.SelectorEntries(null));
        Assert.False(SeasonView.OffersAChoice(null));
        Assert.Null(SeasonView.PastSeasonOrNull(1, null));
    }

    [Fact]
    public void TheSelectorListsNewestFirst_AndNothingAheadOfTheRunningSeason()
    {
        var entries = SeasonView.SelectorEntries(Calendar(3, 1, 2, 3, 4, 2));
        Assert.Equal(new[] { 3, 2, 1 }, entries.Select(e => e.Number));
    }

    /// <summary>
    /// The whole first season is this state: "Season 1" is the only thing there is to pick, and a
    /// choice of one is not a choice.
    /// </summary>
    [Fact]
    public void ASingleSeasonIsNotAChoice()
    {
        Assert.False(SeasonView.OffersAChoice(Calendar(1, 1)));
        Assert.True(SeasonView.OffersAChoice(Calendar(2, 1, 2)));
    }

    [Theory]
    [InlineData(null, null)]   // nothing picked: the running season
    [InlineData(3, null)]      // the running season itself is not "past"
    [InlineData(2, 2)]
    [InlineData(1, 1)]
    [InlineData(0, null)]      // not a season
    [InlineData(5, null)]      // ahead of the server
    public void APickReadsAsAPastSeasonOnlyWhenTheServerListsItAsEnded(int? picked, int? expected)
        => Assert.Equal(expected, SeasonView.PastSeasonOrNull(picked, Calendar(3, 1, 2, 3)));

    [Fact]
    public void APickTheCalendarNoLongerListsReadsAsTheRunningSeason()
        => Assert.Null(SeasonView.PastSeasonOrNull(1, Calendar(3, 2, 3)));

    /// <summary>
    /// Seasons end at 06:00 UTC: midnight in Central America, the morning of the 1st in Spain.
    /// Each reader is told the last day that is true where they are.
    /// </summary>
    [Fact]
    public void TheLastDayIsTheViewersOwn()
    {
        var centralAmerica = TimeZoneInfo.CreateCustomTimeZone("UTC-6", TimeSpan.FromHours(-6), "UTC-6", "UTC-6");
        var spain = TimeZoneInfo.CreateCustomTimeZone("UTC+1", TimeSpan.FromHours(1), "UTC+1", "UTC+1");
        Assert.Equal(new DateTime(2027, 2, 28), SeasonView.LastLocalDay("2027-03-01T06:00:00.000Z", centralAmerica));
        Assert.Equal(new DateTime(2027, 3, 1), SeasonView.LastLocalDay("2027-03-01T06:00:00.000Z", spain));
        Assert.Null(SeasonView.LastLocalDay("not a date", spain));
        Assert.Null(SeasonView.LastLocalDay(null, spain));
    }

    [Theory]
    [InlineData(1, SeasonMedal.Gold)]
    [InlineData(2, SeasonMedal.Silver)]
    [InlineData(3, SeasonMedal.Bronze)]
    [InlineData(4, SeasonMedal.None)]
    [InlineData(0, SeasonMedal.None)]
    public void OnlyTheTopThreeEarnAMetal(int place, SeasonMedal medal)
        => Assert.Equal(medal, SeasonView.MedalFor(place));

    /// <summary>A medal is a claim about a finish, and a wrong one is worse than none.</summary>
    [Fact]
    public void ATitleThatIsNotATopThreeFinishDrawsNothing()
    {
        Assert.False(SeasonView.IsDrawable(null));
        Assert.False(SeasonView.IsDrawable(new SeasonTitleInfo { Season = 1, Place = 4 }));
        Assert.False(SeasonView.IsDrawable(new SeasonTitleInfo { Season = 0, Place = 1 }));
        Assert.True(SeasonView.IsDrawable(new SeasonTitleInfo { Season = 1, Place = 3 }));
    }

    [Fact]
    public void TheProfileListsNewestSeasonFirst_And1v1BeforeTeams()
    {
        var standing = new EloSnapshot
        {
            PastSeasons = new List<PastSeasonEntry>
            {
                new() { Season = 1, Mode = "default", Place = 4, Size = 18 },
                new() { Season = 2, Mode = "team", Place = 2, Size = 6 },
                new() { Season = 2, Mode = "default", Place = 1, Size = 20 },
                new() { Season = 3, Mode = "default", Place = 0, Size = 9 },   // no real place
            },
        };
        var lines = SeasonView.ProfileLines(standing);
        Assert.Equal(new[] { "2|default", "2|team", "1|default" },
            lines.Select(l => $"{l.Season}|{l.Mode}"));
    }

    [Fact]
    public void NoHistoryIsNoLines_NotAnError()
    {
        Assert.Empty(SeasonView.ProfileLines(null));
        Assert.Empty(SeasonView.ProfileLines(new EloSnapshot { PastSeasons = null }));
    }

    /// <summary>
    /// The curve is drawn one season at a time: across a reset, a 2000 player's line would fall
    /// 250 points at one stroke between two matches he won.
    /// </summary>
    [Fact]
    public void TheCurveKeepsOnlyTheRunningSeason_AndEverythingWhenSeasonsAreUnknown()
    {
        var rows = new List<MatchHistoryRow>
        {
            new() { Id = "a", Season = 2 },
            new() { Id = "b", Season = 1 },
            new() { Id = "c", Season = 2 },
        };
        Assert.Equal(new[] { "a", "c" }, SeasonView.RowsOfSeason(rows, 2)!.Select(r => r.Id));
        Assert.Same(rows, SeasonView.RowsOfSeason(rows, null));
    }

    /// <summary>
    /// "1750 +15" on the first day of a season reads as fifteen points gained to reach 1750 —
    /// false: the 1750 came from the reset, and the fifteen were won on a rating that no longer
    /// exists.
    /// </summary>
    [Fact]
    public void TheHeaderDeltaIsDroppedWhenItsMatchBelongsToAnEarlierSeason()
    {
        var rows = new List<MatchHistoryRow>
        {
            new() { Id = "old", Season = 1, Rated = true, RatingBefore = 1600, RatingAfter = 1615 },
        };
        Assert.Null(SeasonView.ScopedDelta(15, rows, currentSeason: 2));
        Assert.Equal(15, SeasonView.ScopedDelta(15, rows, currentSeason: 1));
        // Nothing to compare against: keep what the header always showed.
        Assert.Equal(15, SeasonView.ScopedDelta(15, rows, currentSeason: null));
    }
}
