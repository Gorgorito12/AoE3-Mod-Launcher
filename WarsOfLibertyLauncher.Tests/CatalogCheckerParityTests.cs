using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The catalog's release check (<c>aoe3-mods-catalog-template/.github/scripts/check_releases.py</c>)
/// re-implements the launcher's asset selection in Python. Both read the SAME cases file, so the
/// day one side changes the rule the other's tests fail — a check that disagreed with the launcher
/// would open issues against modders for releases that install fine, or bless ones that do not.
/// </summary>
public class CatalogCheckerParityTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "WarsOfLibertyLauncher", "App.xaml"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not find the repository root.");
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_TheLauncherPicksWhatTheCatalogCheckExpects()
    {
        var path = Path.Combine(RepoRoot(), "aoe3-mods-catalog-template", ".github", "scripts", "check_releases_cases.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var cases = doc.RootElement.GetProperty("cases").EnumerateArray().ToList();
        Assert.True(cases.Count >= 10, "the cases file lost its cases");

        foreach (var c in cases)
        {
            var name = c.GetProperty("name").GetString();
            var assets = c.GetProperty("assets").EnumerateArray().Select(a => a.GetString() ?? "").ToList();
            var nonPayload = c.TryGetProperty("nonPayload", out var np) ? np.GetString() : null;
            int? pick = c.GetProperty("pick").ValueKind == JsonValueKind.Null ? null : c.GetProperty("pick").GetInt32();
            var parts = c.GetProperty("parts").EnumerateArray().Select(p => p.GetInt32()).ToList();

            Assert.True(pick == GitHubReleaseDownloader.PickAssetIndex(assets, null, nonPayload), $"pick: {name}");
            Assert.True(parts.SequenceEqual(GitHubReleaseDownloader.PickPayloadPartIndices(assets, null, nonPayload)), $"parts: {name}");
            if (parts.Count == 0)
            {
                bool unusable = c.GetProperty("unusable").GetBoolean();
                Assert.True(unusable == (GitHubReleaseDownloader.DescribeUnusablePartSet(assets) != null), $"unusable: {name}");
            }
        }
    }
}
