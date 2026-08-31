using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The rule behind the "you are running the launcher as another Windows account" notice.
///
/// <para><b>The rejection cases are the point.</b> A false negative here leaves the launcher
/// behaving exactly as it does today; a false positive puts an alarming — and wrong — message in
/// front of somebody whose machine is fine. So every case where the answer is unknown has to come
/// back as "no mismatch", and most of what follows is that: blank names, whitespace, a bare domain
/// prefix, the same account spelled three different ways.</para>
///
/// <para>Only <see cref="RunningAccount.Evaluate"/> and <see cref="RunningAccount.StripDomain"/> are
/// exercised, which is deliberate — they are the whole decision, and they carry no interop, so the
/// rule can be pinned without a second Windows account to run under.</para>
/// </summary>
public class RunningAccountTests
{
    [Fact]
    public void TheSameAccountIsNeverAMismatch()
    {
        // Everybody's machine. If this ever reports a mismatch, the notice fires for the whole
        // player base at once.
        var info = RunningAccount.Evaluate("Miro", "Miro", elevated: false);

        Assert.False(info.Mismatch);
        Assert.Equal("Miro", info.ProcessUser);
        Assert.Equal("Miro", info.SessionUser);
    }

    [Theory]
    [InlineData(@"PC\Miro", "Miro")]          // WindowsIdentity qualifies, WTS does not
    [InlineData("Miro", @"PC\Miro")]          // and the other way round
    [InlineData("MIRO", "miro")]              // Windows account names are case-insensitive
    [InlineData(@"AzureAD\Miro", "miro")]     // a work account still names the same person
    [InlineData("miro@example.com", @"PC\Miro")] // UPN form, seen on Microsoft accounts
    [InlineData("  Miro  ", "Miro")]          // whatever padding the API hands back
    public void TheSameAccountSpelledDifferentlyIsStillTheSameAccount(string process, string session)
    {
        Assert.False(RunningAccount.Evaluate(process, session, elevated: true).Mismatch);
    }

    [Theory]
    [InlineData(null, "Miro")]
    [InlineData("Miro", null)]
    [InlineData("", "Miro")]
    [InlineData("Miro", "")]
    [InlineData("   ", "Miro")]
    [InlineData("Miro", "   ")]
    [InlineData(@"PC\", "Miro")]   // strips to nothing, which is not a name
    [InlineData("Miro", @"PC\")]
    [InlineData(null, null)]
    public void AnUnknownNameIsNotEvidenceOfAnything(string? process, string? session)
    {
        // The interop can fail — an unusual session, a locked-down machine — and when it does the
        // honest answer is silence, not a guess in either direction.
        Assert.False(RunningAccount.Evaluate(process, session, elevated: true).Mismatch);
    }

    [Fact]
    public void TheMeasuredCaseIsReported()
    {
        // Gommiustan's machine, from the 30 August bundle: the launcher running as the admin
        // account while his own session was open, which is what split his recordings across two
        // Documents folders.
        var info = RunningAccount.Evaluate(@"PC\a-admin", "Miro", elevated: true);

        Assert.True(info.Mismatch);
        // Both names travel to the dialog, and without their prefixes: "PC\a-admin" in a sentence
        // reads as machine detail, not as an account somebody chose.
        Assert.Equal("a-admin", info.ProcessUser);
        Assert.Equal("Miro", info.SessionUser);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ElevationIsCarriedThroughUntouched(bool elevated)
    {
        // Elevation is reported, never used as the signal: running as administrator under your OWN
        // account splits nothing, and plenty of people do it.
        Assert.Equal(elevated, RunningAccount.Evaluate("a-admin", "Miro", elevated).Elevated);
        Assert.Equal(elevated, RunningAccount.Evaluate("Miro", "Miro", elevated).Elevated);
    }

    [Fact]
    public void ElevationAloneIsNotAMismatch()
    {
        Assert.False(RunningAccount.Evaluate("Miro", "Miro", elevated: true).Mismatch);
    }

    [Theory]
    [InlineData(@"PC\Miro", "Miro")]
    [InlineData(@"DOMAIN\SUB\Miro", "Miro")]  // last separator wins
    [InlineData("miro@example.com", "miro")]
    [InlineData("@example.com", "@example.com")] // nothing before the @: leave it alone
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void StripDomainKeepsOnlyTheAccountName(string? input, string expected)
    {
        Assert.Equal(expected, RunningAccount.StripDomain(input));
    }

    [Fact]
    public void DescribeNamesBothAccountsEitherWay()
    {
        // The log line is the whole point of writing this on every launch — including the boring
        // case, because "they match" is what rules the hypothesis out.
        var same = RunningAccount.Describe(RunningAccount.Evaluate("Miro", "Miro", false));
        Assert.Contains("Miro", same);
        Assert.Contains("same account", same);

        var split = RunningAccount.Describe(RunningAccount.Evaluate("a-admin", "Miro", true));
        Assert.Contains("a-admin", split);
        Assert.Contains("Miro", split);
        Assert.Contains("MISMATCH", split);
    }

    [Fact]
    public void DescribeSaysUnknownRatherThanShowingAnEmptyName()
    {
        // A bundle reading "process=''" would look like a bug in the logger rather than a failed
        // lookup, and this line exists to be read by someone diagnosing at a distance.
        Assert.Contains("(unknown)", RunningAccount.Describe(RunningAccount.Evaluate("", "Miro", false)));
    }
}
