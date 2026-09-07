using System;
using System.Windows;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Starting straight into the system tray.
///
/// <para><b>The bug these exist for.</b> At logon the launcher used to be shown with
/// <c>WindowState = Minimized</c> and hidden again from <c>Loaded</c>. A minimized window
/// never lays out — the launcher's own diagnostic log recorded <c>window 0x0 DIP</c> — and
/// everything that runs from <c>Loaded</c> ran against that frame, including the
/// <c>WindowChrome</c> that draws the whole title bar. The window then came back the right
/// size, in the right place, and <b>entirely black</b>. Clicking the tray icon fixed it,
/// because that path forces the frame to be recomputed.</para>
///
/// <para>So the assertion that matters is not about a property: it is that the window has
/// really <b>composed at its real size</b> by the time anything hides it.</para>
/// </summary>
[Collection("wpf-and-language")]
public class TrayStartParkingTests
{
    // ---------------------------------------------------------------- the one that matters

    /// <summary>
    /// THE ONE THAT MATTERS: the window is never MINIMIZED on the way to the tray, and it
    /// still arrives exactly where the user left it.
    ///
    /// <para>Those two together are the whole fix. Minimized was what made
    /// <c>Loaded</c> — where this launcher applies its <c>WindowChrome</c> — run against a
    /// frame with no size, and a full-size window appearing for one frame at every logon is
    /// what minimizing was avoiding in the first place. Parking off-screen is what buys
    /// both.</para>
    ///
    /// <para><b>What this test cannot do</b> is prove the window actually PAINTS: a
    /// <c>Window</c>'s size comes from its HWND, and the suite's STA thread has no message
    /// loop to deliver one, so <c>ActualWidth</c> reads zero here whatever the state. That
    /// half is verified by launching the app — see the runbook note on the reported bug.</para>
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_ItIsNeverMinimizedAndComesBackWhereItWas()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var w = new Window
            {
                Width = 1120,
                Height = 720,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 474.4,
                Top = 202.4,
                ShowInTaskbar = false,
            };

            TrayStartParking.Park(w);
            Assert.NotEqual(WindowState.Minimized, w.WindowState);

            w.Show();
            Assert.NotEqual(WindowState.Minimized, w.WindowState);

            w.Hide();
            Assert.True(TrayStartParking.Unpark(w));

            Assert.NotEqual(WindowState.Minimized, w.WindowState);
            Assert.Equal(474.4, w.Left, 3);
            Assert.Equal(202.4, w.Top, 3);

            w.Close();
        });

        Assert.Null(error);
    }

    /// <summary>And it composes somewhere nobody can see, which is what replaces the
    /// minimize: a full-size window appearing for one frame at every logon is exactly the
    /// flash the old code was avoiding.</summary>
    [Fact]
    public void TheParkedWindowIsOffEveryScreen()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var w = new Window { Width = 1120, Height = 720, ShowInTaskbar = false };
            TrayStartParking.Park(w);

            var screen = SystemParameters.VirtualScreenLeft;
            Assert.True(w.Left + w.Width < screen,
                $"parked at {w.Left} with the virtual desktop starting at {screen}.");
            Assert.True(TrayStartParking.IsParked(w));

            w.Close();
        });

        Assert.Null(error);
    }

    // ---------------------------------------------------------------- coming back

    /// <summary>The saved geometry comes back exactly. This is the position the user last
    /// left the window at; parking is not allowed to cost it.</summary>
    [Fact]
    public void UnparkRestoresAManualPosition()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var w = new Window
            {
                Width = 1120,
                Height = 720,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 474.4,
                Top = 202.4,
                ShowInTaskbar = false,
            };

            TrayStartParking.Park(w);
            w.Show();
            w.Hide();
            Assert.True(TrayStartParking.Unpark(w));

            Assert.Equal(474.4, w.Left, 3);
            Assert.Equal(202.4, w.Top, 3);
            Assert.False(TrayStartParking.IsParked(w));

            w.Close();
        });

        Assert.Null(error);
    }

    /// <summary>
    /// A window that asked to be centred is centred BY HAND, because
    /// <see cref="WindowStartupLocation.CenterScreen"/> only acts on the first <c>Show</c> —
    /// which, on this path, already happened off-screen. Restoring the property alone would
    /// leave the window parked where nobody could ever see it.
    /// </summary>
    [Fact]
    public void UnparkCentresAWindowThatNeverHadAPosition()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var w = new Window
            {
                Width = 1120,
                Height = 720,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ShowInTaskbar = false,
            };

            TrayStartParking.Park(w);
            w.Show();
            w.Hide();
            Assert.True(TrayStartParking.Unpark(w));

            var area = SystemParameters.WorkArea;
            Assert.InRange(w.Left, area.Left, area.Right);
            Assert.InRange(w.Top, area.Top, area.Bottom);

            w.Close();
        });

        Assert.Null(error);
    }

    /// <summary>
    /// The maximized state survives, and that is a second small bug fixed on the way: the
    /// old path overwrote it with Minimized, so a user whose window was maximized when they
    /// last closed it got a small one back the first time they opened it from the tray.
    /// </summary>
    [Fact]
    public void UnparkRestoresTheMaximizedState()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var w = new Window
            {
                Width = 1120,
                Height = 720,
                WindowState = WindowState.Maximized,
                ShowInTaskbar = false,
            };

            TrayStartParking.Park(w);
            // Parked windows are never maximized — a maximized window snaps to the nearest
            // monitor, which would put the parking spot back on screen.
            Assert.Equal(WindowState.Normal, w.WindowState);

            w.Show();
            w.Hide();
            Assert.True(TrayStartParking.Unpark(w));

            Assert.Equal(WindowState.Maximized, w.WindowState);

            w.Close();
        });

        Assert.Null(error);
    }

    /// <summary>
    /// Minimized is the one state never handed back. It is what this class exists to avoid,
    /// and nothing asked for it: it came from the old startup path, not from the user.
    /// </summary>
    [Fact]
    public void UnparkNeverHandsBackMinimized()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var w = new Window
            {
                Width = 900,
                Height = 600,
                WindowState = WindowState.Minimized,
                ShowInTaskbar = false,
            };

            TrayStartParking.Park(w);
            w.Show();
            w.Hide();
            TrayStartParking.Unpark(w);

            Assert.Equal(WindowState.Normal, w.WindowState);

            w.Close();
        });

        Assert.Null(error);
    }

    // ---------------------------------------------------------------- the ordinary launch

    /// <summary>
    /// A normal double-click launch is never parked, and unparking one must change nothing.
    /// The Loaded handler calls it unconditionally on the tray path only, but a false here
    /// is what keeps a future caller from moving a window it never touched.
    /// </summary>
    [Fact]
    public void UnparkingAWindowThatWasNeverParkedIsANoOp()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var w = new Window
            {
                Width = 1120,
                Height = 720,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 100,
                Top = 50,
                ShowInTaskbar = false,
            };

            Assert.False(TrayStartParking.IsParked(w));
            Assert.False(TrayStartParking.Unpark(w));

            Assert.Equal(100, w.Left);
            Assert.Equal(50, w.Top);

            w.Close();
        });

        Assert.Null(error);
    }

    /// <summary>Nulls are an ordinary answer, not a throw: this runs on the startup path,
    /// where an exception is a launcher that does not open.</summary>
    [Fact]
    public void NullsAreSurvived()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            TrayStartParking.Park(null!);
            Assert.False(TrayStartParking.Unpark(null!));
            Assert.False(TrayStartParking.IsParked(null!));
            TrayStartParking.ForceFrameChange(null!);
        });

        Assert.Null(error);
    }

    /// <summary>The frame repair is safe on a window with no handle yet — it runs from
    /// ShowFromTray, which a toast's button can reach before anything is on screen.</summary>
    [Fact]
    public void ForcingAFrameChangeOnAnUnshownWindowIsANoOp()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var w = new Window { Width = 400, Height = 300, ShowInTaskbar = false };
            TrayStartParking.ForceFrameChange(w);
            w.Close();
        });

        Assert.Null(error);
    }
}
