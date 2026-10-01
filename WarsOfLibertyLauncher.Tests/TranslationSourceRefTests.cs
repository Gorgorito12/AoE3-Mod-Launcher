using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// <see cref="TranslationSourceRef.TryParse"/> is the one door every translation source comes
/// through (the Language tab, Settings, the add-source link), and <see cref="TranslationSourceRef.Key"/>
/// is what stops the same source being added twice under two spellings.
/// </summary>
public class TranslationSourceRefTests
{
    [Theory]
    [InlineData("", "TxSrcErrEmpty")]
    [InlineData("   ", "TxSrcErrEmpty")]
    [InlineData("juan", "TxSrcErrInvalid")]                       // not owner/repo
    [InlineData("juan/traducciones/extra", "TxSrcErrInvalid")]
    [InlineData("juan/..", "TxSrcErrInvalid")]
    [InlineData("../x", "TxSrcErrInvalid")]
    [InlineData("juan/tra\nducciones", "TxSrcErrInvalid")]        // control character
    [InlineData("http://example.com/translations-index.json", "TxSrcErrNotHttps")]
    [InlineData("ftp://example.com/translations-index.json", "TxSrcErrInvalid")]
    [InlineData("https://user:pass@example.com/i.json", "TxSrcErrInvalid")]
    [InlineData("https://github.com/juan/traducciones/issues/3", "TxSrcErrGitHubForm")]
    [InlineData("https://github.com/juan", "TxSrcErrGitHubForm")]
    [InlineData("https://mega.nz/file/abc#key", "TxSrcErrMega")]
    [InlineData("https://www.mediafire.com/file/abc/x.json/file", "TxSrcErrMediaFire")]
    [InlineData("https://drive.google.com/drive/folders/1AbCdEfGhIjKlMnOp", "TxSrcErrDriveFolder")]
    public void Refuses_WithItsOwnReason(string input, string reason)
    {
        Assert.False(TranslationSourceRef.TryParse(input, out var source, out var key));
        Assert.Null(source);
        Assert.Equal(reason, key);
    }

    [Fact]
    public void AnOverlongInput_IsRefused()
    {
        var input = "https://example.com/" + new string('a', TranslationSourceRef.MaxInputLength);
        Assert.False(TranslationSourceRef.TryParse(input, out _, out var key));
        Assert.Equal("TxSrcErrTooLong", key);
    }

    [Theory]
    [InlineData("juan/traducciones", "juan/traducciones")]
    [InlineData("  juan/traducciones  ", "juan/traducciones")]
    [InlineData("juan/traducciones.git", "juan/traducciones")]
    [InlineData("https://github.com/juan/traducciones", "juan/traducciones")]
    [InlineData("https://github.com/juan/traducciones/", "juan/traducciones")]
    [InlineData("https://github.com/juan/traducciones/tree/main", "juan/traducciones")]
    [InlineData("https://github.com/juan/traducciones.git", "juan/traducciones")]
    public void RepositoryForms_BecomeTheRepository(string input, string repo)
    {
        Assert.True(TranslationSourceRef.TryParse(input, out var source, out _));
        Assert.Equal(TranslationSourceKind.GitHubFolder, source!.Kind);
        Assert.Equal(repo, source.Location);
    }

    [Theory]
    [InlineData("https://github.com/juan/traducciones/blob/main/translations-index.json")]
    [InlineData("https://drive.google.com/file/d/1AbCdEfGhIjKlMnOpQr/view?usp=sharing")]
    [InlineData("https://www.dropbox.com/scl/fi/abc/translations-index.json?rlkey=KEY&dl=0")]
    [InlineData("https://gist.github.com/juan/0123456789abcdef")]
    [InlineData("https://traducciones.example.com/translations-index.json")]
    public void Links_BecomeAnIndexSource(string input)
    {
        Assert.True(TranslationSourceRef.TryParse(input, out var source, out _));
        Assert.Equal(TranslationSourceKind.Index, source!.Kind);
        Assert.Equal(input, source.Location);   // kept as given; the conversion happens at fetch time
    }

    // ------ identity

    [Fact]
    public void ARepository_IsTheSameSourceInAnyCase()
    {
        TranslationSourceRef.TryParse("Juan/Traducciones", out var a, out _);
        TranslationSourceRef.TryParse("https://github.com/juan/traducciones", out var b, out _);
        Assert.Equal(a!.Key, b!.Key);
    }

    /// <summary>Two share links to the same Drive file are one source.</summary>
    [Fact]
    public void TwoDriveLinksToTheSameFile_AreTheSameSource()
    {
        TranslationSourceRef.TryParse("https://drive.google.com/file/d/1AbCdEfGhIjKlMnOpQr/view?usp=sharing", out var a, out _);
        TranslationSourceRef.TryParse("https://drive.google.com/open?id=1AbCdEfGhIjKlMnOpQr", out var b, out _);
        Assert.Equal(a!.Key, b!.Key);
    }

    [Fact]
    public void AHostsCaseAndATrailingSlash_DoNotMakeANewSource()
    {
        var a = TranslationSourceRef.IndexUrl("https://Traducciones.Example.com/aoe3/");
        var b = TranslationSourceRef.IndexUrl("https://traducciones.example.com/aoe3");
        Assert.Equal(a.Key, b.Key);
    }

    [Fact]
    public void AFolderAndTheReleasesOfOneRepo_AreDifferentSources()
        => Assert.NotEqual(TranslationSourceRef.Repo("juan/t").Key, TranslationSourceRef.Releases("juan/t").Key);

    [Fact]
    public void ARepoAndAnIndex_NeverShareAKey()
    {
        Assert.StartsWith("gh:", TranslationSourceRef.Repo("juan/t").Key);
        Assert.StartsWith("ghr:", TranslationSourceRef.Releases("juan/t").Key);
        Assert.StartsWith("url:", TranslationSourceRef.IndexUrl("https://example.com/i.json").Key);
    }
}
