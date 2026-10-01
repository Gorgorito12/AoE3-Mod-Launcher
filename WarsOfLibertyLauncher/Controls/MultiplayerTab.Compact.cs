using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// The Rooms page's COMPACT layout and the room-code search — design handoff turn 36
/// (<c>docs/design_handoff_salas_laptop</c>, variant 36a).
///
/// <para>A separate partial file because it is one feature with one switch: everything here
/// either answers <see cref="SetCompactLayout"/> or is the search box's code path, and keeping
/// it apart is what lets a reader see the whole of it at once instead of across a
/// twenty-thousand-line code-behind.</para>
///
/// <para><b>What the switch does NOT govern.</b> The functional changes of the handoff — the
/// code pasted into the search, the per-seat bars, the CASUAL tag, the ping colours, the Join
/// look — apply at every size, by the maintainer's decision: a control that moves when a window
/// is maximised is worse than one that never moved. Only geometry hangs off
/// <see cref="_compactLayout"/>.</para>
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
        Thickness SubBarPadding,
        Thickness StripMargin,
        Thickness StripPadding,
        Thickness StripBorder);

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
            SubBar.Padding,
            ActivityStrip.Margin,
            ActivityStrip.Padding,
            ActivityStrip.BorderThickness);

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

        PlaceActivityStrip();

        // The rows change STYLE with the layout, and the header strip's width just moved, so the
        // columns are re-resolved from scratch. _roomColumnsApplied first, for the reason its own
        // comment gives: a resolved set that matches the previous one would otherwise skip the
        // header rebuild. ApplyRoomColumns re-renders when it can measure; the explicit re-render
        // covers the case where it cannot (not laid out yet) so the rows never keep the old style.
        _roomColumnsApplied = false;
        ApplyRoomColumns();
        RerenderRoomsFromCache();
    }

    // ------------------------------------------------------------------------
    // The activity strip: inline (wide) or a 44-px bar + overlay (compact)
    // ------------------------------------------------------------------------

    /// <summary>
    /// Put <c>ActivityStrip</c> where the current layout wants it, and show the compact bar and
    /// overlay accordingly.
    ///
    /// <para>The strip is MOVED rather than duplicated: it has a dozen named children that the
    /// fill methods write to, and a second copy would be a second set to keep in step. A
    /// Border's Child is the slot WPF re-parents cleanly, which is why both hosts are Borders.</para>
    /// </summary>
    private void PlaceActivityStrip()
    {
        if (ActivityStrip == null || ActivityInlineHost == null || ActivityOverlayHost == null) return;

        var target = _compactLayout ? ActivityOverlayHost : ActivityInlineHost;
        if (!ReferenceEquals(ActivityStrip.Parent, target))
        {
            if (ActivityStrip.Parent is Border from) from.Child = null;
            target.Child = ActivityStrip;
        }

        // Inside the overlay the card is the frame, so the strip's own top rule and spacing would
        // only push its content off the card's edge.
        if (_compactLayout)
        {
            ActivityStrip.Margin = new Thickness(0);
            ActivityStrip.Padding = new Thickness(0);
            ActivityStrip.BorderThickness = new Thickness(0);
        }
        else if (_wide != null)
        {
            ActivityStrip.Margin = _wide.StripMargin;
            ActivityStrip.Padding = _wide.StripPadding;
            ActivityStrip.BorderThickness = _wide.StripBorder;
        }

        var hasContent = ActivityStrip.Visibility == Visibility.Visible;
        var expanded = _config?.RoomsActivityExpanded == true;

        ActivityBar.Visibility = _compactLayout && hasContent ? Visibility.Visible : Visibility.Collapsed;
        ActivityOverlay.Visibility = _compactLayout && hasContent && expanded
            ? Visibility.Visible
            : Visibility.Collapsed;
        ApplyActivityToggleCaption();
    }

    /// <summary>The bar's fixed labels. Called from ApplyStrings.</summary>
    private void ApplyActivityBarStrings()
    {
        if (ActivityBarTitle != null) ActivityBarTitle.Text = Strings.Get("MpActivityBarTitle");
        ApplyActivityToggleCaption();
    }

    private void ApplyActivityToggleCaption()
    {
        if (ActivityToggle == null) return;
        ActivityToggle.Content = _config?.RoomsActivityExpanded == true
            ? Strings.Get("MpActivityHide") + " ▾"
            : Strings.Get("MpActivityShow") + " ▴";
    }

    /// <summary>
    /// "Show activity ▴" / "Hide activity ▾". The choice is REMEMBERED, as the handoff asks —
    /// somebody who wants the community block open on a laptop wants it open next time too.
    /// </summary>
    private void ActivityToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_config == null) return;
        _config.RoomsActivityExpanded = !_config.RoomsActivityExpanded;
        try { _config.Save(); }
        catch (Exception ex) { DiagnosticLog.Write($"Activity toggle: config save failed: {ex.Message}"); }
        PlaceActivityStrip();
    }

    /// <summary>
    /// The 44-px line under the compact rooms list: busiest hours with a mini histogram, the
    /// match count, the last community match, and #1 on the ladder — each segment present only
    /// when its data is.
    ///
    /// <para>Built from the same cached payload the full strip draws (<see cref="_communityStats"/>)
    /// and called at the end of <see cref="RenderActivityStrip"/>, so a language change and a
    /// poll repaint the two together and they can never disagree.</para>
    ///
    /// <para><b>Twenty-four bars, not the handoff's fourteen.</b> Bucketing the hours was
    /// proposed, built and rejected for the full card (see <c>DrawPeakBars</c>), and 24 does not
    /// map onto 14 anyway. Same footprint: 24 bars of 2 px with a 1-px gap is 71 px against the
    /// handoff's 68.</para>
    /// </summary>
    private void FillActivityBar()
    {
        if (ActivityBarSegments == null) return;

        var grid = ActivityBarSegments;
        grid.Children.Clear();
        grid.ColumnDefinitions.Clear();

        var stats = _communityStats;
        var muted = (Brush)Application.Current.FindResource("MpTextMuted");
        var size = (double)Application.Current.FindResource("MpLabelSize");

        var segments = new List<(FrameworkElement Element, bool Stretch)>();

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
            segments.Add((peak, false));
        }

        var totals = CommunityStatsView.Totals(stats);
        if (totals != null)
        {
            var line = BarText(muted, size);
            foreach (var run in BuildBarEmphasis(
                         Strings.Get("MpActivityBarMatches"),
                         totals.Matches.ToString(),
                         totals.WindowDays.ToString()))
                line.Inlines.Add(run);
            segments.Add((line, false));
        }

        var recent = CommunityStatsView.RecentMatches(stats).FirstOrDefault();
        if (recent != null)
            segments.Add((BuildBarMatchSegment(recent, muted, size), true));

        var top = CommunityStatsView.Rows(stats).FirstOrDefault();
        if (top != null)
        {
            var name = string.IsNullOrWhiteSpace(top.DisplayName) ? top.DiscordUsername : top.DisplayName;
            var line = BarText(muted, size);
            line.Inlines.Add(new System.Windows.Documents.Run("#1 "));
            line.Inlines.Add(new System.Windows.Documents.Run(name)
            {
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.FindResource("MpTextSecondary"),
            });
            line.Inlines.Add(new System.Windows.Documents.Run(" " + ((int)Math.Round(top.Rating)).ToString())
            {
                FontFamily = new FontFamily("Consolas"),
                Foreground = (Brush)Application.Current.FindResource("MpTextPrimary"),
            });
            segments.Add((line, false));
        }

        var col = 0;
        for (var i = 0; i < segments.Count; i++)
        {
            if (i > 0)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var sep = new Border
                {
                    Width = 1,
                    Height = 20,
                    Margin = new Thickness(14, 0, 14, 0),
                    Background = (Brush)Application.Current.FindResource("MpRimMedium"),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(sep, col++);
                grid.Children.Add(sep);
            }

            var (element, stretch) = segments[i];
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                // The last community match is the ONE segment that shrinks — the handoff says so,
                // and a name is the cheapest thing on the line to lose the tail of. Everything else
                // keeps its width.
                Width = stretch ? new GridLength(1, GridUnitType.Star) : GridLength.Auto,
            });
            Grid.SetColumn(element, col++);
            grid.Children.Add(element);
        }
    }

    private static TextBlock BarText(Brush foreground, double size) => new()
    {
        Foreground = foreground,
        FontSize = size,
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
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
    /// "● Kaiser beat El Taita · 36 min ago" — a Grid, never a horizontal StackPanel, so the
    /// names take the ellipsis and the age survives. The age is its own TextBlock and is
    /// registered in <see cref="_activityAgeCells"/>, which overwrites a cell's WHOLE text: a
    /// cell holding the names too would have them replaced by "36 min ago" on the next tick.
    /// <para><b>Left-aligned, so the age follows the names.</b> Stretched, the star column took
    /// the whole segment and the age landed at its far end, a hand's width from the match it
    /// dates. A left-aligned Grid is arranged at its desired width, and a star column's desired
    /// width is its child's — so the names column is exactly as wide as the names until the
    /// segment runs out of room, and only then does the ellipsis fire.</para>
    /// </summary>
    private FrameworkElement BuildBarMatchSegment(CommunityMatch m, Brush muted, double size)
    {
        var line = CommunityStatsView.Describe(m);
        var players = MatchParticipantsView.Build(m.Participants, null);

        var g = new Grid { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 6,
            Height = 6,
            Fill = (Brush)Application.Current.FindResource(line.Decided ? "MpOk" : "MpTextFaint"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        Grid.SetColumn(dot, 0);
        g.Children.Add(dot);

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
        Grid.SetColumn(names, 1);
        g.Children.Add(names);

        var reportedUtc = RoomAgeFormat.ParseCreatedUtc(m.ReportedAt);
        if (reportedUtc.HasValue)
        {
            var tail = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var dotSep = BarText(muted, size);
            dotSep.Text = " · ";
            tail.Children.Add(dotSep);
            var age = BarText(muted, size);
            var elapsed = DateTime.UtcNow - reportedUtc.Value;
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
            age.Text = Strings.Format("MpActivityAgo", RoomAgeFormat.Coarse(elapsed));
            tail.Children.Add(age);
            _activityAgeCells.Add((age, reportedUtc.Value));
            Grid.SetColumn(tail, 2);
            g.Children.Add(tail);
        }
        return g;
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
