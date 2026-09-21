using System;
using System.IO;
using System.Linq;
using System.Text;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Which civilizations a player can BE, and therefore which art may ever be drawn as a flag.
///
/// <para><b>The bug these exist for.</b> A civ string arriving from the server is a DISPLAY
/// name. In Wars of Liberty one of those collides head-on with a native ally's INTERNAL name:
/// block 48 is <c>WallMapu</c>, playable, displayed as "Mapuche", shipping
/// <c>War of the Triple Alliance\Flags\mapuche</c>; block 77 is <c>Mapuche</c>, a native ally
/// with no flag at all and only <c>ui\native_allies\mapuche</c> — a portrait painting. Looked
/// up as an internal name first, "Mapuche" found the native ally and the launcher drew its
/// painting beside the player's name.</para>
///
/// <para>108 of that file's 168 blocks are natives and 69 carry no flag texture, so the
/// rejections below are a whole class of wrong pictures rather than one civilization.</para>
/// </summary>
public class PlayableCivTests : IDisposable
{
    private readonly string _root;

    public PlayableCivTests()
    {
        _root = Directory.CreateTempSubdirectory("wol-playable-").FullName;
        CivNameResolver.ResetCache();
    }

    public void Dispose()
    {
        CivNameResolver.ResetCache();
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string MakeMod(string name, string civsXml, string? stringTable)
    {
        var install = Path.Combine(_root, name);
        var data = Path.Combine(install, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "civs.xml"), civsXml, Encoding.UTF8);
        if (stringTable != null)
        {
            // UTF-16 with a BOM, which is what AoE3 actually ships.
            File.WriteAllText(
                Path.Combine(data, "stringtabley.xml"), stringTable, new UnicodeEncoding(false, true));
        }
        return install;
    }

    /// <summary>
    /// The real shape, reduced: the playable civ and the native ally that share a display name,
    /// plus a civ that declares no <c>&lt;main&gt;</c> at all.
    /// </summary>
    private const string Civs = """
    <?xml version="1.0" encoding="utf-8"?>
    <civs>
      <civ>
        <name>WallMapu</name>
        <main>1</main>
        <displaynameid>601983</displaynameid>
        <homecityflagtexture>War of the Triple Alliance\Flags\mapuche</homecityflagtexture>
      </civ>
      <civ>
        <name>Mapuche</name>
        <main>0</main>
        <portrait>ui\native_allies\mapuche</portrait>
        <displaynameid>45431</displaynameid>
      </civ>
      <civ>
        <name>NoMainTag</name>
        <displaynameid>777</displaynameid>
        <homecityflagtexture>objects\flags\nomain</homecityflagtexture>
      </civ>
    </civs>
    """;

    private const string Table = """
    <?xml version="1.0" encoding="UTF-16"?>
    <StringTable version='8'>
      <Language name='English'>
        <String _locID ='601983'>Mapuche</String>
        <String _locID ='45431'>Mapuche</String>
        <String _locID ='777'>No Main Tag</String>
      </Language>
    </StringTable>
    """;

    /// <summary>
    /// THE ONE THAT MATTERS. Both blocks display as "Mapuche" and only one of them is a
    /// civilization anybody can play; the native ally must not be reachable at all, because the
    /// only art it has is a painting and drawing it is the reported bug.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_ANativeAllyIsNeverAPlayableCiv()
    {
        var civs = CivNameResolver.ResolvePlayableCivs(MakeMod("wol", Civs, Table));

        Assert.DoesNotContain(civs, c => c.InternalName == "Mapuche");
        Assert.Contains(civs, c => c.InternalName == "WallMapu");

        // And nothing anywhere in the answer points at the painting, by any route.
        Assert.DoesNotContain(civs, c => c.Art != null && c.Art.Contains("native_allies"));
    }

    /// <summary>
    /// The other half of the same bug: the playable block keeps the display name the collision
    /// was hiding, AND the flag the mod actually ships for it.
    /// </summary>
    [Fact]
    public void ThePlayableBlockCarriesBothTheSeenNameAndTheModsOwnFlag()
    {
        var civs = CivNameResolver.ResolvePlayableCivs(MakeMod("wol", Civs, Table));
        var wallmapu = Assert.Single(civs, c => c.InternalName == "WallMapu");

        Assert.Equal("Mapuche", wallmapu.DisplayName);
        Assert.Equal(@"War of the Triple Alliance\Flags\mapuche", wallmapu.Art);
    }

    /// <summary>
    /// A block with NO <c>&lt;main&gt;</c> is kept. Only an explicit zero is refused: a missing
    /// flag is a worse outcome than a native ally nobody will ever be reported as playing, so a
    /// mod that omits the tag must keep working exactly as it did.
    /// </summary>
    [Fact]
    public void AnAbsentMainTagCountsAsPlayable()
    {
        var civs = CivNameResolver.ResolvePlayableCivs(MakeMod("wol", Civs, Table));
        var noMain = Assert.Single(civs, c => c.InternalName == "NoMainTag");

        Assert.Equal(@"objects\flags\nomain", noMain.Art);
    }

    /// <summary>
    /// A playable civ the string table cannot name still comes back, with a null display name.
    /// Napoleonic Era ships 36 display ids that exist in no file of its install, and dropping
    /// those civilizations would cost them their flag as well as their name.
    /// </summary>
    [Fact]
    public void APlayableCivWithNoResolvableNameIsStillPlayable()
    {
        var civs = CivNameResolver.ResolvePlayableCivs(MakeMod("nameless", Civs, stringTable: null));
        var wallmapu = Assert.Single(civs, c => c.InternalName == "WallMapu");

        Assert.Null(wallmapu.DisplayName);
        Assert.Equal(@"War of the Triple Alliance\Flags\mapuche", wallmapu.Art);
    }

    /// <summary>
    /// The index path is NOT filtered, and must never be. A recording's civ number is 1-based
    /// over ALL blocks, natives included, so applying the playability filter there would shift
    /// every index past the first native.
    ///
    /// <para><b>The fixture interleaves them deliberately</b>, because the real file does not:
    /// Wars of Liberty puts its 60 playable civs at positions 1-60 and its 108 natives after
    /// them, so filtering would shift nothing there and look entirely correct — while silently
    /// breaking any mod that mixes the two. Here the native sits at position 2, so a filtered
    /// index path makes this test fail instead of some future mod.</para>
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheRecordingIndexStillCountsNativeAlliesToo()
    {
        var mod = MakeMod("wol", Civs, Table);

        // Block 2 is the native ally. Resolving by index must still reach it, or every civ
        // after a native in the file would resolve to the wrong one.
        Assert.Equal("Mapuche", CivNameResolver.Resolve(mod, 1));   // WallMapu
        Assert.Equal("Mapuche", CivNameResolver.Resolve(mod, 2));   // the native ally
        Assert.Equal("No Main Tag", CivNameResolver.Resolve(mod, 3));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoInstallPathIsEmpty(string? path)
        => Assert.Empty(CivNameResolver.ResolvePlayableCivs(path));

    [Fact]
    public void AMissingCivListIsEmpty()
        => Assert.Empty(CivNameResolver.ResolvePlayableCivs(Path.Combine(_root, "nope")));
}
