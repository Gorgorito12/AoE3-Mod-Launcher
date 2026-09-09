using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Services;
using Xunit;

using Planner = WarsOfLibertyLauncher.Services.DeltaChainPlanner;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="DeltaChainPlanner"/> — the rule that decides how a player gets from the
/// version they have to the version they want once a modder stops shipping the full zip on every
/// release.
///
/// <para>Most of these are REJECTIONS and limits, because that is where this can do damage: a
/// route it should have refused is a wrong install, while a route it merely failed to find costs
/// only a bigger download. Its WoL counterpart, <c>ComputePendingDownloads</c>, is private and has
/// no test at all — this exists so that is not repeated.</para>
/// </summary>
public class DeltaChainPlannerTests
{
    private const long MB = 1024L * 1024;

    private static Planner.ReleaseAssetSnapshot Asset(string name, long size)
        => new(name, size, "https://example.invalid/" + name);

    private static Planner.ReleaseSnapshot Rel(
        string tag, bool prerelease = false, params Planner.ReleaseAssetSnapshot[] assets)
        => new(tag, prerelease, assets.ToList());

    /// <summary>A release carrying a full overlay zip of the given size.</summary>
    private static Planner.ReleaseSnapshot Full(string tag, long size, bool prerelease = false)
        => Rel(tag, prerelease, Asset($"mod-{tag}.zip", size));

    /// <summary>Patch assets (payload + descriptor) for a hop.</summary>
    private static Planner.ReleaseAssetSnapshot[] PatchPair(string from, string to, long size)
        => new[]
        {
            Asset($"patch-{from}-to-{to}.zip", size),
            Asset($"patch-{from}-to-{to}.json", 1024),
        };

    private static Planner.Plan? Build(
        IEnumerable<Planner.ReleaseSnapshot> releases, string? installed, string target,
        Planner.BaselinePolicy policy = Planner.BaselinePolicy.TargetOnly,
        int maxHops = Planner.MaxHops, long hopPenalty = Planner.HopPenaltyBytes)
        => Planner.Build(releases.ToList(), installed, target, policy, maxHops, hopPenalty);

    // ---------------------------------------------------------------- choosing a route

    /// <summary>
    /// The point of the cumulative patch: a player several versions behind takes ONE hop from the
    /// baseline instead of walking every incremental.
    /// </summary>
    [Fact]
    public void ACumulativePatchBeatsAChainOfIncrementals()
    {
        var releases = new[]
        {
            Full("v1", 400 * MB),
            Rel("v2", false, PatchPair("v1", "v2", 5 * MB)),
            Rel("v3", false, PatchPair("v2", "v3", 5 * MB)),
            Rel("v4", false, PatchPair("v3", "v4", 5 * MB)
                .Concat(PatchPair("v1", "v4", 12 * MB)).ToArray()),
        };

        var plan = Build(releases, "v1", "v4");

        Assert.NotNull(plan);
        var step = Assert.Single(plan!.PatchSteps);
        Assert.Equal("v1", step.FromTag);
        Assert.Equal("v4", step.ToTag);
    }

    /// <summary>And a player who is only one version behind takes the small incremental.</summary>
    [Fact]
    public void AnIncrementalBeatsTheCumulativeForSomeoneOneVersionBehind()
    {
        var releases = new[]
        {
            Full("v1", 400 * MB),
            Rel("v2", false, PatchPair("v1", "v2", 90 * MB)
                .Concat(PatchPair("v1", "v2", 90 * MB)).ToArray()),
        };
        // Simpler shape: v3 offers both an incremental from v2 and a cumulative from v1.
        releases = new[]
        {
            Full("v1", 400 * MB),
            Rel("v2", false, PatchPair("v1", "v2", 30 * MB)),
            Rel("v3", false, PatchPair("v2", "v3", 2 * MB)
                .Concat(PatchPair("v1", "v3", 60 * MB)).ToArray()),
        };

        var plan = Build(releases, "v2", "v3");

        var step = Assert.Single(plan!.PatchSteps);
        Assert.Equal("v2", step.FromTag);
        Assert.Equal(2 * MB + 1024, step.Bytes);
    }

    /// <summary>
    /// When patching costs as much as the mod, the mod wins. A full re-overlay also repairs an
    /// install that has quietly diverged, which a patch does not — so this is the safe side.
    /// </summary>
    [Fact]
    public void TheFullZipWinsWhenTheChainIsNotCheaper()
    {
        var releases = new[]
        {
            Full("v1", 100 * MB),
            Rel("v2", false, PatchPair("v1", "v2", 300 * MB)
                .Concat(new[] { Asset("mod-v2.zip", 100 * MB) }).ToArray()),
        };

        var plan = Build(releases, "v1", "v2");

        Assert.NotNull(plan);
        Assert.False(plan!.StartsFromInstalled);
        Assert.Equal(Planner.StepKind.FullBaseline, Assert.Single(plan.Steps).Kind);
    }

    /// <summary>An exact tie goes to the full download, for the same reason. Stated, not incidental.</summary>
    [Fact]
    public void AnExactTieGoesToTheFullDownload()
    {
        // Patch route = payload + descriptor + hop penalty; make the full zip exactly equal.
        long patchBytes = 10 * MB;
        long tie = patchBytes + 1024 + Planner.HopPenaltyBytes;
        var releases = new[]
        {
            Full("v1", 500 * MB),
            Rel("v2", false, PatchPair("v1", "v2", patchBytes)
                .Concat(new[] { Asset("mod-v2.zip", tie) }).ToArray()),
        };

        var plan = Build(releases, "v1", "v2");

        Assert.Equal(Planner.StepKind.FullBaseline, Assert.Single(plan!.Steps).Kind);
    }

    /// <summary>
    /// A hop costs real time regardless of its bytes — a manifest re-write that enumerates the
    /// whole install. Without the penalty the planner picks a swarm of tiny hops and the update
    /// takes far longer than the download it saved.
    /// </summary>
    [Fact]
    public void ManyTinyHopsLoseToOneLargerPatchBecauseHopsAreNotFree()
    {
        var releases = new List<Planner.ReleaseSnapshot> { Full("v1", 900 * MB) };
        for (int i = 2; i <= 5; i++)
            releases.Add(Rel($"v{i}", false, PatchPair($"v{i - 1}", $"v{i}", 1 * MB)));
        // v5 also offers a cumulative that is bigger in raw bytes than the 4 tiny hops.
        releases[^1] = Rel("v5", false,
            PatchPair("v4", "v5", 1 * MB).Concat(PatchPair("v1", "v5", 20 * MB)).ToArray());

        var plan = Build(releases, "v1", "v5");

        var step = Assert.Single(plan!.PatchSteps);
        Assert.Equal("v1", step.FromTag);
    }

    // ---------------------------------------------------------------- limits and refusals

    /// <summary>A chain longer than the cap is refused rather than walked.</summary>
    [Fact]
    public void AChainLongerThanTheCapIsRefused()
    {
        var releases = new List<Planner.ReleaseSnapshot> { Full("v1", 900 * MB) };
        for (int i = 2; i <= 9; i++)
            releases.Add(Rel($"v{i}", false, PatchPair($"v{i - 1}", $"v{i}", 1 * MB)));

        // No full zip on the target and no route within the cap → nothing to do.
        Assert.Null(Build(releases, "v1", "v9", Planner.BaselinePolicy.None, maxHops: 4));
    }

    /// <summary>
    /// THE termination guard. A modder writes fromTag/toTag by hand, so a cycle is possible; the
    /// hop-indexed table means it simply cannot loop.
    /// </summary>
    [Fact]
    public void ACycleTerminatesAndStillFindsTheRealRoute()
    {
        var releases = new[]
        {
            Full("v1", 900 * MB),
            Rel("v2", false, PatchPair("v1", "v2", 2 * MB)),
            // v1 hosts a descriptor claiming v2 -> v1: a back edge, i.e. a cycle.
            Rel("v3", false, PatchPair("v2", "v3", 2 * MB)),
        };
        var withCycle = releases.ToList();
        withCycle[0] = Rel("v1", false,
            new[] { Asset("mod-v1.zip", 900 * MB) }.Concat(PatchPair("v2", "v1", 1 * MB)).ToArray());

        var plan = Build(withCycle, "v1", "v3", Planner.BaselinePolicy.None);

        Assert.NotNull(plan);
        Assert.Equal(new[] { "v2", "v3" }, plan!.PatchSteps.Select(s => s.ToTag).ToArray());
    }

    /// <summary>
    /// A descriptor whose payload zip is missing is dropped: its cost is unknowable, so it cannot
    /// be weighed against anything.
    /// </summary>
    [Fact]
    public void ADescriptorWithNoPayloadBesideItIsIgnored()
    {
        var releases = new[]
        {
            Full("v1", 900 * MB),
            Rel("v2", false, Asset("patch-v1-to-v2.json", 1024)),
        };

        Assert.Null(Build(releases, "v1", "v2", Planner.BaselinePolicy.None));
    }

    /// <summary>
    /// A hop must land on the release hosting it, so an old release cannot inject an edge into a
    /// newer one it knows nothing about.
    /// </summary>
    [Fact]
    public void AHopHostedByTheWrongReleaseIsIgnored()
    {
        var releases = new[]
        {
            Full("v1", 900 * MB),
            // v2 hosts a descriptor claiming to produce v3.
            Rel("v2", false, PatchPair("v1", "v3", 2 * MB)),
            Full("v3", 800 * MB),
        };

        var plan = Build(releases, "v1", "v3", Planner.BaselinePolicy.None);

        Assert.Null(plan);
    }

    /// <summary>
    /// A prerelease is never an intermediate stop — a chain that dies midway must not leave
    /// somebody on a build the modder had not published.
    /// </summary>
    [Fact]
    public void APrereleaseIsNeverWalkedThrough()
    {
        var releases = new[]
        {
            Full("v1", 900 * MB),
            Rel("v2-beta", prerelease: true, PatchPair("v1", "v2-beta", 2 * MB)),
            Rel("v3", false, PatchPair("v2-beta", "v3", 2 * MB)),
        };

        Assert.Null(Build(releases, "v1", "v3", Planner.BaselinePolicy.None));
    }

    /// <summary>…but it is reachable when the user asked for it by name.</summary>
    [Fact]
    public void APrereleaseIsReachableWhenItIsTheExplicitTarget()
    {
        var releases = new[]
        {
            Full("v1", 900 * MB),
            Rel("v2-beta", prerelease: true, PatchPair("v1", "v2-beta", 2 * MB)),
        };

        var plan = Build(releases, "v1", "v2-beta", Planner.BaselinePolicy.None);

        Assert.Equal("v2-beta", Assert.Single(plan!.PatchSteps).ToTag);
    }

    /// <summary>
    /// On an update the only full download considered is the target's own zip, so the choice is
    /// exactly "patch route versus what we would download today" — never a quiet reinstall from
    /// some older release.
    /// </summary>
    [Fact]
    public void TargetOnlyRefusesACheaperNonTargetBaseline()
    {
        var releases = new[]
        {
            Full("v1", 1 * MB),                       // absurdly cheap, and not the target
            Rel("v2", false, Asset("mod-v2.zip", 900 * MB)),
        };

        var plan = Build(releases, installed: null, target: "v2", Planner.BaselinePolicy.TargetOnly);

        Assert.Equal("v2", Assert.Single(plan!.Steps).ToTag);
    }

    // ---------------------------------------------------------------- fresh install

    /// <summary>
    /// A fresh install starts from a full zip and then chains — that is what lets the newest
    /// release carry patches only.
    /// </summary>
    [Fact]
    public void AFreshInstallStartsFromABaselineAndChainsToTheTarget()
    {
        var releases = new[]
        {
            Full("v1", 400 * MB),
            Rel("v2", false, PatchPair("v1", "v2", 5 * MB)),
        };

        var plan = Build(releases, installed: null, target: "v2", Planner.BaselinePolicy.Any);

        Assert.NotNull(plan);
        Assert.False(plan!.StartsFromInstalled);
        Assert.Equal(Planner.StepKind.FullBaseline, plan.Steps[0].Kind);
        Assert.Equal("v1", plan.Steps[0].ToTag);
        Assert.Equal("v2", plan.Steps[^1].ToTag);
    }

    /// <summary>
    /// It picks the baseline that minimises baseline + chain, which is not always the newest one
    /// that has a zip.
    /// </summary>
    [Fact]
    public void AFreshInstallPicksTheCheapestBaselineNotTheNewestOne()
    {
        var releases = new[]
        {
            Full("v1", 100 * MB),                                  // small, one hop away
            Rel("v2", false, new[] { Asset("mod-v2.zip", 900 * MB) }
                .Concat(PatchPair("v1", "v2", 5 * MB)).ToArray()), // newer, but huge
        };

        var plan = Build(releases, installed: null, target: "v2", Planner.BaselinePolicy.Any);

        Assert.Equal("v1", plan!.Steps[0].ToTag);
        Assert.Equal(Planner.StepKind.Patch, plan.Steps[^1].Kind);
    }

    /// <summary>
    /// An installed tag the repo has never heard of (a hand-copied install, a deleted release)
    /// behaves like a fresh install rather than producing a route from nowhere.
    /// </summary>
    [Fact]
    public void AnUnknownInstalledTagBehavesLikeAFreshInstall()
    {
        var releases = new[]
        {
            Full("v1", 400 * MB),
            Rel("v2", false, PatchPair("v1", "v2", 5 * MB)),
        };

        var plan = Build(releases, "who-knows", "v2", Planner.BaselinePolicy.Any);

        Assert.Equal(Planner.StepKind.FullBaseline, plan!.Steps[0].Kind);
    }

    // ---------------------------------------------------------------- degenerate cases

    /// <summary>Already at the target is an empty plan, which is not the same as no route.</summary>
    [Fact]
    public void AlreadyAtTheTargetIsAnEmptyPlanNotAFailure()
    {
        var releases = new[] { Full("v1", 400 * MB) };

        var plan = Build(releases, "v1", "v1");

        Assert.NotNull(plan);
        Assert.Empty(plan!.Steps);
        Assert.True(plan.StartsFromInstalled);
    }

    /// <summary>No patches and no zip on the target: nothing to propose.</summary>
    [Fact]
    public void NoRouteYieldsNull()
    {
        var releases = new[]
        {
            Full("v1", 400 * MB),
            Rel("v2", false, Asset("notes.txt", 10)),
        };

        Assert.Null(Build(releases, "v1", "v2"));
    }

    /// <summary>A target the repo does not have cannot be planned for.</summary>
    [Fact]
    public void AnUnknownTargetYieldsNull()
    {
        Assert.Null(Build(new[] { Full("v1", 400 * MB) }, "v1", "v9"));
    }

    /// <summary>
    /// The plan must not depend on the order GitHub happened to list releases or assets in.
    /// </summary>
    [Fact]
    public void TheResultIsIndependentOfInputOrder()
    {
        var releases = new List<Planner.ReleaseSnapshot>
        {
            Full("v1", 400 * MB),
            Rel("v2", false, PatchPair("v1", "v2", 5 * MB)),
            Rel("v3", false, PatchPair("v2", "v3", 5 * MB)
                .Concat(PatchPair("v1", "v3", 8 * MB)).ToArray()),
        };

        var forward = Build(releases, "v1", "v3", Planner.BaselinePolicy.Any);
        var reversed = Build(Enumerable.Reverse(releases), "v1", "v3", Planner.BaselinePolicy.Any);

        Assert.Equal(forward!.TotalBytes, reversed!.TotalBytes);
        Assert.Equal(forward.Steps.Select(s => s.ToTag), reversed.Steps.Select(s => s.ToTag));
    }

    // ---------------------------------------------------------------- the rescue route

    /// <summary>
    /// THE case this rescue exists for. The target release ships only patches, and the player
    /// cannot be reached by chaining — a DETECTED install whose version the launcher never knew,
    /// someone further behind than the hop cap, or someone whose tag fell off the release listing.
    /// Asking with no installed tag forces a route that STARTS at a baseline, which is always
    /// possible while one exists. Without it the update simply fails: the ordinary full path wants
    /// the target's own zip and there is not one.
    /// </summary>
    [Fact]
    public void APatchOnlyTargetIsStillReachableFromTheBaseline()
    {
        var releases = new[]
        {
            Full("v1", 400 * MB),
            Rel("v2", false, PatchPair("v1", "v2", 5 * MB)),
        };

        var rescue = Build(releases, installed: null, target: "v2", Planner.BaselinePolicy.Any);

        Assert.NotNull(rescue);
        Assert.Equal(Planner.StepKind.FullBaseline, rescue!.Steps[0].Kind);
        Assert.Equal("v1", rescue.Steps[0].ToTag);
        Assert.Equal("v2", rescue.Steps[^1].ToTag);
    }

    /// <summary>
    /// Asking with no installed tag must not be satisfied by "you are already there" — the whole
    /// point is to lay the mod down again from a baseline.
    /// </summary>
    [Fact]
    public void TheRescueAlwaysStartsByDownloadingSomething()
    {
        var releases = new[]
        {
            Full("v1", 400 * MB),
            Rel("v2", false, PatchPair("v1", "v2", 5 * MB)),
            Rel("v3", false, PatchPair("v2", "v3", 5 * MB)),
        };

        var rescue = Build(releases, installed: null, target: "v3", Planner.BaselinePolicy.Any);

        Assert.False(rescue!.StartsFromInstalled);
        Assert.Equal(Planner.StepKind.FullBaseline, rescue.Steps[0].Kind);
    }

    /// <summary>
    /// The rejection that drives the user-facing message: no release anywhere carries a full zip,
    /// so the MOD has no complete version published and nothing the player does can fix it. The
    /// caller must say that rather than leak "No asset matching ''".
    /// </summary>
    [Fact]
    public void WithNoBaselineAnywhereThereIsNoRescue()
    {
        var releases = new[]
        {
            Rel("v1", false, Asset("readme.txt", 10)),
            Rel("v2", false, PatchPair("v1", "v2", 5 * MB)),
        };

        Assert.Null(Build(releases, installed: null, target: "v2", Planner.BaselinePolicy.Any));
    }

    /// <summary>
    /// A cumulative patch is what keeps the rescue at one hop however many releases have gone by —
    /// which is also why the hop cap does not need raising.
    /// </summary>
    [Fact]
    public void ACumulativeKeepsTheRescueAtASingleHop()
    {
        var releases = new List<Planner.ReleaseSnapshot> { Full("v1", 400 * MB) };
        for (int i = 2; i <= 8; i++)
            releases.Add(Rel($"v{i}", false, PatchPair($"v{i - 1}", $"v{i}", 2 * MB)));
        // v8 also ships the cumulative from the baseline.
        releases[^1] = Rel("v8", false,
            PatchPair("v7", "v8", 2 * MB).Concat(PatchPair("v1", "v8", 25 * MB)).ToArray());

        var rescue = Build(releases, installed: null, target: "v8", Planner.BaselinePolicy.Any);

        Assert.NotNull(rescue);
        var hop = Assert.Single(rescue!.PatchSteps);
        Assert.Equal("v1", hop.FromTag);
        Assert.Equal("v8", hop.ToTag);
    }

    /// <summary>
    /// Without a cumulative, a mod that never re-baselines eventually puts the target out of reach
    /// of the cap — the honest failure that the guide's "always ship the cumulative" rule prevents.
    /// </summary>
    [Fact]
    public void WithoutACumulativeADistantTargetFallsOutOfReach()
    {
        var releases = new List<Planner.ReleaseSnapshot> { Full("v1", 400 * MB) };
        for (int i = 2; i <= 8; i++)
            releases.Add(Rel($"v{i}", false, PatchPair($"v{i - 1}", $"v{i}", 2 * MB)));

        Assert.Null(Build(releases, installed: null, target: "v8", Planner.BaselinePolicy.Any));
    }

    // ---------------------------------------------------------------- re-baseline advice

    /// <summary>
    /// The boundary is pinned rather than argued about: exactly half is still worth shipping, a
    /// byte over is not, and an unknown full size never advises anything.
    /// </summary>
    [Fact]
    public void RebaselineAdviceTriggersOnlyPastHalfTheFullZip()
    {
        Assert.False(DeltaPatchService.ShouldAdviseRebaseline(50, 100));   // exactly half
        Assert.True(DeltaPatchService.ShouldAdviseRebaseline(51, 100));
        Assert.False(DeltaPatchService.ShouldAdviseRebaseline(10, 100));
        Assert.False(DeltaPatchService.ShouldAdviseRebaseline(10, 0));     // size unknown
    }

    /// <summary>Each patch step carries its host release's assets, so preparing it costs no API call.</summary>
    [Fact]
    public void EachPatchStepCarriesItsHostReleaseAssets()
    {
        var releases = new[]
        {
            Full("v1", 400 * MB),
            Rel("v2", false, PatchPair("v1", "v2", 5 * MB)),
        };

        var step = Assert.Single(Build(releases, "v1", "v2", Planner.BaselinePolicy.None)!.PatchSteps);

        Assert.Contains(step.HostReleaseAssets, a => a.Name == "patch-v1-to-v2.zip");
        Assert.Equal("patch-v1-to-v2.json", step.DescriptorName);
    }
}
