using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// What Ranking › Matches asks <c>GET /matches</c> for, and the same rules over the preview's
/// samples. The request with nothing set must be the one the view always made, and the preview
/// must filter exactly as the server does, or it shows a list no server would.
/// </summary>
public class MatchBrowseQueryTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 18, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// THE ONE THAT MATTERS: with nothing new set, the path is byte-for-byte the old one — an
    /// older server must not be sent anything it never asked for.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_ADefaultQueryIsTheRequestThisViewAlwaysMade()
    {
        Assert.Equal("matches?limit=30", new MatchBrowseQuery().ToPath(null, 30));
        Assert.Equal("matches?limit=30&cursor=abc&q=Kai%20ser&replay=1&mod=wol",
            new MatchBrowseQuery { Query = " Kai ser ", ReplayOnly = true, ModId = "wol" }.ToPath("abc", 30));
    }

    [Fact]
    public void EveryNewFilterIsSentOnlyWhenSet()
    {
        var q = new MatchBrowseQuery
        {
            Sort = MatchBrowseSort.Oldest, Days = 7, Kind = MatchBrowseKind.Casual, DecidedOnly = true,
        };
        Assert.Equal("matches?limit=30&sort=oldest&days=7&kind=casual&decided=1", q.ToPath(null, 30));
        Assert.EndsWith("&kind=competitive", new MatchBrowseQuery { Kind = MatchBrowseKind.Competitive }.ToPath(null, 30));
        // A window the server would not accept is not sent at all.
        Assert.Equal("matches?limit=30", new MatchBrowseQuery { Days = 3 }.ToPath(null, 30));
    }

    /// <summary>A page that names only some of the four, or none, is an older server.</summary>
    [Fact]
    public void TheServerAppliesTheFiltersOnlyWhenItNamesAllFour()
    {
        Assert.False(MatchBrowseQuery.ServerApplies(null));
        Assert.False(MatchBrowseQuery.ServerApplies(new List<string>()));
        Assert.False(MatchBrowseQuery.ServerApplies(new List<string> { "sort", "days" }));
        Assert.True(MatchBrowseQuery.ServerApplies(new List<string> { "sort", "days", "kind", "decided" }));
        Assert.True(MatchBrowseQuery.ServerApplies(new List<string> { "decided", "KIND", "days", "sort", "later" }));
    }

    /// <summary>The order is a way of reading the list, not a filter on it.</summary>
    [Fact]
    public void TheOrderIsNotAFilter()
    {
        Assert.False(new MatchBrowseQuery { Sort = MatchBrowseSort.Oldest }.IsFiltered);
        Assert.True(new MatchBrowseQuery { Days = 1 }.IsFiltered);
        Assert.True(new MatchBrowseQuery { DecidedOnly = true }.NarrowsBeyondTheSearch);
        Assert.False(new MatchBrowseQuery { Query = "kai", ReplayOnly = true }.NarrowsBeyondTheSearch);
    }

    /// <summary>
    /// The kind follows the room, and a match whose room is unknown is in NEITHER kind — the
    /// server's rule, and the mode label's: an unknown room is never called casual.
    /// </summary>
    [Fact]
    public void AMatchWhoseRoomIsUnknownIsInNeitherKind()
    {
        var all = ReplayDemoData.Matches(Now);
        all.Add(new CommunityMatch { Id = "unknown-room", ModId = "wol", ReportedAt = "2026-10-09 17:00:00", Competitive = null });

        var competitive = new MatchBrowseQuery { Kind = MatchBrowseKind.Competitive }.Apply(all, Now).ToList();
        var casual = new MatchBrowseQuery { Kind = MatchBrowseKind.Casual }.Apply(all, Now).ToList();
        Assert.NotEmpty(competitive);
        Assert.NotEmpty(casual);
        Assert.All(competitive, m => Assert.True(m.Competitive));
        Assert.All(casual, m => Assert.False(m.Competitive));
        Assert.DoesNotContain(competitive.Concat(casual), m => m.Id == "unknown-room");
        Assert.Contains(new MatchBrowseQuery().Apply(all, Now), m => m.Id == "unknown-room");
    }

    [Fact]
    public void ThePeriodAndTheWinnerNarrowAsTheServerDoes()
    {
        var all = ReplayDemoData.Matches(Now);

        var day = new MatchBrowseQuery { Days = 1 }.Apply(all, Now).ToList();
        Assert.NotEmpty(day);
        Assert.All(day, m => Assert.True(Now - RoomAgeFormat.ParseCreatedUtc(m.ReportedAt)!.Value <= TimeSpan.FromDays(1)));
        Assert.True(day.Count < all.Count);

        var won = new MatchBrowseQuery { DecidedOnly = true }.Apply(all, Now).ToList();
        Assert.NotEmpty(won);
        Assert.All(won, m => Assert.Contains(m.Participants, p => p.Result >= 0.999));
        Assert.True(won.Count < all.Count, "the samples carry undecided matches, or this proves nothing");
    }

    /// <summary>Oldest first puts the matches older than a year at the top; newest first, at the bottom.</summary>
    [Fact]
    public void OldestFirstReversesTheList()
    {
        var all = ReplayDemoData.Matches(Now);
        var oldest = new MatchBrowseQuery { Sort = MatchBrowseSort.Oldest }.Apply(all, Now).ToList();
        var newest = new MatchBrowseQuery().Apply(all, Now).ToList();
        var dates = oldest.Select(m => RoomAgeFormat.ParseCreatedUtc(m.ReportedAt)!.Value).ToList();
        Assert.Equal(dates.OrderBy(d => d), dates);
        Assert.Equal(newest.Select(m => m.Id).Reverse(), oldest.Select(m => m.Id));
    }
}
