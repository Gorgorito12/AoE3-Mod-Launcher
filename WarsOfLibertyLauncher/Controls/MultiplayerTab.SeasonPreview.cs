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
/// The SEASON PREVIEW: every surface the rating seasons change, drawn by the real code from
/// <see cref="SeasonDemoData"/> — so the art and the layout can be judged before the first season
/// ends, which no amount of playing could otherwise show.
///
/// <para>Reached the same two ways as the other previews: Settings → Developer (the door that
/// works with a launcher already open) and <c>--demo-seasons=&lt;scene&gt;</c> (the door that
/// makes a screenshot scriptable). Like them it assigns fabricated data straight into the fields
/// the renderers read, and like them it lasts until the launcher restarts.</para>
///
/// <para><b>It saves nothing and asks the server for nothing.</b> It rides on the statistics
/// preview's <c>_demoStats</c>, which already stops every community fetch from replacing the
/// fixture, already excuses the sign-in gate, and already keeps a fabricated calendar from moving
/// the real season latch (<c>MaybeAnnounceSeasonChange</c>). The profile is drawn from a sample
/// SWAPPED in for the length of one synchronous render, so the account chip, the room and the rank
/// guide — everything else that reads the cached standing — never see it.</para>
/// </summary>
public partial class MultiplayerTab
{
    /// <summary>The season preview is up: the ranking's ended seasons and the community payload
    /// come from <see cref="SeasonDemoData"/>. Never reset, like its siblings.</summary>
    private bool _seasonPreview;

    /// <summary>The "first day" scene: the same calendar with the running tables empty.</summary>
    private bool _seasonPreviewFirstDay;

    /// <summary>The Players panel is showing the sample players; live presence frames leave it
    /// alone until the launcher restarts.</summary>
    private bool _seasonPreviewPlayers;

    /// <summary>The sample profile, once the profile scene has been opened.</summary>
    private SeasonProfileSample? _seasonPreviewProfile;

    /// <summary>Set for the length of the one render that draws the sample profile.</summary>
    private bool _renderingPreviewProfile;

    /// <summary>The <c>Tag</c> of the preview's notice lines, so a test can find them.</summary>
    internal const string SeasonPreviewNoticeTag = "SeasonPreviewNotice";

    /// <summary>The <c>Tag</c> of the "sample data" chip on the ranking page.</summary>
    internal const string PreviewChipTag = "PreviewSampleChip";

    /// <summary>
    /// Opens one scene of the season preview. <paramref name="scene"/> is a name from
    /// <see cref="SeasonDemoData.NameOf"/>; missing or unknown opens the ranking.
    ///
    /// <para>The bell scene's notification is MainWindow's to raise — it owns the bell — so here it
    /// only puts the data in place and opens the ranking on the running season, which is where the
    /// bell's own click goes.</para>
    /// </summary>
    public void ShowDemoSeasons(string? scene = null)
    {
        var picked = SeasonDemoData.SceneByName(scene);

        _seasonPreview = true;
        _seasonPreviewFirstDay = picked == SeasonPreviewScene.FirstDay;
        _demoStats = true;
        ApplyDemoStats();

        DiagnosticLog.Write(
            $"Seasons: showing the PREVIEW ({SeasonDemoData.NameOf(picked)}) — nothing here came from a server.");

        switch (picked)
        {
            case SeasonPreviewScene.Final:
                ShowPreviewRanking(SeasonDemoData.EndedSeason);
                break;
            case SeasonPreviewScene.Profile:
                ShowPreviewProfile();
                break;
            case SeasonPreviewScene.Room:
                ShowDemoRoom("full");
                break;
            case SeasonPreviewScene.Players:
                ShowPreviewPlayers();
                break;
            default:
                ShowPreviewRanking(null);
                break;
        }
    }

    /// <summary>The ranking page, on the running season (null) or an ended one.</summary>
    private void ShowPreviewRanking(int? season)
    {
        _rankingSeason = season;
        _activeSubtab = Subtab.Ranking;
        UpdateSubtabHighlights();
        ShowSubtabView();
        RenderRanking();
    }

    /// <summary>The sample profile in its own window — opened, or redrawn if one is up.</summary>
    private void ShowPreviewProfile()
    {
        _seasonPreviewProfile = SeasonDemoData.Profile();
        if (_profileWindow != null)
        {
            RenderProfileTab();
            _profileWindow.Activate();
            return;
        }
        OpenProfileWindow();
    }

    /// <summary>The rooms page, with the sample players in the Players panel.</summary>
    private void ShowPreviewPlayers()
    {
        _seasonPreviewPlayers = true;
        _globalOnlineUsers.Clear();
        foreach (var p in SeasonDemoData.Players())
        {
            _globalOnlineUsers.Add(new OnlinePlayer(
                p.UserId, p.Login, null, p.Status, p.Rating, p.Rd, p.LadderRank,
                LadderRankTeam: p.LadderRankTeam, RatingTeam: p.RatingTeam,
                SeasonTitle: p.SeasonTitle));
        }

        _activeSubtab = Subtab.Rooms;
        UpdateSubtabHighlights();
        ShowSubtabView();
        // Signed out, nothing else would ever show the browser: RenderRoomsTab returns before it
        // gets that far. The preview has to work for somebody who never signed in.
        BrowserPanel.Visibility = Visibility.Visible;
        // The side column opens on the chat; the scene is the OTHER tab.
        ShowPanelTab(players: true);
        RenderActivityStrip();
        RenderPlayersPanel();
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
        if (_renderingPreviewProfile || _seasonPreviewProfile is not { } sample) return false;

        var standing = _cachedStanding;
        var history = _historyRows;
        _renderingPreviewProfile = true;
        _cachedStanding = sample.Standing;
        _historyRows = sample.History;
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
        _profileWindow?.ProfileBody?.Children.Insert(0, BuildSeasonPreviewNotice());
        return true;
    }

    /// <summary>The player the profile is being drawn for: the sample's while the preview renders
    /// it, otherwise whoever is signed in.</summary>
    private Models.Multiplayer.LobbyUserSummary? PreviewProfileUser()
        => _renderingPreviewProfile ? _seasonPreviewProfile?.User : null;

    /// <summary>The id the profile page treats as "me" — whose row is marked, whose ladder place is
    /// looked up, who the usual opponent is the opponent OF.</summary>
    private string? ProfileViewerId
        => _renderingPreviewProfile ? _seasonPreviewProfile?.User.Id : _session?.CurrentUser?.Id;

    /// <summary>One line saying the page is the preview's, in the caution colour the other
    /// previews' banners use.</summary>
    private static TextBlock BuildSeasonPreviewNotice() => new()
    {
        Text = Strings.Get("MpSeasonPreviewNotice"),
        Foreground = (Brush)Application.Current.FindResource("MpCautionText"),
        FontSize = (double)Application.Current.FindResource("MpLabelSize"),
        FontStyle = FontStyles.Italic,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(2, 0, 0, 10),
        Tag = SeasonPreviewNoticeTag,
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
