using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// The bell's panel (design 70a). It lives in the window rather than in a Popup: a transparent
/// Popup is a layered window and loses ClearType, and this one holds paragraphs of text.
/// </summary>
public sealed class NotificationPanel : Border
{
    public event EventHandler<NotificationItem>? Opened;
    public event EventHandler<NotificationItem>? ActionPressed;
    public event EventHandler<NotificationItem>? Dismissed;
    public event EventHandler? MarkAllReadRequested;
    public event EventHandler? ClearReadRequested;
    public event EventHandler? ChooseRequested;

    /// <summary>Supplied by the window so the panel shares its image cache.</summary>
    public Func<NotificationItem, Brush?>? IconFor { get; set; }

    private NotificationFilter _filter = NotificationFilter.All;
    private IReadOnlyList<NotificationItem> _items = Array.Empty<NotificationItem>();
    private readonly StackPanel _list = new() { Margin = new Thickness(8, 4, 8, 8) };
    private readonly WrapPanel _filters = new() { Margin = new Thickness(16, 0, 16, 10) };
    private readonly TextBlock _title = Text("", 15, FontWeights.SemiBold, "UiTitleText");
    private readonly TextBlock _unread = Text("", 12, FontWeights.Normal, "UiDimText");
    private readonly Button _markAll = Link("NotificationMarkAllRead");
    private readonly Button _clearRead = Link("NotificationClearRead");
    private readonly Button _choose = Link("NotificationChoose");

    public NotificationPanel()
    {
        Width = 420;
        CornerRadius = new CornerRadius(12);
        BorderThickness = new Thickness(1);
        // Opaque on purpose: backdrop blur is not available to a WPF element over its own window,
        // and at the glass level the toolbar behind it reads straight through the rows.
        SetResourceReference(BackgroundProperty, "LibraryMenuBg");
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x1F, 255, 255, 255));

        var head = new Grid { Margin = new Thickness(16, 14, 12, 10) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _unread.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(_unread, 1);
        Grid.SetColumn(_markAll, 3);
        _markAll.Margin = new Thickness(0, 0, 8, 0);
        var gear = new Button
        {
            Content = "⚙",
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            FontSize = 15,
            FontFamily = new FontFamily("Segoe UI Symbol, Segoe UI"),
            Style = (Style)Application.Current.Resources["LibraryCardButton"],
        };
        AutomationProperties.SetAutomationId(gear, "NotificationSettings");
        gear.Click += (_, _) => ChooseRequested?.Invoke(this, EventArgs.Empty);
        Grid.SetColumn(gear, 4);
        head.Children.Add(_title);
        head.Children.Add(_unread);
        head.Children.Add(_markAll);
        head.Children.Add(gear);
        _markAll.Click += (_, _) => MarkAllReadRequested?.Invoke(this, EventArgs.Empty);

        var scroll = new ScrollViewer
        {
            Content = _list,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0),
            Focusable = false,
        };
        AutomationProperties.SetAutomationId(scroll, "NotificationList");

        var rule = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = scroll };
        rule.SetResourceReference(BorderBrushProperty, "WindowFooterRule");

        var foot = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(16, 10, 16, 10) };
        foot.SetResourceReference(BorderBrushProperty, "WindowFooterRule");
        var footRow = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(_clearRead, Dock.Right);
        _clearRead.SetResourceReference(Control.ForegroundProperty, "UiMutedText");
        footRow.Children.Add(_clearRead);
        footRow.Children.Add(_choose);
        foot.Child = footRow;
        _clearRead.Click += (_, _) => ClearReadRequested?.Invoke(this, EventArgs.Empty);
        _choose.Click += (_, _) => ChooseRequested?.Invoke(this, EventArgs.Empty);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(head);
        Grid.SetRow(_filters, 1);
        root.Children.Add(_filters);
        Grid.SetRow(rule, 2);
        root.Children.Add(rule);
        Grid.SetRow(foot, 3);
        root.Children.Add(foot);
        Child = root;
    }

    private static TextBlock Text(string text, double size, FontWeight weight, string brush)
    {
        var block = new TextBlock { Text = text, FontSize = size, FontWeight = weight, VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    private static Button Link(string id)
    {
        var link = new Button
        {
            Style = (Style)Application.Current.Resources["LibraryTextLink"],
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        link.SetResourceReference(Control.ForegroundProperty, "UiInfoText");
        AutomationProperties.SetAutomationId(link, id);
        return link;
    }

    public void Show(IEnumerable<NotificationItem> items)
    {
        _items = items.ToList();
        Paint();
    }

    private void Paint()
    {
        var unread = NotificationView.Unread(_items);
        _title.Text = Strings.Get("NotifPanelTitle");
        _unread.Text = Strings.Format("NotifUnreadCount", unread);
        _unread.Visibility = unread > 0 ? Visibility.Visible : Visibility.Collapsed;
        _markAll.Content = Strings.Get("NotifMarkAllRead");
        _markAll.Visibility = unread > 0 ? Visibility.Visible : Visibility.Collapsed;
        _clearRead.Content = Strings.Get("NotifClearRead");
        _clearRead.Visibility = NotificationView.BellItems(_items).Any(i => i.Read) ? Visibility.Visible : Visibility.Collapsed;
        _choose.Content = Strings.Get("NotifChoose");

        _filters.Children.Clear();
        AddFilter(NotificationFilter.All, "NotifFilterAll");
        AddFilter(NotificationFilter.Mods, "NotifFilterMods");
        AddFilter(NotificationFilter.Launcher, "NotifFilterLauncher");

        _list.Children.Clear();
        var groups = NotificationView.Group(_items, _filter, DateTime.Now);
        foreach (var group in groups)
        {
            var label = Text(Strings.Get("NotifDay" + group.Day), 11, FontWeights.SemiBold, "UiMutedText");
            label.Margin = new Thickness(8, 12, 8, 6);
            _list.Children.Add(label);
            foreach (var item in group.Items) _list.Children.Add(Row(item));
        }
        if (groups.Count == 0) _list.Children.Add(Empty());
    }

    private UIElement Empty()
    {
        var panel = new StackPanel { Margin = new Thickness(24, 56, 24, 56), HorizontalAlignment = HorizontalAlignment.Center };
        var check = new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(22),
            Background = new SolidColorBrush(Color.FromArgb(0x24, 0x4F, 0xD6, 0x8A)),
            Child = Text("✓", 18, FontWeights.Bold, "UiOk"),
        };
        ((TextBlock)check.Child).HorizontalAlignment = HorizontalAlignment.Center;
        panel.Children.Add(check);
        var title = Text(Strings.Get("NotifEmpty"), 14, FontWeights.SemiBold, "UiHeadText");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.Margin = new Thickness(0, 8, 0, 0);
        panel.Children.Add(title);
        return panel;
    }

    private void AddFilter(NotificationFilter filter, string key)
    {
        var on = filter == _filter;
        var words = new StackPanel { Orientation = Orientation.Horizontal };
        words.Children.Add(new TextBlock { Text = Strings.Get(key), VerticalAlignment = VerticalAlignment.Center });
        var count = NotificationView.Unread(_items, filter);
        if (count > 0) words.Children.Add(CountBadge(count, 16, new Thickness(7, 0, 0, 0)));

        var pill = new Button { Content = words, Style = (Style)Application.Current.Resources["LibraryPill"], Margin = new Thickness(0, 0, 6, 0) };
        pill.Background = on ? new SolidColorBrush(Color.FromArgb(0x1A, 255, 255, 255)) : Brushes.Transparent;
        pill.BorderBrush = new SolidColorBrush(on ? Color.FromArgb(0x47, 255, 255, 255) : Color.FromArgb(0x1A, 255, 255, 255));
        if (on) pill.Foreground = Brushes.White;
        AutomationProperties.SetAutomationId(pill, "NotificationFilter_" + filter);
        AutomationProperties.SetName(pill, Strings.Get(key));
        pill.Click += (_, _) =>
        {
            _filter = filter;
            Paint();
        };
        _filters.Children.Add(pill);
    }

    internal static Border CountBadge(int count, double size, Thickness margin)
    {
        var text = new TextBlock
        {
            Text = count.ToString(),
            FontSize = size > 16 ? 10.5 : 10,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var badge = new Border
        {
            MinWidth = size,
            Height = size,
            Padding = new Thickness(4, 0, 4, 0),
            CornerRadius = new CornerRadius(size / 2),
            Margin = margin,
            Child = text,
        };
        badge.SetResourceReference(BackgroundProperty, "MpAction");
        return badge;
    }

    private static (string Tone, Color Soft, Color Line, string Glyph) Dress(NotificationItem item)
    {
        var tone = NotificationView.ToneOf(item);
        var (brush, r, g, b) = tone switch
        {
            NotificationTone.Ok => ("UiOk", (byte)0x4F, (byte)0xD6, (byte)0x8A),
            NotificationTone.Error => ("UiError", (byte)0xF4, (byte)0x90, (byte)0x0C),
            NotificationTone.Highlight => ("AccentBrush", (byte)0xD8, (byte)0xB6, (byte)0x6A),
            _ => ("UiInfo", (byte)0x55, (byte)0xAC, (byte)0xEE),
        };
        var glyph = item.Kind switch
        {
            NotificationKind.UpdateAvailable or NotificationKind.LauncherUpdate or NotificationKind.ModPatchPublished => "↑",
            NotificationKind.NewTranslation => "A",
            NotificationKind.NewMod => "+",
            NotificationKind.Announcement => "i",
            NotificationKind.MonthlyHighlights => "★",
            _ => tone == NotificationTone.Error ? "!" : "✓",
        };
        return (brush, Color.FromArgb(0x24, r, g, b), Color.FromArgb(0x66, r, g, b), glyph);
    }

    private UIElement Row(NotificationItem item)
    {
        var (tone, soft, line, glyph) = Dress(item);
        var content = new Grid { Margin = new Thickness(10, 10, 8, 10) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        content.ColumnDefinitions.Add(new ColumnDefinition());
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });

        var icon = IconFor?.Invoke(item);
        var mark = new Grid { Width = 36, Height = 36, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0) };
        if (icon != null)
        {
            mark.Children.Add(new Border { Background = icon });
            var badge = new Border
            {
                Width = 17,
                Height = 17,
                CornerRadius = new CornerRadius(9),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, -4, -4),
                BorderThickness = new Thickness(1.5),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x0C, 0x16, 0x26)),
                Child = new TextBlock
                {
                    Text = glyph,
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x1C, 0x2E)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            badge.SetResourceReference(BackgroundProperty, tone);
            mark.Children.Add(badge);
        }
        else
        {
            var disc = new Border
            {
                CornerRadius = new CornerRadius(18),
                Background = new SolidColorBrush(soft),
                BorderBrush = new SolidColorBrush(line),
                BorderThickness = new Thickness(1),
                Child = Text(glyph, 15, FontWeights.Bold, tone),
            };
            ((TextBlock)disc.Child).HorizontalAlignment = HorizontalAlignment.Center;
            mark.Children.Add(disc);
        }
        content.Children.Add(mark);

        var words = new StackPanel { Margin = new Thickness(12, 0, 12, 0) };
        var title = Text(item.Title, 13, FontWeights.SemiBold, item.Read ? "UiSoftText" : "UiTitleText");
        title.TextWrapping = TextWrapping.Wrap;
        var body = Text(item.Body, 12, FontWeights.Normal, "UiMutedText");
        body.TextWrapping = TextWrapping.Wrap;
        body.TextTrimming = TextTrimming.CharacterEllipsis;
        body.LineHeight = 17.4;
        body.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        body.MaxHeight = 36;
        body.Margin = new Thickness(0, 3, 0, 0);
        words.Children.Add(title);
        words.Children.Add(body);

        var foot = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var action = NotificationView.ActionOf(item.Kind);
        if (action != NotificationAction.None)
        {
            var button = new Button
            {
                Content = Strings.Get("NotifAction" + action),
                Style = (Style)Application.Current.Resources["LibraryCardButton"],
                Padding = new Thickness(12, 0, 12, 0),
                FontSize = 12,
                Margin = new Thickness(0, 0, 10, 0),
            };
            if (action is NotificationAction.Update or NotificationAction.RestartAndUpdate)
            {
                button.Background = new SolidColorBrush(Color.FromArgb(0x29, 0x55, 0xAC, 0xEE));
                button.BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0x55, 0xAC, 0xEE));
                button.Foreground = new SolidColorBrush(Color.FromRgb(0xA9, 0xD6, 0xF7));
            }
            AutomationProperties.SetAutomationId(button, "NotificationAction_" + item.Id);
            button.Click += (_, e) =>
            {
                e.Handled = true;
                ActionPressed?.Invoke(this, item);
            };
            foot.Children.Add(button);
        }
        var time = Text(WhenText(item), 11, FontWeights.Normal, "UiDimText");
        foot.Children.Add(time);
        words.Children.Add(foot);
        Grid.SetColumn(words, 1);
        content.Children.Add(words);

        var row = new Button
        {
            Content = content,
            Margin = new Thickness(0, 0, 0, 2),
            Style = (Style)Application.Current.Resources["LibraryRow"],
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        if (!item.Read) row.Background = new SolidColorBrush(Color.FromArgb(0x0B, 255, 255, 255));
        AutomationProperties.SetAutomationId(row, "NotificationRow_" + item.Id);
        AutomationProperties.SetName(row, item.Title);
        row.Click += (_, _) => Opened?.Invoke(this, item);

        var host = new Grid();
        host.Children.Add(row);

        var side = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 8, 0), Width = 20 };
        var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 2, 0, 10), HorizontalAlignment = HorizontalAlignment.Center };
        if (!item.Read) dot.SetResourceReference(BackgroundProperty, "UiInfo");
        side.Children.Add(dot);
        var dismiss = new Button
        {
            Content = "✕",
            Width = 20,
            Height = 20,
            Style = (Style)Application.Current.Resources["LibraryStar"],
            FontSize = 11,
            ToolTip = TooltipHelper.Wrap(Strings.Get("NotifDismiss")),
        };
        AutomationProperties.SetAutomationId(dismiss, "NotificationDismiss_" + item.Id);
        AutomationProperties.SetName(dismiss, Strings.Get("NotifDismiss"));
        dismiss.Click += (_, _) => Dismissed?.Invoke(this, item);
        side.Children.Add(dismiss);
        host.Children.Add(side);
        return host;
    }

    private static string WhenText(NotificationItem item)
    {
        var day = NotificationView.DayOf(item.CreatedAtUtc, DateTime.Now);
        var local = item.CreatedAtUtc.ToLocalTime();
        return day switch
        {
            NotificationDay.Today => Strings.Format("NotifTimeAgo", RoomAgeFormat.Coarse(DateTime.UtcNow - item.CreatedAtUtc)),
            NotificationDay.Yesterday => Strings.Format("NotifTimeYesterday", local.ToString("HH:mm", Strings.Culture)),
            NotificationDay.ThisWeek => local.ToString("ddd", Strings.Culture),
            _ => local.ToString("d MMM", Strings.Culture),
        };
    }
}
