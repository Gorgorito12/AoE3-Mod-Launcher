using System.IO;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// <see cref="TranslationPathPolicy"/>: what a translation pack may name. A pack can come from a
/// repository the player added by hand, so its <c>translation.json</c> is untrusted — and it used
/// to pick both the files <c>Apply</c> overwrote and the folder the installer deleted
/// recursively. The REJECTION cases are the point; every accepted case is here only to prove the
/// rule is not simply "refuse everything".
/// </summary>
public class TranslationPathPolicyTests
{
    private static readonly string[] WolCovered = { @"data\stringtabley.xml", @"data\unithelpstringsy.xml" };

    // ------ TryNormalizeRelative

    [Theory]
    [InlineData("../x")]                          // climbs out of the install
    [InlineData(@"..\x")]
    [InlineData("data/../../x")]
    [InlineData("data/../stringtabley.xml")]       // even a climb that lands inside is refused, not resolved
    [InlineData("data/./stringtabley.xml")]
    [InlineData(@"C:\Windows\win.ini")]            // rooted, every spelling
    [InlineData("C:x")]
    [InlineData(@"\\srv\share\x")]
    [InlineData("/data/stringtabley.xml")]
    [InlineData("data/stringtabley.xml:hidden")]   // an alternate data stream
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" data/stringtabley.xml")]         // Windows strips surrounding spaces: an alias
    [InlineData("data/stringtabley.xml ")]
    [InlineData("data/stringtabley.xml.")]         // and a trailing dot
    [InlineData("data//stringtabley.xml")]
    [InlineData("data/CON")]                       // device names, with or without an extension
    [InlineData("data/nul.xml")]
    [InlineData("data/string*.xml")]
    [InlineData("data/a\u0000b")]
    public void Normalize_RefusesPathsThatCouldEscapeOrAlias(string raw)
        => Assert.False(TranslationPathPolicy.TryNormalizeRelative(raw, out _));

    [Fact]
    public void Normalize_RefusesNullAndAnOverlongPath()
    {
        Assert.False(TranslationPathPolicy.TryNormalizeRelative(null, out _));
        Assert.False(TranslationPathPolicy.TryNormalizeRelative(
            "data/" + new string('a', TranslationPathPolicy.MaxRelativePathLength), out _));
    }

    [Fact]
    public void Normalize_UsesForwardSlashesAndKeepsTheCase()
    {
        Assert.True(TranslationPathPolicy.TryNormalizeRelative(@"Data\StringTableY.xml", out var n));
        Assert.Equal("Data/StringTableY.xml", n);
    }

    // ------ TryResolveCoveredTarget

    [Theory]
    [InlineData("data/protoy.xml")]                // a real game file, just not a covered one
    [InlineData("DATA/PROTOY.XML")]                // case never turns an uncovered file into a covered one
    [InlineData("stringtabley.xml")]               // the right name in the wrong folder
    [InlineData("data/sub/stringtabley.xml")]
    [InlineData("data/stringtabley.xml.XMB")]      // the compiled twin is not the covered file
    [InlineData("../data/stringtabley.xml")]
    [InlineData(@"C:\game\data\stringtabley.xml")]
    [InlineData("\uFF44ata/stringtabley.xml")]      // a fullwidth 'd' that only looks like one
    public void Resolve_RefusesAnythingButACoveredFile(string raw)
        => Assert.False(TranslationPathPolicy.TryResolveCoveredTarget(raw, WolCovered, out _));

    [Theory]
    [InlineData("data/stringtabley.xml")]
    [InlineData(@"data\stringtabley.xml")]
    [InlineData("DATA/STRINGTABLEY.XML")]
    [InlineData("Data/StringTableY.Xml")]
    public void Resolve_MapsCaseVariantsOntoTheCoveredSpelling(string raw)
    {
        Assert.True(TranslationPathPolicy.TryResolveCoveredTarget(raw, WolCovered, out var canonical));
        Assert.Equal(Path.Combine("data", "stringtabley.xml"), canonical);
    }

    [Fact]
    public void Resolve_NothingResolvesWhenTheModCoversNothing()
    {
        Assert.False(TranslationPathPolicy.TryResolveCoveredTarget("data/stringtabley.xml", null, out _));
        Assert.False(TranslationPathPolicy.TryResolveCoveredTarget(
            "data/stringtabley.xml", System.Array.Empty<string>(), out _));
    }

    /// <summary>A malformed covered entry is skipped — it never becomes a hole in the allow-list.</summary>
    [Fact]
    public void Resolve_IgnoresAMalformedCoveredEntry()
    {
        var covered = new[] { @"..\outside.xml", @"data\stringtabley.xml" };
        Assert.False(TranslationPathPolicy.TryResolveCoveredTarget("../outside.xml", covered, out _));
        Assert.True(TranslationPathPolicy.TryResolveCoveredTarget("data/stringtabley.xml", covered, out _));
    }

    // ------ IsSafePackId / IsSafeVersionSegment

    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("_originals")]                     // the English snapshot folder
    [InlineData(".incoming-abc")]                  // the installer's own scratch folders
    [InlineData(".old-abc")]
    [InlineData("CON")]
    [InlineData("com1")]
    [InlineData("con.txt")]
    [InlineData("Lpt9")]
    [InlineData("es.")]                            // Windows would map this onto "es"
    [InlineData("a/b")]
    [InlineData(@"a\b")]
    [InlineData(@"..\..\x")]
    [InlineData("es lat")]
    [InlineData("es\n")]                           // $ in a regex would let this through
    [InlineData("-es")]
    [InlineData("")]
    public void PackId_RefusesAnythingThatIsNotAPlainFolderName(string id)
        => Assert.False(TranslationPathPolicy.IsSafePackId(id));

    [Fact]
    public void PackId_RefusesNullAndSixtyFiveCharacters()
    {
        Assert.False(TranslationPathPolicy.IsSafePackId(null));
        Assert.False(TranslationPathPolicy.IsSafePackId(new string('a', 65)));
    }

    [Theory]
    [InlineData("ES-LA")]
    [InlineData("pt-br")]
    [InlineData("es")]
    [InlineData("es_419")]
    [InlineData("zh.Hans")]
    public void PackId_AcceptsRealLanguageIds(string id)
        => Assert.True(TranslationPathPolicy.IsSafePackId(id));

    [Theory]
    [InlineData("..")]
    [InlineData("1.0 beta")]
    [InlineData("../1.0")]
    [InlineData("1.0.")]
    [InlineData("v1/2")]
    [InlineData("nul")]
    public void Version_RefusesAnythingThatIsNotAPlainFolderName(string version)
        => Assert.False(TranslationPathPolicy.IsSafeVersionSegment(version));

    [Theory]
    [InlineData("1.2.0e-r1")]
    [InlineData("1.0")]
    [InlineData("2.0+build.3")]
    public void Version_AcceptsTheShapesThePackagerProposes(string version)
        => Assert.True(TranslationPathPolicy.IsSafeVersionSegment(version));

    // ------ IsUnderRoot / HasDuplicateFileNames

    [Fact]
    public void UnderRoot_OnlyStrictlyInside()
    {
        var root = Path.Combine(Path.GetTempPath(), "aoe3ml-root");
        Assert.True(TranslationPathPolicy.IsUnderRoot(root, Path.Combine(root, "data", "x.xml")));
        Assert.False(TranslationPathPolicy.IsUnderRoot(root, root));
        Assert.False(TranslationPathPolicy.IsUnderRoot(root, root + "2" + Path.DirectorySeparatorChar + "x"));
        Assert.False(TranslationPathPolicy.IsUnderRoot(root, Path.Combine(root, "..", "x")));
    }

    [Fact]
    public void DuplicateFileNames_AreFoundAcrossFoldersAndCase()
    {
        Assert.True(TranslationPathPolicy.HasDuplicateFileNames(
            new[] { "data/stringtabley.xml", "other/StringTableY.xml" }));
        Assert.False(TranslationPathPolicy.HasDuplicateFileNames(
            new[] { "data/stringtabley.xml", "data/unithelpstringsy.xml" }));
    }
}
