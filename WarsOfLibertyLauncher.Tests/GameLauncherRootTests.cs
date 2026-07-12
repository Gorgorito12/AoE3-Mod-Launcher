using System;
using System.Collections.Generic;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Tests for <see cref="GameLauncher.DeriveAoe3RootFromExe"/> — the pure
/// exe→root derivation that lets a manually-pointed / non-standard AoE3 (which
/// <see cref="AoE3Detector.FindAll"/> can't auto-locate) resolve the base-game
/// root for the detect-only stock <c>aoe3-tad</c> profile. The
/// <c>dirExists</c> callback is injected so the logic runs without the filesystem.
/// </summary>
public class GameLauncherRootTests
{
    // A dirExists that reports the given set of directories as present.
    private static Func<string, bool> Dirs(params string[] present)
    {
        var set = new HashSet<string>(present, StringComparer.OrdinalIgnoreCase);
        return set.Contains;
    }

    [Fact]
    public void SteamLayout_BinExe_ReturnsParentWithData()
    {
        // age3y.exe in bin\, data\ one level up (the reported Complete Collection).
        const string exe = @"D:\Program Files (x86)\Microsoft Studios\Age of Empires III - Complete Collection\bin\age3y.exe";
        const string root = @"D:\Program Files (x86)\Microsoft Studios\Age of Empires III - Complete Collection";
        var root2 = GameLauncher.DeriveAoe3RootFromExe(exe, Dirs(root + @"\data"));
        Assert.Equal(root, root2);
    }

    [Fact]
    public void RetailLayout_FlatExe_ReturnsExeFolder()
    {
        // age3y.exe and data\ share a folder (older retail layout).
        const string exe = @"C:\Games\Age of Empires III\age3y.exe";
        const string root = @"C:\Games\Age of Empires III";
        var got = GameLauncher.DeriveAoe3RootFromExe(exe, Dirs(root + @"\data"));
        Assert.Equal(root, got);
    }

    [Fact]
    public void BinExe_NoDataAtParent_FallsBackToExeFolder()
    {
        // bin\age3y.exe but data\ sits inside bin\ (unusual) — exe folder wins.
        const string exe = @"E:\AoE3\bin\age3y.exe";
        var got = GameLauncher.DeriveAoe3RootFromExe(exe, Dirs(@"E:\AoE3\bin\data"));
        Assert.Equal(@"E:\AoE3\bin", got);
    }

    [Fact]
    public void NoDataAnywhere_ReturnsNull()
    {
        const string exe = @"F:\NotAGame\bin\age3y.exe";
        var got = GameLauncher.DeriveAoe3RootFromExe(exe, Dirs(/* nothing */));
        Assert.Null(got);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankExe_ReturnsNull(string exe)
    {
        Assert.Null(GameLauncher.DeriveAoe3RootFromExe(exe, _ => true));
    }
}
