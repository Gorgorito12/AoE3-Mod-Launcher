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
/// The Clasificación page (design handoff 55a-55c, rating v3).
///
/// <para><b>What the table is:</b> the ranked players ordered by ELO, highest first, with the
/// server's place, a rank badge, the name followed by "YOU", the streak and INACTIVE, the ELO
/// with a bar measured against first place, the rated W-L and the win percentage; then, at the
/// end of the SAME table with no heading, the players still in placement — no place, no badge,
/// "1490?", their progress and their segments. Under 600 px the table drops to three columns
/// (55b). With nobody ranked yet a box says so above the placement rows, and with nobody at all
/// the box offers to create a room (55c).</para>
///
/// <para><b>Ranks and order are the server's.</b> Nothing here renumbers or re-sorts — a client
/// that did would report a different place than the next player's launcher.</para>
/// </summary>
public partial class MultiplayerTab
{
    /// <summary>Days without a rated match before a player reads INACTIVE (the server's
    /// <c>INACTIVE_AFTER_MS</c>). Only quoted in the footnote: the flag itself is the server's.</summary>
    internal const int InactiveAfterDays = 30;

    /// <summary>Whether the table was last drawn as the 55b variant, so a resize that crosses
    /// the 600-px line redraws it — and one that does not, does not.</summary>
    private bool _rankingNarrow;

    /// <summary>The columns the table was last drawn with, for the pinned copy and the tests.</summary>
    private IReadOnlyList<RankingColumnSpec>? _rankingSpecs;

    /// <summary>The viewer's own row in the table, for the pinned-copy rule.</summary>
    private FrameworkElement? _rankingOwnRow;

    /// <summary>
    /// The width-driven sizes the table was last drawn with (design 59): the gap between
    /// columns, the row heights and the type. Starts at the minimums, which is what a table
    /// that was never laid out — every test that builds a row directly — should get.
    /// </summary>
    private RankingFluid _rankingFluid = RankingTableLayout.Fluid(0);

    /// <summary>The sizes for the page as it is laid out now, with the text-size setting.</summary>
    private RankingFluid CurrentRankingFluid()
        => RankingTableLayout.Fluid(
            RankingPageWidthOverride ?? RankingPage?.ActualWidth ?? 0, TextScale.CurrentFactor);

    /// <summary>Test seam: the page width the fluid sizes are computed from.</summary>
    internal double? RankingPageWidthOverride { get; set; }

    /// <summary>
    /// The width the table has, or 0 before the first layout (which reads as wide).
    ///
    /// <para><b>In the launcher the 55b variant is dormant.</b> The tab is laid out by
    /// <see cref="UiScale"/> at a logical width of at least ~1100 px (the window's minimum is 900,
    /// scaled by 0.82), and the table takes at least 640 of that (design 59's split gives it 60 %
    /// of what is left past its basis), so it never measures under 600 logical px. The variant is kept because the handoff specifies it and a future
    /// layout (a docked panel, a smaller minimum) could reach it; the snapshot harness reaches it
    /// through <see cref="RankingWidthOverride"/>.</para>
    /// </summary>
    private double RankingTableWidth() => RankingWidthOverride ?? RankingRowsScroll?.ActualWidth ?? 0;

    /// <summary>Test seam: the width the table is treated as having, for the 55b snapshot.</summary>
    internal double? RankingWidthOverride { get; set; }

    private void RankingModeSolo_Click(object sender, RoutedEventArgs e)
    {
        _rankingMode = RankingMode.Solo;
        RenderRanking();
    }

    private void RankingModeTeam_Click(object sender, RoutedEventArgs e)
    {
        _rankingMode = RankingMode.Team;
        RenderRanking();
    }

    private void RankingModeHighlights_Click(object sender, RoutedEventArgs e)
    {
        _rankingMode = RankingMode.Highlights;
        RenderRanking();
    }

    /// <summary>
    /// Draws the whole page from the one community payload — both ladders and both placement
    /// lists come in it, so switching between 1v1 and Teams costs no request.
    /// </summary>
    private void RenderRanking()
    {
        if (RankingBody == null) return;
        RankingBody.Children.Clear();
        RankingHeaderHost.Children.Clear();
        RankingPinnedRow.Children.Clear();
        RankingPinnedRow.Visibility = Visibility.Collapsed;
        _rankingOwnRow = null;

        // The match list beside the ladder reads the same payload.
        RenderRankingHistory();

        // The Teams side is offered when the server has a team ladder at all — ranked or placing.
        var teamRanked = CommunityStatsView.TeamRows(_communityStats);
        var hasTeamLadder = teamRanked != null || _communityStats?.LeaderboardTeamPlacement != null;
        RankingModeTeam.Visibility = hasTeamLadder ? Visibility.Visible : Visibility.Collapsed;
        if (!hasTeamLadder && _rankingMode == RankingMode.Team) _rankingMode = RankingMode.Solo;
        RankingModeSolo.Tag = _rankingMode == RankingMode.Solo ? "active" : null;
        RankingModeTeam.Tag = _rankingMode == RankingMode.Team ? "active" : null;
        RankingModeHighlights.Tag = _rankingMode == RankingMode.Highlights ? "active" : null;
        RankingModeMatches.Tag = _rankingMode == RankingMode.Matches ? "active" : null;

        // Highlights and Matches take the whole page: the table and the match list step aside.
        var highlights = _rankingMode == RankingMode.Highlights;
        var matchesView = _rankingMode == RankingMode.Matches;
        RankingTableCard.Visibility = highlights || matchesView ? Visibility.Collapsed : Visibility.Visible;
        RankingHighlightsView.Visibility = highlights ? Visibility.Visible : Visibility.Collapsed;
        RankingMatchesView.Visibility = matchesView ? Visibility.Visible : Visibility.Collapsed;
        RankingMonthCapsule.Visibility = highlights ? Visibility.Visible : Visibility.Collapsed;
        UpdateRankingHistoryVisibility();
        if (highlights)
        {
            RenderRankingHighlights();
            return;
        }
        if (matchesView)
        {
            RenderRankingMatches();
            return;
        }

        var team = _rankingShowsTeam;
        IReadOnlyList<LeaderboardRow> ranked = team
            ? teamRanked ?? (IReadOnlyList<LeaderboardRow>)Array.Empty<LeaderboardRow>()
            : CommunityStatsView.Rows(_communityStats);
        var placing = CommunityStatsView.PlacementRows(_communityStats, team);

        RenderRankingChrome(team, ranked.Count, placing.Count);

        var width = RankingTableWidth();
        _rankingNarrow = RankingTableLayout.IsNarrow(width);
        _rankingFluid = CurrentRankingFluid();
        var specs = RankingTableLayout.For(width);
        _rankingSpecs = specs;

        // 55c, nobody at all: the box and a way to make the first match happen.
        if (ranked.Count == 0 && placing.Count == 0)
        {
            RankingBody.Children.Add(BuildRankingEmptyBox(nobodyAtAll: true, team));
            return;
        }

        // 55c, nobody ranked yet: the box takes the ranked rows' place, the placement rows follow,
        // and there is no column header — there is no ranked column to head.
        if (ranked.Count == 0)
            RankingBody.Children.Add(BuildRankingEmptyBox(nobodyAtAll: false, team));
        else
            RankingHeaderHost.Children.Add(BuildRankingHeader(specs));

        var topRating = ranked.Count > 0 ? ranked.Max(r => r.Rating) : 0;
        var meId = RankingViewerId;
        var ladderSize = LadderSize(team);
        UIElement? pinned = null;

        foreach (var row in ranked)
        {
            var isMe = IsViewer(row.UserId, meId);
            var element = (FrameworkElement)BuildLeaderboardRow(row, topRating, isMe, specs, ladderSize, team);
            RankingBody.Children.Add(element);
            if (isMe)
            {
                _rankingOwnRow = element;
                pinned = BuildLeaderboardRow(row, topRating, isMe: true, specs, ladderSize, team);
            }
        }

        var ownResults = OwnPlacementResults(team);
        foreach (var p in placing)
        {
            var isMe = IsViewer(p.UserId, meId);
            var element = (FrameworkElement)BuildPlacementRow(p, isMe, specs, isMe ? ownResults : null);
            RankingBody.Children.Add(element);
            if (isMe)
            {
                _rankingOwnRow = element;
                pinned = BuildPlacementRow(p, isMe: true, specs, ownResults);
            }
        }

        // The flags beside the match list's names come from the mod's own files.
        _ = EnsureRankingCivArtAsync(ranked);

        if (pinned != null)
        {
            // A SECOND copy of the viewer's row, shown only while the real one is out of sight.
            RankingPinnedRow.Children.Add(new Border
            {
                Child = pinned,
                BorderBrush = (Brush)Application.Current.FindResource("MpOwnRowRim"),
                BorderThickness = new Thickness(0, 1, 0, 0),
            });
        }

        // Deferred: the ScrollViewer has not measured yet, so asking now would compare against a
        // zero-height viewport and pin the row on a table that fits. The width is checked again
        // for the same reason — the first draw had none to go by.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            SyncRankingScrollGutter();
            UpdateRankingPinnedRow();
            ReflowRankingIfShapeChanged();
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>
    /// The heading and the pinned "YOU" row sit OUTSIDE the rows' ScrollViewer, which keeps an 8-px
    /// gutter (the implicit style's padding) and, when the list scrolls, a bar the rows lose width
    /// to. Every column but the first is anchored to the right, so without this the ELO, W-L and %
    /// of the heading and of the pinned row land several pixels right of the same column in the
    /// list. Measured in layout units — the rows' real width against the viewer's — so it stays
    /// right under <c>UiScale</c>, where a system scrollbar width would not.
    /// </summary>
    private void SyncRankingScrollGutter()
    {
        if (RankingRowsScroll == null || RankingBody == null) return;
        var outer = RankingRowsScroll.ActualWidth;
        var inner = RankingBody.ActualWidth;
        if (!(outer > 0) || !(inner > 0)) return;
        var inset = new Thickness(0, 0, Math.Max(0, outer - inner), 0);
        if (!RankingHeaderHost.Margin.Equals(inset)) RankingHeaderHost.Margin = inset;
        if (!RankingPinnedRow.Margin.Equals(inset)) RankingPinnedRow.Margin = inset;
    }

    /// <summary>The rows change width when the scroll bar comes and goes: follow them.</summary>
    private void RankingBody_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged) SyncRankingScrollGutter();
    }

    private static bool IsViewer(string userId, string? meId)
        => !string.IsNullOrEmpty(meId) && string.Equals(userId, meId, StringComparison.Ordinal);

    /// <summary>
    /// Redraws when the table crossed the 600-px line since it was drawn, or when the page's
    /// width moved one of design 59's sizes. Those are rounded (whole pixels, half points), so
    /// dragging the window edge rebuilds the table a handful of times, not once per pixel.
    /// </summary>
    private void ReflowRankingIfShapeChanged()
    {
        if (RankingView?.Visibility != Visibility.Visible) return;
        var width = RankingTableWidth();
        if (width <= 0) return;
        if (RankingTableLayout.IsNarrow(width) != _rankingNarrow
            || CurrentRankingFluid() != _rankingFluid)
            RenderRanking();
    }

    /// <summary>
    /// The viewer's own placement results on a ladder, oldest first — the only rows whose segments
    /// are coloured by result (55a: everybody else's played segments are grey). The rating
    /// preview's sample viewer while the preview owns the page.
    /// </summary>
    private IReadOnlyList<PlacementResultEntry>? OwnPlacementResults(bool team)
    {
        var standing = _eloPreview ? _eloPreviewProfile?.Standing ?? EloDemoData.Profile().Standing : _cachedStanding;
        var ladder = team ? standing?.Ladders?.Team : standing?.Ladders?.Default;
        return ladder?.PlacementResults;
    }

    /// <summary>
    /// The text around the table: the title, "12 ranked · 3 in placement", the footnote and its
    /// link, and the preview chip while a preview owns the data.
    /// </summary>
    private void RenderRankingChrome(bool team, int rankedShown, int placingShown)
    {
        RankingTitleText.Text = Strings.Get("MpSubtabRanking");
        RankingEloHelpButton.Content = Strings.Get("MpRankHowElo");
        RankGuideLink.Content = "?  " + Strings.Get("MpGuideLink");

        // The TOTALS, which are not the length of the lists once the league outgrows the
        // server's page. 0 means an older backend: say how many are shown instead.
        var ranked = CommunityStatsView.RankedPlayers(_communityStats, team);
        var placing = CommunityStatsView.PlacementCount(_communityStats, team);
        RankingSubtitleText.Text = Strings.Format(
            "MpRankCountSummary", ranked > 0 ? ranked : rankedShown, Math.Max(placing, placingShown));

        // Design 59's sentence: what counts, what the flame means, what INACTIVE means. It no
        // longer quotes the placement length — the "?" and "Placement 6/10" say it on the row.
        RankingFootnoteText.Text = Strings.Format("MpRankFootRule", InactiveAfterDays);

        ApplyPreviewChip();
    }

    /// <summary>
    /// 55c. <paramref name="nobodyAtAll"/>: "nobody has played a rated match" and a solid
    /// "+ Create room"; otherwise "nobody has finished placement" with the placement length.
    /// </summary>
    private UIElement BuildRankingEmptyBox(bool nobodyAtAll, bool team)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(new TextBlock
        {
            Text = Strings.Get(nobodyAtAll ? "MpRankEmptyTitle" : "MpRankEmptyPlacementTitle"),
            FontSize = (double)Application.Current.FindResource("MpRankNameSize"),
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.FindResource("MpTextPrimary"),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });
        var required = CommunityStatsView.PlacementRequiredFor(_communityStats, team);
        stack.Children.Add(new TextBlock
        {
            Text = nobodyAtAll
                ? Strings.Get("MpRankEmptyBody")
                : Strings.Format("MpRankEmptyPlacementBody", required > 0 ? required : StatsDemoData.PlacementDefault),
            Margin = new Thickness(0, 6, 0, 0),
            FontSize = (double)Application.Current.FindResource("MpMetaSize"),
            Foreground = (Brush)Application.Current.FindResource("MpTextMuted"),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });
        if (nobodyAtAll)
        {
            var create = new Button
            {
                Content = Strings.Get("MpRankCreateRoom"),
                Style = (Style)Application.Current.FindResource("MpPrimaryButton"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 14, 0, 0),
                Padding = new Thickness(16, 7, 16, 7),
                Tag = "RankingCreateRoom",
            };
            create.Click += (_, _) =>
            {
                ShowRooms();
                CreateRoomButton_Click(create, new RoutedEventArgs());
            };
            stack.Children.Add(create);
        }

        return new Border
        {
            Child = stack,
            Tag = nobodyAtAll ? "RankingEmptyAll" : "RankingEmptyPlacement",
            Padding = new Thickness(16, 20, 16, 20),
            BorderBrush = (Brush)Application.Current.FindResource("MpRimHair"),
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
    }

    /// <summary>
    /// The column headings, from the same specs every row reads: a 34-px row, 10-px labels,
    /// 16 px in from each side (design 59).
    /// </summary>
    private UIElement BuildRankingHeader(IReadOnlyList<RankingColumnSpec> specs)
    {
        var grid = BuildRankingGrid(specs, _rankingFluid.Gap);
        grid.Margin = new Thickness(RankingTableLayout.SidePadding, 0, RankingTableLayout.SidePadding, 0);
        grid.Height = RankingTableLayout.HeaderHeight;
        for (var i = 0; i < specs.Count; i++)
        {
            var spec = specs[i];
            var t = new TextBlock
            {
                Text = Strings.Get(RankingTableLayout.HeaderKey(spec.Column)),
                Foreground = (Brush)Application.Current.FindResource("MpTextLabel"),
                FontSize = (double)Application.Current.FindResource("MpMicroSize"),
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = spec.RightAligned ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(t, GridColumnOf(i));
            grid.Children.Add(t);
        }
        return new Border
        {
            Child = grid,
            BorderBrush = (Brush)Application.Current.FindResource("MpRimFaint"),
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
    }

    /// <summary>
    /// One Grid laid out to the table's columns; the single place widths become
    /// ColumnDefinitions, so the header and every row are the same shape by construction.
    ///
    /// <para>The gap between two columns is a column of its own, <paramref name="gap"/> wide
    /// (<see cref="GridColumnOf"/> maps a spec to its Grid column). That is CSS grid's
    /// <c>gap</c> exactly: subtracted before the fractions are shared out. Carrying it in a
    /// cell's margin instead would count it inside a column's share and skew the
    /// proportions.</para>
    /// </summary>
    private static Grid BuildRankingGrid(IReadOnlyList<RankingColumnSpec> specs, double gap)
    {
        var grid = new Grid();
        for (var i = 0; i < specs.Count; i++)
        {
            if (i > 0) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(gap) });
            var spec = specs[i];
            var column = new ColumnDefinition
            {
                Width = spec.FixedWidth is double fixedWidth
                    ? new GridLength(fixedWidth)
                    : new GridLength(spec.Star, GridUnitType.Star),
            };
            // minmax(MinWidth, Star fr): WPF shares the star space and lifts any column under
            // its minimum to it, taking the difference from the others.
            if (spec.FixedWidth == null && spec.MinWidth > 0) column.MinWidth = spec.MinWidth;
            grid.ColumnDefinitions.Add(column);
        }
        return grid;
    }

    /// <summary>The Grid column a spec's cell lives in — every other column is a gap.</summary>
    internal static int GridColumnOf(int specIndex) => specIndex < 0 ? -1 : specIndex * 2;

    /// <summary>The Grid column of a table column, or -1 when these specs do not have it.</summary>
    private static int CellOf(IReadOnlyList<RankingColumnSpec> specs, RankingColumn column)
        => GridColumnOf(ColumnOf(specs, column));

    private static int ColumnOf(IReadOnlyList<RankingColumnSpec> specs, RankingColumn column)
    {
        for (var i = 0; i < specs.Count; i++)
            if (specs[i].Column == column) return i;
        return -1;
    }

    /// <summary>
    /// One ranked row (55a/55b, sized by design 59). <c>internal</c> so the layout tests build
    /// the real row.
    /// </summary>
    internal UIElement BuildLeaderboardRow(
        LeaderboardRow row, double topRating, bool isMe,
        IReadOnlyList<RankingColumnSpec> specs, int ladderSize, bool team)
    {
        var narrow = specs.Count <= 3;
        var fluid = _rankingFluid;
        var grid = BuildRankingGrid(specs, fluid.Gap);
        grid.Margin = new Thickness(RankingTableLayout.SidePadding, 0, RankingTableLayout.SidePadding, 0);
        // A minimum, not a height: a larger text size makes the row taller rather than cut.
        grid.MinHeight = narrow ? RankingTableLayout.NarrowRowHeight : fluid.RowHeight;
        var inactive = row.Inactive == true;

        // # — the server's place, in the serif, one colour for every place. Design 59 drops
        // 55a's gold first place: the badge beside it already says it.
        grid.Children.Add(WithColumn(new TextBlock
        {
            Text = row.Rank.ToString(),
            FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont"),
            FontSize = (double)Application.Current.FindResource("MpRankNameSize"),
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.FindResource("MpRankNumber"),
            VerticalAlignment = VerticalAlignment.Center,
        }, CellOf(specs, RankingColumn.Rank)));

        // The badge: the AGE comes from the server's place, the double shield on the Teams table.
        var age = RankAges.For(row.Rank, ladderSize);
        var otherRank = team ? row.LadderRank : row.LadderRankTeam;
        var shown = new ShownBadge(
            team ? BadgeKind.Team : BadgeKind.Solo, age, row.Rank,
            RankAges.ForOptional(otherRank, LadderSize(!team)), otherRank ?? 0);
        var badge = RankBadge.BuildFor(
            shown, RankingBadgeSize, row.UserId,
            RankBadgeTips.Text(shown, CommunityStatsView.RequiredDecided(_communityStats)),
            onClick: () => ShowRankGuide(initial: shown.Kind),
            // Only the top of the table wears a moving light; see RankBadge.AnimatedTopPlaces.
            animated: RankBadge.AnimatesAt(row.Rank));

        var name = string.IsNullOrEmpty(row.DisplayName) ? row.DiscordUsername : row.DisplayName;
        var who = BuildRankingWho(
            badge, BuildRankingAvatar(name, row.AvatarUrl), name,
            nameBrush: isMe ? "MpTextHeading" : inactive ? "MpTextBody" : "MpTextPrimary",
            nameSize: fluid.NameSize, avatarOpacity: 1,
            isMe, row.Streak, placementRow: false, inactive);
        grid.Children.Add(WithColumn(who, CellOf(specs, RankingColumn.Player)));

        // ELO: the figure, and under it (wide table only) a 4-px bar against first place that
        // stops at 300 px however wide the column gets (design 59).
        var elo = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        elo.Children.Add(new TextBlock
        {
            Text = PlacementView.RatingText(row.Rating),
            FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"),
            FontSize = fluid.EloSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.FindResource(
                isMe ? "MpTextHeading" : inactive ? "MpTextBody" : "MpTextPrimary"),
        });
        if (!narrow)
        {
            var bar = CappedLeft(new Border
            {
                Height = RankingTableLayout.BarHeight,
                CornerRadius = new CornerRadius(2),
                Background = (Brush)Application.Current.FindResource("MpRankBarTrack"),
                Child = BuildRatingBar(
                    RankingTableLayout.BarFraction(row.Rating, topRating),
                    inactive ? "MpRankBarInactive" : "MpAction"),
                Tag = "RankingBar",
            });
            bar.Margin = new Thickness(0, RankingTableLayout.BarGap, 0, 0);
            elo.Children.Add(bar);
        }
        grid.Children.Add(WithColumn(elo, CellOf(specs, RankingColumn.Rating)));

        var recordCol = CellOf(specs, RankingColumn.Record);
        if (recordCol >= 0)
        {
            grid.Children.Add(WithColumn(RankingFigure(
                Strings.Format("MpRankRecordValue", row.RecordWins, row.RecordLosses),
                isMe ? "MpRankRecordOwn" : inactive ? "MpRankMutedText" : "MpTextSecondary",
                FontWeights.Normal, right: false), recordCol));
        }

        var pctCol = CellOf(specs, RankingColumn.Percent);
        if (pctCol >= 0)
        {
            var pct = CommunityStatsView.WinPercent(row);
            var figure = pct.HasValue
                ? RankingFigure(pct.Value.ToString(), RankingTableLayout.PercentBrushKey(pct.Value, inactive),
                    FontWeights.SemiBold, right: true)
                // Too few decided matches for a rate: a dash, never an empty cell, and the reason
                // on hover — the same bar the profile applies (PlayerStanding.MinDecidedForPercent).
                : RankingFigure(Strings.Get("MpDash"), "MpTextDim", FontWeights.Normal, right: true);
            if (!pct.HasValue)
            {
                figure.Tag = RankingPercentHiddenTag;
                figure.ToolTip = TooltipHelper.Wrap(
                    Strings.Format("MpRankPctFromTip", PlayerStanding.MinDecidedForPercent));
            }
            grid.Children.Add(WithColumn(figure, pctCol));
        }

        return new Border
        {
            Child = grid,
            Tag = row,
            Background = isMe ? (Brush)Application.Current.FindResource("MpRankOwnRow") : null,
            BorderBrush = (Brush)Application.Current.FindResource("MpRimHair"),
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
    }

    /// <summary>The rank badge in a row: 20 px wide (design 59).</summary>
    internal const double RankingBadgeSize = 20;

    /// <summary>
    /// <paramref name="child"/> as wide as its column up to <see cref="RankingTableLayout.BarMaxWidth"/>,
    /// pinned to the left. It takes a STAR column with a MaxWidth: a left-aligned element with
    /// no content of its own measures at zero width, and a stretched one with a MaxWidth is
    /// centred in what is left over.
    /// </summary>
    private static Grid CappedLeft(FrameworkElement child)
    {
        var host = new Grid();
        host.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
            MaxWidth = RankingTableLayout.BarMaxWidth,
        });
        host.Children.Add(child);
        return host;
    }

    /// <summary>
    /// One placement row (55a/55b, design 59): no place and no badge (a gap the badge's width
    /// keeps the names aligned), the name and the avatar dimmed, "1490?" and "Placement 6/10" on
    /// one line, and under them the segments. Everybody else's played segments are grey and
    /// filled; only the viewer's own carry their results — the server sends those to the viewer
    /// alone.
    /// </summary>
    internal UIElement BuildPlacementRow(
        PlacementRow row, bool isMe, IReadOnlyList<RankingColumnSpec> specs,
        IReadOnlyList<PlacementResultEntry>? ownResults)
    {
        var narrow = specs.Count <= 3;
        var fluid = _rankingFluid;
        var grid = BuildRankingGrid(specs, fluid.Gap);
        grid.Margin = new Thickness(RankingTableLayout.SidePadding, 0, RankingTableLayout.SidePadding, 0);
        grid.MinHeight = narrow ? RankingTableLayout.NarrowPlacementRowHeight : fluid.PlacementRowHeight;

        var name = string.IsNullOrEmpty(row.DisplayName) ? row.DiscordUsername : row.DisplayName;
        var spacer = new Border { Width = RankingBadgeSize };
        var who = BuildRankingWho(
            spacer, BuildRankingAvatar(name, row.AvatarUrl), name,
            nameBrush: "MpRankPlacementName",
            nameSize: fluid.NameSize, avatarOpacity: 0.6,
            isMe, row.Streak, placementRow: true, inactive: false);
        grid.Children.Add(WithColumn(who, CellOf(specs, RankingColumn.Player)));

        var elo = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var figure = new TextBlock
        {
            FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"),
            FontSize = narrow ? (double)Application.Current.FindResource("MpRankNameSize") : fluid.EloSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.FindResource("MpRankPlacementFigure"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = TooltipHelper.Wrap(Strings.Get("MpEloProvisionalTip")),
        };
        figure.Inlines.Add(new System.Windows.Documents.Run(PlacementView.RatingText(row.Rating)));
        figure.Inlines.Add(new System.Windows.Documents.Run("?")
        {
            Foreground = (Brush)Application.Current.FindResource("MpCaution"),
        });
        // "1571? Placement 6/10" on one line (design 59), or 55b's "1534? 2/5". The en space is
        // the handoff's 6-px gap: a Run takes no margin.
        figure.Inlines.Add(new System.Windows.Documents.Run("\u2002" + (narrow
            ? Strings.Format("MpPlacementProgressShort", row.PlacementPlayed, row.PlacementRequired)
            : Strings.Format("MpPlacementProgress", row.PlacementPlayed, row.PlacementRequired)))
        {
            FontFamily = (FontFamily)Application.Current.FindResource("BodyFont"),
            FontSize = (double)Application.Current.FindResource(narrow ? "MpRankSmallSize" : "MpLabelSize"),
            FontWeight = FontWeights.Normal,
            Foreground = (Brush)Application.Current.FindResource("MpTextMuted"),
        });
        elo.Children.Add(figure);

        var segments = BuildPlacementSegments(
            PlacementView.Segments(row.PlacementPlayed, row.PlacementRequired, isMe ? ownResults : null),
            height: RankingTableLayout.SegmentHeight, gap: RankingTableLayout.SegmentGap, profile: false);
        // The ELO column grows with the window; the segments stop at 300 px with the bar.
        var segmentHost = CappedLeft(segments);
        segmentHost.Margin = new Thickness(0, RankingTableLayout.BarGap, 0, 0);
        elo.Children.Add(segmentHost);
        grid.Children.Add(WithColumn(elo, CellOf(specs, RankingColumn.Rating)));

        // W-L and % read "—" for everybody else; the viewer sees their own W-L.
        var recordCol = CellOf(specs, RankingColumn.Record);
        if (recordCol >= 0)
        {
            string record = Strings.Get("MpDash");
            if (isMe && ownResults != null)
            {
                var wins = ownResults.Count(r => r.Result >= 0.999);
                var losses = ownResults.Count(r => r.Result <= 0.001);
                record = Strings.Format("MpRankRecordValue", wins, losses);
            }
            grid.Children.Add(WithColumn(RankingFigure(
                record, isMe ? "MpRankRecordOwn" : "MpTextDim", FontWeights.Normal, right: false), recordCol));
        }
        var pctCol = CellOf(specs, RankingColumn.Percent);
        if (pctCol >= 0)
        {
            grid.Children.Add(WithColumn(RankingFigure(
                Strings.Get("MpDash"), "MpTextDim", FontWeights.Normal, right: true), pctCol));
        }

        return new Border
        {
            Child = grid,
            Tag = row,
            Background = isMe ? (Brush)Application.Current.FindResource("MpRankOwnRow") : null,
            BorderBrush = (Brush)Application.Current.FindResource("MpRimHair"),
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
    }

    /// <summary>
    /// The PLAYER cell: the badge (or its gap), the name — the only thing that trims — and after
    /// it "YOU", the streak pill and INACTIVE, which never trim (design 55a: "la racha nunca se
    /// recorta").
    ///
    /// <para>A LEFT-aligned Grid of [Auto][*][Auto], never a horizontal StackPanel: the
    /// StackPanel measures at infinite width, so the ellipsis would never fire and a long name
    /// would push the streak out of the cell. Left-aligned, a short name keeps the tags right
    /// beside it and a long one gives them their room.</para>
    /// </summary>
    private static Grid BuildRankingWho(
        FrameworkElement badge, FrameworkElement avatar, string name, string nameBrush,
        double nameSize, double avatarOpacity,
        bool isMe, int streak, bool placementRow, bool inactive)
    {
        var who = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        who.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        who.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        who.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        who.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        badge.VerticalAlignment = VerticalAlignment.Center;
        who.Children.Add(WithColumn(badge, 0));
        avatar.Margin = new Thickness(8, 0, 0, 0);
        // A placement row's picture is dimmed with the rest of it (design 59). An Opacity on a
        // picture costs nothing: ClearType is a text property.
        avatar.Opacity = avatarOpacity;
        who.Children.Add(WithColumn(avatar, 1));
        who.Children.Add(WithColumn(new TextBlock
        {
            Text = name,
            Margin = new Thickness(8, 0, 0, 0),
            Foreground = (Brush)Application.Current.FindResource(nameBrush),
            FontSize = nameSize,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        }, 2));

        // After the name, in design 59's order: the streak, then "YOU", then INACTIVE.
        var tags = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (StreakView.ShowsPill(streak))
        {
            var pill = BuildStreakPill(streak, placementRow, ownRing: isMe && !placementRow, table: true);
            pill.Margin = new Thickness(8, 0, 0, 0);
            tags.Children.Add(pill);
        }
        if (isMe)
        {
            tags.Children.Add(new TextBlock
            {
                Text = Strings.Get("MpRankYouTag"),
                Margin = new Thickness(8, 0, 0, 0),
                FontSize = (double)Application.Current.FindResource("MpMicroSize"),
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.FindResource("MpActionText"),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        if (inactive)
        {
            var tag = BuildInactiveTag();
            tag.Margin = new Thickness(8, 0, 0, 0);
            tags.Children.Add(tag);
        }
        if (tags.Children.Count > 0) who.Children.Add(WithColumn(tags, 3));
        return who;
    }

    /// <summary>The <c>Tag</c> of a row's avatar and of a % cell with too few matches, for the tests.</summary>
    internal const string RankingAvatarTag = "RankingAvatar";

    internal const string RankingPercentHiddenTag = "RankingPercentHidden";

    /// <summary>
    /// The player's Discord picture beside the name — the monogram when there is none. 24 px in
    /// the table; the Rooms page's ranking card passes its own, page-following size.
    /// </summary>
    private static FrameworkElement BuildRankingAvatar(string name, string? avatarUrl, double size = 24)
    {
        var avatar = BuildAvatarDisc(name, avatarUrl, size);
        avatar.Tag = RankingAvatarTag;
        return avatar;
    }

    /// <summary>
    /// The 🔥N pill (design 55a), with its tooltip — "{n} wins in a row" and the rule that ends a
    /// streak. On a placement row it is dimmer; on the viewer's own ranked row it carries a ring.
    /// </summary>
    internal static FrameworkElement BuildStreakPill(
        int streak, bool placementRow = false, bool ownRing = false, bool table = false)
    {
        if (table)
        {
            // Design 59's pill in the ranking table: 20 tall, the body font at 11.5, "🔥 4"
            // with a space, and one colour for ranked and placing rows alike.
            return new Border
            {
                Tag = "StreakPill",
                Height = 20,
                Padding = new Thickness(7, 0, 7, 0),
                // Half the pill's height: WPF draws CSS's 999px as a distorted ellipse.
                CornerRadius = new CornerRadius(10),
                Background = (Brush)Application.Current.FindResource("MpStreakTableBg"),
                BorderBrush = ownRing ? (Brush)Application.Current.FindResource("MpStreakOwnRing") : null,
                BorderThickness = new Thickness(ownRing ? 1.5 : 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = TooltipHelper.Wrap(
                    Strings.Format("MpStreakTipTitle", streak) + "\n" + Strings.Get("MpStreakTipBody")),
                Child = new TextBlock
                {
                    Text = "🔥 " + streak,
                    FontSize = (double)Application.Current.FindResource("MpLabelSize"),
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)Application.Current.FindResource("MpStreakTableText"),
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
        }

        var text = new TextBlock
        {
            Text = "🔥" + streak,
            FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"),
            FontSize = (double)Application.Current.FindResource("MpRankSmallSize"),
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.FindResource(placementRow ? "MpStreakPlacementText" : "MpStreakText"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        return new Border
        {
            Child = text,
            Tag = "StreakPill",
            Padding = new Thickness(6, 3, 6, 3),
            // Half the pill's height (an 11-px line plus 3 + 3): WPF draws CSS's 999px as a
            // distorted ellipse, so a pill needs its real half-height.
            CornerRadius = new CornerRadius(10),
            Background = (Brush)Application.Current.FindResource(placementRow ? "MpStreakPlacementBg" : "MpStreakBg"),
            BorderBrush = ownRing ? (Brush)Application.Current.FindResource("MpStreakOwnRing") : null,
            BorderThickness = new Thickness(ownRing ? 1.5 : 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = TooltipHelper.Wrap(
                Strings.Format("MpStreakTipTitle", streak) + "\n" + Strings.Get("MpStreakTipBody")),
        };
    }

    /// <summary>The INACTIVE tag (design 55a): thirty days without a rated match; the place stays.</summary>
    internal static FrameworkElement BuildInactiveTag() => new Border
    {
        Tag = "InactiveTag",
        Padding = new Thickness(6, 3, 6, 3),
        CornerRadius = new CornerRadius(4),
        Background = (Brush)Application.Current.FindResource("MpInactiveTagBg"),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock
        {
            Text = Strings.Get("MpRankInactiveTag"),
            FontSize = (double)Application.Current.FindResource("MpSectionLabelSize"),
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.FindResource("MpRankMutedText"),
        },
    };

    /// <summary>
    /// A row of placement segments — 4 px in the table (design 59), 8 px on the profile. One per
    /// match the placement asks for; the colours are <see cref="PlacementView.Segments"/>'s.
    /// </summary>
    internal static FrameworkElement BuildPlacementSegments(
        IReadOnlyList<PlacementView.Segment> segments, double height, double gap, bool profile)
    {
        var grid = new Grid { Height = height, Tag = "PlacementSegments" };
        for (var i = 0; i < segments.Count; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < segments.Count; i++)
        {
            var key = segments[i] switch
            {
                PlacementView.Segment.Win => profile ? "MpSegWin" : "MpSegWinTable",
                PlacementView.Segment.Loss => profile ? "MpSegLoss" : "MpSegLossTable",
                PlacementView.Segment.Played => "MpSegPlayedOther",
                _ => profile ? "MpSegPendingProfile" : "MpSegPending",
            };
            var seg = new Border
            {
                Margin = new Thickness(i == 0 ? 0 : gap, 0, 0, 0),
                CornerRadius = new CornerRadius(profile ? 3 : 1),
                Background = (Brush)Application.Current.FindResource(key),
                Tag = segments[i],
            };
            if (profile && segments[i] == PlacementView.Segment.Pending)
            {
                seg.BorderBrush = (Brush)Application.Current.FindResource("MpSegPendingProfileRim");
                seg.BorderThickness = new Thickness(1);
            }
            Grid.SetColumn(seg, i);
            grid.Children.Add(seg);
        }
        return grid;
    }

    /// <summary>A monospace figure for the W-L and % columns.</summary>
    private static TextBlock RankingFigure(string text, string brush, FontWeight weight, bool right) => new()
    {
        Text = text,
        FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"),
        FontSize = (double)Application.Current.FindResource("MpMetaSize"),
        FontWeight = weight,
        Foreground = (Brush)Application.Current.FindResource(brush),
        HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>
    /// The filled part of a rating bar, as a fraction of its track: two star columns rather than a
    /// pixel width, so the bar keeps its proportion when the column is resized.
    /// </summary>
    private static UIElement BuildRatingBar(double fraction, string brushKey)
    {
        var bar = new Grid();
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(fraction, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, 1 - fraction), GridUnitType.Star) });
        var fill = new Border
        {
            CornerRadius = new CornerRadius(2),
            Background = (Brush)Application.Current.FindResource(brushKey),
            Tag = fraction,
        };
        Grid.SetColumn(fill, 0);
        bar.Children.Add(fill);
        return bar;
    }

    /// <summary>
    /// Shows the pinned copy of the viewer's row only while their real one is scrolled out of the
    /// table's viewport. Deliberately NOT "always append my row": a player who can already see
    /// themselves would then be listed twice.
    /// </summary>
    private void UpdateRankingPinnedRow()
    {
        if (RankingPinnedRow == null || RankingRowsScroll == null) return;
        if (_rankingOwnRow == null || RankingPinnedRow.Children.Count == 0)
        {
            RankingPinnedRow.Visibility = Visibility.Collapsed;
            return;
        }

        var visible = false;
        try
        {
            var top = _rankingOwnRow.TranslatePoint(new Point(0, 0), RankingRowsScroll).Y;
            var bottom = top + _rankingOwnRow.ActualHeight;
            var half = _rankingOwnRow.ActualHeight / 2;
            visible = bottom > half && top < RankingRowsScroll.ViewportHeight - half;
        }
        catch (InvalidOperationException)
        {
            // Not in one visual tree yet: leave it hidden rather than pin a row over a table
            // nobody is looking at.
        }

        RankingPinnedRow.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RankingRowsScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        => UpdateRankingPinnedRow();

    /// <summary>Opens the player guide to the rating — the footnote's link.</summary>
    private void RankingEloHelpButton_Click(object sender, RoutedEventArgs e)
        => SafeUrl.TryOpen(Models.LauncherConfig.RatingHelpUrl);
}
