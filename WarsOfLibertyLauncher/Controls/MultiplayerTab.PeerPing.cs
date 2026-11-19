using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// The ping to each player of the room, under his state word on the right of the row: four bars
/// and the milliseconds (<see cref="PeerPingView"/>). Under the word rather than beside it, so a
/// 300-px left column gives none of its width to it and the name keeps what it had.
///
/// <para>The cells are kept by user id and repainted in place on the room's 2.5-s tick, so a
/// reading never rebuilds a row. The roster is rebuilt on every member change, which empties the
/// table first (<see cref="ForgetPeerPingCells"/>).</para>
/// </summary>
public partial class MultiplayerTab
{
    private readonly Dictionary<string, TextBlock> _peerPingCells = new();

    private void ForgetPeerPingCells() => _peerPingCells.Clear();

    /// <summary>The state word with the ping cell under it; the word alone when there is no ping to show.</summary>
    private FrameworkElement WithPeerPing(FrameworkElement state, RoomMemberEntry m, bool isMe)
    {
        if (isMe || string.IsNullOrEmpty(m.RadminIp)) return state;
        var column = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        state.HorizontalAlignment = HorizontalAlignment.Right;
        column.Children.Add(state);

        var text = new TextBlock
        {
            FontFamily = MonoFont,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        text.SetResourceReference(TextBlock.FontSizeProperty, "SetControlSize");
        var bars = BuildSignalBars();
        bars.Margin = new Thickness(0, 0, 6, 0);
        text.Tag = bars;
        var cell = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 4, 0, 0),
            // Hit-testable across the gap between the bars and the figure, or the tooltip only
            // answers over the painted pixels.
            Background = System.Windows.Media.Brushes.Transparent,
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(cell, "RoomPing:" + m.UserId);
        cell.Children.Add(bars);
        cell.Children.Add(text);
        column.Children.Add(cell);

        _peerPingCells[m.UserId] = text;
        FillPeerPingCell(text, m);
        return column;
    }

    private void FillPeerPingCell(TextBlock text, RoomMemberEntry m)
    {
        var view = PeerPingView.For(isMe: false, !string.IsNullOrEmpty(m.RadminIp),
            m.PingMs, m.LastGoodPingMs, m.ConsecutiveFails);
        text.Text = view.Text;
        text.Foreground = Rm(view.BrushKey);
        if (text.Tag is StackPanel bars)
            for (var i = 0; i < bars.Children.Count; i++)
                if (bars.Children[i] is Border bar) bar.Background = Rm(i < view.LitBars ? view.BrushKey : "RmPingBarOff");
        if (text.Parent is FrameworkElement cell)
            cell.ToolTip = view.TipKey == null ? null : TooltipHelper.Wrap(view.Ms is { } ms
                ? Strings.Format(view.TipKey, MemberName(m), ms)
                : Strings.Format(view.TipKey, MemberName(m)));
    }

    /// <summary>A plausible, stable figure for a sample room's player (18-177 ms; FNV-1a of the id).</summary>
    private static int SamplePing(string userId)
    {
        var h = 2166136261u;
        foreach (var c in userId) h = unchecked((h ^ c) * 16777619u);
        return 18 + (int)(h % 160);
    }

    /// <summary>Repaint every ping cell from the latest readings.</summary>
    private void RefreshPeerPingCells()
    {
        foreach (var (userId, text) in _peerPingCells)
            if (_roomMembers.TryGetValue(userId, out var m)) FillPeerPingCell(text, m);
    }
}
