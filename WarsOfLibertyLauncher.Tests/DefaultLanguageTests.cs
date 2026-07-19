using WarsOfLibertyLauncher.Models;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="LauncherConfig.DefaultLanguageForCulture"/> — the first-run
/// language pick from the Windows display language. Only two languages ship, so
/// Spanish Windows → "es" and everything else (including unknown) → "en".
/// </summary>
public class DefaultLanguageTests
{
    [Theory]
    [InlineData("es", "es")]
    [InlineData("ES", "es")]   // case-insensitive
    [InlineData("en", "en")]
    [InlineData("fr", "en")]   // unshipped language → English
    [InlineData("pt", "en")]
    [InlineData(null, "en")]
    [InlineData("", "en")]
    public void DefaultLanguageForCulture_MapsSpanishToEsElseEn(string? twoLetterIso, string expected)
    {
        Assert.Equal(expected, LauncherConfig.DefaultLanguageForCulture(twoLetterIso));
    }
}
