using System;
using System.Linq;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins the pure path→display helpers behind the install-copy switcher: parent-folder
/// disambiguation and middle-ellipsis path compaction (which must keep the distinguishing
/// tail, unlike WPF's end-trim). See MainWindow's AppendInstallCopiesToModPopup.
/// </summary>
public class PathDisplayTests
{
    [Theory]
    [InlineData(@"C:\Games\Steam\Wars of Liberty (2)", "Steam")]
    [InlineData(@"D:\Mods\Wars of Liberty (2)", "Mods")]
    [InlineData(@"C:\Games\Steam\Wars of Liberty (2)\", "Steam")]  // trailing slash tolerated
    [InlineData("", "")]
    public void ParentFolderName_ReturnsParentLeaf(string path, string expected)
        => Assert.Equal(expected, PathDisplay.ParentFolderName(path));

    [Fact]
    public void CompactPathMiddle_NoOp_WhenItFits()
        => Assert.Equal(@"C:\A\B", PathDisplay.CompactPathMiddle(@"C:\A\B"));

    [Fact]
    public void CompactPathMiddle_KeepsHeadAndTail_WhenLong()
    {
        var p = @"C:\Program Files (x86)\Steam\steamapps\common\AoE3\Wars of Liberty (2)";

        var r = PathDisplay.CompactPathMiddle(p, 40);

        Assert.True(r.Length <= 40, $"length {r.Length}");
        Assert.Contains("…", r);
        Assert.EndsWith("Wars of Liberty (2)", r);   // the distinguishing tail survives
        Assert.StartsWith(@"C:\Pro", r);             // a recognizable head survives
    }

    [Fact]
    public void DisambiguateLabels_MakesEveryLabelUnique()
    {
        var items = new (string Label, string Path)[]
        {
            ("Wars of Liberty (2)", @"C:\Games\AoE3\Wars of Liberty (2)"),
            ("Wars of Liberty (2)", @"D:\Mods\Wars of Liberty (2)"),      // diff parent → parent suffix
            ("Wars of Liberty (2)", @"C:\Games\AoE3\Wars of Liberty (2)"),// same parent as #0 → needs #N
            ("Solo", @"C:\Solo"),
        };

        var r = PathDisplay.DisambiguateLabels(items);

        Assert.Equal(r.Count, r.Distinct(StringComparer.OrdinalIgnoreCase).Count());  // all unique
        Assert.Equal("Solo", r[3]);                                                    // unique label untouched
        Assert.Contains("Mods", r[1]);                                                 // parent suffix applied
    }
}
