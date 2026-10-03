using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// When the bell says "Season N is over". Three traps, and each has a test that fails on it:
/// the flood (a first sight must never ring), seeding from absence (no calendar is not "season
/// 0"), and the place that arrives separately (ring only with a standing from the NEW season).
/// </summary>
[Collection("wpf-and-language")]
public class SeasonNoticeTests
{
    private static SeasonInfo Calendar(int current) => new()
    {
        Current = current,
        EndsAt = "2027-03-01T06:00:00.000Z",
        List = Enumerable.Range(1, current).Select(n => new SeasonListEntry { Number = n, Closed = n < current }).ToList(),
    };

    private static EloSnapshot Standing(int season, params PastSeasonEntry[] past) => new()
    {
        Season = season,
        PastSeasons = past.ToList(),
    };

    /// <summary>
    /// THE ONE THAT MATTERS. A backend older than seasons sends no calendar, and recording that as
    /// anything would make the first payload that DID carry one read as a season ending.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_NoCalendarIsNeverRecorded()
    {
        Assert.Equal(SeasonNoticeStep.Nothing, SeasonNotice.Plan(null, null, null).Step);
        Assert.Equal(SeasonNoticeStep.Nothing, SeasonNotice.Plan(2, null, Standing(2)).Step);
        Assert.Equal(SeasonNoticeStep.Nothing, SeasonNotice.Plan(null, new SeasonInfo { Current = 0 }, null).Step);
    }

    /// <summary>Somebody who installs the launcher in Season 4 must not be told that Season 3 ended.</summary>
    [Fact]
    public void TheFirstSightIsRecordedSilently()
    {
        var plan = SeasonNotice.Plan(null, Calendar(4), Standing(4));
        Assert.Equal(SeasonNoticeStep.Seed, plan.Step);
        Assert.Equal(4, plan.Current);
    }

    [Fact]
    public void TheSameSeasonSaysNothing()
        => Assert.Equal(SeasonNoticeStep.Nothing, SeasonNotice.Plan(2, Calendar(2), Standing(2)).Step);

    /// <summary>The final place comes with /matches/elo, not with the calendar.</summary>
    [Fact]
    public void AnEndedSeasonWaitsForAStandingFromTheNewOne()
    {
        Assert.Equal(SeasonNoticeStep.NeedStanding, SeasonNotice.Plan(1, Calendar(2), null).Step);
        Assert.Equal(SeasonNoticeStep.NeedStanding, SeasonNotice.Plan(1, Calendar(2), Standing(1)).Step);
        Assert.Equal(SeasonNoticeStep.NeedStanding, SeasonNotice.Plan(1, Calendar(2), new EloSnapshot()).Step);
    }

    [Fact]
    public void AnEndedSeasonRingsWithBothPlaces_1v1First()
    {
        var plan = SeasonNotice.Plan(1, Calendar(2), Standing(2,
            new PastSeasonEntry { Season = 1, Mode = "team", Place = 2, Size = 6 },
            new PastSeasonEntry { Season = 1, Mode = "default", Place = 3, Size = 18 },
            new PastSeasonEntry { Season = 0, Mode = "default", Place = 1, Size = 4 }));
        Assert.Equal(SeasonNoticeStep.Ring, plan.Step);
        Assert.Equal(1, plan.Ended);
        Assert.Equal(2, plan.Current);
        Assert.Equal(new[] { "default", "team" }, plan.Places.Select(p => p.Mode));
    }

    /// <summary>Away for two seasons: one bell, for the most recent end — never two at once.</summary>
    [Fact]
    public void AwayForTwoSeasonsRingsOnlyForTheLatestEnd()
    {
        var plan = SeasonNotice.Plan(1, Calendar(3), Standing(3,
            new PastSeasonEntry { Season = 1, Mode = "default", Place = 1, Size = 10 },
            new PastSeasonEntry { Season = 2, Mode = "default", Place = 5, Size = 12 }));
        Assert.Equal(SeasonNoticeStep.Ring, plan.Step);
        Assert.Equal(2, plan.Ended);
        Assert.Equal(5, Assert.Single(plan.Places).Place);
    }

    /// <summary>
    /// A remembered season AHEAD of the server's — a config carried over from a test server —
    /// is re-recorded silently, or the bell would stay mute until the real calendar caught up.
    /// </summary>
    [Fact]
    public void ASeasonAheadOfTheServerIsReRecordedSilently()
        => Assert.Equal(SeasonNoticeStep.Seed, SeasonNotice.Plan(5, Calendar(2), Standing(2)).Step);

    [Fact]
    public void TheBellNamesTheSeasonAndEveryPlace()
    {
        var previous = Strings.Language;
        try
        {
            Strings.Language = Strings.LangEn;
            var plan = new SeasonNoticePlan(SeasonNoticeStep.Ring, 2, 1, new List<PastSeasonEntry>
            {
                new() { Season = 1, Mode = "default", Place = 3, Size = 18 },
                new() { Season = 1, Mode = "team", Place = 2, Size = 6 },
            });
            var (title, body) = SeasonNotice.Text(plan);
            Assert.Contains("1", title);
            Assert.Contains("#3 of 18 in 1v1", body);
            Assert.Contains("#2 of 6 in Teams", body);
            Assert.Contains("Season 2", body);

            Strings.Language = Strings.LangEs;
            var (tituloEs, cuerpoEs) = SeasonNotice.Text(plan);
            Assert.Contains("Temporada 1", tituloEs);
            Assert.Contains("#3 de 18 en 1v1", cuerpoEs);
        }
        finally
        {
            Strings.Language = previous;
        }
    }

    /// <summary>A player who finished on no table is told the news without a place — never "last".</summary>
    [Fact]
    public void NoPlaceStillAnnouncesTheNewSeason()
    {
        var previous = Strings.Language;
        try
        {
            Strings.Language = Strings.LangEn;
            var (_, body) = SeasonNotice.Text(new SeasonNoticePlan(SeasonNoticeStep.Ring, 2, 1, new List<PastSeasonEntry>()));
            Assert.Contains("Season 2", body);
            Assert.DoesNotContain("#", body);
        }
        finally
        {
            Strings.Language = previous;
        }
    }
}
