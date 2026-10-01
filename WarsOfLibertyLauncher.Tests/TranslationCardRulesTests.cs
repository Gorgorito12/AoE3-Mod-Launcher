using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The rules that decide what the Language tab marks "in use" once the same language can come
/// from several translators: <see cref="TranslationCompat.IsActiveVersion"/>,
/// <see cref="TranslationCompat.PickActiveCard"/>, the keys and the display order. Versions used
/// to be told apart by their TEXT, and two different packs both said "1.1".
/// </summary>
public class TranslationCardRulesTests
{
    private static TranslationVersion Ver(string version, string hash = "") =>
        new() { Version = version, ContentHash = hash };

    private static TranslationIndexEntry Card(string source, string id, bool official, params TranslationVersion[] versions)
    {
        var head = versions.FirstOrDefault();
        return new TranslationIndexEntry
        {
            Id = id, Name = id, SourceKey = source, IsOfficial = official,
            Version = head?.Version ?? "", ContentHash = head?.ContentHash ?? "",
            Versions = versions.ToList(),
        };
    }

    // ------ IsActiveVersion

    /// <summary>THE ONE THAT MATTERS: two packs both labelled "1.1" are different packs.</summary>
    [Fact]
    public void TheSameVersionTextWithAnotherHash_IsNotActive()
        => Assert.False(TranslationCompat.IsActiveVersion(Ver("1.1", "bbbb000000000002"), "aaaa000000000001", "1.1"));

    [Fact]
    public void TheSameHashUnderAnotherVersionText_IsActive()
        => Assert.True(TranslationCompat.IsActiveVersion(Ver("1.2.0e-r2", "AAAA000000000001"), "aaaa000000000001", "1.1"));

    [Fact]
    public void WithNoHashRecorded_TheVersionTextDecides()
    {
        Assert.True(TranslationCompat.IsActiveVersion(Ver("1.1", "aaaa000000000001"), "", "1.1"));
        Assert.False(TranslationCompat.IsActiveVersion(Ver("1.0", "aaaa000000000001"), "", "1.1"));
    }

    /// <summary>A recorded hash the version can't be compared with is no licence to guess by text.</summary>
    [Fact]
    public void AHashRecorded_ButTheVersionHasNone_IsNotActive()
        => Assert.False(TranslationCompat.IsActiveVersion(Ver("1.1"), "aaaa000000000001", "1.1"));

    [Fact]
    public void NothingRecorded_IsNeverActive()
        => Assert.False(TranslationCompat.IsActiveVersion(Ver("1.1", "aaaa000000000001"), "", ""));

    // ------ PickActiveCard

    private static readonly TranslationIndexEntry Official =
        Card("gh:official", "ES-LA", true, Ver("1.2.0e-r2", "aaaa000000000001"), Ver("1.2.0e-r1", "aaaa000000000000"));
    private static readonly TranslationIndexEntry Juan =
        Card("url:juan", "ES-LA", false, Ver("1.2.0e-r5", "bbbb000000000005"));
    private static readonly TranslationIndexEntry French =
        Card("gh:official", "FR", true, Ver("1.0", "cccc000000000001"));

    private static readonly TranslationIndexEntry[] Cards = { Official, Juan, French };

    [Fact]
    public void TheCardFromTheSourceItWasAppliedFrom_IsActive()
        => Assert.Same(Juan, TranslationCompat.PickActiveCard(Cards, "ES-LA", "url:juan", "bbbb000000000005"));

    /// <summary>A config written before the source was recorded: the hash finds the card.</summary>
    [Fact]
    public void WithoutASource_TheCardListingTheHash_IsActive()
        => Assert.Same(Juan, TranslationCompat.PickActiveCard(Cards, "ES-LA", "", "bbbb000000000005"));

    [Fact]
    public void AnOlderVersionsHash_StillFindsItsCard()
        => Assert.Same(Official, TranslationCompat.PickActiveCard(Cards, "ES-LA", "", "aaaa000000000000"));

    /// <summary>
    /// The applied pack is in no listed card (its source was removed): NO card is marked — the
    /// official one must not inherit another translator's "in use".
    /// </summary>
    [Fact]
    public void AHashInNoCard_MarksNoCard()
        => Assert.Null(TranslationCompat.PickActiveCard(Cards, "ES-LA", "", "dddd000000000009"));

    [Fact]
    public void ASourceNoLongerListed_WithNoHash_MarksNoCard()
        => Assert.Null(TranslationCompat.PickActiveCard(Cards, "ES-LA", "url:removed", ""));

    /// <summary>The oldest configs recorded only the id: the official card is the one they meant.</summary>
    [Fact]
    public void OnlyTheIdRecorded_MarksTheOfficialCard()
    {
        Assert.Same(Official, TranslationCompat.PickActiveCard(new[] { Juan, Official }, "ES-LA", "", ""));
        Assert.Same(Juan, TranslationCompat.PickActiveCard(new[] { Juan }, "es-la", "", ""));
    }

    [Theory]
    [InlineData("")]
    [InlineData("DE")]
    public void NoOrAnUnlistedId_MarksNoCard(string id)
        => Assert.Null(TranslationCompat.PickActiveCard(Cards, id, "gh:official", "aaaa000000000001"));

    [Fact]
    public void CardHasHash_LooksAtEveryVersion()
    {
        Assert.True(TranslationCompat.CardHasHash(Official, "AAAA000000000000"));
        Assert.False(TranslationCompat.CardHasHash(Official, "bbbb000000000005"));
        Assert.False(TranslationCompat.CardHasHash(Official, ""));
    }

    // ------ keys

    /// <summary>A version and the entry it heads produce the same key, so they never bell twice.</summary>
    [Fact]
    public void AVersionsKey_EqualsTheKeyOfTheEntryItHeads()
        => Assert.Equal(TranslationCompat.KeyOf(Official), TranslationCompat.KeyOfVersion("ES-LA", Official.Versions[0]));

    [Fact]
    public void AnIndexItemWithOnlyASha256_IsKeyedByIt_WithoutCollidingWithAContentHash()
    {
        var sha = "A3F1C2D4E5F60718293A4B5C6D7E8F90A1B2C3D4E5F60718293A4B5C6D7E8F90";
        var entry = new TranslationIndexEntry { Id = "ES-LA", Version = "1.0", Sha256 = sha };
        Assert.Equal("ES-LA@sha256:a3f1c2d4e5f60718", TranslationCompat.KeyOf(entry));
        Assert.Equal("ES-LA@sha256:a3f1c2d4e5f60718",
            TranslationCompat.KeyOfVersion("ES-LA", new TranslationVersion { Version = "1.0", Sha256 = sha }));
    }

    [Fact]
    public void TheKeyCascade_PrefersTheContentHashOverTheSha256()
    {
        var v = new TranslationVersion { Version = "1.0", ContentHash = "18ca36a3d84d2352", Sha256 = new string('a', 64) };
        Assert.Equal("ES-LA@18ca36a3d84d2352", TranslationCompat.KeyOfVersion("ES-LA", v));
        Assert.Equal("ES-LA@1.0", TranslationCompat.KeyOfVersion("ES-LA", new TranslationVersion { Version = "1.0" }));
    }

    [Fact]
    public void EffectiveContentHash_IsTheDeclaredOneElseTheComputedOne()
    {
        var files = new List<TranslationFile> { new() { Path = "data/stringtabley.xml", TranslatedHash = "ABC" } };
        Assert.Equal("18ca36a3d84d2352",
            TranslationCompat.EffectiveContentHash(new TranslationManifest { ContentHash = " 18ca36a3d84d2352 ", Files = files }));
        Assert.Equal(TranslationCompat.ComputeContentHash(files),
            TranslationCompat.EffectiveContentHash(new TranslationManifest { Files = files }));
        Assert.Equal("", TranslationCompat.EffectiveContentHash(null));
    }

    // ------ display order

    [Fact]
    public void Order_ActiveThenCompatibleThenOfficialThenFetchOrder()
    {
        var incompatibleOfficial = Card("gh:official", "PT", true, Ver("1.0", "p"));
        incompatibleOfficial.CompatibleWith = new() { "1.1.0" };
        var compatibleJuan = Card("url:juan", "ES-LA", false, Ver("2.0", "j"));
        compatibleJuan.CompatibleWith = new() { "1.2.0e" };
        var compatibleOfficial = Card("gh:official", "ES-LA", true, Ver("1.0", "o"));
        compatibleOfficial.CompatibleWith = new() { "1.2.0e" };
        var activeUnknown = Card("url:ana", "FR", false, Ver("1.0", "a"));
        var laterFetched = Card("url:zed", "IT", false, Ver("1.0", "z"));
        laterFetched.CompatibleWith = new() { "1.2.0e" };

        var fetchOrder = new[] { incompatibleOfficial, compatibleJuan, compatibleOfficial, activeUnknown, laterFetched };
        var ordered = TranslationCompat.OrderCardsForDisplay(
            new[] { laterFetched, incompatibleOfficial, compatibleJuan, compatibleOfficial, activeUnknown },
            fetchOrder, "1.2.0e", c => ReferenceEquals(c, activeUnknown));

        Assert.Equal(new[] { activeUnknown, compatibleOfficial, compatibleJuan, laterFetched, incompatibleOfficial }, ordered);
    }
}
