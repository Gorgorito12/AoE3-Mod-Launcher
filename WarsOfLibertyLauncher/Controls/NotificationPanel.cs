using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// The bell's panel (designs 70a and 120). It lives in the window rather than in a Popup: a
/// transparent Popup is a layered window and loses ClearType, and this one holds paragraphs of text.
///
/// <para>It is as tall as its notices: the list is the one row that gives, so under a
/// <c>MaxHeight</c> the head, the filters and the foot stay still and only the list scrolls.
/// Every mark is drawn — a character such as "↑" or "⚙" comes from whichever symbol font Windows
/// picks, and it never sat in the middle of its disc.</para>
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

    internal const string MarkTag = "NotificationMark";
    internal const string IconTileTag = "NotificationIconTile";
    internal const string EmptyTag = "NotificationEmpty";

    // The sheet's marks, in a 12-unit box.
    private const string MarkUp = "M6 9.6V2.4M2.9 5.4 6 2.3l3.1 3.1";
    private const string MarkInfo = "M6 5.4v3.8M6 2.8v0.2";
    private const string MarkPlus = "M6 2.5v7M2.5 6h7";
    private const string MarkCheck = "M2.6 6.3l2.3 2.3 4.5-5";
    private const string MarkTranslation = "M3 9.5 6 2.5l3 7M4.2 7h3.6";
    private const string MarkAlert = "M6 2.6v4.2M6 9.2v0.2";
    /// <summary>The month's highlights: the sheet has no such notice, so the star is ours. Filled,
    /// because a 1.9 stroke turns a ten-pixel star into a blot.</summary>
    private const string MarkStar = "M6 2.1 7.06 4.84 9.99 5 7.71 6.86 8.47 9.7 6 8.1 3.53 9.7 4.29 6.86 2.01 5 4.94 4.84Z";

    // In a 20-unit box.
    private const string GearPath = "M8.3 2.5h3.4l0.5 2.1 1.5 0.9 2.1-0.6 1.7 2.9-1.6 1.5v1.4l1.6 1.5-1.7 2.9-2.1-0.6-1.5 0.9-0.5 2.1H8.3l-0.5-2.1-1.5-0.9-2.1 0.6-1.7-2.9 1.6-1.5V9.3L2.5 7.8l1.7-2.9 2.1 0.6 1.5-0.9zM10 12.6a2.6 2.6 0 1 0 0-5.2 2.6 2.6 0 0 0 0 5.2z";
    private const string BellPath = "M10 2.5a4.7 4.7 0 0 0-4.7 4.7v3.3L3.7 13.8h12.6l-1.6-3.3V7.2A4.7 4.7 0 0 0 10 2.5zM8.3 16.2a1.8 1.8 0 0 0 3.4 0";

    internal static readonly string[] AllPaths =
        { MarkUp, MarkInfo, MarkPlus, MarkCheck, MarkTranslation, MarkAlert, MarkStar, GearPath, BellPath };

    private NotificationFilter _filter = NotificationFilter.All;
    private IReadOnlyList<NotificationItem> _items = Array.Empty<NotificationItem>();
    private readonly StackPanel _list = new() { Margin = new Thickness(10, 4, 10, 10) };
    private readonly WrapPanel _filters = new() { Margin = new Thickness(18, 12, 18, 14) };
    private readonly TextBlock _title = Text("", 16, FontWeights.SemiBold, "UiTitleText");
    private readonly TextBlock _unread = Text("", 12.5, FontWeights.Normal, "UiMutedText");
    private readonly Button _markAll = Link("NotificationMarkAllRead");
    private readonly Button _clearRead = Link("NotificationClearRead");
    private readonly Button _choose = Link("NotificationChoose");
    private readonly Button _gear;
    private readonly ScrollViewer _scroll;
    private readonly Border _fade;

    public NotificationPanel()
    {
        Width = NotificationPlacement.PanelWidth;
        CornerRadius = new CornerRadius(12);
        BorderThickness = new Thickness(1);
        // Opaque on purpose: backdrop blur is not available to a WPF element over its own window,
        // and at the glass level the toolbar behind it reads straight through the rows.
        SetResourceReference(BackgroundProperty, "LibraryMenuBg");
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x1F, 255, 255, 255));

        var head = new Grid { Margin = new Thickness(18, 16, 14, 0) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _unread.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(_unread, 1);
        Grid.SetColumn(_markAll, 3);
        _markAll.Margin = new Thickness(0, 0, 10, 0);
        _gear = new Button
        {
            Content = Drawn(GearPath, 20, 17, new SolidColorBrush(Color.FromRgb(0xDC, 0xE7, 0xF5)), 1.4),
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            Style = (Style)Application.Current.Resources["LibraryCardButton"],
            Background = new SolidColorBrush(Color.FromArgb(0x0D, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x29, 255, 255, 255)),
        };
        AutomationProperties.SetAutomationId(_gear, "NotificationSettings");
        _gear.Click += (_, _) => ChooseRequested?.Invoke(this, EventArgs.Empty);
        Grid.SetColumn(_gear, 4);
        head.Children.Add(_title);
        head.Children.Add(_unread);
        head.Children.Add(_markAll);
        head.Children.Add(_gear);
        _markAll.Click += (_, _) => MarkAllReadRequested?.Invoke(this, EventArgs.Empty);

        _scroll = new ScrollViewer
        {
            Content = _list,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0),
            Focusable = false,
        };
        AutomationProperties.SetAutomationId(_scroll, "NotificationList");

        // More below: the last notice fades into the panel instead of being cut by the foot.
        _fade = new Border
        {
            Height = 34,
            VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        _scroll.ScrollChanged += (_, _) => RefreshFade();

        var listCell = new Grid();
        listCell.Children.Add(_scroll);
        listCell.Children.Add(_fade);
        var rule = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = listCell };
        rule.SetResourceReference(BorderBrushProperty, "WindowFooterRule");

        var foot = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(18, 12, 18, 13) };
        foot.SetResourceReference(BorderBrushProperty, "WindowFooterRule");
        var footRow = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(_clearRead, Dock.Right);
        _clearRead.SetResourceReference(Control.ForegroundProperty, "UiMutedText");
        footRow.Children.Add(_clearRead);
        footRow.Children.Add(_choose);
        foot.Child = footRow;
        _clearRead.Click += (_, _) => ClearReadRequested?.Invoke(this, EventArgs.Empty);
        _choose.Click += (_, _) => ChooseRequested?.Invoke(this, EventArgs.Empty);

        // Head, filters and foot take what they need; the list is the row that gives. Top-aligned
        // under a MaxHeight, that row is as tall as its notices until the cap and scrolls after it.
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

    /// <summary>The list, for the tests: whether it scrolls is the point of the height rule.</summary>
    internal ScrollViewer ListForTests => _scroll;
    internal Button GearForTests => _gear;

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
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
        };
        link.SetResourceReference(Control.ForegroundProperty, "UiInfoText");
        AutomationProperties.SetAutomationId(link, id);
        return link;
    }

    /// <summary>
    /// A pictogram: the path keeps the box it was drawn in and the Viewbox sizes it, so the stroke
    /// scales with it exactly as the sheet's SVG does.
    /// </summary>
    private static Viewbox Drawn(string data, double box, double size, Brush ink, double stroke, bool filled = false)
    {
        var path = new Path
        {
            Data = Geometry.Parse(data),
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
        if (filled) path.Fill = ink;
        else
        {
            path.Stroke = ink;
            path.StrokeThickness = stroke;
        }
        var canvas = new Canvas { Width = box, Height = box };
        canvas.Children.Add(path);
        return new Viewbox
        {
            Width = size,
            Height = size,
            Child = canvas,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
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
        _gear.ToolTip = TooltipHelper.Wrap(Strings.Get("NotifSettingsTip"));
        AutomationProperties.SetName(_gear, Strings.Get("NotifSettingsTip"));

        _filters.Children.Clear();
        AddFilter(NotificationFilter.All, "NotifFilterAll");
        AddFilter(NotificationFilter.Mods, "NotifFilterMods");
        AddFilter(NotificationFilter.Launcher, "NotifFilterLauncher");

        _list.Children.Clear();
        var groups = NotificationView.Group(_items, _filter, DateTime.Now);
        foreach (var group in groups)
        {
            var label = Text(Strings.Get("NotifDay" + group.Day), 10.5, FontWeights.Bold, "UiMutedText");
            label.Margin = new Thickness(8, 12, 8, 8);
            _list.Children.Add(label);
            foreach (var item in group.Items) _list.Children.Add(Row(item));
        }
        if (groups.Count == 0) _list.Children.Add(Empty());
        PaintFade();
        RefreshFade();
    }

    /// <summary>The fade ends in the panel's own colour, whatever the glass level made it.</summary>
    private void PaintFade()
    {
        var solid = (TryFindResource("LibraryMenuBg") as SolidColorBrush)?.Color ?? Color.FromRgb(0x12, 0x1E, 0x32);
        var fade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(0, solid.R, solid.G, solid.B), 0));
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(0xF5, solid.R, solid.G, solid.B), 1));
        _fade.Background = fade;
    }

    private void RefreshFade() =>
        _fade.Visibility = _scroll.ScrollableHeight - _scroll.VerticalOffset > 1 ? Visibility.Visible : Visibility.Collapsed;

    private UIElement Empty()
    {
        // The list's own margins are 4 above and 10 below: with these it is the sheet's 30 and 32.
        var panel = new StackPanel { Margin = new Thickness(14, 26, 14, 22), HorizontalAlignment = HorizontalAlignment.Center, Tag = EmptyTag };
        var bell = Drawn(BellPath, 20, 26, (Brush)Application.Current.Resources["UiDimText"], 1.3);
        panel.Children.Add(bell);
        var title = Text(Strings.Get("NotifEmpty"), 13.5, FontWeights.SemiBold, "UiBodyText");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.Margin = new Thickness(0, 10, 0, 0);
        panel.Children.Add(title);
        return panel;
    }

    private void AddFilter(NotificationFilter filter, string key)
    {
        var on = filter == _filter;
        var words = new StackPanel { Orientation = Orientation.Horizontal };
        words.Children.Add(new TextBlock { Text = Strings.Get(key), VerticalAlignment = VerticalAlignment.Center });
        var count = NotificationView.Unread(_items, filter);
        if (count > 0) words.Children.Add(CountBadge(count, 17, new Thickness(7, 0, 0, 0)));

        var pill = new Button
        {
            Content = words,
            Style = (Style)Application.Current.Resources["LibraryPill"],
            Margin = new Thickness(0, 0, 6, 0),
            Padding = new Thickness(13, 0, 13, 0),
            FontWeight = FontWeights.SemiBold,
        };
        pill.Background = on ? new SolidColorBrush(Color.FromArgb(0x1A, 255, 255, 255)) : Brushes.Transparent;
        pill.BorderBrush = new SolidColorBrush(on ? Color.FromArgb(0x57, 255, 255, 255) : Color.FromArgb(0x29, 255, 255, 255));
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

    /// <summary>
    /// How a notice is dressed: <c>Tone</c> is the bright colour of the disc's own glyph,
    /// <c>Fill</c> the deeper one of the corner mark, which carries a white glyph.
    /// </summary>
    private readonly record struct MarkDress(string Tone, Brush Fill, Brush Ink, Color Soft, Color Line, string Path, bool Filled);

    private static MarkDress Dress(NotificationItem item)
    {
        var tone = NotificationView.ToneOf(item);
        var (brush, r, g, b) = tone switch
        {
            NotificationTone.Ok => ("UiOk", (byte)0x4F, (byte)0xD6, (byte)0x8A),
            NotificationTone.Error => ("UiError", (byte)0xF4, (byte)0x90, (byte)0x0C),
            NotificationTone.Highlight => ("AccentBrush", (byte)0xD8, (byte)0xB6, (byte)0x6A),
            _ => ("UiInfo", (byte)0x55, (byte)0xAC, (byte)0xEE),
        };
        Brush fill = tone switch
        {
            NotificationTone.Ok => new SolidColorBrush(Color.FromRgb(0x2E, 0x9D, 0x63)),
            NotificationTone.Error => (Brush)Application.Current.Resources["UiError"],
            NotificationTone.Highlight => (Brush)Application.Current.Resources["AccentBrush"],
            _ => (Brush)Application.Current.Resources["MpAction"],
        };
        // Gold is a light fill and wants a dark glyph; the other three carry white.
        Brush ink = tone == NotificationTone.Highlight ? new SolidColorBrush(Color.FromRgb(0x2B, 0x1F, 0x06)) : Brushes.White;
        var path = item.Kind switch
        {
            NotificationKind.UpdateAvailable or NotificationKind.LauncherUpdate or NotificationKind.ModPatchPublished => MarkUp,
            NotificationKind.NewTranslation => MarkTranslation,
            NotificationKind.NewMod => MarkPlus,
            NotificationKind.Announcement => MarkInfo,
            NotificationKind.MonthlyHighlights or NotificationKind.AchievementUnlocked => MarkStar,
            _ => tone == NotificationTone.Error ? MarkAlert : MarkCheck,
        };
        return new MarkDress(brush, fill, ink, Color.FromArgb(0x24, r, g, b), Color.FromArgb(0x66, r, g, b), path, path == MarkStar);
    }

    private UIElement Row(NotificationItem item)
    {
        var dress = Dress(item);
        var content = new Grid { Margin = new Thickness(11, 11, 9, 11) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        content.ColumnDefinitions.Add(new ColumnDefinition());
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });

        var icon = IconFor?.Invoke(item);
        var mark = new Grid { Width = 36, Height = 36, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0) };
        if (icon != null)
        {
            // A navy tile under the picture, for an icon with transparent corners.
            mark.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x2C, 0x48)),
            });
            mark.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(9),
                Background = icon,
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x24, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Tag = IconTileTag,
            });
            // Seventeen across with a two-pixel ring in the row's own navy, five past the corner.
            mark.Children.Add(new Border
            {
                Width = 21,
                Height = 21,
                CornerRadius = new CornerRadius(10.5),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, -7, -7),
                BorderThickness = new Thickness(2),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x14, 0x23, 0x3A)),
                Background = dress.Fill,
                Child = Drawn(dress.Path, 12, 10, dress.Ink, 1.9, dress.Filled),
                Tag = MarkTag,
            });
        }
        else
        {
            mark.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(18),
                Background = new SolidColorBrush(dress.Soft),
                BorderBrush = new SolidColorBrush(dress.Line),
                BorderThickness = new Thickness(1),
                Child = Drawn(dress.Path, 12, 15, (Brush)Application.Current.Resources[dress.Tone], 1.7, dress.Filled),
                Tag = MarkTag,
            });
        }
        content.Children.Add(mark);

        var words = new StackPanel { Margin = new Thickness(12, 0, 12, 0) };
        var title = Text(item.Title, 13.5, FontWeights.SemiBold, item.Read ? "UiSoftText" : "UiTitleText");
        title.TextWrapping = TextWrapping.Wrap;
        var body = Text(item.Body, 12.5, FontWeights.Normal, "UiMutedText");
        body.TextWrapping = TextWrapping.Wrap;
        body.TextTrimming = TextTrimming.CharacterEllipsis;
        body.LineHeight = 17.5;
        body.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        body.MaxHeight = 35;
        body.Margin = new Thickness(0, 4, 0, 0);
        words.Children.Add(title);
        words.Children.Add(body);

        var foot = new WrapPanel { Margin = new Thickness(0, 9, 0, 0) };
        var action = NotificationView.ActionOf(item.Kind);
        if (action != NotificationAction.None)
        {
            var button = new Button
            {
                Content = Strings.Get("NotifAction" + action),
                Style = (Style)Application.Current.Resources["LibraryCardButton"],
                Height = 28,
                Padding = new Thickness(12, 0, 12, 0),
                FontSize = 12,
                Margin = new Thickness(0, 0, 10, 0),
            };
            if (action is NotificationAction.Update or NotificationAction.RestartAndUpdate)
            {
                button.Background = new SolidColorBrush(Color.FromArgb(0x38, 0x2F, 0x7F, 0xE0));
                button.BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, 0x55, 0xAC, 0xEE));
                button.Foreground = new SolidColorBrush(Color.FromRgb(0xCF, 0xE6, 0xFB));
            }
            AutomationProperties.SetAutomationId(button, "NotificationAction_" + item.Id);
            button.Click += (_, e) =>
            {
                e.Handled = true;
                ActionPressed?.Invoke(this, item);
            };
            foot.Children.Add(button);
        }
        var time = Text(WhenText(item), 11.5, FontWeights.Normal, "UiDimText");
        foot.Children.Add(time);
        words.Children.Add(foot);
        Grid.SetColumn(words, 1);
        content.Children.Add(words);

        var row = new Button
        {
            Content = content,
            Style = (Style)Application.Current.Resources["LibraryRow"],
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetAutomationId(row, "NotificationRow_" + item.Id);
        AutomationProperties.SetName(row, item.Title);
        row.Click += (_, _) => Opened?.Invoke(this, item);

        // Every notice on its own plate, behind the button so the button's hover still shows; the
        // dot, not the plate, tells a read one from an unread one.
        var host = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        host.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(0x0B, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x0D, 255, 255, 255)),
            BorderThickness = new Thickness(1),
        });
        host.Children.Add(row);

        var side = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 13, 10, 0), Width = 20 };
        var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 2, 0, 6), HorizontalAlignment = HorizontalAlignment.Center };
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
