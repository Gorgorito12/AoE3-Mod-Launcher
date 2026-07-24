using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// Hosts <see cref="AppToast"/> cards on the DESKTOP, for when the launcher window can't
/// show them — minimised, hidden in the tray, or buried behind another app.
///
/// <para><b>Why a window and not a Windows notification.</b> The in-app card carries action
/// buttons ("Join" / "Ignore"), and a tray balloon can't: it would announce a room and then
/// make the user find it by hand, which is most of the value gone. The card is reused
/// verbatim — <see cref="AppToast.Show"/> takes any <see cref="Panel"/> and resolves its
/// brushes through <c>Application.Current</c>, so it never depended on the main window.</para>
///
/// <para>Singleton: one window collects every card, so several rooms appearing at once
/// stack in one place instead of scattering overlapping windows across the corner.</para>
/// </summary>
public sealed class DesktopToastWindow : Window
{
    private static DesktopToastWindow? s_instance;

    private readonly StackPanel _host;
    private readonly DispatcherTimer _reaper;

    private DesktopToastWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;

        // Load-bearing: without this the toast steals focus from whatever the user is
        // doing — mid-sentence in another app, or worse mid-game. An unread notification
        // is a smaller problem than swallowed keystrokes.
        ShowActivated = false;

        // The card has rounded corners and a drop shadow over an arbitrary desktop
        // background, so it needs real transparency — otherwise it sits in a black box.
        // The repo removed AllowsTransparency from its DIALOGS on purpose (it costs
        // hardware acceleration); this is a small, transient window, so the trade is
        // inverted here.
        AllowsTransparency = true;

        _host = new StackPanel { Margin = new Thickness(12) };
        Content = _host;

        // AppToast owns its own auto-dismiss; this only notices when the last card is
        // gone so the empty window doesn't linger invisibly on top of everything.
        _reaper = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _reaper.Tick += (_, _) => { if (_host.Children.Count == 0) Close(); };
        _reaper.Start();

        // SizeToContent means the size is only known after a layout pass, so the corner
        // has to be re-resolved as cards come and go.
        SizeChanged += (_, _) => SnapToWorkAreaCorner();
        Loaded += (_, _) => SnapToWorkAreaCorner();
    }

    /// <summary>
    /// Bottom-right of the WORK AREA, not the screen: the work area excludes the taskbar,
    /// so the card can't end up underneath it.
    /// </summary>
    private void SnapToWorkAreaCorner()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - ActualWidth - 12;
        Top = wa.Bottom - ActualHeight - 12;
    }

    protected override void OnClosed(EventArgs e)
    {
        _reaper.Stop();
        if (ReferenceEquals(s_instance, this)) s_instance = null;
        base.OnClosed(e);
    }

    /// <summary>
    /// Shows <paramref name="opts"/> as a floating desktop card, creating the shared
    /// window on first use. Never throws — a notification must not take down a caller.
    /// </summary>
    public static void Show(AppToast.ToastOptions opts)
    {
        if (opts == null) return;
        try
        {
            if (s_instance == null)
            {
                s_instance = new DesktopToastWindow();
                s_instance.Show();
            }

            AppToast.Show(s_instance._host, opts);
            // Re-assert topmost: another app going full-screen can push us behind it.
            s_instance.Topmost = true;
        }
        catch (Exception ex)
        {
            Services.DiagnosticLog.Write($"DesktopToastWindow.Show failed: {ex.Message}");
        }
    }

    /// <summary>Closes the floating window if one is open. Safe when there is none.</summary>
    public static void CloseIfOpen()
    {
        try { s_instance?.Close(); }
        catch (Exception ex) { Services.DiagnosticLog.Write($"DesktopToastWindow.Close failed: {ex.Message}"); }
    }
}
