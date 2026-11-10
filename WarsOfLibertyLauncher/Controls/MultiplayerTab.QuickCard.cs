using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// The quick match card above the rooms list (design 86, PartidaRapidaTarjeta): one 1v1 search of
/// the active mod, said in words before the click. It replaced the small "Find a 1v1 match"
/// button in the list's header, whose logic (<c>MultiplayerTab.QuickMatch.cs</c>) it drives.
/// </summary>
public partial class MultiplayerTab
{
    /// <summary>What the card last drew, so a repaint with the same words rebuilds nothing.</summary>
    private string? _quickCardKey;

    /// <summary>The spinner's and the pulse's clocks: stopped through their controller when the
    /// card is rebuilt or hidden, never merely detached (the animation-clock rule in CLAUDE.md).</summary>
    private readonly System.Collections.Generic.List<AnimationClock> _quickCardClocks = new();

    private void StopQuickCardClocks()
    {
        foreach (var c in _quickCardClocks) c.Controller?.Stop();
        _quickCardClocks.Clear();
    }

    private bool _quickCardClocksHooked;

    /// <summary>The marks move only while somebody can see them, the badges' own rule: paused
    /// while the launcher is behind another program, the card is hidden (another tab, the tray) or
    /// the window is minimised, which keeps IsVisible true.</summary>
    private void SyncQuickCardClocks()
    {
        if (!_quickCardClocksHooked && QuickCardHost != null && Window.GetWindow(this) != null)
        {
            _quickCardClocksHooked = true;
            QuickCardHost.IsVisibleChanged += (_, _) => SyncQuickCardClocks();
            // Foreground, and minimise and restore: one event for both.
            RankBadge.RunConditionsChanged += SyncQuickCardClocks;
        }
        var run = RankBadge.MayRunOn(QuickCardHost);
        foreach (var c in _quickCardClocks)
        {
            if (run) c.Controller?.Resume();
            else c.Controller?.Pause();
        }
    }

    /// <summary>The card's clock, updated in place every second while waiting.</summary>
    private TextBlock? _quickCardClock;

    /// <summary>The compact card (40-px icon, one line) below this window height, as the design.</summary>
    private const double QuickCardCompactBelow = 900;

    private bool QuickCardCompact
        => Window.GetWindow(this) is { } w && w.ActualHeight > 0 && w.ActualHeight < QuickCardCompactBelow;

    /// <summary>Where the page is, as the card sees it.</summary>
    private QuickCardFacts QuickCardFactsNow()
    {
        var profile = _getActiveProfile?.Invoke();
        var mod = profile?.DisplayName ?? "";
        var compact = QuickCardCompact;
        if (IsUpdateGated) return new QuickCardFacts(QuickCardState.Outdated, QuickCardWait.Unknown, mod, compact);
        if (profile == null || !RoomModInstalled(profile.Id))
            return new QuickCardFacts(QuickCardState.NoMod, QuickCardWait.Unknown, mod, compact);
        if (_qmStage == QuickMatchStage.Idle && _qmUnrankedMods.Contains(profile.Id)
            && string.IsNullOrEmpty(_session?.CurrentLobbyId))
            return new QuickCardFacts(QuickCardState.NotRanked, QuickCardWait.Unknown, mod, compact);

        switch (_qmStage)
        {
            case QuickMatchStage.Looking:
                return new QuickCardFacts(QuickCardState.Looking, QuickCardWait.Unknown, mod, compact);
            case QuickMatchStage.Waiting:
                if (RoomPlayerCount() >= 2)
                {
                    var myId = _session?.CurrentUser?.Id;
                    var rival = _roomMembers.Values.FirstOrDefault(m => m.UserId != myId
                        && !string.Equals(m.Role, "spectator", StringComparison.Ordinal))?.Login;
                    return new QuickCardFacts(QuickCardState.Found, QuickCardWait.Unknown, mod, compact,
                        Rival: rival, Hosting: _isHostInCurrentRoom);
                }
                return new QuickCardFacts(QuickCardState.Waiting, QuickCardWait.Unknown, mod, compact);
        }

        if (!_demoRooms && _session?.CurrentLobbyId is { } lobbyId)
        {
            // The room the search found: "You play X", not "leave it to search".
            if (string.Equals(lobbyId, _qmFoundLobbyId, StringComparison.Ordinal) && RoomPlayerCount() >= 2)
                return new QuickCardFacts(QuickCardState.Found, QuickCardWait.Unknown, mod, compact,
                    Rival: _qmFoundRival, Hosting: _qmFoundHosting);
            // The room's name, never its code: a room just created is not in the list yet.
            var name = _lastBrowserList?.FirstOrDefault(l => l.Id == lobbyId)?.Title
                       ?? (string.IsNullOrEmpty(_roomTitle) ? lobbyId : _roomTitle);
            return new QuickCardFacts(QuickCardState.InOtherRoom, QuickCardWait.Unknown, mod, compact, RoomName: name);
        }

        string? peak = null;
        if (TryLocalPeak(_communityStats, out _, out var peakStart))
            peak = Strings.Format("QcPeakRange",
                Strings.Format("MpActivityPeakHour", peakStart),
                Strings.Format("MpActivityPeakHour", (peakStart + CommunityStatsView.PeakWindowHours) % 24));

        return QuickCardReadyFacts(profile.Id, mod, compact, peak);
    }

    /// <summary>The Ready state: who is waiting, worked out from the list already on screen.</summary>
    private QuickCardFacts QuickCardReadyFacts(string modId, string mod, bool compact, string? peak)
    {
        var rooms = _lastBrowserList;
        var myId = _session?.CurrentUser?.Id;
        // My copy's fingerprint, from the same per-mod cache the rows use. The preview stands in
        // for it: the sample's rooms match except the one marked as another version.
        var hash = _demoRooms ? RoomListDemoData.MyHash : KnownModHash(modId);
        if (rooms == null || hash == null)
            return new QuickCardFacts(QuickCardState.Ready, QuickCardWait.Unknown, mod, compact, Peak: peak);

        var join = QuickMatchPlanner.RoomsToJoin(rooms, myId, modId, hash, _cachedStanding?.Rating);
        if (join.Count > 0)
        {
            var room = join[0];
            var created = RoomAgeFormat.ParseCreatedUtc(room.CreatedAt);
            var minutes = created is DateTime c ? Math.Max(1, (int)(DateTime.UtcNow - c).TotalMinutes) : 1;
            return new QuickCardFacts(QuickCardState.Ready, QuickCardWait.Room, mod, compact,
                Host: RoomHostName(room), Minutes: minutes, Peak: peak);
        }
        var other = QuickMatchPlanner.OtherVersionCount(rooms, myId, modId, hash);
        return other > 0
            ? new QuickCardFacts(QuickCardState.Ready, QuickCardWait.OtherVersion, mod, compact, OtherRooms: other, Peak: peak)
            : new QuickCardFacts(QuickCardState.Ready, QuickCardWait.Nobody, mod, compact, Peak: peak);
    }

    /// <summary>Paint the card: hidden offline (the page says it needs internet), otherwise its state.</summary>
    private void RenderQuickCard()
    {
        if (QuickCardHost == null) return;
        var offline = _offlineMode || (_demoRooms && _demoRoomsScenario == RoomListDemoData.Scenario.Offline);
        var signedIn = _demoRooms || _session?.Status == MultiplayerSession.SessionStatus.SignedIn;
        if (offline || !signedIn)
        {
            StopQuickCardClocks();
            _quickCardKey = null;
            QuickCardHost.Child = null;
            if (QuickCardHost.Visibility != Visibility.Collapsed)
            {
                QuickCardHost.Visibility = Visibility.Collapsed;
                QueueActivityLayout();
            }
            return;
        }

        var facts = QuickCardFactsNow();
        var model = QuickCardView.Of(facts);
        var version = _getActiveProfile?.Invoke() is { } p ? Cfg().GetState(p.Id).LastKnownVersion : null;
        var key = string.Join("|", facts.Compact, model.Kicker, model.Mark, model.Title, model.Line, model.Foot,
            model.ShowVersion, version, string.Join(",", model.Buttons.Select(b => b.Action + ":" + b.Primary)), Strings.Language);
        if (QuickCardHost.Visibility != Visibility.Visible)
        {
            QuickCardHost.Visibility = Visibility.Visible;
            QueueActivityLayout();
        }
        if (key == _quickCardKey)
        {
            UpdateQuickCardClock();
            return;
        }
        _quickCardKey = key;
        StopQuickCardClocks();
        QuickCardHost.Child = BuildQuickCard(model, facts.Compact, string.IsNullOrWhiteSpace(version) ? null : version);
        UpdateQuickCardClock();
        SyncQuickCardClocks();
    }

    private void UpdateQuickCardClock()
    {
        if (_quickCardClock != null)
            _quickCardClock.Text = QuickMatchPlanner.Elapsed(DateTime.UtcNow - _qmStartedUtc);
    }

    private FrameworkElement BuildQuickCard(QuickCardModel m, bool compact, string? version)
    {
        _quickCardClock = null;
        var grid = new Grid { Margin = compact ? new Thickness(13, 9, 11, 9) : new Thickness(21, 17, 21, 17) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(compact ? 40 : 56) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var profile = _getActiveProfile?.Invoke();
        var icon = new Border
        {
            Width = compact ? 40 : 56,
            Height = compact ? 40 : 56,
            CornerRadius = new CornerRadius(compact ? 10 : 12),
            BorderBrush = Rm("RmChipRim"),
            BorderThickness = new Thickness(1),
            Background = ResolveRoomModIcon(profile) ?? Rm("MpField"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        grid.Children.Add(icon);

        var text = new StackPanel { Margin = new Thickness(compact ? 14 : 20, 0, compact ? 14 : 20, 0), VerticalAlignment = VerticalAlignment.Center };
        if (m.Kicker != null)
        {
            var kick = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            if (BuildQuickCardMark(m.Mark) is { } mark) kick.Children.Add(mark);
            var k = new TextBlock { Text = m.Kicker, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
            k.SetResourceReference(TextBlock.FontSizeProperty, "RmChipSize");
            if (m.KickerGreen) k.Foreground = Rm("RmTextGreen");
            else k.SetResourceReference(TextBlock.ForegroundProperty, "ModAccentText");
            kick.Children.Add(k);
            text.Children.Add(kick);
        }

        var titleRow = new WrapPanel { Orientation = Orientation.Horizontal };
        if (m.Kicker == null && BuildQuickCardMark(m.Mark) is { } compactMark) titleRow.Children.Add(compactMark);
        var title = new TextBlock
        {
            Text = m.Title,
            Foreground = Rm("RmTextStrong"),
            FontWeight = FontWeights.Bold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        title.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        title.SetResourceReference(TextBlock.FontSizeProperty, compact ? "QcTitleSizeCompact" : "QcTitleSize");
        titleRow.Children.Add(title);
        if (m.ShowClock)
        {
            _quickCardClock = new TextBlock { FontFamily = MonoFont, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            _quickCardClock.SetResourceReference(TextBlock.FontSizeProperty, "QcClockSize");
            _quickCardClock.SetResourceReference(TextBlock.ForegroundProperty, "ModAccentText");
            titleRow.Children.Add(_quickCardClock);
        }
        text.Children.Add(titleRow);

        var line = new TextBlock
        {
            Text = m.Line,
            Foreground = Rm("MpTextSecondary"),
            Margin = new Thickness(0, compact ? 4 : 6, 0, 0),
            TextWrapping = compact ? TextWrapping.NoWrap : TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 760,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        line.SetResourceReference(TextBlock.FontSizeProperty, compact ? "RmHostSize" : "QcLineSize");
        text.Children.Add(line);

        if (m.Foot != null || (m.ShowVersion && version != null))
        {
            var foot = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            if (m.ShowVersion && version != null) foot.Children.Add(BuildVersionChip(version));
            if (m.Foot != null)
            {
                var f = new TextBlock { Text = m.Foot, Foreground = Rm("RmTextLabel"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
                f.SetResourceReference(TextBlock.FontSizeProperty, "RmButtonSize");
                foot.Children.Add(f);
            }
            text.Children.Add(foot);
        }
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var b in m.Buttons)
        {
            var btn = new Button
            {
                Style = (Style)FindResource(b.Primary ? "QcButtonPrimary" : "QcButton"),
                Content = b.Caption,
                Height = compact ? 40 : 52,
                Padding = new Thickness(b.Primary ? (compact ? 18 : 24) : (compact ? 16 : 20), 0, b.Primary ? (compact ? 18 : 24) : (compact ? 16 : 20), 0),
                Margin = new Thickness(buttons.Children.Count > 0 ? 10 : 0, 0, 0, 0),
            };
            btn.SetResourceReference(Control.FontSizeProperty, b.Primary
                ? (compact ? "QcButtonSizeCompact" : "QcButtonSize")
                : (compact ? "QcButton2SizeCompact" : "QcButton2Size"));
            AutomationProperties.SetAutomationId(btn, "QuickCard:" + b.Action);
            AutomationProperties.SetName(btn, b.Caption);
            var action = b.Action;
            btn.Click += async (_, _) => await OnQuickCardAction(action);
            buttons.Children.Add(btn);
        }
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);
        return grid;
    }

    private static Border BuildVersionChip(string version)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M1,1 H6 L11,6 L6,11 L1,6 Z"),
            Fill = new SolidColorBrush(Color.FromRgb(0x7E, 0xE0, 0x8B)),
            Width = 11,
            Height = 11,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        var t = new TextBlock { Text = version, FontFamily = MonoFont, FontWeight = FontWeights.SemiBold, Foreground = Rm("RmTextHost"), VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.FontSizeProperty, "RmChipSize");
        row.Children.Add(t);
        return new Border
        {
            Height = 24,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(9, 0, 9, 0),
            Margin = new Thickness(0, 0, 10, 0),
            Background = Rm("RmChipBg"),
            BorderBrush = Rm("RmButtonRim"),
            BorderThickness = new Thickness(1),
            Child = row,
        };
    }

    /// <summary>The spinner, pulse or green dot. Their clocks are finite-free forever animations,
    /// so they are skipped entirely with reduced effects (the mark is drawn still).</summary>
    private FrameworkElement? BuildQuickCardMark(QuickCardMark mark)
    {
        if (mark == QuickCardMark.None) return null;
        var still = !RankBadge.AnimationsEnabled;
        FrameworkElement el;
        if (mark == QuickCardMark.Spinner)
        {
            var ring = new System.Windows.Shapes.Ellipse
            {
                Width = 12,
                Height = 12,
                StrokeThickness = 2,
                Stroke = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)),
            };
            var arc = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M6,1 A5,5 0 0 1 11,6"),
                StrokeThickness = 2,
                Width = 12,
                Height = 12,
                RenderTransformOrigin = new Point(0.5, 0.5),
            };
            arc.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "ModAccentText");
            var host = new Grid { Width = 12, Height = 12 };
            host.Children.Add(ring);
            host.Children.Add(arc);
            if (!still)
            {
                var rot = new RotateTransform();
                arc.RenderTransform = rot;
                var spin = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever };
                Timeline.SetDesiredFrameRate(spin, RankBadge.FrameRate);
                var clock = spin.CreateClock();
                rot.ApplyAnimationClock(RotateTransform.AngleProperty, clock);
                _quickCardClocks.Add(clock);
            }
            el = host;
        }
        else
        {
            var dot = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8 };
            if (mark == QuickCardMark.Found) dot.Fill = Rm("RmDotGreen");
            else dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "ModAccentText");
            if (mark == QuickCardMark.Pulse && !still)
            {
                var pulse = new DoubleAnimation(1, 0.35, TimeSpan.FromSeconds(0.8)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                Timeline.SetDesiredFrameRate(pulse, RankBadge.FrameRate);
                var clock = pulse.CreateClock();
                dot.ApplyAnimationClock(OpacityProperty, clock);
                _quickCardClocks.Add(clock);
            }
            el = dot;
        }
        el.Margin = new Thickness(0, 0, 8, 0);
        el.VerticalAlignment = VerticalAlignment.Center;
        return el;
    }

    private async System.Threading.Tasks.Task OnQuickCardAction(QuickCardAction action)
    {
        if (_demoRooms)
        {
            DemoRoomsSay(Strings.Get(action switch
            {
                QuickCardAction.Search => "QmFindMatch",
                QuickCardAction.Cancel => "QcCancel",
                QuickCardAction.ViewRoom => "QcViewRoom",
                QuickCardAction.Stop => "QcStop",
                QuickCardAction.Update => "QcUpdate",
                QuickCardAction.Library => "QcLibrary",
                _ => "QcLeaveAndSearch",
            }));
            return;
        }
        switch (action)
        {
            case QuickCardAction.Search:
            case QuickCardAction.LeaveAndSearch:
                await StartQuickMatchAsync();
                break;
            case QuickCardAction.Cancel:
            case QuickCardAction.Stop:
                await CancelQuickMatchAsync(Strings.Get("QmCancelled"));
                break;
            case QuickCardAction.ViewRoom:
                OpenLobbyWindow();
                break;
            case QuickCardAction.Update:
                _onUpdateRequested?.Invoke();
                break;
            case QuickCardAction.Library:
                if (_getActiveProfile?.Invoke() is { } p) _openModInLibrary?.Invoke(p);
                break;
        }
    }
}
