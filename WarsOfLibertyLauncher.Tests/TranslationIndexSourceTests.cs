using System;
using System.Linq;
using System.Text;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// <see cref="TranslationIndexSource.Parse"/>: a <c>translations-index.json</c> at an address the
/// player added. A malformed item is DROPPED (and logged) instead of failing the whole index, so
/// the cases worth pinning are the drops — an item missing what makes it safe must never reach a
/// card.
/// </summary>
public class TranslationIndexSourceTests
{
    private static readonly Uri SiteIndex = new("https://traducciones.example.com/aoe3/translations-index.json");
    private const string Sha = "a3f1c2d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90";

    private static string Item(
        string id = "ES-LA", string version = "1.2.0e-r1", string name = "Español",
        string? targetMod = "wol", string? sha256 = Sha, string zip = "https://traducciones.example.com/aoe3/wol-ES-LA.zip",
        string extra = "")
    {
        var sb = new StringBuilder("{");
        sb.Append($"\"id\":\"{id}\",\"version\":\"{version}\",\"name\":\"{name}\",\"zip\":\"{zip}\"");
        if (targetMod != null) sb.Append($",\"targetMod\":\"{targetMod}\"");
        if (sha256 != null) sb.Append($",\"sha256\":\"{sha256}\"");
        sb.Append(extra);
        return sb.Append('}').ToString();
    }

    private static IndexParseResult Parse(string items, Uri? at = null, bool relativeZipAllowed = true)
        => TranslationIndexSource.Parse("{\"translations\":[" + items + "]}", at ?? SiteIndex, relativeZipAllowed);

    [Fact]
    public void AWellFormedItem_IsRead()
    {
        var r = Parse(Item(extra: ",\"author\":\"Juan\",\"compatibleWith\":[\"1.2.0e\"],\"size\":1249053,"
                                  + "\"contentHash\":\"18CA36A3D84D2352\",\"date\":\"2026-09-09T03:06:52Z\""));

        Assert.True(r.Ok);
        var rec = Assert.Single(r.Records);
        Assert.Equal("ES-LA", rec.Id);
        Assert.Equal("Juan", rec.Author);
        Assert.Equal("wol", rec.TargetMod);
        Assert.Equal(new[] { "1.2.0e" }, rec.CompatibleWith);
        Assert.Equal(1249053, rec.Size);
        Assert.Equal("18ca36a3d84d2352", rec.ContentHash);
        Assert.Equal(Sha, rec.Sha256);
    }

    /// <summary>THE ONE THAT MATTERS: no sha256, no download — the bytes could be anything.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc123")]
    [InlineData("zz3f1c2d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90")]
    public void AnItemWithoutAValidSha256_IsDropped(string? sha)
    {
        var r = Parse(Item(sha256: sha));
        Assert.True(r.Ok);
        Assert.Empty(r.Records);
        Assert.Single(r.Warnings);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("../wol")]
    public void AnItemWithoutAValidTargetMod_IsDropped(string? targetMod)
        => Assert.Empty(Parse(Item(targetMod: targetMod)).Records);

    [Theory]
    [InlineData("..")]
    [InlineData("_originals")]
    [InlineData("ES/LA")]
    [InlineData("CON")]
    public void AnUnsafeId_IsDropped(string id) => Assert.Empty(Parse(Item(id: id)).Records);

    [Theory]
    [InlineData("..")]
    [InlineData("1.0/../x")]
    [InlineData("")]
    public void AnUnsafeVersion_IsDropped(string version) => Assert.Empty(Parse(Item(version: version)).Records);

    [Theory]
    [InlineData("http://traducciones.example.com/wol-ES-LA.zip")]            // https only
    [InlineData("https://mega.nz/file/abc#key")]                             // a host that can't be downloaded from
    [InlineData("https://drive.google.com/drive/folders/1AbCdEfGhIjKlMnOp")] // a folder
    [InlineData("javascript:alert(1)")]
    [InlineData("")]
    public void ABadZipLink_IsDropped(string zip) => Assert.Empty(Parse(Item(zip: zip)).Records);

    [Fact]
    public void ARelativeZip_ResolvesAgainstTheIndexAddress()
    {
        var rec = Assert.Single(Parse(Item(zip: "ES-LA/1.2.0e-r1/wol-ES-LA.zip")).Records);
        Assert.Equal("https://traducciones.example.com/aoe3/ES-LA/1.2.0e-r1/wol-ES-LA.zip", rec.ZipUrl);
    }

    /// <summary>On Drive or Dropbox one file's address says nothing about another's.</summary>
    [Fact]
    public void ARelativeZip_OnAShareHost_IsDropped()
    {
        var drive = new Uri("https://drive.usercontent.google.com/download?id=1AbCdEfGhIjKlMnOpQr&export=download&confirm=t");
        var r = Parse(Item(zip: "wol-ES-LA.zip"), drive, relativeZipAllowed: false);
        Assert.Empty(r.Records);
        Assert.Contains(r.Warnings, w => w.Contains("relative"));
    }

    [Fact]
    public void TheSameIdAndVersionTwice_KeepsTheFirst()
    {
        var r = Parse(Item(name: "First") + "," + Item(name: "Second"));
        Assert.Equal("First", Assert.Single(r.Records).Name);
    }

    [Fact]
    public void OneBadItem_DoesNotHideTheOthers()
    {
        var r = Parse(Item(sha256: null) + "," + Item(version: "1.2.0e-r2"));
        Assert.Equal("1.2.0e-r2", Assert.Single(r.Records).Version);
    }

    [Fact]
    public void MoreItemsThanTheLimit_AreCut()
    {
        var items = string.Join(",", Enumerable.Range(1, TranslationIndexSource.MaxItems + 5)
            .Select(i => Item(version: $"1.0.{i}")));
        Assert.Equal(TranslationIndexSource.MaxItems, Parse(items).Records.Count);
    }

    [Fact]
    public void TheIndexsOwnName_LabelsTheSource()
    {
        var r = TranslationIndexSource.Parse(
            "{ \"name\": \"Traducciones de Juan\", \"translations\": [] }", SiteIndex, true);
        Assert.Equal("Traducciones de Juan", r.SourceName);
    }

    /// <summary>A stranger's text must not be able to make a label read differently from what it is.</summary>
    [Fact]
    public void ControlAndBidiOverrideCharacters_AreStripped()
    {
        var r = TranslationIndexSource.Parse(
            "{ \"name\": \"Juan\\u202Egnp.exe\\u0007\", \"translations\": [] }", SiteIndex, true);
        Assert.Equal("Juangnp.exe", r.SourceName);
    }

    [Fact]
    public void CommentsAndTrailingCommas_AreForgiven()
    {
        var json = "{ // hand-edited\n \"translations\": [ " + Item() + ", ], }";
        Assert.Single(TranslationIndexSource.Parse(json, SiteIndex, true).Records);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"translations\": {}}")]
    [InlineData("{\"items\": []}")]
    public void SomethingThatIsNotAnIndex_IsReportedAsSuch(string json)
        => Assert.Equal("TxSrcErrNotIndex", TranslationIndexSource.Parse(json, SiteIndex, true).ErrorKey);

    [Theory]
    [InlineData("<!DOCTYPE html><html>")]
    [InlineData("﻿  \r\n<html>")]
    public void AViewerPage_LooksLikeHtml(string body) => Assert.True(TranslationIndexSource.LooksLikeHtml(body));

    [Fact]
    public void AnIndex_DoesNotLookLikeHtml() => Assert.False(TranslationIndexSource.LooksLikeHtml("{ \"translations\": [] }"));

    [Fact]
    public void TheLabel_IsTheDeclaredNameElseTheHost()
    {
        var src = TranslationSourceRef.IndexUrl("https://www.example.com/x/translations-index.json");
        Assert.Equal("Traducciones de Juan", TranslationIndexSource.LabelFor(src, "Traducciones de Juan"));
        Assert.Equal("example.com", TranslationIndexSource.LabelFor(src, null));
    }
}
