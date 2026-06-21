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
}
