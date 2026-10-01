using System;
using System.Linq;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// <see cref="TranslationIndexWriter"/>: the packager writes the translator's
/// <c>translations-index.json</c> so nobody types a hash by hand. Updating the SAME file keeps the
/// link players added valid, so a merge must keep every other item — and must never "repair" a
/// file it can't read.
/// </summary>
public class TranslationIndexWriterTests
{
    private const string Sha = "a3f1c2d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90";
    private static readonly Uri Site = new("https://traducciones.example.com/aoe3/translations-index.json");

    private static IndexItemDraft Draft(string version = "1.2.0e-r1", string zip = "https://traducciones.example.com/aoe3/wol-ES-LA.zip",
        string sha = Sha) =>
        new("ES-LA", "Español", "Juan", "es-419", "wol", version, new[] { "1.2.0e" }, zip, sha.ToUpperInvariant(),
            1249053, "18ca36a3d84d2352", "2026-09-30T00:00:00Z", "Revisión completa");

    /// <summary>THE CONTRACT: whatever the packager writes, the launcher reads back intact.</summary>
    [Fact]
    public void WhatTheWriterWrites_TheParserAccepts()
    {
        Assert.True(TranslationIndexWriter.Merge(null, Draft(), "Traducciones de Juan", out var json, out _));

        var parsed = TranslationIndexSource.Parse(json, Site, relativeZipAllowed: true);
        Assert.True(parsed.Ok);
        Assert.Empty(parsed.Warnings);
        Assert.Equal("Traducciones de Juan", parsed.SourceName);
        var rec = Assert.Single(parsed.Records);
        Assert.Equal("ES-LA", rec.Id);
        Assert.Equal("1.2.0e-r1", rec.Version);
        Assert.Equal("wol", rec.TargetMod);
        Assert.Equal(Sha, rec.Sha256);   // written lower-case
        Assert.Equal("18ca36a3d84d2352", rec.ContentHash);
        Assert.Equal(1249053, rec.Size);
        Assert.Equal("Juan", rec.Author);
        Assert.Contains("\"Español\"", json);   // readable by the translator who opens it
    }

    [Fact]
    public void ARelativeZip_IsReadBackAgainstTheIndexAddress()
    {
        TranslationIndexWriter.Merge(null, Draft(zip: "translations/ES-LA/1.2.0e-r1/wol-ES-LA.zip"), null, out var json, out _);
        var rec = Assert.Single(TranslationIndexSource.Parse(json, Site, true).Records);
        Assert.Equal("https://traducciones.example.com/aoe3/translations/ES-LA/1.2.0e-r1/wol-ES-LA.zip", rec.ZipUrl);
    }

    [Fact]
    public void ANewVersion_IsAddedNextToTheOthers()
    {
        TranslationIndexWriter.Merge(null, Draft("1.2.0e-r1"), "Juan", out var first, out _);
        Assert.True(TranslationIndexWriter.Merge(first, Draft("1.2.0e-r2"), "ignored", out var second, out _));

        var parsed = TranslationIndexSource.Parse(second, Site, true);
        Assert.Equal(new[] { "1.2.0e-r1", "1.2.0e-r2" }, parsed.Records.Select(r => r.Version));
        Assert.Equal("Juan", parsed.SourceName);   // the translator's own name is kept
    }

    [Fact]
    public void TheSameIdAndVersion_IsReplacedNotDuplicated()
    {
        TranslationIndexWriter.Merge(null, Draft("1.2.0e-r1"), null, out var first, out _);
        var otherSha = new string('b', 64);
        TranslationIndexWriter.Merge(first, Draft("1.2.0e-r1", sha: otherSha), null, out var second, out _);

        var rec = Assert.Single(TranslationIndexSource.Parse(second, Site, true).Records);
        Assert.Equal(otherSha, rec.Sha256);
    }

    /// <summary>Items the writer doesn't understand (another mod, hand-added fields) are left as they were.</summary>
    [Fact]
    public void OtherItemsAndUnknownFields_AreKept()
    {
        var existing = """
            { "name": "Juan", "homepage": "https://example.com",
              "translations": [ { "id": "FR", "version": 2, "custom": true } ] }
            """;
        Assert.True(TranslationIndexWriter.Merge(existing, Draft(), null, out var json, out _));
        Assert.Contains("\"homepage\"", json);
        Assert.Contains("\"custom\"", json);
        Assert.Contains("\"FR\"", json);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    [InlineData("{ \"translations\": \"oops\" }")]
    public void AFileItCannotRead_IsNotTouched(string existing)
    {
        Assert.False(TranslationIndexWriter.Merge(existing, Draft(), null, out var json, out var error));
        Assert.Null(json);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void AnObjectWithoutAList_GetsOne()
    {
        Assert.True(TranslationIndexWriter.Merge("{ \"name\": \"Juan\" }", Draft(), null, out var json, out _));
        Assert.Single(TranslationIndexSource.Parse(json, Site, true).Records);
    }

    // ------ the version the packager proposes

    [Fact]
    public void NextRevision_StartsAtR1()
        => Assert.Equal("1.2.0e-r1", TranslationIndexWriter.NextRevision("1.2.0e", Array.Empty<string>()));

    [Fact]
    public void NextRevision_FollowsTheHighestTakenRevision()
        => Assert.Equal("1.2.0e-r4",
            TranslationIndexWriter.NextRevision("1.2.0e", new[] { "1.2.0e-r1", "1.2.0E-R3", "1.2.0d-r9", "1.1", "1.2.0e-rx" }));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("?")]
    [InlineData("../1.2")]
    public void NextRevision_WithoutAUsableModVersion_Is1Point0(string? modVersion)
        => Assert.Equal("1.0", TranslationIndexWriter.NextRevision(modVersion, null));

    [Theory]
    [InlineData("1.2.0e-r1", true)]
    [InlineData("1.2.0e-R12", true)]
    [InlineData("1.2.0e", false)]
    [InlineData("1.1", false)]
    [InlineData(null, false)]
    public void IsRevisionOfModVersion(string? version, bool expected)
        => Assert.Equal(expected, TranslationIndexWriter.IsRevisionOfModVersion(version));
}
