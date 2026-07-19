using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Builds tooltip content that WRAPS long text to multiple lines instead of
/// clipping to a single line.
///
/// A plain string assigned to <c>FrameworkElement.ToolTip</c> is rendered by the
/// ToolTip template's <see cref="ContentPresenter"/>, whose auto-generated
/// TextBlock defaults to <c>TextWrapping=NoWrap</c> — and WPF has no way to force
/// wrapping on that TextBlock from the template (<c>TextBlock.TextWrapping</c>
/// isn't an attached property). So long string tooltips got cut off (the reported
/// gear-menu bug, and the same risk for the Settings / Mod Properties / Workshop
/// tooltips). Wrapping the text in our OWN <see cref="TextBlock"/> with
/// <c>TextWrapping=Wrap</c> + a bounded <c>MaxWidth</c> fixes it reliably; the
/// app-wide ToolTip style (App.xaml) still themes + pads the surrounding box.
/// </summary>
internal static class TooltipHelper
{
    /// <summary>Default wrap width for tooltip text (kept below the ToolTip
    /// template's MaxWidth so the box never has to clip).</summary>
    public const double DefaultMaxWidth = 340;

    /// <summary>Wrap a string into a multi-line tooltip content element. Returns
    /// the string unchanged when null/empty so an empty tooltip stays empty.
    ///
    /// The TextBlock also pins a LOCAL <c>FontFamily</c>/<c>FontSize</c> (the normal
    /// UI font). This is load-bearing: a ToolTip INHERITS its font from the control
    /// it's attached to, and the title-bar caption buttons use an ICON font
    /// ("Segoe MDL2 Assets", no letters) at a tiny glyph size — so a raw-string
    /// tooltip on them rendered its words as missing-glyph boxes ("tofu"). A local
    /// value on this TextBlock beats that inheritance, so any tooltip routed through
    /// Wrap is font-safe regardless of its owner. (TryFindResource so it's a no-op
    /// if resources aren't loaded, e.g. off the UI thread.)</summary>
    public static object? Wrap(string? text, double maxWidth = DefaultMaxWidth)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var tb = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = maxWidth,
        };
        if (Application.Current?.TryFindResource("BodyFont") is FontFamily font)
            tb.FontFamily = font;
        if (Application.Current?.TryFindResource("FontSizeBody") is double size)
            tb.FontSize = size;
        return tb;
    }
}
