using System;
using System.IO;
using System.Text.Json;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// <see cref="TranslationService.AssessApplied"/> and <see cref="TranslationService.RevertOrConfirmEnglish"/>:
/// what the DISK says about the pack the config calls active. Written for a real player's bundle —
/// the config said Spanish, the live table was English, there was no <c>_originals</c>, the card
/// read "In use" and couldn't be clicked, and "English" failed with "cannot revert". The caller
/// clears the note only on <see cref="TranslationAppliedState.NotApplied"/>, so every way to get a
/// false NotApplied is a way to lose a working translation — Mixed and Unknown are pinned too.
/// </summary>
public class TranslationAppliedAssessmentTests : IDisposable
{
    private const string English = "<StringTable>Hello</StringTable>";
    private const string Spanish = "<StringTable>Hola</StringTable>";
    private const string EnglishX = "<StringTable>Units</StringTable>";
    private const string SpanishX = "<StringTable>Unidades</StringTable>";

    private readonly string _install = Directory.CreateTempSubdirectory("aoe3ml-txassess-").FullName;

    private static readonly ModProfile Wol = new()
    {
        Id = "wol",
        Translations = new TranslationsSettings
        {
            CoveredFiles = new() { @"data\stringtabley.xml", @"data\stringtablex.xml" },
        },
    };

    private TranslationService Service => TranslationService.ForProfile(_install, Wol);
    private string Live(string name) => Path.Combine(_install, "data", name);
    private string PackFolder => Path.Combine(_install, TranslationService.TranslationsFolderName, "ES-LA");
    private string Originals => Path.Combine(_install, TranslationService.TranslationsFolderName, "_originals");

    public TranslationAppliedAssessmentTests()
    {
        Directory.CreateDirectory(Path.Combine(_install, "data"));
        File.WriteAllText(Live("stringtabley.xml"), English);
        File.WriteAllText(Live("stringtablex.xml"), EnglishX);
    }

    public void Dispose()
    {
        try { Directory.Delete(_install, recursive: true); } catch { }
    }

    private void InstallPack(params (string Path, string Content)[] files)
    {
        Directory.CreateDirectory(PackFolder);
        var manifest = new TranslationManifest { Id = "ES-LA", Name = "Español", Version = "1.2.0e-r2" };
        foreach (var (path, content) in files)
        {
            manifest.Files.Add(new TranslationFile { Path = path });
            File.WriteAllText(Path.Combine(PackFolder, System.IO.Path.GetFileName(path)), content);
        }
        File.WriteAllText(Path.Combine(PackFolder, TranslationManifest.ManifestFileName), JsonSerializer.Serialize(manifest));
    }

    private void Snapshot(string y, string x)
    {
        Directory.CreateDirectory(Originals);
        File.WriteAllText(Path.Combine(Originals, "stringtabley.xml"), y);
        File.WriteAllText(Path.Combine(Originals, "stringtablex.xml"), x);
    }

    /// <summary>THE PLAYER'S CASE: no pack, no snapshot, English live — the note is stale.</summary>
    [Fact]
    public void NoPackAndNoSnapshot_IsNotApplied()
        => Assert.Equal(TranslationAppliedState.NotApplied, Service.AssessApplied("ES-LA"));

    [Fact]
    public void TheLiveFilesAreThePack_IsApplied()
    {
        InstallPack(("data/stringtabley.xml", Spanish), ("data/stringtablex.xml", SpanishX));
        File.WriteAllText(Live("stringtabley.xml"), Spanish);
        File.WriteAllText(Live("stringtablex.xml"), SpanishX);

        Assert.Equal(TranslationAppliedState.Applied, Service.AssessApplied("ES-LA"));
    }

    /// <summary>An update or a repair put English back over a pack that is still installed.</summary>
    [Fact]
    public void ThePackIsInstalledButNoLiveFileIsIt_IsNotApplied()
    {
        InstallPack(("data/stringtabley.xml", Spanish), ("data/stringtablex.xml", SpanishX));
        Assert.Equal(TranslationAppliedState.NotApplied, Service.AssessApplied("ES-LA"));
    }

    /// <summary>Half of it is live: not proof of anything, so the caller leaves it alone.</summary>
    [Fact]
    public void SomeLiveFilesAreThePack_IsMixed()
    {
        InstallPack(("data/stringtabley.xml", Spanish), ("data/stringtablex.xml", SpanishX));
        File.WriteAllText(Live("stringtabley.xml"), Spanish);

        Assert.Equal(TranslationAppliedState.Mixed, Service.AssessApplied("ES-LA"));
    }

    [Fact]
    public void AMissingLiveFile_CountsAsNotThePack()
    {
        InstallPack(("data/stringtabley.xml", Spanish));
        File.Delete(Live("stringtabley.xml"));
        Assert.Equal(TranslationAppliedState.NotApplied, Service.AssessApplied("ES-LA"));
    }

    /// <summary>A file the pack may not replace was never copied, so it is no evidence.</summary>
    [Fact]
    public void AnUncoveredFile_IsIgnored()
    {
        InstallPack(("data/stringtabley.xml", Spanish), ("data/protoy.xml", "<tampered/>"));
        File.WriteAllText(Live("stringtabley.xml"), Spanish);
        File.WriteAllText(Live("protoy.xml"), "<proto/>");

        Assert.Equal(TranslationAppliedState.Applied, Service.AssessApplied("ES-LA"));
    }

    /// <summary>The game holding the file open is not a verdict.</summary>
    [Fact]
    public void ALockedLiveFile_IsUnknown()
    {
        InstallPack(("data/stringtabley.xml", Spanish));
        File.WriteAllText(Live("stringtabley.xml"), Spanish);
        using var held = new FileStream(Live("stringtabley.xml"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.Equal(TranslationAppliedState.Unknown, Service.AssessApplied("ES-LA"));
    }

    /// <summary>No pack to compare, and the live files aren't the English snapshot: can't tell.</summary>
    [Fact]
    public void NoPack_AndTheLiveFilesDifferFromTheSnapshot_IsUnknown()
    {
        Snapshot(English, EnglishX);
        File.WriteAllText(Live("stringtabley.xml"), Spanish);
        Assert.Equal(TranslationAppliedState.Unknown, Service.AssessApplied("ES-LA"));
    }

    [Fact]
    public void NoPack_AndTheLiveFilesAreTheSnapshot_IsNotApplied()
    {
        Snapshot(English, EnglishX);
        Assert.Equal(TranslationAppliedState.NotApplied, Service.AssessApplied("ES-LA"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(@"..\..\x")]
    public void NoOrAnUnsafeId_IsJudgedWithoutAPack(string? id)
        => Assert.Equal(TranslationAppliedState.NotApplied, Service.AssessApplied(id));

    // ------ "Back to English"

    [Fact]
    public void WithASnapshot_EnglishIsRestored()
    {
        Snapshot(English, EnglishX);
        File.WriteAllText(Live("stringtabley.xml"), Spanish);

        Assert.Equal(RevertOutcome.Reverted, Service.RevertOrConfirmEnglish("ES-LA"));
        Assert.Equal(English, File.ReadAllText(Live("stringtabley.xml")));
    }

    /// <summary>THE PLAYER'S CASE: nothing to restore, but nothing to undo either — that's success.</summary>
    [Fact]
    public void WithoutASnapshot_ButThePackNotOnDisk_IsAlreadyEnglish()
        => Assert.Equal(RevertOutcome.AlreadyEnglish, Service.RevertOrConfirmEnglish("ES-LA"));

    [Fact]
    public void WithoutASnapshot_AndThePackLive_Fails()
    {
        InstallPack(("data/stringtabley.xml", Spanish));
        File.WriteAllText(Live("stringtabley.xml"), Spanish);

        Assert.Equal(RevertOutcome.Failed, Service.RevertOrConfirmEnglish("ES-LA"));
        Assert.Equal(Spanish, File.ReadAllText(Live("stringtabley.xml")));
    }

    [Fact]
    public void WithoutASnapshot_AndNoActivePack_Fails()
        => Assert.Equal(RevertOutcome.Failed, Service.RevertOrConfirmEnglish(""));
}
