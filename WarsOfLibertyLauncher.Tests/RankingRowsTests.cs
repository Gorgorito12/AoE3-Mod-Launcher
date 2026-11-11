using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The Clasificación rows (design 93): the player's picture beside every name, the PEAK (with the
/// day it was reached) where the server sends one and a dash where it does not, and the column
/// headings drawn on exactly the same columns as the rows - which live inside a ScrollViewer, as
/// plates with a rim and a padding the headings do not have.
/// </summary>
[Collection("wpf-and-language")]
public class RankingRowsTests
{
    /// <summary>
    /// THE ONE THAT MATTERS: every heading sits at the same X, and is as wide, as its column in a
    /// row of the list. The headings are outside the ScrollViewer and a row is a plate 8 px in
    /// with a rim and 12 px of padding, so nothing but the arithmetic keeps them together - and
    /// with the list scrolling, the bar takes width from the rows only.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheHeadingsUseTheRowsColumns()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            tab.ShowDemoElo("ranking");

            var host = (FrameworkElement)(VisualTreeHelper.GetParent(tab.RankingHeaderHost)
                                          ?? tab.RankingHeaderHost.Parent);
            // Short on purpose: the list has to scroll, which is when the two could part.
            void Layout()
            {
                host.Measure(new Size(1100, 300));
                host.Arrange(new Rect(0, 0, 1100, 300));
                host.UpdateLayout();
            }
            Layout();
            // The gutter is synced after the first layout (it is deferred in the launcher).
            tab.RankingRowsScroll.ScrollToEnd();
            Layout();
            tab.GetType().GetMethod("SyncRankingScrollGutter",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(tab, null);
            Layout();
            Assert.True(tab.RankingRowsScroll.ScrollableHeight > 0, "the list should be scrolling");

            var heading = (Grid)tab.RankingHeaderHost.Children[0];
            var rows = tab.RankingBody.Children.OfType<Border>().Where(b => b.Tag is LeaderboardRow).ToList();
            Assert.NotEmpty(rows);
            foreach (var plate in rows)
            {
                var row = (Grid)plate.Child;
                Assert.Equal(heading.ColumnDefinitions.Count, row.ColumnDefinitions.Count);
                double headOffset = 0, rowOffset = 0;
                for (var c = 0; c < row.ColumnDefinitions.Count; c++)
                {
                    var headX = heading.TranslatePoint(new Point(headOffset, 0), host).X;
                    var rowX = row.TranslatePoint(new Point(rowOffset, 0), host).X;
                    Assert.Equal(rowX, headX, 0.5);
                    Assert.Equal(row.ColumnDefinitions[c].ActualWidth, heading.ColumnDefinitions[c].ActualWidth, 0.5);
                    headOffset += heading.ColumnDefinitions[c].ActualWidth;
                    rowOffset += row.ColumnDefinitions[c].ActualWidth;
                }
            }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// There is no pinned copy of the viewer's row any more: the first three are on the podium,
    /// the table starts at the fourth, and "Your position" beside it says where the viewer is at
    /// every scroll offset.
    /// </summary>
    [Fact]
    public void ThePodiumTakesTheFirstThreeAndYourPositionReplacesThePinnedRow()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            tab.ShowDemoElo("ranking");

            var podium = Walk(tab.RankingPodiumHost).OfType<FrameworkElement>()
                .Where(e => Equals(e.Tag, MultiplayerTab.PodiumCardTag)).ToList();
            Assert.Equal(3, podium.Count);
            Assert.Equal(Visibility.Visible, tab.RankingPodiumHost.Visibility);

            var places = tab.RankingBody.Children.OfType<Border>()
                .Select(b => b.Tag).OfType<LeaderboardRow>().Select(r => r.Rank).ToList();
            Assert.Equal(4, places.Min());
            Assert.DoesNotContain(1, places);

            Assert.Equal(Visibility.Visible, tab.RankingSideColumn.Visibility);
            Assert.Equal(Visibility.Visible, tab.RankingYouHost.Visibility);
            Assert.Single(Walk(tab.RankingYouHost).OfType<FrameworkElement>(),
                e => Equals(e.Tag, MultiplayerTab.YouCardTag));
            Assert.Null(tab.FindName("RankingPinnedRow"));
        });
        Assert.Null(error);
    }

    /// <summary>
    /// The PEAK cell is the figure with the day it was first reached on hover — and a dash, never an
    /// empty cell, for a row the server sent no peak for. The W-L and the % that stood here moved
    /// to the Profile (2026-10-28); a row must not print them any more.
    /// </summary>
    [Fact]
    public void ThePeakIsTheFigureWithItsDay_AndADashWhenTheServerSentNone()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            Strings.SetLanguage(Strings.LangEs);
            try
            {
                var tab = new MultiplayerTab();
                const string at = "2026-08-12T20:00:00.000Z";
                var row = new LeaderboardRow
                {
                    Rank = 4, UserId = "u4", DisplayName = "Nuevo", Rating = 1540, Rd = 120,
                    GamesPlayed = 10, Wins = 6, Losses = 4, RatedWins = 6, RatedLosses = 4,
                    RatingPeak = 1612.4, RatingPeakAt = at,
                };
                var built = (DependencyObject)tab.BuildLeaderboardRow(row, 1630, isMe: false, RankingTableLayout.All, 5, team: false);
                var cell = Walk(built).OfType<TextBlock>().Single(t => Equals(t.Tag, MultiplayerTab.RankingPeakTag));
                Assert.Equal(PlacementView.RatingText(1612.4), cell.Text);
                var tip = Assert.IsAssignableFrom<TextBlock>(cell.ToolTip);
                Assert.Equal("Máximo alcanzado el " + ShortDate.Format(ShortDate.Parse(at)!.Value), tip.Text);
                // The record is the Profile's now: no "6-4" and no "60" anywhere in the row.
                Assert.DoesNotContain(Walk(built).OfType<TextBlock>(), t => t.Text == "60" || t.Text == "6-4");

                // No peak from the server for this row: a dash, and nothing to explain on hover.
                row.RatingPeak = null;
                row.RatingPeakAt = null;
                built = (DependencyObject)tab.BuildLeaderboardRow(row, 1630, isMe: false, RankingTableLayout.All, 5, team: false);
                cell = Walk(built).OfType<TextBlock>().Single(t => Equals(t.Tag, MultiplayerTab.RankingPeakTag));
                Assert.Equal(Strings.Get("MpDash"), cell.Text);
                Assert.Null(cell.ToolTip);
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// A server that sends no peak gets the wide table without the column — and still the WIDE
    /// table: the ELO bar on a ranked row, "Posicionamiento 6/10" on a placement row, a dash with
    /// its reason nowhere because there is no peak cell at all.
    /// </summary>
    [Fact]
    public void WithoutPeaksTheRowsKeepTheirBarAndTheLongPlacementText()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            Strings.SetLanguage(Strings.LangEs);
            try
            {
                var tab = new MultiplayerTab();
                var specs = RankingTableLayout.WithoutPeak;
                var ranked = (DependencyObject)tab.BuildLeaderboardRow(new LeaderboardRow
                {
                    Rank = 1, UserId = "u1", DisplayName = "Ana", Rating = 1600, Rd = 80, GamesPlayed = 12,
                }, 1600, isMe: false, specs, 5, team: false);
                Assert.Contains(Walk(ranked).OfType<Border>(), b => Equals(b.Tag, "RankingBar"));
                Assert.DoesNotContain(Walk(ranked).OfType<TextBlock>(), t => Equals(t.Tag, MultiplayerTab.RankingPeakTag));

                var placing = (DependencyObject)tab.BuildPlacementRow(new PlacementRow
                {
                    UserId = "u2", DisplayName = "Beto", Rating = 1500, PlacementPlayed = 6, PlacementRequired = 10,
                }, isMe: false, specs, ownResults: null);
                var plain = Walk(placing).OfType<TextBlock>().Select(WarsOfLibertyLauncher.RevealText.PlainTextOf);
                Assert.Contains(plain, t => t.Contains(Strings.Format("MpPlacementProgress", 6, 10)));
                Assert.DoesNotContain(Walk(placing).OfType<TextBlock>(), t => Equals(t.Tag, MultiplayerTab.RankingPeakTag));

                // With the column, a placement row's peak is a dash that says when it comes.
                placing = (DependencyObject)tab.BuildPlacementRow(new PlacementRow
                {
                    UserId = "u2", DisplayName = "Beto", Rating = 1500, PlacementPlayed = 6, PlacementRequired = 10,
                }, isMe: false, RankingTableLayout.All, ownResults: null);
                var dash = Walk(placing).OfType<TextBlock>().Single(t => Equals(t.Tag, MultiplayerTab.RankingPeakTag));
                Assert.Equal(Strings.Get("MpDash"), dash.Text);
                Assert.Equal(Strings.Get("MpRankPeakPlacementTip"), Assert.IsAssignableFrom<TextBlock>(dash.ToolTip).Text);
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    /// <summary>Every name, ranked or placing, carries the player's picture.</summary>
    [Fact]
    public void EveryNameCarriesThePlayersAvatar()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            tab.ShowDemoElo("ranking");
            var rows = tab.RankingBody.Children.OfType<Border>()
                .Where(b => b.Tag is LeaderboardRow or PlacementRow).ToList();
            Assert.Contains(rows, b => b.Tag is LeaderboardRow);
            Assert.Contains(rows, b => b.Tag is PlacementRow);
            foreach (var row in rows)
                Assert.Single(Walk(row).OfType<FrameworkElement>(), e => Equals(e.Tag, MultiplayerTab.RankingAvatarTag));
        });
        Assert.Null(error);
    }

    /// <summary>
    /// The podium card is built in code, so nothing checks it at compile time. In both languages,
    /// for each place: the name, the ELO, the age and the peak are there, and the moving light is
    /// on the first card only - and on none with reduced effects.
    /// </summary>
    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    public void ThePodiumCardBuildsForEveryPlace_AndOnlyTheFirstShines(string language)
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            Strings.SetLanguage(language);
            try
            {
                foreach (var effects in new[] { true, false })
                {
                    RankBadge.AnimationsOverride = effects;
                    var tab = new MultiplayerTab();
                    for (var place = 1; place <= 3; place++)
                    {
                        var row = new LeaderboardRow
                        {
                            Rank = place, UserId = "u" + place, DisplayName = "Jugador " + place,
                            Rating = 1800 - place * 40, RatingPeak = 1900, Streak = place == 1 ? 5 : 0,
                        };
                        var climb = place == 2 ? new HighlightPlayer { UserId = "u2", Points = 31, RatingFrom = 1689, RatingTo = 1720, Matches = 9 } : null;
                        var card = tab.BuildPodiumCard(row, team: false, ladderSize: 12, isMe: place == 3, climb, month: "octubre");
                        Assert.Equal(MultiplayerTab.PodiumCardTag, card.Tag);

                        var text = string.Join(" ", TextsOf(card));
                        Assert.Contains("Jugador " + place, text);
                        Assert.Contains(PlacementView.RatingText(row.Rating), text);
                        Assert.Contains(Strings.Get(RankAges.NameKey(RankAges.For(place, 12))).ToUpper(Strings.Culture), text);
                        Assert.Contains(Strings.Format("RkPeakShort", "1900"), text);
                        Assert.Equal(place == 3, text.Contains(Strings.Get("MpRankYouTag")));
                        Assert.Equal(place == 2, text.Contains("+31"));

                        var sheen = Walk(card).OfType<FrameworkElement>().Count(e => Equals(e.Tag, "RankingPodiumSheen"));
                        Assert.Equal(place == 1 && effects ? 1 : 0, sheen);
                    }
                }
            }
            finally
            {
                RankBadge.AnimationsOverride = null;
                Strings.SetLanguage(previous);
            }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// "Your position", built in code, in both languages and its three shapes: on the table with
    /// the place above in a higher age, first on the table, and being placed. The sentence at the
    /// foot is the one for that shape, and a placement shows one segment per match it takes.
    /// </summary>
    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    public void YourPositionBuildsInItsThreeShapes(string language)
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            Strings.SetLanguage(language);
            try
            {
                var tab = new MultiplayerTab();
                string Foot(FrameworkElement card) => Walk(card).OfType<TextBlock>()
                    .Single(t => Equals(t.Tag, "RankingYouNext")).Text;

                var neighbours = new[]
                {
                    new StandingNeighbour(4, "Comandante_Supremo_de_la_Gran_Armada_Libertadora", null, 1467, 65, false),
                    new StandingNeighbour(5, "Gorgo", null, 1402, 0, true),
                    new StandingNeighbour(6, "Malinche", null, 1395, -7, false),
                };
                var ranked = tab.BuildYouCard(new YourStanding
                {
                    Ranked = true, Place = 5, Of = 9, Age = RankAge.Fortress, Elo = 1402, Streak = 3,
                    Neighbours = neighbours, ToNextPlace = 66, NextPlace = 4, NextAge = RankAge.Industrial,
                }, team: false, climb: null, month: null, results: null);
                Assert.Equal(MultiplayerTab.YouCardTag, ranked.Tag);
                Assert.Equal(Strings.Format("RkYouNextAge", 66, 4, Strings.Get(RankAges.NameKey(RankAge.Industrial))), Foot(ranked));
                var words = string.Join(" ", TextsOf(ranked));
                Assert.Contains("#5", words);
                Assert.Contains("1402", words);
                Assert.Contains("+65", words);
                Assert.Contains("Malinche", words);
                // One line per neighbour, the viewer's own among them.
                Assert.Equal(3, Walk(ranked).OfType<FrameworkElement>().Count(e => e.Tag is StandingNeighbour));

                var sameAge = tab.BuildYouCard(new YourStanding
                {
                    Ranked = true, Place = 6, Of = 9, Age = RankAge.Fortress, Elo = 1395,
                    Neighbours = neighbours, ToNextPlace = 8, NextPlace = 5,
                }, team: false, climb: null, month: null, results: null);
                Assert.Equal(Strings.Format("RkYouNext", 8, 5), Foot(sameAge));

                var first = tab.BuildYouCard(new YourStanding
                {
                    Ranked = true, Place = 1, Of = 9, Age = RankAge.Sovereign, Elo = 1630,
                }, team: false, climb: null, month: null, results: null);
                Assert.Equal(Strings.Get("RkYouFirst"), Foot(first));

                var placing = tab.BuildYouCard(new YourStanding
                {
                    Ranked = false, Age = RankAge.Discovery, Elo = 1534, PlacementPlayed = 2, PlacementRequired = 5,
                }, team: true, climb: null, month: null, results: null);
                Assert.Equal(Strings.Format("RkYouLeft", 3), Foot(placing));
                Assert.Contains("2/5", string.Join(" ", TextsOf(placing)));

                var oneLeft = tab.BuildYouCard(new YourStanding
                {
                    Ranked = false, Age = RankAge.Discovery, PlacementPlayed = 9, PlacementRequired = 10,
                }, team: false, climb: null, month: null, results: null);
                Assert.Equal(Strings.Get("RkYouLeftOne"), Foot(oneLeft));
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// Two texts on a line never touch: in "Your position", the long name of a neighbour ends in
    /// an ellipsis before it reaches his ELO, at the narrowest the side column gets.
    /// </summary>
    [Fact]
    public void ALongNeighbourNameTrimsBeforeItReachesTheElo()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            var card = tab.BuildYouCard(new YourStanding
            {
                Ranked = true, Place = 5, Of = 9, Age = RankAge.Fortress, Elo = 1402,
                Neighbours = new[]
                {
                    new StandingNeighbour(4, "Comandante_Supremo_de_la_Gran_Armada_Libertadora_y_Protector", null, 1467, 65, false),
                    new StandingNeighbour(5, "Gorgo", null, 1402, 0, true),
                },
                ToNextPlace = 66, NextPlace = 4,
            }, team: false, climb: null, month: null, results: null);
            card.Measure(new Size(340, double.PositiveInfinity));
            card.Arrange(new Rect(0, 0, 340, card.DesiredSize.Height));
            card.UpdateLayout();

            var line = Walk(card).OfType<FrameworkElement>().First(e => e.Tag is StandingNeighbour { Place: 4 });
            var texts = Walk(line).OfType<TextBlock>().Where(t => t.Text.Length > 0).ToList();
            var name = texts.Single(t => t.Text.StartsWith("Comandante"));
            var elo = texts.Single(t => t.Text == "1467");
            Assert.Equal(TextTrimming.CharacterEllipsis, name.TextTrimming);
            var nameRight = name.TranslatePoint(new Point(name.ActualWidth, 0), line).X;
            var eloLeft = elo.TranslatePoint(new Point(0, 0), line).X;
            Assert.True(eloLeft - nameRight >= 8, $"the name ends {eloLeft - nameRight:0.#} px from the ELO.");
            Assert.True(line.ActualWidth <= 340);
        });
        Assert.Null(error);
    }

    /// <summary>
    /// THE ONE THAT MATTERS: going back to the subtab draws nothing when nothing the page shows
    /// moved - the rows on screen are the SAME objects - and draws again for each thing that did:
    /// the mode, the language, a new answer that SHOWS something new. Without the first half a
    /// tab switch rebuilt the podium, the plates and the side column every time (282-393 ms in a
    /// player's log, because the payload arrives once a minute as a new object whose only changes
    /// are drawn nowhere); without the second the page would show a stale ladder.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_GoingBackDrawsOnlyWhatChanged()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            try
            {
                var tab = new MultiplayerTab();
                tab.ShowDemoElo("ranking");
                object FirstRow() => tab.RankingBody.Children.OfType<Border>().First(b => b.Tag is LeaderboardRow);

                var drawn = FirstRow();
                Assert.False(tab.RenderRankingIfChanged());
                Assert.False(tab.RenderRankingIfChanged());
                Assert.Same(drawn, FirstRow());

                // The language.
                Strings.SetLanguage(Strings.Language == Strings.LangEs ? Strings.LangEn : Strings.LangEs);
                Assert.True(tab.RenderRankingIfChanged());
                Assert.NotSame(drawn, FirstRow());
                Assert.False(tab.RenderRankingIfChanged());

                // A new answer from the server with the same figures: only its stamp and every
                // player's rd moved, and neither is drawn. Nothing is rebuilt.
                drawn = FirstRow();
                var field = typeof(MultiplayerTab).GetField("_communityStats",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                var onScreen = (CommunityStats)field.GetValue(tab)!;
                CommunityStats Copy() => System.Text.Json.JsonSerializer.Deserialize<CommunityStats>(
                    System.Text.Json.JsonSerializer.Serialize(onScreen))!;
                var same = Copy();
                same.GeneratedAt = "2099-01-01T00:00:00Z";
                foreach (var row in same.Leaderboard ?? new()) row.Rd += 0.5;
                field.SetValue(tab, same);
                Assert.False(tab.RenderRankingIfChanged());
                Assert.Same(drawn, FirstRow());

                // One rating moved: that is drawn, so the page is.
                var moved = Copy();
                moved.Leaderboard![0].Rating += 25;
                field.SetValue(tab, moved);
                Assert.True(tab.RenderRankingIfChanged());
                Assert.NotSame(drawn, FirstRow());
                Assert.False(tab.RenderRankingIfChanged());

                // The mode: a mode button draws by itself, and the page is then settled again.
                tab.RankingModeTeam.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.Equal("active", tab.RankingModeTeam.Tag);
                Assert.False(tab.RenderRankingIfChanged());

                // Highlights keeps its own rules: it always draws.
                tab.RankingModeHighlights.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.True(tab.RenderRankingIfChanged());
                Assert.True(tab.RenderRankingIfChanged());
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    /// <summary>Every piece of text under an element: a TextBlock's own, or its runs'.</summary>
    private static IEnumerable<string> TextsOf(DependencyObject root)
        => Walk(root).OfType<TextBlock>().Select(t => t.Inlines.Count > 0
            ? string.Concat(t.Inlines.OfType<System.Windows.Documents.Run>().Select(r => r.Text))
            : t.Text);

    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var d in Walk(child)) yield return d;
    }
}
