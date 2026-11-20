using System;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// "WHAT'S AT STAKE" (2026-11-18): before a ranked match, what this player would gain if he wins
/// and lose if he loses, the server's figures (<c>room_stake</c>, src/elo/stake.ts — the very
/// function that rates the match, anti-farm included), rounded by the result card's own rule
/// (<see cref="StakeView"/>). Since design 116 it is a card in the room's HEADER, left of Invite
/// (<see cref="BuildStakeCard"/>): two halves, the win-chance band, the anti-farm band. In a 1v1
/// the chance of winning is drawn THERE and nowhere else; in a team room the split bar stays in
/// the roster and the card says only this player's figures. Colours are state colours (gain green,
/// loss red, the discount amber), never the mod's. The match-found dialog draws the same card in
/// its own dress (<see cref="StakeCardDress.Found"/>, design 117).
/// </summary>
public partial class MultiplayerTab
{
    /// <summary>The server's stake for this viewer in the current room; null until one arrived.</summary>
    private RoomStake? _roomStake;

    /// <summary>The figures last logged at Start, for the rig's "promised vs result" check.</summary>
    private (int? Gain, int? Loss)? _stakeAtStartLogged;

    private void HandleRoomStake(JsonElement json)
    {
        RoomStake? incoming;
        try { incoming = JsonSerializer.Deserialize<RoomStake>(json.GetRawText()); }
        catch { return; }
        if (incoming == null || !StakeView.IsNewer(_roomStake, incoming)) return;
        _roomStake = incoming;
        if (incoming.Final == true)
        {
            var v = CurrentStakeView();
            _stakeAtStartLogged = v.State == StakeState.Shown ? (v.Gain, v.Loss) : null;
            DiagnosticLog.Write(v.State == StakeState.Shown
                ? $"Room stake: final gain={v.GainText} loss={v.LossText} pct={v.WinPercent}"
                : $"Room stake: final {v.State}");
        }
        if (_lobbyWindow != null) RefreshRoomView();
    }

    /// <summary>Another PLAYER of the match (not the viewer) is being placed on the room's ladder.</summary>
    private bool AnyOtherRoomPlayerPlacing(bool team)
        => OtherRoomPlayers().Any(m => PlacingOn(team, team ? m.InPlacementTeam : m.InPlacement,
            team ? m.GamesPlayedTeam : m.GamesPlayed) == true);

    /// <summary>Another PLAYER keeps his rating private (placed, and the server sent no number).</summary>
    private bool AnyOtherRoomPlayerPrivate(bool team)
        => OtherRoomPlayers().Any(m => RatingDisplay.IsPrivateRating(
            team ? m.RatingTeam : m.Rating, team ? m.GamesPlayedTeam : m.GamesPlayed, PlacementRequiredShown(team)));

    private System.Collections.Generic.IEnumerable<RoomMemberEntry> OtherRoomPlayers()
        => _roomMembers.Values.Where(m => !string.Equals(m.Role, "spectator", StringComparison.Ordinal)
                                          && !IsRoomViewer(m.UserId));

    /// <summary>What the panel draws now, from the server's stake and the launcher's own count.</summary>
    internal StakeView CurrentStakeView()
    {
        var team = CurrentRoomFormat() != RoomFormat.OneVOne;
        var seats = RoomPlayingSeats();
        var viewer = RoomViewerId;
        var plays = viewer != null && _roomMembers.ContainsKey(viewer) && !RoomViewerWatches();
        return StakeView.From(
            _roomStake,
            team ? null : WinOddsView.ForPlayer(_roomOdds, viewer),
            competitive: _currentLobbyIsCompetitive,
            viewerPlays: plays,
            roomComplete: seats > 0 && RoomPlayerCount() >= seats,
            otherPlacing: AnyOtherRoomPlayerPlacing(team),
            otherPrivate: AnyOtherRoomPlayerPrivate(team));
    }

    /// <summary>A warning triangle, drawn (design base: no symbol as a character).</summary>
    internal static Path CautionTriangle(double size, Brush stroke) => new()
    {
        Data = Geometry.Parse("M7,1.6 L13,12.2 H1 Z M7,5.6 V8.8 M7,10.4 V10.5"),
        Width = size,
        Height = size,
        Stretch = Stretch.Uniform,
        Stroke = stroke,
        StrokeThickness = 1.4,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
        VerticalAlignment = VerticalAlignment.Top,
        SnapsToDevicePixels = true,
    };

    /// <summary>The header card's width (design 116). Geometry: it does not follow the text size.</summary>
    internal const double StakeCardWidth = 300;

    /// <summary>
    /// How one stake card is dressed. The pieces and what they say are the same everywhere; only
    /// the frame changes with the surface that holds it.
    /// </summary>
    /// <param name="Width">Fixed, or null to take the width it is given.</param>
    internal sealed record StakeCardDress(double? Width, double Radius, string FillKey, string RimKey,
        string FigureKey, Thickness HalfPadding, double HalfGap, Thickness BandPadding, bool Shadow)
    {
        /// <summary>The room's header (design 116): 300 wide, floating glass, a shadow.</summary>
        public static readonly StakeCardDress Room = new(StakeCardWidth, 16, "GlassMenu", "StakeCardRim",
            "RoomStakeFigureSize", new Thickness(16, 11, 16, 12), 7, new Thickness(16, 9, 16, 10), true);

        /// <summary>The match-found dialog (design 117): the body's width, the floating glass over
        /// the mod's picture, no shadow.</summary>
        public static readonly StakeCardDress Found = new(null, 12, "GlassMenu", "MmFoundStakeRim",
            "MmFoundStakeFigureSize", new Thickness(16, 10, 16, 11), 6, new Thickness(16, 8, 16, 9), false);
    }

    internal const string StakeCardTag = "RoomStakeCard";

    /// <summary>
    /// "What's at stake" as the room header's card (design 116, frame 116b): two halves — green
    /// with an up arrow, "IF YOU WIN" and the gain; red with a down arrow, "IF YOU LOSE" and the
    /// loss — then, in a 1v1 with a percentage, the win chance with BOTH percentages around a split
    /// bar, then the anti-farm note as an amber band. No title row: the halves explain themselves.
    /// A hidden figure, and a 1v1 with nothing more precise to say (<paramref name="bodyWhenNothing"/>),
    /// are the label over one sentence. Null when there is nothing to draw. The captions trim; the
    /// figures never do. Each piece carries its own corner radius, so nothing needs a clip.
    /// </summary>
    internal static FrameworkElement? BuildStakeCard(StakeView v, bool oneVOne, bool bodyWhenNothing,
        StakeCardDress? dress = null)
    {
        static Brush B(string key) => (Brush)Application.Current.FindResource(key);
        static double S(string key) => (double)Application.Current.FindResource(key);
        static FontFamily Mono() => (FontFamily)Application.Current.FindResource("MonoFont");
        dress ??= StakeCardDress.Room;
        var R = dress.Radius - 1;   // the card's radius less its 1-px rim

        var pieces = new System.Collections.Generic.List<FrameworkElement>();
        Border? winHalf = null, lossHalf = null;

        switch (v.State)
        {
            case StakeState.None:
                if (!bodyWhenNothing) return null;
                pieces.Add(Sentence(Strings.Get("RoomStakeBody"), null));
                break;
            case StakeState.HiddenPlacement:
            case StakeState.HiddenPrivate:
                pieces.Add(Sentence(Strings.Get(v.State == StakeState.HiddenPrivate ? "StakeHiddenPrivate"
                    : oneVOne ? "StakeHiddenPlacement" : "StakeHiddenPlacementTeam"), "RoomStakeHidden"));
                break;
        }

        if (v.State == StakeState.Shown)
        {
            var halves = new Grid();
            halves.ColumnDefinitions.Add(new ColumnDefinition());
            halves.ColumnDefinitions.Add(new ColumnDefinition());
            winHalf = Half(win: true, v.GainText ?? "");
            lossHalf = Half(win: false, v.LossText ?? "");
            Grid.SetColumn(lossHalf, 1);
            halves.Children.Add(winHalf);
            halves.Children.Add(lossHalf);
            pieces.Add(halves);
        }

        if (oneVOne && v.WinPercent is int pct && v.State is StakeState.Shown or StakeState.OddsOnly)
            pieces.Add(OddsBand(Math.Clamp(pct, 0, 100)));

        if (v.State == StakeState.Shown && v.Note is { } note)
        {
            var text = note.OnWin
                ? Strings.Format(oneVOne ? "StakeWinReduced" : "StakeWinReducedTeam",
                    Strings.Format("MpPercentValue", note.Percent), note.Streak)
                : Strings.Format("StakeLossReduced", Strings.Format("MpPercentValue", note.Percent), note.Streak);
            pieces.Add(NoteBand(text, note.OnWin));
        }

        if (pieces.Count == 0) return null;

        // The first piece takes the card's top corners and the last its bottom ones.
        var first = pieces[0];
        var last = pieces[^1];
        if (winHalf != null && lossHalf != null)
        {
            var alsoLast = ReferenceEquals(last, first);
            winHalf.CornerRadius = new CornerRadius(R, 0, 0, alsoLast ? R : 0);
            lossHalf.CornerRadius = new CornerRadius(0, R, alsoLast ? R : 0, 0);
        }
        else if (first is Border top)
            top.CornerRadius = new CornerRadius(R, R, ReferenceEquals(last, first) ? R : 0, ReferenceEquals(last, first) ? R : 0);
        if (!ReferenceEquals(last, first) && last is Border bottom)
            bottom.CornerRadius = new CornerRadius(0, 0, R, R);

        var stack = new StackPanel();
        foreach (var p in pieces) stack.Children.Add(p);
        var card = new Border
        {
            CornerRadius = new CornerRadius(dress.Radius),
            BorderBrush = B(dress.RimKey),
            BorderThickness = new Thickness(1),
            Child = stack,
            Tag = StakeCardTag,
            ToolTip = TooltipHelper.Wrap(Strings.Get("StakeTip")),
            SnapsToDevicePixels = true,
        };
        if (dress.Width is double fixedWidth) card.Width = fixedWidth;
        // A resource reference: the room's floating glass follows the transparency level live.
        card.SetResourceReference(Border.BackgroundProperty, dress.FillKey);
        return dress.Shadow ? WithStakeShadow(card) : card;

        Border Half(bool win, string figure)
        {
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.Children.Add(new Path
            {
                Data = Geometry.Parse(win ? "M6,10.2 V1.8 M2.4,5.4 L6,1.8 L9.6,5.4" : "M6,1.8 V10.2 M2.4,6.6 L6,10.2 L9.6,6.6"),
                Width = 12,
                Height = 12,
                Stroke = B(win ? "UiOk" : "StakeLossMark"),
                StrokeThickness = 1.8,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            });
            var caption = new TextBlock
            {
                Text = Strings.Get(win ? "StakeIfWin" : "StakeIfLose").ToUpper(Strings.Culture),
                FontSize = S("SetGroupLabelSize"),
                FontWeight = FontWeights.Bold,
                Foreground = B(win ? "StakeWinCaption" : "StakeLossCaption"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                // Capitals only: a tight line keeps the half at the design's height.
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                LineHeight = Math.Ceiling(S("SetGroupLabelSize") * 1.2),
            };
            Grid.SetColumn(caption, 1);
            head.Children.Add(caption);

            var size = S(dress.FigureKey);
            var value = new TextBlock
            {
                Text = figure,
                Margin = new Thickness(0, dress.HalfGap, 0, 0),
                FontFamily = Mono(),
                FontSize = size,
                FontWeight = FontWeights.Bold,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                LineHeight = Math.Ceiling(size),
                Foreground = B(win ? "RmTextGreen" : "UiDangerText"),
            };
            System.Windows.Automation.AutomationProperties.SetAutomationId(value, win ? "RoomStakeGain" : "RoomStakeLoss");

            var col = new StackPanel();
            col.Children.Add(head);
            col.Children.Add(value);
            return new Border
            {
                Padding = dress.HalfPadding,
                Background = B(win ? "StakeWinFill" : "StakeLossFill"),
                BorderBrush = B("StakeHalfLine"),
                BorderThickness = new Thickness(win ? 0 : 1, 0, 0, 0),
                Child = col,
            };
        }

        Border OddsBand(int mine)
        {
            // One tight line, as drawn (the design's line-height is 1): the band is as tall as
            // its padding plus the percentages.
            var bandLine = Math.Ceiling(S("SetBodySize") * 1.12);
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { MinWidth = 36 });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // The label gives way first: the two percentages and the bar always fit beside it.
            row.Children.Add(new TextBlock
            {
                Text = Strings.Get("StakeChance"),
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                LineHeight = bandLine,
                MaxWidth = dress.Width is double w ? w - 180 : 240,
                FontSize = S("SetDescSize"),
                Foreground = B("UiMutedText"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            });
            var mineText = Percent(mine, "RmTextGreen", "RoomStakePct");
            mineText.Margin = new Thickness(9, 0, 9, 0);
            Grid.SetColumn(mineText, 1);
            row.Children.Add(mineText);

            var bar = new Grid { Height = 5, VerticalAlignment = VerticalAlignment.Center };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(mine, 0.001), GridUnitType.Star), MinWidth = 8 });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(100 - mine, 0.001), GridUnitType.Star), MinWidth = 8 });
            bar.Children.Add(new Border { CornerRadius = new CornerRadius(3, 1, 1, 3), Background = B("StakeBarMine"), Tag = mine });
            var rivalSeg = new Border { CornerRadius = new CornerRadius(1, 3, 3, 1), Background = B("StakeBarRival") };
            Grid.SetColumn(rivalSeg, 2);
            bar.Children.Add(rivalSeg);
            Grid.SetColumn(bar, 2);
            row.Children.Add(bar);

            var rivalText = Percent(100 - mine, "UiDangerText", "RoomStakeRivalPct");
            rivalText.Margin = new Thickness(9, 0, 0, 0);
            Grid.SetColumn(rivalText, 3);
            row.Children.Add(rivalText);

            return new Border
            {
                Padding = dress.BandPadding,
                Background = B("StakeOddsBand"),
                BorderBrush = B("StakeBandLine"),
                BorderThickness = new Thickness(0, pieces.Count == 0 ? 0 : 1, 0, 0),
                Child = row,
            };
        }

        TextBlock Percent(int value, string ink, string id)
        {
            var tb = new TextBlock
            {
                Text = Strings.Format("MpPercentValue", value),
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                LineHeight = Math.Ceiling(S("SetBodySize") * 1.12),
                FontFamily = Mono(),
                FontSize = S("SetBodySize"),
                FontWeight = FontWeights.Bold,
                Foreground = B(ink),
                VerticalAlignment = VerticalAlignment.Center,
            };
            System.Windows.Automation.AutomationProperties.SetAutomationId(tb, id);
            return tb;
        }

        Border NoteBand(string text, bool onWin)
        {
            var line = new Grid();
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition());
            if (onWin)
            {
                var mark = CautionTriangle(12, B("UiErrorText"));
                mark.Margin = new Thickness(0, 1.5, 7, 0);
                line.Children.Add(mark);
            }
            var words = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = S("SetDescSize"),
                Foreground = B(onWin ? "UiErrorText" : "UiSoftText"),
            };
            System.Windows.Automation.AutomationProperties.SetAutomationId(words, "RoomStakeNote");
            Grid.SetColumn(words, 1);
            line.Children.Add(words);
            // A reduced WIN costs points: the amber band. A reduced LOSS saves them: quiet.
            return new Border
            {
                Padding = new Thickness(16, 8, 16, 9),
                Background = onWin ? B("StakeNoteBand") : Brushes.Transparent,
                BorderBrush = B(onWin ? "StakeNoteLine" : "StakeBandLine"),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Child = line,
            };
        }

        Border Sentence(string text, string? id)
        {
            var col = new StackPanel();
            col.Children.Add(new TextBlock
            {
                Text = Strings.Get("RoomStakeTitle"),
                FontSize = S("SetGroupLabelSize"),
                FontWeight = FontWeights.Bold,
                Foreground = B("UiDimText"),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            var words = new TextBlock
            {
                Text = text,
                Margin = new Thickness(0, 7, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                FontSize = S("SetControlSize"),
                Foreground = B("UiSoftText"),
            };
            if (id != null) System.Windows.Automation.AutomationProperties.SetAutomationId(words, id);
            col.Children.Add(words);
            return new Border { Padding = new Thickness(16, 11, 16, 12), Child = col };
        }
    }

    /// <summary>
    /// The card over its shadow (design 116: 0 8 22, black at 26 %). The shadow is a blurred, cached
    /// SIBLING, clipped to everything but the card's own rounded rectangle — the card is translucent,
    /// and an Effect on it would cost its text ClearType. With the effects reduced the card goes alone.
    /// </summary>
    private static FrameworkElement WithStakeShadow(Border card)
    {
        if (RankBadge.ReducedEffects) return card;
        const double reach = 48;
        var shadow = new Border
        {
            CornerRadius = new CornerRadius(16),
            Margin = new Thickness(0, 8, 0, -8),
            Background = (Brush)Application.Current.FindResource("StakeCardShadow"),
            Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 22 },
            CacheMode = new BitmapCache(),
            IsHitTestVisible = false,
        };
        var host = new Grid { IsHitTestVisible = false };
        host.Children.Add(shadow);
        // Clip writes no layout, so setting it from SizeChanged cannot loop.
        host.SizeChanged += (_, e) =>
        {
            var clip = Geometry.Combine(
                new RectangleGeometry(new Rect(-reach, -reach, e.NewSize.Width + 2 * reach, e.NewSize.Height + 2 * reach)),
                new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 16, 16),
                GeometryCombineMode.Exclude, null);
            clip.Freeze();
            host.Clip = clip;
        };
        var stack = new Grid();
        stack.Children.Add(host);
        stack.Children.Add(card);
        return stack;
    }

    /// <summary>
    /// Fills the header's card (<c>LobbyWindow.RoomStakeCard</c>); true when it drew something.
    /// Rebuilt only when what it says changed: the room view refreshes on every frame of the room.
    /// </summary>
    private bool RenderStakeCard(LobbyWindow w, bool allowed)
    {
        // 20 from the button beside it; flush with the header's edge when there is none (a
        // tournament room, a quick match). The header is refreshed before the panels.
        var buttonBeside = w.InvitePlayersButton.Visibility == Visibility.Visible
                           || w.RoomViewBracketButton.Visibility == Visibility.Visible;
        w.RoomStakeCard.Margin = new Thickness(0, 0, buttonBeside ? 20 : 0, 0);

        var oneVOne = CurrentRoomFormat() == RoomFormat.OneVOne;
        var view = allowed ? CurrentStakeView() : StakeView.Nothing;
        // "This match counts for your ELO" only for somebody who plays it, in a 1v1, while there
        // is nothing more precise to say.
        var body = allowed && oneVOne && !RoomViewerWatches();
        var key = $"{allowed}|{view}|{oneVOne}|{body}|{Strings.Language}|{RankBadge.ReducedEffects}|{TextScale.CurrentFactor}";
        if (w.RoomStakeCard.Tag as string == key) return w.RoomStakeCard.Child != null;
        w.RoomStakeCard.Tag = key;
        var card = allowed ? BuildStakeCard(view, oneVOne, body) : null;
        w.RoomStakeCard.Child = card;
        w.RoomStakeCard.Visibility = card != null ? Visibility.Visible : Visibility.Collapsed;
        return card != null;
    }

    /// <summary>The result card's figure against the one promised at Start (the rig reads this).</summary>
    private void LogStakeAgainstResult(MatchOutcomeView model)
    {
        if (_stakeAtStartLogged is not { } promised) return;
        // A match the server did not rate (too short with a new account, a refused reading…) moved
        // nothing, and no promise covered that case.
        if (model.LooksUnrated)
        {
            DiagnosticLog.Write("Result card: not rated, so no figure to compare");
            _stakeAtStartLogged = null;
            return;
        }
        var delta = model.RatingDelta;
        var expected = model.Verdict switch
        {
            MatchVerdict.Win => promised.Gain,
            MatchVerdict.Loss => promised.Loss,
            _ => null,
        };
        if (delta == null || expected == null) return;
        DiagnosticLog.Write(delta == expected
            ? $"Result card: delta={RatingDisplay.FormatDelta(delta)} promised={RatingDisplay.FormatDelta(expected)}"
            : $"Result card: STAKE MISMATCH delta={RatingDisplay.FormatDelta(delta)} promised={RatingDisplay.FormatDelta(expected)}");
        _stakeAtStartLogged = null;
    }
}
