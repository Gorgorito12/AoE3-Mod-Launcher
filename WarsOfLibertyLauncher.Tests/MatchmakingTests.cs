using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The quick match against the server's queue (Controls/MultiplayerTab.Matchmaking.cs): who a
/// search meets, who earns a card, how a searcher becomes a row, and the "rival found" window.
/// The refusals are the point — a card or a Play button for somebody this player can never play
/// with is worse than none.
/// </summary>
[Collection("wpf-and-language")]
public class MatchmakingTests
{
    private static QueueSearcher S(string user, string mod = "wol", string hash = "H", string kind = "ranked",
        bool busy = false, string ticket = "")
        => new()
        {
            TicketId = string.IsNullOrEmpty(ticket) ? "t-" + user : ticket,
            UserId = user,
            DisplayName = user.ToUpperInvariant(),
            ModId = mod,
            ModCombinedHash = hash,
            Kind = kind,
            Busy = busy,
            QueuedAt = "2026-11-12T10:00:00Z",
        };

    [Theory]
    [InlineData(QuickMatchKind.Ranked, QuickMatchKind.Ranked, true)]
    [InlineData(QuickMatchKind.Casual, QuickMatchKind.Casual, true)]
    [InlineData(QuickMatchKind.Either, QuickMatchKind.Casual, true)]
    [InlineData(QuickMatchKind.Ranked, QuickMatchKind.Either, true)]
    [InlineData(QuickMatchKind.Ranked, QuickMatchKind.Casual, false)]
    public void RatedAndCasualNeverMeetAndEitherMeetsBoth(QuickMatchKind a, QuickMatchKind b, bool meet)
        => Assert.Equal(meet, QueueSearchers.KindsMeet(a, b));

    [Fact]
    public void THE_ONE_THAT_MATTERS_ASearchMeetsOnlyTheSameModVersionAndKind()
    {
        var all = new[]
        {
            S("me"),                        // myself
            S("busy", busy: true),          // being matched right now
            S("other-mod", mod: "im"),
            S("other-version", hash: "X"),
            S("casual", kind: "casual"),
            S("good"),
            S("either", kind: "either"),
        };
        var met = QueueSearchers.Compatible(all, "me", "WOL", "h", QuickMatchKind.Ranked).Select(s => s.UserId).ToList();
        Assert.Equal(new[] { "good", "either" }.OrderBy(x => x), met.OrderBy(x => x));
        // My version unknown: nobody, never a guess.
        Assert.Empty(QueueSearchers.Compatible(all, "me", "wol", null, QuickMatchKind.Ranked));
    }

    [Fact]
    public void ACardIsEarnedOnlyByAFreeStrangerOfMyVersionOnceAndNotTooOften()
    {
        var now = new DateTime(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc);
        var seen = new HashSet<string>();
        var last = new Dictionary<string, DateTime>();
        string? Mine(string mod) => mod == "wol" ? "H" : null;
        bool Worth(QueueSearcher s, bool opted = true, bool searching = false)
            => QueueSearchers.WorthNotifying(s, opted, searching, "me", Mine, seen, last, now);

        Assert.True(Worth(S("ana")));
        Assert.False(Worth(S("ana"), opted: false), "only for who asked");
        Assert.False(Worth(S("ana"), searching: true), "my own search meets him anyway");
        Assert.False(Worth(S("me")));
        Assert.False(Worth(S("ana", busy: true)));
        Assert.False(Worth(S("ana", mod: "im")), "a mod I do not have");
        Assert.False(Worth(S("ana", hash: "X")), "another version");

        seen.Add("t-ana");
        Assert.False(Worth(S("ana")), "the same ticket twice");
        last["ana"] = now.AddMinutes(-3);
        Assert.False(Worth(S("ana", ticket: "t2")), "the same person again soon");
        last["ana"] = now.AddMinutes(-11);
        Assert.True(Worth(S("ana", ticket: "t3")));
    }

    [Fact]
    public void ASearcherIsARoomOfOneWaitingPlayerAndNeverItsOwnRating()
    {
        var s = S("ana", kind: "casual");
        s.InPlacement = true;
        s.GamesPlayed = 3;
        var row = new QueueSearcherRow(s);
        Assert.Equal(2, row.MaxPlayers);
        Assert.Equal(1, row.CurrentPlayers);
        Assert.False(row.Competitive);
        Assert.Equal("ana", row.Host.Id);
        Assert.Null(row.Host.Rating);
        Assert.True(row.Host.InPlacement);
        Assert.StartsWith("q:", row.Id);
        Assert.True(new QueueSearcherRow(S("bo", kind: "either")).Competitive);
    }

    [Fact]
    public void FindAnotherFollowsACasualOneVOneToo()
    {
        var casual = new MatchOutcomeView(MatchVerdict.Win, "wol", "Texas", 1440, 2, RatingBefore: 1524, RatingAfter: 1542, RivalLogin: "someone", RivalRating: 1592, Wins: 4, Losses: 1, Rd: 60);
        Assert.False(MultiplayerTab.OffersFindAnother(casual));
        Assert.True(MultiplayerTab.OffersFindAnother(casual, casualOneVOne: true));
    }

    [Fact]
    public void EveryMatchmakingTextFormatsInBothLanguages()
    {
        var previous = Strings.Language;
        try
        {
            foreach (var lang in new[] { "es", "en" })
            {
                Strings.SetLanguage(lang);
                foreach (var (key, args) in new (string, object[])[]
                {
                    ("MmChallengeSentHeadline", new object[] { "Ana" }),
                    ("MmFoundKindRanked", new object[] { "WoL" }),
                    ("MmFoundYouHost", new object[] { "Ana" }),
                    ("MmFoundTheyHost", new object[] { "Ana" }),
                    ("MmFoundPlacing", new object[] { 3, 10 }),
                    ("MmWaitingFor", new object[] { "Ana" }),
                    ("MmAcceptedOf", new object[] { 1, 2 }),
                    ("MmSecondsLeft", new object[] { 12 }),
                    ("MmPenaltyUntil", new object[] { "10:42" }),
                    ("MmRivalDidNotAccept", new object[] { "Ana" }),
                    ("MmRivalDidNotAcceptRequeued", new object[] { "Ana" }),
                    ("MmRivalNeverEntered", new object[] { "Ana" }),
                    ("MmChipWaiting", new object[] { 4 }),
                    ("MmPlayTip", new object[] { "Ana" }),
                    ("MmSomeoneSearchingTitle", new object[] { "Ana" }),
                    ("MmSomeoneSearchingRanked", new object[] { "WoL" }),
                    ("QcQueuedLineAlone", new object[] { "WoL", "rated" }),
                    ("QcQueuedLineOne", new object[] { 1, "WoL", "rated" }),
                    ("QcQueuedLineMany", new object[] { 3, "WoL", "rated" }),
                    ("QcWaitSearcher", new object[] { "Ana" }),
                    ("QcWaitSearcherMany", new object[] { "Ana", 2 }),
                })
                {
                    var text = Strings.Format(key, args);
                    Assert.DoesNotContain("{", text);
                    Assert.NotEqual(key, text);
                }
            }
        }
        finally { Strings.SetLanguage(previous); }
    }

    /// <summary>
    /// The window: it builds in both languages, Accept answers once and becomes the wait, and
    /// Escape is a decline — exactly once, and never after the server already decided.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheFoundWindowAnswersOnceAndEscapeDeclines()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            try
            {
                foreach (var lang in new[] { "es", "en" })
                {
                    Strings.SetLanguage(lang);
                    var accepts = 0;
                    var declines = 0;
                    var dlg = new MatchFoundDialog("Ana", new FrameworkElement(), "1v1", "host", 20000, iChallenged: false, mod: null);
                    dlg.Accepted += () => accepts++;
                    dlg.Declined += () => declines++;
                    Assert.Equal(Strings.Get("MmAccept"), dlg.PrimaryAction.Content);
                    dlg.PrimaryAction.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    dlg.PrimaryAction.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    Assert.Equal(1, accepts);
                    Assert.False(dlg.PrimaryAction.IsEnabled);
                    Assert.Equal(0, declines);

                    var esc = new MatchFoundDialog("Ana", new FrameworkElement(), "1v1", null, 20000, false, null);
                    esc.Declined += () => declines++;
                    esc.CancelAction.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    Assert.Equal(1, declines);

                    var decided = new MatchFoundDialog("Ana", new FrameworkElement(), "1v1", null, 20000, false, null);
                    decided.Declined += () => declines++;
                    decided.CloseQuietly();
                    Assert.Equal(1, declines);

                    // A challenger has said yes by challenging.
                    var challenger = new MatchFoundDialog("Ana", new FrameworkElement(), "1v1", null, 20000, iChallenged: true, mod: null);
                    Assert.False(challenger.PrimaryAction.IsEnabled);
                }
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }
}
