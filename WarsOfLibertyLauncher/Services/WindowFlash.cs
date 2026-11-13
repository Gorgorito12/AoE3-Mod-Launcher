using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Flashes a window's taskbar button until it is brought to the front — the standard Windows
/// "something here needs you" signal, used when a quick match finds a rival. Best-effort: a
/// window with no handle yet, or a shell that refuses, simply does not flash.
/// </summary>
internal static class WindowFlash
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    private const uint FLASHW_ALL = 3;
    private const uint FLASHW_TIMERNOFG = 12;

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

    public static void UntilForeground(Window? window)
    {
        if (window == null) return;
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            var info = new FLASHWINFO
            {
                cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
                hwnd = hwnd,
                dwFlags = FLASHW_ALL | FLASHW_TIMERNOFG,
                uCount = 0,
                dwTimeout = 0,
            };
            FlashWindowEx(ref info);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"WindowFlash: {ex.Message}");
        }
    }
}
