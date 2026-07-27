using System.Collections.Generic;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Tests for the discovery of a mod's <c>My Games</c> folder when its catalog
/// manifest declares none — the rule that makes settings sharing, user-data
/// backup and the diagnostics bundle work for EVERY mod instead of only the
/// ones whose author remembered the field.
///
/// The REJECTION cases are the point, the same way they are in
/// <c>SafeUrlTests</c>. Picking the wrong folder writes one mod's graphics and
/// hotkeys into another mod's profile — silent, and not something a player
/// could diagnose — so refusing to answer is always the better failure.
/// </summary>
public class UserDataDiscoveryTests
{
    private static UserDataService.UserDataCandidate Ok(string name) => new(name, true);

    /// <summary>The five folders that actually exist on the maintainer's machine.</summary>
    private static IReadOnlyList<UserDataService.UserDataCandidate> RealWorld() => new[]
    {
        Ok("Age of Empires 3"),
        Ok("AoE3 Improvement Mod"),
        Ok("Napoleonic Era Beta 2"),
        Ok("Struggle of Indonesia"),
        Ok("Wars of Liberty"),
    };

    private static string? Match(string displayName, params string[] claimed) =>
        UserDataService.MatchUserDataFolder(displayName, RealWorld(), claimed);

    // ---------------- the real cases ----------------

    [Theory]
    [InlineData("Improvement Mod", "AoE3 Improvement Mod")]   // "AoE3 " prefix dropped
    [InlineData("Napoleonic Era", "Napoleonic Era Beta 2")]   // folder carries a version suffix
    [InlineData("Struggle of Indonesia", "Struggle of Indonesia")]
    [InlineData("Wars of Liberty", "Wars of Liberty")]
    public void ResolvesEveryModThatExistsToday(string displayName, string expected)
    {
        Assert.Equal(expected, Match(displayName));
    }

    // ---------------- guard 1: never the base game ----------------

    [Fact]
    public void NeverReturnsTheVanillaFolder_EvenWhenTheNameMatchesExactly()
    {
        // A mod literally called "Age of Empires 3" must not be handed the
        // player's base-game saves; sharing settings would write into them.
        Assert.Null(Match("Age of Empires 3"));
    }

    [Fact]
    public void AVanillaLookingModNameStillDoesNotStealTheBaseGameFolder()
    {
        // "AoE3" normalizes away, so this would otherwise reduce to the vanilla
        // folder's own key.
        Assert.Null(Match("AoE3"));
    }

    // ---------------- guard 2: one folder, one mod ----------------

    [Fact]
    public void SkipsAFolderAnotherModAlreadyOwns()
    {
        Assert.Null(Match("Improvement Mod", "AoE3 Improvement Mod"));
    }

    [Fact]
    public void AClaimedFolderDoesNotBlockADifferentMod()
    {
        Assert.Equal("Wars of Liberty", Match("Wars of Liberty", "AoE3 Improvement Mod"));
    }

    // ---------------- guard 3: it has to be AoE3 data ----------------

    [Fact]
    public void RejectsAFolderWithoutTheAoE3Shape()
    {
        // Right name, but no Users3\ — so there is no profile to read and it is
        // probably some other game's folder that happens to be named similarly.
        var candidates = new[] { new UserDataService.UserDataCandidate("Improvement Mod", false) };
        Assert.Null(UserDataService.MatchUserDataFolder(
            "Improvement Mod", candidates, System.Array.Empty<string>()));
    }

    // ---------------- ambiguity refuses rather than guesses ----------------

    [Fact]
    public void TwoPrefixMatches_RefusesInsteadOfPickingOne()
    {
        var candidates = new[] { Ok("Some Mod Beta 1"), Ok("Some Mod Beta 2") };
        Assert.Null(UserDataService.MatchUserDataFolder(
            "Some Mod", candidates, System.Array.Empty<string>()));
    }

    [Fact]
    public void AnExactMatchWinsOverAPrefixMatch()
    {
        // An exact hit is unambiguous even when a longer sibling exists.
        var candidates = new[] { Ok("Some Mod Beta 2"), Ok("Some Mod") };
        Assert.Equal("Some Mod", UserDataService.MatchUserDataFolder(
            "Some Mod", candidates, System.Array.Empty<string>()));
    }

    [Fact]
    public void NoMatchAtAll_IsNull()
    {
        Assert.Null(Match("A Mod Nobody Installed"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ABlankModNameNeverMatches(string? displayName)
    {
        Assert.Null(UserDataService.MatchUserDataFolder(
            displayName!, RealWorld(), System.Array.Empty<string>()));
    }

    // ---------------- the normalizer ----------------

    [Theory]
    [InlineData("AoE3 Improvement Mod", "improvementmod")]
    [InlineData("Age of Empires 3 Foo", "foo")]
    [InlineData("Wars of Liberty", "warsofliberty")]
    [InlineData("Napoleonic Era Beta 2", "napoleonicerabeta2")]
    public void NormalizeStripsPunctuationCaseAndTheGamePrefix(string input, string expected)
    {
        Assert.Equal(expected, UserDataService.NormalizeFolderKey(input));
    }

    [Fact]
    public void NormalizeKeepsANameThatIsOnlyTheGamePrefix()
    {
        // Stripping here would leave nothing, so the prefix rule must not fire —
        // otherwise the vanilla folder's key becomes empty and matches anything.
        Assert.Equal("aoe3", UserDataService.NormalizeFolderKey("AoE3"));
    }
}
