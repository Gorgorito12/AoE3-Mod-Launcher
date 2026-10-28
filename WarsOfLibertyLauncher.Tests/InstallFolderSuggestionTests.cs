using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="InstallFolderSuggestion"/> — the folder the install dialog opens with, which
/// used to be built inline in <c>MainWindow.InstallAsync</c> straight from the display name.
///
/// <para>Two things went wrong there. A name with a colon produced a folder Windows cannot create
/// (found after a 301 MB download), and two fallbacks returned the bare display name — a RELATIVE
/// path, resolved against whatever the process's current folder was.</para>
///
/// <para>The case that matters is recognising an install that ALREADY EXISTS: a saved path is
/// trusted by its last folder, and that comparison now has to accept the raw name AND the cleaned
/// one, or an install made before the cleaning rule would stop being suggested.</para>
/// </summary>
public class InstallFolderSuggestionTests
{
    private const string Aoe3 = @"C:\Program Files (x86)\Steam\steamapps\common\Age Of Empires 3";
    private static readonly string[] Drives = { @"C:\", @"D:\" };

    private static ModProfile Mod(string name, string defaultFolder = "",
        ModInstallType type = ModInstallType.IsolatedFolder, string id = "test-mod")
        => new() { Id = id, DisplayName = name, DefaultInstallFolder = defaultFolder, InstallType = type };

    private static string Suggest(ModProfile profile, string? aoe3 = Aoe3, string? last = null,
        bool addNewSlot = false, string? inPlaceRoot = null, IReadOnlyCollection<string>? registered = null,
        Func<string, bool>? exists = null)
        => InstallFolderSuggestion.Suggest(
            new SuggestionInputs(profile, addNewSlot, aoe3, inPlaceRoot, last,
                registered ?? Array.Empty<string>(), @"C:\Program Files (x86)"),
            exists ?? (_ => false),
            Drives);

    // ------------------------------------------------------------------ trusting a saved path

    [Fact]
    public void THE_ONE_THAT_MATTERS_AnExistingInstallUnderTheRawNameIsStillTrusted()
    {
        // The ordinary case, unchanged: WoL's own folder.
        Assert.True(InstallFolderSuggestion.IsTrustedSavedPath(
            Aoe3 + @"\Wars of Liberty", Mod("Wars of Liberty")));

        // A raw name the cleaning rule now changes is still recognised as this mod's leaf.
        Assert.True(InstallFolderSuggestion.IsTrustedSavedPath(
            @"C:\Games\Foo: Bar", Mod("Foo: Bar")));

        // Windows drops a trailing dot, so a "Mod v1." install was recorded under the raw name
        // while the folder on disk is "Mod v1". Still recognised, and still suggested — as the
        // folder it really is, since the dialog would refuse the trailing dot if typed.
        var trailingDot = Mod("Mod v1.");
        Assert.True(InstallFolderSuggestion.IsTrustedSavedPath(@"C:\Games\Mod v1.", trailingDot));
        Assert.Equal(@"C:\Games\Mod v1", Suggest(trailingDot, last: @"C:\Games\Mod v1."));
    }

    [Fact]
    public void AnInstallUnderTheCleanNameIsTrustedAndSuggested()
    {
        var aoe1 = Mod("Age of Empires 1: HD Edition", @"C:\Program Files (x86)\Age of Empires 1 HD Edition",
            id: "aoe1-hd-edition");
        var saved = Aoe3 + @"\Age of Empires 1 HD Edition";

        Assert.True(InstallFolderSuggestion.IsTrustedSavedPath(saved, aoe1));
        Assert.Equal(saved, Suggest(aoe1, last: saved));
    }

    [Fact]
    public void TheDefaultFolderLeafIsTrusted()
    {
        // Knights and Barbarians-Remastered lives in "Knights and Barbarians".
        var knb = Mod("Knights and Barbarians-Remastered", @"C:\Program Files (x86)\Knights and Barbarians");
        Assert.True(InstallFolderSuggestion.IsTrustedSavedPath(Aoe3 + @"\Knights and Barbarians", knb));
    }

    [Fact]
    public void TheVanillaBinLeakIsNotTrusted()
    {
        // An earlier detection that fell through to the AoE3 candidates saved "…\bin": suggesting
        // it for a clone would install over the player's own game.
        var wol = Mod("Wars of Liberty");
        Assert.False(InstallFolderSuggestion.IsTrustedSavedPath(Aoe3 + @"\bin", wol));
        Assert.Equal(Aoe3 + @"\Wars of Liberty", Suggest(wol, last: Aoe3 + @"\bin"));
    }

    [Fact]
    public void ASavedPathThatNoLongerValidatesIsNotSuggested()
    {
        var wol = Mod("Wars of Liberty");
        // Trusted by its leaf, but on a drive this PC does not have.
        Assert.Equal(Aoe3 + @"\Wars of Liberty", Suggest(wol, last: @"Q:\Games\Wars of Liberty"));
    }

    // ------------------------------------------------------------------ building a suggestion

    [Fact]
    public void SuggestsTheCleanNameInsideAoE3()
    {
        var aoe1 = Mod("Age of Empires 1: HD Edition", id: "aoe1-hd-edition");

        var suggested = Suggest(aoe1);

        Assert.Equal(Aoe3 + @"\Age of Empires 1 HD Edition", suggested);
        Assert.Null(InstallFolderRules.Validate(suggested, Drives).Problem);
    }

    [Theory]
    [InlineData("")]                                                   // no default folder
    [InlineData("Age of Empires 1 HD Edition")]                       // a relative one
    [InlineData(@"C:\Program Files (x86)\Age of Empires 1: HD Edition")]  // an invalid one
    public void NeverSuggestsARelativeOrInvalidFolder(string defaultFolder)
    {
        var aoe1 = Mod("Age of Empires 1: HD Edition", defaultFolder, id: "aoe1-hd-edition");

        foreach (var suggested in new[]
                 {
                     Suggest(aoe1, aoe3: null),
                     Suggest(aoe1, aoe3: null, addNewSlot: true),
                     Suggest(Mod("Age of Empires 1: HD Edition", defaultFolder, ModInstallType.InPlaceOverlay), aoe3: null),
                 })
        {
            Assert.True(Path.IsPathFullyQualified(suggested), suggested);
            Assert.Null(InstallFolderRules.Validate(suggested, Drives).Problem);
            Assert.Equal(@"C:\Program Files (x86)\Age of Empires 1 HD Edition", suggested);
        }
    }

    [Fact]
    public void AValidDefaultFolderIsUsedWhenAoE3IsNotDetected()
    {
        var knb = Mod("Knights and Barbarians-Remastered", @"D:\Games\Knights and Barbarians");
        Assert.Equal(@"D:\Games\Knights and Barbarians", Suggest(knb, aoe3: null));
    }

    [Fact]
    public void ACopyGetsTheNextFreeNumber()
    {
        var wol = Mod("Wars of Liberty");
        var registered = new[] { Aoe3 + @"\Wars of Liberty", Aoe3 + @"\Wars of Liberty (2)" };
        var onDisk = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Aoe3 + @"\Wars of Liberty (3)" };

        Assert.Equal(Aoe3 + @"\Wars of Liberty (4)",
            Suggest(wol, addNewSlot: true, registered: registered, exists: onDisk.Contains));
    }

    [Fact]
    public void ACopyOfAColonNamedModIsNumberedUnderTheCleanName()
    {
        var aoe1 = Mod("Age of Empires 1: HD Edition", id: "aoe1-hd-edition");
        var onDisk = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Aoe3 + @"\Age of Empires 1 HD Edition" };

        Assert.Equal(Aoe3 + @"\Age of Empires 1 HD Edition (2)",
            Suggest(aoe1, addNewSlot: true, exists: onDisk.Contains));
    }

    [Fact]
    public void AnInPlaceOverlayStillPrefersTheGameFolder()
    {
        var overlay = Mod("Napoleonic Era", type: ModInstallType.InPlaceOverlay);
        Assert.Equal(Aoe3 + @"\bin", Suggest(overlay, inPlaceRoot: Aoe3 + @"\bin"));
        Assert.Equal(Aoe3, Suggest(overlay, inPlaceRoot: null));
    }

    [Theory]
    [InlineData("E:")]       // what detection used to hand over for a game at the root of a drive
    [InlineData(@"E:\")]
    public void AnInPlaceOverlayIntoAGameAtADriveRootGetsTheWholeRoot(string inPlaceRoot)
    {
        // The bare "E:" is drive-RELATIVE: it would install wherever E:'s current folder is.
        var overlay = Mod("Napoleonic Era", type: ModInstallType.InPlaceOverlay);
        Assert.Equal(@"E:\", Suggest(overlay, inPlaceRoot: inPlaceRoot));
        Assert.Equal(@"E:\", Suggest(overlay, aoe3: inPlaceRoot, inPlaceRoot: null));
    }

    // ------------------------------------------------------------------ the Browse button

    [Theory]
    [InlineData(@"D:\Games", @"D:\Games\Age of Empires 1 HD Edition")]
    [InlineData(@"D:\Games\", @"D:\Games\Age of Empires 1 HD Edition")]
    [InlineData(@"D:\", @"D:\Age of Empires 1 HD Edition")]                      // a picked drive root
    [InlineData(@"D:\Games\Age of Empires 1 HD Edition", @"D:\Games\Age of Empires 1 HD Edition")]
    [InlineData(@"D:\Games\age of empires 1 hd edition", @"D:\Games\age of empires 1 hd edition")]
    public void BrowseAppendsTheCleanNameUnlessThePickIsAlreadyTheModsFolder(string picked, string expected)
    {
        var aoe1 = Mod("Age of Empires 1: HD Edition", id: "aoe1-hd-edition");
        Assert.Equal(expected, InstallFolderSuggestion.WithModFolder(
            picked, SafeFolderName.ForProfile(aoe1), SafeFolderName.KnownLeaves(aoe1)));
    }

    [Fact]
    public void BrowseNoLongerTreatsANameThatMerelyEndsTheSameAsTheModsFolder()
    {
        // The old test was EndsWith: "MyWars of Liberty" counted as the mod's folder.
        var wol = Mod("Wars of Liberty");
        Assert.Equal(@"D:\MyWars of Liberty\Wars of Liberty", InstallFolderSuggestion.WithModFolder(
            @"D:\MyWars of Liberty", "Wars of Liberty", SafeFolderName.KnownLeaves(wol)));
    }
}
