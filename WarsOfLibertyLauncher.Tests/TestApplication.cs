using System.Windows;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The one <see cref="Application"/> every XAML-building test shares.
///
/// <para><b>Why this exists, because the failure is not what it looks like.</b> Both
/// <c>DialogXamlTests</c> and <c>WorkshopAndAddonsLayoutTests</c> guarded with
/// <c>Application.Current ?? new Application()</c>, which reads as safe and is not:
/// <c>Application.Current</c> goes NULL when the STA thread that created it exits, while WPF's
/// own "only one Application per AppDomain" guard does NOT reset. So whichever class runs
/// second finds null, calls the constructor, and gets
/// <c>InvalidOperationException</c> — and which class that is depends on test ORDER, so the
/// suite passes until somebody adds a test and it fails somewhere unrelated to their change.
/// That is exactly how it surfaced.</para>
///
/// <para>Holding the instance here survives the thread that made it. The lock is not
/// theoretical: xUnit runs test collections in parallel, so two classes can reach this at
/// once.</para>
/// </summary>
internal static class TestApplication
{
    private static readonly object Gate = new();
    private static Application? _app;

    public static Application Ensure()
    {
        lock (Gate)
        {
            // Application.Current first: another class may have made it before this field was
            // ever written, and adopting it is what keeps there being exactly one.
            _app ??= Application.Current ?? new Application();
            return _app;
        }
    }
}
