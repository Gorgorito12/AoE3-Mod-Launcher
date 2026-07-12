using System;
using System.Collections.Generic;
using System.IO;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Tests for the My Games junction-redirect (<see cref="AoE3UserDataRedirect"/>).
/// Exercises the real filesystem with a temp "My Games" root: a real vanilla folder
/// is moved aside and the standard name becomes a junction to the mod's exclusive
/// folder; restore removes the junction and brings the real vanilla folder back.
/// Load-bearing invariant checked: the real folder's contents are NEVER lost.
/// </summary>
public class AoE3UserDataRedirectTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private string NewRoot()
    {
        var dir = Directory.CreateTempSubdirectory("wol-mygames-").FullName;
        _tempDirs.Add(dir);
        return dir;
    }

    public void Dispose()
    {
        foreach (var d in _tempDirs)
            try { Directory.Delete(d, recursive: true); } catch { /* best effort */ }
    }

    private const string Std = "Age of Empires 3";
    private const string Aside = "Age of Empires 3 (AoE3 vanilla)";
    private const string Target = "Age of Empires 3 (King's Return)";

    [Fact]
    public void Redirect_ThenRestore_PreservesRealVanillaData()
    {
        var root = NewRoot();
        // A real vanilla save folder with a marker file.
        var std = Path.Combine(root, Std);
        Directory.CreateDirectory(std);
        File.WriteAllText(Path.Combine(std, "vanilla-save.txt"), "vanilla");

        // Redirect: std becomes a junction → target; the real folder parks aside.
        var ok = AoE3UserDataRedirect.EnsureRedirectedIn(root, Target);
        Assert.True(ok);
        Assert.True(AoE3UserDataRedirect.IsJunction(std));                        // std is now a junction
        Assert.True(Directory.Exists(Path.Combine(root, Target)));                // exclusive folder exists
        Assert.True(File.Exists(Path.Combine(root, Aside, "vanilla-save.txt")));  // real data preserved aside

        // A "King's Return" write lands in the target (through the junction).
        File.WriteAllText(Path.Combine(std, "kr-save.txt"), "kr");
        Assert.True(File.Exists(Path.Combine(root, Target, "kr-save.txt")));

        // Restore: junction gone, real vanilla folder back with its data intact.
        AoE3UserDataRedirect.EnsureDefaultIn(root);
        Assert.False(AoE3UserDataRedirect.IsJunction(std));
        Assert.True(Directory.Exists(std));
        Assert.True(File.Exists(Path.Combine(std, "vanilla-save.txt")));          // vanilla data survived
        Assert.False(Directory.Exists(Path.Combine(root, Aside)));                // aside consumed
        Assert.True(File.Exists(Path.Combine(root, Target, "kr-save.txt")));      // KR data still isolated
    }

    [Fact]
    public void Redirect_IsIdempotent()
    {
        var root = NewRoot();
        Directory.CreateDirectory(Path.Combine(root, Std));
        Assert.True(AoE3UserDataRedirect.EnsureRedirectedIn(root, Target));
        Assert.True(AoE3UserDataRedirect.EnsureRedirectedIn(root, Target));   // second call is a no-op
        Assert.True(AoE3UserDataRedirect.IsJunction(Path.Combine(root, Std)));
    }

    [Fact]
    public void EnsureDefault_WhenNothingRedirected_IsNoOp()
    {
        var root = NewRoot();
        var std = Path.Combine(root, Std);
        Directory.CreateDirectory(std);
        File.WriteAllText(Path.Combine(std, "x.txt"), "x");

        AoE3UserDataRedirect.EnsureDefaultIn(root);   // no junction, no aside
        Assert.False(AoE3UserDataRedirect.IsJunction(std));
        Assert.True(File.Exists(Path.Combine(std, "x.txt")));
    }

    [Fact]
    public void Redirect_WhenNoVanillaFolder_JustCreatesJunction()
    {
        var root = NewRoot();   // no "Age of Empires 3" yet
        Assert.True(AoE3UserDataRedirect.EnsureRedirectedIn(root, Target));
        Assert.True(AoE3UserDataRedirect.IsJunction(Path.Combine(root, Std)));
        Assert.False(Directory.Exists(Path.Combine(root, Aside)));   // nothing to park aside
    }
}
