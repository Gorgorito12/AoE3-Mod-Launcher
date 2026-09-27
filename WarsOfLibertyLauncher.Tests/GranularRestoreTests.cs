using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services.Repair;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins the ACTING half of the granular restore (<see cref="GranularRestore"/>): files come back
/// one by one from the payload, and ONLY when every one of them is proven against the manifest.
/// The refusals are the point — a payload file that is not the recorded one, a single bad file in
/// the set, and a swap that fails half-way must all leave the install exactly as it was.
/// </summary>
public class GranularRestoreTests : IDisposable
{
    private readonly List<string> _dirs = new();

    public void Dispose()
    {
        foreach (var d in _dirs)
            try { Directory.Delete(d, recursive: true); } catch { }
    }

    private string NewDir(string prefix)
    {
        var d = Directory.CreateTempSubdirectory(prefix).FullName;
        _dirs.Add(d);
        return d;
    }

    private static byte[] Zip(CompressionLevel level, params (string Name, string Content)[] files)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, content) in files)
            {
                using var w = new StreamWriter(zip.CreateEntry(name, level).Open());
                w.Write(content);
            }
        return ms.ToArray();
    }

    private static RemotePayloadIndex.RangeReader Over(byte[] bytes) => (offset, count, _) =>
    {
        if (offset < 0 || offset + count > bytes.Length) return Task.FromResult<byte[]?>(null);
        var chunk = new byte[count];
        Buffer.BlockCopy(bytes, (int)offset, chunk, 0, count);
        return Task.FromResult<byte[]?>(chunk);
    };

    private static FileFingerprint Fp(string content)
    {
        var b = Encoding.UTF8.GetBytes(content);
        return new FileFingerprint(b.Length, Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant());
    }

    private static void Put(string root, string rel, string text)
    {
        var full = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    private static string Read(string root, string rel)
        => File.ReadAllText(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>An install whose manifest recorded <paramref name="recorded"/>, with the files on disk damaged.</summary>
    private async Task<(string Install, GranularPlan Plan, Dictionary<string, FileFingerprint> Hashes, byte[] Zip)> Setup(
        byte[] zip, params (string Path, string Recorded, string OnDisk)[] files)
    {
        var install = NewDir("granular-");
        var hashes = new Dictionary<string, FileFingerprint>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, recorded, onDisk) in files)
        {
            hashes[path] = Fp(recorded);
            Put(install, path, onDisk);
        }
        var entries = await RemotePayloadIndex.TryReadAsync(Over(zip), zip.Length, CancellationToken.None);
        var plan = GranularPlanner.Plan(entries!, files.Select(f => f.Path), hashes, new HashSet<string>());
        return (install, plan, hashes, zip);
    }

    private string Backup() => Path.Combine(NewDir("granular-bak-"), "set");

    private static bool NoStaging(string install)
        => !Directory.EnumerateFiles(install, "*" + EngineRestore.StagingSuffix, SearchOption.AllDirectories).Any();

    [Theory]
    [InlineData(CompressionLevel.Optimal)]
    [InlineData(CompressionLevel.NoCompression)]
    public async Task TheRecordedBytesComeBack_AndTheDamagedOnesAreBackedUp(CompressionLevel level)
    {
        var zip = Zip(level, ("art/a.ddt", "AAAA-good"), ("data/b.xml", "<b/>"), ("sound/c.wav", "ccc"));
        var (install, plan, hashes, _) = await Setup(zip, ("art/a.ddt", "AAAA-good", "broken"));
        var backup = Backup();
        bool journal = false;

        var restored = await GranularRestore.RestoreAsync(Over(zip), install, plan, hashes, backup,
            () => journal = true, CancellationToken.None);

        Assert.Equal(new[] { "art/a.ddt" }, restored);
        Assert.Equal("AAAA-good", Read(install, "art/a.ddt"));
        Assert.Equal("broken", Read(backup, "art/a.ddt"));
        Assert.True(journal);
        Assert.True(NoStaging(install));
    }

    /// <summary>A missing file is put back too (there is nothing to back up).</summary>
    [Fact]
    public async Task AMissingFileIsPutBack()
    {
        var zip = Zip(CompressionLevel.Optimal, ("art/a.ddt", "AAAA"), ("data/b.xml", "<b/>"));
        var (install, plan, hashes, _) = await Setup(zip, ("art/a.ddt", "AAAA", "x"));
        File.Delete(Path.Combine(install, "art", "a.ddt"));

        var restored = await GranularRestore.RestoreAsync(Over(zip), install, plan, hashes, Backup(), null, CancellationToken.None);
        Assert.Single(restored!);
        Assert.Equal("AAAA", Read(install, "art/a.ddt"));
    }

    /// <summary>
    /// THE ONE THAT MATTERS. Same size, other bytes — a patch changed the file after the payload was
    /// built. The remote directory cannot tell; the SHA check on the inflated bytes does, and the
    /// install is left exactly as it was.
    /// </summary>
    [Fact]
    public async Task APayloadFileThatIsNotTheRecordedOneWritesNothing()
    {
        var zip = Zip(CompressionLevel.Optimal, ("art/a.ddt", "OLD-bytes"), ("data/b.xml", "<b/>"));
        var (install, plan, hashes, _) = await Setup(zip, ("art/a.ddt", "NEW-bytes", "damaged!!"));
        Assert.True(plan.AllCoverable);   // the size matches: only the hash can catch it

        var restored = await GranularRestore.RestoreAsync(Over(zip), install, plan, hashes, Backup(), null, CancellationToken.None);
        Assert.Null(restored);
        Assert.Equal("damaged!!", Read(install, "art/a.ddt"));
        Assert.True(NoStaging(install));
    }

    [Fact]
    public async Task OneBadFileStopsTheWholeSet_BeforeAnythingIsWritten()
    {
        var zip = Zip(CompressionLevel.Optimal, ("art/a.ddt", "good-a"), ("art/b.ddt", "OLD-b"), ("data/c.xml", "c"));
        var (install, plan, hashes, _) = await Setup(zip,
            ("art/a.ddt", "good-a", "bad-a!"),
            ("art/b.ddt", "NEW-b", "bad-b"));
        bool journal = false;

        var restored = await GranularRestore.RestoreAsync(Over(zip), install, plan, hashes, Backup(),
            () => journal = true, CancellationToken.None);

        Assert.Null(restored);
        Assert.Equal("bad-a!", Read(install, "art/a.ddt"));   // the good one was NOT written either
        Assert.False(journal);
        Assert.True(NoStaging(install));
    }

    [Fact]
    public async Task ASwapThatFailsHalfWayRollsBackWhatItAlreadySwapped()
    {
        var zip = Zip(CompressionLevel.Optimal, ("art/a.ddt", "good-a"), ("art/b.ddt", "good-b"), ("data/c.xml", "c"));
        var (install, plan, hashes, _) = await Setup(zip,
            ("art/a.ddt", "good-a", "bad-a!"),
            ("art/b.ddt", "good-b", "bad-b!"));

        IReadOnlyList<string>? restored;
        using (new FileStream(Path.Combine(install, "art", "b.ddt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            restored = await GranularRestore.RestoreAsync(Over(zip), install, plan, hashes, Backup(), null, CancellationToken.None);

        Assert.Null(restored);
        Assert.Equal("bad-a!", Read(install, "art/a.ddt"));
        Assert.Equal("bad-b!", Read(install, "art/b.ddt"));
        Assert.True(NoStaging(install));
    }

    // ---------------------------------------------------------------- MayAct

    private static GranularPlan PlanOf(long compressed, params GranularVerdict[] verdicts)
        => new(verdicts.Select((v, i) => new GranularItem($"f{i}", v,
                new PayloadEntry($"f{i}", 0, compressed, compressed * 2, 0, 8))).ToList(),
            verdicts.Count(v => v == GranularVerdict.Coverable) * compressed);

    [Fact]
    public void ItActsOnlyWhenEveryFileIsCoverableAndItIsClearlyCheaper()
    {
        var gib = 1024L * 1024 * 1024;
        Assert.True(GranularRestore.MayAct(PlanOf(1_000_000, GranularVerdict.Coverable), 5 * gib, 0));
        Assert.True(GranularRestore.MayAct(PlanOf(1_000_000, GranularVerdict.Coverable), -1, 0));   // full size unknown

        Assert.False(GranularRestore.MayAct(PlanOf(1_000_000, GranularVerdict.Coverable, GranularVerdict.SizeDiffers), 5 * gib, 0));
        Assert.False(GranularRestore.MayAct(PlanOf(1_000_000, GranularVerdict.Coverable), 5 * gib, structuralFindings: 1));
        Assert.False(GranularRestore.MayAct(PlanOf(1_000_000, GranularVerdict.Coverable), 3_000_000, 0)); // not ≤ ¼
        Assert.False(GranularRestore.MayAct(new GranularPlan(Array.Empty<GranularItem>(), 0), 5 * gib, 0));
        Assert.False(GranularRestore.MayAct(PlanOf(GranularRestore.MaxFileBytes + 1, GranularVerdict.Coverable), 50 * gib, 0));
    }
}
