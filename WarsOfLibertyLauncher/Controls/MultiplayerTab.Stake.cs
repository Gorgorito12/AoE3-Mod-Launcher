using System;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// "WHAT'S AT STAKE" (2026-11-18): before a ranked match, what this player would gain if he wins
/// and lose if he loses, the server's figures (<c>room_stake</c>, src/elo/stake.ts — the very
/// function that rates the match, anti-farm included), rounded by the result card's own rule
/// (<see cref="StakeView"/>). In a 1v1 the chance of winning is drawn HERE and nowhere else (the
/// roster's own odds card is gone); in a team room the split bar stays in the roster and this
/// panel says only this player's figures. Colours are state colours (gain green, loss red, the
/// discount amber), never the mod's.
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

    /// <summary>
    /// The block itself: "If you win +8 · If you lose −17", the 1v1 bar with its percentage, and the
    /// amber note when the discount applies. Null when there is nothing to draw. The captions trim;
    /// the figures never do. <paramref name="compact"/> leaves out the bar (the found dialog).
    /// </summary>
    internal static FrameworkElement? BuildStakeBlock(StakeView v, bool oneVOne, bool compact = false)
    {
        static Brush B(string key) => (Brush)Application.Current.FindResource(key);
        static double S(string key) => (double)Application.Current.FindResource(key);
        var stack = new StackPanel { Tag = "RoomStakeBlock" };

        switch (v.State)
        {
            case StakeState.None:
                return null;
            case StakeState.HiddenPlacement:
            case StakeState.HiddenPrivate:
            {
                var key = v.State == StakeState.HiddenPrivate ? "StakeHiddenPrivate"
                    : oneVOne ? "StakeHiddenPlacement" : "StakeHiddenPlacementTeam";
                var tb = new TextBlock
                {
                    Text = Strings.Get(key),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = B("MpTextSecondary"),
                    FontSize = S("MpMetaSize"),
                };
                System.Windows.Automation.AutomationProperties.SetAutomationId(tb, "RoomStakeHidden");
                stack.Children.Add(tb);
                return stack;
            }
        }

        if (v.State == StakeState.Shown)
        {
            var grid = new Grid { ToolTip = TooltipHelper.Wrap(Strings.Get("StakeTip")) };
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.Children.Add(Figure(Strings.Get("StakeIfWin"), v.GainText ?? "", "MpOkText", "RoomStakeGain"));
            var loss = Figure(Strings.Get("StakeIfLose"), v.LossText ?? "", "MpDestructiveText", "RoomStakeLoss");
            Grid.SetColumn(loss, 2);
            grid.Children.Add(loss);
            stack.Children.Add(grid);
        }

        if (oneVOne && !compact && v.WinPercent is int pct)
        {
            var fill = Math.Clamp(pct / 100.0, 0, 1);
            var bar = new Grid { Height = 5, Margin = new Thickness(0, v.State == StakeState.Shown ? 12 : 2, 0, 0) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(fill, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - fill, GridUnitType.Star) });
            var track = new Border { CornerRadius = new CornerRadius(3), Background = B("MpSegPendingProfile") };
            Grid.SetColumnSpan(track, 2);
            bar.Children.Add(track);
            bar.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = B("MpAction"), Tag = pct });
            stack.Children.Add(bar);
            stack.Children.Add(new TextBlock
            {
                Text = Strings.Format("StakeOddsFor", Strings.Format("MpPercentValue", pct)),
                Margin = new Thickness(0, 6, 0, 0),
                FontSize = S("MpMetaSize"),
                Foreground = B("MpTextSecondary"),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }

        if (v.Note is { } note)
        {
            var text = note.OnWin
                ? Strings.Format(oneVOne ? "StakeWinReduced" : "StakeWinReducedTeam",
                    Strings.Format("MpPercentValue", note.Percent), note.Streak)
                : Strings.Format("StakeLossReduced", Strings.Format("MpPercentValue", note.Percent), note.Streak);
            stack.Children.Add(new TextBlock
            {
                Text = "⚠ " + text,
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                FontSize = S("MpMetaSize"),
                // The discount on a win costs points: caution. On a loss it saves them: quiet.
                Foreground = B(note.OnWin ? "MpCaution" : "MpTextSecondary"),
            });
        }
        return stack.Children.Count == 0 ? null : stack;

        static FrameworkElement Figure(string caption, string figure, string ink, string id)
        {
            var col = new StackPanel();
            col.Children.Add(new TextBlock
            {
                Text = caption,
                FontSize = S("MpMetaSize"),
                Foreground = B("MpTextMuted"),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            var value = new TextBlock
            {
                Text = figure,
                Margin = new Thickness(0, 2, 0, 0),
                FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"),
                FontSize = S("MpRatingSize"),
                FontWeight = FontWeights.SemiBold,
                Foreground = B(ink),
            };
            System.Windows.Automation.AutomationProperties.SetAutomationId(value, id);
            col.Children.Add(value);
            return col;
        }
    }

    /// <summary>Fills the panel's block; true when it drew something.</summary>
    private bool RenderStakeBlock(LobbyWindow w)
    {
        w.RoomStakeBlock.Children.Clear();
        var block = BuildStakeBlock(CurrentStakeView(), CurrentRoomFormat() == RoomFormat.OneVOne);
        if (block == null) return false;
        w.RoomStakeBlock.Children.Add(block);
        return true;
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
