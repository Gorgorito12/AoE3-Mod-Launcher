using System;
using System.Collections.Generic;
using System.IO;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Tests for the packager's translated-file resolution. The translator picks
/// XML files directly (not a folder), and they may be renamed
/// (e.g. stringtabley_translated.xml) — the exporter must still match them to
/// the canonical covered file name so the pack overwrites the right game file.
/// </summary>
public class TranslationExportTests : IDisposable
{
    private readonly List<string> _temp = new();

    private string NewDir()
    {
        var d = Directory.CreateTempSubdirectory("trans-export-test-").FullName;
        _temp.Add(d);
        return d;
    }

    public void Dispose()
    {
        foreach (var d in _temp)
            try { Directory.Delete(d, recursive: true); } catch { }
    }

    [Fact]
    public void ResolveTranslatedFile_PrefersExactName()
    {
        var dir = NewDir();
        var exact = Path.Combine(dir, "stringtabley.xml");
        File.WriteAllText(exact, "x");
        File.WriteAllText(Path.Combine(dir, "stringtabley_translated.xml"), "y");

        Assert.Equal(exact, TranslationService.ResolveTranslatedFile(dir, "stringtabley.xml"));
    }

    [Fact]
    public void ResolveTranslatedFile_MatchesRenamedFile()
    {
        var dir = NewDir();
        var renamed = Path.Combine(dir, "stringtabley_translated.xml");
        File.WriteAllText(renamed, "y");

        // The user's exact case from the bug report.
        Assert.Equal(renamed, TranslationService.ResolveTranslatedFile(dir, "stringtabley.xml"));
    }

    [Fact]
    public void ResolveTranslatedFile_ReturnsNullWhenNothingMatches()
    {
        var dir = NewDir();
        File.WriteAllText(Path.Combine(dir, "unrelated.xml"), "z");

        Assert.Null(TranslationService.ResolveTranslatedFile(dir, "stringtabley.xml"));
    }

    // ---- ResolveFromList: explicit files the user picked (any path, any name) ----

    [Fact]
    public void ResolveFromList_MatchesRenamedFile()
    {
        var files = new[] { @"C:\x\trad\stringtabley_translated.xml" };
        Assert.Equal(files[0], TranslationService.ResolveFromList(files, "stringtabley.xml"));
    }

    [Fact]
    public void ResolveFromList_PrefersExactName()
    {
        var files = new[] { @"C:\x\stringtabley_translated.xml", @"C:\x\stringtabley.xml" };
        Assert.Equal(@"C:\x\stringtabley.xml",
            TranslationService.ResolveFromList(files, "stringtabley.xml"));
    }

    [Fact]
    public void ResolveFromList_NullWhenNameUnrelated()
    {
        var files = new[] { @"C:\x\es.xml" };  // doesn't contain "stringtabley"
        Assert.Null(TranslationService.ResolveFromList(files, "stringtabley.xml"));
    }
}
