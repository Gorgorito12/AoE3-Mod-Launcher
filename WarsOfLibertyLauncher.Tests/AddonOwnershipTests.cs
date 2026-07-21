using System;
using System.Collections.Generic;
using System.IO;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins the record of which files each addon owns.
///
/// It lives outside the install manifest because addons apply to the player's own
/// unmodded Age of Empires III too, and that install has no manifest — requiring
/// one made every addon fail there. Writing a manifest into the real game folder
/// to work around it would be worse: AoE3Detector treats install-manifest.json as
/// "this is a mod, not a clone source", so the launcher would silently stop
/// offering the player's own game as the base for installing new mods.
/// </summary>
public class AddonOwnershipTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private string NewTempDir()
    {
        var dir = Directory.CreateTempSubdirectory("addon-own-").FullName;
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

    [Fact]
    public void RoundTrips()
    {
        var install = NewTempDir();
        var owned = new Dictionary<string, List<string>>
        {
            ["heaven-1932"] = new() { "startup/gamey.con" },
        };

        AddonOwnership.Save(install, owned);
        var loaded = AddonOwnership.Load(install);

        Assert.Equal(new[] { "startup/gamey.con" }, loaded["heaven-1932"]);
    }

    /// <summary>
    /// A stock-game install has no manifest at all. Reading has to work anyway,
    /// which is the whole reason this record exists.
    /// </summary>
    [Fact]
    public void EmptyInstall_ReadsAsNothingOwned()
        => Assert.Empty(AddonOwnership.Load(NewTempDir()));

    /// <summary>
    /// THE case that protects existing users. Addons enabled by an older build
    /// recorded their files only in the install manifest; without absorbing those,
    /// they would show as enabled with no idea which files to restore — permanently
    /// un-disableable.
    /// </summary>
    [Fact]
    public void MigratesFromTheInstallManifest_WhenNoRecordExists()
    {
        var install = NewTempDir();
        new InstallManifest
        {
            ModId = "wol",
            InstallPath = install,
            AddonFiles = new Dictionary<string, List<string>>
            {
                ["heaven-3730"] = new() { "art/effects/smoke.particle", "data/playercolors.xml" },
            },
        }.Save();

        var loaded = AddonOwnership.Load(install);

        Assert.Equal(2, loaded["heaven-3730"].Count);
        Assert.Contains("data/playercolors.xml", loaded["heaven-3730"]);
        // Migrated once and persisted, so the manifest stops being consulted.
        Assert.True(File.Exists(AddonOwnership.PathFor(install)));
    }

    /// <summary>
    /// Once a record exists it wins — a stale manifest entry must not resurrect an
    /// addon the player disabled.
    /// </summary>
    [Fact]
    public void ExistingRecord_TakesPrecedenceOverTheManifest()
    {
        var install = NewTempDir();
        new InstallManifest
        {
            ModId = "wol",
            InstallPath = install,
            AddonFiles = new Dictionary<string, List<string>> { ["old"] = new() { "art/x.ddt" } },
        }.Save();
        AddonOwnership.Save(install, new Dictionary<string, List<string>>());

        Assert.Empty(AddonOwnership.Load(install));
    }

    /// <summary>
    /// A damaged record must not make an install unmanageable — falling back to
    /// the manifest recovers the same data one build older.
    /// </summary>
    [Fact]
    public void CorruptRecord_FallsBackInsteadOfThrowing()
    {
        var install = NewTempDir();
        var path = AddonOwnership.PathFor(install);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");

        var loaded = AddonOwnership.Load(install);

        Assert.NotNull(loaded);
        Assert.Empty(loaded);
    }

    [Fact]
    public void IdsAreCaseInsensitive()
    {
        var install = NewTempDir();
        AddonOwnership.Save(install, new Dictionary<string, List<string>>
        {
            ["Heaven-1932"] = new() { "startup/gamey.con" },
        });

        Assert.True(AddonOwnership.Load(install).ContainsKey("heaven-1932"));
    }

    /// <summary>
    /// The record must never be an install-manifest.json inside the game folder —
    /// that file is how AoE3Detector rules a folder out as a clone source.
    /// </summary>
    [Fact]
    public void RecordIsNotAnInstallManifest()
    {
        var install = NewTempDir();
        AddonOwnership.Save(install, new Dictionary<string, List<string>>
        {
            ["heaven-1932"] = new() { "startup/gamey.con" },
        });

        Assert.False(File.Exists(Path.Combine(install, "install-manifest.json")));
        Assert.True(File.Exists(AddonOwnership.PathFor(install)));
    }
}
