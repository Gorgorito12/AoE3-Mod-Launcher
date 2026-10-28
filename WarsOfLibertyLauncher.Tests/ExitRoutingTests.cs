using System;
using System.IO;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins HOW the launcher quits when the player asks it to, which no behavioural test can reach
/// without a real window, a tray icon and a match in progress.
///
/// <para><b>Why it matters.</b> During <c>Application.Shutdown</c> WPF raises <c>Closing</c> on every
/// window but IGNORES its <c>Cancel</c>. The tray's and the brand menu's Exit both called Shutdown
/// directly, so the two things <c>MainWindow.OnClosing</c> exists to do never happened for the
/// ordinary way to quit (the ✕ only hides to the tray): "wait until the match result is sent" let
/// the launcher die mid-report, and answering No to "quit during a match?" quit anyway — the walkout
/// the player had just declined. Nothing throws and nothing looks wrong in a diff; the only defence
/// is that the Exit paths go through <c>Close()</c>.</para>
/// </summary>
public class ExitRoutingTests
{
    private static string MainWindowSource() => File.ReadAllText(RepoFile("MainWindow.xaml.cs"));

    /// <summary>The body of one member, bounded by the next private/protected member.</summary>
    private static string Body(string src, string signature)
    {
        var start = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start > 0, $"'{signature}' has moved or been renamed.");
        var next = src.IndexOf("\n    private ", start + 10, StringComparison.Ordinal);
        var nextProtected = src.IndexOf("\n    protected ", start + 10, StringComparison.Ordinal);
        if (nextProtected > start && (next < 0 || nextProtected < next)) next = nextProtected;
        return next > start ? src[start..next] : src[start..];
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_AHardExitClosesTheWindowSoOnClosingCanStillSayNotYet()
    {
        var body = Body(MainWindowSource(), "private void RequestHardExit()");
        var close = body.IndexOf("Close();", StringComparison.Ordinal);
        var shutdown = body.IndexOf("Application.Current.Shutdown()", StringComparison.Ordinal);

        Assert.True(close > 0, "RequestHardExit no longer closes the window.");
        Assert.True(shutdown < 0 || shutdown > close,
            "RequestHardExit calls Application.Shutdown before Close(): OnClosing's cancel would be ignored.");

        // A cancelled close keeps the launcher in the tray, so the icon must not go first.
        var dispose = body.IndexOf("TrayIcon.Dispose", StringComparison.Ordinal);
        Assert.True(dispose < 0 || dispose > close,
            "RequestHardExit disposes the tray icon before knowing whether the close goes ahead.");
    }

    [Fact]
    public void TheBrandMenusExitGoesThroughTheSamePath()
    {
        var src = MainWindowSource();
        var label = src.IndexOf("Strings.Get(\"BrandMenuExit\")", StringComparison.Ordinal);
        Assert.True(label > 0, "The brand menu's Exit row has moved or been renamed.");
        var end = src.IndexOf("}));", label, StringComparison.Ordinal);
        var row = src[label..end];

        Assert.Contains("RequestHardExit()", row);
        Assert.DoesNotContain("Application.Current.Shutdown()", row);
    }

    [Fact]
    public void TheTrayMenusExitGoesThroughTheSamePath()
    {
        var body = Body(MainWindowSource(), "private void TrayMenuExit_Click(");
        Assert.Contains("RequestHardExit()", body);
        Assert.DoesNotContain("Shutdown()", body);
    }

    /// <summary>OnClosing's in-match MessageBox pumps messages, so the tray menu stays clickable
    /// behind it: a second Exit must not re-enter a closing window.</summary>
    [Fact]
    public void ASecondExitWhileTheFirstIsDecidingIsIgnored()
    {
        var src = MainWindowSource();
        Assert.Contains("if (_closingInProgress) return;", Body(src, "private void RequestHardExit()"));
        var onClosing = Body(src, "protected override void OnClosing(");
        var raised = onClosing.IndexOf("_closingInProgress = true;", StringComparison.Ordinal);
        var asked = onClosing.IndexOf("MultiplayerView.ConfirmCloseDuringMatch()", StringComparison.Ordinal);
        var lowered = onClosing.IndexOf("finally { _closingInProgress = false; }", StringComparison.Ordinal);
        Assert.True(raised > 0 && asked > raised && lowered > asked,
            "The in-match question in OnClosing is no longer guarded against a second Exit.");
    }

    /// <summary>Walks up to the launcher project, as TextScaleTests does.</summary>
    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var project = Path.Combine(dir.FullName, "WarsOfLibertyLauncher");
            if (File.Exists(Path.Combine(project, "App.xaml")))
                return Path.GetFullPath(Path.Combine(project, relative));
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not find the WarsOfLibertyLauncher project above the test output.");
    }
}
