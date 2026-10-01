using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// <see cref="TranslationNotificationPlanner"/>: "when the translator publishes a new version, it
/// shows up" includes the bell. Every VERSION is keyed, and the two traps are pinned here: the
/// flood on the first look, and a partial look declaring the baseline done.
/// </summary>
public class TranslationNotificationPlannerTests
{
    private static TranslationIndexEntry Card(string source, string id, params string[] hashes) => new()
    {
        Id = id, Name = id, SourceKey = source,
        ContentHash = hashes.FirstOrDefault() ?? "",
        Versions = hashes.Select(h => new TranslationVersion { Version = "v-" + h, ContentHash = h }).ToList(),
    };

    private static readonly TranslationIndexEntry Official = Card("gh:official", "ES-LA", "aaaa000000000002", "aaaa000000000001");
    private static readonly TranslationIndexEntry Juan = Card("url:juan", "ES-LA", "bbbb000000000001");

    [Fact]
    public void EveryVersionHasAKey_NotJustEachCardsNewest()
    {
        var keys = TranslationNotificationPlanner.KeysOf(new[] { Official, Juan }).Select(k => k.Key);
        Assert.Equal(new[] { "ES-LA@aaaa000000000002", "ES-LA@aaaa000000000001", "ES-LA@bbbb000000000001" }, keys);
    }

    [Fact]
    public void AnEntryWithNoVersionList_IsKeyedAsAWhole()
    {
        var release = new TranslationIndexEntry { Id = "es", ReleaseTag = "es-v1.1" };
        var key = Assert.Single(TranslationNotificationPlanner.KeysOf(new[] { release }));
        Assert.Equal("es-v1.1", key.Key);
        Assert.Null(key.Version);
    }

    /// <summary>The same pack listed by two sources is one key, so it can only bell once.</summary>
    [Fact]
    public void TheSamePackFromTwoSources_IsOneKey()
    {
        var mirror = Card("url:mirror", "ES-LA", "aaaa000000000002");
        Assert.Equal(2, TranslationNotificationPlanner.KeysOf(new[] { Official, mirror }).Count);
    }

    /// <summary>Trap 1: the first look records everything silently instead of ringing for old versions.</summary>
    [Fact]
    public void TheFirstCompleteLook_SeedsSilentlyAndSetsTheBaseline()
    {
        var keys = TranslationNotificationPlanner.KeysOf(new[] { Official, Juan });
        var plan = TranslationNotificationPlanner.Decide(Array.Empty<string>(), baselineSeeded: false, lookIsComplete: true, keys);

        Assert.Empty(plan.Bell);
        Assert.Equal(3, plan.SeedSilently.Count);
        Assert.True(plan.MarkBaselineSeeded);
    }

    /// <summary>
    /// Trap 2: the sweep only sees the sources the player added. It may record what it saw, but it
    /// must not declare the baseline done — or the mod's own older versions ring later.
    /// </summary>
    [Fact]
    public void APartialLook_SeedsButDoesNotSetTheBaseline()
    {
        var plan = TranslationNotificationPlanner.Decide(Array.Empty<string>(), baselineSeeded: false, lookIsComplete: false,
            TranslationNotificationPlanner.KeysOf(new[] { Juan }));

        Assert.Empty(plan.Bell);
        Assert.Single(plan.SeedSilently);
        Assert.False(plan.MarkBaselineSeeded);
    }

    /// <summary>A translator the player follows publishes a new version: exactly one bell.</summary>
    [Fact]
    public void ANewVersionFromAnAddedSource_RingsExactlyOnce()
    {
        var known = TranslationNotificationPlanner.KeysOf(new[] { Official, Juan }).Select(k => k.Key).ToList();
        var juanNext = Card("url:juan", "ES-LA", "bbbb000000000002", "bbbb000000000001");

        var plan = TranslationNotificationPlanner.Decide(known, baselineSeeded: true, lookIsComplete: true,
            TranslationNotificationPlanner.KeysOf(new[] { Official, juanNext }));

        var bell = Assert.Single(plan.Bell);
        Assert.Equal("ES-LA@bbbb000000000002", bell.Key);
        Assert.Equal("url:juan", bell.Entry.SourceKey);
        Assert.Empty(plan.SeedSilently);
        Assert.False(plan.MarkBaselineSeeded);
    }

    [Fact]
    public void NothingNew_RingsNothing()
    {
        var keys = TranslationNotificationPlanner.KeysOf(new[] { Official });
        var plan = TranslationNotificationPlanner.Decide(keys.Select(k => k.Key.ToUpperInvariant()), true, true, keys);
        Assert.Empty(plan.Bell);
    }

    /// <summary>
    /// The central feed notifies with <c>id@contentHash</c>; a key it already recorded must not
    /// ring again from the per-version path.
    /// </summary>
    [Fact]
    public void AKeyTheFeedAlreadyRecorded_DoesNotRingAgain()
    {
        var feedKey = TranslationCompat.KeyOf(Official);   // what the feed path records
        var plan = TranslationNotificationPlanner.Decide(new List<string> { feedKey, "ES-LA@aaaa000000000001" }, true, true,
            TranslationNotificationPlanner.KeysOf(new[] { Official }));
        Assert.Empty(plan.Bell);
    }
}
