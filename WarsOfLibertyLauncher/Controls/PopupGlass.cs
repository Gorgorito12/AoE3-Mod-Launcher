using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// What gives a menu its glass over the cover. A popup is a window of its own and WPF cannot blur
/// what lies behind one, so the design's .95 / .74 / .6 are not reachable by making it transparent:
/// the text behind reads through the menu's. Instead the menu is painted with the piece of the
/// already blurred cover that lies under it, then the SAME layers a panel has over it (the side
/// shade, the two gradients, the mod's wash), then the panel's tint, then a denser layer: the same
/// glass as the list it opens over, only more opaque. With the blurred cover and the tint alone it
/// read lighter and warmer than the panel (reported): the layers between were missing.
///
/// <para>It reuses the bitmap the panels use, so opening a menu decodes nothing. With no cover to
/// show (another tab, another window, images off, reduced effects, the Solid level, or a menu that
/// hangs outside the window) the menu keeps the dense fill of <see cref="GlassTheme.FloatingKeys"/>.</para>
/// </summary>
internal static class PopupGlass
{
    /// <summary>The blurred cover, the element it is laid over and the level's tint, when a cover
    /// is on screen in the window of <c>target</c>. Set by the main window; null means no glass.</summary>
    internal static Func<UIElement?, (ImageBrush Blur, FrameworkElement Layer, Brush[] Overlays, Color Tint)?>? Source { get; set; }

    /// <summary>What a menu adds over the panel's tint: the same navy, so the hue does not move,
    /// at an alpha that takes Media's .5 panel to about the design's .74.</summary>
    internal static readonly Color Denser = Color.FromArgb(89, 10, 19, 33);

    private static bool s_registered;

    /// <summary>Every ContextMenu, from one place: a bare one is built all over the launcher.</summary>
    internal static void Register()
    {
        if (s_registered) return;
        s_registered = true;
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is ContextMenu menu) Apply(menu, menu.PlacementTarget);
            }));
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.ClosedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is ContextMenu menu) Restore(menu);
            }));
    }

    /// <summary>For a hand-built popup; <see cref="ChromePopups.Track"/> calls it.</summary>
    internal static void Watch(Popup popup)
    {
        popup.Opened += (_, _) =>
        {
            if (popup.Child is { } child) Apply(child, popup.PlacementTarget);
        };
        popup.Closed += (_, _) =>
        {
            if (popup.Child is { } child) Restore(child);
        };
    }

    /// <summary>The part of the cover under <paramref name="surface"/>. <paramref name="cover"/> is
    /// the viewbox that lays the image over the whole of <paramref name="layer"/>, in fractions of
    /// the image, and <paramref name="surface"/> is in the layer's own units.</summary>
    internal static Rect ViewboxFor(Rect cover, Size layer, Rect surface) => new(
        cover.X + surface.X / layer.Width * cover.Width,
        cover.Y + surface.Y / layer.Height * cover.Height,
        surface.Width / layer.Width * cover.Width,
        surface.Height / layer.Height * cover.Height);

    /// <summary>Past the edge of the layer there is no cover to paint, and a menu half glass and
    /// half tint reads as a fault. Half a pixel of tolerance: a window edge is not a whole DIP.</summary>
    internal static bool LiesWithin(Size layer, Rect surface)
        => surface.Width > 0 && surface.Height > 0
           && surface.Left >= -.5 && surface.Top >= -.5
           && surface.Right <= layer.Width + .5 && surface.Bottom <= layer.Height + .5;

    private static void Apply(DependencyObject root, UIElement? target)
    {
        try
        {
            if (Source?.Invoke(target) is not { } source) return;
            if (FindSurface(root) is not { } found) return;
            if (PresentationSource.FromVisual(found.Surface) == null
                || PresentationSource.FromVisual(source.Layer) == null) return;

            var topLeft = source.Layer.PointFromScreen(found.Surface.PointToScreen(new Point(0, 0)));
            var bottomRight = source.Layer.PointFromScreen(found.Surface.PointToScreen(
                new Point(found.Surface.ActualWidth, found.Surface.ActualHeight)));
            var rect = new Rect(topLeft, bottomRight);
            if (!LiesWithin(source.Layer.RenderSize, rect)) return;

            found.Surface.Background = Compose(source.Blur, source.Overlays, source.Layer.RenderSize, rect, source.Tint,
                ViewboxFor(source.Blur.Viewbox, source.Layer.RenderSize, rect));
            found.Surface.Tag = new Painted(found.Key, found.Surface.Tag);
        }
        catch (Exception ex)
        {
            // Glass is a look: a menu must open whatever happens here.
            DiagnosticLog.Write($"Menu glass failed: {ex.Message}");
        }
    }

    private static void Restore(DependencyObject root)
    {
        if (FindPainted(root) is not { } surface) return;
        var painted = (Painted)surface.Tag;
        surface.Tag = painted.PreviousTag;
        surface.SetResourceReference(Border.BackgroundProperty, painted.Key);
    }

    /// <summary>The layers, bottom to top, with no image: what a test can check without a window.
    /// Each overlay is sampled at the menu's own place in the layer.</summary>
    internal static DrawingGroup Layers(Brush? piece, Brush[] overlays, Size layer, Rect surface, Color tint)
    {
        var box = new RectangleGeometry(new Rect(0, 0, 1, 1));
        var layers = new DrawingGroup();
        if (piece != null) layers.Children.Add(new GeometryDrawing(piece, null, box));
        foreach (var overlay in overlays)
        {
            var whole = new DrawingBrush(new GeometryDrawing(overlay.IsFrozen ? overlay : overlay.CloneCurrentValue(), null, new RectangleGeometry(new Rect(layer))))
            {
                Stretch = Stretch.Fill,
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = surface,
            };
            layers.Children.Add(new GeometryDrawing(whole, null, box));
        }
        layers.Children.Add(new GeometryDrawing(new SolidColorBrush(tint), null, box));
        layers.Children.Add(new GeometryDrawing(new SolidColorBrush(Denser), null, box));
        return layers;
    }

    private static Brush Compose(ImageBrush blur, Brush[] overlays, Size layer, Rect surface, Color tint, Rect viewbox)
    {
        var piece = new ImageBrush(blur.ImageSource)
        {
            Stretch = Stretch.Fill,
            ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
            Viewbox = viewbox,
        };
        var layers = Layers(piece, overlays, layer, surface, tint);
        var brush = new DrawingBrush(layers) { Stretch = Stretch.Fill };
        brush.Freeze();
        return brush;
    }

    /// <summary>The element wearing one of the floating fills: the shadow and the rim around it
    /// differ from one menu to another, and which it is does not matter here.</summary>
    private static (Border Surface, string Key)? FindSurface(DependencyObject root)
    {
        if (root is Border { Background: { } fill } border)
            foreach (var key in GlassTheme.FloatingKeys)
                if (ReferenceEquals(fill, Application.Current.TryFindResource(key)))
                    return (border, key);
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindSurface(VisualTreeHelper.GetChild(root, i)) is { } found)
                return found;
        return null;
    }

    private static Border? FindPainted(DependencyObject root)
    {
        if (root is Border { Tag: Painted } border) return border;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindPainted(VisualTreeHelper.GetChild(root, i)) is { } found)
                return found;
        return null;
    }

    private sealed record Painted(string Key, object? PreviousTag);
}
