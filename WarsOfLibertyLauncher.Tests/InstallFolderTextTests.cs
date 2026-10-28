using System;
using System.Linq;
using System.Text.RegularExpressions;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins the sentences behind <see cref="InstallFolderProblemText"/>: every way a destination can
/// be refused has a sentence in both languages that takes exactly the arguments the code passes.
///
/// <para>A missing key would print as the key itself, and a placeholder count that disagrees with
/// the arguments throws a <c>FormatException</c> on the UI thread — in the one dialog whose job is
/// to say what is wrong. Reads each language explicitly (<c>Strings.GetIn</c>), so it never races
/// a test that switches the current language.</para>
/// </summary>
public class InstallFolderTextTests
{
    private static readonly string[] Languages = { Strings.LangEn, Strings.LangEs };

    private static InstallPathProblem Sample(InstallPathProblemKind kind) => kind switch
    {
        InstallPathProblemKind.InvalidCharacter => new(kind, "Age of Empires 1: HD Edition", ":"),
        InstallPathProblemKind.ReservedName => new(kind, "CON"),
        InstallPathProblemKind.TrailingDotOrSpace => new(kind, "Mod."),
        InstallPathProblemKind.SegmentTooLong => new(kind, new string('m', 300)),
        InstallPathProblemKind.DriveRoot => new(kind, "D:", @"D:\"),
        InstallPathProblemKind.DriveMissing => new(kind, @"Q:\Games", "Q:"),
        InstallPathProblemKind.FileInTheWay => new(kind, Detail: @"C:\Games\Mod"),
        _ => new(kind),
    };

    private static int[] PlaceholdersOf(string template) =>
        Regex.Matches(template, @"\{(\d+)\}").Select(m => int.Parse(m.Groups[1].Value)).Distinct()
            .OrderBy(i => i).ToArray();

    [Fact]
    public void EveryProblemHasASentenceInBothLanguagesThatTakesTheArgumentsTheCodePasses()
    {
        foreach (var kind in Enum.GetValues<InstallPathProblemKind>())
        {
            var (key, args) = InstallFolderProblemText.KeyAndArgs(Sample(kind), "Age of Empires 1 HD Edition");
            // A key with only an English entry passes every check below (GetIn falls back to
            // English), and a Spanish player would read English.
            Assert.NotEqual(Strings.GetIn(Strings.LangEn, key), Strings.GetIn(Strings.LangEs, key));
            foreach (var lang in Languages)
            {
                var template = Strings.GetIn(lang, key);
                Assert.NotEqual(key, template);   // a missing key prints as itself
                Assert.Equal(Enumerable.Range(0, args.Length).ToArray(), PlaceholdersOf(template));

                var sentence = Strings.FormatIn(lang, key, args);
                foreach (var arg in args)
                    Assert.Contains(arg.ToString()!, sentence);
            }
        }
    }

    [Fact]
    public void TheBadCharacterSentenceNamesTheCharacterAndTheFolder()
    {
        var (key, args) = InstallFolderProblemText.KeyAndArgs(
            new InstallPathProblem(InstallPathProblemKind.InvalidCharacter, "Age of Empires 1: HD Edition", ":"),
            "Age of Empires 1 HD Edition");

        foreach (var lang in Languages)
        {
            var sentence = Strings.FormatIn(lang, key, args);
            Assert.Contains(lang == Strings.LangEn ? "“:”" : "«:»", sentence);
            Assert.Contains("Age of Empires 1: HD Edition", sentence);
        }
    }

    /// <summary>For an in-place overlay a drive root is where the GAME sits, not a folder the player
    /// picked, so "the mod needs a folder of its own" would send them looking for the wrong fix.</summary>
    [Fact]
    public void ADriveRootRefusedForAnOverlaySaysWhyAndNamesTheRoot()
    {
        var root = new InstallPathProblem(InstallPathProblemKind.DriveRoot, "E:", @"E:\");

        var (overlayKey, overlayArgs) = InstallFolderProblemText.KeyAndArgs(root, "Mod", inPlaceOverlay: true);
        Assert.Equal("WarnPathDriveRootOverlay", overlayKey);
        Assert.Equal(@"E:\", overlayArgs.Single());
        Assert.Equal("WarnPathDriveRoot", InstallFolderProblemText.KeyAndArgs(root, "Mod").Key);

        Assert.NotEqual(Strings.GetIn(Strings.LangEn, overlayKey), Strings.GetIn(Strings.LangEs, overlayKey));
        foreach (var lang in Languages)
        {
            var template = Strings.GetIn(lang, overlayKey);
            Assert.NotEqual(overlayKey, template);
            Assert.Equal(new[] { 0 }, PlaceholdersOf(template));
            Assert.Contains(@"E:\", Strings.FormatIn(lang, overlayKey, overlayArgs));
        }
    }

    [Fact]
    public void ThePathExampleIsAFullPathBuiltFromTheCleanName()
    {
        var (key, args) = InstallFolderProblemText.KeyAndArgs(
            new InstallPathProblem(InstallPathProblemKind.NotFullyQualified), "Age of Empires 1 HD Edition");

        Assert.Equal("WarnPathNotFullPath", key);
        Assert.Equal(@"D:\Age of Empires 1 HD Edition", args.Single());
    }

    [Theory]
    [InlineData("StatusInstallFolderRefused", 1)]
    [InlineData("StatusInstallSuccessVerified", 2)]   // {0} items, {1} the mod — it said "Wars of Liberty" for every mod
    public void TheInstallStatusLinesTakeTheirArguments(string key, int count)
    {
        foreach (var lang in Languages)
            Assert.Equal(Enumerable.Range(0, count).ToArray(), PlaceholdersOf(Strings.GetIn(lang, key)));
    }

    [Fact]
    public void TheSuccessLineNamesTheModThatWasInstalled()
    {
        foreach (var lang in Languages)
        {
            var line = Strings.FormatIn(lang, "StatusInstallSuccessVerified", 4919, "Age of Empires 1: HD Edition");
            Assert.Contains("Age of Empires 1: HD Edition", line);
            Assert.DoesNotContain("Wars of Liberty", line);
        }
    }

    [Fact]
    public void ALongNameIsShortenedWithoutSplittingACharacter()
    {
        // An emoji is two chars; cutting between them would print a broken character.
        var name = new string('a', 39) + "\U0001F600" + new string('b', 300);

        var shortened = InstallFolderProblemText.Shorten(name);

        Assert.Equal(new string('a', 39) + "…", shortened);
        Assert.Equal(new string('m', 40) + "…", InstallFolderProblemText.Shorten(new string('m', 300)));
        Assert.Equal("Short name", InstallFolderProblemText.Shorten("Short name"));
    }

    /// <summary>es-419 tuteo, never voseo: the forms a Rioplatense draft would produce. Covers
    /// every key a refusal can print, not a hand-kept list.</summary>
    [Fact]
    public void TheSpanishIsTuteo()
    {
        var keys = Enum.GetValues<InstallPathProblemKind>()
            .SelectMany(kind => new[]
            {
                InstallFolderProblemText.KeyAndArgs(Sample(kind), "Mod").Key,
                InstallFolderProblemText.KeyAndArgs(Sample(kind), "Mod", inPlaceOverlay: true).Key,
            })
            .Append("StatusInstallFolderRefused")
            .Distinct();
        string[] voseo = { "Elegí", "Escribí", "Revisá", "Quitalo", "Acortalo", "Probá", " vos " };

        foreach (var key in keys)
        {
            var es = Strings.GetIn(Strings.LangEs, key);
            Assert.NotEqual(Strings.GetIn(Strings.LangEn, key), es);
            foreach (var form in voseo)
                Assert.DoesNotContain(form, es);
        }
    }
}
