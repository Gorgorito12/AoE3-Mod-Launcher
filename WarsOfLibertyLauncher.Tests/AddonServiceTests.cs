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
/// Round-trips applying and disabling an addon over a toy install.
///
/// The reversal is the part that can quietly corrupt someone's game: an addon
/// overwrites real game files, so if the backup/restore is wrong the player is
/// left with a mod that looks fine, verifies clean (the manifest is re-captured
/// from whatever is on disk) and is silently broken. These assert the restore is
/// byte-exact and that files the addon ADDED are deleted rather than left behind.
/// </summary>
public class AddonServiceTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private string NewTempDir()
    {
        var dir = Directory.CreateTempSubdirectory("addon-test-").FullName;
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

    private static readonly ModProfile Profile = new() { Id = "wol", DisplayName = "Wars of Liberty" };

    /// <summary>A minimal install: some real files plus the manifest addons need.</summary>
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
            ModId = "wol",
            InstallPath = root,
            // Forward slashes: the manifest's convention everywhere, and what
            // RecaptureHashes matches against. Storing backslashes here would
            // make this fixture disagree with every real install.
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

    // -- The round trip --------------------------------------------------------

    [Fact]
    public async Task Apply_ThenDisable_RestoresOriginalBytes()
    {
        var install = MakeInstall((@"art\ui\panel.ddt", "ORIGINAL PANEL"));
        var zip = MakeZip((@"art/ui/panel.ddt", "TRANSPARENT PANEL"));

        var applied = await AddonService.ApplyAsync(install, "ui-transparent", zip, Profile, false);
        Assert.Equal(AddonApplyStatus.Applied, applied.Status);
        Assert.Equal("TRANSPARENT PANEL", Read(install, @"art\ui\panel.ddt"));

        Assert.True(await AddonService.DisableAsync(install, "ui-transparent", Profile));
        Assert.Equal("ORIGINAL PANEL", Read(install, @"art\ui\panel.ddt"));
    }

    /// <summary>
    /// A file the addon ADDED has no original to restore, so reverting means
    /// deleting it. Leaving it behind would make "disabled" a lie and would drift
    /// the install away from what the payload says it should contain.
    /// </summary>
    [Fact]
    public async Task Disable_DeletesFilesTheAddonAdded()
    {
        var install = MakeInstall((@"art\ui\panel.ddt", "ORIGINAL"));
        var zip = MakeZip(
            (@"art/ui/panel.ddt", "MODIFIED"),
            (@"Sound/new_musket.wav", "BRAND NEW"));

        await AddonService.ApplyAsync(install, "smoke", zip, Profile, false);
        Assert.True(File.Exists(Path.Combine(install, @"Sound\new_musket.wav")));

        await AddonService.DisableAsync(install, "smoke", Profile);

        Assert.False(File.Exists(Path.Combine(install, @"Sound\new_musket.wav")));
        Assert.Equal("ORIGINAL", Read(install, @"art\ui\panel.ddt"));
    }

    [Fact]
    public async Task Apply_RecordsOwnedFiles_AndDisableClearsThem()
    {
        var install = MakeInstall((@"art\a.ddt", "A"));
        var zip = MakeZip((@"art/a.ddt", "A2"), (@"art/b.ddt", "B"));

        await AddonService.ApplyAsync(install, "pack", zip, Profile, false);

        var manifest = InstallManifest.TryLoad(install)!;
        Assert.Equal(
            new[] { "art/a.ddt", "art/b.ddt" },
            manifest.AddonFiles["pack"].OrderBy(x => x).ToArray());

        await AddonService.DisableAsync(install, "pack", Profile);
        Assert.False(InstallManifest.TryLoad(install)!.AddonFiles.ContainsKey("pack"));
    }

    /// <summary>
    /// Verify compares each overlay file against the manifest, so an applied
    /// addon has to leave the manifest describing the NEW bytes — otherwise every
    /// addon file reports corrupt and Repair silently wipes the addon.
    /// </summary>
    [Fact]
    public async Task Apply_UpdatesManifestFingerprint_SoVerifyStaysClean()
    {
        var install = MakeInstall((@"art\ui\panel.ddt", "ORIGINAL"));
        var zip = MakeZip((@"art/ui/panel.ddt", "TRANSPARENT"));

        await AddonService.ApplyAsync(install, "ui", zip, Profile, false);

        var manifest = InstallManifest.TryLoad(install)!;
        var recorded = manifest.FileHashes["art/ui/panel.ddt"];
        var actual = VerifyService.ComputeFingerprintOf(Path.Combine(install, @"art\ui\panel.ddt"));
        Assert.Equal(actual.Sha256, recorded.Sha256);
    }

    // -- Refusals --------------------------------------------------------------

    /// <summary>
    /// The protected files break version detection and get the player thrown out
    /// of every lobby, so nothing may be written at all — not even the harmless
    /// files bundled alongside them.
    /// </summary>
    [Fact]
    public async Task Apply_RefusesProtectedFile_AndWritesNothing()
    {
        var install = MakeInstall((@"art\ui\panel.ddt", "ORIGINAL"));
        var zip = MakeZip(
            (@"art/ui/panel.ddt", "MODIFIED"),
            (@"data/protoy.xml", "TAMPERED"));

        var result = await AddonService.ApplyAsync(install, "bad", zip, Profile, false);

        Assert.Equal(AddonApplyStatus.Blocked, result.Status);
        Assert.Equal("ORIGINAL", Read(install, @"art\ui\panel.ddt"));
        Assert.False(File.Exists(Path.Combine(install, @"data\protoy.xml")));
    }

    /// <summary>A simulation-risk addon needs explicit consent, and only that.</summary>
    [Fact]
    public async Task Apply_SimulationRisk_NeedsConsent()
    {
        var install = MakeInstall((@"data\civs.xml", "ORIGINAL"));
        var zip = MakeZip((@"data/civs.xml", "TWEAKED"));

        var refused = await AddonService.ApplyAsync(install, "sim", zip, Profile, false);
        Assert.Equal(AddonApplyStatus.Blocked, refused.Status);
        Assert.Equal("ORIGINAL", Read(install, @"data\civs.xml"));

        var allowed = await AddonService.ApplyAsync(install, "sim", zip, Profile, true);
        Assert.Equal(AddonApplyStatus.Applied, allowed.Status);
        Assert.Equal("TWEAKED", Read(install, @"data\civs.xml"));
    }

    /// <summary>
    /// Two addons owning one file can't both revert — whoever disabled second
    /// would restore the first one's bytes as if they were the original. There is
    /// no merging binaries, so the second addon is refused.
    /// </summary>
    [Fact]
    public async Task Apply_RefusesConflictWithAnotherEnabledAddon()
    {
        var install = MakeInstall((@"art\ui\panel.ddt", "ORIGINAL"));
        await AddonService.ApplyAsync(install, "first", MakeZip((@"art/ui/panel.ddt", "FIRST")), Profile, false);

        var result = await AddonService.ApplyAsync(
            install, "second", MakeZip((@"art/ui/panel.ddt", "SECOND")), Profile, false);

        Assert.Equal(AddonApplyStatus.Conflict, result.Status);
        Assert.Equal("first", result.ConflictingAddonId);
        Assert.Equal("FIRST", Read(install, @"art\ui\panel.ddt"));
    }

    [Fact]
    public async Task Apply_EmptyArchive_IsReportedNotSilentlyAccepted()
    {
        var install = MakeInstall((@"art\a.ddt", "A"));
        var result = await AddonService.ApplyAsync(install, "empty", MakeZip(), Profile, false);

        Assert.Equal(AddonApplyStatus.Empty, result.Status);
    }

    /// <summary>An entry escaping the install root is never legitimate packaging.</summary>
    [Fact]
    public async Task Apply_RejectsZipSlipEntries()
    {
        var install = MakeInstall((@"art\a.ddt", "A"));
        var zip = MakeZip((@"../escaped.txt", "PWNED"), (@"art/b.ddt", "B"));

        await AddonService.ApplyAsync(install, "slip", zip, Profile, false);

        var escaped = Path.Combine(Path.GetDirectoryName(install)!, "escaped.txt");
        Assert.False(File.Exists(escaped));
        Assert.True(File.Exists(Path.Combine(install, @"art\b.ddt")));
    }

    // -- Re-apply after an update ---------------------------------------------

    /// <summary>
    /// The subtle one. After an update re-lays the overlay, the stale backup holds
    /// the PREVIOUS version's bytes. If re-apply kept it, disabling the addon later
    /// would restore the old file over the new one — a silent downgrade that verify
    /// would then bless, because the manifest is re-captured from whatever is on
    /// disk. Re-apply must take a FRESH backup of the newly-laid file.
    /// </summary>
    [Fact]
    public async Task Reapply_TakesFreshBackup_SoDisableCannotDowngrade()
    {
        var install = MakeInstall((@"art\ui\panel.ddt", "V1 ORIGINAL"));
        var zip = MakeZip((@"art/ui/panel.ddt", "ADDON"));

        await AddonService.ApplyAsync(install, "ui", zip, Profile, false);

        // Simulate an update re-laying the overlay with the new version's file.
        File.WriteAllText(Path.Combine(install, @"art\ui\panel.ddt"), "V2 ORIGINAL");

        await AddonService.ReapplyAllAsync(
            install, new[] { "ui" }, (_, _) => Task.FromResult<string?>(zip), Profile);
        Assert.Equal("ADDON", Read(install, @"art\ui\panel.ddt"));

        await AddonService.DisableAsync(install, "ui", Profile);
        Assert.Equal("V2 ORIGINAL", Read(install, @"art\ui\panel.ddt"));
    }

    /// <summary>A missing archive must not throw — an update can't fail over a cosmetic addon.</summary>
    [Fact]
    public async Task Reapply_MissingArchive_IsSurvivable()
    {
        var install = MakeInstall((@"art\a.ddt", "A"));

        await AddonService.ReapplyAllAsync(
            install, new[] { "gone" }, (_, _) => Task.FromResult<string?>(null), Profile);

        Assert.Equal("A", Read(install, @"art\a.ddt"));
    }
}
