using System;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// Keeping the community's matches current: refreshing when the server says a match changed
/// (<c>matches_changed</c>), and when a page is entered after it has gone stale.
///
/// <para>The complaint this answers: somebody finishes a match and nothing shows who won — not
/// the community block, not the ranking, not Ranking › Matches, not the history — until the
/// player goes to Library and back. The block refreshed at most once a minute and only on the
/// Rooms page with the window in front, the ranking page never by itself, and Ranking › Matches
/// and the history once per run. The server also memoised its answer for a minute more.</para>
///
/// <para>Two halves: the frame (needs a server that sends it) and refresh-on-entry (works with
/// any server). A page that is not on screen is only MARKED stale — fetching what nobody is
/// looking at would spend the per-IP budget that every launcher behind one address shares.</para>
/// </summary>
public partial class MultiplayerTab
{
    /// <summary>One refresh for every frame that lands inside the debounce.</summary>
    private DispatcherTimer? _matchesChangedTimer;

    /// <summary>Whether any frame inside the debounce named the viewer.</summary>
    private bool _matchesChangedIncludesMe;

    /// <summary>Ranking › Matches is older than what the server has: refresh on the next entry.</summary>
    private bool _matchesStale;

    /// <summary>When Ranking › Matches last fetched its first page.</summary>
    private DateTime _matchesFetchedUtc = DateTime.MinValue;

    /// <summary>The viewer's own history is older than what the server has.</summary>
    private bool _historyStale;

    /// <summary>When the history was last asked for, answered or not — a failure must not make
    /// every repaint of the profile ask again.</summary>
    private DateTime _historyFetchedUtc = DateTime.MinValue;

    /// <summary>How old Ranking › Matches and the history may get before entering them refreshes them.</summary>
    private static readonly TimeSpan EntryMaxAge = TimeSpan.FromSeconds(60);

    private void HandleMatchesChangedFrame(JsonElement json)
    {
        var users = MatchesChanged.UserIds(json);
        QueueMatchesChanged(MatchesChanged.IncludesViewer(users, _session?.CurrentUser?.Id));
    }

    /// <summary>
    /// Schedules one refresh. A frame that lands while one is waiting joins it rather than
    /// restarting the wait, so a burst of changes cannot postpone the refresh for ever.
    /// </summary>
    private void QueueMatchesChanged(bool includesMe)
    {
        _matchesChangedIncludesMe |= includesMe;
        if (_matchesChangedTimer is { IsEnabled: true }) return;

        var delay = MatchesChanged.DebounceMs + Random.Shared.Next(0, MatchesChanged.JitterMs);
        _matchesChangedTimer ??= new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
        _matchesChangedTimer.Interval = TimeSpan.FromMilliseconds(delay);
        _matchesChangedTimer.Tick -= MatchesChangedTimer_Tick;
        _matchesChangedTimer.Tick += MatchesChangedTimer_Tick;
        _matchesChangedTimer.Start();
    }

    private void MatchesChangedTimer_Tick(object? sender, EventArgs e)
    {
        _matchesChangedTimer?.Stop();
        var includesMe = _matchesChangedIncludesMe;
        _matchesChangedIncludesMe = false;
        try
        {
            RefreshAfterMatchesChanged(includesMe);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Refresh after matches_changed failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Marks every page built from the community's matches stale, then fetches the ones on screen.
    /// </summary>
    private void RefreshAfterMatchesChanged(bool includesMe)
    {
        if (_session?.Status != MultiplayerSession.SessionStatus.SignedIn) return;

        _activityFetchedUtc = DateTime.MinValue;
        _rankingLeadersFetchedUtc = DateTime.MinValue;
        MarkStatsStale();
        _matchesStale = true;
        if (includesMe)
        {
            _historyStale = true;
            // The viewer's own record and place moved. Dropped FIRST, for the reason
            // EnterResultPhase gives: a failed fetch then shows "I do not know", never the old
            // number beside a result that changed it.
            _cachedStanding = null;
            _ = LoadStandingAsync();
        }

        // The profile is its own window and can be open over any tab, or with this one hidden.
        if (includesMe && _profileWindow != null) _ = RefreshHistoryAsync();

        // Nothing else is on screen while the tab is hidden; the marks above make the next show
        // ask past the minute's window.
        if (!IsVisible) return;

        // The community payload: the Rooms block, the ranking table and its match list, and the
        // statistics totals all repaint from it when it lands.
        _ = RefreshActivityStripAsync();
        if (_activeSubtab == Subtab.Ranking && _rankingMode == RankingMode.Highlights)
            _ = RefreshRankingLeadersAsync();
        if (_activeSubtab == Subtab.Stats) RefreshStatsForMod();
        if (MatchesShowing && _matchesLoaded && !_matchesLoading
            && MatchesChanged.MayRedrawOpenList(MatchesScroll?.VerticalOffset ?? 0))
            _ = RefreshMatchesInPlaceAsync();
    }

    /// <summary>The statistics page's community figures, without dropping what is on screen.</summary>
    private void MarkStatsStale()
    {
        _statsCommunityFetchedUtc = DateTime.MinValue;
        _statsModsFetchedUtc = DateTime.MinValue;
        _civStatsFetchedUtc = DateTime.MinValue;
        _matchupsFetchedUtc = DateTime.MinValue;
        // Decks are what each player SAYS they carry, not anything a match changes.
    }

    /// <summary>
    /// Entering Ranking › Matches refreshes it when it is stale or over a minute old. Called from
    /// the ways INTO the view, never from its render, which runs on every repaint of the page.
    /// </summary>
    private void MaybeRefreshMatchesOnEntry()
    {
        if (_rankingMode != RankingMode.Matches) return;
        if (_eloPreview || _demoStats) return;
        if (!_matchesLoaded || _matchesLoading) return;   // the first load is the render's
        if (!_matchesStale && DateTime.UtcNow - _matchesFetchedUtc < EntryMaxAge) return;
        _ = RefreshMatchesInPlaceAsync();
        MatchesScroll?.ScrollToTop();
    }

    /// <summary>
    /// The first page again, with the filters on screen, replacing the list only when it arrives —
    /// the old one stays up meanwhile instead of flashing empty. A failure keeps the old list.
    /// </summary>
    private async Task RefreshMatchesInPlaceAsync()
    {
        var api = _session?.Api;
        if (api == null || _matchesLoading) return;
        var generation = ++_matchesGeneration;
        _matchesLoading = true;
        try
        {
            var page = await api.BrowseMatchesAsync(null, MatchesPageSize, MatchesQuery);
            if (generation != _matchesGeneration) return;
            _matchesStale = false;
            _matchesFetchedUtc = DateTime.UtcNow;
            ApplyMatchesPage(page, reset: true);
        }
        catch (Exception ex)
        {
            if (generation == _matchesGeneration)
                DiagnosticLog.Write($"Ranking matches refresh: {ex.Message} (keeping the list on screen)");
        }
        finally
        {
            if (generation == _matchesGeneration) _matchesLoading = false;
        }
    }

    /// <summary>
    /// Whether the profile should ask for the history again: never fetched, stale after a match of
    /// the viewer's, or over a minute old. Never while the rating preview has swapped its sample in.
    /// </summary>
    private bool HistoryNeedsFetch()
    {
        if (_isRefreshingHistory || _renderingPreviewProfile) return false;
        var age = DateTime.UtcNow - _historyFetchedUtc;
        // Nothing on screen yet: ask — but not again within seconds of a failed attempt. The fetch
        // repaints the profile when it ends, and that repaint lands right back here; without the
        // pause a server that keeps failing would be asked in a tight loop.
        if (_historyRows == null) return age >= HistoryRetryPause;
        return _historyStale || age >= EntryMaxAge;
    }

    /// <summary>The shortest gap between two attempts at a history that has never loaded.</summary>
    private static readonly TimeSpan HistoryRetryPause = TimeSpan.FromSeconds(10);
}
