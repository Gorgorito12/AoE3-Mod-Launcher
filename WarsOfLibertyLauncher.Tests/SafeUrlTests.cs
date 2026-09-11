using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins the gate that stands between a mod-supplied string and
/// <c>Process.Start(UseShellExecute: true)</c>. The rejection cases matter more
/// than the acceptance ones: with UseShellExecute the shell runs whatever it is
/// handed, so a <c>file:///</c> or a bare path reaching it is arbitrary local
/// execution driven by a catalog manifest.
/// </summary>
public class SafeUrlTests
{
    [Theory]
    [InlineData("https://discord.gg/example")]
    [InlineData("https://www.moddb.com/mods/example")]
    [InlineData("http://aoe3wol.com/")]            // legacy officialWebsite allowance
    [InlineData("  https://example.com/path?q=1 ")] // surrounding whitespace tolerated
    public void Allows_HttpAndHttps(string url)
        => Assert.True(SafeUrl.IsAllowed(url));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData(@"C:\Windows\System32\cmd.exe")]
    [InlineData(@"\\attacker\share\payload.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ms-settings:")]
    [InlineData("wol-launcher://join/abc")]
    [InlineData("discord.gg/example")]              // not absolute — no scheme
    public void Rejects_EverythingThatIsNotAWebUrl(string? url)
        => Assert.False(SafeUrl.IsAllowed(url));

    /// <summary>
    /// <c>https://real-site.com@evil.example/</c> renders as the real site but
    /// navigates to the host after the '@'. A link whose visible text can lie
    /// about its destination is exactly what the tooltip is meant to expose, so
    /// the credential form is refused outright.
    /// </summary>
    [Theory]
    [InlineData("https://aoe3wol.com@evil.example/")]
    [InlineData("https://user:pass@evil.example/")]
    public void Rejects_EmbeddedCredentials(string url)
        => Assert.False(SafeUrl.IsAllowed(url));

    [Fact]
    public void HostOf_ReturnsHost_ForAllowedUrl()
        => Assert.Equal("discord.gg", SafeUrl.HostOf("https://discord.gg/example"));

    [Fact]
    public void HostOf_IsEmpty_ForRejectedUrl()
        => Assert.Equal("", SafeUrl.HostOf("file:///C:/x.exe"));

    // ---------------------------------------------------------- CompactForDisplay

    /// <summary>
    /// The three real mods in the report, as they now read in the rail footer. The last one is
    /// the whole reason there is a fallback: with its tail it wants 190 px of the 181 the rail
    /// has, so it drops to the bare host rather than showing a second ellipsis.
    /// </summary>
    [Theory]
    [InlineData("http://aoe3wol.com/", "aoe3wol.com")]
    [InlineData("https://www.ageofempires.com/games/aoeiii/", "ageofempires.com/games/aoeiii")]
    [InlineData("https://www.moddb.com/mods/knights-and-barbarians", "moddb.com")]
    public void CompactForDisplay_TheThreeRealMods(string url, string expected)
        => Assert.Equal(expected, SafeUrl.CompactForDisplay(url));

    /// <summary>
    /// A url that already fits comes out whole — scheme and www gone, nothing else touched.
    /// Shortening something that was already short would be pure loss.
    /// </summary>
    [Theory]
    [InlineData("https://example.com", "example.com")]
    [InlineData("https://example.com/", "example.com")]
    [InlineData("https://www.example.com/mods/x", "example.com/mods/x")]
    public void CompactForDisplay_LeavesShortUrlsAlone(string url, string expected)
        => Assert.Equal(expected, SafeUrl.CompactForDisplay(url));

    /// <summary>
    /// THE REJECTIONS, which are the point. Anything this class would not hand to the shell
    /// comes back UNCHANGED rather than blanked: most of these are a mod author writing their
    /// site without a scheme, and erasing the line would hide the one thing they did supply.
    /// It is display text — opening still goes through TryOpen.
    /// </summary>
    [Theory]
    [InlineData("aoe3wol.com")]                 // no scheme: not an absolute Uri
    [InlineData("not a url at all")]
    [InlineData("file:///C:/x.exe")]
    [InlineData("https://real-site.com@evil/")] // the credentials trick IsAllowed refuses
    public void CompactForDisplay_ReturnsRejectedUrlsUntouched(string url)
        => Assert.Equal(url, SafeUrl.CompactForDisplay(url));

    /// <summary>Empty in, empty out — never the word "null" or a stray slash.</summary>
    [Fact]
    public void CompactForDisplay_EmptyStaysEmpty()
    {
        Assert.Equal("", SafeUrl.CompactForDisplay(null));
        Assert.Equal("", SafeUrl.CompactForDisplay("   "));
    }

    /// <summary>
    /// Whole segments, never half a word — the one constraint the handoff put in writing. The
    /// leaf survives intact or it is dropped entirely; it is never cut.
    /// </summary>
    [Fact]
    public void CompactForDisplay_NeverCutsAWordInHalf()
    {
        // Over budget, so the middle goes — and the leaf arrives whole.
        Assert.Equal("example.com/…/the-final-bit",
            SafeUrl.CompactForDisplay("https://example.com/mods/category/the-final-bit"));

        // A leaf that cannot fit even on its own takes the host alone, never a cut leaf.
        Assert.Equal("example.com",
            SafeUrl.CompactForDisplay("https://example.com/a/" + new string('z', 60)));
    }

    /// <summary>
    /// The ladder, in order: whole while it fits, then the middle elided, then the host alone.
    /// Each rung is only taken because the one above it did not fit — a path that fits is never
    /// shortened just because it has several segments.
    /// </summary>
    [Fact]
    public void CompactForDisplay_ShortensOnlyAsMuchAsItHasTo()
    {
        Assert.Equal("example.com/a/b/c",
            SafeUrl.CompactForDisplay("https://example.com/a/b/c"));
        Assert.Equal("example.com/…/the-final-bit",
            SafeUrl.CompactForDisplay("https://example.com/one/two/three/the-final-bit"));
        Assert.Equal("example.com",
            SafeUrl.CompactForDisplay("https://example.com/one/" + new string('q', 40)));
    }

    /// <summary>A rejected url must be reported, never launched.</summary>
    [Fact]
    public void TryOpen_RefusesRejectedUrl_WithoutThrowing()
        => Assert.False(SafeUrl.TryOpen(@"C:\Windows\System32\calc.exe"));
}
