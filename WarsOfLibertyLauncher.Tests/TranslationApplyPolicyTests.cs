using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// <see cref="TranslationService.Apply"/> writes only the files the MOD covers, and only to the
/// path the mod declares. It used to copy every <c>files[].path</c> the pack listed to
/// <c>Path.Combine(install, path)</c>, so a pack from a repository the player added could write
/// anywhere on the disk. Each test builds a real install in a temp folder and looks at what
/// actually landed — including OUTSIDE the install, which is the half that matters.
/// </summary>
public class TranslationApplyPolicyTests : IDisposable
{
    private const string English = "<StringTable><String _locID=\"1\">Hello</String></StringTable>";
    private const string Spanish = "<StringTable><String _locID=\"1\">Hola</String></StringTable>";

    private readonly string _root = Directory.CreateTempSubdirectory("aoe3ml-txapply-").FullName;
    private string Install => Path.Combine(_root, "install");
    private string LiveStringTable => Path.Combine(Install, "data", "stringtabley.xml");

    public TranslationApplyPolicyTests()
    {
        Directory.CreateDirectory(Path.Combine(Install, "data"));
        File.WriteAllText(LiveStringTable, English);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    /// <summary>Lays a pack straight into <c>translations\&lt;id&gt;\</c>, as an older build would have.</summary>
    private void InstallPack(string id, params (string ManifestPath, string FileName, string Content)[] files)
    {
        var folder = Path.Combine(Install, TranslationService.TranslationsFolderName, id);
        Directory.CreateDirectory(folder);
        var manifest = new TranslationManifest { Id = id, Name = id, Version = "1.0" };
        foreach (var (path, name, content) in files)
        {
            File.WriteAllText(Path.Combine(folder, name), content);
            manifest.Files.Add(new TranslationFile { Path = path });
        }
        File.WriteAllText(Path.Combine(folder, TranslationManifest.ManifestFileName),
            JsonSerializer.Serialize(manifest));
    }

    private static ModProfile Participating(params string[] covered) => new()
    {
        Id = "wol",
        Translations = new TranslationsSettings { CoveredFiles = covered.ToList() },
    };

    /// <summary>
    /// THE ONE THAT MATTERS: a pack mixing a real translation with a climb out of the install.
    /// The translation lands; the climb lands nowhere.
    /// </summary>
    [Fact]
    public void AHostileFileBesideAGoodOne_OnlyTheCoveredFileIsWritten()
    {
        InstallPack("es",
            ("data/stringtabley.xml", "stringtabley.xml", Spanish),
            ("../evil.txt", "evil.txt", "pwned"));

        var result = new TranslationService(Install).Apply("es");

        Assert.True(result.Success);
        Assert.Equal(Spanish, File.ReadAllText(LiveStringTable));
        Assert.False(File.Exists(Path.Combine(_root, "evil.txt")));
        Assert.False(File.Exists(Path.Combine(Install, "evil.txt")));
    }

    [Fact]
    public void ARootedPathIsNeverWritten()
    {
        var outside = Path.Combine(_root, "outside.xml");
        InstallPack("es", (outside, "outside.xml", "pwned"));

        var result = new TranslationService(Install).Apply("es");

        Assert.False(result.Success);
        Assert.False(File.Exists(outside));
    }

    [Fact]
    public void APackNamingOnlyUncoveredFiles_FailsAndTouchesNothing()
    {
        var proto = Path.Combine(Install, "data", "protoy.xml");
        File.WriteAllText(proto, "original proto");
        InstallPack("es", ("data/protoy.xml", "protoy.xml", "tampered proto"));

        var result = new TranslationService(Install).Apply("es");

        Assert.False(result.Success);
        Assert.Equal("original proto", File.ReadAllText(proto));
    }

    /// <summary>
    /// A mod with no Translations block declares nothing. Without the gate the covered list fell
    /// back to WoL's, so a pack could overwrite another game's string table.
    /// </summary>
    [Fact]
    public void AProfileWithNoTranslationsBlock_AppliesNothing()
    {
        InstallPack("es", ("data/stringtabley.xml", "stringtabley.xml", Spanish));
        var stockGame = new ModProfile { Id = "aoe3-tad", Translations = null };

        var result = TranslationService.ForProfile(Install, stockGame).Apply("es");

        Assert.False(result.Success);
        Assert.Equal(English, File.ReadAllText(LiveStringTable));
    }

    /// <summary>An empty covered list keeps meaning "the WoL default", as it always has.</summary>
    [Fact]
    public void AProfileWithAnEmptyCoveredList_KeepsTheDefault()
    {
        InstallPack("es", ("data/stringtabley.xml", "stringtabley.xml", Spanish));

        var result = TranslationService.ForProfile(Install, Participating()).Apply("es");

        Assert.True(result.Success);
        Assert.Equal(Spanish, File.ReadAllText(LiveStringTable));
    }

    [Fact]
    public void APackCoveringAFileTheModDoesNotCover_IsRefusedForThatMod()
    {
        InstallPack("es", ("data/unithelpstringsy.xml", "unithelpstringsy.xml", "ayuda"));

        var result = TranslationService.ForProfile(Install, Participating(@"data\stringtabley.xml")).Apply("es");

        Assert.False(result.Success);
        Assert.False(File.Exists(Path.Combine(Install, "data", "unithelpstringsy.xml")));
    }

    [Fact]
    public void TwoFilesWithTheSameName_AreRefused()
    {
        InstallPack("es",
            ("data/stringtabley.xml", "stringtabley.xml", Spanish),
            ("other/stringtabley.xml", "stringtabley.xml", Spanish));

        Assert.False(new TranslationService(Install).Apply("es").Success);
        Assert.Equal(English, File.ReadAllText(LiveStringTable));
    }

    [Fact]
    public void ACaseVariantWritesTheCoveredFile()
    {
        InstallPack("es", ("DATA/STRINGTABLEY.XML", "stringtabley.xml", Spanish));

        Assert.True(new TranslationService(Install).Apply("es").Success);
        Assert.Equal(Spanish, File.ReadAllText(LiveStringTable));
    }

    [Theory]
    [InlineData("..")]
    [InlineData(@"..\..\x")]
    [InlineData("_originals")]
    public void AnUnsafeId_IsRefusedBeforeTheDiskIsRead(string id)
        => Assert.False(new TranslationService(Install).Apply(id).Success);

    /// <summary>
    /// Apply reads <c>translations\&lt;id&gt;\</c>, so a folder whose manifest names another id
    /// would let one pack's files be applied under the other's name.
    /// </summary>
    [Fact]
    public void AFolderWhoseManifestNamesAnotherId_IsNotAPack()
    {
        InstallPack("foo", ("data/stringtabley.xml", "stringtabley.xml", Spanish));
        var manifestPath = Path.Combine(Install, "translations", "foo", TranslationManifest.ManifestFileName);
        File.WriteAllText(manifestPath, File.ReadAllText(manifestPath).Replace("\"foo\"", "\"bar\""));

        var ts = new TranslationService(Install);

        Assert.Empty(ts.ListInstalled());
        Assert.Null(ts.GetInstalled("bar"));
        Assert.False(ts.Apply("bar").Success);
    }

    [Fact]
    public void TheInstallersScratchFolders_AreNeverListedAsPacks()
    {
        var scratch = Path.Combine(Install, "translations", ".incoming-abc");
        Directory.CreateDirectory(scratch);
        File.WriteAllText(Path.Combine(scratch, TranslationManifest.ManifestFileName),
            JsonSerializer.Serialize(new TranslationManifest { Id = ".incoming-abc" }));

        Assert.Empty(new TranslationService(Install).ListInstalled());
    }
}
