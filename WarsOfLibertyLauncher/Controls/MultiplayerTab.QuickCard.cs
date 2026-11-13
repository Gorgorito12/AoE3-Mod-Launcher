using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
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

    /// <summary>
    /// The mod the bar searches: the one chosen in it while it is still installed, else the
    /// Library's active mod. An empty choice FOLLOWS the Library (choosing the active mod writes
    /// empty), so somebody who never strays keeps following it.
    /// </summary>
    internal ModProfile? QuickMatchProfile()
    {
        var chosen = Cfg().QuickMatchModId;
        if (!string.IsNullOrEmpty(chosen) && ModRegistry.Find(chosen) is { } picked && RoomModInstalled(picked.Id))
            return picked;
        return _getActiveProfile?.Invoke();
    }

    /// <summary>What a search would look for: the saved choice, forced to Casual for a mod the
    /// server has refused a ranked room this session.</summary>
    internal QuickMatchKind QuickMatchKindNow(ModProfile? profile)
    {
        if (profile != null && _qmUnrankedMods.Contains(profile.Id)) return QuickMatchKind.Casual;
        return Cfg().QuickMatchKind switch
        {
            "casual" => QuickMatchKind.Casual,
            "either" => QuickMatchKind.Either,
            _ => QuickMatchKind.Ranked,
        };
    }

    private static string KindToken(QuickMatchKind kind) => kind switch
    {
        QuickMatchKind.Casual => "casual",
        QuickMatchKind.Either => "either",
        _ => "ranked",
    };

    /// <summary>The mods the bar can search in: those installed here, by name.</summary>
    private System.Collections.Generic.List<ModProfile> QuickMatchInstalledMods()
        => ModRegistry.All.Where(p => RoomModInstalled(p.Id))
            .OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>My copy's fingerprint only if it is already known: the notice about another mod never
    /// reads an install to answer (the rooms of that mod in the list read it for their own rows).</summary>
    private string? PeekModHash(string modId)
    {
        if (_demoRooms) return RoomListDemoData.MyHash;
        var profile = ModRegistry.Find(modId);
        if (profile == null) return null;
        string path;
        try { path = GetInstallPath(profile) ?? ""; }
        catch { return null; }
        return _roomModHashes.TryGetValue(modId, out var known) && string.Equals(known.Path, path, StringComparison.OrdinalIgnoreCase)
            ? known.Hash
            : null;
    }

    private void SaveQuickMatchChoice(string? modId, QuickMatchKind? kind)
    {
        var config = Cfg();
        if (modId != null)
            config.QuickMatchModId = string.Equals(modId, _getActiveProfile?.Invoke()?.Id, StringComparison.OrdinalIgnoreCase) ? "" : modId;
        if (kind is { } k) config.QuickMatchKind = KindToken(k);
        try { config.Save(); }
        catch (Exception ex) { DiagnosticLog.Write($"QuickMatch: saving the bar's choice: {ex.Message}"); }
        _quickCardKey = null;
        RenderQuickCard();
    }

    /// <summary>Where the page is, as the bar sees it.</summary>
    private QuickCardFacts QuickCardFactsNow()
    {
        var profile = QuickMatchProfile();
        var mod = profile?.DisplayName ?? "";
        var kind = QuickMatchKindNow(profile);
        if (IsUpdateGated) return new QuickCardFacts(QuickCardState.Outdated, QuickCardWait.Unknown, mod, kind);
        if (profile == null || !RoomModInstalled(profile.Id))
            return new QuickCardFacts(QuickCardState.NoMod, QuickCardWait.Unknown, mod, kind);

        switch (_qmStage)
        {
            case QuickMatchStage.Looking:
                return new QuickCardFacts(QuickCardState.Looking, QuickCardWait.Unknown, _qmModName ?? mod, _qmKind);
            case QuickMatchStage.Queued:
                return new QuickCardFacts(QuickCardState.Queued, QuickCardWait.Unknown, _qmModName ?? mod, _qmKind,
                    Searching: CompatibleSearchers(_qmModId ?? "", _qmModHash, _qmKind).Count);
            case QuickMatchStage.Waiting:
                if (RoomPlayerCount() >= 2)
                {
                    var myId = _session?.CurrentUser?.Id;
                    var rival = _roomMembers.Values.FirstOrDefault(m => m.UserId != myId
                        && !string.Equals(m.Role, "spectator", StringComparison.Ordinal))?.Login;
                    return new QuickCardFacts(QuickCardState.Found, QuickCardWait.Unknown, _qmModName ?? mod, _qmKind,
                        Rival: rival, Hosting: _isHostInCurrentRoom);
                }
                return new QuickCardFacts(QuickCardState.Waiting, QuickCardWait.Unknown, _qmModName ?? mod, _qmKind);
        }

        if (!_demoRooms && _session?.CurrentLobbyId is { } lobbyId)
        {
            // The room the search found: "You play X", not "leave it to search".
            if (string.Equals(lobbyId, _qmFoundLobbyId, StringComparison.Ordinal) && RoomPlayerCount() >= 2)
                return new QuickCardFacts(QuickCardState.Found, QuickCardWait.Unknown, mod, kind,
                    Rival: _qmFoundRival, Hosting: _qmFoundHosting);
            // The room's name, never its code: a room just created is not in the list yet.
            var name = _lastBrowserList?.FirstOrDefault(l => l.Id == lobbyId)?.Title
                       ?? (string.IsNullOrEmpty(_roomTitle) ? lobbyId : _roomTitle);
            return new QuickCardFacts(QuickCardState.InOtherRoom, QuickCardWait.Unknown, mod, kind, RoomName: name);
        }

        string? peak = null;
        if (TryLocalPeak(_communityStats, out _, out var peakStart))
            peak = Strings.Format("QcPeakRange",
                Strings.Format("MpActivityPeakHour", peakStart),
                Strings.Format("MpActivityPeakHour", (peakStart + CommunityStatsView.PeakWindowHours) % 24));

        var facts = QuickCardReadyFacts(profile.Id, mod, kind, peak);
        return _qmUnrankedMods.Contains(profile.Id) ? facts with { State = QuickCardState.NotRanked } : facts;
    }

    /// <summary>The Ready state: who is waiting, worked out from the list already on screen.</summary>
    private QuickCardFacts QuickCardReadyFacts(string modId, string mod, QuickMatchKind kind, string? peak)
    {
        var rooms = _lastBrowserList;
        var myId = _session?.CurrentUser?.Id;
        // My copy's fingerprint, from the same per-mod cache the rows use. The preview stands in
        // for it: the sample's rooms match except the one marked as another version.
        var hash = _demoRooms ? RoomListDemoData.MyHash : KnownModHash(modId);
        if (rooms == null || hash == null)
            return new QuickCardFacts(QuickCardState.Ready, QuickCardWait.Unknown, mod, kind, Peak: peak);

        // Somebody in the server's queue comes first: a search is matched with him at once.
        var searchers = CompatibleSearchers(modId, hash, kind);
        if (searchers.Count > 0)
        {
            var first = searchers[0];
            return new QuickCardFacts(QuickCardState.Ready, QuickCardWait.Searcher, mod, kind,
                Host: string.IsNullOrWhiteSpace(first.DisplayName) ? "—" : first.DisplayName,
                Searching: searchers.Count, Peak: peak);
        }

        var join = QuickMatchPlanner.RoomsToJoin(rooms, myId, modId, hash, _cachedStanding?.Rating, kind);
        if (join.Count > 0)
        {
            var room = join[0];
            var created = RoomAgeFormat.ParseCreatedUtc(room.CreatedAt);
            var minutes = created is DateTime c ? Math.Max(1, (int)(DateTime.UtcNow - c).TotalMinutes) : 1;
            return new QuickCardFacts(QuickCardState.Ready, QuickCardWait.Room, mod, kind,
                Host: RoomHostName(room), Minutes: minutes, RoomCasual: !room.Competitive, Peak: peak);
        }

        // Nobody here: is somebody waiting in another mod this player has?
        var elsewhereId = QuickMatchPlanner.WaitingElsewhere(rooms, myId,
            QuickMatchInstalledMods().Select(p => p.Id), PeekModHash, kind, modId);
        var elsewhere = elsewhereId != null ? ModRegistry.Find(elsewhereId)?.DisplayName : null;
        _quickCardElsewhereId = elsewhere != null ? elsewhereId : null;

        var other = QuickMatchPlanner.OtherVersionCount(rooms, myId, modId, hash, kind);
        return other > 0
            ? new QuickCardFacts(QuickCardState.Ready, QuickCardWait.OtherVersion, mod, kind, OtherRooms: other, Peak: peak, ElsewhereMod: elsewhere)
            : new QuickCardFacts(QuickCardState.Ready, QuickCardWait.Nobody, mod, kind, Peak: peak, ElsewhereMod: elsewhere);
    }

    /// <summary>The mod the "Change" link of the notice moves the bar to.</summary>
    private string? _quickCardElsewhereId;

    /// <summary>Paint the bar: hidden offline (the page says it needs internet), otherwise its state.</summary>
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
        var profile = QuickMatchProfile();
        var version = profile is { } p ? Cfg().GetState(p.Id).LastKnownVersion : null;
        var installed = model.ShowPickers ? string.Join(",", QuickMatchInstalledMods().Select(m => m.Id)) : "";
        var key = string.Join("|", model.Mark, model.Title, model.Line, model.Elsewhere, model.ShowPickers,
            profile?.Id, facts.Kind, facts.State, installed, version,
            string.Join(",", model.Buttons.Select(b => b.Action + ":" + b.Primary)), Strings.Language);
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
        QuickCardHost.Child = BuildQuickCard(model, facts, profile, string.IsNullOrWhiteSpace(version) ? null : version);
        UpdateQuickCardClock();
        SyncQuickCardClocks();
    }

    private void UpdateQuickCardClock()
    {
        if (_quickCardClock != null)
            _quickCardClock.Text = QuickMatchPlanner.Elapsed(DateTime.UtcNow - _qmStartedUtc);
    }

    /// <summary>The bar's height: 36-px icon and button inside 8 px of padding, two lines of text.</summary>
    internal const double QuickBarIconSize = 36;

    private FrameworkElement BuildQuickCard(QuickCardModel m, QuickCardFacts facts, ModProfile? profile, string? version)
    {
        _quickCardClock = null;
        var grid = new Grid { Margin = new Thickness(12, 8, 12, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(QuickBarIconSize) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new Border
        {
            Width = QuickBarIconSize,
            Height = QuickBarIconSize,
            CornerRadius = new CornerRadius(9),
            BorderBrush = Rm("RmChipRim"),
            BorderThickness = new Thickness(1),
            Background = ResolveRoomModIcon(profile) ?? Rm("MpField"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        grid.Children.Add(icon);

        var text = new StackPanel { Margin = new Thickness(12, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        // Line 1: the mark, the title, the clock, then the two pickers. A WrapPanel so a narrow window
        // drops the pickers to a second line instead of putting them over the title.
        var head = new WrapPanel { Orientation = Orientation.Horizontal };
        if (BuildQuickCardMark(m.Mark) is { } mark) head.Children.Add(mark);
        var title = new TextBlock
        {
            Text = m.Title,
            Foreground = Rm("RmTextStrong"),
            FontWeight = FontWeights.Bold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 14, 0),
        };
        title.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        title.SetResourceReference(TextBlock.FontSizeProperty, "QcTitleSizeCompact");
        head.Children.Add(title);
        if (m.ShowClock)
        {
            _quickCardClock = new TextBlock { FontFamily = MonoFont, FontWeight = FontWeights.SemiBold, Margin = new Thickness(-4, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            _quickCardClock.SetResourceReference(TextBlock.FontSizeProperty, "QcClockSize");
            _quickCardClock.SetResourceReference(TextBlock.ForegroundProperty, "ModAccentText");
            head.Children.Add(_quickCardClock);
        }
        if (m.ShowPickers)
        {
            var installed = QuickMatchInstalledMods();
            if (installed.Count > 0 && profile != null)
            {
                var picker = BuildQuickModPicker(profile, installed, version);
                picker.Margin = new Thickness(0, 2, 8, 2);
                head.Children.Add(picker);
            }
            if (profile != null) head.Children.Add(BuildQuickKindSegment(profile, facts.Kind));
        }
        text.Children.Add(head);

        // Line 2: one line of state, cut with an ellipsis; the notice about another mod and its
        // "Change" link sit INSIDE the sentence, right after it (as a column they landed at the far
        // end of the bar, beside the button and away from what they belong to).
        var line = new TextBlock
        {
            Foreground = Rm("MpTextSecondary"),
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 0),
        };
        line.SetResourceReference(TextBlock.FontSizeProperty, "RmHostSize");
        line.Inlines.Add(new System.Windows.Documents.Run(m.Elsewhere == null ? m.Line : m.Line + " " + m.Elsewhere));
        if (m.Elsewhere != null && _quickCardElsewhereId != null)
        {
            var change = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(Strings.Get("QcElsewhereSwitch")))
            {
                TextDecorations = null,
                Foreground = Rm("UiInfoText"),
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            change.MouseEnter += (_, _) => change.TextDecorations = TextDecorations.Underline;
            change.MouseLeave += (_, _) => change.TextDecorations = null;
            AutomationProperties.SetAutomationId(change, "QuickCard:Elsewhere");
            change.Click += async (_, _) => await OnQuickCardAction(QuickCardAction.SwitchMod);
            line.Inlines.Add(new System.Windows.Documents.Run("  "));
            line.Inlines.Add(change);
        }
        text.Children.Add(line);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var b in m.Buttons)
        {
            var btn = new Button
            {
                Style = (Style)FindResource(b.Primary ? "QcButtonPrimary" : "QcButton"),
                Content = b.Caption,
                Height = QuickBarIconSize,
                Padding = new Thickness(b.Primary ? 18 : 14, 0, b.Primary ? 18 : 14, 0),
                Margin = new Thickness(buttons.Children.Count > 0 ? 8 : 0, 0, 0, 0),
            };
            btn.SetResourceReference(Control.FontSizeProperty, b.Primary ? "QcButtonSizeCompact" : "QcButton2SizeCompact");
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

    /// <summary>"[icon] Wars of Liberty ▾": opens the list of installed mods. Choosing one never moves
    /// the Library: the bar keeps its own mod (empty when it is the Library's).</summary>
    private Button BuildQuickModPicker(ModProfile profile, System.Collections.Generic.List<ModProfile> installed, string? version)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var name = new TextBlock
        {
            Text = profile.DisplayName,
            Foreground = Rm("RmTextHost"),
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 200,
            VerticalAlignment = VerticalAlignment.Center,
        };
        name.SetResourceReference(TextBlock.FontSizeProperty, "RmButtonSize");
        row.Children.Add(name);
        row.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M0,0 L4,4 L8,0"),
            Stroke = Rm("RmTextLabel"),
            StrokeThickness = 1.4,
            Width = 8,
            Height = 5,
            Margin = new Thickness(8, 1, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        var button = new Button
        {
            Content = row,
            Height = 28,
            Padding = new Thickness(10, 0, 10, 0),
            ToolTip = TooltipHelper.Wrap(version != null
                ? Strings.Format("QcModPickerTip", version)
                : Strings.Get("QcModPickerTipNoVersion")),
        };
        button.SetResourceReference(StyleProperty, "RmRowButton");
        button.HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetAutomationId(button, "QuickCard:Mod");
        AutomationProperties.SetName(button, profile.DisplayName);
        button.Click += (_, _) =>
        {
            var popup = ModSearchPopup.Build(button, installed, _ => true, profile,
                p => ResolveRoomModIcon(p),
                p => ModTheme.AccentOf(p.AccentColor, Cfg().UseModColor),
                picked => SaveQuickMatchChoice(picked.Id, null));
            if (popup.Child is Border { Child: FrameworkElement body }) body.Width = Math.Max(body.Width, 300);
            ChromePopups.Track(popup, button);
            popup.IsOpen = true;
        };
        return button;
    }

    /// <summary>Rated | Casual | Either. A mod with no ladder offers Casual alone, saying why.</summary>
    private FrameworkElement BuildQuickKindSegment(ModProfile profile, QuickMatchKind kind)
    {
        var segment = new SegmentRow { Gap = 4, SegmentHeight = 28, Margin = new Thickness(0, 2, 0, 2) };
        segment.SetOptions(new[]
        {
            new SegmentOption(QuickMatchKind.Ranked, Strings.Get("QcSegRanked")),
            new SegmentOption(QuickMatchKind.Casual, Strings.Get("QcSegCasual")),
            new SegmentOption(QuickMatchKind.Either, Strings.Get("QcSegEither")),
        });
        segment.Selected = kind;
        var noLadder = _qmUnrankedMods.Contains(profile.Id);
        // The segment's template ignores Padding and its font is the monospace of "1v1 / 2v2": here
        // the words get the body font and their own margin.
        foreach (var button in segment.Buttons)
        {
            if (button.Content is string caption)
                button.Content = new TextBlock { Text = caption, Margin = new Thickness(11, 0, 11, 0) };
            button.SetResourceReference(Control.FontFamilyProperty, "BodyFont");
            button.SetResourceReference(Control.FontSizeProperty, "RmButtonSize");
        }
        var buttons = segment.Buttons.ToList();
        for (var i = 0; i < buttons.Count; i++)
        {
            var option = (QuickMatchKind)new[] { QuickMatchKind.Ranked, QuickMatchKind.Casual, QuickMatchKind.Either }[i];
            AutomationProperties.SetAutomationId(buttons[i], "QuickCard:Kind:" + KindToken(option));
            if (noLadder && option != QuickMatchKind.Casual)
            {
                buttons[i].IsEnabled = false;
                ToolTipService.SetShowOnDisabled(buttons[i], true);
                buttons[i].ToolTip = TooltipHelper.Wrap(Strings.Format("QcSegNoLadderTip", profile.DisplayName));
            }
            else
            {
                buttons[i].ToolTip = TooltipHelper.Wrap(Strings.Get(option switch
                {
                    QuickMatchKind.Casual => "QcSegCasualTip",
                    QuickMatchKind.Either => "QcSegEitherTip",
                    _ => "QcSegRankedTip",
                }));
            }
        }
        segment.Picked += picked => SaveQuickMatchChoice(null, (QuickMatchKind)picked);
        // The segments size to their words, not to an equal share of a wide line.
        segment.HorizontalAlignment = HorizontalAlignment.Left;
        foreach (var column in segment.ColumnDefinitions) column.Width = GridLength.Auto;
        return segment;
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
                QuickCardAction.SwitchMod => "QcElsewhereSwitch",
                _ => "QcLeaveAndSearch",
            }));
            if (action == QuickCardAction.SwitchMod && _quickCardElsewhereId is { } demoTo) SaveQuickMatchChoice(demoTo, null);
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
                if (QuickMatchProfile() is { } p) _openModInLibrary?.Invoke(p);
                break;
            case QuickCardAction.SwitchMod:
                if (_quickCardElsewhereId is { } to) SaveQuickMatchChoice(to, null);
                break;
        }
    }
}
