using System;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins the project's own support link.
///
/// <para><b>Why a constant needs a test.</b> Everything that shows this link opens it through
/// <see cref="SafeUrl.TryOpen"/>, which REFUSES anything that is not clean http(s) and returns
/// false with only a log line to show for it. So a typo, an <c>http://</c>, or a paste that
/// picked up tracking junk would not break the build, would not throw, and would not look wrong
/// on screen — the pill would simply do nothing when clicked, in five places at once.</para>
/// </summary>
public class SupportLinkTests
{
    [Fact]
    public void TheSupportLinkIsOneSafeUrlWillActuallyOpen()
        => Assert.True(SafeUrl.IsAllowed(LauncherConfig.SupportDiscordUrl),
            $"SafeUrl would refuse '{LauncherConfig.SupportDiscordUrl}', so every place that " +
            "shows it would silently do nothing.");

    /// <summary>
    /// HTTPS, unlike <c>OfficialWebsite</c>, whose http allowance is a legacy concession to one
    /// mod's site. Nothing forces a project link of our own down to http.
    /// </summary>
    [Fact]
    public void ItIsHttps()
    {
        var uri = new Uri(LauncherConfig.SupportDiscordUrl);
        Assert.Equal(Uri.UriSchemeHttps, uri.Scheme);
    }

    /// <summary>
    /// No credentials in the authority. `SafeUrl` refuses these outright because
    /// `https://real-site.com@evil/` reads as the real site to a person — and the pill shows the
    /// full url in its tooltip precisely so the destination is visible, which is worth nothing
    /// if the url can lie about where it goes.
    /// </summary>
    [Fact]
    public void ItCarriesNoUserInfoTrick()
        => Assert.Equal("", new Uri(LauncherConfig.SupportDiscordUrl).UserInfo);

    /// <summary>
    /// A Discord invite, not a channel deep link or a server-settings url — the point is that a
    /// stranger who has never been in the server can get in.
    /// </summary>
    [Fact]
    public void ItIsAnInviteAnyoneCanUse()
    {
        var uri = new Uri(LauncherConfig.SupportDiscordUrl);
        Assert.Equal("discord.gg", uri.Host);
        Assert.True(uri.AbsolutePath.Trim('/').Length > 0, "an invite needs a code");
    }
}
