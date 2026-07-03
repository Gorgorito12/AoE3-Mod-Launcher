using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins the follow-latest rules for GitHubReleases mods. Two invariants are
/// load-bearing: (1) the effective-tag rule is the SINGLE place deciding which
/// tag a default install/update targets — follow-latest reads the cached
/// latest (written by CheckAsync), external hosting always resolves to the
/// approved tag (its catalog-pinned SHA-256 covers only that tag, and
/// ResolveAssetAsync's external branch throws on any other); (2) the catalog
/// projection keeps requiring approvedReleaseTag even with followLatest —
/// it's the only tag installable with no network/cached state.
/// </summary>
public class GitHubFollowLatestTests
{
    private static GitHubReleasesSettings Gh(
        bool follow, string approved = "v1", string external = "")
        => new()
        {
            SourceRepo = "owner/repo",
            ApprovedReleaseTag = approved,
            FollowLatest = follow,
            ExternalAssetUrlTemplate = external,
        };

    // ---- ResolveEffectiveGitHubTag --------------------------------------

    [Fact]
    public void EffectiveTag_FollowOff_ReturnsApproved_IgnoringCache()
        => Assert.Equal("v1", UpdateService.ResolveEffectiveGitHubTag(Gh(follow: false), "v2"));

    [Fact]
    public void EffectiveTag_FollowOn_WithCachedLatest_ReturnsCache()
        => Assert.Equal("v2", UpdateService.ResolveEffectiveGitHubTag(Gh(follow: true), "v2"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EffectiveTag_FollowOn_NoCachedLatest_FallsBackToApproved(string? cached)
        => Assert.Equal("v1", UpdateService.ResolveEffectiveGitHubTag(Gh(follow: true), cached));

    [Fact]
    public void EffectiveTag_ExternalHosted_AlwaysApproved()
        => Assert.Equal("v1", UpdateService.ResolveEffectiveGitHubTag(
            Gh(follow: true, external: "https://cdn.example.com/{tag}.zip"), "v2"));

    [Fact]
    public void EffectiveTag_NullSettings_ReturnsEmpty()
        => Assert.Equal("", UpdateService.ResolveEffectiveGitHubTag(null, "v2"));

    // ---- catalog projection ----------------------------------------------

    private static ModCatalogEntry Entry(string? approvedTag, bool? followLatest)
        => new()
        {
            Manifest = new ModCatalogManifest
            {
                Id = "test-mod",
                DisplayName = "Test Mod",
                SourceRepo = "owner/repo",
                ApprovedReleaseTag = approvedTag,
                Install = new ModCatalogInstall { Type = "IsolatedFolder" },
                Update = new ModCatalogUpdate
                {
                    Mechanism = "GitHubReleases",
                    Github = followLatest == null
                        ? null
                        : new ModCatalogGitHubSettings { FollowLatest = followLatest },
                },
            },
        };

    [Fact]
    public void ProjectToProfile_MapsFollowLatest()
    {
        var profile = ModRegistry.ProjectToProfile(Entry("v1", followLatest: true));
        Assert.NotNull(profile.GitHubReleases);
        Assert.True(profile.GitHubReleases!.FollowLatest);
        Assert.Equal("v1", profile.GitHubReleases.ApprovedReleaseTag);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public void ProjectToProfile_FollowLatestDefaultsFalse(bool? flag)
    {
        var profile = ModRegistry.ProjectToProfile(Entry("v1", flag));
        Assert.NotNull(profile.GitHubReleases);
        Assert.False(profile.GitHubReleases!.FollowLatest);
    }

    [Fact]
    public void ProjectToProfile_MissingApprovedTag_LeavesGitHubNull()
    {
        // followLatest does NOT relax the seed requirement: with no approved
        // tag there is nothing installable offline and no API-failure fallback.
        var profile = ModRegistry.ProjectToProfile(Entry(approvedTag: null, followLatest: true));
        Assert.Null(profile.GitHubReleases);
    }
}
