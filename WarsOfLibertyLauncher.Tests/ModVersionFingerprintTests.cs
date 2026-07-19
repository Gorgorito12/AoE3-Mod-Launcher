using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins the pure halves of the GitHubReleases version-identification feature:
/// parsing a zip's central directory, picking which files discriminate between
/// releases, and turning hit counts into a verdict.
///
/// The network half (range requests) isn't covered here — it's IO, and it's
/// best-effort by design (any failure falls back to the previous behaviour).
/// </summary>
public class ModVersionFingerprintTests
{
    /// <summary>Build a real zip in memory so the parser is tested against
    /// bytes produced by a genuine zip writer, not a hand-rolled fixture.</summary>
    private static byte[] BuildZip(params (string Name, string Content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var e = zip.CreateEntry(name, CompressionLevel.NoCompression);
                using var s = e.Open();
                var bytes = Encoding.UTF8.GetBytes(content);
                s.Write(bytes, 0, bytes.Length);
            }
        }
        return ms.ToArray();
    }

    private static Dictionary<string, RemoteZipIndex.ZipEntryInfo>? IndexOf(byte[] zip)
    {
        Assert.True(RemoteZipIndex.TryLocateCentralDirectory(zip, out var off, out var size));
        var cd = new byte[size];
        System.Array.Copy(zip, off, cd, 0, size);
        return RemoteZipIndex.ParseCentralDirectory(cd);
    }

    [Fact]
    public void ParseCentralDirectory_ReadsNamesAndCrcs()
    {
        var zip = BuildZip(("data/a.txt", "hello"), ("data/b.txt", "world"));

        var idx = IndexOf(zip);

        Assert.NotNull(idx);
        Assert.Equal(2, idx!.Count);
        Assert.True(idx.ContainsKey("data/a.txt"));
        Assert.True(idx.ContainsKey("data/b.txt"));
        // Same content must yield the same CRC; different content must not.
        Assert.NotEqual(idx["data/a.txt"].Crc32, idx["data/b.txt"].Crc32);
        Assert.Equal(5, idx["data/a.txt"].Size);
    }

    [Fact]
    public void ParseCentralDirectory_SameContentSameCrcAcrossZips()
    {
        var a = IndexOf(BuildZip(("f.txt", "identical")))!;
        var b = IndexOf(BuildZip(("f.txt", "identical")))!;

        // This equality is the whole premise of the feature: a file byte-identical
        // in two releases fingerprints the same, so only files that CHANGE can
        // discriminate between versions.
        Assert.Equal(a["f.txt"].Crc32, b["f.txt"].Crc32);
    }

    [Fact]
    public void ParseCentralDirectory_GarbageReturnsNull()
    {
        Assert.Null(RemoteZipIndex.ParseCentralDirectory(new byte[] { 1, 2, 3, 4, 5 }));
        Assert.Null(RemoteZipIndex.ParseCentralDirectory(new byte[100]));
    }

    [Fact]
    public void TryLocateCentralDirectory_RejectsTooShortBuffer()
    {
        Assert.False(RemoteZipIndex.TryLocateCentralDirectory(new byte[10], out _, out _));
    }

    [Fact]
    public void SelectDiscriminating_PicksOnlyChangedFiles_SmallestFirst()
    {
        var v1 = new Dictionary<string, RemoteZipIndex.ZipEntryInfo>
        {
            ["same.txt"] = new(111, 10),
            ["big-changed.bin"] = new(222, 5000),
            ["small-changed.cfg"] = new(333, 20),
        };
        var v2 = new Dictionary<string, RemoteZipIndex.ZipEntryInfo>
        {
            ["same.txt"] = new(111, 10),            // identical → useless
            ["big-changed.bin"] = new(999, 5000),   // differs
            ["small-changed.cfg"] = new(888, 20),   // differs
        };

        var picked = ModVersionFingerprint.SelectDiscriminating(
            new() { ["v1"] = v1, ["v2"] = v2 });

        Assert.DoesNotContain("same.txt", picked);
        Assert.Equal(new[] { "small-changed.cfg", "big-changed.bin" }, picked);
    }

    [Fact]
    public void SelectDiscriminating_IgnoresFilesPresentInOnlyOneRelease()
    {
        var v1 = new Dictionary<string, RemoteZipIndex.ZipEntryInfo> { ["only-here.txt"] = new(1, 10) };
        var v2 = new Dictionary<string, RemoteZipIndex.ZipEntryInfo> { ["other.txt"] = new(2, 10) };

        Assert.Empty(ModVersionFingerprint.SelectDiscriminating(
            new() { ["v1"] = v1, ["v2"] = v2 }));
    }

    [Fact]
    public void Decide_ClearWinnerWins()
    {
        var hits = new Dictionary<string, int> { ["19.07.2026"] = 15, ["05.07.2026"] = 0 };
        Assert.Equal("19.07.2026", ModVersionFingerprint.Decide(hits, 15));
    }

    [Fact]
    public void Decide_TieIsUnknown()
    {
        var hits = new Dictionary<string, int> { ["a"] = 5, ["b"] = 5 };
        Assert.Null(ModVersionFingerprint.Decide(hits, 10));
    }

    [Fact]
    public void Decide_BelowConfidenceIsUnknown()
    {
        // 5 of 10 probed = 50%, under the 60% floor → refuse rather than guess.
        var hits = new Dictionary<string, int> { ["a"] = 5, ["b"] = 1 };
        Assert.Null(ModVersionFingerprint.Decide(hits, 10));
    }

    [Fact]
    public void Decide_NoHitsOrNoProbesIsUnknown()
    {
        Assert.Null(ModVersionFingerprint.Decide(new Dictionary<string, int> { ["a"] = 0 }, 5));
        Assert.Null(ModVersionFingerprint.Decide(new Dictionary<string, int> { ["a"] = 3 }, 0));
        Assert.Null(ModVersionFingerprint.Decide(new Dictionary<string, int>(), 5));
    }
}
