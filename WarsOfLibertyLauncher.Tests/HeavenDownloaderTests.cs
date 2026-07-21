using System.IO;
using System.Text;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins the scrape that lets a player download an addon with one click.
///
/// AoE3 Heaven serves downloads in two steps: the file page carries a token
/// inside an inline handler, and only <c>getfile.php?...&amp;dd=1&amp;s=&lt;token&gt;</c>
/// returns the archive. A link copied out of a browser stops working once the
/// token ages out, so the token has to be read fresh — which makes this regex
/// the piece that will break the day they change their markup. Testing it
/// against a saved copy of the real page is how that gets noticed without a
/// unit test reaching the network.
/// </summary>
public class HeavenDownloaderTests
{
    private static string RealPageFixture() =>
        File.ReadAllText(Path.Combine("Fixtures", "heaven-showfile-1932.html"));

    [Fact]
    public void ParseToken_FindsTheTokenInTheRealPage()
    {
        var token = HeavenDownloader.ParseToken(RealPageFixture(), "1932");

        Assert.Equal("caf2c858986c325ae0bc4928b56d16c8", token);
    }

    /// <summary>
    /// The token belongs to one file id. Returning another file's token would
    /// download the wrong archive — silently, since the response is still a
    /// valid zip.
    /// </summary>
    [Fact]
    public void ParseToken_IgnoresADifferentFileId()
        => Assert.Null(HeavenDownloader.ParseToken(RealPageFixture(), "9999"));

    /// <summary>
    /// A layout change must surface as "token not found" so the error names the
    /// real cause, rather than falling through to a download that returns HTML.
    /// </summary>
    [Theory]
    [InlineData("<html><body>no download link here</body></html>")]
    [InlineData("")]
    [InlineData(null)]
    public void ParseToken_ReturnsNull_WhenThePatternIsGone(string? html)
        => Assert.Null(HeavenDownloader.ParseToken(html, "1932"));

    [Theory]
    [InlineData("get_file('1932','ABCDEF0123456789')", "ABCDEF0123456789")]
    [InlineData("get_file( \"1932\" , \"abc123def456\" )", "abc123def456")]
    public void ParseToken_ToleratesQuotingAndSpacing(string html, string expected)
        => Assert.Equal(expected, HeavenDownloader.ParseToken(html, "1932"));

    // -- The magic-byte guard --------------------------------------------------

    /// <summary>
    /// The load-bearing check. Every failed attempt while building this returned
    /// HTTP 200 with an HTML interstitial — a valid response that is not a file.
    /// Trusting the status code or Content-Type would write that page out as a
    /// .zip and produce a corrupt addon whose error surfaces much later, far from
    /// the cause.
    /// </summary>
    [Fact]
    public void LooksLikeZip_RejectsAnHtmlPageServedWith200()
    {
        var html = Encoding.UTF8.GetBytes("<html><head><title>Download File</title></head>");

        Assert.False(HeavenDownloader.LooksLikeZip(html));
    }

    [Fact]
    public void LooksLikeZip_AcceptsAZipHeader()
        => Assert.True(HeavenDownloader.LooksLikeZip(new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14 }));

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0x50, 0x4B })]           // truncated
    [InlineData(new byte[] { 0x50, 0x4B, 0x05, 0x06 })] // empty-archive EOCD, not a file entry
    public void LooksLikeZip_RejectsShortOrWrongHeaders(byte[] bytes)
        => Assert.False(HeavenDownloader.LooksLikeZip(bytes));

    [Fact]
    public void LooksLikeZip_RejectsNull()
        => Assert.False(HeavenDownloader.LooksLikeZip(null));

    [Fact]
    public void PageUrlFor_BuildsTheFilePage()
        => Assert.Equal(
            "https://aoe3.heavengames.com/downloads/showfile.php?fileid=1932",
            HeavenDownloader.PageUrlFor("1932"));
}
