using System;
using System.Linq;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="SafeFolderName"/> — the one rule that turns a mod's NAME into a folder,
/// shortcut and registry-key name Windows accepts.
///
/// <para>The catalog shipped "Age of Empires 1: HD Edition". NTFS reads the colon as a stream
/// separator, so the install downloaded and extracted 301 MB and only then failed to create its
/// own folder, and the Desktop shortcut became a hidden stream on an empty file. The maintainer's
/// rule: DROP the forbidden character together with the whitespace around it.</para>
///
/// <para>The cases that matter most are the two halves of the identity rule: the AoE1 name must
/// change, and every name that already works must NOT — that is what keeps every existing
/// install, shortcut and private registry key exactly where it is.</para>
/// </summary>
public class SafeFolderNameTests
{
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheAoE1NameLosesItsColonAndMatchesTheCatalogsOwnFolder()
    {
        // The catalog entry names its defaultFolder leaf and its userDataFolder exactly this, so
        // the install folder, the default folder and the My Games folder now agree.
        Assert.Equal("Age of Empires 1 HD Edition",
            SafeFolderName.From("Age of Empires 1: HD Edition", "aoe1-hd-edition"));
    }

    [Theory]
    [InlineData("Foo:Bar", "Foo Bar")]
    [InlineData("Foo: Bar", "Foo Bar")]
    [InlineData("Foo : Bar", "Foo Bar")]
    [InlineData("Foo  :  Bar", "Foo Bar")]
    [InlineData(":Foo", "Foo")]
    [InlineData("Foo:", "Foo")]
    [InlineData("A<B>C", "A B C")]
    [InlineData("A|B", "A B")]
    [InlineData("A?*B", "A B")]
    [InlineData("The \"Great\" War", "The Great War")]
    [InlineData(@"A/B\C", "A B C")]
    [InlineData("A\tB", "A B")]                                                   // a control character
    [InlineData("Age-of-Empires-1:-HD-Edition", "Age-of-Empires-1 -HD-Edition")]  // the hand-typed one
    [InlineData("Age of Empires III: The Asian Dynasties", "Age of Empires III The Asian Dynasties")]
    public void DropsForbiddenCharactersWithTheSpacesAroundThem(string name, string expected)
        => Assert.Equal(expected, SafeFolderName.From(name, "fallback-id"));

    [Theory]
    [InlineData("Mod v1.", "Mod v1")]
    [InlineData("Mod . . .", "Mod")]
    [InlineData("Mod ", "Mod")]
    [InlineData("  Mod", "Mod")]
    [InlineData("Mod\u00A0", "Mod")]                                  // a trailing no-break space
    public void TrimsWhatWindowsWouldDropSilently(string name, string expected)
        => Assert.Equal(expected, SafeFolderName.From(name, "fallback-id"));

    [Theory]
    [InlineData("CON", "_CON")]
    [InlineData("con", "_con")]
    [InlineData("NUL.txt", "_NUL.txt")]
    [InlineData("CON .txt", "_CON .txt")]
    [InlineData("CONIN$", "_CONIN$")]
    [InlineData("COM0", "_COM0")]
    [InlineData("LPT9", "_LPT9")]
    [InlineData("COM\u00B9", "_COM\u00B9")]
    [InlineData("CON:", "_CON")]                                     // cleaned FIRST, then checked
    public void ReservedDeviceNamesGetAnUnderscore(string name, string expected)
        => Assert.Equal(expected, SafeFolderName.From(name, "fallback-id"));

    [Theory]
    [InlineData("Con Artist")]
    [InlineData("CONSOLE")]
    [InlineData("COM10")]
    [InlineData("Auxiliary")]
    public void ANameThatOnlyStartsLikeADeviceIsLeftAlone(string name)
        => Assert.Equal(name, SafeFolderName.From(name, "fallback-id"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("???")]
    [InlineData(": * ?")]
    [InlineData("...")]
    public void AnEmptyResultFallsBackToTheModId(string? name)
        => Assert.Equal("my-mod", SafeFolderName.From(name, "my-mod"));

    [Fact]
    public void WithNoUsableIdEitherTheLastResortIsMod()
    {
        Assert.Equal(SafeFolderName.LastResort, SafeFolderName.From("???", null));
        Assert.Equal(SafeFolderName.LastResort, SafeFolderName.From("", "::"));
    }

    [Fact]
    public void ALongNameIsCutAtTheNtfsLimitWithoutLeavingADotASpaceOrHalfACharacter()
    {
        var name = new string('a', 250) + " ...." + new string('b', 50);
        var cut = SafeFolderName.From(name, null);
        Assert.True(cut.Length <= SafeFolderName.MaxLength);
        Assert.True(SafeFolderName.IsValid(cut));

        // A surrogate pair straddling the cut is dropped whole, never split.
        var emoji = new string('x', SafeFolderName.MaxLength - 1) + "\U0001F600";
        var cleaned = SafeFolderName.From(emoji, null);
        Assert.False(char.IsHighSurrogate(cleaned[^1]));
        Assert.True(SafeFolderName.IsValid(cleaned));
    }

    /// <summary>
    /// Every name the catalog ships today that already worked — display names, userDataFolder
    /// values and defaultFolder leaves — comes back unchanged. If one of these moved, its install
    /// folder, its shortcuts and (for the privateSetupPath mods) its registry key would move too.
    /// Excluded on purpose: the two names WITH a colon, "Age of Empires 1: HD Edition" and the
    /// stock "Age of Empires III: The Asian Dynasties".
    /// </summary>
    [Theory]
    [InlineData("Wars of Liberty")]
    [InlineData("Improvement Mod")]
    [InlineData("King's Return")]
    [InlineData("Age of Dynasties (Remake)")]
    [InlineData("Age of Dynasties")]
    [InlineData("Knights and Barbarians-Remastered")]
    [InlineData("Knights and Barbarians")]
    [InlineData("Hundred Days")]
    [InlineData("AoE III - Hundred Days")]
    [InlineData("Fire and Reform DE")]
    [InlineData("The Ancient Age")]
    [InlineData("Enhanced Legacy Edition")]
    [InlineData("AoE3 Unleashed Reloaded")]
    [InlineData("The Ottoman Empire")]
    [InlineData("Napoleonic Era")]
    [InlineData("Napoleonic Era Beta 2")]
    [InlineData("Struggle of Indonesia")]
    [InlineData("Age of Empires 1 HD Edition")]
    [InlineData("Wars of Liberty (2)")]
    public void EveryNameThatAlreadyWorksComesBackUnchanged(string name)
    {
        Assert.True(SafeFolderName.IsValid(name), name);
        Assert.Equal(name, SafeFolderName.From(name, "fallback-id"));
    }

    private static readonly string[] Corpus =
    {
        "Wars of Liberty", "Age of Empires 1: HD Edition", "Foo  :  Bar", " lead", "trail.",
        "CON", "con.txt", "A<B>C", "\t", "", "   ", "Mod v1.", "x\u00A0", "Ñandú: Edición",
        "Age-of-Empires-1:-HD-Edition", new string('z', 300), "COM\u00B2 .x", "...", "a..b",
    };

    [Fact]
    public void TheCleanNameIsAlwaysValidAndCleaningTwiceChangesNothing()
    {
        foreach (var name in Corpus)
        {
            var once = SafeFolderName.From(name, "fallback-id");
            Assert.True(SafeFolderName.IsValid(once), $"'{name}' -> '{once}'");
            Assert.Equal(once, SafeFolderName.From(once, "fallback-id"));
        }
    }

    [Fact]
    public void ANameIsUnchangedExactlyWhenItIsValid()
    {
        foreach (var name in Corpus)
            Assert.Equal(SafeFolderName.IsValid(name), SafeFolderName.From(name, "fallback-id") == name);
    }

    [Theory]
    [InlineData("Age of Empires 1: HD Edition")]
    [InlineData("Foo.")]
    [InlineData(" Foo")]
    [InlineData("Foo ")]
    [InlineData("CON")]
    [InlineData("A*B")]
    [InlineData("")]
    public void IsValidRefusesWhatWouldNotComeBackUnchanged(string name)
        => Assert.False(SafeFolderName.IsValid(name));

    [Fact]
    public void ATypedPathSegmentMayStartWithASpaceButAGeneratedNameMayNot()
    {
        Assert.Null(SafeFolderName.CheckSegment(" Mods"));
        Assert.False(SafeFolderName.IsValid(" Mods"));
    }

    [Fact]
    public void CheckSegmentNamesTheCharacterItRefuses()
    {
        var colon = SafeFolderName.CheckSegment("Age of Empires 1: HD Edition");
        Assert.Equal(InstallPathProblemKind.InvalidCharacter, colon!.Kind);
        Assert.Equal(":", colon.Detail);
        Assert.Equal("Age of Empires 1: HD Edition", colon.Segment);

        var tab = SafeFolderName.CheckSegment("A\tB");
        Assert.Equal("U+0009", tab!.Detail);
    }

    // ------------------------------------------------------------------ KnownLeaves

    [Fact]
    public void KnownLeavesAcceptTheRawNameTheCleanNameAndTheDefaultFolderLeaf()
    {
        var profile = new ModProfile
        {
            Id = "aoe1-hd-edition",
            DisplayName = "Age of Empires 1: HD Edition",
            DefaultInstallFolder = @"C:\Program Files (x86)\Age of Empires 1 HD Edition",
        };

        var leaves = SafeFolderName.KnownLeaves(profile);

        Assert.Contains("Age of Empires 1: HD Edition", leaves);
        Assert.Contains("Age of Empires 1 HD Edition", leaves);
        Assert.Equal(2, leaves.Count);   // the clean name and the default leaf are the same name
    }

    [Fact]
    public void KnownLeavesKeepADefaultFolderLeafThatDiffersFromTheName()
    {
        var profile = new ModProfile
        {
            Id = "knights-and-barbarians-remastered",
            DisplayName = "Knights and Barbarians-Remastered",
            DefaultInstallFolder = @"C:\Program Files (x86)\Knights and Barbarians",
        };

        Assert.Contains("Knights and Barbarians", SafeFolderName.KnownLeaves(profile));
        Assert.Contains("Knights and Barbarians-Remastered", SafeFolderName.KnownLeaves(profile));
    }

    // ------------------------------------------------------------------ declared folder names

    [Theory]
    [InlineData("Foo: Bar")]
    [InlineData("C:x")]        // rooted: Path.Combine(docs, "My Games", "C:x") escapes My Games
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("...")]        // only ".." is the parent; these resolve to My Games itself
    [InlineData(". .")]
    [InlineData(".. .")]
    [InlineData("CON")]
    [InlineData("A*B")]
    [InlineData("   ")]
    public void ADeclaredFolderNameThatCannotBeOneFolderIsRefused(string name)
        => Assert.False(SafeFolderName.IsUsableDeclaredName(name));

    [Theory]
    [InlineData("AoE III - Hundred Days")]
    [InlineData("King's Return")]
    [InlineData("Age of Empires 1 HD Edition")]
    [InlineData("Foo.")]        // Windows resolves it (the caller stores it trimmed); dropping costs more
    [InlineData(" Foo")]
    public void ADeclaredFolderNameWindowsCanResolveIsAccepted(string name)
        => Assert.True(SafeFolderName.IsUsableDeclaredName(name));

    // ------------------------------------------------------------------ names used as they are

    /// <summary>
    /// Shortcut names: a name that already works is used exactly as written — including one with
    /// a single trailing dot, whose shortcuts have always worked — and only a name that cannot
    /// work is cleaned. Cleaning "WoL v1." would leave its old shortcuts behind as orphans.
    /// </summary>
    [Theory]
    [InlineData("Wars of Liberty", true)]
    [InlineData("Wars of Liberty (2)", true)]
    [InlineData("WoL v1.", true)]
    [InlineData("Age of Empires 1: HD Edition", false)]
    [InlineData("A|B", false)]
    [InlineData("CON", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    // A re-overlay carries the name trimmed: kept raw, the first repair would orphan these.
    [InlineData(" Foo", false)]
    [InlineData("Foo ", false)]
    // The Start Menu folder is created under one name and the shortcut saved under another.
    [InlineData("Mod v1..", false)]
    [InlineData("Mod .", false)]
    [InlineData("Bar...", false)]
    [InlineData(".", false)]
    [InlineData("..", false)]
    public void ANameThatAlreadyWorksIsUsedAsItIs(string name, bool asIs)
        => Assert.Equal(asIs, SafeFolderName.CanBeUsedAsIs(name));
}
