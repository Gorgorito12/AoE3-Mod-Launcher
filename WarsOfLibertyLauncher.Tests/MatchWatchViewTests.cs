using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The read-only "See match" window as a pure function of <c>GET /lobbies/:id/live</c>. The
/// refusals are the point: a number is never shown for a player being placed or one who keeps it
/// private, even if the server sent one; no stored result never names a winner; and a window that
/// is minimised, hidden, finished or gone asks the server for nothing.
/// </summary>
[Collection("wpf-and-language")]
public class MatchWatchViewTests
{
    private static readonly DateTime Now = new(2026, 11, 18, 20, 0, 0, DateTimeKind.Utc);

    private static LobbyLiveResponse Live(string phase, params LobbyLiveMember[] members) => new()
    {
        Id = "r1", Title = "WoL 1v1", ModId = "wol", Competitive = true, MaxPlayers = 2, SpectatorSlots = 0,
        Status = phase == "playing" ? "in_game" : "open", Phase = phase, StartedAt = "2026-11-18 19:48:00",
        Members = members.ToList(),
    };

    private static T InEnglish<T>(Func<T> f)
    {
        var previous = Strings.Language;
        Strings.SetLanguage(Strings.LangEn);
        try { return f(); }
        finally { Strings.SetLanguage(previous); }
    }

    [Fact]
    public void APlayingRoomSaysForHowLongAndShowsThePlacedNumberOnly()
    {
        var model = InEnglish(() => MatchWatchView.Build(Live("playing",
            new LobbyLiveMember { Id = "a", DisplayName = "Ana", Rating = 1612, GamesPlayed = 40 },
            // Being placed: even a number sent by mistake is not shown.
            new LobbyLiveMember { Id = "b", DisplayName = "Bo", Rating = 1490, GamesPlayed = 3 }), false, "Wars of Liberty", Now));
        Assert.Equal(MatchWatchState.Playing, model.State);
        Assert.Contains("12", model.StateText);
        var people = model.Sides.SelectMany(s => s.People).ToList();
        Assert.Equal("1612 ELO", people[0].Sub);
        Assert.DoesNotContain("1490", people[1].Sub);
        Assert.Equal(2, model.InRoom);
    }

    [Fact]
    public void APlacedPlayerWithNoNumberReadsHidden()
    {
        var model = InEnglish(() => MatchWatchView.Build(Live("waiting",
            new LobbyLiveMember { Id = "a", DisplayName = "Ana", GamesPlayed = 40 }), false, "WoL", Now));
        Assert.Equal(MatchWatchState.Waiting, model.State);
        Assert.Equal(Strings.GetIn(Strings.LangEn, "MpEloHiddenPrivate"), model.Sides[0].People[0].Sub);
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_NoStoredWinnerNamesNoWinner()
    {
        var live = Live("ended");
        live.Result = new LobbyLiveResult
        {
            EndedAt = "2026-11-18 19:58:00",
            Players = new()
            {
                new LobbyLiveResultPlayer { Id = "a", DisplayName = "Ana", Team = 1, Result = 0.5 },
                new LobbyLiveResultPlayer { Id = "b", DisplayName = "Bo", Team = 2, Result = 0.5 },
            },
        };
        var model = InEnglish(() => MatchWatchView.Build(live, false, "WoL", Now));
        Assert.Equal(MatchWatchState.Result, model.State);
        Assert.Equal(Strings.GetIn(Strings.LangEn, "MpWatchNoWinner"), model.Result!.Headline);

        live.Result.Players[0].Result = 1;
        live.Result.Players[1].Result = 0;
        var decided = InEnglish(() => MatchWatchView.Build(live, false, "WoL", Now));
        Assert.Contains("Ana", decided.Result!.Headline);
    }

    [Fact]
    public void AGoneRoomSaysSoAndNeverAsksAgain()
    {
        var model = InEnglish(() => MatchWatchView.Build(null, gone: true, "WoL", Now, fallbackTitle: "Room"));
        Assert.Equal(MatchWatchState.Gone, model.State);
        Assert.False(MatchWatchView.ShouldPoll(MatchWatchState.Gone));
        Assert.False(MatchWatchView.ShouldPoll(MatchWatchState.Result));
        Assert.True(MatchWatchView.ShouldPoll(MatchWatchState.Playing));
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_AMinimizedWatchWindowAsksTheServerForNothing()
    {
        Assert.False(MatchWatchWindow.MayAsk(MatchWatchState.Playing, visible: true, WindowState.Minimized));
        Assert.False(MatchWatchWindow.MayAsk(MatchWatchState.Playing, visible: false, WindowState.Normal));
        Assert.False(MatchWatchWindow.MayAsk(MatchWatchState.Result, visible: true, WindowState.Normal));
        Assert.True(MatchWatchWindow.MayAsk(MatchWatchState.Waiting, visible: true, WindowState.Normal));
    }

    [Fact]
    public void WithoutChatTheWindowSaysOnlyThePlayersSeeIt()
    {
        var model = InEnglish(() => MatchWatchView.Build(Live("waiting"), false, "WoL", Now));
        Assert.Null(model.Chat);
        var withChat = InEnglish(() => MatchWatchView.Build(Live("waiting"), false, "WoL", Now,
            new List<LobbyLiveChatLine> { new() { Login = "ana", Body = "gl", At = 0 } }));
        Assert.Single(withChat.Chat!);
    }

    [Fact]
    public void TheLiveWindowBuildsInBothLanguages()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();
            foreach (var lang in new[] { Strings.LangEn, Strings.LangEs })
            {
                var previous = Strings.Language;
                Strings.SetLanguage(lang);
                try
                {
                    var model = MatchWatchView.Build(Live("playing",
                        new LobbyLiveMember { Id = "a", DisplayName = "Ana", Rating = 1600, GamesPlayed = 40 }), false, "WoL", Now);
                    var w = new MatchWatchWindow(model, () => System.Threading.Tasks.Task.FromResult(model));
                    Assert.Equal(Visibility.Collapsed, w.DecideBtn.Visibility);
                    Assert.Equal("1 / 2", w.RosterCount.Text);
                    w.Close();
                }
                finally { Strings.SetLanguage(previous); }
            }
        });
        Assert.Null(error);
    }

    [Theory]
    [InlineData(true, MatchWatchState.Waiting, true)]
    [InlineData(true, MatchWatchState.Playing, true)]
    [InlineData(true, MatchWatchState.Loading, true)]
    [InlineData(true, MatchWatchState.Result, false)]
    [InlineData(true, MatchWatchState.Gone, false)]
    [InlineData(false, MatchWatchState.Playing, false)]
    public void DecideIsOfferedOnlyToWhoMayDecideAndOnlyUntilItEnds(bool mayDecide, MatchWatchState state, bool offered)
        => Assert.Equal(offered, WarsOfLibertyLauncher.MatchWatchWindow.DecideOffered(mayDecide, state));
}
