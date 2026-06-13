using WarsOfLibertyLauncher.Models;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Tests for the pure translation-compatibility helpers. The version layer is
/// exact-membership (no ranges) on purpose — a pack tested for one version makes
/// no promise about a newer one. The target-mod guard rejects only an explicit
/// mismatch, treating empty (legacy packs) as allowed.
/// </summary>
public class TranslationCompatTests
{
    [Fact]
    public void IsCompatible_TrueOnlyForDeclaredVersion()
    {
        Assert.True(TranslationCompat.IsCompatible(new[] { "1.2.0", "1.2.1" }, "1.2.0"));
        Assert.False(TranslationCompat.IsCompatible(new[] { "1.2.0" }, "1.3.0")); // no ranges
        Assert.False(TranslationCompat.IsCompatible(new string[0], "1.2.0"));     // empty list
        Assert.False(TranslationCompat.IsCompatible(new[] { "1.2.0" }, null));    // unknown version
    }

    [Fact]
    public void IsVersionBlocked_OnlyWhenDeclaredAndMissing()
    {
        // Declared specific versions, current not among them → blocked.
        Assert.True(TranslationCompat.IsVersionBlocked(new[] { "1.2.0" }, "1.3.0"));
        // Current is declared → not blocked.
        Assert.False(TranslationCompat.IsVersionBlocked(new[] { "1.2.0" }, "1.2.0"));
        // No declared versions → unknown, NOT blocked (hash check decides at apply).
        Assert.False(TranslationCompat.IsVersionBlocked(new string[0], "1.3.0"));
    }

    [Fact]
    public void TargetModMatches_RejectsOnlyExplicitMismatch()
    {
        Assert.True(TranslationCompat.TargetModMatches("wol", "wol"));
        Assert.True(TranslationCompat.TargetModMatches("WOL", "wol"));    // case-insensitive
        Assert.True(TranslationCompat.TargetModMatches("", "wol"));        // legacy pack → allowed
        Assert.True(TranslationCompat.TargetModMatches(null, "wol"));      // legacy → allowed
        Assert.False(TranslationCompat.TargetModMatches("wol", "improvement-mod")); // wrong mod
    }
}
