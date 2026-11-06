using System.Windows;
using System.Windows.Media;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Paints the selected mod's colour into <c>Application.Resources</c>, so everything that belongs to
/// that mod follows it through <c>{DynamicResource}</c>: selection, primary button, active filter,
/// progress, active tab. Menus and panels of the launcher itself never read these brushes
/// (BASE.md: "el color del mod solo va en lo que pertenece al mod").
/// </summary>
public static class ModTheme
{
    private static readonly Color LauncherBlue = Color.FromRgb(0x2F, 0x7F, 0xE0);

    public static void Apply(string? accentHex, bool useModColor)
    {
        var resources = Application.Current?.Resources;
        if (resources == null) return;

        var accent = AccentOf(accentHex, useModColor);

        Set(resources, "ModAccent", accent);
        Set(resources, "ModAccentText", ModAccent.TextOn(accent));
        Set(resources, "ModOnAccent", ModAccent.OnAccent(accent));
        Set(resources, "ModAccentFill", ModAccent.SelectionFill(accent));
        Set(resources, "ModAccentSoft", WithAlpha(accent, .16));
        Set(resources, "ModAccentLine", WithAlpha(accent, .55));
        Set(resources, "ModAccentRing", WithAlpha(accent, .6));
        Set(resources, "ModAccentGlow", WithAlpha(accent, .35));
        Set(resources, "ModAccentThumb", WithAlpha(accent, .5));
        resources["ModAccentGlowColor"] = accent;

        var wash = new RadialGradientBrush
        {
            Center = new Point(1, 0),
            GradientOrigin = new Point(1, 0),
            RadiusX = .7,
            RadiusY = .7,
        };
        wash.GradientStops.Add(new GradientStop(WithAlpha(accent, .28), 0));
        wash.GradientStops.Add(new GradientStop(WithAlpha(accent, 0), 1));
        wash.Freeze();
        resources["ModAccentWash"] = wash;

        var bar = new LinearGradientBrush { StartPoint = new Point(0, .5), EndPoint = new Point(1, .5) };
        bar.GradientStops.Add(new GradientStop(WithAlpha(accent, .26), 0));
        bar.GradientStops.Add(new GradientStop(Color.FromArgb(0xB8, 8, 15, 27), .45));
        bar.Freeze();
        resources["ModTitleBar"] = bar;
    }

    public static Color AccentOf(string? accentHex, bool useModColor)
        => useModColor && ModAccent.TryParse(accentHex, out var parsed) ? ModAccent.Normalize(parsed) : LauncherBlue;

    /// <summary>A mod without an image: its colour, lit from the top right.</summary>
    public static Brush HeroGradient(Color accent)
    {
        var brush = new RadialGradientBrush
        {
            Center = new Point(.85, 0),
            GradientOrigin = new Point(.85, 0),
            RadiusX = 1.2,
            RadiusY = 1.4,
        };
        brush.GradientStops.Add(new GradientStop(ModAccent.Mix(accent, Colors.White, .18), 0));
        brush.GradientStops.Add(new GradientStop(accent, .32));
        brush.GradientStops.Add(new GradientStop(ModAccent.Mix(accent, ModAccent.Navy, .72), 1));
        brush.Freeze();
        return brush;
    }

    /// <summary>Images off or effects reduced: only a glow of the colour on navy.</summary>
    public static Brush QuietBackground(Color accent)
    {
        var brush = new RadialGradientBrush
        {
            Center = new Point(.85, .1),
            GradientOrigin = new Point(.85, .1),
            RadiusX = .7,
            RadiusY = .9,
        };
        brush.GradientStops.Add(new GradientStop(WithAlpha(accent, .45), 0));
        brush.GradientStops.Add(new GradientStop(ModAccent.Navy, .7));
        brush.GradientStops.Add(new GradientStop(ModAccent.Navy, 1));
        brush.Freeze();
        return brush;
    }

    private static void Set(ResourceDictionary resources, string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        resources[key] = brush;
    }

    private static Color WithAlpha(Color color, double alpha)
        => Color.FromArgb((byte)System.Math.Round(alpha * 255), color.R, color.G, color.B);
}
