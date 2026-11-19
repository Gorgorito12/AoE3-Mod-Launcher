using System;
using System.IO;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The optional LAN helper writes into a game's own folder, for the base game the player's own
/// Age of Empires III. The refusals are the point: an executable it does not know, another tool's
/// dinput8.dll and a mod that already ships the key are left exactly as they are, and removal
/// takes only what the launcher put there.
/// </summary>
public class LanHelperServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lanhelper-" + Guid.NewGuid());
    private string Exe => Path.Combine(_root, "age3y.exe");
    private string Dll => Path.Combine(_root, LanHelperService.DllName);
    private string Cfg => Path.Combine(_root, LanHelperService.CfgFolder, LanHelperService.CfgName);

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

    [Fact]
    public void TheEmbeddedDllIsTheOneTheHashNames()
    {
        var bytes = LanHelperService.ReadEmbeddedDll();
        Assert.Equal(LanHelperService.CurrentSha256, LanHelperService.Sha256Of(bytes));
        Assert.Equal(LanHelperService.CurrentSha256, LanHelperService.KnownSha256[0]);
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
        Assert.True(LanAutoStartArgs.CfgEnablesLanAutoStart(File.ReadAllLines(Cfg)));

        Assert.True(LanHelperService.Remove(_root));
        Assert.Equal(new[] { Exe }, Directory.GetFileSystemEntries(_root));
        Assert.Equal(LanHelperState.Missing, LanHelperService.Probe(Exe));
    }

    [Fact]
    public void AnUnknownExecutableIsNeverWrittenTo()
    {
        WriteGame(ExeHeader(stamp: 0x11111111));
        Assert.NotNull(LanHelperService.Install(Exe));
        Assert.False(File.Exists(Dll));
        Assert.False(Directory.Exists(Path.Combine(_root, LanHelperService.CfgFolder)));
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_AnotherToolsDllIsNeitherReplacedNorRemoved()
    {
        WriteGame();
        var foreign = new byte[] { 1, 2, 3, 4 };
        File.WriteAllBytes(Dll, foreign);

        Assert.Equal(LanHelperState.ForeignDll, LanHelperService.Probe(Exe));
        Assert.NotNull(LanHelperService.Install(Exe));
        LanHelperService.Remove(_root);

        Assert.Equal(foreign, File.ReadAllBytes(Dll));
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
        // The future WoL patch: its own dinput8.dll and engine-ext.cfg, not ours to touch.
        WriteGame();
        File.WriteAllBytes(Dll, new byte[] { 9, 9, 9 });
        Directory.CreateDirectory(Path.GetDirectoryName(Cfg)!);
        File.WriteAllLines(Cfg, new[] { "lanAutoStart" });

        Assert.Equal(LanHelperState.EnabledByMod, LanHelperService.Probe(Exe));
        LanHelperService.Remove(_root);

        Assert.True(File.Exists(Dll));
        Assert.Equal(new[] { "lanAutoStart" }, File.ReadAllLines(Cfg));
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_AModThatNowShipsTheKeyTakesOursOut()
    {
        // A WoL patch brings things-new's UHC.dll with lanAutoStart in its Startup cfg while our
        // helper is still there: both would host or join, so ours steps aside, and only ours.
        WriteGame();
        Assert.Null(LanHelperService.Install(Exe));
        var startup = Path.Combine(_root, "Startup", "wol.cfg");
        Directory.CreateDirectory(Path.GetDirectoryName(startup)!);
        File.WriteAllLines(startup, new[] { "noAILimitV2", "lanAutoStart" });

        Assert.Equal(LanHelperState.SupersededByMod, LanHelperService.Probe(Exe));
        Assert.NotNull(LanHelperService.Install(Exe));
        Assert.True(LanHelperService.Remove(_root));

        Assert.False(File.Exists(Dll));
        Assert.False(Directory.Exists(Path.Combine(_root, LanHelperService.CfgFolder)));
        Assert.Equal(new[] { "noAILimitV2", "lanAutoStart" }, File.ReadAllLines(startup));
        Assert.Equal(LanHelperState.EnabledByMod, LanHelperService.Probe(Exe));
    }

    [Fact]
    public void APatchThatShipsOurSameDllWithItsOwnCfgOwnsIt()
    {
        // The patch's dinput8.dll is byte-identical to ours, its cfg is its own: not ours to remove.
        WriteGame();
        File.WriteAllBytes(Dll, LanHelperService.ReadEmbeddedDll());
        Directory.CreateDirectory(Path.GetDirectoryName(Cfg)!);
        File.WriteAllLines(Cfg, new[] { "lanAutoStart" });

        Assert.Equal(LanHelperState.EnabledByMod, LanHelperService.Probe(Exe));
        Assert.True(LanHelperService.Remove(_root));

        Assert.True(File.Exists(Dll));
        Assert.Equal(new[] { "lanAutoStart" }, File.ReadAllLines(Cfg));
    }

    [Fact]
    public void APatchThatReplacedOurDllKeepsTheCfgItRunsWith()
    {
        // A newer dinput8.dll from the mod over ours, our two cfg lines still there: switching the
        // helper off must not take the key away from the mod's DLL.
        WriteGame();
        Assert.Null(LanHelperService.Install(Exe));
        var theirs = new byte[] { 7, 7, 7 };
        File.WriteAllBytes(Dll, theirs);
        var cfgBefore = File.ReadAllLines(Cfg);

        Assert.Equal(LanHelperState.EnabledByMod, LanHelperService.Probe(Exe));
        Assert.NotNull(LanHelperService.Install(Exe));
        Assert.True(LanHelperService.Remove(_root));

        Assert.Equal(theirs, File.ReadAllBytes(Dll));
        Assert.Equal(cfgBefore, File.ReadAllLines(Cfg));
    }

    [Fact]
    public void RemovalKeepsWhatSomebodyElseWroteInTheCfg()
    {
        WriteGame();
        Directory.CreateDirectory(Path.GetDirectoryName(Cfg)!);
        File.WriteAllLines(Cfg, new[] { "fastVertexFill" });

        Assert.Null(LanHelperService.Install(Exe));
        Assert.Equal(LanHelperState.Installed, LanHelperService.Probe(Exe));
        Assert.True(LanHelperService.Remove(_root));

        Assert.Equal(new[] { "fastVertexFill" }, File.ReadAllLines(Cfg));
        Assert.False(File.Exists(Dll));
    }

    [Fact]
    public void OurDllWithoutItsCfgLineIsBroughtBack()
    {
        WriteGame();
        Assert.Null(LanHelperService.Install(Exe));
        File.Delete(Cfg);

        Assert.Equal(LanHelperState.Outdated, LanHelperService.Probe(Exe));
        Assert.Null(LanHelperService.Install(Exe));
        Assert.Equal(LanHelperState.Installed, LanHelperService.Probe(Exe));
    }

    [Fact]
    public void FilesSavedForAManualCopyAreRecognisedOnceCopiedAndRemovedLikeOurs()
    {
        WriteGame();
        var export = Path.Combine(_root, "desktop");
        Directory.CreateDirectory(export);
        var folder = LanHelperService.ExportTo(export, "readme", out var error);
        Assert.Null(error);
        Assert.NotNull(folder);
        Assert.True(File.Exists(Path.Combine(folder!, "README.txt")));

        // The player copies the two things next to the game, by hand.
        File.Copy(Path.Combine(folder!, LanHelperService.DllName), Dll);
        Directory.CreateDirectory(Path.GetDirectoryName(Cfg)!);
        File.Copy(Path.Combine(folder!, LanHelperService.CfgFolder, LanHelperService.CfgName), Cfg);
        Assert.Equal(LanHelperState.Installed, LanHelperService.Probe(Exe));

        Assert.True(LanHelperService.Remove(_root));
        Assert.False(File.Exists(Dll));
        Assert.False(Directory.Exists(Path.GetDirectoryName(Cfg)));
    }

    [Fact]
    public void RemovingFromAFolderThatIsGoneIsDone()
    {
        Assert.True(LanHelperService.Remove(Path.Combine(_root, "gone")));
        Assert.True(LanHelperService.Remove(null));
    }
}
