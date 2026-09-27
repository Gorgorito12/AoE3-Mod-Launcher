using System.IO;
using WarsOfLibertyLauncher.Models;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins the install manifest's save/load contract. The save used to be an in-place
/// <c>File.WriteAllText</c>, so a crash mid-write left a truncated manifest that read back as
/// "no manifest" — which silently costs per-file verify, forces a full re-download on every
/// repair and falls back to the conservative uninstall.
/// </summary>
public class ManifestSaveTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aoe3ml-manifest-" + Guid.NewGuid().ToString("N"));

    public ManifestSaveTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void RoundTripsAndLeavesNoScratchBehind()
    {
        var m = new InstallManifest { ModId = "wol", InstallPath = _dir, Version = "1.2.0e" };
        m.FileHashes["data/a.xml"] = new FileFingerprint(3, "abc");
        m.Save();

        var loaded = InstallManifest.TryLoad(_dir);
        Assert.NotNull(loaded);
        Assert.Equal("1.2.0e", loaded!.Version);
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public void ALeftoverScratchFileIsIgnoredAndRecognised()
    {
        new InstallManifest { ModId = "wol", InstallPath = _dir, Version = "1" }.Save();
        var scratch = Path.Combine(_dir, InstallManifest.FileName + ".0123abcd.tmp");
        File.WriteAllText(scratch, "{ truncated");

        Assert.Equal("1", InstallManifest.TryLoad(_dir)!.Version);
        Assert.True(InstallManifest.IsSaveScratch(scratch));
        Assert.True(InstallManifest.IsSaveScratch("install-manifest.json.x.tmp"));
        // The real manifest and ordinary files are NOT scratch.
        Assert.False(InstallManifest.IsSaveScratch(InstallManifest.FileName));
        Assert.False(InstallManifest.IsSaveScratch("data/stringtabley.xml.tmp"));
    }

    [Fact]
    public void SaveNeverCreatesAGhostFolder()
    {
        var gone = Path.Combine(_dir, "moved-away");
        var m = new InstallManifest { ModId = "wol", InstallPath = gone };
        Assert.Throws<DirectoryNotFoundException>(() => m.Save());
        Assert.False(Directory.Exists(gone));
    }

    [Fact]
    public void LoadTakesInstallPathFromWhereItWasRead()
    {
        // A manifest copied or moved with its folder still records the OLD path; a later Save
        // must go to the folder it actually lives in.
        File.WriteAllText(Path.Combine(_dir, InstallManifest.FileName),
            "{\"modId\":\"wol\",\"installPath\":\"D:\\\\old\\\\place\"}");
        Assert.Equal(_dir, InstallManifest.TryLoad(_dir)!.InstallPath);
    }

    [Fact]
    public void MapsAreCaseInsensitiveAndCaseDuplicatesMerge()
    {
        File.WriteAllText(Path.Combine(_dir, InstallManifest.FileName),
            "{\"modId\":\"wol\",\"fileHashes\":{" +
            "\"Data/A.xml\":{\"size\":1,\"sha256\":\"old\"}," +
            "\"data/a.xml\":{\"size\":2,\"sha256\":\"new\"}}}");
        var m = InstallManifest.TryLoad(_dir)!;
        Assert.Single(m.FileHashes);
        Assert.Equal("new", m.FileHashes["DATA/A.XML"].Sha256);
    }

    [Fact]
    public void NullCollectionsReadAsEmpty()
    {
        File.WriteAllText(Path.Combine(_dir, InstallManifest.FileName),
            "{\"modId\":\"wol\",\"engineFileHashes\":null,\"overlayFiles\":null,\"shortcuts\":null}");
        var m = InstallManifest.TryLoad(_dir)!;
        Assert.Empty(m.EngineFileHashes);
        Assert.Empty(m.OverlayFiles);
        Assert.Empty(m.Shortcuts);
    }
}
