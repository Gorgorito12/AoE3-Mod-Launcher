using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// The RATING PREVIEW (design handoff 55): every screen of the rating system drawn by the real
/// code from <see cref="EloDemoData"/>, so it can be judged without a server and without the
/// dozens of rated matches each state needs.
///
/// <para>Reached the same two ways as the other previews: Settings → Developer (the door that
/// works with a launcher already open) and <c>--demo-elo=&lt;scene&gt;</c> (the door that makes a
/// screenshot scriptable). Like them it assigns fabricated data straight into the fields the
/// renderers read, and like them it lasts until the launcher restarts.</para>
///
/// <para><b>It saves nothing and asks the server for nothing.</b> It rides on the statistics
/// preview's <c>_demoStats</c>, which already stops every community fetch from replacing the
/// fixture and already excuses the sign-in gate. The profile is drawn from a sample SWAPPED in for
/// the length of one synchronous render, so the account chip, the room and the rank guide —
/// everything else that reads the cached standing — never see it.</para>
///
/// <para>It replaced the season preview when the seasons were removed (rating v3).</para>
/// </summary>
public partial class MultiplayerTab
{
    /// <summary>The rating preview is up. Never reset, like its siblings.</summary>
    private bool _eloPreview;

    /// <summary>The 55c scene: the ranked tables empty, the placement rows still there.</summary>
    private bool _eloPreviewNobodyRanked;

    /// <summary>The Players panel is showing sample players; live presence frames leave it alone
    /// until the launcher restarts.</summary>
    private bool _eloPreviewPlayers;

    /// <summary>The sample profile, once the profile scene has been opened.</summary>
    private EloProfileSample? _eloPreviewProfile;

    /// <summary>Set for the length of the one render that draws the sample profile.</summary>
    private bool _renderingPreviewProfile;

    /// <summary>The <c>Tag</c> of the preview's notice lines, so a test can find them.</summary>
    internal const string EloPreviewNoticeTag = "EloPreviewNotice";

    /// <summary>The <c>Tag</c> of the "sample data" chip on the ranking page.</summary>
    internal const string PreviewChipTag = "PreviewSampleChip";

    /// <summary>
    /// Opens one scene of the rating preview. <paramref name="scene"/> is a name from
    /// <see cref="EloDemoData.NameOf"/>; missing or unknown opens the ranking.
    /// </summary>
    public void ShowDemoElo(string? scene = null)
    {
        var picked = EloDemoData.SceneByName(scene);

        _eloPreview = true;
        _eloPreviewNobodyRanked = picked == EloPreviewScene.Placement;
        _demoStats = true;
        ApplyDemoStats();

        DiagnosticLog.Write(
            $"Rating: showing the PREVIEW ({EloDemoData.NameOf(picked)}) — nothing here came from a server.");

        switch (picked)
        {
            case EloPreviewScene.Profile:
            case EloPreviewScene.Refund:
                ShowPreviewProfile(refund: picked == EloPreviewScene.Refund);
                break;
            case EloPreviewScene.Room1v1:
                ShowSampleRoom(EloDemoData.Room1v1());
                break;
            case EloPreviewScene.RoomTeams:
                ShowSampleRoom(EloDemoData.RoomTeams());
                break;
            case EloPreviewScene.Countdown:
                ShowPreviewCountdown();
                break;
            case EloPreviewScene.Result:
                ShowPreviewResult();
                break;
            case EloPreviewScene.History:
                ShowPreviewProfile(refund: false, history: true);
                break;
            case EloPreviewScene.Highlights:
                ShowPreviewRooms();
                break;
            default:
                ShowPreviewRanking();
                break;
        }
    }

    /// <summary>The ranking page.</summary>
    private void ShowPreviewRanking()
    {
        _activeSubtab = Subtab.Ranking;
        UpdateSubtabHighlights();
        ShowSubtabView();
        RenderRanking();
    }

    /// <summary>The sample profile in its own window — opened, or redrawn if one is up.</summary>
    private void ShowPreviewProfile(bool refund, bool history = false)
    {
        var sample = EloDemoData.Profile();
        if (refund) sample.Standing.Refunds = new() { EloDemoData.Refund() };
        if (history) sample = sample with { History = EloDemoData.History() };
        _eloPreviewProfile = sample;
        _eloPreviewWantsHistory = history;
        if (_profileWindow != null || ProfileBodyOverride != null)
        {
            RenderProfileTab();
            _profileWindow?.Activate();
            return;
        }
        OpenProfileWindow();
    }

    /// <summary>
    /// Test seam: the profile is drawn into this panel instead of the profile window's, so the
    /// tests and the snapshot harness can build the real page without opening a second window.
    /// </summary>
    internal StackPanel? ProfileBodyOverride { get; set; }

    /// <summary>
    /// One of the profile's sample players (55d-55f) — the snapshot harness draws each. Opens the
    /// preview the way the profile scene does.
    /// </summary>
    internal void ShowDemoEloProfile(EloProfileSample sample)
    {
        _eloPreview = true;
        _demoStats = true;
        ApplyDemoStats();
        _eloPreviewProfile = sample;
        _eloPreviewWantsHistory = false;
        _h2hShowsTeam = null;
        _h2hExpanded = false;
        if (_profileWindow != null || ProfileBodyOverride != null) RenderProfileTab();
        else OpenProfileWindow();
    }

    /// <summary>
    /// 55i: the 2v2 sample with every team picked, counting down. Drawn, not run: no timer is
    /// started, because a real countdown ends by launching the game.
    /// </summary>
    private void ShowPreviewCountdown()
    {
        var sample = EloDemoData.RoomTeams(allPicked: true);
        if (!ShowSampleRoom(sample)) return;
        _countdownTeams = sample.Players.Where(p => p.Team is 1 or 2).ToDictionary(p => p.UserId, p => p.Team!.Value);
        _previewCountdown = true;
        ApplyMatchPhaseUi();
        if (_lobbyWindow != null) _lobbyWindow.TeamCountdownNumber.Text = "5";
    }

    /// <summary>
    /// 55j: the five result cards, stacked in the room window where the result phase puts its
    /// card. Drawn, not entered: the result phase stops the room's socket and holds the Leave
    /// button, none of which a sample room has.
    /// </summary>
    private void ShowPreviewResult()
    {
        if (!ShowSampleRoom(EloDemoData.Room1v1()) || _lobbyWindow == null) return;
        var stack = new StackPanel();
        foreach (var model in EloDemoData.ResultCases())
        {
            var card = MatchResultCard.BuildCaseCard(model);
            ((FrameworkElement)card).Margin = new Thickness(0, 0, 0, 12);
            stack.Children.Add(card);
        }
        _lobbyWindow.MatchResultHost.Children.Clear();
        _lobbyWindow.MatchResultHost.Children.Add(new ScrollViewer
        {
            Content = stack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });
        _previewResult = true;
        ApplyMatchPhaseUi();
    }

    /// <summary>The History scene scrolls the profile to its history section.</summary>
    private bool _eloPreviewWantsHistory;

    /// <summary>The <c>Tag</c> of the profile's history section.</summary>
    internal const string ProfileHistoryTag = "ProfileHistory";

    /// <summary>The first element under <paramref name="root"/> (logical tree) whose Tag is <paramref name="tag"/>.</summary>
    private static DependencyObject? FindTagged(DependencyObject root, string tag)
    {
        if (root is FrameworkElement fe && Equals(fe.Tag, tag)) return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            if (FindTagged(child, tag) is { } hit) return hit;
        return null;
    }

    /// <summary>The rooms page, with the sample highlights under the list.</summary>
    private void ShowPreviewRooms()
    {
        _activeSubtab = Subtab.Rooms;
        UpdateSubtabHighlights();
        ShowSubtabView();
        // Signed out, nothing else would ever show the browser: RenderRoomsTab returns before it
        // gets that far. The preview has to work for somebody who never signed in.
        BrowserPanel.Visibility = Visibility.Visible;
        RenderActivityStrip();
    }

    /// <summary>
    /// The profile page, drawn from the SAMPLE when the profile scene has been opened.
    ///
    /// <para>The sample is swapped into the two fields the page reads for exactly one synchronous
    /// render and put back in a <c>finally</c>. Everything else that reads them — the account chip,
    /// the roster's own row, the rank guide — runs outside this window and keeps seeing the real
    /// values. Nothing in the render can push the chip: <c>StandingChanged</c> is only ever called
    /// from the standing fetch and the badge selector, and neither runs here.</para>
    /// </summary>
    /// <returns>True when it drew the page, so the caller stops.</returns>
    private bool TryRenderPreviewProfile()
    {
        if (_renderingPreviewProfile || _eloPreviewProfile is not { } sample) return false;

        var standing = _cachedStanding;
        var history = _historyRows;
        _renderingPreviewProfile = true;
        _cachedStanding = sample.Standing;
        _historyRows = sample.History.ToList();
        try
        {
            RenderProfileTab();
        }
        finally
        {
            _cachedStanding = standing;
            _historyRows = history;
            _renderingPreviewProfile = false;
        }

        // A populated profile is indistinguishable from a real one in a screenshot, which is how
        // a preview turns into a report about somebody's real account.
        var body = ProfileBodyOverride ?? _profileWindow?.ProfileBody;
        body?.Children.Insert(0, BuildEloPreviewNotice());

        // The History scene opens on the history: it is at the bottom of a long page.
        if (_eloPreviewWantsHistory && body != null
            && FindTagged(body, ProfileHistoryTag) is FrameworkElement section)
        {
            Dispatcher.BeginInvoke(new Action(() => section.BringIntoView()),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
        return true;
    }

    /// <summary>The player the profile is being drawn for: the sample's while the preview renders
    /// it, otherwise whoever is signed in.</summary>
    private Models.Multiplayer.LobbyUserSummary? PreviewProfileUser()
        => _renderingPreviewProfile ? _eloPreviewProfile?.User : null;

    /// <summary>The id the profile page treats as "me" — whose row is marked, whose ladder place is
    /// looked up, who the usual opponent is the opponent OF.</summary>
    private string? ProfileViewerId
        => _renderingPreviewProfile ? _eloPreviewProfile?.User.Id : _session?.CurrentUser?.Id;

    /// <summary>The id the RANKING marks as "you": the sample viewer while the preview owns the
    /// page, otherwise whoever is signed in.</summary>
    private string? RankingViewerId
        => _eloPreview ? EloDemoData.ViewerId : _session?.CurrentUser?.Id;

    /// <summary>One line saying the page is the preview's, in the caution colour the other
    /// previews' banners use.</summary>
    private static TextBlock BuildEloPreviewNotice() => new()
    {
        Text = Strings.Get("MpEloPreviewNotice"),
        Foreground = (Brush)Application.Current.FindResource("MpCautionText"),
        FontSize = (double)Application.Current.FindResource("MpLabelSize"),
        FontStyle = FontStyles.Italic,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(2, 0, 0, 10),
        Tag = EloPreviewNoticeTag,
    };

    /// <summary>
    /// The "sample data" chip on the ranking page, while either preview owns its payload.
    ///
    /// <para>The ranking never had a banner of its own, so a ladder fabricated by the statistics
    /// preview was indistinguishable from the real one. Idempotent: found by its tag and only ever
    /// added once, and taken away again if no preview is up.</para>
    /// </summary>
    private void ApplyPreviewChip()
    {
        var host = RankingScopeChips;
        if (host == null) return;

        var existing = host.Children.OfType<FrameworkElement>()
            .FirstOrDefault(e => Equals(e.Tag, PreviewChipTag));
        if (!_demoStats)
        {
            if (existing != null) host.Children.Remove(existing);
            return;
        }

        if (existing is Border { Child: TextBlock text })
        {
            text.Text = Strings.Get("MpPreviewSampleChip");
            return;
        }

        host.Children.Insert(0, new Border
        {
            Tag = PreviewChipTag,
            Background = (Brush)Application.Current.FindResource("MpCautionBg"),
            BorderBrush = (Brush)Application.Current.FindResource("MpCautionRim"),
            BorderThickness = new Thickness(1),
            CornerRadius = (CornerRadius)Application.Current.FindResource("RadiusSm"),
            Padding = new Thickness(9, 4, 9, 4),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = Strings.Get("MpPreviewSampleChip"),
                Foreground = (Brush)Application.Current.FindResource("MpCautionText"),
                FontSize = (double)Application.Current.FindResource("MpLabelSize"),
                FontWeight = FontWeights.SemiBold,
            },
        });
    }
}
