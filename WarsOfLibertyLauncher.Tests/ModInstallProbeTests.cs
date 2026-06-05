using System;
using System.Collections.Generic;
using System.IO;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Regression tests for <see cref="ModInstallProbe"/> — the content-based mod
/// install detection that replaced the old "install folder must be named after
/// the mod" heuristic. The canonical bug: WoL was only detected when its folder
/// was literally named "Wars of Liberty"; renaming it (or installing under any
/// other name) made the launcher report it as not installed. Detection now goes
/// by content (probe file + optional marker), so the folder name is irrelevant,
/// while the marker still tells a real WoL folder apart from vanilla AoE3 (whose
/// data\ files satisfy the probe too).
/// </summary>
public class ModInstallProbeTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private string NewTempDir()
    {
        var dir = Directory.CreateTempSubdirectory("wol-probe-test-").FullName;
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

    private static ModProfile WolLikeProfile() => new()
    {
        Id = "wol",
        DisplayName = "Wars of Liberty",
        InstallType = ModInstallType.IsolatedFolder,
        InstallProbeFile = @"data\stringtabley.xml",
        InstallMarker = @"art\zulushield",
    };

    private static void CreateFileAt(string root, string relative)
    {
        var full = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "x");
    }

    private static void CreateDirAt(string root, string relative)
        => Directory.CreateDirectory(Path.Combine(root, relative));

    [Fact]
    public void IsolatedMod_WithProbeAndMarker_IsDetected_RegardlessOfFolderName()
    {
        // A WoL install in a folder named "MiWoL" — nothing like the DisplayName.
        var install = Path.Combine(NewTempDir(), "MiWoL");
        CreateFileAt(install, @"data\stringtabley.xml"); // probe
        CreateDirAt(install, @"art\zulushield");          // marker (a directory)

        Assert.True(ModInstallProbe.LooksLikeModInstall(install, WolLikeProfile()));
    }

    [Fact]
    public void IsolatedMod_WithProbeButNoMarker_IsRejected_EvenIfNamedLikeTheMod()
    {
        // Mimics vanilla AoE3: it carries the probe file but NOT the WoL-only
        // marker. Even with the folder named exactly "Wars of Liberty" it must
        // be rejected — the marker, not the name, is the signal.
        var install = Path.Combine(NewTempDir(), "Wars of Liberty");
        CreateFileAt(install, @"data\stringtabley.xml"); // probe present
        // no art\zulushield marker

        Assert.False(ModInstallProbe.LooksLikeModInstall(install, WolLikeProfile()));
    }

    [Fact]
    public void OverlayMod_WithExclusiveProbe_NoMarker_IsDetected()
    {
        // Improvement-Mod-style: overlay, probe is its own exclusive .exe, no
        // marker declared. Detection rides the probe alone, under any name.
        var profile = new ModProfile
        {
            Id = "improvement-mod",
            DisplayName = "Improvement Mod",
            InstallType = ModInstallType.InPlaceOverlay,
            InstallProbeFile = "age3m.exe",
            InstallMarker = "",
        };
        var install = Path.Combine(NewTempDir(), "AnyName");
        CreateFileAt(install, "age3m.exe");

        Assert.True(ModInstallProbe.LooksLikeModInstall(install, profile));
    }

    [Fact]
    public void MissingProbe_IsRejected()
    {
        var install = NewTempDir(); // empty folder, no probe file
        Assert.False(ModInstallProbe.LooksLikeModInstall(install, WolLikeProfile()));
    }

    [Fact]
    public void NonExistentFolder_IsRejected()
    {
        var install = Path.Combine(NewTempDir(), "does-not-exist");
        Assert.False(ModInstallProbe.LooksLikeModInstall(install, WolLikeProfile()));
    }

    [Fact]
    public void MarkerExists_MatchesBothFileAndDirectory()
    {
        var dirMarker = NewTempDir();
        CreateDirAt(dirMarker, @"art\zulushield");
        Assert.True(ModInstallProbe.MarkerExists(dirMarker, @"art\zulushield"));

        var fileMarker = NewTempDir();
        CreateFileAt(fileMarker, @"some\marker.flag");
        Assert.True(ModInstallProbe.MarkerExists(fileMarker, @"some\marker.flag"));

        Assert.False(ModInstallProbe.MarkerExists(dirMarker, @"art\missing"));
        Assert.False(ModInstallProbe.MarkerExists(dirMarker, "")); // empty marker → false
    }
}
