using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// The chassis of a dialog about one mod (designs 74-76): its image as a strip that fades into the
/// navy, the glow of its colour, its icon and tint in the title bar, and its colour on the action
/// (BASE.md). Added at construction rather than repeated in each XAML, and a no-op with no mod
/// selected, which is what a test host has.
/// </summary>
internal static class ModDialogChrome
{
    private const double StripHeight = 300;

    /// <summary>The mod the Library is showing, set by MainWindow.</summary>
    public static ModProfile? Current { get; set; }

    public static LauncherConfig? Config { get; set; }

    public static void Attach(Window window, TitleBar bar)
    {
        if (Current is not { } mod || Config is not { } config || window.Content is not Grid root) return;

        var accent = ModTheme.AccentOf(mod.AccentColor, config.UseModColor);
        var rows = System.Math.Max(1, root.RowDefinitions.Count);

        var image = config.ShowModImage ? MainWindow.TryLoadTileImage(mod.ResolveHeroSources().FirstOrDefault()) : null;
        if (image != null)
        {
            var strip = image.Clone();
            strip.AlignmentY = AlignmentY.Top;
            root.Children.Insert(0, Layer(new Border { Height = StripHeight, VerticalAlignment = VerticalAlignment.Top, Background = strip }, rows));
        }
        var fade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(0x47, 0x0F, 0x1C, 0x2E), 0));
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(0xCC, 0x0F, 0x1C, 0x2E), .6));
        fade.GradientStops.Add(new GradientStop(Color.FromRgb(0x0F, 0x1C, 0x2E), 1));
        var at = image != null ? 1 : 0;
        root.Children.Insert(at, Layer(new Rectangle { Height = StripHeight, VerticalAlignment = VerticalAlignment.Top, Fill = fade }, rows));
        // This mod's colour, not the Library's: a dialog can be about another mod than the one
        // on screen, and the install dialog usually is.
        var glow = new RadialGradientBrush
        {
            Center = new Point(.88, 0),
            GradientOrigin = new Point(.88, 0),
            RadiusX = .9,
            RadiusY = .66,
        };
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0x4D, accent.R, accent.G, accent.B), 0));
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, accent.R, accent.G, accent.B), .7));
        root.Children.Insert(at + 1, Layer(new Rectangle { Fill = glow }, rows));

        // The cards frost over the image in the prototype. Blurring live behind a dialog is what
        // the base forbids, so over an image the glass is simply denser.
        if (image != null) window.Resources["GlassPanel"] = new SolidColorBrush(Color.FromArgb(0xCC, 0x0A, 0x13, 0x21));

        bar.Background = new SolidColorBrush(GlassTheme.ModBar(accent, config.GlassLevel));
        if (MainWindow.TryLoadTileImage(mod.ResolveIconSource()) is { } icon) bar.TitleIcon = icon.ImageSource;

        // Whatever the dialog paints with the action blue takes the mod's colour here, and nowhere
        // else: a resource in the window wins over the application's.
        window.Resources["MpAction"] = new SolidColorBrush(accent);
    }

    /// <summary>A window of the whole launcher, not of one mod (designs 72, 77, 78): navy lit by
    /// the action blue, and the plain title bar.</summary>
    public static void AttachGlobal(Window window)
    {
        window.SetResourceReference(Control.BackgroundProperty, "MpAppBg");
        if (window.Content is not Grid root) return;
        var rows = System.Math.Max(1, root.RowDefinitions.Count);
        var top = new Rectangle();
        top.SetResourceReference(Shape.FillProperty, "WindowGlowTop");
        var bottom = new Rectangle();
        bottom.SetResourceReference(Shape.FillProperty, "WindowGlowBottom");
        root.Children.Insert(0, Layer(top, rows));
        root.Children.Insert(1, Layer(bottom, rows));
    }

    /// <summary>A CSS line-height: from the text's own size, so it follows the text-size setting.</summary>
    public static void SetLineHeight(double factor, params TextBlock[] texts)
    {
        foreach (var text in texts) text.LineHeight = text.FontSize * factor;
    }

    /// <summary>The small green tick of a dialog's list of facts.</summary>
    public static FrameworkElement TickMark()
    {
        var mark = new TextBlock
        {
            Text = "\u2713",
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        mark.SetResourceReference(TextBlock.ForegroundProperty, "WindowSafeTick");
        var disc = new Border
        {
            Width = 14,
            Height = 14,
            CornerRadius = new CornerRadius(7),
            Margin = new Thickness(0, 2, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Child = mark,
        };
        disc.SetResourceReference(Border.BackgroundProperty, "WindowTickFill");
        return disc;
    }

    /// <summary>A sentence from the string table with its emphasis in it: <c>*bold*</c> and
    /// <c>`mono`</c>, so a translation can move them with the words.</summary>
    public static void AddMarkedText(TextBlock target, string text)
    {
        var bold = false;
        var mono = false;
        var run = new System.Text.StringBuilder();
        void Flush()
        {
            if (run.Length == 0) return;
            var piece = new System.Windows.Documents.Run(run.ToString());
            run.Clear();
            if (mono)
            {
                piece.FontFamily = new FontFamily("Consolas, Courier New, monospace");
                piece.SetResourceReference(System.Windows.Documents.TextElement.FontSizeProperty, "SetRowDescSize");
                piece.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "UiBodyText");
            }
            if (bold)
            {
                piece.FontWeight = FontWeights.SemiBold;
                piece.Foreground = Brushes.White;
            }
            target.Inlines.Add(piece);
        }
        foreach (var c in text)
        {
            if (c == '*') { Flush(); bold = !bold; }
            else if (c == '`') { Flush(); mono = !mono; }
            else run.Append(c);
        }
        Flush();
    }

    private static FrameworkElement Layer(FrameworkElement element, int rows)
    {
        element.IsHitTestVisible = false;
        Grid.SetRowSpan(element, rows);
        return element;
    }
}
