using System.Collections.Generic;
using System.Linq;
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

    [Fact]
    public void OrderForDisplay_CompatibleFirstThenNewest()
    {
        // registryOrder is newest-first (as GitHub /releases returns it).
        var registry = new List<TranslationIndexEntry>
        {
            new() { Id = "es",     Name = "Español",       CompatibleWith = { "1.2.0c2" } }, // newest, INCOMPATIBLE
            new() { Id = "es-419", Name = "Español (LatAm)", CompatibleWith = { "1.2.0d" } }, // older, COMPATIBLE
            new() { Id = "fr",     Name = "Français",      CompatibleWith = { "1.2.0d" } }, // oldest, COMPATIBLE
        };

        var ordered = TranslationCompat.OrderForDisplay(
            registry, registry, modVersion: "1.2.0d", activeId: "");
        var ids = ordered.Select(e => e.Id).ToList();

        // Compatible packs (es-419, fr) come before the incompatible one (es).
        // Within compatible, newest-release-first → es-419 (rank 1) before fr (rank 2).
        Assert.Equal(new[] { "es-419", "fr", "es" }, ids);
    }

    [Fact]
    public void OrderForDisplay_ActivePackFloatsToTop()
    {
        var registry = new List<TranslationIndexEntry>
        {
            new() { Id = "es",     Name = "Español",       CompatibleWith = { "1.2.0d" } }, // compatible
            new() { Id = "es-419", Name = "Español (LatAm)", CompatibleWith = { "1.2.0c2" } }, // incompatible but ACTIVE
        };

        var ordered = TranslationCompat.OrderForDisplay(
            registry, registry, modVersion: "1.2.0d", activeId: "es-419");

        // Active pack is first even though it's version-incompatible.
        Assert.Equal("es-419", ordered[0].Id);
    }

    [Fact]
    public void KeyOf_CascadesReleaseTagThenContentHashThenVersion()
    {
        // Release pack → the release tag wins.
        Assert.Equal("v1.2.0-es",
            TranslationCompat.KeyOf(new TranslationIndexEntry
            { Id = "es", Version = "1.0", ContentHash = "abc", ReleaseTag = "v1.2.0-es" }));
        // Folder pack (no tag) → id@contentHash.
        Assert.Equal("es@deadbeef0badf00d",
            TranslationCompat.KeyOf(new TranslationIndexEntry
            { Id = "es", Version = "1.0", ContentHash = "deadbeef0badf00d" }));
        // Legacy (no tag, no hash) → id@version.
        Assert.Equal("es@1.0",
            TranslationCompat.KeyOf(new TranslationIndexEntry { Id = "es", Version = "1.0" }));
    }

    [Fact]
    public void ComputeContentHash_StableAndContentSensitive()
    {
        var files = new List<TranslationFile>
        {
            new() { Path = "data/stringtabley.xml", TranslatedHash = "abc" },
            new() { Path = "data/unithelpstringsy.xml", TranslatedHash = "def" },
        };
        // File ORDER must not matter (sorted by path internally).
        var reordered = new List<TranslationFile> { files[1], files[0] };
        Assert.Equal(
            TranslationCompat.ComputeContentHash(files),
            TranslationCompat.ComputeContentHash(reordered));

        // 16 lowercase-hex chars, and pinned to the value the notifier (Node) also
        // computes for this exact input — guards the cross-impl dedup contract.
        var hash = TranslationCompat.ComputeContentHash(files);
        Assert.Equal(16, hash.Length);
        Assert.Equal("67426f0ebcfec85f", hash);

        // A changed translated-hash (improved pack) yields a DIFFERENT key.
        var improved = new List<TranslationFile>
        {
            new() { Path = "data/stringtabley.xml", TranslatedHash = "abc" },
            new() { Path = "data/unithelpstringsy.xml", TranslatedHash = "XYZ" },
        };
        Assert.NotEqual(
            TranslationCompat.ComputeContentHash(files),
            TranslationCompat.ComputeContentHash(improved));
    }
}
