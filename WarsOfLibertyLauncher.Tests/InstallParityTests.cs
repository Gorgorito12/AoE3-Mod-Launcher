using System;
using System.Collections.Generic;
using System.IO;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Regression tests that pin the launcher's WoL install to the OFFICIAL file
/// set, so it stays byte-faithful to original-installer peers (the OOS / LAN
/// version-mismatch root causes we diagnosed):
///   * <see cref="NativeInstallService.RemoveStaleBuildArtifacts"/> must be a
///     NO-OP: it removes NOTHING. Every file it once stripped (<c>.bak</c>,
///     loose <c>.rar</c>, "(enhanced)" <c>.wav</c>, <c>data\tactics\</c>
///     copies/orphans, the <c>art\WoL\interns\</c> subtree) — and every
///     <c>.xml.xmb</c> — is PRESENT in a canonical setup+updater install, so
///     removing any of it diverged the launcher from peers. <c>interns</c> is
///     even referenced by <c>protoy.xml</c>/<c>techtreey.xml</c> for unit art.
///   * A patch's <c>deleteList</c> is an install-RELATIVE path to a file the
///     patch ships (<c>etc\..._delete.lst</c>), NOT a URL. It must be read
///     locally and applied so a patch's "delete this file" instruction is
///     honoured — it was silently dropped before (treated as a URL download).
/// </summary>
public class InstallParityTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private string NewTempDir()
    {
        var dir = Directory.CreateTempSubdirectory("wol-parity-test-").FullName;
        _tempDirs.Add(dir);
        return dir;
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }

    private static ModProfile WolProfile() => new()
    {
        Id = "wol",
        DisplayName = "Wars of Liberty",
        InstallType = ModInstallType.IsolatedFolder,
    };

    private static void Write(string root, string relative, string content = "x")
    {
        var full = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    [Fact]
    public void RemoveStaleBuildArtifacts_IsNoOp_KeepsEveryFile()
    {
        // The launcher installs the WoL payload byte-faithfully and strips
        // NOTHING. Every file below — .xml.xmb, real data, .bak backups,
        // loose .rar, "(enhanced)" .wav, data\tactics copies/orphans, and the
        // art\WoL\interns\ subtree — is also present in a canonical
        // setup+updater install, so removing any of it diverged the launcher
        // from original-installer peers (the .xml.xmb / interns saga). This
        // test fails loudly if a future change re-introduces any sweep.
        var install = NewTempDir();
        Write(install, @"data\randomnames.xml.xmb");
        Write(install, @"data\protoy.xml");
        Write(install, @"data\techtreey.xml.bak");
        Write(install, @"data\tactics\firepit - copia.tactics");
        Write(install, @"data\tactics\spypishtaco");               // extensionless orphan
        Write(install, @"art\WoL\interns\zupay\units\Outlaw\Cuchillero\Cuchillero.xml");
        Write(install, @"art\ui\logos\watermarky.rar");
        Write(install, @"Sound\WoL\chile\bacamartero\Attack 1 (enhanced).wav");

        NativeInstallService.RemoveStaleBuildArtifacts(WolProfile(), install);

        // Nothing was removed: every written file still exists.
        foreach (var rel in new[]
        {
            @"data\randomnames.xml.xmb",
            @"data\protoy.xml",
            @"data\techtreey.xml.bak",
            @"data\tactics\firepit - copia.tactics",
            @"data\tactics\spypishtaco",
            @"art\WoL\interns\zupay\units\Outlaw\Cuchillero\Cuchillero.xml",
            @"art\ui\logos\watermarky.rar",
            @"Sound\WoL\chile\bacamartero\Attack 1 (enhanced).wav",
        })
        {
            Assert.True(File.Exists(Path.Combine(install, rel)),
                $"byte-faithful install must keep every payload file; '{rel}' was removed.");
        }
    }

    [Fact]
    public void ReadLocalDeleteList_ReadsInstallRelativeFile_AndApplyDeletesListedFiles()
    {
        var install = NewTempDir();
        // The patch ships the delete-list inside the install (etc\); the
        // UpdateInfo deleteList attribute is the relative path to it.
        Write(install, @"etc\120a_delete.lst",
            "data\\homecityhabsburgs.xml\r\nSound\\WOLConsulateRedcoat_snds.xml\r\n");
        Write(install, @"data\homecityhabsburgs.xml");         // listed → deleted
        Write(install, @"Sound\WOLConsulateRedcoat_snds.xml"); // listed → deleted
        Write(install, @"data\protoy.xml");                    // not listed → survives

        var content = ArchiveService.ReadLocalDeleteList(install, @"etc\120a_delete.lst");
        Assert.False(string.IsNullOrEmpty(content));

        ArchiveService.ApplyDeleteList(install, content);

        Assert.False(File.Exists(Path.Combine(install, @"data\homecityhabsburgs.xml")));
        Assert.False(File.Exists(Path.Combine(install, @"Sound\WOLConsulateRedcoat_snds.xml")));
        Assert.True(File.Exists(Path.Combine(install, @"data\protoy.xml")));
    }

    [Fact]
    public void ReadLocalDeleteList_MissingFileOrEmptyRef_ReturnsEmpty()
    {
        var install = NewTempDir();
        Assert.Equal("", ArchiveService.ReadLocalDeleteList(install, @"etc\nope_delete.lst"));
        Assert.Equal("", ArchiveService.ReadLocalDeleteList(install, ""));
    }

    // ------------------------------------------------------------ superseded compiled .XMB
    //
    // The OTHER half of parity, and it points the opposite way to everything above. There the
    // launcher had to stop REMOVING files a canonical install has; here it has to remove files a
    // canonical install does NOT have. An IsolatedFolder install is a clone of the player's own
    // AoE3, so it arrives carrying their compiled data\*.xml.XMB — and AoE3 reads those in
    // preference to the loose .xml a mod ships. Measured on Knights and Barbarians: 11 shadowed
    // files, including stringtable* (Spanish menus for a Spanish owner, where the author's own
    // folder runs in English) and protoy/techtreey, which are SIMULATION data.

    private static IReadOnlyList<string> Superseded(string[] shipped, params string[] onDisk)
    {
        var disk = new HashSet<string>(onDisk, StringComparer.OrdinalIgnoreCase);
        return NativeInstallService.SelectSupersededCompiledXml(shipped, disk.Contains);
    }

    /// <summary>The case the rule exists for: the clone's compiled copy shadows the mod's xml.</summary>
    [Fact]
    public void AClonedXmbShadowingAShippedXmlIsRemoved()
    {
        var take = Superseded(
            new[] { @"data/stringtable.xml", @"data/protoy.xml" },
            @"data/stringtable.xml.XMB", @"data/protoy.xml.XMB");

        Assert.Equal(new[] { @"data/protoy.xml.XMB", @"data/stringtable.xml.XMB" }, take);
    }

    /// <summary>
    /// THE test that matters most. Wars of Liberty ships its own <c>.xml.xmb</c> alongside every
    /// <c>.xml</c> — 209 of them — and stripping those is exactly what caused its LAN
    /// version-mismatch and OOS. A mod that ships both halves overwrote the clone's copy itself,
    /// so nothing is stale and nothing may be touched. The rule cannot get this backwards because
    /// it only ever considers a compiled file the payload did NOT ship.
    /// </summary>
    [Fact]
    public void AModThatShipsItsOwnXmbIsNeverTouched()
    {
        var take = Superseded(
            new[] { @"data/protoy.xml", @"data/protoy.xml.XMB", @"data/stringtabley.xml", @"data/stringtabley.xml.XMB" },
            @"data/protoy.xml.XMB", @"data/stringtabley.xml.XMB");

        Assert.Empty(take);
    }

    /// <summary>Shipping only the compiled file supersedes nothing — there is no xml of ours to win.</summary>
    [Fact]
    public void ShippingOnlyTheCompiledFileSelectsNothing()
    {
        Assert.Empty(Superseded(new[] { @"data/protoy.xml.XMB" }, @"data/protoy.xml.XMB"));
    }

    /// <summary>Nothing stale on disk — the normal case for a mod over a clean base.</summary>
    [Fact]
    public void NothingOnDiskMeansNothingToRemove()
    {
        Assert.Empty(Superseded(new[] { @"data/protoy.xml", @"data/stringtable.xml" }));
    }

    /// <summary>
    /// The files are <c>.XMB</c> on disk while a payload may name anything in any case, and
    /// Windows paths compare case-insensitively — so both the extension test and the
    /// already-shipped lookup have to.
    /// </summary>
    [Fact]
    public void TheMatchIsCaseInsensitiveOnBothHalves()
    {
        Assert.Single(Superseded(new[] { @"data/ProtoY.XML" }, @"data/protoy.xml.xmb"));

        // ...and a mod shipping its compiled copy under different casing is still shipping it.
        Assert.Empty(Superseded(
            new[] { @"data/protoy.xml", @"data/PROTOY.XML.XMB" }, @"data/protoy.xml.XMB"));
    }

    /// <summary>Only <c>.xml</c> has a compiled twin; nothing else is considered.</summary>
    [Fact]
    public void AShippedFileThatIsNotXmlIsIgnored()
    {
        Assert.Empty(Superseded(
            new[] { @"data/protoy.bar", @"age3k.exe", @"art/x.ddt" },
            @"data/protoy.bar.XMB", @"age3k.exe.XMB"));
    }

    // ------------------------------------------------------------ and it is OPT-IN
    //
    // The selection above is only half the safety. Whether removing a compiled file converges on a
    // canonical install or diverges from it depends on how the mod is DISTRIBUTED, which the
    // payload cannot express: packaging drops whatever is identical to the base game, so "the
    // author ships no such file" and "the author's file equals the base game's" arrive looking the
    // same. Measured on a real Wars of Liberty install: of 158 data\*.xml, 156 have no .XMB at all
    // and 2 (randomnames, unithelpstrings) carry the BASE GAME's — and WoL installs over the
    // player's own AoE3, so every canonical peer has those two. Removing them would reproduce the
    // LAN version-mismatch and OOS this project already paid for once.

    private static ModCatalogEntry EntryWith(string type, bool? supersede) => new()
    {
        Manifest = new ModCatalogManifest
        {
            Id = "test-mod",
            DisplayName = "Test Mod",
            Install = new ModCatalogInstall { Type = type, SupersedeCompiledXml = supersede },
            Update = new ModCatalogUpdate { Mechanism = "GitHubReleases" },
        },
    };

    /// <summary>
    /// A mod that declares nothing keeps the base game's compiled files — so every mod already in
    /// the catalog, Wars of Liberty included, is untouched by this feature.
    /// </summary>
    [Fact]
    public void AModThatDeclaresNothingNeverHasCompiledFilesRemoved()
    {
        Assert.False(ModRegistry.ProjectToProfile(EntryWith("IsolatedFolder", null)).SupersedeCompiledXml);
        Assert.False(ModRegistry.ProjectToProfile(EntryWith("IsolatedFolder", false)).SupersedeCompiledXml);
    }

    /// <summary>Declaring it is what turns it on — the Knights and Barbarians shape.</summary>
    [Fact]
    public void AModThatDeclaresItGetsIt()
    {
        Assert.True(ModRegistry.ProjectToProfile(EntryWith("IsolatedFolder", true)).SupersedeCompiledXml);
    }
}
