using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>What a small dialog is about: the tile beside its headline says it before the text.</summary>
public enum DialogTone
{
    /// <summary>No tile: not a warning, like a progress window or a question with a field.</summary>
    None,
    Info,
    Caution,
    /// <summary>It cannot be undone: the action turns red and Enter stops meaning yes.</summary>
    Destructive,
    Done,
}

/// <summary>
/// The one chassis of every small dialog (design 103): a 40-px bar, a tone tile beside a serif
/// headline, the text, whatever the dialog holds, and a footer with Cancel beside the one primary
/// button. Built here so a dialog states what it says rather than how it looks.
///
/// <para><b>What goes between the tags in XAML lands in <see cref="Body"/>, not in
/// <c>Window.Content</c></b>, which this class has already filled: the attribute below is what
/// lets a derived window keep its own x:Name elements.</para>
///
/// <para><b>Escape and the ✕ are Cancel, Enter is the primary button</b>, except on a destructive
/// dialog, where Enter must not carry out what cannot be undone. A dismissal leaves
/// <c>DialogResult</c> null or false, never true.</para>
/// </summary>
[ContentProperty(nameof(Body))]
public class BaseDialog : Window
{
    private const string LauncherName = "AoE3 Mod Launcher";

    private readonly ContentControl _slot = new() { Focusable = false, IsTabStop = false };
    private readonly Border _toneTile = new() { Width = 32, Height = 32, CornerRadius = new CornerRadius(9) };
    private readonly Border _detailBox = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 14, 0, 0) };
    private readonly TextBlock _detailLabel = new();
    private readonly TextBlock _detailValue = new();
    private readonly Grid _root = new();
    private DialogTone _tone;
    private Color _glowColor = Color.FromRgb(0x2F, 0x7F, 0xE0);
    private double _glowTop = .2;

    internal TitleBar Chrome { get; } = new() { ShowMinimize = false, ShowMaximize = false, ShowClose = true };
    internal TextBlock ToneGlyph { get; } = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        FontWeight = FontWeights.Bold,
        FontSize = 15,
    };
    internal TextBlock HeadlineText { get; } = new() { Visibility = Visibility.Collapsed };
    internal TextBlock BodyText { get; } = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
    internal TextBlock FootNote { get; } = new()
    {
        Visibility = Visibility.Collapsed,
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 10, 0),
    };
    internal Button LeftAction { get; } = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 10, 0) };
    internal Button CancelAction { get; } = new() { IsCancel = true };
    internal Button PrimaryAction { get; } = new() { IsDefault = true, Margin = new Thickness(8, 0, 0, 0) };

    public BaseDialog()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Width = 440;

        Chrome.TitleIcon = new System.Windows.Media.Imaging.BitmapImage(
            new Uri("pack://application:,,,/Aoe3ModLauncher;component/AppIcon.png"));

        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.Children.Add(Chrome);

        var page = new StackPanel { Margin = new Thickness(22, 22, 22, 18) };
        Grid.SetRow(page, 1);
        _root.Children.Add(page);

        page.Children.Add(BuildHeader());
        page.Children.Add(BuildDetailBox());
        page.Children.Add(_slot);
        page.Children.Add(BuildFooter());

        Content = _root;
        ModDialogChrome.AttachGlobal(this);
        SizeChanged += (_, _) => PaintGlow();

        Tone = DialogTone.None;
        PreviewKeyDown += OnPreviewKey;
        LeftAction.Click += (_, _) => OnLeft();
        CancelAction.Click += (_, _) => OnCancel();
        PrimaryAction.Click += (_, _) => OnPrimary();
        SetLineHeights();
    }

    /// <summary>The dialog's own content: a field, a list, a progress bar.</summary>
    public object? Body
    {
        get => _slot.Content;
        set
        {
            _slot.Content = value;
            _slot.Margin = value == null ? default : new Thickness(0, 14, 0, 0);
        }
    }

    /// <summary>The tile, and the rule that a red action does not answer to Enter.</summary>
    public DialogTone Tone
    {
        get => _tone;
        set
        {
            _tone = value;
            if (value == DialogTone.None)
            {
                _toneTile.Visibility = Visibility.Collapsed;
            }
            else
            {
                var key = "DialogTone" + value;
                _toneTile.Visibility = Visibility.Visible;
                _toneTile.SetResourceReference(Border.BackgroundProperty, key + "Fill");
                _toneTile.SetResourceReference(Border.BorderBrushProperty, key + "Ring");
                ToneGlyph.SetResourceReference(TextBlock.ForegroundProperty, key + "Glyph");
                ToneGlyph.Text = value switch
                {
                    DialogTone.Info => "i",
                    DialogTone.Done => "✓",
                    _ => "!",
                };
            }
            var destructive = value == DialogTone.Destructive;
            PrimaryAction.SetResourceReference(StyleProperty,
                destructive ? "WindowDangerButton" : "WindowPrimaryButton");
            PrimaryAction.MinWidth = 124;
            PrimaryAction.IsDefault = !destructive;
            CancelAction.IsDefault = destructive;
        }
    }

    /// <summary>The window's name, on the bar and in the task list. Empty reads as the launcher.</summary>
    internal void SetBarTitle(string? title)
    {
        var text = string.IsNullOrWhiteSpace(title) ? LauncherName : title;
        Title = text;
        Chrome.Title = text;
    }

    internal void SetHeadline(string? text)
    {
        HeadlineText.Text = text ?? "";
        HeadlineText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        BodyText.Margin = new Thickness(0, HeadlineText.Visibility == Visibility.Visible ? 8 : 0, 0, 0);
    }

    internal void SetLead(string? text)
    {
        BodyText.Text = text ?? "";
        BodyText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Technical detail in Consolas under the text: an error, a path. Empty hides the box.</summary>
    internal void SetDetail(string? label, string? value)
    {
        var show = !string.IsNullOrWhiteSpace(value);
        _detailBox.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        _detailValue.Text = value ?? "";
        _detailLabel.Text = label ?? "";
        _detailLabel.Visibility = string.IsNullOrEmpty(label) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>A third action, to the left of the pair and apart from it.</summary>
    internal void SetLeftAction(string label)
    {
        LeftAction.Content = label;
        LeftAction.Visibility = Visibility.Visible;
    }

    internal void HideCancel() => CancelAction.Visibility = Visibility.Collapsed;

    internal void HidePrimary()
    {
        PrimaryAction.Visibility = Visibility.Collapsed;
        PrimaryAction.IsDefault = false;
    }

    /// <summary>A dialog about one mod: its tint on the bar, its glow, its colour on the action.
    /// A destructive action stays red. No mod, or none known, leaves the dialog global.</summary>
    internal void UseMod(ModProfile? mod)
    {
        if (mod == null) return;
        var accent = ModDialogChrome.AttachTint(this, Chrome, mod);
        if (accent is not { } color) return;
        _glowColor = color;
        _glowTop = .22;
        PaintGlow();
        if (Tone == DialogTone.Destructive) return;
        PrimaryAction.Foreground = new SolidColorBrush(ModAccent.OnAccent(color));
    }

    /// <summary>In pixels, as the design states them: a brush relative to the window would reach
    /// less far in a small one, which is every window here.</summary>
    private void PaintGlow()
    {
        if (_root.Children.Count < 2 || _root.Children[0] is not Shape top || _root.Children[1] is not Shape bottom) return;
        top.Fill = Glow(new Point(ActualWidth * .88, 0), 520, 340, _glowTop);
        bottom.Fill = Glow(new Point(0, ActualHeight), 420, 300, .08);
    }

    private RadialGradientBrush Glow(Point centre, double rx, double ry, double alpha)
    {
        var c = _glowColor;
        var glow = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            Center = centre,
            GradientOrigin = centre,
            RadiusX = rx,
            RadiusY = ry,
        };
        glow.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(alpha * 255), c.R, c.G, c.B), 0));
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), .7));
        glow.Freeze();
        return glow;
    }

    /// <summary>Modal over <paramref name="owner"/>; with no usable owner, centred on the screen.</summary>
    internal bool? ShowOver(Window? owner)
    {
        if (owner is { IsLoaded: true, IsVisible: true } && !ReferenceEquals(owner, this)) Owner = owner;
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return ShowDialog();
    }

    protected virtual void OnPrimary() => Answer(true);

    protected virtual void OnCancel() => Answer(false);

    protected virtual void OnLeft() { }

    /// <summary>Closes with this answer. A window not shown modally has no DialogResult to set.</summary>
    protected void Answer(bool? result)
    {
        try { DialogResult = result; }
        catch (InvalidOperationException) { }
        Close();
    }

    private void OnPreviewKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        OnCancel();
    }

    private UIElement BuildHeader()
    {
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _toneTile.BorderThickness = new Thickness(1);
        _toneTile.VerticalAlignment = VerticalAlignment.Top;
        _toneTile.Margin = new Thickness(0, 0, 14, 0);
        _toneTile.Child = ToneGlyph;
        header.Children.Add(_toneTile);

        var words = new StackPanel();
        Grid.SetColumn(words, 1);
        HeadlineText.SetResourceReference(StyleProperty, "DialogHeadline");
        BodyText.SetResourceReference(StyleProperty, "WindowBody");
        words.Children.Add(HeadlineText);
        words.Children.Add(BodyText);
        header.Children.Add(words);
        return header;
    }

    private UIElement BuildDetailBox()
    {
        _detailBox.SetResourceReference(StyleProperty, "DialogDetailBox");
        _detailLabel.SetResourceReference(StyleProperty, "WindowFieldLabel");
        _detailLabel.Margin = new Thickness(0, 0, 0, 5);
        _detailValue.SetResourceReference(StyleProperty, "WindowPath");
        var stack = new StackPanel();
        stack.Children.Add(_detailLabel);
        stack.Children.Add(_detailValue);
        _detailBox.Child = stack;
        return _detailBox;
    }

    private UIElement BuildFooter()
    {
        var footer = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        LeftAction.SetResourceReference(StyleProperty, "WindowSecondaryButton");
        LeftAction.MinWidth = 112;
        footer.Children.Add(LeftAction);

        FootNote.SetResourceReference(TextBlock.ForegroundProperty, "WindowFineText");
        FootNote.SetResourceReference(TextBlock.FontSizeProperty, "SetControlSize");
        Grid.SetColumn(FootNote, 1);
        footer.Children.Add(FootNote);

        CancelAction.SetResourceReference(StyleProperty, "WindowSecondaryButton");
        CancelAction.MinWidth = 112;
        var pair = new StackPanel { Orientation = Orientation.Horizontal };
        Grid.SetColumn(pair, 2);
        pair.Children.Add(CancelAction);
        pair.Children.Add(PrimaryAction);
        footer.Children.Add(pair);
        return footer;
    }

    /// <summary>CSS line-heights, from the sizes the text-size setting already applied.</summary>
    private void SetLineHeights()
    {
        static double Size(string key, double fallback)
            => Application.Current?.TryFindResource(key) is double value ? value : fallback;

        HeadlineText.LineHeight = Size("DialogSubheadSize", 19) * 1.3;
        BodyText.LineHeight = Size("DialogBodySize", 13) * 1.55;
        _detailValue.LineHeight = Size("SetRowDescSize", 12.5) * 1.45;
    }
}
