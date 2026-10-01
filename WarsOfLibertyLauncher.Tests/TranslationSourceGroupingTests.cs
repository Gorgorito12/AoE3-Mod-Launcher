using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// <see cref="TranslationSourceGrouping"/>: ONE CARD PER TRANSLATOR. It replaces the old merge,
/// which unioned every repository's versions under one language id and so put another
/// translator's work inside the official card. The cases that matter are the separations and the
/// refusals: two translators never become one card, and a version made for another mod never
/// reaches this one.
/// </summary>
public class TranslationSourceGroupingTests
{
    private static SourcedVersion V(string sourceKey, bool official, string id, string version,
        string hash, string date, string targetMod = "wol",
        TranslationSourceKind kind = TranslationSourceKind.GitHubFolder) =>
        new(id, "Español", id, null, new TranslationVersion
        {
            Version = version, ContentHash = hash, Date = date, TargetMod = targetMod,
            SourceKey = sourceKey, SourceLabel = sourceKey, SourceKind = kind, IsOfficial = official,
            CompatibleWith = new List<string> { "1.2.0e" },
        });

    private static SourceFetchResult Result(string key, bool official, params SourcedVersion[] versions) => new()
    {
        Source = TranslationSourceRef.Repo(key),
        IsOfficial = official,
        Reachable = true,
        Label = key,
        Versions = versions.ToList(),
    };

    /// <summary>THE ONE THAT MATTERS: the same language from two translators is two cards.</summary>
    [Fact]
    public void TwoTranslatorsWithTheSameId_AreTwoCards()
    {
        var cards = TranslationSourceGrouping.BuildForMod(new[]
        {
            Result("gh:official", true, V("gh:official", true, "ES-LA", "1.2.0e-r2", "aaaa000000000001", "2026-09-09")),
            Result("url:juan", false, V("url:juan", false, "ES-LA", "1.2.0e-r5", "bbbb000000000002", "2026-09-20",
                kind: TranslationSourceKind.Index)),
        }, "wol");

        Assert.Equal(2, cards.Count);
        Assert.Equal(new[] { "gh:official", "url:juan" }, cards.Select(c => c.SourceKey));
        Assert.True(cards[0].IsOfficial);
        Assert.False(cards[1].IsOfficial);
        Assert.NotEqual(cards[0].CardKey, cards[1].CardKey);
        // Each card holds ONLY its own translator's versions.
        Assert.All(cards[1].Versions, v => Assert.Equal("url:juan", v.SourceKey));
    }

    [Fact]
    public void OneSourcesVersionsOfAnId_AreOneCardNewestFirst()
    {
        var card = Assert.Single(TranslationSourceGrouping.ForMod(new[]
        {
            V("gh:o", true, "ES-LA", "1.2.0d-r1", "h1", "2026-05-01"),
            V("gh:o", true, "ES-LA", "1.2.0e-r2", "h3", "2026-09-09"),
            V("gh:o", true, "ES-LA", "1.2.0e-r1", "h2", "2026-07-04"),
        }, "wol"));

        Assert.Equal(new[] { "1.2.0e-r2", "1.2.0e-r1", "1.2.0d-r1" }, card.Versions.Select(v => v.Version));
        // The card's top-level fields describe the newest version.
        Assert.Equal("1.2.0e-r2", card.Version);
        Assert.Equal("h3", card.ContentHash);
    }

    /// <summary>
    /// The target-mod filter runs per VERSION, before grouping: a version for another mod never
    /// joins this mod's card, even when its id matches.
    /// </summary>
    [Fact]
    public void AVersionForAnotherMod_IsDroppedBeforeGrouping()
    {
        var card = Assert.Single(TranslationSourceGrouping.ForMod(new[]
        {
            V("url:j", false, "ES-LA", "1.0", "h1", "2026-01-01", targetMod: "wol", kind: TranslationSourceKind.Index),
            V("url:j", false, "ES-LA", "9.9", "h9", "2026-12-31", targetMod: "improvement-mod", kind: TranslationSourceKind.Index),
        }, "wol"));

        Assert.Single(card.Versions);
        Assert.Equal("1.0", card.Version);
    }

    /// <summary>
    /// An empty <c>targetMod</c> predates the field: allowed from the mod's OWN source, never from
    /// one the player added — that one has to say which mod it is for.
    /// </summary>
    [Fact]
    public void AnEmptyTargetMod_IsOnlyAcceptedFromTheOfficialSource()
    {
        Assert.Single(TranslationSourceGrouping.ForMod(new[] { V("gh:o", true, "es", "1.1", "h", "", targetMod: "") }, "wol"));
        Assert.Empty(TranslationSourceGrouping.ForMod(new[] { V("gh:x", false, "es", "1.1", "h", "", targetMod: "") }, "wol"));
    }

    [Fact]
    public void TheOfficialReleaseIsHiddenWhereTheOfficialFolderHasTheSameId()
    {
        var cards = TranslationSourceGrouping.BuildForMod(new[]
        {
            Result("gh:official", true, V("gh:official", true, "ES-LA", "1.2.0e-r2", "h1", "2026-09-09")),
            Result("ghr:official", true,
                V("ghr:official", true, "ES-LA", "1.0", "h0", "", kind: TranslationSourceKind.GitHubReleases),
                V("ghr:official", true, "es", "1.1", "h5", "", targetMod: "", kind: TranslationSourceKind.GitHubReleases)),
        }, "wol");

        // ES-LA from the releases is folded away; the releases' other id stays.
        Assert.Equal(2, cards.Count);
        Assert.Contains(cards, c => c.Id == "ES-LA" && c.SourceKind == TranslationSourceKind.GitHubFolder);
        Assert.Contains(cards, c => c.Id == "es" && c.SourceKind == TranslationSourceKind.GitHubReleases);
        Assert.DoesNotContain(cards, c => c.Id == "ES-LA" && c.SourceKind == TranslationSourceKind.GitHubReleases);
    }

    /// <summary>Only the OFFICIAL pair is folded — another translator's releases never are.</summary>
    [Fact]
    public void AnUnofficialReleaseWithTheSameId_StaysItsOwnCard()
    {
        var cards = TranslationSourceGrouping.BuildForMod(new[]
        {
            Result("gh:official", true, V("gh:official", true, "ES-LA", "1.2.0e-r2", "h1", "2026-09-09")),
            Result("ghr:juan", false, V("ghr:juan", false, "ES-LA", "2.0", "h7", "", kind: TranslationSourceKind.GitHubReleases)),
        }, "wol");

        Assert.Equal(2, cards.Count);
    }

    [Fact]
    public void AnUnreachableSource_ContributesNothing()
    {
        var failed = SourceFetchResult.Failed(TranslationSourceRef.Repo("a/b"), false, "TxSrcErrRepoNotFound");
        Assert.Empty(TranslationSourceGrouping.BuildForMod(new[] { failed }, "wol"));
    }
}
