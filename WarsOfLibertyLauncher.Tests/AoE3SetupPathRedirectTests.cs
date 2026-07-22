using System;
using System.Collections.Generic;
using System.IO;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Tests for the setuppath junction-redirect (<see cref="AoE3SetupPathRedirect"/>),
/// which lets a total conversion shipping the STOCK <c>age3y.exe</c> (e.g. Struggle
/// of Indonesia) load from its own folder by junctioning the folder the registry
/// <c>setuppath</c> points at (the real <c>bin\</c>) at the mod's folder around
/// launch. Exercises the real filesystem with temp folders. Load-bearing invariant:
/// the real setup (bin\) contents are NEVER lost, and the service refuses to junction
/// where no real folder exists (unlike the My Games variant, bin\ must already exist).
/// </summary>
public class AoE3SetupPathRedirectTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private string NewRoot()
    {
        var dir = Directory.CreateTempSubdirectory("wol-setuppath-").FullName;
        _tempDirs.Add(dir);
        return dir;
    }

    public void Dispose()
    {
        foreach (var d in _tempDirs)
            try { Directory.Delete(d, recursive: true); } catch { /* best effort */ }
    }

    private const string Aside = " (AoE3 vanilla)";

    [Fact]
    public void Redirect_ThenRestore_PreservesRealBin()
    {
        var root = NewRoot();
        // A real "bin" with a base-game marker, and the mod's cloned folder.
        var bin = Path.Combine(root, "bin");
        var mod = Path.Combine(root, "Struggle of Indonesia");
        Directory.CreateDirectory(bin);
        Directory.CreateDirectory(mod);
        File.WriteAllText(Path.Combine(bin, "DataPY.bar"), "vanilla");
        File.WriteAllText(Path.Combine(mod, "DataPY.bar"), "indonesia");

        // Redirect: bin becomes a junction → mod folder; the real bin parks aside.
        Assert.True(AoE3SetupPathRedirect.EnsureRedirectedAt(bin, mod));
        Assert.True(AoE3SetupPathRedirect.IsJunction(bin));
        Assert.True(File.Exists(bin + Aside + "\\DataPY.bar"));                // real bin preserved aside
        // Reading through the junction sees the MOD's content.
        Assert.Equal("indonesia", File.ReadAllText(Path.Combine(bin, "DataPY.bar")));

        // Restore: junction gone, real bin back with its data intact.
        AoE3SetupPathRedirect.EnsureDefaultAt(bin);
        Assert.False(AoE3SetupPathRedirect.IsJunction(bin));
        Assert.True(Directory.Exists(bin));
        Assert.Equal("vanilla", File.ReadAllText(Path.Combine(bin, "DataPY.bar")));   // base data survived
        Assert.False(Directory.Exists(bin + Aside));                          // aside consumed
        Assert.Equal("indonesia", File.ReadAllText(Path.Combine(mod, "DataPY.bar"))); // mod data still isolated
    }

    [Fact]
    public void Redirect_IsIdempotent()
    {
        var root = NewRoot();
        var bin = Path.Combine(root, "bin");
        var mod = Path.Combine(root, "mod");
        Directory.CreateDirectory(bin);
        Directory.CreateDirectory(mod);

        Assert.True(AoE3SetupPathRedirect.EnsureRedirectedAt(bin, mod));
        Assert.True(AoE3SetupPathRedirect.EnsureRedirectedAt(bin, mod));   // second call is a no-op
        Assert.True(AoE3SetupPathRedirect.IsJunction(bin));
    }

    [Fact]
    public void EnsureDefault_WhenNothingRedirected_IsNoOp()
    {
        var root = NewRoot();
        var bin = Path.Combine(root, "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "x.bar"), "x");

        AoE3SetupPathRedirect.EnsureDefaultAt(bin);   // no junction, no aside
        Assert.False(AoE3SetupPathRedirect.IsJunction(bin));
        Assert.True(File.Exists(Path.Combine(bin, "x.bar")));
    }

    [Fact]
    public void Redirect_WhenSetupPathMissing_BailsWithoutJunction()
    {
        // Unlike the My Games variant, the setup folder (bin\) must already exist —
        // we won't create a junction where the real base game should be.
        var root = NewRoot();
        var bin = Path.Combine(root, "bin");   // NOT created
        var mod = Path.Combine(root, "mod");
        Directory.CreateDirectory(mod);

        Assert.False(AoE3SetupPathRedirect.EnsureRedirectedAt(bin, mod));
        Assert.False(AoE3SetupPathRedirect.IsJunction(bin));
        Assert.False(Directory.Exists(bin));
    }

    [Fact]
    public void Redirect_WhenAsideAlreadyExists_BailsWithoutClobbering()
    {
        // A prior restore didn't finish (aside present) — leave the real bin intact
        // rather than move a second copy aside and clobber the parked one.
        var root = NewRoot();
        var bin = Path.Combine(root, "bin");
        var mod = Path.Combine(root, "mod");
        Directory.CreateDirectory(bin);
        Directory.CreateDirectory(mod);
        Directory.CreateDirectory(bin + Aside);
        File.WriteAllText(bin + Aside + "\\parked.bar", "parked");
        File.WriteAllText(Path.Combine(bin, "live.bar"), "live");

        Assert.False(AoE3SetupPathRedirect.EnsureRedirectedAt(bin, mod));
        Assert.False(AoE3SetupPathRedirect.IsJunction(bin));
        Assert.True(File.Exists(Path.Combine(bin, "live.bar")));          // real bin untouched
        Assert.True(File.Exists(bin + Aside + "\\parked.bar"));           // parked copy untouched
    }
}
