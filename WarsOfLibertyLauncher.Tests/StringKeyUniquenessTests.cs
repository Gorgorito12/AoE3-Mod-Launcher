using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// No localization key is declared twice in <c>Strings.cs</c>.
///
/// <para>The table is built with the dictionary INDEXER (<c>["key"] = new() {...}</c>),
/// so a repeated key is not a compile error and not a runtime one: the later entry
/// silently replaces the earlier, and the caller that expected the first one gets the
/// second. It fails as wrong TEXT on a button, which nothing else in the build, the
/// suite or a screenshot diff will call out as a bug — it just looks like somebody
/// wrote a strange label.</para>
///
/// <para>Found the hard way: a new short "Install" for the settings row was shadowed by
/// the dashboard's existing <c>BtnInstall</c> ("INSTALL MOD"), several hundred lines
/// further down, and the settings button rendered the dashboard's shouty caption.</para>
/// </summary>
public class StringKeyUniquenessTests
{
    [Fact]
    public void NoLocalizationKeyIsDeclaredTwice()
    {
        var path = FindStringsFile();
        var text = File.ReadAllText(path);

        // Only the top-level table entries: `        ["Key"] = new()`. Anything more
        // deeply indented belongs to something else.
        var rx = new Regex("^        \\[\"(?<k>[A-Za-z0-9_]+)\"\\] = new\\(\\)",
            RegexOptions.Multiline);

        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var dupes = new List<string>();
        foreach (Match m in rx.Matches(text))
        {
            var key = m.Groups["k"].Value;
            int line = text[..m.Index].Split('\n').Length;
            if (seen.TryGetValue(key, out var first)) dupes.Add($"{key} (lines {first} and {line})");
            else seen[key] = line;
        }

        // A guard that matched nothing would pass forever. The table is thousands of
        // entries; anything near zero means the pattern stopped matching the file.
        Assert.True(seen.Count > 1000,
            $"only {seen.Count} keys matched — the pattern no longer fits Strings.cs");

        Assert.True(dupes.Count == 0,
            "these keys are declared more than once, so the LAST one silently wins:\n  "
            + string.Join("\n  ", dupes));
    }

    private static string FindStringsFile()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++)
        {
            var candidate = Path.Combine(dir, "WarsOfLibertyLauncher", "Localization", "Strings.cs");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException("could not locate Strings.cs from " + AppContext.BaseDirectory);
    }
}
