using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Which private Age of Empires III keys the launcher's own uninstall takes with it
/// (<see cref="PrivateKeySweep"/>): only LEFTOVERS, keys whose install is provably gone.
///
/// <para><b>The refusals are the point.</b> A key removed while something still uses it is a mod
/// that opens straight into the 25-character product-key dialog; a key kept is a few bytes nobody
/// reads. So every doubt keeps the key — a folder that exists, one that cannot be read, a drive
/// that is not plugged in, a manifest that still names it, licence values the launcher did not
/// write, a name the helper would refuse.</para>
/// </summary>
public class PrivateKeySweepTests
{
    private const string Root = @"Software\Microsoft\Microsoft Games";
    private static readonly string SoI = $@"{Root}\Struggle of Indonesia\1.0";
    private static readonly string SoI2 = $@"{Root}\Struggle of Indonesia #2\1.0";
    private const string GoneFolder = @"D:\Games\Struggle of Indonesia";

    private static SetupPathPatcher.KeyFacts Facts(string? setup = GoneFolder, bool licence = true)
        => new(setup, licence);

    private static IReadOnlyList<string> Select(
        IEnumerable<string> candidates,
        Func<string, SetupPathPatcher.KeyFacts?> facts,
        PrivateKeySweep.PathState probe = PrivateKeySweep.PathState.Missing,
        params string[] live)
        => PrivateKeySweep.SelectOrphans(candidates, facts, _ => probe, () => live);

    [Fact]
    public void ADeadCloneIsALeftover()
    {
        Assert.Equal(new[] { SoI }, Select(new[] { SoI }, _ => Facts()));
    }

    /// <summary>THE ONE THAT MATTERS: the folder is there, so the install might be — kept.</summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_AKeyWhoseFolderStillExistsIsKept()
    {
        Assert.Empty(Select(new[] { SoI }, _ => Facts(), PrivateKeySweep.PathState.Exists));
    }

    /// <summary>"Access denied" is not "not there": another Windows account's live install, or a
    /// folder this account cannot read, keeps its key.</summary>
    [Fact]
    public void AFolderThatCannotBeReadIsKept()
    {
        Assert.Empty(Select(new[] { SoI }, _ => Facts(), PrivateKeySweep.PathState.Unknown));
    }

    [Fact]
    public void AKeyALiveManifestNamesIsKept()
    {
        Assert.Empty(Select(new[] { SoI }, _ => Facts(), PrivateKeySweep.PathState.Missing, SoI.ToUpperInvariant()));
    }

    [Fact]
    public void AKeyWithLicenceValuesTheLauncherDidNotWriteIsKept()
    {
        Assert.Empty(Select(new[] { SoI }, _ => Facts(licence: false)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"Games\Struggle of Indonesia")]   // not an absolute path
    public void AKeyWithoutAnAbsoluteSetupPathIsKept(string? setup)
    {
        Assert.Empty(Select(new[] { SoI }, _ => Facts(setup)));
    }

    [Fact]
    public void AKeyThatDoesNotExistIsNotALeftover()
    {
        Assert.Empty(Select(new[] { SoI }, _ => null));
    }

    /// <summary>Whatever the registry says about them, the player's own game keys and anything
    /// outside the AoE3 product root are never even considered.</summary>
    [Theory]
    [InlineData(@"Software\Microsoft\Microsoft Games\Age of Empires 3 Expansion Pack 2\1.0")]
    [InlineData(@"Software\Microsoft\Microsoft Games\Age of Empires 3\1.0")]
    [InlineData(@"Software\Microsoft\Windows\CurrentVersion\Run")]
    [InlineData(@"Software\Microsoft\Microsoft Games\Struggle of Indonesia\1.0\Extra")]
    [InlineData(@"Software\Microsoft\Microsoft Games\..\1.0")]
    public void ANameTheHelperWouldRefuseIsNeverALeftover(string key)
    {
        Assert.Empty(Select(new[] { key }, _ => Facts()));
    }

    /// <summary>Manifests are only read when something already looks like a leftover — a Wars of
    /// Liberty manifest is tens of MB, and with no such mod installed there is nothing to read.</summary>
    [Fact]
    public void TheManifestsAreReadOnlyWhenSomethingLooksLikeALeftover()
    {
        var reads = 0;
        IEnumerable<string> Live() { reads++; return Array.Empty<string>(); }

        PrivateKeySweep.SelectOrphans(new[] { SoI }, _ => null, _ => PrivateKeySweep.PathState.Missing, Live);
        Assert.Equal(0, reads);

        PrivateKeySweep.SelectOrphans(new[] { SoI }, _ => Facts(), _ => PrivateKeySweep.PathState.Missing, Live);
        Assert.Equal(1, reads);
    }

    [Fact]
    public void DuplicatesCountOnce_AndOneUninstallTakesAtMostTheHelpersCap()
    {
        Assert.Equal(new[] { SoI }, Select(new[] { SoI, SoI.ToUpperInvariant(), " " + SoI }, _ => Facts()));

        var many = Enumerable.Range(1, 30).Select(i => $@"{Root}\Leftover {i}\1.0").ToList();
        Assert.Equal(PrivateKeyHelper.MaxKeysPerRemoval, Select(many, _ => Facts()).Count);
    }

    /// <summary>
    /// The candidates come from the SAME list the install picks keys from, so a key named one way
    /// is never looked for another: every name <c>ChooseKey</c> would try, in its order.
    /// </summary>
    [Theory]
    [InlineData("Struggle of Indonesia")]
    [InlineData("Age of Dynasties (Remake)")]
    [InlineData("A thirty-three character mod nam")]    // 32: the suffixes shorten the stem
    public void CandidateKeysAreExactlyWhatChooseKeyWalks(string name)
    {
        var asked = new List<string>();
        Assert.Throws<SetupPathPatchException>(() => SetupPathPatcher.ChooseKey(name, k => { asked.Add(k); return true; }));
        Assert.Equal(asked, SetupPathPatcher.CandidateKeys(name).ToList());
        Assert.Equal(9, asked.Count);           // the name, then " #2" … " #9"
        Assert.All(asked, k => Assert.True(PrivateKeyHelper.IsAllowedKey(k), k));
    }

    [Fact]
    public void OnlyModsWithAPrivateKeyContributeCandidates()
    {
        var keys = PrivateKeySweep.CandidateKeysFor(new[]
        {
            new ModProfile { Id = "soi", DisplayName = "Struggle of Indonesia", PrivateSetupPath = true },
            new ModProfile { Id = "wol", DisplayName = "Wars of Liberty" },
            new ModProfile { Id = "aoe3-tad", DisplayName = "Age of Empires III", IsStockGame = true, PrivateSetupPath = true },
        });

        Assert.Equal(9, keys.Count);
        Assert.Contains(SoI, keys);
        Assert.Contains(SoI2, keys);
        Assert.DoesNotContain(keys, k => k.Contains("Wars of Liberty") || k.Contains("Age of Empires III"));
    }

    // ---------------- the folder probe ----------------

    private static FileAttributes Throws<T>(string _) where T : Exception, new() => throw new T();

    [Fact]
    public void ProbeSaysMissingOnlyWhenTheDiskAnswersNotFound()
    {
        Assert.Equal(PrivateKeySweep.PathState.Missing,
            PrivateKeySweep.ProbePath(GoneFolder, _ => true, Throws<DirectoryNotFoundException>));
        Assert.Equal(PrivateKeySweep.PathState.Missing,
            PrivateKeySweep.ProbePath(GoneFolder, _ => true, Throws<FileNotFoundException>));
        Assert.Equal(PrivateKeySweep.PathState.Exists,
            PrivateKeySweep.ProbePath(GoneFolder, _ => true, _ => FileAttributes.Directory));
    }

    [Fact]
    public void ProbeSaysUnknownForEverythingElse()
    {
        Assert.Equal(PrivateKeySweep.PathState.Unknown,                       // access denied
            PrivateKeySweep.ProbePath(GoneFolder, _ => true, Throws<UnauthorizedAccessException>));
        Assert.Equal(PrivateKeySweep.PathState.Unknown,                       // a drive not plugged in
            PrivateKeySweep.ProbePath(GoneFolder, _ => false, Throws<DirectoryNotFoundException>));
        Assert.Equal(PrivateKeySweep.PathState.Unknown,                       // a FILE where the folder was
            PrivateKeySweep.ProbePath(GoneFolder, _ => true, _ => FileAttributes.Archive));
        Assert.Equal(PrivateKeySweep.PathState.Unknown,
            PrivateKeySweep.ProbePath(@"Games\Struggle of Indonesia", _ => true, Throws<DirectoryNotFoundException>));
        Assert.Equal(PrivateKeySweep.PathState.Unknown,
            PrivateKeySweep.ProbePath(null, _ => true, Throws<DirectoryNotFoundException>));
    }

    [Fact]
    public void ProbeAgainstTheRealDisk()
    {
        var dir = Directory.CreateTempSubdirectory("aoe3ml-probe-").FullName;
        try
        {
            Assert.Equal(PrivateKeySweep.PathState.Exists, PrivateKeySweep.ProbePath(dir));
            Assert.Equal(PrivateKeySweep.PathState.Missing, PrivateKeySweep.ProbePath(Path.Combine(dir, "gone")));
            Assert.Equal(PrivateKeySweep.PathState.Missing, PrivateKeySweep.ProbePath(Path.Combine(dir, "gone", "deeper")));

            var free = Enumerable.Range('D', 23).Select(c => (char)c)
                .FirstOrDefault(c => !Directory.Exists($"{c}:\\"));
            if (free != default)
                Assert.Equal(PrivateKeySweep.PathState.Unknown, PrivateKeySweep.ProbePath($@"{free}:\Games\Mod"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ---------------- the licence veto ----------------

    [Fact]
    public void TheLicenceMatchesOnlyWhenEveryValueIsTheSame()
    {
        var original = new Dictionary<string, object?>
        {
            ["pid"] = "12345-678",
            ["digitalproductid"] = new byte[] { 1, 2, 3 },
            ["doublehash"] = new byte[] { 9, 9 },
        };
        Func<string, object?> Of(Dictionary<string, object?> d) => n => d.TryGetValue(n, out var v) ? v : null;

        var clone = new Dictionary<string, object?>(original) { ["digitalproductid"] = new byte[] { 1, 2, 3 } };
        Assert.True(SetupPathPatcher.LicenceValuesMatch(Of(original), Of(clone)));

        var rekeyed = new Dictionary<string, object?>(original) { ["digitalproductid"] = new byte[] { 1, 2, 4 } };
        Assert.False(SetupPathPatcher.LicenceValuesMatch(Of(original), Of(rekeyed)));

        var otherPid = new Dictionary<string, object?>(original) { ["pid"] = "99999-000" };
        Assert.False(SetupPathPatcher.LicenceValuesMatch(Of(original), Of(otherPid)));

        var missing = new Dictionary<string, object?>(original);
        missing.Remove("doublehash");
        Assert.False(SetupPathPatcher.LicenceValuesMatch(Of(original), Of(missing)));

        // Both without a value is a match: nothing to disagree about.
        Assert.True(SetupPathPatcher.LicenceValuesMatch(Of(missing), Of(missing)));
    }
}
