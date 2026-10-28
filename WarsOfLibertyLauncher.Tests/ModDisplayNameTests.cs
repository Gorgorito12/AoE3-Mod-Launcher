using System.Collections.Generic;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// <c>ModRegistry.KnownDisplayName</c> — the name a
/// community match row prints for its mod (2026-10-28). The REFUSALS are the point: a mod this
/// launcher does not know is left out, never shown as its internal id, the rule the statistics
/// picker already follows.
/// </summary>
public class ModDisplayNameTests
{
    private static readonly IReadOnlyList<ModProfile> Catalog = new List<ModProfile>
    {
        new() { Id = "wol", DisplayName = "Wars of Liberty" },
        new() { Id = "napoleonic-era", DisplayName = "  Napoleonic Era  " },
        new() { Id = "nameless", DisplayName = "   " },
    };

    [Fact]
    public void AKnownModIsNamedByItsDisplayName_WhateverTheIdsCase()
    {
        Assert.Equal("Wars of Liberty", ModRegistry.KnownDisplayName("wol", Catalog));
        Assert.Equal("Wars of Liberty", ModRegistry.KnownDisplayName("WoL", Catalog));
        Assert.Equal("Napoleonic Era", ModRegistry.KnownDisplayName("napoleonic-era", Catalog));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("kings-return")]   // not in this catalog: no name, and NOT the id
    [InlineData("nameless")]       // in the catalog with a blank name: nothing to print
    public void THE_ONE_THAT_MATTERS_AnUnknownOrNamelessModIsLeftOut_NeverItsId(string? id)
    {
        Assert.Null(ModRegistry.KnownDisplayName(id, Catalog));
    }
}
