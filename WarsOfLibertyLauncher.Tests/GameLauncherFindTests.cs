using System;
using System.Collections.Generic;
using System.IO;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Regression tests for the interaction between the durable
/// <see cref="LauncherConfig.Aoe3ManualPath"/> (base-game fallback) and
/// <see cref="GameLauncher.Find"/> / <see cref="GameLauncher.FindAoe3Install"/>.
///
/// The load-bearing rule: <c>Aoe3ManualPath</c> is a BASE-game resolver and must
/// NEVER hijack a MOD launch. WoL is an isolated clone with its OWN
/// <c>age3y.exe</c>; if the manual base path (also <c>age3y.exe</c>) were preferred
/// over the mod's own folder, PLAY would launch vanilla AoE3 instead of the mod.
/// The candidate is gated on <c>modInstallPath</c> being empty.
/// </summary>
public class GameLauncherFindTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private string NewDirWithExe(string exeName)
    {
        var dir = Directory.CreateTempSubdirectory("wol-find-test-").FullName;
        _tempDirs.Add(dir);
        File.WriteAllText(Path.Combine(dir, exeName), "");   // a real file for File.Exists
        return dir;
    }

    public void Dispose()
    {
        foreach (var d in _tempDirs)
            try { Directory.Delete(d, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void ModLaunch_WithManualBasePathSet_LaunchesModsOwnExe_NotBase()
    {
        // Both folders hold age3y.exe: one is the WoL clone, one is the base AoE3
        // the user pinned via "Change AoE3 folder".
        var modFolder = NewDirWithExe("age3y.exe");
        var baseFolder = NewDirWithExe("age3y.exe");

        var config = new LauncherConfig
        {
            GameExecutable = "",                 // cleared (e.g. right after a mod switch)
            Aoe3ManualPath = baseFolder,         // durable base pin
        };
        var wol = new ModProfile
        {
            Id = "wol",
            DisplayName = "Wars of Liberty",
            GameExecutable = "age3y.exe",
            IsStockGame = false,
        };

        var resolved = GameLauncher.Find(config, modInstallPath: modFolder, profile: wol);

        Assert.Equal(Path.Combine(modFolder, "age3y.exe"), resolved);
        Assert.NotEqual(Path.Combine(baseFolder, "age3y.exe"), resolved);
    }

    [Fact]
    public void BaseResolution_WithNoModFolder_UsesManualBasePath()
    {
        // The stock-game / general-badge path passes modInstallPath: null, so the
        // manual base pin IS the source of truth there.
        var baseFolder = NewDirWithExe("age3y.exe");
        var config = new LauncherConfig
        {
            GameExecutable = "",
            Aoe3ManualPath = baseFolder,
        };

        var resolved = GameLauncher.FindAoe3Install(config);

        Assert.Equal(Path.Combine(baseFolder, "age3y.exe"), resolved);
    }
}
