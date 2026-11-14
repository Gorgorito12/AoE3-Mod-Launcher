using System;
using System.Windows.Threading;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The set the "UI OP" timing keeps while an operation runs (2026-11-13). It was a
/// ConditionalWeakTable, which leaked its internal containers at one add and one remove per
/// operation: ~235 MB an hour of private memory in a launcher left open while anything animated.
/// </summary>
public class InFlightOperationsTests
{
    private sealed class Op { }

    [Fact]
    public void THE_ONE_THAT_MATTERS_AMillionOperationsLeaveNothingBehind()
    {
        var set = new InFlightOperations<Op, long>();
        for (int i = 0; i < 1_000_000; i++)
        {
            var op = new Op();
            set.Start(op, i);
            Assert.True(set.TryComplete(op, out var started));
            Assert.Equal(i, started);
        }
        Assert.Equal(0, set.Count);
    }

    [Fact]
    public void ANestedOperationFinishesInsideTheOneThatRanIt()
    {
        var set = new InFlightOperations<Op, string>();
        Op outer = new(), inner = new();
        set.Start(outer, "outer");
        set.Start(inner, "inner");
        Assert.True(set.TryComplete(inner, out var a));
        Assert.Equal("inner", a);
        Assert.True(set.TryComplete(outer, out var b));
        Assert.Equal("outer", b);
        Assert.Equal(0, set.Count);
    }

    [Fact]
    public void AnOperationThatNeverReportedIsDroppedWithTheOneBelowIt()
    {
        var set = new InFlightOperations<Op, int>();
        Op outer = new(), lost = new();
        set.Start(outer, 1);
        set.Start(lost, 2);                 // threw past the hook: never completes
        Assert.True(set.TryComplete(outer, out _));
        Assert.Equal(0, set.Count);
        Assert.False(set.TryComplete(lost, out _));
    }

    [Fact]
    public void AnUnknownOperationIsIgnoredAndTheSetIsBounded()
    {
        var set = new InFlightOperations<Op, int>();
        Assert.False(set.TryComplete(new Op(), out _));
        for (int i = 0; i < InFlightOperations<Op, int>.Capacity * 3; i++) set.Start(new Op(), i);
        Assert.Equal(InFlightOperations<Op, int>.Capacity, set.Count);
    }

    /// <summary>The real hooks on a real dispatcher: thousands of operations, nothing kept.</summary>
    [Fact]
    public void TheStallWatchOnARealDispatcherKeepsNothing()
    {
        var error = StaTestThread.Run(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            DiagnosticLog.StartUiStallWatch(dispatcher);
            for (int i = 0; i < 5000; i++)
                dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            var frame = new DispatcherFrame();
            dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Assert.True(DiagnosticLog.InFlightOperationCount <= 2,
                $"{DiagnosticLog.InFlightOperationCount} operations still held after they all completed");
        }, TimeSpan.FromSeconds(60));
        Assert.Null(error);
    }
}
