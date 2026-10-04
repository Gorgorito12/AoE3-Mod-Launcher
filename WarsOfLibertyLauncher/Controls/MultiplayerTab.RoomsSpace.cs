using System;
using System.Windows;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// The Rooms page's chat column and empty list (design handoff 57): the chat at a fixed 280 or
/// 320 px, foldable to a 44-px rail that counts the messages missed while folded, and 56a's
/// compact notice when there are no rooms.
/// </summary>
public partial class MultiplayerTab
{
    /// <summary>Messages from other players that arrived while the chat was folded.</summary>
    private int _chatUnread;

    /// <summary>Read by the tests.</summary>
    internal int ChatUnread => _chatUnread;

    /// <summary>Whether the chat is folded to its rail (remembered in the config).</summary>
    internal bool ChatFolded => _config?.RoomsChatFolded == true;

    /// <summary>
    /// The chat column's width (design 57): 280 below 1600 px of page width, 320 from there, the
    /// 44-px rail when folded. Re-run when the page's width changes and when the chat folds.
    /// </summary>
    private void ApplyChatWidth()
    {
        if (RoomsSideColumn == null) return;
        var width = RoomsActivityLayout.ChatWidth(RoomsView?.ActualWidth ?? 0, ChatFolded);
        var length = new GridLength(width);
        if (!RoomsSideColumn.Width.Equals(length)) RoomsSideColumn.Width = length;
    }

    /// <summary>
    /// The tab's margin and gap as last applied (<see cref="PageSpacing"/>): 12 below 1600 px of
    /// width, 16 from there. Read by the layout passes that set a gap in code, and by the tests.
    /// </summary>
    internal double PageGap { get; private set; } = PageSpacing.Narrow;

    /// <summary>
    /// Write the tab's one margin M and one gap G into its resources (the maintainer's rule):
    /// every margin and gap of the content area reads them with DynamicResource, so this is the
    /// single place they change. Called on construction and whenever the tab's width changes;
    /// writes only when the value moves.
    /// </summary>
    internal void ApplyPageSpacing()
    {
        if (TabRootGrid == null) return;
        var m = PageSpacing.For(TabRootGrid.ActualWidth);
        if (m == PageGap && Resources["MpPageGutter"] is Thickness current && current.Left == m) return;
        PageGap = m;
        Resources["MpPageGutter"] = new Thickness(m);
        Resources["MpPageGutterSides"] = new Thickness(m, 0, m, 0);
        Resources["MpPageGutterBanner"] = new Thickness(m, m, m, 0);
        Resources["MpPageGutterLeftPane"] = new Thickness(m, m, 0, m);
        Resources["MpPageGutterRightPane"] = new Thickness(0, m, m, m);
        Resources["MpPanelGap"] = new GridLength(m);
        Resources["MpPanelGapTop"] = new Thickness(0, m, 0, 0);
        // The passes that use the gap in code: the community cards' columns, the rooms/community
        // split and the ranking's.
        LayOutActivityColumns();
        QueueActivityLayout();
        ApplyRankingSplit();
    }

    /// <summary>Paint the fold: the chat card or its rail, and the column width that goes with it.</summary>
    private void ApplyChatFold()
    {
        if (ChatPanelCard == null || ChatRail == null) return;
        var folded = ChatFolded;
        SetVisibility(ChatPanelCard, !folded);
        SetVisibility(ChatRail, folded);
        if (!folded) _chatUnread = 0;
        UpdateChatRail();
        ApplyChatWidth();
    }

    /// <summary>Fold or unfold the chat and remember it (57c).</summary>
    internal void SetChatFolded(bool folded)
    {
        if (_config != null && _config.RoomsChatFolded != folded)
        {
            _config.RoomsChatFolded = folded;
            try { _config.Save(); }
            catch (Exception ex) { DiagnosticLog.Write($"Chat fold: config save failed: {ex.Message}"); }
        }
        ApplyChatFold();
    }

    private void ChatFoldButton_Click(object sender, RoutedEventArgs e) => SetChatFolded(true);

    private void ChatRailUnfoldButton_Click(object sender, RoutedEventArgs e) => SetChatFolded(false);

    /// <summary>The chat icon unfolds the chat on its own tab.</summary>
    private void ChatRailChatButton_Click(object sender, RoutedEventArgs e)
    {
        ShowPanelTab(players: false);
        SetChatFolded(false);
    }

    /// <summary>The players count unfolds the chat on the Players tab.</summary>
    private void ChatRailPlayersButton_Click(object sender, RoutedEventArgs e)
    {
        ShowPanelTab(players: true);
        SetChatFolded(false);
    }

    /// <summary>
    /// Count one LIVE message from somebody else while the chat is folded. Called from the
    /// frame handler beside the chat sound — never from the history replay, never for our own.
    /// </summary>
    private void CountUnreadChat()
    {
        if (!ChatFolded) return;
        _chatUnread++;
        UpdateChatRail();
    }

    /// <summary>The rail's unread badge and its players count.</summary>
    private void UpdateChatRail()
    {
        if (ChatRail == null) return;
        ChatRailUnreadBadge.Visibility = _chatUnread > 0 ? Visibility.Visible : Visibility.Collapsed;
        ChatRailUnreadText.Text = _chatUnread > 99 ? "99+" : _chatUnread.ToString(Strings.Culture);
        ChatRailPlayersText.Text = _globalOnlineUsers.Count.ToString(Strings.Culture);
        ChatFoldButton.ToolTip = TooltipHelper.Wrap(Strings.Get("MpChatFoldTip"));
        ChatRailUnfoldButton.ToolTip = TooltipHelper.Wrap(Strings.Get("MpChatUnfoldTip"));
        ChatRailChatButton.ToolTip = TooltipHelper.Wrap(_chatUnread > 0
            ? Strings.Format("MpChatRailUnreadTip", _chatUnread)
            : Strings.Get("MpChatUnfoldTip"));
        ChatRailPlayersButton.ToolTip = TooltipHelper.Wrap(
            Strings.Format("MpChatRailPlayersTip", _globalOnlineUsers.Count));
    }

    /// <summary>
    /// 56a's notice: "No rooms right now", then the hours more people play when they are known —
    /// the same window the peak-hours card names — or the plain invitation when they are not;
    /// and "+ Create room", disabled offline like the toolbar's.
    /// </summary>
    private void RefreshRoomsEmptyText()
    {
        if (EmptyTitleText == null) return;
        EmptyTitleText.Text = Strings.Get("MpRoomsEmptyTitle");
        if (TryLocalPeak(_communityStats, out _, out var peakStart))
        {
            var from = Strings.Format("MpActivityPeakHour", peakStart);
            var to = Strings.Format("MpActivityPeakHour", (peakStart + CommunityStatsView.PeakWindowHours) % 24);
            EmptyBodyText.Text = Strings.Format("MpRoomsEmptyPeak", from, to);
        }
        else
        {
            EmptyBodyText.Text = Strings.Get("MpRoomsEmptyBody");
        }
        EmptyCreateButton.Content = "+ " + Strings.Get("MpRoomsCreate");
        EmptyCreateButton.IsEnabled = !_offlineMode;
        EmptyCreateButton.ToolTip = _offlineMode ? _offlineNeedsInternet : null;
    }
}
