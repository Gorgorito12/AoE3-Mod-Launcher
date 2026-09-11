namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// How long the launcher is willing to wait for the recording of a match that just ended, and —
/// the part that needed a home of its own — <b>on which side of the match report it waits</b>.
///
/// <para><b>The report used to never wait, and that cost a competitive match its evidence.</b>
/// The first pass ran once with no delay, reported whatever it had, and the retries ran behind
/// it. For most matches that is right: AoE3's per-match Record Game box comes up unticked, so
/// waiting would slow the majority down for nothing. A COMPETITIVE room inverts every term of
/// that argument — the launcher makes the host confirm Record Game before the countdown, so
/// there IS a recording — and the report going out without it is what leaves the match with no
/// fingerprint, no civilizations, and no way for the server's abandonment rule to clear its
/// own anti-farm brake.</para>
///
/// <para><b>So the competitive ladder is SPLIT, not doubled.</b> Putting the whole thing in
/// front of the report and keeping the continuation behind it would spend ~33 s against
/// <see cref="RoomMatchState.ResultGraceSeconds"/> (30) — the ceiling on how long a player may
/// be held in the room — and the hold is stamped ONCE, when the phase leaves None, so both
/// halves are charged to the same clock. Splitting keeps the total exactly where it was.</para>
///
/// <para>Pure and WPF-free so the arithmetic that keeps it under that ceiling is pinned by a
/// test rather than by a comment.</para>
/// </summary>
public static class ReplayRetryLadder
{
    /// <summary>
    /// The casual ladder, unchanged: one immediate look before the report, and the patient
    /// attempts afterwards. ~8.5 s in total, all of it behind the report.
    /// </summary>
    private static readonly int[] CasualFull = { 0, 1000, 2500, 5000 };

    /// <summary>
    /// The competitive ladder, unchanged in total: ~16.5 s. What changed is where the report
    /// sits in it — see <see cref="PreReport"/>.
    /// </summary>
    private static readonly int[] CompetitiveFull = { 0, 1000, 2500, 5000, 8000 };

    /// <summary>How many of the competitive attempts happen BEFORE the report.</summary>
    private const int CompetitivePreReportAttempts = 3;

    /// <summary>
    /// The attempts to make before reporting.
    ///
    /// <para>Casual is a single immediate look — byte for byte what <c>firstPassOnly</c> did,
    /// so nothing about a casual match's timing moves.</para>
    ///
    /// <para>Competitive is the first three rungs, ~3.5 s: enough for the ordinary case where
    /// the file is simply still being flushed, and short enough that a player whose recording
    /// is never coming is not left staring at a closed game for long.</para>
    /// </summary>
    public static int[] PreReport(bool competitive)
        => competitive ? Slice(CompetitiveFull, 0, CompetitivePreReportAttempts) : new[] { 0 };

    /// <summary>
    /// The attempts left for the continuation, which runs BEHIND the report and so costs
    /// nobody any latency.
    ///
    /// <para>Its first element is always 0: the continuation is a fresh call and its own first
    /// attempt must not re-pay a delay the pre-report pass already spent. The rungs after it
    /// are whatever the pre-report pass did not use.</para>
    /// </summary>
    public static int[] Continuation(bool competitive)
    {
        if (!competitive) return CasualFull;

        var rest = Slice(
            CompetitiveFull,
            CompetitivePreReportAttempts,
            CompetitiveFull.Length - CompetitivePreReportAttempts);

        var ladder = new int[rest.Length + 1];
        ladder[0] = 0;
        rest.CopyTo(ladder, 1);
        return ladder;
    }

    /// <summary>
    /// The total a ladder spends waiting, in milliseconds. Exists for the test that pins the
    /// split against <see cref="RoomMatchState.ResultGraceSeconds"/>; nothing in the launcher
    /// reads it.
    /// </summary>
    public static int TotalDelayMs(int[] ladder)
    {
        var sum = 0;
        foreach (var d in ladder) sum += d;
        return sum;
    }

    /// <summary>The competitive ladder as it was before it was split, for the same test.</summary>
    public static int[] CompetitiveUnsplit() => (int[])CompetitiveFull.Clone();

    private static int[] Slice(int[] source, int start, int count)
    {
        var slice = new int[count];
        System.Array.Copy(source, start, slice, 0, count);
        return slice;
    }
}
