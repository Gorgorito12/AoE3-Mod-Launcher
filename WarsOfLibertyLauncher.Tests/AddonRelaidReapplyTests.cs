using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="AddonService.ReapplyRelaidAsync"/> — the re-apply a repair uses once it knows
/// which files it rewrote. The old whole-addon re-apply discarded every backup and backed up the
/// ADDON's own bytes for the files the repair had not touched, so the mod's real files were lost
/// for good and disabling the addon changed nothing. The cases where nothing must change are
/// the ones that matter.
/// </summary>
public class AddonRelaidReapplyTests : IDisposable
{
    private readonly List<string> _tempDirs = new();
    private static readonly ModProfile Profile = new() { Id = "wol", DisplayName = "Wars of Liberty" };

    private string NewTempDir()
    {
        var dir = Directory.CreateTempSubdirectory("addon-relaid-").FullName;
        _tempDirs.Add(dir);
        return dir;
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
            try { Directory.Delete(dir, recursive: true); } catch { }
    }

    private string MakeInstall(params (string Rel, string Content)[] files)
    {
        var root = NewTempDir();
        foreach (var (rel, content) in files)
        {
            var full = Path.Combine(root, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }
        new InstallManifest
        {
            ModId = "wol", InstallPath = root,
            OverlayFiles = files.Select(f => f.Rel.Replace('\\', '/')).ToList(),
        }.Save();
        return root;
    }

    private string MakeZip(params (string Entry, string Content)[] entries)
    {
        var path = Path.Combine(NewTempDir(), "addon.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, content) in entries)
        {
            using var writer = new StreamWriter(zip.CreateEntry(entry).Open());
            writer.Write(content);
        }
        return path;
    }

    private static string Read(string root, string rel) => File.ReadAllText(Path.Combine(root, rel));
    private static void Write(string root, string rel, string text) => File.WriteAllText(Path.Combine(root, rel), text);

    [Fact]
    public async Task NothingReLaid_LeavesTheOriginalBackupsAlone()
    {
        var install = MakeInstall((@"art\ui\a.ddt", "MOD A"), (@"art\ui\b.ddt", "MOD B"));
        var zip = MakeZip(("art/ui/a.ddt", "ADDON A"), ("art/ui/b.ddt", "ADDON B"));
        await AddonService.ApplyAsync(install, "ui", zip, Profile, false);

        await AddonService.ReapplyRelaidAsync(install, new[] { "ui" }, Array.Empty<string>(),
            (_, _) => Task.FromResult<string?>(zip), Profile);

        await AddonService.DisableAsync(install, "ui", Profile);
        Assert.Equal("MOD A", Read(install, @"art\ui\a.ddt"));
        Assert.Equal("MOD B", Read(install, @"art\ui\b.ddt"));
    }

    [Fact]
    public async Task OnlyTheReLaidFileIsReBased_TheOtherKeepsItsTrueOriginal()
    {
        var install = MakeInstall((@"art\ui\a.ddt", "MOD A v1"), (@"art\ui\b.ddt", "MOD B"));
        var zip = MakeZip(("art/ui/a.ddt", "ADDON A"), ("art/ui/b.ddt", "ADDON B"));
        await AddonService.ApplyAsync(install, "ui", zip, Profile, false);

        // A repair re-lays ONLY a.ddt, with the payload's current bytes.
        Write(install, @"art\ui\a.ddt", "MOD A v2");
        await AddonService.ReapplyRelaidAsync(install, new[] { "ui" }, new[] { "art/ui/a.ddt" },
            (_, _) => Task.FromResult<string?>(zip), Profile);

        Assert.Equal("ADDON A", Read(install, @"art\ui\a.ddt"));
        Assert.Equal("ADDON B", Read(install, @"art\ui\b.ddt"));

        await AddonService.DisableAsync(install, "ui", Profile);
        Assert.Equal("MOD A v2", Read(install, @"art\ui\a.ddt"));   // re-based
        Assert.Equal("MOD B", Read(install, @"art\ui\b.ddt"));      // untouched original
    }

    [Fact]
    public async Task APartialReApplyNeverShrinksWhatTheAddonOwns()
    {
        var install = MakeInstall((@"art\ui\a.ddt", "MOD A"));
        var zip = MakeZip(("art/ui/a.ddt", "ADDON A"), ("art/ui/new.ddt", "ADDED BY ADDON"));
        await AddonService.ApplyAsync(install, "ui", zip, Profile, false);

        Write(install, @"art\ui\a.ddt", "MOD A");
        await AddonService.ReapplyRelaidAsync(install, new[] { "ui" }, new[] { "art/ui/a.ddt" },
            (_, _) => Task.FromResult<string?>(zip), Profile);

        var owned = AddonOwnership.Load(install)["ui"];
        Assert.Contains(owned, p => p.Equals("art/ui/new.ddt", StringComparison.OrdinalIgnoreCase));

        // So disabling still removes the file the addon added.
        await AddonService.DisableAsync(install, "ui", Profile);
        Assert.False(File.Exists(Path.Combine(install, @"art\ui\new.ddt")));
    }

    [Fact]
    public async Task ArchiveGone_DisableRestoresTheCurrentPayloadNotAnOlderOne()
    {
        var install = MakeInstall((@"art\ui\a.ddt", "MOD A v1"));
        var zip = MakeZip(("art/ui/a.ddt", "ADDON A"));
        await AddonService.ApplyAsync(install, "ui", zip, Profile, false);

        Write(install, @"art\ui\a.ddt", "MOD A v2");
        await AddonService.ReapplyRelaidAsync(install, new[] { "ui" }, new[] { "art/ui/a.ddt" },
            (_, _) => Task.FromResult<string?>(null), Profile);

        Assert.Equal("MOD A v2", Read(install, @"art\ui\a.ddt"));
        await AddonService.DisableAsync(install, "ui", Profile);
        Assert.Equal("MOD A v2", Read(install, @"art\ui\a.ddt"));
    }

    [Fact]
    public async Task AFileTheAddonAddedThatThePayloadNowShipsIsRestoredNotDeleted()
    {
        var install = MakeInstall((@"art\ui\a.ddt", "MOD A"));
        var zip = MakeZip(("art/ui/extra.ddt", "ADDED BY ADDON"));
        await AddonService.ApplyAsync(install, "ui", zip, Profile, false);

        // The repair laid a payload that now ships extra.ddt itself.
        Write(install, @"art\ui\extra.ddt", "PAYLOAD EXTRA");
        await AddonService.ReapplyRelaidAsync(install, new[] { "ui" }, new[] { "art/ui/extra.ddt" },
            (_, _) => Task.FromResult<string?>(zip), Profile);

        await AddonService.DisableAsync(install, "ui", Profile);
        Assert.Equal("PAYLOAD EXTRA", Read(install, @"art\ui\extra.ddt"));
    }

    [Fact]
    public async Task AFileTheOperationRemovedIsNotResurrectedByADisable()
    {
        var install = MakeInstall((@"art\ui\a.ddt", "MOD A"), (@"art\ui\gone.ddt", "MOD GONE"));
        var zip = MakeZip(("art/ui/a.ddt", "ADDON A"), ("art/ui/gone.ddt", "ADDON GONE"));
        await AddonService.ApplyAsync(install, "ui", zip, Profile, false);

        // A patch's delete list removes gone.ddt and re-lays nothing else the addon owns.
        File.Delete(Path.Combine(install, @"art\ui\gone.ddt"));
        await AddonService.ReapplyRelaidAsync(install, new[] { "ui" }, Array.Empty<string>(),
            (_, _) => Task.FromResult<string?>(zip), Profile);

        Assert.Equal("ADDON GONE", Read(install, @"art\ui\gone.ddt"));   // the addon still wants it
        Assert.Equal("ADDON A", Read(install, @"art\ui\a.ddt"));

        await AddonService.DisableAsync(install, "ui", Profile);
        Assert.False(File.Exists(Path.Combine(install, @"art\ui\gone.ddt")));   // not resurrected
        Assert.Equal("MOD A", Read(install, @"art\ui\a.ddt"));                  // true original kept
    }

    [Fact]
    public async Task AnAddonEnabledOnAnotherCopyIsNotAppliedHere()
    {
        var install = MakeInstall((@"art\ui\a.ddt", "MOD A"));
        var zip = MakeZip(("art/ui/a.ddt", "ADDON A"));

        await AddonService.ReapplyRelaidAsync(install, new[] { "ui" }, new[] { "art/ui/a.ddt" },
            (_, _) => Task.FromResult<string?>(zip), Profile);

        Assert.Equal("MOD A", Read(install, @"art\ui\a.ddt"));
        Assert.False(AddonOwnership.Load(install).ContainsKey("ui"));
    }
}
