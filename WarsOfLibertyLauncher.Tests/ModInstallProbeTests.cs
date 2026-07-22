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

    /// <summary>
    /// An IsolatedFolder install is a full AoE3 clone (bin\ flattened to root),
    /// so it always has the engine DLLs at the root. Detection now requires one,
    /// so a fixture that should read as a real install must lay it.
    /// </summary>
    private static void CreateEngineAt(string root)
        => CreateFileAt(root, "RockallDLL.dll");

    [Fact]
    public void IsolatedMod_WithProbeMarkerAndEngine_IsDetected_RegardlessOfFolderName()
    {
        // A WoL install in a folder named "MiWoL" — nothing like the DisplayName.
        var install = Path.Combine(NewTempDir(), "MiWoL");
        CreateFileAt(install, @"data\stringtabley.xml"); // probe
        CreateDirAt(install, @"art\zulushield");          // marker (a directory)
        CreateEngineAt(install);                          // cloned base engine

        Assert.True(ModInstallProbe.LooksLikeModInstall(install, WolLikeProfile()));
    }

    /// <summary>
    /// The Napoleonic Era bug: a folder with the mod's probe (and marker) but
    /// NO base game underneath is only the mod's overlay — a leftover manual
    /// download, not an install. Adopting it made the launcher offer a bogus
    /// "update" for a mod it never installed.
    /// </summary>
    [Fact]
    public void IsolatedMod_WithProbeButNoEngine_IsRejected()
    {
        var install = Path.Combine(NewTempDir(), "Napoleonic era");
        CreateFileAt(install, @"data\stringtabley.xml"); // probe
        CreateDirAt(install, @"art\zulushield");          // marker
        // no engine DLL — only the overlay

        Assert.False(ModInstallProbe.LooksLikeModInstall(install, WolLikeProfile()));
        Assert.Equal(ProbeOutcome.EngineMissing,
            ModInstallProbe.Inspect(install, WolLikeProfile()));
    }

    /// <summary>Any one of the engine DLLs is enough — a mod may not ship all four.</summary>
    [Theory]
    [InlineData("RockallDLL.dll")]
    [InlineData("binkw32.dll")]
    [InlineData("granny2.dll")]
    [InlineData("deformerdlly.dll")]
    public void IsolatedMod_AnySingleEngineFile_Satisfies(string engineDll)
    {
        var install = NewTempDir();
        CreateFileAt(install, @"data\stringtabley.xml");
        CreateDirAt(install, @"art\zulushield");
        CreateFileAt(install, engineDll);

        Assert.Equal(ProbeOutcome.Match, ModInstallProbe.Inspect(install, WolLikeProfile()));
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
    public void OverlayMod_WithExclusiveProbe_NoMarker_NoEngine_IsStillDetected()
    {
        // InPlaceOverlay installs INTO the base game, whose engine lives in bin\,
        // not at the install-path root — so the engine check does NOT apply. A
        // probe-only folder is a valid detection for this install type.
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
        // no engine DLL — must NOT matter for InPlaceOverlay

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
    public void Inspect_ReportsTheExactMissingSignal()
    {
        var profile = WolLikeProfile();

        // Probe + marker + engine → Match.
        var full = Path.Combine(NewTempDir(), "MiWoL");
        CreateFileAt(full, @"data\stringtabley.xml");
        CreateDirAt(full, @"art\zulushield");
        CreateEngineAt(full);
        Assert.Equal(ProbeOutcome.Match, ModInstallProbe.Inspect(full, profile));

        // Probe present, marker gone → MarkerMissing (looks like base AoE3 /
        // an install whose overlay was uninstalled). This is the real-world
        // rejection that used to surface as a blind "invalid folder".
        var markerless = Path.Combine(NewTempDir(), "Wars of Liberty");
        CreateFileAt(markerless, @"data\stringtabley.xml");
        Assert.Equal(ProbeOutcome.MarkerMissing, ModInstallProbe.Inspect(markerless, profile));

        // No probe at all → ProbeMissing.
        var empty = NewTempDir();
        Assert.Equal(ProbeOutcome.ProbeMissing, ModInstallProbe.Inspect(empty, profile));

        // Path doesn't exist → NotADirectory.
        var missing = Path.Combine(NewTempDir(), "does-not-exist");
        Assert.Equal(ProbeOutcome.NotADirectory, ModInstallProbe.Inspect(missing, profile));
    }

    [Fact]
    public void Inspect_OutcomeOrdering_MarkerMissingIsMoreInstallLikeThanProbeMissing()
    {
        // The manual picker keeps the "closest to a real install" reason across
        // candidates by comparing outcomes; this ordering is load-bearing for that.
        Assert.True(ProbeOutcome.Match > ProbeOutcome.EngineMissing);
        Assert.True(ProbeOutcome.EngineMissing > ProbeOutcome.MarkerMissing);
        Assert.True(ProbeOutcome.MarkerMissing > ProbeOutcome.ProbeMissing);
        Assert.True(ProbeOutcome.ProbeMissing > ProbeOutcome.NotADirectory);
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
