using System.Linq;
using WarsOfLibertyLauncher.Models;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Regression tests for <see cref="LauncherConfig.GetSiblingInstallPaths"/> —
/// the "sibling mods to exclude from the AoE3 clone" list. The canonical bug:
/// the stock base game (aoe3-tad, IsStockGame) has its detected path = the
/// user's real AoE3 (…\bin), and that path was wrongly returned as a sibling
/// to exclude → the clone copied 0 base files → the mod shipped with no engine
/// DLLs and the game exited on launch. These tests pin the fixed behaviour.
/// </summary>
public class GetSiblingInstallPathsTests
{
    [Fact]
    public void StockGamePath_IsNeverReturnedAsSibling()
    {
        var config = new LauncherConfig();
        const string stockPath = @"C:\Games\Age Of Empires 3\bin";
        const string modPath = @"C:\Games\Age Of Empires 3\Wars of Liberty";

        // aoe3-tad is the built-in stock game (IsStockGame=true); wol is a
        // real mod. Both are in ModRegistry.All, so both are candidates.
        config.GetState("aoe3-tad").InstallPath = stockPath;
        config.GetState("wol").InstallPath = modPath;

        var siblings = config.GetSiblingInstallPaths("improvement-mod");

        Assert.DoesNotContain(stockPath, siblings); // the base game is what we CLONE — never exclude it
        Assert.Contains(modPath, siblings);         // a genuine sibling mod IS excluded
    }

    [Fact]
    public void ModBeingInstalled_IsNotReturnedAsItsOwnSibling()
    {
        var config = new LauncherConfig();
        const string wolPath = @"C:\Games\Age Of Empires 3\Wars of Liberty";
        config.GetState("wol").InstallPath = wolPath;

        var siblings = config.GetSiblingInstallPaths("wol");

        Assert.DoesNotContain(wolPath, siblings);
    }

    [Fact]
    public void EmptyInstallPaths_AreSkipped()
    {
        var config = new LauncherConfig();
        // No paths set anywhere → nothing to exclude (must not throw or emit
        // empty-string entries).
        var siblings = config.GetSiblingInstallPaths("improvement-mod");

        Assert.DoesNotContain(siblings, string.IsNullOrEmpty);
    }
}
