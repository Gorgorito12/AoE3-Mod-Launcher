using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// The Tournaments page (design 80): the list on the left, folding to 72 px, and the chosen
/// tournament on the right over its mod's image, in its colour.
///
/// <para><b>What the player has to do goes first, in one card with the one primary button.</b> The
/// rest is three tabs rather than one long page: Cuadro, Participantes and, for whoever organises,
/// Organizar. <see cref="TournamentPage"/> decides what each says; nothing here decides anything
/// about the tournament.</para>
///
/// <para><b>All three tabs are built once per tournament and shown by Visibility</b>, so changing
/// tab, zooming, searching and picking a match touch what they change and rebuild nothing: a 64
/// draw is sixty-three cards and those are built once. The cards live on a Canvas at positions that
/// are arithmetic, so nothing measures them.</para>
/// </summary>
public partial class MultiplayerTab
{
    private Action<Models.ModProfile?>? _setTournamentBackdrop;

    private static readonly SolidColorBrush PanelRim = Frozen(0x1A, 0xFF, 0xFF, 0xFF);
    private static readonly SolidColorBrush SecondaryRim = Frozen(0x38, 0x82, 0xAF, 0xFF);

    private bool _tournamentListOpen = true;
    private string _tournamentFilter = "all";
    private string _tournamentQuery = "";
    private double _bracketZoom = 100;

    /// <summary>The zoom the player chose for a tournament this session; one not here fits itself
    /// (<see cref="BracketZoom.Auto"/>), and the last fit is kept so a repaint does not jump.</summary>
    private readonly Dictionary<string, double> _bracketZoomManual = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _bracketZoomAuto = new(StringComparer.Ordinal);

    /// <summary>The sheet's size at 100 %, for the fit.</summary>
    private Size _bracketSheetSize;

    /// <summary>How far right the centred sheet starts, so the round titles stay over their columns.</summary>
    private double _bracketHeadOffset;
    private readonly Dictionary<string, TournamentTab> _tournamentTabs = new(StringComparer.Ordinal);

    private TournamentDetail? _pageTournament;
    private Color _pageAccent = ModAccent.Navy;

    /// <summary>The open tournament's mod colour on the page's chrome; null with none open.</summary>
    private TournamentAmbient? _ambient;

    /// <summary>The picked tab or filter: the mod's selection fill, the old blue with none open.</summary>
    private Brush ActiveChipFill() => _ambient is { } a ? Solid(a.ActiveFill) : Ink("#233A5C");

    private Brush ActiveChipRim() => _ambient is { } a ? Solid(a.ActiveRim) : Brushes.Transparent;

    /// <summary>The accent the filters were last painted in, so a new tournament repaints them.</summary>
    private Color? _filtersAccent;

    /// <summary>A panel's rim in the mod's colour, lit from the top.</summary>
    private Brush AmbientRim()
    {
        if (_ambient is not { } a) return PanelRim;
        var rim = new LinearGradientBrush { StartPoint = new Point(.5, 0), EndPoint = new Point(.5, 1) };
        rim.GradientStops.Add(new GradientStop(a.PanelRimTop, 0));
        rim.GradientStops.Add(new GradientStop(a.PanelRimBottom, .55));
        rim.Freeze();
        return rim;
    }
    private readonly Dictionary<string, Border> _bracketCards = new(StringComparer.Ordinal);
    /// <summary>The sheet the bracket was last drawn from: where each card sits, for the round chips.</summary>
    private IReadOnlyList<MatchBox> _bracketBoxes = Array.Empty<MatchBox>();
    private ScrollViewer? _bracketScroll;

    /// <summary>
    /// Drag the bracket to move it (rule in <see cref="BracketPan"/>). Tunnelling handlers on the
    /// scroller, so a drag that started on a card ends with the card's click swallowed, and a short
    /// press is left alone for the card. Empty space is hit-testable through a transparent fill.
    /// </summary>
    private static void AttachBracketPan(ScrollViewer scroll)
    {
        if (scroll.Background == null) scroll.Background = Brushes.Transparent;
        Point start = default;
        double startX = 0, startY = 0;
        bool armed = false, panning = false;

        static bool OnScrollBar(object source)
        {
            for (var d = source as DependencyObject; d != null; d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d))
            {
                if (d is System.Windows.Controls.Primitives.ScrollBar) return true;
                if (d is ScrollViewer) return false;
            }
            return false;
        }

        scroll.PreviewMouseLeftButtonDown += (_, e) =>
        {
            armed = BracketPan.MayPan(scroll.ExtentWidth, scroll.ExtentHeight, scroll.ViewportWidth, scroll.ViewportHeight,
                OnScrollBar(e.OriginalSource));
            panning = false;
            if (!armed) return;
            start = e.GetPosition(scroll);
            startX = scroll.HorizontalOffset;
            startY = scroll.VerticalOffset;
        };
        scroll.PreviewMouseMove += (_, e) =>
        {
            if (!armed || e.LeftButton != MouseButtonState.Pressed) return;
            var now = e.GetPosition(scroll);
            double dx = now.X - start.X, dy = now.Y - start.Y;
            if (!panning)
            {
                if (!BracketPan.Starts(dx, dy)) return;
                panning = scroll.CaptureMouse();
                if (!panning) { armed = false; return; }
                scroll.Cursor = Cursors.SizeAll;
            }
            var (x, y) = BracketPan.Offsets(startX, startY, dx, dy, scroll.ScrollableWidth, scroll.ScrollableHeight);
            scroll.ScrollToHorizontalOffset(x);
            scroll.ScrollToVerticalOffset(y);
            e.Handled = true;
        };
        scroll.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (panning)
            {
                scroll.ReleaseMouseCapture();
                scroll.Cursor = null;
                e.Handled = true;
            }
            armed = false;
            panning = false;
        };
        scroll.LostMouseCapture += (_, _) =>
        {
            if (!panning) return;
            scroll.Cursor = null;
            panning = false;
            armed = false;
        };
    }
    private ScaleTransform? _bracketScale;
    private TranslateTransform? _bracketHeadShift;
    private FrameworkElement? _bracketHost, _bracketHeads, _bracketSheetHost;
    private Grid? _bracketPick;
    private StackPanel? _peopleBody;
    private FrameworkElement? _bracketEmptyView, _peopleView, _organiseView, _tournamentContentCard, _tournamentCallCard;
    private FrameworkElement? _tournamentStepsRow, _tournamentCallSub, _bracketTools;
    private TextBlock? _tournamentTitle, _tournamentSearchCount, _zoomText;
    private Border? _zoomBox;
    private Grid? _tournamentHead;
    private FlexLine? _tournamentTabRow;
    private TextBox? _tournamentSearch;
    private MatchBox? _mineBox;

    private static SolidColorBrush Frozen(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush Ink(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush Tint(Color c, double alpha)
    {
        var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(alpha * 255), c.R, c.G, c.B));
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush Solid(Color c)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    }

    private static TextBlock Say(string text, string sizeKey, string ink, FontWeight? weight = null, Thickness? margin = null, bool mono = false)
    {
        var line = new TextBlock { Text = text, Foreground = Ink(ink), VerticalAlignment = VerticalAlignment.Center };
        if (weight is { } w) line.FontWeight = w;
        if (margin is { } m) line.Margin = m;
        if (mono) line.FontFamily = new FontFamily("Consolas, Courier New, monospace");
        line.SetResourceReference(TextBlock.FontSizeProperty, sizeKey);
        return line;
    }

    private static Border Pill(UIElement child, double height, double left, double right, Brush? fill, Brush? rim, double radius)
        => new()
        {
            Height = height,
            Padding = new Thickness(left, 0, right, 0),
            CornerRadius = new CornerRadius(radius),
            Background = fill,
            BorderBrush = rim,
            BorderThickness = new Thickness(rim == null ? 0 : 1),
            Child = child,
            VerticalAlignment = VerticalAlignment.Center,
        };

    private static Ellipse Dot(double size, Brush fill, double right = 7) => new()
    {
        Width = size,
        Height = size,
        Fill = fill,
        Margin = new Thickness(0, 0, right, 0),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static Button Secondary(string caption, Action click, double height = 32, Brush? ink = null, Brush? rim = null)
    {
        var button = new Button { Content = caption, Height = height };
        button.SetResourceReference(FrameworkElement.StyleProperty, "WindowSecondaryButton");
        button.SetResourceReference(Control.FontSizeProperty, "SetRowDescSize");
        if (ink != null) button.Foreground = ink;
        if (rim != null) button.BorderBrush = rim;
        button.Click += (_, _) => click();
        return button;
    }

    private Color AccentOfTournament(TournamentSummary t)
    {
        var mod = string.IsNullOrEmpty(t.ModId) ? null : ModRegistry.Find(t.ModId);
        return mod != null ? ModTheme.AccentOf(mod.AccentColor, _config?.UseModColor ?? true) : (Color)ColorConverter.ConvertFromString("#2F7FE0");
    }

    // ---------------------------------------------------------------- the list

    private void RenderTournamentsList()
    {
        var panel = TournamentsListPanel;
        if (panel == null) return;
        panel.Children.Clear();
        TournamentsCollapsedPanel.Children.Clear();

        bool open = _tournamentListOpen;
        TournamentsListColumn.Width = new GridLength(open ? ListWidth() : 72);
        TournamentsListOpenView.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        TournamentsListClosedView.Visibility = open ? Visibility.Collapsed : Visibility.Visible;

        if (_tournamentsUnavailable)
        {
            panel.Children.Add(Hint(Strings.Get("MpTournamentsUnavailable")));
            return;
        }

        var me = TournamentViewerId(_tournamentDetail);
        var rows = TournamentPage.Rows(_tournaments, me, _tournamentDetail, _tournamentFilter);
        // Every tournament the list draws, private ones from `mine` included, so the heading and
        // the "All" filter cannot disagree.
        TournamentsTitleCount.Text = TournamentPage.Listed(_tournaments, _tournamentDetail).Count().ToString();
        RenderTournamentFilters();

        if (rows.Count == 0)
        {
            // "Nothing to list" only once the server has said so. Before its first answer the
            // list is loading, and after a failed one it is unknown: neither is empty.
            if (_tournaments == null && !_demoTournaments)
            {
                if (_tournamentsFetchedUtc == DateTime.MinValue) panel.Children.Add(StatsSkeletonRows(1));
                else panel.Children.Add(Hint(Strings.Get("MpTournamentsLoadFailed")));
                return;
            }
            panel.Children.Add(Hint(Strings.Get("MpTournamentsEmpty")));
            return;
        }

        foreach (var group in rows.GroupBy(r => r.Group))
        {
            var key = group.Key switch
            {
                TournamentRowGroup.Playing => "TpGroupPlaying",
                TournamentRowGroup.Organising => "TpGroupOrganising",
                _ => "TpGroupOthers",
            };
            var head = Say(Strings.Get(key), "SetGroupLabelSize", "#8C9CB1", FontWeights.Bold, new Thickness(10, 10, 10, 5));
            ModDialogChrome.SetTightLine(1, head);
            panel.Children.Add(head);
            foreach (var row in group)
            {
                panel.Children.Add(BuildTournamentRow(row));
                TournamentsCollapsedPanel.Children.Add(BuildCollapsedTournament(row));
            }
        }
    }

    /// <summary>The list's <c>clamp(300px, 21cqw, 420px)</c>.</summary>
    private double ListWidth() => Math.Clamp(FrameSize().Width * .21, 300, 420);

    /// <summary>The design's sizes are fractions of the window, not of this tab.</summary>
    private Size FrameSize()
    {
        var window = Window.GetWindow(this);
        return window is { ActualWidth: > 0 } ? new Size(window.ActualWidth, window.ActualHeight) : new Size(ActualWidth, ActualHeight + 96);
    }

    private void TournamentsView_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyTournamentMetrics();

    /// <summary>It tells every entrant and cannot be undone, so it never happens on one click.</summary>
    private async Task ConfirmCancelTournamentAsync(TournamentDetail t)
    {
        bool ok = await MpAlertOverlay.ConfirmAsync(
            TabRootGrid,
            Strings.Get("MpTournamentCancelTitle"),
            Strings.Format("MpTournamentCancelBody", (t.Entrants ?? new List<TournamentEntrant>()).Count(e => e.Status == "confirmed")),
            Strings.Get("MpTournamentCancel"),
            Strings.Get("MpAlertCancel"));
        if (!ok) return;

        await RunTournamentActionAsync(() => TournamentApi.CancelTournamentAsync(t.Id));
    }

    /// <summary>Deleting takes it away for everybody and cannot be undone, so it asks first, as a
    /// destructive confirmation. The refresh that follows finds it gone and clears the selection.</summary>
    private async Task ConfirmDeleteTournamentAsync(TournamentDetail t)
    {
        bool ok = await MpAlertOverlay.ConfirmAsync(
            TabRootGrid,
            Strings.Get("TpDeleteTitle"),
            Strings.Format("TpDeleteBody", t.Name),
            Strings.Get("TpDeleteTournament"),
            Strings.Get("MpAlertCancel"));
        if (!ok) return;

        await RunTournamentActionAsync(() => TournamentApi.DeleteTournamentAsync(t.Id));
        // The refresh found it gone and cleared the selection; open the first one left rather than
        // leave an empty page.
        if (_selectedTournamentId == null
            && TournamentPage.Rows(_tournaments, _session?.CurrentUser?.Id ?? (_demoTournaments ? TournamentDemoData.MeUserId : null), null, "all")
                .FirstOrDefault() is { } next)
            await SelectTournamentAsync(next.Summary.Id);
    }

    /// <summary>What a button holding a panel says to a screen reader: without it, nothing.</summary>
    private static void Spoken(FrameworkElement element, string id, string name)
    {
        System.Windows.Automation.AutomationProperties.SetAutomationId(element, id);
        System.Windows.Automation.AutomationProperties.SetName(element, name);
    }

    private void RenderTournamentFilters()
    {
        TournamentsFilters.Children.Clear();
        var all = TournamentPage.Listed(_tournaments).ToList();
        foreach (var (key, caption) in new[]
                 {
                     ("all", "TpFilterAll"), ("registration", "TpFilterRegistration"),
                     ("running", "TpFilterRunning"), ("finished", "TpFilterFinished"),
                 })
        {
            bool on = _tournamentFilter == key;
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            if (key != "all") line.Children.Add(Dot(7, StatusDot(key), 7));
            line.Children.Add(Say(Strings.Get(caption), "SetControlSize", on ? "#FFFFFF" : "#A8BCD2", on ? FontWeights.SemiBold : FontWeights.Medium));
            line.Children.Add(Say((key == "all" ? all.Count : all.Count(t => t.Status == key)).ToString(), "SetCaptionSize",
                on ? "#C3D2E5" : "#8C9CB1", FontWeights.SemiBold, new Thickness(6, 0, 0, 0), mono: true));
            var chip = new Button
            {
                Content = line,
                Height = 30,
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(0, 0, 6, 6),
                Background = on ? ActiveChipFill() : Frozen(0x80, 0x0A, 0x13, 0x21),
                BorderBrush = on ? ActiveChipRim() : Frozen(0x1A, 0xFF, 0xFF, 0xFF),
                Tag = 15d,
            };
            chip.SetResourceReference(StyleProperty, "TournamentChip");
            Spoken(chip, "TournamentFilter:" + key, Strings.Get(caption));
            chip.Click += (_, _) =>
            {
                _tournamentFilter = key;
                RenderTournamentsList();
            };
            TournamentsFilters.Children.Add(chip);
        }
    }

    internal Button BuildTournamentRow(TournamentListRow row)
    {
        var t = row.Summary;
        bool on = string.Equals(t.Id, _selectedTournamentId, StringComparison.Ordinal);
        var accent = AccentOfTournament(t);

        var icon = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(8), Background = TournamentIcon(t) };

        var name = Say(t.Name, "SetRowTitleSize", "#F0F5FB", FontWeights.SemiBold);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        var subLine = new Grid { Margin = new Thickness(0, 5, 0, 0) };
        subLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        subLine.ColumnDefinitions.Add(new ColumnDefinition());
        subLine.Children.Add(Dot(7, StatusDot(t.Status), 6));
        var sub = Say(row.Sub, "SetControlSize", "#A8BCD2");
        sub.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(sub, 1);
        subLine.Children.Add(sub);
        var words = new StackPanel { Margin = new Thickness(11, 0, 11, 0), VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(name);
        words.Children.Add(subLine);
        Grid.SetColumn(words, 1);

        var flags = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        if (t.IsPrivate)
        {
            var tag = PrivateTournamentTag();
            tag.HorizontalAlignment = HorizontalAlignment.Right;
            tag.Margin = new Thickness(0, 0, 0, row.Mine || row.Alert != null ? 6 : 0);
            flags.Children.Add(tag);
        }
        if (row.Mine)
        {
            flags.Children.Add(new Border
            {
                Height = 20,
                Padding = new Thickness(8, 0, 8, 0),
                CornerRadius = new CornerRadius(10),
                Background = Frozen(0x8C, 0x0A, 0x13, 0x21),
                BorderBrush = Frozen(0x24, 0xFF, 0xFF, 0xFF),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Right,
                Child = Say(Strings.Get("TpMineTag"), "MpTagSize", "#DCE7F5", FontWeights.Bold),
            });
        }
        if (row.Alert != null)
        {
            var alert = Say(row.Alert, "SetCaptionSize", row.AlertIsRequests ? "#F5BD76" : ToHex(ModAccent.TextOn(accent)),
                FontWeights.SemiBold, new Thickness(0, row.Mine ? 6 : 0, 0, 0));
            alert.HorizontalAlignment = HorizontalAlignment.Right;
            flags.Children.Add(alert);
        }
        Grid.SetColumn(flags, 2);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(icon);
        grid.Children.Add(words);
        grid.Children.Add(flags);

        var button = new Button
        {
            Content = grid,
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 2),
            Background = on ? Solid(ModAccent.SelectionFill(accent)) : Brushes.Transparent,
            BorderBrush = on ? Tint(accent, .6) : Brushes.Transparent,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Tag = 10d,
        };
        button.SetResourceReference(StyleProperty, "TournamentChip");
        System.Windows.Automation.AutomationProperties.SetName(button, t.Name);
        button.Click += (_, _) => _ = SelectTournamentAsync(t.Id);
        return button;
    }

    /// <summary>
    /// The "Private" chip of a private tournament (2026-11-13), in the purple a password room
    /// already wears, so the two kinds of "not for everybody" read as one family.
    /// </summary>
    internal static Border PrivateTournamentTag()
    {
        var text = new TextBlock
        {
            Text = Strings.Get("TpPrivateTag"),
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        text.SetResourceReference(TextBlock.FontSizeProperty, "MpTagSize");
        text.SetResourceReference(TextBlock.ForegroundProperty, "MpPrivateText");
        var tag = new Border
        {
            Height = 20,
            Padding = new Thickness(8, 0, 8, 0),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Child = text,
            ToolTip = TooltipHelper.Wrap(Strings.Get("TpPrivateTagTip")),
        };
        tag.SetResourceReference(Border.BackgroundProperty, "MpPrivateBg");
        tag.SetResourceReference(Border.BorderBrushProperty, "MpPrivateRim");
        return tag;
    }

    /// <summary>
    /// "Join with code": the way into a private tournament nobody can see yet. The dialog looks the
    /// code up itself, so a wrong one is said beside the field, and the API remembers the code
    /// for the rest of the session so the detail and the entry carry it.
    /// </summary>
    private void TournamentJoinCode_Click(object sender, RoutedEventArgs e)
    {
        if (TournamentApiOrNull is not { } api) return;
        var dlg = new JoinTournamentCodeDialog(async code =>
        {
            try
            {
                var found = await api.FindTournamentByCodeAsync(code);
                return (found.Id, null);
            }
            catch (LobbyApiException ex) when (ex.Code == "not_found" || ex.Status == 404)
            {
                return (null, Strings.Get("TpJoinCodeNotFound"));
            }
            catch (Exception ex)
            {
                Services.DiagnosticLog.Write($"Tournaments: looking up an invite code failed - {ex.GetType().Name}: {ex.Message}");
                return (null, Strings.Get("TpJoinCodeFailed"));
            }
        });
        if (dlg.ShowOver(Window.GetWindow(this)) != true || dlg.FoundTournamentId is not { } id) return;
        _ = SelectTournamentAsync(id);
    }

    private Button BuildCollapsedTournament(TournamentListRow row)
    {
        var t = row.Summary;
        bool on = string.Equals(t.Id, _selectedTournamentId, StringComparison.Ordinal);
        var accent = AccentOfTournament(t);
        var body = new Grid { Width = 36, Height = 36 };
        body.Children.Add(new Border { CornerRadius = new CornerRadius(8), Background = TournamentIcon(t) });
        body.Children.Add(new Ellipse
        {
            Width = 12,
            Height = 12,
            Fill = StatusDot(t.Status),
            Stroke = Ink("#0F1C2E"),
            StrokeThickness = 2,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, -3, -3),
        });
        var button = new Button
        {
            Content = body,
            Width = 44,
            Height = 44,
            Padding = new Thickness(3),
            Background = Brushes.Transparent,
            BorderBrush = on ? Solid(ModAccent.TextOn(accent)) : Brushes.Transparent,
            ToolTip = TooltipHelper.Wrap(t.Name),
            Tag = 11d,
        };
        button.SetResourceReference(StyleProperty, "TournamentChip");
        Spoken(button, "TournamentRow:" + t.Id, t.Name);
        button.Click += (_, _) => _ = SelectTournamentAsync(t.Id);
        return button;
    }

    private static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private Brush TournamentIcon(TournamentSummary t)
    {
        var mod = string.IsNullOrEmpty(t.ModId) ? null : ModRegistry.Find(t.ModId);
        return (mod == null ? null : (Brush?)ResolveRoomModIcon(mod)) ?? Ink("#1A2C48");
    }

    /// <summary>A tournament's state colour, the same in the chip, the list's dot, the filters and
    /// the steps (design 106): registration green, being played blue, finished gold. Never the
    /// mod's colour.</summary>
    private static Brush StatusDot(string? status) => Solid(StateColors.Mark(StateColors.ForTournament(status)));

    private static string StatusInk(string? status) => ToHex(StateColors.Text(StateColors.ForTournament(status)));

    /// <summary><paramref name="top"/> at <paramref name="topAlpha"/> laid over the page's navy at
    /// <paramref name="underAlpha"/>: the design's two stacked fills as one brush.</summary>
    private static SolidColorBrush Over(Color top, double topAlpha, double underAlpha)
    {
        var under = Color.FromRgb(0x0A, 0x13, 0x21);
        double a = topAlpha + underAlpha * (1 - topAlpha);
        byte Ch(byte t, byte u) => (byte)Math.Round((t * topAlpha + u * underAlpha * (1 - topAlpha)) / a);
        return Frozen((byte)Math.Round(a * 255), Ch(top.R, under.R), Ch(top.G, under.G), Ch(top.B, under.B));
    }

    /// <summary>A tick drawn in a 20-px box (<c>M4.5 10.5l3.8 3.8 7.2-8.3</c>), never the character.</summary>
    internal static FrameworkElement DrawnTick(double size, Brush ink, double thickness = 2.4)
    {
        var path = new Path
        {
            Data = Geometry.Parse("M4.5 10.5l3.8 3.8 7.2-8.3"),
            Stroke = ink,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
        // A Grid, not a Canvas: the bracket is the page's one Canvas, and tests find it by that.
        var box = new Grid { Width = 20, Height = 20 };
        box.Children.Add(path);
        return new Viewbox { Width = size, Height = size, Child = box, IsHitTestVisible = false };
    }

    private void TournamentsFold_Click(object sender, RoutedEventArgs e)
    {
        _tournamentListOpen = !_tournamentListOpen;
        RenderTournamentsList();
        if (_tournamentListOpen) ApplyTournamentMetrics();
        _setTournamentBackdrop?.Invoke(_pageTournament?.ModId is { Length: > 0 } id ? ModRegistry.Find(id) : null);
    }

    /// <summary>"Mis equipos" with the count of invitations waiting in its own badge.</summary>
    private void UpdateMyTeamsButton()
    {
        if (TournamentsMyTeamsButton == null) return;
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(new TextBlock { Text = Strings.Get("MpTeamsButton"), VerticalAlignment = VerticalAlignment.Center });
        if (_teamInviteCount > 0)
        {
            var count = Say(_teamInviteCount.ToString(), "SetCaptionSize", "#0F1C2E", FontWeights.Bold);
            count.HorizontalAlignment = HorizontalAlignment.Center;
            line.Children.Add(new Border { MinWidth = 18, Height = 18, Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(7, 0, 0, 0), CornerRadius = new CornerRadius(9), Background = Ink("#55ACEE"), Child = count });
        }
        TournamentsMyTeamsButton.Content = line;
        System.Windows.Automation.AutomationProperties.SetName(TournamentsMyTeamsButton, Strings.Get("MpTeamsButton"));
    }

    // ---------------------------------------------------------------- the page

    private void RenderTournamentDetail()
    {
        var panel = TournamentDetailPanel;
        if (panel == null) return;
        panel.Children.Clear();
        _bracketCards.Clear();
        _bracketBoxes = Array.Empty<MatchBox>();
        _mineBox = null;

        var t = _tournamentDetail;
        _pageTournament = t;
        _ambient = t == null ? null : TournamentAmbient.For(AccentOfTournament(t));
        TournamentsListPane.BorderBrush = AmbientRim();
        Color? accent = t == null ? null : AccentOfTournament(t);
        if (accent != _filtersAccent)
        {
            _filtersAccent = accent;
            RenderTournamentFilters();
        }
        if (t == null)
        {
            _setTournamentBackdrop?.Invoke(null);
            if (!_tournamentsUnavailable) panel.Children.Add(Hint(Strings.Get("MpTournamentsPickOne")));
            return;
        }

        var me = TournamentViewerId(t);
        _pageAccent = AccentOfTournament(t);
        _bracketZoom = _bracketZoomManual.TryGetValue(t.Id, out var chosen) ? chosen
            : _bracketZoomAuto.TryGetValue(t.Id, out var fitted) ? fitted : 100;
        if (!_tournamentTabs.ContainsKey(t.Id)) _tournamentTabs[t.Id] = TournamentPage.DefaultTab(t, me);
        // The tab that was open may not exist any more: an organiser's, after losing the role.
        if (_tournamentTabs[t.Id] == TournamentTab.Organise && !TournamentPermissions.IsOwnerOrManager(t, me))
            _tournamentTabs[t.Id] = TournamentTab.Bracket;

        var page = new Grid();
        for (int row = 0; row < 4; row++) page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition());
        panel.Children.Add(page);

        if (_demoTournaments)
        {
            // A populated bracket looks exactly like a real one, and a screenshot without this
            // strip ends up somewhere looking like tournaments already work.
            page.Children.Add(BuildTournamentPreviewBanner(t));
        }

        var head = (FrameworkElement)BuildTournamentHeader(t, me);
        head.Margin = new Thickness(0, 0, 0, 14);
        Grid.SetRow(head, 1);
        page.Children.Add(head);

        var content = BuildTournamentContent(t, me);
        var tools = BuildTournamentTools(t);
        Grid.SetRow(tools, 2);
        page.Children.Add(tools);
        // The preview's own strip for the picked match: who won it, or the other side opening its
        // room. Outside the bracket: nothing but the bracket's cards may live in there.
        if (_demoTournaments && t.Matches is { Count: > 0 } && BuildTournamentPreviewMatchStrip(t) is FrameworkElement strip)
        {
            strip.Margin = new Thickness(0, 0, 0, 14);
            Grid.SetRow(strip, 3);
            page.Children.Add(strip);
        }
        Grid.SetRow(content, 4);
        page.Children.Add(content);

        ShowTournamentTab(t, _tournamentTabs[t.Id]);
        ApplyTournamentMetrics();
        _setTournamentBackdrop?.Invoke(_activeSubtab != Subtab.Tournaments || string.IsNullOrEmpty(t.ModId) ? null : ModRegistry.Find(t.ModId));
    }

    /// <summary>The tab is transparent and its sub-bar glass whether a cover runs behind it or the
    /// MainWindow's own page colour does (NeutralBackdrop). It used to turn the sub-bar into solid
    /// MpPanel without a cover, which is what made Multiplayer's bars read as opaque.</summary>
    internal void SetCoverBehind(bool cover)
    {
        TabRootGrid.Background = Brushes.Transparent;
        SubBar.SetResourceReference(Border.BackgroundProperty, "RmSubBarBg");
    }

    /// <summary>Title size, steps and the card's subtitle follow the window: under 900 tall the
    /// head gives up the steps. Writes nothing that can move the size that triggered it: the page
    /// is a star row.</summary>
    private void ApplyTournamentMetrics()
    {
        if (_tournamentTitle == null) return;
        var frame = FrameSize();
        double width = frame.Width;
        bool compact = frame.Height < 900;
        _tournamentTitle.FontSize = compact ? Math.Clamp(width * .018, 24, 34) : Math.Clamp(width * .021, 28, 44);
        if (_tournamentStepsRow != null) _tournamentStepsRow.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        if (_tournamentCallSub != null) _tournamentCallSub.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        if (_tournamentListOpen) TournamentsListColumn.Width = new GridLength(ListWidth());
    }

    /// <summary>The head of the page: chips, the name, its line of facts and the five steps, with
    /// the card of what the viewer has to do beside them, or below on a narrow window.</summary>
    internal UIElement BuildTournamentHeader(TournamentDetail t, string? me)
    {
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };

        var chips = new WrapPanel();
        var mod = string.IsNullOrEmpty(t.ModId) ? null : ModRegistry.Find(t.ModId);
        Brush chipFill = Frozen(0x8C, 0x0A, 0x13, 0x21), chipRim = Frozen(0x1F, 0xFF, 0xFF, 0xFF);
        if (mod != null)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 7, 0), Background = TournamentIcon(t) });
            // The mod's own chip wears its colour; the state chip beside it keeps the state's.
            var ambient = _ambient ?? TournamentAmbient.For(_pageAccent);
            line.Children.Add(Say(ResolveModDisplayName(mod.Id), "SetControlSize", ToHex(ambient.ChipText), FontWeights.SemiBold));
            var chip = Pill(line, 26, 5, 10, Solid(ambient.ChipFill), Solid(ambient.ChipRim), 13);
            chip.Margin = new Thickness(0, 0, 8, 6);
            chips.Children.Add(chip);
        }
        var state = new StackPanel { Orientation = Orientation.Horizontal };
        var stateDot = Dot(7, StatusDot(t.Status));
        if (StateColors.Pulses(t.Status)) AttachChipPulse(stateDot);
        state.Children.Add(stateDot);
        state.Children.Add(Say(StatusLabel(t.Status, false), "SetControlSize", StatusInk(t.Status), FontWeights.SemiBold));
        var stateTone = StateColors.Mark(StateColors.ForTournament(t.Status));
        var stateChip = Pill(state, 26, 11, 11, Over(stateTone, .2, .72), Tint(stateTone, .55), 13);
        stateChip.Margin = new Thickness(0, 0, 8, 6);
        chips.Children.Add(stateChip);
        if (t.IsPrivate)
        {
            var tag = PrivateTournamentTag();
            tag.Height = 26;
            tag.CornerRadius = new CornerRadius(13);
            tag.Padding = new Thickness(10, 0, 10, 0);
            tag.Margin = new Thickness(0, 0, 8, 6);
            chips.Children.Add(tag);
        }
        if (TournamentPermissions.IsOwner(t, me))
        {
            var chip = Pill(Say(Strings.Get("TpOrganisingTag"), "SetCaptionSize", "#DCE7F5", FontWeights.Bold), 26, 10, 10, chipFill, chipRim, 13);
            chip.Margin = new Thickness(0, 0, 8, 6);
            chips.Children.Add(chip);
        }
        left.Children.Add(chips);

        _tournamentTitle = new TextBlock
        {
            Text = t.Name,
            Margin = new Thickness(0, 4, 0, 0),
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
        };
        _tournamentTitle.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        ModDialogChrome.SetTightLine(1.08, _tournamentTitle);
        left.Children.Add(_tournamentTitle);

        var meta = Say(TournamentPage.Meta(t, me), "DialogBodySize", "#C3D2E5", margin: new Thickness(0, 10, 0, 0), mono: true);
        meta.TextWrapping = TextWrapping.Wrap;
        left.Children.Add(meta);

        var stepsRow = BuildTournamentSteps(t.Status);
        _tournamentStepsRow = stepsRow;
        left.Children.Add(stepsRow);

        var grid = new Grid();
        _tournamentHead = grid;
        grid.Children.Add(left);
        var call = BuildCallCard(t, me);
        _tournamentCallCard = call;
        if (call != null) grid.Children.Add(call);

        void Lay(double width)
        {
            grid.ColumnDefinitions.Clear();
            grid.RowDefinitions.Clear();
            if (call == null) return;
            if (width >= 960)
            {
                double spare = (width - 940) / 2;
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(420 + spare, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(520 + spare, GridUnitType.Star) });
                Grid.SetColumn(call, 1);
                Grid.SetRow(call, 0);
                call.Margin = new Thickness(20, 0, 0, 0);
            }
            else
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetColumn(call, 0);
                Grid.SetRow(call, 1);
                call.Margin = new Thickness(0, 14, 0, 0);
            }
        }
        Lay(1000);
        // A width band can only flip this: the row it writes is below the width it read.
        grid.SizeChanged += (_, e) =>
        {
            if (e.WidthChanged && (e.NewSize.Width >= 960) != (e.PreviousSize.Width >= 960)) Lay(e.NewSize.Width);
        };
        return grid;
    }

    /// <summary>
    /// The five steps as ONE capsule (design 106): a circle and a word per step, joined by a line
    /// that is green up to the current step. Done is a green circle with a drawn tick, the current
    /// one a ring and a dot in the state's colour (beating while the tournament is played), what
    /// is still to come an empty ring; "Finished", once it is, is gold.
    /// </summary>
    internal static Border BuildTournamentSteps(string? status)
    {
        var steps = TournamentPage.Steps(status);
        var tone = StateColors.ForTournament(status);
        var line = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            if (i > 0)
            {
                bool lit = step.State != TournamentStepState.Next;
                line.Children.Add(new Border
                {
                    Width = 14, Height = 2, CornerRadius = new CornerRadius(1), Margin = new Thickness(6, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = lit ? Tint(StateColors.Green, .7) : Frozen(0x29, 0xFF, 0xFF, 0xFF),
                });
            }
            bool finishedStep = step.State == TournamentStepState.Done && status == "finished" && i == steps.Count - 1;
            var circle = new Grid { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
            string ink;
            switch (step.State)
            {
                case TournamentStepState.Done:
                    circle.Children.Add(new Ellipse { Fill = Solid(finishedStep ? StateColors.Gold : StateColors.Green) });
                    circle.Children.Add(DrawnTick(14, Ink("#0F1C2E")));
                    ink = finishedStep ? ToHex(StateColors.GoldText) : "#DCE7F5";
                    break;
                case TournamentStepState.Now:
                    circle.Children.Add(new Ellipse { Fill = Frozen(0xBF, 0x0A, 0x13, 0x21), Stroke = Solid(StateColors.Mark(tone)), StrokeThickness = 1.5 });
                    var dot = new Ellipse { Width = 6, Height = 6, Fill = Solid(StateColors.Mark(tone)) };
                    if (StateColors.Pulses(status)) AttachChipPulse(dot);
                    circle.Children.Add(dot);
                    ink = ToHex(StateColors.Text(tone));
                    break;
                default:
                    circle.Children.Add(new Ellipse { Stroke = Frozen(0x3D, 0xFF, 0xFF, 0xFF), StrokeThickness = 1.5 });
                    ink = "#8C9CB1";
                    break;
            }
            var pair = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            pair.Children.Add(circle);
            pair.Children.Add(Say(step.Text, "SetDescSize", ink, FontWeights.SemiBold, new Thickness(6, 0, 0, 0)));
            line.Children.Add(pair);
        }
        return new Border
        {
            MinHeight = 30,
            CornerRadius = new CornerRadius(15),
            Padding = new Thickness(8, 6, 13, 6),
            Margin = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = Frozen(0x9E, 0x0A, 0x13, 0x21),
            BorderBrush = Frozen(0x1A, 0xFF, 0xFF, 0xFF),
            BorderThickness = new Thickness(1),
            Child = line,
            Visibility = steps.Count == 0 ? Visibility.Collapsed : Visibility.Visible,
        };
    }

    internal FrameworkElement? BuildCallCard(TournamentDetail t, string? me)
    {
        var call = TournamentPage.Call(t, me);
        _tournamentCallSub = null;
        Brush accentText = Solid(ModAccent.TextOn(_pageAccent));
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        if (call.Champion) words.Children.Add(BuildChampionBlock(t));
        else
        {
            if (string.IsNullOrEmpty(call.Kicker) && string.IsNullOrEmpty(call.Title)) return null;
            if (!string.IsNullOrEmpty(call.Kicker))
            {
                var kicker = Say(call.Kicker, "SetCaptionSize",
                    t.Status == "registration" ? ToHex(StateColors.GreenText) : ToHex(ModAccent.TextOn(_pageAccent)), FontWeights.Bold);
                // "TU RIVAL ABRIÓ LA SALA · SEMIFINAL" was cut mid-word in a narrow window.
                kicker.TextWrapping = TextWrapping.Wrap;
                words.Children.Add(kicker);
            }
            if (call.Match != null) words.Children.Add(BuildVersus(t, me, call.Match));
            else if (!string.IsNullOrEmpty(call.Title))
            {
                var title = Say(call.Title, "SetSubTitleSize", "#FFFFFF", FontWeights.SemiBold, new Thickness(0, 7, 0, 0));
                title.FontSize = 16;
                title.TextWrapping = TextWrapping.Wrap;
                words.Children.Add(title);
            }
            if (!string.IsNullOrEmpty(call.Sub))
            {
                var sub = Say(call.Sub, "SetRowDescSize", "#C3D2E5", margin: new Thickness(0, 7, 0, 0));
                sub.TextWrapping = TextWrapping.Wrap;
                _tournamentCallSub = sub;
                words.Children.Add(sub);
            }
            if (!string.IsNullOrEmpty(call.Next))
            {
                var next = new TextBlock { Margin = new Thickness(0, 7, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = Ink("#DCE7F5") };
                next.SetResourceReference(TextBlock.FontSizeProperty, "SetRowDescSize");
                next.Inlines.Add(new System.Windows.Documents.Run(Strings.Get(TournamentPage.IsTeam(t) ? "TpIfTeamWins" : "TpIfYouWin")) { FontWeight = FontWeights.SemiBold, Foreground = accentText });
                next.Inlines.Add(new System.Windows.Documents.Run(call.Next));
                words.Children.Add(next);
            }
            if (!string.IsNullOrEmpty(call.Warn))
            {
                var line = new Grid();
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                line.ColumnDefinitions.Add(new ColumnDefinition());
                line.Children.Add(Say("!", "SetControlSize", "#F4900C", FontWeights.Bold, new Thickness(0, 0, 9, 0)));
                var warn = Say(call.Warn, "SetRowDescSize", "#F5BD76");
                warn.TextWrapping = TextWrapping.Wrap;
                Grid.SetColumn(warn, 1);
                line.Children.Add(warn);
                words.Children.Add(new Border
                {
                    Margin = new Thickness(0, 7, 0, 0),
                    Padding = new Thickness(11, 9, 11, 9),
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(1),
                    Background = Frozen(0x1F, 0xF4, 0x90, 0x0C),
                    BorderBrush = Frozen(0x73, 0xF4, 0x90, 0x0C),
                    Child = line,
                });
            }
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(words);

        var buttons = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        if (call.Primary != TournamentPrimary.None)
        {
            var primary = new Button
            {
                Content = call.PrimaryText,
                MinWidth = 170,
                Height = 52,
                Padding = new Thickness(22, 0, 22, 0),
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = Solid(ModAccent.OnAccent(_pageAccent)),
                Background = Solid(_pageAccent),
                BorderBrush = Solid(_pageAccent),
                Tag = 10d,
            };
            // Deleting is destructive: red, never the mod's colour, which says "go ahead".
            if (call.Primary == TournamentPrimary.Delete)
            {
                primary.Foreground = Brushes.White;
                primary.Background = Ink("#C84A4A");
                primary.BorderBrush = Ink("#C84A4A");
            }
            primary.SetResourceReference(StyleProperty, "TournamentChip");
            primary.Click += (_, _) => RunCall(t, call.Primary, call.Match);
            // The one action of the page glows in the mod's colour (a blurred sibling, never an
            // Effect on the button's text); deleting stays plain red.
            buttons.Children.Add(call.Primary == TournamentPrimary.Delete
                ? primary
                : WithGlow(primary, 10, 22, .55, _pageAccent));
        }
        if (call.Secondary is TournamentPrimary.Enter or TournamentPrimary.MyEntry && call.Primary != call.Secondary)
        {
            var caption = Strings.Get(call.Secondary == TournamentPrimary.Enter ? "MpTournamentEnter" : "EnMyEntry");
            var secondary = Secondary(caption, () => RunCall(t, call.Secondary, null));
            secondary.Margin = new Thickness(0, 6, 0, 0);
            buttons.Children.Add(secondary);
        }
        else if (call.Secondary == TournamentPrimary.Delete)
        {
            var delete = Secondary(Strings.Get("TpDeleteTournament"), () => RunCall(t, TournamentPrimary.Delete, null),
                ink: Ink("#F0A6A6"), rim: Frozen(0x99, 0xC8, 0x4A, 0x4A));
            delete.Margin = new Thickness(0, 6, 0, 0);
            buttons.Children.Add(delete);
        }
        if (!string.IsNullOrEmpty(call.Note))
        {
            var note = Say(call.Note, "SetDescSize", "#A8BCD2", margin: new Thickness(0, 6, 0, 0));
            note.HorizontalAlignment = HorizontalAlignment.Center;
            buttons.Children.Add(note);
        }
        if (buttons.Children.Count > 0)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(buttons, 1);
            grid.Children.Add(buttons);
        }

        var card = new Border
        {
            Padding = new Thickness(16, 14, 16, 14),
            CornerRadius = new CornerRadius(14),
            VerticalAlignment = VerticalAlignment.Bottom,
            BorderThickness = new Thickness(call.Champion ? 1.5 : 1),
            BorderBrush = call.Champion ? Tint(StateColors.Gold, .85) : AmbientRim(),
            Child = grid,
        };
        if (!call.Champion)
        {
            card.SetResourceReference(Border.BackgroundProperty, "GlassPanel");
            return card;
        }
        card.Background = ChampionFill(new Point(0, 0), .4, 1.2);
        return WithGlow(card, 14, 36, .3);
    }

    /// <summary>The champion's fill (design 106): gold mixed at 78 % with the navy (#3E403B), lit by
    /// gold at <paramref name="light"/> from <paramref name="from"/> and gone by 65 %. Gold, never the
    /// mod's colour: a red mod's champion read as an error.</summary>
    private static Brush ChampionFill(Point from, double light, double reach)
    {
        var below = ModAccent.Mix(StateColors.Gold, ModAccent.Navy, .78);
        var fill = new RadialGradientBrush { Center = from, GradientOrigin = from, RadiusX = reach, RadiusY = reach };
        fill.GradientStops.Add(new GradientStop(ModAccent.Mix(below, StateColors.Gold, light), 0));
        fill.GradientStops.Add(new GradientStop(below, .65));
        fill.Freeze();
        return fill;
    }

    /// <summary>A gold glow as a sibling under the card: an Effect on the card would take ClearType
    /// from every word on it. Only the champion wears it.</summary>
    private static FrameworkElement WithGlow(FrameworkElement card, double radius, double blur, double strength, Color? color = null)
    {
        // A slow machine, or a player who asked for fewer effects, gets the card alone.
        if (RankBadge.ReducedEffects) return card;
        var glow = color ?? StateColors.Gold;
        var host = new Grid { VerticalAlignment = card.VerticalAlignment, HorizontalAlignment = card.HorizontalAlignment };
        host.Children.Add(new Border
        {
            Background = Solid(glow),
            CornerRadius = new CornerRadius(radius),
            Margin = new Thickness(2),
            IsHitTestVisible = false,
            CacheMode = new BitmapCache(),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = glow, BlurRadius = blur, ShadowDepth = 0, Opacity = strength },
        });
        host.Children.Add(card);
        return host;
    }

    private void RunCall(TournamentDetail t, TournamentPrimary what, TournamentMatch? match)
    {
        switch (what)
        {
            case TournamentPrimary.OpenRoom when match != null: _ = OpenTournamentMatchAsync(t, match); break;
            case TournamentPrimary.CloseRegistration: _ = RunTournamentActionAsync(() => TournamentApi.CloseTournamentRegistrationAsync(t.Id)); break;
            case TournamentPrimary.OpenRegistration: _ = RunTournamentActionAsync(() => TournamentApi.OpenTournamentRegistrationAsync(t.Id)); break;
            case TournamentPrimary.Seed: _ = RunTournamentActionAsync(() => TournamentApi.SeedTournamentAsync(t.Id)); break;
            case TournamentPrimary.Start: _ = RunTournamentActionAsync(() => TournamentApi.StartTournamentAsync(t.Id)); break;
            case TournamentPrimary.Enter or TournamentPrimary.MyEntry: _ = RunTournamentActionAsync(() => OpenEnrollAsync(t)); break;
            case TournamentPrimary.Delete: _ = ConfirmDeleteTournamentAsync(t); break;
        }
    }

    internal UIElement BuildVersus(TournamentDetail t, string? me, TournamentMatch m)
    {
        var mine = TournamentPermissions.MyEntrant(t, me);
        bool team = TournamentPage.IsTeam(t);
        var rivalId = m.Entrant1Id == mine?.Id ? m.Entrant2Id : m.Entrant1Id;
        var rival = t.Entrants?.FirstOrDefault(e => e.Id == rivalId);
        double radius = team ? 9 : 16;

        FrameworkElement Side(TournamentEntrant? e, string role, bool right, Brush fill, Brush ink)
        {
            var name = Say(e?.DisplayName ?? "", "SetSubTitleSize", "#FFFFFF", FontWeights.SemiBold);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            var seed = Say(Strings.Format("TpSeedLine", role, e?.Seed?.ToString() ?? "—"), "SetCaptionSize", "#C3D2E5", margin: new Thickness(0, 3, 0, 0));
            // "Tu equipo · semilla 1" is wider than the side on a laptop: it ended mid-word.
            seed.TextTrimming = TextTrimming.CharacterEllipsis;
            var words = new StackPanel { Margin = new Thickness(right ? 0 : 9, 0, right ? 9 : 0, 0), VerticalAlignment = VerticalAlignment.Center };
            if (right) { name.TextAlignment = TextAlignment.Right; seed.TextAlignment = TextAlignment.Right; name.HorizontalAlignment = HorizontalAlignment.Right; seed.HorizontalAlignment = HorizontalAlignment.Right; }
            words.Children.Add(name);
            words.Children.Add(seed);
            var discLetter = Say(team ? EnrollView.CrestOf(e?.DisplayName) : Initial(e?.DisplayName), "SetRowDescSize", "#FFFFFF", FontWeights.Bold);
            discLetter.Foreground = ink;
            var disc = new Border
            {
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(radius),
                Background = fill,
                BorderBrush = Frozen(0x2E, 0xFF, 0xFF, 0xFF),
                BorderThickness = new Thickness(2),
                Child = LetterWithPhoto(discLetter, PhotoOf(e, team), 28, Math.Max(0, radius - 2)),
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = right ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = right ? GridLength.Auto : new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(right ? disc : words, 1);
            grid.Children.Add(words);
            grid.Children.Add(disc);
            return grid;
        }

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(Side(mine, Strings.Get(team ? "TpYourTeam" : "TpYou"), false, Solid(_pageAccent), Solid(ModAccent.OnAccent(_pageAccent))));
        var vs = new Grid { Width = 46, Height = 36, Margin = new Thickness(5, 0, 5, 0) };
        vs.Children.Add(new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(18),
            Background = Ink("#0F1C2E"),
            BorderBrush = Frozen(0x38, 0xFF, 0xFF, 0xFF),
            BorderThickness = new Thickness(1),
            Child = Say("VS", "SetControlSize", "#FFFFFF", FontWeights.Bold),
        });
        ((TextBlock)((Border)vs.Children[0]).Child).HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetColumn(vs, 1);
        row.Children.Add(vs);
        var other = Side(rival, Strings.Get("TpRival"), true, EntrantDisc(rival, team), Brushes.White);
        Grid.SetColumn(other, 2);
        row.Children.Add(other);

        var fill = new LinearGradientBrush { StartPoint = new Point(0, .5), EndPoint = new Point(1, .5) };
        fill.GradientStops.Add(new GradientStop(Color.FromArgb(0x52, _pageAccent.R, _pageAccent.G, _pageAccent.B), 0));
        fill.GradientStops.Add(new GradientStop(Color.FromArgb(0x66, 10, 19, 33), .42));
        fill.GradientStops.Add(new GradientStop(Color.FromArgb(0x66, 10, 19, 33), .58));
        fill.GradientStops.Add(new GradientStop(Color.FromArgb(0x57, 63, 106, 138), 1));
        fill.Freeze();
        return new Border
        {
            Margin = new Thickness(0, 7, 0, 0),
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(10),
            BorderBrush = Frozen(0x1A, 0xFF, 0xFF, 0xFF),
            BorderThickness = new Thickness(1),
            Background = fill,
            Child = row,
        };
    }

    /// <summary>Team shields take their colour from the seed; the viewer's is the mod's.</summary>
    private static readonly string[] TeamShields = { "#3F6A8A", "#6A4F8A", "#3F7A6A", "#8A5A3F", "#5A6B8C", "#7A3F4F", "#4F6B3F", "#6B5A3F" };

    private static Brush EntrantDisc(TournamentEntrant? e, bool team)
        => Ink(team && e?.Seed is int seed && seed > 0 ? TeamShields[(seed - 1) % TeamShields.Length] : "#3F6A8A");

    private static string Initial(string? name) => string.IsNullOrEmpty(name) ? "?" : name[..1].ToUpperInvariant();

    /// <summary>The Discord picture of a SOLO entrant (its one player), or null for a team - a team
    /// keeps its crest - and for a server that sends no picture.</summary>
    internal static string? PhotoOf(TournamentEntrant? e, bool team)
        => team ? null : e?.Members?.FirstOrDefault()?.AvatarUrl is { Length: > 0 } url ? url : null;

    /// <summary>
    /// A disc's letter, with the player's photo over it when there is one (2026-11-11). The letter
    /// stays underneath, so a photo that fails to load leaves the disc as it always was.
    /// </summary>
    internal static UIElement LetterWithPhoto(TextBlock letter, string? photoUrl, double size, double radius)
    {
        letter.HorizontalAlignment = HorizontalAlignment.Center;
        letter.VerticalAlignment = VerticalAlignment.Center;
        if (string.IsNullOrEmpty(photoUrl) || AvatarBrush(photoUrl, size) is not { } photo) return letter;
        var host = new Grid { Width = size, Height = size };
        host.Children.Add(letter);
        host.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Width = size, Height = size, RadiusX = radius, RadiusY = radius, Fill = photo,
            Tag = "tournament-photo",
        });
        return host;
    }

    /// <summary>The ladder a tournament's players are ranked on: 1v1, or the team one.</summary>
    private static RoomFormat TournamentLadder(TournamentSummary t)
        => t.Format switch { "2v2" => RoomFormat.TwoVTwo, "3v3" => RoomFormat.ThreeVThree, _ => RoomFormat.OneVOne };

    /// <summary>
    /// A SOLO entrant's rank badge, STILL, on the tournament's ladder - or null for a team, a
    /// player still being placed (the server sends no place) and a server that sends none.
    /// </summary>
    private FrameworkElement? EntrantBadge(TournamentDetail t, TournamentEntrant? e, double width)
    {
        if (TournamentPage.IsTeam(t) || e?.Members?.FirstOrDefault() is not { } m) return null;
        if (m.LadderRank is not > 0) return null;
        if (BadgeOf(TournamentLadder(t), null, m.LadderRank, null) is not { } shown) return null;
        var badge = BuildShownBadge(shown, width, "tournament-" + t.Id + "-" + m.UserId);
        badge.VerticalAlignment = VerticalAlignment.Center;
        return badge;
    }

    private UIElement BuildChampionBlock(TournamentDetail t)
    {
        var name = TournamentPage.NameOf(t, t.WinnerEntrantId);
        var final = t.Matches?.OrderByDescending(m => m.Round).FirstOrDefault();
        var rival = final == null ? null : final.Entrant1Id == t.WinnerEntrantId ? final.Entrant2Id : final.Entrant1Id;

        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(BuildShield(Initial(name), 44, 52, 19));
        var words = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(Say(Strings.Get("TpChampion"), "SetCaptionSize", ToHex(StateColors.GoldText), FontWeights.Bold));
        var title = Say(name, "FontSizeHeading", "#FFFFFF", FontWeights.Bold, new Thickness(0, 5, 0, 0));
        title.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        // A solo champion's face and badge beside the name (2026-11-11). The shield stays the trophy.
        var champion = t.Entrants?.FirstOrDefault(x => x.Id == t.WinnerEntrantId);
        var photo = PhotoOf(champion, TournamentPage.IsTeam(t));
        var badge = EntrantBadge(t, champion, 26);
        if (photo != null || badge != null)
        {
            var who = new Grid { Margin = new Thickness(0, 5, 0, 0) };
            who.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            who.ColumnDefinitions.Add(new ColumnDefinition());
            who.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            int col = 0;
            if (photo != null)
            {
                var face = BuildAvatarDisc(champion?.DisplayName ?? name, photo, 30);
                face.Margin = new Thickness(0, 0, 10, 0);
                Grid.SetColumn(face, col);
                who.Children.Add(face);
            }
            title.Margin = new Thickness(0);
            title.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(title, 1);
            who.Children.Add(title);
            if (badge != null)
            {
                badge.Margin = new Thickness(10, 0, 0, 0);
                Grid.SetColumn(badge, 2);
                who.Children.Add(badge);
            }
            words.Children.Add(who);
        }
        else words.Children.Add(title);
        if (!string.IsNullOrEmpty(rival))
            words.Children.Add(Say(Strings.Format("TpBeatInFinal", TournamentPage.NameOf(t, rival)), "SetRowDescSize", "#DCE7F5", margin: new Thickness(0, 5, 0, 0)));
        Grid.SetColumn(words, 1);
        row.Children.Add(words);
        return row;
    }

    /// <summary>The champion's shield: <c>clip-path: polygon(...)</c> in the design.</summary>
    private FrameworkElement BuildShield(string initial, double width, double height, double fontSize)
    {
        // Gold at 160 degrees (design 106): #FFF3CF -> #E6C06A at 52 % -> #8A6418.
        var fill = new LinearGradientBrush { StartPoint = new Point(.33, 0), EndPoint = new Point(.67, 1) };
        fill.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xF3, 0xCF), 0));
        fill.GradientStops.Add(new GradientStop(StateColors.Gold, .52));
        fill.GradientStops.Add(new GradientStop(Color.FromRgb(0x8A, 0x64, 0x18), 1));
        fill.Freeze();
        var shape = new Path
        {
            Fill = fill,
            Stretch = Stretch.Fill,
            Data = Geometry.Parse("M6,0 L94,0 L100,15 L96,60 L50,100 L4,60 L0,15 Z"),
        };
        var grid = new Grid { Width = width, Height = height };
        grid.Children.Add(shape);
        var letter = new TextBlock
        {
            Text = initial,
            FontSize = fontSize,
            FontWeight = FontWeights.Bold,
            Foreground = Ink("#3B2A06"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 7),
        };
        letter.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        grid.Children.Add(letter);
        return grid;
    }

    // ---------------------------------------------------------------- tabs and tools

    private FrameworkElement BuildTournamentTools(TournamentDetail t)
    {
        var line = new FlexLine { Gap = 12, LineGap = 8, Margin = new Thickness(0, 0, 0, 14) };
        _tournamentTabRow = line;
        var me = TournamentViewerId(t);

        var tabs = new StackPanel { Orientation = Orientation.Horizontal };
        int pending = (t.Entrants ?? new()).Count(e => e.Status == "pending");
        int people = (t.Entrants ?? new()).Count(e => e.Status == "confirmed");
        var active = _tournamentTabs[t.Id];

        void Tab(TournamentTab tab, string key, string? count)
        {
            bool on = tab == active;
            var words = new StackPanel { Orientation = Orientation.Horizontal };
            words.Children.Add(Say(Strings.Get(key), "SetRowTitleSize", on ? "#FFFFFF" : "#A8BCD2", on ? FontWeights.SemiBold : FontWeights.Medium));
            ((TextBlock)words.Children[0]).FontSize = 13;
            if (count != null)
            {
                bool caution = tab == TournamentTab.People && pending > 0 && TournamentPermissions.IsOwnerOrManager(t, me);
                words.Children.Add(Say(count, "SetCaptionSize", caution ? "#F5BD76" : on ? "#C3D2E5" : "#8C9CB1", FontWeights.SemiBold, new Thickness(7, 0, 0, 0), mono: true));
            }
            var button = new Button
            {
                Content = words,
                Height = 30,
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(tab == TournamentTab.Bracket ? 0 : 3, 0, 0, 0),
                Background = on ? ActiveChipFill() : Brushes.Transparent,
                BorderBrush = on ? ActiveChipRim() : Brushes.Transparent,
                Tag = 7d,
            };
            button.SetResourceReference(StyleProperty, "TournamentChip");
            Spoken(button, "TournamentTab:" + tab, ((TextBlock)words.Children[0]).Text);
            button.Click += (_, _) =>
            {
                _tournamentTabs[t.Id] = tab;
                RenderTournamentDetail();
            };
            tabs.Children.Add(button);
        }

        Tab(TournamentTab.Bracket, "TpTabBracket", null);
        var peopleCount = people == 0 ? null : people.ToString();
        if (pending > 0 && TournamentPermissions.IsOwnerOrManager(t, me))
            peopleCount = Strings.Format("TpToDecide", people, pending);
        Tab(TournamentTab.People, "TpTabPeople", peopleCount);
        if (TournamentPermissions.IsOwnerOrManager(t, me)) Tab(TournamentTab.Organise, "TpTabOrganise", null);

        line.Children.Add(new Border
        {
            Padding = new Thickness(3),
            CornerRadius = new CornerRadius(9),
            Background = Frozen(0x9E, 0x0A, 0x13, 0x21),
            BorderBrush = Frozen(0x14, 0xFF, 0xFF, 0xFF),
            BorderThickness = new Thickness(1),
            Child = tabs,
        });

        var spring = new Border();
        FlexLine.SetSpring(spring, true);
        line.Children.Add(spring);

        line.Children.Add(BuildTournamentSearch(t));

        var tools = new FlexLine { Gap = 12, LineGap = 8 };
        _bracketTools = tools;
        var anchors = new StackPanel { Orientation = Orientation.Horizontal };
        var current = TournamentPage.CurrentRound(t);
        foreach (var round in (t.Matches ?? new()).Select(m => m.Round).Distinct().OrderBy(r => r))
        {
            bool now = round == current && t.Status == "running";
            var words = new StackPanel { Orientation = Orientation.Horizontal };
            if (now)
            {
                var beat = Dot(6, Solid(StateColors.Blue), 6);
                AttachChipPulse(beat);
                words.Children.Add(beat);
            }
            words.Children.Add(Say(TournamentPage.RoundName(t, round), "SetDescSize", now ? "#FFFFFF" : "#C3D2E5", FontWeights.SemiBold));
            var anchor = new Button
            {
                Content = words,
                Height = 30,
                Padding = new Thickness(11, 0, 11, 0),
                Margin = new Thickness(0, 0, 6, 0),
                Background = Frozen(0x8C, 0x0A, 0x13, 0x21),
                BorderBrush = now ? Tint(StateColors.Blue, .7) : Frozen(0x1F, 0xFF, 0xFF, 0xFF),
                Tag = 15d,
            };
            anchor.SetResourceReference(StyleProperty, "TournamentChip");
            Spoken(anchor, "TournamentRound:" + round, TournamentPage.RoundName(t, round));
            int target = round;
            anchor.Click += (_, _) => ScrollBracketToRound(target);
            anchors.Children.Add(anchor);
        }
        tools.Children.Add(anchors);

        if (_mineBox != null) tools.Children.Add(Secondary(Strings.Get("TpGoMine"), GoToMyMatch));

        var zoom = new StackPanel { Orientation = Orientation.Horizontal };
        Button Step(string glyph, string tip, double delta)
        {
            var button = new Button { Content = glyph, Width = 32, Height = 32, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Ink("#DCE7F5"), Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, ToolTip = TooltipHelper.Wrap(Strings.Get(tip)), Tag = 7d };
            button.SetResourceReference(StyleProperty, "TournamentChip");
            Spoken(button, delta > 0 ? "TournamentZoomIn" : "TournamentZoomOut", Strings.Get(tip));
            button.Click += (_, _) => ChooseBracketZoom(_bracketZoom + delta);
            return button;
        }
        _zoomText = Say(_bracketZoom + " %", "SetDescSize", "#C3D2E5", FontWeights.SemiBold, mono: true);
        _zoomText.Width = 46;
        _zoomText.TextAlignment = TextAlignment.Center;
        _zoomText.VerticalAlignment = VerticalAlignment.Center;
        zoom.Children.Add(Step("−", "TpZoomOut", -BracketZoom.ButtonStep));
        zoom.Children.Add(_zoomText);
        zoom.Children.Add(Step("+", "TpZoomIn", BracketZoom.ButtonStep));
        var fit = new Button
        {
            Content = Say(Strings.Get("TpZoomFit"), "SetDescSize", "#DCE7F5", FontWeights.SemiBold),
            Height = 32, Padding = new Thickness(10, 0, 10, 0),
            Background = Brushes.Transparent, BorderBrush = Brushes.Transparent,
            ToolTip = TooltipHelper.Wrap(Strings.Get("TpZoomFitTip")), Tag = 7d,
        };
        fit.SetResourceReference(StyleProperty, "TournamentChip");
        Spoken(fit, "TournamentZoomFit", Strings.Get("TpZoomFit"));
        fit.Click += (_, _) => FitBracketZoom();
        zoom.Children.Add(new Border { Width = 1, Margin = new Thickness(2, 7, 2, 7), Background = Frozen(0x1F, 0xFF, 0xFF, 0xFF) });
        zoom.Children.Add(fit);
        _zoomBox = new Border { Height = 32, CornerRadius = new CornerRadius(7), Background = Ink("#1A2C48"),
            BorderBrush = _ambient is { } zoomTone ? Solid(zoomTone.ToolRim) : SecondaryRim, BorderThickness = new Thickness(1), Child = zoom };
        tools.Children.Add(_zoomBox);
        line.Children.Add(tools);
        return line;
    }

    private FrameworkElement BuildTournamentSearch(TournamentDetail t)
    {
        var glass = new Path
        {
            Data = Geometry.Parse("M5.5,1.3 A4.2,4.2 0 1 1 5.49,1.3 Z M8.7,8.7 L12,12"),
            Stroke = Ink("#8C9CB1"),
            StrokeThickness = 1.5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Width = 13,
            Height = 13,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _tournamentSearch = new TextBox { Text = _tournamentQuery, VerticalAlignment = VerticalAlignment.Center };
        _tournamentSearch.SetResourceReference(StyleProperty, "DialogBareText");
        _tournamentSearch.SetResourceReference(Control.FontSizeProperty, "SetRowDescSize");
        var placeholder = Say(Strings.Get(TournamentPage.IsTeam(t) ? "TpSearchTeam" : "TpSearchPlayer"), "SetRowDescSize", "#8C9CB1");
        placeholder.IsHitTestVisible = false;
        placeholder.TextTrimming = TextTrimming.CharacterEllipsis;
        placeholder.Visibility = _tournamentQuery.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var field = new Grid { Margin = new Thickness(8, 0, 8, 0) };
        field.Children.Add(_tournamentSearch);
        field.Children.Add(placeholder);
        Grid.SetColumn(field, 1);

        _tournamentSearchCount = Say("", "SetCaptionSize", "#A8BCD2", FontWeights.SemiBold, mono: true);
        var close = new Button { Content = "✕", Width = 18, Height = 18, Foreground = Ink("#8C9CB1"), Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, Tag = 4d, Margin = new Thickness(6, 0, 0, 0) };
        close.SetResourceReference(StyleProperty, "TournamentChip");
        close.SetResourceReference(Control.FontSizeProperty, "SetControlSize");
        close.Click += (_, _) => _tournamentSearch.Text = "";
        var end = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        end.Children.Add(_tournamentSearchCount);
        end.Children.Add(close);
        end.Visibility = _tournamentQuery.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetColumn(end, 2);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(glass);
        grid.Children.Add(field);
        grid.Children.Add(end);

        var box = new Border
        {
            Width = 220,
            Height = 32,
            Padding = new Thickness(10, 0, 10, 0),
            CornerRadius = new CornerRadius(7),
            Background = Ink("#0D1828"),
            BorderBrush = SecondaryRim,
            BorderThickness = new Thickness(1),
            Child = grid,
        };
        _tournamentSearch.TextChanged += (_, _) =>
        {
            _tournamentQuery = _tournamentSearch.Text.Trim();
            bool has = _tournamentQuery.Length > 0;
            placeholder.Visibility = _tournamentSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            end.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            box.BorderBrush = has ? Ink("#2F7FE0") : SecondaryRim;
            ApplyTournamentSearch(t);
        };
        return box;
    }

    /// <summary>A search dims what does not match: it moves nothing, so it rebuilds nothing.</summary>
    private void ApplyTournamentSearch(TournamentDetail t)
    {
        bool bracket = _tournamentTabs.TryGetValue(t.Id, out var tab) && tab == TournamentTab.Bracket;
        int hits = 0;
        foreach (var m in t.Matches ?? new())
        {
            if (!_bracketCards.TryGetValue(m.Id, out var card)) continue;
            bool hit = _tournamentQuery.Length > 0 && (TournamentPage.Matches(EntrantById(t, m.Entrant1Id), _tournamentQuery)
                                                     || TournamentPage.Matches(EntrantById(t, m.Entrant2Id), _tournamentQuery));
            if (hit) hits++;
            card.Opacity = _tournamentQuery.Length == 0 || hit ? 1 : .3;
            PaintCardRim(t, m, card, hit);
        }
        int found = 0;
        if (_peopleBody != null) FillPeople(t, TournamentViewerId(t), out found);
        if (_tournamentSearchCount != null) _tournamentSearchCount.Text = (bracket ? hits : found).ToString();
    }

    private static TournamentEntrant? EntrantById(TournamentDetail t, string? id)
        => t.Entrants?.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.Ordinal));

    private void ShowTournamentTab(TournamentDetail t, TournamentTab tab)
    {
        bool hasBracket = t.Matches is { Count: > 0 };
        if (_bracketHost != null) _bracketHost.Visibility = tab == TournamentTab.Bracket && hasBracket ? Visibility.Visible : Visibility.Collapsed;
        if (_bracketEmptyView != null) _bracketEmptyView.Visibility = tab == TournamentTab.Bracket && !hasBracket ? Visibility.Visible : Visibility.Collapsed;
        if (_peopleView != null) _peopleView.Visibility = tab == TournamentTab.People ? Visibility.Visible : Visibility.Collapsed;
        if (_organiseView != null) _organiseView.Visibility = tab == TournamentTab.Organise ? Visibility.Visible : Visibility.Collapsed;
        if (_bracketTools != null) _bracketTools.Visibility = tab == TournamentTab.Bracket && hasBracket ? Visibility.Visible : Visibility.Collapsed;
        if (_tournamentSearch?.Parent is Grid { Parent: Grid { Parent: Border box } })
            box.Visibility = tab == TournamentTab.Organise || (tab == TournamentTab.Bracket && !hasBracket) ? Visibility.Collapsed : Visibility.Visible;
        ApplyTournamentSearch(t);
    }

    // ---------------------------------------------------------------- the three views

    private FrameworkElement BuildTournamentContent(TournamentDetail t, string? me)
    {
        var host = new Grid();
        var bracket = t.Matches is { Count: > 0 } ? (FrameworkElement)BuildBracketPanel(t, me) : null;
        _bracketHost = bracket;
        if (bracket != null) host.Children.Add(bracket);

        _bracketEmptyView = BuildBracketEmpty(t);
        host.Children.Add(_bracketEmptyView);

        _peopleBody = new StackPanel { Margin = new Thickness(16, 14, 16, 18) };
        FillPeople(t, me, out _);
        var people = new ScrollViewer { Padding = new Thickness(0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _peopleBody };
        _peopleView = people;
        host.Children.Add(people);

        _organiseView = TournamentPermissions.IsOwnerOrManager(t, me) ? BuildOrganise(t, me) : null;
        if (_organiseView != null) host.Children.Add(_organiseView);

        var card = new Border { CornerRadius = new CornerRadius(14), BorderBrush = AmbientRim(), BorderThickness = new Thickness(1), Child = host };
        card.SetResourceReference(Border.BackgroundProperty, "GlassPanel");
        // What is inside follows the rounded corners, the strip of rounds first of all.
        host.SizeChanged += (_, e) => host.Clip = new RectangleGeometry(new Rect(e.NewSize), 13, 13);
        _tournamentContentCard = card;
        return card;
    }

    private static FrameworkElement BuildBracketEmpty(TournamentDetail t)
    {
        var column = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(30) };
        var title = Say(Strings.Get("TpNoBracket"), "SetSubTitleSize", "#F0F5FB", FontWeights.SemiBold);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        var body = Say(Strings.Get(t.Status == "draft" ? "TpNoBracketDraft" : "TpNoBracketRegistration"), "SetRowDescSize", "#A8BCD2", margin: new Thickness(0, 8, 0, 0));
        body.MaxWidth = 420;
        body.TextWrapping = TextWrapping.Wrap;
        body.TextAlignment = TextAlignment.Center;
        column.Children.Add(title);
        column.Children.Add(body);
        return column;
    }

    // ---------------------------------------------------------------- the bracket

    /// <summary>
    /// The bracket: a sheet of absolutely placed cards that scrolls both ways under a strip of
    /// rounds that stays put, a panel for the picked match, and the legend.
    ///
    /// <para><b>A card carries no button.</b> A cell is a position; what can be done about a match
    /// lives in the panel beside the sheet, one selection at a time.</para>
    /// </summary>
    internal UIElement BuildBracketPanel(TournamentDetail t, string? me)
    {
        var sheet = TournamentPage.Sheet(t);
        var canvas = new Canvas { Width = sheet.Width, Height = sheet.Height };
        // The draw's lines in the mod's tone; your path and the champion's stay brighter on top.
        var line = _ambient is { } amb ? Solid(amb.Line) : Frozen(0x29, 0xFF, 0xFF, 0xFF);
        foreach (var l in sheet.Lines) canvas.Children.Add(Bar(l, line));

        var mine = (t.Matches ?? new()).FirstOrDefault(m => MatchCards.For(m, me, t.Entrants)
            is MatchCardState.Playable or MatchCardState.JoinRoom or MatchCardState.ReturnToRoom);
        var path = mine != null ? TournamentPage.PathFrom(t, mine) : TournamentPage.ChampionPath(t);
        // Your way through the bracket is yours (the mod's colour); the champion's is gold.
        var bright = mine != null ? Tint(ModAccent.TextOn(_pageAccent), .85) : Tint(StateColors.Gold, .9);
        foreach (var l in path) canvas.Children.Add(Bar(l, bright));

        _bracketCards.Clear();
        _bracketBoxes = sheet.Cards;
        foreach (var box in sheet.Cards)
        {
            if (!RankBadge.ReducedEffects)
            {
                var cardState = MatchCards.For(box.Match, me, t.Entrants);
                if (cardState is MatchCardState.Playable or MatchCardState.JoinRoom or MatchCardState.ReturnToRoom)
                    canvas.Children.Add(CardHalo(box, _pageAccent));
                else if (cardState is MatchCardState.InProgress or MatchCardState.SuperviseRoom)
                    canvas.Children.Add(CardHalo(box, StateColors.Blue));
            }
            var card = BuildBracketCard(t, box, me);
            Canvas.SetLeft(card, box.X);
            Canvas.SetTop(card, box.Y);
            canvas.Children.Add(card);
            _bracketCards[box.Match.Id] = card;
            if (ReferenceEquals(box.Match, mine)) _mineBox = box;
        }

        double width = sheet.Width;
        if (!string.IsNullOrEmpty(t.WinnerEntrantId))
        {
            var final = sheet.Cards.OrderByDescending(b => b.Match.Round).First();
            double right = final.X + final.Width, cy = final.Y + final.Height / 2;
            canvas.Children.Add(Bar(new BracketLine(right, cy - .5, sheet.Gutter, 2), Tint(StateColors.Gold, .9)));
            var trophy = BuildChampionCard(t, final.Match);
            trophy.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double tall = trophy.DesiredSize.Height;
            // Centred on the final; when that would cut it against the round titles (a 4-player
            // draw), the whole sheet moves down instead (design 106).
            double top = cy - tall / 2, lift = Math.Max(0, -top);
            ShiftSheet(canvas, lift);
            Canvas.SetLeft(trophy, right + sheet.Gutter);
            Canvas.SetTop(trophy, top + lift);
            canvas.Children.Add(trophy);
            width += sheet.Gutter + 200;
            canvas.Width = width;
            canvas.Height = Math.Max(sheet.Height + lift, top + lift + tall);
        }
        // A sheet shorter than 200 sits in the middle of 200, not against the titles.
        if (canvas.Height < SmallSheetHeight)
        {
            ShiftSheet(canvas, (SmallSheetHeight - canvas.Height) / 2);
            canvas.Height = SmallSheetHeight;
        }

        _bracketSheetSize = new Size(canvas.Width, canvas.Height);
        _bracketScale = new ScaleTransform(_bracketZoom / 100, _bracketZoom / 100);
        SetBracketTextMode(canvas, _bracketZoom);
        // Centred while it is smaller than the panel; a bigger sheet fills its slot, so the
        // alignment does nothing and it scrolls from the top-left as before.
        var scaled = new Border { Child = canvas, LayoutTransform = _bracketScale, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _bracketSheetHost = scaled;

        var scroll = new ScrollViewer
        {
            Padding = new Thickness(0),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = scaled,
        };
        scroll.SetResourceReference(StyleProperty, "BothWaysScroll");
        _bracketScroll = scroll;
        var tournamentId = t.Id;
        // The scroller sits in a star cell, so its size never depends on the sheet: fitting from
        // here cannot loop. Only while the player has not chosen a zoom for this tournament.
        scroll.SizeChanged += (_, _) =>
        {
            if (!ReferenceEquals(scroll, _bracketScroll)) return;
            if (!_bracketZoomManual.ContainsKey(tournamentId))
            {
                var auto = BracketZoom.Auto(_bracketSheetSize.Width, _bracketSheetSize.Height, scroll.ViewportWidth, scroll.ViewportHeight);
                _bracketZoomAuto[tournamentId] = auto;
                if (Math.Abs(auto - _bracketZoom) > .5) { SetBracketZoom(auto); return; }
            }
            AlignBracketHeads();
        };
        scroll.ScrollChanged += (_, _) => AlignBracketHeads();
        scroll.PreviewMouseWheel += (_, e) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            e.Handled = true;
            var onSheet = e.GetPosition(canvas);
            var onScreen = e.GetPosition(scroll);
            ChooseBracketZoom(_bracketZoom + Math.Sign(e.Delta) * BracketZoom.WheelStep);
            scroll.UpdateLayout();
            double z = _bracketZoom / 100;
            scroll.ScrollToHorizontalOffset(Math.Max(0, onSheet.X * z - onScreen.X));
            scroll.ScrollToVerticalOffset(Math.Max(0, onSheet.Y * z - onScreen.Y));
        };

        AttachBracketPan(scroll);

        _bracketHeadShift = new TranslateTransform();
        var heads = BuildBracketHeads(t, sheet, width);
        heads.RenderTransform = _bracketHeadShift;
        _bracketHeads = heads;
        var strip = new Border
        {
            ClipToBounds = true,
            Padding = new Thickness(0, 12, 0, 9),
            Background = Frozen(0xE0, 0x0C, 0x16, 0x26),
            BorderBrush = _ambient is { } stripTone ? Solid(stripTone.Hairline) : Frozen(0x12, 0xFF, 0xFF, 0xFF),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = heads,
        };

        var left = new Grid();
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition());
        left.Children.Add(strip);
        Grid.SetRow(scroll, 1);
        left.Children.Add(scroll);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition());
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.Children.Add(left);
        _bracketPick = new Grid();
        Grid.SetColumn(_bracketPick, 1);
        body.Children.Add(_bracketPick);
        FillBracketPick(t, me);

        var host = new Grid();
        host.RowDefinitions.Add(new RowDefinition());
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        host.Children.Add(body);
        var legend = BuildBracketLegend(t);
        Grid.SetRow(legend, 1);
        host.Children.Add(legend);
        return host;
    }

    internal const double SmallSheetHeight = 200;

    /// <summary>Moves everything on the sheet down by <paramref name="by"/>.</summary>
    private static void ShiftSheet(Canvas canvas, double by)
    {
        if (by <= 0) return;
        foreach (UIElement child in canvas.Children)
        {
            var at = Canvas.GetTop(child);
            Canvas.SetTop(child, (double.IsNaN(at) ? 0 : at) + by);
        }
    }

    private static Border Bar(BracketLine l, Brush fill)
    {
        var bar = new Border { Width = l.Width, Height = l.Height, Background = fill, IsHitTestVisible = false };
        Canvas.SetLeft(bar, l.X);
        Canvas.SetTop(bar, l.Y);
        return bar;
    }

    private FrameworkElement BuildBracketHeads(TournamentDetail t, BracketSheet sheet, double width)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Width = width * _bracketZoom / 100, HorizontalAlignment = HorizontalAlignment.Left };
        double z = _bracketZoom / 100;
        var rounds = (t.Matches ?? new()).GroupBy(m => m.Round).OrderBy(g => g.Key).ToList();
        var current = TournamentPage.CurrentRound(t);
        for (int i = 0; i < rounds.Count; i++)
        {
            var round = rounds[i];
            bool now = round.Key == current && t.Status == "running";
            int count = round.Count();
            // The cards shrink with the zoom and the type does not: at the smallest the count keeps
            // its number alone, and what still does not fit trims short of the next heading.
            var cell = new Grid { ClipToBounds = true, Width = ((i == 0 ? sheet.Pad : 0) + sheet.ColumnWidth + sheet.Gutter) * z };
            cell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cell.ColumnDefinitions.Add(new ColumnDefinition());
            var title = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness((i == 0 ? sheet.Pad : 0) * z, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
            if (now)
            {
                // The round being played: blue, with a beating dot (design 106), never the mod's colour.
                var beat = Dot(6, Solid(StateColors.Blue), 6);
                AttachChipPulse(beat);
                title.Children.Add(beat);
            }
            title.Children.Add(Say(TournamentPage.RoundName(t, round.Key).ToUpperInvariant(), "SetCaptionSize", now ? ToHex(StateColors.BlueText) : "#C3D2E5", FontWeights.Bold));
            cell.Children.Add(title);
            var tally = Say(z < .7 ? count.ToString() : Strings.Format(count == 1 ? "TpMatchesOne" : "TpMatchesMany", count), "SetCaptionSize", "#8C9CB1",
                margin: new Thickness(0, 0, 10, 0), mono: true);
            tally.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(tally, 1);
            cell.Children.Add(tally);
            row.Children.Add(cell);
        }
        if (!string.IsNullOrEmpty(t.WinnerEntrantId))
            row.Children.Add(Say(Strings.Get("TpChampion"), "SetCaptionSize", ToHex(StateColors.GoldText), FontWeights.Bold));
        return row;
    }

    private FrameworkElement BuildChampionCard(TournamentDetail t, TournamentMatch final)
    {
        var name = TournamentPage.NameOf(t, t.WinnerEntrantId);
        var rival = final.Entrant1Id == t.WinnerEntrantId ? final.Entrant2Id : final.Entrant1Id;
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var shield = BuildShield(Initial(name), 46, 54, 20);
        shield.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(shield);
        var label = Say(Strings.Get("TpChampion"), "SetCaptionSize", ToHex(StateColors.GoldText), FontWeights.Bold, new Thickness(0, 8, 0, 0));
        label.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(label);
        var title = Say(name, "FontSizeTitle", "#FFFFFF", FontWeights.Bold, new Thickness(0, 8, 0, 0));
        title.FontSize = 22;
        title.TextWrapping = TextWrapping.Wrap;
        title.TextAlignment = TextAlignment.Center;
        title.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        stack.Children.Add(title);
        var sub = Say(Strings.Format("TpBeatInFinal", TournamentPage.NameOf(t, rival)), "SetControlSize", "#DCE7F5", margin: new Thickness(0, 8, 0, 0));
        sub.TextWrapping = TextWrapping.Wrap;
        sub.TextAlignment = TextAlignment.Center;
        stack.Children.Add(sub);
        return WithGlow(new Border
        {
            Width = 200,
            Padding = new Thickness(16, 18, 16, 16),
            CornerRadius = new CornerRadius(14),
            Background = ChampionFill(new Point(.5, 0), .42, 1),
            BorderBrush = Tint(StateColors.Gold, .85),
            BorderThickness = new Thickness(1.5),
            Child = stack,
        }, 14, 38, .34);
    }

    private Border BuildBracketCard(TournamentDetail t, MatchBox box, string? me)
    {
        var m = box.Match;
        bool team = TournamentPage.IsTeam(t);
        bool bye = m.Status == "bye";
        var rows = new Grid();
        rows.RowDefinitions.Add(new RowDefinition());
        if (!bye)
        {
            rows.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
            rows.RowDefinitions.Add(new RowDefinition());
        }
        var first = BuildBracketSide(t, m, m.Entrant1Id, 1, me, team, bye);
        rows.Children.Add(first);
        if (!bye)
        {
            var hair = new Border { Background = Frozen(0x14, 0xFF, 0xFF, 0xFF) };
            Grid.SetRow(hair, 1);
            rows.Children.Add(hair);
            var second = BuildBracketSide(t, m, m.Entrant2Id, 2, me, team, false);
            Grid.SetRow(second, 2);
            rows.Children.Add(second);
        }
        var body = new Grid();
        body.Children.Add(rows);
        // A 1-px light along the top edge (design 106).
        body.Children.Add(new Border { BorderThickness = new Thickness(0, 1, 0, 0), BorderBrush = Frozen(0x0D, 0xFF, 0xFF, 0xFF), CornerRadius = new CornerRadius(8), IsHitTestVisible = false });
        if (MatchCards.For(m, me, t.Entrants) is MatchCardState.InProgress or MatchCardState.SuperviseRoom)
        {
            // Being played: blue, and it beats (it used to be a still green dot).
            var live = new Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = Solid(StateColors.Blue),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 7, 7, 0),
                ToolTip = TooltipHelper.Wrap(Strings.Get("TpStateLive")),
            };
            AttachChipPulse(live);
            body.Children.Add(live);
        }

        var card = new Border
        {
            Width = box.Width,
            Height = box.Height,
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1),
            Child = body,
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        card.MouseLeftButtonUp += (_, _) => PickBracketMatch(m.Id);
        PaintCardRim(t, m, card, false);
        return card;
    }

    /// <summary>A match being played: a blue tint from the left over the card's navy (20 % to 4 % at 72 %).</summary>
    private static readonly Brush LiveCardFill = MakeLiveCardFill();

    private static Brush MakeLiveCardFill()
    {
        var navy = Color.FromRgb(0x12, 0x21, 0x3A);
        var fill = new LinearGradientBrush { StartPoint = new Point(0, .5), EndPoint = new Point(1, .5) };
        fill.GradientStops.Add(new GradientStop(ModAccent.Mix(navy, StateColors.Blue, .2), 0));
        fill.GradientStops.Add(new GradientStop(ModAccent.Mix(navy, StateColors.Blue, .04), .72));
        fill.Freeze();
        return fill;
    }

    /// <summary>The halo under a card being played (blue) or your own (the mod's colour): a blurred
    /// sibling placed behind it on the sheet, never an Effect on the card and its text.</summary>
    private static Border CardHalo(MatchBox box, Color color)
    {
        var halo = new Border
        {
            Width = Math.Max(0, box.Width - 4),
            Height = Math.Max(0, box.Height - 4),
            CornerRadius = new CornerRadius(9),
            Background = Solid(color),
            IsHitTestVisible = false,
            CacheMode = new BitmapCache(),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = color, BlurRadius = 18, ShadowDepth = 0, Opacity = .5 },
        };
        Canvas.SetLeft(halo, box.X + 2);
        Canvas.SetTop(halo, box.Y + 2);
        return halo;
    }

    private void PaintCardRim(TournamentDetail t, TournamentMatch m, Border card, bool hit)
    {
        var me = TournamentViewerId(t);
        var state = MatchCards.For(m, me, t.Entrants);
        bool mine = state is MatchCardState.Playable or MatchCardState.JoinRoom or MatchCardState.ReturnToRoom;
        bool picked = string.Equals(m.Id, _selectedMatchId, StringComparison.Ordinal);
        bool live = state is MatchCardState.InProgress or MatchCardState.SuperviseRoom;
        var text = ModAccent.TextOn(_pageAccent);
        bool champion = OnChampionsPath(t, m);
        // A match you already played (or will): your way through the draw, rimmed in the mod's colour.
        bool onMyPath = !mine && !string.IsNullOrEmpty(me) && t.Entrants != null
            && t.Entrants.Any(e => (e.Id == m.Entrant1Id || e.Id == m.Entrant2Id)
                && e.MemberIds != null && e.MemberIds.Contains(me, StringComparer.Ordinal));
        card.Background = mine ? Solid(ModAccent.SelectionFill(_pageAccent))
            : live ? LiveCardFill
            : m.Status == "bye" ? Frozen(0x8C, 0x12, 0x21, 0x3A) : Ink("#12213A");
        card.BorderThickness = new Thickness(picked || hit ? 2 : live ? 1.5 : 1);
        // Picked in a finished tournament: a gold ring. Your match keeps the mod's colour; being
        // played is blue; the champion's way through is gold (design 106).
        card.BorderBrush = picked ? (t.Status == "finished" ? Solid(StateColors.Gold) : Solid(text))
            : hit ? Brushes.White
            : mine ? Tint(_pageAccent, .65)
            : live ? Tint(StateColors.Blue, .8)
            : champion ? Tint(StateColors.Gold, .55)
            : onMyPath ? Tint(_pageAccent, .40)
            : Frozen(0x1A, 0xFF, 0xFF, 0xFF);
    }

    private FrameworkElement BuildBracketSide(TournamentDetail t, TournamentMatch m, string? id, int slot, string? me, bool team, bool bye)
    {
        bool known = !string.IsNullOrEmpty(id);
        var e = known ? EntrantById(t, id) : null;
        bool decided = m.Status == "done" && !string.IsNullOrEmpty(m.WinnerEntrantId);
        bool won = decided && m.WinnerEntrantId == id;
        bool lost = decided && known && !won;
        bool isMe = e?.MemberIds != null && !string.IsNullOrEmpty(me) && e.MemberIds.Contains(me);

        var grid = new Grid { Margin = new Thickness(10, 0, 10, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength((team ? 26 : 20) + 8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var seed = Say(e?.Seed?.ToString() ?? "", "SetTinySize", "#61779A", FontWeights.Medium, mono: true);
        seed.TextAlignment = TextAlignment.Right;
        seed.HorizontalAlignment = HorizontalAlignment.Right;
        grid.Children.Add(seed);

        var disc = new Border
        {
            Width = team ? 26 : 20,
            Height = team ? 26 : 20,
            CornerRadius = new CornerRadius(team ? 7 : 10),
            Margin = new Thickness(8, 0, 0, 0),
            Background = known ? isMe ? Solid(_pageAccent) : EntrantDisc(e, team) : Frozen(0x0F, 0xFF, 0xFF, 0xFF),
            BorderBrush = Frozen(0x24, 0xFF, 0xFF, 0xFF),
            BorderThickness = new Thickness(1),
        };
        if (known)
        {
            var letter = Say(team ? EnrollView.CrestOf(e?.DisplayName) : Initial(e?.DisplayName), "SetTinySize", "#FFFFFF", FontWeights.Bold);
            // 18 inside the 1-px rim: at 20 px only the photo, a badge there could not be read.
            disc.Child = LetterWithPhoto(letter, PhotoOf(e, team), 18, 9);
        }
        Grid.SetColumn(disc, 1);
        grid.Children.Add(disc);

        var line = new Grid { Margin = new Thickness(8, 0, 0, 0) };
        line.ColumnDefinitions.Add(new ColumnDefinition());
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = Say(known ? e?.DisplayName ?? "" : m.Round > 1 ? TournamentPage.FeederName(t, m, slot) : Strings.Get("MpTournamentTbd"),
            "SetRowDescSize", !known ? "#8C9CB1" : won ? "#F0F5FB" : lost ? "#8C9CB1" : "#DCE7F5", won ? FontWeights.Bold : FontWeights.Medium);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        line.Children.Add(name);
        if (isMe)
        {
            var you = Say(Strings.Get("TpYouTag"), "SetTinySize", ToHex(ModAccent.TextOn(_pageAccent)), FontWeights.Bold, new Thickness(6, 0, 0, 0));
            Grid.SetColumn(you, 1);
            line.Children.Add(you);
        }
        if (team && e?.Members is { Count: > 0 } members)
        {
            var roster = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            foreach (var member in members)
            {
                bool captain = member.UserId == e.CaptainUserId;
                var memberLetter = Say(Initial(member.DisplayName), "SetTinySize", "#DCE7F5", FontWeights.Bold);
                memberLetter.FontSize = 9;
                var letter = LetterWithPhoto(memberLetter, member.AvatarUrl, 18, 9);
                roster.Children.Add(new Border
                {
                    Width = 18,
                    Height = 18,
                    CornerRadius = new CornerRadius(9),
                    Margin = new Thickness(3, 0, 0, 0),
                    Background = Ink("#2C4560"),
                    BorderBrush = captain ? Solid(ModAccent.TextOn(_pageAccent)) : Frozen(0x2E, 0xFF, 0xFF, 0xFF),
                    BorderThickness = new Thickness(captain ? 1.5 : 1),
                    Child = letter,
                    ToolTip = TooltipHelper.Wrap((member.DisplayName ?? "") + (captain ? " (" + Strings.Get("MtRoleCaptain").ToLowerInvariant() + ")" : "")),
                });
            }
            Grid.SetColumn(roster, 2);
            line.Children.Add(roster);
        }
        Grid.SetColumn(line, 2);
        grid.Children.Add(line);

        var marker = won || !decided || !known
            ? BracketLayout.MarkerFor(bye, decided, won, m.Outcome)
            : BracketLayout.LoserMarkerFor(decided, known, m.Outcome);
        bool final = IsFinishedFinal(t, m);
        FrameworkElement? mark = marker switch
        {
            SideMarker.One => ResultSquare(final ? ResultKind.WonTheFinal : ResultKind.Won, 18),
            SideMarker.Zero => ResultSquare(ResultKind.Lost, 18),
            SideMarker.WalkoverTag => ResultSquare(ResultKind.WonWithoutPlaying, 18),
            SideMarker.ByeTag or SideMarker.DqTag or SideMarker.AbsentTag => TagFor(marker),
            _ => null,
        };
        if (mark != null)
        {
            Grid.SetColumn(mark, 3);
            grid.Children.Add(mark);
        }
        // The winner's row: a green tint from the left (gold in the final of a finished tournament).
        return new Border
        {
            Background = won ? final ? RowTint(StateColors.Gold, .24, 0, .8) : RowTint(StateColors.Green, .16, 0, .8) : null,
            Child = grid,
        };
    }

    /// <summary>What a result square says (design 106).</summary>
    private enum ResultKind { Won, Lost, WonWithoutPlaying, WonTheFinal }

    /// <summary>
    /// A result in a square instead of a loose figure (design 106): green for the winner, a quiet
    /// 0 for the loser, a drawn tick for a win nobody played, gold for the final of a finished
    /// tournament. 18 in the bracket, 22 in the match panel.
    /// </summary>
    private static Border ResultSquare(ResultKind kind, double size)
    {
        bool gold = kind == ResultKind.WonTheFinal;
        bool won = kind != ResultKind.Lost;
        var square = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size > 20 ? 6 : 5),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = gold ? Tint(StateColors.Gold, .24) : won ? Solid(StateColors.WonFill) : Frozen(0x0D, 0xFF, 0xFF, 0xFF),
            BorderBrush = gold ? Tint(StateColors.Gold, .75) : won ? Tint(StateColors.Green, .55) : Frozen(0x1A, 0xFF, 0xFF, 0xFF),
        };
        if (kind == ResultKind.WonWithoutPlaying)
        {
            square.Child = DrawnTick(size * .72, Brushes.White);
            square.ToolTip = TooltipHelper.Wrap(Strings.Get("TpWonWithoutPlaying"));
            return square;
        }
        var figure = Say(kind == ResultKind.Lost ? "0" : "1", size > 20 ? "SetRowDescSize" : "SetDescSize",
            gold ? ToHex(StateColors.GoldText) : won ? "#FFFFFF" : "#8C9CB1", FontWeights.Bold, mono: true);
        figure.HorizontalAlignment = HorizontalAlignment.Center;
        square.Child = figure;
        return square;
    }

    /// <summary>ABSENT, DQ and BYE, each in its own colour (design 106), on the row it is about.</summary>
    private static Border TagFor(SideMarker marker)
    {
        var (text, fill, rim, ink) = marker switch
        {
            SideMarker.AbsentTag => (Strings.Get("TpTagAbsent"), Tint(StateColors.Amber, .14), Tint(StateColors.Amber, .5), ToHex(StateColors.AmberText)),
            SideMarker.DqTag => (Strings.Get("TpTagDq"), Tint(StateColors.Red, .18), Tint(StateColors.Red, .55), ToHex(StateColors.RedText)),
            _ => (Strings.Get("TpTagBye"), Frozen(0x14, 0xFF, 0xFF, 0xFF), Frozen(0x24, 0xFF, 0xFF, 0xFF), "#C3D2E5"),
        };
        return new Border
        {
            Height = 18,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(7, 0, 7, 0),
            CornerRadius = new CornerRadius(9),
            Background = fill,
            BorderBrush = rim,
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = Say(text, "SetTinySize", ink, FontWeights.Bold),
        };
    }

    /// <summary>The final of a tournament that is over: what it shows goes gold.</summary>
    private static bool IsFinishedFinal(TournamentDetail t, TournamentMatch m)
        => t.Status == "finished" && t.Matches is { Count: > 0 } all && m.Round == all.Max(x => x.Round);

    /// <summary>A card the champion went through, in a finished tournament.</summary>
    private static bool OnChampionsPath(TournamentDetail t, TournamentMatch m)
        => t.Status == "finished" && !string.IsNullOrEmpty(t.WinnerEntrantId)
           && (m.Entrant1Id == t.WinnerEntrantId || m.Entrant2Id == t.WinnerEntrantId);

    /// <summary>A left-to-right tint that fades out: the winner's row.</summary>
    private static Brush RowTint(Color c, double from, double to, double at)
    {
        var fill = new LinearGradientBrush { StartPoint = new Point(0, .5), EndPoint = new Point(1, .5) };
        fill.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Round(from * 255), c.R, c.G, c.B), 0));
        fill.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Round(to * 255), c.R, c.G, c.B), at));
        fill.Freeze();
        return fill;
    }

    /// <summary>The legend draws the same pieces as the bracket (design 106), not grey text.</summary>
    private FrameworkElement BuildBracketLegend(TournamentDetail t)
    {
        var line = new WrapPanel();
        FrameworkElement Item(params UIElement[] parts)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 0) };
            foreach (var part in parts) item.Children.Add(part);
            return item;
        }
        line.Children.Add(Item(new Border
        {
            Width = 14,
            Height = 10,
            Margin = new Thickness(0, 0, 6, 0),
            CornerRadius = new CornerRadius(3),
            Background = Solid(ModAccent.SelectionFill(_pageAccent)),
            BorderBrush = Tint(_pageAccent, .65),
            BorderThickness = new Thickness(1),
        }, Say(Strings.Get("TpLegendMine"), "SetDescSize", "#A8BCD2")));
        var live = Dot(7, Solid(StateColors.Blue), 6);
        AttachChipPulse(live);
        line.Children.Add(Item(live, Say(Strings.Get("TpLegendLive"), "SetDescSize", "#A8BCD2")));
        var one = ResultSquare(ResultKind.Won, 18);
        one.Margin = new Thickness(0);
        var zero = ResultSquare(ResultKind.Lost, 18);
        zero.Margin = new Thickness(0);
        line.Children.Add(Item(one, Say("/", "SetDescSize", "#A8BCD2", margin: new Thickness(5, 0, 5, 0)), zero,
            Say(Strings.Get("TpLegendResult"), "SetDescSize", "#A8BCD2", margin: new Thickness(6, 0, 0, 0))));
        if (t.Status == "finished" && !string.IsNullOrEmpty(t.WinnerEntrantId))
            line.Children.Add(Item(new Border { Width = 14, Height = 2, CornerRadius = new CornerRadius(1), Margin = new Thickness(0, 0, 6, 0), Background = Tint(StateColors.Gold, .9), VerticalAlignment = VerticalAlignment.Center },
                Say(Strings.Get("TpLegendChampionPath"), "SetDescSize", "#A8BCD2")));
        foreach (var (tag, key) in new[] { (SideMarker.AbsentTag, "TpLegendAbsent"), (SideMarker.DqTag, "TpLegendDq"), (SideMarker.ByeTag, "TpLegendBye") })
        {
            var pill = TagFor(tag);
            pill.Margin = new Thickness(0, 0, 6, 0);
            line.Children.Add(Item(pill, Say(Strings.Get(key), "SetDescSize", "#A8BCD2")));
        }
        return new Border { Padding = new Thickness(16, 9, 16, 9), BorderBrush = Frozen(0x12, 0xFF, 0xFF, 0xFF), BorderThickness = new Thickness(0, 1, 0, 0), Child = line };
    }

    private void PickBracketMatch(string id)
    {
        var t = _pageTournament;
        if (t == null) return;
        var previous = _selectedMatchId;
        _selectedMatchId = string.Equals(previous, id, StringComparison.Ordinal) ? null : id;
        if (_demoTournaments)
        {
            RenderTournamentDetail();
            return;
        }
        foreach (var key in new[] { previous, _selectedMatchId })
        {
            if (key == null || !_bracketCards.TryGetValue(key, out var card)) continue;
            var m = t.Matches?.FirstOrDefault(x => x.Id == key);
            if (m != null) PaintCardRim(t, m, card, false);
        }
        FillBracketPick(t, TournamentViewerId(t));
    }

    private void FillBracketPick(TournamentDetail t, string? me)
    {
        var host = _bracketPick;
        if (host == null) return;
        host.Children.Clear();
        var m = t.Matches?.FirstOrDefault(x => string.Equals(x.Id, _selectedMatchId, StringComparison.Ordinal));
        if (m != null) host.Children.Add(BuildBracketPickPanel(t, m, me));
    }

    /// <summary>
    /// The picked match: who plays it, how it stands, and what the viewer can do about it.
    ///
    /// <para>Awarding and replaying act on other people's game, so they sit under their own heading
    /// and each asks before it acts. A played result has nothing to offer here: undoing one is the
    /// maintainer's CLI, and replaying only exists for a match still undecided.</para>
    /// </summary>
    private FrameworkElement BuildBracketPickPanel(TournamentDetail t, TournamentMatch m, string? me)
    {
        var stack = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Say(Strings.Format("TpPickRound", TournamentPage.RoundName(t, m.Round).ToUpperInvariant(), m.Position + 1), "SetCaptionSize", "#C3D2E5", FontWeights.Bold);
        title.TextWrapping = TextWrapping.Wrap;
        head.Children.Add(title);
        var close = Secondary("✕", () => PickBracketMatch(m.Id), 28);
        close.Width = 28;
        close.MinWidth = 28;
        close.Padding = new Thickness(0);
        close.ToolTip = TooltipHelper.Wrap(Strings.Get("TpClose"));
        Grid.SetColumn(close, 1);
        head.Children.Add(close);
        stack.Children.Add(head);

        bool team = TournamentPage.IsTeam(t);
        // Supervise: the fabricated tournaments, or a real one on a server with the read-only view.
        var state = MatchCards.For(m, me, t.Entrants,
            (_demoTournaments || _tournaments?.LiveView == true) && TournamentPermissions.IsOwnerOrManager(t, me));
        bool bye = state == MatchCardState.Bye;
        bool decided = m.Status == "done" && !string.IsNullOrEmpty(m.WinnerEntrantId);
        stack.Children.Add(BuildPickSide(t, m, m.Entrant1Id, 1, me, team, decided));
        if (!bye) stack.Children.Add(BuildPickSide(t, m, m.Entrant2Id, 2, me, team, decided));

        var winner = TournamentPage.NameOf(t, m.WinnerEntrantId);
        var loser = m.WinnerEntrantId == m.Entrant1Id ? TournamentPage.NameOf(t, m.Entrant2Id) : TournamentPage.NameOf(t, m.Entrant1Id);
        var accent = ToHex(ModAccent.TextOn(_pageAccent));
        var doneDot = ToHex(IsFinishedFinal(t, m) ? StateColors.Gold : StateColors.Green);
        var (text, ink, dot) = state switch
        {
            MatchCardState.Bye => (Strings.Format("TpStateBye", TournamentPage.NameOf(t, m.Entrant1Id ?? m.Entrant2Id)), "#C3D2E5", "#8C9CB1"),
            MatchCardState.InProgress or MatchCardState.SuperviseRoom => (Strings.Get("TpStateLive"), ToHex(StateColors.BlueText), ToHex(StateColors.Blue)),
            MatchCardState.Playable or MatchCardState.ReturnToRoom => (Strings.Get("TpStateMine"), accent, accent),
            MatchCardState.JoinRoom => (Strings.Get("TpStateJoin"), accent, accent),
            MatchCardState.Done when m.Outcome == "walkover" => (Strings.Format("TpStateWalkover", winner, loser), "#DCE7F5", doneDot),
            MatchCardState.Done when m.Outcome == "dq" => (Strings.Format("TpStateDq", winner, loser), "#DCE7F5", doneDot),
            MatchCardState.Done => (Strings.Format("TpStateWon", winner), "#DCE7F5", doneDot),
            _ when string.IsNullOrEmpty(m.Entrant1Id) || string.IsNullOrEmpty(m.Entrant2Id) => (Strings.Get("TpStateWaiting"), "#C3D2E5", "#8C9CB1"),
            _ => (Strings.Get("TpStateToPlay"), "#C3D2E5", "#8C9CB1"),
        };
        var stateLine = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var stateDot = Dot(7, Ink(dot), 8);
        if (state is MatchCardState.InProgress or MatchCardState.SuperviseRoom) AttachChipPulse(stateDot);
        stateLine.Children.Add(stateDot);
        var stateText = Say(text, "SetRowDescSize", ink, FontWeights.SemiBold);
        stateText.TextWrapping = TextWrapping.Wrap;
        stateText.MaxWidth = 250;
        stateLine.Children.Add(stateText);
        stack.Children.Add(stateLine);

        // A tie that WAS played and did not move: why, from the server. Without it a played match
        // just sits undecided and nobody can tell whether to wait, replay it or award it.
        if (TournamentRefusals.PendingReportKey(m.PendingReport?.State) is { } reportKey)
        {
            var note = Say(Strings.Get(reportKey), "SetControlSize", m.PendingReport!.State == "awaiting_confirmation" ? "#A8BCD2" : "#F5BD76", margin: new Thickness(0, 12, 0, 0));
            note.Tag = PendingReportTag;
            note.TextWrapping = TextWrapping.Wrap;
            stack.Children.Add(note);
        }

        if (state == MatchCardState.SuperviseRoom)
        {
            var watch = Secondary(Strings.Get("MpTournamentWatchRoom"), () => OpenMatchWatch(t, m));
            watch.Margin = new Thickness(0, 12, 0, 0);
            stack.Children.Add(watch);
        }
        string? noteText = state switch
        {
            MatchCardState.InProgress or MatchCardState.SuperviseRoom => Strings.Get(team ? "TpNoteLiveTeams" : "TpNoteLive"),
            MatchCardState.Playable => Strings.Format("TpNoteMine", Strings.Get("MpTournamentPlayMyMatch")),
            MatchCardState.JoinRoom => Strings.Format("TpNoteMine", Strings.Get("MpTournamentJoinRoom")),
            MatchCardState.ReturnToRoom => Strings.Format("TpNoteMine", Strings.Get("MpTournamentReturnToRoom")),
            _ => null,
        };
        if (noteText != null)
        {
            var note = Say(noteText, "SetControlSize", "#A8BCD2", margin: new Thickness(0, 12, 0, 0));
            note.TextWrapping = TextWrapping.Wrap;
            stack.Children.Add(note);
        }

        var organiser = BuildPickOrganiser(t, m, me);
        if (organiser != null) stack.Children.Add(organiser);

        return new Border
        {
            Width = 300,
            Background = Frozen(0x9E, 0x0C, 0x16, 0x26),
            BorderBrush = Frozen(0x14, 0xFF, 0xFF, 0xFF),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Child = new ScrollViewer
            {
                Padding = new Thickness(0),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new Border { Padding = new Thickness(16, 14, 16, 14), Child = stack },
            },
        };
    }

    private FrameworkElement BuildPickSide(TournamentDetail t, TournamentMatch m, string? id, int slot, string? me, bool team, bool decided)
    {
        bool known = !string.IsNullOrEmpty(id);
        var e = known ? EntrantById(t, id) : null;
        bool isMe = e?.MemberIds != null && !string.IsNullOrEmpty(me) && e.MemberIds.Contains(me);
        bool won = decided && m.WinnerEntrantId == id;

        string sub = !known ? Strings.Get("TpSideUnknown")
            : e?.Seed == null ? ""
            : Strings.Format("TpSeedSub", e.Seed);
        if (team && e?.Members is { Count: > 0 } members)
            sub += (sub.Length > 0 ? " · " : "") + string.Join(", ", members.Select(x => x.UserId == e.CaptainUserId ? Strings.Format("TpCaptainMark", x.DisplayName ?? "") : x.DisplayName));

        var name = Say(known ? e?.DisplayName ?? "" : m.Round > 1 ? TournamentPage.FeederName(t, m, slot) : Strings.Get("MpTournamentTbd"),
            "SetRowTitleSize", !known || (decided && !won) ? "#A8BCD2" : "#FFFFFF", FontWeights.SemiBold);
        name.TextWrapping = TextWrapping.Wrap;
        var words = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(name);
        if (sub.Length > 0)
        {
            var small = Say(sub, "SetDescSize", "#A8BCD2", margin: new Thickness(0, 3, 0, 0));
            small.TextWrapping = TextWrapping.Wrap;
            words.Children.Add(small);
        }
        Grid.SetColumn(words, 1);

        var disc = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(team ? 8 : 15),
            Background = !known ? Ink("#2C3E55") : isMe ? Solid(_pageAccent) : EntrantDisc(e, team),
            Child = LetterWithPhoto(Say(!known ? "?" : team ? EnrollView.CrestOf(e?.DisplayName) : Initial(e?.DisplayName), "SetDescSize", "#FFFFFF", FontWeights.Bold),
                known ? PhotoOf(e, team) : null, 30, team ? 8 : 15),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(disc);
        grid.Children.Add(words);

        // The same squares and tags as the bracket, at 22 (design 106).
        bool final = IsFinishedFinal(t, m);
        var marker = m.Status == "bye" ? SideMarker.ByeTag
            : !decided || !known ? SideMarker.None
            : won ? BracketLayout.MarkerFor(false, true, true, m.Outcome)
            : BracketLayout.LoserMarkerFor(true, true, m.Outcome);
        FrameworkElement? mark = marker switch
        {
            SideMarker.One => ResultSquare(final ? ResultKind.WonTheFinal : ResultKind.Won, 22),
            SideMarker.Zero => ResultSquare(ResultKind.Lost, 22),
            SideMarker.WalkoverTag => ResultSquare(ResultKind.WonWithoutPlaying, 22),
            SideMarker.ByeTag or SideMarker.DqTag or SideMarker.AbsentTag => TagFor(marker),
            _ => null,
        };
        if (mark != null)
        {
            Grid.SetColumn(mark, 2);
            grid.Children.Add(mark);
        }

        return new Border
        {
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1),
            Background = isMe ? Solid(ModAccent.SelectionFill(_pageAccent))
                : won ? final ? RowTint(StateColors.Gold, .22, .06, 1) : RowTint(StateColors.Green, .18, .05, 1)
                : !known ? Frozen(0x59, 0x0A, 0x13, 0x21) : Frozen(0x73, 0x0A, 0x13, 0x21),
            BorderBrush = isMe ? Tint(_pageAccent, .65) : won ? (final ? Tint(StateColors.Gold, .6) : Tint(StateColors.Green, .5)) : Frozen(0x14, 0xFF, 0xFF, 0xFF),
            Child = grid,
        };
    }

    private FrameworkElement? BuildPickOrganiser(TournamentDetail t, TournamentMatch m, string? me)
    {
        bool canAward = TournamentPermissions.CanAwardMatch(t, me, m);
        bool canReplay = TournamentPermissions.CanReplayMatch(t, me, m);
        bool canDq = TournamentPermissions.CanAwardOrDisqualify(t, me) && m.Status == "pending"
                     && !string.IsNullOrEmpty(m.Entrant1Id) && !string.IsNullOrEmpty(m.Entrant2Id);
        if (!canAward && !canReplay && !canDq) return null;

        var stack = new StackPanel();
        stack.Children.Add(Say(Strings.Get("TpAsOrganiser"), "SetCaptionSize", "#A8BCD2", FontWeights.Bold));
        void Add(Button button)
        {
            button.Margin = new Thickness(0, 8, 0, 0);
            stack.Children.Add(button);
        }
        if (canAward)
        {
            foreach (var id in new[] { m.Entrant1Id!, m.Entrant2Id! })
            {
                var name = TournamentPage.NameOf(t, id);
                var winner = id;
                Add(Secondary(Strings.Format("MpTournamentAwardTo", name), () => _ = ConfirmAwardAsync(t, m, winner, name)));
            }
        }
        if (canReplay) Add(Secondary(Strings.Get("MpTournamentReplay"), () => _ = ConfirmReplayAsync(t, m)));
        if (canDq)
        {
            var menu = new ContextMenu();
            foreach (var id in new[] { m.Entrant1Id!, m.Entrant2Id! })
            {
                if (EntrantById(t, id) is not { } entrant) continue;
                var item = new MenuItem { Header = entrant.DisplayName };
                item.Click += (_, _) => _ = ConfirmDisqualifyAsync(t, entrant);
                menu.Items.Add(item);
            }
            Button? dq = null;
            dq = Secondary(Strings.Get("TpDisqualify"), () =>
            {
                menu.PlacementTarget = dq;
                menu.IsOpen = true;
            }, ink: Ink("#F0A6A6"), rim: Frozen(0x99, 0xC8, 0x4A, 0x4A));
            dq.ContextMenu = menu;
            Add(dq);
        }
        var note = Say(Strings.Get("TpOrgNoteOpen"), "SetDescSize", "#8C9CB1", margin: new Thickness(0, 8, 0, 0));
        note.TextWrapping = TextWrapping.Wrap;
        stack.Children.Add(note);
        return new Border
        {
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(0, 12, 0, 0),
            BorderBrush = Frozen(0x14, 0xFF, 0xFF, 0xFF),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = stack,
        };
    }

    private void ScrollBracketToRound(int round)
    {
        var t = _pageTournament;
        if (t == null || _bracketScroll == null) return;
        var first = t.Matches?.Min(m => m.Round) ?? round;
        double column = TournamentPage.IsTeam(t) ? TournamentPage.ColumnWidthTeam : TournamentPage.ColumnWidthSolo;
        double x = TournamentPage.Pad + (round - first) * (column + TournamentPage.Gutter);
        _bracketScroll.ScrollToHorizontalOffset(Math.Max(0, x * _bracketZoom / 100 - 12));
        if (TournamentPage.VerticalOffsetForRound(_bracketBoxes, round, _bracketZoom / 100,
                _bracketScroll.VerticalOffset, _bracketScroll.ViewportHeight) is { } y)
            _bracketScroll.ScrollToVerticalOffset(y);
    }

    private void GoToMyMatch()
    {
        if (_mineBox == null || _bracketScroll == null) return;
        double z = _bracketZoom / 100;
        _bracketScroll.ScrollToHorizontalOffset(Math.Max(0, _mineBox.X * z - 60));
        _bracketScroll.ScrollToVerticalOffset(Math.Max(0, _mineBox.Y * z - 80));
    }

    /// <summary>A zoom the player chose (a button, the wheel, Fit): kept for this tournament, so it
    /// stops fitting itself.</summary>
    private void ChooseBracketZoom(double value)
    {
        var z = BracketZoom.Clamp(value);
        if (_pageTournament is { } t) _bracketZoomManual[t.Id] = z;
        SetBracketZoom(z);
    }

    /// <summary>Fit: the whole sheet on screen, chosen like any other zoom.</summary>
    private void FitBracketZoom()
    {
        if (_bracketScroll is not { } scroll) return;
        ChooseBracketZoom(BracketZoom.Fit(_bracketSheetSize.Width, _bracketSheetSize.Height, scroll.ViewportWidth, scroll.ViewportHeight));
    }

    /// <summary>
    /// The sheet's text off the pixel grid while it is scaled, back on it at exactly 100 %: text laid
    /// out for one size and then stretched by a transform smudges (the same switch
    /// <see cref="UiScale.SetTextCrispForScale"/> makes for a shrunken surface).
    /// </summary>
    private static void SetBracketTextMode(DependencyObject sheet, double percent)
    {
        bool scaled = Math.Abs(percent - 100) > .5;
        TextOptions.SetTextFormattingMode(sheet, scaled ? TextFormattingMode.Ideal : TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(sheet, scaled ? TextRenderingMode.Grayscale : TextRenderingMode.ClearType);
        TextOptions.SetTextHintingMode(sheet, scaled ? TextHintingMode.Animated : TextHintingMode.Fixed);
    }

    /// <summary>The round titles over their columns: shifted by where the centred sheet starts and
    /// by the horizontal scroll.</summary>
    private void AlignBracketHeads()
    {
        if (_bracketScroll is not { } scroll || _bracketHeadShift == null) return;
        double sheetWidth = _bracketSheetSize.Width * _bracketZoom / 100;
        _bracketHeadOffset = Math.Max(0, (scroll.ViewportWidth - sheetWidth) / 2);
        _bracketHeadShift.X = _bracketHeadOffset - scroll.HorizontalOffset;
    }

    /// <summary>The zoom is one transform: the cards stay where they are and nothing is rebuilt but
    /// the strip of rounds, whose widths follow it.</summary>
    private void SetBracketZoom(double value)
    {
        _bracketZoom = BracketZoom.Clamp(value);
        if (_zoomText != null) _zoomText.Text = _bracketZoom + " %";
        if (_bracketScale != null)
        {
            _bracketScale.ScaleX = _bracketZoom / 100;
            _bracketScale.ScaleY = _bracketZoom / 100;
        }
        if (_bracketSheetHost is Border { Child: Canvas sheetCanvas }) SetBracketTextMode(sheetCanvas, _bracketZoom);
        var t = _pageTournament;
        if (t != null && _bracketHeads is StackPanel old && old.Parent is Border strip)
        {
            var sheet = TournamentPage.Sheet(t);
            double width = sheet.Width + (string.IsNullOrEmpty(t.WinnerEntrantId) ? 0 : sheet.Gutter + 200);
            var heads = BuildBracketHeads(t, sheet, width);
            heads.RenderTransform = _bracketHeadShift;
            strip.Child = heads;
            _bracketHeads = heads;
        }
        // The extent changes on the next layout and raises ScrollChanged, which aligns again.
        AlignBracketHeads();
    }

    // ---------------------------------------------------------------- participants

    /// <summary>Solicitudes, Dentro and Fuera del cuadro, as tiles that fill the width.</summary>
    internal UIElement BuildEntrantsList(TournamentDetail t, string? me)
    {
        var host = new StackPanel();
        var previous = _peopleBody;
        _peopleBody = host;
        FillPeople(t, me, out _);
        _peopleBody = previous;
        return host;
    }

    private void FillPeople(TournamentDetail t, string? me, out int found)
    {
        found = 0;
        var host = _peopleBody;
        if (host == null) return;
        host.Children.Clear();

        var query = _tournamentQuery;
        bool Keep(TournamentEntrant e) => query.Length == 0 || TournamentPage.Matches(e, query);
        var all = t.Entrants ?? new List<TournamentEntrant>();
        var pending = all.Where(e => e.Status == "pending").Where(Keep).ToList();
        var inside = all.Where(e => e.Status == "confirmed").OrderBy(e => e.Seed ?? int.MaxValue).Where(Keep).ToList();
        var outside = all.Where(e => e.Status is "waitlist" or "withdrawn" or "rejected" or "disqualified").Where(Keep).ToList();
        found = pending.Count + inside.Count + outside.Count;
        bool team = TournamentPage.IsTeam(t);
        bool hasBracket = t.Matches is { Count: > 0 };

        void Group(string title, string count, string ink, string? hint, double min, IEnumerable<TournamentEntrant> rows)
        {
            var head = new WrapPanel { Margin = new Thickness(4, host.Children.Count == 0 ? 0 : 18, 4, 7) };
            head.Children.Add(Say(title, "SetCaptionSize", ink, FontWeights.Bold));
            head.Children.Add(Say(count, "SetCaptionSize", ink, FontWeights.SemiBold, new Thickness(8, 0, 0, 0), mono: true));
            if (!string.IsNullOrEmpty(hint)) head.Children.Add(Say(hint, "SetControlSize", "#8C9CB1", margin: new Thickness(8, 0, 0, 0)));
            host.Children.Add(head);
            var tiles = new AutoFillPanel { MinColumn = min, Gap = 8 };
            foreach (var e in rows) tiles.Children.Add(BuildEntrantTile(t, e, me));
            host.Children.Add(tiles);
        }

        if (pending.Count > 0)
            Group(Strings.Get("MpTournamentGroupRequests"), pending.Count.ToString(), "#F5BD76", Strings.Get("TpHintRequests"), 300, pending);

        // A draft team tournament: until the teams exist the bracket cannot be drawn and nothing
        // else on the page would say why.
        if (TournamentPermissions.CanFormDraftTeams(t, me))
        {
            var solos = DraftPlayersWithoutTeam(t);
            if (solos.Count > 0) host.Children.Add(BuildDraftTeamsBar(t, solos.Count));
        }

        if (inside.Count > 0 || pending.Count == 0)
        {
            string count = t.Capacity is int cap && !hasBracket ? Strings.Format("TpOfCapacity", inside.Count, cap)
                : team ? Strings.Format("TpTeamsCount", inside.Count) : inside.Count.ToString();
            Group(Strings.Get("MpTournamentGroupIn"), count, "#C3D2E5",
                hasBracket ? team ? Strings.Get("TpHintLineups") : null : Strings.Get("TpHintIn"),
                team ? 280 : 220, inside);
        }
        if (outside.Count > 0)
            Group(Strings.Get("MpTournamentGroupOut"), outside.Count.ToString(), "#8C9CB1", null, 220, outside);

        if (found == 0)
        {
            var empty = Say(query.Length > 0 ? Strings.Format("TpNobodyMatches", query)
                : t.Status == "draft" ? Strings.Get("TpNotOpenYet") : Strings.Get("MpTournamentsEmpty"),
                "DialogBodySize", "#A8BCD2", margin: new Thickness(20, 40, 20, 40));
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            host.Children.Clear();
            host.Children.Add(empty);
        }
    }

    internal const string PerformanceTag = "TournamentPerformance";

    private FrameworkElement BuildEntrantTile(TournamentDetail t, TournamentEntrant e, string? me)
    {
        bool team = TournamentPage.IsTeam(t);
        bool isMine = e.MemberIds != null && !string.IsNullOrEmpty(me) && e.MemberIds.Contains(me);
        bool isOut = MatchCards.EntrantIsOut(e);
        bool needsTeam = TournamentPermissions.NeedsTeam(t, e);
        var mergedInto = TournamentPermissions.MergedInto(t, e);
        bool noSeed = e.Status == "confirmed" && !e.Seed.HasValue && !needsTeam && t.Status == "ready";
        bool pending = e.Status == "pending";

        var seed = Say(e.Seed?.ToString() ?? "—", "SetCaptionSize", "#8C9CB1", FontWeights.SemiBold, mono: true);
        seed.TextAlignment = TextAlignment.Right;
        seed.HorizontalAlignment = HorizontalAlignment.Right;

        var disc = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(team ? 8 : 15),
            Background = isOut ? Ink("#2C3E55") : isMine ? Solid(_pageAccent) : EntrantDisc(e, team),
            Child = LetterWithPhoto(Say(team ? EnrollView.CrestOf(e.DisplayName) : Initial(e.DisplayName), "SetDescSize", "#FFFFFF", FontWeights.Bold),
                PhotoOf(e, team), 30, team ? 8 : 15),
        };
        Grid.SetColumn(disc, 1);

        var nameLine = new Grid();
        nameLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        nameLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = Say(e.DisplayName ?? "", "SetRowTitleSize", isOut ? "#A8BCD2" : "#F0F5FB", FontWeights.SemiBold);
        name.FontSize = 13;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        nameLine.Children.Add(name);
        if (isMine)
        {
            var you = Say(Strings.Get("TpYouTag"), "MpTagSize", ToHex(ModAccent.TextOn(_pageAccent)), FontWeights.Bold, new Thickness(7, 0, 0, 0));
            Grid.SetColumn(you, 1);
            nameLine.Children.Add(you);
        }
        // The badge after the name and its tag, 8 px clear of them (2026-11-11).
        if (EntrantBadge(t, e, 20) is { } entrantBadge)
        {
            nameLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            entrantBadge.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(entrantBadge, 2);
            nameLine.Children.Add(entrantBadge);
        }
        var words = new StackPanel { Margin = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(nameLine);

        if (team && e.Members is { Count: > 0 } members)
        {
            var roster = new WrapPanel { Margin = new Thickness(0, 3, 0, 0) };
            foreach (var member in members)
            {
                bool captain = member.UserId == e.CaptainUserId;
                var chip = Pill(Say((member.DisplayName ?? "") + (captain ? " ©" : ""), "SetCaptionSize", "#DCE7F5", FontWeights.Medium), 20, 7, 7,
                    Frozen(0x0F, 0xFF, 0xFF, 0xFF), captain ? Tint(_pageAccent, .6) : Frozen(0x1F, 0xFF, 0xFF, 0xFF), 10);
                chip.Margin = new Thickness(0, 0, 4, 3);
                roster.Children.Add(chip);
            }
            words.Children.Add(roster);
        }

        var fate = TournamentPage.FateOf(t, e);
        bool isChampion = !string.IsNullOrEmpty(t.WinnerEntrantId) && t.WinnerEntrantId == e.Id;
        var (text, ink, dot) = fate != null
            ? (fate.Text, fate.Tone switch { "champion" => "#F3D995", "ok" => "#8FE0B0", "caution" => "#F5BD76", "danger" => "#F0A6A6", "plain" => "#DCE7F5", _ => "#A8BCD2" },
               fate.Tone switch { "champion" => "#E6C06A", "ok" => "#4FD68A", "caution" => "#F4900C", "danger" => "#C84A4A", "plain" => "#C3D2E5", _ => "#61779A" })
            : needsTeam ? (Strings.Get("MpTournamentNeedsTeam"), "#F5BD76", "#F4900C")
            : noSeed ? (Strings.Get("MpTournamentNoSeed"), "#F5BD76", "#F4900C")
            : pending && TournamentPermissions.CanDecideEntrant(t, me, e) ? (Strings.Get("MpTournamentAskedToEnter"), "#F5BD76", "#F4900C")
            : mergedInto != null ? (Strings.Format("MpTournamentInTeam", mergedInto), "#A8BCD2", "#61779A")
            : e.Status switch
            {
                "confirmed" => (EntrantStatusLabel(e.Status), "#8FE0B0", "#4FD68A"),
                "pending" => (EntrantStatusLabel(e.Status), "#F5BD76", "#F4900C"),
                "disqualified" => (EntrantStatusLabel(e.Status), "#F0A6A6", "#C84A4A"),
                _ => (EntrantStatusLabel(e.Status), "#8C9CB1", "#61779A"),
            };
        var status = new Grid { Margin = new Thickness(0, 3, 0, 0) };
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        status.ColumnDefinitions.Add(new ColumnDefinition());
        status.Children.Add(Dot(6, Ink(dot), 6));
        var label = Say(text, "SetDescSize", ink, FontWeights.Medium);
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(label, 1);
        status.Children.Add(label);
        // The level he played at, at the right end of the line (2026-11-18): mono and neutral —
        // gold stays the champion's. The status trims first; the figure never does.
        if (TournamentPerformanceView.For(e.Performance, isMine) is { } perf)
        {
            status.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var figure = Say(perf.Figure != null ? Strings.Format("TpPerfFigure", perf.Figure) : Strings.Get("TpPerfNone"),
                "SetDescSize", perf.HiddenWhy != null ? "#8C9CB1" : "#DCE7F5", FontWeights.SemiBold,
                new Thickness(10, 0, 0, 0), mono: true);
            figure.Tag = PerformanceTag;
            var tip = perf.HiddenWhy
                ?? (perf.BestWin != null ? Strings.Format("TpPerfBestWin", perf.BestWin) + "\n" : "") + Strings.Get("TpPerfTip");
            figure.ToolTip = TooltipHelper.Wrap(tip);
            Grid.SetColumn(figure, 2);
            status.Children.Add(figure);
        }
        words.Children.Add(status);
        Grid.SetColumn(words, 2);

        var actions = BuildEntrantActions(t, e, me, needsTeam);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(seed);
        disc.Margin = new Thickness(9, 0, 0, 0);
        disc.Width = 30;
        grid.ColumnDefinitions[1].Width = new GridLength(39);
        grid.Children.Add(disc);
        words.Margin = new Thickness(9, 0, 0, 0);
        grid.Children.Add(words);
        if (actions != null)
        {
            Grid.SetColumn(actions, 3);
            grid.Children.Add(actions);
        }

        return new Border
        {
            MinHeight = 54,
            Padding = new Thickness(10, 8, 10, 8),
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1),
            // The champion's tile is gold even when it is yours: nothing of the champion takes the mod's colour.
            Background = isChampion ? GoldTint(.2, .05) : isMine ? Solid(ModAccent.SelectionFill(_pageAccent)) : pending ? Frozen(0x14, 0xF4, 0x90, 0x0C) : isOut ? Frozen(0x47, 0x0A, 0x13, 0x21) : Frozen(0x73, 0x0A, 0x13, 0x21),
            BorderBrush = isChampion ? Tint(StateColors.Gold, .55) : isMine ? Tint(_pageAccent, .65) : pending ? Frozen(0x59, 0xF4, 0x90, 0x0C) : Frozen(0x14, 0xFF, 0xFF, 0xFF),
            Child = grid,
        };
    }

    /// <summary>A left-to-right gold tint over the page's glass: the champion's row (design 106).</summary>
    private static Brush GoldTint(double from, double to, double reach = 1)
    {
        var fill = new LinearGradientBrush { StartPoint = new Point(0, .5), EndPoint = new Point(1, .5) };
        var g = StateColors.Gold;
        fill.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Round(from * 255), g.R, g.G, g.B), 0));
        fill.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Round(to * 255), g.R, g.G, g.B), reach));
        fill.Freeze();
        return fill;
    }

    /// <summary>The players of an entry whose profile can be opened: never the viewer, never a sample.</summary>
    private IEnumerable<(string Id, string Name, string? Photo)> EntrantProfiles(TournamentEntrant e, string? me)
    {
        if (_demoTournaments || _session?.Status != MultiplayerSession.SessionStatus.SignedIn) yield break;
        if (e.Members is { Count: > 0 } members)
        {
            foreach (var m in members)
                if (!string.IsNullOrEmpty(m.UserId) && m.UserId != me)
                    yield return (m.UserId, m.DisplayName ?? e.DisplayName ?? "", m.AvatarUrl);
            yield break;
        }
        // An older server: the captain is the player of a solo entry.
        if (!string.IsNullOrEmpty(e.CaptainUserId) && e.CaptainUserId != me && e.Kind != "team")
            yield return (e.CaptainUserId!, e.DisplayName ?? "", null);
    }

    private FrameworkElement? BuildEntrantActions(TournamentDetail t, TournamentEntrant e, string? me, bool needsTeam)
    {
        bool team = TournamentPage.IsTeam(t);
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0, 0, 0) };
        void Act(string caption, Brush? ink, Func<Task> action)
        {
            var button = Secondary(caption, () => _ = RunTournamentActionAsync(action), 28, ink);
            button.MinWidth = 0;
            button.Padding = new Thickness(11, 0, 11, 0);
            button.SetResourceReference(Control.FontSizeProperty, "SetControlSize");
            if (stack.Children.Count > 0) button.Margin = new Thickness(0, 5, 0, 0);
            stack.Children.Add(button);
        }

        if (e.Status == "pending" && TournamentPermissions.CanDecideEntrant(t, me, e))
        {
            Act(Strings.Get("TpAcceptShort"), Ink("#8FE0B0"), () => TournamentApi.AcceptEntrantAsync(t.Id, e.Id));
            Act(Strings.Get("MpTournamentReject"), null, () => TournamentApi.RejectEntrantAsync(t.Id, e.Id));
        }
        else if (needsTeam && e.Status != "pending" && TournamentPermissions.CanFormDraftTeams(t, me))
        {
            // The odd one out of a draft: nobody to make a team with, and the bracket cannot be
            // drawn while he is in it. Rejected before the bracket exists, never after.
            Act(Strings.Get("MpTournamentRemoveEntrant"), null, () => TournamentApi.RejectEntrantAsync(t.Id, e.Id));
        }
        else if (e.Status == "waitlist" && TournamentPermissions.IsOwner(t, me))
        {
            // Accept takes a seat if there is one and leaves them waiting if there is not, so the
            // same route promotes a waitlisted entrant. The server decides which.
            Act(Strings.Get("MpTournamentGivePlace"), null, () => TournamentApi.AcceptEntrantAsync(t.Id, e.Id));
        }

        // What an organiser does TO somebody takes a deliberate extra click. A request is not in
        // the tournament yet: Reject is its answer.
        var menu = new ContextMenu();
        // Whose entry this is, first: one player in a 1v1, each member of a team. Real
        // tournaments only, signed in — the sample players have no profile to ask for.
        foreach (var (id, who, photo) in EntrantProfiles(e, me))
        {
            var view = new MenuItem
            {
                Header = team ? Strings.Format("TpViewProfileOf", who) : Strings.Get("TpViewProfile"),
            };
            view.Click += (_, _) => OpenProfileOf(id, who, null, photo);
            menu.Items.Add(view);
        }
        bool hasProfileItems = menu.Items.Count > 0;
        if (TournamentPermissions.CanAwardOrDisqualify(t, me) && e.Status is "confirmed" or "waitlist")
        {
            if (hasProfileItems) menu.Items.Add(new Separator());
            var dq = new MenuItem { Header = Strings.Get("MpTournamentDisqualify") };
            dq.Click += (_, _) => _ = ConfirmDisqualifyAsync(t, e);
            menu.Items.Add(dq);
        }
        if (menu.Items.Count > 0)
        {
            Button? more = null;
            more = Secondary("⋯", () =>
            {
                menu.PlacementTarget = more;
                menu.IsOpen = true;
            }, 28);
            more.MinWidth = 30;
            more.Padding = new Thickness(0);
            more.ContextMenu = menu;
            more.ToolTip = TooltipHelper.Wrap(Strings.Get("MpTournamentMoreActions"));
            if (stack.Children.Count > 0) more.Margin = new Thickness(0, 5, 0, 0);
            stack.Children.Add(more);
        }
        return stack.Children.Count > 0 ? stack : null;
    }

    // ---------------------------------------------------------------- organise

    private FrameworkElement BuildOrganise(TournamentDetail t, string? me)
    {
        var tiles = new AutoFillPanel { MinColumn = 320, Gap = 14, Margin = new Thickness(16) };

        void Tile(string title, string titleInk, string body, Brush rim, FrameworkElement? middle, params Button[] buttons)
        {
            var stack = new StackPanel();
            stack.Children.Add(Say(title, "SetCaptionSize", titleInk, FontWeights.Bold));
            var text = Say(body, "SetRowDescSize", "#C3D2E5", margin: new Thickness(0, 10, 0, 0));
            text.TextWrapping = TextWrapping.Wrap;
            stack.Children.Add(text);
            if (middle != null) stack.Children.Add(middle);
            if (buttons.Length > 0)
            {
                var row = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
                foreach (var button in buttons)
                {
                    button.MinWidth = 200;
                    button.Margin = new Thickness(0, 0, 8, 0);
                    row.Children.Add(button);
                }
                stack.Children.Add(row);
            }
            tiles.Children.Add(new Border
            {
                Padding = new Thickness(16, 14, 16, 14),
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1),
                BorderBrush = rim,
                Background = Frozen(0x8C, 0x0A, 0x13, 0x21),
                Child = stack,
            });
        }

        var rim = Frozen(0x1A, 0xFF, 0xFF, 0xFF);
        // A private tournament's code: the only way somebody new finds it. The server sends it to
        // the owner and the co-organisers alone, so its presence is the permission.
        if (t.IsPrivate && !string.IsNullOrEmpty(t.InviteCode))
        {
            var code = t.InviteCode!;
            var codeText = Say(code, "SetRowTitleSize", "#E2D5F7", FontWeights.Bold, new Thickness(0, 12, 0, 0));
            codeText.FontFamily = new FontFamily("Consolas, Courier New, monospace");
            codeText.IsHitTestVisible = true;
            System.Windows.Automation.AutomationProperties.SetAutomationId(codeText, "TournamentInviteCode");
            Button? copy = null;
            copy = Secondary(Strings.Get("TpOrgCodeCopy"), () =>
            {
                try { Clipboard.SetText(code); } catch { return; }
                if (copy != null) copy.Content = Strings.Get("TpOrgCodeCopied");
            });
            var codeButtons = new List<Button> { copy };
            if (TournamentPermissions.CanRenewCode(t, me, VisibilityEditable))
                codeButtons.Add(Secondary(Strings.Get("TpOrgCodeRenew"), () => _ = ConfirmRenewCodeAsync(t)));
            Tile(Strings.Get("TpOrgCode"), "#CBA7F2", Strings.Get("TpOrgCodeBody"),
                Frozen(0x4D, 0xA0, 0x6E, 0xF0), codeText, codeButtons.ToArray());
        }
        if (TournamentPermissions.CanChangeVisibility(t, me, VisibilityEditable))
        {
            var segment = new SegmentRow();
            segment.SetOptions(new[]
            {
                new SegmentOption("public", Strings.Get("TpOrgVisibilityPublic")),
                new SegmentOption("private", Strings.Get("TpOrgVisibilityPrivate")),
            });
            segment.Selected = t.IsPrivate ? "private" : "public";
            segment.Picked += key => _ = ConfirmVisibilityAsync(t, (string)key);
            segment.Margin = new Thickness(0, 12, 0, 0);
            Tile(Strings.Get("TpOrgVisibility"), "#A8BCD2", Strings.Get("TpOrgVisibilityBody"), rim, segment);
        }
        if (TournamentPermissions.CanAppointManagers(t, me) || t.Managers is { Count: > 0 })
        {
            var add = TournamentPermissions.CanAppointManagers(t, me) ? new[] { Secondary(Strings.Get("TpOrgAddManager"), () => _ = AddManagerAsync(t, me)) } : Array.Empty<Button>();
            Tile(Strings.Get("TpOrgManagers"), "#A8BCD2", Strings.Get("TpOrgManagersBody"), rim, BuildManagerChips(t, me), add);
        }
        if (t.Status == "running")
            Tile(Strings.Get("TpOrgResolve"), "#A8BCD2", Strings.Get("TpOrgResolveBody"), rim, null,
                Secondary(Strings.Get("TpOrgGoBracket"), () =>
                {
                    _tournamentTabs[t.Id] = TournamentTab.Bracket;
                    RenderTournamentDetail();
                }));
        if (t.Status == "registration")
            Tile(Strings.Get("TpOrgRegistration"), "#A8BCD2", Strings.Get(t.EntryMode == "approval" ? "TpOrgRegistrationApproval" : "TpOrgRegistrationOpen"), rim, null);
        if (t.Status == "ready")
        {
            var buttons = new List<Button>();
            if (TournamentPermissions.CanOpenRegistration(t, me))
                buttons.Add(Secondary(Strings.Get("MpTournamentReopenRegistration"), () => _ = RunTournamentActionAsync(() => TournamentApi.OpenTournamentRegistrationAsync(t.Id))));
            if (TournamentPermissions.CanStart(t, me))
                buttons.Add(Secondary(Strings.Get("MpTournamentSeed"), () => _ = RunTournamentActionAsync(() => TournamentApi.SeedTournamentAsync(t.Id))));
            Tile(Strings.Get("TpOrgSeeds"), "#A8BCD2", Strings.Get("TpOrgSeedsBody"), rim, null, buttons.ToArray());
        }
        if (TournamentPermissions.CanCancel(t, me) || TournamentPermissions.CanDelete(t, me))
        {
            var danger = new List<Button>();
            if (TournamentPermissions.CanCancel(t, me))
                danger.Add(Secondary(Strings.Get("MpTournamentCancel"), () => _ = ConfirmCancelTournamentAsync(t),
                    ink: Ink("#F0A6A6"), rim: Frozen(0x99, 0xC8, 0x4A, 0x4A)));
            if (TournamentPermissions.CanDelete(t, me))
                danger.Add(Secondary(Strings.Get("TpDeleteTournament"), () => _ = ConfirmDeleteTournamentAsync(t),
                    ink: Ink("#F0A6A6"), rim: Frozen(0x99, 0xC8, 0x4A, 0x4A)));
            Tile(Strings.Get("TpOrgDanger"), "#F0A6A6",
                Strings.Get(TournamentPermissions.CanCancel(t, me) ? "TpOrgDangerBody" : "TpOrgDangerDeleteBody"),
                Frozen(0x73, 0xC8, 0x4A, 0x4A), null, danger.ToArray());
        }

        return new ScrollViewer { Padding = new Thickness(0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = tiles };
    }

    private FrameworkElement? BuildManagerChips(TournamentDetail t, string? me)
    {
        if (t.Managers is not { Count: > 0 } managers) return null;
        bool canRemove = TournamentPermissions.CanAppointManagers(t, me);
        var wrap = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        foreach (var manager in managers)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            var letter = LetterWithPhoto(Say(Initial(manager.DisplayName), "SetTinySize", "#FFFFFF", FontWeights.Bold), manager.AvatarUrl, 20, 10);
            line.Children.Add(new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = Ink("#3F6A8A"), Margin = new Thickness(0, 0, 7, 0), Child = letter });
            line.Children.Add(Say(manager.DisplayName ?? "", "SetControlSize", "#DCE7F5", FontWeights.Medium));
            if (canRemove)
            {
                var who = manager.UserId;
                var x = new Button { Content = "✕", Width = 20, Height = 20, Foreground = Ink("#8C9CB1"), Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, Tag = 10d, ToolTip = TooltipHelper.Wrap(Strings.Get("MpTournamentRemoveManager")) };
                x.SetResourceReference(StyleProperty, "TournamentChip");
                x.Click += (_, _) => _ = RunTournamentActionAsync(() => TournamentApi.RemoveTournamentManagerAsync(t.Id, who));
                line.Children.Add(x);
            }
            var chip = Pill(line, 28, 4, canRemove ? 6 : 10, Frozen(0x0F, 0xFF, 0xFF, 0xFF), Frozen(0x1F, 0xFF, 0xFF, 0xFF), 14);
            chip.Margin = new Thickness(0, 0, 6, 6);
            wrap.Children.Add(chip);
        }
        return wrap;
    }

    private async Task AddManagerAsync(TournamentDetail t, string? me)
    {
        var candidates = (t.Entrants ?? new())
            .Where(e => e.Status == "confirmed" && !string.IsNullOrEmpty(e.CaptainUserId)
                        && e.CaptainUserId != t.OwnerUserId && !AlreadyManages(t, e.CaptainUserId))
            .Select(e => new PickCandidate(e.CaptainUserId!, e.DisplayName ?? ""))
            .ToList();
        var dialog = new PlayerPickerDialog(Strings.Get("TpOrgAddManager"), Strings.Format("TpOrgAddManagerIntro", t.Name),
            Strings.Get("MpPickPlayers"), candidates, required: 1, okLabel: Strings.Get("TpOrgAddManager"));
        if (!Show(dialog)) return;
        await RunTournamentActionAsync(() => TournamentApi.AddTournamentManagerAsync(t.Id, dialog.SelectedIds[0]));
    }

    /// <summary>What the blur behind the panels has to reach: <c>MainWindow</c> clips to these.</summary>
    internal IEnumerable<(FrameworkElement Element, double Radius)> TournamentGlassElements()
    {
        yield return (SubBar, 0);
        yield return (TournamentsListPane, 14);
        if (_tournamentCallCard != null) yield return (_tournamentCallCard, 14);
        if (_tournamentContentCard != null) yield return (_tournamentContentCard, 14);
    }
}
