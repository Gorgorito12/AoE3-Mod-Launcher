using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Tests for <see cref="DiagnosticLog.ExportBundle"/>: the one-file diagnostic
/// bundle a user attaches when reporting a bug. The load-bearing rule is the
/// privacy exclusion — the config (Discord session token) must NEVER end up in
/// the shared zip.
/// </summary>
public class DiagnosticLogTests : IDisposable
{
    private readonly List<string> _tempPaths = new();

    private string NewTempDir()
    {
        var dir = Directory.CreateTempSubdirectory("diag-test-").FullName;
        _tempPaths.Add(dir);
        return dir;
    }

    public void Dispose()
    {
        foreach (var p in _tempPaths)
        {
            try { if (Directory.Exists(p)) Directory.Delete(p, recursive: true); else File.Delete(p); }
            catch { /* best-effort */ }
        }
    }

    [Fact]
    public void ExportBundle_IncludesLogsAndSnapshots_ExcludesConfig()
    {
        var src = NewTempDir();
        File.WriteAllText(Path.Combine(src, "launcher-debug.log"), "log contents");
        File.WriteAllText(Path.Combine(src, "multiplayer-events.log"), "telemetry");
        File.WriteAllText(Path.Combine(src, "UpdateInfo-snapshot.xml"), "<xml/>");
        // The install integrity summary rides the same *snapshot* glob — its NAME
        // is the only thing that gets it into the bundle, so pin that here.
        File.WriteAllText(Path.Combine(src, InstallSnapshot.FileName), "=== Install snapshot ===");
        File.WriteAllText(Path.Combine(src, "launcher-config.json"), "{\"sessionToken\":\"SECRET\"}");
        // A subfolder must be ignored (TopDirectoryOnly).
        Directory.CreateDirectory(Path.Combine(src, "mod-assets"));
        File.WriteAllText(Path.Combine(src, "mod-assets", "wol-icon.png"), "PNG");

        var zipPath = Path.Combine(NewTempDir(), "bundle.zip");
        DiagnosticLog.ExportBundle(zipPath, src);

        Assert.True(File.Exists(zipPath));
        using var zip = ZipFile.OpenRead(zipPath);
        var names = zip.Entries.Select(e => e.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("launcher-debug.log", names);
        Assert.Contains("multiplayer-events.log", names);
        Assert.Contains("UpdateInfo-snapshot.xml", names);
        Assert.Contains(InstallSnapshot.FileName, names);
        // Privacy: the config (Discord token) is never bundled.
        Assert.DoesNotContain("launcher-config.json", names);
        // Subfolders are not included.
        Assert.DoesNotContain(names, n => n.Contains("wol-icon.png", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    // Small OOS / sync / text-log artifacts are the whole point — included.
    [InlineData("oosSyncData.txt", 1024, true)]
    [InlineData("SyncLog.txt", 4096, true)]
    [InlineData("warnings.txt", 512, true)]
    [InlineData("engine.log", 2048, true)]
    [InlineData("OOSdump", 100, true)]                 // name match, no extension
    // Recorded games / savegames / configs / binaries are never swept.
    [InlineData("partida.age3Yrec", 5_000_000, false)] // over cap AND no name match
    [InlineData("save.age3Ysav", 200_000, false)]
    [InlineData("user.cfg", 300, false)]
    [InlineData("icon.png", 60_000, false)]
    // A matching name but over the size cap is rejected (e.g. a huge .txt).
    [InlineData("hugeSync.txt", DiagnosticLog.GameFileMaxBytes + 1, false)]
    [InlineData("", 100, false)]
    public void ShouldIncludeGameFile_TakesOnlySmallOosSyncLogs(string name, long size, bool expected)
    {
        Assert.Equal(expected, DiagnosticLog.ShouldIncludeGameFile(name, size));
    }

    [Fact]
    public void ExportBundle_IncludesGameUserDataArtifacts_AndListing()
    {
        var src = NewTempDir();
        File.WriteAllText(Path.Combine(src, "launcher-debug.log"), "log");

        var game = NewTempDir();
        File.WriteAllText(Path.Combine(game, "oosSyncData.txt"), "desync at tick 12345");
        File.WriteAllText(Path.Combine(game, "user.cfg"), "should be skipped");
        // A recorded game is too big / wrong kind — must be skipped.
        File.WriteAllBytes(Path.Combine(game, "match.age3Yrec"),
            new byte[DiagnosticLog.GameFileMaxBytes + 10]);

        var zipPath = Path.Combine(NewTempDir(), "bundle.zip");
        DiagnosticLog.ExportBundle(zipPath, src, game);

        using var zip = ZipFile.OpenRead(zipPath);
        var names = zip.Entries.Select(e => e.FullName.Replace('\\', '/'))
                               .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("game-userdata/oosSyncData.txt", names);
        Assert.Contains("game-userdata/game-userdata-listing.txt", names);
        // The config-like and oversized recorded-game files are NOT copied.
        Assert.DoesNotContain(names, n => n.EndsWith("user.cfg", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.EndsWith(".age3Yrec", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExportBundle_OverwritesExistingZip()
    {
        var src = NewTempDir();
        File.WriteAllText(Path.Combine(src, "launcher-debug.log"), "v1");

        var zipPath = Path.Combine(NewTempDir(), "bundle.zip");
        File.WriteAllText(zipPath, "not a zip");   // stale file in the way

        DiagnosticLog.ExportBundle(zipPath, src);   // must delete + recreate, not throw

        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Contains(zip.Entries, e => e.FullName.Equals("launcher-debug.log", StringComparison.OrdinalIgnoreCase));
    }

    // ---------- rotated logs ----------
    //
    // A player sent a bundle for two matches that had gone wrong FOUR DAYS earlier. The launcher
    // rotated one generation per launch and he had opened it many times since, so both logs in
    // the bundle described that morning and the evening in question was long gone. Nobody reports
    // a multiplayer problem within one restart.

    /// <summary>
    /// THE ONE THAT MATTERS. Every generation has to reach the bundle, and the only thing that
    /// puts it there is the <c>*.log</c> glob — a generation named anything else would rotate
    /// perfectly and never be seen by anybody.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_EveryRotatedGenerationReachesTheBundle()
    {
        var src = NewTempDir();
        File.WriteAllText(Path.Combine(src, "launcher-debug.log"), "this session");
        for (var n = 1; n <= DiagnosticLog.KeepPreviousLogs; n++)
            File.WriteAllText(
                Path.Combine(src, Path.GetFileName(DiagnosticLog.RotatedLogPath(n))), $"session -{n}");

        var zipPath = Path.Combine(NewTempDir(), "bundle.zip");
        DiagnosticLog.ExportBundle(zipPath, src);

        using var zip = ZipFile.OpenRead(zipPath);
        var names = zip.Entries.Select(e => e.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var n = 1; n <= DiagnosticLog.KeepPreviousLogs; n++)
            Assert.Contains(Path.GetFileName(DiagnosticLog.RotatedLogPath(n)), names);
    }

    /// <summary>
    /// Generation 1 keeps the historical name. CLAUDE.md names it, the older tests above assert
    /// on it, and a player who has been asked for <c>launcher-debug.prev.log</c> by name should
    /// still find one — renaming it to <c>prev1</c> would be tidier and would break all three.
    /// </summary>
    [Fact]
    public void TheNewestGenerationKeepsTheNameEverythingElseAlreadyUses()
        => Assert.Equal("launcher-debug.prev.log", Path.GetFileName(DiagnosticLog.RotatedLogPath(1)));

    /// <summary>The generations are distinct files, or the ring overwrites itself.</summary>
    [Fact]
    public void TheGenerationsDoNotCollide()
    {
        var names = Enumerable.Range(1, DiagnosticLog.KeepPreviousLogs)
            .Select(n => Path.GetFileName(DiagnosticLog.RotatedLogPath(n)))
            .ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(names, n => Assert.EndsWith(".log", n, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Deep enough to outlast the restarts between a match going wrong and somebody writing it
    /// up, shallow enough that the bundle stays a thing you can drop into Discord — a real
    /// session's log measured ~58 KB.
    /// </summary>
    [Fact]
    public void TheRingIsDeepEnoughToSurviveAFewRestarts()
        => Assert.InRange(DiagnosticLog.KeepPreviousLogs, 3, 10);
}
