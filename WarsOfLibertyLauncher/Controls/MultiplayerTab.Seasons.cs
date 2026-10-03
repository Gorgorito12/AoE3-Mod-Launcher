using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
/// RATING SEASONS on the multiplayer surface: the ranking's season selector and the final tables
/// of ended seasons, the profile's season history, the medal beside a name, and the bell when a
/// season ends.
///
/// <para><b>The launcher draws seasons and never decides them.</b> The calendar comes with
/// <c>/stats/community</c>, a player's final places and medals with <c>/matches/elo</c>, an
/// ended season's table with <c>/stats/season/:n</c>, and a room member's medal with the room
/// state. Every one of those is null on a backend older than seasons, and every surface here then
/// draws exactly what it drew before — no selector, no medal, no season in a title.</para>
///
/// <para>A partial of its own so the season work sits in one place and touches the main file only
/// at the few hooks that hand it the data.</para>
/// </summary>
public partial class MultiplayerTab
{
    /// <summary>The ranking page's season: null is the RUNNING one (the live ladder).</summary>
    private int? _rankingSeason;

    /// <summary>
    /// Ended seasons' final tables, kept for the session: a season that is over cannot change by
    /// itself (only an operator correction can, and the server's own memo is five minutes).
    /// </summary>
    private readonly Dictionary<int, SeasonStandings> _seasonStandings = new();
    private readonly HashSet<int> _seasonStandingsInFlight = new();
    private readonly Dictionary<int, DateTime> _seasonStandingsFailedAt = new();

    /// <summary>How long a failed season table is left alone before a repaint asks again.</summary>
    private static readonly TimeSpan SeasonRetryAfter = TimeSpan.FromSeconds(30);

    /// <summary>Set while the selector is being filled, so filling it is not read as a choice.</summary>
    private bool _suppressSeasonSelection;

    /// <summary>What the selector was last filled with, so a repaint does not rebuild its items
    /// under the user's pointer — the same rule the statistics page's mod chips follow.</summary>
    private string? _seasonComboSignature;

    /// <summary>
    /// The running season a standing reload was last asked for, so a season boundary costs ONE
    /// extra <c>/matches/elo</c> rather than one per community refresh while it is in flight.
    /// </summary>
    private int _seasonStandingReloadFor;

    /// <summary>
    /// A season ended and the player's final place in it is known. Raised once per season — the
    /// latch is <c>LauncherConfig.LastSeenSeason</c> — and handled by MainWindow, which owns the
    /// bell.
    /// </summary>
    internal event Action<SeasonNoticePlan>? SeasonEnded;

    /// <summary>The medal's diameter after a name in each list: a shade under the avatar beside
    /// it, so it never makes a row taller.</summary>
    internal const double RosterMedalSize = 16;
    internal const double PlayersMedalSize = 15;
    internal const double RankingMedalSize = 16;
    internal const double ProfileMedalSize = 22;
    private const double MedalGap = 6;

    // ------------------------------------------------------------------ ranking: which table

    /// <summary>
    /// The rows the ranking table draws: the running season's from the community payload, or an
    /// ended season's when the selector picked one.
    ///
    /// <para>Null while that ended season's table is still on its way, or after it failed: the
    /// chrome and a line saying which of the two are drawn HERE, and the page repaints when the
    /// table lands. Never an empty list in that case — empty means "nobody finished", which is a
    /// fact about the season, and a table that has not loaded says nothing about it yet.</para>
    /// </summary>
    private IReadOnlyList<LeaderboardRow>? RankingRowsForSelectedSeason(IReadOnlyList<LeaderboardRow>? currentTeam)
    {
        var past = SeasonView.PastSeasonOrNull(_rankingSeason, _communityStats?.Season);
        if (past is not int season)
        {
            return _rankingShowsTeam
                ? currentTeam ?? Array.Empty<LeaderboardRow>()
                : CommunityStatsView.Rows(_communityStats);
        }

        if (TryGetSeasonTable(season, out var table))
        {
            return _rankingShowsTeam
                ? (IReadOnlyList<LeaderboardRow>?)table.LeaderboardTeam ?? Array.Empty<LeaderboardRow>()
                : (IReadOnlyList<LeaderboardRow>?)table.Leaderboard ?? Array.Empty<LeaderboardRow>();
        }

        var failed = _seasonStandingsFailedAt.TryGetValue(season, out var at)
                     && DateTime.UtcNow - at < SeasonRetryAfter;
        RenderRankingChrome(0);
        RankingBody.Children.Add(new TextBlock
        {
            Text = Strings.Format(failed ? "MpSeasonLoadFailed" : "MpSeasonLoading", season),
            Foreground = (Brush)Application.Current.FindResource("MpTextDim"),
            FontSize = (double)Application.Current.FindResource("MpMetaSize"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(14, 12, 14, 14),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Tag = "SeasonTableStatus",
        });
        if (!failed) _ = LoadSeasonStandingsAsync(season);
        return null;
    }

    /// <summary>
    /// An ended season's table, if it is in hand. Under a preview it is the fixture — computed
    /// every time and NEVER cached, or leaving the preview would leave fabricated final tables in
    /// the session's cache, indistinguishable from real ones. The season preview's tables are the
    /// ones its medals were derived from, so the two cannot disagree on screen.
    /// </summary>
    private bool TryGetSeasonTable(int season, out SeasonStandings table)
    {
        if (_demoStats)
        {
            table = _seasonPreview ? SeasonDemoData.SeasonTable(season) : StatsDemoData.SeasonTable(season);
            return true;
        }
        if (_seasonStandings.TryGetValue(season, out var cached))
        {
            table = cached;
            return true;
        }
        table = null!;
        return false;
    }

    /// <summary>Whether the ranking page is showing an ENDED season rather than the live ladder.</summary>
    private bool ShowingPastSeason()
        => SeasonView.PastSeasonOrNull(_rankingSeason, _communityStats?.Season) != null;

    /// <summary>
    /// How many players are on the table on screen — the denominator the rank badges are cut
    /// by. An ended season's table is cut by ITS OWN size: a badge on a past table is the age that
    /// player finished at, and measuring it against today's ladder would hand him a different one.
    /// </summary>
    private int RankingLadderSize()
    {
        if (SeasonView.PastSeasonOrNull(_rankingSeason, _communityStats?.Season) is int season
            && TryGetSeasonTable(season, out var table))
            return _rankingShowsTeam ? table.RankedPlayersTeam : table.RankedPlayers;
        return LadderSize(_rankingShowsTeam);
    }

    /// <summary>
    /// The line an empty table shows. An ended season that nobody finished says exactly that;
    /// the running ladder keeps the entry-bar sentence it always had.
    /// </summary>
    private string RankingEmptyText(int? required)
    {
        if (SeasonView.PastSeasonOrNull(_rankingSeason, _communityStats?.Season) is int season)
            return Strings.Format("MpSeasonEmptyPast", season);
        if (!required.HasValue) return Strings.Get("MpRankingUnavailable");
        return required.Value <= 1
            ? Strings.Get("MpActivityRankingEmptyOne")
            : Strings.Format("MpActivityRankingEmpty", required.Value);
    }

    /// <summary>
    /// Fetches an ended season's final tables and repaints the ranking when they land — if that
    /// season is still the one on screen.
    /// </summary>
    private async Task LoadSeasonStandingsAsync(int season)
    {
        if (_seasonStandings.ContainsKey(season) || !_seasonStandingsInFlight.Add(season)) return;
        var api = _session?.Api;
        if (api == null)
        {
            _seasonStandingsInFlight.Remove(season);
            return;
        }

        try
        {
            var table = await api.GetSeasonStandingsAsync(season);
            _seasonStandings[season] = table;
            _seasonStandingsFailedAt.Remove(season);
        }
        catch (Exception ex)
        {
            // A 404 is a season the server does not consider ended — the calendar we hold is
            // ahead of it, which can only happen across a boundary. Either way the page says it
            // could not load, and a repaint after SeasonRetryAfter asks again.
            _seasonStandingsFailedAt[season] = DateTime.UtcNow;
            DiagnosticLog.Write($"Season {season} standings: fetch failed: {ex.Message}");
        }
        finally
        {
            _seasonStandingsInFlight.Remove(season);
        }

        if (_activeSubtab == Subtab.Ranking && _rankingSeason == season) RenderRanking();
    }

    // ------------------------------------------------------------------ ranking: the selector

    /// <summary>
    /// Fills the season selector beside the 1v1 / Teams capsule, or hides it.
    ///
    /// <para>Hidden on an older backend and while there is only ONE season: a choice with one
    /// option is not a choice, and the subtitle already names the running season. The items are
    /// rebuilt only when the list or the language changed — rebuilding them on every repaint
    /// would replace the item under the pointer while the list is open.</para>
    /// </summary>
    private void RenderRankingSeasonSelector()
    {
        var combo = RankingSeasonCombo;
        if (combo == null) return;

        var season = _communityStats?.Season;
        if (!SeasonView.OffersAChoice(season))
        {
            combo.Visibility = Visibility.Collapsed;
            return;
        }

        var entries = SeasonView.SelectorEntries(season);
        var current = season!.Current;
        var selected = SeasonView.PastSeasonOrNull(_rankingSeason, season) ?? current;
        var signature = Strings.Language + "|" + current + "|"
                        + string.Join(",", entries.Select(e => e.Number));

        _suppressSeasonSelection = true;
        try
        {
            if (signature != _seasonComboSignature)
            {
                _seasonComboSignature = signature;
                combo.Items.Clear();
                foreach (var entry in entries)
                {
                    combo.Items.Add(new ComboBoxItem
                    {
                        Content = entry.Number == current
                            ? Strings.Format("MpSeasonCurrentItem", entry.Number)
                            : Strings.Format("MpSeasonName", entry.Number),
                        Tag = entry.Number,
                    });
                }
            }

            foreach (var item in combo.Items.OfType<ComboBoxItem>())
            {
                if (item.Tag is int n && n == selected)
                {
                    if (!ReferenceEquals(combo.SelectedItem, item)) combo.SelectedItem = item;
                    break;
                }
            }
        }
        finally
        {
            _suppressSeasonSelection = false;
        }

        combo.ToolTip = TooltipHelper.Wrap(Strings.Get("MpSeasonSelectorTip"));
        combo.Visibility = Visibility.Visible;
    }

    private void RankingSeasonCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSeasonSelection) return;
        if (RankingSeasonCombo?.SelectedItem is not ComboBoxItem { Tag: int picked }) return;

        var current = _communityStats?.Season?.Current ?? 0;
        int? next = picked >= current ? null : picked;
        if (next == _rankingSeason) return;
        _rankingSeason = next;

        // A deliberate pick retries a table that failed a moment ago, rather than making the
        // player wait out the window a repaint would have waited.
        if (next is int past) _seasonStandingsFailedAt.Remove(past);

        // Deferred: this runs inside the combo's own selection change, and RenderRanking touches
        // the combo again on its way through.
        Dispatcher.BeginInvoke(new Action(RenderRanking), System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>
    /// The page's own words for the season on screen, applied after <c>RenderRankingChrome</c>
    /// has written the ordinary ones. The running season adds "Season 2 · until 28 Feb" to the
    /// subtitle; an ended one replaces it, since the count of the live ladder says nothing about
    /// a table that is closed, and hides the time-window chip, which is today's window.
    /// </summary>
    private void ApplySeasonToRankingChrome(int shown)
    {
        // "Sample data", while a preview owns the payload (MultiplayerTab.SeasonPreview.cs).
        ApplyPreviewChip();
        RenderRankingSeasonSelector();

        var season = _communityStats?.Season;
        if (season == null || season.Current < 1) return;

        if (SeasonView.PastSeasonOrNull(_rankingSeason, season) is int past)
        {
            var size = TryGetSeasonTable(past, out var table)
                ? (_rankingShowsTeam ? table.RankedPlayersTeam : table.RankedPlayers)
                : 0;
            var count = size > 0 ? size : shown;
            RankingSubtitleText.Text = (count > 0 ? Strings.Format("MpRankSubtitleFinal", count) + " · " : "")
                                       + Strings.Format("MpSeasonFinal", past);
            RankingScopeWindowChip.Visibility = Visibility.Collapsed;
            return;
        }

        RankingSubtitleText.Text += " · " + RunningSeasonLabel(season);
    }

    /// <summary>"Season 2 · until 28 Feb" — the last day in the viewer's own time zone.</summary>
    private static string RunningSeasonLabel(SeasonInfo season)
        => SeasonView.LastLocalDay(season.EndsAt) is DateTime day
            ? Strings.Format("MpSeasonUntil", season.Current, day.ToString("d MMM", Strings.Culture))
            : Strings.Format("MpSeasonName", season.Current);

    /// <summary>
    /// The Ranking subtab, on an ended season's table when one is named — for the bell's "Season
    /// 1 is over" item, whose point is the table it is about. A season the calendar does not list
    /// as ended (not loaded yet) is still remembered, and becomes the table on screen as soon as
    /// the calendar arrives.
    /// </summary>
    internal void ShowSeasonRanking(int? season)
    {
        _rankingSeason = season;
        if (season is int past) _seasonStandingsFailedAt.Remove(past);
        ShowRanking();
    }

    // ------------------------------------------------------------------ the bell

    /// <summary>
    /// Asks <see cref="SeasonNotice.Plan"/> whether a season has ended since the launcher last
    /// looked, and acts on the answer. Called when the community payload lands and when a
    /// standing lands — the two halves the decision needs arrive separately.
    /// </summary>
    private void MaybeAnnounceSeasonChange()
    {
        // The previews own the payload while they are up, and a fabricated calendar must never
        // move the latch the real one depends on.
        if (_config == null || _demoStats || _demoTournaments) return;

        try
        {
            var plan = SeasonNotice.Plan(_config.LastSeenSeason, _communityStats?.Season, _cachedStanding);
            switch (plan.Step)
            {
                case SeasonNoticeStep.Seed:
                    _config.LastSeenSeason = plan.Current;
                    _config.Save();
                    DiagnosticLog.Write($"Seasons: baseline recorded at season {plan.Current}.");
                    break;

                case SeasonNoticeStep.NeedStanding:
                    // The standing in hand still describes the season that just ended — and so
                    // does everything drawn from it: the chip, the profile, my own badge. Fetch it
                    // again ONCE per boundary; the plan runs again when it lands.
                    if (_seasonStandingReloadFor != plan.Current)
                    {
                        _seasonStandingReloadFor = plan.Current;
                        _cachedStanding = null;
                        DiagnosticLog.Write($"Seasons: season {plan.Ended} ended; reloading the standing.");
                        _ = LoadStandingAsync();
                    }
                    break;

                case SeasonNoticeStep.Ring:
                    _config.LastSeenSeason = plan.Current;
                    _config.Save();
                    DiagnosticLog.Write(
                        $"Seasons: season {plan.Ended} ended; announcing ({plan.Places.Count} place(s)).");
                    SeasonEnded?.Invoke(plan);
                    break;
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Seasons: notice failed: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------ the medal

    /// <summary>
    /// A season medal for a name, or null when the player has none — the caller then reserves
    /// no width for it. The medal is the SERVER's choice among a player's titles.
    /// </summary>
    private static FrameworkElement? BuildSeasonMedal(SeasonTitleInfo? title, double size)
    {
        var medal = SeasonTitleBadge.Build(title, size);
        if (medal != null) medal.Margin = new Thickness(MedalGap, 0, 0, 0);
        return medal;
    }

    /// <summary>The width a medal takes beside a name, gap included; 0 without one.</summary>
    private static double MedalFootprint(SeasonTitleInfo? title, double size)
        => SeasonView.IsDrawable(title) ? size + MedalGap : 0;

    /// <summary>
    /// A season title out of a socket frame, never throwing: a malformed or absent field reads as
    /// "no medal", which is what an older backend sends anyway.
    /// </summary>
    private static SeasonTitleInfo? ReadSeasonTitle(JsonElement frame, string property)
    {
        try
        {
            if (!frame.TryGetProperty(property, out var el) || el.ValueKind != JsonValueKind.Object)
                return null;
            var title = JsonSerializer.Deserialize<SeasonTitleInfo>(el.GetRawText());
            return SeasonView.IsDrawable(title) ? title : null;
        }
        catch
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ the profile

    /// <summary>"RATING 1v1 · SEASON 2" — the big number is the running season's.</summary>
    private string ProfileRatingLabel()
        => _cachedStanding?.Season is int s && s >= 1
            ? Strings.Format("MpProfileRatingLabelSeason", s)
            : Strings.Get("MpProfileRatingLabel");

    private string ProfileCurveTitle()
        => _cachedStanding?.Season is int s && s >= 1
            ? Strings.Format("MpProfileCurveTitleSeason", s)
            : Strings.Get("MpProfileCurveTitle");

    private string ProfileRecordTitle()
        => _cachedStanding?.Season is int s && s >= 1
            ? Strings.Format("MpProfileRecordTitleSeason", s)
            : Strings.Get("MpProfileRecordTitle");

    /// <summary>
    /// The history the rating curve is drawn from: the running season's matches only, because a
    /// curve drawn across a reset shows a fall nobody suffered. Everything, on an older backend.
    /// </summary>
    private IReadOnlyList<MatchHistoryRow>? SeasonHistoryRows()
        => SeasonView.RowsOfSeason(_historyRows, _cachedStanding?.Season);

    /// <summary>
    /// The SEASONS card: where the player finished every ended season he was on a table in — the
    /// medal for a top-3 place, the age his badge had at the end, the place out of how many, his
    /// final rating and his record in it.
    ///
    /// <para>Null — no card at all — when there is nothing to list: an older backend, or a player
    /// who has not finished a season on a table. A card that only said "nothing yet" would be
    /// every player's card for the whole first season.</para>
    /// </summary>
    private UIElement? BuildProfileSeasons()
    {
        var lines = SeasonView.ProfileLines(_cachedStanding);
        if (lines.Count == 0) return null;

        var card = BuildProfileCard(Strings.Get("MpProfileSeasonsTitle"));
        var stack = (StackPanel)card.Child;
        card.Margin = new Thickness(0, 11, 0, 0);
        card.Tag = "ProfileSeasonsCard";

        var required = CommunityStatsView.RequiredDecided(_communityStats);
        for (var i = 0; i < lines.Count; i++)
            stack.Children.Add(BuildProfileSeasonLine(lines[i], required, first: i == 0));

        stack.Children.Add(new TextBlock
        {
            Text = Strings.Get("MpProfileSeasonsHint"),
            Foreground = (Brush)Application.Current.FindResource("MpTextFaint"),
            FontSize = (double)Application.Current.FindResource("MpMicroSize"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
        });
        return card;
    }

    private UIElement BuildProfileSeasonLine(PastSeasonEntry line, int? required, bool first)
    {
        var grid = new Grid { Margin = new Thickness(0, first ? 10 : 8, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // The age his badge had when the season closed — cut by THAT table's size, which is the
        // only size it was ever drawn against.
        var age = RankAges.For(line.Place, line.Size);
        var shown = new ShownBadge(line.IsTeam ? BadgeKind.Team : BadgeKind.Solo, age, line.Place, null, 0);
        var badge = RankBadge.BuildFor(shown, 22, $"season-{line.Season}-{line.Mode}",
            RankBadge.TooltipFor(age, line.Place, required));
        badge.Margin = new Thickness(0, 0, 10, 0);
        badge.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(WithColumn(badge, 0));

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(new TextBlock
        {
            Text = Strings.Format("MpProfileSeasonLine", line.Season, SeasonTitleBadge.ModeWord(line.IsTeam)),
            Foreground = (Brush)Application.Current.FindResource("MpTextPrimary"),
            FontSize = (double)Application.Current.FindResource("MpBodySize"),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (BuildSeasonMedal(
                new SeasonTitleInfo { Season = line.Season, Place = line.Place, Mode = line.Mode },
                RosterMedalSize) is { } medal)
        {
            head.Children.Add(medal);
        }
        text.Children.Add(head);

        var detail = new List<string> { Strings.Get(RankAges.NameKey(age)) };
        detail.Add(Strings.Format("MpRoomMemberElo", (int)Math.Round(line.Rating)));
        if (line.Wins + line.Losses > 0)
            detail.Add(Strings.Format("MpRankRecordValue", line.Wins, line.Losses));
        text.Children.Add(new TextBlock
        {
            Text = string.Join(" · ", detail),
            Margin = new Thickness(0, 2, 0, 0),
            Foreground = (Brush)Application.Current.FindResource("MpTextMuted"),
            FontSize = (double)Application.Current.FindResource("MpMetaSize"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        grid.Children.Add(WithColumn(text, 1));

        var place = new TextBlock
        {
            Text = Strings.Format("MpProfileSeasonPlace", line.Place, line.Size),
            FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"),
            FontSize = (double)Application.Current.FindResource("MpBodySize"),
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.FindResource("MpTextHeading"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        };
        grid.Children.Add(WithColumn(place, 2));
        return grid;
    }
}
