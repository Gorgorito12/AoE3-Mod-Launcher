using System;
using System.Windows;
using System.Windows.Media;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Window-size UI scaler — generalises the dashboard hero's "shrink to fit
/// the window" transform to any view or window.
///
/// A <see cref="ScaleTransform"/> on a FOREGROUND content root is driven by
/// the live content footprint over a FIXED reference, clamped to
/// <c>[<see cref="MinScale"/>, 1.0]</c>. The 1.0 ceiling means the UI never
/// grows past its design size: the default window (and anything larger /
/// maximized) renders at the crispest 1.0 ClearType path, and only windows
/// SMALLER than the reference shrink down to the readability floor. Each
/// caller passes its OWN default content footprint as the reference, so a
/// window at its default size lands at 1.0 — zero regression versus the
/// pre-scaler build; the scaler only adds "shrink to fit" for smaller windows.
///
/// Text crispness follows the hero's recipe: glyphs rasterised for the
/// pre-transform pixel grid look blurry once a transform shrinks them, so the
/// scaled subtree flips to Ideal/Grayscale/Animated under a non-1.0 transform
/// and restores the app-wide Display/ClearType/Fixed trio at exactly 1.0.
///
/// IMPORTANT (see CLAUDE.md): attach ONLY to a foreground content root, and
/// drive it from a STABLE <c>sizeSource</c> that the transform does NOT feed
/// back into (the element's parent / the window, never the scaled element
/// itself — a <see cref="LayoutTransform"/> changes the element's own
/// ActualWidth and would oscillate). Never scale a full-bleed background layer
/// (it must keep filling the window) or an alert-overlay host whose scrim must
/// cover at base coordinates. Use <see cref="Kind.Render"/> only for the
/// bottom-pinned hero (it must not reflow over its full-bleed background);
/// everything else uses <see cref="Kind.Layout"/> so scaled content still
/// fills the window and feeds the enclosing ScrollViewer correctly.
/// </summary>
public static class UiScale
{
    /// <summary>Readability floor — the same 0.82 the hero used.</summary>
    public const double MinScale = 0.82;

    public enum Kind
    {
        /// <summary>LayoutTransform — reflows + fills the slot; default.</summary>
        Layout,
        /// <summary>RenderTransform — visual-only, no reflow; the hero only.</summary>
        Render,
    }

    /// <summary>
    /// Last computed MainWindow content factor. Popups live in their own
    /// top-level visual tree (out of reach of a content-root transform), so
    /// they read this to match the shell. 1.0 until the first layout pass.
    /// </summary>
    public static double Current { get; private set; } = 1.0;

    private static double Clamp(double w, double h, double refW, double refH)
    {
        if (w <= 0 || h <= 0 || refW <= 0 || refH <= 0) return 1.0;
        double scale = Math.Min(w / refW, h / refH);
        scale = Math.Min(scale, 1.0);
        scale = Math.Max(scale, MinScale);
        return scale;
    }

    /// <summary>
    /// Install a window-size <see cref="ScaleTransform"/> on <paramref name="scaled"/>,
    /// driven by <paramref name="sizeSource"/>'s footprint over
    /// <paramref name="refWidth"/> x <paramref name="refHeight"/>.
    /// <paramref name="sizeSource"/> MUST be a container the transform does not
    /// resize (typically the scaled element's parent / the window).
    /// </summary>
    public static void Attach(FrameworkElement scaled, FrameworkElement sizeSource,
        double refWidth, double refHeight, Kind kind = Kind.Layout,
        Point origin = default, bool publishCurrent = false)
    {
        if (scaled == null || sizeSource == null) return;

        var transform = new ScaleTransform(1.0, 1.0);
        if (kind == Kind.Render)
        {
            scaled.RenderTransformOrigin = origin;
            scaled.RenderTransform = transform;
        }
        else
        {
            scaled.LayoutTransform = transform;
        }

        void Update()
        {
            double scale = Clamp(sizeSource.ActualWidth, sizeSource.ActualHeight, refWidth, refHeight);
            // Guard the 0-size case (tab collapsed / not yet laid out): Clamp
            // returns 1.0, but we only want to skip — leave the last good value.
            if (sizeSource.ActualWidth <= 0 || sizeSource.ActualHeight <= 0) return;
            transform.ScaleX = scale;
            transform.ScaleY = scale;
            SetTextCrispForScale(scaled, scale);
            if (publishCurrent) Current = scale;
        }

        sizeSource.SizeChanged += (_, _) => Update();
        sizeSource.Loaded += (_, _) => Update();
        if (sizeSource.IsLoaded) Update();
    }

    /// <summary>
    /// Compute + publish <see cref="Current"/> from <paramref name="sizeSource"/>
    /// WITHOUT transforming anything — used on the MainWindow content host so
    /// popups have a stable factor regardless of which tab is active.
    /// </summary>
    public static void Track(FrameworkElement sizeSource, double refWidth, double refHeight)
    {
        if (sizeSource == null) return;
        void Update()
        {
            if (sizeSource.ActualWidth <= 0 || sizeSource.ActualHeight <= 0) return;
            Current = Clamp(sizeSource.ActualWidth, sizeSource.ActualHeight, refWidth, refHeight);
        }
        sizeSource.SizeChanged += (_, _) => Update();
        sizeSource.Loaded += (_, _) => Update();
        if (sizeSource.IsLoaded) Update();
    }

    /// <summary>
    /// Flip a subtree's text-rendering mode for crispness under a transform:
    /// Ideal/Grayscale/Animated when scaled below 1.0, Display/ClearType/Fixed
    /// at 1.0. Inherited TextOptions cascade to the whole subtree.
    /// </summary>
    public static void SetTextCrispForScale(DependencyObject subtree, double scale)
    {
        if (subtree == null) return;
        bool scaledDown = scale < 0.999;
        TextOptions.SetTextFormattingMode(subtree,
            scaledDown ? TextFormattingMode.Ideal : TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(subtree,
            scaledDown ? TextRenderingMode.Grayscale : TextRenderingMode.ClearType);
        TextOptions.SetTextHintingMode(subtree,
            scaledDown ? TextHintingMode.Animated : TextHintingMode.Fixed);
    }
}
