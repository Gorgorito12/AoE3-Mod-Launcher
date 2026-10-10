using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// The Rooms page's layout — its compact geometry, how the column splits between the room list
/// and the community panel, and the room-code search (design handoff turns 36 and 38-39,
/// <c>docs/design_handoff_salas_laptop</c>).
///
/// <para>A separate partial file because it is one feature: everything here either answers
/// <see cref="SetCompactLayout"/>, decides the list/panel split, or is the search box's code
/// path, and keeping it apart is what lets a reader see the whole of it at once instead of
/// across a twenty-thousand-line code-behind.</para>
///
/// <para><b>What the compact switch does NOT govern.</b> The functional changes — the code
/// pasted into the search, the per-seat bars, the CASUAL tag, the ping colours, the Join look,
/// and since turns 38-39 the whole list/panel split — apply at every size. A small window shows
/// the same blocks as a big one; only geometry hangs off <see cref="_compactLayout"/>.</para>
/// </summary>
public partial class MultiplayerTab
{
    // ------------------------------------------------------------------------
    // The compact switch
    // ------------------------------------------------------------------------

    /// <summary>
    /// Whether the page draws its compact geometry. FALSE until <c>MainWindow</c> says
    /// otherwise, so a tab constructed on its own — every layout test in the suite — is the
    /// wide layout those tests were written against.
    /// </summary>
    private bool _compactLayout;

    /// <summary>Read by the tests, and by nothing else that could reach the field.</summary>
    internal bool IsCompactLayout => _compactLayout;

    /// <summary>
    /// Switch between the wide and the compact geometry. Idempotent; called by
    /// <c>MainWindow</c> whenever its own size crosses <see cref="CompactLayout"/>'s thresholds.
    ///
    /// <para>It changes the sub-bar's height and the room rows' style, and nothing else. Since
    /// design 57 the chat column follows the page's width (<see cref="ApplyChatWidth"/>), and
    /// since the maintainer's "one margin, one gap" rule the margins and gaps do too
    /// (<see cref="ApplyPageSpacing"/>): they follow the WIDTH alone, so a window that is compact
    /// only because it is short keeps the wide spacing.</para>
    /// </summary>
    internal void SetCompactLayout(bool compact)
    {
        if (compact == _compactLayout) return;

        _compactLayout = compact;
        DiagnosticLog.Write($"MultiplayerTab: {(compact ? "compact" : "wide")} layout");

        SubBar.SetResourceReference(HeightProperty, compact ? "MpSubBarHeightCompact" : "MpSubBarHeight");

        // The rows change STYLE with the layout, and the header strip's width just moved, so the
        // columns are re-resolved from scratch. _roomColumnsApplied first, for the reason its own
        // comment gives: a resolved set that matches the previous one would otherwise skip the
        // header rebuild. ApplyRoomColumns re-renders when it can measure; the explicit re-render
        // covers the case where it cannot (not laid out yet) so the rows never keep the old style.
        _roomColumnsApplied = false;
        ApplyRoomColumns();
        RerenderRoomsFromCache();
        QueueActivityLayout();
    }

    // ------------------------------------------------------------------------
    // The list / community-block split (designs 57b, 60 and 61)
    // ------------------------------------------------------------------------

    /// <summary>
    /// What the layout-storm line says about this tab — enough to tell, from a player's bundle,
    /// which page was on screen when the UI thread stopped resting.
    /// </summary>
    internal string DescribeForStormLog()
    {
        try
        {
            return $"mp {_activeSubtab}{(IsVisible ? "" : " (not shown)")}, {(_compactLayout ? "compact" : "wide")}, "
                   + $"rooms {RoomsListPanel?.Children.Count ?? 0}, activity {ActivityMode}, "
                   + $"chat rows {GlobalChatPanel?.Children.Count ?? 0}{(ChatFolded ? " (folded)" : "")}, "
                   + $"players rows {PlayersPanel?.Children.Count ?? 0}, room window {(_lobbyWindow == null ? "none" : _lobbyWindow.WindowState.ToString())}";
        }
        catch { return "mp ?"; }
    }

    /// <summary>The mode the last layout pass applied; read by the tests.</summary>
    internal RoomsActivityMode ActivityMode { get; private set; } = RoomsActivityMode.None;

    /// <summary>The cards' height the last pass applied (0 folded); read by the tests.</summary>
    internal double ActivityCardsHeight { get; private set; }

    private bool _activityLayoutQueued;
    private bool _activityLayoutHooked;

    /// <summary>Whether any of the three cards has something; without them the block can only fold.</summary>
    private bool _activityHasCards;

    /// <summary>The block's last measured sizes (design 61); null until measured.</summary>
    private double? _activityFoldedHeight;
    private double? _activityExpandedChrome;
    private double? _activityMinCards;

    /// <summary>61's page-following sizes as last applied; null until the first layout pass.</summary>
    private ActivityFluid? _activityFluid;

    /// <summary>Test seam: the page width the block's fluid sizes are computed from.</summary>
    internal double? ActivityPageWidthOverride { get; set; }

    /// <summary>The fluid sizes the builders use: the applied ones, or the page's own before the first pass.</summary>
    private ActivityFluid CurrentActivityFluid
        => _activityFluid ?? RoomsActivityLayout.Fluid(ActivityPageWidth, TextScale.CurrentFactor);

    private double ActivityPageWidth => ActivityPageWidthOverride ?? RoomsView?.ActualWidth ?? 0;

    /// <summary>
    /// Re-decide the split at Loaded priority, once per burst: a poll that re-renders the rooms,
    /// the block repainting and the window resizing all land within a frame of each other.
    /// </summary>
    private void QueueActivityLayout()
    {
        HookActivityLayout();
        PerfCounters.Increment("QueueActivityLayout");
        if (_activityLayoutQueued) return;
        _activityLayoutQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _activityLayoutQueued = false;
            ApplyActivityLayout();
        }));
    }

    /// <summary>
    /// What can change the answer: the column's size, the page's width (the fluid sizes), the
    /// lists' natural heights and the block's own height. Hooked once, lazily, so a tab that never
    /// shows the Rooms page pays nothing.
    /// </summary>
    private void HookActivityLayout()
    {
        if (_activityLayoutHooked || RoomsLeftColumn == null) return;
        _activityLayoutHooked = true;
        RoomsLeftColumn.SizeChanged += (_, _) => QueueActivityLayout();
        if (RoomsView != null) RoomsView.SizeChanged += (_, e) => { if (e.WidthChanged) QueueActivityLayout(); };
        ActivityRecentList.NaturalHeightChanged += (_, _) => QueueActivityLayout();
        ActivityRankingList.NaturalHeightChanged += (_, _) => QueueActivityLayout();
        ActivityStrip.SizeChanged += (_, _) => QueueActivityLayout();
        ActivityBlock.SizeChanged += (_, _) => QueueActivityLayout();
    }

    /// <summary>
    /// The block's one-line labels at the handoff's line heights. WPF gives a 14-px label a
    /// ~20-px line and a 10-px one ~14, where the mockup's CSS says <c>line-height: 1</c>; those
    /// few pixels per label are what the cards' rows are counted against. Set from the CURRENT
    /// font size, which already carries the launcher's text scale, and re-run on every layout
    /// pass, so a text-size change re-derives them.
    ///
    /// <para><see cref="TextBlock.LineHeightProperty"/> inherits, which is how setting it on a
    /// link button reaches the text its template generates.</para>
    ///
    /// <para>The two PEAK HOURS sentences are the other case: they WRAP, at the handoff's 1.3
    /// line height, and stop at two lines (turn 40) — the hours are the answer, and an ellipsis
    /// used to eat them.</para>
    /// </summary>
    private void TightenActivityLines()
    {
        static void Tight(FrameworkElement? e, double factor)
        {
            if (e == null) return;
            var size = e is TextBlock t ? t.FontSize : e is Control c ? c.FontSize : 0;
            if (!(size > 0)) return;
            TextBlock.SetLineStackingStrategy(e, LineStackingStrategy.BlockLineHeight);
            var height = Math.Round(size * factor, 1);
            if (!TextBlock.GetLineHeight(e).Equals(height)) TextBlock.SetLineHeight(e, height);
        }

        Tight(ActivityStripTitle, 1.2);
        Tight(ActivityToggle, 1.0);
        Tight(ActivityFactsMonthLink, 1.0);
        Tight(ActivityPeakTitle, 1.0);
        Tight(ActivityRecentTitle, 1.0);
        Tight(ActivityRecentSeeAll, 1.0);
        Tight(ActivityRankingTitle, 1.0);
        Tight(ActivityRankingSeeAll, 1.0);

        static void TwoLines(TextBlock? t)
        {
            if (t == null) return;
            Tight(t, 1.3);
            var max = TextBlock.GetLineHeight(t) * 2;
            if (!t.MaxHeight.Equals(max)) t.MaxHeight = max;
        }
        TwoLines(ActivityPeakLine);
        TwoLines(ActivityPeakSubtitle);
    }

    /// <summary>
    /// Apply 61's page-following sizes (<see cref="RoomsActivityLayout.Fluid"/>) to the block's
    /// fixed parts and, when they changed, rebuild the parts built in code — the facts and the
    /// two lists' rows. Returns whether anything changed, so a resize that moves nothing visible
    /// rebuilds nothing.
    /// </summary>
    private bool ApplyActivityFluid()
    {
        var fluid = RoomsActivityLayout.Fluid(ActivityPageWidth, TextScale.CurrentFactor);
        if (_activityFluid is { } applied && applied.Equals(fluid)) return false;
        _activityFluid = fluid;

        ActivityStripTitle.FontSize = fluid.TitleSize;
        ActivityPeakBars.MinHeight = fluid.PeakBarsMin;
        var gap = new Thickness(0, fluid.PeakGap, 0, 0);
        ActivityPeakBars.Margin = gap;
        ActivityPeakAxis.Margin = gap;
        ActivityPeakLine.Margin = gap;
        ActivityPeakSubtitle.Margin = gap;
        ActivityPeakLine.FontSize = fluid.PeakLineSize;
        ActivityPeakSubtitle.FontSize = fluid.PeakSubSize;
        EmptyTitleText.FontSize = fluid.EmptyTitleSize;
        EmptyBodyText.FontSize = fluid.EmptyBodySize;

        // The facts and the rows carry their sizes as local values: rebuild them from the cache.
        if (_communityStats != null) RenderActivityStrip();
        else RenderActivityFacts();
        return true;
    }

    /// <summary>The width an element's panel lays it out at, or infinity before that panel has one.</summary>
    private static double WidthInItsPanel(FrameworkElement element)
        => element.Parent is FrameworkElement { ActualWidth: > 0 } panel ? panel.ActualWidth : double.PositiveInfinity;

    /// <summary>
    /// Share the left column (<see cref="RoomsActivityLayout.Plan"/>, designs 57b and 61): the
    /// rooms keep their minimum, the community block sits ANCHORED AT THE BOTTOM — about a third of
    /// the column when it fits, shrunk toward its minimum when it does not, folded to its header
    /// line when even that does not — and the cards OVER the list's bottom when the player asked
    /// for them and they do not fit. The rooms row is always the star: the block is always at the
    /// bottom. Writes only what changes, so the layout pass it causes cannot ask for another.
    /// </summary>
    internal void ApplyActivityLayout()
    {
        if (RoomsLeftColumn == null || ActivityHost == null) return;
        PerfCounters.Increment("ApplyActivityLayout");
        HookActivityLayout();
        ApplyActivityFluid();
        TightenActivityLines();
        RememberActivityHeights();

        var hasActivity = ActivityStrip.Tag is true;
        var column = RoomsLeftColumn.ActualHeight;
        var gap = PageGap;
        var empty = RoomsEmptyState.Visibility == Visibility.Visible;

        // 1. What the rooms keep: the card's chrome (title, column headings, the list's padding,
        //    the rim) and four rows — or, with no rooms, the card's title and its notice.
        double roomsMin;
        if (empty)
        {
            // At the width their own panel gives them — the card's inner grid — never the column's.
            // The column is wider by the card's padding and rim, so the busy-hours sentence could
            // wrap differently out here: the plan then under-counted the notice, and the changed
            // DesiredSize invalidated the card's grid for one more layout pass every run. The
            // grid's ActualWidth, not a hand-computed one: layout rounding at fractional DPI
            // makes the arithmetic a sub-pixel off. Infinite height on purpose — DesiredSize is
            // clamped to the constraint.
            RoomsSectionHeader.Measure(new Size(WidthInItsPanel(RoomsSectionHeader), double.PositiveInfinity));
            RoomsEmptyState.Measure(new Size(WidthInItsPanel(RoomsEmptyState), double.PositiveInfinity));
            // The panel's padding and rim around its title and its notice.
            roomsMin = RoomsBlock.Padding.Top + RoomsBlock.Padding.Bottom
                       + RoomsBlock.BorderThickness.Top + RoomsBlock.BorderThickness.Bottom
                       + RoomsSectionHeader.DesiredSize.Height + RoomsEmptyState.DesiredSize.Height;
        }
        else
        {
            var chrome = Math.Max(0, RoomsBlock.ActualHeight - RoomsListScroll.ViewportHeight)
                         + RoomsListPanel.Margin.Top + RoomsListPanel.Margin.Bottom;
            var rowMin = TryFindResource(_compactLayout ? "MpRoomRowHeightCompact" : "MpRoomRowHeight") is double r ? r : 58;
            // A search with no matches is one line, which counts as a row here; an error line
            // likewise. What matters is that a short list never takes four rows' worth of space
            // from the block under it when it holds one.
            var rows = Math.Max(1, RoomsListPanel.Children.Count);
            roomsMin = RoomsActivityLayout.RoomsMinHeight(chrome, rows, rowMin);
        }

        var plan = RoomsActivityLayout.Plan(
            column, roomsMin, empty, hasActivity, _config?.RoomsActivityChoice, MeasuredActivitySizes(), gap);
        var mode = plan.Mode;
        // A block whose three cards are all empty has nothing to open: it is its header line.
        if (!_activityHasCards && mode is RoomsActivityMode.Fixed or RoomsActivityMode.Overlay)
            mode = RoomsActivityMode.Folded;
        ActivityMode = mode;

        var open = mode == RoomsActivityMode.Fixed;
        var overlay = mode == RoomsActivityMode.Overlay;
        ActivityCardsHeight = open || overlay ? plan.CardsHeight : 0;

        // The rooms row is ALWAYS the star (61): the block is always at the bottom, and an empty
        // list centres its notice in the space above it instead of shrinking to it.
        var star = new GridLength(1, GridUnitType.Star);
        if (!RoomsRow.Height.Equals(star)) RoomsRow.Height = star;
        if (!ActivityRow.Height.Equals(GridLength.Auto)) ActivityRow.Height = GridLength.Auto;

        PlaceActivityStrip(overlay);
        SetVisibility(ActivityHost, mode != RoomsActivityMode.None);
        // The gap above the block is MpPanelGapTop, the tab's G (ApplyPageSpacing).
        SetVisibility(ActivityStrip, open || overlay);
        SetVisibility(ActivityOverlayHost, overlay);
        var height = open || overlay ? plan.CardsHeight : double.NaN;
        if (!ActivityStrip.Height.Equals(height)) ActivityStrip.Height = height;

        ApplyActivityToggleCaption();
    }

    /// <summary>
    /// Remember the block's sizes as the last pass laid it out: folded, its whole height; open,
    /// what it adds to its cards (header line, padding, gap, rim) and the least the cards can be
    /// (<see cref="MeasureMinCards"/>). A larger text size changes all three, which is why none is
    /// a constant.
    /// </summary>
    private void RememberActivityHeights()
    {
        if (ActivityBlock == null || ActivityHost.Visibility != Visibility.Visible) return;
        var block = ActivityBlock.ActualHeight;
        if (!(block > 0)) return;
        if (ActivityMode is RoomsActivityMode.Folded or RoomsActivityMode.Overlay)
        {
            _activityFoldedHeight = block;
        }
        else if (ActivityMode == RoomsActivityMode.Fixed
                 && ReferenceEquals(ActivityStrip.Parent, ActivityBlockGrid)
                 && ActivityStrip.ActualHeight > 0)
        {
            _activityExpandedChrome = block - ActivityStrip.ActualHeight;
        }
        // The cards' minimum wherever they are drawn — in the block or laid over the list.
        if (ActivityMode is RoomsActivityMode.Fixed or RoomsActivityMode.Overlay
            && ActivityStrip.Visibility == Visibility.Visible
            && MeasureMinCards() is double min)
        {
            _activityMinCards = min;
        }
    }

    /// <summary>
    /// The least the three cards can be (61): the tallest of — the peak card with its bars at
    /// their minimum, the ranking card with all of its rows, and the matches card with three of
    /// them. Each is the cards row's height less what that card's flexible part takes, plus what
    /// that part needs. Null when nothing is laid out.
    /// </summary>
    private double? MeasureMinCards()
    {
        static bool Shown(UIElement e) => e.Visibility == Visibility.Visible;
        var strip = ActivityStrip.ActualHeight;
        if (!(strip > 0)) return null;
        double? min = null;
        void Need(double h) { if (h > 0 && double.IsFinite(h)) min = Math.Max(min ?? 0, Math.Ceiling(h)); }

        // The peak card at its smallest (bars at 44) is the floor whatever else is on show.
        if (Shown(ActivityPeakCard) && Shown(ActivityPeakBars) && ActivityPeakBars.ActualHeight > 0)
            Need(strip - ActivityPeakBars.ActualHeight + ActivityPeakBars.MinHeight);

        // The fifth player is the limit (the maintainer): with the ranking on show, the cards are
        // its card with all its rows — five on any real ladder — and the matches card shows the
        // whole rows that fit inside. A ladder of fewer is shorter, and the peak card's floor
        // above keeps the block from collapsing to it.
        if (Shown(ActivityMiddleCard) && Shown(ActivityRankingCard) && Shown(ActivityRankingList)
            && ActivityRankingList.ActualHeight > 0 && ActivityRankingList.NaturalHeight > 0)
        {
            Need(strip - ActivityRankingList.ActualHeight + ActivityRankingList.NaturalHeight);
            return min;
        }

        // No ranking (an empty ladder): the matches card with three rows, too.
        if (Shown(ActivityRecentCard) && ActivityRecentList.ActualHeight > 0)
        {
            var first = ActivityRecentList.Children.OfType<UIElement>().Take(3).ToList();
            var rows = first.Sum(c => c.DesiredSize.Height);
            if (rows > 0) Need(strip - ActivityRecentList.ActualHeight + rows);
        }
        return min;
    }

    /// <summary>The block's three sizes: measured where they have been, the design's until then.</summary>
    private ActivitySizes MeasuredActivitySizes() => new(
        _activityExpandedChrome ?? RoomsActivityLayout.ExpandedChrome,
        _activityMinCards ?? RoomsActivityLayout.EstimateMinCards(CurrentActivityFluid),
        _activityFoldedHeight ?? RoomsActivityLayout.FoldedHeight);

    /// <summary>
    /// The block's two shapes (61): open — padding 10/14/12, the cards under the header line — or
    /// folded, the header line alone. The header line itself is the same in both.
    /// </summary>
    /// <summary>
    /// Put the cards where the plan wants them: in the block, under its header line, or in the
    /// card laid over the list's bottom. One element moved between two parents, so the cards are
    /// never drawn twice and everything that fills them keeps a single target.
    /// </summary>
    private void PlaceActivityStrip(bool overlay)
    {
        if (overlay)
        {
            if (ReferenceEquals(ActivityOverlayCard.Child, ActivityStrip)) return;
            ActivityBlockGrid.Children.Remove(ActivityStrip);
            ActivityOverlayCard.Child = ActivityStrip;
        }
        else
        {
            if (ReferenceEquals(ActivityStrip.Parent, ActivityBlockGrid)) return;
            ActivityOverlayCard.Child = null;
            ActivityBlockGrid.Children.Add(ActivityStrip);
        }
    }

    private static void SetVisibility(UIElement e, bool visible)
    {
        var v = visible ? Visibility.Visible : Visibility.Collapsed;
        if (e.Visibility != v) e.Visibility = v;
    }

    /// <summary>
    /// The block's one link (61): "Hide ▾" while the cards are shown — open, or laid over the
    /// list — and "Show ▴" folded. Hidden when there are no cards to show.
    /// </summary>
    private void ApplyActivityToggleCaption()
    {
        if (ActivityToggle == null) return;
        var shown = ActivityMode is RoomsActivityMode.Fixed or RoomsActivityMode.Overlay;
        ActivityToggle.Content = shown
            ? Strings.Get("MpActivityHide") + " ▾"
            : Strings.Get("MpActivityShow") + " ▴";
        SetVisibility(ActivityToggle, _activityHasCards);
    }

    /// <summary>
    /// "Show ▴" folded and "Hide ▾" open. The choice is REMEMBERED, as the handoff asks, and it
    /// is the player's from then on: the default ("open when it fits") stops applying the first
    /// time it is pressed.
    /// </summary>
    private void ActivityToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_config == null) return;
        var open = ActivityMode is RoomsActivityMode.Fixed or RoomsActivityMode.Overlay;
        _config.RoomsActivityChoice = !open;
        try { _config.Save(); }
        catch (Exception ex) { DiagnosticLog.Write($"Activity toggle: config save failed: {ex.Message}"); }
        ApplyActivityLayout();
    }

    /// <summary>
    /// The viewer's local-hour histogram and the start of its busiest three-hour window, or
    /// false when there is none worth naming. Shared by the peak card and the compact bar, so
    /// the two can never name different hours.
    /// </summary>
    private static bool TryLocalPeak(CommunityStats? stats, out int[] local, out int peakStart)
    {
        local = Array.Empty<int>();
        peakStart = 0;
        var activity = stats?.Activity;
        if (activity == null) return false;

        var utc = new int[24];
        foreach (var h in activity.Hours)
            if (h.Hour >= 0 && h.Hour < 24) utc[h.Hour] = h.Count;

        local = CommunityStatsView.ToLocalHours(utc, TimeZoneInfo.Local.GetUtcOffset(DateTimeOffset.UtcNow));
        var peak = CommunityStatsView.PeakWindow(local, activity.Total);
        if (!peak.HasValue) return false;
        peakStart = peak.Value;
        return true;
    }

    // ------------------------------------------------------------------------
    // The search box doubles as the room-code field
    // ------------------------------------------------------------------------

    /// <summary>
    /// The room code in the search box, or null. Recomputed on every keystroke; the join row
    /// and Enter both read it, so they cannot disagree about whether what was typed is a code.
    /// </summary>
    private string? _roomCodeQuery;

    /// <summary>
    /// Enter joins a typed code; Escape clears the box. Enter with anything that is not a code
    /// does nothing — the list is already filtered as you type, so there is nothing to submit.
    /// </summary>
    private void RoomSearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape && !string.IsNullOrEmpty(RoomSearchBox.Text))
        {
            RoomSearchBox.Text = string.Empty;
            e.Handled = true;
            return;
        }
        if (e.Key != System.Windows.Input.Key.Return) return;
        e.Handled = true;
        if (_roomCodeQuery is { } code && !_offlineMode) SubmitRoomCode(code);
    }

    /// <summary>
    /// Joins by a room code. Delegates to the SAME path the deep link and the invite toast use,
    /// so a pasted code, a Discord link and an invite cannot diverge in what they check before
    /// letting you in.
    ///
    /// <para>The box is cleared straight away: the code is consumed, and leaving it there invites
    /// a second Enter that would resolve the room a second time.</para>
    /// </summary>
    private void SubmitRoomCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return;
        RoomSearchBox.Text = string.Empty;
        _ = JoinByLobbyIdAsync(code);
    }

    /// <summary>
    /// THE ONE renderer of the rooms list, used by both the network refresh and every local
    /// re-render (typing, sorting, a column change, the layout switching).
    ///
    /// <para>There used to be two, and they disagreed: the network path built rows without the
    /// search filter, so the 5-second poll quietly threw away whatever somebody had typed the
    /// moment any room changed — and with the room code now typed into that same box, the
    /// "Join room" row would have vanished under the player's cursor.</para>
    /// </summary>
    private void RenderRoomRows(IReadOnlyList<LobbySummary>? all)
    {
        RoomsListPanel.Children.Clear();
        _roomPingCells.Clear();
        _roomAgeCells.Clear();

        var rooms = all ?? (IReadOnlyList<LobbySummary>)Array.Empty<LobbySummary>();
        // Filter BEFORE sorting: the sort is stable, so filtering first keeps the surviving rooms
        // in exactly the order they would have had anyway.
        var ordered = ApplyRoomSort(RoomSearchFilter.Apply(rooms, _roomsQuery));

        // A listed room whose id IS the code is already on screen with its own Join; a second
        // "Join room" row for it would be the same door drawn twice.
        var code = _roomCodeQuery;
        if (code != null && ordered.Any(l => string.Equals(l.Id, code, StringComparison.OrdinalIgnoreCase)))
            code = null;

        if (code != null)
        {
            RoomsListPanel.Children.Add(BuildJoinCodeRow(code));
            // The error line shares this cell, top-aligned, and would draw over the row.
            RoomsErrorBox.Visibility = Visibility.Collapsed;
        }

        // The column headings only head rows: over 56a's notice they would head nothing.
        var noRooms = ordered.Count == 0 && code == null && rooms.Count == 0;
        SetVisibility(RoomsHeaderBand, !noRooms);

        if (ordered.Count == 0)
        {
            if (code != null)
            {
                RoomsEmptyState.Visibility = Visibility.Collapsed;
            }
            else if (rooms.Count == 0)
            {
                // 56a's compact notice: the list is only as tall as it, and the spare height goes
                // to the bottom of the column (RoomsActivityLayout.Plan).
                RefreshRoomsEmptyText();
                RoomsEmptyState.Visibility = Visibility.Visible;
            }
            else
            {
                // A search that matches nothing must SAY so. Without this the panel simply
                // renders empty, which is indistinguishable from "there are no rooms".
                RoomsEmptyState.Visibility = Visibility.Collapsed;
                RoomsListPanel.Children.Add(new TextBlock
                {
                    Text = Strings.Get("MpRoomsNoMatches"),
                    Foreground = (Brush)Application.Current.FindResource("MpTextFaint"),
                    FontSize = (double)Application.Current.FindResource("FontSizeCaption"),
                    Margin = new Thickness(13, 10, 13, 10),
                });
            }
            UpdateRoomsCount(0);
        }
        else
        {
            RoomsEmptyState.Visibility = Visibility.Collapsed;
            var idx = 0;
            foreach (var lobby in ordered)
                RoomsListPanel.Children.Add(BuildRoomCard(lobby, idx++));
            UpdateRoomsCount(ordered.Count);
        }

        // Rooms the filter hid were still SEEN: without this, clearing the search would flash
        // every one of them as "new". Added after the rows are built, so a genuinely new room in
        // this very render still flashes.
        foreach (var l in rooms)
            if (!string.IsNullOrEmpty(l.Id)) _knownRoomIds.Add(l.Id);

        // The column's split depends on how many rows there are now (57b keeps four).
        QueueActivityLayout();
    }

    /// <summary>
    /// "Join room SJMD9J6W" — the first row of the list when the search box holds a room code.
    /// Drawn whether or not the room is in the list: a private room never is, and that is the
    /// usual reason somebody has a code at all.
    /// </summary>
    private Border BuildJoinCodeRow(string code)
    {
        var card = new Border
        {
            Style = (Style)FindResource(_compactLayout ? "MpRoomCardCompact" : "MpRoomCard"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 6),
            BorderBrush = (Brush)Application.Current.FindResource("MpRoomJoinRim"),
            Tag = "JoinCodeRow",
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(7),
            Background = (Brush)Application.Current.FindResource("MpActionSoftBg"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
            Child = new TextBlock
            {
                Text = "",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 14,
                Foreground = (Brush)Application.Current.FindResource("MpActionText"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock
        {
            Foreground = (Brush)Application.Current.FindResource("MpTextPrimary"),
            FontSize = (double)Application.Current.FindResource(_compactLayout ? "MpRoomNameSizeCompact" : "FontSizeBodyStrong"),
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        // The localised template is split at {0} so the code can be monospaced inside the
        // sentence — two half-sentence keys could not be translated.
        var parts = Strings.Get("MpRoomsJoinCodeRow").Split(new[] { "{0}" }, 2, StringSplitOptions.None);
        if (parts[0].Length > 0) title.Inlines.Add(new System.Windows.Documents.Run(parts[0]));
        title.Inlines.Add(new System.Windows.Documents.Run(code) { FontFamily = new FontFamily("Consolas") });
        if (parts.Length > 1 && parts[1].Length > 0) title.Inlines.Add(new System.Windows.Documents.Run(parts[1]));
        text.Children.Add(title);
        text.Children.Add(new TextBlock
        {
            Text = Strings.Get("MpRoomsJoinCodeHint"),
            Foreground = (Brush)Application.Current.FindResource("MpTextFaint"),
            FontSize = (double)Application.Current.FindResource("MpLabelSize"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 2, 0, 0),
        });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var join = new Button
        {
            Style = (Style)Application.Current.FindResource(_offlineMode ? "MpRoomActionInert" : "MpRoomActionJoin"),
            // The same green Join, dot and all, as a listed room's (design 66); offline it is
            // the inert look with no dot, like a Join for a mod you do not have.
            Content = _offlineMode
                ? Strings.Get("MpRoomJoin")
                : RoomActionContent(Strings.Get("MpRoomJoin"), "MpRoomJoinDot"),
            MinWidth = 92,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
            IsEnabled = !_offlineMode,
            ToolTip = _offlineMode ? _offlineNeedsInternet : null,
        };
        join.Click += (_, _) => SubmitRoomCode(code);
        Grid.SetColumn(join, 2);
        grid.Children.Add(join);

        card.Child = grid;
        return card;
    }
}
