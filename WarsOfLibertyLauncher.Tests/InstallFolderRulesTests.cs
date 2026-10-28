using System;
using System.IO;
using System.Linq;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="InstallFolderRules"/> — whether a destination folder can exist at all, decided
/// BEFORE anything is downloaded.
///
/// <para>The install dialog used to rely on <c>Path.GetFullPath</c> throwing, which .NET Core
/// never does for <c>: ? * &lt; &gt; | "</c>. Both paths the maintainer actually tried were
/// accepted, downloaded 301 MB each, and failed creating the folder. The REJECTION cases are the
/// point; the accepted ones are here to prove the rule is not simply "refuse everything".</para>
/// </summary>
public class InstallFolderRulesTests
{
    /// <summary>The drives a test pretends exist. C: is on every machine that runs the suite.</summary>
    private static readonly string[] Drives = { @"C:\", @"D:\" };

    private static InstallPathProblem? ProblemOf(string? text) =>
        InstallFolderRules.Validate(text, Drives, systemFolders: new[] { @"C:\Windows" }).Problem;

    [Theory]
    [InlineData(@"C:\Program Files (x86)\Steam\steamapps\common\Age Of Empires 3\Age of Empires 1: HD Edition",
        "Age of Empires 1: HD Edition")]
    [InlineData(@"C:\Program Files (x86)\Steam\steamapps\common\Age Of Empires 3\Age-of-Empires-1:-HD-Edition",
        "Age-of-Empires-1:-HD-Edition")]
    public void THE_ONE_THAT_MATTERS_RefusesBothPathsTheMaintainerTyped(string path, string folder)
    {
        var problem = ProblemOf(path);

        Assert.NotNull(problem);
        Assert.Equal(InstallPathProblemKind.InvalidCharacter, problem!.Kind);
        Assert.Equal(":", problem.Detail);       // the message names the character…
        Assert.Equal(folder, problem.Segment);   // …and the folder holding it
    }

    [Theory]
    [InlineData("C:")]
    [InlineData(@"C:Games\Mod")]
    [InlineData(@"Games\Mod")]
    [InlineData(@"\Games\Mod")]
    [InlineData("Age of Empires 1 HD Edition")]   // the old relative fallback
    public void RefusesAPathThatIsNotWhole(string path)
        => Assert.Equal(InstallPathProblemKind.NotFullyQualified, ProblemOf(path)!.Kind);

    [Theory]
    [InlineData(@"\\?\C:\Games\Mod")]
    [InlineData(@"\\.\C:\Games\Mod")]
    [InlineData(@"\??\C:\Games\Mod")]
    // A separator too many in front of a UNC path: GetFullPath keeps it, Windows cannot open it,
    // and the root slice used to skip part of the server name.
    [InlineData(@"\\\nas\Games\Mod")]
    [InlineData(@"\\\\nas\\Games\\Mod")]
    [InlineData("///nas/Games/Mod")]
    public void RefusesDevicePaths(string path)
        => Assert.Equal(InstallPathProblemKind.UnsupportedForm, ProblemOf(path)!.Kind);

    /// <summary>The server and the share are names too: the segment loop starts after them, so
    /// they are checked on their own.</summary>
    [Theory]
    [InlineData(@"\\localhost\C$:x\Windows", "C$:x", ":")]
    [InlineData(@"\\local?host\C$\Windows", "local?host", "?")]
    public void RefusesAForbiddenCharacterInAUncServerOrShare(string path, string part, string character)
    {
        var problem = ProblemOf(path);

        Assert.Equal(InstallPathProblemKind.InvalidCharacter, problem!.Kind);
        Assert.Equal(part, problem.Segment);
        Assert.Equal(character, problem.Detail);
    }

    /// <summary>
    /// A drive root is refused even when an in-place overlay's game sits there: Uninstall refuses
    /// a drive root outright and the manifest cannot list one, so the install could be made and
    /// never removed. The suggestion arrives as the whole root ("E:\"), never the drive-RELATIVE
    /// "E:", so the player is told the real reason rather than asked for a whole path.
    /// </summary>
    [Fact]
    public void AGameAtADriveRootIsStillADriveRoot()
    {
        string[] drives = { @"C:\", @"E:\" };
        var none = Array.Empty<string>();

        var check = InstallFolderRules.Validate(@"E:\", drives, none);
        Assert.Equal(InstallPathProblemKind.DriveRoot, check.Problem!.Kind);
        Assert.Equal(@"E:\", check.Problem.Detail);
        Assert.Equal(InstallPathProblemKind.NotFullyQualified,
            InstallFolderRules.Validate("E:", drives, none).Problem!.Kind);
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData("C:/")]
    [InlineData(@"D:\")]
    [InlineData(@"\\server\share")]
    [InlineData(@"\\server\share\")]
    [InlineData(@"C:\Games\..")]
    public void RefusesADriveRoot(string path)
        => Assert.Equal(InstallPathProblemKind.DriveRoot, ProblemOf(path)!.Kind);

    [Theory]
    [InlineData('<')]
    [InlineData('>')]
    [InlineData(':')]
    [InlineData('"')]
    [InlineData('|')]
    [InlineData('?')]
    [InlineData('*')]
    [InlineData('\t')]
    public void RefusesEachForbiddenCharacter(char c)
    {
        var problem = ProblemOf($@"C:\Games\Mod{c}Name");

        Assert.Equal(InstallPathProblemKind.InvalidCharacter, problem!.Kind);
        Assert.Equal(SafeFolderName.DescribeChar(c), problem.Detail);
    }

    [Theory]
    [InlineData(@"C:\Games\CON\Mod", "CON")]
    [InlineData(@"C:\Games\nul.txt", "nul.txt")]
    [InlineData(@"C:\Games\Mod\LPT1", "LPT1")]
    public void RefusesReservedNames(string path, string segment)
    {
        var problem = ProblemOf(path);
        Assert.Equal(InstallPathProblemKind.ReservedName, problem!.Kind);
        Assert.Equal(segment, problem.Segment);
    }

    /// <summary>Win32 removes a middle folder's trailing dot and every trailing dot or space at the
    /// end of a path, and keeps a middle folder's trailing space — either way the folder on disk
    /// is not the one typed, which is why the rules look at the RAW segments.</summary>
    [Theory]
    [InlineData(@"D:\Games \Mod", "Games ")]
    [InlineData(@"D:\Games.\Mod", "Games.")]
    [InlineData(@"D:\Mod.", "Mod.")]
    public void RefusesATrailingDotOrSpaceInAnySegment(string path, string segment)
    {
        var problem = ProblemOf(path);
        Assert.Equal(InstallPathProblemKind.TrailingDotOrSpace, problem!.Kind);
        Assert.Equal(segment, problem.Segment);
    }

    [Fact]
    public void RefusesASegmentLongerThanNtfsAllows()
        => Assert.Equal(InstallPathProblemKind.SegmentTooLong,
            ProblemOf(@"C:\Games\" + new string('m', 256))!.Kind);

    [Theory]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\Windows\System32\Mod")]
    public void RefusesTheWindowsFolder(string path)
        => Assert.Equal(InstallPathProblemKind.SystemFolder, ProblemOf(path)!.Kind);

    [Fact]
    public void AFolderMerelyNamedLikeWindowsIsFine()
        => Assert.Null(ProblemOf(@"C:\WindowsGames\Mod"));

    [Fact]
    public void RefusesADriveThatIsNotThere()
    {
        var problem = ProblemOf(@"Q:\Games\Mod");
        Assert.Equal(InstallPathProblemKind.DriveMissing, problem!.Kind);
        Assert.Equal("Q:", problem.Detail);
    }

    [Fact]
    public void WithNoDriveListTheDriveIsNotChecked()
        => Assert.Null(InstallFolderRules.Validate(@"Q:\Games\Mod", existingDriveRoots: null,
            systemFolders: Array.Empty<string>()).Problem);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"\"")]
    public void AnEmptyBoxIsSaidToBeEmpty(string text)
        => Assert.Equal(InstallPathProblemKind.Empty, ProblemOf(text)!.Kind);

    [Theory]
    [InlineData("\"C:\\Games\\Age of Empires 1 HD Edition\"", @"C:\Games\Age of Empires 1 HD Edition")]  // Explorer's "Copy as path"
    [InlineData("  C:/Games/Mod/  ", @"C:\Games\Mod")]
    [InlineData(@"C:\Games\Mod\", @"C:\Games\Mod")]
    [InlineData(@"C:\Games\\Mod", @"C:\Games\Mod")]
    [InlineData(@"C:\Games\Other\..\Mod", @"C:\Games\Mod")]
    [InlineData(@"C:\Games\ Mods\X", @"C:\Games\ Mods\X")]  // a leading space is legal
    [InlineData(@"\\server\share\Games\Mod", @"\\server\share\Games\Mod")]
    [InlineData(@"\\server\\share\Games\Mod", @"\\server\share\Games\Mod")]   // one too many INSIDE is harmless
    public void AcceptsAndCanonicalisesWhatAPlayerPastes(string text, string expected)
    {
        var check = InstallFolderRules.Validate(text, Drives, Array.Empty<string>());

        Assert.Null(check.Problem);
        Assert.Equal(expected, check.FullPath);
    }

    [Fact]
    public void ARootKeepsItsSeparatorWhenNormalised()
    {
        // A bare "D:" is drive-RELATIVE when used as a folder on its own: it resolves to that
        // drive's current folder, not to its root.
        Assert.Equal(@"D:\", InstallFolderRules.Normalize(@"D:\"));
        Assert.Equal(@"D:\", InstallFolderRules.Normalize("D:/"));
    }

    /// <summary>The folder every catalog mod is suggested today validates — the rules must not
    /// refuse a single real install.</summary>
    [Theory]
    [InlineData(@"C:\Program Files (x86)\Steam\steamapps\common\Age Of Empires 3\Wars of Liberty (5)")]
    [InlineData(@"C:\Program Files (x86)\Steam\steamapps\common\Age Of Empires 3\Age of Dynasties (Remake)")]
    [InlineData(@"C:\Program Files (x86)\Steam\steamapps\common\Age Of Empires 3\King's Return")]
    [InlineData(@"C:\Program Files (x86)\Steam\steamapps\common\Age Of Empires 3\Age of Empires 1 HD Edition")]
    [InlineData(@"C:\Program Files (x86)\Knights and Barbarians")]
    [InlineData(@"C:\Program Files (x86)\Steam\steamapps\common\Age Of Empires 3\bin")]   // an in-place overlay
    public void EverySuggestedCatalogFolderValidates(string path)
        => Assert.Null(ProblemOf(path));

    // ------------------------------------------------------------------ disk reads

    private static string NewTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "aoe3ml-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    [Fact]
    public void FindsAFileStandingWhereTheDestinationGoes()
    {
        var root = NewTempRoot();
        try
        {
            var file = Path.Combine(root, "Mod");
            File.WriteAllText(file, "not a folder");

            Assert.Equal(file, InstallFolderRules.FindFileInTheWay(file));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void FindsAFileStandingWhereAFolderAboveTheDestinationGoes()
    {
        var root = NewTempRoot();
        try
        {
            var file = Path.Combine(root, "Games");
            File.WriteAllText(file, "not a folder");

            Assert.Equal(file, InstallFolderRules.FindFileInTheWay(Path.Combine(file, "New", "Mod")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void NothingIsInTheWayOfAFolderThatDoesNotExistYet()
    {
        var root = NewTempRoot();
        try
        {
            Assert.Null(InstallFolderRules.FindFileInTheWay(Path.Combine(root, "Games", "Mod")));
            Assert.Null(InstallFolderRules.FindFileInTheWay(root));   // an existing folder
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void TheNearestExistingAncestorIsTheFolderWhosePermissionsDecide()
    {
        var root = NewTempRoot();
        try
        {
            Assert.Equal(root, InstallFolderRules.NearestExistingAncestor(Path.Combine(root, "New", "Mod")));
            Assert.Equal(root, InstallFolderRules.NearestExistingAncestor(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void AMissingDriveHasNoExistingAncestor()
    {
        var present = Environment.GetLogicalDrives().Select(d => char.ToUpperInvariant(d[0])).ToHashSet();
        var missing = Enumerable.Range('D', 'Z' - 'D' + 1).Select(i => (char)i).FirstOrDefault(c => !present.Contains(c));
        if (missing == default) return;   // every letter is taken on this machine

        Assert.Null(InstallFolderRules.NearestExistingAncestor($@"{missing}:\Games\Mod"));
    }
}
