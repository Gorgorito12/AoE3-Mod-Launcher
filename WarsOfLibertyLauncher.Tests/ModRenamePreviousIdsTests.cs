using System;
using System.Linq;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins the sanitisation of a manifest's <c>previousIds</c>.
///
/// <para>The field is what lets a renamed mod keep its users' installs, and it works by
/// widening the set of ids one profile answers to. That is only safe while the widening
/// is confined to ids the mod can legitimately claim, so the filters here ARE the
/// feature's security boundary — not tidying. A manifest that got one of these past the
/// launcher would adopt another mod's install folder and inherit its saved install
/// path.</para>
///
/// <para>The catalog gate makes <c>previousIds</c> a Tier-3 (reviewed) field for the
/// same reason. The launcher repeats the rules anyway, on the principle the link
/// sanitiser already follows: a manifest does not get to be trusted because CI probably
/// saw it.</para>
/// </summary>
public class ModRenamePreviousIdsTests
{
    private const string Current = "knights-and-barbarians-remastered";

    [Fact]
    public void KeepsWellFormedIdsInOrder()
    {
        var result = ModRegistry.SanitizePreviousIds(
            new[] { "knights-and-barbarians", "knb-old" }, Current);

        Assert.Equal(new[] { "knights-and-barbarians", "knb-old" }, result);
    }

    [Fact]
    public void NormalisesCaseAndWhitespaceAndDropsDuplicates()
    {
        var result = ModRegistry.SanitizePreviousIds(
            new[] { "  Knights-And-Barbarians  ", "knights-and-barbarians", "" }, Current);

        Assert.Equal(new[] { "knights-and-barbarians" }, result);
    }

    /// <summary>
    /// Noise rather than danger: it would make the probe compare a folder against the
    /// same id twice and the config migrate a key onto itself.
    /// </summary>
    [Fact]
    public void DropsTheModsOwnId()
    {
        var result = ModRegistry.SanitizePreviousIds(new[] { Current }, Current);

        Assert.Empty(result);
    }

    /// <summary>
    /// The one that matters. A built-in's install is never adoptable — built-ins already
    /// win id collisions so a catalog entry cannot shadow them, and a former-id claim
    /// must not become a way around that.
    /// </summary>
    [Theory]
    [InlineData("wol")]
    [InlineData("WOL")]
    [InlineData("aoe3-tad")]
    public void RefusesToClaimABuiltIn(string builtIn)
    {
        var result = ModRegistry.SanitizePreviousIds(
            new[] { builtIn, "knights-and-barbarians" }, Current);

        Assert.Equal(new[] { "knights-and-barbarians" }, result);
    }

    /// <summary>
    /// Capitals are normalised, not refused. Every id comparison in the launcher is
    /// already case-insensitive, so lowercasing cannot collapse two ids the catalog
    /// considers distinct — and refusing would only punish a modder for a typo the
    /// schema would have caught anyway.
    /// </summary>
    [Fact]
    public void LowercasesRatherThanRefusingCapitals()
    {
        var result = ModRegistry.SanitizePreviousIds(new[] { "Knights-And-Barbarians" }, Current);

        Assert.Equal(new[] { "knights-and-barbarians" }, result);
    }

    [Theory]
    [InlineData("1-starts-with-a-digit")]
    [InlineData("has spaces")]
    [InlineData("has/slash")]
    [InlineData("..")]
    [InlineData("a")]  // shorter than the schema's minimum of two
    public void RefusesMalformedIds(string malformed)
    {
        var result = ModRegistry.SanitizePreviousIds(new[] { malformed }, Current);

        Assert.Empty(result);
    }

    [Fact]
    public void CapsTheList()
    {
        var many = Enumerable.Range(1, ModRegistry.MaxPreviousIds + 4)
                             .Select(i => $"old-id-{i}");

        var result = ModRegistry.SanitizePreviousIds(many, Current);

        Assert.Equal(ModRegistry.MaxPreviousIds, result.Count);
        Assert.Equal("old-id-1", result[0]);
    }

    [Fact]
    public void NullMeansEmpty()
    {
        Assert.Empty(ModRegistry.SanitizePreviousIds(null, Current));
        Assert.Empty(ModRegistry.SanitizePreviousIds(Array.Empty<string>(), Current));
    }
}
