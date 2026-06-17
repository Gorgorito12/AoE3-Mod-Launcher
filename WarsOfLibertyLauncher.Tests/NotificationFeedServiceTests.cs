using System.Linq;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pure-logic tests for <see cref="NotificationFeedService.ParseFeed"/> — the
/// parsing layer of the central notification feed. The network/304/cache behaviour
/// of <c>FetchAsync</c> needs a live HTTP server so it's left to the manual smoke
/// test; <c>ParseFeed</c> is the testable core that turns the manifest JSON into
/// the model the launcher diffs against its local state.
/// </summary>
public class NotificationFeedServiceTests
{
    private const string SampleJson = """
    {
      "version": 1,
      "generatedAt": "2026-06-17T12:00:00Z",
      "mods": {
        "wol": { "latestVersion": "1.0.18", "translations": ["v1.0.18-es", "v1.0.17-pt"] },
        "improvement-mod": { "latestVersion": "9.0.4", "translations": [] }
      }
    }
    """;

    [Fact]
    public void ParseFeed_ValidManifest_MapsModsAndFields()
    {
        var feed = NotificationFeedService.ParseFeed(SampleJson);

        Assert.NotNull(feed);
        Assert.Equal(1, feed!.Version);
        Assert.Equal(2, feed.Mods.Count);

        var wol = feed.Mods["wol"];
        Assert.Equal("1.0.18", wol.LatestVersion);
        Assert.Equal(new[] { "v1.0.18-es", "v1.0.17-pt" }, wol.Translations.ToArray());

        Assert.Empty(feed.Mods["improvement-mod"].Translations);
    }

    [Fact]
    public void ParseFeed_ModIds_MatchCaseInsensitively()
    {
        // The manifest uses "WoL" but the launcher matches profile ids OrdinalIgnoreCase.
        var feed = NotificationFeedService.ParseFeed(
            """{ "version": 1, "mods": { "WoL": { "latestVersion": "2.0" } } }""");

        Assert.NotNull(feed);
        Assert.True(feed!.Mods.TryGetValue("wol", out var entry));
        Assert.Equal("2.0", entry!.LatestVersion);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{ unterminated")]
    public void ParseFeed_InvalidOrEmpty_ReturnsNull(string? json)
    {
        Assert.Null(NotificationFeedService.ParseFeed(json));
    }

    [Fact]
    public void ParseFeed_MissingModsKey_YieldsEmptyDictionary()
    {
        var feed = NotificationFeedService.ParseFeed("""{ "version": 1 }""");

        Assert.NotNull(feed);
        Assert.Empty(feed!.Mods);
    }
}
