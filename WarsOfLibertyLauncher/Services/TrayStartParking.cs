using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Where the window sits while the launcher starts straight into the tray.
///
/// <para><b>Starting into the tray is not the same as starting MINIMIZED, and the difference
/// is a bug somebody reported.</b> A window shown at logon with
/// <c>WindowState = Minimized</c> never lays out: its frame has no size, and the diagnostic
/// log says so in as many words — <c>window 0x0 DIP</c>. Everything that runs from
/// <c>Loaded</c> then runs against that frame, including
/// <c>App.OnAnyWindowLoaded</c>'s <see cref="System.Windows.Shell.WindowChrome"/>, which is
/// what draws this launcher's entire title bar. The next time anything shows the window it
/// comes up the right size, in the right place, and <b>completely black</b> — no title bar,
/// no content — until something forces the frame to be recomputed. Clicking the tray icon
/// does force it, which is exactly why the reported workaround worked.</para>
///
/// <para><b>So the window is parked off-screen instead, at its real size.</b> It shows,
/// lays out and composes once, properly; <c>Loaded</c> hides it to the tray; and this puts
/// the geometry back for the first real appearance. Nothing flashes, because no monitor
/// contains the parking spot.</para>
///
/// <para>The window cannot simply be left unshown: the tray icon it must leave behind lives
/// inside its visual tree, so nothing exists until <c>Show()</c> builds it.</para>
/// </summary>
public static class TrayStartParking
{
    /// <summary>
    /// Off every desktop, and not so far that the number stops surviving the trip through
    /// Win32. Multiplied by the DPI scale this is still tens of thousands of pixels from
    /// any monitor, and it stays inside the range legacy window messages can carry.
    /// </summary>
    internal const double ParkedAt = -8000;

    /// <summary>What the window asked for before it was parked.</summary>
    private sealed record Spot(
        WindowStartupLocation Location,
        double Left,
        double Top,
        WindowState State,
        bool ShowActivated);

    /// <summary>Keyed on the window and holding nothing alive: a parked window that is
    /// closed without ever being unparked must not be kept for the life of the process.</summary>
    private static readonly ConditionalWeakTable<Window, Spot> Parked = new();

    /// <summary>
    /// Put the window somewhere no screen shows before it is first shown. Call this
    /// <b>before</b> <c>Show()</c>; after it, the window has a handle and the startup
    /// location no longer means anything.
    /// </summary>
    public static void Park(Window window)
    {
        if (window == null) return;

        Parked.Remove(window);
        Parked.Add(window, new Spot(
            window.WindowStartupLocation, window.Left, window.Top,
            window.WindowState, window.ShowActivated));

        // Not Minimized, and not Maximized either: a maximized window snaps to the monitor
        // nearest its position, so parking one off-screen would land it on the primary
        // display at full size — the flash this is here to avoid.
        window.WindowState = WindowState.Normal;
        window.ShowActivated = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = ParkedAt;
        window.Top = ParkedAt;
    }

    /// <summary>
    /// Put the geometry back, once the window is hidden. Returns false when this window was
    /// never parked, which is the ordinary answer for a normal launch.
    ///
    /// <para>The saved <see cref="WindowState"/> comes back with it, so a user whose window
    /// was maximized when they last closed it gets a maximized window when they open it from
    /// the tray — which is what happens on every other launch and did not happen here.</para>
    /// </summary>
    public static bool Unpark(Window window)
    {
        if (window == null || !Parked.TryGetValue(window, out var spot)) return false;
        Parked.Remove(window);

        window.ShowActivated = spot.ShowActivated;

        if (spot.Location == WindowStartupLocation.Manual
            && !double.IsNaN(spot.Left) && !double.IsNaN(spot.Top))
        {
            window.Left = spot.Left;
            window.Top = spot.Top;
        }
        else
        {
            // CenterScreen only acts on the FIRST Show, which has already happened by now,
            // so the centring has to be done by hand or the window stays parked.
            Center(window);
        }

        // Minimized is the one state never restored: it is what this class exists to avoid,
        // and it is not what the user asked for anyway — it came from the old startup path.
        window.WindowState = spot.State == WindowState.Minimized
            ? WindowState.Normal
            : spot.State;

        return true;
    }

    /// <summary>True while this window is parked — for a caller that wants to say so.</summary>
    public static bool IsParked(Window window) =>
        window != null && Parked.TryGetValue(window, out _);

    private static void Center(Window window)
    {
        var area = SystemParameters.WorkArea;
        var width = double.IsNaN(window.Width) ? window.ActualWidth : window.Width;
        var height = double.IsNaN(window.Height) ? window.ActualHeight : window.Height;
        if (width <= 0) width = area.Width / 2;
        if (height <= 0) height = area.Height / 2;

        window.Left = area.Left + Math.Max(0, (area.Width - width) / 2);
        window.Top = area.Top + Math.Max(0, (area.Height - height) / 2);
    }

    // ------------------------------------------------------------------ the repair

    private const int SWP_NOSIZE = 0x0001;
    private const int SWP_NOMOVE = 0x0002;
    private const int SWP_NOZORDER = 0x0004;
    private const int SWP_NOACTIVATE = 0x0010;
    private const int SWP_FRAMECHANGED = 0x0020;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    /// <summary>
    /// Tell Windows the frame changed, without moving, resizing or raising the window.
    ///
    /// <para>Belt and braces for the same bug: <b>every</b> path that shows this window runs
    /// through it — the tray icon, a deep link, a toast's button, Windows restoring the
    /// session after a reboot — and a frame change is what makes the custom chrome recompute
    /// instead of trusting WPF to have noticed. It costs one message and is a no-op when
    /// there was nothing wrong.</para>
    /// </summary>
    public static void ForceFrameChange(Window window)
    {
        if (window == null) return;
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOSIZE | SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"TrayStartParking: could not force a frame change — {ex.Message}");
        }
    }
}
