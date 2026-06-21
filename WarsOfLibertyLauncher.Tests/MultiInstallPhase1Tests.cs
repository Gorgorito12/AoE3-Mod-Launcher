using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Phase 1 of multi-install: sibling-path enumeration over all copies, and the
/// SAFETY-CRITICAL uninstall guard — an in-place overlay over the user's real
/// AoE3 must never be blanket-deleted.
/// </summary>
public class MultiInstallPhase1Tests : IDisposable
{
    private readonly List<string> _temp = new();
    private string NewDir()
    {
        var d = Directory.CreateTempSubdirectory("wol-phase1-test-").FullName;
        _temp.Add(d);
        return d;
    }
    public void Dispose()
    {
        foreach (var d in _temp) { try { Directory.Delete(d, recursive: true); } catch { } }
    }

    private static ModProfile Wol() => ModRegistry.Find(ModRegistry.WolId)!;

    private static void WriteProbe(string dir, ModProfile p)
    {
        var probe = Path.Combine(dir, p.InstallProbeFile);
        Directory.CreateDirectory(Path.GetDirectoryName(probe)!);
        File.WriteAllText(probe, "x");
    }

    private static void WriteManifest(string dir, bool cloned, params string[] overlayNetNew)
    {
        var m = new InstallManifest { ModId = "wol", InstallPath = dir, ClonedAoe3 = cloned };
        m.OverlayNetNew.AddRange(overlayNetNew);
        m.Save();
    }

    // ---- sibling / all-install enumeration ----

    [Fact]
    public void GetAllInstallPaths_IncludesActiveAndOtherCopies()
    {
        var cfg = new LauncherConfig();
        var st = cfg.GetState(ModRegistry.WolId);
        st.InstallPath = @"C:\WoL-1";
        st.OtherInstalls.Add(new ModInstall { InstallPath = @"C:\WoL-2" });

        var all = cfg.GetAllInstallPaths();

        Assert.Contains(@"C:\WoL-1", all);
        Assert.Contains(@"C:\WoL-2", all);
    }

    [Fact]
    public void GetSiblingInstallPaths_EnumeratesAllCopiesOfOtherMods_ExcludesCurrent()
    {
        var cfg = new LauncherConfig();
        var wol = cfg.GetState(ModRegistry.WolId);
        wol.InstallPath = @"C:\WoL-1";
        wol.OtherInstalls.Add(new ModInstall { InstallPath = @"C:\WoL-2" });

        // Excluding a DIFFERENT mod id → WoL (a built-in, non-stock) and BOTH its
        // copies are siblings.
        var siblings = cfg.GetSiblingInstallPaths("aoe3-tad");
        Assert.Contains(@"C:\WoL-1", siblings);
        Assert.Contains(@"C:\WoL-2", siblings);

        // Excluding WoL itself → none of its copies appear.
        var excludingWol = cfg.GetSiblingInstallPaths(ModRegistry.WolId);
        Assert.DoesNotContain(@"C:\WoL-1", excludingWol);
        Assert.DoesNotContain(@"C:\WoL-2", excludingWol);
    }

    // ---- uninstall safety guard ----

    [Fact]
    public void Plan_InPlaceOverlay_IsOverlayOnly_NotBlanketDelete()
    {
        var dir = NewDir();
        var wol = Wol();
        WriteProbe(dir, wol);
        WriteManifest(dir, cloned: false, "data/modfile1.xml", "data/modfile2.xml");

        var plan = new UninstallService().Plan(wol, dir);

        Assert.Equal(UninstallMode.Valid, plan.Mode);
        Assert.True(plan.OverlayOnly);          // the guard: NO recursive delete
        Assert.Equal(2, plan.FileCount);        // only the net-new overlay files
    }

    [Fact]
    public void Plan_ClonedInstall_IsBlanketDelete()
    {
        var dir = NewDir();
        var wol = Wol();
        WriteProbe(dir, wol);
        WriteManifest(dir, cloned: true, "data/modfile1.xml");

        var plan = new UninstallService().Plan(wol, dir);

        Assert.Equal(UninstallMode.Valid, plan.Mode);
        Assert.False(plan.OverlayOnly);         // launcher clone → normal delete
    }

    [Fact]
    public void Plan_StockGame_StillRefused()
    {
        var stock = ModRegistry.Find("aoe3-tad");
        Assert.NotNull(stock);                  // built-in stock profile must exist
        var dir = NewDir();

        var plan = new UninstallService().Plan(stock!, dir);

        Assert.Equal(UninstallMode.NotAValidInstall, plan.Mode);
    }
}
