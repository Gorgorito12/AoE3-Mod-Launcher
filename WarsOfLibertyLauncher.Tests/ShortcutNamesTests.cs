using System;
using System.IO;
using System.Linq;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins the NAMES an install's shortcuts get (<see cref="NativeInstallService.ShortcutStemFor"/>,
/// <see cref="NativeInstallService.ShortcutPathsFor"/>) and how the shortcut record is merged
/// after a legacy cleanup (<see cref="NativeInstallService.MergeShortcutRecords"/>).
///
/// <para>"Age of Empires 1: HD Edition.lnk" is not a file name: NTFS wrote it as a stream on an
/// empty file called "Age of Empires 1", the Start Menu folder could not be created, and no error
/// reached anyone. Nothing else in the suite creates shortcuts, so without these a regression to
/// the raw AppName would pass every test.</para>
/// </summary>
public class ShortcutNamesTests
{
    private const string Desktop = @"C:\Users\Player\Desktop";
    private const string Programs = @"C:\Users\Player\AppData\Roaming\Microsoft\Windows\Start Menu\Programs";

    private static ModProfile Aoe1() => new() { Id = "aoe1-hd-edition", DisplayName = "Age of Empires 1: HD Edition" };
    private static ModProfile Wol() => new() { Id = "wol", DisplayName = "Wars of Liberty" };

    [Fact]
    public void THE_ONE_THAT_MATTERS_TheAoE1ShortcutsAreFilesNotStreams()
    {
        var aoe1 = Aoe1();
        var identity = InstallIdentity.Fresh(aoe1, null);

        // The Add/Remove Programs name keeps the colon — it is legal there.
        Assert.Equal("Age of Empires 1: HD Edition", identity.AppName);
        Assert.Equal("Age of Empires 1 HD Edition", NativeInstallService.ShortcutStemFor(identity, aoe1));

        var (desktopLink, startMenuFolder, startMenuLink) =
            NativeInstallService.ShortcutPathsFor(identity, aoe1, Desktop, Programs);
        Assert.Equal(Desktop + @"\Age of Empires 1 HD Edition.lnk", desktopLink);
        Assert.Equal(Programs + @"\Age of Empires 1 HD Edition", startMenuFolder);
        Assert.Equal(Programs + @"\Age of Empires 1 HD Edition\Age of Empires 1 HD Edition.lnk", startMenuLink);
        Assert.False(LegacyShortcuts.IsStreamPath(desktopLink));
        Assert.False(LegacyShortcuts.IsStreamPath(startMenuLink));
    }

    /// <summary>A name that already works is used exactly as written, so every existing install
    /// keeps overwriting its own shortcuts instead of leaving them behind next to new ones.</summary>
    [Theory]
    [InlineData(null, "Wars of Liberty")]
    [InlineData("Wars of Liberty (2)", "Wars of Liberty (2)")]
    [InlineData("WoL v1.", "WoL v1.")]                    // a trailing dot has always worked
    [InlineData("WoL: test copy", "WoL test copy")]       // a colon never did
    [InlineData("A|B", "A B")]
    public void ANameIsCleanedOnlyWhenItCannotWork(string? label, string expected)
    {
        var wol = Wol();
        Assert.Equal(expected, NativeInstallService.ShortcutStemFor(InstallIdentity.Fresh(wol, label), wol));
    }

    /// <summary>A display name that cannot name both the Start Menu folder and the shortcut inside
    /// it is cleaned; it used to leave a Desktop shortcut and an empty Start Menu folder.</summary>
    [Theory]
    [InlineData(" Foo", "Foo")]
    [InlineData("Foo ", "Foo")]
    [InlineData("Mod v1..", "Mod v1")]
    [InlineData("Mod .", "Mod")]
    [InlineData("Bar...", "Bar")]
    public void ANameWhoseStartMenuFolderWouldNotResolveIsCleaned(string displayName, string expected)
    {
        var profile = new ModProfile { Id = "test-mod", DisplayName = displayName };
        Assert.Equal(expected, NativeInstallService.ShortcutStemFor(InstallIdentity.Fresh(profile, null), profile));
    }

    /// <summary>
    /// The install names the shortcuts from the display name as written; every repair names them
    /// from the AppName carried in the manifest, TRIMMED. The two must agree, or the first repair
    /// creates a second set and the first is left behind.
    /// </summary>
    [Theory]
    [InlineData("Wars of Liberty")]
    [InlineData("Age of Empires 1: HD Edition")]
    [InlineData(" Foo")]
    [InlineData("Foo ")]
    [InlineData("Mod v1.")]
    [InlineData("Mod v1..")]
    public void ARepairNamesTheShortcutsAsTheInstallDid(string displayName)
    {
        var profile = new ModProfile { Id = "test-mod", DisplayName = displayName };
        var installed = InstallIdentity.Fresh(profile, null);
        var manifest = new InstallManifest
        {
            ModId = profile.Id, ProductGuid = installed.ProductGuid, AppName = installed.AppName,
        };
        var repaired = InstallIdentity.ForReoverlay(profile, manifest, callerLabel: null);

        Assert.True(repaired.Carried);
        Assert.Equal(NativeInstallService.ShortcutStemFor(installed, profile),
            NativeInstallService.ShortcutStemFor(repaired, profile));
    }

    [Fact]
    public void AStemCleanedToNothingFallsBackToTheModId()
    {
        var aoe1 = Aoe1();
        var identity = InstallIdentity.Fresh(aoe1, null) with { AppName = "::" };
        Assert.Equal("aoe1-hd-edition", NativeInstallService.ShortcutStemFor(identity, aoe1));
    }

    // ------------------------------------------------------------------ whose shortcut it is

    /// <summary>
    /// The legacy heal never overwrites a clean-named shortcut that starts a live program in
    /// ANOTHER folder — a copy carries the primary's name — but one that starts this install's
    /// program, or nothing at all any more, is this install's to rewrite and record. Real
    /// <c>.lnk</c> files under a temp folder.
    /// </summary>
    [Fact]
    public void AShortcutIsReplacedOnlyWhenItIsThisInstallsOrPointsAtNothing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aoe3ml-" + Guid.NewGuid().ToString("N"));
        var install = Path.Combine(dir, "install");
        var other = Path.Combine(dir, "other copy");
        Directory.CreateDirectory(install);
        Directory.CreateDirectory(other);
        try
        {
            var lnk = Path.Combine(dir, "Age of Empires 1 HD Edition.lnk");
            Assert.True(NativeInstallService.MayReplaceShortcut(lnk, install));   // nothing there yet

            var ours = Path.Combine(install, "age3y.exe");
            File.WriteAllText(ours, "not a real game");
            NativeInstallService.CreateShortcutFile(lnk, ours, install, "test");
            if (!File.Exists(lnk)) return;   // no Windows Script Host on this machine
            Assert.True(NativeInstallService.MayReplaceShortcut(lnk, install));   // starts this install

            var theirs = Path.Combine(other, "age3y.exe");
            File.WriteAllText(theirs, "not a real game");
            NativeInstallService.CreateShortcutFile(lnk, theirs, other, "test");
            Assert.False(NativeInstallService.MayReplaceShortcut(lnk, install));  // another install's

            File.Delete(theirs);
            Assert.True(NativeInstallService.MayReplaceShortcut(lnk, install));   // starts nothing
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ------------------------------------------------------------------ the record after a cleanup

    private const string Stream = Desktop + @"\Age of Empires 1: HD Edition.lnk";
    private const string Clean = Desktop + @"\Age of Empires 1 HD Edition.lnk";
    private const string StartMenuClean = Programs + @"\Age of Empires 1 HD Edition\Age of Empires 1 HD Edition.lnk";

    [Fact]
    public void AStreamPathLeavesTheRecordOnceItIsGone()
    {
        var merged = NativeInstallService.MergeShortcutRecords(new[] { Stream }, new[] { Clean, StartMenuClean });
        Assert.Equal(new[] { Clean, StartMenuClean }, merged);
    }

    [Fact]
    public void AStreamPathThatIsStillThereStaysRecorded()
    {
        // The cleanup could not remove it: recorded, the next repair or the uninstall tries again.
        var merged = NativeInstallService.MergeShortcutRecords(new[] { Stream }, new[] { Clean },
            stillThere: new[] { Stream.ToUpperInvariant() });
        Assert.Equal(new[] { Stream, Clean }, merged);
    }

    [Fact]
    public void TheValidRecordsSurviveWhenNothingWasCreated()
    {
        // A missing exe creates no shortcut; the shortcuts that exist must stay recorded, or the
        // uninstall would leave them behind.
        var startMenuOld = Programs + @"\Mod\Mod.lnk";
        var merged = NativeInstallService.MergeShortcutRecords(new[] { Stream, startMenuOld }, Array.Empty<string>());
        Assert.Equal(new[] { startMenuOld }, merged);
    }

    [Fact]
    public void TheRecordHasNoDuplicatesInAnyCase()
    {
        var merged = NativeInstallService.MergeShortcutRecords(
            new[] { Clean, Clean.ToUpperInvariant(), "", "  " },
            new[] { Clean.ToLowerInvariant(), StartMenuClean });

        Assert.Equal(new[] { Clean, StartMenuClean }, merged);
        Assert.Equal(merged.Count, merged.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
