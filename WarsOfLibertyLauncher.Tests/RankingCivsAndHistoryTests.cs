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
/// The ranking page's row (rating v3, design 55a) and the match list beside the ladder (who
/// beat whom, on which map, with which civilization). The CIVS column this class was written for
/// went with the handoff's five-column table.
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
        Rating = 1500, Rd = 80, GamesPlayed = 10, Wins = 6, Losses = 4, RatedWins = 6, RatedLosses = 4,
        TopCivs = civs,
    };

    // ---------------------------------------------------------------- the row (rating v3)

    /// <summary>
    /// THE ONE THAT MATTERS: rating v3 orders the table by ELO, so the bar is the ELO against
    /// FIRST PLACE — read back out of the real row, because a bar test that only calls the pure
    /// function passes whatever the row actually feeds it.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheBarIsTheEloAgainstFirstPlace()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            var top = Row("top", null);
            top.Rating = 1748;
            var lower = Row("lower", null);
            lower.Rating = 1502;
            lower.Rd = 30;   // the deviation no longer moves the bar at all

            var a = BarFractionOf(tab.BuildLeaderboardRow(top, 1748, isMe: false, RankingTableLayout.All, 12, team: false));
            var b = BarFractionOf(tab.BuildLeaderboardRow(lower, 1748, isMe: false, RankingTableLayout.All, 12, team: false));
            Assert.Equal(1.0, a, 3);
            Assert.Equal(RankingTableLayout.BarFraction(1502, 1748), b, 3);
            Assert.True(b < a);
        });

        Assert.Null(error);
    }

    /// <summary>
    /// Reads the bar back out of a built row: the fill is the first of a star/star pair inside the
    /// track tagged "RankingBar", so that column's width IS the fraction.
    /// </summary>
    private static double BarFractionOf(UIElement row)
    {
        foreach (var track in Descendants(row).OfType<Border>())
            if (Equals(track.Tag, "RankingBar") && track.Child is Grid fill && fill.ColumnDefinitions.Count == 2)
                return fill.ColumnDefinitions[0].Width.Value;
        throw new Xunit.Sdk.XunitException("no rating bar found in the row");
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var d in Descendants(child)) yield return d;
    }

    /// <summary>
    /// A row lands its cells inside its own columns in both shapes — five columns wide, three
    /// under 600 px (55b, no W-L, no %, no bar) — and the percentage is the LAST column when there
    /// is one.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARowPlacesItsCellsByColumnNotByPosition(bool narrow)
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            var specs = narrow ? RankingTableLayout.Narrow : RankingTableLayout.All;
            var element = tab.BuildLeaderboardRow(Row("me", null), 1600, isMe: false, specs, 12, team: false);
            var grid = Assert.IsType<Grid>(Assert.IsType<Border>(element).Child);

            // The table's columns with a gap column between each two (design 59); every cell sits
            // in a table column, never in a gap.
            Assert.Equal(2 * specs.Count - 1, grid.ColumnDefinitions.Count);
            foreach (UIElement child in grid.Children)
            {
                Assert.InRange(Grid.GetColumn(child), 0, 2 * specs.Count - 2);
                Assert.Equal(0, Grid.GetColumn(child) % 2);
            }

            if (narrow)
            {
                Assert.Throws<Xunit.Sdk.XunitException>(() => BarFractionOf(element));
            }
            else
            {
                var last = grid.Children.Cast<UIElement>()
                    .Where(c => Grid.GetColumn(c) == MultiplayerTab.GridColumnOf(specs.Count - 1))
                    .OfType<TextBlock>()
                    .Single();
                Assert.Equal("60", last.Text);   // 6-4: the rated record, no "%" sign (55a)
            }
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
    /// the map with the length and the age under it.
    ///
    /// <para><b>The civilization is NOT in the sentence</b> (design handoff turns 38-39): it is
    /// the flag beside the name, with the name of the civ in the flag's tooltip. Printed inline
    /// it is what made a 2v2 wrap to several lines. There is no mod art in a test, so no flag
    /// either — the sentence is exactly the two names and the verb.</para>
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
                Assert.Equal("Geaf_Argento le ganó a Aluclown", words);
                Assert.DoesNotContain("Ethiopians", words);
                // ONE line: a sentence that wraps is what made the cards 10-12 lines tall.
                Assert.Equal(TextWrapping.NoWrap, sentence.TextWrapping);
                Assert.Equal(TextTrimming.CharacterEllipsis, sentence.TextTrimming);
                // The winner is bold; "le ganó a" is not.
                Assert.Contains(sentence.Inlines.OfType<Run>(),
                    r => r.Text == "Geaf_Argento" && r.FontWeight == FontWeights.SemiBold);

                Assert.Contains(texts, t => Plain(t).Contains("ESOC Hudson Bay") && Plain(t).Contains("25 min"));
                Assert.Contains(texts, t => Plain(t).Contains("10 h"));
                // The mod is not on the line under it any more, as drawn: the row is the match.
                Assert.DoesNotContain(texts, t => Plain(t).Contains("Wars of Liberty"));
            }
            finally { Strings.SetLanguage(previous); }
        });

        Assert.Null(error);
    }

    /// <summary>
    /// A match whose result was never read lists who was there without claiming a winner, on
    /// ONE line: the two sides joined by "vs", or a plain list when there are no sides to join.
    /// A decided team match is the same sentence as a 1v1 — the winning side "beat" the losing
    /// one — with no ✓/✕ marks, which were dropped with the stacked layout (turns 38-39).
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
                Assert.Contains(texts, t => Plain(t).Contains("no result"));
                Assert.DoesNotContain(texts, t => Plain(t).Contains("beat"));
                // Both names, and the "vs" between them, in the SAME block.
                var who = Assert.Single(texts, t => t.Inlines.OfType<Run>().Any(r => r.Text == "A"));
                Assert.Contains(who.Inlines.OfType<Run>(), r => r.Text == "B");
                Assert.Contains(who.Inlines.OfType<Run>(), r => r.Text == " vs ");

                // A decided team match: the winners beat the losers, every name on ONE line.
                var team = MultiplayerTab.BuildRankingMatchRow(
                    Match(("A", 1, "Zulu"), ("B", 1, null), ("C", 0, null), ("D", 0, "Dutch")), vocab: null);
                var teamRuns = TextBlocks(team).SelectMany(t => t.Inlines.OfType<Run>()).ToList();
                Assert.DoesNotContain(teamRuns, r => r.Text.Contains('✓') || r.Text.Contains('✕'));
                Assert.Contains(teamRuns, r => r.Text.Contains("beat"));
                var teamWho = Assert.Single(TextBlocks(team), t => t.Inlines.OfType<Run>().Any(r => r.Text == "A"));
                foreach (var name in new[] { "B", "C", "D" })
                    Assert.Contains(teamWho.Inlines.OfType<Run>(), r => r.Text == name);
                Assert.Contains(teamWho.Inlines.OfType<Run>(), r => r.Text == "A" && r.FontWeight == FontWeights.SemiBold);
                Assert.Contains(teamWho.Inlines.OfType<Run>(), r => r.Text == "C" && r.FontWeight != FontWeights.SemiBold);

                // An undecided team match WITH sides on record: side A "vs" side B.
                var sided = Match(("A", 0.5, null), ("B", 0.5, null), ("C", 0.5, null), ("D", 0.5, null));
                sided.Participants[0].Team = 1; sided.Participants[1].Team = 1;
                sided.Participants[2].Team = 2; sided.Participants[3].Team = 2;
                var sidedRuns = TextBlocks(MultiplayerTab.BuildRankingMatchRow(sided, vocab: null))
                    .SelectMany(t => t.Inlines.OfType<Run>()).ToList();
                Assert.Single(sidedRuns, r => r.Text == " vs ");

                // Four players with NO sides on record is a list, not a duel: no "vs"
                // pretending to name two sides.
                var list = MultiplayerTab.BuildRankingMatchRow(
                    Match(("A", 0.5, null), ("B", 0.5, null), ("C", 0.5, null), ("D", 0.5, null)), vocab: null);
                var listRuns = TextBlocks(list).SelectMany(t => t.Inlines.OfType<Run>()).ToList();
                Assert.DoesNotContain(listRuns, r => r.Text == " vs ");
                Assert.Equal(3, listRuns.Count(r => r.Text == " · "));
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

                // And the sub-line leads with the LABEL and then the reason it did not count,
                // both before the map, because that line trims from the right (turn 40). The
                // room's mode is unknown here, so the label is the format alone.
                var under = TextBlocks(undecided).First(t => RunText(t).Contains("sin resultado"));
                Assert.StartsWith("1v1 · " + Strings.Get("MpRankHistoryUndecided"), RunText(under));
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

    /// <summary>
    /// Design handoff turn 40: line 2 leads with ONE label that carries the mode AND the format
    /// — "COMPETITIVE 2v2" — and the format appears nowhere else on the line. It used to be a
    /// separate segment shown only when somebody won, so a decided 1v1 read "COMPETITIVE · 1v1"
    /// and an undecided one "COMPETITIVE · no result read", two shapes for one kind of match.
    /// </summary>
    [Fact]
    public void TheSubLineLeadsWithModeAndFormat_AndSaysTheFormatOnce()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            try
            {
                Strings.SetLanguage("en");
                var competitive = Strings.Get("MpMatchModeCompetitive");

                var oneVsOne = Match(("Geaf_Argento", 1, null), ("Aluclown", 0, null));
                oneVsOne.Competitive = true;
                Assert.Equal($"{competitive} 1v1 · ESOC Hudson Bay · 25 min", SubLine(oneVsOne));

                var twoVsTwo = Match(("A", 0.5, null), ("B", 0.5, null), ("C", 0.5, null), ("D", 0.5, null));
                twoVsTwo.Competitive = true;
                twoVsTwo.Participants[0].Team = 1;
                twoVsTwo.Participants[1].Team = 1;
                twoVsTwo.Participants[2].Team = 2;
                twoVsTwo.Participants[3].Team = 2;
                var line = SubLine(twoVsTwo);
                Assert.Equal($"{competitive} 2v2 · no result · ESOC Hudson Bay · 25 min", line);
                Assert.Single(System.Text.RegularExpressions.Regex.Matches(line, "2v2"));

                // Four players with no team on record could be a 2v2 stored before teams were:
                // no format is claimed, and the mode word stands alone.
                var unknownShape = Match(("A", 0.5, null), ("B", 0.5, null), ("C", 0.5, null), ("D", 0.5, null));
                unknownShape.Competitive = true;
                Assert.Equal($"{competitive} · no result · ESOC Hudson Bay · 25 min", SubLine(unknownShape));
            }
            finally { Strings.SetLanguage(previous); }
        });

        Assert.Null(error);

        static string SubLine(CommunityMatch m)
        {
            var row = MultiplayerTab.BuildRankingMatchRow(m, vocab: null);
            return TextBlocks(row).Select(RunText).First(t => t.Contains("ESOC Hudson Bay"));
        }
    }

    /// <summary>
    /// THE ONE THAT MATTERS for the label: one match reads the same in the Ranking's «Latest
    /// matches», the Rooms card and the profile History — the kind of room in BOLD, competitive in
    /// #e6c06a and casual in #a8bcd2 (designs 57 and 59). A label with no known mode (a bare "1v1")
    /// keeps the line's own colour: the casual colour there would claim a mode nobody recorded.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheModeLabelIsTheSameBoldColourEverywhere()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            try
            {
                Strings.SetLanguage("en");
                var competitive = Strings.Get("MpMatchModeCompetitive");
                var gold = (System.Windows.Media.SolidColorBrush)Application.Current.FindResource("MpMatchLabelCompetitive");
                var casualBrush = (System.Windows.Media.SolidColorBrush)Application.Current.FindResource("MpMatchLabelCasual");
                Assert.Equal(System.Windows.Media.Color.FromRgb(0xE6, 0xC0, 0x6A), gold.Color);
                Assert.Equal(System.Windows.Media.Color.FromRgb(0xA8, 0xBC, 0xD2), casualBrush.Color);

                Run LabelOf(CommunityMatch m, MultiplayerTab.MatchRowLook? look)
                    => TextBlocks(MultiplayerTab.BuildRankingMatchRow(m, vocab: null, look: look))
                        .First(t => RunText(t).Contains("ESOC Hudson Bay"))
                        .Inlines.OfType<Run>().First();

                var match = Match(("Geaf_Argento", 1, null), ("Aluclown", 0, null));
                foreach (var look in new MultiplayerTab.MatchRowLook?[]
                         {
                             MultiplayerTab.MatchRowLook.Ranking(15),
                             MultiplayerTab.MatchRowLook.Rooms(RoomsActivityLayout.Fluid(1300)),
                         })
                {
                    match.Competitive = true;
                    var label = LabelOf(match, look);
                    Assert.Equal($"{competitive} 1v1", label.Text);
                    Assert.Equal(FontWeights.Bold, label.FontWeight);
                    Assert.Same(gold, label.Foreground);

                    match.Competitive = false;
                    Assert.Same(casualBrush, LabelOf(match, look).Foreground);

                    match.Competitive = null;
                    var bare = LabelOf(match, look);
                    Assert.Equal("1v1", bare.Text);
                    Assert.Same(Application.Current.FindResource("MpTextMuted"), bare.Foreground);
                }

                // The profile History's meta line leads with the same run.
                var tab = new MultiplayerTab();
                var history = new MatchHistoryRow
                {
                    Id = "h1", ModId = "wol", MapName = "ESOC_Hudson_Bay", Competitive = true,
                    StartedAt = "2026-08-29T17:50:00Z", EndedAt = "2026-08-29T18:15:00Z",
                    Result = 1.0, Rated = true, RatingBefore = 1500, RatingAfter = 1516,
                    Participants = new List<MatchHistoryParticipant>
                    {
                        new() { UserId = "me", DisplayName = "Geaf_Argento", Result = 1.0 },
                        new() { UserId = "alu", DisplayName = "Aluclown", Result = 0.0 },
                    },
                };
                var historyLabel = TextBlocks(tab.BuildHistoryRow(history, "me"))
                    .SelectMany(t => t.Inlines.OfType<Run>())
                    .First(r => r.Text == competitive);
                Assert.Equal(FontWeights.Bold, historyLabel.FontWeight);
                Assert.Same(gold, historyLabel.Foreground);
            }
            finally { Strings.SetLanguage(previous); }
        });

        Assert.Null(error);
    }

    /// <summary>The new texts exist in both languages.</summary>
    [Theory]
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
