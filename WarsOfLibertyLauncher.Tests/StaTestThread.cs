using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Runs a test body on an STA thread of its own, and — the reason this exists — tears the
/// thread's WPF state down before the thread is allowed to end.
///
/// <para><b>Without the teardown the test HOST dies, intermittently, and the run still reports
/// success.</b> A Window that was shown owns an HWND subclassed by WPF; when the thread that
/// created it exits with the HWND still alive, Windows destroys it during thread teardown and
/// its messages arrive at WPF's managed window procedure after the managed thread is already
/// gone — <c>NullReferenceException at Thread.get_CurrentThread() / HwndSubclass.SubclassWndProc</c>,
/// an unhandled exception that kills the host. Measured: roughly one full run in three aborted
/// that way, always inside <c>TrayStartParkingTests</c>, the one class that shows real windows,
/// and each of those runs printed "passed" with forty or fifty fewer tests than it has.</para>
///
/// <para>So the body runs in a <c>try</c>, and the <c>finally</c> closes every presentation
/// source this thread still owns and shuts its dispatcher down, which is what unhooks WPF's
/// window procedures while the thread is still alive to answer them. That applies whether the
/// body finished or an assertion threw halfway, which is the case a per-test <c>Close()</c>
/// never covered.</para>
/// </summary>
internal static class StaTestThread
{
    /// <summary>
    /// Runs <paramref name="body"/> on a fresh STA thread and returns the exception it threw,
    /// or null. Fails the calling test if the thread does not finish within
    /// <paramref name="timeout"/>.
    /// </summary>
    internal static Exception? Run(Action body, TimeSpan timeout)
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { captured = ex; }
            finally { TearDown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(timeout), "the STA thread did not finish");
        return captured;
    }

    /// <summary>
    /// Closes every window (or disposes every other source) this thread created, then shuts its
    /// dispatcher down. Never throws: it runs after the test's own verdict and must not replace
    /// it.
    /// </summary>
    private static void TearDown()
    {
        var dispatcher = Dispatcher.FromThread(Thread.CurrentThread);
        if (dispatcher == null) return;

        try
        {
            var mine = new List<PresentationSource>();
            foreach (PresentationSource source in PresentationSource.CurrentSources)
                if (source.Dispatcher == dispatcher) mine.Add(source);

            foreach (var source in mine)
            {
                try
                {
                    if (source.RootVisual is Window window) window.Close();
                    else if (source is HwndSource hwnd && !hwnd.IsDisposed) hwnd.Dispose();
                }
                catch { /* a window that refuses to close is shut down with its dispatcher */ }
            }
        }
        catch { /* the enumeration is best-effort; the shutdown below is what matters */ }

        try { dispatcher.InvokeShutdown(); }
        catch { }
    }
}
