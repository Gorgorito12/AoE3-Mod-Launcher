using System;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// <see cref="DeepLinkService.TryParseAddSource"/>: the one-click "add this translator" link.
/// Any web page can fire it, so it is never acted on directly (the player confirms with the full
/// address on screen) — and anything ambiguous is refused before it gets that far.
/// </summary>
public class DeepLinkAddSourceTests
{
    private static string Url(string source) => "wol-launcher://add-source?url=" + Uri.EscapeDataString(source);

    [Fact]
    public void AnIndexLink_IsAccepted()
    {
        Assert.True(DeepLinkService.TryParseAddSource(
            Url("https://traducciones.example.com/translations-index.json"), out var source));
        Assert.Equal(TranslationSourceKind.Index, source!.Kind);
        Assert.Equal("https://traducciones.example.com/translations-index.json", source.Location);
    }

    [Fact]
    public void ARepo_IsAccepted()
    {
        Assert.True(DeepLinkService.TryParseAddSource("WOL-LAUNCHER://ADD-SOURCE?repo=juan%2Ftraducciones", out var source));
        Assert.Equal(TranslationSourceKind.GitHubFolder, source!.Kind);
        Assert.Equal("juan/traducciones", source.Location);
    }

    [Theory]
    [InlineData("")]
    [InlineData("wol-launcher://add-source")]                                          // nothing to add
    [InlineData("wol-launcher://add-source?")]
    [InlineData("wol-launcher://add-sourcex?repo=juan%2Ft")]                            // another host
    [InlineData("wol-launcher://join/ABC?repo=juan%2Ft")]
    [InlineData("https://add-source?repo=juan%2Ft")]                                   // another scheme
    [InlineData("wol-launcher://add-source/extra?repo=juan%2Ft")]                      // a path
    [InlineData("wol-launcher://add-source?repo=juan%2Ft&repo=ana%2Ft")]                // repeated
    [InlineData("wol-launcher://add-source?repo=juan%2Ft&url=https%3A%2F%2Fexample.com%2Fi.json")] // both
    [InlineData("wol-launcher://add-source?repo=juan%2Ft&x=1")]                         // an extra parameter
    [InlineData("wol-launcher://add-source?source=juan%2Ft")]                           // unknown key
    [InlineData("wol-launcher://add-source?Repo=juan%2Ft")]                             // keys are exact
    [InlineData("wol-launcher://add-source?repo=")]
    [InlineData("wol-launcher://add-source?repo=..%2Fx")]
    [InlineData("wol-launcher://add-source?repo=juan%2Ft%0A")]                          // encoded newline
    [InlineData("wol-launcher://add-source?repo=https%3A%2F%2Fexample.com%2Fi.json")]   // a link where a repo goes
    [InlineData("wol-launcher://add-source?url=juan%2Ft")]                              // a repo where a link goes
    [InlineData("wol-launcher://add-source?url=http%3A%2F%2Fexample.com%2Fi.json")]     // plain http
    [InlineData("wol-launcher://add-source?url=javascript%3Aalert(1)")]
    [InlineData("wol-launcher://add-source?url=https%3A%2F%2Fuser%3Apass%40example.com%2Fi.json")] // credentials
    [InlineData("wol-launcher://add-source?url=https%3A%2F%2Fmega.nz%2Ffile%2Fabc")]    // a host that can't serve files
    [InlineData("wol-launcher://add-source?url=%E0%A4%A")]                             // broken escape
    public void Refuses(string arg)
    {
        Assert.False(DeepLinkService.TryParseAddSource(arg, out var source));
        Assert.Null(source);
    }

    [Fact]
    public void AnOverlongLink_IsRefused()
    {
        var arg = Url("https://example.com/" + new string('a', DeepLinkService.MaxAddSourceLength));
        Assert.False(DeepLinkService.TryParseAddSource(arg, out _));
    }

    [Theory]
    [InlineData("juan/traducciones")]
    [InlineData("https://drive.google.com/file/d/1AbCdEfGhIjKlMnOpQr/view?usp=sharing")]
    [InlineData("https://www.dropbox.com/scl/fi/abc/translations-index.json?rlkey=KEY&dl=0")]
    public void BuildAddSourceUri_RoundTrips(string input)
    {
        Assert.True(TranslationSourceRef.TryParse(input, out var source, out _));
        var uri = DeepLinkService.BuildAddSourceUri(source!);

        Assert.True(DeepLinkService.TryParseAddSource(uri, out var back));
        Assert.Equal(source!.Key, back!.Key);
        Assert.Equal(source.Location, back.Location);
    }

    [Fact]
    public void FindAddSource_TakesTheFirstValidArgument()
    {
        var source = DeepLinkService.FindAddSource(new[]
        {
            "--minimized", "wol-launcher://add-source?url=http%3A%2F%2Fbad", "wol-launcher://add-source?repo=juan%2Ft",
        });
        Assert.Equal("juan/t", source!.Location);
        Assert.Null(DeepLinkService.FindAddSource(new[] { "--minimized" }));
    }

    /// <summary>
    /// The pipe carries "src &lt;link&gt;" next to bare lobby ids: an add-source payload must never
    /// be mistaken for a lobby to join.
    /// </summary>
    [Fact]
    public void AnAddSourcePipePayload_IsNeverALobbyId()
    {
        var payload = "src " + DeepLinkService.BuildAddSourceUri(TranslationSourceRef.Repo("juan/t"));
        Assert.False(DeepLinkService.IsValidLobbyId(payload));
        Assert.False(DeepLinkService.TryParseJoin(payload, out _));
    }

    // ------ carried across the startup auto-update's restart

    [Fact]
    public void TheRelaunch_CarriesTheAddSourceLink()
    {
        var source = TranslationSourceRef.IndexUrl("https://www.dropbox.com/scl/fi/abc/i.json?rlkey=K&dl=0");
        var args = StartupUpdateGate.BuildRelaunchArguments(new StartupUpdateGate.Context(false, false, false, null, source));

        Assert.Contains("\"" + DeepLinkService.BuildAddSourceUri(source) + "\"", args);
        // Escaped, so the quoted argument can't be broken out of.
        Assert.DoesNotContain(" ", DeepLinkService.BuildAddSourceUri(source));
    }

    /// <summary>A join link wins: one deep link per launch, and joining a lobby is time-sensitive.</summary>
    [Fact]
    public void TheRelaunch_PrefersAJoinLink()
    {
        var args = StartupUpdateGate.BuildRelaunchArguments(
            new StartupUpdateGate.Context(false, false, false, "NHHXP1NR", TranslationSourceRef.Repo("juan/t")));
        Assert.Contains(DeepLinkService.BuildJoinUri("NHHXP1NR"), args);
        Assert.DoesNotContain("add-source", args);
    }
}
