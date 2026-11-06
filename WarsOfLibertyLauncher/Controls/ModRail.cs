using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>What a row shows while its mod is being installed or updated.</summary>
public readonly record struct RailOperation(string ModId, double Percent, bool Stopped);

/// <summary>
/// The Library's list (design 68a): favourites, the player's mods and the catalog in one column,
/// folded to icons when the window is narrow. Built in code because every row is data; the rules
/// are in <see cref="LibraryList"/> and this only draws them.
///
/// <para>Each row is a Button with an AutomationId, and the star a sibling on top of it rather than
/// a child: a button inside a button swallows the click of whichever is pressed second.</para>
/// </summary>
public sealed class ModRail : UserControl
{
    public event EventHandler<ModProfile>? Selected;
    public event EventHandler<ModProfile>? FavoriteToggled;
    public event EventHandler? FoldToggled;

    /// <summary>Supplied by the window: the menu of a row, which needs its services.</summary>
    public Func<ModProfile, ContextMenu?>? ContextMenuFor { get; set; }

    /// <summary>Supplied by the window so the list shares its image cache.</summary>
    public Func<ModProfile, Brush?>? IconFor { get; set; }

    private readonly Border _shell = new();
    private IReadOnlyList<LibraryGroup> _groups = Array.Empty<LibraryGroup>();
    private Func<ModProfile, ModRowState> _state = _ => new ModRowState();
    private Func<string, bool> _favorite = _ => false;
    private string _selected = "";
    private RailOperation? _operation;
    private bool _open = true;
    private double _rowHeight = 56, _nameSize = 13.5;
    private string _emptyTitle = "", _emptyBody = "";
    private Func<ModProfile, string?> _subtitle = _ => null;
    private ScrollViewer? _scroll;
    private Border? _fill, _track;
    private TextBlock? _percent;
    private readonly List<(Button Row, TextBlock Name)> _rows = new();

    public ModRail()
    {
        _shell.CornerRadius = new CornerRadius(14);
        _shell.BorderThickness = new Thickness(1);
        _shell.ClipToBounds = true;
        _shell.SetResourceReference(Border.BackgroundProperty, "GlassPanel");
        _shell.SetResourceReference(Border.BorderBrushProperty, "GlassRim");
        Content = _shell;
    }

    public void Show(
        IReadOnlyList<LibraryGroup> groups,
        Func<ModProfile, ModRowState> state,
        Func<string, bool> favorite,
        string selectedId,
        RailOperation? operation,
        bool open,
        LibraryMetrics metrics,
        string emptyTitle,
        string emptyBody,
        Func<ModProfile, string?> subtitle)
    {
        var offset = _scroll?.VerticalOffset ?? 0;
        _emptyTitle = emptyTitle;
        _emptyBody = emptyBody;
        _subtitle = subtitle;
        _fill = _track = null;
        _percent = null;
        _groups = groups;
        _state = state;
        _favorite = favorite;
        _selected = selectedId;
        _operation = operation;
        _open = open;
        _rowHeight = metrics.RowHeight;
        _nameSize = metrics.RowNameSize;
        _rows.Clear();
        _shell.Child = open ? BuildOpen() : BuildFolded();
        _scroll?.ScrollToVerticalOffset(offset);
    }

    /// <summary>A window being resized moves the row height on every tick: the rows are resized
    /// where they stand, because rebuilding the list that often is a layout pass per tick.</summary>
    public void ApplyMetrics(LibraryMetrics metrics)
    {
        _rowHeight = metrics.RowHeight;
        _nameSize = metrics.RowNameSize;
        foreach (var (row, name) in _rows)
        {
            row.Height = _rowHeight;
            name.FontSize = _nameSize;
        }
    }

    /// <summary>The row of the mod being downloaded, on every progress tick. Rebuilding the list
    /// that often would lose the pointer's hover and cost a layout per tick.</summary>
    public void UpdateOperation(RailOperation? operation)
    {
        var changed = operation is { } a && _operation is { } b
            ? !string.Equals(a.ModId, b.ModId, StringComparison.OrdinalIgnoreCase) || a.Stopped != b.Stopped
            : operation.HasValue != _operation.HasValue;
        _operation = operation;
        if (changed && _groups.Count > 0)
        {
            var offset = _scroll?.VerticalOffset ?? 0;
            _fill = _track = null;
            _percent = null;
            _rows.Clear();
            _shell.Child = _open ? BuildOpen() : BuildFolded();
            _scroll?.ScrollToVerticalOffset(offset);
        }
        if (operation is { } op && _fill != null && _track != null && _percent != null)
        {
            _fill.Width = _track.ActualWidth * Math.Clamp(op.Percent, 0, 100) / 100;
            _percent.Text = $"{(int)op.Percent} %";
        }
    }

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    private static double Size(string key) => (double)Application.Current.Resources[key];

    private static TextBlock Text(string text, double size, FontWeight weight, string brush)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = weight,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    private UIElement BuildOpen()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
        root.RowDefinitions.Add(new RowDefinition());

        var header = new Border { BorderThickness = new Thickness(0, 0, 0, 1) };
        header.SetResourceReference(Border.BorderBrushProperty, "GlassRim");
        var head = new DockPanel { LastChildFill = false, Margin = new Thickness(14, 0, 8, 0) };
        var fold = FoldButton("«", Strings.Get("LibRailFold"));
        DockPanel.SetDock(fold, Dock.Right);
        head.Children.Add(fold);
        head.Children.Add(Text(Strings.Get("LibRailTitle"), 11, FontWeights.SemiBold, "UiBodyText"));
        var count = Text(_groups.Sum(g => g.Mods.Count).ToString(), 11, FontWeights.Normal, "UiDimText");
        count.Margin = new Thickness(8, 0, 0, 0);
        head.Children.Add(count);
        header.Child = head;
        root.Children.Add(header);

        var list = new StackPanel { Margin = new Thickness(6) };
        foreach (var group in _groups)
        {
            list.Children.Add(GroupHeader(group));
            foreach (var mod in group.Mods) list.Children.Add(BuildRow(mod));
        }
        if (_groups.Count == 0) list.Children.Add(EmptyState());

        var scroll = _scroll = new ScrollViewer
        {
            Content = list,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
            Padding = new Thickness(0),
        };
        scroll.Resources["BgNeutral"] = Application.Current.Resources["ModAccentThumb"];
        AutomationProperties.SetAutomationId(scroll, "LibraryRailList");
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        return root;
    }

    private UIElement EmptyState()
    {
        var panel = new StackPanel { Margin = new Thickness(20, 40, 20, 40) };
        var title = Text(_emptyTitle, 14, FontWeights.SemiBold, "UiHeadText");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        var body = Text(_emptyBody, 12, FontWeights.Normal, "UiDimText");
        body.HorizontalAlignment = HorizontalAlignment.Center;
        body.TextAlignment = TextAlignment.Center;
        body.TextWrapping = TextWrapping.Wrap;
        body.Margin = new Thickness(0, 8, 0, 0);
        panel.Children.Add(title);
        panel.Children.Add(body);
        return panel;
    }

    private UIElement BuildFolded()
    {
        var list = new StackPanel { Margin = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Center };
        var unfold = FoldButton("»", Strings.Get("LibRailUnfold"));
        unfold.Width = 44;
        unfold.Height = 28;
        unfold.Margin = new Thickness(0, 4, 0, 6);
        list.Children.Add(unfold);

        var favorites = _groups.FirstOrDefault(g => g.Kind == LibraryGroupKind.Favorites);
        foreach (var group in _groups)
        {
            foreach (var mod in group.Mods) list.Children.Add(BuildTile(mod));
            if (group == favorites && _groups.Count > 1)
            {
                var divider = new Border { Width = 32, Height = 1, Margin = new Thickness(0, 4, 0, 4) };
                divider.SetResourceReference(Border.BackgroundProperty, "GlassRimStrong");
                list.Children.Add(divider);
            }
        }
        return _scroll = new ScrollViewer
        {
            Content = list,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
            Padding = new Thickness(0),
        };
    }

    private Button FoldButton(string glyph, string tip)
    {
        var button = new Button
        {
            Content = glyph,
            Width = 28,
            Height = 26,
            Padding = new Thickness(0),
            ToolTip = TooltipHelper.Wrap(tip),
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)Application.Current.Resources["LibrarySmallButton"],
        };
        AutomationProperties.SetAutomationId(button, "LibraryRailFold");
        AutomationProperties.SetName(button, tip);
        button.Click += (_, _) => FoldToggled?.Invoke(this, EventArgs.Empty);
        return button;
    }

    private static UIElement GroupHeader(LibraryGroup group)
    {
        var (label, note) = group.Kind switch
        {
            LibraryGroupKind.Favorites => (Strings.Get("LibGroupFavorites"), group.Mods.Count.ToString()),
            LibraryGroupKind.Mine => (Strings.Get("LibGroupMine"), Strings.Format("LibGroupMineNote", group.Mods.Count)),
            _ => (Strings.Get("LibGroupCatalog"), group.Mods.Count.ToString()),
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 10, 6, 6) };
        panel.Children.Add(Text(label, 11, FontWeights.SemiBold, "UiMutedText"));
        var count = Text(note, 11, FontWeights.Normal, "UiLabelText");
        count.Margin = new Thickness(8, 0, 0, 0);
        panel.Children.Add(count);
        return panel;
    }

    private (string Sub, string SubBrush, string Dot) Describe(ModProfile mod, ModRowState state)
    {
        if (_operation is { } op && string.Equals(op.ModId, mod.Id, StringComparison.OrdinalIgnoreCase))
            return op.Stopped
                ? (Strings.Format("LibRowStopped", (int)op.Percent), "UiErrorText", "UiError")
                : ("", "ModAccentText", "UiInfo");
        return state.Status switch
        {
            ModRowStatus.UpdateAvailable => (Strings.Get("LibRowUpdate"), "UiInfo", "UiInfo"),
            ModRowStatus.Installed when mod.IsStockGame => (Strings.Get("LibRowBase"), "UiDimText", "UiMutedText"),
            ModRowStatus.Installed => (_subtitle(mod) ?? Version(state), "UiDimText", "UiOk"),
            ModRowStatus.Error => (state.Note, "UiErrorText", "UiError"),
            _ => (LibraryList.IsMine(state) ? Strings.Get("LibRowNotInstalled") : mod.Author ?? "", "UiDimText", "UiLabelText"),
        };
    }

    private static string Version(ModRowState state)
    {
        var version = state.CurrentVersion ?? "";
        return version.Length == 0 || version.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? version : "v" + version;
    }

    private UIElement BuildRow(ModProfile mod)
    {
        var state = _state(mod);
        var selected = string.Equals(mod.Id, _selected, StringComparison.OrdinalIgnoreCase);
        var (sub, subBrush, dotBrush) = Describe(mod, state);

        var content = new Grid { Margin = new Thickness(12, 0, 12, 0) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        content.ColumnDefinitions.Add(new ColumnDefinition());
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });

        content.Children.Add(Icon(mod, 40));

        var words = new StackPanel { Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        var name = Text(mod.DisplayName ?? "", _nameSize, FontWeights.SemiBold, selected ? "LibraryWhite" : "UiBodyText");
        words.Children.Add(name);
        if (_operation is { Stopped: false } op && string.Equals(op.ModId, mod.Id, StringComparison.OrdinalIgnoreCase))
            words.Children.Add(RowProgress(op.Percent));
        else if (sub.Length > 0)
        {
            var line = Text(sub, 11.5, FontWeights.Normal, subBrush);
            line.Margin = new Thickness(0, 5, 0, 0);
            words.Children.Add(line);
        }
        Grid.SetColumn(words, 1);
        content.Children.Add(words);

        var button = new Button
        {
            Content = content,
            Height = _rowHeight,
            Style = (Style)Application.Current.Resources[selected ? "LibraryRowSelected" : "LibraryRow"],
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            ContextMenu = ContextMenuFor?.Invoke(mod),
        };
        AutomationProperties.SetAutomationId(button, "ModRow_" + mod.Id);
        AutomationProperties.SetName(button, mod.DisplayName ?? mod.Id);
        button.Click += (_, _) => Selected?.Invoke(this, mod);
        _rows.Add((button, name));

        var host = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        host.Children.Add(button);

        var trailing = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        var favorite = _favorite(mod.Id);
        var star = new Button
        {
            Content = favorite ? "★" : "☆",
            Width = 22,
            Height = 22,
            Style = (Style)Application.Current.Resources[favorite ? "LibraryStarOn" : "LibraryStar"],
            ToolTip = TooltipHelper.Wrap(Strings.Get(favorite ? "LibFavoriteRemove" : "LibFavoriteAdd")),
        };
        AutomationProperties.SetAutomationId(star, "ModStar_" + mod.Id);
        AutomationProperties.SetName(star, Strings.Get(favorite ? "LibFavoriteRemove" : "LibFavoriteAdd"));
        star.Click += (_, _) => FavoriteToggled?.Invoke(this, mod);
        trailing.Children.Add(star);
        var dot = new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(3.5), Margin = new Thickness(8, 0, 0, 0) };
        dot.SetResourceReference(Border.BackgroundProperty, dotBrush);
        trailing.Children.Add(dot);
        host.Children.Add(trailing);
        return host;
    }

    private UIElement RowProgress(double percent)
    {
        var line = new Grid { Margin = new Thickness(0, 7, 0, 0) };
        line.ColumnDefinitions.Add(new ColumnDefinition());
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var track = new Border { Height = 4, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center };
        track.SetResourceReference(Border.BackgroundProperty, "LibraryTrack");
        var fill = new Border { Height = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left };
        fill.SetResourceReference(Border.BackgroundProperty, "ModAccent");
        track.SizeChanged += (_, e) => fill.Width = e.NewSize.Width * Math.Clamp(_operation?.Percent ?? percent, 0, 100) / 100;
        _track = track;
        _fill = fill;
        track.Child = fill;
        line.Children.Add(track);

        var label = Text($"{(int)percent} %", 11, FontWeights.SemiBold, "ModAccentText");
        label.FontFamily = (FontFamily)Application.Current.Resources["MonoFont"];
        label.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(label, 1);
        line.Children.Add(label);
        _percent = label;
        return line;
    }

    private UIElement BuildTile(ModProfile mod)
    {
        var state = _state(mod);
        var selected = string.Equals(mod.Id, _selected, StringComparison.OrdinalIgnoreCase);
        var (_, _, dotBrush) = Describe(mod, state);

        var content = new Grid { Width = 56, Height = 56 };
        var icon = Icon(mod, 36);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        content.Children.Add(icon);
        var dot = new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 6, 6, 0),
        };
        dot.SetResourceReference(Border.BackgroundProperty, dotBrush);
        content.Children.Add(dot);
        if (_favorite(mod.Id))
        {
            var star = Text("★", 12, FontWeights.Normal, "UiFavorite");
            star.HorizontalAlignment = HorizontalAlignment.Left;
            star.VerticalAlignment = VerticalAlignment.Top;
            star.Margin = new Thickness(4, 3, 0, 0);
            content.Children.Add(star);
        }

        if (_operation is { Stopped: false } op && string.Equals(op.ModId, mod.Id, StringComparison.OrdinalIgnoreCase))
        {
            var bar = new ProgressBar
            {
                Style = (Style)Application.Current.Resources["LibraryProgressBar"],
                Width = 36,
                Height = 3,
                Value = op.Percent,
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4),
            };
            content.Children.Add(bar);
        }

        var button = new Button
        {
            Content = content,
            Width = 56,
            Height = 56,
            Margin = new Thickness(0, 0, 0, 4),
            Style = (Style)Application.Current.Resources[selected ? "LibraryRowSelected" : "LibraryRow"],
            ToolTip = TooltipHelper.Wrap(mod.DisplayName ?? ""),
            ContextMenu = ContextMenuFor?.Invoke(mod),
        };
        AutomationProperties.SetAutomationId(button, "ModRow_" + mod.Id);
        AutomationProperties.SetName(button, mod.DisplayName ?? mod.Id);
        button.Click += (_, _) => Selected?.Invoke(this, mod);
        return button;
    }

    private FrameworkElement Icon(ModProfile mod, double size)
    {
        var brush = IconFor?.Invoke(mod);
        if (brush != null)
            return new Border { Width = size, Height = size, Background = brush };

        var disc = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2) };
        disc.SetResourceReference(Border.BackgroundProperty, "UiButtonFill");
        var letter = Text((mod.DisplayName ?? "?").Substring(0, 1).ToUpperInvariant(), size / 2.4, FontWeights.SemiBold, "UiBodyText");
        letter.HorizontalAlignment = HorizontalAlignment.Center;
        disc.Child = letter;
        return disc;
    }
}
