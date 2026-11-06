using System.IO;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>This file runs as administrator, so the refusals are the point.</summary>
public class RadminInstallerTests
{
    [Fact]
    public void TheAddressComesFromTheOfficialPage()
    {
        const string page = "<a href=\"https://download.radmin-vpn.com/download/files/Radmin_VPN_2.1.4951.1.exe\">x</a>";
        Assert.Equal("https://download.radmin-vpn.com/download/files/Radmin_VPN_2.1.4951.1.exe",
            RadminVpnService.FindInstallerUrl(page));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://download.radmin-vpn.com/download/files/Radmin_VPN_2.1.4951.1.exe")]
    [InlineData("https://evil.example/download/files/Radmin_VPN_2.1.4951.1.exe")]
    [InlineData("https://download.radmin-vpn.com.evil.example/download/files/Radmin_VPN_2.1.exe")]
    [InlineData("https://download.radmin-vpn.com/download/files/Radmin_VPN.msi")]
    [InlineData("https://download.radmin-vpn.com/download/files/Other_2.1.exe")]
    public void NothingElseCountsAsTheInstaller(string? page)
        => Assert.Null(RadminVpnService.FindInstallerUrl(page));

    [Fact]
    public void FamatechIsThePublisher()
        => Assert.True(RadminVpnService.IsRadminPublisher("CN=Famatech Corp., O=Famatech Corp., L=Road Town, C=VG"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("CN=Gorgorito")]
    [InlineData("CN=Not Famatech Corp., O=Famatech Corp.")]
    [InlineData("CN=Famatech Corp.Evil, O=Other")]
    public void NobodyElseIs(string? subject)
        => Assert.False(RadminVpnService.IsRadminPublisher(subject));

    [Fact]
    public void AnUnsignedFileHasNoTrustedSigner()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "not a program");
            Assert.Null(AuthenticodeCheck.TrustedSigner(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
