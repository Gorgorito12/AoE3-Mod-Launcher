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

        var image = config.ShowModImage ? MainWindow.TryLoadTileImage(mod.ResolveHeroSources().FirstOrDefault(), MainWindow.StripDecodeWidth) : null;
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
        TintPrimary(window, accent);
    }

    /// <summary>
    /// The chassis of a creation window (88, 81): the same pieces as <see cref="Attach"/> with the
    /// numbers those two designs draw, for a mod picked INSIDE the window. So it can run again: it
    /// takes down what the previous mod put up.
    /// </summary>
    internal static Color AttachPicked(Window window, TitleBar bar, Grid root, ModProfile mod, double width)
    {
        foreach (var old in root.Children.OfType<FrameworkElement>().Where(c => ReferenceEquals(c.Tag, PickedLayer)).ToList())
            root.Children.Remove(old);

        var accent = Config is { } settings
            ? ModTheme.AccentOf(mod.AccentColor, settings.UseModColor)
            : Color.FromRgb(0x2F, 0x7F, 0xE0);
        var rows = System.Math.Max(1, root.RowDefinitions.Count);
        var at = 0;
        void Add(FrameworkElement layer)
        {
            layer.Tag = PickedLayer;
            root.Children.Insert(at++, Layer(layer, rows));
        }

        var image = Config?.ShowModImage == true
            ? MainWindow.TryLoadTileImage(mod.ResolveHeroSources().FirstOrDefault(), MainWindow.StripDecodeWidth)
            : null;
        if (image != null)
        {
            var strip = image.Clone();
            strip.Stretch = Stretch.Fill;
            if (strip.ImageSource is System.Windows.Media.Imaging.BitmapSource { PixelWidth: > 0 } source)
                strip.Viewbox = HeroCover.Viewbox(source.PixelWidth, source.PixelHeight, width, StripHeight, .3);
            Add(new Border { Height = StripHeight, VerticalAlignment = VerticalAlignment.Top, Background = strip });

            // The frost of the cards, as the Library does it: the image blurred once and shown only
            // where a card lies (FrostCards). A live blur behind a dialog is what the base forbids.
            if (GlassTheme.Normalize(Config?.GlassLevel) != GlassTheme.Solid
                && MainWindow.BuildBlurredCover(image.ImageSource) is { } blurred)
            {
                blurred.Viewbox = strip.Viewbox;
                var frost = new Border { Height = StripHeight, VerticalAlignment = VerticalAlignment.Top, Background = blurred, Clip = Geometry.Empty };
                frost.Name = FrostName;
                Add(frost);
            }
        }
        var fade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(0x59, 0x0F, 0x1C, 0x2E), 0));
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(0xC7, 0x0F, 0x1C, 0x2E), .55));
        fade.GradientStops.Add(new GradientStop(Color.FromRgb(0x0F, 0x1C, 0x2E), 1));
        Add(new Rectangle { Height = StripHeight + 2, VerticalAlignment = VerticalAlignment.Top, Fill = fade });

        var glow = new RadialGradientBrush { Center = new Point(.8, 0), GradientOrigin = new Point(.8, 0), RadiusX = .6, RadiusY = .6 };
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0x42, accent.R, accent.G, accent.B), 0));
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, accent.R, accent.G, accent.B), .72));
        Add(new Rectangle { Height = 360, Margin = new Thickness(0, 220, 0, 0), VerticalAlignment = VerticalAlignment.Top, Fill = glow });

        if (Config is { } config) bar.Background = new SolidColorBrush(GlassTheme.ModBar(accent, config.GlassLevel));
        if (MainWindow.TryLoadTileImage(mod.ResolveIconSource()) is { } icon) bar.TitleIcon = icon.ImageSource;
        TintPrimary(window, accent);
        return accent;
    }

    /// <summary>The primary button, its hover and its halo, which all follow the mod.</summary>
    private static void TintPrimary(Window window, Color accent)
    {
        window.Resources["MpAction"] = new SolidColorBrush(accent);
        window.Resources["WindowPrimaryHover"] = new SolidColorBrush(ModAccent.Mix(accent, Colors.White, .12));
        window.Resources["WindowPrimaryHalo"] = accent;
    }

    private static readonly object PickedLayer = new();
    private const string FrostName = "CardFrost";

    /// <summary>Shows the blurred image under these cards and nowhere else. Cards and layer are
    /// siblings' descendants, so the rectangles go through their common ancestor.</summary>
    internal static void FrostCards(Grid root, FrameworkElement[] cards)
    {
        var frost = root.Children.OfType<Border>().FirstOrDefault(c => c.Name == FrostName);
        if (frost == null) return;
        var clip = new GeometryGroup();
        foreach (var card in cards.Where(c => c.IsVisible && c.ActualHeight > 0))
        {
            var rect = card.TransformToVisual(root).TransformBounds(new Rect(card.RenderSize));
            clip.Children.Add(new RectangleGeometry(rect, 12, 12));
        }
        clip.Freeze();
        frost.Clip = clip;
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

    /// <summary>
    /// A small dialog about one mod (103c, 103i, 103j, 103l): no image, only its tint on the bar
    /// and its colour on the action, for a mod that can be another than the one the Library shows.
    /// Returns the colour, or null with no settings to read, which leaves the dialog global.
    /// </summary>
    internal static Color? AttachTint(Window window, TitleBar bar, ModProfile mod)
    {
        if (Config is not { } config) return null;
        var accent = ModTheme.AccentOf(mod.AccentColor, config.UseModColor);
        bar.Background = new SolidColorBrush(GlassTheme.ModBar(accent, config.GlassLevel));
        if (MainWindow.TryLoadTileImage(mod.ResolveIconSource()) is { } icon) bar.TitleIcon = icon.ImageSource;
        TintPrimary(window, accent);
        return accent;
    }

    /// <summary>A CSS line-height: from the text's own size, so it follows the text-size setting.</summary>
    public static void SetLineHeight(double factor, params TextBlock[] texts)
    {
        foreach (var text in texts) text.LineHeight = text.FontSize * factor;
    }

    /// <summary>The same for a line tighter than the font's own, as in <c>font: 11px/1</c>, which
    /// the default stacking ignores. Applied once the text is in the tree: until then a size that
    /// comes from a resource still reads as the default.</summary>
    public static void SetTightLine(double factor, params TextBlock[] texts)
    {
        foreach (var text in texts)
        {
            text.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            void Apply(object sender, RoutedEventArgs e)
            {
                text.Loaded -= Apply;
                text.LineHeight = text.FontSize * factor;
            }
            if (text.IsLoaded) Apply(text, new RoutedEventArgs());
            else text.Loaded += Apply;
        }
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
