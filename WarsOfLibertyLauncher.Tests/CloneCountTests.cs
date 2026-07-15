using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins that <see cref="FolderCloneService.CloneAsync"/>'s return value means
/// "files actually copied".
///
/// It used to increment the counter OUTSIDE the try that guards the copy, so a
/// file skipped for access-denied or a sharing violation still counted — making
/// "Clone complete: N/N" mean ATTEMPTED/enumerated. That is not just a cosmetic
/// log bug: <c>InstallAsync</c> gates on
/// <c>if (clonedFiles == 0) throw InstallBaseGameMissingException</c>, so an
/// inflated count means a clone where every file failed would sail past the very
/// gate that exists to stop a mod being overlaid on an empty base game.
///
/// The skip paths themselves need a locked/ACL'd file, which is not reliably
/// reproducible in CI — so these pin the accounting contract instead.
/// </summary>
public class CloneCountTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private string NewTempDir()
    {
        var dir = Directory.CreateTempSubdirectory("clone-test-").FullName;
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

    private static void Write(string root, string rel, string content = "x")
    {
        var path = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    /// <summary>The count must equal what actually landed on disk, not what was enumerated.</summary>
    [Fact]
    public async void CloneAsync_ReturnsFilesActuallyCopied()
    {
        var src = NewTempDir();
        var dst = Path.Combine(NewTempDir(), "clone");
        Write(src, "bin/age3y.exe");
        Write(src, "data/protoy.xml");
        Write(src, "data/techtreey.xml");

        int copied = await new FolderCloneService().CloneAsync(src, dst);

        var landed = Directory.GetFiles(dst, "*", SearchOption.AllDirectories).Length;
        Assert.Equal(landed, copied);
        Assert.Equal(3, copied);
    }

    /// <summary>
    /// An empty source must return 0 — this is the value InstallAsync's
    /// base-game-missing gate keys off, so it has to stay honest.
    /// </summary>
    [Fact]
    public async void CloneAsync_EmptySource_ReturnsZero_SoTheInstallGateFires()
    {
        var src = NewTempDir();
        var dst = Path.Combine(NewTempDir(), "clone");

        int copied = await new FolderCloneService().CloneAsync(src, dst);

        Assert.Equal(0, copied);
    }

    /// <summary>
    /// Files excluded by the skip patterns (storefront metadata) are never
    /// attempted, so they must not inflate the count either.
    /// </summary>
    [Fact]
    public async void CloneAsync_SkipPatternedFiles_AreNotCounted()
    {
        var src = NewTempDir();
        var dst = Path.Combine(NewTempDir(), "clone");
        Write(src, "data/protoy.xml");
        Write(src, "steam_appid.txt");        // SkipPatterns
        Write(src, "installscript.vdf");      // SkipPatterns

        int copied = await new FolderCloneService().CloneAsync(src, dst);

        var landed = Directory.GetFiles(dst, "*", SearchOption.AllDirectories).Length;
        Assert.Equal(landed, copied);
        Assert.Equal(1, copied);
    }
}
