using System;
using System.Linq;
using System.Windows;
using Microsoft.Win32;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Swaps the application-level color dictionary between dark, light, and
/// "follow Windows" modes. Operates on Application.Current.Resources so
/// every DynamicResource consumer (the dialog buttons, the ProgressBar
/// fills, etc.) repaints automatically when Apply() runs.
/// </summary>
public static class ThemeService
{
    public const string Dark = "dark";
    public const string Light = "light";
    public const string System = "system";

    /// <summary>
    /// Resolves the effective theme: "dark" or "light". Pass "system" to
    /// read the Windows AppsUseLightTheme registry key (defaults to dark
    /// if the key is missing).
    /// </summary>
    public static string Resolve(string theme)
    {
        if (string.Equals(theme, Light, StringComparison.OrdinalIgnoreCase)) return Light;
        if (string.Equals(theme, System, StringComparison.OrdinalIgnoreCase)) return IsWindowsLight() ? Light : Dark;
        return Dark;
    }

    /// <summary>
    /// Replaces the current color dictionary in Application.Resources with
    /// the one matching <paramref name="theme"/>. Safe to call before the
    /// main window opens (App.Current.Resources is populated at App.xaml
    /// parse time, which runs before MainWindow's constructor).
    /// </summary>
    public static void Apply(string theme)
    {
        var effective = Resolve(theme);
        var newSource = effective == Light
            ? "/Styles/Colors.Light.xaml"
            : "/Styles/Colors.xaml";
        var targetSuffix = effective == Light ? "Colors.Light.xaml" : "Colors.xaml";

        var merged = Application.Current?.Resources?.MergedDictionaries;
        if (merged == null) return;

        var existing = merged.FirstOrDefault(d =>
            d.Source is { } src &&
            (src.OriginalString.EndsWith("Colors.Light.xaml", StringComparison.OrdinalIgnoreCase)
             || src.OriginalString.EndsWith("Colors.xaml", StringComparison.OrdinalIgnoreCase)));
        if (existing == null) return;

        // Already on the target theme — skip the swap (avoids needless
        // DynamicResource invalidation churn on no-op Apply() calls).
        if (existing.Source.OriginalString.EndsWith(targetSuffix, StringComparison.OrdinalIgnoreCase))
            return;

        var replacement = new ResourceDictionary { Source = new Uri(newSource, UriKind.Relative) };
        var idx = merged.IndexOf(existing);
        merged[idx] = replacement;
    }

    private static bool IsWindowsLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int i && i != 0;
        }
        catch
        {
            return false;
        }
    }
}
