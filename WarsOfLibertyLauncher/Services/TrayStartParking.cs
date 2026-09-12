using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Where the window sits while the launcher starts straight into the tray, and what to do
/// when something other than WPF puts it on screen.
///
/// <para><b>Starting into the tray is not the same as starting MINIMIZED, and the difference
/// is a bug somebody reported.</b> A window shown at logon with
/// <c>WindowState = Minimized</c> never lays out: its frame has no size, and the diagnostic
/// log says so in as many words — <c>window 0x0 DIP</c>. Everything that runs from
/// <c>Loaded</c> then runs against that frame, including
/// <c>App.OnAnyWindowLoaded</c>'s <see cref="System.Windows.Shell.WindowChrome"/>, which is
/// what draws this launcher's entire title bar.</para>
///
/// <para><b>So the window is parked off-screen instead, at its real size.</b> It shows,
/// lays out and composes once, properly; <c>App.ShowMainWindow</c> hides it to the tray the
/// instant <c>Show()</c> RETURNS; and it STAYS parked until somebody actually opens it —
/// <see cref="Unpark"/> runs from <c>ShowFromTray</c>, right before <c>Show()</c>. Nothing
/// flashes, because no monitor contains the parking spot.</para>
///
/// <para><b>The second report, and the reason the window stays parked.</b> With the first
/// version of this class the window was unparked from <c>Loaded</c>, so a hidden window
/// sat at its real position with a deferred <c>Maximized</c>. The next morning it was on
/// screen again — maximized, black, and with WPF still convinced it was hidden: the log had
/// no <c>ShowFromTray</c> and no <c>visible=True</c>, while the HWND itself reported
/// <c>WS_VISIBLE</c> and a <c>WPF_RESTORETOMAXIMIZED</c> placement. Somebody had called
/// <c>ShowWindow</c> on it without going through WPF, and WPF does not paint a window it
/// believes is hidden. Nothing in this repository does that, and the session was a
/// fast-startup resume, where Windows re-places windows as the display comes back. Whoever
/// it is, the answer is the same: a hidden window that is shown from outside is
/// <see cref="OnForeignShow">re-hidden</see>, and the event is logged with a stack so the
/// next report names the caller.</para>
///
/// <para><b>The THIRD report, and it was this class firing on its own launcher.</b> The
/// re-hide above answered a show that came from <c>App.OnStartup</c> → <c>Window.Show()</c>
/// — the stack it logs named the caller, exactly as intended, and the caller was us.
/// <c>Window.Show()</c> creates the HWND and raises <c>Loaded</c> BEFORE it issues its own
/// native <c>ShowWindow</c>, so hiding to the tray from <c>Loaded</c> ran a nested
/// show-helper, drove WPF's visibility back to hidden, and then let the outer <c>Show()</c>
/// fire a now-stale <c>ShowWindow</c> at a window WPF believed was hidden. The decision below
/// read that as foreign and answered with <c>SW_HIDE</c>; from there WPF said visible while
/// the HWND had no <c>WS_VISIBLE</c>, <c>Show()</c> early-returned for ever, and the tray icon
/// could never open the launcher again. Two things came out of it: the tray-start hide moved
/// OUT of <c>Loaded</c> to after <c>Show()</c> returns, and the premise "WPF sets Visibility
/// first" stopped being an inference — <see cref="EnterWpfShow"/> states it outright. The
/// window is also put back in step by <see cref="ReconcileVisibility"/> rather than being left
/// unopenable, because a desync from ANY cause used to be terminal.</para>
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
        // display at full size — the flash this is here to avoid. And Normal is what it
        // KEEPS while hidden: a deferred Maximized is exactly what turned a foreign
        // ShowWindow into a full-screen black window.
        window.WindowState = WindowState.Normal;
        window.ShowActivated = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = ParkedAt;
        window.Top = ParkedAt;
    }

    /// <summary>
    /// Put the geometry back. Call it right before the <c>Show()</c> that first puts the
    /// window in front of the user. Returns false when this window was never parked, which
    /// is the ordinary answer for a normal launch.
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

    /// <summary>True while this window is parked — for a caller that wants to say so, and
    /// for <c>SaveWindowState</c>, which must not write the parking spot to the config.</summary>
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

    // ------------------------------------------------------------------ foreign shows

    private static readonly ConditionalWeakTable<Window, StrongBox<int>> ShowDepth = new();

    /// <summary>
    /// "WPF is inside <c>Show()</c> for this window, RIGHT NOW." Wrap every <c>Show()</c> of a
    /// window this class guards.
    ///
    /// <para>The guard used to infer the same fact from <see cref="UIElement.Visibility"/>, and
    /// that inference is sound only while nothing hides the window mid-<c>Show()</c> — which is
    /// exactly what a <c>Hide()</c> from <c>Loaded</c> does, and exactly how this class came to
    /// hide the launcher's own startup show. This makes the premise true by construction rather
    /// than by assumption.</para>
    ///
    /// <para>Nestable, because <see cref="ReconcileVisibility"/> opens one around a
    /// <c>Hide()</c>/<c>Show()</c> pair that may itself sit inside a caller's scope.</para>
    /// </summary>
    public static ShowScope EnterWpfShow(Window window) => new(window);

    /// <summary>True while a <see cref="EnterWpfShow"/> scope is open for this window.</summary>
    public static bool IsWpfShowing(Window window) =>
        window != null && ShowDepth.TryGetValue(window, out var depth) && depth.Value > 0;

    /// <summary>The scope <see cref="EnterWpfShow"/> hands back. Disposing it is what ends the
    /// "WPF is showing" window; a missed dispose would make the guard permanently blind, so it
    /// is a <c>using</c> at every call site.</summary>
    public readonly struct ShowScope : IDisposable
    {
        private readonly Window? _window;

        internal ShowScope(Window? window)
        {
            _window = window;
            if (window != null) ShowDepth.GetOrCreateValue(window).Value++;
        }

        public void Dispose()
        {
            if (_window != null && ShowDepth.TryGetValue(_window, out var depth) && depth.Value > 0)
                depth.Value--;
        }
    }

    /// <summary>What to do about a native show that WPF did not ask for.</summary>
    public enum ForeignShowAction
    {
        /// <summary>Not foreign, or not a show: WPF's own doing, or an owner opening.</summary>
        Ignore,
        /// <summary>Hide it again, natively — WPF's <c>Hide()</c> is a no-op here because WPF
        /// already believes the window is hidden.</summary>
        Rehide,
        /// <summary>It keeps coming back. Stop fighting and show it properly, through WPF, so
        /// that whatever is on screen is at least painted.</summary>
        GiveUpAndShow,
    }

    /// <summary>How many foreign shows inside <see cref="ForeignShowWindow"/> before the
    /// window is shown properly instead of hidden again.</summary>
    internal const int ForeignShowsBeforeGivingUp = 3;

    internal static readonly TimeSpan ForeignShowWindow = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The decision, with nothing attached to it so it can be tested without an HWND.
    /// </summary>
    /// <param name="wpfVisibility">The window's <see cref="UIElement.Visibility"/>. WPF sets it
    /// to Visible before its own <c>ShowWindow</c>, so a show arriving while it is anything else
    /// is PROBABLY not WPF's — but only probably, which is why
    /// <paramref name="wpfShowInProgress"/> outranks it. Hiding the window from inside the
    /// <c>Show()</c> that is creating it leaves this reading Hidden for WPF's own show.</param>
    /// <param name="nativeShowing">The message's <c>wParam</c>: true for a show, false for
    /// a hide.</param>
    /// <param name="showStatus">The message's <c>lParam</c>: 0 for a programmatic
    /// <c>ShowWindow</c>; 1–4 mean an owner window is closing or opening, which WPF
    /// handles itself and this must leave alone.</param>
    /// <param name="recentAttempts">Foreign shows already handled for this window inside
    /// <see cref="ForeignShowWindow"/>, not counting this one.</param>
    /// <param name="wpfShowInProgress">A <see cref="EnterWpfShow"/> scope is open: WPF is inside
    /// <c>Show()</c> for this window and the show being reported is its own. This is the
    /// AUTHORITY, and it is read BEFORE <paramref name="wpfVisibility"/> precisely because that
    /// property is the one thing that cannot be trusted inside a <c>Show()</c>.</param>
    public static ForeignShowAction ForeignShowDecision(
        Visibility wpfVisibility, bool nativeShowing, int showStatus, int recentAttempts,
        bool wpfShowInProgress = false)
    {
        if (!nativeShowing) return ForeignShowAction.Ignore;
        if (showStatus != 0) return ForeignShowAction.Ignore;
        if (wpfShowInProgress) return ForeignShowAction.Ignore;
        if (wpfVisibility == Visibility.Visible) return ForeignShowAction.Ignore;

        return recentAttempts + 1 >= ForeignShowsBeforeGivingUp
            ? ForeignShowAction.GiveUpAndShow
            : ForeignShowAction.Rehide;
    }

    private sealed class ForeignShowLog
    {
        public readonly List<DateTime> Attempts = new();
        public bool Pending;
    }

    private static readonly ConditionalWeakTable<Window, ForeignShowLog> ForeignShows = new();

    /// <summary>
    /// The window's HWND is being shown and WPF did not ask for it. Decide, log with a
    /// stack, and act — after the current message, never inside it.
    ///
    /// <para>One native show arrives as two messages (<c>WM_SHOWWINDOW</c>, then
    /// <c>WM_WINDOWPOSCHANGED</c> with <c>SWP_SHOWWINDOW</c>); the second is ignored while
    /// the first's repair is pending, so a single show counts once.</para>
    /// </summary>
    /// <param name="source">Which message noticed, for the log.</param>
    /// <param name="showStatus">The <c>WM_SHOWWINDOW</c> status, 0 when it came from
    /// <c>WM_WINDOWPOSCHANGED</c>.</param>
    /// <param name="showProperly">What to run when giving up: the launcher's own
    /// show-from-tray path.</param>
    public static ForeignShowAction OnForeignShow(
        Window window, string source, int showStatus, Action showProperly)
    {
        if (window == null) return ForeignShowAction.Ignore;

        var log = ForeignShows.GetOrCreateValue(window);
        if (log.Pending) return ForeignShowAction.Ignore;

        var now = DateTime.UtcNow;
        log.Attempts.RemoveAll(t => now - t > ForeignShowWindow);

        var decision = ForeignShowDecision(
            window.Visibility, true, showStatus, log.Attempts.Count, IsWpfShowing(window));
        if (decision == ForeignShowAction.Ignore) return decision;

        log.Attempts.Add(now);
        log.Pending = true;

        DiagnosticLog.Write(
            $"TrayStartParking: foreign show via {source} (status={showStatus}) while WPF "
            + $"visibility={window.Visibility}, state={window.WindowState}, parked={IsParked(window)}; "
            + $"{DescribeNative(window)}; attempt {log.Attempts.Count} in the last minute → {decision}.\n"
            + Environment.StackTrace);

        try
        {
            window.Dispatcher.BeginInvoke(DispatcherPriority.Send, () =>
            {
                log.Pending = false;
                try
                {
                    if (decision == ForeignShowAction.GiveUpAndShow)
                    {
                        DiagnosticLog.Write("TrayStartParking: giving up on hiding — showing properly.");
                        showProperly?.Invoke();
                        return;
                    }

                    // Still hidden as far as WPF knows? Then hide the HWND to match. If WPF
                    // showed it in the meantime, there is nothing to repair.
                    if (window.Visibility == Visibility.Visible) return;
                    // And re-ask the authority: this repair is QUEUED, so a nested message loop
                    // can pump it while ShowWindow is still on the stack inside WPF's own
                    // Show(). Hiding there is how the launcher came to hide itself at logon.
                    if (IsWpfShowing(window)) return;
                    var handle = new WindowInteropHelper(window).Handle;
                    if (handle == IntPtr.Zero) return;
                    ShowWindow(handle, SW_HIDE);
                    DiagnosticLog.Write($"TrayStartParking: re-hidden; {DescribeNative(window)}.");
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Write($"TrayStartParking: repair after a foreign show failed — {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            log.Pending = false;
            DiagnosticLog.Write($"TrayStartParking: could not schedule the repair — {ex.Message}");
        }

        return decision;
    }

    /// <summary>
    /// The HWND's own idea of its state, for the log lines that so far only had WPF's.
    /// This is the line that was missing from the second report: WPF said hidden, the
    /// HWND said visible and maximized, and only a probe from outside the process could
    /// tell.
    /// </summary>
    public static string DescribeNative(Window window)
    {
        try
        {
            if (window == null) return "native=?";
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return "native=no-hwnd";

            var text = IsWindowVisible(handle) ? "native=visible" : "native=hidden";
            if (IsIconic(handle)) text += ",iconic";
            if (IsZoomed(handle)) text += ",zoomed";

            var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
            if (GetWindowPlacement(handle, ref placement))
            {
                text += $" placement(showCmd={placement.showCmd},flags={placement.flags},"
                      + $"normal={placement.rcNormalPosition.Left},{placement.rcNormalPosition.Top}-"
                      + $"{placement.rcNormalPosition.Right},{placement.rcNormalPosition.Bottom})";
            }
            return text;
        }
        catch (Exception ex)
        {
            return $"native=? ({ex.Message})";
        }
    }

    /// <summary>Does the HWND carry <c>WS_VISIBLE</c>? The question WPF cannot answer about
    /// itself, and the one that separates "hidden in the tray" from "WPF thinks it is on screen
    /// and Windows disagrees". False for a window with no handle yet, and for null.</summary>
    public static bool IsNativelyVisible(Window window)
    {
        try
        {
            if (window == null) return false;
            var handle = new WindowInteropHelper(window).Handle;
            return handle != IntPtr.Zero && IsWindowVisible(handle);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"TrayStartParking: could not read native visibility — {ex.Message}");
            return false;
        }
    }

    private static bool _reconciling;

    /// <summary>
    /// WPF says the window is on screen and the HWND says it is not. Put them back in step.
    /// Returns true when a repair was actually needed.
    ///
    /// <para><b>Why this exists at all: without it a desync is TERMINAL.</b> WPF short-circuits
    /// <c>Show()</c> while it believes the window is already shown, so once its idea of
    /// visibility and the HWND's diverge, every later <c>Show()</c> returns without ever
    /// reaching <c>ShowWindow</c> — and since every route back (the tray icon, the tray menu, a
    /// deep link, a relaunch, a toast's button) funnels through <c>ShowFromTray</c>, the
    /// launcher can never be opened again. That is not a theory; it is a user's log, eleven tray
    /// clicks over three minutes, each one answered with <c>visible=True … native=hidden</c>.
    /// The cause has been fixed, so this should now never fire — which is exactly why it must
    /// stay: the NEXT cause turns a dead launcher into a logged blip.</para>
    ///
    /// <para>Cheap when nothing is wrong: one <c>IsWindowVisible</c>, and both states have to
    /// disagree before anything is touched.</para>
    /// </summary>
    public static bool ReconcileVisibility(Window window, string caller)
    {
        if (window == null || _reconciling) return false;

        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return false;
            if (!window.IsVisible) return false;        // WPF agrees it is hidden: not this bug.
            if (IsWindowVisible(handle)) return false;  // In step: nothing to repair.

            DiagnosticLog.Write(
                $"TrayStartParking: visibility desync after {caller} — WPF IsVisible=True, "
                + $"Visibility={window.Visibility}, state={window.WindowState}, "
                + $"{DescribeNative(window)}. Re-showing through WPF.");

            _reconciling = true;

            // Hide() FIRST, and it is not optional: Show() cannot help here, because WPF
            // short-circuits it while it believes the window is already shown. Hide() is what
            // drives that belief back — and its own ShowWindow(SW_HIDE) is a no-op on an HWND
            // that is already hidden, so it costs nothing and cannot make anything worse. The
            // Show() that follows then runs the whole show path, ShowWindow included. Setting
            // Visibility = Visible instead does nothing at all: the property already says so.
            using (EnterWpfShow(window))
            {
                window.Hide();
                window.Show();
            }

            if (IsWindowVisible(handle))
            {
                DiagnosticLog.Write($"TrayStartParking: desync repaired; {DescribeNative(window)}.");
                ForceFrameChange(window);
                return true;
            }

            // Last resort, and safe here in a way it explicitly was NOT in the second report:
            // that black box came from showing a window WPF believed was HIDDEN, and WPF does
            // not paint one of those. Here WPF believes it is visible, so it paints.
            DiagnosticLog.Write("TrayStartParking: still hidden after Hide/Show — forcing SW_SHOW.");
            ShowWindow(handle, window.WindowState == WindowState.Maximized
                ? SW_SHOWMAXIMIZED
                : SW_SHOWNORMAL);
            ForceFrameChange(window);
            DiagnosticLog.Write($"TrayStartParking: after SW_SHOW, {DescribeNative(window)}.");
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"TrayStartParking: reconcile failed — {ex.Message}");
            return false;
        }
        finally
        {
            _reconciling = false;
        }
    }

    // ------------------------------------------------------------------ the repair

    private const int SW_HIDE = 0;
    private const int SW_SHOWNORMAL = 1;
    private const int SW_SHOWMAXIMIZED = 3;
    private const int SWP_NOSIZE = 0x0001;
    private const int SWP_NOMOVE = 0x0002;
    private const int SWP_NOZORDER = 0x0004;
    private const int SWP_NOACTIVATE = 0x0010;
    private const int SWP_FRAMECHANGED = 0x0020;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public int length;
        public int flags;
        public int showCmd;
        public POINT ptMinPosition;
        public POINT ptMaxPosition;
        public RECT rcNormalPosition;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

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
