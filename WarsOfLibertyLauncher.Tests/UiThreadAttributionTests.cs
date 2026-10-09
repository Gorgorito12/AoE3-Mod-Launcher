using System;
using System.IO;
using System.Windows.Threading;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Every slow timer tick is attributed to its timer.
///
/// <para>A tick used to log as "DispatcherTimer+&lt;&gt;c.&lt;Restart&gt;b__21_0" — the same line
/// for about 25 timers of ours and WPF's own tooltip and menu timers — so a 797-ms stall could not
/// be pinned on Radmin, the lobby tick, the rooms ticks or a tooltip. The real-frame test is what
/// proves the private field names still hold on this runtime: a reflection that silently finds
/// nothing would print the old unattributable line and look fine.</para>
/// </summary>
public class UiThreadAttributionTests
{
    [Fact]
    public void ASlowOpCarriesTheGcPauseOnlyWhenItMatters()
    {
        Assert.Equal("UI OP  800 ms — X.Y  (priority Normal)",
            DiagnosticLog.FormatSlowOp(800, "X.Y  (priority Normal)", gcMs: 19));
        Assert.Equal("UI OP  800 ms — X.Y (GC 20 ms)", DiagnosticLog.FormatSlowOp(800, "X.Y", gcMs: 20));
        Assert.Equal("", DiagnosticLog.GcSuffix(0));
    }

    private static void NamedTickHandler(object? sender, EventArgs e) { }

    [Fact]
    public void ATimerIsNamedByItsHandlerAndInterval()
    {
        var error = StaTestThread.Run(() =>
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5), Tag = "radmin" };
            timer.Tick += NamedTickHandler;
            var text = DiagnosticLog.DescribeTimer(timer);
            Assert.Contains("2.5 s", text);
            Assert.Contains("UiThreadAttributionTests.NamedTickHandler", text);
            Assert.Contains("[radmin]", text);

            var fast = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            Assert.Contains("100 ms", DiagnosticLog.DescribeTimer(fast));
        }, TimeSpan.FromSeconds(30));
        Assert.Null(error);
    }

    /// <summary>
    /// THE ONE THAT MATTERS: a real tick, run by a real dispatcher, is described with the
    /// handler's name — which only works if DispatcherOperation still keeps the timer in the
    /// private field the description reads.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_ARealTickNamesItsHandler()
    {
        var error = StaTestThread.Run(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            string? described = null;
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(1),
            };
            timer.Tick += NamedTickHandler;
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };

            dispatcher.Hooks.OperationCompleted += (_, a) =>
            {
                var text = DiagnosticLog.DescribeOperation(a.Operation);
                if (text.Contains("NamedTickHandler", StringComparison.Ordinal)) described = text;
            };

            var giveUp = new DispatcherTimer(TimeSpan.FromSeconds(10), DispatcherPriority.Normal,
                (_, _) => frame.Continue = false, dispatcher);
            timer.Start();
            Dispatcher.PushFrame(frame);
            giveUp.Stop();

            Assert.NotNull(described);
            Assert.Contains("timer every 1 ms", described);
        }, TimeSpan.FromSeconds(30));
        Assert.Null(error);
    }

    /// <summary>The startup Show and the storm tick are timed — the two untimed suspects.</summary>
    [Fact]
    public void TheStartupShowAndTheStormTickAreTimed()
    {
        var app = File.ReadAllText(LauncherFile("App.xaml.cs"));
        Assert.Contains("Time(\"startup: MainWindow.Show\"", app);
        var log = File.ReadAllText(LauncherFile("Services/DiagnosticLog.cs"));
        Assert.Contains("Time(\"layout storm tick\", TickLayoutStorm)", log);
        var tab = File.ReadAllText(LauncherFile("Controls/MultiplayerTab.xaml.cs"));
        foreach (var what in new[] { "MP radmin tick", "MP lobby tick", "MP in-game tick", "MP rooms-ping tick", "MP rooms-list tick" })
            Assert.Contains($"\"{what}\"", tab);
    }

    internal static string LauncherFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "WarsOfLibertyLauncher", relative);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(relative);
    }
}
