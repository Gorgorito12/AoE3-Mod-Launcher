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
            // Hidden, and STILL parked: the geometry only comes back when somebody asks
            // to see the window. A hidden window sitting at its real position was what a
            // foreign ShowWindow turned into a black full-screen box.
            Assert.True(TrayStartParking.IsParked(w));
            Assert.Equal(TrayStartParking.ParkedAt, w.Left);
            Assert.Equal(WindowState.Normal, w.WindowState);

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
            TrayStartParking.DescribeNative(w);
            w.Close();
        });

        Assert.Null(error);
    }

    // ---------------------------------------------------------------- foreign shows

    /// <summary>
    /// THE SECOND REPORT: the HWND was shown from outside WPF — WS_VISIBLE and maximized
    /// while WPF still said hidden — and came up black. A show that arrives while WPF's
    /// Visibility is anything but Visible did not come from WPF, and gets hidden again.
    /// </summary>
    [Fact]
    public void AShowWpfDidNotAskForIsHiddenAgain()
    {
        Assert.Equal(TrayStartParking.ForeignShowAction.Rehide,
            TrayStartParking.ForeignShowDecision(Visibility.Hidden, nativeShowing: true, showStatus: 0, recentAttempts: 0));
        Assert.Equal(TrayStartParking.ForeignShowAction.Rehide,
            TrayStartParking.ForeignShowDecision(Visibility.Collapsed, nativeShowing: true, showStatus: 0, recentAttempts: 1));
    }

    /// <summary>
    /// WPF sets Visibility BEFORE its own ShowWindow, so its shows are never foreign. Getting
    /// this wrong would hide the window every time the user opened it.
    ///
    /// <para><b>This test was not enough, and the gap is the third report.</b> It covers the
    /// easy half — WPF's show seen while Visibility says Visible — and the premise it encodes
    /// is false the moment something hides the window mid-Show(). In production this function
    /// was handed <c>Visibility.Hidden</c> for WPF's OWN show, answered "foreign", and the
    /// launcher hid itself for ever. The honest version is the test below.</para>
    /// </summary>
    [Fact]
    public void WpfsOwnShowIsLeftAlone()
    {
        Assert.Equal(TrayStartParking.ForeignShowAction.Ignore,
            TrayStartParking.ForeignShowDecision(Visibility.Visible, nativeShowing: true, showStatus: 0, recentAttempts: 0));
    }

    /// <summary>
    /// THE THIRD REPORT: the launcher hid its own startup show and could never be opened again.
    ///
    /// <para>The window starts into the tray, so it is hidden from inside the very
    /// <c>Show()</c> that is creating it — Loaded runs before Show's own native ShowWindow.
    /// That left WPF's <c>Visibility</c> reading Hidden when WPF's own show arrived, this
    /// function called it foreign, and the guard answered with a raw <c>SW_HIDE</c>. WPF then
    /// said visible while the HWND had no WS_VISIBLE, <c>Show()</c> early-returned for ever,
    /// and eleven tray clicks in three minutes did nothing at all.</para>
    ///
    /// <para>So the scope is the authority and Visibility is only a hint. <b>This is the
    /// regression test.</b></para>
    /// </summary>
    [Theory]
    [InlineData(Visibility.Hidden)]
    [InlineData(Visibility.Collapsed)]
    public void THE_THIRD_REPORT_WpfsOwnShowIsNotForeignEvenWhenItsLoadedHandlerAlreadyHidIt(
        Visibility duringShow)
    {
        Assert.Equal(TrayStartParking.ForeignShowAction.Ignore,
            TrayStartParking.ForeignShowDecision(
                duringShow, nativeShowing: true, showStatus: 0, recentAttempts: 0,
                wpfShowInProgress: true));

        // Even once it has given up on hiding: a show WPF is making itself is never the
        // show the give-up path exists for.
        Assert.Equal(TrayStartParking.ForeignShowAction.Ignore,
            TrayStartParking.ForeignShowDecision(
                duringShow, nativeShowing: true, showStatus: 0, recentAttempts: 9,
                wpfShowInProgress: true));
    }

    /// <summary>
    /// And with no scope open the old behaviour is untouched — spelled out rather than left to
    /// the default, so the parameter can never quietly change meaning and disarm the guard.
    /// </summary>
    [Fact]
    public void WithNoShowInFlightAForeignShowIsStillHiddenAgain()
    {
        Assert.Equal(TrayStartParking.ForeignShowAction.Rehide,
            TrayStartParking.ForeignShowDecision(
                Visibility.Hidden, nativeShowing: true, showStatus: 0, recentAttempts: 0,
                wpfShowInProgress: false));
        Assert.Equal(TrayStartParking.ForeignShowAction.GiveUpAndShow,
            TrayStartParking.ForeignShowDecision(
                Visibility.Hidden, nativeShowing: true, showStatus: 0, recentAttempts: 2,
                wpfShowInProgress: false));
    }

    /// <summary>A hide is never a foreign show, whatever WPF thinks.</summary>
    [Fact]
    public void AHideIsNeverForeign()
    {
        Assert.Equal(TrayStartParking.ForeignShowAction.Ignore,
            TrayStartParking.ForeignShowDecision(Visibility.Hidden, nativeShowing: false, showStatus: 0, recentAttempts: 0));
    }

    /// <summary>
    /// Statuses 1–4 mean an owner window is closing or opening. WPF syncs its own state
    /// from those (SW_PARENTOPENING sets Visibility itself), and this must not fight it.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void AnOwnerOpeningOrClosingIsWpfsBusiness(int status)
    {
        Assert.Equal(TrayStartParking.ForeignShowAction.Ignore,
            TrayStartParking.ForeignShowDecision(Visibility.Hidden, nativeShowing: true, showStatus: status, recentAttempts: 0));
    }

    /// <summary>
    /// Whoever keeps showing the window wins on the third try inside a minute: a window
    /// that is on screen and painted beats a fight with Windows that ends in a black one.
    /// </summary>
    [Fact]
    public void TheThirdShowInAMinuteIsShownProperlyInstead()
    {
        Assert.Equal(TrayStartParking.ForeignShowAction.Rehide,
            TrayStartParking.ForeignShowDecision(Visibility.Hidden, true, 0, recentAttempts: 1));
        Assert.Equal(TrayStartParking.ForeignShowAction.GiveUpAndShow,
            TrayStartParking.ForeignShowDecision(Visibility.Hidden, true, 0, recentAttempts: 2));
        Assert.Equal(TrayStartParking.ForeignShowAction.GiveUpAndShow,
            TrayStartParking.ForeignShowDecision(Visibility.Hidden, true, 0, recentAttempts: 7));
    }

    /// <summary>The live path, on a real hidden window: the decision is Rehide, and the
    /// repair is scheduled rather than run inside the message — so a call from a WndProc
    /// hook returns at once.</summary>
    [Fact]
    public void OnForeignShowDecidesAndSchedulesWithoutThrowing()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var w = new Window { Width = 400, Height = 300, ShowInTaskbar = false };
            TrayStartParking.Park(w);
            w.Show();
            w.Hide();

            var decision = TrayStartParking.OnForeignShow(w, "test", 0, () => { });
            Assert.Equal(TrayStartParking.ForeignShowAction.Rehide, decision);

            // The second message of the same native show is folded into the first.
            Assert.Equal(TrayStartParking.ForeignShowAction.Ignore,
                TrayStartParking.OnForeignShow(w, "test-second-message", 0, () => { }));

            // Nulls are an ordinary answer on a startup path.
            Assert.Equal(TrayStartParking.ForeignShowAction.Ignore,
                TrayStartParking.OnForeignShow(null!, "test", 0, () => { }));

            w.Close();
        });

        Assert.Null(error);
    }

    // ---------------------------------------------------------------- the show scope

    /// <summary>
    /// THE SHAPE OF THE BUG: a window hidden while WPF's own <c>Show()</c> is still in flight.
    /// That is the exact state the guard used to misread — Visibility says Hidden, yet the show
    /// about to be reported is WPF's — and the whole point of the scope is that it survives it.
    ///
    /// <para><b>The hide is made by hand rather than from a <c>Loaded</c> handler, and that is
    /// a limitation of this harness, not a weaker test.</b> The suite's STA thread has no
    /// message loop (see the class doc), so <c>Loaded</c> is not delivered inside <c>Show()</c>
    /// here the way it is in the real launcher — asserting that it is would be asserting
    /// something about xUnit. What this pins is the STATE and the DECISION, which is what the
    /// launcher actually got wrong.</para>
    /// </summary>
    [Fact]
    public void AHideWhileWpfIsStillShowingIsNotAForeignShow()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var w = new Window { Width = 400, Height = 300, ShowInTaskbar = false };

            using (TrayStartParking.EnterWpfShow(w))
            {
                w.Show();
                w.Hide();                     // what the Loaded handler used to do, mid-Show

                Assert.True(TrayStartParking.IsWpfShowing(w));
                // Visibility could never have been the signal: WPF's own ShowWindow is still
                // to come, and the window already reads Hidden.
                Assert.NotEqual(Visibility.Visible, w.Visibility);

                // THE REGRESSION, on the live path: this combination used to be answered with
                // a raw SW_HIDE, and the launcher could never be opened again.
                Assert.Equal(TrayStartParking.ForeignShowAction.Ignore,
                    TrayStartParking.OnForeignShow(w, "wpf-own-show", 0, () => { }));
            }

            Assert.False(TrayStartParking.IsWpfShowing(w));

            w.Close();
        });

        Assert.Null(error);
    }

    /// <summary>Nestable and balanced: ReconcileVisibility opens one inside a caller's.</summary>
    [Fact]
    public void TheShowScopeNestsAndUnwinds()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var w = new Window { Width = 400, Height = 300, ShowInTaskbar = false };
            Assert.False(TrayStartParking.IsWpfShowing(w));

            using (TrayStartParking.EnterWpfShow(w))
            {
                Assert.True(TrayStartParking.IsWpfShowing(w));
                using (TrayStartParking.EnterWpfShow(w))
                    Assert.True(TrayStartParking.IsWpfShowing(w));
                // The inner one closing must NOT end the outer one's claim.
                Assert.True(TrayStartParking.IsWpfShowing(w));
            }

            Assert.False(TrayStartParking.IsWpfShowing(w));
            w.Close();
        });

        Assert.Null(error);
    }

    /// <summary>One window's show says nothing about another's — the launcher has several,
    /// and blinding the guard for all of them would undo the second report's fix.</summary>
    [Fact]
    public void AShowScopeDoesNotLeakToAnotherWindow()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var shown = new Window { Width = 400, Height = 300, ShowInTaskbar = false };
            var other = new Window { Width = 400, Height = 300, ShowInTaskbar = false };

            using (TrayStartParking.EnterWpfShow(shown))
            {
                Assert.True(TrayStartParking.IsWpfShowing(shown));
                Assert.False(TrayStartParking.IsWpfShowing(other));
            }

            Assert.False(TrayStartParking.IsWpfShowing(null!));

            shown.Close();
            other.Close();
        });

        Assert.Null(error);
    }

    /// <summary>The live path: inside a scope the guard leaves the window alone, and it does
    /// not bank the attempt either — otherwise ordinary shows would spend the budget that
    /// decides when to stop fighting a genuinely foreign one.</summary>
    [Fact]
    public void OnForeignShowIgnoresEverythingInsideAShowScope()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var w = new Window { Width = 400, Height = 300, ShowInTaskbar = false };
            TrayStartParking.Park(w);
            w.Show();
            w.Hide();

            using (TrayStartParking.EnterWpfShow(w))
            {
                Assert.Equal(TrayStartParking.ForeignShowAction.Ignore,
                    TrayStartParking.OnForeignShow(w, "wpf-own-show", 0, () => { }));
            }

            // Nothing was counted while we were ignoring: the first REAL foreign show is still
            // attempt one, so it is re-hidden rather than given up on.
            Assert.Equal(TrayStartParking.ForeignShowAction.Rehide,
                TrayStartParking.OnForeignShow(w, "really-foreign", 0, () => { }));

            w.Close();
        });

        Assert.Null(error);
    }

    // ---------------------------------------------------------------- the recovery net

    /// <summary>
    /// The net only fires on a real disagreement. Everything else is one cheap probe: a window
    /// with no handle, a window WPF agrees is hidden, and null are all ordinary answers, not
    /// throws — this runs on the path that opens the launcher.
    /// </summary>
    [Fact]
    public void ReconcileVisibilityIsANoOpWhenThereIsNothingToRepair()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            Assert.False(TrayStartParking.ReconcileVisibility(null!, "test"));
            Assert.False(TrayStartParking.IsNativelyVisible(null!));

            var w = new Window { Width = 400, Height = 300, ShowInTaskbar = false };
            // No HWND yet.
            Assert.False(TrayStartParking.IsNativelyVisible(w));
            Assert.False(TrayStartParking.ReconcileVisibility(w, "no-handle"));

            w.Show();
            w.Hide();
            // WPF agrees it is hidden: that is the tray, not a desync.
            Assert.False(TrayStartParking.ReconcileVisibility(w, "hidden-on-purpose"));

            w.Close();
        });

        Assert.Null(error);
    }

    /// <summary>
    /// The net never HIDES anything: whatever it does, a window the caller just showed is still
    /// asking to be visible when it returns, and no scope is left open behind it (one that
    /// leaked would blind the guard for the rest of the session).
    ///
    /// <para><b>What this deliberately does not assert is <c>IsVisible</c>.</b> The suite's STA
    /// thread has no message loop, so a shown window does not reliably reach that state here —
    /// which also means the repair branch itself cannot be exercised in xUnit at all. It is
    /// verified by launching the app and reading the log; see the class doc.</para>
    /// </summary>
    [Fact]
    public void ReconcileVisibilityNeverHidesAWindowAndLeavesNoScopeOpen()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var w = new Window { Width = 400, Height = 300, ShowInTaskbar = false };
            w.Show();
            var before = w.Visibility;

            TrayStartParking.ReconcileVisibility(w, "test");

            Assert.Equal(before, w.Visibility);
            Assert.Equal(Visibility.Visible, w.Visibility);
            Assert.False(TrayStartParking.IsWpfShowing(w));

            w.Close();
        });

        Assert.Null(error);
    }
}
