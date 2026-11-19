using System;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The two-account rig's pieces only exist inside the harness. What matters here is every way
/// they must stay off: a published launcher, another server, a recording outside the run.
/// </summary>
public class HarnessFakeGameTests
{
    private const string Docs = @"C:\Temp\testbed\documents-a";
    private const string Json = "{\"seconds\":190,\"recording\":\"C:\\\\Temp\\\\a.age3Yrec\"}";

    [Fact]
    public void ASpecIsReadInsideTheHarness()
    {
        var spec = HarnessFakeGame.SpecFor(true, Docs, Json);
        Assert.NotNull(spec);
        Assert.Equal(190, spec!.Seconds);
        Assert.Equal(@"C:\Temp\a.age3Yrec", spec.Recording);
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_OutsideTheHarnessThereIsNoFakeGame()
        => Assert.Null(HarnessFakeGame.SpecFor(false, Docs, Json));

    [Fact]
    public void ARunWithoutItsOwnDocumentsGetsNone()
        => Assert.Null(HarnessFakeGame.SpecFor(true, null, Json));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"seconds\":0,\"recording\":\"x\"}")]
    [InlineData("{\"seconds\":190,\"recording\":\"\"}")]
    [InlineData("{\"seconds\":190}")]
    public void AnUnusableSpecIsNone(string? json)
        => Assert.Null(HarnessFakeGame.SpecFor(true, Docs, json));

    [Fact]
    public void TheRecordingLandsInTheModsSavegameFolder()
    {
        var path = HarnessFakeGame.DropPath(Docs, Docs + @"\My Games\Wars of Liberty",
            new DateTime(2026, 11, 18, 21, 5, 9, DateTimeKind.Utc));
        Assert.Equal(Docs + @"\My Games\Wars of Liberty\Savegame\Record Game 20261118-210509.age3Yrec", path);
    }

    [Theory]
    [InlineData(@"C:\Users\Someone\Documents\My Games\Wars of Liberty")]
    [InlineData(@"C:\Temp\testbed\documents-ab\My Games\Wars of Liberty")]
    [InlineData(@"C:\Temp\testbed\documents-a\..\documents-b\My Games\Wars of Liberty")]
    [InlineData(null)]
    public void THE_ONE_THAT_MATTERS_NothingIsWrittenOutsideTheRun(string? folder)
        => Assert.Null(HarnessFakeGame.DropPath(Docs, folder, DateTime.UtcNow));

    [Theory]
    [InlineData("http://127.0.0.1:8799", "http://127.0.0.1:8799")]
    [InlineData("http://localhost:8799/", "http://localhost:8799")]
    [InlineData(" http://127.0.0.1:8799 ", "http://127.0.0.1:8799")]
    public void TheRigsServerIsUsedInsideTheHarness(string value, string expected)
        => Assert.Equal(expected, HarnessMode.LobbyUrlFor(true, value));

    [Fact]
    public void THE_ONE_THAT_MATTERS_APublishedLauncherNeverChangesServer()
        => Assert.Null(HarnessMode.LobbyUrlFor(false, "http://127.0.0.1:8799"));

    [Theory]
    [InlineData("https://127.0.0.1:8799")]
    [InlineData("http://wol-lobby.duckdns.org")]
    [InlineData("http://192.168.1.10:8799")]
    [InlineData("http://user:pass@127.0.0.1:8799")]
    [InlineData("http://127.0.0.1@evil.example")]
    [InlineData("file:///C:/x")]
    [InlineData("127.0.0.1:8799")]
    [InlineData("")]
    [InlineData(null)]
    public void OnlyPlainHttpToThisMachine(string? value)
        => Assert.Null(HarnessMode.LobbyUrlFor(true, value));
}
