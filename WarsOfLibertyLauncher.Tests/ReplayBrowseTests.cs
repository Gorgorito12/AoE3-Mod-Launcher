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
using System.Windows.Threading;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;
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

    /// <summary>A moment on a local calendar day relative to <see cref="Now"/>'s, in UTC.</summary>
    private static DateTime LocalDaysAgo(int days)
        => Now.ToLocalTime().Date.AddDays(-days).AddHours(12).ToUniversalTime();

    /// <summary>
    /// Newest first: TODAY, YESTERDAY and THIS WEEK lead, then one heading per month, then the
    /// year that is gone — in arrival order. An undated match joins the heading of the match
    /// before it rather than being dropped or called "today".
    /// </summary>
    [Fact]
    public void NewestFirst_TheRecentHeadingsLeadThenTheMonths_ThenTheYearThatIsGone()
    {
        var dates = new DateTime?[]
        {
            Now,
            LocalDaysAgo(1),
            LocalDaysAgo(4),
            null,
            LocalDaysAgo(20),
            LocalDaysAgo(50),
            Now.AddDays(-400),
        };
        var groups = ReplayBrowse.GroupForBrowse(Enumerable.Range(0, dates.Length), i => dates[i], Now, ascending: false);

        Assert.Equal(new[]
        {
            MatchGroupKind.Today, MatchGroupKind.Yesterday, MatchGroupKind.ThisWeek,
            MatchGroupKind.Month, MatchGroupKind.Month, MatchGroupKind.OlderThanAYear,
        }, groups.Select(g => g.Kind));
        Assert.Equal(new[] { 0 }, groups[0].Items);
        Assert.Equal(new[] { 1 }, groups[1].Items);
        Assert.Equal(new[] { 2, 3 }, groups[2].Items);
        var twenty = dates[4]!.Value.ToLocalTime();
        Assert.Equal((twenty.Year, twenty.Month), (groups[3].Year, groups[3].Month));
        Assert.Equal(new[] { 6 }, groups[5].Items);
    }

    /// <summary>The week ends after six days: the seventh is a month heading's again.</summary>
    [Fact]
    public void TheWeekHoldsTwoToSixDaysAgo()
    {
        var dates = new DateTime?[] { LocalDaysAgo(2), LocalDaysAgo(6), LocalDaysAgo(7) };
        var groups = ReplayBrowse.GroupForBrowse(Enumerable.Range(0, dates.Length), i => dates[i], Now, ascending: false);
        Assert.Equal(MatchGroupKind.ThisWeek, groups[0].Kind);
        Assert.Equal(new[] { 0, 1 }, groups[0].Items);
        Assert.Equal(MatchGroupKind.Month, groups[1].Kind);
    }

    /// <summary>
    /// Oldest first: months only, and the year that is gone comes FIRST because that is where
    /// those matches arrive. "Today" at the bottom of an oldest-first list would be noise.
    /// </summary>
    [Fact]
    public void OldestFirst_MonthsOnly_AndTheYearThatIsGoneComesFirst()
    {
        var dates = new DateTime?[] { Now.AddDays(-400), LocalDaysAgo(50), LocalDaysAgo(1), Now };
        var groups = ReplayBrowse.GroupForBrowse(Enumerable.Range(0, dates.Length), i => dates[i], Now, ascending: true);

        Assert.Equal(MatchGroupKind.OlderThanAYear, groups[0].Kind);
        Assert.Equal(new[] { 0 }, groups[0].Items);
        Assert.All(groups.Skip(1), g => Assert.Equal(MatchGroupKind.Month, g.Kind));
        Assert.Equal(new[] { 0, 1, 2, 3 }, groups.SelectMany(g => g.Items));
    }

    /// <summary>An undated match that leads the list joins the first heading; alone, it goes under "now".</summary>
    [Fact]
    public void AnUndatedMatchIsNeverDropped()
    {
        var dates = new DateTime?[] { null, LocalDaysAgo(1) };
        var groups = ReplayBrowse.GroupForBrowse(Enumerable.Range(0, dates.Length), i => dates[i], Now, ascending: false);
        Assert.Single(groups);
        Assert.Equal(MatchGroupKind.Yesterday, groups[0].Kind);
        Assert.Equal(new[] { 0, 1 }, groups[0].Items);

        var alone = ReplayBrowse.GroupForBrowse(new[] { 0 }, _ => null, Now, ascending: false);
        Assert.Equal(MatchGroupKind.Today, Assert.Single(alone).Kind);
    }

    /// <summary>
    /// The next page is asked for only when the end is less than a screen away — and never while
    /// one is in flight, after one failed, with nothing left, or before the view is laid out. The
    /// refusals are the point: each one is a request that must not go out.
    /// </summary>
    [Theory]
    [InlineData(2000, 500, 1100, true, false, false, true)]   // 400 left, less than a screen: load
    [InlineData(2000, 500, 900, true, false, false, false)]   // 600 left, just over a screen: not yet
    [InlineData(2000, 500, 0, true, false, false, false)]     // far from the end
    [InlineData(400, 500, 0, true, false, false, true)]       // shorter than the window: fill it
    [InlineData(2000, 500, 1500, false, false, false, false)] // nothing left to ask for
    [InlineData(2000, 500, 1500, true, true, false, false)]   // one already in flight
    [InlineData(2000, 500, 1500, true, false, true, false)]   // the last one failed: only Retry asks again
    [InlineData(2000, 0, 1500, true, false, false, false)]    // not laid out (hidden)
    public void TheNextPageIsAskedForOnlyNearTheEnd(double extent, double viewport, double offset,
        bool hasMore, bool busy, bool failed, bool expected)
    {
        Assert.Equal(expected, ReplayBrowse.ShouldLoadMore(extent, viewport, offset, hasMore, busy, failed));
    }

    /// <summary>
    /// THE ONE THAT MATTERS for the append: grouping a list one page at a time, each page continuing
    /// from the heading the last one ended under, draws exactly the headings and rows that grouping
    /// the whole list at once does — at every split, in both orders, with an undated match on the
    /// boundary. Otherwise the list would read differently depending on how it was scrolled.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AppendingAPageGroupsExactlyAsTheWholeList(bool ascending)
    {
        var dates = ReplayDemoData.Matches(Now).Select(m => RoomAgeFormat.ParseCreatedUtc(m.ReportedAt)).ToList();
        if (ascending) dates.Reverse();
        dates[10] = null;
        dates[25] = null;
        var items = Enumerable.Range(0, dates.Count).ToList();

        var whole = Flatten(ReplayBrowse.GroupForBrowse(items, i => dates[i], Now, ascending));
        for (var split = 1; split < items.Count; split++)
        {
            var first = ReplayBrowse.GroupForBrowse(items.Take(split), i => dates[i], Now, ascending);
            var lastKey = first.First(g => g.Items[^1] == split - 1).Key;
            var second = ReplayBrowse.GroupForBrowse(items.Skip(split), i => dates[i], Now, ascending, continueFrom: lastKey);

            var merged = new List<(MatchGroupKey Key, List<int> Items)>();
            foreach (var g in first.Concat(second))
            {
                var at = merged.FindIndex(x => x.Key == g.Key);
                if (at < 0) merged.Add((g.Key, g.Items.ToList()));
                else merged[at].Items.AddRange(g.Items);
            }
            Assert.Equal(whole, merged.Select(x => $"{x.Key}:{string.Join(",", x.Items)}").ToList());
        }

        static List<string> Flatten(IReadOnlyList<MatchGroup<int>> groups)
            => groups.Select(g => $"{g.Key}:{string.Join(",", g.Items)}").ToList();
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
    /// The two "See all" links on the Rooms page each open the view that continues their card:
    /// the ranking card the 1v1 table, the community-matches card the Matches view. Both used to
    /// switch subtab and keep whatever view was open last, so after a look at Matches the
    /// ranking card's link opened Matches — the two read as swapped. Each case starts from the
    /// OTHER view, which is what the old links failed.
    /// </summary>
    [Fact]
    public void EachSeeAllOpensTheViewThatContinuesItsCard()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            tab.ShowDemoElo("ranking");

            // A tab with no session (never Attached) does not redraw from a state pass, so the
            // page is drawn again the way the preview draws it — which keeps the view the link
            // chose, and is therefore what tells a link that names its view from one that does not.
            void Redraw() => tab.ShowDemoElo("ranking");

            Click(tab.RankingModeMatches);
            Click(tab.ActivityRankingSeeAll);
            Redraw();
            Assert.Equal("active", tab.RankingModeSolo.Tag);
            Assert.Equal(Visibility.Visible, tab.RankingTableCard.Visibility);
            Assert.Equal(Visibility.Collapsed, tab.RankingMatchesView.Visibility);

            Click(tab.RankingModeHighlights);
            Click(tab.ActivityRecentSeeAll);
            Redraw();
            Assert.Equal("active", tab.RankingModeMatches.Tag);
            Assert.Equal(Visibility.Visible, tab.RankingMatchesView.Visibility);
            Assert.Equal(Visibility.Collapsed, tab.RankingTableCard.Visibility);
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
    /// Lay the tab out at a size with no window, then let everything it queued run — the
    /// ScrollChanged handler posts its work, and a posted page draws more cards.
    /// </summary>
    private static void LayOut(FrameworkElement e, double width, double height)
    {
        for (var i = 0; i < 4; i++)
        {
            e.Measure(new Size(width, height));
            e.Arrange(new Rect(0, 0, width, height));
            e.UpdateLayout();
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
    }

    private static int Loaded(MultiplayerTab tab)
        => ((System.Collections.ICollection)typeof(MultiplayerTab).GetField("_matchesItems", Private)!.GetValue(tab)!).Count;

    /// <summary>
    /// THE ONE THAT MATTERS: scrolling to the end brings the rest with no button to press — the
    /// last group, older than a year, whose rows say "expired", and then the end of the list.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_ScrollingToTheEndLoadsTheRestWithoutAButton()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            Strings.SetLanguage(Strings.LangEs);
            try
            {
                var tab = new MultiplayerTab();
                tab.ShowDemoElo("rankingmatches");
                Assert.Equal(MultiplayerTab.MatchesPageSize, Loaded(tab));

                LayOut(tab, 1440, 600);
                tab.MatchesScroll.ScrollToEnd();
                LayOut(tab, 1440, 600);
                tab.MatchesScroll.ScrollToEnd();
                LayOut(tab, 1440, 600);

                Assert.Equal(ReplayDemoData.Matches(DateTime.UtcNow).Count, Loaded(tab));
                Assert.Single(Walk(tab.MatchesBody).OfType<TextBlock>(), t => Equals(t.Tag, MultiplayerTab.MatchesEndTag));
                Assert.DoesNotContain(Walk(tab.MatchesBody).OfType<Button>(), b => Equals(b.Tag, MultiplayerTab.MatchesRetryTag));
                var expired = ButtonsIn(tab.MatchesBody).Where(b => b.State == ReplayButtonState.Expired).ToList();
                Assert.Equal(2, expired.Count);
                Assert.All(expired, b => Assert.Equal("caducada", (b.Content as TextBlock)?.Text));
                Assert.Contains(Walk(tab.MatchesBody).OfType<TextBlock>(), t => t.Text == "MÁS DE UN AÑO");
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    /// <summary>A window the first page does not fill asks for the next at once, with no scrolling.</summary>
    [Fact]
    public void ATallWindowFillsItselfWithoutScrolling()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            tab.ShowDemoElo("rankingmatches");
            LayOut(tab, 1440, 4000);
            Assert.Equal(ReplayDemoData.Matches(DateTime.UtcNow).Count, Loaded(tab));
            Assert.Equal(0, tab.MatchesScroll.VerticalOffset);
        });
        Assert.Null(error);
    }

    /// <summary>
    /// A page is APPENDED: the cards already on screen are the same objects afterwards, the
    /// matches that continue a heading join its grid, and its count follows.
    /// </summary>
    [Fact]
    public void AppendingAPageKeepsTheCardsAlreadyOnScreen()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            tab.ShowDemoElo("rankingmatches");
            var before = Walk(tab.MatchesBody).OfType<UniformGrid>().SelectMany(g => g.Children.OfType<Border>()).ToList();
            Assert.Equal(MultiplayerTab.MatchesPageSize, before.Count);

            typeof(MultiplayerTab).GetMethod("LoadMoreMatches", Private)!.Invoke(tab, null);

            var after = Walk(tab.MatchesBody).OfType<UniformGrid>().SelectMany(g => g.Children.OfType<Border>()).ToList();
            Assert.Equal(ReplayDemoData.Matches(DateTime.UtcNow).Count, after.Count);
            Assert.All(before, card => Assert.Contains(card, after));
            Assert.Single(Walk(tab.MatchesBody).OfType<TextBlock>(), t => Equals(t.Tag, MultiplayerTab.MatchesEndTag));

            // Every heading's count says how many cards its grid holds.
            foreach (var group in TaggedBorders(tab.MatchesBody, MultiplayerTab.MatchesGroupTag))
            {
                var grid = (UniformGrid)group.Child;
                var head = (StackPanel)((StackPanel)group.Parent).Children[0];
                var note = ((TextBlock)head.Children[1]).Text;
                if (note.Any(char.IsDigit))
                    Assert.Equal(grid.Children.Count, int.Parse(new string(note.Where(char.IsDigit).ToArray())));
            }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// A page that failed stops the loading: the footer says so and offers Retry, and the list
    /// growing or moving does not ask again by itself. Retry does.
    /// </summary>
    [Fact]
    public void AFailedPageOffersARetryAndScrollingDoesNotRetry()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            Strings.SetLanguage(Strings.LangEn);
            try
            {
                var tab = new MultiplayerTab();
                tab.ShowDemoElo("rankingmatches");
                typeof(MultiplayerTab).GetField("_matchesFailure", Private)!.SetValue(tab, -1);
                typeof(MultiplayerTab).GetMethod("RepaintMatchesFooter", Private)!.Invoke(tab, null);

                LayOut(tab, 1440, 4000);
                Assert.Equal(MultiplayerTab.MatchesPageSize, Loaded(tab));
                var retry = Walk(tab.MatchesBody).OfType<Button>().Single(b => Equals(b.Tag, MultiplayerTab.MatchesRetryTag));
                Assert.Equal("Retry", retry.Content);
                Assert.Contains(Walk(tab.MatchesBody).OfType<TextBlock>(), t => t.Text == "Couldn't load more matches.");

                Click(retry);
                Assert.Equal(ReplayDemoData.Matches(DateTime.UtcNow).Count, Loaded(tab));
                Assert.DoesNotContain(Walk(tab.MatchesBody).OfType<Button>(), b => Equals(b.Tag, MultiplayerTab.MatchesRetryTag));
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// "↑" appears once the reader is more than a screen down and takes them back; a filter change
    /// starts the new list at the top, where the old offset would land somewhere arbitrary in it.
    /// </summary>
    [Fact]
    public void TheArrowAppearsDownTheListAndAFilterChangeGoesBackToTheTop()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            tab.ShowDemoElo("rankingmatches");
            LayOut(tab, 1440, 600);
            Assert.Equal(Visibility.Collapsed, tab.MatchesTopButton.Visibility);

            tab.MatchesScroll.ScrollToEnd();
            LayOut(tab, 1440, 600);
            tab.MatchesScroll.ScrollToEnd();
            LayOut(tab, 1440, 600);
            Assert.True(tab.MatchesScroll.VerticalOffset > tab.MatchesScroll.ViewportHeight);
            Assert.Equal(Visibility.Visible, tab.MatchesTopButton.Visibility);
            Assert.NotNull(tab.MatchesTopButton.ToolTip);

            Click(tab.MatchesTopButton);
            LayOut(tab, 1440, 600);
            Assert.Equal(0, tab.MatchesScroll.VerticalOffset);
            Assert.Equal(Visibility.Collapsed, tab.MatchesTopButton.Visibility);

            tab.MatchesScroll.ScrollToEnd();
            LayOut(tab, 1440, 600);
            Assert.True(tab.MatchesScroll.VerticalOffset > 0);
            tab.MatchesKindCombo.SelectedIndex = 1; // competitive
            LayOut(tab, 1440, 600);
            Assert.Equal(0, tab.MatchesScroll.VerticalOffset);
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

    private static int CountShown(MultiplayerTab tab)
        => int.Parse(new string(tab.MatchesCountText.Text.Where(char.IsDigit).ToArray()));

    private static int Expected(MultiplayerTab tab)
        => tab.MatchesQuery.Apply(ReplayDemoData.Matches(DateTime.UtcNow), DateTime.UtcNow).Count();

    /// <summary>
    /// THE ONE THAT MATTERS: the period, kind, sort and winner controls are offered only when a
    /// page says the server applies them. An older server ignores the parameters, so offering them
    /// there would draw the whole list under controls claiming to filter it — and any of them left
    /// set is cleared, because the list on screen IS the unfiltered one.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheNewControlsShowOnlyWhenTheServerAppliesThem()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            tab.ShowDemoElo("rankingmatches");
            Assert.Equal(Visibility.Visible, tab.MatchesPeriodCombo.Visibility);
            Assert.Equal(Visibility.Visible, tab.MatchesKindCombo.Visibility);
            Assert.Equal(Visibility.Visible, tab.MatchesSortCombo.Visibility);
            Assert.Equal(Visibility.Visible, tab.MatchesDecidedChip.Visibility);

            tab.MatchesKindCombo.SelectedIndex = 2;
            Assert.Equal(MatchBrowseKind.Casual, tab.MatchesQuery.Kind);

            // A page from a server that names no filters.
            tab.ApplyMatchesPage(new MatchBrowsePage { Items = ReplayDemoData.Matches(DateTime.UtcNow).Take(5).ToList() }, reset: true);
            Assert.Equal(Visibility.Collapsed, tab.MatchesPeriodCombo.Visibility);
            Assert.Equal(Visibility.Collapsed, tab.MatchesKindCombo.Visibility);
            Assert.Equal(Visibility.Collapsed, tab.MatchesSortCombo.Visibility);
            Assert.Equal(Visibility.Collapsed, tab.MatchesDecidedChip.Visibility);
            Assert.Equal(MatchBrowseKind.All, tab.MatchesQuery.Kind);
            Assert.Equal(0, tab.MatchesKindCombo.SelectedIndex);
        });
        Assert.Null(error);
    }

    /// <summary>
    /// Each control reloads from the first page and narrows the list exactly as the server would;
    /// the count says so. Oldest first opens on the year that is gone.
    /// </summary>
    [Fact]
    public void EachFilterNarrowsTheListAndOldestFirstOpensOnTheOldest()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            Strings.SetLanguage(Strings.LangEn);
            try
            {
                var tab = new MultiplayerTab();
                tab.ShowDemoElo("rankingmatches");
                var all = CountShown(tab);
                Assert.Equal(Expected(tab), all);

                tab.MatchesKindCombo.SelectedIndex = 1; // competitive
                Assert.Equal(Expected(tab), CountShown(tab));
                Assert.True(CountShown(tab) < all);

                tab.MatchesDecidedChip.IsChecked = true;
                Click(tab.MatchesDecidedChip);
                Assert.True(tab.MatchesQuery.DecidedOnly);
                Assert.Equal(Expected(tab), CountShown(tab));

                tab.MatchesPeriodCombo.SelectedIndex = 2; // 7 days
                Assert.Equal(7, tab.MatchesQuery.Days);
                Assert.Equal(Expected(tab), CountShown(tab));

                tab.MatchesPeriodCombo.SelectedIndex = 0;
                tab.MatchesKindCombo.SelectedIndex = 0;
                tab.MatchesDecidedChip.IsChecked = false;
                Click(tab.MatchesDecidedChip);
                Assert.Equal(all, CountShown(tab));

                // Newest first opens on a recent heading; oldest first on the year that is gone.
                var first = Walk(tab.MatchesBody).OfType<TextBlock>().First().Text;
                Assert.Contains(first, new[] { "TODAY", "YESTERDAY" });
                tab.MatchesSortCombo.SelectedIndex = 1;
                Assert.Equal(MatchBrowseSort.Oldest, tab.MatchesQuery.Sort);
                Assert.Equal("OLDER THAN ONE YEAR", Walk(tab.MatchesBody).OfType<TextBlock>().First().Text);
                Assert.DoesNotContain(Walk(tab.MatchesBody).OfType<TextBlock>(), t => t.Text == "TODAY");
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// Narrowed to nothing, the list does not say "no matches YET" — there are matches, just none
    /// of these — and "Clear filters" clears the new ones too while the order stays.
    /// </summary>
    [Fact]
    public void ANarrowedEmptyListSaysSoAndClearingKeepsTheOrder()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            Strings.SetLanguage(Strings.LangEs);
            try
            {
                var tab = new MultiplayerTab();
                tab.ShowDemoElo("rankingmatches");
                tab.MatchesSortCombo.SelectedIndex = 1;
                tab.MatchesKindCombo.SelectedIndex = 2; // casual
                tab.MatchesPeriodCombo.SelectedIndex = 1; // 24 hours
                Assert.Equal(0, Expected(tab));

                var empty = TaggedBorders(tab.MatchesBody, MultiplayerTab.MatchesEmptyTag).Single();
                var texts = Walk(empty).OfType<TextBlock>().Select(t => t.Text).ToList();
                Assert.Contains("Ninguna partida con estos filtros", texts);
                Assert.DoesNotContain("Todavía no hay partidas", texts);

                Click(Walk(empty).OfType<Button>().Single());
                Assert.Equal(MatchBrowseKind.All, tab.MatchesQuery.Kind);
                Assert.Null(tab.MatchesQuery.Days);
                Assert.Equal(MatchBrowseSort.Oldest, tab.MatchesQuery.Sort);
                Assert.Equal(1, tab.MatchesSortCombo.SelectedIndex);
                Assert.Equal(0, tab.MatchesKindCombo.SelectedIndex);
                Assert.NotEmpty(TaggedBorders(tab.MatchesBody, MultiplayerTab.MatchesGroupTag));
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
            foreach (var tile in new[] { false, true })
            {
                var with = (Border)MultiplayerTab.BuildRankingMatchRow(m, null, look: look,
                    replayCell: x => new ReplayDownloadButton(x.Id, "expired", _ => ("t", null)), tile: tile);
                var without = (Border)MultiplayerTab.BuildRankingMatchRow(m, null, look: look,
                    replayCell: _ => null, tile: tile);
                with.Measure(new Size(500, double.PositiveInfinity));
                without.Measure(new Size(500, double.PositiveInfinity));
                Assert.InRange(with.DesiredSize.Height, 49, 51);
                Assert.InRange(with.DesiredSize.Height - without.DesiredSize.Height, -0.5, 0.5);
            }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// THE ONE THAT MATTERS for design 64b: in Matches every match is a card of its own on the
    /// page — the fill, a 1-px rim, a radius of 6 — with no panel around the group, 8 px between
    /// columns and 6 between rows, and the outer cards flush with the group's edges. Rows packed in
    /// one panel and split by a faint line read as one mass of text, which is what 64 fixes.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_EachMatchInTheMatchesViewIsATile()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            tab.ShowDemoElo("rankingmatches");
            var fill = Application.Current.FindResource("MpMatchTileBg");
            var rim = Application.Current.FindResource("MpMatchTileRim");

            var groups = TaggedBorders(tab.MatchesBody, MultiplayerTab.MatchesGroupTag);
            Assert.NotEmpty(groups);
            foreach (var group in groups)
            {
                Assert.Null(group.Background);
                Assert.Equal(new Thickness(0), group.BorderThickness);
                var grid = Assert.IsType<UniformGrid>(group.Child);
                Assert.All(grid.Children.OfType<Border>(), wrapper =>
                {
                    var row = Assert.IsType<Border>(wrapper.Child);
                    Assert.Same(fill, row.Background);
                    Assert.Same(rim, row.BorderBrush);
                    Assert.Equal(new Thickness(1), row.BorderThickness);
                    Assert.Equal(new CornerRadius(6), row.CornerRadius);
                });
            }

            // The geometry, from where the cards actually land in two columns.
            var wide = groups.First(g => ((UniformGrid)g.Child).Children.Count >= 3);
            var cells = (UniformGrid)wide.Child;
            cells.Columns = 2;
            wide.Measure(new Size(1000, double.PositiveInfinity));
            wide.Arrange(new Rect(0, 0, 1000, wide.DesiredSize.Height));
            Border Card(int i) => (Border)((Border)cells.Children[i]).Child;
            Rect At(int i) => new(Card(i).TranslatePoint(new Point(0, 0), wide), Card(i).RenderSize);

            Assert.InRange(At(0).Left, -0.5, 0.5);
            Assert.InRange(At(1).Right, 999.5, 1000.5);
            Assert.InRange(At(1).Left - At(0).Right, 7.5, 8.5);
            Assert.InRange(At(2).Top - At(0).Bottom, 5.5, 6.5);
            Assert.InRange(At(0).Height, 49, 51);
        });
        Assert.Null(error);
    }

    /// <summary>
    /// The card is the Matches view's alone: «Latest matches» keeps its hairline rows, as the
    /// handoff draws them. A tile everywhere would be the opt-in leaking.
    /// </summary>
    [Fact]
    public void TheLatestMatchesPanelKeepsItsHairlineRows()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            tab.ShowDemoElo("ranking");
            // The rating preview's payload carries no recent matches; give it some with recordings.
            var stats = (CommunityStats)typeof(MultiplayerTab).GetField("_communityStats", Private)!.GetValue(tab)!;
            typeof(CommunityStats).GetProperty(nameof(CommunityStats.RecentMatches))!
                .SetValue(stats, ReplayDemoData.Matches(DateTime.UtcNow).Take(6).ToList());
            typeof(MultiplayerTab).GetMethod("RenderRankingHistory", Private)!.Invoke(tab, null);

            var rows = tab.RankingHistoryList.Children.OfType<Border>().ToList();
            Assert.Equal(6, rows.Count);
            Assert.NotEmpty(ButtonsIn(tab.RankingHistoryList));
            Assert.All(rows, row =>
            {
                Assert.Null(row.Background);
                Assert.Equal(new Thickness(0, 0, 0, 1), row.BorderThickness);
            });
        });
        Assert.Null(error);
    }

    /// <summary>
    /// The right column reaches 4 px into the row's bottom padding (the handoff's 38 px with a
    /// −4 margin), so the button ends 4 px above the row's edge and stands 4 px clear of the age
    /// — in both kinds of row, and with the row still 50 px.
    /// </summary>
    [Fact]
    public void TheButtonEndsFourPixelsAboveTheRowsBottom()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var m = ReplayDemoData.Matches(DateTime.UtcNow).First(x => x.HasReplay == true);
            var look = new MultiplayerTab.MatchRowLook(13, 11, 8);
            foreach (var tile in new[] { false, true })
            {
                var row = (Border)MultiplayerTab.BuildRankingMatchRow(m, null, look: look,
                    replayCell: x => new ReplayDownloadButton(x.Id, "expired", _ => ("t", null)), tile: tile);
                row.Measure(new Size(500, double.PositiveInfinity));
                row.Arrange(new Rect(0, 0, 500, row.DesiredSize.Height));
                var button = ButtonsIn(row).Single();
                var bottom = button.TranslatePoint(new Point(0, button.ActualHeight), row).Y;
                Assert.InRange(row.ActualHeight, 49, 51);
                Assert.InRange(row.ActualHeight - bottom, 3.5, 4.5);
            }
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
