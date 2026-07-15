using System;
using System.Collections.Generic;
using System.IO;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Tests for <see cref="InstallSnapshot"/> — the install-integrity summary that
/// ships in a diagnostic bundle.
///
/// It exists because a real report (the game showing an older version than the
/// launcher reported) could NOT be closed from a bundle: the log only carried the
/// <c>_originals</c> hash, never the live file's, and nothing recorded whether a
/// manifest-tracked file was simply gone. These pin the two answers.
/// </summary>
public class InstallSnapshotTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private string NewTempDir()
    {
        var dir = Directory.CreateTempSubdirectory("snapshot-test-").FullName;
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

    private static void Write(string root, string rel, string content)
    {
        var path = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    /// <summary>A manifest whose FileHashes point at <paramref name="rels"/>.</summary>
    private static InstallManifest ManifestWith(string installPath, params string[] rels)
    {
        var m = new InstallManifest { ModId = "wol", Version = "1.2.0e", InstallPath = installPath };
        foreach (var rel in rels)
            m.FileHashes[rel] = new FileFingerprint { Size = 1, Sha256 = "deadbeef" };
        m.Save();
        return m;
    }

    /// <summary>
    /// The headline case: a file the manifest tracks is GONE from disk (what a
    /// Defender quarantine looks like). The summary must count it and name it —
    /// that is the whole point of shipping this in a bundle.
    /// </summary>
    [Fact]
    public async void Build_ReportsMissingFiles_ByCountAndName()
    {
        var install = NewTempDir();
        Write(install, "data/present.xml", "x");
        ManifestWith(install, "data/present.xml", "AI3/wolai.upl");   // second one never written

        var text = await InstallSnapshot.BuildAsync("wol", install, "", "");

        Assert.Contains("MISSING: 1", text);
        Assert.Contains("AI3/wolai.upl", text);
        Assert.DoesNotContain("  data/present.xml", text);   // present ones are not listed
    }

    /// <summary>An intact install must report zero missing — no crying wolf.</summary>
    [Fact]
    public async void Build_IntactInstall_ReportsNoMissing()
    {
        var install = NewTempDir();
        Write(install, "data/a.xml", "a");
        Write(install, "data/b.xml", "b");
        ManifestWith(install, "data/a.xml", "data/b.xml");

        var text = await InstallSnapshot.BuildAsync("wol", install, "", "");

        Assert.Contains("MISSING: 0", text);
    }

    /// <summary>
    /// The exact blind spot from the report: live stringtabley.xml differs from the
    /// _originals snapshot that version detection hashes. The game reads the live
    /// file, so the summary has to surface the divergence.
    /// </summary>
    [Fact]
    public async void Build_LiveStringTableDiffersFromSnapshot_IsFlagged()
    {
        var install = NewTempDir();
        Write(install, "data/stringtabley.xml", "TRANSLATED-OR-STALE");
        Write(install, "translations/_originals/stringtabley.xml", "CANONICAL-ENGLISH");

        var text = await InstallSnapshot.BuildAsync("wol", install, "es", "1.1");

        Assert.Contains("DIFFER", text);
        Assert.Contains("'es' v1.1", text);            // the active pack is named
    }

    /// <summary>No translation applied: live == snapshot must read as "same", not a scare.</summary>
    [Fact]
    public async void Build_LiveStringTableMatchesSnapshot_ReadsAsSame()
    {
        var install = NewTempDir();
        Write(install, "data/stringtabley.xml", "CANONICAL-ENGLISH");
        Write(install, "translations/_originals/stringtabley.xml", "CANONICAL-ENGLISH");

        var text = await InstallSnapshot.BuildAsync("wol", install, "", "");

        Assert.Contains("no translation applied", text);
        Assert.Contains("(none — English)", text);
    }

    /// <summary>
    /// Manifest baseline vs live drift — the state that makes version recognition
    /// refuse to trust the baseline. Surfacing it is why the section exists.
    /// </summary>
    [Fact]
    public async void Build_KeyFileDriftFromBaseline_IsFlagged()
    {
        var install = NewTempDir();
        Write(install, "data/protoy.xml", "live-bytes");
        var m = new InstallManifest { ModId = "wol", Version = "1.2.0e", InstallPath = install };
        m.KeyFileHashes["data/protoy.xml"] = "0000000000000000ffffffffffffffff";   // not the live hash
        m.Save();

        var text = await InstallSnapshot.BuildAsync("wol", install, "", "");

        Assert.Contains("DIFFERS", text);
    }

    /// <summary>A pre-manifest install must degrade to a readable note, not throw.</summary>
    [Fact]
    public async void Build_NoManifest_DoesNotThrow()
    {
        var install = NewTempDir();
        var text = await InstallSnapshot.BuildAsync("wol", install, "", "");

        Assert.Contains("ABSENT", text);
        Assert.Contains("nothing to check", text);
    }

    /// <summary>A bogus path must not throw — diagnostics can't break the export.</summary>
    [Fact]
    public async void Build_MissingInstallFolder_DoesNotThrow()
    {
        var text = await InstallSnapshot.BuildAsync(
            "wol", Path.Combine(Path.GetTempPath(), "no-such-install-" + Guid.NewGuid()), "", "");

        Assert.Contains("does not exist", text);
    }

    /// <summary>
    /// The file name is load-bearing: ExportBundle stages *.log / *snapshot* by
    /// glob, so a rename would silently drop it out of every bundle.
    /// </summary>
    [Fact]
    public void FileName_ContainsSnapshot_SoExportBundleGlobStagesIt()
    {
        Assert.Contains("snapshot", InstallSnapshot.FileName, StringComparison.OrdinalIgnoreCase);
    }
}
