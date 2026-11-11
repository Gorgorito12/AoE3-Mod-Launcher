using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    private readonly Dictionary<string, TournamentTab> _tournamentTabs = new(StringComparer.Ordinal);

    private TournamentDetail? _pageTournament;
    private Color _pageAccent = ModAccent.Navy;
    private readonly Dictionary<string, Border> _bracketCards = new(StringComparer.Ordinal);
    private ScrollViewer? _bracketScroll;
    private ScaleTransform? _bracketScale;
    private TranslateTransform? _bracketHeadShift;
    private FrameworkElement? _bracketHost, _bracketHeads, _bracketSheetHost;
    private Grid? _bracketPick;
    private StackPanel? _peopleBody;
    private FrameworkElement? _bracketEmptyView, _peopleView, _organiseView, _tournamentContentCard, _tournamentCallCard;
    private FrameworkElement? _tournamentStepsRow, _tournamentCallSub, _bracketTools;
    private TextBlock? _tournamentTitle, _tournamentSearchCount, _zoomText;
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
        TournamentsTitleCount.Text = ((_tournaments?.Drafts?.Count ?? 0) + (_tournaments?.Tournaments?.Count ?? 0)).ToString();
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
            line.Children.Add(Say(Strings.Get(caption), "SetControlSize", on ? "#FFFFFF" : "#A8BCD2", on ? FontWeights.SemiBold : FontWeights.Medium));
            line.Children.Add(Say((key == "all" ? all.Count : all.Count(t => t.Status == key)).ToString(), "SetCaptionSize",
                on ? "#C3D2E5" : "#8C9CB1", FontWeights.SemiBold, new Thickness(6, 0, 0, 0), mono: true));
            var chip = new Button
            {
                Content = line,
                Height = 30,
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(0, 0, 6, 6),
                Background = on ? Ink("#233A5C") : Frozen(0x80, 0x0A, 0x13, 0x21),
                BorderBrush = on ? Brushes.Transparent : Frozen(0x1A, 0xFF, 0xFF, 0xFF),
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

    private static Brush StatusDot(string? status) => Ink(status switch
    {
        "registration" or "ready" => "#55ACEE",
        "running" => "#4FD68A",
        "cancelled" => "#C84A4A",
        _ => "#8C9CB1",
    });

    private static string StatusInk(string? status) => status switch
    {
        "registration" or "ready" => "#9FD2F6",
        "running" => "#8FE0B0",
        "cancelled" => "#F0A6A6",
        _ => "#C3D2E5",
    };

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
        _mineBox = null;

        var t = _tournamentDetail;
        _pageTournament = t;
        if (t == null)
        {
            _setTournamentBackdrop?.Invoke(null);
            if (!_tournamentsUnavailable) panel.Children.Add(Hint(Strings.Get("MpTournamentsPickOne")));
            return;
        }

        var me = TournamentViewerId(t);
        _pageAccent = AccentOfTournament(t);
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
            line.Children.Add(Say(ResolveModDisplayName(mod.Id), "SetControlSize", "#DCE7F5", FontWeights.SemiBold));
            var chip = Pill(line, 26, 5, 10, chipFill, chipRim, 13);
            chip.Margin = new Thickness(0, 0, 8, 6);
            chips.Children.Add(chip);
        }
        var state = new StackPanel { Orientation = Orientation.Horizontal };
        state.Children.Add(Dot(7, StatusDot(t.Status)));
        state.Children.Add(Say(StatusLabel(t.Status, false), "SetControlSize", StatusInk(t.Status), FontWeights.SemiBold));
        var stateChip = Pill(state, 26, 11, 11, chipFill, chipRim, 13);
        stateChip.Margin = new Thickness(0, 0, 8, 6);
        chips.Children.Add(stateChip);
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

        var stepsRow = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        foreach (var step in TournamentPage.Steps(t.Status))
        {
            var (glyph, ink) = step.State switch
            {
                TournamentStepState.Done => ("✓", "#C3D2E5"),
                TournamentStepState.Now => ("●", ToHex(ModAccent.TextOn(_pageAccent))),
                _ => ("○", "#8C9CB1"),
            };
            var pill = Pill(Say(glyph + " " + step.Text, "SetCaptionSize", ink, FontWeights.SemiBold), 24, 10, 10,
                Frozen(step.State == TournamentStepState.Now ? (byte)0xB3 : (byte)0x73, 0x0A, 0x13, 0x21),
                step.State == TournamentStepState.Now ? Tint(_pageAccent, .7) : Frozen(0x1A, 0xFF, 0xFF, 0xFF), 12);
            pill.Margin = new Thickness(0, 0, 6, 0);
            stepsRow.Children.Add(pill);
        }
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

    private FrameworkElement? BuildCallCard(TournamentDetail t, string? me)
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
                words.Children.Add(Say(call.Kicker, "SetCaptionSize", call.Primary == TournamentPrimary.CloseRegistration ? "#9FD2F6" : ToHex(ModAccent.TextOn(_pageAccent)), FontWeights.Bold));
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
            buttons.Children.Add(primary);
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
            BorderBrush = call.Champion ? Tint(ModAccent.TextOn(_pageAccent), .85) : Frozen(0x1F, 0xFF, 0xFF, 0xFF),
            Child = grid,
        };
        if (!call.Champion)
        {
            card.SetResourceReference(Border.BackgroundProperty, "GlassPanel");
            return card;
        }
        card.Background = ChampionFill(.64, 1.2);
        return WithGlow(card, 14, 36, .4);
    }

    /// <summary>The design's <c>radial-gradient(...), mix(...)</c>, in one brush.</summary>
    private Brush ChampionFill(double navy, double reach)
    {
        var below = ModAccent.Mix(_pageAccent, ModAccent.Navy, navy);
        var fill = new RadialGradientBrush { Center = new Point(0, 0), GradientOrigin = new Point(0, 0), RadiusX = reach, RadiusY = reach };
        fill.GradientStops.Add(new GradientStop(ModAccent.Mix(below, _pageAccent, .45), 0));
        fill.GradientStops.Add(new GradientStop(below, .65));
        fill.Freeze();
        return fill;
    }

    /// <summary>A glow as a sibling under the card: an Effect on the card would take ClearType from
    /// every word on it.</summary>
    private FrameworkElement WithGlow(FrameworkElement card, double radius, double blur, double strength)
    {
        // A slow machine, or a player who asked for fewer effects, gets the card alone.
        if (RankBadge.ReducedEffects) return card;
        var host = new Grid { VerticalAlignment = card.VerticalAlignment };
        host.Children.Add(new Border
        {
            Background = Solid(_pageAccent),
            CornerRadius = new CornerRadius(radius),
            Margin = new Thickness(2),
            IsHitTestVisible = false,
            CacheMode = new BitmapCache(),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = _pageAccent, BlurRadius = blur, ShadowDepth = 0, Opacity = strength },
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

    private UIElement BuildVersus(TournamentDetail t, string? me, TournamentMatch m)
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
        words.Children.Add(Say(Strings.Get("TpChampion"), "SetCaptionSize", ToHex(ModAccent.TextOn(_pageAccent)), FontWeights.Bold));
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
        var fill = new LinearGradientBrush { StartPoint = new Point(.15, 0), EndPoint = new Point(.85, 1) };
        var text = ModAccent.TextOn(_pageAccent);
        fill.GradientStops.Add(new GradientStop(ModAccent.Mix(text, Colors.White, .3), 0));
        fill.GradientStops.Add(new GradientStop(_pageAccent, .6));
        fill.GradientStops.Add(new GradientStop(ModAccent.Mix(_pageAccent, Colors.Black, .55), 1));
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
            Foreground = Brushes.White,
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
                Background = on ? Ink("#233A5C") : Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
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
            if (now) words.Children.Add(Dot(6, Solid(ModAccent.TextOn(_pageAccent)), 6));
            words.Children.Add(Say(TournamentPage.RoundName(t, round), "SetDescSize", now ? "#FFFFFF" : "#C3D2E5", FontWeights.SemiBold));
            var anchor = new Button
            {
                Content = words,
                Height = 30,
                Padding = new Thickness(11, 0, 11, 0),
                Margin = new Thickness(0, 0, 6, 0),
                Background = Frozen(0x8C, 0x0A, 0x13, 0x21),
                BorderBrush = now ? Tint(_pageAccent, .7) : Frozen(0x1F, 0xFF, 0xFF, 0xFF),
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
            button.Click += (_, _) => SetBracketZoom(_bracketZoom + delta);
            return button;
        }
        _zoomText = Say(_bracketZoom + " %", "SetDescSize", "#C3D2E5", FontWeights.SemiBold, mono: true);
        _zoomText.Width = 46;
        _zoomText.TextAlignment = TextAlignment.Center;
        zoom.Children.Add(Step("−", "TpZoomOut", -20));
        zoom.Children.Add(_zoomText);
        zoom.Children.Add(Step("+", "TpZoomIn", 20));
        tools.Children.Add(new Border { Height = 32, CornerRadius = new CornerRadius(7), Background = Ink("#1A2C48"), BorderBrush = SecondaryRim, BorderThickness = new Thickness(1), Child = zoom });
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

        var card = new Border { CornerRadius = new CornerRadius(14), BorderBrush = PanelRim, BorderThickness = new Thickness(1), Child = host };
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
        var line = Frozen(0x29, 0xFF, 0xFF, 0xFF);
        foreach (var l in sheet.Lines) canvas.Children.Add(Bar(l, line));

        var mine = (t.Matches ?? new()).FirstOrDefault(m => MatchCards.For(m, me, t.Entrants)
            is MatchCardState.Playable or MatchCardState.JoinRoom or MatchCardState.ReturnToRoom);
        var path = mine != null ? TournamentPage.PathFrom(t, mine) : TournamentPage.ChampionPath(t);
        var bright = Tint(ModAccent.TextOn(_pageAccent), .85);
        foreach (var l in path) canvas.Children.Add(Bar(l, bright));

        _bracketCards.Clear();
        foreach (var box in sheet.Cards)
        {
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
            canvas.Children.Add(Bar(new BracketLine(right, cy - .5, sheet.Gutter, 2), bright));
            var trophy = BuildChampionCard(t, final.Match);
            Canvas.SetLeft(trophy, right + sheet.Gutter);
            Canvas.SetTop(trophy, Math.Max(0, cy - 82));
            canvas.Children.Add(trophy);
            width += sheet.Gutter + 200;
            canvas.Width = width;
            canvas.Height = Math.Max(sheet.Height, Math.Max(0, cy - 82) + 170);
        }

        _bracketScale = new ScaleTransform(_bracketZoom / 100, _bracketZoom / 100);
        var scaled = new Border { Child = canvas, LayoutTransform = _bracketScale, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
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

        _bracketHeadShift = new TranslateTransform();
        var heads = BuildBracketHeads(t, sheet, width);
        heads.RenderTransform = _bracketHeadShift;
        _bracketHeads = heads;
        var strip = new Border
        {
            ClipToBounds = true,
            Padding = new Thickness(0, 12, 0, 9),
            Background = Frozen(0xE0, 0x0C, 0x16, 0x26),
            BorderBrush = Frozen(0x12, 0xFF, 0xFF, 0xFF),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = heads,
        };
        scroll.ScrollChanged += (_, e) => { if (e.HorizontalChange != 0 && _bracketHeadShift != null) _bracketHeadShift.X = -scroll.HorizontalOffset; };

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
        var legend = BuildBracketLegend();
        Grid.SetRow(legend, 1);
        host.Children.Add(legend);
        return host;
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
            var name = Say(TournamentPage.RoundName(t, round.Key).ToUpperInvariant(), "SetCaptionSize", now ? ToHex(ModAccent.TextOn(_pageAccent)) : "#C3D2E5", FontWeights.Bold, new Thickness((i == 0 ? sheet.Pad : 0) * z, 0, 7, 0));
            cell.Children.Add(name);
            var tally = Say(z < .7 ? count.ToString() : Strings.Format(count == 1 ? "TpMatchesOne" : "TpMatchesMany", count), "SetCaptionSize", "#8C9CB1",
                margin: new Thickness(0, 0, 10, 0), mono: true);
            tally.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(tally, 1);
            cell.Children.Add(tally);
            row.Children.Add(cell);
        }
        if (!string.IsNullOrEmpty(t.WinnerEntrantId))
            row.Children.Add(Say(Strings.Get("TpChampion"), "SetCaptionSize", ToHex(ModAccent.TextOn(_pageAccent)), FontWeights.Bold));
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
        var label = Say(Strings.Get("TpChampion"), "SetCaptionSize", ToHex(ModAccent.TextOn(_pageAccent)), FontWeights.Bold, new Thickness(0, 8, 0, 0));
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
            Background = ChampionFill(.62, 1),
            BorderBrush = Tint(ModAccent.TextOn(_pageAccent), .85),
            BorderThickness = new Thickness(1.5),
            Child = stack,
        }, 14, 38, .45);
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
        if (MatchCards.For(m, me, t.Entrants) is MatchCardState.InProgress or MatchCardState.SuperviseRoom)
        {
            body.Children.Add(new Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = Ink("#4FD68A"),
                Stroke = Frozen(0x38, 0x4F, 0xD6, 0x8A),
                StrokeThickness = 0,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 7, 7, 0),
                ToolTip = TooltipHelper.Wrap(Strings.Get("TpStateLive")),
            });
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

    private void PaintCardRim(TournamentDetail t, TournamentMatch m, Border card, bool hit)
    {
        var me = TournamentViewerId(t);
        var state = MatchCards.For(m, me, t.Entrants);
        bool mine = state is MatchCardState.Playable or MatchCardState.JoinRoom or MatchCardState.ReturnToRoom;
        bool picked = string.Equals(m.Id, _selectedMatchId, StringComparison.Ordinal);
        bool live = state is MatchCardState.InProgress or MatchCardState.SuperviseRoom;
        var text = ModAccent.TextOn(_pageAccent);
        card.Background = mine ? Solid(ModAccent.SelectionFill(_pageAccent))
            : m.Status == "bye" ? Frozen(0x8C, 0x12, 0x21, 0x3A) : Ink("#12213A");
        card.BorderThickness = new Thickness(picked || hit ? 2 : 1);
        card.BorderBrush = picked ? Solid(text)
            : hit ? Brushes.White
            : mine ? Tint(_pageAccent, .65)
            : live ? Frozen(0x99, 0x4F, 0xD6, 0x8A)
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
        FrameworkElement? mark = marker switch
        {
            SideMarker.One => Say("1", "SetControlSize", "#8FE0B0", FontWeights.SemiBold, new Thickness(8, 0, 0, 0), mono: true),
            SideMarker.Zero => Say("0", "SetControlSize", "#61779A", FontWeights.SemiBold, new Thickness(8, 0, 0, 0), mono: true),
            SideMarker.ByeTag => BracketTag(Strings.Get("TpTagBye"), "#C3D2E5"),
            SideMarker.WalkoverTag => Say("✓", "SetControlSize", "#8FE0B0", FontWeights.SemiBold, new Thickness(8, 0, 0, 0)),
            SideMarker.DqTag => BracketTag("DQ", "#F0A6A6"),
            _ => null,
        };
        // A walkover names who did not come: the loser's side carries it, with no figure.
        if (lost && m.Outcome == "walkover") mark = BracketTag(Strings.Get("TpTagAbsent"), "#F5BD76");
        if (mark != null)
        {
            Grid.SetColumn(mark, 3);
            grid.Children.Add(mark);
        }
        return grid;
    }

    private static Border BracketTag(string text, string ink) => new()
    {
        Height = 18,
        Margin = new Thickness(8, 0, 0, 0),
        Padding = new Thickness(7, 0, 7, 0),
        CornerRadius = new CornerRadius(9),
        Background = Frozen(0x14, 0xFF, 0xFF, 0xFF),
        BorderBrush = Frozen(0x24, 0xFF, 0xFF, 0xFF),
        BorderThickness = new Thickness(1),
        Child = Say(text, "SetTinySize", ink, FontWeights.Bold),
    };

    private FrameworkElement BuildBracketLegend()
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
        line.Children.Add(Item(Dot(7, Ink("#4FD68A"), 6), Say(Strings.Get("TpLegendLive"), "SetDescSize", "#A8BCD2")));
        line.Children.Add(Item(Say("1", "SetCaptionSize", "#8FE0B0", FontWeights.SemiBold, mono: true), Say("/", "SetDescSize", "#A8BCD2", margin: new Thickness(5, 0, 5, 0)),
            Say("0", "SetCaptionSize", "#61779A", FontWeights.SemiBold, mono: true), Say(Strings.Get("TpLegendResult"), "SetDescSize", "#A8BCD2", margin: new Thickness(6, 0, 0, 0))));
        line.Children.Add(Say(Strings.Get("TpLegendTags"), "SetDescSize", "#A8BCD2"));
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
        var state = MatchCards.For(m, me, t.Entrants, _demoTournaments && TournamentPermissions.IsOwnerOrManager(t, me));
        bool bye = state == MatchCardState.Bye;
        bool decided = m.Status == "done" && !string.IsNullOrEmpty(m.WinnerEntrantId);
        stack.Children.Add(BuildPickSide(t, m, m.Entrant1Id, 1, me, team, decided));
        if (!bye) stack.Children.Add(BuildPickSide(t, m, m.Entrant2Id, 2, me, team, decided));

        var winner = TournamentPage.NameOf(t, m.WinnerEntrantId);
        var loser = m.WinnerEntrantId == m.Entrant1Id ? TournamentPage.NameOf(t, m.Entrant2Id) : TournamentPage.NameOf(t, m.Entrant1Id);
        var accent = ToHex(ModAccent.TextOn(_pageAccent));
        var (text, ink, dot) = state switch
        {
            MatchCardState.Bye => (Strings.Format("TpStateBye", TournamentPage.NameOf(t, m.Entrant1Id ?? m.Entrant2Id)), "#C3D2E5", "#8C9CB1"),
            MatchCardState.InProgress or MatchCardState.SuperviseRoom => (Strings.Get("TpStateLive"), "#8FE0B0", "#4FD68A"),
            MatchCardState.Playable or MatchCardState.ReturnToRoom => (Strings.Get("TpStateMine"), accent, accent),
            MatchCardState.JoinRoom => (Strings.Get("TpStateJoin"), accent, accent),
            MatchCardState.Done when m.Outcome == "walkover" => (Strings.Format("TpStateWalkover", winner, loser), "#DCE7F5", "#8FE0B0"),
            MatchCardState.Done when m.Outcome == "dq" => (Strings.Format("TpStateDq", winner, loser), "#DCE7F5", "#8FE0B0"),
            MatchCardState.Done => (Strings.Format("TpStateWon", winner), "#DCE7F5", "#8FE0B0"),
            _ when string.IsNullOrEmpty(m.Entrant1Id) || string.IsNullOrEmpty(m.Entrant2Id) => (Strings.Get("TpStateWaiting"), "#C3D2E5", "#8C9CB1"),
            _ => (Strings.Get("TpStateToPlay"), "#C3D2E5", "#8C9CB1"),
        };
        var stateLine = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        stateLine.Children.Add(Dot(7, Ink(dot), 8));
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

        string res = "", resInk = "#61779A";
        if (m.Status == "bye") { res = Strings.Get("TpTagBye"); resInk = "#C3D2E5"; }
        else if (decided && known)
        {
            (res, resInk) = m.Outcome switch
            {
                "walkover" => won ? ("✓", "#8FE0B0") : (Strings.Get("TpTagAbsent"), "#F5BD76"),
                "dq" => won ? ("DQ", "#F0A6A6") : ("", "#61779A"),
                _ => won ? ("1", "#8FE0B0") : ("0", "#61779A"),
            };
        }
        if (res.Length > 0)
        {
            var mark = Say(res, "SetControlSize", resInk, FontWeights.Bold, new Thickness(8, 0, 0, 0), mono: true);
            Grid.SetColumn(mark, 2);
            grid.Children.Add(mark);
        }

        return new Border
        {
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1),
            Background = isMe ? Solid(ModAccent.SelectionFill(_pageAccent)) : won ? Frozen(0x14, 0x4F, 0xD6, 0x8A) : !known ? Frozen(0x59, 0x0A, 0x13, 0x21) : Frozen(0x73, 0x0A, 0x13, 0x21),
            BorderBrush = isMe ? Tint(_pageAccent, .65) : won ? Frozen(0x59, 0x4F, 0xD6, 0x8A) : Frozen(0x14, 0xFF, 0xFF, 0xFF),
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
    }

    private void GoToMyMatch()
    {
        if (_mineBox == null || _bracketScroll == null) return;
        double z = _bracketZoom / 100;
        _bracketScroll.ScrollToHorizontalOffset(Math.Max(0, _mineBox.X * z - 60));
        _bracketScroll.ScrollToVerticalOffset(Math.Max(0, _mineBox.Y * z - 80));
    }

    /// <summary>The zoom is one transform: the cards stay where they are and nothing is rebuilt but
    /// the strip of rounds, whose widths follow it.</summary>
    private void SetBracketZoom(double value)
    {
        _bracketZoom = Math.Clamp(value, 60, 100);
        if (_zoomText != null) _zoomText.Text = _bracketZoom + " %";
        if (_bracketScale != null)
        {
            _bracketScale.ScaleX = _bracketZoom / 100;
            _bracketScale.ScaleY = _bracketZoom / 100;
        }
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
        var (text, ink, dot) = fate != null
            ? (fate.Text, fate.Tone switch { "ok" => "#8FE0B0", "caution" => "#F5BD76", "danger" => "#F0A6A6", "plain" => "#DCE7F5", _ => "#A8BCD2" },
               fate.Tone switch { "ok" => "#4FD68A", "caution" => "#F4900C", "danger" => "#C84A4A", "plain" => "#C3D2E5", _ => "#61779A" })
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
            Background = isMine ? Solid(ModAccent.SelectionFill(_pageAccent)) : pending ? Frozen(0x14, 0xF4, 0x90, 0x0C) : isOut ? Frozen(0x47, 0x0A, 0x13, 0x21) : Frozen(0x73, 0x0A, 0x13, 0x21),
            BorderBrush = isMine ? Tint(_pageAccent, .65) : pending ? Frozen(0x59, 0xF4, 0x90, 0x0C) : Frozen(0x14, 0xFF, 0xFF, 0xFF),
            Child = grid,
        };
    }

    private FrameworkElement? BuildEntrantActions(TournamentDetail t, TournamentEntrant e, string? me, bool needsTeam)
    {
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
        if (TournamentPermissions.CanAwardOrDisqualify(t, me) && e.Status is "confirmed" or "waitlist")
        {
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
