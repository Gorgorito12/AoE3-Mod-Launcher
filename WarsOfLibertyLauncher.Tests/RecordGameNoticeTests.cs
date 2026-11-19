using System;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The Record Game question before a competitive start. It is asked only with something to say,
/// and a snooze may silence it, but never past the next match that went unrecorded.
/// </summary>
public class RecordGameNoticeTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ATickedBoxAndARecordedMatchAskNothing()
    {
        Assert.Equal(RecordGameNotice.Reason.None, RecordGameNotice.Decide(false, true, null, Now));
        Assert.Equal(RecordGameNotice.Reason.None, RecordGameNotice.Decide(null, true, null, Now));
    }

    [Fact]
    public void AnUnrecordedLastMatchLeadsAndAFailedWriteIsSaid()
    {
        Assert.Equal(RecordGameNotice.Reason.LastNotRecorded, RecordGameNotice.Decide(true, true, null, Now));
        Assert.Equal(RecordGameNotice.Reason.LastNotRecorded, RecordGameNotice.Decide(true, false, null, Now));
        Assert.Equal(RecordGameNotice.Reason.CouldNotTick, RecordGameNotice.Decide(null, false, null, Now));
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_ASnoozeLastsItsDaysAndNoLonger()
    {
        var until = RecordGameNotice.SnoozeUntil(Now);
        Assert.Equal(RecordGameNotice.Reason.None, RecordGameNotice.Decide(true, false, until, Now.AddDays(6)));
        Assert.Equal(RecordGameNotice.Reason.LastNotRecorded, RecordGameNotice.Decide(true, false, until, Now.AddDays(7)));
    }

    [Fact]
    public void ASnoozeInThePastSilencesNothing()
        => Assert.Equal(RecordGameNotice.Reason.CouldNotTick,
            RecordGameNotice.Decide(null, false, Now.AddMinutes(-1), Now));
}
