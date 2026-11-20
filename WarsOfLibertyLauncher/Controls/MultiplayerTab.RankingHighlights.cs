using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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
/// civilization of the month).
///
/// <para><b>It is a mosaic (design 119, <c>docs/design_destacados</c>).</b> Four columns, two on a
/// laptop (<see cref="BentoPanel"/>), each card as tall as its content and drawn the way its
/// figure asks: the climb with where it started and where it got to, bars against the first place,
/// one mark per win of a streak, a ring for a win rate, the upset as a duel, and the civilization
/// of the month over two columns. The first place of each is on soft gold; the viewer's own row
/// is blue with "YOU", which wins over the gold. Each player wears his rank badge, taken from the
/// ladder the page already has; a place with none keeps the room for it.</para>
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

    /// <summary>The <c>Tag</c> of each card and of each row, for the tests.</summary>
    internal const string LeaderCardTag = "LeaderCard";
    internal const string LeaderRowTag = "LeaderRow";

    /// <summary>The mosaic on screen, for the tests.</summary>
    internal BentoPanel? RankingLeadersMosaic { get; private set; }

    /// <summary>
    /// Draw the Highlights view from what is known, and ask the server when that is stale. Called
    /// by <see cref="RenderRanking"/> in Highlights mode.
    /// </summary>
    private void RenderRankingHighlights()
    {
        ApplyPreviewChip();
        RankingHighlightsBody.Children.Clear();
        RankingLeadersMosaic = null;

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

        // The mosaic decides its own columns from the width it gets, so a resize moves the cards
        // that are already there.
        var mosaic = new BentoPanel { Gap = 14, VerticalAlignment = VerticalAlignment.Top };
        var meId = RankingViewerId;
        foreach (var card in cards) mosaic.Children.Add(BuildLeaderCard(card, meId));
        RankingLeadersMosaic = mosaic;
        RankingHighlightsBody.Children.Add(mosaic);

        _ = EnsureLeaderCivArtAsync(cards);
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

    // --- The mosaic's cards (design 119) --------------------------------------------------------

    /// <summary>The pictogram of each highlight, in a 20-unit box: a stroke, never a character.</summary>
    internal static readonly System.Collections.Generic.IReadOnlyDictionary<HighlightCellKind, string> LeaderIcons =
        new System.Collections.Generic.Dictionary<HighlightCellKind, string>
        {
            [HighlightCellKind.TopClimb] = "M3 14l4.5-5 3.5 3 6-7M12.5 5H17v4.5",
            [HighlightCellKind.MostWins] = "M6 3.5h8v3.5a4 4 0 0 1-8 0zM6 5H3.5v1A2.5 2.5 0 0 0 6 8.5M14 5h2.5v1A2.5 2.5 0 0 1 14 8.5M10 11v3M7 16.5h6",
            [HighlightCellKind.MostMatches] = "M3.5 3.5l9.5 9.5M10.5 15.5l5-5M13 13l3.5 3.5M16.5 3.5L7 13M4.5 10.5l5 5M7 13l-3.5 3.5",
            [HighlightCellKind.BestStreak] = "M10 2.5c.6 2.8 4 4.5 4 8.3a4 4 0 0 1-8 0c0-1.4.5-2.5 1.3-3.4.4 1.2 1 1.8 1.8 2C8.9 7.5 9 4.5 10 2.5z",
            [HighlightCellKind.BestWinRate] = "M5 15L15 5M6.5 4.5a2 2 0 1 0 0 4 2 2 0 0 0 0-4zM13.5 11.5a2 2 0 1 0 0 4 2 2 0 0 0 0-4z",
            [HighlightCellKind.BiggestUpset] = "M11 2.5L4.5 11.5h5l-1 6 7-9h-5z",
            [HighlightCellKind.TopCiv] = "M5 17.5V3M5 3.5h9l-1.8 3 1.8 3H5",
        };

    /// <summary>The flame of the streak pill, filled.</summary>
    internal const string LeaderFlame = "M10 1.5c.6 3 4.5 4.8 4.5 9a4.5 4.5 0 0 1-9 0c0-1.5.6-2.8 1.5-3.8.4 1.3 1.1 2 2 2.3C8.8 7 8.8 3.8 10 1.5z";

    private const string HlGold = "#E6C06A";
    private const string HlGoldText = "#F3D995";
    private const string HlGreen = "#7FE0A8";

    /// <summary>A drawing in a 20-unit box, shown at <paramref name="size"/>.</summary>
    private static FrameworkElement HlIcon(string data, double size, Brush? stroke, Brush? fill)
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(data),
            Stroke = stroke,
            Fill = fill,
            StrokeThickness = 1.6,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
        var box = new Canvas { Width = 20, Height = 20 };
        box.Children.Add(path);
        return new Viewbox { Width = size, Height = size, Child = box, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
    }

    private static TextBlock HlText(string? text, string sizeKey, string hex, bool mono = false, FontWeight? weight = null, bool trim = false)
    {
        var block = new TextBlock
        {
            Text = text ?? "",
            Foreground = Solid(hex),
            FontWeight = weight ?? FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (trim) block.TextTrimming = TextTrimming.CharacterEllipsis;
        if (mono) block.FontFamily = (FontFamily)Application.Current.FindResource("MonoFont");
        block.SetResourceReference(TextBlock.FontSizeProperty, sizeKey);
        return block;
    }

    /// <summary>The first place's plate: soft gold, or the viewer's blue, which wins over it.</summary>
    private static (Brush Fill, Brush Rim) HlFirstDress(bool isMe)
        => isMe
            ? (Solid("#1455ACEE"), Solid("#5955ACEE"))
            : (Diagonal((Rgba(HlGold, .2), 0), (Rgba(HlGold, .05), .62)), Solid("#57E6C06A"));

    private bool IsLeaderMe(LeaderRow row, string? meId)
        => !string.IsNullOrEmpty(meId) && row.UserIds.Any(id => string.Equals(id, meId, StringComparison.Ordinal));

    /// <summary>
    /// One card of the mosaic: a glass plate with its pictogram, title and rule (and the
    /// "1v1 / Teams" switch for climb and streak when both ladders have someone), then its places
    /// drawn the way its figure asks: a block for the first, bars, marks, rings, a duel.
    /// </summary>
    private FrameworkElement BuildLeaderCard(LeaderCard card, string? meId)
    {
        var stack = new StackPanel();
        stack.Children.Add(BuildLeaderHead(card));

        FrameworkElement body = card.Kind switch
        {
            HighlightCellKind.TopClimb => BuildClimbBody(card, meId),
            HighlightCellKind.BiggestUpset => BuildUpsetBody(card, meId),
            HighlightCellKind.TopCiv => BuildCivBody(card),
            HighlightCellKind.BestWinRate => BuildRateBody(card, meId),
            _ => BuildBarBody(card, meId),
        };
        body.Margin = new Thickness(0, 12, 0, 0);
        stack.Children.Add(body);

        var plate = new Border
        {
            Tag = LeaderCardTag,
            Child = stack,
            Padding = new Thickness(14, 14, 14, 12),
            CornerRadius = new CornerRadius(14),
            BorderBrush = Solid("#14FFFFFF"),
            BorderThickness = new Thickness(1),
        };
        // The fill follows the transparency level, like every other card of the page.
        plate.SetResourceReference(Border.BackgroundProperty, "GlassPanel");
        // The civilization's card lies over two columns: it is the one with two halves.
        BentoPanel.SetSpan(plate, card.Kind == HighlightCellKind.TopCiv ? 2 : 1);
        return plate;
    }

    private FrameworkElement BuildLeaderHead(LeaderCard card)
    {
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        head.Children.Add(new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = Solid("#21E6C06A"),
            BorderBrush = Solid("#4DE6C06A"),
            BorderThickness = new Thickness(1),
            Child = LeaderIcons.TryGetValue(card.Kind, out var icon) ? HlIcon(icon, 16, Solid(HlGoldText), null) : null,
        });

        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(HlText(Strings.Get(card.TitleKey), "HlTitleSize", "#F0F5FB", weight: FontWeights.SemiBold, trim: true));
        if (!string.IsNullOrWhiteSpace(card.Rule))
        {
            var rule = HlText(card.Rule, "HlRuleSize", "#8C9CB1", trim: true);
            rule.Margin = new Thickness(0, 3, 0, 0);
            words.Children.Add(rule);
        }
        Grid.SetColumn(words, 1);
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
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(capsule, 2);
            head.Children.Add(capsule);
        }
        return head;
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

    // --- Pieces every card shares ---------------------------------------------------------------

    /// <summary>The <c>Tag</c> of the room a row keeps for a rank badge, filled or not.</summary>
    internal const string LeaderBadgeSlotTag = "LeaderBadgeSlot";

    /// <summary>
    /// The player's rank badge on the ladder the card shows, still, or null when he has no place
    /// on the page's ladder: being placed, a private rating, or past the page the server sent.
    /// </summary>
    private FrameworkElement? BuildLeaderBadge(string? userId, bool team, double width)
    {
        var ladder = team ? CommunityStatsView.TeamRows(_communityStats) : CommunityStatsView.Rows(_communityStats);
        if (HighlightsMosaic.RankOf(userId, ladder) is not int rank) return null;
        var shown = new ShownBadge(team ? BadgeKind.Team : BadgeKind.Solo, RankAges.For(rank, LadderSize(team)), rank, null, 0);
        // No click of its own: the row under it opens the player's profile.
        return RankBadge.BuildFor(shown, width, "hl-" + userId,
            RankBadgeTips.Text(shown, CommunityStatsView.RequiredDecided(_communityStats)), onClick: null, animated: false);
    }

    /// <summary>
    /// The badge before the photo (16 wide, 7 from it) and the photo. A place with no badge keeps
    /// the room, so the photos of a card line up. A row naming several players wears none.
    /// </summary>
    private FrameworkElement BuildLeaderFace(LeaderRow row, double size)
    {
        var team = row.Facts.Team;
        var slot = new Grid
        {
            Tag = LeaderBadgeSlotTag,
            Width = RankBadge.FootprintWidth(16, team ? BadgeKind.Team : BadgeKind.Solo),
            Height = 19,
            Margin = new Thickness(0, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (row.UserIds.Count == 1 && BuildLeaderBadge(row.UserIds[0], team, 16) is { } badge)
        {
            badge.HorizontalAlignment = HorizontalAlignment.Right;
            badge.VerticalAlignment = VerticalAlignment.Center;
            slot.Children.Add(badge);
        }
        var face = BuildAvatarDisc(row.Name, row.AvatarUrl, size);
        face.VerticalAlignment = VerticalAlignment.Center;
        var group = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        group.Children.Add(slot);
        group.Children.Add(face);
        return group;
    }

    /// <summary>The first place's photo: a gold ring round it and the badge on its corner (6 outside).</summary>
    private FrameworkElement BuildLeaderHeroFace(string name, string? avatarUrl, string? userId, bool team, bool ring)
    {
        const double size = 46;
        var host = new Grid { Width = size + 7, Height = size + 7, VerticalAlignment = VerticalAlignment.Center };
        var disc = BuildAvatarDisc(name, avatarUrl, size);
        disc.HorizontalAlignment = HorizontalAlignment.Center;
        disc.VerticalAlignment = VerticalAlignment.Center;
        host.Children.Add(disc);
        if (ring)
            host.Children.Add(new Border
            {
                CornerRadius = new CornerRadius((size + 7) / 2),
                BorderBrush = Solid("#D9E6C06A"),
                BorderThickness = new Thickness(1.5),
                IsHitTestVisible = false,
            });
        if (userId != null && BuildLeaderBadge(userId, team, 20) is { } badge)
        {
            badge.HorizontalAlignment = HorizontalAlignment.Right;
            badge.VerticalAlignment = VerticalAlignment.Bottom;
            badge.Margin = new Thickness(0, 0, -2.5, -2.5);
            host.Children.Add(badge);
        }
        return host;
    }

    private static TextBlock HlPlace(int place, bool first)
    {
        var text = HlText(place.ToString(Strings.Culture), "HlDetailSize", first ? HlGoldText : "#8C9CB1", mono: true, weight: FontWeights.SemiBold);
        text.Width = 14;
        text.TextAlignment = TextAlignment.Center;
        text.Margin = new Thickness(0, 0, 10, 0);
        return text;
    }

    /// <summary>The name, trimmed when it runs out of room, with "YOU" right after it on the viewer's row.</summary>
    private static FrameworkElement HlName(string name, bool isMe, string sizeKey, string hex = "#F0F5FB")
    {
        var text = HlText(name, sizeKey, hex, weight: FontWeights.SemiBold, trim: true);
        if (!isMe) return text;
        // A grid that hugs its content: the name takes what it needs, and what is left once the
        // tag has its room. A horizontal StackPanel would never trim it.
        var line = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.Children.Add(text);
        var you = HlText(Strings.Get("MpRankYouTag"), "MpMicroSize", "#9FD0F5", weight: FontWeights.Bold);
        you.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(you, 1);
        line.Children.Add(you);
        return line;
    }

    /// <summary>
    /// A detail that follows a name on the same line: when a long name leaves it only a sliver, it
    /// is not drawn at all rather than as a stray dot. Opacity, never visibility or size: this
    /// runs from the text own SizeChanged and must not move the size it reacts to.
    /// </summary>
    private static void HideWhenCramped(TextBlock detail)
        => detail.SizeChanged += (_, e) => detail.Opacity = e.NewSize.Width < 28 ? 0 : 1;

    /// <summary>A bar that fills <paramref name="share"/> of its track.</summary>
    private static FrameworkElement HlBar(double share, double height, Brush fill)
    {
        share = Math.Clamp(share, 0, 1);
        var bar = new Grid { Height = height, IsHitTestVisible = false };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(share, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - share, GridUnitType.Star) });
        var track = new Border { CornerRadius = new CornerRadius(3), Background = Solid("#12FFFFFF") };
        Grid.SetColumnSpan(track, 2);
        bar.Children.Add(track);
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = fill });
        return bar;
    }

    /// <summary>A plate round one place: the tag the tests count, the first's dress, the viewer's blue.</summary>
    private Border HlPlate(LeaderRow row, string? meId, FrameworkElement child, Thickness padding, double radius, bool hairline = false)
    {
        var isMe = IsLeaderMe(row, meId);
        var first = row.Place == 1;
        var plate = new Border { Tag = LeaderRowTag, Child = child, Padding = padding, CornerRadius = new CornerRadius(radius) };
        if (isMe || (first && !hairline))
        {
            var (fill, rim) = HlFirstDress(isMe);
            plate.Background = fill;
            plate.BorderBrush = rim;
            plate.BorderThickness = new Thickness(1);
            plate.CornerRadius = new CornerRadius(Math.Max(radius, 10));
        }
        else if (hairline)
        {
            plate.BorderBrush = Solid("#0DFFFFFF");
            plate.BorderThickness = new Thickness(0, 0, 0, 1);
            plate.CornerRadius = default;
            plate.Padding = new Thickness(padding.Left + 1, padding.Top + 1, padding.Right + 1, padding.Bottom);
        }
        else
        {
            // The same 1 px the dressed rows have, unpainted, so every row is the same height.
            plate.BorderBrush = Brushes.Transparent;
            plate.BorderThickness = new Thickness(1);
        }
        // A row about one player opens his profile; a team's row names several and opens none.
        if (row.UserIds.Count == 1 && row.Facts.Rival == null)
            LinkRankingPlayer(plate, isMe, row.UserIds[0], row.Name, null, row.AvatarUrl);
        return plate;
    }

    // --- Biggest climb -----------------------------------------------------------------------

    private FrameworkElement BuildClimbBody(LeaderCard card, string? meId)
    {
        var body = new StackPanel();
        foreach (var row in card.Rows)
            body.Children.Add(row.Place == 1 ? BuildClimbHero(row, meId) : BuildClimbRow(row, meId));
        return body;
    }

    private FrameworkElement BuildClimbHero(LeaderRow row, string? meId)
    {
        var isMe = IsLeaderMe(row, meId);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(BuildLeaderHeroFace(row.Name, row.AvatarUrl, row.UserIds.Count == 1 ? row.UserIds[0] : null, row.Facts.Team, ring: true));

        var words = new StackPanel { Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(HlName(row.Name, isMe, "HlNameFirstSize", "#FFFFFF"));
        if (row.Facts.RatingFrom is int from && row.Facts.RatingTo is int to)
        {
            var line = new Grid { Margin = new Thickness(0, 5, 0, 0) };
            for (var i = 0; i < 3; i++) line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.Children.Add(HlText(from.ToString(System.Globalization.CultureInfo.InvariantCulture), "HlMonoSize", "#DCE7F5", mono: true, weight: FontWeights.SemiBold));
            var arrow = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M1 5h11M8.5 1.5 12 5l-3.5 3.5"),
                Width = 14,
                Height = 10,
                Stroke = Solid(HlGreen),
                StrokeThickness = 1.6,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Margin = new Thickness(6, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(arrow, 1);
            line.Children.Add(arrow);
            var reached = HlText(to.ToString(System.Globalization.CultureInfo.InvariantCulture), "HlMonoSize", "#FFFFFF", mono: true, weight: FontWeights.SemiBold);
            Grid.SetColumn(reached, 2);
            line.Children.Add(reached);
            if (row.Facts.Matches is int played)
            {
                var after = HlText(Strings.Format("MpHlAfterMatches", played.ToString("N0", Strings.Culture)), "HlDetailSize", "#A8BCD2", trim: true);
                after.Margin = new Thickness(6, 0, 0, 0);
                Grid.SetColumn(after, 3);
                line.Children.Add(after);
            }
            words.Children.Add(line);
        }
        else if (!string.IsNullOrWhiteSpace(row.Detail))
        {
            var detail = HlText(row.Detail, "HlDetailSize", "#A8BCD2", trim: true);
            detail.Margin = new Thickness(0, 5, 0, 0);
            words.Children.Add(detail);
        }
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);

        var figure = HlText(row.Figure, "HlHeroFigureSize", HlGreen, mono: true, weight: FontWeights.Bold);
        Grid.SetColumn(figure, 2);
        grid.Children.Add(figure);
        return HlPlate(row, meId, grid, new Thickness(13, 11, 13, 11), 12);
    }

    private FrameworkElement BuildClimbRow(LeaderRow row, string? meId)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(HlPlace(row.Place, false));
        var face = BuildLeaderFace(row, 24);
        Grid.SetColumn(face, 1);
        grid.Children.Add(face);
        var name = HlName(row.Name, IsLeaderMe(row, meId), "HlNameSize");
        name.Margin = new Thickness(10, 0, 10, 0);
        Grid.SetColumn(name, 2);
        grid.Children.Add(name);
        if (row.Facts.RatingFrom is int from && row.Facts.RatingTo is int to)
        {
            var span = HlText(from.ToString(System.Globalization.CultureInfo.InvariantCulture) + " → " + to.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "HlDetailSize", "#A8BCD2", mono: true);
            Grid.SetColumn(span, 3);
            grid.Children.Add(span);
        }
        var figure = HlText(row.Figure, "HlFigureSize", HlGreen, mono: true, weight: FontWeights.Bold);
        figure.MinWidth = 34;
        figure.TextAlignment = TextAlignment.Right;
        figure.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(figure, 4);
        grid.Children.Add(figure);
        var plate = HlPlate(row, meId, grid, new Thickness(3, 5, 3, 6), 0, hairline: true);
        plate.ToolTip = string.IsNullOrWhiteSpace(row.Detail) ? null : TooltipHelper.Wrap(row.Detail);
        return plate;
    }

    // --- Most wins, most matches, best streak: rows with a bar or a row of marks --------------

    private FrameworkElement BuildBarBody(LeaderCard card, string? meId)
    {
        var body = new StackPanel();
        foreach (var row in card.Rows)
        {
            var built = BuildBarRow(row, meId, card.Kind == HighlightCellKind.BestStreak);
            if (row.Place > 1) built.Margin = new Thickness(0, 2, 0, 0);
            body.Children.Add(built);
        }
        return body;
    }

    private FrameworkElement BuildBarRow(LeaderRow row, string? meId, bool streak)
    {
        var first = row.Place == 1;
        var isMe = IsLeaderMe(row, meId);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(HlPlace(row.Place, first));
        var face = BuildLeaderFace(row, first ? 34 : 24);
        Grid.SetColumn(face, 1);
        grid.Children.Add(face);

        var words = new StackPanel { Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        var name = HlName(row.Name, isMe, first ? "HlNameFirstSize" : "HlNameSize");
        FrameworkElement figure;
        if (streak)
        {
            words.Children.Add(name);
            var marks = HlMarks(row.Streak ?? 0, first ? HlGold : "#4FD68A");
            marks.Margin = new Thickness(0, 6, 0, 0);
            words.Children.Add(marks);
            figure = HlStreakPill(row.Streak ?? 0);
        }
        else
        {
            // The name first and whole; the detail takes what is left of the line and trims.
            var line = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(name, Dock.Left);
            line.Children.Add(name);
            var detail = HlText(row.Detail, "HlDetailSize", "#A8BCD2", trim: true);
            detail.Margin = new Thickness(8, 0, 0, 0);
            detail.VerticalAlignment = VerticalAlignment.Bottom;
            HideWhenCramped(detail);
            line.Children.Add(detail);
            words.Children.Add(line);
            var bar = HlBar(row.Facts.BarShare, first ? 6 : 4,
                first ? Across((Rgba("#D2A54A", 1), 0), (Rgba(HlGoldText, 1), 1)) : Solid("#8CA8BCD2"));
            bar.Margin = new Thickness(0, 5, 0, 0);
            words.Children.Add(bar);
            var text = HlText(row.Figure, first ? "HlFigureFirstSize" : "HlFigureSize", first ? HlGoldText : "#F0F5FB", mono: true, weight: FontWeights.Bold);
            text.MinWidth = 30;
            text.TextAlignment = TextAlignment.Right;
            figure = text;
        }
        Grid.SetColumn(words, 2);
        grid.Children.Add(words);
        Grid.SetColumn(figure, 3);
        grid.Children.Add(figure);
        return HlPlate(row, meId, grid, first ? new Thickness(9, 8, 9, 8) : new Thickness(9, 4, 9, 4), 10);
    }

    /// <summary>One mark per win, 9 wide and 3 apart: the streak, counted out.</summary>
    private static FrameworkElement HlMarks(int wins, string hex)
    {
        var mark = new GeometryDrawing(Solid(hex), null, new RectangleGeometry(new Rect(0, 0, 9, 6)));
        var tile = new DrawingBrush(mark)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 12, 6),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, 12, 6),
            ViewboxUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top,
        };
        tile.Freeze();
        return new System.Windows.Shapes.Rectangle
        {
            Height = 6,
            Width = Math.Max(9, wins * 12 - 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            Fill = tile,
            SnapsToDevicePixels = true,
            IsHitTestVisible = false,
        };
    }

    /// <summary>The streak as an amber pill with a drawn flame.</summary>
    private static FrameworkElement HlStreakPill(int wins)
    {
        var inside = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        inside.Children.Add(HlIcon(LeaderFlame, 11, null, Solid("#F4900C")));
        var count = HlText(wins.ToString(Strings.Culture), "HlMonoSize", "#F5BD76", mono: true, weight: FontWeights.Bold);
        count.Margin = new Thickness(4, 0, 0, 0);
        inside.Children.Add(count);
        return new Border
        {
            Tag = "StreakPill",
            Height = 22,
            Padding = new Thickness(8, 0, 8, 0),
            CornerRadius = new CornerRadius(11),
            Background = Solid("#29F4900C"),
            BorderBrush = Solid("#66F4900C"),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = TooltipHelper.Wrap(Strings.Format("MpStreakTipTitle", wins) + "\n" + Strings.Get("MpStreakTipBody")),
            Child = inside,
        };
    }

    // --- Best win rate: a ring per place -------------------------------------------------------

    private FrameworkElement BuildRateBody(LeaderCard card, string? meId)
    {
        var body = new StackPanel();
        foreach (var row in card.Rows)
        {
            var first = row.Place == 1;
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(HlRing(first ? 64 : 40, first ? 7 : 5, row.Facts.Percent ?? 0, first ? "HlRingFirstSize" : "HlRingSize"));
            var face = BuildLeaderFace(row, first ? 34 : 24);
            face.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(face, 1);
            grid.Children.Add(face);
            var words = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(HlName(row.Name, IsLeaderMe(row, meId), first ? "HlNameFirstSize" : "HlNameSize"));
            if (!string.IsNullOrWhiteSpace(row.Detail))
            {
                var detail = HlText(row.Detail, "HlDetailSize", "#A8BCD2", trim: true);
                detail.Margin = new Thickness(0, 3, 0, 0);
                words.Children.Add(detail);
            }
            Grid.SetColumn(words, 2);
            grid.Children.Add(words);
            var plate = HlPlate(row, meId, grid, first ? new Thickness(9, 8, 9, 8) : new Thickness(9, 4, 9, 4), 12);
            if (row.Place > 1) plate.Margin = new Thickness(0, 6, 0, 0);
            body.Children.Add(plate);
        }
        return body;
    }

    /// <summary>A ring: the share won in green, the rest in red, the percentage inside.</summary>
    private static FrameworkElement HlRing(double size, double thickness, int percent, string sizeKey)
    {
        var share = Math.Clamp(percent, 0, 100) / 100.0;
        var ring = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        ring.Children.Add(new System.Windows.Shapes.Ellipse { Fill = Solid("#101D30"), Margin = new Thickness(thickness / 2) });
        ring.Children.Add(new System.Windows.Shapes.Ellipse { Stroke = Solid("#8CC84A4A"), StrokeThickness = thickness });
        if (share >= 1)
        {
            ring.Children.Add(new System.Windows.Shapes.Ellipse { Stroke = Solid("#4FD68A"), StrokeThickness = thickness });
        }
        else if (share > 0)
        {
            var radius = (size - thickness) / 2;
            var centre = size / 2;
            var angle = share * 2 * Math.PI;
            var figure = new PathFigure { StartPoint = new Point(centre, centre - radius), IsClosed = false };
            figure.Segments.Add(new ArcSegment(
                new Point(centre + radius * Math.Sin(angle), centre - radius * Math.Cos(angle)),
                new Size(radius, radius), 0, share > .5, SweepDirection.Clockwise, true));
            var arc = new PathGeometry();
            arc.Figures.Add(figure);
            arc.Freeze();
            ring.Children.Add(new System.Windows.Shapes.Path { Data = arc, Stroke = Solid("#4FD68A"), StrokeThickness = thickness });
        }
        var text = HlText(percent.ToString(Strings.Culture) + "%", sizeKey, "#F0F5FB", mono: true, weight: FontWeights.Bold);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        ring.Children.Add(text);
        return ring;
    }

    // --- Biggest upset: a duel ------------------------------------------------------------------

    private FrameworkElement BuildUpsetBody(LeaderCard card, string? meId)
    {
        var body = new StackPanel();
        foreach (var row in card.Rows)
            body.Children.Add(row.Place == 1 && row.Facts.Rival != null ? BuildUpsetHero(row, meId) : BuildUpsetRow(row, meId));
        return body;
    }

    private FrameworkElement BuildUpsetHero(LeaderRow row, string? meId)
    {
        var rival = row.Facts.Rival!;
        var isMe = IsLeaderMe(row, meId);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        StackPanel Side(string name, string? avatar, System.Collections.Generic.IReadOnlyList<string> ids, int rating, bool winner)
        {
            var side = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var face = BuildLeaderHeroFace(name, avatar, ids.Count == 1 ? ids[0] : null, row.Facts.Team, ring: winner);
            face.HorizontalAlignment = HorizontalAlignment.Center;
            side.Children.Add(face);
            var label = HlName(name, winner && isMe, "HlTitleSize", winner ? "#FFFFFF" : "#DCE7F5");
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.Margin = new Thickness(0, 5, 0, 0);
            side.Children.Add(label);
            var elo = HlText(rating.ToString(System.Globalization.CultureInfo.InvariantCulture), "HlMonoSize", winner ? "#DCE7F5" : "#A8BCD2", mono: true, weight: FontWeights.SemiBold);
            elo.HorizontalAlignment = HorizontalAlignment.Center;
            elo.Margin = new Thickness(0, 5, 0, 0);
            side.Children.Add(elo);
            // Each half is one player (or one side): his own profile, not the row's.
            if (ids.Count == 1)
            {
                side.Background = Brushes.Transparent;
                LinkRankingPlayer(side, winner && isMe, ids[0], name, null, avatar);
            }
            return side;
        }

        grid.Children.Add(Side(row.Name, row.AvatarUrl, row.UserIds, rival.WinnersRating, winner: true));

        var middle = new StackPanel { Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        var gap = HlText(row.Figure, "HlUpsetFigureSize", HlGreen, mono: true, weight: FontWeights.Bold);
        gap.HorizontalAlignment = HorizontalAlignment.Center;
        middle.Children.Add(gap);
        var beat = HlText(Strings.Get("MpHlBeat").ToUpper(Strings.Culture), "HlPillSize", HlGreen, weight: FontWeights.Bold);
        middle.Children.Add(new Border
        {
            Height = 18,
            Padding = new Thickness(8, 0, 8, 0),
            Margin = new Thickness(0, 6, 0, 0),
            CornerRadius = new CornerRadius(9),
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = Solid("#294FD68A"),
            BorderBrush = Solid("#664FD68A"),
            BorderThickness = new Thickness(1),
            Child = beat,
        });
        Grid.SetColumn(middle, 1);
        grid.Children.Add(middle);

        var loser = Side(rival.Name, rival.AvatarUrl, rival.UserIds, rival.LosersRating, winner: false);
        loser.Opacity = .82;
        Grid.SetColumn(loser, 2);
        grid.Children.Add(loser);

        var plate = HlPlate(row, meId, grid, new Thickness(11, 11, 11, 10), 12);
        plate.ToolTip = null;
        return plate;
    }

    private FrameworkElement BuildUpsetRow(LeaderRow row, string? meId)
    {
        var isMe = IsLeaderMe(row, meId);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(HlPlace(row.Place, false));
        var face = BuildLeaderFace(row, 24);
        Grid.SetColumn(face, 1);
        grid.Children.Add(face);

        // One line: who, in white, then whom he beat and both ratings, which trim first.
        var line = new DockPanel { LastChildFill = true, Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        var name = HlName(row.Name, isMe, "HlMonoSize");
        DockPanel.SetDock(name, Dock.Left);
        line.Children.Add(name);
        var detail = HlText(row.Detail, "HlMonoSize", "#A8BCD2", trim: true);
        detail.Margin = new Thickness(5, 0, 0, 0);
        HideWhenCramped(detail);
        line.Children.Add(detail);
        Grid.SetColumn(line, 2);
        grid.Children.Add(line);

        var figure = HlText(row.Figure, "HlFigureSize", HlGreen, mono: true, weight: FontWeights.Bold);
        figure.MinWidth = 34;
        figure.TextAlignment = TextAlignment.Right;
        Grid.SetColumn(figure, 3);
        grid.Children.Add(figure);
        var plate = HlPlate(row, meId, grid, new Thickness(3, 5, 3, 6), 0, hairline: true);
        // A row about one winner opens his profile, as before the mosaic.
        if (row.UserIds.Count == 1) LinkRankingPlayer(plate, isMe, row.UserIds[0], row.Name, null, row.AvatarUrl);
        return plate;
    }

    // --- Civilization of the month: two halves --------------------------------------------------

    private static FrameworkElement HlFlag(LeaderRow row, double width, double height, double radius)
    {
        var flag = DeckCardNames.Peek(row.CivModId)?.CivIconOf(row.Civ ?? "");
        var chip = new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(radius),
            // Until the mod's files have been read the chip is an empty frame: the row keeps its shape.
            Background = flag != null ? new ImageBrush(flag) { Stretch = Stretch.UniformToFill } : Solid("#0DFFFFFF"),
            BorderBrush = (Brush)Application.Current.FindResource("MpFlagRim"),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
        };
        RenderOptions.SetBitmapScalingMode(chip, BitmapScalingMode.HighQuality);
        return chip;
    }

    private FrameworkElement BuildCivBody(LeaderCard card)
    {
        var first = card.Rows[0];
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.8, GridUnitType.Star), MinWidth = 190 });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });

        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.Children.Add(HlFlag(first, 60, 40, 5));
        var words = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var civName = HlText(first.Name, "HlCivNameSize", "#FFFFFF", weight: FontWeights.SemiBold);
        civName.TextWrapping = TextWrapping.Wrap;
        words.Children.Add(civName);
        if (!string.IsNullOrWhiteSpace(first.Detail))
        {
            var detail = HlText(first.Detail, "HlCivDetailSize", "#DCE7F5", trim: true);
            detail.Margin = new Thickness(0, 3, 0, 0);
            words.Children.Add(detail);
        }
        Grid.SetColumn(words, 1);
        top.Children.Add(words);

        // One TextBlock, two runs: the figure and its unit share a baseline by construction.
        var picks = new TextBlock { Margin = new Thickness(0, 10, 0, 0), Foreground = Solid("#DCE7F5") };
        picks.SetResourceReference(TextBlock.FontSizeProperty, "HlCivDetailSize");
        var count = new System.Windows.Documents.Run(first.Figure ?? "") { FontWeight = FontWeights.Bold, Foreground = Solid(HlGoldText) };
        count.SetResourceReference(System.Windows.Documents.TextElement.FontFamilyProperty, "MonoFont");
        count.SetResourceReference(System.Windows.Documents.TextElement.FontSizeProperty, "HlCivFigureSize");
        picks.Inlines.Add(count);
        picks.Inlines.Add(new System.Windows.Documents.Run("  " + Strings.Get("MpHlPicks")));

        var hero = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        hero.Children.Add(top);
        hero.Children.Add(picks);
        body.Children.Add(HlPlate(first, null, hero, new Thickness(13), 12));

        var rest = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        foreach (var row in card.Rows.Skip(1))
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.9, GridUnitType.Star), MinWidth = 90 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            grid.Children.Add(HlPlace(row.Place, false));
            var flag = HlFlag(row, 28, 19, 3);
            Grid.SetColumn(flag, 1);
            grid.Children.Add(flag);
            var names = new StackPanel { Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            names.Children.Add(HlText(row.Name, "HlNameSize", "#F0F5FB", weight: FontWeights.SemiBold, trim: true));
            if (!string.IsNullOrWhiteSpace(row.Detail))
            {
                var detail = HlText(row.Detail, "HlDetailSize", "#A8BCD2", trim: true);
                detail.Margin = new Thickness(0, 1, 0, 0);
                names.Children.Add(detail);
            }
            Grid.SetColumn(names, 2);
            grid.Children.Add(names);
            var bar = HlBar(row.Facts.BarShare, 6, Solid("#99E6C06A"));
            bar.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(bar, 3);
            grid.Children.Add(bar);
            var figure = HlText(row.Figure, "HlFigureSize", "#F0F5FB", mono: true, weight: FontWeights.Bold);
            figure.TextAlignment = TextAlignment.Right;
            figure.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(figure, 4);
            grid.Children.Add(figure);
            rest.Children.Add(HlPlate(row, null, grid, new Thickness(3, 5, 3, 5), 10));
        }
        Grid.SetColumn(rest, 1);
        body.Children.Add(rest);
        return body;
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
