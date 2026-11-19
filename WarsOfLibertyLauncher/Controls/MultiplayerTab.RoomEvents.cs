using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// The global chat's "X opened a room" cards (design handoff 1a), kept LIVE: each follows its room
/// through the rooms list and the Rooms page's own decision (<see cref="RoomActionRule"/>), so it
/// offers Join only while the room has a seat, and says Full, In a match or Closed when it does
/// not. It used to be drawn once from the announcement and offer Join for ever.
/// </summary>
public partial class MultiplayerTab
{
    private sealed class RoomEventEntry
    {
        public RoomEventEntry(RoomEventSnapshot snap, Border card) { Snap = snap; Card = card; }
        public RoomEventSnapshot Snap { get; }
        public Border Card { get; }
        public string? Key { get; set; }
    }

    /// <summary>The cards on the chat, by room. One a trim or a clear took away is dropped on the
    /// next refresh (its card has no parent any more).</summary>
    private readonly Dictionary<string, RoomEventEntry> _roomEventCards = new(StringComparer.Ordinal);

    /// <summary>When the rooms list now held was ASKED for: a room missing from it closed only if
    /// it was announced before that.</summary>
    private DateTime? _roomsListAskedUtc;

    /// <summary>
    /// A card for a room just announced, inserted into the chat flow. Unlike the toast it is NOT
    /// filtered: a room whose mod you don't have, or your own, still reads as activity. What it
    /// offers is the Rooms page's own answer for that room.
    /// </summary>
    private void AppendGlobalChatRoomEvent(RoomEventSnapshot snap)
    {
        if (GlobalChatPanel == null || !_globalChatRendered) return;
        var card = new Border
        {
            Background = (Brush)Application.Current.FindResource("MpEventBg"),
            BorderBrush = (Brush)Application.Current.FindResource("MpEventRim"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 9, 10, 10),
            Margin = new Thickness(0, 8, 0, 8),
        };
        AutomationProperties.SetAutomationId(card, "RoomEvent:" + snap.Id);
        var entry = new RoomEventEntry(snap, card);
        _roomEventCards[snap.Id] = entry;
        PaintRoomEventCard(entry);
        GlobalChatPanel.Children.Add(card);

        // A room card breaks the "same author = continuation" run, or the next message
        // would tuck itself under a header that is no longer above it.
        _lastGlobalChatAuthor = null;
        TrimGlobalChat();
        // A card is content: the "no messages yet, say hi" line must not sit under it.
        UpdateGlobalChatEmptyHint();
        ScrollGlobalChatToEnd();
    }

    /// <summary>Repaints the cards whose room changed; called when a rooms list arrives.</summary>
    private void RefreshRoomEventCards()
    {
        if (_roomEventCards.Count == 0) return;
        foreach (var gone in _roomEventCards.Where(kv => kv.Value.Card.Parent == null).Select(kv => kv.Key).ToList())
            _roomEventCards.Remove(gone);
        foreach (var entry in _roomEventCards.Values) PaintRoomEventCard(entry);
    }

    private void PaintRoomEventCard(RoomEventEntry entry)
    {
        var snap = entry.Snap;
        var live = _lastBrowserList?.FirstOrDefault(l => string.Equals(l.Id, snap.Id, StringComparison.Ordinal));
        var status = RoomEventCard.StatusOf(live != null, _roomsListAskedUtc, snap.AnnouncedUtc);
        var room = live ?? (status == RoomEventStatus.Announced ? RoomEventCard.FromAnnouncement(snap) : null);
        RoomRowFacts? facts = room != null ? RoomFactsOf(room) : null;
        RoomAction? action = facts is { } f ? RoomActionRule.Decide(f) : null;
        var key = RoomEventCard.KeyOf(status, room, action) + "|" + Strings.Language;
        if (key == entry.Key) return;
        entry.Key = key;
        entry.Card.Child = BuildRoomEventContent(snap, status, room, facts, action);
        // Only on a change. "Full" and "Closed" are text, not buttons, so this line is the one
        // place a bundle (or the testbed) can see what the card said.
        DiagnosticLog.Write($"Chat room card {snap.Id}: {status}, {(action?.ToString() ?? "no action")}");
    }

    private FrameworkElement BuildRoomEventContent(RoomEventSnapshot snap, RoomEventStatus status,
        LobbySummary? room, RoomRowFacts? facts, RoomAction? action)
    {
        bool closed = status == RoomEventStatus.Closed;
        var strong = (Brush)Application.Current.FindResource(closed ? "MpTextMuted" : "MpTextSecondary");
        var muted = (Brush)Application.Current.FindResource("MpTextMuted");
        var label = (double)Application.Current.FindResource("MpLabelSize");
        var stack = new StackPanel();

        // 1. The mod's icon, who opened it, and when.
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var modName = ResolveModDisplayName(snap.ModId);
        var icon = ResolveRoomModIcon(ModRegistry.Find(snap.ModId));
        var tile = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(6),
            Background = icon ?? (Brush)Application.Current.FindResource("MpEventRim"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 9, 0),
            Opacity = closed ? .55 : 1,
            ToolTip = TooltipHelper.Wrap(modName),
        };
        if (icon == null)
            tile.Child = new TextBlock
            {
                Text = "⚑",
                Foreground = (Brush)Application.Current.FindResource("MpActionText"),
                FontSize = (double)Application.Current.FindResource("FontSizeCaption"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        head.Children.Add(tile);
        var who = new TextBlock
        {
            Text = Strings.Format("MpChatRoomOpened", string.IsNullOrWhiteSpace(snap.HostLogin) ? "—" : snap.HostLogin),
            Foreground = strong,
            FontSize = label,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        Grid.SetColumn(who, 1);
        head.Children.Add(who);
        var when = new TextBlock
        {
            Text = snap.AnnouncedUtc.ToLocalTime().ToString("HH:mm"),
            Foreground = muted,
            FontSize = (double)Application.Current.FindResource("MpPillSize"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(when, 2);
        head.Children.Add(when);
        stack.Children.Add(head);

        // 2. The room's name and its seats.
        var title = room?.Title is { Length: > 0 } t ? t : snap.Title;
        var named = new Grid { Margin = new Thickness(0, 7, 0, 0) };
        named.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        named.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        named.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(title) ? modName : title,
            Foreground = (Brush)Application.Current.FindResource(closed ? "MpTextMuted" : "MpTextBody"),
            FontSize = (double)Application.Current.FindResource("MpBodySize"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (room != null && !closed)
        {
            var seats = RoomFormats.SeatsOf(room.MaxPlayers, room.SpectatorSlots, room.CurrentPlayers, room.SpectatorsPresent);
            var count = new TextBlock
            {
                Text = Strings.Format("MpChatRoomSeats", seats.PlayersSeated, seats.PlayerSeats),
                Foreground = (Brush)Application.Current.FindResource(seats.PlayingFull ? "RmGroupOffText" : "MpTextSecondary"),
                FontSize = label,
                FontWeight = FontWeights.SemiBold,
                FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(count, 1);
            named.Children.Add(count);
        }
        stack.Children.Add(named);

        // 3. What it is, and the one thing to do: the Rooms page's own answer.
        var foot = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var chips = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        if (room != null && facts is { } fx)
            foreach (var chip in RoomChipsOf(room, fx).Take(2))
            {
                chip.Margin = new Thickness(0, 2, 6, 2);
                chips.Children.Add(chip);
            }
        foot.Children.Add(chips);
        var act = BuildRoomEventAction(snap, room, action, closed);
        if (act != null)
        {
            act.Margin = new Thickness(8, 0, 0, 0);
            act.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(act, 1);
            foot.Children.Add(act);
        }
        stack.Children.Add(foot);
        return stack;
    }

    /// <summary>The card's one action. Join goes through the deep link's path, which asks the
    /// server for the room again, so a card built from the announcement alone never joins a stale
    /// copy; every other answer is the Rooms row's own control.</summary>
    private FrameworkElement? BuildRoomEventAction(RoomEventSnapshot snap, LobbySummary? room, RoomAction? action, bool closed)
    {
        if (closed)
        {
            var gone = new TextBlock
            {
                Text = Strings.Get("MpChatRoomClosed"),
                Foreground = (Brush)Application.Current.FindResource("MpTextMuted"),
                FontWeight = FontWeights.SemiBold,
            };
            gone.SetResourceReference(TextBlock.FontSizeProperty, "RmRowButtonSize");
            return gone;
        }
        if (room == null || action is not RoomAction a) return null;
        if (a == RoomAction.Join)
        {
            var caption = Strings.Get(RoomActionRule.CaptionKey(a));
            var join = new Button { Style = (Style)Application.Current.FindResource("RmRowButtonJoin"), Content = caption };
            AutomationProperties.SetAutomationId(join, "RoomEventAction:" + snap.Id);
            AutomationProperties.SetName(join, caption);
            join.Click += async (_, _) =>
            {
                if (_demoRooms) { DemoRoomsSay(caption); return; }
                await JoinByLobbyIdAsync(snap.Id);
            };
            return join;
        }
        var control = BuildRoomAction(room, a, out _);
        AutomationProperties.SetAutomationId(control, "RoomEventAction:" + snap.Id);
        return control;
    }
}
