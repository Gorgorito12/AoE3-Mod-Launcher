using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// The Profile's rating half (design handoff 55d-55f): the header with its status line, ONE CARD
/// PER MODE — each mode carries its own rating and its own placement — and "against each
/// opponent". It replaced the RECORD card and the PROVISIONAL tag: placement is a count a player
/// can see the end of, where "provisional" was a judgement about a Glicko deviation.
///
/// <para>Everything drawn here is the SERVER's: the place, the placement count, the streaks and
/// when one ran out, peak and low, the head-to-head. A field the server did not send leaves its
/// part of a card out.</para>
/// </summary>
public partial class MultiplayerTab
{
    /// <summary>Below this width the two mode cards stack (design 55e).</summary>
    internal const double ModeCardsStackBelow = 600;

    /// <summary>The "against each opponent" card shows the team ladder; null = not chosen yet.</summary>
    private bool? _h2hShowsTeam;

    /// <summary>The "against each opponent" card lists everybody the server sent.</summary>
    private bool _h2hExpanded;

    /// <summary>The <c>Tag</c> of each mode card, so the tests find them.</summary>
    internal const string ModeCardTag = "ProfileModeCard";

    /// <summary>The <c>Tag</c> of the "against each opponent" card.</summary>
    internal const string HeadToHeadCardTag = "ProfileHeadToHead";

    /// <summary>The ladders the page reads, or — from a backend older than them — the 1v1 ladder
    /// put together from the top-level fields, with nothing the old payload did not carry.</summary>
    private static (LadderStandings Ladders, bool Legacy) ProfileLadders(EloSnapshot standing)
    {
        if (standing.Ladders != null) return (standing.Ladders, false);
        return (new LadderStandings
        {
            Default = new LadderStanding
            {
                Rating = standing.Rating,
                Rd = standing.Rd,
                GamesPlayed = standing.GamesPlayed,
                LadderRank = standing.LadderRank,
                LadderSize = standing.LadderSize,
                Wins = standing.Wins,
                Losses = standing.Losses,
            },
        }, true);
    }

    /// <summary>How many rated matches a ladder's placement takes, from the ladder itself, then the
    /// payload's own table, then the rule (10 in 1v1, 5 in teams).</summary>
    private int PlacementRequiredFor(LadderStanding? ladder, bool team)
    {
        if (ladder is { PlacementRequired: > 0 }) return ladder.PlacementRequired;
        var table = _cachedStanding?.PlacementRequired;
        var fromTable = team ? table?.Team : table?.Default;
        return fromTable is > 0 ? fromTable.Value : team ? 5 : 10;
    }

    private static string ModeName(bool team)
        => Strings.Get(team ? "MpRankingModeTeam" : "MpModeOneVsOne");

    // ------------------------------------------------------------------ the header

    /// <summary>
    /// The header (55d): the 56-px portrait, the name in 20 px of serif, and one line saying where
    /// the player stands in each mode — "Industrial in 1v1 · in placement in Teams". The rating
    /// itself lives in the mode cards now, one per ladder, so the header carries none.
    ///
    /// <para>With no rated match anywhere (55f) the line becomes the sentence that explains it and
    /// a "—" stands where a rating would be: never a 1500 nobody earned.</para>
    /// </summary>
    /// <remarks><c>internal</c> so the tests build the real header.</remarks>
    internal UIElement BuildProfileHeader(LobbyUserSummary user)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var avatar = BuildAvatarDisc(user.DisplayName, user.AvatarUrl, 56, cornerRadius: 14);
        avatar.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(avatar, 0);
        grid.Children.Add(avatar);

        var who = new StackPanel { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        who.Children.Add(new TextBlock
        {
            Text = user.DisplayName,
            FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont"),
            FontSize = (double)Application.Current.FindResource("MpProfileNameSize"),
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.FindResource("UiTextHeadline"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        string? status = null;
        var noGames = false;
        if (_cachedStanding is { } standing)
        {
            var (ladders, _) = ProfileLadders(standing);
            noGames = ProfileModeView.NoGamesAnywhere(ladders);
            status = noGames
                ? Strings.Get("MpProfileNoMatchesYou")
                : ProfileModeView.StatusLine(new[]
                {
                    ProfileModeView.StatusSegment(ladders.Default, ModeName(false),
                        CommunityStatsView.RankedPlayers(_communityStats, team: false)),
                    ProfileModeView.StatusSegment(ladders.Team, ModeName(true),
                        CommunityStatsView.RankedPlayers(_communityStats, team: true)),
                });
        }
        if (status != null)
        {
            who.Children.Add(new TextBlock
            {
                Text = status,
                Margin = new Thickness(0, noGames ? 3 : 5, 0, 0),
                Foreground = (Brush)Application.Current.FindResource("MpRankMutedText"),
                FontSize = (double)Application.Current.FindResource("MpMetaSize"),
                TextWrapping = TextWrapping.Wrap,
            });
        }
        Grid.SetColumn(who, 1);
        grid.Children.Add(who);

        if (noGames)
        {
            var dash = new TextBlock
            {
                Text = Strings.Get("MpDash"),
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont"),
                FontSize = (double)Application.Current.FindResource("MpProfileDashSize"),
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)Application.Current.FindResource("MpTextFade"),
            };
            Grid.SetColumn(dash, 2);
            grid.Children.Add(dash);
        }

        return new Border
        {
            Child = grid,
            Tag = "ProfileHeader",
            Padding = new Thickness(18, 16, 18, 16),
            CornerRadius = (CornerRadius)Application.Current.FindResource("RadiusLg"),
            Background = (Brush)Application.Current.FindResource("MpProfileHeaderBg"),
            // One rung up from the handoff's .15: WPF composites a Border's rim over what is
            // behind the card, CSS over the card's own fill (see the bracket card's rim).
            BorderBrush = (Brush)Application.Current.FindResource("MpRimStrong"),
            BorderThickness = new Thickness(1),
        };
    }

    // ------------------------------------------------------------------ the mode cards

    /// <summary>
    /// One card per mode, side by side, stacked below <see cref="ModeCardsStackBelow"/> (55d/55e).
    /// Null when there is nothing to draw: the standing has not arrived, or the player has no
    /// rated match in either mode — the header says so (55f).
    /// </summary>
    internal UIElement? BuildProfileModeCards()
    {
        if (_cachedStanding is not { } standing) return null;
        var (ladders, legacy) = ProfileLadders(standing);
        if (ProfileModeView.NoGamesAnywhere(ladders)) return null;

        // A refund not yet dismissed puts its banner over its mode's card (55n).
        var solo = WithRefundBanner(BuildModeCard(ladders.Default, team: false, legacy), team: false);
        // An older backend has no team ladder to show; a card would only say "no matches".
        var team = legacy ? null : WithRefundBanner(BuildModeCard(ladders.Team, team: true, legacy: false), team: true);

        var grid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(team == null ? 0 : 10) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(team == null ? 0 : 1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumn(solo, 0);
        grid.Children.Add(solo);
        if (team != null)
        {
            Grid.SetColumn(team, 2);
            grid.Children.Add(team);
            grid.SizeChanged += (_, e) => LayOutModeCards(grid, team, e.NewSize.Width < ModeCardsStackBelow);
        }
        return grid;
    }

    /// <summary>Side by side, or the second card under the first (55e). Only writes what changes.</summary>
    private static void LayOutModeCards(Grid grid, FrameworkElement second, bool stacked)
    {
        var wantColumn = stacked ? 0 : 2;
        if (Grid.GetColumn(second) == wantColumn) return;
        grid.ColumnDefinitions[1].Width = new GridLength(stacked ? 0 : 10);
        grid.ColumnDefinitions[2].Width = new GridLength(stacked ? 0 : 1, GridUnitType.Star);
        Grid.SetColumn(second, wantColumn);
        Grid.SetRow(second, stacked ? 1 : 0);
        second.Margin = new Thickness(0, stacked ? 10 : 0, 0, 0);
    }

    /// <summary>One mode's card, in whichever of its four shapes the ladder calls for.</summary>
    private Border BuildModeCard(LadderStanding? ladder, bool team, bool legacy)
    {
        var shape = ProfileModeView.ShapeOf(ladder);
        var body = new StackPanel();

        // The card's head: the badge when the player is on the table, the mode, and on the right
        // the place — or "no place yet" while placing. A mode with no matches has the label alone.
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var ladderSize = ladder?.LadderSize is > 0 ? ladder.LadderSize
            : CommunityStatsView.RankedPlayers(_communityStats, team) is > 0 and var n ? n : (int?)null;
        if (shape is ProfileModeShape.Ranked or ProfileModeShape.Inactive
            && ProfileModeView.AgeOf(ladder, ladderSize) is { } age)
        {
            var shown = new ShownBadge(team ? BadgeKind.Team : BadgeKind.Solo, age, ladder!.LadderRank ?? 0, null, 0);
            // No click: the rank guide opens over the multiplayer tab, which is behind this window.
            var badge = RankBadge.BuildFor(shown, 18, "profile-mode-" + (team ? "team" : "solo"),
                RankBadgeTips.Text(shown, CommunityStatsView.RequiredDecided(_communityStats)));
            badge.VerticalAlignment = VerticalAlignment.Center;
            badge.Margin = new Thickness(0, 0, 8, 0);
            Grid.SetColumn(badge, 0);
            head.Children.Add(badge);
        }

        var label = new TextBlock
        {
            Text = ModeName(team).ToUpper(Strings.Culture),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = (double)Application.Current.FindResource("MpPillSize"),
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.FindResource("MpTextMuted"),
        };
        Grid.SetColumn(label, 1);
        head.Children.Add(label);

        string? where = shape switch
        {
            ProfileModeShape.Placement => Strings.Get("MpProfileNoRankYet"),
            ProfileModeShape.Ranked or ProfileModeShape.Inactive
                when ladder!.LadderRank is > 0 and var rank && ladderSize is > 0 and var size
                => Strings.Format("MpProfileRankOf", rank, size),
            _ => null,
        };
        if (where != null)
        {
            var right = new TextBlock
            {
                Text = where,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = (double)Application.Current.FindResource("MpLabelSize"),
                Foreground = (Brush)Application.Current.FindResource("MpRankMutedText"),
            };
            Grid.SetColumn(right, 3);
            head.Children.Add(right);
        }
        body.Children.Add(head);

        switch (shape)
        {
            case ProfileModeShape.NoGames:
                body.Children.Add(new TextBlock
                {
                    Text = Strings.Format("MpProfileModeEmptyYou",
                        ModeName(team).ToLower(Strings.Culture), PlacementRequiredFor(ladder, team)),
                    Margin = new Thickness(0, 6, 0, 0),
                    FontSize = (double)Application.Current.FindResource("MpMetaSize"),
                    LineHeight = 18,
                    Foreground = (Brush)Application.Current.FindResource("MpRankMutedText"),
                    TextWrapping = TextWrapping.Wrap,
                });
                break;
            case ProfileModeShape.Placement:
                FillPlacementCard(body, ladder!, team);
                break;
            default:
                FillRankedCard(body, ladder!, inactive: shape == ProfileModeShape.Inactive, legacy);
                break;
        }

        return new Border
        {
            Child = body,
            Tag = ModeCardTag,
            Padding = new Thickness(15, 14, 15, 14),
            CornerRadius = (CornerRadius)Application.Current.FindResource("RadiusLg"),
            Background = (Brush)Application.Current.FindResource("MpPanel"),
            BorderBrush = (Brush)Application.Current.FindResource("MpRimMedium"),
            BorderThickness = new Thickness(1),
        };
    }

    /// <summary>
    /// Ranked (55d) or inactive (55f): the rating, peak and low with their dates, and the three
    /// streak cells — or, while inactive, the rating, the peak and the amber notice.
    /// </summary>
    private void FillRankedCard(StackPanel body, LadderStanding ladder, bool inactive, bool legacy)
    {
        var rating = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        rating.Children.Add(new TextBlock
        {
            Text = PlacementView.RatingText(ladder.Rating),
            FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont"),
            FontSize = (double)Application.Current.FindResource("MpProfileRatingSize"),
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.FindResource(inactive ? "MpTextSecondary" : "UiTextHeadline"),
        });

        // Peak and low, each with its date. Null until placement is over, and an older backend
        // never sends them: then the line is simply not there.
        var extremes = new WrapPanel { Margin = new Thickness(0, 5, 0, 0) };
        if (ladder.RatingPeak is double peak)
            extremes.Children.Add(Extreme("MpProfilePeak", peak, ladder.RatingPeakAt));
        if (!inactive && ladder.RatingLow is double low)
            extremes.Children.Add(Extreme("MpProfileLow", low, ladder.RatingLowAt));
        if (extremes.Children.Count > 0) rating.Children.Add(extremes);
        body.Children.Add(rating);

        if (inactive)
        {
            body.Children.Add(BuildInactiveNotice(ladder));
            return;
        }
        // The old payload carried no streaks; three cells of zeros would be a claim it never made.
        if (legacy) return;

        var cells = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        for (var i = 0; i < 5; i++)
            cells.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = i % 2 == 1 ? new GridLength(8) : new GridLength(1, GridUnitType.Star),
            });

        var current = ProfileModeView.CurrentStreak(ladder, DateTimeOffset.Now, Strings.Language);
        cells.Children.Add(StreakCellOf(0, "MpProfileStreak", current.Text, current.BrushKey, current.Note,
            infoTip: ladder.StreakCurrent > 0
                ? Strings.Format("MpProfileStreakTipTitle", ladder.StreakCurrent) + "\n" + Strings.Get("MpProfileStreakTipBody")
                : null));
        cells.Children.Add(StreakCellOf(2, "MpProfileLongestWin", ladder.StreakBest.ToString(), "MpTextPrimary", null, null));
        cells.Children.Add(StreakCellOf(4, "MpProfileLongestLoss", ladder.LossStreakBest.ToString(), "MpTextPrimary", null, null));

        body.Children.Add(new Border
        {
            Child = cells,
            Margin = new Thickness(0, 10, 0, 0),
            BorderBrush = (Brush)Application.Current.FindResource("MpRimHair"),
            BorderThickness = new Thickness(0, 1, 0, 0),
        });

        static FrameworkElement Extreme(string key, double value, string? at)
        {
            var date = ShortDate.Parse(at) is { } when ? ShortDate.Format(when) : null;
            var tb = new TextBlock
            {
                Margin = new Thickness(0, 0, 14, 4),
                FontSize = (double)Application.Current.FindResource("MpLabelSize"),
                Foreground = (Brush)Application.Current.FindResource("MpRankMutedText"),
            };
            // "Peak: {0} · {1}" — the figure monospaced and brighter, the date as written. Without a
            // date the " · {1}" goes too, rather than ending the line on a separator.
            var template = Strings.Get(key);
            if (date == null) template = Regex.Replace(template, @"\s*·\s*\{1\}", "");
            AddTemplateRuns(tb, template, i => i == 0
                ? new Run(PlacementView.RatingText(value))
                {
                    FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"),
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)Application.Current.FindResource("UiTextStrong"),
                }
                : new Run(date ?? ""));
            return tb;
        }
    }

    /// <summary>One of the three streak cells: the label (wrapping — the longest label is "longest
    /// winning streak"), the figure, and under the current streak the line saying when it ran out.</summary>
    private static FrameworkElement StreakCellOf(int column, string labelKey, string value, string brushKey,
        string? note, string? infoTip)
    {
        var cell = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        // With the "i" the label sits in an Auto column so the circle follows the word, as drawn;
        // only the current streak has one, and its label is short. The two long labels ("longest
        // winning streak") keep the star column and wrap.
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = infoTip != null ? GridLength.Auto : new GridLength(1, GridUnitType.Star),
        });
        if (infoTip != null)
        {
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        top.Children.Add(new TextBlock
        {
            Text = Strings.Get(labelKey),
            FontSize = (double)Application.Current.FindResource("MpSectionLabelSize"),
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.FindResource("MpTextLabel"),
            TextWrapping = infoTip != null ? TextWrapping.NoWrap : TextWrapping.Wrap,
            TextTrimming = infoTip != null ? TextTrimming.CharacterEllipsis : TextTrimming.None,
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (infoTip != null)
        {
            // The "i" whose tooltip says what ends a streak (55d). Only beside a streak there IS —
            // with none, there is nothing for that sentence to be about.
            var info = new Border
            {
                Width = 13,
                Height = 13,
                Margin = new Thickness(5, 0, 0, 0),
                CornerRadius = new CornerRadius(6.5),
                BorderBrush = (Brush)Application.Current.FindResource("MpTextLabel"),
                BorderThickness = new Thickness(1),
                Background = Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = "StreakInfo",
                Child = new TextBlock
                {
                    Text = "i",
                    FontSize = 8.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = (Brush)Application.Current.FindResource("MpTextMuted"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                ToolTip = TooltipHelper.Wrap(infoTip),
            };
            ToolTipService.SetInitialShowDelay(info, 0);
            Grid.SetColumn(info, 1);
            top.Children.Add(info);
        }
        cell.Children.Add(top);
        cell.Children.Add(new TextBlock
        {
            Text = value,
            Margin = new Thickness(0, 5, 0, 0),
            FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"),
            FontSize = (double)Application.Current.FindResource("MpProfileStreakSize"),
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.FindResource(brushKey),
        });
        if (note != null)
        {
            cell.Children.Add(new TextBlock
            {
                Text = note,
                Margin = new Thickness(0, 2, 0, 0),
                FontSize = (double)Application.Current.FindResource("MpRankSmallSize"),
                Foreground = (Brush)Application.Current.FindResource("MpTextMuted"),
                TextWrapping = TextWrapping.Wrap,
            });
        }
        Grid.SetColumn(cell, column);
        return cell;
    }

    /// <summary>The amber notice of an inactive ladder (55f): the place is kept, and the date of the
    /// last rated match when the server sent it.</summary>
    private static FrameworkElement BuildInactiveNotice(LadderStanding ladder)
    {
        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = Strings.Get("MpInactiveNotice"),
            FontSize = (double)Application.Current.FindResource("MpBodySize"),
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.FindResource("MpCautionTextAlt"),
            TextWrapping = TextWrapping.Wrap,
        });
        if (ShortDate.Parse(ladder.LastRatedAt) is { } last)
        {
            text.Children.Add(new TextBlock
            {
                Text = Strings.Format("MpInactiveLast", ShortDate.Format(last)),
                Margin = new Thickness(0, 3, 0, 0),
                FontSize = (double)Application.Current.FindResource("MpLabelSize"),
                Foreground = (Brush)Application.Current.FindResource("MpCautionText"),
                TextWrapping = TextWrapping.Wrap,
            });
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock
        {
            Text = "i",
            Margin = new Thickness(0, 0, 10, 0),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.FindResource("MpCaution"),
        });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        return new Border
        {
            Child = grid,
            Tag = "InactiveNotice",
            Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = (CornerRadius)Application.Current.FindResource("RadiusRow"),
            Background = (Brush)Application.Current.FindResource("MpCautionBg"),
            BorderBrush = (Brush)Application.Current.FindResource("MpInactiveNoticeRim"),
            BorderThickness = new Thickness(1),
        };
    }

    /// <summary>
    /// Placing (55d's team card, 55e): the rating with its "?", "Placement 2/5", one 8-px segment
    /// per required match coloured by the player's own results, and how many are left.
    /// </summary>
    private void FillPlacementCard(StackPanel body, LadderStanding ladder, bool team)
    {
        var required = PlacementRequiredFor(ladder, team);
        var played = Math.Min(ladder.PlacementPlayed > 0 ? ladder.PlacementPlayed : ladder.GamesPlayed, required);

        var rating = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        var figure = new TextBlock
        {
            FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont"),
            FontSize = (double)Application.Current.FindResource("MpProfileRatingSize"),
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.FindResource("MpTextSecondary"),
            ToolTip = TooltipHelper.Wrap(Strings.Get("MpEloProvisionalTip")),
        };
        figure.Inlines.Add(new Run(PlacementView.RatingText(ladder.Rating)));
        figure.Inlines.Add(new Run("?") { Foreground = (Brush)Application.Current.FindResource("MpCaution") });
        rating.Children.Add(figure);

        var progress = new TextBlock
        {
            Margin = new Thickness(0, 5, 0, 0),
            FontSize = (double)Application.Current.FindResource("MpLabelSize"),
            Foreground = (Brush)Application.Current.FindResource("MpRankMutedText"),
        };
        AddTemplateRuns(progress, Strings.Get("MpProfilePlacementLine"), _ => new Run(
            Strings.Format("MpPlacementProgressShort", played, required))
        {
            FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"),
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.FindResource("UiTextStrong"),
        });
        rating.Children.Add(progress);
        body.Children.Add(rating);

        var bottom = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        var segments = BuildPlacementSegments(
            PlacementView.Segments(played, required, ladder.PlacementResults), height: 8, gap: 4, profile: true);
        bottom.Children.Add(segments);
        var left = PlacementView.Remaining(played, required);
        if (left > 0)
        {
            bottom.Children.Add(new TextBlock
            {
                Text = left == 1 ? Strings.Get("MpPlacementLeftOne") : Strings.Format("MpPlacementLeft", left),
                Margin = new Thickness(0, 8, 0, 0),
                FontSize = (double)Application.Current.FindResource("MpMetaSize"),
                Foreground = (Brush)Application.Current.FindResource("MpTextSecondary"),
                TextWrapping = TextWrapping.Wrap,
            });
        }
        body.Children.Add(new Border
        {
            Child = bottom,
            Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(0, 10, 0, 0),
            BorderBrush = (Brush)Application.Current.FindResource("MpRimHair"),
            BorderThickness = new Thickness(0, 1, 0, 0),
        });
    }

    // ------------------------------------------------------------------ against each opponent

    /// <summary>
    /// "Against each opponent" (55d): the player's record against everybody they met on one
    /// ladder, newest-met order as the server sends it, four rows then "See all N opponents".
    /// Null until the standing arrives. With nobody on either ladder it says so (55f).
    /// </summary>
    internal UIElement? BuildHeadToHeadCard()
    {
        if (_cachedStanding is not { } standing) return null;
        var (ladders, legacy) = ProfileLadders(standing);
        // An older backend sends no head-to-head at all: no card is better than a false "nobody".
        if (legacy) return null;

        var title = new TextBlock
        {
            Text = Strings.Get("MpH2HTitle"),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = (double)Application.Current.FindResource("MpProfileH2HSize"),
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.FindResource("MpTextPrimary"),
        };

        var anybody = ProfileModeView.HeadToHeadTotal(ladders.Default) > 0
                      || ProfileModeView.HeadToHeadTotal(ladders.Team) > 0;
        if (!anybody)
        {
            var empty = new StackPanel();
            empty.Children.Add(title);
            empty.Children.Add(new TextBlock
            {
                Text = Strings.Get("MpH2HEmptyYou"),
                Margin = new Thickness(0, 5, 0, 0),
                FontSize = (double)Application.Current.FindResource("MpMetaSize"),
                LineHeight = 18,
                Foreground = (Brush)Application.Current.FindResource("MpTextMuted"),
                TextWrapping = TextWrapping.Wrap,
            });
            return H2HFrame(empty, new Thickness(14, 13, 14, 13));
        }

        var showsTeam = _h2hShowsTeam ?? ProfileModeView.HeadToHeadOpensOnTeams(ladders);
        var ladder = showsTeam ? ladders.Team : ladders.Default;

        var list = new StackPanel();
        var head = new Grid { Margin = new Thickness(14, 10, 14, 10) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(title);
        var sub = new TextBlock
        {
            Text = Strings.Get("MpH2HSub"),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = (double)Application.Current.FindResource("MpRankSmallSize"),
            Foreground = (Brush)Application.Current.FindResource("MpTextFade"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(sub, 1);
        head.Children.Add(sub);
        var toggle = BuildH2HToggle(showsTeam);
        Grid.SetColumn(toggle, 2);
        head.Children.Add(toggle);
        list.Children.Add(head);

        var rows = ProfileModeView.HeadToHeadRows(ladder, _h2hExpanded);
        if (rows.Count == 0)
        {
            list.Children.Add(new Border
            {
                Padding = new Thickness(14, 10, 14, 12),
                BorderBrush = (Brush)Application.Current.FindResource("MpRimHair"),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Child = new TextBlock
                {
                    Text = Strings.Get("MpH2HEmptyYou"),
                    FontSize = (double)Application.Current.FindResource("MpMetaSize"),
                    Foreground = (Brush)Application.Current.FindResource("MpTextMuted"),
                    TextWrapping = TextWrapping.Wrap,
                },
            });
        }
        foreach (var entry in rows) list.Children.Add(BuildH2HRow(entry));

        var total = ProfileModeView.HeadToHeadTotal(ladder);
        if (total > ProfileModeView.HeadToHeadFolded)
        {
            var more = new Button
            {
                Content = _h2hExpanded ? Strings.Get("MpH2HSeeLess") : Strings.Format("MpH2HSeeAll", total),
                Style = (Style)Application.Current.FindResource("MpLinkButton"),
                FontSize = (double)Application.Current.FindResource("MpLabelSize"),
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = "H2HSeeAll",
            };
            more.Click += (_, _) =>
            {
                _h2hExpanded = !_h2hExpanded;
                RenderProfileTab();
            };
            list.Children.Add(new Border
            {
                Height = 36,
                Padding = new Thickness(14, 0, 14, 0),
                BorderBrush = (Brush)Application.Current.FindResource("MpRimHair"),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Child = more,
            });
        }

        return H2HFrame(list, new Thickness(0));
    }

    private static Border H2HFrame(UIElement child, Thickness padding) => new()
    {
        Child = child,
        Tag = HeadToHeadCardTag,
        Margin = new Thickness(0, 12, 0, 0),
        Padding = padding,
        CornerRadius = (CornerRadius)Application.Current.FindResource("RadiusPanel"),
        Background = (Brush)Application.Current.FindResource("MpPanel"),
        BorderBrush = (Brush)Application.Current.FindResource("MpRimMedium"),
        BorderThickness = new Thickness(1),
    };

    /// <summary>The 1v1 / Teams pill on the card's head. Choosing one folds the list again.</summary>
    private FrameworkElement BuildH2HToggle(bool showsTeam)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var team in new[] { false, true })
        {
            var b = new Button
            {
                Content = ModeName(team),
                Style = (Style)Application.Current.FindResource("MpPillSegment"),
                Tag = team == showsTeam ? "active" : null,
                Margin = new Thickness(team ? 3 : 0, 0, 0, 0),
                Name = team ? "H2HTabTeam" : "H2HTab1v1",
            };
            var chosen = team;
            b.Click += (_, _) =>
            {
                if (_h2hShowsTeam == chosen) return;
                _h2hShowsTeam = chosen;
                _h2hExpanded = false;
                RenderProfileTab();
            };
            row.Children.Add(b);
        }
        return new Border
        {
            Child = row,
            Padding = new Thickness(3),
            CornerRadius = new CornerRadius(15),
            Background = (Brush)Application.Current.FindResource("MpGuideSegmentTray"),
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    /// <summary>
    /// One opponent: the face and "Against X" (the only thing that trims), the record as wins in
    /// green and losses in red, a 5-px bar of the same split, and how long since they last met.
    /// </summary>
    private static FrameworkElement BuildH2HRow(HeadToHeadEntry entry)
    {
        var grid = new Grid { Height = 42 };
        foreach (var w in new[] { -1.0, 14, 56, 14, 150, 14, 86 })
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = w < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w),
            });

        var name = string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.UserId : entry.DisplayName;
        var who = new Grid { VerticalAlignment = VerticalAlignment.Center };
        who.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        who.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        who.Children.Add(BuildAvatarDisc(name, entry.AvatarUrl, 24));
        var label = new TextBlock
        {
            Text = Strings.Format("MpH2HRow", name),
            Margin = new Thickness(9, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = (double)Application.Current.FindResource("MpProfileH2HSize"),
            FontWeight = FontWeights.Medium,
            Foreground = (Brush)Application.Current.FindResource("UiTextStrong"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(label, 1);
        who.Children.Add(label);
        grid.Children.Add(who);

        var record = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"),
            FontSize = (double)Application.Current.FindResource("MpProfileH2HSize"),
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.FindResource("MpTextPrimary"),
        };
        record.Inlines.Add(new Run(entry.Wins.ToString()) { Foreground = (Brush)Application.Current.FindResource("MpOkText") });
        record.Inlines.Add(new Run("–"));
        record.Inlines.Add(new Run(entry.Losses.ToString()) { Foreground = (Brush)Application.Current.FindResource("MpDestructiveText") });
        Grid.SetColumn(record, 2);
        grid.Children.Add(record);

        var share = ProfileModeView.WinShare(entry);
        var split = new Grid();
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(share, GridUnitType.Star) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - share, GridUnitType.Star) });
        split.Children.Add(new Border { Background = (Brush)Application.Current.FindResource("MpSegWin") });
        var lost = new Border { Background = (Brush)Application.Current.FindResource("MpSegLoss") };
        Grid.SetColumn(lost, 1);
        split.Children.Add(lost);
        // A Border with a radius clips its child, which is what rounds the two ends of the split.
        var bar = new Border
        {
            Height = 5,
            CornerRadius = new CornerRadius(3),
            VerticalAlignment = VerticalAlignment.Center,
            Child = split,
            Tag = share,
        };
        Grid.SetColumn(bar, 4);
        grid.Children.Add(bar);

        var when = RelativeDay.Format(entry.LastAt, DateTimeOffset.Now);
        if (when != null)
        {
            var ago = new TextBlock
            {
                Text = when,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = (double)Application.Current.FindResource("MpRankSmallSize"),
                Foreground = (Brush)Application.Current.FindResource("MpTextMuted"),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(ago, 6);
            grid.Children.Add(ago);
        }

        return new Border
        {
            Child = grid,
            Tag = entry,
            Padding = new Thickness(14, 0, 14, 0),
            Background = Brushes.Transparent,
            BorderBrush = (Brush)Application.Current.FindResource("MpRimHair"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            ToolTip = TooltipHelper.Wrap(Strings.Format("MpH2HRowTip", name, entry.Wins, entry.Losses)),
        };
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// Fills a TextBlock from a string-table template, each <c>{n}</c> becoming the Run the caller
    /// builds for it — so one figure inside a translated sentence can be monospaced without the
    /// sentence being cut into halves that cannot be translated.
    /// </summary>
    private static void AddTemplateRuns(TextBlock target, string template, Func<int, Run> placeholder)
    {
        var at = 0;
        foreach (Match m in Regex.Matches(template, @"\{(\d+)\}"))
        {
            if (m.Index > at) target.Inlines.Add(new Run(template[at..m.Index]));
            target.Inlines.Add(placeholder(int.Parse(m.Groups[1].Value)));
            at = m.Index + m.Length;
        }
        if (at < template.Length) target.Inlines.Add(new Run(template[at..]));
    }
}
