using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The release scripts must read the same under Windows PowerShell 5.1 as under PowerShell 7.
///
/// <para><b>Why it is a byte-level rule.</b> 5.1 reads a script with no BOM in the system ANSI
/// code page (cp1252 on the maintainer's PCs). A UTF-8 em dash is E2 80 94, and 0x94 in cp1252
/// is U+201D, a closing curly quote, which PowerShell accepts as a quote. So a dash inside a
/// double-quoted string ended that string early and <c>build-release.ps1</c> did not parse at all
/// under 5.1 — while it parsed with 0 errors under 7, where it was always tried. Nothing in the
/// build or the tests would ever notice. The files carry no BOM and the repo has no
/// <c>.gitattributes</c> or <c>.editorconfig</c> that would keep one, so the rule is: ASCII only,
/// or a BOM.</para>
/// </summary>
public class PowerShellScriptsTests
{
    [Fact]
    public void EveryScriptReadsTheSameUnderWindowsPowerShell51()
    {
        var root = RepoRoot();
        var scripts = Directory.EnumerateFiles(root, "*.ps1", SearchOption.AllDirectories)
            .Where(p => !IsSkipped(Path.GetRelativePath(root, p)))
            .ToList();

        // Not vacuous: the four the release depends on are found.
        Assert.Contains(scripts, p => p.EndsWith("build-release.ps1", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(scripts, p => p.EndsWith("publish.ps1", StringComparison.OrdinalIgnoreCase));

        var offenders = new List<string>();
        foreach (var path in scripts)
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) continue;
            var bad = Array.FindIndex(bytes, b => b >= 0x80);
            if (bad < 0) continue;
            var line = 1 + bytes.Take(bad).Count(b => b == (byte)'\n');
            offenders.Add($"{Path.GetRelativePath(root, path)}:{line}");
        }

        Assert.True(offenders.Count == 0,
            "These scripts hold a non-ASCII byte and no BOM, so Windows PowerShell 5.1 reads them "
            + "in the ANSI code page, where a UTF-8 dash ends in a curly quote: "
            + string.Join(", ", offenders));
    }

    private static bool IsSkipped(string relative)
    {
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(p => p is "bin" or "obj" or "publish" or ".git" or "node_modules");
    }

    /// <summary>The folder holding WarsOfLibertyLauncher/App.xaml, walking up as AccountChipTests does.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "WarsOfLibertyLauncher", "App.xaml")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not find the repository root above " + AppContext.BaseDirectory);
    }
}
