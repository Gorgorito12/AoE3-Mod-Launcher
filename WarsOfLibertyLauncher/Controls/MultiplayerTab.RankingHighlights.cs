using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// Ranking › Highlights: the month's highlights IN DEPTH — one card per highlight with its top
/// five, the figure and a detail line — over the whole page, the third option beside 1v1 and
/// Teams. The Rooms page's data strip names only the first of three (design 60); this is where
/// the rest are, the four added later included (most wins, best win rate, biggest upset,
/// civilization of the month). No design handoff covers this view; it is built from the page's
/// own tokens (cards <c>MpPanel</c> + <c>MpRimFaint</c>, radius 10, <c>MpTextLabel</c> titles).
///
/// <para><b>Its data comes from its own route</b> (<c>GET /stats/highlights</c>), fetched only
/// when the view opens and kept a minute: the community payload, polled once a minute by every
/// launcher, carries only the first of each list. An older server answers 404 and the view says
/// so instead of drawing nothing.</para>
/// </summary>
public partial class MultiplayerTab
{
    /// <summary>The month's top lists, both months; null until fetched.</summary>
    private MonthlyHighlights? _rankingLeaders;

    private DateTime _rankingLeadersFetchedUtc = DateTime.MinValue;
    private bool _rankingLeadersInFlight;

    /// <summary>What the last fetch said when it brought nothing: null (fine), 404, or another failure.</summary>
    private int? _rankingLeadersFailure;

    /// <summary>Last month is on screen (the month capsule).</summary>
    private bool _rankingLeadersPrevious;

    /// <summary>The climb and streak cards show the team ladder.</summary>
    private bool _rankingLeadersClimbTeam;
    private bool _rankingLeadersStreakTeam;

    /// <summary>How long a fetched answer is kept: the server memoises it longer still.</summary>
    private static readonly TimeSpan RankingLeadersMaxAge = TimeSpan.FromSeconds(60);

    /// <summary>The width one card column asks for; the grid has as many as fit, 1 to 4.</summary>
    internal const double LeaderColumnMinWidth = 300;

    /// <summary>The <c>Tag</c> of each card and of each row, for the tests.</summary>
    internal const string LeaderCardTag = "LeaderCard";
    internal const string LeaderRowTag = "LeaderRow";

    private UniformGrid? _rankingLeadersGrid;

    /// <summary>
    /// Draw the Highlights view from what is known, and ask the server when that is stale. Called
    /// by <see cref="RenderRanking"/> in Highlights mode.
    /// </summary>
    private void RenderRankingHighlights()
    {
        ApplyPreviewChip();
        RankingHighlightsBody.Children.Clear();
        _rankingLeadersGrid = null;

        var data = _eloPreview || _demoStats ? EloDemoData.Highlights() : _rankingLeaders;
        if (!(_eloPreview || _demoStats)) _ = RefreshRankingLeadersAsync();

        var current = data?.Current;
        var previous = data?.Previous;
        var showingPrevious = _rankingLeadersPrevious && previous != null;
        var month = showingPrevious ? previous : current;

        // The month capsule: "October · September", the shown one lit.
        RankingMonthCurrent.Content = MonthLabel(current);
        RankingMonthPrevious.Content = MonthLabel(previous);
        RankingMonthCurrent.Tag = showingPrevious ? null : "active";
        RankingMonthPrevious.Tag = showingPrevious ? "active" : null;
        RankingMonthCurrent.Visibility = current != null ? Visibility.Visible : Visibility.Collapsed;
        RankingMonthPrevious.Visibility = previous != null ? Visibility.Visible : Visibility.Collapsed;
        RankingMonthCapsule.Visibility = current != null && previous != null ? Visibility.Visible : Visibility.Collapsed;

        RankingSubtitleText.Text = month == null
            ? ""
            // Inside a sentence the month keeps its own case ("en octubre", "in October"); only
            // the capsule, a label, capitalises it.
            : Strings.Format(month.SoFar ? "MpHlLeadersSubtitleSoFar" : "MpHlLeadersSubtitle",
                month.TotalRated.ToString("N0", Strings.Culture),
                HighlightsView.MonthName(month.Month, Strings.Culture) ?? month.Month);

        if (data == null)
        {
            RankingHighlightsBody.Children.Add(LeadersNotice(
                _rankingLeadersFailure == 404 ? "MpHlLeadersUnavailable"
                : _rankingLeadersFailure != null ? "MpHlLeadersFailed"
                : "MpHlLeadersLoading"));
            return;
        }

        var cards = HighlightLeadersView.Cards(month, _rankingLeadersClimbTeam, _rankingLeadersStreakTeam,
            Strings.Culture, (key, args) => Strings.Format(key, args));
        if (cards.Count == 0)
        {
            RankingHighlightsBody.Children.Add(LeadersNotice("MpHlLeadersEmpty", HighlightsView.MonthName(month?.Month, Strings.Culture) ?? month?.Month ?? ""));
            return;
        }

        // Each card carries the gap to the next as its own margin; the grid takes the last
        // column's back so the right edge lines up with the page.
        var grid = new UniformGrid
        {
            Columns = LeaderColumns(cards.Count),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, -PageGap, 0),
        };
        var meId = RankingViewerId;
        foreach (var card in cards) grid.Children.Add(BuildLeaderCard(card, meId));
        _rankingLeadersGrid = grid;
        RankingHighlightsBody.Children.Add(grid);

        _ = EnsureLeaderCivArtAsync(cards);
    }

    /// <summary>
    /// How many columns of cards (design 93f): all of them in one row if each gets 300 px, else
    /// two rows, else three, else two columns, else one.
    /// </summary>
    internal static int LeaderColumns(int cards, double pageWidth, double gap)
    {
        if (cards <= 0 || !(pageWidth > 0)) return Math.Max(1, Math.Min(cards, 3));
        foreach (var k in new[] { cards, (cards + 1) / 2, (cards + 2) / 3, 2, 1 })
            if (k > 0 && (pageWidth - (k - 1) * gap) / k >= LeaderColumnMinWidth) return k;
        return 1;
    }

    private int LeaderColumns(int cards) => LeaderColumns(cards, RankingPage?.ActualWidth ?? 0, PageGap);

    /// <summary>Follow the page's width without rebuilding the cards.</summary>
    private void UpdateRankingHighlightsColumns()
    {
        if (_rankingLeadersGrid == null) return;
        var columns = LeaderColumns(_rankingLeadersGrid.Children.Count);
        if (_rankingLeadersGrid.Columns != columns) _rankingLeadersGrid.Columns = columns;
    }

    private static string MonthLabel(MonthHighlights? month)
    {
        if (month == null) return "";
        var name = HighlightsView.MonthName(month.Month, Strings.Culture) ?? month.Month;
        return name.Length > 0 ? char.ToUpper(name[0], Strings.Culture) + name[1..] : name;
    }

    /// <summary>Fetch the top lists when they are stale; repaint when they arrive.</summary>
    private async Task RefreshRankingLeadersAsync(bool force = false)
    {
        if (_session?.Api == null || _rankingLeadersInFlight) return;
        if (!force && DateTime.UtcNow - _rankingLeadersFetchedUtc < RankingLeadersMaxAge) return;
        _rankingLeadersInFlight = true;
        try
        {
            var answer = await _session.Api.GetHighlightsAsync();
            _rankingLeaders = answer;
            _rankingLeadersFailure = null;
            _rankingLeadersFetchedUtc = DateTime.UtcNow;
        }
        catch (LobbyApiException ex)
        {
            _rankingLeadersFailure = ex.Status;
            _rankingLeadersFetchedUtc = DateTime.UtcNow;
            DiagnosticLog.Write($"Ranking highlights: HTTP {ex.Status} {ex.Code}");
        }
        catch (Exception ex)
        {
            // Stamped like a 404 is, or a dead network would refetch on every repaint, and the
            // repaint after a failure IS a repaint.
            _rankingLeadersFailure = -1;
            _rankingLeadersFetchedUtc = DateTime.UtcNow;
            DiagnosticLog.Write($"Ranking highlights: {ex.Message}");
        }
        finally
        {
            _rankingLeadersInFlight = false;
        }
        if (_rankingMode == RankingMode.Highlights && _activeSubtab == Subtab.Ranking) RenderRanking();
    }

    private void RankingMonthCurrent_Click(object sender, RoutedEventArgs e)
    {
        _rankingLeadersPrevious = false;
        RenderRanking();
    }

    private void RankingMonthPrevious_Click(object sender, RoutedEventArgs e)
    {
        _rankingLeadersPrevious = true;
        RenderRanking();
    }

    private static TextBlock LeadersNotice(string key, params object[] args) => new()
    {
        Text = args.Length == 0 ? Strings.Get(key) : Strings.Format(key, args),
        Foreground = (Brush)Application.Current.FindResource("MpTextMuted"),
        FontSize = (double)Application.Current.FindResource("MpBodySize"),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(4, 8, 4, 0),
    };

    /// <summary>
    /// One card (design 93f): a glass plate with its title, the "1v1 / Teams" switch for climb and
    /// streak when both ladders have someone, its rule in one muted line, then up to five rows.
    /// </summary>
    private FrameworkElement BuildLeaderCard(LeaderCard card, string? meId)
    {
        var stack = new StackPanel();

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var words = new StackPanel();
        var title = new TextBlock
        {
            Text = Strings.Get(card.TitleKey),
            Foreground = Solid("#F0F5FB"),
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        title.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBodyStrong");
        words.Children.Add(title);
        if (!string.IsNullOrWhiteSpace(card.Rule))
        {
            var rule = new TextBlock
            {
                Text = card.Rule,
                Foreground = Solid("#8C9CB1"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
            };
            rule.SetResourceReference(TextBlock.FontSizeProperty, "MpMetaSize");
            words.Children.Add(rule);
        }
        head.Children.Add(words);
        if (card.HasBothLadders)
        {
            var sw = new StackPanel { Orientation = Orientation.Horizontal };
            sw.Children.Add(LadderSwitch(card, team: false));
            sw.Children.Add(LadderSwitch(card, team: true));
            var capsule = new Border
            {
                Child = sw,
                Margin = new Thickness(10, 0, 0, 0),
                Padding = new Thickness(3),
                CornerRadius = new CornerRadius(13),
                Background = Solid("#66050A12"),
                VerticalAlignment = VerticalAlignment.Top,
            };
            Grid.SetColumn(capsule, 1);
            head.Children.Add(capsule);
        }
        stack.Children.Add(head);

        var rows = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        foreach (var row in card.Rows) rows.Children.Add(BuildLeaderRow(row, meId));
        stack.Children.Add(rows);

        var plate = new Border
        {
            Tag = LeaderCardTag,
            Child = stack,
            Margin = new Thickness(0, 0, PageGap, PageGap),
            Padding = new Thickness(16, 14, 14, 12),
            CornerRadius = new CornerRadius(14),
            BorderBrush = Solid("#12FFFFFF"),
            BorderThickness = new Thickness(1),
        };
        // The fill follows the transparency level, like every other card of the page.
        plate.SetResourceReference(Border.BackgroundProperty, "GlassPanel");
        return plate;
    }

    /// <summary>One side of the climb / streak card's "1v1 / Teams" switch: a small capsule.</summary>
    private Button LadderSwitch(LeaderCard card, bool team)
    {
        var button = new Button
        {
            Content = Strings.Get(team ? "MpRankingModeTeam" : "MpRankingModeSolo"),
            Style = (Style)FindResource("RkSwitchButton"),
            Margin = new Thickness(team ? 3 : 0, 0, 0, 0),
            Tag = card.Team == team ? "active" : null,
        };
        // Its name ("1v1" / "Teams") is the page's mode buttons' too, so the testbed presses it by id.
        System.Windows.Automation.AutomationProperties.SetAutomationId(
            button, $"RkSwitch:{card.Kind}:{(team ? "team" : "solo")}");
        button.Click += (_, _) =>
        {
            if (card.Kind == HighlightCellKind.TopClimb) _rankingLeadersClimbTeam = team;
            else _rankingLeadersStreakTeam = team;
            RenderRanking();
        };
        return button;
    }

    /// <summary>
    /// One row (design 93f): place, avatar (or the civilization's flag), name over its detail,
    /// then the figure or the streak pill. The first of each card is soft gold; the viewer's own
    /// is blue with a rim and "YOU", whatever its place.
    /// </summary>
    private FrameworkElement BuildLeaderRow(LeaderRow row, string? meId)
    {
        var isMe = !string.IsNullOrEmpty(meId) && row.UserIds.Any(id => string.Equals(id, meId, StringComparison.Ordinal));
        var first = row.Place == 1;

        var grid = new Grid { MinHeight = 32 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var place = new TextBlock
        {
            Text = row.Place.ToString(Strings.Culture),
            FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"),
            Foreground = Solid(first ? "#F4D9A0" : "#8C9CB1"),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        place.SetResourceReference(TextBlock.FontSizeProperty, "MpMetaSize");
        grid.Children.Add(place);

        FrameworkElement? face = null;
        if (row.Civ != null)
        {
            var flag = DeckCardNames.Peek(row.CivModId)?.CivIconOf(row.Civ);
            if (flag != null)
            {
                var chip = new Border
                {
                    Width = 28,
                    Height = 19,
                    CornerRadius = new CornerRadius(3),
                    Background = new ImageBrush(flag) { Stretch = Stretch.UniformToFill },
                    BorderBrush = (Brush)Application.Current.FindResource("MpFlagRim"),
                    BorderThickness = new Thickness(1),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                RenderOptions.SetBitmapScalingMode(chip, BitmapScalingMode.HighQuality);
                face = chip;
            }
        }
        else
        {
            face = BuildAvatarDisc(row.Name, row.AvatarUrl, 28);
        }
        if (face != null)
        {
            face.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetColumn(face, 1);
            grid.Children.Add(face);
        }

        var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        // A grid, not a horizontal StackPanel: that one measures the name with no limit, so it
        // would never trim and "YOU" would be pushed out of the row.
        var nameLine = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        nameLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        nameLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = new TextBlock
        {
            Text = row.Name,
            Foreground = Solid("#F0F5FB"),
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        name.SetResourceReference(TextBlock.FontSizeProperty, "MpBodySize");
        nameLine.Children.Add(name);
        if (isMe)
        {
            var you = new TextBlock
            {
                Text = Strings.Get("MpRankYouTag"),
                Margin = new Thickness(8, 0, 0, 0),
                FontWeight = FontWeights.Bold,
                Foreground = Solid("#9FD0F5"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            you.SetResourceReference(TextBlock.FontSizeProperty, "MpMicroSize");
            Grid.SetColumn(you, 1);
            nameLine.Children.Add(you);
        }
        who.Children.Add(nameLine);
        if (!string.IsNullOrWhiteSpace(row.Detail))
        {
            var detail = new TextBlock
            {
                Text = row.Detail,
                Foreground = Solid("#A8BCD2"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 2, 0, 0),
            };
            detail.SetResourceReference(TextBlock.FontSizeProperty, "MpMetaSize");
            who.Children.Add(detail);
        }
        Grid.SetColumn(who, 2);
        grid.Children.Add(who);

        FrameworkElement figure;
        if (row.Streak is int wins)
        {
            figure = BuildStreakPill(wins, table: true);
        }
        else
        {
            var text = new TextBlock
            {
                Text = row.Figure ?? "",
                FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"),
                FontWeight = FontWeights.SemiBold,
                Foreground = Solid(row.Positive ? "#7FE0A8" : "#F0F5FB"),
            };
            text.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBodyStrong");
            figure = text;
        }
        figure.VerticalAlignment = VerticalAlignment.Center;
        figure.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(figure, 3);
        grid.Children.Add(figure);

        return new Border
        {
            Tag = LeaderRowTag,
            Child = grid,
            MinHeight = 46,
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, row.Place > 1 ? 3 : 0, 0, 0),
            CornerRadius = new CornerRadius(9),
            Background = isMe ? Solid("#1455ACEE")
                : first ? Across((Rgba("#F4D9A0", .13), 0), (Rgba("#F4D9A0", .02), 1))
                : Brushes.Transparent,
            BorderBrush = isMe ? Solid("#5955ACEE") : first ? Solid("#33F4D9A0") : Brushes.Transparent,
            BorderThickness = new Thickness(1),
        };
    }

    /// <summary>
    /// Read the flags of the civilization card off each mod's files, then repaint. Cheap on every
    /// draw: the vocabulary cache answers from memory when it covers the names, and the page
    /// repaints only when the answer is a new instance — which is what stops it looping.
    /// </summary>
    private async Task EnsureLeaderCivArtAsync(System.Collections.Generic.IReadOnlyList<LeaderCard> cards)
    {
        try
        {
            var learned = false;
            foreach (var group in cards.SelectMany(c => c.Rows)
                         .Where(r => !string.IsNullOrWhiteSpace(r.CivModId) && !string.IsNullOrWhiteSpace(r.Civ))
                         .GroupBy(r => r.CivModId!, StringComparer.OrdinalIgnoreCase))
            {
                var before = DeckCardNames.Peek(group.Key);
                await DeckCardNames.ResolveAsync(group.Key, GetInstallPath, Array.Empty<string>(),
                    group.Select(r => r.Civ!).Distinct(StringComparer.Ordinal).ToList());
                var after = DeckCardNames.Peek(group.Key);
                if (after != null && !ReferenceEquals(before, after)) learned = true;
            }
            if (learned && _rankingMode == RankingMode.Highlights && _activeSubtab == Subtab.Ranking) RenderRanking();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Ranking highlights civ art: {ex.Message}");
        }
    }
}
