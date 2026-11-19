using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The LAN helper's elevated copy takes one path from a command line and runs as administrator,
/// so the refusals are the point: anything that is not exactly one canonical absolute path to an
/// executable never reaches the copy.
/// </summary>
public class LanHelperElevatedTests
{
    [Fact]
    public void TheInstallVerbTakesExactlyOneExecutable()
    {
        Assert.True(LanHelperElevated.TryParse(new[] { "--install-lan-helper", @"C:\Games\WoL\age3y.exe" }, out var install, out var path));
        Assert.True(install);
        Assert.Equal(@"C:\Games\WoL\age3y.exe", path);
    }

    [Theory]
    [InlineData(@"C:\Games\WoL\age3y.dll")]
    [InlineData(@"C:\Games\WoL\..\Windows\age3y.exe")]
    [InlineData(@"Games\WoL\age3y.exe")]
    [InlineData(@"C:\Games\Wo""L\age3y.exe")]
    [InlineData(@"\\?\C:\Games\age3y.exe")]
    [InlineData("")]
    public void THE_ONE_THAT_MATTERS_AnythingElseIsRefused(string bad)
    {
        Assert.False(LanHelperElevated.TryParse(new[] { "--install-lan-helper", bad }, out _, out _));
        Assert.Null(LanHelperElevated.BuildInstallArguments(bad));
    }

    [Fact]
    public void ExtraOrMissingArgumentsAreRefused()
    {
        Assert.False(LanHelperElevated.TryParse(new[] { "--install-lan-helper" }, out _, out _));
        Assert.False(LanHelperElevated.TryParse(new[] { "--install-lan-helper", @"C:\G\age3y.exe", "--more" }, out _, out _));
        Assert.False(LanHelperElevated.TryParse(new[] { "--other", @"C:\G\age3y.exe" }, out _, out _));
        Assert.Equal((int)LanHelperElevated.Exit.BadArguments, LanHelperElevated.Run(new[] { "--install-lan-helper" }));
    }

    [Fact]
    public void TheRemovalVerbTakesAFolderAndNeverADriveRoot()
    {
        Assert.True(LanHelperElevated.TryParse(new[] { "--remove-lan-helper", @"C:\Games\WoL" }, out var install, out _));
        Assert.False(install);
        Assert.False(LanHelperElevated.TryParse(new[] { "--remove-lan-helper", @"C:\" }, out _, out _));
        Assert.Equal(@"--remove-lan-helper ""C:\Games\WoL""", LanHelperElevated.BuildRemoveArguments(@"C:\Games\WoL\"));
    }

    [Fact]
    public void ONE_PROMPT_TheRemovalVerbTakesSeveralFolders()
    {
        var line = LanHelperElevated.BuildRemoveArguments(new[] { @"C:\Games\WoL\", @"D:\Steam\AoE3\bin" });
        Assert.Equal(@"--remove-lan-helper ""C:\Games\WoL"" ""D:\Steam\AoE3\bin""", line);
        Assert.True(LanHelperElevated.TryParseRemove(
            new[] { "--remove-lan-helper", @"C:\Games\WoL", @"D:\Steam\AoE3\bin" }, out var folders));
        Assert.Equal(new[] { @"C:\Games\WoL", @"D:\Steam\AoE3\bin" }, folders);
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_OneBadFolderRefusesTheWholeRemoval()
    {
        Assert.False(LanHelperElevated.TryParseRemove(new[] { "--remove-lan-helper", @"C:\Games\WoL", @"C:\" }, out _));
        Assert.False(LanHelperElevated.TryParseRemove(new[] { "--remove-lan-helper", @"C:\Games\WoL", @"Games\x" }, out _));
        Assert.False(LanHelperElevated.TryParseRemove(new[] { "--remove-lan-helper", @"C:\Games\WoL", @"c:\games\wol" }, out _));
        Assert.False(LanHelperElevated.TryParseRemove(new[] { "--remove-lan-helper" }, out _));
        Assert.False(LanHelperElevated.TryParseRemove(new[] { "--install-lan-helper", @"C:\Games\WoL" }, out _));
        var seventeen = new string[LanHelperElevated.MaxFoldersPerRemoval + 2];
        seventeen[0] = "--remove-lan-helper";
        for (var i = 1; i < seventeen.Length; i++) seventeen[i] = $@"C:\Games\G{i}";
        Assert.False(LanHelperElevated.TryParseRemove(seventeen, out _));
        Assert.Null(LanHelperElevated.BuildRemoveArguments(new[] { @"C:\Games\WoL", @"C:\" }));
        Assert.Null(LanHelperElevated.BuildRemoveArguments(new[] { @"C:\Games\WoL", @"C:\Games\WoL\" }));
        Assert.Null(LanHelperElevated.BuildRemoveArguments(System.Array.Empty<string>()));
        Assert.Equal((int)LanHelperElevated.Exit.BadArguments,
            LanHelperElevated.Run(new[] { "--remove-lan-helper", @"C:\Games\WoL", @"C:\" }));
    }

    [Fact]
    public void BothVerbsAreRecognisedFirstAndOnlyFirst()
    {
        Assert.True(LanHelperElevated.IsInvocation(new[] { "--INSTALL-LAN-HELPER", "x" }));
        Assert.True(LanHelperElevated.IsInvocation(new[] { "--remove-lan-helper", "x" }));
        Assert.False(LanHelperElevated.IsInvocation(new[] { "--minimized", "--install-lan-helper" }));
        Assert.False(LanHelperElevated.IsInvocation(null));
    }
}
