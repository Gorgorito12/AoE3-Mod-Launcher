using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// The Rooms page with sample rooms in it (design 86a-86e), for the same reason its siblings exist:
/// a list with seven rooms of every kind needs seven hosts, and a list that failed to load needs a
/// server that fails. Reached by <c>--demo-rooms=86a..86e</c> and from Settings → Developer.
/// </summary>
public partial class MultiplayerTab
{
    /// <summary>True while the Rooms page shows the sample. It stops the list poll, the community
    /// fetch and the chat fold's save from replacing or recording anything, and the buttons say
    /// what they would do instead of doing it.</summary>
    private bool _demoRooms;

    private RoomListDemoData.Scenario _demoRoomsScenario;

    /// <summary>The fold the sample asks for, never written to the config.</summary>
    private bool? _demoChatFolded;

    /// <summary>Fill the Rooms page with the sample and show it.</summary>
    /// <param name="scenario"><c>86a</c>..<c>86e</c>, or empty / loading / error / offline.</param>
    public void ShowDemoRooms(string? scenario = null)
    {
        _demoRooms = true;
        _demoRoomsScenario = RoomListDemoData.Parse(scenario);
        _activeSubtab = Subtab.Rooms;
        UpdateSubtabHighlights();
        ShowSubtabView();
        // Signed out, nothing else would show the browser: RenderRoomsTab returns before it.
        BrowserPanel.Visibility = Visibility.Visible;

        _communityStats = StatsDemoData.Community();
        _communityStats.RecentMatches = RoomListDemoData.RecentMatches(DateTime.UtcNow);
        _activityFetchedUtc = DateTime.UtcNow;
        RenderActivityStrip();

        _demoChatFolded = RoomListDemoData.ChatFolded(_demoRoomsScenario);
        ApplyChatFold();
        FillDemoChat();

        var now = DateTime.UtcNow;
        RoomsErrorBox.Visibility = Visibility.Collapsed;
        switch (_demoRoomsScenario)
        {
            case RoomListDemoData.Scenario.Loading:
                _lastBrowserList = null;
                RoomsListPanel.Children.Clear();
                RoomsEmptyState.Visibility = Visibility.Collapsed;
                SetVisibility(RoomsHeaderBand, false);
                for (var i = 0; i < SkeletonRowCount; i++) RoomsListPanel.Children.Add(BuildRoomSkeletonRow());
                break;
            case RoomListDemoData.Scenario.Error:
            case RoomListDemoData.Scenario.Offline:
                _lastBrowserList = null;
                ShowRoomsProblem(offline: _demoRoomsScenario == RoomListDemoData.Scenario.Offline);
                // Offline, "+ Create room" is off as it is for real (SetOfflineMode).
                CreateRoomButton.IsEnabled = _demoRoomsScenario != RoomListDemoData.Scenario.Offline;
                break;
            case RoomListDemoData.Scenario.Empty:
                _lastBrowserList = new();
                RenderRoomRows(_lastBrowserList);
                break;
            default:
                _lastBrowserList = RoomListDemoData.Rooms(now);
                RenderRoomRows(_lastBrowserList);
                break;
        }
        _roomIdsSeeded = true;
        _lastRoomsRenderedAt = DateTime.Now;
        UpdateRoomsUpdatedLabel();
        _quickCardKey = null;
        RenderQuickCard();
        QueueActivityLayout();

        DiagnosticLog.Write($"Rooms: showing DEMO rooms ({_demoRoomsScenario}) — nothing came from a server.");
    }

    /// <summary>
    /// The list could not be loaded, or there is no connection (86d / 86e): the rows, the headings
    /// and the empty notice give way to one centred message with Retry. The next successful load
    /// takes it down (RefreshRoomsListAsync, RenderRoomRows).
    /// </summary>
    private void ShowRoomsProblem(bool offline)
    {
        RoomsListPanel.Children.Clear();
        _roomPingTexts.Clear();
        RoomsEmptyState.Visibility = Visibility.Collapsed;
        SetVisibility(RoomsHeaderBand, false);
        RoomsErrorTitle.Text = Strings.Get(offline ? "RmOfflineTitle" : "RmErrorTitle");
        RoomsErrorTitle.Foreground = (System.Windows.Media.Brush)Application.Current.FindResource(offline ? "RmTextStrong" : "RmPingSlow");
        RoomsErrorText.Text = Strings.Get(offline ? "RmOfflineBody" : "RmErrorBody");
        RoomsErrorRetry.Content = Strings.Get("RmRetry");
        RoomsErrorBox.Visibility = Visibility.Visible;
        UpdateRoomsCount(0);
        UpdateRoomsUpdatedLabel();
        QueueActivityLayout();
    }

    /// <summary>The sample chat and the sample players beside the list.</summary>
    private void FillDemoChat()
    {
        GlobalChatPanel.Children.Clear();
        _lastGlobalChatAuthor = null;
        _lastGlobalChatDate = null;
        _globalChatRendered = true;
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var (login, body, ago) in RoomListDemoData.Chat)
            AppendGlobalChatRow(login, body, nowMs - ago * 60_000L, null, "demo-" + login);
        UpdateGlobalChatEmptyHint();
        ScrollGlobalChatToEnd();

        _globalOnlineUsers.Clear();
        foreach (var (login, rating, status) in RoomListDemoData.Online)
            _globalOnlineUsers.Add(new OnlinePlayer("demo-" + login, login, null, status, rating, 80, null));
        RenderPlayersPanel();
    }

    /// <summary>What a sample row's button would have done, said instead of done.</summary>
    private void DemoRoomsSay(string what)
    {
        DiagnosticLog.Write($"Rooms DEMO: '{what}' pressed — nothing is sent.");
        _showAppToast?.Invoke(new AppToast.ToastOptions(
            "", Strings.Get("RmDemoNotice"), what, Array.Empty<AppToast.ToastAction>(),
            AutoDismissMs: 3500, Cause: ToastCause.Answer));
    }
}
