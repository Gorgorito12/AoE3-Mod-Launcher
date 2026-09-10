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

    /// <summary>
    /// The two paths 19b measured, which are the whole reason the install dialog was
    /// redrawn: at 560 px the source lost its END and the destination lost its START —
    /// drive letter included — so a window whose only job was to have you confirm two
    /// paths confirmed neither. The root and the last folder must survive whatever else
    /// is dropped.
    /// </summary>
    [Fact]
    public void SplitForDisplay_KeepsTheDriveAndTheFolderYouAreChoosing()
    {
        var (r1, m1, l1) = PathDisplay.SplitForDisplay(
            @"C:\Program Files (x86)\Steam\steamapps\common\Age Of Empires 3");
        Assert.Equal(@"C:\", r1);
        Assert.Equal("Age Of Empires 3", l1);
        Assert.Contains("…", m1);
        Assert.EndsWith(@"common\", m1);          // the folders nearest the leaf survive
        Assert.DoesNotContain("Program Files", m1); // the ones furthest from it do not

        var (r2, _, l2) = PathDisplay.SplitForDisplay(
            @"C:\Program Files (x86)\Steam\steamapps\common\Age Of Empires 3\Knights and Barbarians");
        Assert.Equal(@"C:\", r2);
        Assert.Equal("Knights and Barbarians", l2);
    }

    /// <summary>A path that already fits keeps every segment and gains no ellipsis.</summary>
    [Fact]
    public void SplitForDisplay_LeavesAShortPathWhole()
    {
        var (root, mid, leaf) = PathDisplay.SplitForDisplay(@"D:\Games\Knights and Barbarians");
        Assert.Equal(@"D:\", root);
        Assert.Equal(@"Games\", mid);
        Assert.Equal("Knights and Barbarians", leaf);
        Assert.DoesNotContain("…", mid);
    }

    /// <summary>
    /// The refusals. A blank, a bare drive and a path with nothing between root and leaf
    /// all have to come out as something the dialog can paint rather than as an exception
    /// or an ellipsis standing on its own.
    /// </summary>
    [Theory]
    [InlineData(null, "", "", "")]
    [InlineData("", "", "", "")]
    [InlineData(@"C:\", @"C:\", "", "")]
    [InlineData(@"C:\Games", @"C:\", "", "Games")]
    public void SplitForDisplay_HandlesTheEdges(string? input, string root, string mid, string leaf)
    {
        var r = PathDisplay.SplitForDisplay(input);
        Assert.Equal(root, r.Root);
        Assert.Equal(mid, r.Middle);
        Assert.Equal(leaf, r.Leaf);
    }

    /// <summary>
    /// At least one middle segment always survives, even when it alone blows the budget:
    /// "C:\ … \Name" tells you nothing that "C:\Name" would not have, so an ellipsis that
    /// replaces everything is worse than a middle that runs a little long.
    /// </summary>
    [Fact]
    public void SplitForDisplay_NeverElidesTheWholeMiddle()
    {
        var (_, mid, _) = PathDisplay.SplitForDisplay(
            @"C:\a\an-extremely-long-single-folder-name-that-will-not-fit\Leaf", maxMiddleChars: 10);
        Assert.Contains("an-extremely-long-single-folder-name-that-will-not-fit", mid);
        Assert.Contains("…", mid);   // and the ones before it are still marked as dropped
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
