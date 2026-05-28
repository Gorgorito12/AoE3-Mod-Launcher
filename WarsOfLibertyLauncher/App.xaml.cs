using System.Windows;
using System.Windows.Media;

namespace WarsOfLibertyLauncher;

public partial class App : System.Windows.Application
{
    // App-level startup applies HiDPI-friendly rendering settings to every
    // Window in the launcher.
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
    /// Apply the HiDPI-crisp rendering settings to a Window the first
    /// time it loads. Idempotent — re-running on the same Window just
    /// re-sets the same values, so we don't bother unregistering.
    /// </summary>
    private static void OnAnyWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Window w) return;

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
    }
}
