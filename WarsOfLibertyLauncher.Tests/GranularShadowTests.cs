using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services.Repair;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins the SHADOW half of the granular restore: reading a payload's central directory from its
/// release parts (<see cref="RemotePayloadIndex"/>) and deciding which damaged files a per-file
/// restore could put back (<see cref="GranularPlanner"/>). The refusals in the planner are the
/// point — they are the failures that got the earlier granular repair removed.
/// </summary>
public class GranularShadowTests
{
    private static byte[] Zip(params (string Name, string Content)[] files)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, content) in files)
            {
                var e = zip.CreateEntry(name, CompressionLevel.Optimal);
                using var w = new StreamWriter(e.Open());
                w.Write(content);
            }
        return ms.ToArray();
    }

    private static RemotePayloadIndex.RangeReader Over(byte[] bytes)
        => (offset, count, _) =>
        {
            if (offset < 0 || offset + count > bytes.Length) return Task.FromResult<byte[]?>(null);
            var chunk = new byte[count];
            Buffer.BlockCopy(bytes, (int)offset, chunk, 0, count);
            return Task.FromResult<byte[]?>(chunk);
        };

    [Fact]
    public async Task TheDirectoryOfAnOrdinaryZipIsRead_WithRealOffsetsAndSizes()
    {
        var bytes = Zip(("data/a.xml", "hello"), ("art/b.ddt", new string('x', 5000)));
        var entries = await RemotePayloadIndex.TryReadAsync(Over(bytes), bytes.Length, CancellationToken.None);

        Assert.NotNull(entries);
        var b = Assert.Single(entries!, e => e.Name == "art/b.ddt");
        Assert.Equal(5000, b.Size);
        Assert.Equal(8, b.Method);
        // The local header the offset points at really is one.
        Assert.Equal(new byte[] { 0x50, 0x4B, 0x03, 0x04 }, bytes.Skip((int)b.LocalHeaderOffset).Take(4).ToArray());
    }

    /// <summary>
    /// Wars of Liberty's payload has ~43,000 entries, past the classic 65,535 limit's cousin — the
    /// point where writers switch to Zip64. A reader that bailed on Zip64 (as the fingerprint reader
    /// deliberately does) would never see the one payload this exists for.
    /// </summary>
    [Fact]
    public async Task AZip64DirectoryIsRead()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            for (int i = 0; i < 65_600; i++)
                zip.CreateEntry($"f/{i}.txt", CompressionLevel.NoCompression);
        var bytes = ms.ToArray();

        var entries = await RemotePayloadIndex.TryReadAsync(Over(bytes), bytes.Length, CancellationToken.None);
        Assert.NotNull(entries);
        Assert.Equal(65_600, entries!.Count);
    }

    [Fact]
    public async Task APayloadSplitAcrossPartsReadsAsOneStream()
    {
        var whole = Zip(("data/a.xml", "hello"), ("art/b.ddt", new string('y', 70_000)));
        // Cut in three, with a boundary inside the central directory and one inside the tail probe.
        var cuts = new[] { whole.Length / 3, whole.Length - 90 };
        var parts = new List<byte[]>
        {
            whole.Take(cuts[0]).ToArray(),
            whole.Skip(cuts[0]).Take(cuts[1] - cuts[0]).ToArray(),
            whole.Skip(cuts[1]).ToArray(),
        };
        var urls = new[] { "p1", "p2", "p3" };
        var reader = new RemotePayloadIndex.PartReader(urls, parts.Select(p => (long)p.Length).ToArray(),
            (url, from, to, _) =>
            {
                var part = parts[Array.IndexOf(urls, url)];
                return Task.FromResult<byte[]?>(part.Skip((int)from).Take((int)(to - from + 1)).ToArray());
            });

        Assert.Equal(whole.Length, reader.Total);
        var entries = await RemotePayloadIndex.TryReadAsync(reader.ReadAsync, reader.Total, CancellationToken.None);
        Assert.Equal(new[] { "art/b.ddt", "data/a.xml" }, entries!.Select(e => e.Name).OrderBy(n => n).ToArray());
    }

    [Fact]
    public async Task NotAZipIsNull_NeverAThrow()
    {
        var junk = Encoding.ASCII.GetBytes(new string('z', 5000));
        Assert.Null(await RemotePayloadIndex.TryReadAsync(Over(junk), junk.Length, CancellationToken.None));
    }

    // ---------------------------------------------------------------- planner

    private static PayloadEntry Entry(string name, long size, ushort method = 8)
        => new(name, 0, size / 2, size, 0, method);

    private static Dictionary<string, FileFingerprint> Hashes(params (string Path, long Size)[] files)
        => files.ToDictionary(f => f.Path, f => new FileFingerprint(f.Size, new string('a', 64)),
            StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void AFileOfTheRecordedSizeIsCoverable_AndPricedAtItsCompressedSize()
    {
        var plan = GranularPlanner.Plan(
            new[] { Entry("art/a.ddt", 1000), Entry("data/other.xml", 1) }, new[] { "art/a.ddt" },
            Hashes(("art/a.ddt", 1000)), new HashSet<string>());
        Assert.True(plan.AllCoverable);
        Assert.Equal(500 + GranularPlanner.LocalHeaderAllowance, plan.EstimatedBytes);
    }

    /// <summary>
    /// THE ONE THAT MATTERS. A later patch changed the file: the payload holds the OLD bytes, and
    /// restoring them is exactly what the removed granular repair did wrong.
    /// </summary>
    [Fact]
    public void AFileAPatchChangedIsNeverCoverable()
    {
        var plan = GranularPlanner.Plan(
            new[] { Entry("data/protoy.xml", 1000), Entry("art/other.ddt", 1) }, new[] { "data/protoy.xml" },
            Hashes(("data/protoy.xml", 1234)), new HashSet<string>());
        Assert.Equal(GranularVerdict.SizeDiffers, Assert.Single(plan.Items).Verdict);
        Assert.Equal(0, plan.EstimatedBytes);
    }

    [Fact]
    public void RefusalsAreNamed()
    {
        var excluded = GranularPlanner.Excluded(
            new ModProfile
            {
                Id = "m", PrivateSetupPath = true, GameExecutable = "age3n.exe",
                Translations = new TranslationsSettings { CoveredFiles = new() { @"data\stringtabley.xml" } },
            },
            new[] { @"art\addon.ddt" }, translationActive: true);

        var plan = GranularPlanner.Plan(
            new[]
            {
                Entry("art/addon.ddt", 10), Entry("age3n.exe", 10), Entry("data/stringtabley.xml", 10),
                Entry("art/odd.ddt", 10, method: 14),
            },
            new[] { "art/addon.ddt", "age3n.exe", "data/stringtabley.xml", "art/new.ddt", "art/odd.ddt", "data/" },
            Hashes(("art/addon.ddt", 10), ("age3n.exe", 10), ("data/stringtabley.xml", 10),
                ("art/new.ddt", 10), ("art/odd.ddt", 10)),
            excluded);

        var v = plan.Items.ToDictionary(i => i.Path, i => i.Verdict);
        Assert.Equal(GranularVerdict.Excluded, v["art/addon.ddt"]);
        Assert.Equal(GranularVerdict.Excluded, v["age3n.exe"]);
        Assert.Equal(GranularVerdict.Excluded, v["data/stringtabley.xml"]);
        Assert.Equal(GranularVerdict.NotInPayload, v["art/new.ddt"]);
        Assert.Equal(GranularVerdict.Unsupported, v["art/odd.ddt"]);
        Assert.Equal(GranularVerdict.NoFingerprint, v["data/"]);
        Assert.Equal(0, plan.Coverable);
    }

    /// <summary>A payload with ONE top-level folder is a wrapper, exactly as the install reads it.</summary>
    [Fact]
    public void AWrappedPayloadIsMatchedAfterItsWrapperFolder()
    {
        var plan = GranularPlanner.Plan(
            new[] { Entry("Knights and Barbarians/data/a.xml", 7), Entry("Knights and Barbarians/art/b.ddt", 8) },
            new[] { "data/a.xml" }, Hashes(("data/a.xml", 7)), new HashSet<string>());
        Assert.True(plan.AllCoverable);
    }
}
