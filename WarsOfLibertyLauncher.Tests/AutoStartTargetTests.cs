using System;
using System.IO;
using System.Linq;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins the auto-start / self-install decisions that produced the confirmed
/// "start with Windows launched nothing" bug — in TWO independent ways:
///
/// (1) <see cref="SelfInstallService.SelectAutoStartExe"/> — WHICH exe the Run key
///     points at: the stable installed copy only when it's RUNNABLE, else the running
///     exe. Registering the RUNNING (portable/dev) exe let a deleted <c>publish\</c>
///     path survive in the Run key until login launched nothing.
/// (2) <see cref="SelfInstallService.CanonicalRunnable"/> + <see cref="SelfInstallService.CopyPayload"/>
///     — the canonical copy must actually be runnable. "Install on this PC" from a
///     framework-dependent dev build copied ONLY the ~0.29 MB apphost stub (no DLLs),
///     so auto-start faithfully launched a broken exe.
/// </summary>
public class AutoStartTargetTests
{
    // ---- SelectAutoStartExe ----------------------------------------------------

    [Fact]
    public void CanonicalRunnable_PrefersCanonical()
    {
        var chosen = SelfInstallService.SelectAutoStartExe(
            canonicalRunnable: true,
            canonicalExe: @"C:\Users\x\AppData\Local\Programs\Aoe3ModLauncher\Aoe3ModLauncher.exe",
            runningExe: @"C:\Users\x\Downloads\Aoe3ModLauncher.exe");

        Assert.Equal(
            @"C:\Users\x\AppData\Local\Programs\Aoe3ModLauncher\Aoe3ModLauncher.exe", chosen);
    }

    /// <summary>No canonical copy at all → the running exe (today's fragile fallback).</summary>
    [Fact]
    public void NoCanonicalCopy_FallsBackToRunningExe()
    {
        var chosen = SelfInstallService.SelectAutoStartExe(
            canonicalRunnable: false,
            canonicalExe: @"C:\Users\x\AppData\Local\Programs\Aoe3ModLauncher\Aoe3ModLauncher.exe",
            runningExe: @"C:\Users\x\Downloads\Aoe3ModLauncher.exe");

        Assert.Equal(@"C:\Users\x\Downloads\Aoe3ModLauncher.exe", chosen);
    }

    /// <summary>The bug that made round 1 look fixed but still not work: a canonical
    /// copy that EXISTS but is a broken apphost (not runnable) must NOT be registered —
    /// fall back to the running exe.</summary>
    [Fact]
    public void CanonicalPresentButBroken_FallsBackToRunningExe()
    {
        // canonicalRunnable=false models "exists but incomplete".
        var chosen = SelfInstallService.SelectAutoStartExe(
            canonicalRunnable: false,
            canonicalExe: @"C:\canon\Aoe3ModLauncher.exe",
            runningExe: @"C:\dev\bin\Debug\Aoe3ModLauncher.exe");

        Assert.Equal(@"C:\dev\bin\Debug\Aoe3ModLauncher.exe", chosen);
    }

    [Fact]
    public void BlankCanonical_FallsBackToRunningExe()
    {
        Assert.Equal("run.exe", SelfInstallService.SelectAutoStartExe(true, "", "run.exe"));
        Assert.Equal("run.exe", SelfInstallService.SelectAutoStartExe(true, null, "run.exe"));
    }

    // ---- CanonicalRunnable -----------------------------------------------------

    [Fact]
    public void CanonicalRunnable_FrameworkDependentComplete_IsRunnable()
    {
        // Small apphost, but its sibling DLL is present → complete FD install.
        Assert.True(SelfInstallService.CanonicalRunnable(
            exeExists: true, siblingDllExists: true, exeLength: 295384));
    }

    [Fact]
    public void CanonicalRunnable_SelfContainedSingleFile_IsRunnable()
    {
        // No sibling DLL, but the exe is huge → self-contained single-file.
        Assert.True(SelfInstallService.CanonicalRunnable(
            exeExists: true, siblingDllExists: false, exeLength: 165L * 1024 * 1024));
    }

    [Fact]
    public void CanonicalRunnable_BrokenApphostStub_IsNotRunnable()
    {
        // The exact broken case: ~0.29 MB apphost, no DLL beside it → NOT runnable.
        Assert.False(SelfInstallService.CanonicalRunnable(
            exeExists: true, siblingDllExists: false, exeLength: 295384));
    }

    [Fact]
    public void CanonicalRunnable_Missing_IsNotRunnable()
    {
        Assert.False(SelfInstallService.CanonicalRunnable(
            exeExists: false, siblingDllExists: true, exeLength: 165L * 1024 * 1024));
    }

    // ---- CopyPayload -----------------------------------------------------------

    [Fact]
    public void CopyPayload_FrameworkDependent_CopiesWholeFolder()
    {
        var root = NewTempDir();
        try
        {
            var src = Path.Combine(root, "src");
            var dst = Path.Combine(root, "dst");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "Aoe3ModLauncher.exe"), "stub");
            File.WriteAllText(Path.Combine(src, "Aoe3ModLauncher.dll"), "managed");   // FD marker
            File.WriteAllText(Path.Combine(src, "Aoe3ModLauncher.runtimeconfig.json"), "{}");
            File.WriteAllText(Path.Combine(src, "SharpCompress.dll"), "dep");
            Directory.CreateDirectory(Path.Combine(src, "runtimes"));
            File.WriteAllText(Path.Combine(src, "runtimes", "native.dll"), "n");       // nested

            var (ok, _) = SelfInstallService.CopyPayload(
                Path.Combine(src, "Aoe3ModLauncher.exe"), dst);

            Assert.True(ok);
            Assert.True(File.Exists(Path.Combine(dst, "Aoe3ModLauncher.exe")));
            Assert.True(File.Exists(Path.Combine(dst, "Aoe3ModLauncher.dll")));         // the fix
            Assert.True(File.Exists(Path.Combine(dst, "Aoe3ModLauncher.runtimeconfig.json")));
            Assert.True(File.Exists(Path.Combine(dst, "SharpCompress.dll")));
            Assert.True(File.Exists(Path.Combine(dst, "runtimes", "native.dll")));      // recursive
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void CopyPayload_SelfContainedSingleFile_CopiesOnlyExe()
    {
        var root = NewTempDir();
        try
        {
            var src = Path.Combine(root, "src");
            var dst = Path.Combine(root, "dst");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "Aoe3ModLauncher.exe"), "single-file");  // no sibling DLL

            var (ok, _) = SelfInstallService.CopyPayload(
                Path.Combine(src, "Aoe3ModLauncher.exe"), dst);

            Assert.True(ok);
            Assert.True(File.Exists(Path.Combine(dst, "Aoe3ModLauncher.exe")));
            Assert.Single(Directory.EnumerateFileSystemEntries(dst));  // exe only
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // ---- BuildDeferredDeleteScript ---------------------------------------------

    /// <summary>The script must wait for THIS process to exit (the exe lives inside
    /// the folder it deletes), then remove the install folder and self-delete.</summary>
    [Fact]
    public void DeferredDeleteScript_WaitsThenRemovesInstallFolder()
    {
        var script = SelfInstallService.BuildDeferredDeleteScript(
            pid: 4321, canonicalDir: @"C:\Users\x\AppData\Local\Programs\Aoe3ModLauncher", dataDir: null);

        Assert.Contains("PID eq 4321", script);                                  // waits on the pid
        Assert.Contains(@"rmdir /s /q ""C:\Users\x\AppData\Local\Programs\Aoe3ModLauncher""", script);
        Assert.Contains("del /f /q \"%~f0\"", script);                           // self-deletes
    }

    /// <summary>With no dataDir (the "keep my settings" choice) the data folder must
    /// NOT be removed — and its path must not even appear in the script.</summary>
    [Fact]
    public void DeferredDeleteScript_NullDataDir_KeepsUserData()
    {
        var script = SelfInstallService.BuildDeferredDeleteScript(
            pid: 1, canonicalDir: @"C:\canon", dataDir: null);

        Assert.Contains(@"rmdir /s /q ""C:\canon""", script);
        Assert.DoesNotContain("AoE3ModLauncher", script.Replace(@"C:\canon", ""));  // no data path
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(script, @"rmdir /s /q"));
    }

    /// <summary>With a dataDir (the "delete everything" choice) BOTH folders are
    /// removed — two rmdir lines, one per folder.</summary>
    [Fact]
    public void DeferredDeleteScript_WithDataDir_RemovesBoth()
    {
        var script = SelfInstallService.BuildDeferredDeleteScript(
            pid: 1, canonicalDir: @"C:\canon", dataDir: @"C:\data\AoE3ModLauncher");

        Assert.Contains(@"rmdir /s /q ""C:\canon""", script);
        Assert.Contains(@"rmdir /s /q ""C:\data\AoE3ModLauncher""", script);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(script, @"rmdir /s /q").Count);
    }

    private static string NewTempDir()
    {
        // Avoid Path.GetRandomFileName (needs no RNG ban here, but keep it simple/stable):
        var dir = Path.Combine(Path.GetTempPath(), "aoe3selfinstall-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
