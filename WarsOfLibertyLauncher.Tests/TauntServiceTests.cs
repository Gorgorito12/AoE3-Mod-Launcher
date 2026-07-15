using System.Collections.Generic;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Tests for <see cref="TauntService"/> — typing a bare number in a lobby plays
/// that AoE3 taunt for everyone in the room.
///
/// The parsing rule is the whole requirement: "just the number, not something
/// that ends up appending the number and plays". A loose rule (e.g. "contains a
/// number") would make ordinary chat like "gg 11" blast a taunt at the room, so
/// the rejections below matter more than the acceptances.
/// </summary>
public class TauntServiceTests
{
    [Theory]
    [InlineData("1", 1)]
    [InlineData("11", 11)]
    [InlineData("20", 20)]
    [InlineData("33", 33)]
    [InlineData(" 11 ", 11)]        // the composer trims anyway; be explicit
    [InlineData("011", 11)]         // leading zeros still name a valid taunt
    public void TryParseTaunt_BareNumberInRange_IsATaunt(string body, int expected)
    {
        Assert.True(TauntService.TryParseTaunt(body, out int n));
        Assert.Equal(expected, n);
    }

    /// <summary>
    /// The explicit ask: a message that merely CONTAINS a number is chat, not a
    /// taunt. These are what a "contains digits" rule would get wrong.
    /// </summary>
    [Theory]
    [InlineData("gg 11")]
    [InlineData("11 gg")]
    [InlineData("taunt 11")]
    [InlineData("11!")]
    [InlineData("1 1")]
    [InlineData("#11")]
    public void TryParseTaunt_NumberEmbeddedInText_IsNotATaunt(string body)
    {
        Assert.False(TauntService.TryParseTaunt(body, out _));
    }

    /// <summary>Out of range / not a number / empty must never play anything.</summary>
    [Theory]
    [InlineData("0")]
    [InlineData("34")]
    [InlineData("99")]
    [InlineData("100")]
    [InlineData("-1")]
    [InlineData("+5")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryParseTaunt_OutOfRangeOrNotANumber_IsNotATaunt(string? body)
    {
        Assert.False(TauntService.TryParseTaunt(body, out _));
    }

    /// <summary>
    /// Every taunt 1..33 must be embedded in BOTH languages. A missing one is
    /// SILENT at runtime — the taunt simply never plays and only a diagnostic
    /// line records it — so the .csproj glob is pinned here instead.
    ///
    /// Reads the compiled assembly's WPF resource table directly rather than
    /// going through pack:// URIs: the pack scheme is only registered once WPF's
    /// Application type initialises, which never happens in a test host (it throws
    /// "Invalid URI: Invalid port specified"). This checks the same bytes the app
    /// loads, without needing WPF to be alive.
    /// </summary>
    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    public void EmbeddedTaunts_CoverOneToMax_WithNoGaps(string lang)
    {
        var keys = GResourceKeys();
        var missing = new List<int>();
        for (int i = 1; i <= TauntService.MaxTaunt; i++)
            if (!keys.Contains($"assets/taunts/{lang}/{i:D3}.mp3")) missing.Add(i);

        Assert.True(missing.Count == 0,
            $"[{lang}] missing taunt resources: {string.Join(",", missing)}");
    }

    /// <summary>Resource names in the WPF .g.resources table (lowercase paths).</summary>
    private static HashSet<string> GResourceKeys()
    {
        var asm = typeof(TauntService).Assembly;
        var name = asm.GetName().Name + ".g.resources";
        using var s = asm.GetManifestResourceStream(name);
        Assert.True(s != null, $"resource table '{name}' not found in {asm.GetName().Name}");

        using var reader = new System.Resources.ResourceReader(s!);
        var keys = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry e in reader)
            if (e.Key is string k) keys.Add(k);
        return keys;
    }
}
