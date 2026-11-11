using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// One row of the rooms list, as design 86 draws it: the mod's icon, the room's name over its
/// chips, the host over his rating, the players, the ping to the host (when the server publishes
/// it) and ONE action. What the action is and
/// which heading the row sits under come from <see cref="RoomActionRule"/>, so they cannot
/// disagree.
/// </summary>
public partial class MultiplayerTab
{
    /// <summary>The prototype's 12-px gap between columns, carried by every cell after the first.</summary>
    private const double RoomColumnGap = 12;

    /// <summary>The icon (36) and the gap after it (12): what the ROOM heading is pushed right by,
    /// since the icon has no heading of its own.</summary>
    internal const double RoomIconInset = 48;

    private static Brush Rm(string key) => (Brush)Application.Current.FindResource(key);

    /// <summary>I host this room, or I am in it.</summary>
    private bool IsMyRoom(LobbySummary lobby)
    {
        if (string.Equals(lobby.Id, _session?.CurrentLobbyId, StringComparison.Ordinal)) return true;
        var me = _session?.CurrentUser;
        if (me == null || lobby.Host == null) return false;
        return (!string.IsNullOrEmpty(lobby.Host.Id) && string.Equals(lobby.Host.Id, me.Id, StringComparison.Ordinal))
            || (!string.IsNullOrEmpty(lobby.Host.DiscordUsername)
                && string.Equals(lobby.Host.DiscordUsername, me.DiscordUsername, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Everything <see cref="RoomActionRule"/> needs about one room.</summary>
    private RoomRowFacts RoomFactsOf(LobbySummary lobby)
    {
        var seats = RoomFormats.SeatsOf(lobby.MaxPlayers, lobby.SpectatorSlots, lobby.CurrentPlayers, lobby.SpectatorsPresent);
        var installed = RoomModInstalled(lobby.ModId);
        return new RoomRowFacts(
            IsMyRoom(lobby),
            lobby.Status == "in_game",
            RoomFormats.OfferFor(seats, ObserversUnlocked),
            installed,
            installed && RoomVersionDiffers(lobby));
    }

    /// <summary>The mod is here. The rooms preview answers from its sample instead of the disk.</summary>
    private bool RoomModInstalled(string modId)
        => _demoRooms ? RoomListDemoData.IsInstalled(modId) : IsModInstalledLocally(modId);

    // ---- "Other version": the room's fingerprint against my copy's, worked out once per mod ----

    /// <summary>My copy's fingerprint per mod, keyed by the install it was read from so a copy
    /// switch is a new answer. Null while it is being worked out, or when it could not be.</summary>
    private readonly Dictionary<string, (string Path, string? Hash)> _roomModHashes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _roomModHashesPending = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether my copy is KNOWN to be another version of the room's mod. Unknown is false: a row
    /// never says "other version" about a fingerprint it has not read. The first ask starts the
    /// read off the UI thread and the list repaints when it lands, once per mod, never per row.
    /// </summary>
    private bool RoomVersionDiffers(LobbySummary lobby)
    {
        if (_demoRooms) return RoomListDemoData.VersionDiffers(lobby.Id);
        if (string.IsNullOrWhiteSpace(lobby.ModCombinedHash)) return false;
        var mine = KnownModHash(lobby.ModId);
        return mine != null && !string.Equals(mine, lobby.ModCombinedHash, StringComparison.OrdinalIgnoreCase);
    }

    private string? KnownModHash(string modId)
    {
        if (_computeModFingerprint == null) return null;
        var profile = ModRegistry.Find(modId);
        if (profile == null) return null;
        string path;
        try { path = GetInstallPath(profile) ?? ""; }
        catch { return null; }
        if (_roomModHashes.TryGetValue(modId, out var known) && string.Equals(known.Path, path, StringComparison.OrdinalIgnoreCase))
            return known.Hash;
        if (_roomModHashesPending.Add(modId)) _ = ReadModHashAsync(profile, path);
        return null;
    }

    private async Task ReadModHashAsync(ModProfile profile, string path)
    {
        string? hash = null;
        try { hash = await _computeModFingerprint!(profile); }
        catch (Exception ex) { DiagnosticLog.Write($"Rooms: could not fingerprint '{profile.Id}' for the version chip: {ex.Message}"); }
        _roomModHashesPending.Remove(profile.Id);
        _roomModHashes[profile.Id] = (path, string.IsNullOrWhiteSpace(hash) ? null : hash);
        // The quick match card can now say who waits — or that nobody does.
        RenderQuickCard();
        // Only a hash that could change a row is worth a repaint.
        if (hash != null && _lastBrowserList?.Any(l => string.Equals(l.ModId, profile.Id, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(l.ModCombinedHash)) == true)
            RerenderRoomsFromCache();
    }

    // ---- The row ----

    /// <summary>A heading over one of the three groups (PUEDES ENTRAR / EN PARTIDA / NO DISPONIBLES).</summary>
    private static TextBlock BuildRoomGroupHeading(RoomGroup group)
    {
        var tb = new TextBlock
        {
            Text = Strings.Get(RoomActionRule.GroupKey(group)),
            Foreground = Rm("RmHeading"),
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(4, 8, 4, 6),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Tag = "RoomGroup:" + group,
        };
        tb.SetResourceReference(TextBlock.FontSizeProperty, "RmChipSize");
        return tb;
    }

    /// <summary>One room. The name is kept for the tests that reach it by reflection.</summary>
    private Border BuildRoomCard(LobbySummary lobby, int rowIndex)
    {
        var facts = RoomFactsOf(lobby);
        var action = RoomActionRule.Decide(facts);
        var mine = action == RoomAction.OpenMine;

        var card = new Border
        {
            Style = (Style)FindResource(mine ? "RmRowCardMine" : "RmRowCard"),
            Tag = lobby,
        };
        AutomationProperties.SetAutomationId(card, "RoomRow:" + lobby.Id);

        var grid = new Grid { VerticalAlignment = VerticalAlignment.Center };
        foreach (var spec in _roomColumns)
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = spec.FixedWidth is double w ? new GridLength(w) : new GridLength(1, GridUnitType.Star),
            });

        var profile = ModRegistry.Find(lobby.ModId);

        // === ROOM: icon, then the name over its chips ===
        var room = new Grid();
        room.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        room.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(8),
            BorderBrush = Rm("RmChipRim"),
            BorderThickness = new Thickness(1),
            Background = ResolveRoomModIcon(profile) ?? Rm("MpField"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        room.Children.Add(icon);

        var roomText = new StackPanel { Margin = new Thickness(RoomColumnGap, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock
        {
            Text = lobby.Title,
            Foreground = Rm("RmTextStrong"),
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        name.SetResourceReference(TextBlock.FontSizeProperty, "RmNameSize");
        roomText.Children.Add(name);

        // ONE line of chips. A WrapPanel in a 22-px clip: a chip that does not fit wraps onto a
        // line nobody sees, so it leaves WHOLE rather than cut in half.
        var chips = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var chip in RoomChipsOf(lobby, facts)) chips.Children.Add(chip);
        var chipLine = new Grid { ClipToBounds = true, Height = 22, Margin = new Thickness(0, 5, 0, 0) };
        chipLine.Children.Add(chips);
        roomText.Children.Add(chipLine);
        Grid.SetColumn(roomText, 1);
        room.Children.Add(roomText);
        PlaceRoomCell(grid, RoomColumn.Room, room);

        // === HOST: photo, badge, then the name (and "you") over the rating ===
        var hostName = RoomHostName(lobby);
        var hostCell = new Grid { VerticalAlignment = VerticalAlignment.Center };
        hostCell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        // The badge's place is kept on every row, so the names line up whether a host wears one
        // (a host being placed does not).
        hostCell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        hostCell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var avatar = BuildAvatarDisc(hostName, lobby.Host?.AvatarUrl, HostAvatarSize);
        avatar.VerticalAlignment = VerticalAlignment.Center;
        hostCell.Children.Add(avatar);
        if (BuildRoomHostBadge(lobby) is { } hostBadge)
        {
            hostBadge.Margin = new Thickness(8, 0, 0, 0);
            hostBadge.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(hostBadge, 1);
            hostCell.Children.Add(hostBadge);
        }
        var host = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        Grid.SetColumn(host, 2);
        hostCell.Children.Add(host);
        var hostLine = new Grid();
        hostLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hostLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var hostText = new TextBlock
        {
            Text = hostName,
            Foreground = Rm("RmTextHost"),
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        hostText.SetResourceReference(TextBlock.FontSizeProperty, "RmHostSize");
        var hostIsMe = mine && IsHostedByMe(lobby);
        if (hostIsMe)
        {
            // The name trims, "you" never does: a bounded width keeps the two apart (the "never
            // touch" rule) without letting a long name push "you" out of the cell.
            hostText.MaxWidth = 140;
            var you = new TextBlock
            {
                Text = Strings.Get("RmHostYou"),
                Foreground = Rm("MpTextFaint"),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Bottom,
            };
            you.SetResourceReference(TextBlock.FontSizeProperty, "RmSmallSize");
            Grid.SetColumn(you, 1);
            hostLine.Children.Add(you);
        }
        else
        {
            Grid.SetColumnSpan(hostText, 2);
        }
        hostLine.Children.Insert(0, hostText);
        host.Children.Add(hostLine);
        if (BuildRoomHostRating(lobby, hostIsMe) is { } rating)
        {
            rating.Margin = new Thickness(0, 4, 0, 0);
            host.Children.Add(rating);
        }
        PlaceRoomCell(grid, RoomColumn.Host, hostCell, gap: true);

        // === PLAYERS: "1/2", clickable to see who is in ===
        var seats = RoomFormats.SeatsOf(lobby.MaxPlayers, lobby.SpectatorSlots, lobby.CurrentPlayers, lobby.SpectatorsPresent);
        var players = new TextBlock
        {
            Text = $"{seats.PlayersSeated}/{seats.PlayerSeats}",
            Foreground = Rm("RmTextHost"),
            FontFamily = MonoFont,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            Cursor = System.Windows.Input.Cursors.Hand,
            Tag = lobby,
            ToolTip = Strings.Get("MpRoomPeekTooltip"),
        };
        players.SetResourceReference(TextBlock.FontSizeProperty, "RmHostSize");
        players.MouseEnter += (_, _) => players.TextDecorations = TextDecorations.Underline;
        players.MouseLeave += (_, _) => players.TextDecorations = null;
        players.MouseLeftButtonUp += PlayersPeek_Click;
        var people = new TextBlock
        {
            Text = "\uE716",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 13,
            Foreground = Rm("RmTextLabel"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0),
        };
        var playersCell = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        playersCell.Children.Add(people);
        playersCell.Children.Add(players);
        PlaceRoomCell(grid, RoomColumn.Players, playersCell, gap: true);

        // === PING: to this host over Radmin, only when the server publishes hosts' addresses ===
        if (HostPingColumnShown)
            PlaceRoomCell(grid, RoomColumn.Ping, BuildHostPingCell(lobby), gap: true);

        // === ACTION ===
        var actionCell = BuildRoomAction(lobby, action, out var fire);
        PlaceRoomCell(grid, RoomColumn.Action, actionCell, gap: true);

        // A double click does what the button does, through the button, so the row can never do
        // something its own button would not.
        card.MouseLeftButtonUp += (_, e) =>
        {
            if (e.ClickCount >= 2) fire?.Invoke();
        };

        if (_roomIdsSeeded && !string.IsNullOrEmpty(lobby.Id) && !_knownRoomIds.Contains(lobby.Id))
            FlashNewRoom(card);
        if (!string.IsNullOrEmpty(lobby.Id)) _knownRoomIds.Add(lobby.Id);

        card.Child = grid;
        return card;
    }

    private static readonly FontFamily MonoFont = new("Consolas, Courier New, monospace");

    /// <summary>The host's Discord photo in the rooms row (the maintainer asked for it back over 86).</summary>
    internal const double HostAvatarSize = 28;

    /// <summary>The host's badge, STILL, on the room's ladder (team for a ranked 2v2/3v3, as the
    /// rating under his name). None for a host still being placed — the badge would give away the
    /// number placement privacy hides — and none when the server sent no place.</summary>
    private FrameworkElement? BuildRoomHostBadge(LobbySummary lobby)
    {
        var h = lobby.Host;
        if (h == null) return null;
        var format = RoomFormats.Resolve(lobby.Competitive, lobby.MaxPlayers, lobby.SpectatorSlots);
        var team = format is RoomFormat.TwoVTwo or RoomFormat.ThreeVThree;
        if ((team ? h.InPlacementTeam : h.InPlacement) == true) return null;
        if (BadgeOf(format, h.BadgeMode, h.LadderRank, h.LadderRankTeam) is not { } shown) return null;
        return BuildShownBadge(shown, 22, "room-host-" + h.Id);
    }

    /// <summary>I am the host (as opposed to merely in the room).</summary>
    private bool IsHostedByMe(LobbySummary lobby)
    {
        var me = _session?.CurrentUser;
        if (me == null || lobby.Host == null) return false;
        return (!string.IsNullOrEmpty(lobby.Host.Id) && string.Equals(lobby.Host.Id, me.Id, StringComparison.Ordinal))
            || (!string.IsNullOrEmpty(lobby.Host.DiscordUsername)
                && string.Equals(lobby.Host.DiscordUsername, me.DiscordUsername, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Display name → Discord login → "—".</summary>
    private static string RoomHostName(LobbySummary lobby)
    {
        var n = lobby.Host?.DisplayName;
        if (string.IsNullOrWhiteSpace(n) || n == "-") n = lobby.Host?.DiscordUsername;
        return string.IsNullOrWhiteSpace(n) || n == "-" ? "—" : n!;
    }

    /// <summary>
    /// "1630 ELO", or "Placement 3/10" for a host still being placed. The ladder is the room's:
    /// a 2v2/3v3 room shows the team rating, everything else the 1v1 one. Nothing when the server
    /// sent no rating, never somebody's 1500.
    /// </summary>
    private TextBlock? BuildRoomHostRating(LobbySummary lobby, bool hostIsMe)
    {
        var h = lobby.Host;
        if (h == null) return null;
        var format = RoomFormats.Resolve(lobby.Competitive, lobby.MaxPlayers, lobby.SpectatorSlots);
        var team = format is RoomFormat.TwoVTwo or RoomFormat.ThreeVThree;
        var rating = team ? h.RatingTeam : h.Rating;
        var placing = team ? h.InPlacementTeam : h.InPlacement;
        var played = team ? h.GamesPlayedTeam : h.GamesPlayed;

        var tb = new TextBlock { Foreground = Rm("RmTextLabel"), TextTrimming = TextTrimming.CharacterEllipsis };
        tb.SetResourceReference(TextBlock.FontSizeProperty, "RmSmallSize");
        var number = new System.Windows.Documents.Run { FontFamily = MonoFont, Foreground = Rm("MpTextSecondary") };
        // A host still being placed shows his progress, NEVER a number — even when an older
        // server sends one beside the flag (placement privacy, design 55).
        if (placing == true)
        {
            tb.Tag = HiddenPlacementTag;
            tb.Inlines.Add(new System.Windows.Documents.Run(Strings.Get("RmHostPlacement")));
            if (played is int p)
            {
                var required = team ? StatsDemoData.PlacementTeam : StatsDemoData.PlacementDefault;
                number.Text = $"{Math.Min(p, required)}/{required}";
                tb.Inlines.Add(new System.Windows.Documents.Run(" "));
                tb.Inlines.Add(number);
            }
            return tb;
        }
        if (rating is not double r) return null;
        number.Text = PlacementView.RatingText(r);
        tb.Inlines.Add(number);
        tb.Inlines.Add(new System.Windows.Documents.Run(" ELO"));
        return tb;
    }

    /// <summary>The chips under a room's name, in the order the design draws them.</summary>
    private IEnumerable<FrameworkElement> RoomChipsOf(LobbySummary lobby, RoomRowFacts facts)
    {
        var format = RoomFormats.Resolve(lobby.Competitive, lobby.MaxPlayers, lobby.SpectatorSlots);
        var seats = RoomFormats.SeatsOf(lobby.MaxPlayers, lobby.SpectatorSlots, lobby.CurrentPlayers, lobby.SpectatorsPresent);
        if (lobby.Competitive)
        {
            var key = RoomFormats.LabelKey(format);
            yield return RoomChip(key == null ? Strings.Get("MpRoomCompetitiveBadge") : Strings.Format("RmChipRanked", Strings.Get(key)),
                "RmDotRanked", "RmTextHost", Strings.Get("MpRoomCompetitiveTooltip"));
        }
        else
        {
            var size = seats.PlayerSeats;
            var label = size == 2 ? "1v1" : size == 4 ? "2v2" : size == 6 ? "3v3" : null;
            yield return RoomChip(label != null ? Strings.Format("RmChipCasual", label) : Strings.Format("RmChipCasualPlayers", size),
                "RmDotCasual", "RmTextHost", Strings.Get("MpRoomCasualTooltip"));
        }
        if (lobby.IsPrivate && !facts.Mine) yield return RoomChip(Strings.Get("RmChipPassword"), "RmDotPassword", "RmTextHost");
        if (facts.InGame)
        {
            var started = RoomAgeFormat.ParseCreatedUtc(lobby.StartedAt ?? "");
            yield return RoomChip(started is DateTime s
                    ? Strings.Format("RmChipInGame", RoomAgeFormat.Coarse(DateTime.UtcNow - s))
                    : Strings.Get("RmChipInGameBare"),
                "RmDotGreen", "RmTextGreen");
        }
        if (facts.VersionDiffers) yield return RoomChip(Strings.Get("RmChipOtherVersion"), "RmDotOrange", "RmTextOrange");
        if (seats.WatchSeats > 0) yield return RoomChip(Strings.Format("RmChipObservers", seats.Watching, seats.WatchSeats), "RmDotBlue", "RmTextBlue");
        if (facts.Mine)
            yield return RoomChip(Strings.Get(IsHostedByMe(lobby) ? "RmChipYours" : "RmChipInside"), "RmDotBlue", "RmTextBlue");
    }

    /// <summary>A 22-px chip: a 6-px dot and a short label, on a faint fill with a rim.</summary>
    private static Border RoomChip(string text, string dotKey, string textKey, string? tip = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new System.Windows.Shapes.Ellipse { Width = 6, Height = 6, Fill = Rm(dotKey), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        var label = new TextBlock { Text = text, Foreground = Rm(textKey), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.FontSizeProperty, "RmChipSize");
        row.Children.Add(label);
        var chip = new Border
        {
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(8, 0, 8, 0),
            Margin = new Thickness(0, 0, 6, 0),
            Background = Rm("RmChipBg"),
            BorderBrush = Rm("RmChipRim"),
            BorderThickness = new Thickness(1),
            Child = row,
        };
        if (tip != null) chip.ToolTip = TooltipHelper.Wrap(tip);
        return chip;
    }

    /// <summary>The row's one action: a button, or the word "Full".</summary>
    private FrameworkElement BuildRoomAction(LobbySummary lobby, RoomAction action, out Action? fire)
    {
        fire = null;
        var caption = Strings.Get(RoomActionRule.CaptionKey(action));
        if (action == RoomAction.Full)
        {
            var full = new TextBlock
            {
                Text = caption,
                Foreground = Rm("MpTextFaint"),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 6, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            full.SetResourceReference(TextBlock.FontSizeProperty, "RmButtonSize");
            return full;
        }

        var btn = new Button
        {
            Style = (Style)Application.Current.FindResource(action switch
            {
                RoomAction.OpenMine => "RmRowButtonPrimary",
                RoomAction.Join => "RmRowButtonJoin",
                _ => "RmRowButton",
            }),
            Content = caption,
            Tag = lobby,
        };
        AutomationProperties.SetAutomationId(btn, "RoomAction:" + lobby.Id);
        AutomationProperties.SetName(btn, caption);
        switch (action)
        {
            case RoomAction.OpenMine:
                btn.Click += (_, _) =>
                {
                    if (_demoRooms) { DemoRoomsSay(caption); return; }
                    if (string.Equals(lobby.Id, _session?.CurrentLobbyId, StringComparison.Ordinal)) OpenLobbyWindow();
                };
                break;
            case RoomAction.Join:
                btn.Click += (s, e) => { if (_demoRooms) DemoRoomsSay(caption); else JoinRoomButton_Click(s, e); };
                break;
            case RoomAction.Watch:
                btn.Click += (s, e) => { if (_demoRooms) DemoRoomsSay(caption); else WatchRoomButton_Click(s, e); };
                break;
            case RoomAction.InstallMod:
                btn.Click += (_, _) =>
                {
                    if (ModRegistry.Find(lobby.ModId) is { } p) _openModInLibrary?.Invoke(p);
                };
                break;
            case RoomAction.CheckMyCopy:
                btn.Click += (_, _) =>
                {
                    if (ModRegistry.Find(lobby.ModId) is { } p) _openModSettings?.Invoke(p, "local-files");
                };
                break;
            case RoomAction.SeeMatch:
                btn.Click += (_, _) => { if (_demoRooms) DemoRoomsSay(caption); else OpenPlayersPeek(btn); };
                break;
        }
        fire = () => btn.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        return btn;
    }

    // ---- Loading (86c) ----

    /// <summary>Four placeholder rows, as the design draws them while the list loads.</summary>
    private const int SkeletonRowCount = 4;

    /// <summary>The width of each placeholder's name bar, as a share of its column (the design's).</summary>
    private static readonly double[] SkeletonNameShare = { 0.48, 0.62, 0.40, 0.55 };

    private int _skeletonIndex;

    /// <summary>
    /// A placeholder in the SHAPE of a room row — icon, name and chip line, host, players, ping — on
    /// the row's own columns, so the list does not jump when the answer lands. Not animated: the
    /// brushes are frozen, and a still page is what "loading" needs to say.
    /// </summary>
    private Border BuildRoomSkeletonRow()
    {
        Brush A(byte alpha) => new SolidColorBrush(Color.FromArgb(alpha, 0xFF, 0xFF, 0xFF));
        Border Bar(double w, double h, byte alpha) => new()
        {
            Width = w,
            Height = h,
            CornerRadius = new CornerRadius(h / 2),
            Background = A(alpha),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // Not hit-testable: a placeholder that lit up on hover would read as a room.
        var row = new Border { Style = (Style)FindResource("RmRowCard"), IsHitTestVisible = false };
        var grid = new Grid();
        foreach (var spec in _roomColumns)
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = spec.FixedWidth is double w ? new GridLength(w) : new GridLength(1, GridUnitType.Star),
            });

        var room = new Grid();
        room.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        room.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        room.Children.Add(new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(8), Background = A(0x12) });
        var share = SkeletonNameShare[_skeletonIndex++ % SkeletonNameShare.Length];
        var nameLine = new Grid { Height = 12 };
        nameLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(share, GridUnitType.Star) });
        nameLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - share, GridUnitType.Star) });
        nameLine.Children.Add(new Border { CornerRadius = new CornerRadius(6), Background = A(0x1A) });
        var lines = new StackPanel { Margin = new Thickness(RoomColumnGap, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        lines.Children.Add(nameLine);
        var chipBar = Bar(120, 10, 0x0F);
        chipBar.Margin = new Thickness(0, 8, 0, 0);
        lines.Children.Add(chipBar);
        Grid.SetColumn(lines, 1);
        room.Children.Add(lines);
        PlaceRoomCell(grid, RoomColumn.Room, room);
        PlaceRoomCell(grid, RoomColumn.Host, Bar(90, 12, 0x12), gap: true);
        PlaceRoomCell(grid, RoomColumn.Players, Bar(30, 12, 0x12), gap: true);
        if (HostPingColumnShown) PlaceRoomCell(grid, RoomColumn.Ping, Bar(40, 12, 0x12), gap: true);
        row.Child = grid;
        return row;
    }

    // ---- The viewer's own ping, once, above the list ----
    //
    // There is no per-host figure to put in a row: the lobby server does not publish a host's
    // Radmin address. What the launcher measures is the viewer's internet latency, the same for
    // every room, and beside a room it read as the host's. So it is said once, as what it is.

    private double? CurrentRoomPing() => _demoRooms ? RoomListDemoData.PingMs : _connectionPingMs >= 0 ? _connectionPingMs : null;

    /// <summary>"Your connection · 18 ms", or nothing at all while there is no measure.</summary>
    internal static void FillMyPing(TextBlock tb, double? ms)
    {
        int? v = ms is double d ? (int)Math.Round(d) : null;
        tb.Visibility = v.HasValue ? Visibility.Visible : Visibility.Collapsed;
        if (v is not int n) return;
        tb.Text = Strings.Format("RmMyPing", n);
        tb.Foreground = Rm(RoomActionRule.PingBrushKey(v));
    }

    private void RefreshMyPing()
    {
        if (RoomsMyPingText == null) return;
        FillMyPing(RoomsMyPingText, _offlineMode ? null : CurrentRoomPing());
        RoomsMyPingText.ToolTip = Strings.Get("MpRoomPingTooltip");
    }
}
