using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins the gate between a community addon's ZIP and the install folder.
///
/// The REJECTION cases are the point. Three subsystems key off the same three
/// files — version detection (MD5s them to identify the build), the multiplayer
/// fingerprint (hashes them into the CombinedHash the lobby validates), and the
/// translation snapshot — so letting an addon write one of them locks the player
/// out of every room and makes the launcher queue the entire patch chain. A miss
/// here is a bug the player experiences as "the launcher broke my multiplayer",
/// with nothing in the UI connecting it back to an addon they enabled.
/// </summary>
public class AddonRiskTests
{
    private static AddonRiskLevel Level(params string[] entries)
        => AddonRisk.Assess(entries).Level;

    // -- Blocked: the three identity files ------------------------------------

    [Theory]
    [InlineData(@"data\protoy.xml")]
    [InlineData(@"data\techtreey.xml")]
    [InlineData(@"data\stringtabley.xml")]
    public void ProtectedFiles_AreBlocked(string entry)
        => Assert.Equal(AddonRiskLevel.Blocked, Level(entry));

    /// <summary>
    /// Zips store forward slashes and arbitrary casing; neither is a signal, and
    /// treating them as one would let the exact file we refuse walk straight in.
    /// </summary>
    [Theory]
    [InlineData("data/protoy.xml")]
    [InlineData(@"DATA\PROTOY.XML")]
    [InlineData(@"Data\ProtoY.xml")]
    [InlineData(@"./data/protoy.xml")]
    public void ProtectedFiles_AreBlocked_RegardlessOfSeparatorOrCase(string entry)
        => Assert.Equal(AddonRiskLevel.Blocked, Level(entry));

    /// <summary>
    /// Packing the payload inside one wrapper folder is the NORMAL shape for a
    /// community zip, not an edge case — matching only root-relative paths would
    /// miss most real archives.
    /// </summary>
    [Theory]
    [InlineData(@"My Addon\data\protoy.xml")]
    [InlineData(@"rotate-buildings-v2\data\techtreey.xml")]
    [InlineData(@"outer\inner\data\protoy.xml")]
    public void ProtectedFiles_AreBlocked_UnderAnyWrapperDepth(string entry)
        => Assert.Equal(AddonRiskLevel.Blocked, Level(entry));

    /// <summary>One protected file condemns the whole addon — it ships as a unit.</summary>
    [Fact]
    public void Blocked_WinsOverEverythingElseInTheArchive()
    {
        var result = AddonRisk.Assess(new[]
        {
            @"art\ui\panel.ddt",
            @"Sound\musket.wav",
            @"data\protoy.xml",
            @"data\civs.xml",
        });

        Assert.Equal(AddonRiskLevel.Blocked, result.Level);
        Assert.Equal(new[] { @"data\protoy.xml" }, result.BlockingFiles);
    }

    // -- SimulationRisk: data\ outside the three -------------------------------

    /// <summary>
    /// The asymmetry that makes this tier necessary: the lobby fingerprint covers
    /// three files, not the simulation. These pass the join check and can still
    /// desync the match, so the warning has to happen before applying — nothing
    /// downstream will catch it.
    /// </summary>
    [Theory]
    [InlineData(@"data\civs.xml")]
    [InlineData(@"data\abilities\powers.xml")]
    [InlineData(@"My Addon\data\tactics\sarna.tactics")]
    public void OtherDataFiles_AreSimulationRisk(string entry)
        => Assert.Equal(AddonRiskLevel.MultiplayerRisk, Level(entry));

    [Fact]
    public void SimulationFiles_AreReported_SoTheUiCanNameThem()
    {
        var result = AddonRisk.Assess(new[] { @"art\x.ddt", @"data\civs.xml" });

        Assert.Equal(AddonRiskLevel.MultiplayerRisk, result.Level);
        Assert.Equal(new[] { @"data\civs.xml" }, result.SimulationFiles);
        Assert.Empty(result.BlockingFiles);
    }

    // -- Cosmetic --------------------------------------------------------------

    [Fact]
    public void ArtSoundAndUi_AreCosmetic()
    {
        var level = Level(
            @"art\ui\hud\panel.ddt",
            @"Sound\Weapons\musket_01.wav",
            @"ui\layout.xml");

        Assert.Equal(AddonRiskLevel.Cosmetic, level);
    }

    /// <summary>
    /// "data" has to be a path SEGMENT. A file merely containing the substring —
    /// or an art folder that happens to be named for it — isn't simulation data,
    /// and flagging it would train users to click past the warning.
    /// </summary>
    [Theory]
    [InlineData(@"art\metadata.xml")]
    [InlineData(@"art\database\icon.ddt")]
    [InlineData(@"Sound\data_stream.wav")]
    public void DataAsASubstring_IsNotSimulationRisk(string entry)
        => Assert.Equal(AddonRiskLevel.Cosmetic, Level(entry));

    // -- Degenerate input ------------------------------------------------------

    /// <summary>
    /// An empty archive must not read as Cosmetic: "nothing to apply" is a
    /// failure to surface, not a safe addon to green-light.
    /// </summary>
    [Fact]
    public void EmptyArchive_IsEmpty_NotCosmetic()
        => Assert.Equal(AddonRiskLevel.Empty, AddonRisk.Assess(new string[0]).Level);

    [Fact]
    public void Null_IsEmpty()
        => Assert.Equal(AddonRiskLevel.Empty, AddonRisk.Assess(null).Level);

    /// <summary>Directory entries carry no content, so they can't make an archive non-empty.</summary>
    [Fact]
    public void DirectoryEntriesOnly_IsEmpty()
        => Assert.Equal(AddonRiskLevel.Empty, Level(@"data\", @"art\ui\", "My Addon/"));

    [Fact]
    public void BlankEntries_AreIgnored()
        => Assert.Equal(AddonRiskLevel.Cosmetic, Level("", "   ", @"art\x.ddt"));

    /// <summary>
    /// The block list is derived from UpdateService's constants rather than
    /// re-typed, so detection and this gate can never drift apart.
    /// </summary>
    [Fact]
    public void ProtectedList_TracksTheDetectionConstants()
    {
        Assert.Contains(UpdateService.ProtoRelativePath, AddonRisk.ProtectedFiles);
        Assert.Contains(UpdateService.TechRelativePath, AddonRisk.ProtectedFiles);
        Assert.Contains(UpdateService.StrRelativePath, AddonRisk.ProtectedFiles);
    }

    // -- Executables and documentation ----------------------------------------
    //
    // Modelled on the real "building rotator" archive: one config file that does
    // the work, plus a UPX-packed PE32, a PDF and a screenshot. Copying a packed
    // binary into a game folder is the exact heuristic that got this project's
    // own executable quarantined by Defender, so those never get written.

    [Theory]
    [InlineData("Building Rotator.exe")]
    [InlineData(@"tools\helper.DLL")]
    [InlineData("install.bat")]
    [InlineData("run.cmd")]
    [InlineData("setup.msi")]
    [InlineData("script.ps1")]
    public void Executables_AreReported(string entry)
    {
        var result = AddonRisk.Assess(new[] { entry, @"art\ui\panel.ddt" });

        Assert.Single(result.ExecutableFiles);
        Assert.True(AddonRisk.IsExecutable(entry));
    }

    /// <summary>
    /// An executable doesn't condemn the addon — the useful part of a real
    /// archive is usually one config file sitting next to the author's tool. It
    /// is skipped, not fatal.
    /// </summary>
    [Fact]
    public void Executable_DoesNotBlockTheAddon()
    {
        var result = AddonRisk.Assess(new[] { "Building Rotator.exe", @"startup\gamey.con" });

        Assert.Equal(AddonRiskLevel.Cosmetic, result.Level);
        Assert.Empty(result.BlockingFiles);
    }

    /// <summary>
    /// An archive of nothing but an executable has nothing to apply — reporting
    /// Empty is what stops it being silently treated as a working addon.
    /// </summary>
    [Fact]
    public void ExecutableOnlyArchive_HasNothingToApply()
    {
        var result = AddonRisk.Assess(new[] { "Building Rotator.exe" });

        Assert.Single(result.ExecutableFiles);
        Assert.Empty(result.BlockingFiles);
        Assert.Empty(result.SimulationFiles);
    }

    [Theory]
    [InlineData("EV Products ReadMe.pdf")]
    [InlineData("rotated.png")]
    [InlineData("readme.txt")]
    public void Documentation_IsSkippable(string entry)
    {
        Assert.True(AddonRisk.IsDocument(entry));
        Assert.True(AddonRisk.IsSkippable(entry));
    }

    /// <summary>
    /// Game files must NOT be caught by the skip rules — `.con` startup configs
    /// are exactly what the rotate-buildings addon actually delivers.
    /// </summary>
    [Theory]
    [InlineData(@"startup\gamey.con")]
    [InlineData(@"art\ui\panel.ddt")]
    [InlineData(@"Sound\musket.wav")]
    public void GameFiles_AreNotSkipped(string entry)
        => Assert.False(AddonRisk.IsSkippable(entry));

    // -- .xmb and the engine's own version check -------------------------------
    //
    // Modelled on the gun-smoke addon, which replaces 77 .xmb files. AoE3 hashes
    // these for its LAN version match, so peers without the addon may not be able
    // to play — and the launcher's fingerprint covers three data\ files, so it
    // cannot detect this afterwards. Saying so before applying is the only chance.

    [Theory]
    [InlineData(@"art\effects\explosions\1exp_flash.particle.xmb")]
    [InlineData("AO3/art/effects/chiefpower.xml.xmb")]
    public void XmbFiles_AreAMultiplayerRisk(string entry)
    {
        var result = AddonRisk.Assess(new[] { entry });

        Assert.Equal(AddonRiskLevel.MultiplayerRisk, result.Level);
        Assert.Single(result.VersionMatchFiles);
        Assert.Empty(result.SimulationFiles);
    }

    /// <summary>
    /// The two causes are reported separately because the symptoms differ: a
    /// data\ change desyncs a match in progress, an .xmb change can stop it from
    /// starting. The warning names whichever applies.
    /// </summary>
    [Fact]
    public void SimulationAndVersionMatch_AreReportedSeparately()
    {
        var result = AddonRisk.Assess(new[] { @"data\playercolors.xml", @"art\x.particle.xmb" });

        Assert.Equal(AddonRiskLevel.MultiplayerRisk, result.Level);
        Assert.Single(result.SimulationFiles);
        Assert.Single(result.VersionMatchFiles);
    }

    [Fact]
    public void PlainArtAssets_StayCosmetic()
        => Assert.Equal(AddonRiskLevel.Cosmetic, Level(@"art\ui\panel.ddt", @"Sound\shot.wav"));

    /// <summary>
    /// An archive of nothing but an installer and its docs has no game files, so
    /// there is nothing to apply — reporting Empty stops it being offered as a
    /// working addon. This is the real "transparent UI" archive: one .exe.
    /// </summary>
    [Fact]
    public void InstallerOnlyArchive_IsEmpty()
        => Assert.Equal(AddonRiskLevel.Empty, Level("Ekanta TAD UI.exe"));
}
