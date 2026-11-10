using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// The podium and "Your position" of the Clasificación page (design 93).
///
/// <para><b>The podium</b> is the first three of the ranked list, drawn second · first · third, each
/// on a pedestal of its age's colour — taller the better the place. Places, ages and ELO are the
/// server's; nothing is renumbered. First place carries a light that crosses the card every few
/// seconds: ONE clock, kept and stopped through its controller, running only while the page can be
/// seen and never under reduced effects (the rules of <see cref="RankBadge"/>).</para>
///
/// <para><b>Only the viewer's own card opens something</b> (his profile). The prototype opens every
/// player's profile; the launcher has no page for another player, and a card that looks clickable
/// and does nothing is worse than one that does not.</para>
/// </summary>
public partial class MultiplayerTab
{
    /// <summary>The <c>Tag</c>s of the podium's cards and of "Your position", for the tests.</summary>
    internal const string PodiumCardTag = "RankingPodiumCard";
    internal const string YouCardTag = "RankingYouCard";
    internal const string GroupHeadTag = "RankingGroupHead";

    private RankingPodiumFluid _podiumFluid = RankingPodiumFluid.For(0);

    /// <summary>The podium's sizes for the page as it is laid out now, with the text-size setting.</summary>
    private RankingPodiumFluid CurrentPodiumFluid()
        => RankingPodiumFluid.For(RankingPageWidthOverride ?? RankingPage?.ActualWidth ?? 0, TextScale.CurrentFactor);

    /// <summary>The moving light on first place; null when there is none.</summary>
    private AnimationClock? _podiumSheenClock;

    private static Color Rgba(string hex, double alpha)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        return Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), c.R, c.G, c.B);
    }

    private static Brush Frozen(Brush brush)
    {
        if (brush.CanFreeze) brush.Freeze();
        return brush;
    }

    /// <summary>A left-to-right gradient, as CSS's <c>linear-gradient(90deg, …)</c>.</summary>
    private static Brush Across(params (Color Color, double Offset)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        foreach (var (color, offset) in stops) brush.GradientStops.Add(new GradientStop(color, offset));
        return Frozen(brush);
    }

    /// <summary>A gradient from the top-left corner down, as CSS's <c>linear-gradient(155deg, …)</c>.</summary>
    private static Brush Diagonal(params (Color Color, double Offset)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0.2, 0), EndPoint = new Point(0.8, 1) };
        foreach (var (color, offset) in stops) brush.GradientStops.Add(new GradientStop(color, offset));
        return Frozen(brush);
    }

    /// <summary>
    /// Who gained ELO this month and how much, by player id: the month's first climber from the
    /// community payload, and the top five when the Highlights view has fetched them. Nobody else
    /// gets a figure — there is none to give.
    /// </summary>
    private Dictionary<string, HighlightPlayer> RankingClimbs(bool team)
    {
        var map = new Dictionary<string, HighlightPlayer>(StringComparer.Ordinal);
        void Add(HighlightPlayer? p)
        {
            if (p == null || string.IsNullOrEmpty(p.UserId) || (p.Points ?? 0) <= 0) return;
            map[p.UserId] = p;
        }
        var month = _communityStats?.MonthlyHighlights?.Current;
        Add(team ? month?.BiggestClimb?.Team : month?.BiggestClimb?.Default);
        var leaders = (_eloPreview || _demoStats ? EloDemoData.Highlights() : _rankingLeaders)?.Current?.Leaders?.BiggestClimb;
        foreach (var p in (team ? leaders?.Team : leaders?.Default) ?? new List<HighlightPlayer>()) Add(p);
        return map;
    }

    /// <summary>The month the climbs are about, in words ("octubre"); null when unknown.</summary>
    private string? RankingClimbMonth()
        => HighlightsView.MonthName(_communityStats?.MonthlyHighlights?.Current?.Month, Strings.Culture);

    /// <summary>"+96 in October" after a figure, with what it is made of on hover; nothing when the
    /// player is not among the month's climbers.</summary>
    private void AddClimbRuns(TextBlock line, HighlightPlayer? climb, string? month)
    {
        if (climb?.Points is not int points || points <= 0) return;
        line.Inlines.Add(new Run("   +" + points)
        {
            FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"),
            FontSize = (double)Application.Current.FindResource("MpMetaSize"),
            FontWeight = FontWeights.SemiBold,
            Foreground = Solid("#7FE0A8"),
        });
        if (!string.IsNullOrEmpty(month))
            line.Inlines.Add(new Run(" " + Strings.Format("RkInMonth", month))
            {
                FontSize = (double)Application.Current.FindResource("MpLabelSize"),
                FontWeight = FontWeights.Normal,
                Foreground = Solid("#A8BCD2"),
            });
        line.ToolTip = TooltipHelper.Wrap(climb is { RatingFrom: int from, RatingTo: int to, Matches: int played }
            ? Strings.Format("RkClimbTip", from, to, played)
            : Strings.Get("RkClimbTipPlain"));
    }

    /// <summary>An avatar with its age's ring and its rank badge on the lower right corner.</summary>
    private FrameworkElement BuildRingedAvatar(string name, string? avatarUrl, double size, string light, FrameworkElement badge)
    {
        var host = new Grid { Width = size + 4, Height = size + 4, VerticalAlignment = VerticalAlignment.Center };
        var disc = BuildAvatarDisc(name, avatarUrl, size);
        disc.HorizontalAlignment = HorizontalAlignment.Center;
        host.Children.Add(disc);
        host.Children.Add(new Border
        {
            CornerRadius = new CornerRadius((size + 4) / 2),
            BorderBrush = Solid(light),
            BorderThickness = new Thickness(2),
            IsHitTestVisible = false,
        });
        badge.HorizontalAlignment = HorizontalAlignment.Right;
        badge.VerticalAlignment = VerticalAlignment.Bottom;
        badge.Margin = new Thickness(0, 0, -6, -7);
        host.Children.Add(badge);
        return host;
    }

    /// <summary>
    /// The page gap, under the podium and between its cards. Kept apart from the drawing so a
    /// window that crosses the 1600-px line gets the new gap at once, without a redraw.
    /// </summary>
    private void ApplyPodiumGap(double gap)
    {
        if (RankingPodiumHost == null) return;
        RankingPodiumHost.Margin = new Thickness(0, 0, 0, gap);
        // The odd columns are the gaps between two cards.
        for (var i = 1; i < RankingPodiumHost.ColumnDefinitions.Count; i += 2)
            RankingPodiumHost.ColumnDefinitions[i].Width = new GridLength(gap);
    }

    /// <summary>Draws the podium from the ranked list; hides it when nobody is ranked.</summary>
    private void RenderRankingPodium(IReadOnlyList<LeaderboardRow> ranked, bool team, int ladderSize)
    {
        StopPodiumSheen();
        RankingPodiumHost.Children.Clear();
        RankingPodiumHost.ColumnDefinitions.Clear();
        var drawn = RankingGroups.PodiumDrawOrder(ranked);
        RankingPodiumHost.Visibility = drawn.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (drawn.Count == 0) return;

        _podiumFluid = CurrentPodiumFluid();
        _podiumEffectsDrawn = RankBadge.AnimationsEnabled;
        RankingPodiumHost.Margin = new Thickness(0, 0, 0, PageGap);

        // The soft red light behind the podium: decoration, gone with reduced effects.
        if (RankBadge.AnimationsEnabled)
        {
            var glow = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.46), GradientOrigin = new Point(0.5, 0.46), RadiusX = 0.46, RadiusY = 0.72,
            };
            glow.GradientStops.Add(new GradientStop(Rgba("#FF2F45", .17), 0));
            glow.GradientStops.Add(new GradientStop(Rgba("#FF2F45", 0), 0.72));
            var light = new Rectangle { Fill = Frozen(glow), Margin = new Thickness(0, -48, 0, -24), IsHitTestVisible = false };
            Grid.SetColumnSpan(light, 5);
            RankingPodiumHost.Children.Add(light);
        }

        var climbs = RankingClimbs(team);
        var month = RankingClimbMonth();
        var meId = RankingViewerId;
        for (var i = 0; i < drawn.Count; i++)
        {
            if (i > 0) RankingPodiumHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(PageGap) });
            RankingPodiumHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var row = drawn[i];
            climbs.TryGetValue(row.UserId, out var climb);
            var card = BuildPodiumCard(row, team, ladderSize, IsViewer(row.UserId, meId), climb, month);
            Grid.SetColumn(card, i * 2);
            RankingPodiumHost.Children.Add(card);
        }
        SyncPodiumSheen();
    }

    /// <summary>One place of the podium. <c>internal</c> so the tests build the real card.</summary>
    internal FrameworkElement BuildPodiumCard(
        LeaderboardRow row, bool team, int ladderSize, bool isMe, HighlightPlayer? climb, string? month)
    {
        var fluid = _podiumFluid;
        var first = row.Rank <= 1;
        var age = RankAges.For(row.Rank, ladderSize);
        var (main, light) = RankAgeColors.Of(age);
        var name = string.IsNullOrEmpty(row.DisplayName) ? row.DiscordUsername : row.DisplayName;

        var otherRank = team ? row.LadderRank : row.LadderRankTeam;
        var shown = new ShownBadge(team ? BadgeKind.Team : BadgeKind.Solo, age, row.Rank,
            RankAges.ForOptional(otherRank, LadderSize(!team)), otherRank ?? 0);
        var tip = RankBadgeTips.Text(shown, CommunityStatsView.RequiredDecided(_communityStats));
        var badge = RankBadge.BuildFor(shown, RankingBadgeSize, row.UserId, tip,
            onClick: () => ShowRankGuide(initial: shown.Kind), animated: RankBadge.AnimatesAt(row.Rank));

        // --- the top: avatar, name, ELO ---
        var top = new Grid { Margin = new Thickness(fluid.Padding) };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.Children.Add(BuildRingedAvatar(name, row.AvatarUrl, fluid.Avatar, light, badge));

        var words = new StackPanel { Margin = new Thickness(fluid.Gap + 4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var nameLine = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        nameLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        nameLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        nameLine.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = fluid.NameSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = Solid("#F0F5FB"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (isMe)
            nameLine.Children.Add(WithColumn(new TextBlock
            {
                Text = Strings.Get("MpRankYouTag"),
                Margin = new Thickness(8, 0, 0, 0),
                FontSize = (double)Application.Current.FindResource("MpMicroSize"),
                FontWeight = FontWeights.Bold,
                Foreground = Solid("#9FD0F5"),
                VerticalAlignment = VerticalAlignment.Center,
            }, 1));
        words.Children.Add(nameLine);

        var eloLine = new TextBlock { Margin = new Thickness(0, 7, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        eloLine.Inlines.Add(new Run(PlacementView.RatingText(row.Rating))
        {
            FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"),
            FontSize = fluid.EloSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
        });
        eloLine.Inlines.Add(new Run("  " + Strings.Get("RkEloLabel"))
        {
            FontSize = (double)Application.Current.FindResource("MpMicroSize"),
            FontWeight = FontWeights.Bold,
            Foreground = Solid("#8C9CB1"),
        });
        AddClimbRuns(eloLine, climb, month);
        words.Children.Add(eloLine);
        top.Children.Add(WithColumn(words, 1));

        // --- the pedestal: place, age, streak, peak ---
        var pedestal = new Grid { Margin = new Thickness(fluid.Padding, 0, fluid.Padding, 0) };
        pedestal.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pedestal.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pedestal.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pedestal.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var numberBrush = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        numberBrush.GradientStops.Add(new GradientStop(Colors.White, 0.2));
        numberBrush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(light), 1));
        pedestal.Children.Add(new TextBlock
        {
            Text = row.Rank.ToString(),
            FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont"),
            FontSize = fluid.NumberOf(row.Rank),
            FontWeight = FontWeights.Bold,
            Foreground = Frozen(numberBrush),
            VerticalAlignment = VerticalAlignment.Center,
        });
        pedestal.Children.Add(WithColumn(new TextBlock
        {
            Text = Strings.Get(RankAges.NameKey(age)).ToUpper(Strings.Culture),
            Margin = new Thickness(10, 0, 10, 0),
            FontSize = (double)Application.Current.FindResource("MpMicroSize"),
            FontWeight = FontWeights.Bold,
            Foreground = Solid(light),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = TooltipHelper.Wrap(tip),
        }, 1));
        if (StreakView.ShowsPill(row.Streak))
        {
            var pill = BuildStreakPill(row.Streak, table: true);
            pill.Margin = new Thickness(0, 0, 10, 0);
            pedestal.Children.Add(WithColumn(pill, 2));
        }
        if (row.RatingPeak is double peak)
        {
            var max = new TextBlock
            {
                Text = Strings.Format("RkPeakShort", PlacementView.RatingText(peak)),
                FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"),
                FontSize = (double)Application.Current.FindResource("MpLabelSize"),
                Foreground = Solid("#C3D2E5"),
                VerticalAlignment = VerticalAlignment.Center,
                Tag = RankingPeakTag,
            };
            if (ShortDate.Parse(row.RatingPeakAt) is { } when)
                max.ToolTip = TooltipHelper.Wrap(Strings.Format("MpRankPeakTip", ShortDate.Format(when)));
            pedestal.Children.Add(WithColumn(max, 3));
        }
        var pedestalBand = new Border
        {
            Height = fluid.PedestalOf(row.Rank),
            CornerRadius = new CornerRadius(0, 0, 13, 13),
            Background = Across((Rgba(main, first ? .55 : .4), 0), (Rgba(main, .14), 0.7), (Rgba(light, .08), 1)),
            BorderBrush = new SolidColorBrush(Rgba(light, first ? .5 : .35)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = pedestal,
        };

        var stack = new Grid();
        stack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        stack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        stack.Children.Add(top);
        Grid.SetRow(pedestalBand, 1);
        stack.Children.Add(pedestalBand);

        var layers = new Grid();
        layers.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(13),
            Background = Diagonal((Rgba(main, first ? .45 : .32), 0), (Rgba(main, .1), 0.48), (Rgba(main, 0), 0.8)),
            IsHitTestVisible = false,
        });
        if (first && RankBadge.AnimationsEnabled) layers.Children.Add(BuildPodiumSheen());
        layers.Children.Add(stack);

        var card = new Border
        {
            Tag = PodiumCardTag,
            DataContext = row,
            CornerRadius = new CornerRadius(14),
            BorderBrush = isMe ? Solid("#9955ACEE") : new SolidColorBrush(Rgba(main, first ? .6 : .38)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Bottom,
            Child = layers,
        };
        card.SetResourceReference(Border.BackgroundProperty, "GlassPanel");
        if (isMe)
        {
            card.Cursor = System.Windows.Input.Cursors.Hand;
            card.MouseLeftButtonUp += (_, _) => OpenOwnProfileFromRanking();
        }
        return card;
    }

    /// <summary>
    /// The light that crosses first place: a narrow band of a gradient brush, moved by its
    /// transform. One clock, capped at the badges' frame rate; <see cref="SyncPodiumSheen"/> pauses
    /// it whenever nobody can see it.
    /// </summary>
    private FrameworkElement BuildPodiumSheen()
    {
        var move = new TranslateTransform(-1.3, 0);
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(0.42, 0.08),
            RelativeTransform = move,
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.1));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(18, 255, 255, 255), 0.4));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(41, 255, 255, 255), 0.5));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(18, 255, 255, 255), 0.6));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.9));

        // 7.5 s: it crosses in the first third and rests for the rest (the design's keyframes).
        var frames = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(7.5),
            BeginTime = TimeSpan.FromSeconds(1.2),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(-1.3, KeyTime.FromPercent(0)));
        frames.KeyFrames.Add(new EasingDoubleKeyFrame(1.3, KeyTime.FromPercent(0.34),
            new SineEase { EasingMode = EasingMode.EaseInOut }));
        frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(1.3, KeyTime.FromPercent(1)));
        Timeline.SetDesiredFrameRate(frames, RankBadge.FrameRate);
        _podiumSheenClock = frames.CreateClock();
        move.ApplyAnimationClock(TranslateTransform.XProperty, _podiumSheenClock);

        return new Rectangle { Fill = brush, RadiusX = 13, RadiusY = 13, IsHitTestVisible = false, Tag = "RankingPodiumSheen" };
    }

    /// <summary>Stops the light for good (its card is being rebuilt or the effects went off).</summary>
    private void StopPodiumSheen()
    {
        _podiumSheenClock?.Controller?.Stop();
        _podiumSheenClock = null;
    }

    private bool _podiumSheenHooked;

    /// <summary>Whether the podium was last drawn with its effects (the glow and the light).</summary>
    private bool _podiumEffectsDrawn;

    /// <summary>
    /// Runs the light only while somebody can see it, the badges' own rule: the launcher in the
    /// foreground, the podium on screen (not another subtab, another mode or the tray) and the
    /// window not minimised, which keeps IsVisible true. With the effects reduced there is no
    /// light at all, so the podium is drawn again without it - and again when they come back.
    /// </summary>
    private void SyncPodiumSheen()
    {
        if (!_podiumSheenHooked && RankingPodiumHost != null && Window.GetWindow(this) != null)
        {
            _podiumSheenHooked = true;
            RankingPodiumHost.IsVisibleChanged += (_, _) => SyncPodiumSheen();
            // Foreground, minimise and restore, and the effects setting: one event for the three.
            RankBadge.RunConditionsChanged += SyncPodiumSheen;
        }
        if (_podiumEffectsDrawn != RankBadge.AnimationsEnabled
            && RankingPodiumHost?.IsVisible == true && RankingPodiumHost.Children.Count > 0)
        {
            RenderRanking();
            return;
        }
        var controller = _podiumSheenClock?.Controller;
        if (controller == null) return;
        if (RankBadge.MayRunOn(RankingPodiumHost)) controller.Resume();
        else controller.Pause();
    }

    // ---------------------------------------------------------------------------------------
    // "Your position"
    // ---------------------------------------------------------------------------------------

    /// <summary>Draws "Your position" above the latest matches; hides it for a viewer who is neither
    /// on the table nor being placed.</summary>
    private void RenderRankingYou(IReadOnlyList<LeaderboardRow> ranked, IReadOnlyList<PlacementRow> placing, bool team, int ladderSize)
    {
        RankingYouHost.Children.Clear();
        var own = OwnLadder(team);
        var you = YourStandingView.For(ranked, placing, RankingViewerId, ladderSize, own);
        _rankingYouShown = you != null;
        RankingYouHost.Visibility = you != null ? Visibility.Visible : Visibility.Collapsed;
        if (you == null) return;
        RankingYouHost.Margin = new Thickness(0, 0, 0, PageGap);
        RankingClimbs(team).TryGetValue(RankingViewerId ?? "", out var climb);
        RankingYouHost.Children.Add(BuildYouCard(you, team, climb, RankingClimbMonth(),
            you.Ranked ? null : OwnPlacementResults(team)));
    }

    private bool _rankingYouShown;

    /// <summary>The viewer's own profile, from his row, his podium card or "Your position". While
    /// the preview owns the page it is the sample profile: the real one would ask for a session.</summary>
    private void OpenOwnProfileFromRanking()
    {
        if (_eloPreview) ShowPreviewProfile(refund: false);
        else OpenProfileWindow();
    }

    /// <summary>The viewer's name and picture, for his own card: the session's, or the preview's.</summary>
    private (string Name, string? Avatar) RankingViewerFace(IReadOnlyList<StandingNeighbour> neighbours)
    {
        var self = neighbours.FirstOrDefault(n => n.Self);
        if (self.Self) return (self.Name, self.AvatarUrl);
        var user = _eloPreview ? null : _session?.CurrentUser;
        var name = user == null ? "" : string.IsNullOrEmpty(user.DisplayName) ? user.DiscordUsername : user.DisplayName;
        return (name ?? "", user?.AvatarUrl);
    }

    /// <summary>"Your position". <c>internal</c> so the tests build the real card.</summary>
    internal FrameworkElement BuildYouCard(
        YourStanding you, bool team, HighlightPlayer? climb, string? month, IReadOnlyList<PlacementResultEntry>? results)
    {
        var (main, light) = RankAgeColors.Of(you.Age);
        var (name, avatar) = RankingViewerFace(you.Neighbours);
        if (name.Length == 0)
        {
            // Being placed: the face comes from his own placement row.
            var row = CommunityStatsView.PlacementRows(_communityStats, team)
                .FirstOrDefault(p => IsViewer(p.UserId, RankingViewerId));
            if (row != null) (name, avatar) = (string.IsNullOrEmpty(row.DisplayName) ? row.DiscordUsername : row.DisplayName, row.AvatarUrl);
        }

        var body = new StackPanel();

        // --- heading ---
        var head = new Grid { Margin = new Thickness(16, 12, 16, 0) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(new TextBlock
        {
            Text = Strings.Get("RkYouTitle"),
            FontSize = (double)Application.Current.FindResource("FontSizeBodyStrong"),
            FontWeight = FontWeights.SemiBold,
            Foreground = Solid("#F0F5FB"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        head.Children.Add(WithColumn(new TextBlock
        {
            Text = Strings.Get(team ? "MpRankingModeTeam" : "MpModeOneVsOne"),
            Margin = new Thickness(10, 0, 10, 0),
            FontSize = (double)Application.Current.FindResource("MpMetaSize"),
            Foreground = Solid("#8C9CB1"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        }, 1));
        // A real button, not only the card's click: it takes the keyboard and has a name to press.
        var profile = new Button
        {
            Content = Strings.Get("RkYouProfile"),
            Style = (Style)Application.Current.FindResource("MpLinkButton"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        profile.SetResourceReference(Control.FontSizeProperty, "MpMetaSize");
        System.Windows.Automation.AutomationProperties.SetAutomationId(profile, "RankingYouProfile");
        profile.Click += (_, _) => OpenOwnProfileFromRanking();
        head.Children.Add(WithColumn(profile, 2));
        body.Children.Add(head);

        // --- who and where ---
        var shown = new ShownBadge(team ? BadgeKind.Team : BadgeKind.Solo, you.Age, you.Ranked ? you.Place : 0, null, 0);
        var badge = RankBadge.BuildFor(shown, RankingBadgeSize, RankingViewerId ?? "me",
            RankBadgeTips.Text(shown, CommunityStatsView.RequiredDecided(_communityStats)), animated: true);
        var who = new Grid { Margin = new Thickness(16, 10, 16, 10) };
        who.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        who.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        who.Children.Add(BuildRingedAvatar(name, avatar, 40, light, badge));

        var lines = new StackPanel { Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var place = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
        place.Inlines.Add(new Run(you.Ranked ? "#" + you.Place : you.PlacementPlayed + "/" + you.PlacementRequired)
        {
            FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont"),
            FontSize = 22 * TextScale.CurrentFactor,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
        });
        if (you.Ranked)
            place.Inlines.Add(new Run("  " + Strings.Format("RkYouOf", you.Of))
            {
                FontSize = (double)Application.Current.FindResource("MpMetaSize"),
                Foreground = Solid("#A8BCD2"),
            });
        place.Inlines.Add(new Run("   " + (you.Ranked
            ? Strings.Get(RankAges.NameKey(you.Age)).ToUpper(Strings.Culture)
            : Strings.Get("RkGroupPlacing")))
        {
            FontSize = (double)Application.Current.FindResource("MpMicroSize"),
            FontWeight = FontWeights.Bold,
            Foreground = Solid(light),
        });
        lines.Children.Add(place);

        var eloRow = new Grid { Margin = new Thickness(0, 5, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        eloRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        eloRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var elo = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        if (you.Elo is int figure)
        {
            elo.Inlines.Add(new Run(figure.ToString())
            {
                FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"),
                FontSize = (double)Application.Current.FindResource("FontSizeBodyStrong"),
                FontWeight = FontWeights.SemiBold,
                Foreground = Solid("#F0F5FB"),
            });
            if (!you.Ranked)
                elo.Inlines.Add(new Run("?")
                {
                    FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"),
                    FontSize = (double)Application.Current.FindResource("FontSizeBodyStrong"),
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Solid("#F5BD76"),
                });
            elo.Inlines.Add(new Run("  " + Strings.Get("RkEloLabel"))
            {
                FontSize = (double)Application.Current.FindResource("MpMicroSize"),
                FontWeight = FontWeights.Bold,
                Foreground = Solid("#8C9CB1"),
            });
            if (you.Ranked) AddClimbRuns(elo, climb, month);
            else elo.ToolTip = TooltipHelper.Wrap(Strings.Get("RkYouProvisionalTip"));
        }
        eloRow.Children.Add(elo);
        if (StreakView.ShowsPill(you.Streak))
        {
            var pill = BuildStreakPill(you.Streak, table: true);
            pill.Margin = new Thickness(10, 0, 0, 0);
            eloRow.Children.Add(WithColumn(pill, 1));
        }
        lines.Children.Add(eloRow);
        who.Children.Add(WithColumn(lines, 1));
        body.Children.Add(who);

        if (you.Ranked)
        {
            var list = new StackPanel { Margin = new Thickness(8, 0, 8, 10) };
            foreach (var n in you.Neighbours) list.Children.Add(BuildNeighbourRow(n));
            var next = you.First ? Strings.Get("RkYouFirst")
                : you.ToNextPlace <= 0 ? null
                : you.NextAge is { } up
                    ? Strings.Format("RkYouNextAge", you.ToNextPlace, you.NextPlace, Strings.Get(RankAges.NameKey(up)))
                    : Strings.Format("RkYouNext", you.ToNextPlace, you.NextPlace);
            if (next != null)
                list.Children.Add(new TextBlock
                {
                    Text = next,
                    Margin = new Thickness(8, list.Children.Count > 0 ? 6 : 0, 8, 0),
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = (double)Application.Current.FindResource("MpLabelSize"),
                    Foreground = Solid("#C3D2E5"),
                    Tag = "RankingYouNext",
                });
            if (list.Children.Count > 0) body.Children.Add(list);
        }
        else
        {
            var placing = new StackPanel { Margin = new Thickness(16, 0, 16, 12) };
            placing.Children.Add(BuildPlacementSegments(
                PlacementView.Segments(you.PlacementPlayed, you.PlacementRequired, results),
                height: 5, gap: 4, profile: false));
            placing.Children.Add(new TextBlock
            {
                Text = you.PlacementLeft == 1 ? Strings.Get("RkYouLeftOne") : Strings.Format("RkYouLeft", you.PlacementLeft),
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                FontSize = (double)Application.Current.FindResource("MpLabelSize"),
                Foreground = Solid("#C3D2E5"),
                Tag = "RankingYouNext",
            });
            body.Children.Add(placing);
        }

        var layers = new Grid();
        layers.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(13),
            Background = Diagonal((Rgba(main, .3), 0), (Rgba(main, .07), 0.5), (Rgba(main, 0), 0.85)),
            IsHitTestVisible = false,
        });
        layers.Children.Add(body);

        var card = new Border
        {
            Tag = YouCardTag,
            DataContext = you,
            CornerRadius = new CornerRadius(14),
            BorderBrush = Solid("#6155ACEE"),
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
            Child = layers,
        };
        card.SetResourceReference(Border.BackgroundProperty, "GlassPanel");
        card.MouseLeftButtonUp += (_, _) => OpenOwnProfileFromRanking();
        return card;
    }

    /// <summary>One line of the three around the viewer: place, picture, name, ELO, and the gap.</summary>
    private static FrameworkElement BuildNeighbourRow(StandingNeighbour n)
    {
        var grid = new Grid { Height = 30, Margin = new Thickness(8, 0, 8, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        var mono = (FontFamily)Application.Current.FindResource("MonoFont");
        grid.Children.Add(new TextBlock
        {
            Text = n.Place.ToString(),
            FontFamily = mono,
            FontSize = (double)Application.Current.FindResource("MpLabelSize"),
            FontWeight = FontWeights.SemiBold,
            Foreground = Solid("#8C9CB1"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var avatar = BuildAvatarDisc(n.Name, n.AvatarUrl, 24);
        avatar.HorizontalAlignment = HorizontalAlignment.Right;
        grid.Children.Add(WithColumn(avatar, 1));
        grid.Children.Add(WithColumn(new TextBlock
        {
            Text = n.Name,
            Margin = new Thickness(10, 0, 10, 0),
            FontSize = (double)Application.Current.FindResource("MpBodySize"),
            FontWeight = n.Self ? FontWeights.SemiBold : FontWeights.Medium,
            Foreground = Solid(n.Self ? "#FFFFFF" : "#DCE7F5"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        }, 2));
        grid.Children.Add(WithColumn(new TextBlock
        {
            Text = n.Elo.ToString(),
            FontFamily = mono,
            FontSize = (double)Application.Current.FindResource("MpBodySize"),
            FontWeight = FontWeights.SemiBold,
            Foreground = Solid("#DCE7F5"),
            VerticalAlignment = VerticalAlignment.Center,
        }, 3));
        grid.Children.Add(WithColumn(new TextBlock
        {
            // A real minus sign: the hyphen is narrower than the plus it sits under.
            Text = n.Self ? "" : (n.Gap >= 0 ? "+" : "−") + Math.Abs(n.Gap),
            FontFamily = mono,
            FontSize = (double)Application.Current.FindResource("MpLabelSize"),
            Foreground = Solid("#A8BCD2"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        }, 4));
        return new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = n.Self ? Solid("#2455ACEE") : null,
            Child = grid,
            Tag = n,
        };
    }
}
