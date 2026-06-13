using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Tests for the GitHubReleases update-time file deletion: the "net-new vs
/// shadow" overlay classification (with the sticky rule) and the deletion pass
/// itself (explicit delete.lst + auto net-new, with the load-bearing guard that
/// a base-shadowing file is NEVER auto-deleted, and path-traversal defence).
///
/// Covers <see cref="NativeInstallService.ClassifyOverlay"/> and
/// <see cref="NativeInstallService.ApplyUpdateDeletions"/>.
/// </summary>
public class OverlayDeletionTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private string NewTempDir()
    {
        var dir = Directory.CreateTempSubdirectory("overlay-del-test-").FullName;
        _tempDirs.Add(dir);
        return dir;
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }

    private static NativeInstallService.OverlayCaptureResult Capture(
        IEnumerable<string> allFiles, IEnumerable<string> freshOnDisk) =>
        new(new List<string>(allFiles), new List<string>(freshOnDisk));

    private static InstallManifest Manifest(
        IEnumerable<string> overlayFiles, IEnumerable<string> overlayNetNew) =>
        new() { OverlayFiles = new List<string>(overlayFiles), OverlayNetNew = new List<string>(overlayNetNew) };

    // ---------------- ClassifyOverlay ----------------

    [Fact]
    public void Classify_FreshInstall_UsesExistenceAtCopyTime()
    {
        // No previous manifest: net-new == "wasn't on disk when copied".
        var cap = Capture(allFiles: new[] { "a.txt", "b.txt" }, freshOnDisk: new[] { "a.txt" });
        var (files, netNew) = NativeInstallService.ClassifyOverlay(cap, previous: null);

        Assert.Equal(new[] { "a.txt", "b.txt" }, files);
        Assert.Equal(new[] { "a.txt" }, netNew);   // b.txt shadowed a base file
    }

    [Fact]
    public void Classify_Sticky_NetNewStaysNetNew_OnReoverlay()
    {
        // Re-overlay: the file already exists, so FreshOnDisk is empty. Without
        // stickiness a.txt would be mis-flagged as shadow and lost forever.
        var prev = Manifest(overlayFiles: new[] { "a.txt", "b.txt" }, overlayNetNew: new[] { "a.txt" });
        var cap = Capture(allFiles: new[] { "a.txt", "b.txt" }, freshOnDisk: Array.Empty<string>());

        var (_, netNew) = NativeInstallService.ClassifyOverlay(cap, prev);

        Assert.Contains("a.txt", netNew);      // sticky: still net-new
        Assert.DoesNotContain("b.txt", netNew); // sticky: still shadow
    }

    [Fact]
    public void Classify_Sticky_ShadowNeverBecomesNetNew()
    {
        // Even if a shadow file shows up as "fresh on disk" this run, its prior
        // shadow status sticks — it must never be promoted to net-new.
        var prev = Manifest(overlayFiles: new[] { "s.xml" }, overlayNetNew: Array.Empty<string>());
        var cap = Capture(allFiles: new[] { "s.xml" }, freshOnDisk: new[] { "s.xml" });

        var (_, netNew) = NativeInstallService.ClassifyOverlay(cap, prev);

        Assert.DoesNotContain("s.xml", netNew);
    }

    [Fact]
    public void Classify_GenuinelyNewPaths_UseExistence()
    {
        // Paths absent from the previous overlay are classified by existence.
        var prev = Manifest(overlayFiles: new[] { "a.txt" }, overlayNetNew: new[] { "a.txt" });
        var cap = Capture(
            allFiles: new[] { "a.txt", "c.txt", "d.txt" },
            freshOnDisk: new[] { "c.txt" });  // c didn't exist (net-new); d did (shadow)

        var (_, netNew) = NativeInstallService.ClassifyOverlay(cap, prev);

        Assert.Contains("a.txt", netNew);       // sticky
        Assert.Contains("c.txt", netNew);       // genuinely new + fresh
        Assert.DoesNotContain("d.txt", netNew); // genuinely new but shadowed a base file
    }

    // ---------------- ApplyUpdateDeletions ----------------

    private static void Touch(string dir, string rel)
    {
        var full = Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "x");
    }

    private static bool Exists(string dir, string rel) =>
        File.Exists(Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar)));

    [Fact]
    public void Apply_AutoDeletes_DroppedNetNew()
    {
        var install = NewTempDir();
        Touch(install, "a.txt");   // net-new, dropped in the new release
        Touch(install, "b.txt");   // shadow, kept

        var prev = Manifest(overlayFiles: new[] { "a.txt", "b.txt" }, overlayNetNew: new[] { "a.txt" });
        var cap = Capture(allFiles: new[] { "b.txt" }, freshOnDisk: Array.Empty<string>());

        NativeInstallService.ApplyUpdateDeletions(install, cap, prev, null);

        Assert.False(Exists(install, "a.txt"));  // net-new no longer shipped → deleted
        Assert.True(Exists(install, "b.txt"));
    }

    [Fact]
    public void Apply_KeepsNetNew_StillShipped()
    {
        var install = NewTempDir();
        Touch(install, "a.txt");

        var prev = Manifest(overlayFiles: new[] { "a.txt" }, overlayNetNew: new[] { "a.txt" });
        var cap = Capture(allFiles: new[] { "a.txt", "c.txt" }, freshOnDisk: new[] { "c.txt" });

        NativeInstallService.ApplyUpdateDeletions(install, cap, prev, null);

        Assert.True(Exists(install, "a.txt"));  // still in the new release → kept
    }

    [Fact]
    public void Apply_NeverAutoDeletes_Shadow()
    {
        // THE load-bearing regression: a base-shadowing file the new release
        // stopped shipping must NOT be auto-deleted (it would leave a hole).
        var install = NewTempDir();
        Touch(install, "s.xml");  // shadow (NOT in overlayNetNew)

        var prev = Manifest(overlayFiles: new[] { "s.xml" }, overlayNetNew: Array.Empty<string>());
        var cap = Capture(allFiles: Array.Empty<string>(), freshOnDisk: Array.Empty<string>());

        NativeInstallService.ApplyUpdateDeletions(install, cap, prev, null);

        Assert.True(Exists(install, "s.xml"));  // survives — base game stays intact
    }

    [Fact]
    public void Apply_DeleteList_RemovesListedAndItself()
    {
        var install = NewTempDir();
        Touch(install, "old.xml");
        Touch(install, "keep.xml");
        File.WriteAllText(Path.Combine(install, "delete.lst"),
            "# remove obsolete files\nold.xml\n");

        var prev = Manifest(overlayFiles: new[] { "old.xml", "keep.xml" }, overlayNetNew: Array.Empty<string>());
        var cap = Capture(allFiles: new[] { "keep.xml", "delete.lst" }, freshOnDisk: Array.Empty<string>());

        NativeInstallService.ApplyUpdateDeletions(install, cap, prev, null);

        Assert.False(Exists(install, "old.xml"));   // listed → deleted
        Assert.True(Exists(install, "keep.xml"));
        Assert.False(Exists(install, "delete.lst")); // instruction file removed
        Assert.DoesNotContain("delete.lst", cap.AllFiles); // dropped from capture
    }

    [Fact]
    public void Apply_DeleteList_RejectsPathTraversal()
    {
        var root = NewTempDir();
        var install = Path.Combine(root, "install");
        Directory.CreateDirectory(install);
        var secret = Path.Combine(root, "secret.txt");
        File.WriteAllText(secret, "do not touch");

        File.WriteAllText(Path.Combine(install, "delete.lst"), "..\\secret.txt\n");

        var prev = Manifest(overlayFiles: Array.Empty<string>(), overlayNetNew: Array.Empty<string>());
        var cap = Capture(allFiles: new[] { "delete.lst" }, freshOnDisk: Array.Empty<string>());

        NativeInstallService.ApplyUpdateDeletions(install, cap, prev, null);

        Assert.True(File.Exists(secret));  // escape rejected — file outside install survives
    }

    // ---------------- End-to-end (exercises the real capture seam) ----------------

    [Fact]
    public async Task Integration_CopyClassifyDelete_EndToEnd()
    {
        var install = NewTempDir();
        Touch(install, "data/base.xml");   // simulated cloned base-game file

        // v1 overlay: shadows base.xml, adds two net-new files.
        var v1 = NewTempDir();
        Touch(v1, "data/base.xml");
        Touch(v1, "data/mod_a.xml");
        Touch(v1, "data/mod_b.xml");

        var svc = new NativeInstallService();
        var cap1 = await svc.CopyPayloadToDestinationAsync(v1, install, null, null, default);
        var (files1, netNew1) = NativeInstallService.ClassifyOverlay(cap1, null);

        // The REAL File.Exists-before-Copy classification ran here, not a hand-built capture.
        Assert.Contains("data/mod_a.xml", netNew1);
        Assert.Contains("data/mod_b.xml", netNew1);
        Assert.DoesNotContain("data/base.xml", netNew1);  // shadowed the base → not net-new

        var manifest1 = Manifest(files1, netNew1);

        // v2 overlay: drops mod_a, keeps base + mod_b.
        var v2 = NewTempDir();
        Touch(v2, "data/base.xml");
        Touch(v2, "data/mod_b.xml");

        var cap2 = await svc.CopyPayloadToDestinationAsync(v2, install, null, null, default);
        NativeInstallService.ApplyUpdateDeletions(install, cap2, manifest1, null);

        Assert.False(Exists(install, "data/mod_a.xml"));  // dropped net-new → deleted
        Assert.True(Exists(install, "data/mod_b.xml"));   // still shipped → kept
        Assert.True(Exists(install, "data/base.xml"));    // shadow survives → game intact
    }
}
