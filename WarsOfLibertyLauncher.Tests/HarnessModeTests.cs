using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The harness turns off auto-start, deep links and every process the launcher starts, so what
/// matters here is when it must NOT turn on: a published launcher, or the player's own folder.
/// </summary>
public class HarnessModeTests
{
    private const string Real = @"C:\Users\Someone\AppData\Local\AoE3ModLauncher";
    private const string Sandbox = @"C:\Temp\testbed\run1";

    [Fact]
    public void AllThreeConditionsTurnItOn()
        => Assert.True(HarnessMode.Decide("1", Sandbox, Real, developerBuild: true));

    [Fact]
    public void THE_ONE_THAT_MATTERS_APublishedLauncherIgnoresTheVariable()
        => Assert.False(HarnessMode.Decide("1", Sandbox, Real, developerBuild: false));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("true")]
    public void OnlyTheExactValueCounts(string? flag)
        => Assert.False(HarnessMode.Decide(flag, Sandbox, Real, developerBuild: true));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoDataFolderOfItsOwnMeansNoHarness(string? dataDir)
        => Assert.False(HarnessMode.Decide("1", dataDir, Real, developerBuild: true));

    [Theory]
    [InlineData(Real)]
    [InlineData(Real + @"\")]
    [InlineData(@"c:\users\someone\appdata\local\aoe3modlauncher")]
    [InlineData(@"C:\Users\Someone\AppData\Local\Other\..\AoE3ModLauncher")]
    public void ThePlayersOwnFolderIsRefusedHoweverItIsSpelled(string dataDir)
        => Assert.False(HarnessMode.Decide("1", dataDir, Real, developerBuild: true));

    [Fact]
    public void AnUnresolvablePathCannotBeProvedDifferent()
        => Assert.False(HarnessMode.Decide("1", "bad\0path", Real, developerBuild: true));

    [Fact]
    public void ATestRunIsNotTheHarness_SoNothingHereIsBlocked()
    {
        Assert.False(HarnessMode.Active);
        Assert.False(HarnessMode.Block("anything"));
        Assert.Equal("", HarnessMode.InstanceSuffix);
    }

    [Fact]
    public void TheSuffixFollowsTheFolderAndIgnoresHowItIsWritten()
    {
        Assert.Equal(HarnessMode.SuffixFor(Sandbox), HarnessMode.SuffixFor(Sandbox.ToLowerInvariant() + @"\"));
        Assert.NotEqual(HarnessMode.SuffixFor(Sandbox), HarnessMode.SuffixFor(Sandbox + "2"));
        Assert.Matches("^[.a-z0-9-]+$", HarnessMode.SuffixFor(Sandbox));
    }

    /// <summary>
    /// A new direct Process.Start would open a real browser, Explorer or installer from a test
    /// run, and the sweep would still read as a pass because nothing on screen says so.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_EveryProcessGoesThroughTheFunnel()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "WarsOfLibertyLauncher"));
        var call = new Regex(@"(?<![\w.])(?:System\.Diagnostics\.)?Process\.Start\(");
        // Reads the Windows event log for the diagnostics bundle: no window, no effect.
        string[] allowed = { "HarnessMode.cs", "WindowsCrashEventLog.cs" };

        var offenders = Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !allowed.Contains(Path.GetFileName(f)))
            .SelectMany(f => File.ReadLines(f)
                .Select((text, i) => (f, i, text))
                .Where(l => !l.text.TrimStart().StartsWith("//") && call.IsMatch(l.text)))
            .Select(l => $"{Path.GetFileName(l.f)}:{l.i + 1}")
            .ToList();

        Assert.True(Directory.Exists(root), root);
        Assert.Empty(offenders);
    }

    /// <summary>The refusals are the point: outside the harness saved games never move.</summary>
    [Theory]
    [InlineData(false, @"C:\\test\\Documents", null)]
    [InlineData(false, null, null)]
    [InlineData(true, null, null)]
    [InlineData(true, "  ", null)]
    [InlineData(true, @"C:\\test\\Documents", @"C:\\test\\Documents")]
    public void DocumentsMoveOnlyInsideTheHarness(bool active, string? value, string? expected)
        => Assert.Equal(expected, HarnessMode.DocumentsRootFor(active, value));

    /// <summary>The refusals are the point: a Repair may only write inside the run's sandbox.</summary>
    [Theory]
    [InlineData(true, @"C:\t\sandbox", @"C:\t\sandbox\Mod", true)]
    [InlineData(true, @"C:\t\sandbox\", @"c:\T\SANDBOX\Mod\", true)]
    [InlineData(false, @"C:\t\sandbox", @"C:\t\sandbox\Mod", false)]
    [InlineData(true, null, @"C:\t\sandbox\Mod", false)]
    [InlineData(true, @"C:\t\sandbox", null, false)]
    [InlineData(true, @"C:\t\sandbox", @"C:\t\sandbox", false)]
    [InlineData(true, @"C:\t\sandbox", @"C:\t\sandbox2\Mod", false)]
    [InlineData(true, @"C:\t\sandbox", @"C:\t\sandbox\..\real\Mod", false)]
    [InlineData(true, @"C:\t\sandbox", @"D:\Games\Mod", false)]
    public void ARepairWritesOnlyInsideTheSandbox(bool active, string? sandbox, string? path, bool expected)
        => Assert.Equal(expected, HarnessMode.IsInsideSandbox(active, sandbox, path));
}
