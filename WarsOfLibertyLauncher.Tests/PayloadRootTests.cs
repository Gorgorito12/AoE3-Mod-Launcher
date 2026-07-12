using System;
using System.Collections.Generic;
using System.IO;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Tests for <see cref="NativeInstallService.NormalizePayloadRoot"/> — the single-
/// wrapper-folder flatten that lets a mod whose zip nests everything inside one
/// folder (e.g. <c>Knights and Barbarians/data/…</c>) overlay at the install root
/// instead of one level too deep. Must be a NO-OP for a normal flat payload
/// (WoL / Improvement Mod ship several top-level dirs).
/// </summary>
public class PayloadRootTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private string NewTempDir()
    {
        var dir = Directory.CreateTempSubdirectory("wol-payloadroot-").FullName;
        _tempDirs.Add(dir);
        return dir;
    }

    public void Dispose()
    {
        foreach (var d in _tempDirs)
            try { Directory.Delete(d, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void SingleWrapperFolder_DescendsIntoIt()
    {
        // extracted/Knights and Barbarians/{data,art}  → root becomes the wrapper.
        var root = NewTempDir();
        var wrapper = Path.Combine(root, "Knights and Barbarians");
        Directory.CreateDirectory(Path.Combine(wrapper, "data"));
        Directory.CreateDirectory(Path.Combine(wrapper, "art"));
        File.WriteAllText(Path.Combine(wrapper, "data", "protoy.xml"), "x");

        var resolved = NativeInstallService.NormalizePayloadRoot(root);
        Assert.Equal(wrapper, resolved);
    }

    [Fact]
    public void FlatPayload_ReturnsUnchanged()
    {
        // Several top-level dirs (the WoL/IM shape) → no flatten.
        var root = NewTempDir();
        Directory.CreateDirectory(Path.Combine(root, "data"));
        Directory.CreateDirectory(Path.Combine(root, "art"));
        Directory.CreateDirectory(Path.Combine(root, "Sound"));

        Assert.Equal(root, NativeInstallService.NormalizePayloadRoot(root));
    }

    [Fact]
    public void SingleDirPlusLooseFile_DoesNotDescend()
    {
        // One folder BUT also a loose file at the root → not a pure wrapper.
        var root = NewTempDir();
        Directory.CreateDirectory(Path.Combine(root, "data"));
        File.WriteAllText(Path.Combine(root, "readme.txt"), "x");

        Assert.Equal(root, NativeInstallService.NormalizePayloadRoot(root));
    }

    [Fact]
    public void DoubleWrapper_DescendsUntilRealContent()
    {
        // A doubly-wrapped payload descends until the level with real content.
        var root = NewTempDir();
        var inner = Path.Combine(root, "outer", "inner");
        Directory.CreateDirectory(Path.Combine(inner, "data"));
        Directory.CreateDirectory(Path.Combine(inner, "art"));

        Assert.Equal(inner, NativeInstallService.NormalizePayloadRoot(root));
    }

    [Fact]
    public void EmptyFolder_ReturnsUnchanged()
    {
        var root = NewTempDir();
        Assert.Equal(root, NativeInstallService.NormalizePayloadRoot(root));
    }
}
