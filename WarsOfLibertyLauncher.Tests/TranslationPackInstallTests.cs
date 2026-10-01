using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// <see cref="TranslationService.InstallPackFromZip"/> and the download guards in
/// <see cref="TranslationRegistryService"/>. The installer used to read the id out of the zip,
/// run <c>Directory.Delete(translations\&lt;id&gt;, recursive)</c> with it unchecked, and then
/// extract the whole archive. Now it extracts only the manifest and the covered files, counts the
/// bytes itself, and swaps the result in only when complete — so every refusal below also checks
/// that the PREVIOUS pack is still there, untouched.
/// </summary>
public class TranslationPackInstallTests : IDisposable
{
    private const string Spanish = "<StringTable><String _locID=\"1\">Hola</String></StringTable>";

    private readonly string _root = Directory.CreateTempSubdirectory("aoe3ml-txinstall-").FullName;
    private string Install => Path.Combine(_root, "install");
    private string PacksRoot => Path.Combine(Install, TranslationService.TranslationsFolderName);

    public TranslationPackInstallTests()
    {
        Directory.CreateDirectory(Path.Combine(Install, "data"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    /// <summary>MD5 of <see cref="Spanish"/> as the zip stores it — what a real packager records.</summary>
    private static readonly string SpanishMd5 =
        Convert.ToHexString(System.Security.Cryptography.MD5.HashData(new UTF8Encoding(false).GetBytes(Spanish)));

    /// <summary>
    /// Every listed file records <see cref="SpanishMd5"/>: staging verifies each extracted file
    /// against its <c>translatedHash</c>, and these tests are about the OTHER refusals.
    /// </summary>
    private static string ManifestJson(string id, params string[] paths)
    {
        var m = new TranslationManifest { Id = id, Name = id, Version = "1.0" };
        foreach (var p in paths) m.Files.Add(new TranslationFile { Path = p, TranslatedHash = SpanishMd5 });
        return JsonSerializer.Serialize(m);
    }

    private string Zip(params (string Name, string Content)[] entries)
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            using var w = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
            w.Write(content);
        }
        return path;
    }

    /// <summary>An already-installed pack, so a refusal can prove it was left alone.</summary>
    private string ExistingPack(string id)
    {
        var folder = Path.Combine(PacksRoot, id);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, TranslationManifest.ManifestFileName),
            ManifestJson(id, "data/stringtabley.xml"));
        File.WriteAllText(Path.Combine(folder, "stringtabley.xml"), "previous version");
        return folder;
    }

    private TranslationService Service => new(Install);

    private void AssertNoScratchLeft()
    {
        if (!Directory.Exists(PacksRoot)) return;
        Assert.DoesNotContain(Directory.GetDirectories(PacksRoot), d => Path.GetFileName(d).StartsWith('.'));
    }

    [Fact]
    public void AWellFormedPack_InstallsOnlyTheManifestAndTheCoveredFiles()
    {
        var zip = Zip(
            ("translation.json", ManifestJson("es", "data/stringtabley.xml")),
            ("stringtabley.xml", Spanish),
            ("readme.txt", "notes"),
            ("tool.exe", "MZ"),
            ("sub/stringtabley.xml", "nested copy"));

        var manifest = Service.InstallPackFromZip(zip, "es", CancellationToken.None);

        Assert.Equal("es", manifest.Id);
        var names = Directory.GetFiles(Path.Combine(PacksRoot, "es")).Select(Path.GetFileName).OrderBy(n => n);
        Assert.Equal(new[] { "stringtabley.xml", "translation.json" }, names);
        Assert.Equal(Spanish, File.ReadAllText(Path.Combine(PacksRoot, "es", "stringtabley.xml")));
        AssertNoScratchLeft();
    }

    /// <summary>A zip entry that climbs out is never written anywhere, whatever the manifest says.</summary>
    [Fact]
    public void ATraversalEntryIsNeverWritten()
    {
        var zip = Zip(
            ("translation.json", ManifestJson("es", "data/stringtabley.xml", "../evil.txt")),
            ("stringtabley.xml", Spanish),
            ("../evil.txt", "pwned"),
            ("../../evil.txt", "pwned"));

        Service.InstallPackFromZip(zip, "es", CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(_root, "evil.txt")));
        Assert.False(File.Exists(Path.Combine(Install, "evil.txt")));
        Assert.False(File.Exists(Path.Combine(PacksRoot, "evil.txt")));
        Assert.False(File.Exists(Path.Combine(PacksRoot, "es", "evil.txt")));
    }

    /// <summary>
    /// THE ONE THAT MATTERS: the id is what the old installer handed to a recursive delete. An id
    /// that climbs must be refused before anything on disk is touched.
    /// </summary>
    [Theory]
    [InlineData(@"..\..\sentinel")]
    [InlineData("..")]
    [InlineData("_originals")]
    public void AnUnsafeId_IsRefusedAndDeletesNothing(string id)
    {
        var sentinel = Path.Combine(_root, "sentinel");
        Directory.CreateDirectory(sentinel);
        File.WriteAllText(Path.Combine(sentinel, "keep.txt"), "keep");
        var previous = ExistingPack("es");
        var zip = Zip(("translation.json", ManifestJson(id, "data/stringtabley.xml")), ("stringtabley.xml", Spanish));

        Assert.Throws<InvalidDataException>(() => Service.InstallPackFromZip(zip, null, CancellationToken.None));

        Assert.True(File.Exists(Path.Combine(sentinel, "keep.txt")));
        Assert.Equal("previous version", File.ReadAllText(Path.Combine(previous, "stringtabley.xml")));
        AssertNoScratchLeft();
    }

    [Fact]
    public void AnIdOtherThanTheOneChosen_IsRefused()
    {
        var zip = Zip(("translation.json", ManifestJson("fr", "data/stringtabley.xml")), ("stringtabley.xml", Spanish));

        Assert.Throws<InvalidDataException>(() => Service.InstallPackFromZip(zip, "es", CancellationToken.None));
        Assert.False(Directory.Exists(Path.Combine(PacksRoot, "fr")));
    }

    [Fact]
    public void APackWithNoCoveredFile_IsRefused()
    {
        var zip = Zip(("translation.json", ManifestJson("es", "data/protoy.xml")), ("protoy.xml", "tampered"));

        Assert.Throws<InvalidDataException>(() => Service.InstallPackFromZip(zip, "es", CancellationToken.None));
        Assert.False(Directory.Exists(Path.Combine(PacksRoot, "es")));
    }

    [Fact]
    public void ADuplicatedEntry_IsRefusedAndThePreviousPackSurvives()
    {
        var previous = ExistingPack("es");
        var zip = Zip(
            ("translation.json", ManifestJson("es", "data/stringtabley.xml")),
            ("stringtabley.xml", Spanish),
            ("stringtabley.xml", "second copy"));

        Assert.Throws<InvalidDataException>(() => Service.InstallPackFromZip(zip, "es", CancellationToken.None));
        Assert.Equal("previous version", File.ReadAllText(Path.Combine(previous, "stringtabley.xml")));
        AssertNoScratchLeft();
    }

    [Fact]
    public void ADuplicatedManifest_IsRefused()
    {
        var zip = Zip(
            ("translation.json", ManifestJson("es", "data/stringtabley.xml")),
            ("TRANSLATION.JSON", ManifestJson("fr", "data/stringtabley.xml")),
            ("stringtabley.xml", Spanish));

        Assert.Throws<InvalidDataException>(() => Service.InstallPackFromZip(zip, "es", CancellationToken.None));
    }

    [Fact]
    public void AModThatAcceptsNoTranslations_InstallsNothing()
    {
        var zip = Zip(("translation.json", ManifestJson("es", "data/stringtabley.xml")), ("stringtabley.xml", Spanish));
        var stockGame = new ModProfile { Id = "aoe3-tad", Translations = null };

        Assert.Throws<InvalidOperationException>(() =>
            TranslationService.ForProfile(Install, stockGame).InstallPackFromZip(zip, "es", CancellationToken.None));
        Assert.False(Directory.Exists(Path.Combine(PacksRoot, "es")));
    }

    /// <summary>
    /// A highly compressible entry that inflates past the per-file limit: the zip is tiny, the
    /// extraction is not. Refused while copying, and the previous pack is still there.
    /// </summary>
    [Fact]
    public void AnEntryThatInflatesPastTheLimit_IsRefusedAndThePreviousPackSurvives()
    {
        var previous = ExistingPack("es");
        var path = Path.Combine(_root, "bomb.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using (var w = new StreamWriter(archive.CreateEntry("translation.json").Open()))
                w.Write(ManifestJson("es", "data/stringtabley.xml"));
            using var s = archive.CreateEntry("stringtabley.xml", CompressionLevel.Optimal).Open();
            var zeros = new byte[1024 * 1024];
            for (long written = 0; written <= TranslationService.MaxPackEntryBytes; written += zeros.Length)
                s.Write(zeros, 0, zeros.Length);
        }

        Assert.Throws<InvalidDataException>(() => Service.InstallPackFromZip(path, "es", CancellationToken.None));
        Assert.Equal("previous version", File.ReadAllText(Path.Combine(previous, "stringtabley.xml")));
        AssertNoScratchLeft();
    }

    [Fact]
    public void ReinstallingReplacesThePreviousVersionAndLeavesNoScratch()
    {
        ExistingPack("es");
        var zip = Zip(("translation.json", ManifestJson("es", "data/stringtabley.xml")), ("stringtabley.xml", Spanish));

        Service.InstallPackFromZip(zip, "es", CancellationToken.None);

        Assert.Equal(Spanish, File.ReadAllText(Path.Combine(PacksRoot, "es", "stringtabley.xml")));
        AssertNoScratchLeft();
    }

    // ------ CopyCapped

    [Fact]
    public void CopyCapped_RefusesPastThePerFileLimit()
    {
        using var src = new MemoryStream(new byte[101]);
        Assert.Throws<InvalidDataException>(() =>
            TranslationService.CopyCapped(src, Stream.Null, 100, 1000, "x", CancellationToken.None));
    }

    [Fact]
    public void CopyCapped_RefusesPastTheRemainingTotal()
    {
        using var src = new MemoryStream(new byte[60]);
        Assert.Throws<InvalidDataException>(() =>
            TranslationService.CopyCapped(src, Stream.Null, 100, 50, "x", CancellationToken.None));
    }

    [Fact]
    public void CopyCapped_AcceptsExactlyTheLimit()
    {
        using var src = new MemoryStream(new byte[100]);
        Assert.Equal(100, TranslationService.CopyCapped(src, Stream.Null, 100, 100, "x", CancellationToken.None));
    }

    // ------ Download guards (TranslationRegistryService)

    [Theory]
    [InlineData("http://raw.githubusercontent.com/o/r/main/x.zip")]  // https only
    [InlineData("ftp://example.com/x.zip")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/x.zip")]
    [InlineData("https://user:pass@example.com/x.zip")]               // credentials disguise the host
    [InlineData("")]
    [InlineData("translations/es/1.0/x.zip")]                          // relative
    public void PackUrl_RefusesAnythingButAPlainHttpsLink(string url)
        => Assert.False(TranslationRegistryService.IsAllowedPackUrl(url));

    [Fact]
    public void PackUrl_AcceptsTheRawGitHubLinksTheFolderScanBuilds()
    {
        Assert.True(TranslationRegistryService.IsAllowedPackUrl(
            "https://raw.githubusercontent.com/Gorgorito12/translations/main/translations/ES-LA/1.1/wol-ES-LA.zip"));
        Assert.False(TranslationRegistryService.IsAllowedPackUrl(null));
    }

    [Fact]
    public async Task CopyCappedAsync_RefusesPastTheLimit()
    {
        using var src = new MemoryStream(new byte[101]);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            TranslationRegistryService.CopyCappedAsync(src, Stream.Null, 100, CancellationToken.None));
    }

    /// <summary>A share link that answers with a web page must be reported as one, not unzipped.</summary>
    [Fact]
    public void StartsLikeZip_RefusesAnHtmlPageAndAcceptsAZip()
    {
        var html = Path.Combine(_root, "page.zip");
        File.WriteAllText(html, "<!DOCTYPE html><html><body>Sign in</body></html>");
        Assert.False(TranslationRegistryService.StartsLikeZip(html));

        var zip = Zip(("translation.json", "{}"));
        Assert.True(TranslationRegistryService.StartsLikeZip(zip));

        Assert.False(TranslationRegistryService.StartsLikeZip(Path.Combine(_root, "missing.zip")));
    }
}
