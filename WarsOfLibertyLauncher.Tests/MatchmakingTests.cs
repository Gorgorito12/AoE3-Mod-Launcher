using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
                    var dlg = Found();
                    dlg.Accepted += () => accepts++;
                    dlg.Declined += () => declines++;
                    Assert.Equal(Strings.Get("MmAccept"), dlg.PrimaryAction.Content);
                    dlg.PrimaryAction.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    dlg.PrimaryAction.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    Assert.Equal(1, accepts);
                    Assert.False(dlg.PrimaryAction.IsEnabled);
                    Assert.Equal(0, declines);

                    var esc = Found();
                    esc.Declined += () => declines++;
                    esc.CancelAction.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    Assert.Equal(1, declines);

                    var decided = Found();
                    decided.Declined += () => declines++;
                    decided.CloseQuietly();
                    Assert.Equal(1, declines);

                    // A challenger has said yes by challenging.
                    var challenger = Found(challenged: true);
                    Assert.False(challenger.PrimaryAction.IsEnabled);
                }
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    private static MatchFoundDialog Found(bool? iHost = true, bool challenged = false, bool rated = true,
        string me = "Yo", string rival = "Ana", int acceptMs = 20000)
        => new(new MatchDuelist(me, new FrameworkElement { Width = 44, Height = 44 }, "1383 ELO"),
            new MatchDuelist(rival, new FrameworkElement { Width = 44, Height = 44 }, "1642 ELO"),
            "1v1 clasificatoria · WoL", rated, "Ana es el anfitrión: crea la partida.", iHost, acceptMs, challenged, null);

    private static IEnumerable<DependencyObject> Tree(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var d in Tree(child)) yield return d;
    }

    /// <summary>
    /// Design 117: who accepted is said on each tile, worked out from the server's count; my own
    /// click marks mine at once, turns the clock's label and never marks the rival.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_EachTileSaysWhoAccepted()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();
            var dlg = Found();
            Assert.False(dlg.MeMarkedAccepted);
            Assert.False(dlg.RivalMarkedAccepted);
            Assert.Equal(Strings.Get("MmFoundAcceptIn"), dlg.ClockLabelText);

            // The rival said yes first: the count is 1 and I have not pressed anything.
            dlg.SetProgress(1, 2);
            Assert.True(dlg.RivalMarkedAccepted);
            Assert.False(dlg.MeMarkedAccepted);

            // I press Accept before the server's next frame: mine is marked, his stays.
            dlg.PrimaryAction.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.True(dlg.MeMarkedAccepted);
            Assert.True(dlg.RivalMarkedAccepted);
            Assert.Equal(Strings.Get("MmFoundTimeLeft"), dlg.ClockLabelText);

            // Alone: my own yes counted by the server is not the rival's.
            var alone = Found();
            alone.PrimaryAction.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            alone.SetProgress(1, 2);
            Assert.True(alone.MeMarkedAccepted);
            Assert.False(alone.RivalMarkedAccepted);
        });
        Assert.Null(error);
    }

    [Fact]
    public void TheClockTurnsAmberAtFiveSeconds()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();
            var dlg = Found();
            var start = DateTime.UtcNow;
            dlg.PaintAt(start.AddSeconds(6));
            Assert.Same(Application.Current.FindResource("UiHeadText"), dlg.ClockSeconds.Foreground);
            Assert.Same(Application.Current.FindResource("MmFoundClockBar"), dlg.ClockFill.Background);
            Assert.True(int.Parse(dlg.ClockSecondsText) > MatchFoundDialog.LowSeconds);

            dlg.PaintAt(start.AddSeconds(15.5));
            Assert.Equal(MatchFoundDialog.LowSeconds, int.Parse(dlg.ClockSecondsText));
            Assert.Same(Application.Current.FindResource("UiErrorText"), dlg.ClockSeconds.Foreground);
            Assert.Same(Application.Current.FindResource("MmFoundClockLow"), dlg.ClockFill.Background);

            dlg.PaintAt(start.AddSeconds(60));
            Assert.Equal("0", dlg.ClockSecondsText);
        });
        Assert.Null(error);
    }

    /// <summary>The HOST tag is on the tile of whoever hosts, and the whole sentence is its tooltip.</summary>
    [Fact]
    public void TheHostTagSitsOnWhoeverHostsAndCarriesTheWholeSentence()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();
            static Border? TagIn(DependencyObject tile)
                => Tree(tile).OfType<Border>().FirstOrDefault(b => b.Tag as string == MatchFoundDialog.HostTag);

            var mine = Found(iHost: true);
            Assert.NotNull(TagIn(mine.MyTile));
            Assert.Null(TagIn(mine.RivalTile));
            Assert.NotNull(TagIn(mine.MyTile)!.ToolTip);

            var his = Found(iHost: false);
            Assert.Null(TagIn(his.MyTile));
            Assert.NotNull(TagIn(his.RivalTile));

            // Not known: no tag on anybody, rather than a guess.
            var unknown = Found(iHost: null);
            Assert.Null(TagIn(unknown.MyTile));
            Assert.Null(TagIn(unknown.RivalTile));
        });
        Assert.Null(error);
    }

    /// <summary>Nothing about points until the server's stake arrives, and the card takes the body's width.</summary>
    [Fact]
    public void TheStakeIsTheRoomsCardInThisWindowsDress()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();
            var dlg = Found();
            Assert.False(dlg.StakeShown);

            var card = MultiplayerTab.BuildStakeCard(new StakeView(StakeState.Shown, 22, -5, 19, null, false),
                oneVOne: true, bodyWhenNothing: false, MultiplayerTab.StakeCardDress.Found)!;
            // No fixed width: it stretches to the dialog's body (the room's is 300).
            Assert.True(double.IsNaN(((FrameworkElement)card).Width));
            dlg.ShowStake(card);
            Assert.True(dlg.StakeShown);
            var texts = Tree(card).OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains("+22", texts);
            Assert.Contains(Strings.Format("MpPercentValue", 81), texts);

            dlg.ShowStake(null);
            Assert.False(dlg.StakeShown);
        });
        Assert.Null(error);
    }

    /// <summary>
    /// The two tiles at the dialog's width, in Spanish, with names far too long: each name trims
    /// inside its own tile and the tiles never reach each other or the "VS" between them.
    /// </summary>
    [Fact]
    public void BothTilesFitWithLongNames()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();
            var previous = Strings.Language;
            Strings.SetLanguage(Strings.LangEs);
            try
            {
                var dlg = Found(me: "un_nombre_de_discord_muy_largo_1234", rival: "otro_nombre_todavia_mas_largo_5678");
                var root = (FrameworkElement)dlg.Content;
                root.Measure(new Size(dlg.Width, double.PositiveInfinity));
                root.Arrange(new Rect(0, 0, dlg.Width, root.DesiredSize.Height));
                var myRight = dlg.MyTile.TranslatePoint(new Point(dlg.MyTile.ActualWidth, 0), root).X;
                var hisLeft = dlg.RivalTile.TranslatePoint(new Point(0, 0), root).X;
                var hisRight = hisLeft + dlg.RivalTile.ActualWidth;
                Assert.True(hisLeft - myRight >= 18, $"mine ends {myRight:0.#}, his starts {hisLeft:0.#}");
                Assert.True(hisRight <= dlg.Width - 21.5, $"his tile ends {hisRight:0.#} of {dlg.Width}");
                Assert.Equal(dlg.MyTile.ActualWidth, dlg.RivalTile.ActualWidth, 1);
                foreach (var tile in new[] { dlg.MyTile, dlg.RivalTile })
                {
                    var name = Tree(tile).OfType<TextBlock>().First(t => t.Text.Contains("nombre"));
                    Assert.Equal(TextTrimming.CharacterEllipsis, name.TextTrimming);
                    var nameRight = name.TranslatePoint(new Point(name.ActualWidth, 0), tile).X;
                    Assert.True(nameRight <= tile.ActualWidth - 12, $"the name runs to {nameRight:0.#} of {tile.ActualWidth:0.#}");
                }
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }
}
