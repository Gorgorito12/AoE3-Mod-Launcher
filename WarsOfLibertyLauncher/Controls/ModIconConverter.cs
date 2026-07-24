using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// Paints a notification row with the icon of the mod it is about, from the
/// <c>ModId</c> the item already carries.
///
/// <para>Resolution is the shared one — <see cref="ModRegistry.Find"/> →
/// <see cref="Models.ModProfile.ResolveIconSource"/> →
/// <see cref="MainWindow.TryLoadTileImage"/> — so bell rows use the same cache and the
/// same cached-icon → catalog-URL → packed-fallback order as every other icon surface,
/// and pick up a replaced icon when <c>InvalidateTileImageCache</c> runs.</para>
///
/// <para><b>Returning null is a normal outcome, not a failure.</b> Some kinds have no mod
/// at all (<c>LauncherUpdate</c>, <c>Connectivity</c>), and notifications PERSIST across
/// sessions, so an item can outlive its mod's presence in the catalog. Both cases fall
/// back to the per-kind glyph, which is why <see cref="ModIconPresentConverter"/> exists
/// alongside this one.</para>
/// </summary>
public sealed class ModIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Resolve(value as string);

    /// <summary>Shared by both converters so they can never disagree about a row.</summary>
    internal static ImageBrush? Resolve(string? modId)
    {
        if (string.IsNullOrWhiteSpace(modId)) return null;

        var profile = ModRegistry.Find(modId);
        if (profile == null) return null;

        return MainWindow.TryLoadTileImage(profile.ResolveIconSource());
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// <c>Visible</c> when <see cref="ModIconConverter"/> would produce an icon for this
/// notification, so the row can show the mod icon and hide the fallback glyph — or the
/// other way round with <c>ConverterParameter=invert</c>.
///
/// <para>Kept as a separate converter rather than a <c>Background</c>-null trigger because
/// a WPF <c>DataTrigger</c> can only compare against a literal, and "the icon failed to
/// load" is not a value the row's own bindings can see.</para>
/// </summary>
public sealed class ModIconPresentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool present = ModIconConverter.Resolve(value as string) != null;
        if (string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase))
            present = !present;
        return present ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
