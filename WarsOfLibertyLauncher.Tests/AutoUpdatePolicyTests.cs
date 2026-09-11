using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Whether a launch may replace the launcher with a newer release and restart into it,
/// unattended, before the main window opens.
///
/// <para><b>Every case here but one is a REFUSAL, and that is the point.</b> The feature
/// itself is three lines of orchestration; what makes it safe to ship is that it declines in
/// the situations where applying an update silently would be destructive, and each of those
/// is a failure nobody would see happening.</para>
/// </summary>
public class AutoUpdatePolicyTests
{
    private const string OurExe = @"C:\Users\p\AppData\Local\Programs\Aoe3ModLauncher\Aoe3ModLauncher.exe";

    private static AutoUpdateDecision Decide(
        bool updateAvailable = true,
        string? downloadUrl = "https://example.invalid/Aoe3ModLauncher.exe",
        string? remoteTag = "v1.0.15",
        bool checkUpdatesOnStartup = true,
        bool explicitTask = false,
        bool bypassed = false,
        string? processPath = OurExe,
        string? attemptTag = "",
        int attemptCount = 0,
        bool enoughDisk = true)
        => AutoUpdatePolicy.Decide(
            updateAvailable, downloadUrl, remoteTag, checkUpdatesOnStartup, explicitTask,
            bypassed, processPath, attemptTag, attemptCount, enoughDisk);

    // ------------------------------------------------------------------ the one Apply

    /// <summary>An ordinary release on an ordinary machine installs itself.</summary>
    [Fact]
    public void AnOrdinaryReleaseIsAppliedUnattended()
        => Assert.Equal(AutoUpdateDecision.Apply, Decide());

    // ------------------------------------------------------------------ the refusals

    /// <summary>
    /// THE ONE THAT MATTERS. We restarted for this tag and it is STILL being offered — the
    /// release's binary reports an older version than the tag it ships under (published
    /// without <c>-Version</c>), or the wrong asset is attached. Without this the launcher
    /// re-downloads ~165 MB and restarts on every single launch, for ever, and the only
    /// symptom the user sees is a launcher that keeps closing itself.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_ARelaunchThatDidNotStickNeverLoops()
        => Assert.Equal(
            AutoUpdateDecision.AttemptsExhausted,
            Decide(remoteTag: "v1.0.15", attemptTag: "v1.0.15",
                   attemptCount: AutoUpdatePolicy.MaxAttemptsPerTag));

    /// <summary>
    /// One retry, though. A download that died on a dropped connection is not a broken
    /// release, and giving up on the first stumble would strand people on an old build.
    /// </summary>
    [Fact]
    public void OneRetryIsAllowedBeforeGivingUpOnATag()
        => Assert.Equal(
            AutoUpdateDecision.Apply,
            Decide(remoteTag: "v1.0.15", attemptTag: "v1.0.15", attemptCount: 1));

    /// <summary>A different tag is a different question — the latch does not carry over.</summary>
    [Fact]
    public void ANewerTagStartsTheCountOver()
    {
        Assert.Equal(
            AutoUpdateDecision.Apply,
            Decide(remoteTag: "v1.0.16", attemptTag: "v1.0.15",
                   attemptCount: AutoUpdatePolicy.MaxAttemptsPerTag));
        Assert.Equal(1, AutoUpdatePolicy.NextAttemptCount("v1.0.16", "v1.0.15", 9));
        Assert.Equal(2, AutoUpdatePolicy.NextAttemptCount("v1.0.15", "v1.0.15", 1));
    }

    /// <summary>
    /// The tag comparison ignores case but nothing else: a latch that failed to recognise its
    /// own tag would wave the loop straight through.
    /// </summary>
    [Fact]
    public void TheLatchRecognisesItsOwnTagRegardlessOfCase()
        => Assert.Equal(
            AutoUpdateDecision.AttemptsExhausted,
            Decide(remoteTag: "V1.0.15", attemptTag: "v1.0.15",
                   attemptCount: AutoUpdatePolicy.MaxAttemptsPerTag));

    /// <summary>
    /// THE OTHER ONE THAT MATTERS. The swap renames <c>Environment.ProcessPath</c>, and the
    /// smoke test this repository documents is <c>dotnet bin/Release/.../Aoe3ModLauncher.dll</c>
    /// — where that path is the .NET HOST. Unattended, without this refusal, every Release
    /// smoke test would rename the machine's <c>dotnet.exe</c> aside and drop the launcher in
    /// its place.
    /// </summary>
    [Theory]
    [InlineData(@"C:\Program Files\dotnet\dotnet.exe")]   // the documented smoke test
    [InlineData(@"C:\tools\Aoe3ModLauncher.old")]         // a leftover from a previous swap
    [InlineData(@"C:\tools\launcher.exe")]                // somebody renamed it
    [InlineData("")]                                      // ProcessPath unavailable
    [InlineData(null)]
    public void TheDotnetHostAndAnythingElseIsNeverOverwritten(string? processPath)
        => Assert.Equal(AutoUpdateDecision.NotOurExecutable, Decide(processPath: processPath));

    /// <summary>...and the name is the whole test, wherever the file happens to live.</summary>
    [Fact]
    public void OurOwnExeIsRecognisedByNameNotByLocation()
    {
        Assert.True(AutoUpdatePolicy.IsOurExecutable(@"D:\portable\Aoe3ModLauncher.exe"));
        Assert.True(AutoUpdatePolicy.IsOurExecutable(@"C:\x\AOE3MODLAUNCHER.EXE"));
        Assert.False(AutoUpdatePolicy.IsOurExecutable(@"C:\x\Aoe3ModLauncher.dll"));
    }

    /// <summary>
    /// "Check for updates on startup" off means metered — stay off the network. It is the only
    /// opt-out this feature has (it ships with no setting of its own), so it is checked FIRST
    /// and beats everything, including a pending release.
    /// </summary>
    [Fact]
    public void AMeteredUserIsNeverDownloadedTo()
    {
        Assert.Equal(
            AutoUpdateDecision.StartupChecksOff,
            Decide(checkUpdatesOnStartup: false));

        // First, not merely present: it outranks every other refusal.
        Assert.Equal(
            AutoUpdateDecision.StartupChecksOff,
            AutoUpdatePolicy.DecideBeforeCheck(
                checkUpdatesOnStartup: false, explicitTask: true, bypassed: true,
                processPath: @"C:\Program Files\dotnet\dotnet.exe"));
    }

    /// <summary>
    /// An elevated relaunch that exists to apply a MOD update carries its job in its arguments.
    /// Restarting out from under it would lose <c>--update-now</c>, leave the pending list
    /// empty, and make the elevated update the user just approved silently do nothing.
    /// </summary>
    [Fact]
    public void AnElevatedModUpdateIsNotHijacked()
        => Assert.Equal(AutoUpdateDecision.ExplicitTask, Decide(explicitTask: true));

    /// <summary>
    /// The maintainer. A locally published build calls itself <c>v1.0.14</c> — the letter only
    /// exists as an argument to <c>publish.ps1</c> — so against a released <c>v1.0.14d</c> it
    /// would replace itself with the published build on every launch, deleting the very thing
    /// being tested. Same bypass as the multiplayer gate.
    /// </summary>
    [Fact]
    public void TheMaintainersBuildIsNeverReplaced()
        => Assert.Equal(AutoUpdateDecision.Bypassed, Decide(bypassed: true));

    /// <summary>
    /// The manual dialog warns about low disk and lets the user continue anyway. Unattended
    /// there is nobody to ask, and filling the volume that holds the executable is not a thing
    /// to do on somebody's behalf.
    /// </summary>
    [Fact]
    public void AFullDiskFallsBackToThePill()
        => Assert.Equal(AutoUpdateDecision.NotEnoughDisk, Decide(enoughDisk: false));

    /// <summary>Nothing newer, and a release carrying no usable asset, are both left alone.</summary>
    [Fact]
    public void NothingToApplyIsNotAFailure()
    {
        Assert.Equal(AutoUpdateDecision.NotAvailable, Decide(updateAvailable: false));
        Assert.Equal(AutoUpdateDecision.NoDownloadUrl, Decide(downloadUrl: null));
        Assert.Equal(AutoUpdateDecision.NoDownloadUrl, Decide(downloadUrl: "  "));
        Assert.Equal(AutoUpdateDecision.NoDownloadUrl, Decide(remoteTag: ""));
    }

    /// <summary>
    /// The pre-check and the full decision must agree about the things both can see — they are
    /// two entry points onto one rule, and a rule that holds at only one of them is how a
    /// refusal quietly stops applying.
    /// </summary>
    [Theory]
    [InlineData(false, false, false, OurExe, AutoUpdateDecision.StartupChecksOff)]
    [InlineData(true, true, false, OurExe, AutoUpdateDecision.ExplicitTask)]
    [InlineData(true, false, true, OurExe, AutoUpdateDecision.Bypassed)]
    [InlineData(true, false, false, "dotnet.exe", AutoUpdateDecision.NotOurExecutable)]
    [InlineData(true, false, false, OurExe, AutoUpdateDecision.Apply)]
    public void ThePreCheckAndTheFullDecisionCannotDrift(
        bool checkUpdatesOnStartup, bool explicitTask, bool bypassed, string processPath,
        AutoUpdateDecision expected)
    {
        Assert.Equal(expected, AutoUpdatePolicy.DecideBeforeCheck(
            checkUpdatesOnStartup, explicitTask, bypassed, processPath));

        Assert.Equal(expected, Decide(
            checkUpdatesOnStartup: checkUpdatesOnStartup, explicitTask: explicitTask,
            bypassed: bypassed, processPath: processPath));
    }

    /// <summary>
    /// A verification failure ends the tag's unattended life at once — the same bytes would
    /// fail the same way — and the burn value has to actually reach the latch's ceiling.
    /// </summary>
    [Fact]
    public void BurningATagReachesTheCeiling()
    {
        Assert.Equal(
            AutoUpdateDecision.AttemptsExhausted,
            Decide(remoteTag: "v1.0.15", attemptTag: "v1.0.15",
                   attemptCount: AutoUpdatePolicy.BurnedAttemptCount));
    }
}
