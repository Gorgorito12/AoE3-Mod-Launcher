using System.Globalization;
using System.Threading;
using WarsOfLibertyLauncher.Controls;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The pure half of the settings search — the only part that can be reached without a window,
/// and the part that was silently wrong.
///
/// <para>The search shipped with no tests at all. It compared with
/// <c>StringComparison.CurrentCultureIgnoreCase</c>, which is case-insensitive and
/// accent-SENSITIVE, so in a Spanish-first UI half the words in the launcher could not be
/// found by typing them the way people actually type.</para>
/// </summary>
public class SectionSearchTests
{
    /// <summary>
    /// The es-419 UI is the reason this matters, so the test asserts under a Spanish culture
    /// rather than whatever the build machine happens to run.
    /// </summary>
    private static void InSpanish(Action body)
    {
        var was = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("es-419");
            body();
        }
        finally
        {
            CultureInfo.CurrentCulture = was;
        }
    }

    [Fact]
    public void CaseNeverMatters()
    {
        InSpanish(() =>
        {
            Assert.True(SectionSearch.Matches("Idioma del launcher", "IDIOMA"));
            Assert.True(SectionSearch.Matches("Idioma del launcher", "launcher"));
            Assert.True(SectionSearch.Matches("MINIMIZAR A LA BANDEJA", "bandeja"));
        });
    }

    /// <summary>
    /// The fix. Nobody reaches for the accent key while filtering a list, and before this the
    /// search answered "nothing found" — which reads as broken, not as empty.
    /// </summary>
    [Theory]
    [InlineData("Buscar actualizaciones", "actualizacion")]
    [InlineData("Aplicar traducción", "traduccion")]
    [InlineData("Configuración de la partida", "configuracion")]
    [InlineData("Versión instalada", "version")]
    public void AnAccentIsNeverSomethingYouHaveToType(string haystack, string query)
    {
        InSpanish(() => Assert.True(SectionSearch.Matches(haystack, query),
            $"typing \"{query}\" found nothing in \"{haystack}\""));
    }

    /// <summary>It works the other way too — the text is what carries the accent, not the query.</summary>
    [Fact]
    public void TypingTheAccentAlsoWorks()
    {
        InSpanish(() => Assert.True(SectionSearch.Matches("Buscar actualizaciones", "actualización")));
    }

    /// <summary>
    /// THE REJECTIONS ARE THE POINT. Ignoring diacritics widens what matches, and a filter that
    /// matches too much is worse than one that matches too little: it looks like it worked.
    /// </summary>
    [Theory]
    [InlineData("Idioma del launcher", "carpeta")]
    [InlineData("Minimizar a la bandeja", "bandera")]
    [InlineData("Buscar actualizaciones", "desinstalar")]
    public void SomethingThatIsNotThereIsNotFound(string haystack, string query)
    {
        InSpanish(() => Assert.False(SectionSearch.Matches(haystack, query)));
    }

    /// <summary>
    /// An empty query matches everything, which is what makes "clear the box" mean "show it all"
    /// rather than "hide it all" — the difference between a working search and a blank page.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void AnEmptyQueryMatchesEverything(string? query)
    {
        Assert.True(SectionSearch.Matches("anything at all", query));
    }

    [Fact]
    public void EmptyTextMatchesNothingRealYouCouldType()
    {
        Assert.False(SectionSearch.Matches("", "idioma"));
        Assert.False(SectionSearch.Matches(null, "idioma"));
    }

    /// <summary>
    /// The limit of the accent rule, measured rather than assumed: Ñ is NOT reachable by
    /// typing N.
    ///
    /// <para>That is correct rather than a gap. Spanish collation treats ñ as its own LETTER,
    /// not as an n carrying a mark, so <c>IgnoreNonSpace</c> leaves it alone — and Spanish
    /// keyboards have the key. Pinned so nobody "fixes" it into a general
    /// strip-every-diacritic pass, which would also make cañón and canon the same word.</para>
    /// </summary>
    [Fact]
    public void NIsNotAWayToTypeEnye()
    {
        InSpanish(() =>
        {
            Assert.True(SectionSearch.Matches("Añadir carpeta", "añadir"));
            Assert.False(SectionSearch.Matches("Añadir carpeta", "anadir"));
        });
    }
}
