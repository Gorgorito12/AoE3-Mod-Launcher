using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The string-table cleaner. Small, but it is the difference between a card description and a
/// line of visible junk — measured on one real deck, 2 of its 23 descriptions carry markup.
/// </summary>
public class GameTextTests
{
    /// <summary>
    /// Both of the marked-up descriptions reachable from a real deck, verbatim from
    /// <c>stringtabley.xml</c>. The values are floats 0-1, which is why nothing tries to honour
    /// them as a colour.
    /// </summary>
    [Theory]
    [InlineData(
        "Nien Riders do more damage against Villagers and buildings. Manchus do more damage "
        + "against <color=0.74, 0.25, 0.11>assault units</color>.",
        "Nien Riders do more damage against Villagers and buildings. Manchus do more damage "
        + "against assault units.")]
    [InlineData(
        "Chu Ko Nu and Xiangu attack increased and do extra damage to light cavalry "
        + "and <color=0.19, 0.52, 0.76>line units</color>.",
        "Chu Ko Nu and Xiangu attack increased and do extra damage to light cavalry "
        + "and line units.")]
    public void ColourSpansComeOutAndTheSentenceSurvives(string raw, string expected) =>
        Assert.Equal(expected, GameText.Clean(raw));

    /// <summary>
    /// The overwhelmingly common case, and the one a careless stripper would damage: a
    /// description with no markup at all has to come back identical.
    /// </summary>
    [Theory]
    [InlineData("You get a Trading Post Rickshaw, and Trading Posts are cheaper and stronger.")]
    [InlineData("Wood source.")]
    [InlineData("Ships 1 Mariscala.")]
    public void PlainTextIsUntouched(string raw) => Assert.Equal(raw, GameText.Clean(raw));

    /// <summary>
    /// <b>Why the stripper is not <c>&lt;[^&gt;]+&gt;</c>.</b> These strings are written by
    /// modders, and a general rule would eat a lone comparison sign together with everything up
    /// to the next one — turning a sentence into half a sentence, silently.
    /// </summary>
    [Fact]
    public void ALoneAngleBracketDoesNotSwallowTheRestOfTheLine()
    {
        Assert.Equal("Range < 12 and speed > 4", GameText.Clean("Range < 12 and speed > 4"));
        Assert.Equal("Damage <br> up", GameText.Clean("Damage <br> up"));
    }

    /// <summary>The table writes line breaks as the two characters backslash and n.</summary>
    [Fact]
    public void AWrittenOutNewlineBecomesARealOne() =>
        Assert.Equal("Cost:\nWood", GameText.Clean(@"Cost:\nWood"));

    [Fact]
    public void AnInlineIconReferenceIsRemoved() =>
        Assert.Equal("Pop:", GameText.Clean(@"Pop: <icon=""(32)(ui/ingame/resource_population)"">"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NothingComesBackAsAnEmptyString(string? raw) => Assert.Equal("", GameText.Clean(raw));

    [Fact]
    public void SurroundingSpaceIsTrimmed() => Assert.Equal("Wood source.", GameText.Clean("  Wood source.  "));
}
