using System;
using System.Linq;
using System.Text.RegularExpressions;
using WarsOfLibertyLauncher.Localization;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins the administrator-permission and copy-settings texts, which used to say the wrong thing
/// in a way that misled a real player.
///
/// <para>The old elevation body named <b>Wars of Liberty</b> and "applying updates" for every mod
/// and every reason — so an Age of Dynasties install that only needed its own registry key read as
/// if choosing to copy settings FROM Wars of Liberty had asked for admin. And these strings are
/// formatted with <c>string.Format</c>: a placeholder count that disagrees with a caller throws
/// <c>FormatException</c> inside a click handler, the crash net swallows it, and the prompt
/// simply never appears. Hence the argument counts below.</para>
/// </summary>
public class ElevationStringsTests
{
    private static readonly string[] Languages = { "en", "es" };

    /// <summary>Key → how many arguments the code passes when it formats it.</summary>
    public static TheoryData<string, int> FormattedKeys => new()
    {
        { "DlgElevationFolderBody", 3 },
        { "DlgPrivateKeyBody", 1 },
        { "DlgPrivateKeyRestartBody", 2 },
        { "StatusPrivateKeyCreating", 1 },
        { "StatusPrivateKeyHelperFailed", 2 },
        { "StatusPrivateKeyBaseMissing", 1 },
        { "DlgElevationContinues", 0 },
        { "DlgElevationRestartAction", 0 },
        { "DlgElevationRestartConfirm", 0 },
        { "DlgPrivateKeyTitle", 0 },
        { "DlgPrivateKeyConfirm", 0 },
        { "StatusElevationDenied", 0 },
        { "StatusPrivateKeyNeedsAdmin", 0 },
        { "DlgInstallCopySettingsHintFirstLaunch", 0 },
        { "DlgInstallCopySettingsHint", 0 },
    };

    [Theory]
    [MemberData(nameof(FormattedKeys))]
    public void EachTextExistsInBothLanguagesAndTakesTheArgumentsTheCodePasses(string key, int argumentCount)
    {
        foreach (var lang in Languages)
        {
            var template = Strings.GetIn(lang, key);
            Assert.NotEqual(key, template);   // a missing key renders as the key itself

            var used = Regex.Matches(template, @"\{(\d+)\}")
                .Select(m => int.Parse(m.Groups[1].Value))
                .Distinct()
                .OrderBy(n => n)
                .ToArray();
            Assert.Equal(Enumerable.Range(0, argumentCount).ToArray(), used);

            var args = Enumerable.Range(0, argumentCount).Select(i => (object?)$"<arg{i}>").ToArray();
            var formatted = Strings.FormatIn(lang, key, args);
            for (var i = 0; i < argumentCount; i++)
                Assert.Contains($"<arg{i}>", formatted);
        }

        // Spanish is a translation, not a copy of the English.
        Assert.NotEqual(Strings.GetIn("en", key), Strings.GetIn("es", key));
    }

    [Theory]
    [InlineData("DlgElevationFolderBody")]
    [InlineData("DlgPrivateKeyBody")]
    [InlineData("DlgPrivateKeyRestartBody")]
    [InlineData("DlgElevationRequiredTitle")]
    [InlineData("StatusPrivateKeyHelperFailed")]
    public void THE_ONE_THAT_MATTERS_NoElevationTextNamesAModOfItsOwn(string key)
    {
        foreach (var lang in Languages)
            Assert.DoesNotContain("Wars of Liberty", Strings.GetIn(lang, key));
    }

    [Fact]
    public void TheOldBodyThatNamedWarsOfLibertyIsGone()
    {
        Assert.Equal("DlgElevationRequiredBody", Strings.GetIn("en", "DlgElevationRequiredBody"));
    }

    [Fact]
    public void ADeclinedPromptIsNotCalledAnUpdate()
    {
        Assert.DoesNotContain("Update", Strings.GetIn("en", "StatusElevationDenied"), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Actualización", Strings.GetIn("es", "StatusElevationDenied"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheTwoCopySettingsHintsPromiseDifferentLaunches()
    {
        foreach (var lang in Languages)
            Assert.NotEqual(
                Strings.GetIn(lang, "DlgInstallCopySettingsHint"),
                Strings.GetIn(lang, "DlgInstallCopySettingsHintFirstLaunch"));
        Assert.Contains("first", Strings.GetIn("en", "DlgInstallCopySettingsHintFirstLaunch"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("primera", Strings.GetIn("es", "DlgInstallCopySettingsHintFirstLaunch"), StringComparison.OrdinalIgnoreCase);
    }
}
