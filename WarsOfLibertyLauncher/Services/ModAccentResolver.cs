using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// The colour of a mod whose author declared none. It used to be one fixed blue (#3a8cd9) for all
/// of them, so the second such mod looked exactly like the first. In order:
/// <list type="number">
/// <item>Declared: the modder's own <c>accentColor</c>, never replaced.</item>
/// <item>From the art: the dominant colour of the mod's icon, once the launcher has the image.</item>
/// <item>Fallback: a hue of its own, spread round the wheel by the golden angle in id order, so
/// two such mods do not coincide; the same on every PC.</item>
/// </list>
/// Everything goes through <see cref="ModAccent.Normalize"/>, the band every other colour is in.
/// </summary>
public static class ModAccentResolver
{
    /// <summary>Where the wheel starts: the launcher's own blue family, so a single undeclared mod
    /// still reads as at home.</summary>
    private const double FirstHue = 205;
    private const double GoldenAngle = 137.50776;

    /// <summary>A hue for the <paramref name="index"/>-th undeclared mod (in id order).</summary>
    public static Color Fallback(int index)
        => ModAccent.Normalize(FromHue((FirstHue + index * GoldenAngle) % 360));

    /// <summary>Two colours closer than this read as the same mod's.</summary>
    public const double MinHueApart = 25;

    /// <summary>The dominant colour of an image, or null when it has none worth taking.</summary>
    public static Color? FromPixels(byte[] bgra, int width, int height, int stride)
        => Candidates(bgra, width, height, stride).Cast<Color?>().FirstOrDefault();

    /// <summary>
    /// Up to three colours an image offers, strongest first, from peaks at least 40 degrees apart
    /// and each at least 15 % as heavy as the strongest. AoE3 art is full of gold, so the first is
    /// usually gold; the second is often the one that sets the mod apart (teal for Age of
    /// Dynasties, olive for Hundred Days, red for King's Return, measured on the catalog's icons).
    /// Empty when the image has no colour worth taking (black and white, grey, nearly empty).
    /// Pixels are Bgra32; transparent, grey, near-black and near-white pixels are left out, the rest
    /// are grouped into 36 hue buckets weighted by saturation, and a peak is a bucket with its two
    /// neighbours.
    /// </summary>
    public static IReadOnlyList<Color> Candidates(byte[] bgra, int width, int height, int stride)
    {
        var weight = new double[36];
        var r = new double[36];
        var g = new double[36];
        var b = new double[36];
        int opaque = 0, chromatic = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var at = y * stride + x * 4;
                if (at + 3 >= bgra.Length) continue;
                if (bgra[at + 3] < 128) continue;
                opaque++;
                double bl = bgra[at] / 255d, gr = bgra[at + 1] / 255d, rd = bgra[at + 2] / 255d;
                double max = Math.Max(rd, Math.Max(gr, bl)), min = Math.Min(rd, Math.Min(gr, bl));
                double light = (max + min) / 2, delta = max - min;
                if (delta < .15 || light < .12 || light > .92) continue;
                double sat = delta / (1 - Math.Abs(2 * light - 1));
                double hue = rd == max ? (gr - bl) / delta % 6 : gr == max ? (bl - rd) / delta + 2 : (rd - gr) / delta + 4;
                hue = (hue * 60 + 360) % 360;
                var bucket = (int)(hue / 10) % 36;
                weight[bucket] += sat;
                r[bucket] += rd * sat;
                g[bucket] += gr * sat;
                b[bucket] += bl * sat;
                chromatic++;
            }
        }
        // Less than a tenth of the picture in colour is a black-and-white picture with a stray
        // pixel, not a colour.
        if (opaque == 0 || chromatic < Math.Max(8, opaque / 10)) return Array.Empty<Color>();

        double Peak(int i) => weight[(i + 35) % 36] + weight[i] + weight[(i + 1) % 36];
        var top = Enumerable.Range(0, 36).Max(Peak);
        var peaks = new List<int>();
        foreach (var i in Enumerable.Range(0, 36).OrderByDescending(Peak))
        {
            if (Peak(i) < .15 * top || peaks.Count == 3) break;
            if (peaks.All(j => Math.Min(Math.Abs(i - j), 36 - Math.Abs(i - j)) >= 4)) peaks.Add(i);
        }

        var colors = new List<Color>();
        foreach (var best in peaks)
        {
            double wt = 0, sr = 0, sg = 0, sb = 0;
            foreach (var i in new[] { (best + 35) % 36, best, (best + 1) % 36 })
            {
                wt += weight[i];
                sr += r[i];
                sg += g[i];
                sb += b[i];
            }
            if (wt <= 0) continue;
            var mean = Color.FromRgb((byte)Math.Round(sr / wt * 255), (byte)Math.Round(sg / wt * 255), (byte)Math.Round(sb / wt * 255));
            colors.Add(ModAccent.Normalize(mean));
        }
        return colors;
    }

    /// <summary>The first candidate not already worn by another mod, or null: then the mod keeps
    /// its fallback hue rather than becoming one more gold mod.</summary>
    public static Color? Pick(IEnumerable<Color> candidates, IReadOnlyCollection<Color> taken)
        => candidates.Cast<Color?>().FirstOrDefault(c => taken.All(t => HueApart(c!.Value, t) >= MinHueApart));

    /// <summary>Hue distance in degrees, 0-180.</summary>
    public static double HueApart(Color a, Color b)
    {
        var d = Math.Abs(HueOf(a) - HueOf(b)) % 360;
        return Math.Min(d, 360 - d);
    }

    private static double HueOf(Color c)
    {
        double r = c.R / 255d, g = c.G / 255d, b = c.B / 255d;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        if (d <= 0) return 0;
        var h = r == max ? (g - b) / d % 6 : g == max ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h * 60 + 360) % 360;
    }

    /// <summary>The <paramref name="index"/>-th fallback hue that is not near a colour in use: an
    /// undeclared mod must not land on a declared mod's colour either.</summary>
    public static Color FallbackAvoiding(int index, IReadOnlyCollection<Color> taken)
    {
        var found = -1;
        for (var k = 0; k < 64; k++)
        {
            var hue = Fallback(k);
            if (taken.Any(t => HueApart(hue, t) < MinHueApart / 2)) continue;
            if (++found == index) return hue;
        }
        return Fallback(index);
    }

    /// <summary><see cref="Candidates"/> for a decoded image, read at most 64 pixels wide.</summary>
    public static IReadOnlyList<Color> FromBitmap(BitmapSource source)
    {
        BitmapSource bitmap = source;
        if (bitmap.PixelWidth > 64)
        {
            var scale = 64d / bitmap.PixelWidth;
            bitmap = new TransformedBitmap(bitmap, new ScaleTransform(scale, scale));
        }
        if (bitmap.Format != PixelFormats.Bgra32) bitmap = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        int width = bitmap.PixelWidth, height = bitmap.PixelHeight, stride = width * 4;
        if (width <= 0 || height <= 0) return Array.Empty<Color>();
        var pixels = new byte[stride * height];
        bitmap.CopyPixels(pixels, stride, 0);
        return Candidates(pixels, width, height, stride);
    }

    public static string Hex(Color color) => $"#{color.R:x2}{color.G:x2}{color.B:x2}";

    private static Color FromHue(double hue)
    {
        // HSV at full value and .6 saturation; Normalize then brings it into the band.
        double s = .6, v = .85, c = v * s, h = hue / 60, x = c * (1 - Math.Abs(h % 2 - 1)), m = v - c;
        (double R, double G, double B) rgb = h switch
        {
            < 1 => (c, x, 0),
            < 2 => (x, c, 0),
            < 3 => (0, c, x),
            < 4 => (0, x, c),
            < 5 => (x, 0, c),
            _ => (c, 0, x),
        };
        return Color.FromRgb((byte)Math.Round((rgb.R + m) * 255), (byte)Math.Round((rgb.G + m) * 255), (byte)Math.Round((rgb.B + m) * 255));
    }
}

/// <summary>
/// The colours learned from art, kept between sessions so a mod opens in its colour from the
/// first frame. Keyed by the image's address: a modder who replaces the icon gets a new key, and
/// the colour is worked out again.
/// </summary>
public static class ModAccentCache
{
    private static readonly object Gate = new();
    private static Dictionary<string, string>? s_map;

    /// <summary>For tests: where the cache lives. Null is <c>AppPaths.DataDir\accent-cache.json</c>.</summary>
    internal static string? PathOverride { get; set; }

    private static string FilePath => PathOverride ?? Path.Combine(AppPaths.DataDir, "accent-cache.json");

    public static string? Get(string? imageKey)
    {
        if (string.IsNullOrWhiteSpace(imageKey)) return null;
        lock (Gate)
            return Map().TryGetValue(imageKey, out var hex) ? hex : null;
    }

    public static void Put(string imageKey, string hex)
    {
        Dictionary<string, string> copy;
        lock (Gate)
        {
            var map = Map();
            if (map.TryGetValue(imageKey, out var existing) && existing == hex) return;
            map[imageKey] = hex;
            copy = new Dictionary<string, string>(map);
        }
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(copy));
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Accent cache not saved: {ex.Message}");
        }
    }

    internal static void Forget()
    {
        lock (Gate) s_map = null;
    }

    private static Dictionary<string, string> Map()
    {
        if (s_map != null) return s_map;
        try
        {
            s_map = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath)) ?? new()
                : new();
        }
        catch (Exception)
        {
            s_map = new();
        }
        return s_map;
    }

    /// <summary>The image a mod's colour is taken from: its icon, else its banner.</summary>
    public static string? ImageKeyOf(ModProfile profile)
        => !string.IsNullOrWhiteSpace(profile.IconUrl) ? profile.IconUrl
            : !string.IsNullOrWhiteSpace(profile.BannerUrl) ? profile.BannerUrl
            : null;
}
