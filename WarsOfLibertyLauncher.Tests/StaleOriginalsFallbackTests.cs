using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins the stale-<c>_originals</c> fallback in
/// <see cref="UpdateService.DetectCurrentVersionAsync"/>.
///
/// Version detection hashes <c>translations\_originals\stringtabley.xml</c> instead of
/// the live file on purpose (so an applied translation doesn't break recognition). But
/// that snapshot is only a COPY of the live file taken at install/patch/pack time — a
/// patch applied BY HAND updates <c>data\</c> and leaves the snapshot behind. Detection
/// then matches nothing, and the install becomes unusable: no version means the UI
/// queues the entire patch chain instead of a real update, and ModHashService (same
/// three files, same snapshot) builds a hybrid fingerprint that matches no peer, so
/// every lobby join is rejected as a version mismatch.
///
/// Reproduces the shape seen in the wild: proto+tech at v2, snapshot still at v1.
/// </summary>
public class StaleOriginalsFallbackTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private string NewInstall()
    {
        var dir = Directory.CreateTempSubdirectory("stale-orig-").FullName;
        _tempDirs.Add(dir);
        Directory.CreateDirectory(Path.Combine(dir, "data"));
        return dir;
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); }
            catch { /* best-effort */ }
        }
    }

    private static string Md5(string content) =>
        Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    private static void Write(string root, string rel, string content)
    {
        var p = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content, new UTF8Encoding(false));
    }

    /// <summary>The snapshot lives flat in translations\_originals\, by file name.</summary>
    private static string SnapshotPath(string install) =>
        Path.Combine(install, "translations", "_originals", "stringtabley.xml");

    private static List<VersionInfo> TwoVersions(
        string protoV1, string techV1, string strV1,
        string protoV2, string techV2, string strV2) => new()
    {
        new VersionInfo { Ver = "1.0", ProtoMd5 = Md5(protoV1), TechMd5 = Md5(techV1), StrMd5 = Md5(strV1), MinReqDownload = 5 },
        new VersionInfo { Ver = "2.0", ProtoMd5 = Md5(protoV2), TechMd5 = Md5(techV2), StrMd5 = Md5(strV2), MinReqDownload = 0 },
    };

    /// <summary>
    /// The reported bug: sim files patched to v2 by hand, snapshot left at v1.
    /// Must recognise v2 (via the live file) and re-sync the snapshot — the re-sync is
    /// what also repairs the multiplayer fingerprint.
    /// </summary>
    [Fact]
    public async void StaleSnapshot_LiveFileMatches_DetectsVersion_AndResyncsSnapshot()
    {
        var install = NewInstall();
        Write(install, "data/protoy.xml", "PROTO-V2");
        Write(install, "data/techtreey.xml", "TECH-V2");
        Write(install, "data/stringtabley.xml", "STR-V2");        // live: correct
        Write(install, "translations/_originals/stringtabley.xml", "STR-V1");   // snapshot: STALE

        var known = TwoVersions("PROTO-V1", "TECH-V1", "STR-V1", "PROTO-V2", "TECH-V2", "STR-V2");

        var match = await UpdateService.DetectCurrentVersionAsync(install, known, default);

        Assert.NotNull(match);
        Assert.Equal("2.0", match!.Ver);
        Assert.Equal(0, match.MinReqDownload);       // 0 pending, not the whole chain
        // The snapshot was re-synced from the live file, so ModHashService now
        // fingerprints v2 like every other peer.
        Assert.Equal("STR-V2", File.ReadAllText(SnapshotPath(install)));
    }

    /// <summary>
    /// The guard that protects the backup: with a TRANSLATION applied the live file
    /// matches no version, so detection must stay NO MATCH and leave _originals alone.
    /// Refreshing here would copy the translated file over the only canonical-English
    /// backup and permanently break both revert-to-English and the fingerprint.
    /// </summary>
    [Fact]
    public async void TranslationApplied_LiveFileMatchesNothing_LeavesSnapshotUntouched()
    {
        var install = NewInstall();
        Write(install, "data/protoy.xml", "PROTO-V2");
        Write(install, "data/techtreey.xml", "TECH-V2");
        Write(install, "data/stringtabley.xml", "STR-TRANSLATED-SPANISH");   // live: a pack
        Write(install, "translations/_originals/stringtabley.xml", "STR-V1"); // snapshot: stale

        var known = TwoVersions("PROTO-V1", "TECH-V1", "STR-V1", "PROTO-V2", "TECH-V2", "STR-V2");

        var match = await UpdateService.DetectCurrentVersionAsync(install, known, default);

        Assert.Null(match);   // still unrecognised — correct, we cannot invent the bytes
        Assert.Equal("STR-V1", File.ReadAllText(SnapshotPath(install)));   // NOT clobbered
    }

    /// <summary>
    /// A healthy install with an in-sync snapshot: the fallback must be a no-op and the
    /// snapshot must keep winning (localization invariance is the whole point).
    /// </summary>
    [Fact]
    public async void HealthySnapshot_MatchesNormally_FallbackIsNoOp()
    {
        var install = NewInstall();
        Write(install, "data/protoy.xml", "PROTO-V2");
        Write(install, "data/techtreey.xml", "TECH-V2");
        Write(install, "data/stringtabley.xml", "STR-V2");
        Write(install, "translations/_originals/stringtabley.xml", "STR-V2");

        var known = TwoVersions("PROTO-V1", "TECH-V1", "STR-V1", "PROTO-V2", "TECH-V2", "STR-V2");

        var match = await UpdateService.DetectCurrentVersionAsync(install, known, default);

        Assert.NotNull(match);
        Assert.Equal("2.0", match!.Ver);
        Assert.Equal("STR-V2", File.ReadAllText(SnapshotPath(install)));
    }

    /// <summary>
    /// A translated install whose snapshot IS in sync must keep resolving through the
    /// snapshot — the behaviour the whole _originals mechanism exists for. Guards
    /// against the fallback accidentally taking over the happy path.
    /// </summary>
    [Fact]
    public async void TranslationApplied_SnapshotInSync_StillDetectsViaSnapshot()
    {
        var install = NewInstall();
        Write(install, "data/protoy.xml", "PROTO-V2");
        Write(install, "data/techtreey.xml", "TECH-V2");
        Write(install, "data/stringtabley.xml", "STR-TRANSLATED-SPANISH");  // live: a pack
        Write(install, "translations/_originals/stringtabley.xml", "STR-V2"); // snapshot: correct

        var known = TwoVersions("PROTO-V1", "TECH-V1", "STR-V1", "PROTO-V2", "TECH-V2", "STR-V2");

        var match = await UpdateService.DetectCurrentVersionAsync(install, known, default);

        Assert.NotNull(match);
        Assert.Equal("2.0", match!.Ver);
    }

    /// <summary>
    /// No snapshot at all (English install): detection hashes the live file directly and
    /// the fallback never engages.
    /// </summary>
    [Fact]
    public async void NoSnapshot_HashesLiveDirectly()
    {
        var install = NewInstall();
        Write(install, "data/protoy.xml", "PROTO-V2");
        Write(install, "data/techtreey.xml", "TECH-V2");
        Write(install, "data/stringtabley.xml", "STR-V2");

        var known = TwoVersions("PROTO-V1", "TECH-V1", "STR-V1", "PROTO-V2", "TECH-V2", "STR-V2");

        var match = await UpdateService.DetectCurrentVersionAsync(install, known, default);

        Assert.NotNull(match);
        Assert.Equal("2.0", match!.Ver);
    }
}
