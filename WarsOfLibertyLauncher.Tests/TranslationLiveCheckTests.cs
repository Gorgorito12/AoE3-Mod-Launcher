using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// <see cref="TranslationService.CheckLive"/> and <see cref="TranslationService.FindShadowingCompiled"/>:
/// whether a translation the config calls "in use" is what the game will actually read.
///
/// <para>It exists because a player reported "Spanish is on and it does not work" with the card
/// saying "In use ✓". The likeliest cause is a compiled <c>stringtabley.xml.XMB</c> left beside
/// the translated <c>.xml</c>: AoE3 reads the compiled one first, so the copy succeeds and nothing
/// on screen changes. The card read the config and could not know.</para>
/// </summary>
public class TranslationLiveCheckTests : IDisposable
{
    private readonly string _install = Path.Combine(
        Path.GetTempPath(), "aoe3ml-translive-" + Guid.NewGuid().ToString("N"));

    private const string PackId = "es";
    private const string Translated = "<StringTable><String _locID=\"1\">Hola</String></StringTable>";

    public TranslationLiveCheckTests()
    {
        Directory.CreateDirectory(Path.Combine(_install, "data"));
        var pack = Path.Combine(_install, TranslationService.TranslationsFolderName, PackId);
        Directory.CreateDirectory(pack);
        File.WriteAllText(Path.Combine(pack, "stringtabley.xml"), Translated);

        var manifest = new TranslationManifest
        {
            Id = PackId,
            Name = "Español",
            Files =
            {
                new TranslationFile
                {
                    Path = "data/stringtabley.xml",
                    TranslatedHash = Md5(Translated),
                },
            },
        };
        File.WriteAllText(Path.Combine(pack, TranslationManifest.ManifestFileName),
            JsonSerializer.Serialize(manifest));
    }

    public void Dispose()
    {
        try { Directory.Delete(_install, recursive: true); } catch { }
    }

    private static string Md5(string text)
        => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(text)));

    private string Live => Path.Combine(_install, "data", "stringtabley.xml");
    private string Twin => Path.Combine(_install, "data", "stringtabley.xml.XMB");

    [Fact]
    public void AppliedAndNothingInTheWay_IsLive()
    {
        var ts = new TranslationService(_install);
        Assert.True(ts.Apply(PackId).Success);

        var check = ts.CheckLive(PackId);
        Assert.Equal(TranslationLiveState.Live, check.State);
        Assert.Empty(check.ShadowingFiles);
    }

    /// <summary>
    /// THE ONE THAT MATTERS: the copy succeeds, the hashes even match, and the game still
    /// reads the compiled twin. The file matching must not hide the twin.
    /// </summary>
    [Fact]
    public void ACompiledTwinHidesTheTranslationEvenWhenTheFileMatches()
    {
        File.WriteAllText(Twin, "compiled english");
        var ts = new TranslationService(_install);

        var apply = ts.Apply(PackId);
        Assert.True(apply.Success);
        Assert.Equal(new[] { "data/stringtabley.xml.XMB" }, apply.ShadowingFiles);

        var check = ts.CheckLive(PackId);
        Assert.Equal(TranslationLiveState.Shadowed, check.State);
        Assert.Equal(new[] { "data/stringtabley.xml.XMB" }, check.ShadowingFiles);
    }

    /// <summary>An update or a repair put the English file back: "in use" is no longer true.</summary>
    [Fact]
    public void TheEnglishFileBackOnDisk_IsNotOnDisk()
    {
        var ts = new TranslationService(_install);
        Assert.True(ts.Apply(PackId).Success);
        File.WriteAllText(Live, "<StringTable><String _locID=\"1\">Hello</String></StringTable>");

        Assert.Equal(TranslationLiveState.NotOnDisk, ts.CheckLive(PackId).State);
    }

    /// <summary>A pack that is not installed says nothing — never a guess in either direction.</summary>
    [Fact]
    public void APackThatIsNotThereIsUnknown()
    {
        Assert.Equal(TranslationLiveState.Unknown,
            new TranslationService(_install).CheckLive("fr").State);
    }

    [Fact]
    public void OnlyXmlFilesHaveCompiledTwins_AndBackslashesAreAccepted()
    {
        File.WriteAllText(Twin, "x");
        File.WriteAllText(Path.Combine(_install, "data", "readme.txt.XMB"), "x");

        var found = TranslationService.FindShadowingCompiled(
            _install, new[] { @"data\stringtabley.xml", "data/readme.txt", "", "data/missing.xml" });

        Assert.Equal(new[] { "data/stringtabley.xml.XMB" }, found);
    }
}
