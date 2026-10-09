using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The pure rules behind downloading a recording from Ranking (design handoff 63): which state a
/// row's button is in, where a file lands, how the Matches view lays out and groups its rows.
/// </summary>
public class ReplayBrowseTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ARecordingInsideItsYearIsAvailable()
        => Assert.Equal(ReplayAvailability.Available, ReplayBrowse.Decide(true, "2027-10-09T18:00:00Z", Now));

    [Fact]
    public void AMatchThatHadOneAndLostItReadsExpired_NeverNeverRecorded()
        => Assert.Equal(ReplayAvailability.Expired, ReplayBrowse.Decide(false, "2026-01-01T00:00:00Z", Now));

    [Fact]
    public void PastItsDateItIsExpiredEvenWhileTheServerStillSaysItHasOne()
        => Assert.Equal(ReplayAvailability.Expired, ReplayBrowse.Decide(true, "2026-10-09T17:59:59Z", Now));

    [Fact]
    public void NoRecordingAndOlderServersOfferNothing()
    {
        Assert.Equal(ReplayAvailability.None, ReplayBrowse.Decide(false, null, Now));
        // An older backend sends neither field: "we do not know" draws no button.
        Assert.Equal(ReplayAvailability.None, ReplayBrowse.Decide(null, null, Now));
        Assert.Equal(ReplayAvailability.None, ReplayBrowse.Decide(null, "2027-01-01T00:00:00Z", Now));
    }

    [Fact]
    public void AKnownRecordingWithNoExpiryStaysAvailable()
        => Assert.Equal(ReplayAvailability.Available, ReplayBrowse.Decide(true, null, Now));

    [Fact]
    public void AFreeNameIsKept_AndATakenOneGetsANumber()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folder = @"C:\Games\Savegame";
        Assert.Equal(Path.Combine(folder, "a.age3Yrec"), ReplayBrowse.UniqueReplayPath(folder, "a.age3Yrec", taken.Contains));

        taken.Add(Path.Combine(folder, "a.age3Yrec"));
        Assert.Equal(Path.Combine(folder, "a (2).age3Yrec"), ReplayBrowse.UniqueReplayPath(folder, "a.age3Yrec", taken.Contains));

        taken.Add(Path.Combine(folder, "a (2).age3Yrec"));
        Assert.Equal(Path.Combine(folder, "a (3).age3Yrec"), ReplayBrowse.UniqueReplayPath(folder, "a.age3Yrec", taken.Contains));
    }

    [Fact]
    public void AnExistingFileIsNeverTheAnswer()
    {
        var path = ReplayBrowse.UniqueReplayPath(@"C:\x", "a.age3Yrec", _ => true);
        Assert.EndsWith(".age3Yrec", path);
        Assert.StartsWith(@"C:\x\a-", path);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(899, 1)]
    [InlineData(900, 2)]
    [InlineData(1899, 2)]
    [InlineData(1900, 3)]
    [InlineData(2560, 3)]
    public void TheMatchesViewHasOneTwoOrThreeColumns(double width, int columns)
        => Assert.Equal(columns, ReplayBrowse.Columns(width));

    [Fact]
    public void UpToAMonthTheAgeIsRelative()
    {
        var reported = Now.AddDays(-30);
        Assert.Equal("recent", ReplayBrowse.AgeText(reported, Now, CultureInfo.GetCultureInfo("es"), _ => "recent"));
    }

    [Fact]
    public void PastAMonthItIsADate_PastAYearAMonthAndYear_InTheLaunchersLanguage()
    {
        var reported = new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
        var now = reported.AddDays(31);
        Assert.Equal("28 sep", ReplayBrowse.AgeText(reported, now, CultureInfo.GetCultureInfo("es"), _ => "x"));
        Assert.Equal("28 Sep", ReplayBrowse.AgeText(reported, now, CultureInfo.GetCultureInfo("en"), _ => "x"));

        var old = new DateTime(2025, 9, 28, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal("sep 2025", ReplayBrowse.AgeText(old, old.AddDays(366), CultureInfo.GetCultureInfo("es"), _ => "x"));
        Assert.Equal("Sep 2025", ReplayBrowse.AgeText(old, old.AddDays(366), CultureInfo.GetCultureInfo("en"), _ => "x"));
    }

    [Fact]
    public void AShortMonthIsThreeLettersWithNoPeriod()
    {
        Assert.Equal("sep", ReplayBrowse.ShortMonth(9, CultureInfo.GetCultureInfo("es")));
        Assert.Equal("may", ReplayBrowse.ShortMonth(5, CultureInfo.GetCultureInfo("es")));
        Assert.Equal("Sep", ReplayBrowse.ShortMonth(9, CultureInfo.GetCultureInfo("en")));
    }

    [Fact]
    public void MatchesAreGroupedByMonthInArrivalOrder_ThenOneGroupOlderThanAYear()
    {
        var dates = new DateTime?[]
        {
            new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc),
            null,
            new DateTime(2025, 9, 1, 12, 0, 0, DateTimeKind.Utc),
        };
        var groups = ReplayBrowse.GroupByMonth(Enumerable.Range(0, dates.Length), i => dates[i], Now);

        Assert.Equal(3, groups.Count);
        Assert.Equal((2026, 10), (groups[0].Year, groups[0].Month));
        Assert.Equal(new[] { 0, 1, 3 }, groups[0].Items);
        Assert.Equal((2026, 9), (groups[1].Year, groups[1].Month));
        Assert.True(groups[2].OlderThanAYear);
        Assert.Equal(new[] { 4 }, groups[2].Items);
    }

    [Fact]
    public void TheSizeHasOneDecimalInTheLaunchersLanguage()
    {
        Assert.Equal("1.4 MB", ReplayBrowse.SizeText(1_468_006, CultureInfo.GetCultureInfo("en")));
        Assert.Equal("1,4 MB", ReplayBrowse.SizeText(1_468_006, CultureInfo.GetCultureInfo("es")));
    }

    [Fact]
    public void TheMatchLineNamesBothSidesAndTheMap()
    {
        var players = MatchParticipantsView.Build(new List<MatchHistoryParticipant>
        {
            new() { UserId = "a", DisplayName = "Aluclown", Result = 1 },
            new() { UserId = "b", DisplayName = "Geaf_Argento", Result = 0 },
        }, null);
        Assert.Equal("Aluclown vs Geaf_Argento · ESOC Indonesia", ReplayBrowse.MatchLine(players, "ESOC_Indonesia", "vs"));
        Assert.Equal("Aluclown vs Geaf_Argento", ReplayBrowse.MatchLine(players, null, "vs"));
    }

    [Fact]
    public void ATeamGameNamesTheFirstPlayerOfEachSide()
    {
        var players = MatchParticipantsView.Build(new List<MatchHistoryParticipant>
        {
            new() { UserId = "a1", DisplayName = "A1", Team = 0, Result = 1 },
            new() { UserId = "a2", DisplayName = "A2", Team = 0, Result = 1 },
            new() { UserId = "b1", DisplayName = "B1", Team = 1, Result = 0 },
            new() { UserId = "b2", DisplayName = "B2", Team = 1, Result = 0 },
        }, null);
        Assert.Equal("A1 vs B1 · Texas", ReplayBrowse.MatchLine(players, "Texas", "vs"));
    }
}

/// <summary>Which recordings were downloaded and where: a cache of facts about files.</summary>
public class ReplayDownloadIndexTests
{
    [Fact]
    public void ARecordedDownloadIsFoundWhileItsFileExists_AndForgottenWhenItIsGone()
    {
        var dir = Path.Combine(Path.GetTempPath(), "replay-index-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "a.age3Yrec");
            File.WriteAllText(file, "x");
            var index = new ReplayDownloadIndex(Path.Combine(dir, "index.json"));
            index.Record("m1", file);
            Assert.True(index.TryGetDownloaded("m1", out var path));
            Assert.Equal(file, path);

            // A fresh reader of the same file sees it too: it was saved.
            Assert.True(new ReplayDownloadIndex(Path.Combine(dir, "index.json")).TryGetDownloaded("m1", out _));

            File.Delete(file);
            Assert.False(index.TryGetDownloaded("m1", out _));
            Assert.False(index.TryGetDownloaded("unknown", out _));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void ACorruptFileReadsAsEmpty()
    {
        Assert.Empty(ReplayDownloadIndex.Parse("{not json"));
        Assert.Empty(ReplayDownloadIndex.Parse(""));
        Assert.Empty(ReplayDownloadIndex.Parse(null));
    }

    [Fact]
    public void TheIndexKeepsTheNewestEntriesOnly()
    {
        var entries = Enumerable.Range(0, ReplayDownloadIndex.MaxEntries + 20)
            .Select(i => new ReplayDownloadIndex.Entry { MatchId = "m" + i, Path = "p", SavedAt = DateTime.UnixEpoch.AddMinutes(i) })
            .ToList();
        ReplayDownloadIndex.Trim(entries);
        Assert.Equal(ReplayDownloadIndex.MaxEntries, entries.Count);
        Assert.DoesNotContain(entries, e => e.MatchId == "m0");
        Assert.Contains(entries, e => e.MatchId == "m" + (ReplayDownloadIndex.MaxEntries + 19));
    }
}

/// <summary>The fields the community matches carry for their recording, as the server sends them.</summary>
public class ReplayWireContractTests
{
    [Fact]
    public void ACommunityMatchCarriesItsRecordingFields()
    {
        var m = JsonSerializer.Deserialize<CommunityMatch>(
            """{"id":"m1","mod_id":"wol","reported_at":"2026-10-09 18:00:00","has_replay":true,"replay_expires_at":"2027-10-09T18:00:00Z","replay_size_bytes":1468006,"participants":[]}""")!;
        Assert.True(m.HasReplay);
        Assert.Equal("2027-10-09T18:00:00Z", m.ReplayExpiresAt);
        Assert.Equal(1_468_006, m.ReplaySizeBytes);
    }

    [Fact]
    public void AnOlderServerSendsNoneOfThem()
    {
        var m = JsonSerializer.Deserialize<CommunityMatch>("""{"id":"m1","mod_id":"wol","reported_at":"x","participants":[]}""")!;
        Assert.Null(m.HasReplay);
        Assert.Null(m.ReplayExpiresAt);
        Assert.Null(m.ReplaySizeBytes);
    }

    [Fact]
    public void APageCarriesItsItemsCursorAndTotal()
    {
        var page = JsonSerializer.Deserialize<MatchBrowsePage>(
            """{"items":[{"id":"m1","mod_id":"wol","reported_at":"x","participants":[]}],"next_cursor":"abc","total":412}""")!;
        Assert.Single(page.Items);
        Assert.Equal("abc", page.NextCursor);
        Assert.Equal(412, page.Total);

        var last = JsonSerializer.Deserialize<MatchBrowsePage>("""{"items":[],"next_cursor":null}""")!;
        Assert.Null(last.NextCursor);
        Assert.Null(last.Total);
    }
}

/// <summary>
/// Ranking › Matches and the recording button on the real page. Each test names the failure it
/// is for; most would build clean and look fine in a screenshot of the wrong mode.
/// </summary>
[Collection("wpf-and-language")]
public class RankingMatchesTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static void Click(ButtonBase b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var d in Walk(child)) yield return d;
    }

    private static List<ReplayDownloadButton> ButtonsIn(DependencyObject root)
        => Walk(root).OfType<ReplayDownloadButton>().ToList();

    private static List<Border> TaggedBorders(DependencyObject root, string tag)
        => Walk(root).OfType<Border>().Where(b => Equals(b.Tag, tag)).ToList();

    /// <summary>
    /// THE ONE THAT MATTERS: the Matches button shows the view and puts the table and the match
    /// list away, and 1v1 brings them back — with both on screen the page would be the table with
    /// a second page drawn over it.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheMatchesButtonSwapsThePage()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            tab.ShowDemoElo("ranking");
            Assert.Equal(Visibility.Collapsed, tab.RankingMatchesView.Visibility);

            Click(tab.RankingModeMatches);
            Assert.Equal("active", tab.RankingModeMatches.Tag);
            Assert.Equal(Visibility.Visible, tab.RankingMatchesView.Visibility);
            Assert.Equal(Visibility.Collapsed, tab.RankingTableCard.Visibility);
            Assert.Equal(Visibility.Collapsed, tab.RankingHistoryCard.Visibility);
            Assert.Equal(Visibility.Collapsed, tab.RankingHighlightsView.Visibility);
            Assert.NotEmpty(TaggedBorders(tab.MatchesBody, MultiplayerTab.MatchesGroupTag));

            Click(tab.RankingModeSolo);
            Assert.Equal(Visibility.Visible, tab.RankingTableCard.Visibility);
            Assert.Equal(Visibility.Collapsed, tab.RankingMatchesView.Visibility);
        });
        Assert.Null(error);
    }

    /// <summary>
    /// Only recorded competitive matches carry a button; casual, undecided and unrecorded ones leave
    /// the space empty. The first page has thirty rows and fewer buttons than rows.
    /// </summary>
    [Fact]
    public void OnlyRecordedMatchesCarryAButton()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            tab.ShowDemoElo("rankingmatches");
            var page = ReplayDemoData.Matches(DateTime.UtcNow).Take(MultiplayerTab.MatchesPageSize).ToList();
            var expected = page.Count(m => ReplayBrowse.Decide(m, DateTime.UtcNow) != ReplayAvailability.None);
            var buttons = ButtonsIn(tab.MatchesBody);
            Assert.Equal(expected, buttons.Count);
            Assert.InRange(buttons.Count, 1, MultiplayerTab.MatchesPageSize - 1);
            Assert.All(buttons, b => Assert.Equal(ReplayButtonState.Idle, b.State));
        });
        Assert.Null(error);
    }

    /// <summary>
    /// "Load 30 more" brings the rest — and the last group, older than a year, whose rows say
    /// "expired" with no button to press; then the list says it has ended.
    /// </summary>
    [Fact]
    public void LoadingMoreReachesTheExpiredMatchesAndTheEnd()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            Strings.SetLanguage(Strings.LangEs);
            try
            {
                var tab = new MultiplayerTab();
                tab.ShowDemoElo("rankingmatches");
                var more = Walk(tab.MatchesBody).OfType<Button>().Single(b => Equals(b.Tag, MultiplayerTab.MatchesMoreTag));
                Assert.Equal("Cargar 30 más", more.Content);
                Click(more);

                Assert.DoesNotContain(Walk(tab.MatchesBody).OfType<Button>(), b => Equals(b.Tag, MultiplayerTab.MatchesMoreTag));
                Assert.Single(Walk(tab.MatchesBody).OfType<TextBlock>(), t => Equals(t.Tag, MultiplayerTab.MatchesEndTag));
                var expired = ButtonsIn(tab.MatchesBody).Where(b => b.State == ReplayButtonState.Expired).ToList();
                Assert.Equal(2, expired.Count);
                Assert.All(expired, b => Assert.Equal("caducada", (b.Content as TextBlock)?.Text));
                Assert.Contains(Walk(tab.MatchesBody).OfType<TextBlock>(), t => t.Text == "MÁS DE UN AÑO");
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// A search that finds nothing says so, names the search and the filter, and offers to clear
    /// them all — which brings the list back.
    /// </summary>
    [Fact]
    public void AnEmptySearchSaysSoAndClearingBringsTheListBack()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            Strings.SetLanguage(Strings.LangEn);
            try
            {
                var tab = new MultiplayerTab();
                tab.ShowDemoElo("rankingmatches");
                tab.MatchesReplayChip.IsChecked = true;
                Click(tab.MatchesReplayChip);
                typeof(MultiplayerTab).GetMethod("ApplyMatchesQuery", Private)!.Invoke(tab, new object?[] { "kaiserr" });

                var empty = TaggedBorders(tab.MatchesBody, MultiplayerTab.MatchesEmptyTag).Single();
                var texts = Walk(empty).OfType<TextBlock>().Select(t => t.Text).ToList();
                Assert.Contains("No matches with a replay for “kaiserr”", texts);
                var clear = Walk(empty).OfType<Button>().Single();
                Assert.Equal("Clear filters", clear.Content);

                Click(clear);
                Assert.Empty(TaggedBorders(tab.MatchesBody, MultiplayerTab.MatchesEmptyTag));
                Assert.NotEmpty(TaggedBorders(tab.MatchesBody, MultiplayerTab.MatchesGroupTag));
                Assert.False(tab.MatchesReplayChip.IsChecked);
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// The row takes the button only when asked: the Rooms card's row is exactly what it was, and
    /// in the Ranking row line 2 keeps to the content column so it never runs under the button.
    /// </summary>
    [Fact]
    public void TheRoomsRowHasNoButtonAndTheRankingRowKeepsLineTwoInItsColumn()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var m = ReplayDemoData.Matches(DateTime.UtcNow).First(x => x.HasReplay == true);

            var rooms = (Border)MultiplayerTab.BuildRankingMatchRow(m, null);
            Assert.Empty(ButtonsIn(rooms));
            var roomsLine2 = ((Grid)rooms.Child).Children.OfType<TextBlock>().Single(t => Grid.GetRow(t) == 1);
            Assert.Equal(2, Grid.GetColumnSpan(roomsLine2));

            var ranking = (Border)MultiplayerTab.BuildRankingMatchRow(m, null,
                replayCell: x => new ReplayDownloadButton(x.Id, "expired", _ => ("t", null)));
            Assert.Single(ButtonsIn(ranking));
            // The two-line block and, BESIDE it, the right column — never spanning the block's rows.
            var outer = (Grid)ranking.Child;
            var lines = outer.Children.OfType<Grid>().Single(g => Grid.GetColumn(g) == 0);
            var right = outer.Children.OfType<Grid>().Single(g => Grid.GetColumn(g) == 1);
            Assert.Single(ButtonsIn(right));
            var line2 = lines.Children.OfType<TextBlock>().Single(t => Grid.GetRow(t) == 1);
            Assert.Equal(1, Grid.GetColumnSpan(line2));

            // A cell that answers null (no recording) leaves the space empty.
            var none = (Border)MultiplayerTab.BuildRankingMatchRow(m, null, replayCell: _ => null);
            Assert.Empty(ButtonsIn(none));
        });
        Assert.Null(error);
    }

    /// <summary>«Latest matches» ends in a way to the whole year.</summary>
    [Fact]
    public void LatestMatchesEndsInALinkToTheMatchesView()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            Strings.SetLanguage(Strings.LangEs);
            try
            {
                var tab = new MultiplayerTab();
                tab.ShowDemoElo("ranking");
                Assert.Equal("Todas las partidas →", tab.RankingAllMatchesLink.Content);
                Click(tab.RankingAllMatchesLink);
                Assert.Equal(Visibility.Visible, tab.RankingMatchesView.Visibility);
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    /// <summary>Every state of the button builds with its own look and the right tooltip.</summary>
    [Fact]
    public void EveryButtonStateBuilds()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var asked = new List<ReplayButtonState>();
            var button = new ReplayDownloadButton("m1", "expired", s => { asked.Add(s); return ("title " + s, "body"); });
            Assert.Equal(ReplayButtonState.Idle, button.State);
            Assert.IsType<ToolTip>(button.ToolTip);

            button.SetState(ReplayButtonState.Downloading, 0.4);
            Assert.Equal(0.4, button.Progress);
            button.SetState(ReplayButtonState.Downloading, null);
            Assert.Null(button.Progress);

            button.SetState(ReplayButtonState.Done);
            button.SetState(ReplayButtonState.Error);
            Assert.Contains(ReplayButtonState.Done, asked);
            Assert.Contains(ReplayButtonState.Error, asked);

            button.SetState(ReplayButtonState.Expired);
            Assert.Null(button.ToolTip);
            Assert.Equal("expired", (button.Content as TextBlock)?.Text);
        });
        Assert.Null(error);
    }

    [Fact]
    public void TheRingIsAnArcUntilItIsFull()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            Assert.IsType<PathGeometry>(ReplayDownloadButton.ArcGeometry(0.4));
            Assert.IsType<EllipseGeometry>(ReplayDownloadButton.ArcGeometry(1));
            Assert.True(ReplayDownloadButton.ArcGeometry(0).IsEmpty());
        });
        Assert.Null(error);
    }

    /// <summary>
    /// The row stays the handoff's 50 px with the button in it: 8 above, the two lines' 34, and
    /// 7 + the hairline below. Spanning the right column over the two Auto rows made WPF give
    /// line 1 the spare height and the row grew to 57 — nothing failed, it just looked loose.
    /// </summary>
    [Fact]
    public void ARowWithAButtonIsAsTallAsOneWithout()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var m = ReplayDemoData.Matches(DateTime.UtcNow).First(x => x.HasReplay == true);
            var look = new MultiplayerTab.MatchRowLook(13, 11, 8);
            var with = (Border)MultiplayerTab.BuildRankingMatchRow(m, null, look: look,
                replayCell: x => new ReplayDownloadButton(x.Id, "expired", _ => ("t", null)));
            var without = (Border)MultiplayerTab.BuildRankingMatchRow(m, null, look: look, replayCell: _ => null);
            with.Measure(new Size(500, double.PositiveInfinity));
            without.Measure(new Size(500, double.PositiveInfinity));
            Assert.InRange(with.DesiredSize.Height, 49, 51);
            Assert.InRange(with.DesiredSize.Height - without.DesiredSize.Height, -0.5, 0.5);
        });
        Assert.Null(error);
    }

    /// <summary>The handoff's notice: the circle, the match line, the action and the close.</summary>
    [Fact]
    public void TheReplayNoticeBuildsWithItsMatchLineAndAction()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var card = AppToast.BuildToned(new AppToast.ToastOptions(
                "", "Replay saved", "Open it in AoE3", new[] { new AppToast.ToastAction("Show in folder", true, () => { }) },
                Tone: AppToast.ToastTone.Ok, Subtitle: "Aluclown vs Geaf_Argento · ESOC Indonesia"), AppToast.ToastTone.Ok, () => { });
            Assert.Equal(360, card.Width);
            var texts = Walk(card).OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains("Replay saved", texts);
            Assert.Contains("Aluclown vs Geaf_Argento · ESOC Indonesia", texts);
            Assert.Contains(Walk(card).OfType<Button>(), b => Equals(b.Content, "Show in folder"));
            Assert.Contains(Walk(card).OfType<Button>(), b => Equals(b.Tag, AppToast.ToastCloseTag));
        });
        Assert.Null(error);
    }
}
