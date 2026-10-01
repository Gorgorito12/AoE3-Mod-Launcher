using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Stage → apply → promote (<see cref="TranslationService.StagePackFromZip"/>,
/// <see cref="TranslationService.ApplyStaged"/>, <see cref="TranslationService.PromoteStaged"/>).
/// A download is verified in a scratch folder against what the LISTING promised — the id, the
/// mod, every file's MD5, the content hash — and becomes <c>translations\&lt;id&gt;</c> only after
/// it applied. Every refusal below also checks that the previously installed pack is untouched,
/// because with one card per translator several packs share that folder.
/// </summary>
public class TranslationStagingTests : IDisposable
{
    private const string Spanish = "<StringTable><String _locID=\"1\">Hola</String></StringTable>";
    private const string English = "<StringTable><String _locID=\"1\">Hello</String></StringTable>";

    private readonly string _root = Directory.CreateTempSubdirectory("aoe3ml-txstage-").FullName;
    private string Install => Path.Combine(_root, "install");
    private string PacksRoot => Path.Combine(Install, TranslationService.TranslationsFolderName);
    private string LiveTable => Path.Combine(Install, "data", "stringtabley.xml");

    private static readonly ModProfile Wol = new()
    {
        Id = "wol",
        Translations = new TranslationsSettings { CoveredFiles = new() { @"data\stringtabley.xml" } },
    };

    private TranslationService Service => TranslationService.ForProfile(Install, Wol);

    public TranslationStagingTests()
    {
        Directory.CreateDirectory(Path.Combine(Install, "data"));
        File.WriteAllText(LiveTable, English);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static string Md5(string content) =>
        Convert.ToHexString(MD5.HashData(new UTF8Encoding(false).GetBytes(content)));

    private static TranslationManifest Manifest(string id, string content, string targetMod = "wol",
        string version = "1.2.0e-r1", string? recordedHash = null) => new()
    {
        Id = id, Name = "Español", Version = version, TargetMod = targetMod,
        Files = { new TranslationFile { Path = "data/stringtabley.xml", TranslatedHash = recordedHash ?? Md5(content) } },
    };

    private string Zip(TranslationManifest manifest, string content)
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        void Add(string name, string text)
        {
            using var w = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
            w.Write(text);
        }
        Add(TranslationManifest.ManifestFileName, JsonSerializer.Serialize(manifest));
        Add("stringtabley.xml", content);
        return path;
    }

    /// <summary>The pack another translator's card installed earlier.</summary>
    private string ExistingPack(string id = "ES-LA")
    {
        var folder = Path.Combine(PacksRoot, id);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, TranslationManifest.ManifestFileName),
            JsonSerializer.Serialize(Manifest(id, "previous")));
        File.WriteAllText(Path.Combine(folder, "stringtabley.xml"), "previous");
        return folder;
    }

    private void AssertPreviousIntact(string folder)
        => Assert.Equal("previous", File.ReadAllText(Path.Combine(folder, "stringtabley.xml")));

    private void AssertNoScratchLeft()
    {
        if (!Directory.Exists(PacksRoot)) return;
        Assert.DoesNotContain(Directory.GetDirectories(PacksRoot), d => Path.GetFileName(d).StartsWith('.'));
    }

    // ------ refusals: the download is not the pack that was listed

    [Fact]
    public void AFileThatDoesNotMatchItsRecordedHash_IsRefused()
    {
        var previous = ExistingPack();
        var zip = Zip(Manifest("ES-LA", Spanish, recordedHash: Md5("something else")), Spanish);

        var ex = Assert.Throws<InvalidDataException>(() =>
            Service.StagePackFromZip(zip, new PackExpectation("ES-LA", "wol"), CancellationToken.None));
        Assert.Contains("damaged", ex.Message);
        AssertPreviousIntact(previous);
        AssertNoScratchLeft();
    }

    /// <summary>A file with no recorded hash can't be tied to the listing, so it isn't trusted.</summary>
    [Fact]
    public void AFileWithNoRecordedHash_IsRefused()
    {
        var manifest = Manifest("ES-LA", Spanish);
        manifest.Files[0].TranslatedHash = "";
        var zip = Zip(manifest, Spanish);

        Assert.Throws<InvalidDataException>(() =>
            Service.StagePackFromZip(zip, new PackExpectation("ES-LA", "wol"), CancellationToken.None));
        AssertNoScratchLeft();
    }

    [Fact]
    public void AnotherContentHashThanTheListingAdvertised_IsRefused()
    {
        var previous = ExistingPack();
        var zip = Zip(Manifest("ES-LA", Spanish), Spanish);

        Assert.Throws<InvalidDataException>(() => Service.StagePackFromZip(zip,
            new PackExpectation("ES-LA", "wol", AdvertisedContentHash: "0123456789abcdef"), CancellationToken.None));
        AssertPreviousIntact(previous);
        AssertNoScratchLeft();
    }

    [Fact]
    public void APackMadeForAnotherMod_IsRefused()
    {
        var previous = ExistingPack();
        var zip = Zip(Manifest("ES-LA", Spanish, targetMod: "improvement-mod"), Spanish);

        Assert.Throws<InvalidDataException>(() =>
            Service.StagePackFromZip(zip, new PackExpectation("ES-LA", "wol"), CancellationToken.None));
        AssertPreviousIntact(previous);
    }

    /// <summary>
    /// An empty <c>targetMod</c> predates the field: tolerated from the mod's own source, refused
    /// from one the player added (<see cref="PackExpectation.RequireTargetMod"/>).
    /// </summary>
    [Fact]
    public void AnEmptyTargetMod_IsOnlyToleratedWhenTheSourceIsTheModsOwn()
    {
        var zip = Zip(Manifest("ES-LA", Spanish, targetMod: ""), Spanish);

        Assert.Throws<InvalidDataException>(() => Service.StagePackFromZip(zip,
            new PackExpectation("ES-LA", "wol", RequireTargetMod: true), CancellationToken.None));

        var staged = Service.StagePackFromZip(zip, new PackExpectation("ES-LA", "wol"), CancellationToken.None);
        Service.DiscardStaged(staged);
    }

    [Fact]
    public void AnotherIdThanTheOneChosen_IsRefused()
    {
        var zip = Zip(Manifest("FR", Spanish), Spanish);
        Assert.Throws<InvalidDataException>(() =>
            Service.StagePackFromZip(zip, new PackExpectation("ES-LA", "wol"), CancellationToken.None));
        Assert.False(Directory.Exists(Path.Combine(PacksRoot, "FR")));
    }

    // ------ what is accepted

    /// <summary>
    /// Real packs carry one version text in their folder and another inside the zip. The bytes are
    /// what identify a pack, so a different version TEXT is no reason to refuse it.
    /// </summary>
    [Fact]
    public void ADifferentVersionText_IsTolerated()
    {
        var manifest = Manifest("ES-LA", Spanish, version: "1.0");
        var zip = Zip(manifest, Spanish);
        var advertised = TranslationCompat.ComputeContentHash(manifest.Files);

        var staged = Service.StagePackFromZip(zip,
            new PackExpectation("ES-LA", "wol", AdvertisedContentHash: advertised, RequireTargetMod: true),
            CancellationToken.None);

        Assert.Equal(advertised, staged.ContentHash);
        Service.DiscardStaged(staged);
    }

    /// <summary>The listing may advertise the manifest's own declared hash rather than the computed one.</summary>
    [Fact]
    public void TheManifestsDeclaredContentHash_IsAcceptedAsTheAdvertisedOne()
    {
        var manifest = Manifest("ES-LA", Spanish);
        manifest.ContentHash = "18ca36a3d84d2352";
        var zip = Zip(manifest, Spanish);

        var staged = Service.StagePackFromZip(zip,
            new PackExpectation("ES-LA", "wol", AdvertisedContentHash: "18CA36A3D84D2352"), CancellationToken.None);

        Assert.Equal("18ca36a3d84d2352", staged.ContentHash);
        Service.DiscardStaged(staged);
    }

    // ------ stage → apply → promote

    [Fact]
    public void Staging_LeavesTheInstalledPackAlone_UntilItIsPromoted()
    {
        var previous = ExistingPack();
        var zip = Zip(Manifest("ES-LA", Spanish), Spanish);

        var staged = Service.StagePackFromZip(zip, new PackExpectation("ES-LA", "wol"), CancellationToken.None);
        AssertPreviousIntact(previous);

        Assert.True(Service.ApplyStaged(staged).Success);
        Assert.Equal(Spanish, File.ReadAllText(LiveTable));
        AssertPreviousIntact(previous);

        Service.PromoteStaged(staged);
        Assert.True(staged.Promoted);
        Assert.Equal(Spanish, File.ReadAllText(Path.Combine(previous, "stringtabley.xml")));
        AssertNoScratchLeft();
    }

    [Fact]
    public void DiscardingAStagedPack_RemovesTheScratchAndKeepsThePreviousPack()
    {
        var previous = ExistingPack();
        var staged = Service.StagePackFromZip(Zip(Manifest("ES-LA", Spanish), Spanish),
            new PackExpectation("ES-LA", "wol"), CancellationToken.None);

        Service.DiscardStaged(staged);

        Assert.False(Directory.Exists(staged.Folder));
        AssertPreviousIntact(previous);
        AssertNoScratchLeft();
    }

    /// <summary>Discarding after a promote must not delete the pack that is now installed.</summary>
    [Fact]
    public void DiscardAfterPromote_IsANoOp()
    {
        var staged = Service.StagePackFromZip(Zip(Manifest("ES-LA", Spanish), Spanish),
            new PackExpectation("ES-LA", "wol"), CancellationToken.None);
        Service.ApplyStaged(staged);
        Service.PromoteStaged(staged);

        Service.DiscardStaged(staged);

        Assert.True(File.Exists(Path.Combine(PacksRoot, "ES-LA", "stringtabley.xml")));
    }

    // ------ the snapshot guard

    /// <summary>
    /// With no English snapshot, applying builds one from the live files — except a live file that
    /// already IS this pack's file. Snapshotting it would store Spanish as the "English" backup,
    /// and "English" would then restore Spanish.
    /// </summary>
    [Fact]
    public void ALiveFileThatAlreadyIsTheTranslation_IsNotSnapshottedAsEnglish()
    {
        File.WriteAllText(LiveTable, Spanish);
        var staged = Service.StagePackFromZip(Zip(Manifest("ES-LA", Spanish), Spanish),
            new PackExpectation("ES-LA", "wol"), CancellationToken.None);

        Assert.True(Service.ApplyStaged(staged).Success);

        Assert.False(File.Exists(Path.Combine(Service.OriginalsFolder, "stringtabley.xml")));
        Service.DiscardStaged(staged);
    }

    [Fact]
    public void AnEnglishLiveFile_IsSnapshottedBeforeTheFirstApply()
    {
        var staged = Service.StagePackFromZip(Zip(Manifest("ES-LA", Spanish), Spanish),
            new PackExpectation("ES-LA", "wol"), CancellationToken.None);

        Service.ApplyStaged(staged);

        Assert.Equal(English, File.ReadAllText(Path.Combine(Service.OriginalsFolder, "stringtabley.xml")));
        Service.DiscardStaged(staged);
    }

    [Fact]
    public void AModWithoutATranslationsBlock_StagesNothing()
    {
        var zip = Zip(Manifest("ES-LA", Spanish), Spanish);
        var stock = TranslationService.ForProfile(Install, new ModProfile { Id = "aoe3-tad" });

        Assert.Throws<InvalidOperationException>(() =>
            stock.StagePackFromZip(zip, new PackExpectation("ES-LA", "aoe3-tad"), CancellationToken.None));
        AssertNoScratchLeft();
    }
}
