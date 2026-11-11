using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// The Profile window, design 95: a player card that stays put on the left, and the section it
/// has selected on the right.
///
/// <para>The card holds what used to be the page's header and adds the three facts a player looks
/// for first. Everything else is drawn by the same builders it always was: the sections only
/// decide which of them are on screen.</para>
/// </summary>
public partial class MultiplayerTab
{
    /// <summary>How wide the window is, or the design's own width while there is no window.</summary>
    private double ProfileWidth => _profileWindow?.ActualWidth is > 0 and var w ? w : 1040;

    /// <summary>The right-hand side once the card and the gutters are gone.</summary>
    private double ProfileContentWidth => ProfileWidth - ProfileWindow.RailWidthFor(ProfileWidth) - 56;

    /// <summary>What decides the layout of the sections, so a resize repaints only on a change.</summary>
    private (bool Rail, bool Compact, bool Together) ProfileShape()
    {
        double width = ProfileWidth;
        return (width >= 1300, width < 900, ProfileContentWidth >= 900);
    }

    private (bool Rail, bool Compact, bool Together) _profileShape;

    /// <summary>Called by the window: only a change of shape redraws anything.</summary>
    internal void OnProfileWindowResized()
    {
        var shape = ProfileShape();
        if (shape == _profileShape) return;
        _profileShape = shape;
        RenderProfileTab();
    }

    // ---------------------------------------------------------------- the card

    private void RenderProfileRail(Panel host, LobbyUserSummary user)
    {
        host.Children.Add(BuildProfileHeader(user));
        host.Children.Add(BuildProfileNav());
        if (BuildProfileFacts() is { } facts) host.Children.Add(facts);
    }

    /// <summary>The ladder the card speaks for: the 1v1 one, or teams when that is all there is.</summary>
    private (LadderStanding? Ladder, bool Team, int? Size) ProfileCardLadder()
    {
        if (_cachedStanding is not { } standing) return (null, false, null);
        var (ladders, _) = ProfileLadders(standing);

        foreach (var (ladder, team) in new[] { (ladders.Default, false), (ladders.Team, true) })
        {
            var size = ladder?.LadderSize is > 0 ? ladder.LadderSize
                : CommunityStatsView.RankedPlayers(_communityStats, team) is > 0 and var n ? n : (int?)null;
            var shape = ProfileModeView.ShapeOf(ladder);
            if (shape is ProfileModeShape.Ranked or ProfileModeShape.Inactive
                && ProfileModeView.AgeOf(ladder, size) != null)
            {
                return (ladder, team, size);
            }
        }

        return (null, false, null);
    }

    private static string AgeInkKey(RankAge age) => age switch
    {
        RankAge.Sovereign => "RankInkSovereign",
        RankAge.Imperial => "RankInkImperial",
        RankAge.Industrial => "RankInkIndustrial",
        RankAge.Fortress => "RankInkFortress",
        RankAge.Colonial => "RankInkColonial",
        _ => "RankInkDiscovery",
    };

    /// <summary>
    /// The top of the card (95a): the portrait with its ring and badge, the name, the login, a
    /// pill with the age, the mode and the place, and one line saying where the player stands in
    /// each mode. With no age the ring is the neutral blue and neither pill nor badge is drawn:
    /// a place nobody earned.
    /// </summary>
    /// <remarks><c>internal</c> so the tests build the real header.</remarks>
    internal UIElement BuildProfileHeader(LobbyUserSummary user)
    {
        bool compact = ProfileWidth < 900;
        bool wide = ProfileWidth >= 1300;
        double size = compact ? 64 : wide ? 104 : 88;

        var (ladder, team, ladderSize) = ProfileCardLadder();
        RankAge? age = ladder != null ? ProfileModeView.AgeOf(ladder, ladderSize) : null;

        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };

        // ---- the portrait. Three discs rather than a stroke: a stroke on a disc is drawn across
        // its edge, and the dark gap between the ring and the picture is the whole effect.
        var portrait = new Grid
        {
            Width = size + 10,
            Height = size + 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, age != null ? 14 : 4),
        };

        var ring = new Ellipse();
        if (age is { } shownAge) ring.SetResourceReference(Shape.FillProperty, AgeInkKey(shownAge));
        else ring.SetResourceReference(Shape.FillProperty, "ProfileNeutralRing");
        portrait.Children.Add(ring);

        var gap = new Ellipse { Margin = new Thickness(2) };
        gap.SetResourceReference(Shape.FillProperty, "MpAppBg");
        portrait.Children.Add(gap);

        var avatar = BuildAvatarDisc(user.DisplayName, user.AvatarUrl, size);
        avatar.HorizontalAlignment = HorizontalAlignment.Center;
        avatar.VerticalAlignment = VerticalAlignment.Center;
        portrait.Children.Add(avatar);

        if (age is { } badgeAge)
        {
            var shown = new ShownBadge(team ? BadgeKind.Team : BadgeKind.Solo, badgeAge,
                                       ladder!.LadderRank ?? 0, null, 0);
            var badge = RankBadge.BuildFor(shown, 28, "profile-card",
                RankBadgeTips.Text(shown, CommunityStatsView.RequiredDecided(_communityStats)));
            badge.HorizontalAlignment = HorizontalAlignment.Center;
            badge.VerticalAlignment = VerticalAlignment.Bottom;
            badge.Margin = new Thickness(0, 0, 0, -15);
            portrait.Children.Add(badge);
        }
        stack.Children.Add(portrait);

        // ---- who
        var name = new TextBlock
        {
            Text = user.DisplayName,
            FontWeight = FontWeights.Bold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = Brushes.White,
        };
        name.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        name.SetResourceReference(TextBlock.FontSizeProperty,
            compact ? "ProfileNameSizeCompact" : wide ? "ProfileNameSizeWide" : "ProfileNameSize");
        stack.Children.Add(name);

        if (!string.IsNullOrWhiteSpace(user.DiscordUsername))
        {
            var login = StatsText("@" + user.DiscordUsername, "StatsNoteSize", "StatsProse", FontWeights.Normal);
            login.HorizontalAlignment = HorizontalAlignment.Center;
            login.TextTrimming = TextTrimming.CharacterEllipsis;
            login.Margin = new Thickness(0, 3, 0, 0);
            stack.Children.Add(login);
        }

        // ---- the pill
        if (age is { } pillAge)
        {
            string ageName = Strings.Get(RankAges.NameKey(pillAge));
            bool inactive = ProfileModeView.ShapeOf(ladder) == ProfileModeShape.Inactive;
            string tier = inactive || ladder!.LadderRank is not > 0 || ladderSize is not > 0
                ? Strings.Format("MpProfileTierInactive", ageName, ModeName(team))
                : Strings.Format("MpProfileTier", ageName, ModeName(team), ladder.LadderRank!.Value,
                                 ladderSize!.Value);

            var dot = new Ellipse
            {
                Width = 6,
                Height = 6,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            dot.SetResourceReference(Shape.FillProperty, AgeInkKey(pillAge));

            var words = StatsText(tier.ToUpper(Strings.Culture), "StatsLabelSize", AgeInkKey(pillAge),
                                  FontWeights.SemiBold);
            words.TextTrimming = TextTrimming.CharacterEllipsis;

            var line = new Grid();
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.Children.Add(dot);
            line.Children.Add(WithColumn(words, 1));

            var pill = new Border
            {
                Height = 24,
                Padding = new Thickness(10, 0, 10, 0),
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 10, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = line,
            };
            pill.SetResourceReference(Border.BackgroundProperty, "ProfilePillFill");
            pill.SetResourceReference(Border.BorderBrushProperty, "ProfilePillRim");
            stack.Children.Add(pill);
        }

        // ---- where the player stands
        string? status = ProfileStatusLine(out _);
        if (status != null)
        {
            var line = StatsProse(status, "StatsNoteSize", "StatsSoft");
            line.TextAlignment = TextAlignment.Center;
            line.Margin = new Thickness(0, 10, 0, 0);
            stack.Children.Add(line);
        }

        var hero = new Border
        {
            Child = stack,
            Tag = "ProfileHeader",
            Padding = new Thickness(14, compact ? 18 : 26, 14, 14),
        };
        hero.SetResourceReference(Border.BackgroundProperty, "ProfileCardHero");
        return hero;
    }

    /// <summary>"Fortress in 1v1 · in placement in Teams", or why there is nothing to say.</summary>
    private string? ProfileStatusLine(out bool noGames)
    {
        noGames = false;
        if (_cachedStanding is not { } standing)
            return Strings.Get(RatingState == ProfileRatingState.Failed
                ? "MpProfileRatingFailed" : "MpProfileRatingLoading");

        var (ladders, _) = ProfileLadders(standing);
        noGames = ProfileModeView.NoGamesAnywhere(ladders);
        return noGames
            ? Strings.Get("MpProfileNoMatchesYou")
            : ProfileModeView.StatusLine(new[]
            {
                ProfileModeView.StatusSegment(ladders.Default, ModeName(false),
                    CommunityStatsView.RankedPlayers(_communityStats, team: false)),
                ProfileModeView.StatusSegment(ladders.Team, ModeName(true),
                    CommunityStatsView.RankedPlayers(_communityStats, team: true)),
            });
    }

    // ---------------------------------------------------------------- the navigation

    private UIElement BuildProfileNav()
    {
        var nav = new StackPanel { Margin = new Thickness(10, 4, 10, 0) };

        var totals = ProfileSummaryView.Totals(_historyRows);
        int civs = CivStatsView.Rows(_historyRows).Count;
        int decks = _mpDeckProfiles.Sum(p => p.Decks.Count);

        foreach (var (section, key, count) in new[]
                 {
                     (ProfileSection.Overview, "MpProfileSectionOverview", 0),
                     (ProfileSection.History, "MpSubtabHistory", totals.Played),
                     (ProfileSection.Civs, "MpProfileSectionCivs", civs),
                     (ProfileSection.Decks, "MpProfileSectionDecks", decks),
                 })
        {
            var inner = new Grid();
            inner.ColumnDefinitions.Add(new ColumnDefinition());
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            inner.Children.Add(new TextBlock
            {
                Text = Strings.Get(key),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            });
            if (count > 0)
            {
                inner.Children.Add(WithColumn(
                    StatsMono(count.ToString(), "StatsNoteSize", "StatsNote", FontWeights.Medium), 1));
            }

            var item = new Button
            {
                Content = inner,
                Tag = _profileSection == section ? "active" : null,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            item.SetResourceReference(FrameworkElement.StyleProperty, "ProfileNavButton");
            System.Windows.Automation.AutomationProperties.SetName(item, Strings.Get(key));

            var chosen = section;
            item.Click += (_, _) =>
            {
                if (_profileSection == chosen) return;
                _profileSection = chosen;
                RenderProfileTab();
            };
            nav.Children.Add(item);
        }

        return nav;
    }

    // ---------------------------------------------------------------- the three facts

    private UIElement? BuildProfileFacts()
    {
        var totals = ProfileSummaryView.Totals(_historyRows);
        if (totals.Played <= 0) return null;

        var facts = new StackPanel { Margin = new Thickness(14, 8, 14, 10) };
        var rule = new Border { Height = 1, Margin = new Thickness(0, 0, 0, 4) };
        rule.SetResourceReference(Border.BackgroundProperty, "StatsHeadRule");
        facts.Children.Add(rule);

        facts.Children.Add(BuildProfileFact(
            Strings.Get("MpProfileFactMatches"), totals.Played.ToString(),
            Strings.Format("MpProfileFactDecided", totals.Decided),
            Strings.Format("MpProfileTotalBreakdown", totals.Decided, totals.Unrated)));

        var (map, mapCount) = MatchHistoryView.TopMap(_historyRows);
        if (!string.IsNullOrWhiteSpace(map))
        {
            facts.Children.Add(BuildProfileFact(
                Strings.Get("MpProfileTopMap"), LocalMatchView.PrettyMap(map),
                Strings.Format(mapCount == 1 ? "MpProfileFactMapOne" : "MpProfileFactMap", mapCount),
                null));
        }

        var rival = ProfileSummaryView.FrequentOpponent(_historyRows, ProfileViewerId);
        if (rival != null)
        {
            facts.Children.Add(BuildProfileFact(
                Strings.Get("MpProfileFactRival"), rival.Name,
                Strings.Format("MpProfileFactRecord", rival.Wins, rival.Losses, rival.Matches),
                null));
        }

        return facts;
    }

    private static FrameworkElement BuildProfileFact(string label, string value, string note, string? tip)
    {
        var row = new Grid { MinHeight = 40, Margin = new Thickness(0, 6, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        if (tip != null) row.ToolTip = TooltipHelper.Wrap(tip);

        row.Children.Add(StatsText(label.ToUpper(Strings.Culture), "StatsHeaderSize", "StatsNote",
                                   FontWeights.Bold));

        var right = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        var figure = StatsText(value, "StatsRowNameSizeCompact", "StatsStrong", FontWeights.SemiBold);
        figure.HorizontalAlignment = HorizontalAlignment.Right;
        figure.TextTrimming = TextTrimming.CharacterEllipsis;
        right.Children.Add(figure);
        var small = StatsText(note, "StatsLabelSize", "StatsNote", FontWeights.Normal);
        small.HorizontalAlignment = HorizontalAlignment.Right;
        small.Margin = new Thickness(0, 3, 0, 0);
        right.Children.Add(small);
        row.Children.Add(WithColumn(right, 1));

        return StatsRule(row);
    }

    // ---------------------------------------------------------------- the sections

    private static TextBlock ProfileSectionTitle(string text)
    {
        var title = new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        title.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        title.SetResourceReference(TextBlock.FontSizeProperty, "ProfileTitleSize");
        title.SetResourceReference(TextBlock.ForegroundProperty, "StatsStrong");
        return title;
    }

    /// <summary>The Summary: the refund, the two modes, the curve and the rivals.</summary>
    private void RenderProfileOverview(Panel body)
    {
        // Everything else here reads the standing or sits under it, so with no standing there is
        // nothing true to draw beside these two.
        switch (RatingState)
        {
            case ProfileRatingState.Loading:
                body.Children.Add(BuildProfileSkeleton());
                return;
            case ProfileRatingState.Failed:
                body.Children.Add(BuildProfileRatingFailed());
                return;
        }

        if (BuildRefundNotice() is { } refund) body.Children.Add(refund);
        if (BuildProfileModeCards() is { } modeCards) body.Children.Add(modeCards);

        var curve = BuildProfileCurveCard();
        var rivals = BuildHeadToHeadCard();

        if (rivals is FrameworkElement second && ProfileContentWidth >= 900)
        {
            var pair = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            pair.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star) });
            pair.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            pair.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            second.Margin = new Thickness(0);
            second.VerticalAlignment = VerticalAlignment.Top;
            curve.Margin = new Thickness(0);
            curve.VerticalAlignment = VerticalAlignment.Top;
            pair.Children.Add(WithColumn(curve, 0));
            pair.Children.Add(WithColumn(second, 2));
            body.Children.Add(pair);
        }
        else
        {
            body.Children.Add(curve);
            if (rivals is FrameworkElement below)
            {
                below.Margin = new Thickness(0, 14, 0, 0);
                body.Children.Add(below);
            }
        }

        if (BuildBadgeModeCard(PreviewProfileUser() ?? _session?.CurrentUser!) is { } badgeCard)
            body.Children.Add(badgeCard);
    }

    private ProfileRatingState RatingState => ProfileRatingView.StateOf(
        _cachedStanding != null, _standingFailed, ConnectivityState.IsOffline);

    /// <summary>The two mode cards before they have anything to say (design 95e).</summary>
    private UIElement BuildProfileSkeleton()
    {
        static Border Bar(byte alpha, double height, double radius) => new()
        {
            Height = height,
            CornerRadius = new CornerRadius(radius),
            Background = new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255)),
        };

        // The two short lines are a share of the card, so they stay in a star column.
        static UIElement Line(byte alpha, double share)
        {
            var row = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(share, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - share, GridUnitType.Star) });
            row.Children.Add(Bar(alpha, 10, 5));
            return row;
        }

        Border Card()
        {
            var stack = new StackPanel();
            var label = Bar(26, 10, 5);
            label.Width = 80;
            label.HorizontalAlignment = HorizontalAlignment.Left;
            stack.Children.Add(label);
            var figure = Bar(20, 28, 7);
            figure.Width = 120;
            figure.HorizontalAlignment = HorizontalAlignment.Left;
            figure.Margin = new Thickness(0, 12, 0, 0);
            stack.Children.Add(figure);
            stack.Children.Add(Line(18, 0.7));
            stack.Children.Add(Line(15, 0.52));
            return ProfileGlass(stack, new Thickness(16));
        }

        bool stacked = ProfileContentWidth < ModeCardsStackBelow;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(stacked ? 0 : 12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = stacked ? new GridLength(0) : new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(WithColumn(Card(), 0));
        if (!stacked) grid.Children.Add(WithColumn(Card(), 2));
        return grid;
    }

    /// <summary>The standing did not arrive (design 95f). The decks do not depend on it.</summary>
    private Border BuildProfileRatingFailed()
    {
        var stack = new StackPanel();
        stack.Children.Add(StatsText(Strings.Get("MpProfileRatingFailedTitle"), "StatsSubtitleSize",
                                     "WindowNoticeTitle", FontWeights.SemiBold));
        var body = StatsProse(Strings.Get("MpProfileRatingFailedBody"), "StatsBodySize", "StatsProse");
        body.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(body);

        var retry = new Button
        {
            Content = Strings.Get("MpStatsRetry"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 10, 0, 0),
        };
        retry.SetResourceReference(FrameworkElement.StyleProperty, "WindowSecondaryButton");
        retry.Click += (_, _) =>
        {
            _standingFailed = false;
            RenderProfileTab();
        };
        stack.Children.Add(retry);

        var card = ProfileGlass(stack, new Thickness(18, 16, 18, 16));
        card.SetResourceReference(Border.BorderBrushProperty, "WindowNoticeRim");
        return card;
    }

    /// <summary>A card of the page: the fill, the rim and the light along its top edge.</summary>
    private static Border ProfileGlass(UIElement child, Thickness padding)
    {
        var card = new Border
        {
            Child = child,
            Padding = padding,
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
        };
        card.SetResourceReference(Border.BackgroundProperty, "GlassPanel");
        card.SetResourceReference(Border.BorderBrushProperty, "ProfileCardRim");
        return card;
    }

    // ---------------------------------------------------------------- the curve

    /// <summary>Which ladder's matches a row counts for.</summary>
    private static bool IsTeamRow(MatchHistoryRow row)
        => string.Equals(row.RatingMode, "team", StringComparison.OrdinalIgnoreCase);

    private bool _curveShowsTeam;

    /// <summary>
    /// How the rating moved (95a). Each point is one rated match, so the hover can say which: the
    /// date and the rival are what the line alone cannot tell.
    /// </summary>
    private Border BuildProfileCurveCard()
    {
        var stack = new StackPanel();

        var head = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel();
        left.Children.Add(StatsText(Strings.Get("MpProfileCurveTitle"), "StatsBandSize", "StatsStrong",
                                    FontWeights.SemiBold));
        left.Children.Add(BuildCurveTabs());
        head.Children.Add(left);

        var rows = (_historyRows ?? new List<MatchHistoryRow>())
            .Where(r => IsTeamRow(r) == _curveShowsTeam)
            .ToList();
        var points = ProfileSummaryView.RatingCurve(rows);

        if (points.Count >= 3)
        {
            int now = (int)Math.Round(points[^1]);
            int first = (int)Math.Round(points[0]);
            var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };

            var top = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            var figure = StatsMono(now.ToString(), "ProfileCurveFigureSize", "StatsStrong",
                                   FontWeights.SemiBold);
            top.Children.Add(figure);

            int delta = now - first;
            var chip = new Border
            {
                Height = 22,
                Padding = new Thickness(8, 0, 8, 0),
                CornerRadius = new CornerRadius(11),
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = StatsMono(RatingDisplay.FormatDelta(delta) ?? "0", "StatsNoteSize",
                    delta >= 0 ? "StatsWin" : "StatsLoss", FontWeights.SemiBold),
                ToolTip = TooltipHelper.Wrap(Strings.Format("MpProfileCurveChangeTip", first, now)),
            };
            chip.SetResourceReference(Border.BackgroundProperty,
                delta >= 0 ? "ProfileGainFill" : "ProfileLossFill");
            top.Children.Add(chip);
            right.Children.Add(top);

            var sub = StatsText(Strings.Format("MpProfileCurveSub", points.Count - 1), "StatsNoteSize",
                                "StatsNote", FontWeights.Normal);
            sub.HorizontalAlignment = HorizontalAlignment.Right;
            sub.Margin = new Thickness(0, 7, 0, 0);
            right.Children.Add(sub);
            head.Children.Add(WithColumn(right, 1));
        }
        stack.Children.Add(head);

        if (points.Count >= 3)
        {
            stack.Children.Add(new RatingCurveChart(
                points, CurveTips(rows, points.Count), ProfileContentWidth >= 900 ? 190 : 160));

            var axis = new Grid { Margin = new Thickness(0, 8, 38, 0) };
            axis.ColumnDefinitions.Add(new ColumnDefinition());
            axis.ColumnDefinitions.Add(new ColumnDefinition());
            axis.ColumnDefinitions.Add(new ColumnDefinition());
            var dates = rows
                .Where(r => MatchHistoryView.IsRated(r) && r.RatingAfter.HasValue)
                .Select(r => MatchHistoryView.ParseLocal(r.EndedAt))
                .Reverse()
                .ToList();
            string Stamp(DateTime? when) => when is { } date
                ? date.Date == DateTime.Today ? Strings.Get("MpProfileCurveToday") : ShortDate.Format(date)
                : "";
            if (dates.Count > 0)
            {
                var a = StatsText(Stamp(dates[0]), "StatsLabelSize", "StatsNote", FontWeights.Normal);
                var b = StatsText(Stamp(dates[dates.Count / 2]), "StatsLabelSize", "StatsNote", FontWeights.Normal);
                b.HorizontalAlignment = HorizontalAlignment.Center;
                var c = StatsText(Stamp(dates[^1]), "StatsLabelSize", "StatsNote", FontWeights.Normal);
                c.HorizontalAlignment = HorizontalAlignment.Right;
                axis.Children.Add(a);
                axis.Children.Add(WithColumn(b, 1));
                axis.Children.Add(WithColumn(c, 2));
            }
            stack.Children.Add(axis);
        }
        else
        {
            string words = _curveShowsTeam
                ? Strings.Get("MpProfileCurveTeamWait")
                : points.Count == 2
                    ? Strings.Format("MpProfileCurveCompact",
                        (int)Math.Round(points[0]), (int)Math.Round(points[1]),
                        RatingDisplay.FormatDelta((int)Math.Round(points[1] - points[0])) ?? "0", 1)
                    : Strings.Get("MpProfileCurveTooFew");

            var text = StatsProse(words, "StatsBodySize");
            text.TextAlignment = TextAlignment.Center;
            var empty = new Border
            {
                MinHeight = _curveShowsTeam ? 150 : 64,
                Padding = new Thickness(20, 12, 20, 12),
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                Child = text,
            };
            empty.SetResourceReference(Border.BorderBrushProperty, "StatsChipRim");
            stack.Children.Add(empty);
        }

        var card = ProfileGlass(stack, new Thickness(18, 16, 18, 14));
        card.Margin = new Thickness(0, 14, 0, 0);
        return card;
    }

    private FrameworkElement BuildCurveTabs()
    {
        var host = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(3) };
        foreach (var (team, label) in new[] { (false, ModeName(false)), (true, ModeName(true)) })
        {
            var tab = new Button { Content = label, Tag = _curveShowsTeam == team ? "active" : null };
            tab.SetResourceReference(FrameworkElement.StyleProperty, "ProfileMiniTab");
            var chosen = team;
            tab.Click += (_, _) =>
            {
                if (_curveShowsTeam == chosen) return;
                _curveShowsTeam = chosen;
                RenderProfileTab();
            };
            host.Children.Add(tab);
        }

        var group = new Border
        {
            Child = host,
            CornerRadius = new CornerRadius(14),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 9, 0, 0),
        };
        group.SetResourceReference(Border.BackgroundProperty, "ProfileTabsFill");
        return group;
    }

    /// <summary>What the hover says about each point, oldest first. The first point can be the
    /// rating BEFORE the oldest match, which belongs to no match and says nothing.</summary>
    private IReadOnlyList<string?> CurveTips(List<MatchHistoryRow> rows, int pointCount)
    {
        var rated = rows
            .Where(r => MatchHistoryView.IsRated(r) && r.RatingAfter.HasValue)
            .Reverse()
            .ToList();

        var tips = new List<string?>(pointCount);
        for (int i = rated.Count; i < pointCount; i++) tips.Add(null);

        foreach (var row in rated)
        {
            string? rival = MatchHistoryView.SoleOpponent(row, ProfileViewerId);
            string? when = MatchHistoryView.ParseLocal(row.EndedAt) is { } date
                ? ShortDate.Format(date)
                : null;
            string rating = ((int)Math.Round(row.RatingAfter!.Value)).ToString();

            tips.Add(string.Join(" · ", new[] { rating, rival, when }
                .Where(p => !string.IsNullOrWhiteSpace(p))));
        }

        return tips;
    }

    // ---------------------------------------------------------------- the streaks

    /// <summary>
    /// The two boxes under a ranked rating (95a): the streak going on, with the last matches as
    /// squares, and the longest ones.
    /// </summary>
    private FrameworkElement BuildStreakCards(LadderStanding ladder, bool team)
    {
        var grid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        grid.Children.Add(BuildCurrentStreakBox(ladder, team));
        grid.Children.Add(WithColumn(BuildLongestStreakBox(ladder), 2));
        return grid;
    }

    private FrameworkElement BuildCurrentStreakBox(LadderStanding ladder, bool team)
    {
        var current = ProfileModeView.CurrentStreak(ladder, DateTimeOffset.Now, Strings.Language);
        int streak = Math.Max(0, ladder.StreakCurrent);

        var stack = new StackPanel();
        stack.Children.Add(StatsText(Strings.Get("MpProfileStreakNow"), "StatsHeaderSize", "StatsNote",
                                     FontWeights.Bold));

        var figure = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 7, 0, 0) };
        figure.Children.Add(StatsMono(streak > 0 ? streak.ToString() : Strings.Get("MpDash"),
            "ProfileStreakFigureSize", streak > 0 ? "StatsWin" : "StatsNote", FontWeights.SemiBold));
        if (streak > 0)
        {
            var unit = StatsText(Strings.Get(streak == 1 ? "MpProfileStreakWinsOne" : "MpProfileStreakWins"),
                                 "StatsProseSize", "StatsSoft", FontWeights.Normal);
            unit.VerticalAlignment = VerticalAlignment.Bottom;
            unit.Margin = new Thickness(6, 0, 0, 4);
            figure.Children.Add(unit);
        }
        stack.Children.Add(figure);

        // The last rated matches, oldest first. The ones inside the streak are lit.
        var recent = (_historyRows ?? new List<MatchHistoryRow>())
            .Where(r => MatchHistoryView.IsRated(r) && IsTeamRow(r) == team)
            .Take(8)
            .Reverse()
            .ToList();
        if (recent.Count > 0)
        {
            var pips = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 7, 0, 0),
                Background = Brushes.Transparent,
                ToolTip = TooltipHelper.Wrap(Strings.Get("MpProfileStreakPipsTip")),
            };
            for (int i = 0; i < recent.Count; i++)
            {
                bool won = MatchOutcomeView.Classify(recent[i].Result) == MatchVerdict.Win;
                var pip = new Border
                {
                    Width = 9,
                    Height = 9,
                    CornerRadius = new CornerRadius(2),
                    Margin = new Thickness(0, 0, 3, 0),
                    Opacity = i >= recent.Count - streak ? 1 : .4,
                };
                pip.SetResourceReference(Border.BackgroundProperty, won ? "MpOk" : "StatsBarLoss");
                pips.Children.Add(pip);
            }
            stack.Children.Add(pips);
        }

        string? note = current.Note;
        if (note == null && streak > 0)
        {
            var from = (_historyRows ?? new List<MatchHistoryRow>())
                .Where(r => MatchHistoryView.IsRated(r) && IsTeamRow(r) == team)
                .Take(streak)
                .LastOrDefault();
            if (from != null && MatchHistoryView.ParseLocal(from.EndedAt) is { } date)
                note = Strings.Format("MpProfileStreakSince", ShortDate.Format(date));
        }
        if (note != null)
        {
            var line = StatsText(note, "StatsDeckCountSize", "StatsProse", FontWeights.Normal);
            line.Margin = new Thickness(0, 7, 0, 0);
            stack.Children.Add(line);
        }

        var box = new Border
        {
            Child = stack,
            Padding = new Thickness(12, 11, 12, 11),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
        };
        box.SetResourceReference(Border.BackgroundProperty, streak > 0 ? "ProfileGainFillSoft" : "StatsChipFill");
        box.SetResourceReference(Border.BorderBrushProperty, streak > 0 ? "ProfileGainRim" : "StatsChipRim");
        if (streak > 0)
        {
            box.ToolTip = TooltipHelper.Wrap(
                Strings.Format("MpProfileStreakTipTitle", streak) + "\n" + Strings.Get("MpProfileStreakTipBody"));
        }
        return box;
    }

    private static FrameworkElement BuildLongestStreakBox(LadderStanding ladder)
    {
        var stack = new StackPanel();
        stack.Children.Add(StatsText(Strings.Get("MpProfileStreakLong"), "StatsHeaderSize", "StatsNote",
                                     FontWeights.Bold));

        void Row(string key, int value, string ink)
        {
            var row = new Grid { Margin = new Thickness(0, 7, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var words = StatsText(Strings.Get(key), "StatsProseSize", "StatsSoft", FontWeights.Normal);
            words.Margin = new Thickness(0, 0, 8, 0);
            row.Children.Add(words);
            row.Children.Add(WithColumn(StatsMono(value.ToString(), "ProfileStreakBestSize", ink,
                                                  FontWeights.SemiBold), 1));

            var line = new Border
            {
                Child = row,
                Margin = new Thickness(0, 7, 0, 0),
                BorderThickness = new Thickness(0, 1, 0, 0),
            };
            line.SetResourceReference(Border.BorderBrushProperty, "StatsBandRule");
            stack.Children.Add(line);
        }

        Row("MpProfileStreakBestWins", ladder.StreakBest, "StatsWin");
        Row("MpProfileStreakBestLosses", ladder.LossStreakBest, "StatsLoss");

        var box = new Border
        {
            Child = stack,
            Padding = new Thickness(12, 11, 12, 11),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
        };
        box.SetResourceReference(Border.BackgroundProperty, "StatsChipFill");
        box.SetResourceReference(Border.BorderBrushProperty, "StatsChipRim");
        return box;
    }

    /// <summary>The placement, match by match: a square per match, what is left in words.</summary>
    private FrameworkElement BuildPlacementBox(LadderStanding ladder, int played, int required, bool team)
    {
        var stack = new StackPanel();

        var head = new Grid { Margin = new Thickness(0, 0, 0, 9) };
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(StatsText(Strings.Get("MpProfilePlacementBox"), "StatsHeaderSize", "StatsNote",
                                    FontWeights.Bold));
        head.Children.Add(WithColumn(StatsMono(
            Strings.Format("MpPlacementProgressShort", played, required), "StatsBodySize", "StatsStrong",
            FontWeights.SemiBold), 1));
        stack.Children.Add(head);

        var segments = PlacementView.Segments(played, required, ladder.PlacementResults);
        var cells = new Grid { Tag = "PlacementSegments" };
        for (int i = 0; i < segments.Count; i++)
        {
            cells.ColumnDefinitions.Add(new ColumnDefinition());
            cells.Children.Add(WithColumn(BuildPlacementCell(segments[i], i, i == segments.Count - 1), i));
        }
        stack.Children.Add(cells);

        int left = PlacementView.Remaining(played, required);
        if (left > 0)
        {
            var line = StatsProse(
                left == 1
                    ? Strings.Format("MpProfilePlacementLeftOneMode", ModeName(team))
                    : Strings.Format("MpProfilePlacementLeftMode", left, ModeName(team)),
                "StatsProseSize", "StatsSoft");
            line.Margin = new Thickness(0, 9, 0, 0);
            stack.Children.Add(line);
        }

        var box = new Border
        {
            Child = stack,
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(12, 11, 12, 11),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
        };
        box.SetResourceReference(Border.BackgroundProperty, "StatsChipFill");
        box.SetResourceReference(Border.BorderBrushProperty, "StatsChipRim");
        return box;
    }

    private static FrameworkElement BuildPlacementCell(PlacementView.Segment segment, int index, bool last)
    {
        string text = segment switch
        {
            PlacementView.Segment.Win => Strings.Get("MpPlacementWinShort"),
            PlacementView.Segment.Loss => Strings.Get("MpPlacementLossShort"),
            PlacementView.Segment.Pending => (index + 1).ToString(),
            _ => "",
        };
        string ink = segment switch
        {
            PlacementView.Segment.Win => "StatsWin",
            PlacementView.Segment.Loss => "StatsLoss",
            _ => "StatsLabel",
        };

        var cell = new Grid { Height = 30, Margin = new Thickness(0, 0, last ? 0 : 5, 0) };
        var label = StatsText(text, "StatsNoteSize", ink, FontWeights.Bold);
        label.HorizontalAlignment = HorizontalAlignment.Center;

        if (segment == PlacementView.Segment.Pending)
        {
            var rim = new Rectangle
            {
                RadiusX = 7,
                RadiusY = 7,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 3 },
            };
            rim.SetResourceReference(Shape.StrokeProperty, "StatsDimBar");
            cell.Children.Add(rim);
        }
        else
        {
            var fill = new Border { CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1) };
            fill.SetResourceReference(Border.BackgroundProperty,
                segment == PlacementView.Segment.Win ? "ProfileGainFill" : segment == PlacementView.Segment.Loss ? "ProfileLossFill" : "StatsChipFill");
            fill.SetResourceReference(Border.BorderBrushProperty,
                segment == PlacementView.Segment.Win ? "ProfileGainRim" : segment == PlacementView.Segment.Loss ? "ProfileLossRim" : "StatsChipRim");
            cell.Children.Add(fill);
        }

        cell.Children.Add(label);
        return cell;
    }

    // ---------------------------------------------------------------- a match

    /// <summary>
    /// The frame of a history card (95d): the verdict in its square, the lines beside it, the
    /// recording, and the players folded away until they are asked for.
    /// </summary>
    private Border FinishHistoryCard(
        MatchHistoryRow row, StackPanel body, IReadOnlyList<MatchParticipantLine> players,
        bool rated, MatchVerdict verdict, string? meId)
    {
        // ---- the actions: the recording on the left, the players on the right.
        var actions = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Only when the server says there IS one (has_replay). ReplayObjectKey is deliberately
        // not a reason to show it: that was the old local-disk upload, and its route is gone.
        if (row.HasReplay == true)
        {
            var download = new Button
            {
                Name = "DownloadReplayButton",
                Content = Strings.Get(IsReplaySaved(row.Id) ? "MpHistoryShowReplay" : "MpHistoryDownloadReplay"),
                ToolTip = TooltipHelper.Wrap(Strings.Get("MpHistoryDownloadReplayTip")),
                Style = (Style)Application.Current.FindResource("MpSecondaryButton"),
                FontSize = (double)Application.Current.FindResource("MpMetaSize"),
                Height = 30,
                Padding = new Thickness(12, 0, 12, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Tag = row.Id,
            };
            download.Click += async (_, _) => await DownloadHistoryReplayAsync(row, download);
            actions.Children.Add(download);
        }

        FrameworkElement? roster = null;
        if (players.Count > 0)
        {
            roster = BuildHistorySides(row, players);
            roster.Visibility = Visibility.Collapsed;

            var toggle = new Button
            {
                Content = Strings.Get("MpHistoryShowPlayers"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            toggle.SetResourceReference(FrameworkElement.StyleProperty, "MpLinkButton");
            toggle.SetResourceReference(Control.ForegroundProperty, "ProfileLinkText");
            toggle.SetResourceReference(Control.FontSizeProperty, "StatsNoteSize");
            var sides = roster;
            toggle.Click += (_, _) =>
            {
                bool open = sides.Visibility != Visibility.Visible;
                sides.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                toggle.Content = Strings.Get(open ? "MpHistoryHidePlayers" : "MpHistoryShowPlayers");
            };
            actions.Children.Add(WithColumn(toggle, 1));
        }

        if (actions.Children.Count > 0) body.Children.Add(actions);
        if (roster != null) body.Children.Add(roster);

        // ---- the card
        bool won = rated && verdict == MatchVerdict.Win;
        bool lost = rated && verdict == MatchVerdict.Loss;

        var mark = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Top,
            Child = StatsText(
                Strings.Get(won ? "MpPlacementWinShort" : lost ? "MpPlacementLossShort" : "MpDash"),
                "StatsBodySize", won ? "StatsWin" : lost ? "StatsLoss" : "StatsSoft", FontWeights.Bold),
        };
        ((TextBlock)mark.Child).HorizontalAlignment = HorizontalAlignment.Center;
        mark.SetResourceReference(Border.BackgroundProperty,
            won ? "ProfileGainFill" : lost ? "ProfileLossFill" : "StatsChipFill");
        mark.SetResourceReference(Border.BorderBrushProperty,
            won ? "ProfileGainRim" : lost ? "ProfileLossRim" : "StatsChipRim");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(mark);
        grid.Children.Add(WithColumn(body, 1));

        var card = new Border
        {
            Child = grid,
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(14, 12, 16, 12),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
        };
        card.SetResourceReference(Border.BackgroundProperty,
            won ? "ProfileWinFill" : lost ? "ProfileLossCardFill" : "GlassPanel");
        card.SetResourceReference(Border.BorderBrushProperty,
            won ? "ProfileWinRim" : lost ? "ProfileLossCardRim" : "ProfileCardRim");
        return card;
    }

    /// <summary>Who played, one side a column: you and yours, and the others.</summary>
    private FrameworkElement BuildHistorySides(MatchHistoryRow row, IReadOnlyList<MatchParticipantLine> players)
    {
        var me = players.FirstOrDefault(p => p.IsMe);
        bool teams = MatchParticipantsView.HasTeams(players);

        var mine = players.Where(p => teams && me != null ? p.Team == me.Team : p.IsMe).ToList();
        var others = players.Except(mine).ToList();
        if (mine.Count == 0)
        {
            mine = others.Take(others.Count / 2).ToList();
            others = others.Except(mine).ToList();
        }

        var grid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        FrameworkElement Side(string label, string ink, List<MatchParticipantLine> lines)
        {
            var side = new StackPanel();
            var title = StatsText(label, "StatsHeaderSize", ink, FontWeights.Bold);
            title.Margin = new Thickness(0, 0, 0, 8);
            side.Children.Add(title);
            foreach (var line in lines) side.Children.Add(BuildHistoryPlayerRow(line));
            return side;
        }

        grid.Children.Add(Side(
            Strings.Get(mine.Count > 1 ? "MpHistorySideYourTeam" : "MpHistorySideYou"), "StatsWin", mine));
        grid.Children.Add(WithColumn(Side(
            Strings.Get(others.Count > 1 ? "MpHistorySideRivals" : "MpHistorySideRival"), "StatsLoss", others), 2));

        var wrap = new Border
        {
            Child = grid,
            Margin = new Thickness(0, 10, 0, 0),
            BorderThickness = new Thickness(0, 1, 0, 0),
        };
        wrap.SetResourceReference(Border.BorderBrushProperty, "StatsHeadRule");
        return wrap;
    }

    // ---------------------------------------------------------------- decks

    private string? _profileDeckModId;

    /// <summary>The mod the Decks section is reading: the one picked there, or the active one.</summary>
    private ModProfile? ProfileDeckMod()
        => (_profileDeckModId != null ? ModRegistry.Find(_profileDeckModId) : null)
           ?? _getActiveProfile?.Invoke();

    /// <summary>
    /// The player's own decks (2026-11-10, over 95g): which mod, the decks listed by civilization with
    /// the game's ready-made ones folded and the empty ones only counted, and the chosen deck as a
    /// board of its cards with the game's art, in deck order, the chosen card read below it. These
    /// are the cards the player BRINGS: a deck holds 25 and a match may use five, and nobody else's
    /// deck can be read at all.
    /// </summary>
    private UIElement BuildProfileDecks()
    {
        var page = new StackPanel();

        var head = new WrapPanel { Orientation = Orientation.Horizontal };
        var heading = ProfileSectionTitle(Strings.Get("MpProfileSectionDecks"));
        heading.Margin = new Thickness(0, 0, 14, 0);
        heading.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(heading);
        var current = ProfileDeckMod();
        if (current != null)
        {
            var picker = BuildDeckModPicker(current);
            picker.Margin = new Thickness(0, 2, 10, 2);
            head.Children.Add(picker);
        }
        if (_mpDecksLoaded && _mpDeckProfiles.Count > 0) head.Children.Add(BuildDeckCivSearch());
        page.Children.Add(head);

        if (!_mpDecksLoaded)
        {
            page.Children.Add(ProfileNote(Strings.Get("MpStatsDecksLoading")));
            return page;
        }

        // Decks of ANOTHER mod left in this one's folder are not drawn, and the page says how many
        // and why: silently dropping them would read as decks going missing.
        if (_mpDeckForeignCount > 0)
            page.Children.Add(ProfileNote(Strings.Format("MpProfileDecksForeignHidden", _mpDeckForeignCount)));

        var book = OwnDeckView.Organize(_mpDeckProfiles, CivDisplayName);
        if (!book.All.Any())
        {
            page.Children.Add(ProfileNote(Strings.Get("MpStatsDecksEmpty")));
            return page;
        }
        if (_mpSelectedDeck == null || book.All.All(e => !ReferenceEquals(e.Deck, _mpSelectedDeck)))
        {
            var first = book.Mine.SelectMany(g => g.Decks).FirstOrDefault() ?? book.All.First();
            _mpSelectedDeck = first.Deck;
            _deckSelectedCard = null;
        }

        _deckListHost = new Border();
        _deckDetailHost = new Border();
        FillDeckList();
        FillDeckDetail();

        // Side by side as long as the list and a board of five cards fit (290 + 14 + 300).
        var stacked = ProfileContentWidth < DeckListWidth + 14 + 300;
        var body = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        if (stacked)
        {
            var column = new StackPanel();
            column.Children.Add(_deckListHost);
            _deckDetailHost.Margin = new Thickness(0, 16, 0, 0);
            column.Children.Add(_deckDetailHost);
            body.Children.Add(column);
        }
        else
        {
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(DeckListWidth) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });
            _deckListHost.VerticalAlignment = VerticalAlignment.Top;
            _deckDetailHost.VerticalAlignment = VerticalAlignment.Top;
            body.Children.Add(_deckListHost);
            body.Children.Add(WithColumn(_deckDetailHost, 2));
        }
        page.Children.Add(body);
        return page;
    }

    /// <summary>The list's width beside the board, and the most it grows before it scrolls.</summary>
    internal const double DeckListWidth = 290;
    internal const double DeckListMaxHeight = 560;

    private Border? _deckListHost;
    private Border? _deckDetailHost;
    private string _deckCivFilter = "";
    private bool _deckDefaultsOpen;
    private string? _deckSelectedCard;
    private readonly Dictionary<HomeCityDeckEntry, Button> _deckRows = new();

    /// <summary>"[icon] Wars of Liberty ▾": the mods to read decks from, as in the quick match bar.</summary>
    private Button BuildDeckModPicker(ModProfile current)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            Margin = new Thickness(0, 0, 7, 0),
            Background = ResolveRoomModIcon(current),
            VerticalAlignment = VerticalAlignment.Center,
        });
        var name = new TextBlock { Text = ResolveModDisplayName(current.Id), VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
        row.Children.Add(name);
        if (_mpDecksLoaded)
        {
            var count = StatsMono(_mpDeckProfiles.Sum(p => p.Decks.Count(d => d.Cards.Count > 0)).ToString(),
                                  "StatsDeckCountSize", "StatsNote", FontWeights.Medium);
            count.Margin = new Thickness(8, 0, 0, 0);
            count.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(count);
        }
        row.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M0,0 L4,4 L8,0"),
            Stroke = (Brush)Application.Current.FindResource("RmTextLabel"),
            StrokeThickness = 1.4,
            Width = 8,
            Height = 5,
            Margin = new Thickness(9, 1, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        var button = new Button { Content = row, Height = 30, Padding = new Thickness(10, 0, 10, 0), HorizontalAlignment = HorizontalAlignment.Left };
        button.SetResourceReference(FrameworkElement.StyleProperty, "RmRowButton");
        System.Windows.Automation.AutomationProperties.SetAutomationId(button, "ProfileDecks:Mod");
        System.Windows.Automation.AutomationProperties.SetName(button, ResolveModDisplayName(current.Id));
        button.Click += (_, _) =>
        {
            var mods = StatsModOptions().ToList();
            var popup = ModSearchPopup.Build(button, mods, _ => true, current,
                p => ResolveRoomModIcon(p),
                p => ModTheme.AccentOf(p.AccentColor, _config?.UseModColor ?? true),
                picked =>
                {
                    if (string.Equals(ProfileDeckMod()?.Id, picked.Id, StringComparison.OrdinalIgnoreCase)) return;
                    _profileDeckModId = picked.Id;
                    _mpDecksLoaded = false;
                    _mpSelectedDeck = null;
                    _deckSelectedCard = null;
                    _deckCivFilter = "";
                    RenderProfileTab();
                });
            if (popup.Child is Border { Child: FrameworkElement body }) body.Width = Math.Max(body.Width, 300);
            ChromePopups.Track(popup, button);
            popup.IsOpen = true;
        };
        return button;
    }

    /// <summary>The civilization search: refills the list alone, so the box keeps its focus.</summary>
    private FrameworkElement BuildDeckCivSearch()
    {
        var box = new TextBox { Text = _deckCivFilter, Width = 220, VerticalContentAlignment = VerticalAlignment.Center };
        box.SetResourceReference(FrameworkElement.StyleProperty, "DialogBareText");
        box.SetResourceReference(Control.FontSizeProperty, "StatsProseSize");
        var hint = new TextBlock
        {
            Text = Strings.Get("MpProfileDeckSearch"),
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = string.IsNullOrEmpty(_deckCivFilter) ? Visibility.Visible : Visibility.Collapsed,
        };
        hint.SetResourceReference(TextBlock.FontSizeProperty, "StatsProseSize");
        hint.SetResourceReference(TextBlock.ForegroundProperty, "WindowFineText");
        var field = new Grid();
        field.Children.Add(box);
        field.Children.Add(hint);
        box.TextChanged += (_, _) =>
        {
            hint.Visibility = string.IsNullOrEmpty(box.Text) ? Visibility.Visible : Visibility.Collapsed;
            _deckCivFilter = box.Text;
            FillDeckList();
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(box, "ProfileDecks:Search");
        var shell = new Border
        {
            Height = 30,
            Padding = new Thickness(10, 0, 10, 0),
            CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1),
            Child = field,
            VerticalAlignment = VerticalAlignment.Center,
        };
        shell.SetResourceReference(Border.BackgroundProperty, "GlassWell");
        shell.SetResourceReference(Border.BorderBrushProperty, "MpRimSoft");
        return shell;
    }

    /// <summary>The list: one heading per civilization, its decks under it, the game's ready-made
    /// decks folded at the end and the empty ones only counted.</summary>
    private void FillDeckList()
    {
        if (_deckListHost == null) return;
        _deckRows.Clear();
        var book = OwnDeckView.Organize(_mpDeckProfiles, CivDisplayName, _deckCivFilter);
        var list = new StackPanel();
        foreach (var group in book.Mine) AddDeckGroup(list, group);
        if (book.Mine.Count == 0 && book.DefaultCount == 0)
            list.Children.Add(ProfileNote(Strings.Get("MpProfileDeckNoMatch")));

        if (book.DefaultCount > 0)
        {
            var toggle = new Button
            {
                Content = Strings.Format(_deckDefaultsOpen ? "MpProfileDeckDefaultsOpen" : "MpProfileDeckDefaults", book.DefaultCount),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 12, 0, 4),
            };
            toggle.SetResourceReference(FrameworkElement.StyleProperty, "MpLinkButton");
            System.Windows.Automation.AutomationProperties.SetAutomationId(toggle, "ProfileDecks:Defaults");
            toggle.Click += (_, _) =>
            {
                _deckDefaultsOpen = !_deckDefaultsOpen;
                FillDeckList();
            };
            list.Children.Add(toggle);
            if (_deckDefaultsOpen)
                foreach (var group in book.Defaults) AddDeckGroup(list, group);
        }
        if (book.Empty > 0)
            list.Children.Add(ProfileNote(Strings.Format("MpProfileDeckEmptyHidden", book.Empty)));

        _deckListHost.Child = new ScrollViewer
        {
            Content = list,
            MaxHeight = DeckListMaxHeight,
            Padding = new Thickness(0, 0, 8, 0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
    }

    private void AddDeckGroup(Panel list, OwnDeckCivGroup group)
    {
        var header = new Grid { Margin = new Thickness(0, list.Children.Count == 0 ? 0 : 10, 0, 4) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        if (!string.IsNullOrWhiteSpace(group.Civ) && _mpDeckCivFlags.TryGetValue(group.Civ, out var flag))
        {
            var picture = ArtImage(flag, CivFlagSize);
            picture.Margin = new Thickness(0, 0, 8, 0);
            header.Children.Add(picture);
        }
        var civName = StatsText(group.CivName, "StatsSubtitleSize", "StatsStrong", FontWeights.SemiBold);
        civName.TextTrimming = TextTrimming.CharacterEllipsis;
        civName.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(WithColumn(civName, 1));
        list.Children.Add(header);
        foreach (var entry in group.Decks) list.Children.Add(BuildProfileDeckRow(entry));
    }

    private Button BuildProfileDeckRow(DeckListEntry entry)
    {
        var stack = new StackPanel();
        var first = StatsText(entry.Name ?? entry.Profile.CityName, "StatsProseSize", "StatsStrong", FontWeights.Medium);
        first.TextTrimming = TextTrimming.CharacterEllipsis;
        stack.Children.Add(first);
        var facts = new List<string>();
        if (entry.Name != null && !string.IsNullOrWhiteSpace(entry.Profile.CityName)) facts.Add(entry.Profile.CityName);
        if (entry.Profile.Level > 0) facts.Add(Strings.Format("MpProfileDeckLevel", entry.Profile.Level));
        facts.Add(Strings.Format("MpProfileDeckCards", entry.Deck.Cards.Count));
        var second = StatsText(string.Join(" · ", facts), "StatsNoteSize", "StatsNote", FontWeights.Normal);
        second.TextTrimming = TextTrimming.CharacterEllipsis;
        second.Margin = new Thickness(0, 2, 0, 0);
        stack.Children.Add(second);

        var row = new Button
        {
            Content = stack,
            Tag = ReferenceEquals(entry.Deck, _mpSelectedDeck) ? "active" : null,
            Margin = new Thickness(0, 0, 0, 4),
            Padding = new Thickness(11, 7, 11, 7),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        row.SetResourceReference(FrameworkElement.StyleProperty, "ProfileDeckTile");
        System.Windows.Automation.AutomationProperties.SetName(row, CivDisplayName(entry.Profile) + " · " + (entry.Name ?? entry.Profile.CityName));
        _deckRows[entry.Deck] = row;
        row.Click += (_, _) =>
        {
            if (ReferenceEquals(_mpSelectedDeck, entry.Deck)) return;
            if (_mpSelectedDeck != null && _deckRows.TryGetValue(_mpSelectedDeck, out var before)) before.Tag = null;
            _mpSelectedDeck = entry.Deck;
            _deckSelectedCard = null;
            row.Tag = "active";
            FillDeckDetail();
        };
        return row;
    }

    /// <summary>The chosen deck: its board of cards in deck order, and the chosen card read below.</summary>
    private void FillDeckDetail()
    {
        if (_deckDetailHost == null || _mpSelectedDeck == null) return;
        var profile = _mpDeckProfiles.FirstOrDefault(p => p.Decks.Contains(_mpSelectedDeck));
        if (profile == null) return;
        _mpSelectedDeckCiv = profile.Civ;
        _deckDetailHost.Child = BuildProfileDeckDetail(profile, _mpSelectedDeck);
    }

    private Border BuildProfileDeckDetail(HomeCityProfile profile, HomeCityDeckEntry deck)
    {
        string civ = CivDisplayName(profile);
        string name = OwnDeckView.IsGenericName(deck.Name) ? civ : civ + " \u00b7 " + deck.Name.Trim();

        var stack = new StackPanel();
        var title = new TextBlock { Text = name, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
        title.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        title.SetResourceReference(TextBlock.FontSizeProperty, "ProfileDeckTitleSize");
        title.SetResourceReference(TextBlock.ForegroundProperty, "StatsStrong");
        stack.Children.Add(title);

        var facts = new List<string> { profile.CityName };
        if (profile.Level > 0) facts.Add(Strings.Format("MpProfileDeckLevel", profile.Level));
        facts.Add(Strings.Format("MpProfileDeckCards", deck.Cards.Count));
        facts.Add(ResolveModDisplayName(ProfileDeckMod()?.Id ?? ""));
        var sub = StatsProse(string.Join(" · ", facts.Where(f => !string.IsNullOrWhiteSpace(f))));
        sub.Margin = new Thickness(0, 4, 0, 0);
        stack.Children.Add(sub);

        // The community's figures, only when they are about this mod: the Statistics page scopes
        // them to its own.
        bool sameMod = string.Equals(StatsModId(), ProfileDeckMod()?.Id, StringComparison.OrdinalIgnoreCase);
        var compared = OwnDeckView.CompareWithCommunity(
            deck.Cards.Select(c => c.InternalName), profile.Civ, sameMod ? _deckStats?.Cards : null);

        // The board: the game's art, in deck order (the order IS the slot), never sorted.
        var board = new WrapPanel { Margin = new Thickness(0, 12, -6, -6) };
        var tiles = DeckTiles.Build(deck, _mpCardDetails, _mpCardIcons, ProfileDeckTileSize, "MpRimSoft");
        var cardInfo = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        Border? selectedFace = null;
        void Show(Button tile, string card)
        {
            _deckSelectedCard = card;
            selectedFace = DeckTiles.Select(tile, selectedFace);
            FillCardInfo(cardInfo, profile, card, compared.Shares.TryGetValue(card, out var s) ? s : null, civ);
        }
        foreach (var tile in tiles)
        {
            tile.Margin = new Thickness(0, 0, 6, 6);
            var card = tile.Tag as string ?? "";
            _mpCardDetails.TryGetValue(card, out var detail);
            tile.ToolTip = TooltipHelper.Wrap(detail?.Name ?? card);
            tile.Click += (_, _) => Show(tile, card);
            board.Children.Add(tile);
        }
        stack.Children.Add(board);

        var chosen = tiles.FirstOrDefault(t => string.Equals(t.Tag as string, _deckSelectedCard, StringComparison.Ordinal)) ?? tiles.FirstOrDefault();
        if (chosen != null) Show(chosen, chosen.Tag as string ?? "");
        stack.Children.Add(cardInfo);

        if (compared.Others.Count > 0)
        {
            var cards = new WrapPanel { Margin = new Thickness(0, 6, -5, -5) };
            foreach (var (card, percent) in compared.Others)
            {
                _mpCardDetails.TryGetValue(card, out var detail);
                var vocabulary = DeckCardNames.Peek(StatsModId());
                cards.Children.Add(BuildDeckChip(
                    detail?.Name ?? vocabulary?.NameOf(card) ?? card, vocabulary?.IconOf(card), percent, community: true,
                    Strings.Format("MpProfileDeckCardTip", percent, civ)));
            }
            stack.Children.Add(DeckAgeBlock(Strings.Get("MpProfileDeckCommunity"), cards));
        }

        if (compared.Decks >= DeckStatsView.MinDecksForPercent)
        {
            var note = StatsProse(
                Strings.Format("MpProfileDeckCompared", compared.Decks, civ, DeckStatsView.TailMaxPlayers),
                "StatsNoteSize", "StatsNote");
            note.Margin = new Thickness(0, 12, 0, 0);
            stack.Children.Add(note);
        }

        return ProfileGlass(stack, new Thickness(16, 14, 16, 14));
    }

    /// <summary>A card on the board: 40 px, the prototype's size, five to a row in the narrowest column.</summary>
    internal const int ProfileDeckTileSize = 40;

    /// <summary>The chosen card: its name, its age when known, what it does, and how many of the
    /// community's decks bring it. A card whose age is not known simply does not say one.</summary>
    private void FillCardInfo(Panel info, HomeCityProfile profile, string card, int? share, string civ)
    {
        info.Children.Clear();
        _mpCardDetails.TryGetValue(card, out var detail);
        var line = new WrapPanel();
        var cardName = StatsText(detail?.Name ?? card, "StatsSubtitleSize", "StatsStrong", FontWeights.SemiBold);
        cardName.Margin = new Thickness(0, 0, 10, 0);
        line.Children.Add(cardName);
        if (_mpDeckFacts.TryGetValue(HomeCityCardFacts.Key(profile.Civ, card), out var fact)
            && OwnDeckView.AgeNumeral(fact.Age) is { } numeral)
        {
            var age = StatsText(Strings.Format("MpProfileDeckCardAge", numeral), "StatsNoteSize", "StatsLabel", FontWeights.SemiBold);
            age.VerticalAlignment = VerticalAlignment.Center;
            line.Children.Add(age);
        }
        info.Children.Add(line);
        if (!string.IsNullOrWhiteSpace(detail?.Description))
        {
            var text = StatsProse(detail!.Description);
            text.Margin = new Thickness(0, 6, 0, 0);
            info.Children.Add(text);
        }
        if (share != null)
        {
            var note = StatsProse(Strings.Format("MpProfileDeckCardTip", share.Value, civ), "StatsNoteSize", "StatsNote");
            note.Margin = new Thickness(0, 6, 0, 0);
            info.Children.Add(note);
        }
    }

    private static TextBlock ProfileNote(string text)
    {
        var note = StatsProse(text, "StatsProseSize", "StatsNote");
        note.Margin = new Thickness(0, 10, 0, 0);
        return note;
    }

    private static FrameworkElement DeckAgeBlock(string heading, UIElement cards)
    {
        var block = new StackPanel();
        block.Children.Add(StatsText(heading, "StatsHeaderSize", "StatsLabel", FontWeights.Bold));
        block.Children.Add(cards);

        var wrap = new Border
        {
            Child = block,
            Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(0, 8, 0, 0),
            BorderThickness = new Thickness(0, 1, 0, 0),
        };
        wrap.SetResourceReference(Border.BorderBrushProperty, "StatsRowRule");
        return wrap;
    }

    private static string CardTip(string card, CardDetail? detail, int? share, string civ)
    {
        var lines = new List<string> { detail?.Name ?? card };
        if (!string.IsNullOrWhiteSpace(detail?.Description)) lines.Add(detail!.Description);
        if (share != null) lines.Add(Strings.Format("MpProfileDeckCardTip", share.Value, civ));
        return string.Join("\n", lines);
    }

    internal static FrameworkElement BuildDeckChip(
        string name, ImageSource? art, int? percent, bool community, string tip)
    {
        var inner = new StackPanel { Orientation = Orientation.Horizontal };
        if (art != null)
        {
            var picture = ArtImage(art, 24);
            picture.Margin = new Thickness(0, 0, 6, 0);
            inner.Children.Add(picture);
        }
        inner.Children.Add(StatsText(name, "StatsNoteSize", "StatsFigure", FontWeights.Medium));
        if (percent != null)
        {
            var share = StatsMono(percent.Value + " %", "StatsLabelSize", "ProfileLinkText", FontWeights.SemiBold);
            share.Margin = new Thickness(6, 0, 0, 0);
            inner.Children.Add(share);
        }

        var chip = new Border
        {
            Height = art != null ? 32 : 24,
            Padding = new Thickness(art != null ? 4 : 9, 0, 9, 0),
            Margin = new Thickness(0, 0, 5, 5),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Child = inner,
            ToolTip = TooltipHelper.Wrap(tip),
        };
        ToolTipService.SetInitialShowDelay(chip, 0);
        ToolTipService.SetShowDuration(chip, 30_000);
        chip.SetResourceReference(Border.BackgroundProperty, community ? "ProfileCommunityFill" : "StatsChipFill");
        chip.SetResourceReference(Border.BorderBrushProperty, community ? "ProfileCommunityRim" : "StatsChipRim");
        return chip;
    }

    // ---------------------------------------------------------------- civilizations

    private void RenderProfileCivilizations(Panel body)
    {
        body.Children.Add(ProfileSectionTitle(Strings.Get("MpProfileSectionCivs")));

        var rows = CivStatsView.Rows(_historyRows);
        var stack = new StackPanel();

        if (rows.Count == 0)
        {
            var empty = StatsProse(Strings.Get("MpProfileCivsEmpty"));
            empty.Margin = new Thickness(16, 14, 16, 14);
            stack.Children.Add(empty);
        }
        else
        {
            var head = ProfileCivGrid();
            head.Margin = new Thickness(16, 12, 16, 8);
            string[] titles =
            {
                Strings.Get("MpCivColCiv"), Strings.Get("MpCivColPlayed"),
                Strings.Get("MpStatsColRecord"), Strings.Get("MpActivityRankColPct"),
            };
            for (int i = 0; i < titles.Length; i++)
            {
                var title = StatsText(titles[i], "StatsLabelSize", "StatsLabel", FontWeights.Bold);
                if (i == 3) title.HorizontalAlignment = HorizontalAlignment.Right;
                head.Children.Add(WithColumn(title, i));
            }
            stack.Children.Add(head);

            var civNames = DeckCardNames.Peek(StatsModId());
            foreach (var row in rows) stack.Children.Add(BuildProfileCivRow(row, civNames?.CivIconOf(row.Civ)));

            int noCiv = _historyRows?.Count(r => string.IsNullOrWhiteSpace(r.Civ)) ?? 0;
            var note = StatsProse(
                Strings.Format("MpProfileCivsRules", CivStatsView.MinDecidedForPercent)
                + (noCiv > 0
                    ? " " + Strings.Format(noCiv == 1 ? "MpProfileCivsNoCivOne" : "MpProfileCivsNoCiv", noCiv)
                    : ""),
                "StatsNoteSize", "StatsNote");

            var foot = new Border
            {
                Padding = new Thickness(16, 10, 16, 14),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Child = note,
            };
            foot.SetResourceReference(Border.BorderBrushProperty, "StatsRowRule");
            stack.Children.Add(foot);
        }

        var card = ProfileGlass(stack, new Thickness(0));
        card.Margin = new Thickness(0, 14, 0, 0);
        body.Children.Add(card);
    }

    private static Grid ProfileCivGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(108) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        return grid;
    }

    /// <summary>
    /// One civilization: name, matches, record, and a percentage only when there is enough behind
    /// it to state one.
    /// </summary>
    /// <remarks><c>internal static</c> so <c>DialogXamlTests</c> can build the real row.</remarks>
    internal static FrameworkElement BuildProfileCivRow(CivStatRow row, ImageSource? flag = null)
    {
        var grid = ProfileCivGrid();
        grid.Height = 40;

        var name = StatsText(row.Civ, "StatsRowNameSizeCompact", "StatsFigure", FontWeights.Medium);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.Margin = new Thickness(0, 0, 12, 0);
        if (flag != null)
        {
            var cell = new Grid();
            cell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cell.ColumnDefinitions.Add(new ColumnDefinition());
            var picture = ArtImage(flag, CivFlagSize);
            picture.Margin = new Thickness(0, 0, 8, 0);
            cell.Children.Add(picture);
            Grid.SetColumn(name, 1);
            cell.Children.Add(name);
            grid.Children.Add(cell);
        }
        else grid.Children.Add(name);

        grid.Children.Add(WithColumn(
            StatsMono(row.Played.ToString(), "StatsBodySize", "StatsSoft", FontWeights.Medium), 1));

        var record = BuildRecordCell(row.Wins, row.Losses, 0, 1);
        record.HorizontalAlignment = HorizontalAlignment.Left;
        record.FontWeight = FontWeights.SemiBold;
        grid.Children.Add(WithColumn(record, 2));

        // Null below the bar, and then NOTHING is drawn: not an em dash where a number would go.
        var pct = CivStatsView.WinPercent(row);
        var percent = StatsMono(
            pct == null ? "" : pct.Value + " %",
            "StatsBodySize",
            pct == null ? "StatsLabel" : RankingTableLayout.PercentBrushKey(pct.Value),
            FontWeights.SemiBold);
        percent.HorizontalAlignment = HorizontalAlignment.Right;
        grid.Children.Add(WithColumn(percent, 3));

        var line = new Border
        {
            Padding = new Thickness(16, 0, 16, 0),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = grid,
        };
        line.SetResourceReference(Border.BorderBrushProperty, "StatsRowRule");
        return line;
    }
}

/// <summary>
/// The rating line (95a): the area under it, three reference lines with their values, the highest
/// and the lowest named, the current rating as a lit dot, and the match under the pointer.
///
/// <para>A <c>Canvas</c> redrawn at each width rather than a stretched <c>Polyline</c>: a stretch
/// scales the dots and the labels with the line, and the labels are the point.</para>
/// </summary>
internal sealed class RatingCurveChart : Grid
{
    private const double RightGutter = 38;

    private readonly IReadOnlyList<double> _points;
    private readonly IReadOnlyList<string?> _tips;
    private readonly Canvas _canvas = new() { ClipToBounds = false, Background = Brushes.Transparent };
    private readonly Border _tip;
    private readonly TextBlock _tipText;
    private readonly Rectangle _cursor;
    private readonly Ellipse _cursorDot;

    internal RatingCurveChart(IReadOnlyList<double> points, IReadOnlyList<string?> tips, double height)
    {
        _points = points;
        _tips = tips;
        Height = height;
        Children.Add(_canvas);

        _tipText = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        _tipText.SetResourceReference(TextBlock.FontSizeProperty, "StatsNoteSize");
        _tipText.SetResourceReference(TextBlock.ForegroundProperty, "StatsFigure");
        _tip = new Border
        {
            Padding = new Thickness(11, 7, 11, 7),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Child = _tipText,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        _tip.SetResourceReference(Border.BackgroundProperty, "ProfileChartTip");
        _tip.SetResourceReference(Border.BorderBrushProperty, "StatsChipRim");

        _cursor = new Rectangle { Width = 1, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        _cursor.SetResourceReference(Shape.FillProperty, "StatsDimBar");
        _cursorDot = new Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = Brushes.White,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };

        SizeChanged += (_, _) => Draw();
        MouseMove += OnMove;
        MouseLeave += (_, _) => HideCursor();
    }

    private double PlotWidth => Math.Max(10, ActualWidth - RightGutter);

    private double X(int index) => index * PlotWidth / Math.Max(1, _points.Count - 1);

    private double Y(double value)
    {
        double low = _points.Min();
        double span = Math.Max(1, _points.Max() - low);
        // Room above the highest point and under the lowest for their labels.
        double top = 34, bottom = 30;
        return top + (1 - (value - low) / span) * Math.Max(10, ActualHeight - top - bottom);
    }

    private void Draw()
    {
        _canvas.Children.Clear();
        if (ActualWidth <= 0 || ActualHeight <= 0 || _points.Count < 2) return;

        // Reference lines: the extremes and the middle, on round numbers.
        double low = _points.Min(), high = _points.Max();
        foreach (double value in new[] { high, (high + low) / 2, low })
        {
            double y = Y(value);
            var rule = new Rectangle { Width = PlotWidth, Height = 1 };
            rule.SetResourceReference(Shape.FillProperty, "StatsRowRule");
            Canvas.SetTop(rule, y);
            _canvas.Children.Add(rule);

            var label = new TextBlock { Text = ((int)Math.Round(value)).ToString() };
            label.SetResourceReference(TextBlock.FontSizeProperty, "StatsHeaderSize");
            label.SetResourceReference(TextBlock.ForegroundProperty, "StatsLabel");
            Canvas.SetLeft(label, PlotWidth + 10);
            Canvas.SetTop(label, y - 7);
            _canvas.Children.Add(label);
        }

        // The area and the line, through one geometry so they cannot disagree.
        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(new Point(X(0), Y(_points[0])), false, false);
            for (int i = 1; i < _points.Count; i++)
                ctx.LineTo(new Point(X(i), Y(_points[i])), true, true);
        }
        line.Freeze();

        var area = new StreamGeometry();
        using (var ctx = area.Open())
        {
            ctx.BeginFigure(new Point(X(0), ActualHeight), true, true);
            for (int i = 0; i < _points.Count; i++)
                ctx.LineTo(new Point(X(i), Y(_points[i])), true, false);
            ctx.LineTo(new Point(X(_points.Count - 1), ActualHeight), true, false);
        }
        area.Freeze();

        var fill = new System.Windows.Shapes.Path { Data = area };
        fill.SetResourceReference(Shape.FillProperty, "ProfileChartFill");
        _canvas.Children.Add(fill);

        var stroke = new System.Windows.Shapes.Path
        {
            Data = line,
            StrokeThickness = 2.5,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };
        stroke.SetResourceReference(Shape.StrokeProperty, "ProfileChartLine");
        _canvas.Children.Add(stroke);

        int highest = IndexOf(high), lowest = IndexOf(low);
        Marker(highest, Strings.Format("MpProfileCurveMax", (int)Math.Round(high)), above: true);
        Marker(lowest, Strings.Format("MpProfileCurveMin", (int)Math.Round(low)), above: false);

        // Where the player is now.
        var halo = new Ellipse { Width = 20, Height = 20 };
        halo.SetResourceReference(Shape.FillProperty, "ProfileGainFill");
        Canvas.SetLeft(halo, X(_points.Count - 1) - 10);
        Canvas.SetTop(halo, Y(_points[^1]) - 10);
        _canvas.Children.Add(halo);

        var dot = new Ellipse { Width = 12, Height = 12 };
        dot.SetResourceReference(Shape.FillProperty, "MpOk");
        Canvas.SetLeft(dot, X(_points.Count - 1) - 6);
        Canvas.SetTop(dot, Y(_points[^1]) - 6);
        _canvas.Children.Add(dot);

        _canvas.Children.Add(_cursor);
        _canvas.Children.Add(_cursorDot);
        _canvas.Children.Add(_tip);
    }

    private int IndexOf(double value)
    {
        for (int i = 0; i < _points.Count; i++)
            if (_points[i] == value) return i;
        return 0;
    }

    private void Marker(int index, string text, bool above)
    {
        double x = X(index), y = Y(_points[index]);

        var dot = new Ellipse { Width = 8, Height = 8, StrokeThickness = 2 };
        dot.SetResourceReference(Shape.FillProperty, "MpAppBg");
        dot.SetResourceReference(Shape.StrokeProperty, above ? "StatsFigure" : "StatsProse");
        Canvas.SetLeft(dot, x - 4);
        Canvas.SetTop(dot, y - 4);
        _canvas.Children.Add(dot);

        var label = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold };
        label.SetResourceReference(TextBlock.FontSizeProperty, "StatsLabelSize");
        label.SetResourceReference(TextBlock.ForegroundProperty, above ? "StatsFigure" : "StatsProse");

        var chip = new Border
        {
            Height = 20,
            Padding = new Thickness(8, 0, 8, 0),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Child = label,
        };
        label.VerticalAlignment = VerticalAlignment.Center;
        chip.SetResourceReference(Border.BackgroundProperty, "ProfileChartTip");
        chip.SetResourceReference(Border.BorderBrushProperty, "StatsChipRim");

        chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double width = chip.DesiredSize.Width;
        Canvas.SetLeft(chip, Math.Clamp(x - width / 2, 0, Math.Max(0, PlotWidth - width)));
        Canvas.SetTop(chip, above ? y - 30 : y + 10);
        _canvas.Children.Add(chip);
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        double x = e.GetPosition(this).X;
        int index = (int)Math.Round(x / PlotWidth * (_points.Count - 1));
        index = Math.Clamp(index, 0, _points.Count - 1);

        string? tip = index < _tips.Count ? _tips[index] : null;
        if (tip == null)
        {
            HideCursor();
            return;
        }

        double px = X(index), py = Y(_points[index]);

        _cursor.Height = ActualHeight;
        Canvas.SetLeft(_cursor, px);
        _cursor.Visibility = Visibility.Visible;

        Canvas.SetLeft(_cursorDot, px - 5);
        Canvas.SetTop(_cursorDot, py - 5);
        _cursorDot.Visibility = Visibility.Visible;

        _tipText.Text = tip;
        _tip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double width = _tip.DesiredSize.Width;
        Canvas.SetLeft(_tip, Math.Clamp(px - width / 2, 0, Math.Max(0, ActualWidth - width)));
        Canvas.SetTop(_tip, py < 60 ? py + 12 : py - 46);
        _tip.Visibility = Visibility.Visible;
    }

    private void HideCursor()
    {
        _tip.Visibility = Visibility.Collapsed;
        _cursor.Visibility = Visibility.Collapsed;
        _cursorDot.Visibility = Visibility.Collapsed;
    }
}
