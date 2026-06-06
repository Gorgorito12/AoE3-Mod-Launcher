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
///   * <see cref="NativeInstallService.RemoveStaleBuildArtifacts"/> must KEEP
///     every <c>.xml.xmb</c> — the canonical install ships them and AoE3 hashes
///     them for its LAN version match — while still stripping inert dev junk
///     (<c>.bak</c>). Deleting the .xmb made launcher installs the odd one out.
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
    public void RemoveStaleBuildArtifacts_KeepsXmlXmb_ButStripsBak()
    {
        var install = NewTempDir();
        Write(install, @"data\randomnames.xml.xmb");   // precompiled — MUST survive
        Write(install, @"data\proto\stuff.xml.xmb");   // nested .xml.xmb — MUST survive
        Write(install, @"data\techtreey.xml.bak");     // editor backup — must go
        Write(install, @"data\protoy.xml");            // real data — MUST survive

        NativeInstallService.RemoveStaleBuildArtifacts(WolProfile(), install);

        Assert.True(File.Exists(Path.Combine(install, @"data\randomnames.xml.xmb")),
            ".xml.xmb must be kept (parity with the official build's LAN hash).");
        Assert.True(File.Exists(Path.Combine(install, @"data\proto\stuff.xml.xmb")));
        Assert.True(File.Exists(Path.Combine(install, @"data\protoy.xml")));
        Assert.False(File.Exists(Path.Combine(install, @"data\techtreey.xml.bak")),
            ".bak backups are inert dev junk and should still be stripped.");
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
