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

    /// <summary>A mod whose picture is a real file on disk, so nothing is downloaded.</summary>
    private static (WarsOfLibertyLauncher.Models.ModProfile Mod, string File) ModWithPicture()
    {
        var file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aoe3ml-found-" + Guid.NewGuid().ToString("N") + ".png");
        var pixels = new byte[64 * 36 * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 40; pixels[i + 1] = 90; pixels[i + 2] = 160; pixels[i + 3] = 255; }
        var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(64, 36, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, 64 * 4);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var stream = System.IO.File.Create(file)) encoder.Save(stream);
        return (new WarsOfLibertyLauncher.Models.ModProfile { Id = "found-test", DisplayName = "Found test", LocalHeroImagePath = file }, file);
    }

    private static MatchFoundDialog FoundFor(WarsOfLibertyLauncher.Models.ModProfile? mod)
        => new(new MatchDuelist("Yo", new FrameworkElement { Width = 44, Height = 44 }, "1383 ELO"),
            new MatchDuelist("Ana", new FrameworkElement { Width = 44, Height = 44 }, "1642 ELO"),
            "1v1 clasificatoria · WoL", true, "Ana es el anfitrión: crea la partida.", false, 20000, false, mod);

    private static double HeightOf(Window window)
    {
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(window.Width, double.PositiveInfinity));
        return content.DesiredSize.Height;
    }

    /// <summary>
    /// Design 117: the picture of the mod goes up behind the bar with its veil and the glow under
    /// it. The two glows the chassis paints stay its first two children (it finds them by
    /// position) and are left empty, and no layer acts as a floor under a window that sizes to
    /// its content.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_ThePictureGoesUpWithoutMovingAnything()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();
            var previous = ModDialogChrome.Config;
            var (mod, file) = ModWithPicture();
            try
            {
                ModDialogChrome.Config = new WarsOfLibertyLauncher.Models.LauncherConfig();
                var plain = FoundFor(null);
                Assert.False(plain.HasModPicture);

                var dlg = FoundFor(mod);
                Assert.True(dlg.HasModPicture);
                var root = dlg.RootForTests;
                Assert.IsAssignableFrom<System.Windows.Shapes.Shape>(root.Children[0]);
                Assert.IsAssignableFrom<System.Windows.Shapes.Shape>(root.Children[1]);
                Assert.Same(System.Windows.Media.Brushes.Transparent, ((System.Windows.Shapes.Shape)root.Children[0]).Fill);
                Assert.Same(System.Windows.Media.Brushes.Transparent, ((System.Windows.Shapes.Shape)root.Children[1]).Fill);

                var strip = Assert.Single(root.Children.OfType<Border>(), b => b.Name == ModDialogChrome.FoundPictureName);
                Assert.Equal(ModDialogChrome.FoundStripHeight, strip.Height);
                Assert.Equal(VerticalAlignment.Top, strip.VerticalAlignment);
                // The picture is under the bar and the page, never over them.
                Assert.True(root.Children.IndexOf(strip) < root.Children.IndexOf(dlg.Chrome));
                // Every layer lets the click through to what is above the picture.
                Assert.All(root.Children.OfType<FrameworkElement>().Where(c => !ReferenceEquals(c, dlg.Chrome) && c is not StackPanel),
                    layer => Assert.False(layer.IsHitTestVisible));

                Assert.Equal(HeightOf(plain), HeightOf(dlg), 3);
            }
            finally
            {
                ModDialogChrome.Config = previous;
                try { System.IO.File.Delete(file); } catch (System.IO.IOException) { }
            }
        });
        Assert.Null(error);
    }

    /// <summary>No picture to show: nothing is added and the chassis keeps its own glows.</summary>
    [Fact]
    public void WithNoPictureTheWindowStaysAsItWas()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();
            var previous = ModDialogChrome.Config;
            var (mod, file) = ModWithPicture();
            try
            {
                // The setting is off.
                ModDialogChrome.Config = new WarsOfLibertyLauncher.Models.LauncherConfig { ShowModImage = false };
                var off = FoundFor(mod);
                Assert.False(off.HasModPicture);
                Assert.DoesNotContain(off.RootForTests.Children.OfType<Border>(), b => b.Name == ModDialogChrome.FoundPictureName);
                Assert.NotSame(System.Windows.Media.Brushes.Transparent, ((System.Windows.Shapes.Shape)off.RootForTests.Children[0]).Fill);

                // The mod has none.
                ModDialogChrome.Config = new WarsOfLibertyLauncher.Models.LauncherConfig();
                var none = FoundFor(new WarsOfLibertyLauncher.Models.ModProfile { Id = "found-none", DisplayName = "None" });
                Assert.False(none.HasModPicture);

                // No settings at all, which is what most tests run with.
                ModDialogChrome.Config = null;
                Assert.False(FoundFor(mod).HasModPicture);
            }
            finally
            {
                ModDialogChrome.Config = previous;
                try { System.IO.File.Delete(file); } catch (System.IO.IOException) { }
            }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// The tiles and the stake card are glass that follows the transparency level: a resource
    /// reference, never a fill of their own. At the solid level there is no blurred copy to show
    /// under them.
    /// </summary>
    [Fact]
    public void TheTilesAndTheStakeCardAreGlass()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();
            var previous = ModDialogChrome.Config;
            var (mod, file) = ModWithPicture();
            try
            {
                ModDialogChrome.Config = new WarsOfLibertyLauncher.Models.LauncherConfig();
                var dlg = FoundFor(mod);
                var panel = Application.Current.FindResource(WarsOfLibertyLauncher.Services.GlassTheme.PanelKey);
                Assert.Same(panel, dlg.MyTile.Background);
                Assert.Same(panel, dlg.RivalTile.Background);
                Assert.Equal("GlassMenu", MultiplayerTab.StakeCardDress.Found.FillKey);
                Assert.Contains(dlg.RootForTests.Children.OfType<Border>(), b => b.Name == "CardFrost");

                ModDialogChrome.Config = new WarsOfLibertyLauncher.Models.LauncherConfig { GlassLevel = WarsOfLibertyLauncher.Services.GlassTheme.Solid };
                var solid = FoundFor(mod);
                Assert.True(solid.HasModPicture);
                Assert.DoesNotContain(solid.RootForTests.Children.OfType<Border>(), b => b.Name == "CardFrost");
            }
            finally
            {
                ModDialogChrome.Config = previous;
                try { System.IO.File.Delete(file); } catch (System.IO.IOException) { }
            }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// Design 117f-h: no tile is coloured until its player accepts, each turns green with its own
    /// acceptance and with nobody else's, "YOU" is on my tile only, and the button says the room
    /// is opening only once both said yes.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_ATileIsColouredByItsOwnAcceptance()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();
            static bool Green(Border tile)
                => Tree(tile).OfType<Border>().Single(b => b.Tag as string == MatchFoundDialog.AcceptedWashTag).Visibility == Visibility.Visible;
            static int YouTags(Border tile)
                => Tree(tile).OfType<TextBlock>().Count(t => t.Tag as string == MatchFoundDialog.YouTag);

            var dlg = Found();
            Assert.False(Green(dlg.MyTile));
            Assert.False(Green(dlg.RivalTile));
            Assert.Equal(1, YouTags(dlg.MyTile));
            Assert.Equal(0, YouTags(dlg.RivalTile));
            Assert.Equal(Strings.Get("MmAccept"), dlg.PrimaryText);

            // The rival said yes: his tile, not mine.
            dlg.SetProgress(1, 2);
            Assert.True(Green(dlg.RivalTile));
            Assert.False(Green(dlg.MyTile));
            Assert.Equal(Strings.Get("MmAccept"), dlg.PrimaryText);

            // Now me: both green, and the room is being opened.
            dlg.PrimaryAction.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.True(Green(dlg.MyTile));
            Assert.Equal(Strings.Get("MmOpeningRoom"), dlg.PrimaryText);

            // Me alone: mine green, his not, and the button waits for him by name.
            var alone = Found();
            alone.PrimaryAction.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.True(Green(alone.MyTile));
            Assert.False(Green(alone.RivalTile));
            Assert.Equal(Strings.Format("MmWaitingFor", "Ana"), alone.PrimaryText);
        });
        Assert.Null(error);
    }

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
