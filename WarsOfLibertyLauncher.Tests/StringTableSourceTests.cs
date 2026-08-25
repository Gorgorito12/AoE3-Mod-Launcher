using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Guards the string table against a key being declared twice.
///
/// <para><b>Why this reads the SOURCE instead of the table.</b> <c>Strings.Table</c> is built with
/// indexer syntax (<c>["Key"] = new() { ... }</c>), which does NOT throw on a repeat — it
/// overwrites. So by the time a test could look at the dictionary the evidence is gone: the loser
/// has already been replaced by the winner and the two look like one entry. The compiler cannot
/// see it either. The file on disk is the only place the duplicate still exists.</para>
///
/// <para>It found six, and they were not all harmless. <c>BtnCancel</c> and <c>BtnClose</c> each
/// had a sentence-case definition silently replaced by an ALL-CAPS one, so every Cancel button in
/// the launcher shouted next to a neighbouring "Guardar cambios". Worse, <c>MenuOpenModFolder</c>
/// had its generic "Abrir carpeta del mod" replaced by "Abrir carpeta de Wars of Liberty" — a
/// hardcoded mod name shown while the user was in a different mod entirely.</para>
/// </summary>
public class StringTableSourceTests
{
    [Fact]
    public void NoKeyIsDeclaredTwice()
    {
        var source = File.ReadAllText(FindStringsFile());

        // Only top-level entries of the table: `["Key"] = new()` at the start of a line. The
        // per-language `[LangEn] = "..."` lines inside each entry use identifiers, not string
        // literals, so they cannot match.
        var keys = Regex.Matches(source, @"^\s*\[""(?<key>[A-Za-z0-9_]+)""\]\s*=\s*new\(",
                                 RegexOptions.Multiline)
                        .Select(m => m.Groups["key"].Value)
                        .ToList();

        // If this trips, the regex stopped matching the table's shape rather than the table
        // becoming empty — a silent pass would make the whole guard useless.
        Assert.True(keys.Count > 500, $"only matched {keys.Count} keys; the pattern is stale");

        var duplicates = keys.GroupBy(k => k, StringComparer.Ordinal)
                             .Where(g => g.Count() > 1)
                             .Select(g => $"{g.Key} (x{g.Count()})")
                             .OrderBy(s => s, StringComparer.Ordinal)
                             .ToList();

        Assert.True(duplicates.Count == 0,
            "These keys are declared more than once. The later one silently wins and the earlier "
            + "is dead: " + string.Join(", ", duplicates));
    }

    /// <summary>
    /// Walks up from the test assembly until the repository is recognisable. Done by looking for
    /// the file itself rather than for a marker like the .sln, so a rename of the surrounding
    /// layout fails loudly here instead of quietly skipping the check.
    /// </summary>
    private static string FindStringsFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(
                dir.FullName, "WarsOfLibertyLauncher", "Localization", "Strings.cs");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            "Could not find WarsOfLibertyLauncher/Localization/Strings.cs above "
            + AppContext.BaseDirectory);
    }
}
