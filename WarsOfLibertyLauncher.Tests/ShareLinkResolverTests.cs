using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// <see cref="ShareLinkResolver"/>: turning the link a translator copies from a sharing service
/// into one that returns the FILE. The refusals matter most — each one names its reason, so the
/// player is told what to ask the translator for instead of getting a page saved as a zip.
/// </summary>
public class ShareLinkResolverTests
{
    [Theory]
    [InlineData("https://mega.nz/file/abc#key", "TxSrcErrMega")]
    [InlineData("https://mega.co.nz/#!abc", "TxSrcErrMega")]
    [InlineData("https://www.mediafire.com/file/abc/pack.zip/file", "TxSrcErrMediaFire")]
    [InlineData("https://drive.google.com/drive/folders/1AbCdEfGhIjKlMnOp", "TxSrcErrDriveFolder")]
    [InlineData("https://drive.google.com/drive/u/0/folders/1AbCdEfGhIjKlMnOp", "TxSrcErrDriveFolder")]
    [InlineData("https://docs.google.com/document/d/1AbCdEfGhIjKlMnOp/edit", "TxSrcErrGoogleDocs")]
    [InlineData("https://www.dropbox.com/scl/fo/abc123/xyz?rlkey=k", "TxSrcErrDropboxFolder")]
    [InlineData("https://www.dropbox.com/sh/abc/xyz?dl=0", "TxSrcErrDropboxFolder")]
    [InlineData("https://onedrive.live.com/?id=abc", "TxSrcErrOneDrive")]
    [InlineData("https://1drv.ms/u/s!abc", "TxSrcErrOneDrive")]
    [InlineData("https://contoso.sharepoint.com/:u:/g/abc", "TxSrcErrOneDrive")]
    [InlineData("http://example.com/translations-index.json", "TxSrcErrNotHttps")]
    [InlineData("https://user:pass@example.com/index.json", "TxSrcErrInvalid")]   // credentials disguise the host
    [InlineData("javascript:alert(1)", "TxSrcErrInvalid")]
    [InlineData("file:///C:/index.json", "TxSrcErrInvalid")]
    [InlineData("not a link", "TxSrcErrInvalid")]
    [InlineData("https://drive.google.com/file/d/short/view", "TxSrcErrDriveForm")]   // no real file id
    public void Refuses_WithItsOwnReason(string url, string reason)
    {
        var r = ShareLinkResolver.Resolve(url);
        Assert.Equal(ShareLinkKind.Rejected, r.Kind);
        Assert.Equal(reason, r.ReasonKey);
    }

    [Theory]
    [InlineData("https://drive.google.com/file/d/1AbCdEfGhIjKlMnOpQr/view?usp=sharing")]
    [InlineData("https://drive.google.com/open?id=1AbCdEfGhIjKlMnOpQr")]
    [InlineData("https://drive.google.com/uc?id=1AbCdEfGhIjKlMnOpQr&export=download")]
    public void GoogleDrive_BecomesItsDirectDownload(string url)
    {
        var r = ShareLinkResolver.Resolve(url);
        Assert.Equal(ShareLinkKind.Converted, r.Kind);
        Assert.Equal("https://drive.usercontent.google.com/download?id=1AbCdEfGhIjKlMnOpQr&export=download&confirm=t", r.Url);
        Assert.True(r.IsShareHost);
    }

    [Fact]
    public void GoogleDrive_KeepsTheResourceKeyOlderFilesNeed()
    {
        var r = ShareLinkResolver.Resolve("https://drive.google.com/file/d/1AbCdEfGhIjKlMnOpQr/view?resourcekey=0-XyZ");
        Assert.EndsWith("&resourcekey=0-XyZ", r.Url);
    }

    [Theory]
    [InlineData("https://www.dropbox.com/s/abc123/translations-index.json?dl=0",
                "https://www.dropbox.com/s/abc123/translations-index.json?dl=1")]
    [InlineData("https://www.dropbox.com/scl/fi/abc/translations-index.json?rlkey=KEY&dl=0",
                "https://www.dropbox.com/scl/fi/abc/translations-index.json?rlkey=KEY&dl=1")]
    [InlineData("https://www.dropbox.com/scl/fi/abc/translations-index.json?rlkey=KEY",
                "https://www.dropbox.com/scl/fi/abc/translations-index.json?rlkey=KEY&dl=1")]
    public void Dropbox_GetsDl1AndKeepsTheRlkey(string url, string expected)
    {
        var r = ShareLinkResolver.Resolve(url);
        Assert.Equal(ShareLinkKind.Converted, r.Kind);
        Assert.Equal(expected, r.Url);
    }

    [Fact]
    public void GitHubBlob_BecomesTheRawFile()
    {
        var r = ShareLinkResolver.Resolve("https://github.com/juan/traducciones/blob/main/translations-index.json");
        Assert.Equal("https://raw.githubusercontent.com/juan/traducciones/main/translations-index.json", r.Url);
        Assert.False(r.IsShareHost);   // a relative zip next to it resolves
    }

    [Fact]
    public void GistPage_BecomesItsRawLink()
    {
        var r = ShareLinkResolver.Resolve("https://gist.github.com/juan/0123456789abcdef");
        Assert.Equal("https://gist.githubusercontent.com/juan/0123456789abcdef/raw", r.Url);
    }

    /// <summary>
    /// A raw gist link copied from the page is PINNED to one revision; kept as it is, the
    /// translator's next version would never show up — the whole point of following a source.
    /// </summary>
    [Fact]
    public void APinnedGistRawLink_IsUnpinned()
    {
        var r = ShareLinkResolver.Resolve(
            "https://gist.githubusercontent.com/juan/0123456789abcdef/raw/0123456789abcdef0123456789abcdef01234567/translations-index.json");
        Assert.Equal("https://gist.githubusercontent.com/juan/0123456789abcdef/raw/translations-index.json", r.Url);
    }

    [Fact]
    public void AnOrdinaryWebsite_IsUsedAsItIs()
    {
        var r = ShareLinkResolver.Resolve("https://traducciones.example.com/aoe3/translations-index.json");
        Assert.Equal(ShareLinkKind.Direct, r.Kind);
        Assert.False(r.IsShareHost);
    }

    // ------ Drive's large-file confirmation page

    private const string DriveConfirmPage = """
        <!DOCTYPE html><html><head><title>Google Drive - Virus scan warning</title></head><body>
        <form id="download-form" action="https://drive.usercontent.google.com/download" method="get">
        <input type="submit" id="uc-download-link" class="goog-inline-block jfk-button" value="Download anyway"/>
        <input type="hidden" name="id" value="1AbCdEfGhIjKlMnOpQr">
        <input type="hidden" name="export" value="download">
        <input type="hidden" name="confirm" value="t">
        <input type="hidden" name="uuid" value="a1b2c3d4-e5f6">
        </form></body></html>
        """;

    [Fact]
    public void DriveConfirmForm_IsFollowedToDrivesOwnDownloadHost()
    {
        Assert.True(ShareLinkResolver.TryParseDriveConfirmForm(DriveConfirmPage, out var url));
        Assert.Equal(
            "https://drive.usercontent.google.com/download?id=1AbCdEfGhIjKlMnOpQr&export=download&confirm=t&uuid=a1b2c3d4-e5f6",
            url);
    }

    /// <summary>The page is HTML from the network: it may only point back at Drive.</summary>
    [Fact]
    public void DriveConfirmForm_PostingAnywhereElse_IsRefused()
    {
        var hostile = DriveConfirmPage.Replace("https://drive.usercontent.google.com/download", "https://evil.example.com/x");
        Assert.False(ShareLinkResolver.TryParseDriveConfirmForm(hostile, out _));
        var plainHttp = DriveConfirmPage.Replace("https://drive.usercontent", "http://drive.usercontent");
        Assert.False(ShareLinkResolver.TryParseDriveConfirmForm(plainHttp, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html><body>Sign in to continue</body></html>")]
    [InlineData("<form id=\"download-form\" action=\"https://drive.usercontent.google.com/download\"></form>")]   // no id field
    public void DriveConfirmForm_WithoutTheForm_IsNotFollowed(string html)
        => Assert.False(ShareLinkResolver.TryParseDriveConfirmForm(html, out _));
}
