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
        // Privacy: the config (Discord token) is never bundled.
        Assert.DoesNotContain("launcher-config.json", names);
        // Subfolders are not included.
        Assert.DoesNotContain(names, n => n.Contains("wol-icon.png", StringComparison.OrdinalIgnoreCase));
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
}
