using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Tests for the payload-integrity guard: the launcher must never produce a
/// silently incomplete install when real-time antivirus quarantines a payload
/// file out of %TEMP% between extraction and the overlay copy.
///
/// The exposure is not a race but MINUTES — InstallAsync runs the whole AoE3
/// clone (~2 min on a real payload) in between — and today the copy enumerates
/// the DISK, so a vanished file is simply absent: no error, and the manifest is
/// then written from what was copied, making the loss permanent and invisible to
/// Verify.
///
/// The FIRST test here is the important one. The guard's failure mode is
/// aborting a HEALTHY install with an antivirus message, which is worse than the
/// bug it fixes — so "an intact payload is a no-op" is what has to stay pinned.
/// </summary>
public class PayloadIntegrityTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private string NewTempDir()
    {
        var dir = Directory.CreateTempSubdirectory("payload-test-").FullName;
        _tempDirs.Add(dir);
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

    /// <summary>
    /// Build a zip. Entries ending in '/' are DIRECTORY entries — they have an
    /// empty Name and the extract loop skips them, so they must never be counted
    /// as expected files (this is one of the ways a naive expected-set would
    /// abort every healthy install).
    /// </summary>
    private string MakeZip(string name, params string[] entries)
    {
        var zipPath = Path.Combine(NewTempDir(), name);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var e in entries)
        {
            var entry = zip.CreateEntry(e);
            if (e.EndsWith("/", StringComparison.Ordinal)) continue;   // directory entry
            using var s = entry.Open();
            using var w = new StreamWriter(s);
            w.Write($"content of {e}");
        }
        return zipPath;
    }

    /// <summary>
    /// THE no-op test: a healthy, flat payload must extract and pass the guard.
    /// If this ever fails, the fix is breaking real installs.
    /// </summary>
    [Fact]
    public async void Extract_IntactFlatPayload_DoesNotThrow_AndListsWrittenFiles()
    {
        var zip = MakeZip("payload.zip", "data/protoy.xml", "data/techtreey.xml", "AI3/wolai.upl");
        var svc = new NativeInstallService();

        var payload = await svc.ExtractPayloadAsync(zip, null, null, default);

        Assert.Equal(3, payload.Written.Count);
        Assert.All(payload.Written, p => Assert.True(File.Exists(p)));
    }

    /// <summary>
    /// No-op with the two shapes that would trip a naive expected-set: a zip that
    /// wraps everything in ONE folder (NormalizePayloadRoot descends into it, so
    /// the root no longer matches the entry paths) plus explicit directory
    /// entries. Neither may cause an abort.
    /// </summary>
    [Fact]
    public async void Extract_WrappedPayloadWithDirectoryEntries_DoesNotThrow()
    {
        var zip = MakeZip("wrapped.zip",
            "Knights and Barbarians/",              // directory entry
            "Knights and Barbarians/data/",         // directory entry
            "Knights and Barbarians/data/protoy.xml",
            "Knights and Barbarians/art/thing.ddt");
        var svc = new NativeInstallService();

        var payload = await svc.ExtractPayloadAsync(zip, null, null, default);

        // Directory entries must NOT be counted as expected files.
        Assert.Equal(2, payload.Written.Count);
        // The wrapper was flattened away for the overlay copy's benefit...
        Assert.EndsWith("Knights and Barbarians", payload.Root);
        // ...but the written paths are absolute, so the guard is unaffected by it.
        Assert.All(payload.Written, p => Assert.True(File.Exists(p)));
    }

    /// <summary>
    /// The detection case: a file written fine and removed a moment later (what a
    /// Defender quarantine looks like) must abort, naming the file.
    /// </summary>
    [Fact]
    public async void Extract_ThenFileVanishes_ThrowsPayloadFileBlocked_NamingIt()
    {
        var zip = MakeZip("payload.zip", "data/protoy.xml", "AI3/wolai.upl");
        var svc = new NativeInstallService();
        var payload = await svc.ExtractPayloadAsync(zip, null, null, default);

        // Simulate the quarantine: the file was written, then removed.
        var victim = payload.Written.First(p => p.EndsWith("wolai.upl", StringComparison.OrdinalIgnoreCase));
        File.Delete(victim);

        var ex = Assert.Throws<PayloadFileBlockedException>(
            () => NativeInstallService.VerifyExtractIntact(payload.Written, "test"));
        Assert.Equal("wolai.upl", ex.BlockedFile);
    }

    /// <summary>An intact set must pass the guard — the no-op, at the unit level.</summary>
    [Fact]
    public void VerifyExtractIntact_AllPresent_DoesNotThrow()
    {
        var dir = NewTempDir();
        var files = new List<string>();
        foreach (var n in new[] { "a.xml", "b.upl" })
        {
            var p = Path.Combine(dir, n);
            File.WriteAllText(p, "x");
            files.Add(p);
        }

        NativeInstallService.VerifyExtractIntact(files, "test");   // must not throw
    }

    /// <summary>An empty payload is vacuously intact — must not throw.</summary>
    [Fact]
    public void VerifyExtractIntact_EmptyList_DoesNotThrow()
    {
        NativeInstallService.VerifyExtractIntact(Array.Empty<string>(), "test");
    }
}
