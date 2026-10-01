using System;
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
/// The Profile's badge selector (docs/design_insignia_equipos, 51c): which of a player's two ranks
/// sits beside their name wherever no room decides it — the chat, the Players list, the account
/// block, the profile header.
///
/// <para><b>The choice lives on the SERVER</b> (<c>users.badge_mode</c>, <c>POST
/// /me/badge-mode</c>), because the people who need to see it are the OTHER players. A value kept
/// in <c>LauncherConfig</c> would change only this launcher's view of its own player.</para>
/// </summary>
public partial class MultiplayerTab
{
    /// <summary>Why the last save failed, shown under the tray until the next choice.</summary>
    private string? _badgeModeError;

    /// <summary>
    /// Bumped by every choice, so an answer that arrives after a later click cannot undo it —
    /// two quick clicks are two requests, and only the newest one's outcome may be applied.
    /// </summary>
    private int _badgeModeSeq;

    /// <summary>
    /// The selector card, or null when the server sent no preference (a backend older than the
    /// field): offering a choice nobody can save is worse than not offering one.
    /// </summary>
    internal UIElement? BuildBadgeModeCard(LobbyUserSummary user)
    {
        var standing = _cachedStanding;
        if (standing?.BadgeMode == null) return null;

        var pref = BadgeModes.Parse(standing.BadgeMode);

        int? soloRank = standing.LadderRank;
        int? soloSize = standing.LadderSize;
        if (soloRank == null && MyLadderRank() is > 0 and var fromTable)
        {
            soloRank = fromTable;
            soloSize = LadderSize(team: false);
        }
        var teamSize = standing.LadderSizeTeam is > 0 and var ts ? ts : LadderSize(team: true);
        var soloAge = RankAges.ForOptional(soloRank, soloSize);
        var teamAge = RankAges.ForOptional(standing.LadderRankTeam, teamSize);
        var teamUnlocked = RankBadgeChoice.HasTeamBadge(teamAge);

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = Strings.Get("MpBadgeModeTitle"),
            Foreground = (Brush)Application.Current.FindResource("MpTextPrimary"),
            FontSize = (double)Application.Current.FindResource("MpBodySize"),
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        stack.Children.Add(new TextBlock
        {
            Text = Strings.Get("MpBadgeModeBody"),
            Margin = new Thickness(0, 3, 0, 0),
            Foreground = (Brush)Application.Current.FindResource("MpTextMuted"),
            FontSize = (double)Application.Current.FindResource("MpLabelSize"),
            TextWrapping = TextWrapping.Wrap,
        });

        // The tray: three equal segments. A Grid of star columns rather than a horizontal
        // StackPanel, so the three share the card's width the way the handoff draws them.
        var tray = new Grid();
        for (var i = 0; i < 3; i++)
            tray.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tray.Children.Add(BuildBadgeSegment(BadgeMode.Highest, "MpBadgeModeBest", pref, enabled: true, 0));
        tray.Children.Add(BuildBadgeSegment(BadgeMode.Solo, "MpBadgeMode1v1", pref, enabled: true, 1));
        tray.Children.Add(BuildBadgeSegment(BadgeMode.Team, "MpBadgeModeTeams", pref, teamUnlocked, 2));
        stack.Children.Add(new Border
        {
            Child = tray,
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(3),
            CornerRadius = (CornerRadius)Application.Current.FindResource("RadiusRow"),
            Background = (Brush)Application.Current.FindResource("MpField"),
        });

        if (!string.IsNullOrEmpty(_badgeModeError))
        {
            stack.Children.Add(new TextBlock
            {
                Text = _badgeModeError,
                Margin = new Thickness(0, 8, 0, 0),
                Foreground = (Brush)Application.Current.FindResource("MpDestructiveText"),
                FontSize = (double)Application.Current.FindResource("MpLabelSize"),
                TextWrapping = TextWrapping.Wrap,
            });
        }

        stack.Children.Add(new TextBlock
        {
            Text = Strings.Get("MpBadgeOthersSee"),
            Margin = new Thickness(0, 16, 0, 0),
            Foreground = (Brush)Application.Current.FindResource("UiGroupLabelText"),
            FontSize = (double)Application.Current.FindResource("MpPillSize"),
            FontWeight = FontWeights.SemiBold,
        });
        stack.Children.Add(BuildBadgePreview(user));

        var boxes = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        boxes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        boxes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        boxes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        boxes.Children.Add(WithColumn(BuildLadderBox(
            "MpBadgeMode1v1", soloAge, soloRank ?? 0, standing.Rating, standing.Rd, standing.GamesPlayed), 0));
        boxes.Children.Add(WithColumn(BuildLadderBox(
            "MpBadgeModeTeams", teamAge, standing.LadderRankTeam ?? 0,
            standing.RatingTeam, standing.RdTeam, standing.GamesPlayedTeam), 2));
        stack.Children.Add(boxes);

        return new Border
        {
            Child = stack,
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(15, 14, 15, 14),
            CornerRadius = (CornerRadius)Application.Current.FindResource("RadiusLg"),
            Background = (Brush)Application.Current.FindResource("MpPanel"),
            BorderBrush = (Brush)Application.Current.FindResource("MpRimSoft"),
            BorderThickness = new Thickness(1),
            Tag = "BadgeModeCard",
        };
    }

    private Button BuildBadgeSegment(BadgeMode mode, string captionKey, BadgeMode current, bool enabled, int column)
    {
        var button = new Button
        {
            Content = Strings.Get(captionKey),
            Style = (Style)FindResource("MpBadgeSegment"),
            Tag = mode == current ? "active" : null,
            IsEnabled = enabled,
            Margin = new Thickness(column == 0 ? 0 : 1.5, 0, column == 2 ? 0 : 1.5, 0),
        };
        if (!enabled)
        {
            // A disabled control shows no tooltip unless asked to, and this one is the only
            // place that says how to unlock the option.
            button.ToolTip = TooltipHelper.Wrap(Strings.Get("MpBadgeModeTeamsLocked"));
            ToolTipService.SetShowOnDisabled(button, true);
        }
        Grid.SetColumn(button, column);
        button.Click += (_, _) => _ = ChooseBadgeModeAsync(mode);
        return button;
    }

    /// <summary>How the player looks to others with the badge they chose: avatar, badge, name, and
    /// "Teams · Imperial" on the right. It is the same <see cref="MyBadge"/> the account block and
    /// the Players panel draw, so it cannot show one thing while they show another.</summary>
    private UIElement BuildBadgePreview(LobbyUserSummary user)
    {
        var shown = MyBadge();
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var avatar = BuildAvatarDisc(user.DisplayName, user.AvatarUrl, 26);
        avatar.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(WithColumn(avatar, 0));

        if (shown is { } badge)
        {
            var built = RankBadge.BuildFor(badge, PlayersBadgeWidth, "preview-" + user.Id);
            built.VerticalAlignment = VerticalAlignment.Center;
            built.Margin = new Thickness(10, 0, 0, 0);
            built.IsHitTestVisible = false;
            grid.Children.Add(WithColumn(built, 1));
        }

        grid.Children.Add(WithColumn(new TextBlock
        {
            Text = user.DisplayName,
            Margin = new Thickness(10, 0, 10, 0),
            Foreground = (Brush)Application.Current.FindResource("MpTextHeading"),
            FontSize = (double)Application.Current.FindResource("FontSizeCaption"),
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        }, 2));

        if (shown is { } named)
        {
            grid.Children.Add(WithColumn(new TextBlock
            {
                Text = RankBadgeTips.ModeAndAge(named),
                Foreground = (Brush)Application.Current.FindResource("MpBadgePreviewText"),
                FontSize = (double)Application.Current.FindResource("MpLabelSize"),
                VerticalAlignment = VerticalAlignment.Center,
            }, 3));
        }

        return new Border
        {
            Child = grid,
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = (CornerRadius)Application.Current.FindResource("RadiusRow"),
            Background = (Brush)Application.Current.FindResource("MpRowHighlight"),
        };
    }

    /// <summary>
    /// One ladder box: its name, the age and place in the age's light colour, and the rating of
    /// THAT ladder. Discovery prints no "#0"; an age the server did not report prints a dash, never
    /// Discovery; an unrated ladder says so rather than showing the 1500 everyone starts from.
    /// </summary>
    private static Border BuildLadderBox(string modeKey, RankAge? age, int position, double? rating,
        double? rd, int? games)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = Strings.Get(modeKey).ToUpperInvariant(),
            Foreground = (Brush)Application.Current.FindResource("UiGroupLabelText"),
            FontSize = (double)Application.Current.FindResource("MpMicroSize"),
            FontWeight = FontWeights.SemiBold,
        });

        string place;
        Brush placeBrush;
        if (age is { } a)
        {
            place = Strings.Get(RankAges.NameKey(a));
            if (a != RankAge.Discovery && position > 0)
                place += " · " + Strings.Format("MpBadgeBoxPlace", position);
            placeBrush = (Brush)Application.Current.FindResource("RankLabel" + a);
        }
        else
        {
            place = Strings.Get("MpDash");
            placeBrush = (Brush)Application.Current.FindResource("MpTextFaint");
        }
        stack.Children.Add(new TextBlock
        {
            Text = place,
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = placeBrush,
            FontSize = (double)Application.Current.FindResource("MpBodySize"),
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        string? elo = null;
        if (RatingDisplay.ShouldShow(rating))
            elo = RatingDisplay.IsUnrated(rd, games)
                ? Strings.Get("MpEloUnrated")
                : Strings.Format("MpChipElo", (int)Math.Round(rating!.Value));
        if (elo != null)
        {
            stack.Children.Add(new TextBlock
            {
                Text = elo,
                Margin = new Thickness(0, 2, 0, 0),
                Foreground = (Brush)Application.Current.FindResource("MpTextMuted"),
                FontSize = (double)Application.Current.FindResource("MpFigureSize"),
                FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"),
            });
        }

        return new Border
        {
            Child = stack,
            Padding = new Thickness(11, 9, 11, 9),
            CornerRadius = (CornerRadius)Application.Current.FindResource("RadiusRow"),
            BorderBrush = (Brush)Application.Current.FindResource("UiCardRim"),
            BorderThickness = new Thickness(1),
        };
    }

    /// <summary>
    /// Applies a choice at once — the chip, the profile, the Players panel and the room repaint
    /// before the server answers — then saves it. A refused save puts the previous choice back and
    /// says why; the last click wins when several are in flight.
    /// </summary>
    private async Task ChooseBadgeModeAsync(BadgeMode mode)
    {
        var standing = _cachedStanding;
        var session = _session;
        if (standing == null || session == null) return;

        var wire = BadgeModes.ToWire(mode);
        if (string.Equals(standing.BadgeMode, wire, StringComparison.Ordinal) && _pendingBadgeMode == null)
            return;

        var previous = _pendingBadgeMode == null ? standing.BadgeMode : _badgeModeConfirmed;
        if (_pendingBadgeMode == null) _badgeModeConfirmed = standing.BadgeMode;
        var seq = ++_badgeModeSeq;
        _badgeModeError = null;
        _pendingBadgeMode = wire;
        standing.BadgeMode = wire;
        StandingChanged();

        try
        {
            var response = await session.Api.SetBadgeModeAsync(wire);
            if (seq != _badgeModeSeq) return;
            _pendingBadgeMode = null;
            var saved = string.IsNullOrWhiteSpace(response?.BadgeMode) ? wire : response!.BadgeMode!;
            _badgeModeConfirmed = saved;
            if (_cachedStanding != null) _cachedStanding.BadgeMode = saved;
            DiagnosticLog.Write($"Badge mode saved: {saved}.");
            if (!string.Equals(saved, wire, StringComparison.Ordinal)) StandingChanged();
        }
        catch (Exception ex)
        {
            if (seq != _badgeModeSeq) return;
            _pendingBadgeMode = null;
            if (_cachedStanding != null) _cachedStanding.BadgeMode = _badgeModeConfirmed ?? previous;
            _badgeModeError = ex is LobbyApiException { Code: "team_badge_locked" }
                ? Strings.Get("MpBadgeModeTeamsLocked")
                : Strings.Get("MpBadgeModeSaveFailed");
            DiagnosticLog.Write($"Badge mode NOT saved ({wire}): {ex.GetType().Name}: {ex.Message}");
            StandingChanged();
        }
    }

    /// <summary>What the server last confirmed, so a failed save goes back to THAT and not to an
    /// earlier click that was itself still in flight.</summary>
    private string? _badgeModeConfirmed;
}
