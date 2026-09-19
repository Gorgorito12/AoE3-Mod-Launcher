using System;
using System.Linq;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Regression tests for <see cref="LauncherUpdateService.EvaluateUpdate"/> —
/// the pure self-update decision. The canonical bug: a binary obtained outside
/// the in-app self-updater (a manual download from GitHub Releases, or a build
/// run straight from <c>publish\</c>) has no saved <c>LastInstalledLauncherTag</c>,
/// so the launcher couldn't recognise its own version and offered an "update" to
/// the version it was already running, shown as "current: —". The fix falls back
/// to the binary's stamped AssemblyVersion as the effective current version.
/// These tests pin that behaviour without touching the network.
/// </summary>
public class LauncherUpdateServiceTests
{
    /// <summary>
    /// THE CANONICAL FOLDER, after months of self-updates: the single-file bundle the updater
    /// swapped in, beside everything a framework-dependent "Install on this PC" copied there
    /// once and nothing ever removed. The two .json are the developer-build signal
    /// LauncherUpdateGate reads and the .dll is what SelfInstallService.CopyPayload reads as
    /// "copy the whole folder" — those three go, and only those three: the third-party
    /// DLLs are nobody's signal, and the .pdb is what dotnet publish legitimately leaves beside
    /// a bundle.
    /// </summary>
    [Fact]
    public void ABundleShedsTheBuildFilesAFrameworkDependentInstallLeftBesideIt()
    {
        var beside = new[]
        {
            "Aoe3ModLauncher.exe", "Aoe3ModLauncher.dll", "Aoe3ModLauncher.pdb",
            "Aoe3ModLauncher.deps.json", "Aoe3ModLauncher.runtimeconfig.json",
            "Hardcodet.NotifyIcon.Wpf.dll", "SharpCompress.dll", "Aoe3ModLauncher.exe.old",
        };
        var stale = LauncherUpdateService.SelectStaleBuildFiles(
            @"C:\Users\x\AppData\Local\Programs\Aoe3ModLauncher\Aoe3ModLauncher.exe",
            178L * 1024 * 1024, beside);

        Assert.Equal(
            new[] { "Aoe3ModLauncher.deps.json", "Aoe3ModLauncher.dll", "Aoe3ModLauncher.runtimeconfig.json" },
            stale.OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// THE ONE THAT MATTERS: beside a stub those same files ARE the build, and this sweep runs
    /// at every startup — including every launch of bin\Release by the maintainer. A build
    /// output is never touched, whatever is in it.
    /// </summary>
    [Fact]
    public void ABuildOutputIsNeverTouched()
    {
        var beside = new[]
        {
            "Aoe3ModLauncher.exe", "Aoe3ModLauncher.dll",
            "Aoe3ModLauncher.deps.json", "Aoe3ModLauncher.runtimeconfig.json",
        };
        Assert.Empty(LauncherUpdateService.SelectStaleBuildFiles(
            @"C:\src\bin\Release\net8.0-windows\Aoe3ModLauncher.exe", 290 * 1024, beside));

        // And a bundle with nothing beside it — a player's ordinary folder — selects nothing.
        Assert.Empty(LauncherUpdateService.SelectStaleBuildFiles(
            @"C:\Users\x\Downloads\Aoe3ModLauncher.exe", 178L * 1024 * 1024,
            new[] { "Aoe3ModLauncher.exe", "Aoe3ModLauncher.pdb" }));
    }

    [Theory]
    // The exact bug: a freshly-downloaded v0.9.9 with no saved tag must NOT be
    // offered an "update" to v0.9.9 — its own AssemblyVersion is the fallback.
    [InlineData("",       "0.9.9.0", null,     "v0.9.9", false)]
    // A genuinely old binary (asm 0.6.0) with no saved tag still gets the real update.
    [InlineData("",       "0.6.0.0", null,     "v0.9.9", true)]
    // Saved tag present and equal to remote → no update (unchanged behaviour).
    [InlineData("v0.9.9", "0.9.9.0", null,     "v0.9.9", false)]
    // Saved tag older than remote → update (unchanged behaviour).
    [InlineData("v0.9.8", "0.9.9.0", null,     "v0.9.9", true)]
    // With no informational stamp to consult, the saved tag is what is left to go on and it
    // beats the numeric AssemblyVersion. It used to beat the STAMP too; see the theory below.
    [InlineData("v0.9.9", "0.6.0.0", null,     "v0.9.9", false)]
    // Dismissed tag suppresses the prompt even with no saved tag and an old asm.
    [InlineData("",       "0.6.0.0", "v0.9.9", "v0.9.9", false)]
    // Remote rolled back below the effective current → no backwards "update".
    [InlineData("",       "0.9.9.0", null,     "v0.9.8", false)]
    // A non-SemVer saved tag keeps prompt-on-difference (never silently miss one).
    [InlineData("weird",  "0.9.9.0", null,     "v1.0.0", true)]
    [InlineData("weird",  "0.9.9.0", null,     "weird",  false)]
    // --- WoL-style LETTER suffix support ---
    // Letter release is newer than the plain patch → offer.
    [InlineData("v1.0.5",  "1.0.5.0", null,    "v1.0.5a", true)]
    // Same letter tag installed → no offer (equality).
    [InlineData("v1.0.5a", "1.0.5.0", null,    "v1.0.5a", false)]
    // Next patch is newer than a letter release → offer.
    [InlineData("v1.0.5a", "1.0.5.0", null,    "v1.0.6",  true)]
    // Letter-vs-letter ordering: b > a installed → offer.
    [InlineData("v1.0.5a", "1.0.5.0", null,    "v1.0.5b", true)]
    // Downgrade guard now WORKS for letters: remote a < installed b → no offer.
    [InlineData("v1.0.5b", "1.0.5.0", null,    "v1.0.5a", false)]
    // Downgrade guard: plain patch is OLDER than the letter release → no offer.
    [InlineData("v1.0.5a", "1.0.5.0", null,    "v1.0.5",  false)]
    public void EvaluateUpdate_OfferDecision(
        string lastInstalledTag, string asmVersion, string? skippedTag,
        string remoteTag, bool expectedOffer)
    {
        var (offer, _) = LauncherUpdateService.EvaluateUpdate(
            lastInstalledTag, Version.Parse(asmVersion), skippedTag, remoteTag);

        Assert.Equal(expectedOffer, offer);
    }

    /// <summary>
    /// <b>The reported bug: a hand-downloaded older build was never offered the newer one.</b>
    ///
    /// <para>The config records what was last INSTALLED and lives in %LocalAppData%, away from
    /// the .exe — so running a v1.0.12e downloaded by hand reads the tag a previous v1.0.13
    /// wrote, concludes it is already newest, and goes quiet forever. Confirmed from a real
    /// machine, whose log printed <c>Current tag: 'v1.0.13', AssemblyVersion: 1.0.12.0</c>.</para>
    ///
    /// <para>The binary's own stamp wins now. The last row is the one that keeps the reason the
    /// saved tag was ever first: a build with no stamp still has to fall back to it.</para>
    /// </summary>
    [Theory]
    // The exact report: saved tag NEWER than the running binary → offer anyway.
    [InlineData("v1.0.13", "v1.0.12e", "v1.0.13", true)]
    // The same shape without the letter, so the fix does not hinge on suffix parsing.
    [InlineData("v1.0.13", "v1.0.12",  "v1.0.13", true)]
    // The binary IS the newest → still nothing to offer. The fix must not invent updates.
    [InlineData("v1.0.12", "v1.0.13",  "v1.0.13", false)]
    // Both agree, which is every normal self-updated install: unchanged.
    [InlineData("v1.0.13", "v1.0.13",  "v1.0.13", false)]
    // No stamp (a build published without -Version) → the saved tag is the fallback.
    [InlineData("v1.0.13", "",         "v1.0.13", false)]
    public void EvaluateUpdate_TrustsTheRunningBinaryOverTheSavedTag(
        string savedTag, string informationalTag, string remoteTag, bool expectedOffer)
    {
        var (offer, _) = LauncherUpdateService.EvaluateUpdate(
            savedTag, new Version(1, 0, 12), skippedTag: "", remoteTag: remoteTag,
            currentInformationalTag: informationalTag);

        Assert.Equal(expectedOffer, offer);
    }

    /// <summary>
    /// Whether the saved tag has to be thrown away — and with it the cached ETag, because a
    /// conditional request answering 304 skips the comparison entirely.
    ///
    /// <para>The refusals are the point: with nothing saved, or a binary that carries no stamp,
    /// there is no contradiction to act on and the config must be left alone.</para>
    /// </summary>
    [Theory]
    [InlineData("v1.0.13", "v1.0.12e", true)]   // the reported case
    [InlineData("v1.0.13", "v1.0.13",  false)]  // they agree
    [InlineData("v1.0.13", "V1.0.13",  false)]  // ...case-insensitively
    [InlineData("",        "v1.0.12e", false)]  // nothing saved yet — a first run
    [InlineData("v1.0.13", "",         false)]  // unstamped build: it cannot contradict anything
    public void SavedTagContradictsBinary_OnlyWhenBothAreKnownAndDiffer(
        string savedTag, string informationalTag, bool expected)
    {
        Assert.Equal(
            expected,
            LauncherUpdateService.SavedTagContradictsBinary(savedTag, informationalTag));
    }

    [Fact]
    public void EvaluateUpdate_EmptyTag_UsesAssemblyVersionAsCurrentLabel()
    {
        // No saved tag + running 0.9.9 + remote v0.9.9 → no offer, and the
        // "current" label is the real version, NOT the "—" placeholder.
        var (offer, currentLabel) = LauncherUpdateService.EvaluateUpdate(
            "", new Version(0, 9, 9, 0), null, "v0.9.9");

        Assert.False(offer);
        Assert.Equal("v0.9.9", currentLabel);
    }

    [Fact]
    public void EvaluateUpdate_OldBinary_OffersUpdateWithHonestCurrentLabel()
    {
        var (offer, currentLabel) = LauncherUpdateService.EvaluateUpdate(
            "", new Version(0, 6, 0, 0), null, "v0.9.9");

        Assert.True(offer);
        Assert.Equal("v0.6.0", currentLabel); // honest current version, not "—"
    }

    [Fact]
    public void EvaluateUpdate_LetterBinaryNoSavedTag_RecognisedViaInformationalTag()
    {
        // A manually-downloaded "1.0.5a" binary: AssemblyVersion is numeric 1.0.5
        // (the letter can't live there), but InformationalVersion carries "v1.0.5a".
        // With the informational tag as the effective-current fallback, the remote
        // "v1.0.5a" must NOT be offered as an update to itself.
        var (offer, currentLabel) = LauncherUpdateService.EvaluateUpdate(
            lastInstalledTag: "", assemblyVersion: new Version(1, 0, 5, 0),
            skippedTag: null, remoteTag: "v1.0.5a", currentInformationalTag: "v1.0.5a");

        Assert.False(offer);
        Assert.Equal("v1.0.5a", currentLabel);
    }

    [Fact]
    public void EvaluateUpdate_LetterBinaryNoSavedTag_WithoutInformational_WouldOfferItself()
    {
        // Without the informational fallback, the numeric AssemblyVersion (1.0.5)
        // can't represent the letter, so the same-version "v1.0.5a" reads as newer
        // and gets offered — this is exactly why the informational tag exists.
        var (offer, _) = LauncherUpdateService.EvaluateUpdate(
            lastInstalledTag: "", assemblyVersion: new Version(1, 0, 5, 0),
            skippedTag: null, remoteTag: "v1.0.5a");

        Assert.True(offer);
    }

    [Theory]
    [InlineData("0.9.9.0", "v0.9.9")]
    [InlineData("1.0.0.0", "v1.0.0")]
    [InlineData("0.0.0",   "v0.0.0")]
    public void FormatVersionTag_FormatsAssemblyVersionAsGitHubTag(string asmVersion, string expected)
    {
        Assert.Equal(expected, LauncherUpdateService.FormatVersionTag(Version.Parse(asmVersion)));
    }

    [Fact]
    public void FormatVersionTag_TwoPartVersion_FloorsBuildToZero()
    {
        // new Version(1, 0) has Build = -1; must not produce "v1.0.-1".
        Assert.Equal("v1.0.0", LauncherUpdateService.FormatVersionTag(new Version(1, 0)));
    }

    // ---------------------------------------------------------- the staging path

    /// <summary>
    /// THE REGRESSION. The staging path used to be a hardcoded
    /// <c>WarsOfLibertyLauncher_new.exe</c> beside the running exe, so a user who ended up
    /// RUNNING that file — which a refused swap invites, by leaving a runnable launcher on
    /// their desktop — had the launcher compute its own image as the download destination.
    /// A running image can be renamed but not deleted, so every update died on the finalize
    /// delete, for ever. Deriving the name from the process path makes that collision
    /// structurally impossible: there is no <c>s</c> for which <c>s == s + ".new"</c>.
    /// </summary>
    [Theory]
    [InlineData(@"C:\Users\Despacho\Desktop\WarsOfLibertyLauncher_new.exe")]
    [InlineData(@"C:\Users\p\AppData\Local\Programs\Aoe3ModLauncher\Aoe3ModLauncher.exe")]
    [InlineData(@"D:\portable\WoL.exe")]
    public void ThePendingUpdatePathIsNeverTheRunningExecutable(string currentExe)
    {
        var pending = LauncherUpdateService.GetPendingUpdatePath(currentExe);

        Assert.NotEqual(currentExe, pending, StringComparer.OrdinalIgnoreCase);

        // Same directory: the free-space check measures that volume, and the finalize step
        // relies on a same-volume rename rather than a ~178 MB copy.
        Assert.Equal(
            System.IO.Path.GetDirectoryName(currentExe),
            System.IO.Path.GetDirectoryName(pending));
    }

    /// <summary>
    /// The staged file must not be runnable. This is the half that actually disarms the trap
    /// rather than moving it one filename over: a swap can still legitimately refuse and leave
    /// this file behind, and what made the old name dangerous was that double-clicking it was
    /// the obvious thing for a stranded user to do. The <c>.part</c> inherits the property.
    /// </summary>
    [Fact]
    public void ThePendingUpdateIsNotSomethingTheUserCanDoubleClick()
    {
        var pending = LauncherUpdateService.GetPendingUpdatePath(
            @"C:\Users\p\Desktop\Aoe3ModLauncher.exe");

        Assert.False(pending.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        Assert.False((pending + ".part").EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
    }

    // ---------------------------------------------------------- the stale sweep

    /// <summary>
    /// The legacy staging file and its partial are swept, so an affected machine stops
    /// carrying a 178 MB runnable decoy beside its launcher. Nothing will ever resume that
    /// <c>.part</c> — the destination name it belongs to no longer exists.
    /// </summary>
    [Fact]
    public void TheLegacyStagingFileAndItsPartialAreSwept()
    {
        var swept = LauncherUpdateService.SelectStaleSelfUpdateFiles(
            @"C:\Users\p\Desktop\Aoe3ModLauncher.exe");

        Assert.Contains(@"C:\Users\p\Desktop\WarsOfLibertyLauncher_new.exe", swept);
        Assert.Contains(@"C:\Users\p\Desktop\WarsOfLibertyLauncher_new.exe.part", swept);
        Assert.Contains(@"C:\Users\p\Desktop\Aoe3ModLauncher.exe.old", swept);
        Assert.Contains(@"C:\Users\p\Desktop\Aoe3ModLauncher.exe.new", swept);
    }

    /// <summary>
    /// THE ONE THAT MATTERS. On an affected machine the running executable IS the legacy
    /// staging name, so an unconditional sweep would target the user's own live image.
    /// Windows refuses that and the caller swallows the error, which means today it is safe
    /// only by accident — and "delete the user's only launcher" is the single outcome here
    /// that cannot be undone.
    /// </summary>
    [Fact]
    public void TheFileYouAreRunningIsNeverSwept()
    {
        const string running = @"C:\Users\Despacho\Desktop\WarsOfLibertyLauncher_new.exe";

        var swept = LauncherUpdateService.SelectStaleSelfUpdateFiles(running);

        Assert.DoesNotContain(running, swept, StringComparer.OrdinalIgnoreCase);

        // Its partial is still fair game — that one is not the running image.
        Assert.Contains(running + ".part", swept);
    }

    /// <summary>
    /// The CURRENT partial is never swept. A cancelled unattended update deliberately leaves
    /// it so the next launch resumes from it over HTTP Range; sweeping it here would delete
    /// that feature silently and restart every skipped update from byte 0.
    /// </summary>
    [Fact]
    public void AnInProgressPartialIsNeverSwept()
    {
        const string running = @"C:\Users\p\Desktop\Aoe3ModLauncher.exe";

        var swept = LauncherUpdateService.SelectStaleSelfUpdateFiles(running);

        Assert.DoesNotContain(
            LauncherUpdateService.GetPendingUpdatePath(running) + ".part",
            swept,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>A neighbour's unrelated executable is not ours to delete.</summary>
    [Fact]
    public void AnUnrelatedNeighbourIsNeverSwept()
    {
        var swept = LauncherUpdateService.SelectStaleSelfUpdateFiles(
            @"C:\Users\p\Desktop\Aoe3ModLauncher.exe");

        Assert.DoesNotContain(@"C:\Users\p\Desktop\SomethingElse.exe", swept);
    }
}
