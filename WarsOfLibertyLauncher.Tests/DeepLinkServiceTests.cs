using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Tests for <see cref="DeepLinkService.TryParseJoin"/> — the parser for the
/// Discord "Join" deep link. The URI is UNTRUSTED (any web page can fire it), so
/// only a strict <c>wol-launcher://join/&lt;alphanumeric-id&gt;</c> is accepted.
/// </summary>
public class DeepLinkServiceTests
{
    [Theory]
    [InlineData("wol-launcher://join/NHHXP1NR", true, "NHHXP1NR")]
    [InlineData("WOL-LAUNCHER://JOIN/AbC123", true, "AbC123")]   // scheme + host case-insensitive
    [InlineData("wol-launcher://join/NHHXP1NR/", true, "NHHXP1NR")] // trailing slash tolerated
    [InlineData("wol-launcher://join/ABCDEFGHIJ0123456789ABCDEFGHIJ12", true, "ABCDEFGHIJ0123456789ABCDEFGHIJ12")] // 32 chars
    public void TryParseJoin_ValidLinks(string arg, bool ok, string expectedId)
    {
        Assert.Equal(ok, DeepLinkService.TryParseJoin(arg, out var id));
        Assert.Equal(expectedId, id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("--update-now")]                                  // a normal launch arg
    [InlineData("https://wol-lobby.duckdns.org/join/ABC")]        // wrong scheme
    [InlineData("wol-launcher://foo/ABC")]                        // wrong host/action
    [InlineData("wol-launcher://join/")]                          // no id
    [InlineData("wol-launcher://join/a.b")]                       // dot — not in the allowed charset
    [InlineData("wol-launcher://join/a-b")]                       // hyphen — not allowed
    [InlineData("wol-launcher://join/bad id")]                    // space
    [InlineData("wol-launcher://join/ABCDEFGHIJ0123456789ABCDEFGHIJ123")] // 33 chars — too long
    [InlineData("not a uri at all")]
    public void TryParseJoin_Rejects(string arg)
    {
        Assert.False(DeepLinkService.TryParseJoin(arg, out var id));
        Assert.Equal("", id);
    }

    [Fact]
    public void FindJoinLobbyId_PicksTheJoinArgAmongOthers()
    {
        var args = new[] { "Aoe3ModLauncher.exe", "--update-now", "wol-launcher://join/ROOM42" };
        Assert.Equal("ROOM42", DeepLinkService.FindJoinLobbyId(args));

        Assert.Null(DeepLinkService.FindJoinLobbyId(new[] { "Aoe3ModLauncher.exe", "--update-now" }));
    }
}
