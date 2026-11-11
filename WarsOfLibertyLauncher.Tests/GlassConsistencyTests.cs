using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Media;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// One transparency logic for the whole launcher (reported: changing the level moved the list and
/// left the tab bar, the search box and the chips beside it as they were). Every translucent navy
/// surface is a brush <see cref="GlassTheme"/> rewrites; a fill written by hand in XAML is how a
/// surface stops following, and nothing on screen says so until somebody changes the level.
/// </summary>
public class GlassConsistencyTests
{
    /// <summary>The navies glass is made of. A literal fill in one of them, with an alpha below
    /// FF, is a hand-written glass.</summary>
    private static readonly Regex HandWrittenFill = new(
        @"(?:Background|Value)=""#(?!FF)[0-9A-Fa-f]{2}(?:0A1321|0C1626|0B1422|080F1B|050A12|0F1C2E|12213A)""",
        RegexOptions.Compiled);

    /// <summary>Veils over the content while an overlay is up: they dim, they are not glass.</summary>
    private static readonly string[] Veils = { "LibraryShotViewer", "SheetLayer" };

    [Fact]
    public void THE_ONE_THAT_MATTERS_NoTranslucentNavyIsWrittenByHand()
    {
        var offenders = Directory.EnumerateFiles(LauncherDir(), "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                        && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (f, line, i)))
            .Where(x => HandWrittenFill.IsMatch(x.line) && !Veils.Any(v => Context(x.f, x.i).Contains(v)))
            .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}: {x.line.Trim()}")
            .ToList();
        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    /// <summary>At Media each surface keeps the value the XAML declares, which is the handoff's: the
    /// level code moves the other two levels and must not quietly change the default one.</summary>
    [Fact]
    public void EveryFollowerKeepsItsDeclaredValueAtMedia()
    {
        var xaml = string.Join("\n", new[] { "Styles/Library.xaml", "Styles/Colors.xaml" }
            .Select(f => File.ReadAllText(Path.Combine(LauncherDir(), f))));
        foreach (var (key, color, alpha, inset) in GlassTheme.Followers)
        {
            var match = Regex.Match(xaml, $@"x:Key=""{key}""\s+Color=""#([0-9A-Fa-f]{{8}})""");
            Assert.True(match.Success, $"{key} is not declared in the styles");
            var declared = (Color)ColorConverter.ConvertFromString("#" + match.Groups[1].Value);
            var want = Color.FromArgb((byte)Math.Round(GlassTheme.Follow(alpha, GlassTheme.Medium, inset) * 255), color.R, color.G, color.B);
            Assert.Equal(declared, want);
        }
    }

    [Fact]
    public void EachLevelMovesEveryFollowerTheWayThePanelMoves()
    {
        foreach (var (key, _, alpha, inset) in GlassTheme.Followers)
        {
            var solid = GlassTheme.Follow(alpha, GlassTheme.Solid, inset);
            var max = GlassTheme.Follow(alpha, GlassTheme.Maximum, inset);
            Assert.True(max < alpha, $"{key}: Maximum lets no more through");
            Assert.True(solid >= alpha, $"{key}: Solid lets more through");
            if (!inset) Assert.True(solid >= .86, $"{key}: Solid is less opaque than a panel");
        }
    }

    [Fact]
    public void ReducedEffectsMakeEveryGlassOpaqueAndKeepItsLook()
    {
        var glass = GlassTheme.For(GlassTheme.Medium).Panel;
        var settled = GlassTheme.Settle(glass, reducedEffects: true);
        Assert.Equal(255, settled.A);
        Assert.Equal(glass, GlassTheme.Settle(glass, reducedEffects: false));
        // What the panel looks like over the page's navy, made solid: darker than the navy, not grey.
        Assert.True(settled.B > settled.R && settled.B < GlassTheme.PageNavy.B + 1);
    }

    private static string Context(string file, int line)
    {
        var lines = File.ReadAllLines(file);
        return string.Join("\n", lines.Skip(Math.Max(0, line - 3)).Take(4));
    }

    private static string LauncherDir([CallerFilePath] string here = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "WarsOfLibertyLauncher"));
}
