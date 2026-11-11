using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// A context menu opened from the KEYBOARD (the menu key, Shift+F10) opens under the element that has
/// focus, as a right click opens it under the pointer.
///
/// <para>WPF centres a keyboard-opened menu on its element: measured on the folded Library list, the
/// menu covered the very tile it belonged to and ran 87 px out of the left edge of the window. A
/// right click is unaffected - its menu still opens at the pointer, because only a menu whose
/// placement is the default <see cref="PlacementMode.MousePoint"/> is touched, and only for that
/// one opening. WPF itself sets a keyboard opening to <see cref="PlacementMode.Center"/> after
/// <c>ContextMenuOpening</c>, which is why the menu is opened here rather than repositioned.</para>
/// </summary>
internal static class KeyboardContextMenu
{
    private static bool s_registered;

    /// <summary>Marks a menu whose placement this class changed, so only those are put back.</summary>
    private static readonly DependencyProperty MovedProperty =
        DependencyProperty.RegisterAttached("Moved", typeof(bool), typeof(KeyboardContextMenu));

    internal static void Register()
    {
        if (s_registered) return;
        s_registered = true;
        EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.ContextMenuOpeningEvent,
            new ContextMenuEventHandler(OnOpening));
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.ClosedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is ContextMenu menu) Restore(menu);
            }));
    }

    /// <summary>A keyboard opening reports no pointer: both coordinates are -1.</summary>
    internal static bool IsFromKeyboard(double cursorLeft, double cursorTop) => cursorLeft < 0 && cursorTop < 0;

    private static void OnOpening(object sender, ContextMenuEventArgs e)
    {
        if (e.Handled || !IsFromKeyboard(e.CursorLeft, e.CursorTop)) return;
        if (sender is not FrameworkElement owner || owner.ContextMenu is not { } menu) return;
        // The class handler runs on every element of the route; only the nearest owner's menu opens.
        if (!ReferenceEquals(NearestOwner(e.OriginalSource as DependencyObject), owner)) return;
        if (menu.Placement != PlacementMode.MousePoint) return;
        // Opened HERE, and the event handled: WPF overwrites the placement of a menu it opens from
        // the keyboard with Center after this event, so setting it and letting WPF open the menu
        // changes nothing (measured).
        e.Handled = true;
        Place(menu, owner);
        menu.IsOpen = true;
    }

    /// <summary>Under <paramref name="owner"/>, left-aligned, 4 px below it - unless the caller
    /// chose a placement of its own.</summary>
    internal static void Place(ContextMenu menu, FrameworkElement owner)
    {
        if (menu.Placement != PlacementMode.MousePoint) return;
        menu.Placement = PlacementMode.Bottom;
        menu.PlacementTarget = owner;
        menu.VerticalOffset = 4;
        menu.SetValue(MovedProperty, true);
    }

    internal static void Restore(ContextMenu menu)
    {
        if (!(bool)menu.GetValue(MovedProperty)) return;
        menu.ClearValue(MovedProperty);
        menu.ClearValue(ContextMenu.PlacementProperty);
        menu.ClearValue(ContextMenu.PlacementTargetProperty);
        menu.ClearValue(ContextMenu.VerticalOffsetProperty);
    }

    private static FrameworkElement? NearestOwner(DependencyObject? from)
    {
        for (var d = from; d != null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is FrameworkElement fe && fe.ContextMenu != null) return fe;
        return null;
    }
}
