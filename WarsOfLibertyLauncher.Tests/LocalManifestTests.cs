using System;
using System.IO;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins "add local mod" — loading a <c>mod.json</c> off disk so a manifest can be tried
/// before it is published.
///
/// <para>Two things matter more than the happy path. First, the REJECTIONS have to name
/// their cause: the whole point of trying a manifest locally is learning it is wrong
/// before opening a PR, and "could not add" teaches nothing. Second, the path-traversal
/// guard has to hold here exactly as it does for a downloaded manifest — a file being on
/// disk makes it local, not trusted, and the user may well be trying someone else's.</para>
/// </summary>
public class LocalManifestTests : IDisposable
{
    private readonly string _dir;

    public LocalManifestTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "aoe3ml-localmanifest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    private string WriteManifest(string json, string name = "mod.json")
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Loads_MinimalManifest()
    {
        var path = WriteManifest("""
            { "id": "test-mod", "displayName": "Test Mod" }
            """);

        var entry = ModCatalogService.LoadLocalEntry(path);

        Assert.Equal("test-mod", entry.Manifest.Id);
        Assert.Equal("Test Mod", entry.Manifest.DisplayName);
        // The path is what lets the Workshop mark the row and the removal know what to
        // forget, so it must survive the load.
        Assert.Equal(Path.GetFullPath(path), entry.LocalPath);
    }

    [Fact]
    public void Rejects_MissingFile()
    {
        var missing = Path.Combine(_dir, "nope.json");

        var ex = Assert.Throws<ModCatalogService.LocalManifestException>(
            () => ModCatalogService.LoadLocalEntry(missing));

        Assert.Contains("nope.json", ex.Message);
    }

    [Fact]
    public void Rejects_InvalidJson_AndSaysWhy()
    {
        var path = WriteManifest("{ \"id\": \"broken\", ");

        var ex = Assert.Throws<ModCatalogService.LocalManifestException>(
            () => ModCatalogService.LoadLocalEntry(path));

        // Surfaced verbatim to the user — a bare "invalid" would leave them guessing
        // which line of their manifest is at fault.
        Assert.Contains("Invalid JSON", ex.Message);
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public void Rejects_ManifestWithoutId()
    {
        var path = WriteManifest("""
            { "displayName": "No Id Here" }
            """);

        var ex = Assert.Throws<ModCatalogService.LocalManifestException>(
            () => ModCatalogService.LoadLocalEntry(path));

        Assert.Contains("id", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolvesAsset_SittingNextToTheManifest()
    {
        File.WriteAllBytes(Path.Combine(_dir, "icon.png"), new byte[] { 1, 2, 3 });

        var resolved = ModCatalogService.ResolveLocalAsset(_dir, "icon.png");

        Assert.Equal(Path.Combine(_dir, "icon.png"), resolved);
    }

    [Fact]
    public void ResolvesAsset_NullWhenDeclaredButAbsent()
    {
        // Null rather than a broken path: the UI then falls back to its monogram instead
        // of trying to paint a file that isn't there.
        Assert.Null(ModCatalogService.ResolveLocalAsset(_dir, "missing.png"));
    }

    [Theory]
    [InlineData("../../../secret.png")]
    [InlineData("..\\..\\secret.png")]
    [InlineData("sub/dir/icon.png")]
    [InlineData("sub\\dir\\icon.png")]
    public void ResolvesAsset_RejectsPathTraversal(string declared)
    {
        // The guard mirrors the one on the remote path. Without it a manifest could point
        // the launcher at any file on disk and have it rendered in the Workshop.
        Assert.Null(ModCatalogService.ResolveLocalAsset(_dir, declared));
    }

    [Fact]
    public void ResolvesAsset_NullForUnsetName()
    {
        Assert.Null(ModCatalogService.ResolveLocalAsset(_dir, null));
        Assert.Null(ModCatalogService.ResolveLocalAsset(_dir, "   "));
    }
}
