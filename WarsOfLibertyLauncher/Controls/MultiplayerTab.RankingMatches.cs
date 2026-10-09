using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// Ranking › Matches (design handoff 63, 63d-63f): every community match, newest first, loaded
/// as the reader scrolls, searchable by player, filterable to the ones with a recording and by mod, grouped
/// by month — and each competitive one with its recording button. It exists because a
/// recording is kept a YEAR and «Latest matches» shows thirty: without it most recordings could
/// never be found.
///
/// <para><b>Four more controls narrow and order it</b>: a period (24 hours / 7 / 30 days), the
/// room's kind (competitive / casual), "only with a winner", and newest or oldest first. All of
/// them run on the SERVER — the list is paged, so filtering what is loaded would lie about the
/// count — and they are only offered once a page says the server applies them
/// (<see cref="MatchBrowsePage.Filters"/>): an older server ignores the parameters and would hand
/// back the whole list under controls claiming otherwise. Newest first, the list is headed TODAY,
/// YESTERDAY and THIS WEEK before the months, which is what makes the newest ones findable at a
/// glance (<see cref="ReplayBrowse.GroupForBrowse"/>).</para>
///
/// <para><b>The next page loads by itself as the reader scrolls</b>, once the end of what is loaded
/// is less than one screen away (<see cref="ReplayBrowse.ShouldLoadMore"/>); a window the first page
/// does not fill asks for the next at once. This REPLACES the handoff's "Load 30 more" button, at
/// the maintainer's request: at 210 matches it was six clicks, each one a trip to the end of the
/// list. Two properties make it safe. The server pages by keyset (<c>GET /matches</c>), so a match
/// reported while somebody reads never appears twice or goes missing; and a page is APPENDED
/// (<see cref="AppendMatchesPage"/>) rather than redrawing the list, so nothing the reader is looking
/// at moves and a long list does not cost more with every page. One request is in flight at a time,
/// and <b>a failed page stops the loading until the reader presses Retry</b> — scrolling never
/// retries, or one failure would hammer a per-IP quota of 30 a minute.</para>
///
/// <para><b>A search waits 300 ms after the last keystroke</b>, and every request carries a
/// generation number: an answer to a query that has since changed is dropped, or a slow first
/// letter would paint over the results of the whole name.</para>
/// </summary>
public partial class MultiplayerTab
{
    /// <summary>One page of <c>GET /matches</c>; the next loads by itself as the reader scrolls.</summary>
    internal const int MatchesPageSize = 30;

    /// <summary>The space between two match cards side by side, and one above the other (64b).</summary>
    internal const double MatchTileColumnGap = 8;
    internal const double MatchTileRowGap = 6;

    /// <summary>The <c>Tag</c> of a month group's rows and of the empty state, for the tests.</summary>
    internal const string MatchesGroupTag = "MatchesGroup";
    internal const string MatchesEmptyTag = "MatchesEmpty";
    internal const string MatchesRetryTag = "MatchesRetry";
    internal const string MatchesLoadingMoreTag = "MatchesLoadingMore";
    internal const string MatchesEndTag = "MatchesEnd";

    private readonly List<CommunityMatch> _matchesItems = new();

    /// <summary>A heading on screen: its grid of cards, its count, and the note that states it.</summary>
    private sealed class DrawnMatchGroup
    {
        public required UniformGrid Grid { get; init; }
        public TextBlock? CountNote { get; init; }
        public int Count { get; set; }
    }

    /// <summary>
    /// What is drawn, so a page that arrives can be APPENDED under it: the headings by key, the
    /// heading of the last match drawn (an undated match on the next page joins it), how many
    /// matches are drawn, and the footer the page replaces.
    /// </summary>
    private readonly Dictionary<MatchGroupKey, DrawnMatchGroup> _matchesDrawn = new();
    private MatchGroupKey? _matchesLastDrawnKey;
    private int _matchesDrawnItems;
    private FrameworkElement? _matchesFooter;
    private bool _matchesAutoLoadQueued;
    private string? _matchesCursor;
    private int? _matchesTotal;
    private bool _matchesLoading;
    private bool _matchesLoaded;
    private int? _matchesFailure;
    private int _matchesGeneration;
    private string _matchesQuery = "";
    private bool _matchesReplayOnly;
    private string? _matchesModId;
    private MatchBrowseSort _matchesSort = MatchBrowseSort.Newest;
    private int? _matchesDays;
    private MatchBrowseKind _matchesKind = MatchBrowseKind.All;
    private bool _matchesDecidedOnly;

    /// <summary>Whether the last page said the server applies the four filters above.</summary>
    private bool _matchesFiltersSupported;
    private DispatcherTimer? _matchesSearchTimer;
    private bool _matchesFillingMods;
    private bool _matchesFillingFilters;
    private int _matchesDemoOffset;

    /// <summary>Whether what is loaded came from the preview's samples: a switch either way reloads.</summary>
    private bool? _matchesFromDemo;

    private bool MatchesShowing => _activeSubtab == Subtab.Ranking && _rankingMode == RankingMode.Matches;

    /// <summary>Everything the next request asks for.</summary>
    internal MatchBrowseQuery MatchesQuery => new()
    {
        Query = _matchesQuery,
        ReplayOnly = _matchesReplayOnly,
        ModId = _matchesModId,
        Sort = _matchesSort,
        Days = _matchesDays,
        Kind = _matchesKind,
        DecidedOnly = _matchesDecidedOnly,
    };

    private void RankingModeMatches_Click(object sender, RoutedEventArgs e)
    {
        _rankingMode = RankingMode.Matches;
        RenderRanking();
    }

    /// <summary>"All matches →" under «Latest matches»: the Matches view.</summary>
    private void RankingAllMatchesLink_Click(object sender, RoutedEventArgs e)
    {
        _rankingMode = RankingMode.Matches;
        RenderRanking();
    }

    /// <summary>Called by <see cref="RenderRanking"/> in Matches mode.</summary>
    private void RenderRankingMatches()
    {
        RankingTitleText.Text = Strings.Get("MpSubtabRanking");
        RankGuideLink.Content = "?  " + Strings.Get("MpGuideLink");
        RankingSubtitleText.Text = "";
        ApplyPreviewChip();

        ApplyMatchesLabels();
        FillMatchesMods();
        FillMatchesFilterCombos();
        ApplyMatchesFilterAvailability();

        var demo = _eloPreview || _demoStats;
        if (_matchesFromDemo != demo)
        {
            // Samples must never stay on screen once the preview is gone, nor the server's
            // matches under a preview that says nothing here came from a server.
            _matchesFromDemo = demo;
            _matchesItems.Clear();
            _matchesCursor = null;
            _matchesTotal = null;
            _matchesLoaded = false;
            _matchesLoading = false;
            _matchesGeneration++;
        }

        if (!_matchesLoaded && !_matchesLoading)
        {
            if (_eloPreview || _demoStats) LoadDemoMatches(reset: true);
            else _ = LoadMatchesAsync(reset: true);
        }
        PaintMatches();
    }

    /// <summary>The fixed words of the filter bar, re-read on a language change.</summary>
    private void ApplyMatchesLabels()
    {
        MatchesSearchPlaceholder.Text = Strings.Get("MpMatchesSearchPlaceholder");
        MatchesSearchPlaceholder.Visibility = string.IsNullOrEmpty(MatchesSearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        MatchesSearchClear.Visibility = string.IsNullOrEmpty(MatchesSearchBox.Text) ? Visibility.Collapsed : Visibility.Visible;
        MatchesSearchShell.BorderBrush = (Brush)Application.Current.FindResource(
            string.IsNullOrEmpty(MatchesSearchBox.Text) ? "MpMatchesFieldRim" : "MpAction");
        MatchesReplayChip.Content = Strings.Get("MpMatchesOnlyReplay");
        MatchesReplayChip.IsChecked = _matchesReplayOnly;
        MatchesDecidedChip.Content = Strings.Get("MpMatchesOnlyDecided");
        MatchesDecidedChip.IsChecked = _matchesDecidedOnly;
        MatchesDecidedChip.ToolTip = TooltipHelper.Wrap(Strings.Get("MpMatchesOnlyDecidedTip"));
        MatchesPeriodCombo.ToolTip = TooltipHelper.Wrap(Strings.Get("MpMatchesPeriodTip"));
        MatchesKindCombo.ToolTip = TooltipHelper.Wrap(Strings.Get("MpMatchesKindTip"));
        MatchesSortCombo.ToolTip = TooltipHelper.Wrap(Strings.Get("MpMatchesSortTip"));
        MatchesTopButton.ToolTip = TooltipHelper.Wrap(Strings.Get("MpMatchesBackToTop"));
        System.Windows.Automation.AutomationProperties.SetName(MatchesTopButton, Strings.Get("MpMatchesBackToTop"));
        RankingModeMatches.Content = Strings.Get("MpRankingModeMatches");
        RankingAllMatchesLink.Content = Strings.Get("MpRankHistoryAllMatches");
    }

    /// <summary>"All mods" and the mods the statistics picker offers, the chosen one kept.</summary>
    private void FillMatchesMods()
    {
        _matchesFillingMods = true;
        try
        {
            MatchesModCombo.Items.Clear();
            MatchesModCombo.Items.Add(new ComboBoxItem { Content = Strings.Get("MpMatchesAllMods"), Tag = null });
            var selected = 0;
            foreach (var profile in StatsModOptions())
            {
                if (profile.IsStockGame && string.IsNullOrWhiteSpace(GetInstallPath(profile))) continue;
                MatchesModCombo.Items.Add(new ComboBoxItem { Content = profile.DisplayName, Tag = profile.Id });
                if (string.Equals(profile.Id, _matchesModId, StringComparison.OrdinalIgnoreCase))
                    selected = MatchesModCombo.Items.Count - 1;
            }
            MatchesModCombo.SelectedIndex = selected;
        }
        finally
        {
            _matchesFillingMods = false;
        }
    }

    /// <summary>The period, kind and sort lists in the current language, the chosen entries kept.</summary>
    private void FillMatchesFilterCombos()
    {
        _matchesFillingFilters = true;
        try
        {
            Fill(MatchesPeriodCombo, new (string Key, object? Tag)[]
            {
                ("MpMatchesPeriodAny", null),
                ("MpMatchesPeriod1", 1),
                ("MpMatchesPeriod7", 7),
                ("MpMatchesPeriod30", 30),
            }, _matchesDays);
            Fill(MatchesKindCombo, new (string Key, object? Tag)[]
            {
                ("MpMatchesKindAll", MatchBrowseKind.All),
                ("MpMatchesKindCompetitive", MatchBrowseKind.Competitive),
                ("MpMatchesKindCasual", MatchBrowseKind.Casual),
            }, _matchesKind);
            Fill(MatchesSortCombo, new (string Key, object? Tag)[]
            {
                ("MpMatchesSortNewest", MatchBrowseSort.Newest),
                ("MpMatchesSortOldest", MatchBrowseSort.Oldest),
            }, _matchesSort);
        }
        finally
        {
            _matchesFillingFilters = false;
        }

        static void Fill(ComboBox combo, (string Key, object? Tag)[] entries, object? selected)
        {
            combo.Items.Clear();
            var index = 0;
            for (var i = 0; i < entries.Length; i++)
            {
                combo.Items.Add(new ComboBoxItem { Content = Strings.Get(entries[i].Key), Tag = entries[i].Tag });
                if (Equals(entries[i].Tag, selected)) index = i;
            }
            combo.SelectedIndex = index;
        }
    }

    /// <summary>
    /// Show the period, kind, sort and winner controls only when the server applies them. A
    /// server that stopped advertising them gets their state cleared too: it ignored them, so the
    /// list on screen is the unfiltered one and the controls must not claim otherwise.
    /// </summary>
    private void ApplyMatchesFilterAvailability()
    {
        if (!_matchesFiltersSupported
            && (_matchesSort != MatchBrowseSort.Newest || _matchesDays != null
                || _matchesKind != MatchBrowseKind.All || _matchesDecidedOnly))
        {
            _matchesSort = MatchBrowseSort.Newest;
            _matchesDays = null;
            _matchesKind = MatchBrowseKind.All;
            _matchesDecidedOnly = false;
            FillMatchesFilterCombos();
            MatchesDecidedChip.IsChecked = false;
        }
        var shown = _matchesFiltersSupported ? Visibility.Visible : Visibility.Collapsed;
        MatchesPeriodCombo.Visibility = shown;
        MatchesKindCombo.Visibility = shown;
        MatchesSortCombo.Visibility = shown;
        MatchesDecidedChip.Visibility = shown;
    }

    private void MatchesFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_matchesFillingFilters) return;
        var days = (MatchesPeriodCombo.SelectedItem as ComboBoxItem)?.Tag as int?;
        var kind = (MatchesKindCombo.SelectedItem as ComboBoxItem)?.Tag is MatchBrowseKind k ? k : MatchBrowseKind.All;
        var sort = (MatchesSortCombo.SelectedItem as ComboBoxItem)?.Tag is MatchBrowseSort o ? o : MatchBrowseSort.Newest;
        if (days == _matchesDays && kind == _matchesKind && sort == _matchesSort) return;
        _matchesDays = days;
        _matchesKind = kind;
        _matchesSort = sort;
        ReloadMatches();
    }

    private void MatchesDecidedChip_Click(object sender, RoutedEventArgs e)
    {
        _matchesDecidedOnly = MatchesDecidedChip.IsChecked == true;
        ReloadMatches();
    }

    private void MatchesSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyMatchesLabels();
        _matchesSearchTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _matchesSearchTimer.Stop();
        _matchesSearchTimer.Tick -= MatchesSearchTimer_Tick;
        _matchesSearchTimer.Tick += MatchesSearchTimer_Tick;
        _matchesSearchTimer.Start();
    }

    private void MatchesSearchTimer_Tick(object? sender, EventArgs e)
    {
        _matchesSearchTimer?.Stop();
        ApplyMatchesQuery(MatchesSearchBox.Text);
    }

    private void MatchesSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _matchesSearchTimer?.Stop();
            ApplyMatchesQuery(MatchesSearchBox.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && !string.IsNullOrEmpty(MatchesSearchBox.Text))
        {
            MatchesSearchBox.Text = "";
            e.Handled = true;
        }
    }

    private void MatchesSearchClear_Click(object sender, RoutedEventArgs e)
    {
        MatchesSearchBox.Text = "";
        _matchesSearchTimer?.Stop();
        ApplyMatchesQuery("");
        MatchesSearchBox.Focus();
    }

    private void MatchesReplayChip_Click(object sender, RoutedEventArgs e)
    {
        _matchesReplayOnly = MatchesReplayChip.IsChecked == true;
        ReloadMatches();
    }

    private void MatchesModCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_matchesFillingMods) return;
        var id = (MatchesModCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        if (string.Equals(id, _matchesModId, StringComparison.OrdinalIgnoreCase)) return;
        _matchesModId = id;
        ReloadMatches();
    }

    /// <summary>A query of one letter matches everybody, and the server ignores it the same way.</summary>
    private void ApplyMatchesQuery(string? text)
    {
        var query = (text ?? "").Trim();
        if (query.Length < 2) query = "";
        if (string.Equals(query, _matchesQuery, StringComparison.Ordinal)) return;
        _matchesQuery = query;
        ReloadMatches();
    }

    /// <summary>
    /// The empty state's "Clear filters": every filter back to its default. The ORDER is kept —
    /// it is a way of reading the list, not a filter on it, and nothing it does can empty it.
    /// </summary>
    private void ClearMatchesFilters()
    {
        _matchesQuery = "";
        _matchesReplayOnly = false;
        _matchesModId = null;
        _matchesDays = null;
        _matchesKind = MatchBrowseKind.All;
        _matchesDecidedOnly = false;
        _matchesSearchTimer?.Stop();
        MatchesSearchBox.Text = "";
        FillMatchesMods();
        FillMatchesFilterCombos();
        ApplyMatchesLabels();
        ReloadMatches();
    }

    /// <summary>
    /// A filter, the order or the search changed: start again from the first page, at the TOP —
    /// the old offset belongs to a different list.
    /// </summary>
    private void ReloadMatches()
    {
        if (_eloPreview || _demoStats) LoadDemoMatches(reset: true);
        else _ = LoadMatchesAsync(reset: true);
        if (MatchesShowing) PaintMatches();
        MatchesScroll?.ScrollToTop();
    }

    /// <summary>
    /// Fetch the first page (<paramref name="reset"/>) or the next one. An answer to a query
    /// that has changed since is dropped; a failure is kept so the view can say so, and does not
    /// make every repaint ask again.
    /// </summary>
    private async Task LoadMatchesAsync(bool reset)
    {
        var api = _session?.Api;
        if (api == null) return;
        if (!reset && (_matchesLoading || _matchesCursor == null)) return;

        var generation = reset ? ++_matchesGeneration : _matchesGeneration;
        if (reset)
        {
            _matchesItems.Clear();
            _matchesCursor = null;
            _matchesTotal = null;
        }
        _matchesLoading = true;
        _matchesFailure = null;
        if (MatchesShowing)
        {
            // A next page only changes the footer; redrawing every card for it would be the cost
            // the append exists to avoid.
            if (reset) PaintMatches();
            else RepaintMatchesFooter();
        }

        try
        {
            var page = await api.BrowseMatchesAsync(reset ? null : _matchesCursor, MatchesPageSize, MatchesQuery);
            if (generation != _matchesGeneration) return;
            ApplyMatchesPage(page, reset: false);
        }
        catch (LobbyApiException ex)
        {
            if (generation != _matchesGeneration) return;
            _matchesFailure = ex.Status;
            DiagnosticLog.Write($"Ranking matches: HTTP {ex.Status} {ex.Code}");
        }
        catch (Exception ex)
        {
            if (generation != _matchesGeneration) return;
            _matchesFailure = -1;
            DiagnosticLog.Write($"Ranking matches: {ex.Message}");
        }
        finally
        {
            if (generation == _matchesGeneration)
            {
                _matchesLoading = false;
                _matchesLoaded = true;
            }
        }
        // A success was drawn by ApplyMatchesPage; a failure is said in the footer, or in place of
        // the list when there is no list.
        if (generation == _matchesGeneration && MatchesShowing && _matchesFailure != null)
        {
            if (_matchesItems.Count == 0) PaintMatches();
            else RepaintMatchesFooter();
        }
    }

    /// <summary>Add a page and remember where the next one starts. Also the tests' way in.</summary>
    internal void ApplyMatchesPage(MatchBrowsePage page, bool reset)
    {
        if (reset)
        {
            _matchesItems.Clear();
            _matchesTotal = null;
        }
        // The same match twice would mean a page boundary moved; keep the first.
        var added = new List<CommunityMatch>();
        foreach (var m in page.Items)
            if (!_matchesItems.Any(x => string.Equals(x.Id, m.Id, StringComparison.Ordinal)))
            {
                _matchesItems.Add(m);
                added.Add(m);
            }
        _matchesCursor = page.NextCursor;
        if (page.Total is int total) _matchesTotal = total;
        var supported = MatchBrowseQuery.ServerApplies(page.Filters);
        if (supported != _matchesFiltersSupported)
        {
            _matchesFiltersSupported = supported;
            ApplyMatchesFilterAvailability();
        }
        _matchesLoaded = true;
        _matchesLoading = false;
        _matchesFailure = null;
        if (!MatchesShowing) return;
        if (CanAppendMatches(added.Count)) AppendMatchesPage(added);
        else PaintMatches();
    }

    /// <summary>
    /// Whether what is on screen is exactly the list less the page that just arrived — the only
    /// state an append is right for. Anything else (a reset, a view drawn while hidden, an empty
    /// list) is drawn whole.
    /// </summary>
    private bool CanAppendMatches(int added)
        => MatchesBody != null
           && _matchesFooter != null
           && _matchesDrawn.Count > 0
           && _matchesDrawnItems == _matchesItems.Count - added
           && MatchesBody.Children.Contains(_matchesFooter);

    /// <summary>
    /// The preview's pages, filtered and ordered here by the server's own rules
    /// (<see cref="MatchBrowseQuery.Apply"/>), and advertising the filters a current server does.
    /// </summary>
    private void LoadDemoMatches(bool reset)
    {
        if (reset) _matchesDemoOffset = 0;
        var now = DateTime.UtcNow;
        var list = MatchesQuery.Apply(ReplayDemoData.Matches(now), now).ToList();
        var items = list.Skip(_matchesDemoOffset).Take(MatchesPageSize).ToList();
        _matchesDemoOffset += items.Count;
        ApplyMatchesPage(new MatchBrowsePage
        {
            Items = items,
            NextCursor = _matchesDemoOffset < list.Count ? "demo" : null,
            Total = reset ? list.Count : null,
            Filters = MatchBrowseQuery.NewFilters.ToList(),
        }, reset);
    }

    private void LoadMoreMatches()
    {
        if (_eloPreview || _demoStats) LoadDemoMatches(reset: false);
        else _ = LoadMatchesAsync(reset: false);
    }

    /// <summary>The footer's Retry: the one way loading starts again after a page failed.</summary>
    private void RetryMatchesPage()
    {
        _matchesFailure = null;
        LoadMoreMatches();
    }

    /// <summary>
    /// The list moved, grew or was resized: show or hide "↑" and, when the end is near, ask for
    /// the next page. It fires on a change of extent too, which is what makes a window the first
    /// page does not fill ask again by itself. The work is posted rather than done here because
    /// this runs inside a layout pass, and a page drawn in the preview arrives synchronously.
    /// </summary>
    private void MatchesScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, MatchesScroll)) return;
        UpdateMatchesTopButton();
        if (_matchesAutoLoadQueued) return;
        _matchesAutoLoadQueued = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _matchesAutoLoadQueued = false;
            MaybeLoadMoreMatches();
        }), DispatcherPriority.Background);
    }

    /// <summary>Ask for the next page when <see cref="ReplayBrowse.ShouldLoadMore"/> says the end is near.</summary>
    private void MaybeLoadMoreMatches()
    {
        if (!MatchesShowing || MatchesScroll == null || _matchesItems.Count == 0) return;
        if (ReplayBrowse.ShouldLoadMore(MatchesScroll.ExtentHeight, MatchesScroll.ViewportHeight,
                MatchesScroll.VerticalOffset,
                hasMore: _matchesCursor != null,
                busy: _matchesLoading || !_matchesLoaded,
                failed: _matchesFailure != null))
            LoadMoreMatches();
    }

    /// <summary>"↑" once the reader is more than a screen down.</summary>
    private void UpdateMatchesTopButton()
    {
        if (MatchesTopButton == null || MatchesScroll == null) return;
        var show = MatchesShowing && MatchesScroll.ViewportHeight > 0
                   && MatchesScroll.VerticalOffset > MatchesScroll.ViewportHeight;
        MatchesTopButton.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void MatchesTopButton_Click(object sender, RoutedEventArgs e) => MatchesScroll.ScrollToTop();

    /// <summary>Draw the view from what is known: the counter, the groups, and what ends the list.</summary>
    private void PaintMatches()
    {
        if (MatchesBody == null) return;
        MatchesBody.Children.Clear();
        _matchesDrawn.Clear();
        _matchesLastDrawnKey = null;
        _matchesDrawnItems = 0;
        _matchesFooter = null;

        PaintMatchesCount();

        if (_matchesItems.Count == 0)
        {
            if (_matchesLoading || (!_matchesLoaded && _session?.Api != null))
                MatchesBody.Children.Add(LeadersNotice("MpMatchesLoading"));
            else if (_matchesFailure == 404)
                MatchesBody.Children.Add(LeadersNotice("MpMatchesUnavailable"));
            else if (_matchesFailure != null)
                MatchesBody.Children.Add(LeadersNotice("MpMatchesFailed"));
            else
                MatchesBody.Children.Add(BuildMatchesEmpty());
            return;
        }

        var now = DateTime.UtcNow;
        var columns = ReplayBrowse.Columns(MatchesWidth());
        var look = MatchesLook();
        var groups = ReplayBrowse.GroupForBrowse(_matchesItems, m => RoomAgeFormat.ParseCreatedUtc(m.ReportedAt), now,
            ascending: _matchesSort == MatchBrowseSort.Oldest);
        foreach (var group in groups)
            MatchesBody.Children.Add(BuildMatchesGroup(group, columns, look, now));
        _matchesLastDrawnKey = KeyOfLast(groups, _matchesItems[^1]);
        _matchesDrawnItems = _matchesItems.Count;

        _matchesFooter = BuildMatchesFooter();
        MatchesBody.Children.Add(_matchesFooter);
        _ = EnsureMatchesCivArtAsync();
    }

    /// <summary>
    /// Draw a page under the list already on screen: its matches join the headings already drawn
    /// where they belong, new headings go at the end, and only the footer is replaced. Nothing the
    /// reader is looking at is rebuilt, so the page does not jump and a long list does not cost
    /// more with every page.
    /// </summary>
    private void AppendMatchesPage(IReadOnlyList<CommunityMatch> added)
    {
        if (MatchesBody == null) return;
        var now = DateTime.UtcNow;
        var columns = ReplayBrowse.Columns(MatchesWidth());
        var look = MatchesLook();
        var groups = ReplayBrowse.GroupForBrowse(added, m => RoomAgeFormat.ParseCreatedUtc(m.ReportedAt), now,
            ascending: _matchesSort == MatchBrowseSort.Oldest, continueFrom: _matchesLastDrawnKey);

        if (_matchesFooter != null) MatchesBody.Children.Remove(_matchesFooter);
        foreach (var group in groups)
        {
            if (_matchesDrawn.TryGetValue(group.Key, out var drawn))
            {
                foreach (var m in group.Items) drawn.Grid.Children.Add(BuildMatchTile(m, look, now));
                drawn.Count += group.Items.Count;
                if (drawn.CountNote != null) drawn.CountNote.Text = MatchesCountCaption(drawn.Count);
            }
            else
            {
                MatchesBody.Children.Add(BuildMatchesGroup(group, columns, look, now));
            }
        }
        if (added.Count > 0) _matchesLastDrawnKey = KeyOfLast(groups, added[^1]);
        _matchesDrawnItems += added.Count;

        _matchesFooter = BuildMatchesFooter();
        MatchesBody.Children.Add(_matchesFooter);
        PaintMatchesCount();
        _ = EnsureMatchesCivArtAsync();
    }

    /// <summary>Replace the footer alone — loading, failed, or how many are shown — or draw the whole list when there is none.</summary>
    private void RepaintMatchesFooter()
    {
        if (MatchesBody == null) return;
        var index = _matchesFooter == null ? -1 : MatchesBody.Children.IndexOf(_matchesFooter);
        if (index < 0)
        {
            PaintMatches();
            return;
        }
        MatchesBody.Children.RemoveAt(index);
        _matchesFooter = BuildMatchesFooter();
        MatchesBody.Children.Insert(index, _matchesFooter);
    }

    private void PaintMatchesCount()
        => MatchesCountText.Text = _matchesTotal is int total ? MatchesCountCaption(total) : "";

    private static string MatchesCountCaption(int count)
        => count == 1 ? Strings.Get("MpMatchesCountOne") : Strings.Format("MpMatchesCount", count.ToString("N0", Strings.Culture));

    private MatchRowLook MatchesLook()
        => MatchRowLook.Ranking(_rankingFluid.MatchLineSize > 0 ? _rankingFluid.MatchLineSize : 13);

    /// <summary>The heading the last match went under: the group whose last item it is.</summary>
    private static MatchGroupKey? KeyOfLast(IReadOnlyList<MatchGroup<CommunityMatch>> groups, CommunityMatch last)
    {
        foreach (var g in groups)
            if (g.Items.Count > 0 && ReferenceEquals(g.Items[^1], last)) return g.Key;
        return groups.Count > 0 ? groups[^1].Key : null;
    }

    private double MatchesWidth() => MatchesWidthOverride ?? RankingPage?.ActualWidth ?? 0;

    /// <summary>Test seam: the width the Matches view is laid out at.</summary>
    internal double? MatchesWidthOverride { get; set; }

    /// <summary>Follow the page's width without rebuilding the rows.</summary>
    private void UpdateMatchesColumns()
    {
        if (_matchesDrawn.Count == 0) return;
        var columns = ReplayBrowse.Columns(MatchesWidth());
        foreach (var drawn in _matchesDrawn.Values)
            if (drawn.Grid.Columns != columns) drawn.Grid.Columns = columns;
    }

    /// <summary>
    /// One heading — today, yesterday, this week or a month ("OCTOBER 2026 · 9 matches") — over
    /// its rows in one, two or three columns, filled row by row. The group older than a year says
    /// why its recordings are gone.
    ///
    /// <para><b>Design 64b: every match is a card of its own, on the page's background, with no
    /// panel around the group</b> — 8 px between columns and 6 between rows. Rows packed inside
    /// one panel and split by a 7 % line read as one mass of text. The cards cost the names 24 px
    /// of width; the handoff accepts that.</para>
    /// </summary>
    private FrameworkElement BuildMatchesGroup(MatchGroup<CommunityMatch> group, int columns, MatchRowLook look, DateTime now)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 6, 4, 6) };
        var title = new TextBlock
        {
            Text = group.Kind switch
            {
                MatchGroupKind.Today => Strings.Get("MpMatchesGroupToday"),
                MatchGroupKind.Yesterday => Strings.Get("MpMatchesGroupYesterday"),
                MatchGroupKind.ThisWeek => Strings.Get("MpMatchesGroupThisWeek"),
                MatchGroupKind.OlderThanAYear => Strings.Get("MpMatchesOlderGroup"),
                _ => ReplayBrowse.MonthHeading(group.Year, group.Month, Strings.Culture),
            },
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.FindResource(group.OlderThanAYear ? "MpTextFaint" : "MpTextSecondary"),
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        title.SetResourceReference(TextBlock.FontSizeProperty, "MpHistoryMetaSize");
        head.Children.Add(title);
        var note = new TextBlock
        {
            Text = group.OlderThanAYear ? Strings.Get("MpMatchesOlderNote") : MatchesCountCaption(group.Items.Count),
            Foreground = (Brush)Application.Current.FindResource("MpTextFaint"),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        note.SetResourceReference(TextBlock.FontSizeProperty, "MpHistoryMetaSize");
        head.Children.Add(note);
        stack.Children.Add(head);

        // Half the gap on each side of every card and minus half around the grid: 8 between
        // columns, 6 between rows, and the outer cards flush with the heading's edges.
        var grid = new UniformGrid
        {
            Columns = columns,
            Margin = new Thickness(-MatchTileColumnGap / 2, -MatchTileRowGap / 2, -MatchTileColumnGap / 2, -MatchTileRowGap / 2),
        };
        foreach (var m in group.Items) grid.Children.Add(BuildMatchTile(m, look, now));
        _matchesDrawn[group.Key] = new DrawnMatchGroup
        {
            Grid = grid,
            CountNote = group.OlderThanAYear ? null : note,
            Count = group.Items.Count,
        };

        // No panel: the cards sit on the page. The wrapper stays so a group can be found.
        stack.Children.Add(new Border { Tag = MatchesGroupTag, Child = grid });
        return stack;
    }

    /// <summary>One match as a card of its own (64b), with its age, its mod and its recording button.</summary>
    private FrameworkElement BuildMatchTile(CommunityMatch m, MatchRowLook look, DateTime now)
    {
        var reported = RoomAgeFormat.ParseCreatedUtc(m.ReportedAt);
        var age = reported is DateTime r
            ? ReplayBrowse.AgeText(r, now, Strings.Culture, elapsed => AgoFrom(now - elapsed) ?? "")
            : null;
        var modName = ModRegistry.Find(m.ModId)?.DisplayName ?? m.ModId;
        var row = BuildRankingMatchRow(m, MatchVocabulary(m), look: look,
            replayCell: BuildReplayCell, ageText: age, modSuffix: modName, tile: true);
        return new Border
        {
            Child = row,
            Margin = new Thickness(MatchTileColumnGap / 2, MatchTileRowGap / 2, MatchTileColumnGap / 2, MatchTileRowGap / 2),
        };
    }

    /// <summary>
    /// Under the list, one of: the next page is on its way; it failed, with Retry; "Showing 30 of
    /// 412" while more remain; or the end of the list. The fixed height keeps the list from
    /// twitching as one state replaces another.
    /// </summary>
    private FrameworkElement BuildMatchesFooter()
    {
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            MinHeight = 34,
            Margin = new Thickness(0, 4, 0, 16),
        };

        if (_matchesCursor == null)
        {
            footer.Children.Add(FooterText(Strings.Get("MpMatchesEnd"), MatchesEndTag));
        }
        else if (_matchesFailure != null)
        {
            footer.Children.Add(FooterText(Strings.Get("MpMatchesFailedMore"), null));
            var retry = new Button
            {
                Tag = MatchesRetryTag,
                Content = Strings.Get("MpMatchesRetry"),
                Style = (Style)FindResource("MpMatchesMoreButton"),
                Margin = new Thickness(14, 0, 0, 0),
            };
            retry.Click += (_, _) => RetryMatchesPage();
            footer.Children.Add(retry);
        }
        else if (_matchesLoading)
        {
            footer.Children.Add(FooterText(Strings.Get("MpMatchesLoadingMore"), MatchesLoadingMoreTag));
        }
        else if (_matchesTotal is int total)
        {
            footer.Children.Add(FooterText(Strings.Format("MpMatchesShowing",
                _matchesItems.Count.ToString("N0", Strings.Culture), total.ToString("N0", Strings.Culture)), null));
        }
        return footer;

        static TextBlock FooterText(string text, string? tag)
        {
            var t = new TextBlock
            {
                Tag = tag,
                Text = text,
                Foreground = (Brush)Application.Current.FindResource("MpTextFaint"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            t.SetResourceReference(TextBlock.FontSizeProperty, "MpMetaSize");
            return t;
        }
    }

    /// <summary>
    /// Nothing matched: a title that names the search and the filter, a hint, and — when any
    /// filter is on — a way to clear them all (63f).
    /// </summary>
    private FrameworkElement BuildMatchesEmpty()
    {
        var query = MatchesQuery;
        var hasQuery = !string.IsNullOrEmpty(_matchesQuery);
        var filtered = query.IsFiltered;
        // The mod, a period, a kind or "with a winner": "no matches YET" would be false — there
        // are matches, just none of these.
        var narrowed = query.NarrowsBeyondTheSearch;

        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var title = new TextBlock
        {
            Text = hasQuery
                ? Strings.Format(_matchesReplayOnly ? "MpMatchesEmptyQueryReplay" : "MpMatchesEmptyQuery", _matchesQuery)
                : narrowed
                    ? Strings.Get("MpMatchesEmptyFiltered")
                    : Strings.Get(_matchesReplayOnly ? "MpMatchesEmptyReplay" : "MpMatchesEmpty"),
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.FindResource("MpTextHeading"),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.SetResourceReference(TextBlock.FontSizeProperty, "MpMatchesEmptyTitleSize");
        stack.Children.Add(title);

        if (hasQuery || narrowed)
        {
            var hint = new TextBlock
            {
                Text = Strings.Get(hasQuery
                    ? narrowed ? "MpMatchesEmptyHintQueryFilters"
                      : _matchesReplayOnly ? "MpMatchesEmptyHint" : "MpMatchesEmptyHintQuery"
                    : "MpMatchesEmptyHintFilters"),
                Foreground = (Brush)Application.Current.FindResource("MpTextFaint"),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 420,
                Margin = new Thickness(0, 10, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            hint.SetResourceReference(TextBlock.FontSizeProperty, "MpBodySize");
            stack.Children.Add(hint);
        }

        if (filtered)
        {
            var clear = new Button
            {
                Content = Strings.Get("MpMatchesClearFilters"),
                Style = (Style)FindResource("MpMatchesPillButton"),
                Background = (Brush)Application.Current.FindResource("MpMatchesEmptyActionBg"),
                Margin = new Thickness(0, 14, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            clear.Click += (_, _) => ClearMatchesFilters();
            stack.Children.Add(clear);
        }

        return new Border
        {
            Tag = MatchesEmptyTag,
            Child = stack,
            Padding = new Thickness(20, 46, 20, 46),
            CornerRadius = (CornerRadius)Application.Current.FindResource("RadiusLg"),
            Background = (Brush)Application.Current.FindResource("MpPanel"),
        };
    }

    /// <summary>The flags of the shown matches, read off each mod's files; repaint when something was learned.</summary>
    private async Task EnsureMatchesCivArtAsync()
    {
        try
        {
            var learned = false;
            foreach (var group in _matchesItems
                         .Where(m => !string.IsNullOrWhiteSpace(m.ModId))
                         .GroupBy(m => m.ModId, StringComparer.OrdinalIgnoreCase)
                         .ToList())
            {
                var civs = group.SelectMany(m => m.Participants).Select(p => p.Civ)
                    .Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c!).Distinct(StringComparer.Ordinal).ToList();
                if (civs.Count == 0) continue;
                var before = DeckCardNames.Peek(group.Key);
                await DeckCardNames.ResolveAsync(group.Key, GetInstallPath, Array.Empty<string>(), civs);
                var after = DeckCardNames.Peek(group.Key);
                if (after != null && !ReferenceEquals(before, after)) learned = true;
            }
            if (learned && MatchesShowing) PaintMatches();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Ranking matches civ art: {ex.Message}");
        }
    }
}
