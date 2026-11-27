using System;
using System.IO;
using System.Linq;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The optional LAN helper writes into a game's own folder, for the base game the player's own
/// Age of Empires III. The refusals are the point: an executable it does not know, another tool's
/// avifil32.dll, a dinput8.dll that is not ours and a mod that already turns the key on are left
/// exactly as they are, and removal takes only what the launcher put there — its own file, and the
/// earlier helper (dinput8.dll + two marked cfg lines) only when it is provably the launcher's.
/// </summary>
public class LanHelperServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lanhelper-" + Guid.NewGuid());
    private string Exe => Path.Combine(_root, "age3y.exe");
    private string Dll => Path.Combine(_root, LanHelperService.DllName);
    private string OldDll => Path.Combine(_root, LanHelperService.LegacyDllName);
    private string Cfg => Path.Combine(_root, LanHelperService.CfgFolder, LanHelperService.CfgName);
    private static readonly string[] OurLines = { LanHelperService.CfgMarker, LanHelperService.CfgKey };

    public LanHelperServiceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static byte[] ExeHeader(uint stamp = 0x49EF86AC, uint entry = 0x001876AC)
    {
        var bytes = new byte[512];
        bytes[0] = (byte)'M'; bytes[1] = (byte)'Z';
        BitConverter.GetBytes(0x80).CopyTo(bytes, 0x3C);
        bytes[0x80] = (byte)'P'; bytes[0x81] = (byte)'E';
        BitConverter.GetBytes(stamp).CopyTo(bytes, 0x80 + 8);
        BitConverter.GetBytes(entry).CopyTo(bytes, 0x80 + 40);
        return bytes;
    }

    private void WriteGame(byte[]? header = null) => File.WriteAllBytes(Exe, header ?? ExeHeader());

    private void WriteCfg(params string[] lines)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Cfg)!);
        File.WriteAllLines(Cfg, lines);
    }

    /// <summary>The dinput8.dll launcher v1.1.0 really installed (the 0.4.0 build), kept as a fixture.</summary>
    private static byte[] LegacyDll() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "lan-helper-0.4.0-dinput8.dll"));

    [Fact]
    public void TheEmbeddedDllIsTheOneTheHashNames()
    {
        var bytes = LanHelperService.ReadEmbeddedDll();
        Assert.Equal(LanHelperService.CurrentSha256, LanHelperService.Sha256Of(bytes));
        Assert.Equal(LanHelperService.CurrentSha256, LanHelperService.KnownSha256[0]);
    }

    /// <summary>
    /// The earlier helper went out with v1.1.0 as dinput8.dll. Its hash must stay in the legacy
    /// list, or a player who has it keeps both helpers for ever: the launcher would no longer
    /// recognise that file as its own to take away. Held against the real file, not a constant.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheEarlierHelperIsStillRecognised()
    {
        var shipped = LanHelperService.Sha256Of(LegacyDll());
        Assert.Contains(shipped, LanHelperService.LegacySha256);
        Assert.DoesNotContain(shipped, LanHelperService.KnownSha256);
        Assert.Equal(LanHelperService.KnownSha256.Count, LanHelperService.KnownSha256.Distinct().Count());
        Assert.NotEqual(LanHelperService.LegacyDllName, LanHelperService.DllName, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0x49EF86ACu, 0x001876ACu, true)]   // the stock executable
    [InlineData(0x49EF86ACu, 0x007836BEu, true)]   // the same build with the UHC loader
    [InlineData(0x49EF86ACu, 0x00123456u, false)]  // an entry point nobody tested
    [InlineData(0x12345678u, 0x001876ACu, false)]  // another build (age3.exe, a patched game)
    public void OnlyTheKnownGameBuildIsSupported(uint stamp, uint entry, bool expected)
    {
        Assert.Equal(expected, LanHelperService.IsSupportedExeHeader(ExeHeader(stamp, entry)));
    }

    [Fact]
    public void GarbageIsNotAnExecutable()
    {
        Assert.False(LanHelperService.IsSupportedExeHeader(new byte[10]));
        Assert.False(LanHelperService.IsSupportedExeHeader(new byte[512]));
        Assert.Equal(LanHelperState.Unsupported, LanHelperService.Probe(null));
        Assert.Equal(LanHelperState.Unsupported, LanHelperService.Probe(Path.Combine(_root, "missing.exe")));
    }

    [Fact]
    public void InstallThenRemoveLeavesTheFolderAsItWas()
    {
        WriteGame();
        Assert.Equal(LanHelperState.Missing, LanHelperService.Probe(Exe));

        Assert.Null(LanHelperService.Install(Exe));
        Assert.Equal(LanHelperState.Installed, LanHelperService.Probe(Exe));
        // One file, and no cfg of any kind.
        Assert.Equal(new[] { Exe, Dll }.OrderBy(p => p), Directory.GetFileSystemEntries(_root).OrderBy(p => p));

        Assert.True(LanHelperService.Remove(_root));
        Assert.Equal(new[] { Exe }, Directory.GetFileSystemEntries(_root));
        Assert.Equal(LanHelperState.Missing, LanHelperService.Probe(Exe));
    }

    [Fact]
    public void RemovalTakesTheDllsOwnLogToo()
    {
        WriteGame();
        Assert.Null(LanHelperService.Install(Exe));
        File.WriteAllText(Path.Combine(_root, LanHelperService.LogName), "lan helper: done");

        Assert.True(LanHelperService.Remove(_root));
        Assert.Equal(new[] { Exe }, Directory.GetFileSystemEntries(_root));
    }

    [Fact]
    public void AnUnknownExecutableIsNeverWrittenTo()
    {
        WriteGame(ExeHeader(stamp: 0x11111111));
        Assert.NotNull(LanHelperService.Install(Exe));
        Assert.False(File.Exists(Dll));
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_AnotherToolsDllIsNeitherReplacedNorRemoved()
    {
        WriteGame();
        var foreign = new byte[] { 1, 2, 3, 4 };
        File.WriteAllBytes(Dll, foreign);
        File.WriteAllText(Path.Combine(_root, LanHelperService.LogName), "theirs, by name");

        Assert.Equal(LanHelperState.ForeignDll, LanHelperService.Probe(Exe));
        Assert.NotNull(LanHelperService.Install(Exe));
        LanHelperService.Remove(_root);

        Assert.Equal(foreign, File.ReadAllBytes(Dll));
        Assert.True(File.Exists(Path.Combine(_root, LanHelperService.LogName)));
        Assert.False(LanHelperService.HasOurDll(_root));
    }

    [Fact]
    public void AModThatShipsTheKeyIsLeftAlone()
    {
        WriteGame();
        Directory.CreateDirectory(Path.Combine(_root, "Startup"));
        File.WriteAllLines(Path.Combine(_root, "Startup", "wol.cfg"), new[] { "noAILimitV2", "lanAutoStart" });

        Assert.Equal(LanHelperState.EnabledByMod, LanHelperService.Probe(Exe));
        Assert.NotNull(LanHelperService.Install(Exe));
        Assert.False(File.Exists(Dll));
    }

    [Fact]
    public void AModsOwnDinput8WithItsCfgIsTheMods()
    {
        // The mod's patch: its own dinput8.dll and engine-ext.cfg with the key. Not ours to touch.
        WriteGame();
        File.WriteAllBytes(OldDll, new byte[] { 9, 9, 9 });
        WriteCfg("fastVertexFill", "lanAutoStart");

        Assert.Equal(LanHelperState.EnabledByMod, LanHelperService.Probe(Exe));
        Assert.NotNull(LanHelperService.Install(Exe));
        LanHelperService.Remove(_root);

        Assert.False(File.Exists(Dll));
        Assert.Equal(new byte[] { 9, 9, 9 }, File.ReadAllBytes(OldDll));
        Assert.Equal(new[] { "fastVertexFill", "lanAutoStart" }, File.ReadAllLines(Cfg));
    }

    /// <summary>
    /// What this helper exists for: the mod ships its own dinput8.dll and its own cfg WITHOUT the
    /// key. With the earlier helper that switched the entry off and the launcher could do nothing;
    /// now it installs its own file beside them and touches neither.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_AModsDinput8WithoutTheKeyNoLongerBlocksTheHelper()
    {
        WriteGame();
        var theirs = new byte[] { 7, 7, 7 };
        File.WriteAllBytes(OldDll, theirs);
        WriteCfg("fastVertexFill");

        Assert.Equal(LanHelperState.Missing, LanHelperService.Probe(Exe));
        Assert.Null(LanHelperService.Install(Exe));
        Assert.Equal(LanHelperState.Installed, LanHelperService.Probe(Exe));
        Assert.Equal(theirs, File.ReadAllBytes(OldDll));
        Assert.Equal(new[] { "fastVertexFill" }, File.ReadAllLines(Cfg));

        Assert.True(LanHelperService.Remove(_root));
        Assert.False(File.Exists(Dll));
        Assert.Equal(theirs, File.ReadAllBytes(OldDll));
        Assert.Equal(new[] { "fastVertexFill" }, File.ReadAllLines(Cfg));
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_AModThatNowShipsTheKeyTakesOursOut()
    {
        // A mod's patch turns lanAutoStart on in its Startup cfg while our helper is still there:
        // both would host or join, so ours steps aside, and only ours.
        WriteGame();
        Assert.Null(LanHelperService.Install(Exe));
        var startup = Path.Combine(_root, "Startup", "wol.cfg");
        Directory.CreateDirectory(Path.GetDirectoryName(startup)!);
        File.WriteAllLines(startup, new[] { "noAILimitV2", "lanAutoStart" });

        Assert.Equal(LanHelperState.SupersededByMod, LanHelperService.Probe(Exe));
        Assert.NotNull(LanHelperService.Install(Exe));
        Assert.True(LanHelperService.Remove(_root));

        Assert.False(File.Exists(Dll));
        Assert.Equal(new[] { "noAILimitV2", "lanAutoStart" }, File.ReadAllLines(startup));
        Assert.Equal(LanHelperState.EnabledByMod, LanHelperService.Probe(Exe));
    }

    [Fact]
    public void AModsDinput8ThatTurnsTheKeyOnTakesOursOutToo()
    {
        WriteGame();
        Assert.Null(LanHelperService.Install(Exe));
        File.WriteAllBytes(OldDll, new byte[] { 5, 5 });
        WriteCfg("lanAutoStart");

        Assert.Equal(LanHelperState.SupersededByMod, LanHelperService.Probe(Exe));
        Assert.True(LanHelperService.Remove(_root));

        Assert.False(File.Exists(Dll));
        Assert.True(File.Exists(OldDll));
        Assert.Equal(new[] { "lanAutoStart" }, File.ReadAllLines(Cfg));
    }

    // ----- the earlier helper (v1.1.0): dinput8.dll + two marked lines -----

    /// <summary>
    /// How every player of v1.1.0 changes over: entering a room installs the new file and takes the
    /// earlier pair away, so the folder ends with the game and ONE file of ours.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheEarlierHelperIsReplacedWhole()
    {
        WriteGame();
        File.WriteAllBytes(OldDll, LegacyDll());
        WriteCfg(OurLines);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(Cfg)!, "engine-ext.log"), "dinput8.dll 0.4.0");

        Assert.Equal(LanHelperState.Missing, LanHelperService.Probe(Exe));
        Assert.True(LanHelperService.LegacyStillEnters(_root));
        Assert.True(LanHelperService.HasOurDll(_root));

        Assert.Null(LanHelperService.Install(Exe));

        Assert.Equal(LanHelperState.Installed, LanHelperService.Probe(Exe));
        Assert.Equal(new[] { Exe, Dll }.OrderBy(p => p), Directory.GetFileSystemEntries(_root).OrderBy(p => p));
    }

    [Fact]
    public void OursBesideTheEarlierHelperIsOutdatedUntilItIsTidied()
    {
        // The new file copied by hand while the earlier pair is still there.
        WriteGame();
        File.WriteAllBytes(OldDll, LegacyDll());
        WriteCfg(OurLines);
        File.WriteAllBytes(Dll, LanHelperService.ReadEmbeddedDll());

        Assert.Equal(LanHelperState.Outdated, LanHelperService.Probe(Exe));
        Assert.Null(LanHelperService.Install(Exe));

        Assert.Equal(LanHelperState.Installed, LanHelperService.Probe(Exe));
        Assert.False(File.Exists(OldDll));
        Assert.False(Directory.Exists(Path.GetDirectoryName(Cfg)));
    }

    [Fact]
    public void TheEarlierHelperIsRemovedWhenThePlayerSwitchesItOff()
    {
        WriteGame();
        File.WriteAllBytes(OldDll, LegacyDll());
        WriteCfg("fastVertexFill", LanHelperService.CfgMarker, LanHelperService.CfgKey);

        Assert.True(LanHelperService.Remove(_root));

        Assert.False(File.Exists(OldDll));
        Assert.Equal(new[] { "fastVertexFill" }, File.ReadAllLines(Cfg));
        Assert.False(LanHelperService.HasOurDll(_root));
    }

    [Fact]
    public void TheEarlierHelpersLinesAloneAreTidiedAwayByInstalling()
    {
        // Its DLL is already gone (the player deleted it); the marked lines are still ours.
        WriteGame();
        WriteCfg(OurLines);

        Assert.Equal(LanHelperState.Missing, LanHelperService.Probe(Exe));
        Assert.Null(LanHelperService.Install(Exe));

        Assert.Equal(LanHelperState.Installed, LanHelperService.Probe(Exe));
        Assert.False(Directory.Exists(Path.GetDirectoryName(Cfg)));
    }

    [Fact]
    public void TheEarlierHelpersLinesGoAndSomebodyElsesStay()
    {
        WriteGame();
        WriteCfg("fastVertexFill", LanHelperService.CfgMarker, LanHelperService.CfgKey);

        Assert.Null(LanHelperService.Install(Exe));

        Assert.Equal(new[] { "fastVertexFill" }, File.ReadAllLines(Cfg));
        Assert.Equal(LanHelperState.Installed, LanHelperService.Probe(Exe));
    }

    [Fact]
    public void APatchThatReplacedOurOldDllKeepsTheCfgItRunsWith()
    {
        // A newer dinput8.dll from the mod over the earlier helper, our two cfg lines still there:
        // that DLL enters with our lines, so they are the mod's now and ours stays out.
        WriteGame();
        var theirs = new byte[] { 7, 7, 7 };
        File.WriteAllBytes(OldDll, theirs);
        WriteCfg(OurLines);

        Assert.Equal(LanHelperState.EnabledByMod, LanHelperService.Probe(Exe));
        Assert.NotNull(LanHelperService.Install(Exe));
        Assert.True(LanHelperService.Remove(_root));

        Assert.False(File.Exists(Dll));
        Assert.Equal(theirs, File.ReadAllBytes(OldDll));
        Assert.Equal(OurLines, File.ReadAllLines(Cfg));
    }

    [Fact]
    public void APatchThatShipsOurSameOldDllWithItsOwnCfgOwnsIt()
    {
        // The patch's dinput8.dll is byte-identical to the earlier helper, its cfg is its own.
        WriteGame();
        File.WriteAllBytes(OldDll, LegacyDll());
        WriteCfg("lanAutoStart");

        Assert.Equal(LanHelperState.EnabledByMod, LanHelperService.Probe(Exe));
        Assert.False(LanHelperService.HasOurDll(_root));
        Assert.True(LanHelperService.Remove(_root));

        Assert.True(File.Exists(OldDll));
        Assert.Equal(new[] { "lanAutoStart" }, File.ReadAllLines(Cfg));
    }

    // ----- by hand -----

    [Fact]
    public void TheFileSavedForAManualCopyIsRecognisedOnceCopiedAndRemovedLikeOurs()
    {
        WriteGame();
        var export = Path.Combine(_root, "desktop");
        Directory.CreateDirectory(export);
        var folder = LanHelperService.ExportTo(export, "readme", out var error);
        Assert.Null(error);
        Assert.NotNull(folder);
        Assert.Equal(new[] { "README.txt", LanHelperService.DllName }.OrderBy(n => n, StringComparer.Ordinal),
            Directory.GetFileSystemEntries(folder!).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal));

        // The player copies it next to the game, by hand.
        File.Copy(Path.Combine(folder!, LanHelperService.DllName), Dll);
        Assert.Equal(LanHelperState.Installed, LanHelperService.Probe(Exe));

        Assert.True(LanHelperService.Remove(_root));
        Assert.False(File.Exists(Dll));
    }

    [Fact]
    public void RemovingFromAFolderThatIsGoneIsDone()
    {
        Assert.True(LanHelperService.Remove(Path.Combine(_root, "gone")));
        Assert.True(LanHelperService.Remove(null));
    }
}
