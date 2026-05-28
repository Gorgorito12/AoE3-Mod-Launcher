using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace WarsOfLibertyLauncher;

public partial class App : System.Windows.Application
{
    // App-level startup applies HiDPI-friendly rendering settings AND a
    // maximize-respects-taskbar fix to every Window in the launcher.
    //
    // -- HiDPI text crispness ----------------------------------------
    //
    // The problem: on Windows displays scaled above 100% (the default on
    // most modern laptops — 125% or 150%), WPF's defaults produce visibly
    // blurry text and slightly fuzzy borders because:
    //   • UseLayoutRounding defaults to false, so a 50-logical-pixel
    //     element at 125% DPI scale lands on physical pixel 62.5 — and
    //     ClearType antialiasing smudges that fractional position into a
    //     blur instead of rendering crisply at integer pixels.
    //   • TextFormattingMode defaults to Ideal (typographically pretty
    //     at every scale, but soft-looking for small UI text) instead of
    //     Display (snapped to the target pixel grid, optimal for UI).
    //   • TextRenderingMode + TextHintingMode default to Auto, which can
    //     pick non-ClearType paths on some configurations.
    //
    // Three of the launcher's Windows (MainWindow, RadminAssistantWindow,
    // ModPropertiesDialog) already set TextOptions on their XAML root, but
    // none of them set UseLayoutRounding, and the other 14 dialogs set
    // neither. Rather than copy-paste four attribute lines into 17 XAML
    // files — and remember to add them to every future Window — we hook
    // a routed-event class handler for Window.Loaded and apply the
    // settings programmatically. This catches every Window subclass
    // uniformly, current and future, without per-XAML maintenance.
    //
    // -- Maximize respects the taskbar -------------------------------
    //
    // The problem: WPF Windows with WindowStyle="None" + custom
    // WindowChrome (MainWindow + LauncherSettings + ModProperties +
    // LobbyWindow all use this recipe) cover the ENTIRE monitor when
    // maximised, including the Windows taskbar. The user sees the
    // launcher chrome eat the system tray, start button and clock.
    //
    // Cause: when Windows asks "how big should this window get when
    // maximised?" (the WM_GETMINMAXINFO message), it normally clamps
    // to the work area (monitor minus taskbar) for regular windows.
    // For WindowStyle="None" + WindowChrome it defaults to the full
    // monitor rectangle — the "I'm doing my own chrome, I'll take the
    // whole thing" mode — which is wrong for app windows that want
    // to look like regular OS windows.
    //
    // Fix: every Window that's WindowStyle=None gets a WndProc hook
    // installed at Loaded time. The hook listens for WM_GETMINMAXINFO,
    // reads the current monitor's work area, and overrides MaxPosition
    // / MaxSize / MaxTrackSize to that. Windows then maximises to the
    // visible area only and the taskbar stays put.
    //
    // The hook needs the HWND, which doesn't exist until SourceInitialized;
    // we use Loaded (one routed-event tick later) because there's no
    // routed-event class handler for SourceInitialized. The maximise
    // override applies to the FIRST size change after the user clicks
    // maximise, so a one-tick delay is invisible.
    //
    // Why Loaded and not Initialized: WPF has no routed-event class
    // handler for FrameworkElement.Initialized (it's a normal .NET
    // event, not a routed event, so EventManager.RegisterClassHandler
    // can't bind to it). Loaded fires after the first layout pass; the
    // re-layout / re-render triggered by setting these properties at
    // that point completes in microseconds and is invisible to the user.
    // Explicit XAML settings on individual Windows still work — they
    // just set the same value as the class handler, no conflict.
    //
    // (The original theme picker that previously lived here is gone —
    // the launcher is dark-only "dorado imperial" via Styles/Colors.xaml.)
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnAnyWindowLoaded));
    }

    /// <summary>
    /// Apply the HiDPI-crisp rendering settings + the maximize-respects-
    /// taskbar hook to a Window the first time it loads. Idempotent —
    /// re-running on the same Window just re-sets the same values and
    /// the second AddHook is shadowed by HwndSource's internal dedup.
    /// </summary>
    private static void OnAnyWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Window w) return;

        // -- HiDPI crispness (always on) --
        //
        // UseLayoutRounding is the single biggest fix for "blurry WPF
        // text at non-100% DPI". It rounds every layout coordinate to
        // a whole device pixel, eliminating the sub-pixel positioning
        // that triggers ClearType's smudge-mode rendering.
        w.UseLayoutRounding = true;

        // TextOptions are inherited attached properties — set on the
        // Window root, they cascade to every TextBlock, TextBox, Label,
        // Button content, etc., in the visual tree.
        TextOptions.SetTextFormattingMode(w, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(w, TextRenderingMode.ClearType);
        TextOptions.SetTextHintingMode(w, TextHintingMode.Fixed);

        // -- Maximize-respects-taskbar (only Windows with no OS chrome) --
        //
        // Regular Windows (WindowStyle=SingleBorderWindow or Three­Dim…)
        // already maximise to the work area by default — Windows handles
        // that internally for them. Only chrome-less Windows fall through
        // to "use the full monitor" behaviour, so we restrict the hook
        // to those. Saves a no-op WndProc on every dialog that opts to
        // keep the native title bar.
        if (w.WindowStyle == WindowStyle.None)
        {
            InstallMaximizeFix(w);
        }
    }

    // ==================================================================
    // Maximize-respects-taskbar: WM_GETMINMAXINFO interop
    // ==================================================================

    private const int WM_GETMINMAXINFO = 0x0024;
    private const int MONITOR_DEFAULTTONEAREST = 0x00000002;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    /// <summary>
    /// Wire a WndProc hook on the Window's HWND that responds to
    /// WM_GETMINMAXINFO with the current monitor's WORK area (excluding
    /// the taskbar) instead of letting Windows default to the full
    /// monitor rectangle. The hook lives for the Window's lifetime;
    /// HwndSource cleans it up when the HWND is destroyed.
    /// </summary>
    private static void InstallMaximizeFix(Window w)
    {
        var helper = new WindowInteropHelper(w);
        var source = HwndSource.FromHwnd(helper.Handle);
        if (source == null) return;
        source.AddHook(MaximizeFixWndProc);
    }

    private static IntPtr MaximizeFixWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_GETMINMAXINFO) return IntPtr.Zero;

        // Snap the MINMAXINFO struct out of the lParam buffer, edit it,
        // write it back. Marshal.StructureToPtr's third param (fDeleteOld)
        // is false because the unmanaged buffer wasn't allocated by us
        // — Windows owns it and just wants us to mutate in place.
        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);

        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor != IntPtr.Zero)
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                var work = info.rcWork;
                var mon = info.rcMonitor;
                // MaxPosition is monitor-relative (origin = top-left of
                // the monitor that owns the window), so subtract the
                // monitor's origin from the work area's origin. On a
                // single-monitor / left-anchored taskbar setup this is
                // (0, 0); with a side-mounted taskbar or a non-primary
                // monitor it shifts.
                mmi.ptMaxPosition.X = Math.Abs(work.Left - mon.Left);
                mmi.ptMaxPosition.Y = Math.Abs(work.Top - mon.Top);
                mmi.ptMaxSize.X = Math.Abs(work.Right - work.Left);
                mmi.ptMaxSize.Y = Math.Abs(work.Bottom - work.Top);
                mmi.ptMaxTrackSize.X = mmi.ptMaxSize.X;
                mmi.ptMaxTrackSize.Y = mmi.ptMaxSize.Y;
            }
        }

        Marshal.StructureToPtr(mmi, lParam, fDeleteOld: false);
        handled = true;
        return IntPtr.Zero;
    }
}
