using System;
using System.Linq;
using System.Windows.Media;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// What the launcher does to a catalog <c>accentColor</c> before painting with it. Authors pick
/// wildly different intensities, so the design base keeps the hue and brings every colour into one
/// band: OKLCH lightness 0.56 to 0.68 and chroma 0.11 to 0.16. Same arithmetic as the prototype's
/// <c>normalize</c> (Biblioteca unificada, 68b).
/// </summary>
public static class ModAccent
{
    private const double MinLightness = .56, MaxLightness = .68;
    private const double MinChroma = .11, MaxChroma = .16;

    /// <summary>The launcher's navy, which every "mix" below is measured against.</summary>
    public static readonly Color Navy = Color.FromRgb(0x0F, 0x1C, 0x2E);

    public static bool TryParse(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var text = hex.Trim().TrimStart('#');
        if (text.Length != 6 || !int.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var v))
            return false;
        color = Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    public static Color Normalize(Color color)
    {
        var (l0, a0, b0) = ToOklab(color);
        var hue = Math.Atan2(b0, a0);
        var lightness = Math.Clamp(l0, MinLightness, MaxLightness);
        var chroma = Math.Clamp(Math.Sqrt(a0 * a0 + b0 * b0), MinChroma, MaxChroma);

        // A hue may not reach the band's chroma at this lightness; step down until it is in gamut.
        (double R, double G, double B) rgb = default;
        for (var i = 0; i < 30; i++)
        {
            rgb = FromOklab(lightness, chroma * Math.Cos(hue), chroma * Math.Sin(hue));
            if (InGamut(rgb.R) && InGamut(rgb.G) && InGamut(rgb.B)) break;
            chroma -= .005;
        }
        return Color.FromRgb(ToByte(rgb.R), ToByte(rgb.G), ToByte(rgb.B));
    }

    /// <summary>The selected row's fill: an opaque mix, so the tone is the same over the image and
    /// over the plain background.</summary>
    public static Color SelectionFill(Color accent) => Mix(accent, Navy, .58);

    /// <summary><paramref name="a"/> moved <paramref name="amount"/> of the way to <paramref name="b"/>.</summary>
    public static Color Mix(Color a, Color b, double amount)
    {
        byte Channel(byte x, byte y) => (byte)Math.Round(x + (y - x) * amount);
        return Color.FromRgb(Channel(a.R, b.R), Channel(a.G, b.G), Channel(a.B, b.B));
    }

    /// <summary>Text in the mod's colour: lightened until it reaches 5:1 against the navy.</summary>
    public static Color TextOn(Color accent)
    {
        var amount = .3;
        var color = Mix(accent, Colors.White, amount);
        while (Contrast(color, Navy) < 5 && amount < .9)
        {
            amount += .08;
            color = Mix(accent, Colors.White, amount);
        }
        return color;
    }

    /// <summary>White or navy, whichever reads better on the accent.</summary>
    public static Color OnAccent(Color accent)
        => Contrast(accent, Colors.White) >= Contrast(accent, Navy) ? Colors.White : Navy;

    public static double Contrast(Color a, Color b)
    {
        var x = Luminance(a);
        var y = Luminance(b);
        return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
    }

    private static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            var s = v / 255d;
            return s <= .03928 ? s / 12.92 : Math.Pow((s + .055) / 1.055, 2.4);
        }
        return .2126 * Channel(c.R) + .7152 * Channel(c.G) + .0722 * Channel(c.B);
    }

    private static bool InGamut(double v) => v is >= -.001 and <= 1.001;

    private static byte ToByte(double linear)
    {
        var v = Math.Clamp(linear, 0, 1);
        var gamma = v <= .0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - .055;
        return (byte)Math.Round(gamma * 255);
    }

    private static (double L, double A, double B) ToOklab(Color c)
    {
        static double Linear(byte b)
        {
            var v = b / 255d;
            return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4);
        }
        double r = Linear(c.R), g = Linear(c.G), b = Linear(c.B);
        var l = Math.Cbrt(.4122214708 * r + .5363325363 * g + .0514459929 * b);
        var m = Math.Cbrt(.2119034982 * r + .6806995451 * g + .1073969566 * b);
        var s = Math.Cbrt(.0883024619 * r + .2817188376 * g + .6299787005 * b);
        return (.2104542553 * l + .793617785 * m - .0040720468 * s,
                1.9779984951 * l - 2.428592205 * m + .4505937099 * s,
                .0259040371 * l + .7827717662 * m - .808675766 * s);
    }

    private static (double R, double G, double B) FromOklab(double lightness, double a, double b)
    {
        var l = Math.Pow(lightness + .3963377774 * a + .2158037573 * b, 3);
        var m = Math.Pow(lightness - .1055613458 * a - .0638541728 * b, 3);
        var s = Math.Pow(lightness - .0894841775 * a - 1.291485548 * b, 3);
        return (4.0767416621 * l - 3.3077115913 * m + .2309699292 * s,
                -1.2684380046 * l + 2.6097574011 * m - .3413193965 * s,
                -.0041960863 * l - .7034186147 * m + 1.707614701 * s);
    }
}
