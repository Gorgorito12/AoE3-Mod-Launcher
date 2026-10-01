using System.Linq;
using WarsOfLibertyLauncher.Models;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The config half of community translation sources: the sanitiser in front of
/// <see cref="LauncherConfig.ExtraTranslationIndexUrls"/> (a hand-edited config must not put
/// anything into a request that the "Add" box would refuse), and the active-translation state
/// that now records the content hash and the source, carried through every install rotation.
/// </summary>
public class TranslationSourceConfigTests
{
    [Fact]
    public void IndexUrls_OnlyWhatTheAddBoxWouldAccept()
    {
        var config = new LauncherConfig
        {
            ExtraTranslationIndexUrls = new[]
            {
                "https://traducciones.example.com/translations-index.json",
                "http://plain.example.com/translations-index.json",
                "https://mega.nz/file/abc#key",
                "https://drive.google.com/drive/folders/1AbCdEfGhIjKlMnOp",
                "juan/traducciones",                                         // a repo belongs in the other list
                "https://user:pass@example.com/i.json",
                "",
                null!,
            },
        };

        Assert.Equal(new[] { "https://traducciones.example.com/translations-index.json" },
            config.GetExtraTranslationIndexUrls());
    }

    [Fact]
    public void IndexUrls_TheSameSourceTwice_IsKeptOnce()
    {
        var config = new LauncherConfig
        {
            ExtraTranslationIndexUrls = new[]
            {
                "https://drive.google.com/file/d/1AbCdEfGhIjKlMnOpQr/view?usp=sharing",
                "https://drive.google.com/open?id=1AbCdEfGhIjKlMnOpQr",
            },
        };
        Assert.Single(config.GetExtraTranslationIndexUrls());
    }

    [Fact]
    public void IndexUrls_AreCapped()
    {
        var config = new LauncherConfig
        {
            ExtraTranslationIndexUrls = Enumerable.Range(0, LauncherConfig.MaxExtraTranslationSources + 5)
                .Select(i => $"https://example.com/{i}/translations-index.json").ToArray(),
        };
        Assert.Equal(LauncherConfig.MaxExtraTranslationSources, config.GetExtraTranslationIndexUrls().Length);
    }

    [Fact]
    public void IndexUrls_ANullListIsEmpty()
        => Assert.Empty(new LauncherConfig { ExtraTranslationIndexUrls = null! }.GetExtraTranslationIndexUrls());

    // ------ active translation state

    [Fact]
    public void SetAndClearActiveTranslation_MoveAllFourFieldsTogether()
    {
        var state = new ModState();
        state.SetActiveTranslation("ES-LA", "1.2.0e-r2", "aaaa000000000001", "url:juan");

        Assert.Equal("ES-LA", state.ActiveTranslationId);
        Assert.Equal("1.2.0e-r2", state.ActiveTranslationVersion);
        Assert.Equal("aaaa000000000001", state.ActiveTranslationContentHash);
        Assert.Equal("url:juan", state.ActiveTranslationSource);

        state.ClearActiveTranslation();
        Assert.Equal("", state.ActiveTranslationId);
        Assert.Equal("", state.ActiveTranslationVersion);
        Assert.Equal("", state.ActiveTranslationContentHash);
        Assert.Equal("", state.ActiveTranslationSource);
    }

    [Fact]
    public void SetActiveTranslation_NullsBecomeEmpty()
    {
        var state = new ModState();
        state.SetActiveTranslation("ES-LA", null, null, null);
        Assert.Equal("", state.ActiveTranslationVersion);
        Assert.Equal("", state.ActiveTranslationContentHash);
        Assert.Equal("", state.ActiveTranslationSource);
    }

    /// <summary>A switch between installs must not lose which translator's pack is applied where.</summary>
    [Fact]
    public void TheHashAndSource_SurviveSnapshotAndAdopt()
    {
        var state = new ModState { InstallPath = @"C:\Games\WoL" };
        state.SetActiveTranslation("ES-LA", "1.2.0e-r5", "bbbb000000000005", "url:juan");

        var slot = state.SnapshotActive();
        Assert.Equal("bbbb000000000005", slot.ActiveTranslationContentHash);
        Assert.Equal("url:juan", slot.ActiveTranslationSource);

        var other = new ModState();
        other.AdoptInstall(slot);
        Assert.Equal("ES-LA", other.ActiveTranslationId);
        Assert.Equal("bbbb000000000005", other.ActiveTranslationContentHash);
        Assert.Equal("url:juan", other.ActiveTranslationSource);
    }

    [Fact]
    public void ClearInstallState_ForgetsTheActiveTranslationCompletely()
    {
        var state = new ModState { InstallPath = @"C:\Games\WoL" };
        state.SetActiveTranslation("ES-LA", "1.2.0e-r5", "bbbb000000000005", "url:juan");

        state.ClearInstallState();

        Assert.Equal("", state.ActiveTranslationId);
        Assert.Equal("", state.ActiveTranslationContentHash);
        Assert.Equal("", state.ActiveTranslationSource);
    }
}
