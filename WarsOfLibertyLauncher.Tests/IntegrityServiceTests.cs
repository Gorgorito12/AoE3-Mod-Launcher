using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins the ONE verdict Verify and Repair share (<see cref="IntegrityService"/>). The cases that
/// matter are the ones the old split got wrong: a locked file read as corrupt (a multi-GB re-lay
/// that then failed on that file), engine damage that Verify reported and Repair called "nothing
/// to repair", and WoL's folder checks applied to every WolPatcher mod.
/// </summary>
public class IntegrityServiceTests : IDisposable
{
    private readonly List<string> _dirs = new();

    public void Dispose()
    {
        foreach (var d in _dirs)
            try { Directory.Delete(d, recursive: true); } catch { }
    }

    private static ModProfile Profile(ModInstallType type = ModInstallType.IsolatedFolder,
        ModUpdateMechanism mechanism = ModUpdateMechanism.GitHubReleases, params string[] covered) => new()
    {
        Id = "mod",
        DisplayName = "Mod",
        InstallType = type,
        UpdateMechanism = mechanism,
        Translations = covered.Length == 0 ? null : new TranslationsSettings { CoveredFiles = covered.ToList() },
    };

    /// <summary>An install with the three key files, overlay files hashed, and an engine DLL.</summary>
    private string MakeInstall(ModProfile profile, params (string Rel, string Content)[] overlay)
    {
        var root = Directory.CreateTempSubdirectory("integrity-").FullName;
        _dirs.Add(root);
        void Put(string rel, string text)
        {
            var full = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text);
        }
        Put("data/protoy.xml", "proto");
        Put("data/techtreey.xml", "tech");
        Put("data/stringtabley.xml", "strings");
        Put("RockallDLL.dll", "engine bytes");
        foreach (var (rel, content) in overlay) Put(rel, content);

        var hashes = new Dictionary<string, FileFingerprint>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rel, _) in overlay)
            hashes[rel] = VerifyService.ComputeFingerprintOf(Path.Combine(root, rel));
        new InstallManifest
        {
            ModId = profile.Id,
            InstallPath = root,
            OverlayFiles = overlay.Select(o => o.Rel).ToList(),
            FileHashes = hashes,
            EngineFileHashes = new Dictionary<string, FileFingerprint>(StringComparer.OrdinalIgnoreCase)
            {
                ["RockallDLL.dll"] = VerifyService.ComputeFingerprintOf(Path.Combine(root, "RockallDLL.dll")),
            },
        }.Save();
        return root;
    }

    [Fact]
    public void AHealthyInstallIsHealthy()
    {
        var p = Profile();
        var root = MakeInstall(p, ("art/a.ddt", "A"), ("art/b.ddt", "B"));
        var report = IntegrityService.Diagnose(root, p, new DiagnoseOptions());
        Assert.True(report.IsHealthy, string.Join("; ", report.Findings.Select(IntegrityService.FormatLine)));
        Assert.Equal(RepairRoute.Nothing, IntegrityService.Route(report));
    }

    [Fact]
    public void ALockedFileIsInUse_NotDamaged_AndIsNeverRelaid()
    {
        var p = Profile();
        var root = MakeInstall(p, ("art/a.ddt", "AAAA"));
        using (new FileStream(Path.Combine(root, "art", "a.ddt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var report = IntegrityService.Diagnose(root, p, new DiagnoseOptions());
            Assert.Contains(report.Findings, f => f.Kind == IntegrityKind.Unreadable && f.Path == "art/a.ddt");
            Assert.DoesNotContain(report.Findings, f => f.Kind == IntegrityKind.Damaged);
            Assert.Equal(RepairRoute.InUse, IntegrityService.Route(report));
        }
    }

    [Fact]
    public void EngineDamageAloneRoutesToEngineOnly_NotToNothing()
    {
        var p = Profile();
        var root = MakeInstall(p, ("art/a.ddt", "A"));
        File.WriteAllText(Path.Combine(root, "RockallDLL.dll"), "engine BYTES");   // same size, other bytes
        var report = IntegrityService.Diagnose(root, p, new DiagnoseOptions());
        Assert.Contains(report.Findings, f => f.Kind == IntegrityKind.Engine && f.Path == "RockallDLL.dll");
        Assert.Equal(RepairRoute.EngineOnly, IntegrityService.Route(report));
    }

    [Fact]
    public void OverlayDamageWinsOverEngineDamage()
    {
        var p = Profile();
        var root = MakeInstall(p, ("art/a.ddt", "A"));
        File.Delete(Path.Combine(root, "art", "a.ddt"));
        File.WriteAllText(Path.Combine(root, "RockallDLL.dll"), "x");
        Assert.Equal(RepairRoute.Relay, IntegrityService.Route(IntegrityService.Diagnose(root, p, new DiagnoseOptions())));
    }

    [Fact]
    public void ACommunityWolPatcherModGetsNoneOfWolsFolderChecks()
    {
        var p = Profile(mechanism: ModUpdateMechanism.WolPatcher);
        var root = MakeInstall(p, ("art/a.ddt", "A"));
        var report = IntegrityService.Diagnose(root, p, new DiagnoseOptions());
        Assert.True(report.IsHealthy, string.Join("; ", report.Findings.Select(IntegrityService.FormatLine)));
    }

    [Fact]
    public void DeclaredStructuralChecksAreApplied()
    {
        var p = Profile();
        p.StructuralChecks.Add(new StructuralCheck("art/zulushield", Recursive: true, MinFiles: 1));
        p.StructuralChecks.Add(new StructuralCheck("data", "*.bar", MinFiles: 1, MinBytesEach: 1024));
        var root = MakeInstall(p, ("art/a.ddt", "A"));
        File.WriteAllText(Path.Combine(root, "data", "tiny.bar"), "x");

        var report = IntegrityService.Diagnose(root, p, new DiagnoseOptions());
        Assert.Contains(report.Findings, f => f.Kind == IntegrityKind.Structural && f.Path == "art/zulushield/");
        Assert.Contains(report.Findings, f => f.Kind == IntegrityKind.Structural && f.Path == "data/tiny.bar");
        Assert.Equal(RepairRoute.Relay, IntegrityService.Route(report));
    }

    [Fact]
    public void TheWolBuiltInCarriesItsFolderChecks()
    {
        var wol = ModRegistry.Find("wol");
        Assert.NotNull(wol);
        Assert.Contains(wol!.StructuralChecks, c => c.Folder == "art/zulushield");
        Assert.Contains(wol.StructuralChecks, c => c.Folder == "data" && c.Pattern == "*.bar");
    }

    [Fact]
    public void ACorruptLiveStringTableIsCaughtWhenNoTranslationIsApplied()
    {
        var p = Profile(covered: "data/stringtabley.xml");
        var root = MakeInstall(p, ("data/stringtabley.xml", "canonical"));
        // The snapshot still holds the canonical bytes; only the LIVE file went bad.
        var originals = VerifyService.OriginalsFolderOf(root);
        Directory.CreateDirectory(originals);
        File.WriteAllText(Path.Combine(originals, "stringtabley.xml"), "canonical");
        File.WriteAllText(Path.Combine(root, "data", "stringtabley.xml"), "CANONICAL");

        var none = IntegrityService.Diagnose(root, p, new DiagnoseOptions(TranslationActive: false));
        Assert.Contains(none.Findings, f => f.Kind == IntegrityKind.Damaged && f.Path == "data/stringtabley.xml");

        // With a translation applied the live file is SUPPOSED to differ: compare the snapshot.
        var translated = IntegrityService.Diagnose(root, p, new DiagnoseOptions(TranslationActive: true));
        Assert.DoesNotContain(translated.Findings, f => f.Path == "data/stringtabley.xml");
    }

    [Fact]
    public void TheRecheckSizesEverythingButHashesOnlyWhatItWasAskedTo()
    {
        var p = Profile();
        var root = MakeInstall(p, ("art/a.ddt", "AAAA"), ("art/b.ddt", "BBBB"), ("art/c.ddt", "CCCC"));
        File.WriteAllText(Path.Combine(root, "art", "a.ddt"), "aaaa");   // same size, damaged
        File.WriteAllText(Path.Combine(root, "art", "b.ddt"), "bbbb");   // same size, damaged
        File.WriteAllText(Path.Combine(root, "art", "c.ddt"), "CC");     // size changed

        var report = IntegrityService.Diagnose(root, p, new DiagnoseOptions(HashOnly: new[] { "art/a.ddt" }));
        var damaged = report.PathsOf(IntegrityKind.Damaged);
        Assert.Contains("art/a.ddt", damaged);      // hashed
        Assert.DoesNotContain("art/b.ddt", damaged); // not asked to hash, size right
        Assert.Contains("art/c.ddt", damaged);      // size is checked for every file
    }

    [Fact]
    public void AnotherModsManifestGivesNoHashVerdict()
    {
        var p = Profile();
        var root = MakeInstall(p, ("art/a.ddt", "A"));
        var m = InstallManifest.TryLoad(root)!;
        m.ModId = "someone-else";
        m.Save();
        File.Delete(Path.Combine(root, "art", "a.ddt"));

        var report = IntegrityService.Diagnose(root, p, new DiagnoseOptions());
        Assert.False(report.HashesAvailable);
        Assert.DoesNotContain(report.Findings, f => f.Kind == IntegrityKind.Missing);
    }

    [Fact]
    public void DiagnoseWritesNothing()
    {
        var p = Profile(covered: "data/stringtabley.xml");
        var root = MakeInstall(p, ("art/a.ddt", "A"), ("data/stringtabley.xml", "canonical"));
        File.WriteAllText(Path.Combine(root, "art", "a.ddt"), "B");
        var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(f => f, f => File.GetLastWriteTimeUtc(f));

        IntegrityService.Diagnose(root, p, new DiagnoseOptions());

        var after = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
        Assert.Equal(before.Keys.OrderBy(x => x), after.OrderBy(x => x));
        foreach (var f in after) Assert.Equal(before[f], File.GetLastWriteTimeUtc(f));
    }

    [Fact]
    public void AnAntivirusReadFailureIsBlocked_ALockIsUnreadable_AnythingElseIsDamage()
    {
        Assert.Equal(VerifyService.FileProblem.Blocked,
            VerifyService.ClassifyReadFailure(new IOException("virus", unchecked((int)0x800700E1))));
        Assert.Equal(VerifyService.FileProblem.Unreadable,
            VerifyService.ClassifyReadFailure(new IOException("sharing", unchecked((int)0x80070020))));
        Assert.Equal(VerifyService.FileProblem.HashMismatch,
            VerifyService.ClassifyReadFailure(new IOException("crc", unchecked((int)0x80070017))));
    }

    [Fact]
    public void BlockedOutranksEverything()
    {
        var report = new IntegrityReport(new[]
        {
            new IntegrityFinding(IntegrityKind.Damaged, "a"),
            new IntegrityFinding(IntegrityKind.Unreadable, "b"),
            new IntegrityFinding(IntegrityKind.Blocked, "c"),
        }, true, 3);
        Assert.Equal(RepairRoute.Antivirus, IntegrityService.Route(report));
    }
}
