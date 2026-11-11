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
/// The Clasificación page (design 93, over handoffs 55 and 59; rating v3).
///
/// <para><b>What the page is:</b> the first three ranked players on a podium
/// (<c>MultiplayerTab.RankingPodium.cs</c>), and from the fourth on a table grouped by AGE — a
/// heading in the age's colour, then one plate per player tinted with it: the server's place, the
/// rank badge, the picture, the name followed by "YOU", the streak and INACTIVE, the ELO with a bar
/// measured against first place, and the highest ELO held since placement (only when the server
/// sends it). The players still being placed are the last group, with no place, the Discovery
/// badge, their progress and their segments. With nobody ranked yet a notice says so above them,
/// and with nobody at all the table offers to create a room. Beside it, "Your position" and the
/// community's last matches.</para>
///
/// <para><b>Ranks, order and ages are the server's.</b> Nothing here renumbers or re-sorts — a
/// client that did would report a different place than the next player's launcher. The groups come
/// from <see cref="RankingGroups"/>, which only reads the places it is given.</para>
///
/// <para><b>There is no pinned copy of the viewer's row any more</b>: "Your position" says where
/// he is at every scroll offset, with the players around him.</para>
/// </summary>
public partial class MultiplayerTab
{
    /// <summary>Days without a rated match before a player reads INACTIVE (the server's
    /// <c>INACTIVE_AFTER_MS</c>). Only quoted in the footnote: the flag itself is the server's.</summary>
    internal const int InactiveAfterDays = 30;

    /// <summary>Whether the table was last drawn as the 55b variant, so a resize that crosses
    /// the 600-px line redraws it — and one that does not, does not.</summary>
    private bool _rankingNarrow;

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
        TraceRankingModeChange();
        RenderRanking();
    }

    private void RankingModeTeam_Click(object sender, RoutedEventArgs e)
    {
        _rankingMode = RankingMode.Team;
        TraceRankingModeChange();
        RenderRanking();
    }

    private void RankingModeHighlights_Click(object sender, RoutedEventArgs e)
    {
        _rankingMode = RankingMode.Highlights;
        TraceRankingModeChange();
        RenderRanking();
    }

    /// <summary>
    /// Everything the table page is drawn from, so going back to the subtab draws nothing when
    /// none of it moved. The payload by CONTENT: it arrives once a minute as a new object whose
    /// only differences are drawn nowhere (<c>generated_at</c>, every player's <c>rd</c>), and
    /// keying it by reference rebuilt the whole ladder on nearly every switch (282-393 ms in a
    /// player's log). The standing and the month's highlights by reference. The minute is NOT
    /// here: it only moves the match list's "3 h ago", which <see cref="RenderRankingIfChanged"/>
    /// repaints on its own.
    /// </summary>
    private readonly record struct RankingPaintKey(
        string? Stats, RankingMode Mode, string Language, string? Viewer, object? Standing, object? Leaders,
        RankingFluid Fluid, RankingPodiumFluid Podium, double Gap, bool Narrow, bool Effects,
        string Art, int Catalog);

    private RankingPaintKey? _rankingPaintKey;

    /// <summary>The minute the match list's relative times were drawn at.</summary>
    private long _rankingHistoryMinute;

    private static long CurrentMinute => DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute;

    /// <summary>The payload's content, worked out once per answer.</summary>
    private (object? Stats, string? Content) _rankingStatsContent;

    private string? RankingStatsContent()
    {
        if (!ReferenceEquals(_rankingStatsContent.Stats, _communityStats))
            _rankingStatsContent = (_communityStats,
                Services.Multiplayer.ActivityPaintKey.Content(_communityStats));
        return _rankingStatsContent.Content;
    }

    private RankingPaintKey CurrentRankingPaintKey() => new(
        RankingStatsContent(), _rankingMode, Strings.Language, RankingViewerId,
        _eloPreview ? _eloPreviewProfile : _cachedStanding, _rankingLeaders,
        CurrentRankingFluid(), CurrentPodiumFluid(), PageGap,
        RankingTableLayout.IsNarrow(RankingTableWidth()), RankBadge.AnimationsEnabled,
        ActivityArtStamp(), ModRegistry.Generation);

    /// <summary>
    /// Draws the whole page from the one community payload — both ladders and both placement
    /// lists come in it, so switching between 1v1 and Teams costs no request.
    /// </summary>
    private void RenderRanking()
    {
        if (RankingBody == null) return;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        _rankingPaintKey = null;
        RenderRankingCore();
        // The Matches view reports its own builds from PaintMatches.
        if (_rankingMode != RankingMode.Matches)
            Services.ScreenTrace.PageBuilt("Ranking", System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        _rankingHistoryMinute = CurrentMinute;
        // After the draw: it can change the mode (Teams on a server with no team ladder).
        if (_rankingMode is RankingMode.Solo or RankingMode.Team) _rankingPaintKey = CurrentRankingPaintKey();
    }

    /// <summary>
    /// Going BACK to the subtab: draws only when something the page shows moved. The podium, the
    /// plates and the side column cost about twice what the plain table did (27 to 63 ms a
    /// switch, measured), and rebuilding them to show the same thing was most of a tab switch.
    /// Every caller that changed something - a mode button, a new payload, the language, the
    /// flags arriving - calls <see cref="RenderRanking"/>. Only the table modes: Highlights and
    /// Matches keep their own rules for when to draw.
    /// </summary>
    internal bool RenderRankingIfChanged()
    {
        if (RankingBody == null) return false;
        if (_rankingMode is RankingMode.Solo or RankingMode.Team
            && _rankingPaintKey is { } drawn && drawn == CurrentRankingPaintKey())
        {
            Services.PerfCounters.Increment("Ranking.Unchanged");
            // Only the match list's "3 h ago" moves with the clock: that list alone.
            if (_rankingHistoryMinute != CurrentMinute)
            {
                _rankingHistoryMinute = CurrentMinute;
                RenderRankingHistory();
            }
            return false;
        }
        RenderRanking();
        return true;
    }

    private void RenderRankingCore()
    {
        RankingBody.Children.Clear();
        RankingHeaderHost.Children.Clear();
        _rankingGapDrawn = PageGap;

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

        // Highlights and Matches take the whole page: the podium, the table and the side column
        // step aside.
        var highlights = _rankingMode == RankingMode.Highlights;
        var matchesView = _rankingMode == RankingMode.Matches;
        RankingLeftColumn.Visibility = highlights || matchesView ? Visibility.Collapsed : Visibility.Visible;
        // Its own visibility too, not only its column's: the card is what "the table is on screen" means.
        if (highlights || matchesView) RankingTableCard.Visibility = Visibility.Collapsed;
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
        // The PEAK column only when the server sent a peak for somebody on this ladder: an older
        // server sends none, and the table then simply has no such column.
        var specs = RankingTableLayout.For(width, CommunityStatsView.ShowsRatingPeak(ranked));
        var ladderSize = LadderSize(team);

        // Design 93: the first three on the podium, the viewer's own place beside the table.
        RenderRankingPodium(ranked, team, ladderSize);
        RenderRankingYou(ranked, placing, team, ladderSize);
        UpdateRankingHistoryVisibility();

        var groups = RankingGroups.Table(ranked, ladderSize);
        var hasRows = groups.Count > 0 || placing.Count > 0;
        var nobodyAtAll = ranked.Count == 0 && placing.Count == 0;
        // Three or fewer ranked and nobody being placed: the podium is the whole ladder, and an
        // empty card under it would only be a frame around nothing.
        RankingTableCard.Visibility = hasRows || nobodyAtAll ? Visibility.Visible : Visibility.Collapsed;
        RankingFootBand.Visibility = hasRows ? Visibility.Visible : Visibility.Collapsed;
        // The empty state sits in the middle of the card (93e); rows start at its top.
        RankingBody.VerticalAlignment = nobodyAtAll ? VerticalAlignment.Center : VerticalAlignment.Stretch;

        // Nothing has ARRIVED yet: that is not "nobody is ranked", which is what the empty state
        // below says. Rows in the table's shape while it loads, and a sentence if it failed.
        if (_communityStats == null && !_eloPreview && !_demoStats)
        {
            RankingFootBand.Visibility = Visibility.Collapsed;
            RankingBody.VerticalAlignment = VerticalAlignment.Top;
            if (_activityError == ActivityFailure.None)
            {
                var skeleton = StatsSkeletonRows(4);
                if (skeleton is FrameworkElement fe) fe.Margin = new Thickness(8, 12, 8, 12);
                RankingBody.Children.Add(skeleton);
            }
            else
            {
                var failed = StatsProse(Strings.Get("RkLoadFailed"), "StatsProseSize", "StatsNote");
                failed.Margin = new Thickness(8, 16, 8, 16);
                RankingBody.Children.Add(failed);
            }
            return;
        }

        // Nobody at all: the table says so and offers a way to make the first match happen.
        if (nobodyAtAll)
        {
            RankingBody.Children.Add(BuildRankingEmptyBox(nobodyAtAll: true, team));
            return;
        }

        // Nobody ranked yet: the notice takes the ranked rows' place, the placement group follows.
        if (ranked.Count == 0)
            RankingBody.Children.Add(BuildRankingEmptyBox(nobodyAtAll: false, team));
        if (hasRows) RankingHeaderHost.Children.Add(BuildRankingHeader(specs));

        var topRating = ranked.Count > 0 ? ranked.Max(r => r.Rating) : 0;
        var meId = RankingViewerId;

        foreach (var group in groups)
        {
            var ageName = Strings.Get(RankAges.NameKey(group.Age));
            var one = group.FirstPlace == group.LastPlace;
            RankingBody.Children.Add(BuildRankingGroupHead(group.Age, ageName.ToUpper(Strings.Culture),
                group.FirstPlace <= 0 ? ""
                    : one ? Strings.Format("RkGroupPlace", group.FirstPlace)
                    : Strings.Format("RkGroupPlaces", group.FirstPlace, group.LastPlace),
                group.FirstPlace <= 0 ? null
                    : one ? Strings.Format("RkGroupTipOne", ageName, group.FirstPlace)
                    : Strings.Format("RkGroupTip", ageName, group.FirstPlace, group.LastPlace)));
            foreach (var row in group.Rows)
                RankingBody.Children.Add(BuildLeaderboardRow(
                    row, topRating, IsViewer(row.UserId, meId), specs, ladderSize, team));
        }

        if (placing.Count > 0)
        {
            var required = CommunityStatsView.PlacementRequiredFor(_communityStats, team);
            RankingBody.Children.Add(BuildRankingGroupHead(RankAge.Discovery, Strings.Get("RkGroupPlacing"),
                placing.Count == 1 ? Strings.Get("RkGroupPlayerOne") : Strings.Format("RkGroupPlayers", placing.Count),
                required > 0 ? Strings.Format("RkGroupPlacingTip", required) : Strings.Get("RkGroupPlacingTipPlain")));
            var ownResults = OwnPlacementResults(team);
            // The viewer's own placing number, for his own row: a current server sends none on these
            // rows to anybody, him included (src/elo/placementPrivacy.ts on the server).
            var ownRating = OwnLadder(team)?.Rating;
            foreach (var p in placing)
            {
                var isMe = IsViewer(p.UserId, meId);
                RankingBody.Children.Add(BuildPlacementRow(p, isMe, specs, isMe ? ownResults : null,
                    isMe ? ownRating : null));
            }
        }

        // The flags beside the match list's names come from the mod's own files.
        _ = EnsureRankingCivArtAsync(ranked);

        // Deferred: the ScrollViewer has not measured yet. The width is checked again for the same
        // reason — the first draw had none to go by.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            SyncRankingScrollGutter();
            ReflowRankingIfShapeChanged();
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>
    /// The heading of one group of the table (design 93): a small shield in the age's plate, the age
    /// in its light colour, the places it covers, and a line that fades out. The rule is on hover.
    /// </summary>
    internal static FrameworkElement BuildRankingGroupHead(RankAge age, string title, string range, string? tip)
    {
        var (main, light) = RankAgeColors.Of(age);
        var grid = new Grid
        {
            Height = 32,
            Margin = new Thickness(12, 0, 12, 0),
            Tag = GroupHeadTag,
            Background = Brushes.Transparent,
        };
        if (tip != null)
        {
            grid.ToolTip = TooltipHelper.Wrap(tip);
            grid.Cursor = System.Windows.Input.Cursors.Help;
        }
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var shield = new System.Windows.Shapes.Path
        {
            Data = (Geometry)Application.Current.FindResource("RankShieldGeometry"),
            Stretch = Stretch.Fill,
            Width = 11,
            Height = 13,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 6),
        };
        shield.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "RankPlate" + age);
        grid.Children.Add(shield);
        grid.Children.Add(WithColumn(new TextBlock
        {
            Text = title,
            Margin = new Thickness(9, 0, 0, 5),
            FontSize = (double)Application.Current.FindResource("MpMicroSize"),
            FontWeight = FontWeights.Bold,
            Foreground = Solid(light),
            VerticalAlignment = VerticalAlignment.Bottom,
        }, 1));
        grid.Children.Add(WithColumn(new TextBlock
        {
            Text = range,
            Margin = new Thickness(9, 0, 0, 5),
            FontSize = (double)Application.Current.FindResource("MpLabelSize"),
            Foreground = Solid("#8C9CB1"),
            VerticalAlignment = VerticalAlignment.Bottom,
        }, 2));
        grid.Children.Add(WithColumn(new System.Windows.Shapes.Rectangle
        {
            Height = 1,
            Margin = new Thickness(9, 0, 0, 11),
            VerticalAlignment = VerticalAlignment.Bottom,
            Fill = Across((Rgba(main, .55), 0), (Rgba(main, 0), 1)),
        }, 3));
        return grid;
    }

    /// <summary>
    /// The heading sits OUTSIDE the rows' ScrollViewer, which loses width to its bar when the list
    /// scrolls. Every column but the first is anchored to the right, so without this the ELO and the
    /// peak of the heading land several pixels right of the same column in the list. Measured in
    /// layout units — the rows' real width against the viewer's — so it stays right under
    /// <c>UiScale</c>, where a system scrollbar width would not.
    /// </summary>
    private void SyncRankingScrollGutter()
    {
        if (RankingRowsScroll == null || RankingBody == null) return;
        var outer = RankingRowsScroll.ActualWidth;
        var inner = RankingBody.ActualWidth + RankingBody.Margin.Left + RankingBody.Margin.Right;
        if (!(outer > 0) || !(inner > 0)) return;
        var inset = new Thickness(0, 0, Math.Max(0, outer - inner), 0);
        if (!RankingHeaderHost.Margin.Equals(inset)) RankingHeaderHost.Margin = inset;
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
        // The page's gap (12 or 16) is drawn into the podium, the side column and the Highlights
        // cards. Not in Matches: redrawing that list would move it under whoever is reading it.
        if (PageGap != _rankingGapDrawn && _rankingMode != RankingMode.Matches)
        {
            RenderRanking();
            return;
        }
        var width = RankingTableWidth();
        if (width <= 0) return;
        if (RankingTableLayout.IsNarrow(width) != _rankingNarrow
            || CurrentRankingFluid() != _rankingFluid
            || CurrentPodiumFluid() != _podiumFluid)
            RenderRanking();
    }

    /// <summary>The page gap the page was last drawn with.</summary>
    private double _rankingGapDrawn;

    /// <summary>
    /// The viewer's own placement results on a ladder, oldest first — the only rows whose segments
    /// are coloured by result (55a: everybody else's played segments are grey). The rating
    /// preview's sample viewer while the preview owns the page.
    /// </summary>
    private IReadOnlyList<PlacementResultEntry>? OwnPlacementResults(bool team)
        => OwnLadder(team)?.PlacementResults;

    /// <summary>
    /// The viewer's own standing on a ladder — the rating preview's sample viewer while the preview
    /// owns the page. Where his own placement row takes its results and its number from.
    /// </summary>
    private LadderStanding? OwnLadder(bool team)
    {
        // The preview of an empty ladder has no standing for anybody, the viewer included.
        if (_eloPreview && _eloPreviewEmptyLadder) return null;
        var standing = _eloPreview ? _eloPreviewProfile?.Standing ?? EloDemoData.Profile().Standing : _cachedStanding;
        return team ? standing?.Ladders?.Team : standing?.Ladders?.Default;
    }

    /// <summary>
    /// The text around the table: "12 ranked · 3 in placement" with its figures in the mono face,
    /// the footnote and its link, and the preview chip while a preview owns the data.
    /// </summary>
    private void RenderRankingChrome(bool team, int rankedShown, int placingShown)
    {
        RankingEloHelpButton.Content = Strings.Get("MpRankHowElo");

        // The TOTALS, which are not the length of the lists once the league outgrows the
        // server's page. 0 means an older backend: say how many are shown instead.
        var ranked = CommunityStatsView.RankedPlayers(_communityStats, team);
        var placing = CommunityStatsView.PlacementCount(_communityStats, team);
        SetRankingSummary(ranked > 0 ? ranked : rankedShown, Math.Max(placing, placingShown));

        // Design 59's sentence: what counts, what the flame means, what INACTIVE means.
        RankingFootnoteText.Text = Strings.Format("MpRankFootRule", InactiveAfterDays);

        ApplyPreviewChip();
    }

    /// <summary>"9 ranked · 4 in placement", the two figures apart from the words; empty when
    /// there is nobody at all (the table says that itself).</summary>
    private void SetRankingSummary(int ranked, int placing)
    {
        RankingSubtitleText.Inlines.Clear();
        if (ranked + placing == 0) return;
        System.Windows.Documents.Run Figure(int n) => new(n.ToString("N0", Strings.Culture))
        {
            FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"),
            FontWeight = FontWeights.SemiBold,
            Foreground = Solid("#DCE7F5"),
        };
        RankingSubtitleText.Inlines.Add(Figure(ranked));
        RankingSubtitleText.Inlines.Add(new System.Windows.Documents.Run(Strings.Get("RkCountRanked")));
        RankingSubtitleText.Inlines.Add(Figure(placing));
        RankingSubtitleText.Inlines.Add(new System.Windows.Documents.Run(Strings.Get("RkCountPlacing")));
    }

    /// <summary>
    /// <paramref name="nobodyAtAll"/>: "nobody has played a rated match" in the serif, centred, and
    /// a solid "+ Create room"; otherwise the blue notice "nobody has finished placement" with the
    /// placement length, above the placement group (design 93d, 93e).
    /// </summary>
    private UIElement BuildRankingEmptyBox(bool nobodyAtAll, bool team)
    {
        var required = CommunityStatsView.PlacementRequiredFor(_communityStats, team);
        if (!nobodyAtAll)
        {
            var words = new StackPanel();
            words.Children.Add(new TextBlock
            {
                Text = Strings.Get("MpRankEmptyPlacementTitle"),
                FontSize = (double)Application.Current.FindResource("FontSizeBody"),
                FontWeight = FontWeights.SemiBold,
                Foreground = Solid("#F0F5FB"),
                TextWrapping = TextWrapping.Wrap,
            });
            words.Children.Add(new TextBlock
            {
                Text = Strings.Format("MpRankEmptyPlacementBody", required > 0 ? required : StatsDemoData.PlacementDefault),
                Margin = new Thickness(0, 3, 0, 0),
                FontSize = (double)Application.Current.FindResource("MpMetaSize"),
                Foreground = Solid("#C3D2E5"),
                TextWrapping = TextWrapping.Wrap,
            });
            var notice = new Grid();
            notice.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            notice.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            notice.Children.Add(new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                Background = Solid("#2955ACEE"),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock
                {
                    Text = "i",
                    FontSize = (double)Application.Current.FindResource("MpLabelSize"),
                    FontWeight = FontWeights.Bold,
                    Foreground = Solid("#55ACEE"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            });
            notice.Children.Add(WithColumn(words, 1));
            return new Border
            {
                Child = notice,
                Tag = "RankingEmptyPlacement",
                Margin = new Thickness(0, 2, 0, 6),
                Padding = new Thickness(14, 12, 14, 12),
                CornerRadius = new CornerRadius(10),
                Background = Solid("#1455ACEE"),
                BorderBrush = Solid("#4D55ACEE"),
                BorderThickness = new Thickness(1),
            };
        }

        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(new TextBlock
        {
            Text = Strings.Get("MpRankEmptyTitle"),
            FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont"),
            FontSize = (double)Application.Current.FindResource("RmEmptyTitleSize"),
            FontWeight = FontWeights.Bold,
            Foreground = Solid("#F0F5FB"),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });
        stack.Children.Add(new TextBlock
        {
            Text = Strings.Get("MpRankEmptyBody"),
            Margin = new Thickness(0, 10, 0, 0),
            MaxWidth = 420,
            FontSize = (double)Application.Current.FindResource("MpBodySize"),
            Foreground = Solid("#A8BCD2"),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });
        var create = new Button
        {
            Content = Strings.Get("MpRankCreateRoom"),
            Style = (Style)Application.Current.FindResource("MpPrimaryButton"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 16, 0, 0),
            Height = 34,
            Padding = new Thickness(14, 0, 14, 0),
            Tag = "RankingCreateRoom",
        };
        create.Click += (_, _) =>
        {
            ShowRooms();
            CreateRoomButton_Click(create, new RoutedEventArgs());
        };
        stack.Children.Add(create);

        return new Border
        {
            Child = stack,
            Tag = "RankingEmptyAll",
            Padding = new Thickness(24, 72, 24, 72),
        };
    }

    /// <summary>
    /// The column headings, from the same specs every row reads: a 38-px row, 11-px bold labels,
    /// in line with the rows' own content (design 93).
    /// </summary>
    private UIElement BuildRankingHeader(IReadOnlyList<RankingColumnSpec> specs)
    {
        var grid = BuildRankingGrid(specs, _rankingFluid.Gap);
        grid.Margin = new Thickness(RankingTableLayout.SidePadding, 4, RankingTableLayout.SidePadding, 0);
        grid.Height = RankingTableLayout.HeaderHeight - 4;
        for (var i = 0; i < specs.Count; i++)
        {
            var spec = specs[i];
            var t = new TextBlock
            {
                Text = Strings.Get(RankingTableLayout.HeaderKey(spec.Column)),
                Foreground = Solid("#61779A"),
                FontSize = (double)Application.Current.FindResource("MpMicroSize"),
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = spec.RightAligned ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            };
            if (RankingTableLayout.HeaderTipKey(spec.Column) is { } tip)
                t.ToolTip = TooltipHelper.Wrap(Strings.Get(tip));
            Grid.SetColumn(t, GridColumnOf(i));
            grid.Children.Add(t);
        }
        return grid;
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
    /// One ranked row (design 93, sized by design 59): a plate tinted with its age's colour, the
    /// viewer's own in blue with a rim. <c>internal</c> so the layout tests build the real row.
    /// </summary>
    internal UIElement BuildLeaderboardRow(
        LeaderboardRow row, double topRating, bool isMe,
        IReadOnlyList<RankingColumnSpec> specs, int ladderSize, bool team)
    {
        var narrow = RankingTableLayout.IsNarrowSet(specs);
        var fluid = _rankingFluid;
        var grid = BuildRankingGrid(specs, fluid.Gap);
        // A minimum, not a height: a larger text size makes the row taller rather than cut.
        grid.MinHeight = (narrow ? RankingTableLayout.NarrowRowHeight : fluid.RowHeight) - 2;
        var inactive = row.Inactive == true;
        var age = RankAges.For(row.Rank, ladderSize);
        var (main, light) = RankAgeColors.Of(age);

        // # — the server's place, in the serif, one colour for every place.
        grid.Children.Add(WithColumn(new TextBlock
        {
            Text = row.Rank.ToString(),
            FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont"),
            FontSize = fluid.NameSize,
            FontWeight = FontWeights.Bold,
            Foreground = Solid("#CDD9E9"),
            VerticalAlignment = VerticalAlignment.Center,
        }, CellOf(specs, RankingColumn.Rank)));

        // The badge: the AGE comes from the server's place, the double shield on the Teams table.
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
            badge, BuildRankingAvatar(name, row.AvatarUrl, RankingTableLayout.AvatarSize(fluid.RowHeight)), name,
            nameHex: isMe ? "#FFFFFF" : inactive ? "#B9C9DE" : "#F0F5FB",
            nameSize: fluid.NameSize, nameWeight: FontWeights.SemiBold, avatarOpacity: 1,
            isMe, row.Streak, placementRow: false, inactive);
        grid.Children.Add(WithColumn(who, CellOf(specs, RankingColumn.Player)));

        // ELO: the figure, and under it (wide table only) a bar against first place, in the age's
        // colours, that stops at 300 px however wide the column gets.
        var elo = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        elo.Children.Add(new TextBlock
        {
            Text = PlacementView.RatingText(row.Rating),
            FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"),
            FontSize = fluid.EloSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = Solid(inactive ? "#B9C9DE" : "#F0F5FB"),
        });
        if (!narrow)
        {
            var bar = CappedLeft(new Border
            {
                Height = RankingTableLayout.BarHeight,
                CornerRadius = new CornerRadius(3),
                Background = Solid("#52000000"),
                Child = BuildRatingBar(
                    RankingTableLayout.BarFraction(row.Rating, topRating),
                    inactive ? Solid("#4A6A96") : Across((Rgba(main, 1), 0), (Rgba(light, 1), 1))),
                Tag = "RankingBar",
            });
            bar.Margin = new Thickness(0, RankingTableLayout.BarGap, 0, 0);
            elo.Children.Add(bar);
        }
        grid.Children.Add(WithColumn(elo, CellOf(specs, RankingColumn.Rating)));

        // Guarded: the column is absent from a server that sends no peak, and from the 55b table
        // — and Grid.SetColumn(-1) throws.
        var peakCol = CellOf(specs, RankingColumn.Peak);
        if (peakCol >= 0)
            grid.Children.Add(WithColumn(BuildPeakCell(row, isMe, inactive), peakCol));

        var plate = RankingPlate(grid, row, isMe,
            Across((Rgba(main, .22), 0), (Rgba(main, .06), 0.42), (Rgba("#FFFFFF", .03), 1)));
        if (isMe)
        {
            // The viewer's own row opens his profile. Nobody else's does: there is no page for
            // another player, and a row that looks clickable and does nothing is worse.
            plate.Cursor = System.Windows.Input.Cursors.Hand;
            plate.MouseLeftButtonUp += (_, _) => OpenOwnProfileFromRanking();
        }
        return plate;
    }

    /// <summary>
    /// A row of the table as a plate: rounded, 4 px under the one before, its content 12 px in.
    /// The viewer's own is blue with a rim, whatever tint the caller passed.
    /// </summary>
    private static Border RankingPlate(Grid content, object tag, bool isMe, Brush tint) => new()
    {
        Child = content,
        Tag = tag,
        Margin = new Thickness(0, RankingTableLayout.RowGap, 0, 0),
        Padding = new Thickness(RankingTableLayout.RowPadding, 0, RankingTableLayout.RowPadding, 0),
        CornerRadius = new CornerRadius(10),
        Background = isMe ? Across((Rgba("#55ACEE", .22), 0), (Rgba("#55ACEE", .07), 1)) : tint,
        BorderBrush = Solid(isMe ? "#6655ACEE" : "#0DFFFFFF"),
        BorderThickness = new Thickness(1),
    };

    /// <summary>
    /// The PEAK cell: the highest ELO held since placement, with the day it was first reached on
    /// hover. A row the server sent no peak for reads a dim dash — never an empty cell, and never
    /// a number nobody earned.
    /// </summary>
    private static TextBlock BuildPeakCell(LeaderboardRow row, bool isMe, bool inactive)
    {
        if (row.RatingPeak is not double peak)
        {
            var dash = RankingFigure(Strings.Get("MpDash"), "MpTextDim", FontWeights.Normal, right: false);
            dash.Tag = RankingPeakTag;
            return dash;
        }

        var figure = RankingFigure(PlacementView.RatingText(peak),
            isMe ? "MpRankRecordOwn" : inactive ? "MpRankMutedText" : "MpTextSecondary",
            FontWeights.Normal, right: false);
        if (!isMe && !inactive) figure.Foreground = Solid("#C3D2E5");
        figure.Tag = RankingPeakTag;
        var date = ShortDate.Parse(row.RatingPeakAt) is { } when ? ShortDate.Format(when) : null;
        if (date != null) figure.ToolTip = TooltipHelper.Wrap(Strings.Format("MpRankPeakTip", date));
        return figure;
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
    ///
    /// <para><b>The "1490?" is the viewer's OWN row only.</b> A rating still being placed is its
    /// owner's alone: a current server sends nobody a number on these rows, and anybody else's row
    /// is the progress and the segments with no figure (and none drawn even if an older server
    /// sent one). The viewer's own number comes from the row or, when the server sent none, from
    /// <paramref name="ownRating"/> — his own standing.</para>
    /// </summary>
    internal UIElement BuildPlacementRow(
        PlacementRow row, bool isMe, IReadOnlyList<RankingColumnSpec> specs,
        IReadOnlyList<PlacementResultEntry>? ownResults, double? ownRating = null)
    {
        var narrow = RankingTableLayout.IsNarrowSet(specs);
        var fluid = _rankingFluid;
        var grid = BuildRankingGrid(specs, fluid.Gap);
        grid.MinHeight = (narrow ? RankingTableLayout.NarrowPlacementRowHeight : fluid.PlacementRowHeight) - 2;

        var name = string.IsNullOrEmpty(row.DisplayName) ? row.DiscordUsername : row.DisplayName;
        // Design 93: a player being placed wears the Discovery badge, like everywhere else he is
        // seen. It carries no numeral and never a light.
        var team = _rankingShowsTeam;
        var shown = new ShownBadge(team ? BadgeKind.Team : BadgeKind.Solo, RankAge.Discovery, 0, null, 0);
        var badge = RankBadge.BuildFor(shown, RankingBadgeSize, row.UserId,
            RankBadgeTips.Text(shown, CommunityStatsView.RequiredDecided(_communityStats)),
            onClick: () => ShowRankGuide(initial: shown.Kind), animated: false);
        var who = BuildRankingWho(
            badge, BuildRankingAvatar(name, row.AvatarUrl, RankingTableLayout.AvatarSize(fluid.RowHeight)), name,
            nameHex: isMe ? "#FFFFFF" : "#A8BCD2",
            nameSize: fluid.NameSize, nameWeight: FontWeights.Medium, avatarOpacity: 0.8,
            isMe, row.Streak, placementRow: true, inactive: false);
        grid.Children.Add(WithColumn(who, CellOf(specs, RankingColumn.Player)));

        var elo = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        // The figure is the viewer's own and nobody else's (see the summary).
        var shownRating = isMe ? row.Rating ?? ownRating : null;
        var figure = new TextBlock
        {
            FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"),
            FontSize = narrow ? (double)Application.Current.FindResource("MpRankNameSize") : fluid.EloSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = Solid("#F0F5FB"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = TooltipHelper.Wrap(shownRating.HasValue
                ? Strings.Get("MpEloProvisionalTip")
                : HiddenPlacementTip(row.PlacementPlayed, row.PlacementRequired)),
            Tag = shownRating.HasValue ? null : HiddenPlacementTag,
        };
        if (shownRating is double own)
        {
            figure.Inlines.Add(new System.Windows.Documents.Run(PlacementView.RatingText(own)));
            figure.Inlines.Add(new System.Windows.Documents.Run("?")
            {
                Foreground = (Brush)Application.Current.FindResource("MpCaution"),
            });
        }
        // "1571? Placement 6/10" on one line (design 59), or 55b's "1534? 2/5" - or, on anybody
        // else's row, only the progress. The en space is the handoff's 6-px gap: a Run takes no
        // margin, and with no figure in front of it there is nothing to keep a gap from.
        figure.Inlines.Add(new System.Windows.Documents.Run((shownRating.HasValue ? "\u2002" : "") + (narrow
            ? Strings.Format("MpPlacementProgressShort", row.PlacementPlayed, row.PlacementRequired)
            : Strings.Format("MpPlacementProgress", row.PlacementPlayed, row.PlacementRequired)))
        {
            FontFamily = (FontFamily)Application.Current.FindResource("BodyFont"),
            FontSize = (double)Application.Current.FindResource(narrow ? "MpRankSmallSize" : "MpLabelSize"),
            FontWeight = FontWeights.Normal,
            Foreground = Solid("#A8BCD2"),
        });
        elo.Children.Add(figure);

        var segments = BuildPlacementSegments(
            PlacementView.Segments(row.PlacementPlayed, row.PlacementRequired, isMe ? ownResults : null),
            height: RankingTableLayout.SegmentHeight, gap: RankingTableLayout.SegmentGap, profile: false);
        // The ELO column grows with the window; the segments stop at 300 px with the bar.
        var segmentHost = CappedLeft(segments);
        segmentHost.Margin = new Thickness(0, RankingTableLayout.BarGap + 1, 0, 0);
        elo.Children.Add(segmentHost);
        grid.Children.Add(WithColumn(elo, CellOf(specs, RankingColumn.Rating)));

        // No peak before placement is over (the server's rule): a dash that says when it comes.
        // The viewer's own results are already in the segments above.
        var peakCol = CellOf(specs, RankingColumn.Peak);
        if (peakCol >= 0)
        {
            var dash = RankingFigure(Strings.Get("MpDash"), "MpTextDim", FontWeights.Normal, right: false);
            dash.Tag = RankingPeakTag;
            dash.ToolTip = TooltipHelper.Wrap(Strings.Get("MpRankPeakPlacementTip"));
            grid.Children.Add(WithColumn(dash, peakCol));
        }

        var plate = RankingPlate(grid, row, isMe, Solid("#05FFFFFF"));
        if (isMe)
        {
            plate.Cursor = System.Windows.Input.Cursors.Hand;
            plate.MouseLeftButtonUp += (_, _) => OpenOwnProfileFromRanking();
        }
        return plate;
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
        FrameworkElement badge, FrameworkElement avatar, string name, string nameHex,
        double nameSize, FontWeight nameWeight, double avatarOpacity,
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
        avatar.Margin = new Thickness(10, 0, 0, 0);
        // A placement row's picture is dimmed with the rest of it (design 59). An Opacity on a
        // picture costs nothing: ClearType is a text property.
        avatar.Opacity = avatarOpacity;
        who.Children.Add(WithColumn(avatar, 1));
        who.Children.Add(WithColumn(new TextBlock
        {
            Text = name,
            Margin = new Thickness(10, 0, 0, 0),
            Foreground = Solid(nameHex),
            FontSize = nameSize,
            FontWeight = nameWeight,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        }, 2));

        // After the name, in design 93's order: "YOU", then the streak, then INACTIVE.
        var tags = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (isMe)
        {
            tags.Children.Add(new TextBlock
            {
                Text = Strings.Get("MpRankYouTag"),
                Margin = new Thickness(10, 0, 0, 0),
                FontSize = (double)Application.Current.FindResource("MpMicroSize"),
                FontWeight = FontWeights.Bold,
                Foreground = Solid("#9FD0F5"),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        if (StreakView.ShowsPill(streak))
        {
            var pill = BuildStreakPill(streak, placementRow, ownRing: false, table: true);
            pill.Margin = new Thickness(10, 0, 0, 0);
            tags.Children.Add(pill);
        }
        if (inactive)
        {
            var tag = BuildInactiveTag();
            tag.Margin = new Thickness(10, 0, 0, 0);
            tags.Children.Add(tag);
        }
        if (tags.Children.Count > 0) who.Children.Add(WithColumn(tags, 3));
        return who;
    }

    /// <summary>The <c>Tag</c> of a row's avatar and of its PEAK cell, for the tests.</summary>
    internal const string RankingAvatarTag = "RankingAvatar";

    internal const string RankingPeakTag = "RankingPeak";

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

    /// <summary>The INACTIVE tag (design 93): an amber pill with a dot. Thirty days without a rated
    /// match; the place stays, and the hover says so.</summary>
    internal static FrameworkElement BuildInactiveTag()
    {
        var words = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 6, Height = 6, Fill = Solid("#F4900C"), VerticalAlignment = VerticalAlignment.Center,
        });
        words.Children.Add(new TextBlock
        {
            Text = Strings.Get("MpRankInactiveTag"),
            Margin = new Thickness(5, 0, 0, 0),
            FontSize = (double)Application.Current.FindResource("MpMicroSize"),
            FontWeight = FontWeights.Bold,
            Foreground = Solid("#F5BD76"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        return new Border
        {
            Tag = "InactiveTag",
            Height = 20,
            Padding = new Thickness(8, 0, 8, 0),
            CornerRadius = new CornerRadius(10),
            Background = Solid("#1AF4900C"),
            BorderBrush = Solid("#66F4900C"),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = TooltipHelper.Wrap(Strings.Format("RkInactiveTip", InactiveAfterDays)),
            Child = words,
        };
    }

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

    /// <summary>A monospace figure for the PEAK column.</summary>
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
    private static UIElement BuildRatingBar(double fraction, Brush fillBrush)
    {
        var bar = new Grid();
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(fraction, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, 1 - fraction), GridUnitType.Star) });
        var fill = new Border
        {
            CornerRadius = new CornerRadius(3),
            Background = fillBrush,
            Tag = fraction,
        };
        Grid.SetColumn(fill, 0);
        bar.Children.Add(fill);
        return bar;
    }

    /// <summary>Opens the player guide to the rating — the footnote's link.</summary>
    private void RankingEloHelpButton_Click(object sender, RoutedEventArgs e)
        => SafeUrl.TryOpen(Models.LauncherConfig.RatingHelpUrl);
}
