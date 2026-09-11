using System;
using System.IO;
using System.Text.Json;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The few settings the startup auto-update reads and writes without going through
/// <see cref="LauncherConfig"/>, because it runs before MainWindow owns one.
///
/// <para>Two failures are pinned here and both are silent. A write that serialises a whole
/// config from this path would stamp this process's defaults over the user's real file; and a
/// latch that is not a declared property is dropped by the very next <c>Save()</c>, which
/// disables the loop guard without changing a single visible behaviour.</para>
/// </summary>
public class StartupUpdateStateTests
{
    private static string TempConfig(string json)
    {
        var path = Path.Combine(
            Path.GetTempPath(), "aoe3ml-tests", $"cfg-{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
        return path;
    }

    /// <summary>
    /// THE ONE THAT MATTERS. A surgical write must leave every key it was not asked about
    /// exactly as it found it — including keys a NEWER build wrote that this one has never
    /// heard of. Serialising a LauncherConfig here instead would silently reset the user's
    /// installed mods, their language and their sign-in to this process's defaults.
    /// </summary>
    [Fact]
    public void WritingTheLatchTouchesNothingElse()
    {
        var path = TempConfig("""
        {
          "activeModId": "improvement-mod",
          "language": "es",
          "launcherUpdateETag": "W/\"abc\"",
          "aKeyFromANewerBuild": { "nested": [1, 2, 3] }
        }
        """);

        StartupUpdateState.RecordAttempt(path, "v1.0.15", 1);
        StartupUpdateState.CommitInstalled(path, "v1.0.15");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;

        Assert.Equal("improvement-mod", root.GetProperty("activeModId").GetString());
        Assert.Equal("es", root.GetProperty("language").GetString());
        Assert.Equal("W/\"abc\"", root.GetProperty("launcherUpdateETag").GetString());
        Assert.Equal(3, root.GetProperty("aKeyFromANewerBuild").GetProperty("nested").GetArrayLength());

        Assert.Equal("v1.0.15", root.GetProperty("autoUpdateAttemptTag").GetString());
        Assert.Equal(1, root.GetProperty("autoUpdateAttemptCount").GetInt32());
        Assert.Equal("v1.0.15", root.GetProperty("lastInstalledLauncherTag").GetString());
    }

    /// <summary>
    /// THE OTHER ONE. Both latch fields must be declared properties of
    /// <see cref="LauncherConfig"/>: <c>Save()</c> serialises the object, so a field that only
    /// ever existed as raw JSON is dropped from the file by MainWindow's first save — and the
    /// loop guard is disabled on the very next launch, silently.
    /// </summary>
    [Fact]
    public void TheLatchSurvivesAConfigRoundTrip()
    {
        var json = JsonSerializer.Serialize(new LauncherConfig
        {
            AutoUpdateAttemptTag = "v1.0.15",
            AutoUpdateAttemptCount = 2,
        });

        Assert.Contains("autoUpdateAttemptTag", json);

        var back = JsonSerializer.Deserialize<LauncherConfig>(json)!;
        Assert.Equal("v1.0.15", back.AutoUpdateAttemptTag);
        Assert.Equal(2, back.AutoUpdateAttemptCount);
    }

    /// <summary>Values come back as written; a missing file is defaults, not a throw.</summary>
    [Fact]
    public void ReadingWhatWasWritten()
    {
        var path = TempConfig("""
        {
          "checkUpdatesOnStartup": false,
          "lastInstalledLauncherTag": "v1.0.14d",
          "autoUpdateAttemptTag": "v1.0.15",
          "autoUpdateAttemptCount": 2,
          "languageExplicitlyChosen": true,
          "language": "es"
        }
        """);

        var s = StartupUpdateState.Read(path);
        Assert.False(s.CheckUpdatesOnStartup);
        Assert.Equal("v1.0.14d", s.LastInstalledLauncherTag);
        Assert.Equal("v1.0.15", s.AttemptTag);
        Assert.Equal(2, s.AttemptCount);
        Assert.Equal("es", StartupUpdateState.ResolveLanguage(s));
    }

    /// <summary>
    /// A config that cannot be read must not decide anything by accident. The one default that
    /// matters is <c>checkUpdatesOnStartup: true</c> — it mirrors the property's own default,
    /// so a first launch behaves the same whether the file exists yet or not.
    /// </summary>
    [Theory]
    [InlineData("{ }")]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    public void AnUnreadableConfigFallsBackToTheDeclaredDefaults(string body)
    {
        var s = StartupUpdateState.Read(TempConfig(body));
        Assert.True(s.CheckUpdatesOnStartup);
        Assert.Equal("", s.LastInstalledLauncherTag);
        Assert.Equal("", s.AttemptTag);
        Assert.Equal(0, s.AttemptCount);
    }

    /// <summary>A file that is not there at all is the same answer, not an exception.</summary>
    [Fact]
    public void AMissingConfigIsNotAFailure()
    {
        var s = StartupUpdateState.Read(
            Path.Combine(Path.GetTempPath(), $"aoe3ml-missing-{Guid.NewGuid():N}.json"));
        Assert.True(s.CheckUpdatesOnStartup);
    }

    /// <summary>
    /// Writing into a file that does not exist yet creates one rather than losing the latch —
    /// a fresh install auto-updating on its very first launch is a real sequence.
    /// </summary>
    [Fact]
    public void TheLatchCanBeWrittenBeforeAConfigExists()
    {
        var path = Path.Combine(
            Path.GetTempPath(), "aoe3ml-tests", $"cfg-new-{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        StartupUpdateState.RecordAttempt(path, "v1.0.15", 1);

        Assert.Equal("v1.0.15", StartupUpdateState.Read(path).AttemptTag);
    }
}
