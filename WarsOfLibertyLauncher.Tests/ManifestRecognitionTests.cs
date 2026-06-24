using System.Collections.Generic;
using System.Text.Json;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Regression tests for the manifest-baseline recognition that fixes the
/// byte-faithful WoL install never matching any UpdateInfo version. The bug:
/// the snapshot payload's data files don't MD5-match any version in
/// UpdateInfo.xml, so version detection returned null → the launcher treated a
/// working install as "needs install" forever (repair looped to "Install",
/// "update" reinstalled, Mod Properties didn't recognize it). The launcher now
/// records the laid-down hashes + version in install-manifest.json and
/// recognizes its own install from that. These pin the pure decision (no I/O).
/// </summary>
public class ManifestRecognitionTests
{
    private static List<VersionInfo> KnownVersions() => new()
    {
        new VersionInfo { Ver = "1.0.9h", ProtoMd5 = "aaa", TechMd5 = "bbb", StrMd5 = "ccc", MinReqDownload = 0 },
        new VersionInfo { Ver = "1.0.9g", ProtoMd5 = "ddd", TechMd5 = "eee", StrMd5 = "fff", MinReqDownload = 19 },
    };

    private static InstallManifest ManifestWithBaseline(string version, string p, string t, string s) =>
        new()
        {
            Version = version,
            KeyFileHashes = new Dictionary<string, string>
            {
                ["data/protoy.xml"] = p,
                ["data/techtreey.xml"] = t,
                ["data/stringtabley.xml"] = s,
            },
        };

    [Fact]
    public void Baseline_MatchesLiveHashes_RecognizedAtManifestVersion()
    {
        var manifest = ManifestWithBaseline("1.0.9h", "p1", "t1", "s1");

        var result = UpdateService.RecognizeFromManifestData(
            manifest, KnownVersions(), "p1", "t1", "s1");

        Assert.NotNull(result);
        Assert.Equal("1.0.9h", result!.Ver);
        // Resolved to the known entry → MinReqDownload 0 = nothing pending.
        Assert.Equal(0, result.MinReqDownload);
    }

    [Fact]
    public void Baseline_DriftedLiveHashes_NotRecognized()
    {
        var manifest = ManifestWithBaseline("1.0.9h", "p1", "t1", "s1");

        // One live file differs from the recorded baseline → corruption/edit.
        var result = UpdateService.RecognizeFromManifestData(
            manifest, KnownVersions(), "p1", "t1", "DIFFERENT");

        Assert.Null(result);
    }

    [Fact]
    public void NoBaseline_TrustsRecordedVersion_Migration()
    {
        // Pre-baseline manifest: has a Version but no KeyFileHashes.
        var manifest = new InstallManifest { Version = "1.0.9h" };

        var result = UpdateService.RecognizeFromManifestData(
            manifest, KnownVersions(), "live-p", "live-t", "live-s");

        Assert.NotNull(result);
        Assert.Equal("1.0.9h", result!.Ver);
        Assert.Equal(0, result.MinReqDownload);
    }

    [Fact]
    public void NoManifest_ReturnsNull()
    {
        Assert.Null(UpdateService.RecognizeFromManifestData(
            null, KnownVersions(), "p", "t", "s"));
    }

    [Fact]
    public void EmptyVersion_ReturnsNull()
    {
        var manifest = ManifestWithBaseline("", "p1", "t1", "s1");
        Assert.Null(UpdateService.RecognizeFromManifestData(
            manifest, KnownVersions(), "p1", "t1", "s1"));
    }

    [Fact]
    public void ResolveVersionInfo_KnownVersion_UsesItsMinReqDownload()
    {
        var result = UpdateService.ResolveVersionInfo("1.0.9g", KnownVersions());
        Assert.Equal("1.0.9g", result.Ver);
        Assert.Equal(19, result.MinReqDownload); // chains the pending patch
    }

    [Fact]
    public void ResolveVersionInfo_UnknownVersion_SynthesizesAtLatest()
    {
        // Payload newer than every UpdateInfo entry → treat as latest, nothing pending.
        var result = UpdateService.ResolveVersionInfo("1.2.0d", KnownVersions());
        Assert.Equal("1.2.0d", result.Ver);
        Assert.Equal(0, result.MinReqDownload);
    }

    [Fact]
    public void InstallManifest_RoundTripsKeyFileHashes()
    {
        var manifest = ManifestWithBaseline("1.0.9h", "p1", "t1", "s1");
        var json = JsonSerializer.Serialize(manifest);
        var back = JsonSerializer.Deserialize<InstallManifest>(json);

        Assert.NotNull(back);
        Assert.Equal("p1", back!.KeyFileHashes["data/protoy.xml"]);
        Assert.Equal("s1", back.KeyFileHashes["data/stringtabley.xml"]);
    }

    [Fact]
    public void InstallManifest_OldJsonWithoutField_DeserializesToEmpty()
    {
        // A manifest written before baseline recording has no keyFileHashes key.
        const string oldJson = "{\"version\":\"1.0.9h\",\"installPath\":\"C:\\\\x\"}";
        var back = JsonSerializer.Deserialize<InstallManifest>(oldJson);

        Assert.NotNull(back);
        Assert.NotNull(back!.KeyFileHashes);
        Assert.Empty(back.KeyFileHashes);
    }
}
