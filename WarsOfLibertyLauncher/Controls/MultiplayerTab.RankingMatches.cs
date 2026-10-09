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
/// Ranking › Matches (design handoff 63, 63d-63f): every community match, newest first, thirty
/// at a time, searchable by player, filterable to the ones with a recording and by mod, grouped
/// by month — and each competitive one with its recording button. It exists because a
/// recording is kept a YEAR and «Latest matches» shows thirty: without it most recordings could
/// never be found.
///
/// <para><b>"Load 30 more", never an infinite scroll</b> — the handoff's choice: simpler in WPF,
/// and the reader keeps their place. The server pages by keyset (<c>GET /matches</c>), so a
/// match reported while somebody reads never appears twice.</para>
///
/// <para><b>A search waits 300 ms after the last keystroke</b>, and every request carries a
/// generation number: an answer to a query that has since changed is dropped, or a slow first
/// letter would paint over the results of the whole name.</para>
/// </summary>
public partial class MultiplayerTab
{
    /// <summary>One page, as the handoff's button says ("Load 30 more").</summary>
    internal const int MatchesPageSize = 30;

    /// <summary>The <c>Tag</c> of a month group's panel and of the empty state, for the tests.</summary>
    internal const string MatchesGroupTag = "MatchesGroup";
    internal const string MatchesEmptyTag = "MatchesEmpty";
    internal const string MatchesMoreTag = "MatchesMore";
    internal const string MatchesEndTag = "MatchesEnd";

    private readonly List<CommunityMatch> _matchesItems = new();
    private readonly List<UniformGrid> _matchesGrids = new();
    private string? _matchesCursor;
    private int? _matchesTotal;
    private bool _matchesLoading;
    private bool _matchesLoaded;
    private int? _matchesFailure;
    private int _matchesGeneration;
    private string _matchesQuery = "";
    private bool _matchesReplayOnly;
    private string? _matchesModId;
    private DispatcherTimer? _matchesSearchTimer;
    private bool _matchesFillingMods;
    private int _matchesDemoOffset;

    /// <summary>Whether what is loaded came from the preview's samples: a switch either way reloads.</summary>
    private bool? _matchesFromDemo;

    private bool MatchesShowing => _activeSubtab == Subtab.Ranking && _rankingMode == RankingMode.Matches;

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

    /// <summary>The empty state's "Clear filters": every filter back to its default.</summary>
    private void ClearMatchesFilters()
    {
        _matchesQuery = "";
        _matchesReplayOnly = false;
        _matchesModId = null;
        _matchesSearchTimer?.Stop();
        MatchesSearchBox.Text = "";
        FillMatchesMods();
        ApplyMatchesLabels();
        ReloadMatches();
    }

    private void ReloadMatches()
    {
        if (_eloPreview || _demoStats) LoadDemoMatches(reset: true);
        else _ = LoadMatchesAsync(reset: true);
        if (MatchesShowing) PaintMatches();
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
        if (MatchesShowing) PaintMatches();

        try
        {
            var page = await api.BrowseMatchesAsync(reset ? null : _matchesCursor, MatchesPageSize,
                _matchesQuery, _matchesReplayOnly, _matchesModId);
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
        if (generation == _matchesGeneration && MatchesShowing) PaintMatches();
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
        foreach (var m in page.Items)
            if (!_matchesItems.Any(x => string.Equals(x.Id, m.Id, StringComparison.Ordinal)))
                _matchesItems.Add(m);
        _matchesCursor = page.NextCursor;
        if (page.Total is int total) _matchesTotal = total;
        _matchesLoaded = true;
        _matchesLoading = false;
        _matchesFailure = null;
        if (MatchesShowing) PaintMatches();
    }

    /// <summary>The preview's pages, filtered here the way the server filters them.</summary>
    private void LoadDemoMatches(bool reset)
    {
        if (reset) _matchesDemoOffset = 0;
        var now = DateTime.UtcNow;
        IEnumerable<CommunityMatch> all = ReplayDemoData.Matches(now);
        if (_matchesReplayOnly) all = all.Where(m => ReplayBrowse.Decide(m, now) == ReplayAvailability.Available);
        if (!string.IsNullOrEmpty(_matchesQuery))
            all = all.Where(m => m.Participants.Any(p =>
                p.DisplayName.Contains(_matchesQuery, StringComparison.OrdinalIgnoreCase)));
        if (!string.IsNullOrEmpty(_matchesModId))
            all = all.Where(m => string.Equals(m.ModId, _matchesModId, StringComparison.OrdinalIgnoreCase));
        var list = all.ToList();
        var items = list.Skip(_matchesDemoOffset).Take(MatchesPageSize).ToList();
        _matchesDemoOffset += items.Count;
        ApplyMatchesPage(new MatchBrowsePage
        {
            Items = items,
            NextCursor = _matchesDemoOffset < list.Count ? "demo" : null,
            Total = reset ? list.Count : null,
        }, reset);
    }

    private void LoadMoreMatches()
    {
        if (_eloPreview || _demoStats) LoadDemoMatches(reset: false);
        else _ = LoadMatchesAsync(reset: false);
    }

    /// <summary>Draw the view from what is known: the counter, the groups, and what ends the list.</summary>
    private void PaintMatches()
    {
        if (MatchesBody == null) return;
        MatchesBody.Children.Clear();
        _matchesGrids.Clear();

        MatchesCountText.Text = _matchesTotal is int total
            ? (total == 1 ? Strings.Get("MpMatchesCountOne") : Strings.Format("MpMatchesCount", total.ToString("N0", Strings.Culture)))
            : "";

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
        var look = MatchRowLook.Ranking(_rankingFluid.MatchLineSize > 0 ? _rankingFluid.MatchLineSize : 13);
        foreach (var group in ReplayBrowse.GroupByMonth(_matchesItems, m => RoomAgeFormat.ParseCreatedUtc(m.ReportedAt), now))
            MatchesBody.Children.Add(BuildMatchesGroup(group, columns, look, now));

        MatchesBody.Children.Add(BuildMatchesFooter());
        _ = EnsureMatchesCivArtAsync();
    }

    private double MatchesWidth() => MatchesWidthOverride ?? RankingPage?.ActualWidth ?? 0;

    /// <summary>Test seam: the width the Matches view is laid out at.</summary>
    internal double? MatchesWidthOverride { get; set; }

    /// <summary>Follow the page's width without rebuilding the rows.</summary>
    private void UpdateMatchesColumns()
    {
        if (_matchesGrids.Count == 0) return;
        var columns = ReplayBrowse.Columns(MatchesWidth());
        foreach (var grid in _matchesGrids)
            if (grid.Columns != columns) grid.Columns = columns;
    }

    /// <summary>
    /// One month: its heading ("OCTOBER 2026 · 9 matches") over a panel of rows in one, two or
    /// three columns, filled row by row. The last group, older than a year, says why its
    /// recordings are gone.
    /// </summary>
    private FrameworkElement BuildMatchesGroup(MatchMonthGroup<CommunityMatch> group, int columns, MatchRowLook look, DateTime now)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 6, 4, 6) };
        var title = new TextBlock
        {
            Text = group.OlderThanAYear
                ? Strings.Get("MpMatchesOlderGroup")
                : ReplayBrowse.MonthHeading(group.Year, group.Month, Strings.Culture),
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.FindResource(group.OlderThanAYear ? "MpTextFaint" : "MpTextSecondary"),
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        title.SetResourceReference(TextBlock.FontSizeProperty, "MpHistoryMetaSize");
        head.Children.Add(title);
        var note = new TextBlock
        {
            Text = group.OlderThanAYear
                ? Strings.Get("MpMatchesOlderNote")
                : group.Items.Count == 1
                    ? Strings.Get("MpMatchesCountOne")
                    : Strings.Format("MpMatchesCount", group.Items.Count.ToString("N0", Strings.Culture)),
            Foreground = (Brush)Application.Current.FindResource("MpTextFaint"),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        note.SetResourceReference(TextBlock.FontSizeProperty, "MpHistoryMetaSize");
        head.Children.Add(note);
        stack.Children.Add(head);

        var grid = new UniformGrid { Columns = columns, Margin = new Thickness(-7, 0, -7, 0) };
        foreach (var m in group.Items)
        {
            var reported = RoomAgeFormat.ParseCreatedUtc(m.ReportedAt);
            var age = reported is DateTime r
                ? ReplayBrowse.AgeText(r, now, Strings.Culture, elapsed => AgoFrom(now - elapsed) ?? "")
                : null;
            var modName = ModRegistry.Find(m.ModId)?.DisplayName ?? m.ModId;
            var row = BuildRankingMatchRow(m, MatchVocabulary(m), look: look,
                replayCell: BuildReplayCell, ageText: age, modSuffix: modName);
            grid.Children.Add(new Border { Child = row, Margin = new Thickness(7, 0, 7, 0) });
        }
        _matchesGrids.Add(grid);

        stack.Children.Add(new Border
        {
            Tag = MatchesGroupTag,
            Child = grid,
            Padding = new Thickness(16, 2, 16, 2),
            CornerRadius = (CornerRadius)Application.Current.FindResource("RadiusLg"),
            Background = (Brush)Application.Current.FindResource("MpPanel"),
            BorderBrush = (Brush)Application.Current.FindResource("MpMatchesGroupRim"),
            BorderThickness = new Thickness(1),
        });
        return stack;
    }

    /// <summary>"Load 30 more" with "Showing 30 of 412" beside it, or the end of the list.</summary>
    private FrameworkElement BuildMatchesFooter()
    {
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 16),
        };

        if (_matchesCursor != null)
        {
            var more = new Button
            {
                Tag = MatchesMoreTag,
                Content = Strings.Format("MpMatchesLoadMore", MatchesPageSize),
                Style = (Style)FindResource("MpMatchesMoreButton"),
                IsEnabled = !_matchesLoading,
            };
            more.Click += (_, _) => LoadMoreMatches();
            footer.Children.Add(more);
            if (_matchesTotal is int total)
            {
                var shown = new TextBlock
                {
                    Text = Strings.Format("MpMatchesShowing",
                        _matchesItems.Count.ToString("N0", Strings.Culture), total.ToString("N0", Strings.Culture)),
                    Foreground = (Brush)Application.Current.FindResource("MpTextFaint"),
                    Margin = new Thickness(14, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                shown.SetResourceReference(TextBlock.FontSizeProperty, "MpMetaSize");
                footer.Children.Add(shown);
            }
            if (_matchesFailure != null && _matchesFailure != 404)
                footer.Children.Add(new TextBlock
                {
                    Text = Strings.Get("MpMatchesFailed"),
                    Foreground = (Brush)Application.Current.FindResource("MpTextFaint"),
                    Margin = new Thickness(14, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                });
        }
        else
        {
            var end = new TextBlock
            {
                Tag = MatchesEndTag,
                Text = Strings.Get("MpMatchesEnd"),
                Foreground = (Brush)Application.Current.FindResource("MpTextFaint"),
            };
            end.SetResourceReference(TextBlock.FontSizeProperty, "MpMetaSize");
            footer.Children.Add(end);
        }
        return footer;
    }

    /// <summary>
    /// Nothing matched: a title that names the search and the filter, a hint, and — when any
    /// filter is on — a way to clear them all (63f).
    /// </summary>
    private FrameworkElement BuildMatchesEmpty()
    {
        var hasQuery = !string.IsNullOrEmpty(_matchesQuery);
        var filtered = hasQuery || _matchesReplayOnly || !string.IsNullOrEmpty(_matchesModId);

        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var title = new TextBlock
        {
            Text = hasQuery
                ? Strings.Format(_matchesReplayOnly ? "MpMatchesEmptyQueryReplay" : "MpMatchesEmptyQuery", _matchesQuery)
                : Strings.Get(_matchesReplayOnly ? "MpMatchesEmptyReplay" : "MpMatchesEmpty"),
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.FindResource("MpTextHeading"),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.SetResourceReference(TextBlock.FontSizeProperty, "MpMatchesEmptyTitleSize");
        stack.Children.Add(title);

        if (hasQuery)
        {
            var hint = new TextBlock
            {
                Text = Strings.Get(_matchesReplayOnly ? "MpMatchesEmptyHint" : "MpMatchesEmptyHintQuery"),
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
