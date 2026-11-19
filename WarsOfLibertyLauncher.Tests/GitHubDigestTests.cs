using System.Collections.Generic;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// GitHub's per-asset digest, read in one place. The refusals are the point: anything that is not
/// exactly <c>sha256:</c> + 64 hex reads as ABSENT — a hash compared against garbage would turn a
/// good download into "corrupt" and re-download it for ever.
/// </summary>
public class GitHubDigestTests
{
    private const string Hex = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void AWellFormedDigestIsItsLowercaseHex()
    {
        Assert.Equal(Hex, GitHubDigest.TryParseSha256("sha256:" + Hex));
        Assert.Equal(Hex, GitHubDigest.TryParseSha256("sha256:" + Hex.ToUpperInvariant()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sha1:0123456789abcdef0123456789abcdef01234567")]
    [InlineData("sha256:0123")]
    [InlineData("sha256:" + Hex + "00")]
    [InlineData("md5:" + Hex)]
    [InlineData("SHA256 " + Hex)]
    public void AnythingElseIsAbsent(string? digest) => Assert.Null(GitHubDigest.TryParseSha256(digest));

    [Fact]
    public void ABaselineWithNoDigestAnywhereIsChecked_ByItsSizeOnly()
    {
        var step = new DeltaChainPlanner.PlanStep(
            DeltaChainPlanner.StepKind.FullBaseline, "", "v2", 100, "full.zip", "u/full.zip", null, null,
            new List<DeltaChainPlanner.ReleaseAssetSnapshot> { new("full.zip", 100, "u/full.zip") });
        Assert.Null(DeltaChainPlanner.BaselineDigestsOf(step));

        var withDigest = step with
        {
            HostReleaseAssets = new List<DeltaChainPlanner.ReleaseAssetSnapshot> { new("full.zip", 100, "u/full.zip", Hex) },
        };
        Assert.Equal(new[] { Hex }, DeltaChainPlanner.BaselineDigestsOf(withDigest));
    }
}
