using System;
using System.IO;
using System.Text;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The rules behind turning a recording's civilization INDEX into the name the players saw.
///
/// <para><b>The refusals are the point.</b> A missing civilization costs a badge; a wrong one
/// writes a civ somebody never played into their own match history and into whatever balance
/// figures are computed from it, with nothing downstream able to tell. So every case where the
/// answer is not certain has to come back null, and those are most of the cases below.</para>
///
/// <para>The fixtures are deliberately TINY. The real Wars of Liberty civ list is 481 KB and its
/// string table 5.9 MB; copying either into the repo to test a lookup would be absurd, and the
/// live check against the maintainer's real installs is a separate step that no test host can
/// run.</para>
/// </summary>
public class CivNameResolverTests : IDisposable
{
    private readonly string _root;

    public CivNameResolverTests()
    {
        _root = Directory.CreateTempSubdirectory("wol-civres-").FullName;
        CivNameResolver.ResetCache();
    }

    public void Dispose()
    {
        CivNameResolver.ResetCache();
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    // ------------------------------------------------------------------ fixtures

    /// <summary>
    /// A mod install holding just the two files the resolver reads. The civ list mirrors the
    /// real shape, including a block with NO display id — the base game ships several.
    /// </summary>
    private string MakeMod(string name, string civsXml, string? stringTable, string tableFile = "stringtabley.xml")
    {
        var install = Path.Combine(_root, name);
        var data = Path.Combine(install, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "civs.xml"), civsXml, Encoding.UTF8);
        if (stringTable != null)
        {
            // UTF-16 with a BOM, which is what AoE3 actually ships.
            File.WriteAllText(Path.Combine(data, tableFile), stringTable, new UnicodeEncoding(false, true));
        }
        return install;
    }

    private const string ThreeCivs = """
    <?xml version="1.0" encoding="utf-8"?>
    <civs>
      <subcivalliancecostfactor>2.0</subcivalliancecostfactor>
      <civ>
        <name>Spanish</name>
        <displaynameid>22864</displaynameid>
      </civ>
      <civ>
        <name>British</name>
        <displaynameid>22861</displaynameid>
      </civ>
      <civ>
        <name>NoNameId</name>
      </civ>
    </civs>
    """;

    private const string Table = """
    <?xml version="1.0" encoding="UTF-16"?>
    <StringTable version='8'>
      <Language name='English'>
        <String _locID ='22864'>Erucakran</String>
        <String _locID ='22861'>Aceh</String>
        <String _locID ='99999'>Something Else</String>
      </Language>
    </StringTable>
    """;

    // ------------------------------------------------------------------ the index

    /// <summary>
    /// THE MEASURED RULE: the index is 1-BASED. Nine real recordings across two mods say so,
    /// each cross-checked against an independent field of the same file — the home-city file
    /// name, the explorer, or the AI personality. Reading it 0-based lands one civ short every
    /// time, which is a real civilization and therefore invisible.
    /// </summary>
    [Fact]
    public void TheIndexIsOneBased()
    {
        var mod = MakeMod("m1", ThreeCivs, Table);

        Assert.Equal("Erucakran", CivNameResolver.Resolve(mod, 1));
        Assert.Equal("Aceh", CivNameResolver.Resolve(mod, 2));
    }

    // ------------------------------------------------------------------ by internal name

    /// <summary>
    /// <b>The whole reason this second lookup exists.</b> A home city file names its civilization
    /// internally — and a mod that reskins a base civ keeps the original, so Struggle of
    /// Indonesia files a deck under <c>Spanish</c> that every player of it knows as
    /// <b>Erucakran</b>. Showing the internal name beside somebody's own deck would name a
    /// civilization they have never heard of.
    /// </summary>
    [Fact]
    public void AnInternalCivNameResolvesToWhatThePlayerActuallySaw()
    {
        var mod = MakeMod("byname", ThreeCivs, Table);

        Assert.Equal("Erucakran", CivNameResolver.ResolveByInternalName(mod, "Spanish"));
        Assert.Equal("Aceh", CivNameResolver.ResolveByInternalName(mod, "British"));
    }

    /// <summary>
    /// The refusals. Null means "show the internal name instead", which still identifies the
    /// civilization — never a blank, and never another civ's name.
    /// </summary>
    [Fact]
    public void AnUnknownOrUndescribedCivResolvesToNothingRatherThanToSomethingElse()
    {
        var mod = MakeMod("byname2", ThreeCivs, Table);

        Assert.Null(CivNameResolver.ResolveByInternalName(mod, "Nobody"));
        Assert.Null(CivNameResolver.ResolveByInternalName(mod, "NoNameId"));   // declares no id
        Assert.Null(CivNameResolver.ResolveByInternalName(mod, ""));
        Assert.Null(CivNameResolver.ResolveByInternalName(null, "Spanish"));
        Assert.Null(CivNameResolver.ResolveByInternalName(Path.Combine(_root, "nope"), "Spanish"));
    }

    /// <summary>Whitespace and case come from a hand-editable file, so neither may cost a name.</summary>
    [Fact]
    public void TheInternalNameLookupIsCaseInsensitiveAndTrims()
    {
        var mod = MakeMod("byname3", ThreeCivs, Table);

        Assert.Equal("Erucakran", CivNameResolver.ResolveByInternalName(mod, "  spanish  "));
    }

    /// <summary>
    /// Index 0 is AoE3's nature slot and a negative is its "unset". Neither is a civilization,
    /// and a 0-based reading would hand slot 0's name to whoever the file said was civ 0 —
    /// which is every empty slot in the header.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ZeroAndNegativeNameNothing(int index)
        => Assert.Null(CivNameResolver.Resolve(MakeMod("m2", ThreeCivs, Table), index));

    /// <summary>Past the end of the list, rather than clamping to the last civ.</summary>
    [Fact]
    public void AnIndexPastTheListIsRefused()
        => Assert.Null(CivNameResolver.Resolve(MakeMod("m3", ThreeCivs, Table), 4));

    // ------------------------------------------------------------------ the display name

    /// <summary>
    /// THE OTHER MEASURED RULE, and the one that would be tempting to "simplify" away: a mod
    /// that reskins a base civilization keeps the original internal name. In Struggle of
    /// Indonesia the block called <c>Ottomans</c> displays as "Surakarta" and the one called
    /// <c>Spanish</c> as "Erucakran" — a player of that mod never saw either internal word.
    ///
    /// <para>So an unresolvable display id yields NULL and never the internal name. Falling
    /// back would print a civilization that does not exist in the mod being played.</para>
    /// </summary>
    [Fact]
    public void TheInternalNameIsNeverUsedAsAFallback()
    {
        // Third civ declares no display id at all.
        var mod = MakeMod("m4", ThreeCivs, Table);
        Assert.Null(CivNameResolver.Resolve(mod, 3));

        // And an id the table does not carry is the same answer.
        var orphan = MakeMod("m5", """
        <civs><civ><name>British</name><displaynameid>12345</displaynameid></civ></civs>
        """, Table);
        Assert.Null(CivNameResolver.Resolve(orphan, 1));
    }

    /// <summary>
    /// Improvement Mod and Napoleonic Era keep their civ list packed inside <c>Data.bar</c>, so
    /// "no loose civs.xml" is the ORDINARY state for two of the five shipped mods rather than a
    /// fault. It must be silent and it must be null.
    /// </summary>
    [Fact]
    public void AModWithNoLooseCivListResolvesNothing()
    {
        var install = Path.Combine(_root, "packed");
        Directory.CreateDirectory(Path.Combine(install, "data"));

        Assert.Null(CivNameResolver.Resolve(install, 1));
    }

    [Fact]
    public void ANonsensePathIsRefusedRatherThanThrowing()
    {
        Assert.Null(CivNameResolver.Resolve(null, 1));
        Assert.Null(CivNameResolver.Resolve("   ", 1));
        Assert.Null(CivNameResolver.Resolve(Path.Combine(_root, "does-not-exist"), 1));
    }

    /// <summary>
    /// A civ list that parses but declares nothing. Distinct from a missing file and equally
    /// unusable, and it must not throw on the path that reports a match.
    /// </summary>
    [Fact]
    public void AnEmptyCivListResolvesNothing()
        => Assert.Null(CivNameResolver.Resolve(MakeMod("m6", "<civs></civs>", Table), 1));

    /// <summary>
    /// Malformed XML from a mod author must cost one unresolved civilization, never an
    /// exception — this runs while a finished match is being reported.
    /// </summary>
    [Fact]
    public void BrokenXmlIsSwallowed()
        => Assert.Null(CivNameResolver.Resolve(
            MakeMod("m7", "<civs><civ><name>Oops", Table), 1));

    /// <summary>
    /// Only the ROOT's own civ children are counted. A civ element nested inside something else
    /// would otherwise shift every index after it by one — silently, and for that whole mod.
    /// </summary>
    [Fact]
    public void ANestedCivElementDoesNotShiftTheNumbering()
    {
        var mod = MakeMod("m8", """
        <civs>
          <civ><name>Spanish</name><displaynameid>22864</displaynameid></civ>
          <somethingelse><civ><name>Decoy</name><displaynameid>99999</displaynameid></civ></somethingelse>
          <civ><name>British</name><displaynameid>22861</displaynameid></civ>
        </civs>
        """, Table);

        Assert.Equal("Erucakran", CivNameResolver.Resolve(mod, 1));
        Assert.Equal("Aceh", CivNameResolver.Resolve(mod, 2));
    }

    // ------------------------------------------------------------------ which table

    /// <summary>
    /// The layers are read in the engine's own override order, so a mod's own
    /// <c>stringtabley.xml</c> beats the base table for the same id. Getting this backwards
    /// would print the BASE GAME's name for every civilization a mod renamed.
    /// </summary>
    [Fact]
    public void TheModsOwnLayerWinsOverTheBaseTable()
    {
        var install = MakeMod("m9", ThreeCivs, """
        <?xml version="1.0" encoding="UTF-16"?>
        <StringTable version='8'><Language name='English'>
          <String _locID ='22864'>Spanish</String>
        </Language></StringTable>
        """, "stringtable.xml");

        // The same id in the y layer, which is where mods write.
        File.WriteAllText(
            Path.Combine(install, "data", "stringtabley.xml"), Table, new UnicodeEncoding(false, true));

        Assert.Equal("Erucakran", CivNameResolver.Resolve(install, 1));
    }

    /// <summary>An id only the base layer carries is still found.</summary>
    [Fact]
    public void ALowerLayerStillAnswersWhatTheTopOneDoesNot()
    {
        var install = MakeMod("m10", ThreeCivs, """
        <?xml version="1.0" encoding="UTF-16"?>
        <StringTable version='8'><Language name='English'>
          <String _locID ='22861'>Aceh</String>
        </Language></StringTable>
        """, "stringtablex.xml");

        Assert.Equal("Aceh", CivNameResolver.Resolve(install, 2));
    }

    /// <summary>
    /// THE CANONICAL-ENGLISH SNAPSHOT WINS OVER THE LIVE FILE, the same rule the multiplayer
    /// fingerprint and version detection already follow.
    ///
    /// <para>The resolved name is stored on the server and shown to everybody, so it must not
    /// depend on which translation the reporting player happens to have applied — otherwise one
    /// Spanish host writes Spanish civ names into every participant's history.</para>
    /// </summary>
    [Fact]
    public void TheCanonicalSnapshotBeatsATranslatedLiveFile()
    {
        var install = MakeMod("m11", ThreeCivs, """
        <?xml version="1.0" encoding="UTF-16"?>
        <StringTable version='8'><Language name='Spanish'>
          <String _locID ='22864'>Traducido</String>
        </Language></StringTable>
        """);

        var originals = Path.Combine(install, "translations", "_originals");
        Directory.CreateDirectory(originals);
        File.WriteAllText(
            Path.Combine(originals, "stringtabley.xml"), Table, new UnicodeEncoding(false, true));

        Assert.Equal("Erucakran", CivNameResolver.Resolve(install, 1));
    }
}
