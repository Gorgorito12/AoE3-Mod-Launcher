using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

public class VersusViewTests
{
    private static MatchHistoryRow Match(string id, params (string user, int team, double result)[] players) => new()
    {
        Id = id,
        Participants = players.Select(p => new MatchHistoryParticipant { UserId = p.user, Team = p.team, Result = p.result }).ToList(),
    };

    [Fact]
    public void CountsTheViewersWinsAndLossesAgainstHim()
    {
        var page = new List<MatchHistoryRow>
        {
            Match("1", ("me", 1, 1), ("him", 2, 0)),
            Match("2", ("me", 1, 0), ("him", 2, 1)),
            Match("3", ("me", 1, 1), ("him", 2, 0)),
            Match("4", ("x", 1, 1), ("him", 2, 0)),
        };
        var v = VersusView.For(page, "me", "him")!;
        Assert.Equal(2, v.Wins);
        Assert.Equal(1, v.Losses);
        Assert.Equal(3, v.Played);
        Assert.Equal(new[] { "1", "2", "3" }, v.Recent.Select(m => m.Match.Id));
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_AFullPageNeverClaimsNeverPlayed()
    {
        var page = Enumerable.Range(0, VersusView.HistoryPageSize)
            .Select(i => Match(i.ToString(), ("x", 1, 1), ("him", 2, 0))).ToList();
        var v = VersusView.For(page, "me", "him")!;
        Assert.Equal(0, v.Played);
        Assert.False(v.NeverPlayed);

        Assert.True(VersusView.For(page.Take(5).ToList(), "me", "him")!.NeverPlayed);
    }

    [Fact]
    public void PlayingOnTheSameTeamIsAMeetingButNeitherAWinNorALoss()
    {
        var page = new List<MatchHistoryRow> { Match("1", ("me", 1, 1), ("him", 1, 1), ("a", 2, 0), ("b", 2, 0)) };
        var v = VersusView.For(page, "me", "him")!;
        Assert.Equal(1, v.Played);
        Assert.Equal(0, v.Wins + v.Losses);
    }

    [Fact]
    public void NothingToSayOnTheViewersOwnPageOrWithoutAViewer()
    {
        var page = new List<MatchHistoryRow> { Match("1", ("me", 1, 1), ("him", 2, 0)) };
        Assert.Null(VersusView.For(page, "me", "me"));
        Assert.Null(VersusView.For(page, null, "him"));
        Assert.Null(VersusView.For(null, "me", "him"));
    }
}

public class ProfileCacheTests
{
    private static readonly DateTime T0 = new(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ANewPlayerIsAskedForAndThenNotForAMinute()
    {
        var e = new ProfileCache().Get("a", T0);
        Assert.True(ProfileCache.NeedsFetch(e, T0));
        e.Standing = new EloSnapshot();
        e.AskedUtc = T0;
        Assert.False(ProfileCache.NeedsFetch(e, T0.AddSeconds(59)));
        Assert.True(ProfileCache.NeedsFetch(e, T0.AddSeconds(60)));
    }

    [Fact]
    public void AFailureIsRetriedSoonerButNotEveryRepaint()
    {
        var e = new ProfileCache().Get("a", T0);
        e.StandingFailed = true;
        e.AskedUtc = T0;
        Assert.False(ProfileCache.NeedsFetch(e, T0.AddSeconds(5)));
        Assert.True(ProfileCache.NeedsFetch(e, T0.AddSeconds(10)));
        e.Loading = true;
        Assert.False(ProfileCache.NeedsFetch(e, T0.AddMinutes(5)));
    }

    [Fact]
    public void TheLeastRecentlyOpenedGoesFirst()
    {
        var cache = new ProfileCache();
        for (int i = 0; i < ProfileCache.Capacity; i++) cache.Get("p" + i, T0.AddSeconds(i));
        cache.Get("p0", T0.AddMinutes(1));
        cache.Get("new", T0.AddMinutes(2));
        Assert.Equal(ProfileCache.Capacity, cache.Count);
        Assert.True(cache.Contains("p0"));
        Assert.False(cache.Contains("p1"));
    }

    [Fact]
    public void AMatchNamingThePlayerMakesHimStale()
    {
        var cache = new ProfileCache();
        var e = cache.Get("a", T0);
        e.Standing = new EloSnapshot();
        e.AskedUtc = T0;
        cache.MarkStale(new[] { "a" });
        Assert.True(ProfileCache.NeedsFetch(e, T0.AddSeconds(1)));
    }
}
