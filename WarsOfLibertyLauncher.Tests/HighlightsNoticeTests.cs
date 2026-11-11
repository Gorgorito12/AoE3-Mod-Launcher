using System;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Last month's highlights in the bell (2026-11-10). The refusals are the point: an empty month, a
/// month already said, a launcher first run late in the month, and an older month say nothing.
/// </summary>
[Collection("wpf-and-language")]
public class HighlightsNoticeTests
{
    private static readonly DateTime Nov3 = new(2026, 11, 3, 12, 0, 0);

    private static MonthlyHighlights Payload(string month = "2026-10", int rated = 119) => new()
    {
        Previous = new MonthHighlights
        {
            Month = month,
            TotalRated = rated,
            BiggestClimb = new HighlightPerMode { Default = new HighlightPlayer { DisplayName = "El Taita", Points = 197 } },
            MostMatches = new HighlightPlayer { DisplayName = "Unstoppable", Matches = 46 },
            BestStreak = new HighlightPerMode { Default = new HighlightPlayer { DisplayName = "NathanR06", Wins = 8 } },
        },
    };

    [Fact]
    public void THE_ONE_THAT_MATTERS_TheFirstWeekSaysItOnce()
    {
        var d = HighlightsNotice.Decide(Payload(), Nov3, "");
        Assert.True(d.Notify);
        Assert.True(d.MarkSeen);
        Assert.Equal("2026-10", d.Month);
        Assert.Contains("El Taita", d.Body);
        Assert.Contains("197", d.Body);
        Assert.Contains("NathanR06", d.Body);

        // Said: never again.
        Assert.False(HighlightsNotice.Decide(Payload(), Nov3, "2026-10").Notify);
    }

    [Fact]
    public void AnEmptyMonthIsMarkedSeenAndSaysNothing()
    {
        var d = HighlightsNotice.Decide(Payload(rated: 0), Nov3, "");
        Assert.False(d.Notify);
        Assert.True(d.MarkSeen);
    }

    [Fact]
    public void AfterTheFirstWeekNothingIsSaid()
        => Assert.False(HighlightsNotice.Decide(Payload(), new DateTime(2026, 11, 8), "").Notify);

    /// <summary>A server answering with an older "previous" month (a stale cache) is not last month.</summary>
    [Fact]
    public void AnOlderMonthIsNeverSaid()
    {
        var d = HighlightsNotice.Decide(Payload(month: "2026-09"), Nov3, "");
        Assert.False(d.Notify);
        Assert.False(d.MarkSeen);
        Assert.False(HighlightsNotice.Decide(null, Nov3, "").Notify);
    }

    [Fact]
    public void ACategoryWithNobodyIsLeftOut()
    {
        var month = Payload().Previous!;
        month.BestStreak = null;
        month.MostMatches = null;
        var body = HighlightsNotice.Body(month);
        Assert.Contains("El Taita", body);
        Assert.DoesNotContain("·", body);
        // With no category at all, the month's count.
        month.BiggestClimb = null;
        Assert.Contains("119", HighlightsNotice.Body(month));
    }

    [Fact]
    public void BothLanguagesNameTheMonth()
    {
        var previous = Strings.Language;
        try
        {
            Strings.SetLanguage("es");
            Assert.Contains("octubre", HighlightsNotice.Decide(Payload(), Nov3, "").Title);
            Strings.SetLanguage("en");
            Assert.Contains("October", HighlightsNotice.Decide(Payload(), Nov3, "").Title);
        }
        finally { Strings.SetLanguage(previous); }
    }

    [Fact]
    public void TheBellRefusesAMonthAlreadySaid()
    {
        var config = new LauncherConfig();
        var center = new NotificationCenter(config);
        Assert.True(center.RaiseMonthlyHighlights("2026-10", "t", "b"));
        Assert.False(center.RaiseMonthlyHighlights("2026-10", "t", "b"));
        Assert.Equal("2026-10", config.NotifiedHighlightsMonth);
        Assert.Equal(NotificationKind.MonthlyHighlights, center.Items[0].Kind);
    }
}
