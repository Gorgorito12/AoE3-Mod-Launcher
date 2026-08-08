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

    // ---- config.GameExecutable cache scoping -------------------------------
    //
    // The cache is a single launcher-wide field shared by every profile, and every
    // WoL/AoE3 folder ships the same filename — so a filename match alone let a path
    // from an install the user had moved away from win over the mod's own exe. Seen
    // in a real bundle: the launcher patched and verified '…\Wars of Liberty
    // ORIGINAL\bin' while PLAY launched '…\Age Of Empires 3\Wars of Liberty\age3y.exe'.

    [Fact]
    public void ModLaunch_WithCachedExeOutsideInstall_LaunchesModsOwnExe()
    {
        var modFolder = NewDirWithExe("age3y.exe");    // the install the launcher manages
        var otherCopy = NewDirWithExe("age3y.exe");    // a stale copy left in the cache

        var config = new LauncherConfig { GameExecutable = Path.Combine(otherCopy, "age3y.exe") };
        var wol = new ModProfile { Id = "wol", GameExecutable = "age3y.exe" };

        var resolved = GameLauncher.Find(config, modInstallPath: modFolder, profile: wol);

        Assert.Equal(Path.Combine(modFolder, "age3y.exe"), resolved);
    }

    [Fact]
    public void ModLaunch_WithCachedExeInsideInstall_StillPrefersTheCache()
    {
        // The cache keeps its priority for the case it exists for: the exe of the
        // very install we're launching (here in a bin\ subfolder, Steam layout).
        var modFolder = Directory.CreateTempSubdirectory("wol-find-test-").FullName;
        _tempDirs.Add(modFolder);
        var bin = Directory.CreateDirectory(Path.Combine(modFolder, "bin")).FullName;
        File.WriteAllText(Path.Combine(bin, "age3y.exe"), "");

        var config = new LauncherConfig { GameExecutable = Path.Combine(bin, "age3y.exe") };
        var wol = new ModProfile { Id = "wol", GameExecutable = "age3y.exe" };

        var resolved = GameLauncher.Find(config, modInstallPath: modFolder, profile: wol);

        Assert.Equal(Path.Combine(bin, "age3y.exe"), resolved);
    }

    [Theory]
    // Base-game resolution (no install folder) keeps trusting the cache — that is
    // what lets a manually-pointed, non-standard AoE3 resolve at all.
    [InlineData(@"C:\Games\AoE3\age3y.exe", "age3y.exe", null, true)]
    // Wrong filename never matches, install folder or not.
    [InlineData(@"C:\Games\AoE3\age3y.exe", "age3m.exe", null, false)]
    [InlineData(@"C:\Mod\age3y.exe", "age3m.exe", @"C:\Mod", false)]
    // Inside the install (directly, and in a subfolder) → usable.
    [InlineData(@"C:\Mod\age3y.exe", "age3y.exe", @"C:\Mod", true)]
    [InlineData(@"C:\Mod\bin\age3y.exe", "age3y.exe", @"C:\Mod", true)]
    [InlineData(@"C:\Mod\age3y.exe", "age3y.exe", @"C:\Mod\", true)]
    // Outside the install → rejected, including the sibling-with-a-shared-prefix
    // case a naive StartsWith would wrongly accept.
    [InlineData(@"C:\Other\age3y.exe", "age3y.exe", @"C:\Mod", false)]
    [InlineData(@"C:\Mod2\age3y.exe", "age3y.exe", @"C:\Mod", false)]
    // The install folder itself is not an exe inside it.
    [InlineData(@"C:\Mod", "age3y.exe", @"C:\Mod", false)]
    // Nothing cached.
    [InlineData("", "age3y.exe", @"C:\Mod", false)]
    [InlineData(null, "age3y.exe", null, false)]
    public void CachedExeIsUsable_ScopesTheCacheToTheActiveInstall(
        string? cachedExe, string exeName, string? modInstallPath, bool expected)
    {
        Assert.Equal(expected, GameLauncher.CachedExeIsUsable(cachedExe, exeName, modInstallPath));
    }
}
