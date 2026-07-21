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

    /// <summary>A rejected url must be reported, never launched.</summary>
    [Fact]
    public void TryOpen_RefusesRejectedUrl_WithoutThrowing()
        => Assert.False(SafeUrl.TryOpen(@"C:\Windows\System32\calc.exe"));
}
