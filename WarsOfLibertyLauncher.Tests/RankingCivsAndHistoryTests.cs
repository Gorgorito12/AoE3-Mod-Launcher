using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The ranking page's two additions: the CIVS column (a player's three most-played
/// civilizations, as flags) and the match list beside the ladder (who beat whom, on which
/// map, with which civilization).
///
/// <para>Asked for after the ladder sat beside a "civilization balance" strip that named
/// civilizations and nobody, over a statistics table that showed one match — because the
/// civilization only ever travelled in the host's first-pass report, and the recording that
/// names it is usually not on disk yet when that report goes out.</para>
/// </summary>
[Collection("wpf-and-language")]
public class RankingCivsAndHistoryTests
{
    private static LeaderboardRow Row(string id, List<PlayerTopCiv>? civs) => new()
    {
        Rank = 1, UserId = id, DisplayName = id, DiscordUsername = id,
        Rating = 1500, Rd = 80, GamesPlayed = 10, Wins = 6, Losses = 4, TopCivs = civs,
    };

    // ---------------------------------------------------------------- the column

    /// <summary>
    /// THE ONE THAT MATTERS: the CIVS column exists only when a row has something to put in
    /// it. A backend that predates the field (null) and a community with nothing on record
    /// yet (empty lists) both draw the six-column table, exactly as before.
    /// </summary>
    [Fact]
    public void TheCivsColumnIsOnlyThereWhenARowCarriesOne()
    {
        var without = RankingTableLayout.For(new[] { Row("a", null), Row("b", new()) });
        Assert.DoesNotContain(without, c => c.Column == RankingColumn.Civs);
        Assert.Equal(RankingTableLayout.All.Count - 1, without.Count);

        var with = RankingTableLayout.For(new[]
        {
            Row("a", null),
            Row("b", new() { new PlayerTopCiv { Civ = "Ethiopians", Played = 1 } }),
        });
        Assert.Contains(with, c => c.Column == RankingColumn.Civs);
        Assert.Same(RankingTableLayout.All, with);

        Assert.DoesNotContain(RankingTableLayout.For(null), c => c.Column == RankingColumn.Civs);
    }

    /// <summary>The column sits between the rating and the numbers, is fixed at three flags
    /// wide, and reads left to right like the flags in it.</summary>
    [Fact]
    public void TheCivsColumnIsThreeFlagsWideAndSitsAfterTheRating()
    {
        var all = RankingTableLayout.All.Select(c => c.Column).ToList();
        Assert.Equal(all.IndexOf(RankingColumn.Rating) + 1, all.IndexOf(RankingColumn.Civs));
        Assert.Equal(all.IndexOf(RankingColumn.Civs) + 1, all.IndexOf(RankingColumn.Decided));

        var spec = RankingTableLayout.All.Single(c => c.Column == RankingColumn.Civs);
        Assert.Equal(RankingTableLayout.CivsWidth, spec.FixedWidth);
        Assert.False(spec.RightAligned);
        // Wide enough for three flags, and for the heading that names the column.
        Assert.True(RankingTableLayout.CivsWidth
            >= 3 * RankingTableLayout.CivFlagSize + 2 * RankingTableLayout.CivFlagGap);
    }

    /// <summary>
    /// A row builds with and without the column, and the cells land in the right columns
    /// either way: every child of the grid sits inside the grid's own column count, and the
    /// percentage is in the LAST column in both shapes.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARowPlacesItsCellsByColumnNotByPosition(bool withCivs)
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            var row = Row("me", withCivs
                ? new() { new PlayerTopCiv { Civ = "Ethiopians", Played = 3 } }
                : null);

            var element = tab.BuildLeaderboardRow(row, 1400, 1600, isMe: false);
            var grid = Assert.IsType<Grid>(Assert.IsType<Border>(element).Child);

            var expectedColumns = withCivs ? RankingTableLayout.All.Count : RankingTableLayout.All.Count - 1;
            Assert.Equal(expectedColumns, grid.ColumnDefinitions.Count);
            foreach (UIElement child in grid.Children)
                Assert.InRange(Grid.GetColumn(child), 0, expectedColumns - 1);

            var last = grid.Children.Cast<UIElement>()
                .Where(c => Grid.GetColumn(c) == expectedColumns - 1)
                .OfType<TextBlock>()
                .Single();
            Assert.Contains("%", last.Text);
        });

        Assert.Null(error);
    }

    /// <summary>
    /// Without the mod's art the cell still says which civilizations: the name, trimmed, and
    /// the tooltip with the count. Three at most, however many the server sent.
    /// </summary>
    [Fact]
    public void TheCivsCellNamesTheCivilizationsWhenThereIsNoFlag()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            try
            {
                Strings.SetLanguage("es");
                var row = Row("a", new()
                {
                    new PlayerTopCiv { Civ = "Ethiopians", Played = 5 },
                    new PlayerTopCiv { Civ = "Zulu", Played = 2 },
                    new PlayerTopCiv { Civ = "Chinese", Played = 1 },
                    new PlayerTopCiv { Civ = "Dutch", Played = 1 },
                });

                var cell = Assert.IsType<StackPanel>(MultiplayerTab.BuildTopCivsCell(row, vocab: null));
                Assert.Equal(3, cell.Children.Count);

                // Not a button: nothing to click. The flag — or the name, when there is no art.
                var first = Assert.IsType<TextBlock>(cell.Children[0]);
                Assert.Equal("Ethiopians", first.Text);

                // Hovering reveals the card at once: the name, the count and its place.
                Assert.Equal(0, ToolTipService.GetInitialShowDelay(first));
                var card = Assert.IsAssignableFrom<DependencyObject>(first.ToolTip);
                var words = string.Join(" ", TextBlocks(card).Select(t => t.Text));
                Assert.Contains("Ethiopians", words);
                Assert.Contains("5", words);
                Assert.Contains("1", words);
            }
            finally { Strings.SetLanguage(previous); }
        });

        Assert.Null(error);
    }

    // ---------------------------------------------------------------- the match list

    private static CommunityMatch Match(params (string Name, double Result, string? Civ)[] players) => new()
    {
        Id = "m1",
        ModId = "wol",
        MapName = "ESOC_Hudson Bay",
        DurationSeconds = 1493,
        ReportedAt = DateTime.UtcNow.AddHours(-10).ToString("s"),
        Participants = players.Select((p, i) => new MatchHistoryParticipant
        {
            UserId = "u" + i, DisplayName = p.Name, DiscordUsername = p.Name.ToLowerInvariant(),
            Result = p.Result, Civ = p.Civ,
        }).ToList(),
    };

    private static IEnumerable<TextBlock> TextBlocks(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is TextBlock t) yield return t;
            foreach (var deeper in TextBlocks(child)) yield return deeper;
        }
    }

    /// <summary>
    /// What a block SHOWS, which is not what <c>TextBlock.Text</c> reports.
    ///
    /// <para>That property answers only for content assigned through it; a block built by
    /// adding <c>Run</c>s comes back as the empty string. The sub-line here is run-built — it
    /// leads with a coloured COMPETITIVE/CASUAL word — so asking <c>.Text</c> makes a
    /// <c>Contains</c> assertion fail and, far worse, makes a <c>DoesNotContain</c> one pass
    /// over nothing at all. Production has the same reader for the same reason.</para>
    /// </summary>
    private static string Plain(TextBlock t) => WarsOfLibertyLauncher.RevealText.PlainTextOf(t);

    /// <summary>
    /// A decided match is a sentence in the language's own word order, both names in it, and
    /// the map with the length and the age under it. No flags here — there is no mod art in
    /// a test — so the civilizations ride after the names in text.
    /// </summary>
    [Fact]
    public void ADecidedMatchReadsAsWhoBeatWhomOnWhichMap()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            try
            {
                Strings.SetLanguage("es");
                var row = MultiplayerTab.BuildRankingMatchRow(
                    Match(("Geaf_Argento", 1, "Ethiopians"), ("Aluclown", 0, "Zulu")), vocab: null);

                var texts = TextBlocks(row).ToList();
                var sentence = texts[0];
                var words = string.Concat(sentence.Inlines.OfType<Run>().Select(r => r.Text));
                // The civilization's NAME is always in the sentence — with the flag beside it
                // when the mod has one, and alone when it does not. A flag alone asked the
                // reader to know every flag.
                Assert.Equal("Geaf_Argento · Ethiopians le ganó a Aluclown · Zulu", words);
                // The winner is bold; "le ganó a" is not.
                Assert.Contains(sentence.Inlines.OfType<Run>(),
                    r => r.Text == "Geaf_Argento" && r.FontWeight == FontWeights.SemiBold);

                Assert.Contains(texts, t => Plain(t).Contains("ESOC Hudson Bay") && Plain(t).Contains("25 min"));
                Assert.Contains(texts, t => Plain(t).Contains("10 h"));
            }
            finally { Strings.SetLanguage(previous); }
        });

        Assert.Null(error);
    }

    /// <summary>
    /// A match whose result was never read lists who was there without claiming a winner, on
    /// ONE line, and a team match lists everybody on that same line with a ✓ or ✕ where the
    /// result is known.
    ///
    /// <para>The marks are counted as RUNS, not as TextBlocks. They used to be one TextBlock
    /// per player, which is what made this row four lines tall for a 1v1 and eight for a 3v3
    /// — see the height test below for why that mattered.</para>
    /// </summary>
    [Fact]
    public void AnUndecidedOrTeamMatchListsWhoWasThere()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            try
            {
                Strings.SetLanguage("en");

                var undecided = MultiplayerTab.BuildRankingMatchRow(
                    Match(("A", 0.5, null), ("B", 0.5, null)), vocab: null);
                var texts = TextBlocks(undecided).ToList();
                Assert.Contains(texts, t => Plain(t).Contains("no result read"));
                Assert.DoesNotContain(texts, t => Plain(t).Contains("beat"));
                // Both names, and the "vs" between them, in the SAME block.
                var who = Assert.Single(texts, t => t.Inlines.OfType<Run>().Any(r => r.Text == "A"));
                Assert.Contains(who.Inlines.OfType<Run>(), r => r.Text == "B");
                Assert.Contains(who.Inlines.OfType<Run>(), r => r.Text == " vs ");

                var team = MultiplayerTab.BuildRankingMatchRow(
                    Match(("A", 1, "Zulu"), ("B", 1, null), ("C", 0, null), ("D", 0, "Dutch")), vocab: null);
                var teamRuns = TextBlocks(team).SelectMany(t => t.Inlines.OfType<Run>()).ToList();
                Assert.Equal(2, teamRuns.Count(r => r.Text == "✓ "));
                Assert.Equal(2, teamRuns.Count(r => r.Text == "✕ "));
                // Four players is a list, not a duel: no "vs" pretending to name two sides.
                Assert.DoesNotContain(teamRuns, r => r.Text == " vs ");
            }
            finally { Strings.SetLanguage(previous); }
        });

        Assert.Null(error);
    }

    /// <summary>
    /// THE ONE THAT MATTERS: a match is TWO LINES whether or not anybody knows who won.
    ///
    /// <para>Reported as the community strip eating the rooms list. An undecided match used to
    /// spend a line on the mod, one on EACH player and one on the length — four lines for a
    /// 1v1 against a decided match's two — and the three cards of that strip share one grid
    /// row, so the tallest of them sets the height of the strip, which is paid for out of the
    /// list underneath it.</para>
    ///
    /// <para>This is the size limit, and it is a property rather than a number: no MaxHeight
    /// (it would clip the third match with no scrollbar and nothing to say so) and no inner
    /// ScrollViewer (the whole Rooms column is one page, and a nested scroller is what
    /// <c>TheRoomsListScrollsWithThePageAndNeverOnItsOwn</c> exists to forbid). Measured at a
    /// FINITE width, because Measure clamps DesiredSize to the constraint it is given and an
    /// infinite one would report every row as fitting on a single line.</para>
    /// </summary>
    [Fact]
    public void AnUndecidedMatchIsNoTallerThanADecidedOne()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            try
            {
                Strings.SetLanguage("es");

                // About what one of the three activity cards gets in the default window.
                var room = new Size(240, double.PositiveInfinity);

                var decided = (FrameworkElement)MultiplayerTab.BuildRankingMatchRow(
                    Match(("Geaf_Argento", 1, null), ("Aluclown", 0, null)), vocab: null);
                var undecided = (FrameworkElement)MultiplayerTab.BuildRankingMatchRow(
                    Match(("Geaf_Argento", 0.5, null), ("Aluclown", 0.5, null)), vocab: null);
                decided.Measure(room);
                undecided.Measure(room);

                Assert.True(undecided.DesiredSize.Height <= decided.DesiredSize.Height,
                    $"undecided {undecided.DesiredSize.Height} > decided {decided.DesiredSize.Height}");

                // THREE text blocks in each, and the same three: who, the line under it, and
                // the age in its own column. A fourth is a row that grew a line back — which
                // is exactly what one TextBlock per player was.
                Assert.Equal(3, TextBlocks(decided).Count(t => !string.IsNullOrEmpty(RunText(t))));
                Assert.Equal(3, TextBlocks(undecided).Count(t => !string.IsNullOrEmpty(RunText(t))));

                // And the sub-line leads with the reason it did not count, because that line
                // trims from the right.
                var under = TextBlocks(undecided).First(t => RunText(t).Contains("Wars of Liberty")
                                                          || RunText(t).Contains("sin resultado"));
                Assert.StartsWith(Strings.Get("MpRankHistoryUndecided"), RunText(under));
            }
            finally { Strings.SetLanguage(previous); }
        });

        Assert.Null(error);
    }

    /// <summary>A TextBlock built from Runs answers "" to .Text; this is what it really says.</summary>
    private static string RunText(TextBlock t) =>
        !string.IsNullOrEmpty(t.Text) ? t.Text : string.Concat(t.Inlines.OfType<Run>().Select(r => r.Text));

    /// <summary>The degraded shape: nothing known at all. It must still build.</summary>
    [Fact]
    public void AMatchWithNothingKnownStillBuilds()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            Assert.NotNull(MultiplayerTab.BuildRankingMatchRow(new CommunityMatch(), vocab: null));
        });

        Assert.Null(error);
    }

    /// <summary>
    /// The statistics map table prints the WHOLE name, pack included, in both the full table
    /// and the sidebar. It split "ESOC_Fertile Crescent" into a name and a pack tag and the
    /// sidebar dropped the tag, so it said "Fertile Crescent" to people who know the map as
    /// "ESOC Fertile Crescent" — the name the match list beside the ladder already prints.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheMapTablePrintsTheWholeNamePackIncluded(bool compact)
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            var row = tab.BuildMapRow(1, "ESOC_Fertile Crescent", 5, 5, compact, isLast: false);
            var texts = TextBlocks(row).Select(t => t.Text).ToList();
            Assert.Contains("ESOC Fertile Crescent", texts);
            Assert.DoesNotContain("Fertile Crescent", texts);
            Assert.DoesNotContain("ESOC", texts);
        });

        Assert.Null(error);
    }

    /// <summary>
    /// The rooms strip's community card offers "See all", and it is the community's list it
    /// promises: on the FALLBACK branch — the viewer's own history, from a backend too old to
    /// send recent matches — there is no such list to send anybody to, so no link.
    /// </summary>
    [Fact]
    public void TheCommunityCardOffersSeeAllOnlyWhenItIsTheCommunitys()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            Assert.Equal(Visibility.Collapsed, tab.ActivityRecentSeeAll.Visibility);

            // No payload at all: no card, and nothing to see.
            Assert.False(tab.FillRecentMatches(null));
            Assert.Equal(Visibility.Collapsed, tab.ActivityRecentSeeAll.Visibility);

            // The community's matches: the card, and the link to the rest of them.
            var stats = new CommunityStats
            {
                RecentMatches = new List<CommunityMatch> { Match(("A", 1, null), ("B", 0, null)) },
            };
            Assert.True(tab.FillRecentMatches(stats));
            Assert.Equal(Visibility.Visible, tab.ActivityRecentCard.Visibility);
            Assert.Equal(Visibility.Visible, tab.ActivityRecentSeeAll.Visibility);

            // And it goes away again when the payload does.
            Assert.False(tab.FillRecentMatches(new CommunityStats()));
            Assert.Equal(Visibility.Collapsed, tab.ActivityRecentSeeAll.Visibility);
        });

        Assert.Null(error);
    }

    /// <summary>The new texts exist in both languages.</summary>
    [Theory]
    [InlineData("MpRankColCivs")]
    [InlineData("MpRankCivsTooltip")]
    [InlineData("MpRankHistoryTitle")]
    [InlineData("MpRankHistoryUndecided")]
    [InlineData("MpRankHistoryDuration")]
    public void TheTextsExistInBothLanguages(string key)
    {
        var previous = Strings.Language;
        try
        {
            Strings.SetLanguage("es");
            Assert.NotEqual(key, Strings.Get(key));
            Strings.SetLanguage("en");
            Assert.NotEqual(key, Strings.Get(key));
        }
        finally { Strings.SetLanguage(previous); }
    }

    /// <summary>
    /// The confirmation carries the civilizations only when there are some: an empty map
    /// would be a claim that nobody played anything, and the wire contract says "omitted".
    /// </summary>
    [Fact]
    public void TheConfirmationOmitsTheCivsWhenItHasNone()
    {
        var without = System.Text.Json.JsonSerializer.Serialize(new ConfirmMatchRequest { LobbyId = "l" });
        Assert.DoesNotContain("civs", without);
        Assert.DoesNotContain("home_cities", without);

        var with = System.Text.Json.JsonSerializer.Serialize(new ConfirmMatchRequest
        {
            LobbyId = "l",
            Civs = new Dictionary<string, string> { ["u1"] = "Ethiopians" },
        });
        Assert.Contains("\"civs\":{\"u1\":\"Ethiopians\"}", with);
    }
}
