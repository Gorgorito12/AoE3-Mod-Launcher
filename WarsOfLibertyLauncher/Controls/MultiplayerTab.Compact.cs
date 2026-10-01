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
    /// The wide layout's values, captured from the XAML the first time the layout goes compact,
    /// so going back restores exactly what the file says rather than a second copy of it typed
    /// in here. Null until the first switch.
    /// </summary>
    private WideGeometry? _wide;

    private sealed record WideGeometry(
        Thickness ContentMargin,
        GridLength Gutter,
        GridLength SideWidth,
        double SideMin,
        double SideMax,
        Thickness BannerMargin,
        Thickness SectionHeaderMargin,
        double SectionHeaderHeight,
        Thickness HeaderStripMargin,
        Thickness ListMargin,
        Thickness SubBarPadding);

    /// <summary>
    /// Switch between the wide and the compact geometry. Idempotent; called by
    /// <c>MainWindow</c> whenever its own size crosses <see cref="CompactLayout"/>'s thresholds.
    /// </summary>
    internal void SetCompactLayout(bool compact)
    {
        if (compact == _compactLayout) return;

        _wide ??= new WideGeometry(
            RoomsContentGrid.Margin,
            RoomsGutterColumn.Width,
            RoomsSideColumn.Width,
            RoomsSideColumn.MinWidth,
            RoomsSideColumn.MaxWidth,
            RadminBanner.Margin,
            RoomsSectionHeader.Margin,
            RoomsSectionHeader.Height,
            RoomsHeaderStrip.Margin,
            RoomsListPanel.Margin,
            SubBar.Padding);

        _compactLayout = compact;
        DiagnosticLog.Write($"MultiplayerTab: {(compact ? "compact" : "wide")} layout");

        if (compact)
        {
            // The handoff's 12-px padding and gap, and a fixed 300-px side panel. Geometry is
            // read with TryFindResource rather than a (double) cast on FindResource: the text-size
            // test treats every such cast as a FONT token, so a width read that way fails it.
            RoomsContentGrid.Margin = new Thickness(12);
            RoomsGutterColumn.Width = new GridLength(12);
            var side = TryFindResource("MpSidePanelWidthCompact") is double s ? s : 300;
            RoomsSideColumn.Width = new GridLength(side);
            RoomsSideColumn.MinWidth = 0;
            RoomsSideColumn.MaxWidth = double.PositiveInfinity;
            RadminBanner.Margin = new Thickness(12, 12, 12, 0);

            // The list's own header is one 24-px line flush with the column, and the rows sit
            // flush too: the inset that used to put their content under the column labels (the
            // list's 16 + the row's border and padding) is now ONLY the row's 1 + 12, so the
            // column header strip moves in to 13 to stay over its cells.
            RoomsSectionHeader.Margin = new Thickness(0);
            RoomsSectionHeader.Height = 24;
            RoomsHeaderStrip.Margin = new Thickness(13, 8, 13, 6);
            RoomsListPanel.Margin = new Thickness(0, 8, 0, 0);

            SubBar.SetResourceReference(HeightProperty, "MpSubBarHeightCompact");
            SubBar.Padding = new Thickness(12, 0, 12, 0);
        }
        else
        {
            RoomsContentGrid.Margin = _wide.ContentMargin;
            RoomsGutterColumn.Width = _wide.Gutter;
            RoomsSideColumn.Width = _wide.SideWidth;
            RoomsSideColumn.MinWidth = _wide.SideMin;
            RoomsSideColumn.MaxWidth = _wide.SideMax;
            RadminBanner.Margin = _wide.BannerMargin;
            RoomsSectionHeader.Margin = _wide.SectionHeaderMargin;
            RoomsSectionHeader.Height = _wide.SectionHeaderHeight;
            RoomsHeaderStrip.Margin = _wide.HeaderStripMargin;
            RoomsListPanel.Margin = _wide.ListMargin;
            SubBar.SetResourceReference(HeightProperty, "MpSubBarHeight");
            SubBar.Padding = _wide.SubBarPadding;
        }

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
    // The list / community-panel split (design handoff turns 38-39)
    // ------------------------------------------------------------------------

    /// <summary>The mode the last layout pass applied; read by the tests.</summary>
    internal RoomsActivityMode ActivityMode { get; private set; } = RoomsActivityMode.None;

    private bool _activityLayoutQueued;
    private bool _activityLayoutHooked;

    /// <summary>
    /// Re-decide the split at Loaded priority, once per burst: a poll that re-renders the rooms,
    /// the strip repainting and the window resizing all land within a frame of each other.
    /// </summary>
    private void QueueActivityLayout()
    {
        HookActivityLayout();
        if (_activityLayoutQueued) return;
        _activityLayoutQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _activityLayoutQueued = false;
            ApplyActivityLayout();
        }));
    }

    /// <summary>
    /// The three things that can change the answer: the column's size, the list's content
    /// (its extent), and the list's viewport. Hooked once, lazily, so a tab that never shows
    /// the Rooms page pays nothing.
    /// </summary>
    private void HookActivityLayout()
    {
        if (_activityLayoutHooked || RoomsLeftColumn == null || RoomsListScroll == null) return;
        _activityLayoutHooked = true;
        RoomsLeftColumn.SizeChanged += (_, _) => QueueActivityLayout();
        RoomsListScroll.ScrollChanged += (_, e) =>
        {
            if (e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0) QueueActivityLayout();
        };
        ActivityBarSegments.SizeChanged += (_, _) => FitActivityBar();
        ActivityPeakCard.SizeChanged += (_, e) =>
            ActivityPeakBars.Height = ActivityFit.PeakBarsHeight(e.NewSize.Height);
    }

    /// <summary>
    /// Decide and apply how the left column splits (<see cref="RoomsActivityLayout.Decide"/>),
    /// and which of the panel's two faces shows. Writes the rows only when they change, so the
    /// layout pass it causes cannot ask for another.
    /// </summary>
    /// <summary>
    /// The panel's one-line labels at the handoff's line heights. WPF gives a 14-px label a
    /// ~20-px line and a 10-px one ~14, where the mockup's CSS says <c>line-height: 1</c>; those
    /// few pixels per label are exactly the difference between the 248-px panel holding four
    /// matches, as 38b says it does, and three. Set from the CURRENT font size, which already
    /// carries the launcher's text scale, and re-run on every layout pass, so a text-size change
    /// (which resizes everything, and so lands here) re-derives them.
    ///
    /// <para><see cref="TextBlock.LineHeightProperty"/> inherits, which is how setting it on a
    /// link button reaches the text its template generates.</para>
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

        Tight(ActivityStripTitle, 1.0);
        Tight(ActivityHideButton, 1.0);
        Tight(ActivityPeakTitle, 1.0);
        Tight(ActivityRecentTitle, 1.0);
        Tight(ActivityRecentSeeAll, 1.0);
        Tight(ActivityRankingTitle, 1.0);
        Tight(ActivityRankingSeeAll, 1.0);
    }

    internal void ApplyActivityLayout()
    {
        if (RoomsLeftColumn == null || ActivityHost == null) return;
        HookActivityLayout();
        TightenActivityLines();

        var hasActivity = ActivityStrip.Tag is true;
        var column = RoomsLeftColumn.ActualHeight;
        var chrome = Math.Max(0, RoomsBlock.ActualHeight - RoomsListScroll.ViewportHeight);
        // The natural height is the same in every mode: in Fill the viewport IS the extent, in
        // the others the extent is what the rows would take unscrolled. That is what keeps a
        // switch from feeding back into the next decision.
        var natural = chrome + RoomsListScroll.ExtentHeight;
        var rowMin = TryFindResource(_compactLayout ? "MpRoomRowHeightCompact" : "MpRoomRowHeight") is double r ? r : 58;
        var minimum = chrome + 2 * (rowMin + 6);

        var mode = RoomsActivityLayout.Decide(column, natural, minimum, _config?.RoomsActivityChoice, hasActivity);
        ActivityMode = mode;

        GridLength rooms, activity;
        switch (mode)
        {
            case RoomsActivityMode.Fill:
                rooms = GridLength.Auto;
                activity = new GridLength(1, GridUnitType.Star);
                break;
            default:
                rooms = new GridLength(1, GridUnitType.Star);
                activity = GridLength.Auto;
                break;
        }
        if (!RoomsRow.Height.Equals(rooms)) RoomsRow.Height = rooms;
        if (!ActivityRow.Height.Equals(activity)) ActivityRow.Height = activity;

        var expanded = mode is RoomsActivityMode.Fill or RoomsActivityMode.Fixed;
        SetVisibility(ActivityHost, mode != RoomsActivityMode.None);
        SetVisibility(ActivityStrip, expanded);
        SetVisibility(ActivityBar, mode == RoomsActivityMode.Folded);
        var height = mode == RoomsActivityMode.Fixed ? RoomsActivityLayout.ExpandedHeight : double.NaN;
        if (!ActivityStrip.Height.Equals(height)) ActivityStrip.Height = height;

        ApplyActivityToggleCaption();
    }

    private static void SetVisibility(UIElement e, bool visible)
    {
        var v = visible ? Visibility.Visible : Visibility.Collapsed;
        if (e.Visibility != v) e.Visibility = v;
    }

    /// <summary>The fixed labels of both faces. Called from ApplyStrings.</summary>
    private void ApplyActivityBarStrings()
    {
        if (ActivityBarTitle != null) ActivityBarTitle.Text = Strings.Get("MpActivityBarTitle");
        ApplyActivityToggleCaption();
    }

    private void ApplyActivityToggleCaption()
    {
        if (ActivityToggle != null) ActivityToggle.Content = Strings.Get("MpActivityShow") + " ▴";
        if (ActivityHideButton != null) ActivityHideButton.Content = Strings.Get("MpActivityHide") + " ▾";
    }

    /// <summary>
    /// "Show activity ▴" on the folded strip and "Hide activity ▾" on the open panel's header.
    /// The choice is REMEMBERED, as the handoff asks, and it is the player's from then on: the
    /// default ("open when it fits") stops applying the first time either is pressed.
    /// </summary>
    private void ActivityToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_config == null) return;
        var open = ActivityMode is RoomsActivityMode.Fill or RoomsActivityMode.Fixed;
        _config.RoomsActivityChoice = !open;
        try { _config.Save(); }
        catch (Exception ex) { DiagnosticLog.Write($"Activity toggle: config save failed: {ex.Message}"); }
        ApplyActivityLayout();
    }

    // ------------------------------------------------------------------------
    // The folded strip (design handoff turn 39a)
    // ------------------------------------------------------------------------

    /// <summary>The strip's segments in order, each with its leading separator, for the fit.</summary>
    private readonly List<FrameworkElement> _barSegments = new();

    /// <summary>Indices into <see cref="_barSegments"/> in the order they leave when the strip is
    /// short: the matches count, then the last match. The peak never leaves.</summary>
    private readonly List<int> _barDropOrder = new();

    /// <summary>
    /// The 44-px line the folded panel shows: busiest hours with a mini histogram, the last
    /// community match, the match count — in the handoff's order, each present only when its
    /// data is.
    ///
    /// <para>Built from the same cached payload the open panel draws (<see cref="_communityStats"/>)
    /// and called at the end of <see cref="RenderActivityStrip"/>, so a language change and a
    /// poll repaint the two together and they can never disagree.</para>
    ///
    /// <para><b>No segment trims.</b> Turn 36 made the last match the one star column, and it
    /// came out "E…". Each segment is as wide as its content now, and when they do not all fit
    /// whole segments leave (<see cref="FitActivityBar"/>).</para>
    ///
    /// <para><b>Twenty-four bars, not the handoff's fourteen.</b> Bucketing the hours was
    /// proposed, built and rejected for the full card, and 24 does not map onto 14 anyway. Same
    /// footprint: 24 bars of 2 px with a 1-px gap is 71 px against the handoff's 68.</para>
    /// </summary>
    private void FillActivityBar()
    {
        if (ActivityBarSegments == null) return;

        ActivityBarSegments.Children.Clear();
        _barSegments.Clear();
        _barDropOrder.Clear();

        var stats = _communityStats;
        var muted = (Brush)Application.Current.FindResource("MpTextMuted");
        var size = (double)Application.Current.FindResource("MpLabelSize");

        int? matchIndex = null, countIndex = null;

        if (TryLocalPeak(stats, out var local, out var peakStart))
        {
            var peak = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            peak.Children.Add(BuildMiniHistogram(local, peakStart));
            var from = Strings.Format("MpActivityPeakHour", peakStart);
            var to = Strings.Format("MpActivityPeakHour", (peakStart + CommunityStatsView.PeakWindowHours) % 24);
            var line = BarText(muted, size);
            foreach (var run in BuildBarEmphasis(Strings.Get("MpActivityBarPeak"), from, to)) line.Inlines.Add(run);
            line.Margin = new Thickness(8, 0, 0, 0);
            peak.Children.Add(line);
            AddBarSegment(peak);
        }

        var recent = CommunityStatsView.RecentMatches(stats).FirstOrDefault();
        if (recent != null)
            matchIndex = AddBarSegment(BuildBarMatchSegment(recent, muted, size));

        var totals = CommunityStatsView.Totals(stats);
        if (totals != null)
        {
            var line = BarText(muted, size);
            foreach (var run in BuildBarEmphasis(
                         Strings.Get("MpActivityBarMatches"),
                         totals.Matches.ToString(),
                         totals.WindowDays.ToString()))
                line.Inlines.Add(run);
            countIndex = AddBarSegment(line);
        }

        if (countIndex is { } c) _barDropOrder.Add(c);
        if (matchIndex is { } m) _barDropOrder.Add(m);
        FitActivityBar();
    }

    /// <summary>Add one segment, with a 1×20 separator before it unless it is the first.</summary>
    private int AddBarSegment(FrameworkElement content)
    {
        var holder = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (_barSegments.Count > 0)
            holder.Children.Add(new Border
            {
                Width = 1,
                Height = 20,
                Margin = new Thickness(14, 0, 14, 0),
                Background = (Brush)Application.Current.FindResource("MpRimMedium"),
                VerticalAlignment = VerticalAlignment.Center,
            });
        holder.Children.Add(content);
        ActivityBarSegments.Children.Add(holder);
        _barSegments.Add(holder);
        return _barSegments.Count - 1;
    }

    /// <summary>
    /// Show the segments that fit whole (<see cref="ActivityFit.VisibleSegments"/>). Measured at
    /// INFINITE width, because a measure at the real width clamps DesiredSize to it and reports
    /// an overflow as a fit.
    /// </summary>
    private void FitActivityBar()
    {
        if (_barSegments.Count == 0 || ActivityBarSegments == null) return;
        var widths = new double[_barSegments.Count];
        for (var i = 0; i < widths.Length; i++)
        {
            var s = _barSegments[i];
            s.Visibility = Visibility.Visible;
            s.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            widths[i] = s.DesiredSize.Width;
        }
        // The COLUMN's width, never the panel's: a StackPanel wider than its slot is arranged at
        // its own desired width and clipped, so its ActualWidth reports the overflow as room.
        var available = ActivityBarSegments.Parent is Grid g && Grid.GetColumn(ActivityBarSegments) < g.ColumnDefinitions.Count
            ? g.ColumnDefinitions[Grid.GetColumn(ActivityBarSegments)].ActualWidth
            : ActivityBarSegments.ActualWidth;
        if (!(available > 0)) return;
        var shown = ActivityFit.VisibleSegments(available, widths, _barDropOrder);
        for (var i = 0; i < shown.Length; i++)
            _barSegments[i].Visibility = shown[i] ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Strip text: never trimmed — a segment that does not fit leaves whole.</summary>
    private static TextBlock BarText(Brush foreground, double size) => new()
    {
        Foreground = foreground,
        FontSize = size,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>
    /// The bar's figures, bold in the brighter rung — the handoff's "values in 600 #cdd9e9".
    /// A separate method from <c>BuildEmphasisRuns</c> only because that one paints the full
    /// strip's figures in the HEADING rung, which on a 44-px line would outshout the room list.
    /// </summary>
    private static IEnumerable<System.Windows.Documents.Run> BuildBarEmphasis(string template, params string[] values)
    {
        foreach (var run in BuildEmphasisRuns(template, values))
        {
            if (run.FontWeight == FontWeights.SemiBold)
                run.Foreground = (Brush)Application.Current.FindResource("MpTextSecondary");
            yield return run;
        }
    }

    /// <summary>
    /// "● Geaf_Argento beat aoe · 3 h ago" — one line, as wide as its content. The age is its
    /// own TextBlock and is registered in <see cref="_activityAgeCells"/>, which overwrites a
    /// cell's WHOLE text: a cell holding the names too would have them replaced on the next tick.
    /// </summary>
    private FrameworkElement BuildBarMatchSegment(CommunityMatch m, Brush muted, double size)
    {
        var line = CommunityStatsView.Describe(m);
        var players = MatchParticipantsView.Build(m.Participants, null);

        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 6,
            Height = 6,
            Fill = (Brush)Application.Current.FindResource(line.Decided ? "MpOk" : "MpMatchDotUndecided"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        });

        var names = BarText(muted, size);
        var strong = (Brush)Application.Current.FindResource("MpTextSecondary");
        if (line.Decided)
        {
            foreach (var run in BuildBarEmphasis(Strings.Get("MpActivityWon"), line.Winner ?? "", line.Loser ?? ""))
                names.Inlines.Add(run);
        }
        else
        {
            var separator = players.Count == 2 ? " " + Strings.Get("MpActivityVersus") + " " : " · ";
            for (var i = 0; i < players.Count; i++)
            {
                if (i > 0) names.Inlines.Add(new System.Windows.Documents.Run(separator));
                names.Inlines.Add(new System.Windows.Documents.Run(players[i].Name)
                {
                    FontWeight = FontWeights.SemiBold,
                    Foreground = strong,
                });
            }
        }
        row.Children.Add(names);

        var reportedUtc = RoomAgeFormat.ParseCreatedUtc(m.ReportedAt);
        if (reportedUtc.HasValue)
        {
            var dotSep = BarText(muted, size);
            dotSep.Text = " · ";
            row.Children.Add(dotSep);
            var age = BarText(muted, size);
            var elapsed = DateTime.UtcNow - reportedUtc.Value;
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
            age.Text = Strings.Format("MpActivityAgo", RoomAgeFormat.Coarse(elapsed));
            row.Children.Add(age);
            _activityAgeCells.Add((age, reportedUtc.Value));
        }
        return row;
    }

    /// <summary>
    /// The histogram at 18 px: one 2-px bar per hour, the peak window's hours solid, the rest
    /// the same blue at 40 % — the handoff's two tones. No tooltips: at 2 px a bar is not a
    /// target anybody can hold still on (the full card's tooltips already prove that).
    /// </summary>
    private FrameworkElement BuildMiniHistogram(int[] local, int peakStart)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Height = 18,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var max = local.Length == 0 ? 0 : local.Max();
        if (max <= 0) return panel;

        var accent = ((SolidColorBrush)Application.Current.FindResource("MpAction")).Color;
        var dim = new SolidColorBrush(Color.FromArgb(0x66, accent.R, accent.G, accent.B));
        dim.Freeze();
        var solid = (Brush)Application.Current.FindResource("MpAction");

        for (var h = 0; h < 24; h++)
        {
            var inPeak = false;
            for (var i = 0; i < CommunityStatsView.PeakWindowHours; i++)
                if ((peakStart + i) % 24 == h) { inPeak = true; break; }

            var frac = local[h] / (double)max;
            panel.Children.Add(new Border
            {
                Width = 2,
                Height = local[h] == 0 ? 1 : Math.Max(2, frac * 18),
                Margin = new Thickness(0, 0, h == 23 ? 0 : 1, 0),
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = inPeak ? solid : dim,
            });
        }
        return panel;
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

        if (ordered.Count == 0)
        {
            if (code != null)
            {
                RoomsEmptyState.Visibility = Visibility.Collapsed;
            }
            else if (rooms.Count == 0)
            {
                // One line, not a card: the activity strip below stays on screen, which is where
                // someone with no rooms to join actually has something to do.
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
                    Margin = _compactLayout ? new Thickness(13, 12, 13, 12) : new Thickness(30, 18, 30, 18),
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
            Content = Strings.Get("MpRoomJoin"),
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
