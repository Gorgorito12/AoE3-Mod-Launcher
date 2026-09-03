using System;
using System.Collections.Generic;
using System.IO;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Regression tests for <see cref="ModInstallProbe"/> — the content-based mod
/// install detection that replaced the old "install folder must be named after
/// the mod" heuristic. The canonical bug: WoL was only detected when its folder
/// was literally named "Wars of Liberty"; renaming it (or installing under any
/// other name) made the launcher report it as not installed. Detection now goes
/// by content (probe file + optional marker), so the folder name is irrelevant,
/// while the marker still tells a real WoL folder apart from vanilla AoE3 (whose
/// data\ files satisfy the probe too).
/// </summary>
public class ModInstallProbeTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private string NewTempDir()
    {
        var dir = Directory.CreateTempSubdirectory("wol-probe-test-").FullName;
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

    private static ModProfile WolLikeProfile() => new()
    {
        Id = "wol",
        DisplayName = "Wars of Liberty",
        InstallType = ModInstallType.IsolatedFolder,
        InstallProbeFile = @"data\stringtabley.xml",
        InstallMarker = @"art\zulushield",
    };

    private static void CreateFileAt(string root, string relative)
    {
        var full = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "x");
    }

    private static void CreateDirAt(string root, string relative)
        => Directory.CreateDirectory(Path.Combine(root, relative));

    /// <summary>
    /// An IsolatedFolder install is a full AoE3 clone (bin\ flattened to root),
    /// so it always has the engine DLLs at the root. Detection now requires one,
    /// so a fixture that should read as a real install must lay it.
    /// </summary>
    private static void CreateEngineAt(string root)
        => CreateFileAt(root, "RockallDLL.dll");

    [Fact]
    public void IsolatedMod_WithProbeMarkerAndEngine_IsDetected_RegardlessOfFolderName()
    {
        // A WoL install in a folder named "MiWoL" — nothing like the DisplayName.
        var install = Path.Combine(NewTempDir(), "MiWoL");
        CreateFileAt(install, @"data\stringtabley.xml"); // probe
        CreateDirAt(install, @"art\zulushield");          // marker (a directory)
        CreateEngineAt(install);                          // cloned base engine

        Assert.True(ModInstallProbe.LooksLikeModInstall(install, WolLikeProfile()));
    }

    /// <summary>
    /// The Napoleonic Era bug: a folder with the mod's probe (and marker) but
    /// NO base game underneath is only the mod's overlay — a leftover manual
    /// download, not an install. Adopting it made the launcher offer a bogus
    /// "update" for a mod it never installed.
    /// </summary>
    [Fact]
    public void IsolatedMod_WithProbeButNoEngine_IsRejected()
    {
        var install = Path.Combine(NewTempDir(), "Napoleonic era");
        CreateFileAt(install, @"data\stringtabley.xml"); // probe
        CreateDirAt(install, @"art\zulushield");          // marker
        // no engine DLL — only the overlay

        Assert.False(ModInstallProbe.LooksLikeModInstall(install, WolLikeProfile()));
        Assert.Equal(ProbeOutcome.EngineMissing,
            ModInstallProbe.Inspect(install, WolLikeProfile()));
    }

    /// <summary>Any one of the engine DLLs is enough — a mod may not ship all four.</summary>
    [Theory]
    [InlineData("RockallDLL.dll")]
    [InlineData("binkw32.dll")]
    [InlineData("granny2.dll")]
    [InlineData("deformerdlly.dll")]
    public void IsolatedMod_AnySingleEngineFile_Satisfies(string engineDll)
    {
        var install = NewTempDir();
        CreateFileAt(install, @"data\stringtabley.xml");
        CreateDirAt(install, @"art\zulushield");
        CreateFileAt(install, engineDll);

        Assert.Equal(ProbeOutcome.Match, ModInstallProbe.Inspect(install, WolLikeProfile()));
    }

    [Fact]
    public void IsolatedMod_WithProbeButNoMarker_IsRejected_EvenIfNamedLikeTheMod()
    {
        // Mimics vanilla AoE3: it carries the probe file but NOT the WoL-only
        // marker. Even with the folder named exactly "Wars of Liberty" it must
        // be rejected — the marker, not the name, is the signal.
        var install = Path.Combine(NewTempDir(), "Wars of Liberty");
        CreateFileAt(install, @"data\stringtabley.xml"); // probe present
        // no art\zulushield marker

        Assert.False(ModInstallProbe.LooksLikeModInstall(install, WolLikeProfile()));
    }

    private static ModProfile OverlayProfile() => new()
    {
        Id = "napoleonic-era",
        DisplayName = "Napoleonic Era",
        InstallType = ModInstallType.InPlaceOverlay,
        InstallProbeFile = "age3n.exe",
        InstallMarker = "",
    };

    [Fact]
    public void OverlayMod_WithProbeButNoEngineAnywhere_IsRejected()
    {
        // The reported bug. An InPlaceOverlay install path IS the base game's folder, so
        // a folder holding only the mod's own executable is a leftover download, not an
        // install. This case used to be ACCEPTED (overlays were exempt from the engine
        // check), which is how a Napoleonic Era folder with age3n.exe and no engine read
        // as installed: the launcher showed PLAY and the game died two seconds later,
        // every time, with nothing explaining it.
        var install = Path.Combine(NewTempDir(), "Napoleonic Era");
        CreateFileAt(install, "age3n.exe");

        Assert.False(ModInstallProbe.LooksLikeModInstall(install, OverlayProfile()));
    }

    [Fact]
    public void OverlayMod_WithEngineBesideIt_IsDetected()
    {
        // Steam layout: the overlay lands in …\Age Of Empires 3\bin, which holds the
        // engine, data\ and everything else the game reads.
        var install = Path.Combine(NewTempDir(), "bin");
        CreateFileAt(install, "age3n.exe");
        CreateFileAt(install, "RockallDLL.dll");

        Assert.True(ModInstallProbe.LooksLikeModInstall(install, OverlayProfile()));
    }

    [Fact]
    public void OverlayMod_WithEngineInBinSubfolder_IsDetected()
    {
        // Other layouts keep data\ at the game root with the executables under bin\, and
        // an overlay installed to that root is equally valid — so the engine is accepted
        // one level down too. Without this the check would depend on guessing the layout.
        var install = Path.Combine(NewTempDir(), "Age of Empires III");
        CreateFileAt(install, "age3n.exe");
        CreateFileAt(install, @"bin\binkw32.dll");

        Assert.True(ModInstallProbe.LooksLikeModInstall(install, OverlayProfile()));
    }

    [Fact]
    public void MissingProbe_IsRejected()
    {
        var install = NewTempDir(); // empty folder, no probe file
        Assert.False(ModInstallProbe.LooksLikeModInstall(install, WolLikeProfile()));
    }

    [Fact]
    public void NonExistentFolder_IsRejected()
    {
        var install = Path.Combine(NewTempDir(), "does-not-exist");
        Assert.False(ModInstallProbe.LooksLikeModInstall(install, WolLikeProfile()));
    }

    [Fact]
    public void Inspect_ReportsTheExactMissingSignal()
    {
        var profile = WolLikeProfile();

        // Probe + marker + engine → Match.
        var full = Path.Combine(NewTempDir(), "MiWoL");
        CreateFileAt(full, @"data\stringtabley.xml");
        CreateDirAt(full, @"art\zulushield");
        CreateEngineAt(full);
        Assert.Equal(ProbeOutcome.Match, ModInstallProbe.Inspect(full, profile));

        // Probe present, marker gone → MarkerMissing (looks like base AoE3 /
        // an install whose overlay was uninstalled). This is the real-world
        // rejection that used to surface as a blind "invalid folder".
        var markerless = Path.Combine(NewTempDir(), "Wars of Liberty");
        CreateFileAt(markerless, @"data\stringtabley.xml");
        Assert.Equal(ProbeOutcome.MarkerMissing, ModInstallProbe.Inspect(markerless, profile));

        // No probe at all → ProbeMissing.
        var empty = NewTempDir();
        Assert.Equal(ProbeOutcome.ProbeMissing, ModInstallProbe.Inspect(empty, profile));

        // Path doesn't exist → NotADirectory.
        var missing = Path.Combine(NewTempDir(), "does-not-exist");
        Assert.Equal(ProbeOutcome.NotADirectory, ModInstallProbe.Inspect(missing, profile));
    }

    [Fact]
    public void Inspect_OutcomeOrdering_MarkerMissingIsMoreInstallLikeThanProbeMissing()
    {
        // The manual picker keeps the "closest to a real install" reason across
        // candidates by comparing outcomes; this ordering is load-bearing for that.
        Assert.True(ProbeOutcome.Match > ProbeOutcome.EngineMissing);
        Assert.True(ProbeOutcome.EngineMissing > ProbeOutcome.MarkerMissing);
        Assert.True(ProbeOutcome.MarkerMissing > ProbeOutcome.ProbeMissing);
        Assert.True(ProbeOutcome.ProbeMissing > ProbeOutcome.NotADirectory);
    }

    [Fact]
    public void MarkerExists_MatchesBothFileAndDirectory()
    {
        var dirMarker = NewTempDir();
        CreateDirAt(dirMarker, @"art\zulushield");
        Assert.True(ModInstallProbe.MarkerExists(dirMarker, @"art\zulushield"));

        var fileMarker = NewTempDir();
        CreateFileAt(fileMarker, @"some\marker.flag");
        Assert.True(ModInstallProbe.MarkerExists(fileMarker, @"some\marker.flag"));

        Assert.False(ModInstallProbe.MarkerExists(dirMarker, @"art\missing"));
        Assert.False(ModInstallProbe.MarkerExists(dirMarker, "")); // empty marker → false
    }

    // ---- Profiles that declare no probe file -----------------------------------
    //
    // These pin the branch that made UpdateService keep a second, older opinion
    // alongside this one. Its gate used to be
    // `IsProfileInstalled(p) && LooksLikeRealModInstall(p)`, where the first half
    // fell back to the WoL registry marker when a profile declared no probe file.
    // Collapsing that pair into a single IsRealInstall is only safe because Inspect
    // SKIPS the probe step for such a profile rather than failing it — otherwise the
    // legacy no-probe installs would all have stopped being recognised.

    private static ModProfile NoProbeFileProfile() => new()
    {
        Id = "legacy",
        DisplayName = "Legacy Mod",
        InstallType = ModInstallType.IsolatedFolder,
        InstallProbeFile = "",              // none declared
        InstallMarker = @"art\zulushield",
    };

    [Fact]
    public void NoProbeFileDeclared_ProbeStepIsSkipped_NotFailed()
    {
        // No data\stringtabley.xml anywhere — with a probe declared this would be
        // ProbeMissing. Without one, the marker and the engine carry the decision.
        var install = NewTempDir();
        CreateDirAt(install, @"art\zulushield");
        CreateEngineAt(install);

        Assert.Equal(ProbeOutcome.Match, ModInstallProbe.Inspect(install, NoProbeFileProfile()));
    }

    [Fact]
    public void NoProbeFileDeclared_StillRequiresTheMarker()
    {
        // Dropping the probe requirement must not drop the anti-vanilla signal too:
        // a bare AoE3 clone has the engine and no marker, and must not be adopted.
        var install = NewTempDir();
        CreateEngineAt(install);

        Assert.Equal(ProbeOutcome.MarkerMissing,
            ModInstallProbe.Inspect(install, NoProbeFileProfile()));
    }

    [Fact]
    public void NoProbeFileDeclared_StillRequiresTheEngine()
    {
        // The overlay-only leftover case, for a profile with no probe file.
        var install = NewTempDir();
        CreateDirAt(install, @"art\zulushield");

        Assert.Equal(ProbeOutcome.EngineMissing,
            ModInstallProbe.Inspect(install, NoProbeFileProfile()));
    }

    // ---------------- Interrupted install ----------------
    //
    // THE case that matters here. An install that dies partway leaves a folder holding
    // the AoE3 clone (probe file + engine DLLs) plus however much of the payload landed
    // — and the mod's marker is early in the payload. So every content signal says
    // "real install", and UpdateService's broad fallback scan adopts the first content
    // match it finds WITHOUT asking the user. Before the in-progress marker, that meant
    // a half-extracted folder could silently become "the install" and the launcher would
    // offer PLAY on a mod missing most of its files.

    [Fact]
    public void InterruptedInstall_IsNotAdopted_EvenThoughEverySignalIsPresent()
    {
        var install = NewTempDir();
        CreateFileAt(install, @"data\stringtabley.xml");   // from the clone
        CreateDirAt(install, @"art\zulushield");            // landed early in the payload
        CreateEngineAt(install);                            // from the clone
        CreateFileAt(install, ModInstallProbe.InstallInProgressMarker);

        Assert.Equal(ProbeOutcome.InstallInProgress,
            ModInstallProbe.Inspect(install, WolLikeProfile()));
        Assert.False(ModInstallProbe.LooksLikeModInstall(install, WolLikeProfile()));
    }

    /// <summary>
    /// The no-op half: clearing the marker (what a finished install does, right before it
    /// writes the manifest) restores a normal Match. If this ever fails, the guard is
    /// rejecting real installs — far worse than the bug it prevents.
    /// </summary>
    [Fact]
    public void FinishedInstall_WithTheMarkerCleared_IsAdoptedNormally()
    {
        var install = NewTempDir();
        CreateFileAt(install, @"data\stringtabley.xml");
        CreateDirAt(install, @"art\zulushield");
        CreateEngineAt(install);
        CreateFileAt(install, ModInstallProbe.InstallInProgressMarker);
        File.Delete(Path.Combine(install, ModInstallProbe.InstallInProgressMarker));

        Assert.Equal(ProbeOutcome.Match, ModInstallProbe.Inspect(install, WolLikeProfile()));
    }

    /// <summary>
    /// The marker only vetoes a folder that would otherwise pass. A folder missing the
    /// marker file must still report the signal it is actually missing, so the picker's
    /// rejection message keeps naming the real cause.
    /// </summary>
    [Fact]
    public void TheMarkerDoesNotMaskAMoreBasicFailure()
    {
        var install = NewTempDir();
        CreateFileAt(install, @"data\stringtabley.xml");
        CreateEngineAt(install);
        CreateFileAt(install, ModInstallProbe.InstallInProgressMarker);   // no art\zulushield

        Assert.Equal(ProbeOutcome.MarkerMissing,
            ModInstallProbe.Inspect(install, WolLikeProfile()));
    }

    // ---------------------------------------------------------------------
    // Ownership: a folder another mod's manifest claims.
    //
    // THE INCIDENT, and why these are the tests that matter in this file.
    // Napoleonic Era declares probe `age3n.exe` and NO marker, on the reasoning that
    // its own executable is exclusive to it. A stray orphan `age3n.exe` was sitting in
    // the user's AoE3 root, and FolderCloneService copies that root into every
    // IsolatedFolder install — so EVERY cloned mod folder carried it and satisfied
    // every content signal NE had. The launcher adopted Struggle of Indonesia's folder
    // as Napoleonic Era and then DELETED it, 11,408 files, reporting the whole time
    // that it was uninstalling Napoleonic Era.
    //
    // The lesson generalises past that mod: a probe file stops being exclusive the
    // moment one stray copy lands anywhere that gets cloned.
    // ---------------------------------------------------------------------

    private static ModProfile NapoleonicLikeProfile() => new()
    {
        Id = "napoleonic-era",
        DisplayName = "Napoleonic Era",
        InstallType = ModInstallType.IsolatedFolder,
        InstallProbeFile = "age3n.exe",
        InstallMarker = "",           // as the catalog shipped it
    };

    private static void WriteManifestOwnedBy(string root, string modIdJson)
        => File.WriteAllText(Path.Combine(root, InstallManifest.FileName),
                             $"{{ \"modId\": {modIdJson} }}");

    /// <summary>
    /// The incident, reduced to a fixture: every content signal Napoleonic Era declares
    /// is present, and the folder is still Struggle of Indonesia's install — which its
    /// own manifest says outright. Adopting it is what made a destructive uninstall
    /// possible, so this must not be a Match.
    /// </summary>
    [Fact]
    public void AFolderOwnedByAnotherModIsRejectedEvenWhenEveryContentSignalPasses()
    {
        var install = NewTempDir();
        CreateFileAt(install, "age3n.exe");      // the stray, carried in by the clone
        CreateEngineAt(install);                 // it IS a clone, so the engine is here
        WriteManifestOwnedBy(install, "\"struggle-of-indonesia\"");

        Assert.Equal(ProbeOutcome.ForeignInstall,
            ModInstallProbe.Inspect(install, NapoleonicLikeProfile()));
    }

    /// <summary>
    /// Ownership vetoes a COMPLETE content match, not merely a marker-less one — so a
    /// mod that does everything right is still refused somebody else's folder.
    /// </summary>
    [Fact]
    public void OwnershipVetoesEvenAFullContentMatch()
    {
        var install = NewTempDir();
        CreateFileAt(install, @"data\stringtabley.xml");
        CreateDirAt(install, @"art\zulushield");
        CreateEngineAt(install);
        WriteManifestOwnedBy(install, "\"improvement-mod\"");

        Assert.Equal(ProbeOutcome.ForeignInstall,
            ModInstallProbe.Inspect(install, WolLikeProfile()));
    }

    [Fact]
    public void AManifestNamingUsIsStillAMatch()
    {
        var install = NewTempDir();
        CreateFileAt(install, @"data\stringtabley.xml");
        CreateDirAt(install, @"art\zulushield");
        CreateEngineAt(install);
        WriteManifestOwnedBy(install, "\"WOL\"");   // and case must not matter

        Assert.Equal(ProbeOutcome.Match, ModInstallProbe.Inspect(install, WolLikeProfile()));
    }

    /// <summary>
    /// ⚠ The landmine. <c>InstallManifest.ModId</c> is declared non-nullable with a
    /// <c>""</c> initialiser, but <c>"modId": null</c> in the file writes a real null
    /// straight over it — System.Text.Json does not enforce the annotation. A plain
    /// <c>!=</c> would read every such install as foreign and refuse to detect OR
    /// uninstall it: total failure, looking exactly like "the launcher forgot my mod".
    /// All three "no owner recorded" shapes must fall through to the content answer.
    /// </summary>
    [Theory]
    [InlineData("{ }")]                    // key absent — the pre-modId builds
    [InlineData("{ \"modId\": \"\" }")]
    [InlineData("{ \"modId\": null }")]
    [InlineData("{ \"modId\": \"   \" }")]
    public void AManifestWithNoRecordedOwnerNeverRejects(string manifestJson)
    {
        var install = NewTempDir();
        CreateFileAt(install, @"data\stringtabley.xml");
        CreateDirAt(install, @"art\zulushield");
        CreateEngineAt(install);
        File.WriteAllText(Path.Combine(install, InstallManifest.FileName), manifestJson);

        Assert.Equal(ProbeOutcome.Match, ModInstallProbe.Inspect(install, WolLikeProfile()));
    }

    /// <summary>
    /// A manifest that cannot be parsed is no evidence in either direction. Stated as a
    /// test because it is a real residual: for a marker-less profile it drops protection
    /// back to the content signals that failed in the incident, which is why the
    /// catalog-side marker is the half of the fix the launcher cannot supply.
    /// </summary>
    [Fact]
    public void AnUnreadableManifestIsNoEvidenceEitherWay()
    {
        var install = NewTempDir();
        CreateFileAt(install, @"data\stringtabley.xml");
        CreateDirAt(install, @"art\zulushield");
        CreateEngineAt(install);
        File.WriteAllText(Path.Combine(install, InstallManifest.FileName), "{ not json at all");

        Assert.Equal(ProbeOutcome.Match, ModInstallProbe.Inspect(install, WolLikeProfile()));
    }

    /// <summary>
    /// A folder with no manifest at all — a hand-made install — is still detected. The
    /// guard refuses only on POSITIVE evidence of somebody else.
    /// </summary>
    [Fact]
    public void AFolderWithNoManifestIsStillDetected()
    {
        var install = NewTempDir();
        CreateFileAt(install, @"data\stringtabley.xml");
        CreateDirAt(install, @"art\zulushield");
        CreateEngineAt(install);

        Assert.Equal(ProbeOutcome.Match, ModInstallProbe.Inspect(install, WolLikeProfile()));
    }

    /// <summary>
    /// Ownership is checked LAST so it cannot mask a more basic failure — a foreign
    /// folder that also lacks our probe reports the probe, which is the honest answer to
    /// "why isn't my mod here". Same rule the marker already follows.
    /// </summary>
    [Fact]
    public void AForeignManifestDoesNotMaskAMoreBasicFailure()
    {
        var install = NewTempDir();
        CreateEngineAt(install);                          // no probe file
        WriteManifestOwnedBy(install, "\"improvement-mod\"");

        Assert.Equal(ProbeOutcome.ProbeMissing,
            ModInstallProbe.Inspect(install, WolLikeProfile()));
    }

    /// <summary>
    /// The ranking is load-bearing, not bookkeeping: ResolvePickedModInstall reports the
    /// HIGHEST outcome across candidates, and that is what lets the folder picker name
    /// the owner instead of listing the signals it went looking for.
    /// </summary>
    [Fact]
    public void AForeignInstallOutranksAHalfWrittenOneAndLosesToAMatch()
    {
        Assert.True(ProbeOutcome.ForeignInstall > ProbeOutcome.InstallInProgress);
        Assert.True(ProbeOutcome.Match > ProbeOutcome.ForeignInstall);
    }

    /// <summary>The pure rule, exhaustively — no disk involved.</summary>
    [Theory]
    [InlineData(null, "wol", false)]
    [InlineData("", "wol", false)]
    [InlineData("   ", "wol", false)]
    [InlineData("wol", "wol", false)]
    [InlineData("WoL", "wol", false)]
    [InlineData(" wol ", "wol", false)]
    [InlineData("improvement-mod", "wol", true)]
    [InlineData("wol", "", false)]          // no profile id to compare against
    public void TheOwnershipRuleRefusesOnlyOnPositiveEvidence(
        string? manifestModId, string profileId, bool expected)
    {
        Assert.Equal(expected,
            ModInstallProbe.ManifestClaimsAnotherMod(manifestModId, profileId));
    }
}
