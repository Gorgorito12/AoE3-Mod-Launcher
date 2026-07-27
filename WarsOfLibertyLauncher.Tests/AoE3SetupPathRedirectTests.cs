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
    public void EnsureDefault_LeavesAForeignJunctionAlone()
    {
        // The one that matters. Users junction game folders themselves — moving an
        // install to another drive is the usual reason — and the old code deleted ANY
        // reparse point it found at the setup path. With no aside to restore it also
        // left the path GONE: no AoE3 where the registry says AoE3 lives. The aside
        // folder is the ownership proof, so a link without one must survive untouched.
        var root = NewRoot();
        var bin = Path.Combine(root, "bin");
        var elsewhere = Path.Combine(root, "moved-to-another-drive");
        Directory.CreateDirectory(elsewhere);
        File.WriteAllText(Path.Combine(elsewhere, "age3y.exe"), "the real game");

        // The user's own junction: bin → elsewhere, and NO "(AoE3 vanilla)" aside.
        MakeJunction(bin, elsewhere);
        Assert.True(AoE3SetupPathRedirect.IsJunction(bin));
        Assert.False(Directory.Exists(bin + Aside));

        AoE3SetupPathRedirect.EnsureDefaultAt(bin);

        Assert.True(AoE3SetupPathRedirect.IsJunction(bin));                       // still linked
        Assert.Equal("the real game", File.ReadAllText(Path.Combine(bin, "age3y.exe")));
    }

    [Fact]
    public void EnsureDefault_RestoresEvenWhenTheJunctionTargetIsGone()
    {
        // The recovery path that actually got exercised: the mod folder was deleted while
        // the junction was live, leaving `bin` pointing at nothing. That breaks the BASE
        // GAME too — the registry setuppath resolves to a dangling link — so the startup
        // self-heal has to handle it. It can: on a dangling junction Directory.Exists
        // still returns true and the attributes still carry ReparsePoint, so IsJunction
        // sees it. Don't "optimise" IsJunction into anything that enumerates the folder.
        var root = NewRoot();
        var bin = Path.Combine(root, "bin");
        var mod = Path.Combine(root, "mod");
        Directory.CreateDirectory(bin);
        Directory.CreateDirectory(mod);
        File.WriteAllText(Path.Combine(bin, "age3y.exe"), "the real game");

        Assert.True(AoE3SetupPathRedirect.EnsureRedirectedAt(bin, mod));
        Directory.Delete(mod, recursive: true);          // the target vanishes
        Assert.True(AoE3SetupPathRedirect.IsJunction(bin));

        AoE3SetupPathRedirect.EnsureDefaultAt(bin);

        Assert.False(AoE3SetupPathRedirect.IsJunction(bin));
        Assert.Equal("the real game", File.ReadAllText(Path.Combine(bin, "age3y.exe")));
        Assert.False(Directory.Exists(bin + Aside));
    }

    [Fact]
    public void EnsureDefault_NeverLeavesTheSetupPathMissing()
    {
        // Same failure seen from the other side: whatever happens, something must be
        // reachable at the setup path when we're done.
        var root = NewRoot();
        var bin = Path.Combine(root, "bin");
        var mod = Path.Combine(root, "mod");
        Directory.CreateDirectory(mod);
        MakeJunction(bin, mod);                       // junction, no aside

        AoE3SetupPathRedirect.EnsureDefaultAt(bin);

        Assert.True(Directory.Exists(bin));
    }

    /// <summary>A junction created outside the service, to stand in for the user's own.</summary>
    private static void MakeJunction(string link, string target)
    {
        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c mklink /J \"{link}\" \"{target}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });
        p!.WaitForExit(10_000);
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
